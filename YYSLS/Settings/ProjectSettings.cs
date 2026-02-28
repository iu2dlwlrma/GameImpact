using GameImpact.UI.Settings;

namespace YYSLS.Settings;

/// <summary>
/// YYSLS 项目业务设置模型。
/// 通过 SettingsItemAttribute 和 SettingsGroupAttribute 自动生成设置界面。
/// </summary>
public class ProjectSettings
{
    /// <summary>
    /// 目标游戏窗口标题（用于自动匹配窗口）
    /// </summary>
    [SettingsGroup("窗口与捕获", Order = 0)]
    [SettingsItem("目标窗口标题", Description = "启动时自动匹配包含此标题的窗口", Order = 0)]
    public string TargetWindowTitle { get; set; } = string.Empty;


}
