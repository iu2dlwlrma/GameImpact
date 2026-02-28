using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace GameImpact.UI.Settings;

/// <summary>
/// 描述单个设置项的反射元数据（已缓存）
/// </summary>
public sealed class SettingsItemMetadata
{
    /// <summary>
    /// 对应的属性反射信息
    /// </summary>
    public PropertyInfo Property { get; init; } = null!;

    /// <summary>
    /// 设置项 Attribute
    /// </summary>
    public SettingsItemAttribute Item { get; init; } = null!;

    /// <summary>
    /// 所属分组 Attribute（可能为 null 表示无分组）
    /// </summary>
    public SettingsGroupAttribute? Group { get; init; }
}

/// <summary>
/// 描述一个设置分组，包含该组下的所有设置项
/// </summary>
public sealed class SettingsGroupMetadata
{
    /// <summary>
    /// 分组显示名称
    /// </summary>
    public string GroupName { get; init; } = string.Empty;

    /// <summary>
    /// 分组排序权重
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// 分组图标
    /// </summary>
    public string Icon { get; init; } = string.Empty;

    /// <summary>
    /// 该组下的所有设置项（已按 Order 排序）
    /// </summary>
    public List<SettingsItemMetadata> Items { get; init; } = new();
}

/// <summary>
/// 设置模型反射元数据缓存。
/// 对每个 Settings 类型只做一次反射解析，后续全部走缓存。
/// </summary>
public static class SettingsMetadataCache
{
    private static readonly ConcurrentDictionary<Type, List<SettingsGroupMetadata>> s_cache = new();

    /// <summary>
    /// 获取指定 Settings 类型的分组元数据列表（带缓存）
    /// </summary>
    public static List<SettingsGroupMetadata> GetGroups<T>() where T : class
    {
        return s_cache.GetOrAdd(typeof(T), type => BuildGroups(type));
    }

    /// <summary>
    /// 通过反射解析 Settings 类型中所有带 SettingsItemAttribute 的属性
    /// </summary>
    private static List<SettingsGroupMetadata> BuildGroups(Type type)
    {
        var items = new List<SettingsItemMetadata>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var itemAttr = property.GetCustomAttribute<SettingsItemAttribute>();
            if (itemAttr == null)
            {
                continue;
            }

            var groupAttr = property.GetCustomAttribute<SettingsGroupAttribute>();

            items.Add(new SettingsItemMetadata
            {
                Property = property,
                Item = itemAttr,
                Group = groupAttr
            });
        }

        // 按分组聚合，未指定分组的用空字符串标识（不归入"其他"）
        var groups = items
            .GroupBy(i => i.Group?.GroupName ?? string.Empty)
            .Select(g =>
            {
                var firstGroup = g.FirstOrDefault(i => i.Group != null)?.Group;

                return new SettingsGroupMetadata
                {
                    GroupName = g.Key,
                    Order = firstGroup?.Order ?? -1,
                    Icon = firstGroup?.Icon ?? string.Empty,
                    Items = g.OrderBy(i => i.Item.Order).ToList()
                };
            })
            .OrderBy(g => g.Order)
            .ToList();

        return groups;
    }
}
