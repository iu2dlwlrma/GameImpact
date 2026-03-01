#region

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameImpact.UI.Models;
using GameImpact.UI.Views.Debug;
using GameImpact.Utilities.UI;

#endregion

namespace GameImpact.UI.Views
{
    /// <summary>独立调试窗口，包含日志、预览、坐标/OCR/键盘测试等调试功能</summary>
    public partial class DebugWindow
    {
#region 字段和属性

        private readonly MainModel m_model;
        private TabDragHelper? m_tabDragHelper;
        private BitmapSource? m_lastMatchImage;
        private BitmapSource? m_lastCaptureImage;

#endregion

#region 构造函数和初始化

        /// <summary>构造函数</summary>
        /// <param name="model">主视图模型</param>
        public DebugWindow(MainModel model)
        {
            InitializeComponent();
            m_model = model;
            DataContext = model;

            // 初始化UserControl（传递model）
            InitializeUserControls();

            Loaded += DebugWindow_Loaded;
            Closing += DebugWindow_Closing;
        }

        /// <summary>初始化UserControl</summary>
        private void InitializeUserControls()
        {
            if (LogTabControl != null)
            {
                LogTabControl.SetModel(m_model);
            }

            if (PreviewTabControl != null)
            {
                PreviewTabControl.SetModel(m_model);
            }

            if (CoordinateTabControl != null)
            {
                CoordinateTabControl.SetModel(m_model);
            }
        }

        /// <summary>窗口加载完成后初始化</summary>
        private void DebugWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 初始化Tab拖拽功能
            m_tabDragHelper = new TabDragHelper(MainTabControl, this);
        }

        /// <summary>DebugWindow关闭时清理资源</summary>
        private void DebugWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (Owner != null && Owner.IsLoaded)
            {
                Owner.Focus();
            }
        }

#endregion

#region 窗口基础功能

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

#endregion

#region Tab拖拽功能

        /// <summary>TabItem鼠标按下事件 - 开始拖拽</summary>
        private void TabItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            m_tabDragHelper?.OnTabItemMouseLeftButtonDown(sender, e);
        }

        /// <summary>TabItem鼠标移动事件 - 检测拖拽</summary>
        private void TabItem_MouseMove(object sender, MouseEventArgs e)
        {
            m_tabDragHelper?.OnTabItemMouseMove(sender, e);
        }

        /// <summary>TabItem鼠标释放事件 - 结束拖拽</summary>
        private void TabItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            m_tabDragHelper?.OnTabItemMouseLeftButtonUp(sender, e);
        }

#endregion

