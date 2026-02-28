#region

using GameImpact.UI.Services;

#endregion

namespace GameImpact.UI.Settings
{
    /// <summary>应用级通用设置模型，所有子项目共享。 通过 SettingsItemAttribute 和 SettingsGroupAttribute 自动生成设置界面。</summary>
    public class AppSettings
    {
        /// <summary>主题设置</summary>
        [SettingsItem("主题", Description = "选择应用的外观主题", Options = "深色:Dark|浅色:Light")]
        public AppTheme Theme{ get; set; } = AppTheme.Dark;

        /// <summary>是否启用 GPU HDR 转换</summary>
        [SettingsItem("GPU HDR 转换", Description = "使用 GPU 加速进行 HDR 到 SDR 色彩空间转换", Order = 2)]
        public bool EnableGpuHdrConversion{ get; set; } = false;

        /// <summary>是否在启动时自动检查更新</summary>
        [SettingsItem("自动检查更新", Description = "启动时自动检查是否有新版本")]
        public bool AutoCheckUpdate{ get; set; } = true;

        /// <summary>日志级别（Debug, Information, Warning, Error）</summary>
        [SettingsItem("日志级别", Description = "控制日志输出的详细程度", Options = "调试 (Debug):Debug|信息 (Info):Information|警告 (Warning):Warning|错误 (Error):Error")]
        public string LogLevel{ get; set; } = "Debug";

        /// <summary>是否启用自动捕获（选择窗口后自动开始捕获）</summary>
        [SettingsItem("自动开始捕获", Description = "选择窗口后自动开始画面捕获", Order = 1)]
        public bool AutoStartCapture{ get; set; } = false;

        /// <summary>捕获帧率限制（0 表示不限制）</summary>
        [SettingsGroup("窗口与捕获", Order = 0)]
        [SettingsItem("捕获帧率限制", Description = "设为 0 表示不限制帧率", Order = 3, Min = 0, Max = 240)]
        public int CaptureFrameRateLimit{ get; set; } = 0;

        /// <summary>识别置信度阈值（0.0 - 1.0）</summary>
        [SettingsGroup("识别", Order = 1)]
        [SettingsItem("识别置信度阈值", Description = "范围 0.0 - 1.0，值越高匹配越严格", Min = 0.0, Max = 1.0)]
        public double RecognitionConfidenceThreshold{ get; set; } = 0.7;

        /// <summary>是否启用 Overlay 窗口</summary>
        [SettingsGroup("叠加层", Order = 2)]
        [SettingsItem("启用 Overlay 窗口", Description = "在游戏窗口上方显示叠加信息", Order = 0)]
        public bool EnableOverlay{ get; set; } = true;

        /// <summary>是否显示调试信息叠加层</summary>
        [SettingsGroup("叠加层", Order = 2)]
        [SettingsItem("显示调试信息", Description = "在 Overlay 上显示帧率和识别调试信息", Order = 1)]
        public bool ShowDebugOverlay{ get; set; } = false;
    }
}
