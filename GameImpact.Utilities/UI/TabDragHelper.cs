using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GameImpact.Utilities.Logging;

namespace GameImpact.Utilities.UI
{
    /// <summary>Tab拖拽辅助类，支持将TabItem拖出成独立窗口，并支持拖回</summary>
    public class TabDragHelper
    {
        private readonly TabControl m_tabControl;
        private readonly Window m_parentWindow;
        private TabItem? m_draggedTab;
        private Point m_dragStartPoint;
        private bool m_isDragging;
        private DateTime m_mouseDownTime;
        private Dictionary<Window, TabItem> m_detachedTabs = new();
        private bool m_isClosing = false;

        /// <summary>构造函数</summary>
        /// <param name="tabControl">要启用拖拽功能的TabControl</param>
        /// <param name="parentWindow">父窗口</param>
        public TabDragHelper(TabControl tabControl, Window parentWindow)
        {
            m_tabControl = tabControl ?? throw new ArgumentNullException(nameof(tabControl));
            m_parentWindow = parentWindow ?? throw new ArgumentNullException(nameof(parentWindow));
            
            // 监听父窗口关闭事件
            parentWindow.Closing += (s, e) =>
            {
                m_isClosing = true;
                m_detachedTabs.Clear();
            };
        }

        /// <summary>TabItem鼠标按下事件 - 开始拖拽</summary>
        public void OnTabItemMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 如果只剩一个Tab，不允许拖出
            if (m_tabControl.Items.Count <= 1)
            {
                return;
            }

            // 查找父TabItem
            var tabItem = UIHelper.FindParent<TabItem>(sender as DependencyObject);
            if (tabItem != null && e.ClickCount == 1)
            {
                m_draggedTab = tabItem;
                m_dragStartPoint = e.GetPosition(m_parentWindow);
                m_mouseDownTime = DateTime.Now;
                m_isDragging = false;
                // 不设置 e.Handled，允许正常的Tab选择行为
            }
        }

        /// <summary>TabItem鼠标移动事件 - 检测拖拽</summary>
        public void OnTabItemMouseMove(object sender, MouseEventArgs e)
        {
            if (m_draggedTab != null && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPoint = e.GetPosition(m_parentWindow);
                var delta = currentPoint - m_dragStartPoint;
                var pressDuration = DateTime.Now - m_mouseDownTime;

                // 需要「长按 + 一定距离」才触发拖出
                if (!m_isDragging &&
                    pressDuration.TotalMilliseconds >= 150 &&
                    (Math.Abs(delta.X) > 10 || Math.Abs(delta.Y) > 10))
                {
                    // 如果只剩一个Tab，不允许拖出
                    if (m_tabControl.Items.Count <= 1)
                    {
                        m_draggedTab = null;
                        return;
                    }

                    m_isDragging = true;
                    // 捕获鼠标，阻止Tab选择
                    m_draggedTab.CaptureMouse();
                    DetachTab(m_draggedTab, currentPoint);
                }
            }
        }

