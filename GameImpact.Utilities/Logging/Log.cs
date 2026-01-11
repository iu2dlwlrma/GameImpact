using Microsoft.Extensions.Logging;

namespace GameImpact.Utilities.Logging;

/// <summary>
/// 全局静态日志工具
/// </summary>
public static class Log
{
    private static ILoggerFactory? _factory;
    private static ILogger? _defaultLogger;

    /// <summary>
    /// 日志消息事件 (level, message)
    /// </summary>
    public static event Action<string, string>? OnLogMessage;

    /// <summary>
    /// 初始化日志工厂
    /// </summary>
    public static void Initialize(ILoggerFactory factory)
    {
        _factory = factory;
        _defaultLogger = factory.CreateLogger("GameImpact");
    }

    /// <summary>
    /// 获取指定类型的Logger
    /// </summary>
    public static ILogger<T> For<T>() => _factory?.CreateLogger<T>() ?? NullLogger<T>.Instance;

    /// <summary>
    /// 获取指定名称的Logger
    /// </summary>
    public static ILogger For(string categoryName) => _factory?.CreateLogger(categoryName) ?? NullLogger.Instance;

    public static void Debug(string message)
    {
        _defaultLogger?.LogDebug(message);
        RaiseLogMessage("DBG", message);
    }

    public static void Debug(string message, params object[] args)
    {
        _defaultLogger?.LogDebug(message, args);
        RaiseLogMessage("DBG", Format(message, args));
    }

    public static void Info(string message)
    {
        _defaultLogger?.LogInformation(message);
        RaiseLogMessage("INF", message);
    }

    public static void Info(string message, params object[] args)
    {
        _defaultLogger?.LogInformation(message, args);
        RaiseLogMessage("INF", Format(message, args));
    }

    public static void Warn(string message)
    {
        _defaultLogger?.LogWarning(message);
        RaiseLogMessage("WRN", message);
    }

    public static void Warn(string message, params object[] args)
    {
        _defaultLogger?.LogWarning(message, args);
        RaiseLogMessage("WRN", Format(message, args));
    }

    public static void Error(string message)
    {
        _defaultLogger?.LogError(message);
        RaiseLogMessage("ERR", message);
    }

    public static void Error(string message, params object[] args)
    {
        _defaultLogger?.LogError(message, args);
        RaiseLogMessage("ERR", Format(message, args));
    }

    public static void Error(Exception ex, string message)
    {
        _defaultLogger?.LogError(ex, message);
        RaiseLogMessage("ERR", $"{message}: {ex.Message}");
    }

    public static bool IsEnabled(LogLevel level) => _defaultLogger?.IsEnabled(level) ?? false;

    private static void RaiseLogMessage(string level, string message)
    {
        try { OnLogMessage?.Invoke(level, message); } catch { }
    }

    private static string Format(string template, object[] args)
    {
        try
        {
            // 简单替换 {xxx} 占位符
            var result = template;
            for (int i = 0; i < args.Length; i++)
            {
                var idx = result.IndexOf('{');
                if (idx < 0) break;
                var end = result.IndexOf('}', idx);
                if (end < 0) break;
                result = result[..idx] + args[i] + result[(end + 1)..];
            }
            return result;
        }
        catch { return template; }
    }
}

/// <summary>
/// 空Logger实现
/// </summary>
internal class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}

internal class NullLogger<T> : ILogger<T>
{
    public static readonly NullLogger<T> Instance = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}
