using System.Collections.Concurrent;
using GameImpact.Abstractions.Automation;
using GameImpact.Abstractions.Capture;
using GameImpact.Utilities.Logging;

namespace GameImpact.Automation;

/// <summary>
/// 自动化任务引擎，管理任务注册、触发和执行
/// </summary>
public class TaskEngine : IDisposable
{
    private readonly ConcurrentDictionary<string, IAutomationTask> m_tasks = new();
    private readonly ConcurrentDictionary<string, ITaskTrigger> m_triggers = new();
    private readonly SemaphoreSlim m_taskLock = new(1, 1);
    private CancellationTokenSource? m_cts;
    private IScreenCapture? m_capture;
    private Task? m_triggerLoopTask;
    private int m_frameIndex;

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning => m_taskLock.CurrentCount == 0;

    /// <summary>
    /// 所有已注册的任务
    /// </summary>
    public IReadOnlyDictionary<string, IAutomationTask> Tasks => m_tasks;

    /// <summary>
    /// 所有已注册的触发器
    /// </summary>
    public IReadOnlyDictionary<string, ITaskTrigger> Triggers => m_triggers;

    /// <summary>
    /// 注册任务
    /// </summary>
    /// <param name="name">任务名称</param>
    /// <param name="task">任务实例</param>
    public void RegisterTask(string name, IAutomationTask task)
    {
        m_tasks[name] = task;
        Log.Debug("[TaskEngine] Registered task: {Name}", name);
    }

    /// <summary>
    /// 注册触发器
    /// </summary>
    /// <param name="name">触发器名称</param>
    /// <param name="trigger">触发器实例</param>
    public void RegisterTrigger(string name, ITaskTrigger trigger)
    {
        m_triggers[name] = trigger;
        Log.Debug("[TaskEngine] Registered trigger: {Name}, priority={Priority}", name, trigger.Priority);
    }

    /// <summary>
    /// 注销任务
    /// </summary>
    /// <param name="name">任务名称</param>
    public void UnregisterTask(string name)
    {
        m_tasks.TryRemove(name, out _);
        Log.Debug("[TaskEngine] Unregistered task: {Name}", name);
    }

    /// <summary>
    /// 注销触发器
    /// </summary>
    /// <param name="name">触发器名称</param>
    public void UnregisterTrigger(string name)
    {
        m_triggers.TryRemove(name, out _);
        Log.Debug("[TaskEngine] Unregistered trigger: {Name}", name);
    }

    /// <summary>
    /// 设置屏幕捕获实例
    /// </summary>
    /// <param name="capture">屏幕捕获实例</param>
    public void SetCapture(IScreenCapture capture)
    {
        m_capture = capture;
        Log.Debug("[TaskEngine] Capture set");
    }

    /// <summary>
    /// 异步执行任务
    /// </summary>
    /// <param name="taskName">任务名称</param>
    /// <returns>是否成功执行</returns>
    public async Task<bool> RunTaskAsync(string taskName)
    {
        if (!m_tasks.TryGetValue(taskName, out var task))
        {
            Log.Warn("[TaskEngine] Task not found: {Name}", taskName);
            return false;
        }

        if (!await m_taskLock.WaitAsync(0))
        {
            Log.Warn("[TaskEngine] Another task is running");
            return false;
        }

        try
        {
            m_cts?.Cancel();
            m_cts?.Dispose();
            m_cts = new CancellationTokenSource();

            Log.Info("[TaskEngine] Starting task: {Name}", taskName);
            await task.ExecuteAsync(m_cts.Token);
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
            m_taskLock.Release();
        }
    }

    /// <summary>
    /// 启动触发器循环
    /// </summary>
    /// <param name="intervalMs">检查间隔（毫秒）</param>
    public void StartTriggerLoop(int intervalMs = 50)
    {
        if (m_triggerLoopTask != null)
        {
            Log.Warn("[TaskEngine] Trigger loop already running");
            return;
        }

        m_cts?.Cancel();
        m_cts?.Dispose();
        m_cts = new CancellationTokenSource();

        foreach (var trigger in m_triggers.Values)
        {
            trigger.Init();
        }

        Log.Info("[TaskEngine] Starting trigger loop, interval={Interval}ms, triggers={Count}", intervalMs, m_triggers.Count);

        m_triggerLoopTask = Task.Run(async () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!m_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var frame = m_capture?.Capture();
                    if (frame != null)
                    {
                        var context = new FrameContext(frame, m_frameIndex++, sw.Elapsed, m_cts.Token);
                        var sortedTriggers = m_triggers.Values
                            .Where(t => t.IsEnabled)
                            .OrderByDescending(t => t.Priority);

                        foreach (var trigger in sortedTriggers)
                        {
                            if (m_cts.Token.IsCancellationRequested)
                            {
                                break;
                            }
                            trigger.OnFrame(context);
                            if (trigger.IsExclusive)
                            {
                                break;
                            }
                        }

                        frame.Dispose();
                    }

                    await Task.Delay(intervalMs, m_cts.Token);
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
            Log.Debug("[TaskEngine] Trigger loop ended, processed {Count} frames", m_frameIndex);
        }, m_cts.Token);
    }

    /// <summary>
    /// 停止触发器循环
    /// </summary>
    public void StopTriggerLoop()
    {
        Log.Debug("[TaskEngine] Stopping trigger loop");
        m_cts?.Cancel();
        m_triggerLoopTask?.Wait(1000);
        m_triggerLoopTask = null;
    }

    /// <summary>
    /// 取消所有操作
    /// </summary>
    public void Cancel()
    {
        Log.Debug("[TaskEngine] Cancelling all operations");
        m_cts?.Cancel();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Cancel();
        m_cts?.Dispose();
        m_taskLock.Dispose();
        Log.Debug("[TaskEngine] Disposed");
        GC.SuppressFinalize(this);
    }
}