        /// <summary>TabItem鼠标释放事件 - 结束拖拽</summary>
        public void OnTabItemMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (m_draggedTab != null)
            {
                if (m_isDragging)
                {
                    // 如果正在拖拽，释放鼠标捕获
                    m_draggedTab.ReleaseMouseCapture();
                }
                m_draggedTab = null;
                m_isDragging = false;
            }
        }

        /// <summary>将Tab分离成独立窗口</summary>
        private void DetachTab(TabItem tabItem, Point mousePosition)
        {
            if (tabItem.Parent != m_tabControl)
            {
                return;
            }

            // 释放鼠标捕获
            if (tabItem.IsMouseCaptured)
            {
                tabItem.ReleaseMouseCapture();
            }

            // 获取Tab的内容和标题
            var content = tabItem.Content;
            var header = tabItem.Header;

            // 先断开内容与TabItem的连接
            tabItem.Content = null;

            // 从TabControl中移除
            m_tabControl.Items.Remove(tabItem);

            // 创建新窗口
            var newWindow = new Window
            {
                Title = header?.ToString() ?? "调试面板",
                Width = 600,
                Height = 400,
                MinWidth = 400,
                MinHeight = 300,
                WindowStartupLocation = WindowStartupLocation.Manual,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Owner = m_parentWindow
            };

            // 设置窗口位置（让新窗口标题栏中心大致出现在鼠标位置）
            var screenPos = new Point(
                m_parentWindow.Left + mousePosition.X,
                m_parentWindow.Top + mousePosition.Y);
            newWindow.Left = screenPos.X - newWindow.Width / 2;
            newWindow.Top = screenPos.Y - 20;

            // 创建窗口内容
            var border = new Border
            {
                Background = (Brush)m_parentWindow.FindResource("AppBackground"),
                CornerRadius = new CornerRadius(8),
                BorderBrush = (Brush)m_parentWindow.FindResource("AppBorder"),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });                        // 标题栏
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });      // 内容区
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                           // 右下角 ResizeGrip

            // 标题栏 - 支持拖拽和拖回检测
            var titleBar = new Border
            {
                Background = (Brush)m_parentWindow.FindResource("AppSurface"),
                CornerRadius = new CornerRadius(8, 8, 0, 0)
            };

            // 标题栏拖拽：使用系统 DragMove，自由拖动
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try
                    {
                        newWindow.DragMove();
                    }
                    catch
                    {
                        // 忽略拖拽异常
                    }

                    // 拖动结束后，判断鼠标是否停在父窗口的 TabControl 区域，如果是则认为是“拖回页签”
                    try
                    {
                        if (m_parentWindow.IsLoaded && !m_isClosing)
                        {
                            var mousePosInTab = Mouse.GetPosition(m_tabControl);
                            var tabRect = new Rect(
                                new Point(0, 0),
                                new Size(m_tabControl.ActualWidth, m_tabControl.ActualHeight));

                            if (tabRect.Contains(mousePosInTab))
                            {
                                ReattachTab(newWindow, tabItem, content, header);
                                newWindow.Close();
                            }
                        }
                    }
                    catch
                    {
                        // 父窗口已关闭等情况，忽略错误
                    }
                }
            };

            var titleGrid = new Grid();
            var titleStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(new TextBlock
            {
                Text = header?.ToString() ?? "调试面板",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)m_parentWindow.FindResource("AppText"),
                VerticalAlignment = VerticalAlignment.Center
            });

            var closeButton = new Button
            {
                Style = (Style)m_parentWindow.FindResource("WindowCloseButton"),
                Content = new TextBlock { Text = "✕", FontSize = 11 },
                ToolTip = "关闭",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            closeButton.Click += (s, e) => newWindow.Close();

            titleGrid.Children.Add(titleStack);
            titleGrid.Children.Add(closeButton);
            titleBar.Child = titleGrid;

            Grid.SetRow(titleBar, 0);
            grid.Children.Add(titleBar);

            // 内容区域
            var contentBorder = new Border
            {
                Background = (Brush)m_parentWindow.FindResource("AppSurface"),
                CornerRadius = new CornerRadius(0, 6, 6, 6),
                BorderBrush = (Brush)m_parentWindow.FindResource("AppBorderSubtle"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(10, 6, 10, 10)
            };

            if (content is UIElement uiElement)
            {
                contentBorder.Child = uiElement;
            }
            else if (content is FrameworkElement frameworkElement)
            {
                contentBorder.Child = frameworkElement;
            }

            Grid.SetRow(contentBorder, 1);
            grid.Children.Add(contentBorder);

            // 底部右下角的 ResizeGrip，用于调整窗口大小
            var resizeGrip = new ResizeGrip
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 6),
            };
            Grid.SetRow(resizeGrip, 2);
            grid.Children.Add(resizeGrip);

            border.Child = grid;
            newWindow.Content = border;

            // 保存窗口和TabItem的映射关系
            m_detachedTabs[newWindow] = tabItem;

            // 窗口关闭时，将Tab重新添加回TabControl
            newWindow.Closed += (s, e) =>
            {
                if (m_detachedTabs.TryGetValue(newWindow, out var detachedTab))
                {
                    // 只有在父窗口还存在且窗口正常关闭时才恢复Tab
                    if (!m_isClosing && m_parentWindow.IsLoaded && newWindow.DialogResult == null)
                    {
                        ReattachTab(newWindow, detachedTab, content, header);
                    }
                    m_detachedTabs.Remove(newWindow);
                }
            };

            newWindow.Show();

            // 刚从 Tab 拖出时，如果鼠标仍然按下，立即开始拖动，让窗口“吸附”在鼠标下方
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    newWindow.DragMove();
                }
                catch
                {
                    // 如果当前环境不允许 DragMove（例如鼠标不在窗口上），则忽略
                }
            }
        }

        /// <summary>将分离的Tab重新附加回TabControl</summary>
        private void ReattachTab(Window window, TabItem tabItem, object? originalContent, object? originalHeader)
        {
            try
            {
                // 检查父窗口是否仍然存在且已加载
                if (!m_parentWindow.IsLoaded || m_isClosing)
                {
                    return;
                }

                // 从窗口中获取内容
                object? uiContent = null;
                if (window.Content is Border border && border.Child is Grid grid && grid.Children.Count > 1)
                {
                    var contentBorder = grid.Children[1] as Border;
                    uiContent = contentBorder?.Child;

                    // 断开内容与窗口的连接
                    if (contentBorder != null)
                    {
                        contentBorder.Child = null;
                    }
                }

                // 使用窗口中的内容，如果没有则使用原始内容
                var contentToRestore = uiContent ?? originalContent;

                // 恢复TabItem的内容
                tabItem.Content = contentToRestore;
                tabItem.Header = originalHeader;

                // 将TabItem添加回TabControl
                if (!m_tabControl.Items.Contains(tabItem))
                {
                    m_tabControl.Items.Add(tabItem);
                }

                // 选中这个Tab
                tabItem.IsSelected = true;
            }
            catch (Exception ex)
            {
                Log.Debug($"[TabDragHelper] 恢复Tab失败: {ex.Message}");
            }
        }
    }
}
