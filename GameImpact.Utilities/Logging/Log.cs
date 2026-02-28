#region

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

#endregion

namespace GameImpact.Utilities.Logging
{
    /// <summary>
    ///     全局静态日志工具类。
    ///     <para>
    ///         提供统一的日志输出入口，内部委托 <see cref="ILogger"/> 实现实际写入。 支持两种日志通道：
    ///         <list type="bullet">
    ///             <item>
    ///                 <description><b>常规日志</b>（Debug/Info/Warn/Error）—— 仅输出到日志后端（控制台/文件）并触发 <see cref="OnLogMessage"/> 事件。</description>
    ///             </item>
    ///             <item>
    ///                 <description><b>屏幕日志</b>（DebugScreen/InfoScreen/WarnScreen/ErrorScreen）—— 在常规日志的基础上额外触发 <see cref="OnScreenLogMessage"/> 事件， 供 UI 层（如 MainWindow 日志面板、OverlayWindow 叠加日志）订阅并显示。</description>
    ///             </item>
    ///         </list>
    ///     </para>
    ///     <para>使用前需调用 <see cref="Initialize"/> 注入 <see cref="ILoggerFactory"/>。</para>
    /// </summary>
    public static class Log
    {
        /// <summary>日志工厂实例，用于按类别创建 <see cref="ILogger"/></summary>
        private static ILoggerFactory? s_factory;

        /// <summary>默认 Logger（类别名 "GameImpact"），所有静态方法通过此实例输出</summary>
        private static ILogger? s_defaultLogger;

        /// <summary>日志级别 → 缩写映射表（DBG / INF / WRN / ERR）</summary>
        private static readonly Dictionary<LogLevel, string> LevelAbbreviations = new()
        {
                [LogLevel.Debug] = "DBG",
                [LogLevel.Information] = "INF",
                [LogLevel.Warning] = "WRN",
                [LogLevel.Error] = "ERR"
        };

        /// <summary>用于匹配消息模板中 {Xxx} 占位符的正则表达式（预编译，线程安全）</summary>
        private static readonly Regex PlaceholderRegex = new(@"\{[^}]+\}", RegexOptions.Compiled);

        /// <summary>常规日志消息事件。
        ///     <para>每次调用 Debug/Info/Warn/Error 时触发，参数为 (级别缩写, 格式化后的消息)。</para>
        ///     <para>级别缩写：DBG / INF / WRN / ERR。</para>
        /// </summary>
        public static event Action<string, string>? OnLogMessage;

        /// <summary>屏幕日志消息事件。
        ///     <para>仅在调用 XxxScreen 系列方法时触发，参数为 (级别缩写, 格式化后的消息)。</para>
        ///     <para>UI 层（MainModel、OverlayWindow）应订阅此事件来展示关键业务日志。</para>
        /// </summary>
        public static event Action<string, string>? OnScreenLogMessage;

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  初始化 & Logger 获取
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        /// <summary>初始化日志系统，注入 <see cref="ILoggerFactory"/>。
        ///     <para>应在应用程序启动时（如 App.OnStartup）调用一次。</para>
        /// </summary>
        /// <param name="factory">由 DI 容器或手动构建的日志工厂</param>
        public static void Initialize(ILoggerFactory factory)
        {
            s_factory = factory;
            s_defaultLogger = factory.CreateLogger("GameImpact");
        }

        /// <summary>获取指定类型 <typeparamref name="T"/> 的强类型 Logger。
        ///     <para>若尚未初始化，则返回空实现 <see cref="NullLogger{T}"/>，不会抛出异常。</para>
        /// </summary>
        /// <typeparam name="T">日志类别对应的类型（通常为调用方的类）</typeparam>
        /// <returns>强类型 Logger 实例</returns>
        public static ILogger<T> For<T>()
        {
            return s_factory?.CreateLogger<T>() ?? NullLogger<T>.Instance;
        }

