#region

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

#endregion

namespace GameImpact.UI.Settings
{
    /// <summary>设置窗口：左侧动态树形导航，右侧内容区显示对应的设置视图。 导航栏由 SettingsPage 列表动态构建，支持多级子页签。</summary>
    public partial class SettingsWindow : Window
    {
        /// <summary>页签节点到 TreeViewItem 的映射，用于查找关联的 SettingsPage</summary>
        private readonly Dictionary<TreeViewItem, SettingsPage> m_treeItemMap = new();

        /// <summary>创建设置窗口</summary>
        /// <param name="pages">设置页签列表（支持多级嵌套）</param>
        public SettingsWindow(IEnumerable<SettingsPage> pages)
        {
            InitializeComponent();
            BuildNavTree(pages);
            SelectFirstLeaf();
        }

        /// <summary>根据 SettingsPage 列表递归构建左侧树形导航</summary>
        private void BuildNavTree(IEnumerable<SettingsPage> pages)
        {
            foreach (var page in pages.OrderBy(p => p.Order))
            {
                var treeItem = CreateTreeViewItem(page);
                NavTree.Items.Add(treeItem);
            }
        }

        /// <summary>递归创建 TreeViewItem</summary>
        private TreeViewItem CreateTreeViewItem(SettingsPage page)
        {
            var treeItem = new TreeViewItem
            {
                    Header = page.DisplayTitle,
                    IsExpanded = true
            };

            m_treeItemMap[treeItem] = page;

            // 递归添加子页签
            foreach (var child in page.Children.OrderBy(c => c.Order))
            {
                var childItem = CreateTreeViewItem(child);
                treeItem.Items.Add(childItem);
            }

            return treeItem;
        }

        /// <summary>默认选中第一个叶子节点</summary>
        private void SelectFirstLeaf()
        {
            var firstNode = FindFirstContentNode(NavTree.Items);
            if (firstNode != null)
            {
                firstNode.IsSelected = true;
            }
        }

        /// <summary>递归查找第一个有内容视图的节点（包括同时有子节点和内容的节点）</summary>
        private TreeViewItem? FindFirstContentNode(ItemCollection items)
        {
            foreach (var item in items)
            {
                if (item is not TreeViewItem treeItem)
                {
                    continue;
                }

                if (m_treeItemMap.TryGetValue(treeItem, out var page) && page.Content != null)
                {
                    return treeItem;
                }

                var childNode = FindFirstContentNode(treeItem.Items);
                if (childNode != null)
                {
                    return childNode;
                }
            }

            return null;
        }

        /// <summary>树形导航选中项变更</summary>
        private void OnNavTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ContentHost == null)
            {
                return;
            }

            if (e.NewValue is TreeViewItem treeItem && m_treeItemMap.TryGetValue(treeItem, out var page))
            {
                if (page.Content != null)
                {
                    // 有内容的节点（包括同时有子节点和自身内容的一级页签）：直接显示
                    ContentHost.Content = page.Content;
                }
                else if (page.Children.Count > 0)
                {
                    // 纯容器节点（无自身内容）：自动选中其第一个叶子子节点
                    var firstChildNode = FindFirstContentNode(treeItem.Items);
                    if (firstChildNode != null)
                    {
                        firstChildNode.IsSelected = true;
                    }
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
