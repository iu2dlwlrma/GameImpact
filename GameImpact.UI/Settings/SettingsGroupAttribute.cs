using System;

namespace GameImpact.UI.Settings;

/// <summary>
/// 标注在 Settings 模型的属性上，定义该属性所属的设置分组。
/// 同一个 GroupName 的属性会被归入同一个卡片区域内。
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SettingsGroupAttribute : Attribute
{
    /// <summary>
    /// 分组的显示名称
    /// </summary>
    public string GroupName { get; }

    /// <summary>
    /// 分组的排序权重（数值越小越靠前），默认为 0
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// 分组的图标（Emoji 或文字前缀），可选
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// 创建分组 Attribute
    /// </summary>
    /// <param name="groupName">分组的显示名称</param>
    public SettingsGroupAttribute(string groupName)
    {
        GroupName = groupName;
    }
}
