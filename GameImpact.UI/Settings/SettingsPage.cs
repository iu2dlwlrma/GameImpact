using System.Collections.Generic;
using System.Windows;

namespace GameImpact.UI.Settings;

/// <summary>
/// 表示设置导航栏中的一个页签节点。
/// 支持树形嵌套：顶层节点为类别（如"应用设置"），子节点为具体分组。
/// </summary>
public class SettingsPage
{
    /// <summary>
    /// 页签显示标题
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// 页签图标（Emoji 或文字前缀），可选
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// 排序权重（数值越小越靠前）
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// 子页签列表（为空则表示叶子节点）
    /// </summary>
    public List<SettingsPage> Children { get; init; } = new();

    /// <summary>
    /// 关联的内容视图（仅叶子节点有值，分类节点为 null）。
    /// 当用户点击此页签时，右侧内容区显示此视图。
    /// </summary>
    public FrameworkElement? Content { get; init; }

    /// <summary>
    /// 拼接了图标的显示文本
    /// </summary>
    public string DisplayTitle => string.IsNullOrEmpty(Icon)
        ? Title
        : $"{Icon} {Title}";

    /// <summary>
    /// 是否为叶子节点（无子节点且有内容视图）
    /// </summary>
    public bool IsLeaf => Children.Count == 0;
}