        /// <summary>获取指定名称的 Logger。
        ///     <para>若尚未初始化，则返回空实现 <see cref="NullLogger"/>。</para>
        /// </summary>
        /// <param name="categoryName">日志类别名称（如 "GameImpact.Input"）</param>
        /// <returns>Logger 实例</returns>
        public static ILogger For(string categoryName)
        {
            return s_factory?.CreateLogger(categoryName) ?? NullLogger.Instance;
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  常规日志方法（仅输出到日志后端 + 触发 OnLogMessage）
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        /// <summary>输出 Debug 级别日志（纯文本）</summary>
        public static void Debug(string message)
        {
            LogInternal(LogLevel.Debug, message);
        }

        /// <summary>输出 Debug 级别日志（带结构化占位符）</summary>
        public static void Debug(string message, params object[] args)
        {
            LogInternal(LogLevel.Debug, message, args);
        }

        /// <summary>输出 Info 级别日志（纯文本）</summary>
        public static void Info(string message)
        {
            LogInternal(LogLevel.Information, message);
        }

        /// <summary>输出 Info 级别日志（带结构化占位符）</summary>
        public static void Info(string message, params object[] args)
        {
            LogInternal(LogLevel.Information, message, args);
        }

        /// <summary>输出 Warn 级别日志（纯文本）</summary>
        public static void Warn(string message)
        {
            LogInternal(LogLevel.Warning, message);
        }

        /// <summary>输出 Warn 级别日志（带结构化占位符）</summary>
        public static void Warn(string message, params object[] args)
        {
            LogInternal(LogLevel.Warning, message, args);
        }

        /// <summary>输出 Error 级别日志（纯文本）</summary>
        public static void Error(string message)
        {
            LogInternal(LogLevel.Error, message);
        }

        /// <summary>输出 Error 级别日志（带结构化占位符）</summary>
        public static void Error(string message, params object[] args)
        {
            LogInternal(LogLevel.Error, message, args);
        }

        /// <summary>输出 Error 级别日志（含异常信息）</summary>
        /// <param name="ex">捕获的异常对象</param>
        /// <param name="message">附加的描述消息</param>
        public static void Error(Exception ex, string message)
        {
            s_defaultLogger?.LogError(ex, "{Message}", message);
            RaiseLogMessage("ERR", $"{message}: {ex.Message}");
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  Screen 系列方法：常规日志 + 触发 OnScreenLogMessage
        //  供 UI 层（日志面板 / Overlay 叠加层）订阅并显示
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        /// <summary>Debug 级别日志，同时输出到屏幕（纯文本）</summary>
        public static void DebugScreen(string message)
        {
            LogScreenInternal(LogLevel.Debug, message);
        }

        /// <summary>Debug 级别日志，同时输出到屏幕（带结构化占位符）</summary>
        public static void DebugScreen(string message, params object[] args)
        {
            LogScreenInternal(LogLevel.Debug, message, args);
        }

        /// <summary>Info 级别日志，同时输出到屏幕（纯文本）</summary>
        public static void InfoScreen(string message)
        {
            LogScreenInternal(LogLevel.Information, message);
        }

        /// <summary>Info 级别日志，同时输出到屏幕（带结构化占位符）</summary>
        public static void InfoScreen(string message, params object[] args)
        {
            LogScreenInternal(LogLevel.Information, message, args);
        }

        /// <summary>Warn 级别日志，同时输出到屏幕（纯文本）</summary>
        public static void WarnScreen(string message)
        {
            LogScreenInternal(LogLevel.Warning, message);
        }

        /// <summary>Warn 级别日志，同时输出到屏幕（带结构化占位符）</summary>
        public static void WarnScreen(string message, params object[] args)
        {
            LogScreenInternal(LogLevel.Warning, message, args);
        }

        /// <summary>Error 级别日志，同时输出到屏幕（纯文本）</summary>
        public static void ErrorScreen(string message)
        {
            LogScreenInternal(LogLevel.Error, message);
        }

        /// <summary>Error 级别日志（带结构化占位符），同时输出到屏幕</summary>
        public static void ErrorScreen(string message, params object[] args)
        {
            LogScreenInternal(LogLevel.Error, message, args);
        }

        /// <summary>Error 级别日志（含异常信息），同时输出到屏幕</summary>
        /// <param name="ex">捕获的异常对象</param>
        /// <param name="message">附加的描述消息</param>
        public static void ErrorScreen(Exception ex, string message)
        {
            Error(ex, message);
            RaiseScreenLogMessage("ERR", $"{message}: {ex.Message}");
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        //  辅助方法
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

        /// <summary>检查默认 Logger 是否启用了指定的日志级别</summary>
        /// <param name="level">要检查的日志级别</param>
        /// <returns>若已初始化且该级别已启用则返回 true；否则返回 false</returns>
        public static bool IsEnabled(LogLevel level)
        {
            return s_defaultLogger is not null && s_defaultLogger.IsEnabled(level);
        }

        /// <summary>常规日志核心方法（纯文本）：写入日志后端 + 触发 OnLogMessage。
        ///     <para>所有无参数的 Debug/Info/Warn/Error 均委托此方法。</para>
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        private static void LogInternal(LogLevel level, string message)
        {
            s_defaultLogger?.Log(level, "{Message}", message);
            RaiseLogMessage(GetLevelAbbreviation(level), message);
        }

        /// <summary>常规日志核心方法（带占位符）：写入日志后端 + 触发 OnLogMessage。
        ///     <para>所有带 params 的 Debug/Info/Warn/Error 均委托此方法。</para>
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">包含 {Xxx} 占位符的消息模板</param>
        /// <param name="args">按顺序替换占位符的参数</param>
        private static void LogInternal(LogLevel level, string message, object[] args)
        {
            s_defaultLogger?.Log(level, "{Message}", Format(message, args));
            RaiseLogMessage(GetLevelAbbreviation(level), Format(message, args));
        }

        /// <summary>屏幕日志核心方法（纯文本）：写入日志后端 + 触发 OnLogMessage + 触发 OnScreenLogMessage。
        ///     <para>所有无参数的 XxxScreen 均委托此方法。</para>
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        private static void LogScreenInternal(LogLevel level, string message)
        {
            var abbr = GetLevelAbbreviation(level);
            s_defaultLogger?.Log(level, "{Message}", message);
            RaiseLogMessage(abbr, message);
            RaiseScreenLogMessage(abbr, message);
        }

        /// <summary>
        ///     屏幕日志核心方法（带占位符）：写入日志后端 + 触发 OnLogMessage + 触发 OnScreenLogMessage。
        ///     <para>所有带 params 的 XxxScreen 均委托此方法。 格式化仅执行一次，避免旧实现中 Format 被重复调用的性能浪费。</para>
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">包含 {Xxx} 占位符的消息模板</param>
        /// <param name="args">按顺序替换占位符的参数</param>
        private static void LogScreenInternal(LogLevel level, string message, object[] args)
        {
            var abbr = GetLevelAbbreviation(level);
            var formattedMessage = Format(message, args);
            s_defaultLogger?.Log(level, "{Message}", formattedMessage);
            RaiseLogMessage(abbr, formattedMessage);
            RaiseScreenLogMessage(abbr, formattedMessage);
        }

        /// <summary>获取日志级别的缩写字符串</summary>
        /// <param name="level">日志级别</param>
        /// <returns>缩写字符串（DBG / INF / WRN / ERR），未知级别返回 "???"</returns>
        private static string GetLevelAbbreviation(LogLevel level)
        {
            return LevelAbbreviations.GetValueOrDefault(level, "???");
        }

        /// <summary>触发 <see cref="OnLogMessage"/> 事件，通知所有常规日志订阅者。
        ///     <para>内部使用 try-catch 防止订阅者异常影响日志调用方。</para>
        /// </summary>
        /// <param name="level">日志级别缩写（DBG / INF / WRN / ERR）</param>
        /// <param name="message">格式化后的日志消息</param>
        private static void RaiseLogMessage(string level, string message)
        {
            try { OnLogMessage?.Invoke(level, message); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Log] RaiseLogMessage 订阅者异常: {ex.Message}"); }
        }

        /// <summary>触发 <see cref="OnScreenLogMessage"/> 事件，通知所有屏幕日志订阅者。
        ///     <para>内部使用 try-catch 防止订阅者异常影响日志调用方。</para>
        /// </summary>
        /// <param name="level">日志级别缩写（DBG / INF / WRN / ERR）</param>
        /// <param name="message">格式化后的日志消息</param>
        private static void RaiseScreenLogMessage(string level, string message)
        {
            try { OnScreenLogMessage?.Invoke(level, message); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Log] RaiseScreenLogMessage 订阅者异常: {ex.Message}"); }
        }

        /// <summary>
        ///     简易模板格式化方法：按顺序将 <paramref name="args"/> 替换消息模板中的 {Xxx} 占位符。
        ///     <para>使用正则表达式匹配 {Xxx} 占位符，逐个替换为对应参数的字符串表示。 与手动 IndexOf 方式不同，即使参数的 ToString() 结果中包含 '{' 也不会导致后续替换错乱。</para>
        ///     <example>Format("鼠标点击 ({X}, {Y})", 100, 200) → "鼠标点击 (100, 200)"</example>
        /// </summary>
        /// <param name="template">包含 {Xxx} 占位符的消息模板</param>
        /// <param name="args">按顺序替换占位符的参数数组</param>
        /// <returns>替换后的纯文本字符串；若替换过程出错则原样返回模板</returns>
        private static string Format(string template, object[] args)
        {
            try
            {
                var index = 0;
                return PlaceholderRegex.Replace(template, match =>
                {
                    if (index < args.Length)
                    {
                        return args[index++].ToString() ?? "";
                    }
                    return match.Value; // 参数不足时保留原占位符
                });
            }
            catch { return template; }
        }
    }
    /// <summary>
    ///     空 Logger 实现（非泛型版本）。
    ///     <para>当 <see cref="Log"/> 尚未通过 <see cref="Log.Initialize"/> 初始化时， 作为兜底返回，避免调用方出现 NullReferenceException。 所有日志操作均为空操作（no-op）。</para>
    /// </summary>
    internal class NullLogger : ILogger
    {
        /// <summary>单例实例</summary>
        public static readonly NullLogger Instance = new();

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        /// <summary>始终返回 false，表示不启用任何日志级别</summary>
        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        /// <summary>空操作，不执行任何写入</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
    /// <summary>
    ///     空 Logger 实现（泛型版本）。
    ///     <para>当 <see cref="Log"/> 尚未初始化时，由 <see cref="Log.For{T}"/> 返回， 提供与 <see cref="ILogger{T}"/> 兼容的空操作实现。</para>
    /// </summary>
    /// <typeparam name="T">日志类别对应的类型</typeparam>
    internal class NullLogger<T> : ILogger<T>
    {
        /// <summary>单例实例</summary>
        public static readonly NullLogger<T> Instance = new();

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        /// <summary>始终返回 false，表示不启用任何日志级别</summary>
        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        /// <summary>空操作，不执行任何写入</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
