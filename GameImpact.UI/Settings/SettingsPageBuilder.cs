#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

#endregion

namespace GameImpact.UI.Settings
{
    /// <summary>设置页签构建器：将 Settings 模型按分组自动拆分成树形 SettingsPage 结构。 用于 SettingsWindow 动态生成导航栏。</summary>
    public static class SettingsPageBuilder
    {
        /// <summary>将一个 Settings 模型拆分成带子页签的 SettingsPage 节点。 每个 SettingsGroup 生成一个子页签，各自独立的 AutoSettingsView。</summary>
        /// <typeparam name="T">设置模型类型</typeparam>
        /// <param name="settingsProvider">设置持久化提供者</param>
        /// <param name="title">顶层分类标题（如"应用设置"）</param>
        /// <param name="icon">顶层分类图标</param>
        /// <param name="order">顶层分类排序权重</param>
        /// <param name="settingChangedHandler">设置项变更回调（可选）</param>
        /// <returns>带子页签的 SettingsPage 节点</returns>
        public static SettingsPage Build<T>(ISettingsProvider<T> settingsProvider,
                string title,
                string icon = "",
                int order = 0,
                Action<T, string>? settingChangedHandler = null) where T : class, new()
        {
            var allGroups = AutoSettingsView<T>.GetAllGroups();

            // 分离：无分组项（GroupName 为空）和有分组项
            var ungrouped = allGroups.Where(g => string.IsNullOrEmpty(g.GroupName)).ToList();
            var grouped = allGroups.Where(g => !string.IsNullOrEmpty(g.GroupName)).ToList();

            // 如果全部都没有分组，整体作为一个叶子节点
            if (grouped.Count == 0)
            {
                var view = new AutoSettingsView<T>(settingsProvider);

                if (settingChangedHandler != null)
                {
                    view.SettingChanged += settingChangedHandler;
                }

                return new SettingsPage
                {
                        Title = title,
                        Icon = icon,
                        Order = order,
                        Content = view
                };
            }

            // 一级节点自身的内容视图：无分组项（如果有的话）
            FrameworkElement? selfContent = null;

            if (ungrouped.Count > 0)
            {
                var ungroupedView = new AutoSettingsView<T>(settingsProvider, ungrouped);

                if (settingChangedHandler != null)
                {
                    ungroupedView.SettingChanged += settingChangedHandler;
                }

                selfContent = ungroupedView;
            }

            // 有分组项：每个分组生成一个子页签
            var children = new List<SettingsPage>();

            foreach (var group in grouped)
            {
                var groupView = new AutoSettingsView<T>(
                        settingsProvider,
                        new List<SettingsGroupMetadata> { group });

                if (settingChangedHandler != null)
                {
                    groupView.SettingChanged += settingChangedHandler;
                }

                children.Add(new SettingsPage
                {
                        Title = group.GroupName,
                        Icon = group.Icon,
                        Order = group.Order,
                        Content = groupView
                });
            }

            // 如果没有无分组项且只有一个分组，不创建多余层级
            if (selfContent == null && children.Count == 1)
            {
                return new SettingsPage
                {
                        Title = title,
                        Icon = icon,
                        Order = order,
                        Content = children[0].Content
                };
            }

            return new SettingsPage
            {
                    Title = title,
                    Icon = icon,
                    Order = order,
                    Content = selfContent,
                    Children = children.OrderBy(c => c.Order).ToList()
            };
        }

        /// <summary>创建一个纯容器页签（仅包含子页签，无自己的内容视图）。 适用于手动组织多个不同来源的设置页面。</summary>
        /// <param name="title">分类标题</param>
        /// <param name="icon">分类图标</param>
        /// <param name="order">排序权重</param>
        /// <param name="children">子页签列表</param>
        public static SettingsPage CreateCategory(string title,
                string icon = "",
                int order = 0,
                params SettingsPage[] children)
        {
            return new SettingsPage
            {
                    Title = title,
                    Icon = icon,
                    Order = order,
                    Children = children.OrderBy(c => c.Order).ToList()
            };
        }

        /// <summary>创建一个叶子页签（直接关联一个内容视图）。 适用于自定义的非 AutoSettingsView 内容。</summary>
        /// <param name="title">页签标题</param>
        /// <param name="content">内容视图</param>
        /// <param name="icon">页签图标</param>
        /// <param name="order">排序权重</param>
        public static SettingsPage CreateLeaf(string title,
                FrameworkElement content,
                string icon = "",
                int order = 0)
        {
            return new SettingsPage
            {
                    Title = title,
                    Icon = icon,
                    Order = order,
                    Content = content
            };
        }
    }
}
