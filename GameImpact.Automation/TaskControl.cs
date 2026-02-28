namespace GameImpact.Automation;

public static class TaskControl
{
    public static void Sleep(int milliseconds, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (milliseconds <= 0) return;
        Thread.Sleep(milliseconds);
        ct.ThrowIfCancellationRequested();
    }

    public static async Task DelayAsync(int milliseconds, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (milliseconds <= 0) return;
        await Task.Delay(milliseconds, ct);
        ct.ThrowIfCancellationRequested();
    }

    public static async Task WaitUntilAsync(Func<bool> condition, int checkIntervalMs = 100, int timeoutMs = 10000, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            if (sw.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException("Wait condition timeout");
            }
            await Task.Delay(checkIntervalMs, ct);
        }
    }

    public static async Task<T> RetryAsync<T>(Func<Task<T>> action, Func<T, bool> successCondition, int maxRetries = 3, int delayMs = 500, CancellationToken ct = default)
    {
        for (var i = 0; i < maxRetries; i++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await action();
            if (successCondition(result))
            {
                return result;
            }
            if (i < maxRetries - 1)
            {
                await Task.Delay(delayMs, ct);
            }
        }
        throw new InvalidOperationException($"Retry failed after {maxRetries} attempts");
    }
}
