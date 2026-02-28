#region

using System.Diagnostics;
using GameImpact.Utilities.Logging;

#endregion

namespace GameImpact.Utilities.Timing
{
    /// <summary>性能计时工具</summary>
    public static class PerfTimer
    {
        /// <summary>测量操作耗时</summary>
        public static TimeSpan Measure(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.Elapsed;
        }

        /// <summary>测量操作耗时并返回结果</summary>
        public static (T Result, TimeSpan Elapsed) Measure<T>(Func<T> func)
        {
            var sw = Stopwatch.StartNew();
            var result = func();
            sw.Stop();
            return (result, sw.Elapsed);
        }

        /// <summary>测量异步操作耗时</summary>
        public static async Task<TimeSpan> MeasureAsync(Func<Task> action)
        {
            var sw = Stopwatch.StartNew();
            await action();
            sw.Stop();
            return sw.Elapsed;
        }

        /// <summary>创建一个计时作用域，结束时自动记录日志</summary>
        public static TimingScope BeginScope(string operationName)
        {
            return new TimingScope(operationName);
        }
    }

    /// <summary>计时作用域</summary>
    public class TimingScope : IDisposable
    {
        private readonly string m_operationName;
        private readonly Stopwatch m_sw;

        /// <summary>构造函数</summary>
        /// <param name="operationName">操作名称</param>
        public TimingScope(string operationName)
        {
            m_operationName = operationName;
            m_sw = Stopwatch.StartNew();
        }

        /// <summary>已耗时</summary>
        public TimeSpan Elapsed => m_sw.Elapsed;

        /// <inheritdoc/>
        public void Dispose()
        {
            m_sw.Stop();
            Log.Debug("[Timing] {Operation} completed in {Elapsed}ms", m_operationName, m_sw.ElapsedMilliseconds);
        }
    }
}