#region 图像页签辅助方法

        /// <summary>设置匹配图像（供CoordinateTab调用）</summary>
        public void SetMatchImage(BitmapSource? bitmapSource)
        {
            m_lastMatchImage = bitmapSource;
            System.Diagnostics.Debug.WriteLine($"[DebugWindow] SetMatchImage: {(bitmapSource != null ? "已设置" : "为空")}");
        }

        /// <summary>设置捕获图像（供CoordinateTab调用）</summary>
        public void SetCaptureImage(BitmapSource? bitmapSource)
        {
            m_lastCaptureImage = bitmapSource;
            System.Diagnostics.Debug.WriteLine($"[DebugWindow] SetCaptureImage: {(bitmapSource != null ? "已设置" : "为空")}");
        }

        /// <summary>在独立窗口中打开匹配图 / 捕获图预览</summary>
        public void OpenImagePreviewWindows()
        {
            System.Diagnostics.Debug.WriteLine($"[DebugWindow] OpenImagePreviewWindows: MatchImage={(m_lastMatchImage != null ? $"有 ({m_lastMatchImage.Width}x{m_lastMatchImage.Height})" : "无")}, CaptureImage={(m_lastCaptureImage != null ? $"有 ({m_lastCaptureImage.Width}x{m_lastCaptureImage.Height})" : "无")}");

            if (m_lastMatchImage == null && m_lastCaptureImage == null)
            {
                System.Windows.MessageBox.Show("没有图像可显示，请先执行模板匹配", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 计算屏幕中心位置
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var windowWidth = 600.0;
            var windowHeight = 400.0;
            var gap = 20.0; // 两个窗口之间的间距

            // 计算两个窗口的总宽度
            var totalWidth = (m_lastMatchImage != null ? windowWidth : 0) + 
                           (m_lastCaptureImage != null ? windowWidth : 0) + 
                           (m_lastMatchImage != null && m_lastCaptureImage != null ? gap : 0);

            // 计算起始X位置（使两个窗口在屏幕中心并排）
            var startX = (screenWidth - totalWidth) / 2;
            var centerY = (screenHeight - windowHeight) / 2;

            // 创建匹配图预览窗口（左侧）
            if (m_lastMatchImage != null)
            {
                CreateImagePreviewWindow("匹配图预览", m_lastMatchImage, startX, centerY);
                startX += windowWidth + gap; // 更新下一个窗口的X位置
            }

            // 创建捕获图预览窗口（右侧）
            if (m_lastCaptureImage != null)
            {
                CreateImagePreviewWindow("捕获图预览", m_lastCaptureImage, startX, centerY);
            }
        }

        /// <summary>创建一个简单的图像预览窗口（不会参与 Tab 拖拽/吸附）</summary>
        private void CreateImagePreviewWindow(string title, BitmapSource source, double left, double top)
        {
            var window = new Window
            {
                    Title = title,
                    Width = 600,
                    Height = 400,
                    MinWidth = 300,
                    MinHeight = 200,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    ResizeMode = ResizeMode.CanResizeWithGrip,
                    Owner = this,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent
            };

            // 位置：直接使用传入的屏幕坐标
            window.Left = left;
            window.Top = top;

            // 创建窗口内容容器 - 简化边框
            var border = new Border
            {
                    Background = (Brush)FindResource("AppBackground"),
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = (Brush)FindResource("AppBorder"),
                    BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) }); // 标题栏 - 减少高度
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容区
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // ResizeGrip

            // 标题栏
            var titleBar = new Border
            {
                    Background = (Brush)FindResource("AppSurface"),
                    CornerRadius = new CornerRadius(6, 6, 0, 0)
            };

            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try
                    {
                        window.DragMove();
                    }
                    catch
                    {
                        // 忽略拖拽异常
                    }
                }
            };

            var titleGrid = new Grid();
            var titleStack = new StackPanel
            {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(new TextBlock
            {
                    Text = title,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("AppText"),
                    VerticalAlignment = VerticalAlignment.Center
            });

            var closeButton = new Button
            {
                    Style = (Style)FindResource("WindowCloseButton"),
                    Content = new TextBlock { Text = "✕", FontSize = 10 },
                    ToolTip = "关闭",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Width = 24,
                    Height = 24
            };
            closeButton.Click += (s, e) => window.Close();

            titleGrid.Children.Add(titleStack);
            titleGrid.Children.Add(closeButton);
            titleBar.Child = titleGrid;

            Grid.SetRow(titleBar, 0);
            grid.Children.Add(titleBar);

            // 内容区域 - 使用 ImagePreview UserControl，移除多余的边框和边距
            var imagePreview = new ImagePreview();
            imagePreview.SetImage(source);

            var contentBorder = new Border
            {
                    Background = (Brush)FindResource("AppBackground"),
                    Margin = new Thickness(0)
            };
            contentBorder.Child = imagePreview;

            Grid.SetRow(contentBorder, 1);
            grid.Children.Add(contentBorder);

            // ResizeGrip
            var resizeGrip = new ResizeGrip
            {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 6, 6)
            };
            Grid.SetRow(resizeGrip, 2);
            grid.Children.Add(resizeGrip);

            border.Child = grid;
            window.Content = border;

            window.Show();
        }

#endregion
    }
}
