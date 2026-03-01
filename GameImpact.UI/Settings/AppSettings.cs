#region

using GameImpact.Abstractions.Recognition;
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

        /// <summary>游戏根目录（与子类提供的 GameStartPath 拼接后作为启动路径，未设置时点击启动会提示配置）</summary>
        [SettingsGroup("窗口与捕获", Order = 0)]
        [SettingsItem("游戏路径", Description = "游戏根目录", Order = 0)]
        public string GameRootPath{ get; set; } = string.Empty;

        /// <summary>捕获帧率限制（0 表示不限制）</summary>
        [SettingsItem("捕获帧率限制", Description = "设为 0 表示不限制帧率", Order = 3, Min = 0, Max = 240)]
        public int CaptureFrameRateLimit{ get; set; } = 0;

        /// <summary>识别置信度阈值（0.0 - 1.0）</summary>
        [SettingsGroup("图像处理", Order = 1)]
        [SettingsItem("识别置信度阈值", Description = "范围 0.0 - 1.0，值越高匹配越严格", Min = 0.0, Max = 1.0)]
        public double RecognitionConfidenceThreshold{ get; set; } = 0.7;

        /// <summary>模板匹配算法组合</summary>
        [SettingsGroup("图像处理", Order = 1)]
        [SettingsItem("匹配算法", Description = "选择用于模板匹配的算法，可多选", Options = "标准相关性匹配:NCC|边缘特征匹配:Edge|特征点匹配:FeaturePoints|感知哈希验证:PHash|带掩模匹配:Masked")]
        public MatchAlgorithm MatchAlgorithms{ get; set; } = MatchAlgorithm.NCC;

        /// <summary>混合匹配的综合方式</summary>
        [SettingsGroup("图像处理", Order = 1)]
        [SettingsItem("混合匹配方式", Description = "当使用多个算法时，如何综合结果", Options = "平均值:Average|最大值:Max|最小值:Min|加权平均:WeightedAverage")]
        public MatchCombineMode MatchCombineMode{ get; set; } = MatchCombineMode.Average;

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
