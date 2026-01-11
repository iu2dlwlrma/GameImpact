using System.Collections.Concurrent;
using GameImpact.Abstractions.Automation;
using GameImpact.Abstractions.Capture;
using GameImpact.Utilities.Logging;

namespace GameImpact.Automation;

public class TaskEngine : IDisposable
{
    private readonly ConcurrentDictionary<string, IAutomationTask> _tasks = new();
    private readonly ConcurrentDictionary<string, ITaskTrigger> _triggers = new();
    private readonly SemaphoreSlim _taskLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private IScreenCapture? _capture;
    private Task? _triggerLoopTask;
    private int _frameIndex;

    public bool IsRunning => _taskLock.CurrentCount == 0;
    public IReadOnlyDictionary<string, IAutomationTask> Tasks => _tasks;
    public IReadOnlyDictionary<string, ITaskTrigger> Triggers => _triggers;

    public void RegisterTask(string name, IAutomationTask task)
    {
        _tasks[name] = task;
        Log.Debug("[TaskEngine] Registered task: {Name}", name);
    }

    public void RegisterTrigger(string name, ITaskTrigger trigger)
    {
        _triggers[name] = trigger;
        Log.Debug("[TaskEngine] Registered trigger: {Name}, priority={Priority}", name, trigger.Priority);
    }

    public void UnregisterTask(string name)
    {
        _tasks.TryRemove(name, out _);
        Log.Debug("[TaskEngine] Unregistered task: {Name}", name);
    }

    public void UnregisterTrigger(string name)
    {
        _triggers.TryRemove(name, out _);
        Log.Debug("[TaskEngine] Unregistered trigger: {Name}", name);
    }

    public void SetCapture(IScreenCapture capture)
    {
        _capture = capture;
        Log.Debug("[TaskEngine] Capture set");
    }

    public async Task<bool> RunTaskAsync(string taskName)
    {
        if (!_tasks.TryGetValue(taskName, out var task))
        {
            Log.Warn("[TaskEngine] Task not found: {Name}", taskName);
            return false;
        }

        if (!await _taskLock.WaitAsync(0))
        {
            Log.Warn("[TaskEngine] Another task is running");
            return false;
        }

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            Log.Info("[TaskEngine] Starting task: {Name}", taskName);
            await task.ExecuteAsync(_cts.Token);
            Log.Info("[TaskEngine] Task completed: {Name}", taskName);
            return true;
        }
        catch (OperationCanceledException)
        {
            Log.Info("[TaskEngine] Task cancelled: {Name}", taskName);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[TaskEngine] Task failed: {Name}");
            return false;
        }
        finally
        {
            _taskLock.Release();
        }
    }

    public void StartTriggerLoop(int intervalMs = 50)
    {
        if (_triggerLoopTask != null)
        {
            Log.Warn("[TaskEngine] Trigger loop already running");
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        foreach (var trigger in _triggers.Values)
            trigger.Init();

        Log.Info("[TaskEngine] Starting trigger loop, interval={Interval}ms, triggers={Count}", intervalMs, _triggers.Count);

        _triggerLoopTask = Task.Run(async () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var frame = _capture?.Capture();
                    if (frame != null)
                    {
                        var context = new FrameContext(frame, _frameIndex++, sw.Elapsed, _cts.Token);
                        var sortedTriggers = _triggers.Values
                            .Where(t => t.IsEnabled)
                            .OrderByDescending(t => t.Priority);

                        foreach (var trigger in sortedTriggers)
                        {
                            if (_cts.Token.IsCancellationRequested) break;
                            trigger.OnFrame(context);
                            if (trigger.IsExclusive) break;
                        }

                        frame.Dispose();
                    }

                    await Task.Delay(intervalMs, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[TaskEngine] Trigger loop error");
                }
            }
            Log.Debug("[TaskEngine] Trigger loop ended, processed {Count} frames", _frameIndex);
        }, _cts.Token);
    }

    public void StopTriggerLoop()
    {
        Log.Debug("[TaskEngine] Stopping trigger loop");
        _cts?.Cancel();
        _triggerLoopTask?.Wait(1000);
        _triggerLoopTask = null;
    }

    public void Cancel()
    {
        Log.Debug("[TaskEngine] Cancelling all operations");
        _cts?.Cancel();
    }

    public void Dispose()
    {
        Cancel();
        _cts?.Dispose();
        _taskLock.Dispose();
        Log.Debug("[TaskEngine] Disposed");
        GC.SuppressFinalize(this);
    }
}
