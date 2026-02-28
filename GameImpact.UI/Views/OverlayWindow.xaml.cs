#region

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using GameImpact.Utilities.Logging;
using Rect = OpenCvSharp.Rect;

#endregion

namespace GameImpact.UI.Views
{
    /// <summary>覆盖层窗口，用于在目标窗口上显示叠加信息、坐标拾取、截图框选等功能。</summary>
    public partial class OverlayWindow : Window
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20;

        private const int MaxLogLines = 18;
        private static OverlayWindow? s_instance;

        // ── 叠加日志 ──────────────────────────────────────────────
        /// <summary>日志级别优先级，数值越大越严重</summary>
        private static readonly Dictionary<string, int> s_levelPriority = new()
        {
                ["DBG"] = 0, ["INF"] = 1, ["WRN"] = 2, ["ERR"] = 3
        };

        private static readonly Dictionary<string, Brush> s_levelColor = new()
        {
                ["DBG"] = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                ["INF"] = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                ["WRN"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                ["ERR"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55))
        };
        private readonly DispatcherTimer m_positionTimer;
        private double m_dpiScale = 1.0;
        private bool m_isPickingCoord;
        private bool m_isScreenshotRegion;
        private string m_logMinLevel = "INF";
        private Action<int, int>? m_onCoordPicked;
        private Action<int, int, int, int>? m_onScreenshotRegionComplete;
        private nint m_overlayHwnd;
        private Point m_screenshotStart;

        private nint m_targetHwnd;
        // ─────────────────────────────────────────────────────────

        private OverlayWindow()
        {
            InitializeComponent();

            m_positionTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            m_positionTimer.Tick += UpdatePosition;

            Log.OnScreenLogMessage += OnScreenLogReceived;

            Loaded += OnLoaded;
            Closing += OnClosing;
        }
        public static OverlayWindow Instance => s_instance ??= new OverlayWindow();

        /// <summary>显示/隐藏叠加日志面板，并设置最低显示级别（"DBG"/"INF"/"WRN"/"ERR"）。</summary>
        public void SetOverlayLog(bool visible, string minLevel = "INF")
        {
            m_logMinLevel = s_levelPriority.ContainsKey(minLevel) ? minLevel : "INF";

            Dispatcher.Invoke(() =>
            {
                LogOverlayPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                LogLevelBadge.Text = visible ? $"[{m_logMinLevel}+]" : string.Empty;
                if (!visible)
                {
                    LogLines.Children.Clear();
                }
            });
        }

        /// <summary>窗口关闭事件处理</summary>
        private void OnClosing(object? sender, CancelEventArgs e)
        {
            e.Cancel = true;
            Detach();
        }

        /// <summary>窗口加载完成事件处理</summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            m_overlayHwnd = new WindowInteropHelper(this).Handle;

            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                m_dpiScale = source.CompositionTarget.TransformToDevice.M11;
            }

            var exStyle = GetWindowLong(m_overlayHwnd, GWL_EXSTYLE);
            SetWindowLong(m_overlayHwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        /// <summary>将覆盖层附加到目标窗口</summary>
        /// <param name="targetHwnd">目标窗口句柄</param>
        public void AttachTo(nint targetHwnd)
        {
            m_targetHwnd = targetHwnd;

            var monitor = MonitorFromWindow(targetHwnd, 2);
            if (GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0)
            {
                m_dpiScale = dpiX / 96.0;
            }

            UpdateWindowPosition();
            Show();
            m_positionTimer.Start();
        }

        /// <summary>从目标窗口分离覆盖层</summary>
        public void Detach()
        {
            if (m_isPickingCoord)
            {
                StopPickCoord();
            }
            if (m_isScreenshotRegion)
            {
                StopScreenshotRegion();
            }
            m_positionTimer.Stop();
            m_targetHwnd = 0;
            Hide();
            ClearInfo();
            ClearDrawings();
        }

        /// <summary>强制关闭覆盖层窗口</summary>
        public void ForceClose()
        {
            Log.OnScreenLogMessage -= OnScreenLogReceived;
            m_positionTimer.Stop();
            Closing -= OnClosing;
            Close();
            s_instance = null;
        }

        /// <summary>进入拾取/截图等需要接收鼠标的模式时，去掉透明与不激活，使覆盖层可点击。</summary>
        /// <param name="receive">是否接收输入</param>
        private void SetOverlayReceivesInput(bool receive)
        {
            if (m_overlayHwnd == nint.Zero)
            {
                return;
            }

            var exStyle = GetWindowLong(m_overlayHwnd, GWL_EXSTYLE);
            if (receive)
            {
                // 移除透明和不激活标志
                _ = SetWindowLong(m_overlayHwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT & ~WS_EX_NOACTIVATE);
            }
            else
            {
                // 恢复透明和不激活标志
                _ = SetWindowLong(m_overlayHwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
            }
        }

        /// <summary>将覆盖层窗口置于前台并激活，确保能收到鼠标键盘。</summary>
        private void BringOverlayToFront()
        {
            if (m_overlayHwnd == nint.Zero)
            {
                return;
            }
            SetForegroundWindow(m_overlayHwnd);
        }

        /// <summary>将目标窗口置于前台</summary>
        private void BringTargetWindowToFront()
        {
            if (m_targetHwnd == 0)
            {
                return;
            }

            if (IsIconic(m_targetHwnd))
            {
                ShowWindow(m_targetHwnd, 9);
            }

            SetForegroundWindow(m_targetHwnd);
        }

        /// <summary>隐藏应用程序的其他窗口</summary>
        private void HideAppWindow()
        {
            try
            {
                // 隐藏除当前 OverlayWindow 之外的所有窗口
                foreach (Window window in Application.Current.Windows)
                {
                    if (window != this && window != null)
                    {
                        var hwnd = new WindowInteropHelper(window).Handle;
                        if (hwnd != nint.Zero)
                        {
                            ShowWindow(hwnd, SW_HIDE);
                        }
                    }
                }
            }
            catch
            {
                /* ignore */
            }
        }

        /// <summary>显示应用程序的其他窗口</summary>
        private void ShowAppWindow()
        {
            try
            {
                // 显示除当前 OverlayWindow 之外的所有窗口
                foreach (Window window in Application.Current.Windows)
                {
                    if (window != this && window != null)
                    {
                        var hwnd = new WindowInteropHelper(window).Handle;
                        if (hwnd != nint.Zero)
                        {
                            ShowWindow(hwnd, SW_SHOW);
                        }
                    }
                }
            }
            catch
            {
                /* ignore */
            }
        }

        /// <summary>定时器回调：更新窗口位置和十字线</summary>
        private void UpdatePosition(object? sender, EventArgs e)
        {
            if (m_targetHwnd == 0)
            {
                return;
            }

            if (!IsWindow(m_targetHwnd))
            {
                Detach();
                return;
            }

            UpdateWindowPosition();

            if (m_isPickingCoord || m_isScreenshotRegion)
            {
                UpdateCrosshair();
                // 持续限制光标到窗口范围内，防止游戏鼠标出现
                // 使用定时器比依赖鼠标事件更可靠
                ClipCursorToWindow();
            }
        }

        /// <summary>更新覆盖层窗口位置以匹配目标窗口</summary>
        private void UpdateWindowPosition()
        {
            if (m_targetHwnd == 0)
            {
                return;
            }

            if (GetClientRect(m_targetHwnd, out var clientRect) &&
                    ClientToScreen(m_targetHwnd, out var point))
            {
                Left = point.X / m_dpiScale;
                Top = point.Y / m_dpiScale;
                Width = (clientRect.Right - clientRect.Left) / m_dpiScale;
                Height = (clientRect.Bottom - clientRect.Top) / m_dpiScale;
            }
        }

        /// <summary>将光标限制在窗口范围内</summary>
        private void ClipCursorToWindow()
        {
            var rect = new RECT
            {
                    Left = (int)(Left * m_dpiScale),
                    Top = (int)(Top * m_dpiScale),
                    Right = (int)((Left + Width) * m_dpiScale),
                    Bottom = (int)((Top + Height) * m_dpiScale)
            };
            ClipCursor(ref rect);
        }

        /// <summary>更新十字线和坐标显示</summary>
        private void UpdateCrosshair()
        {
            GetCursorPos(out var screenPos);

            var physX = (int)(screenPos.X - Left * m_dpiScale);
            var physY = (int)(screenPos.Y - Top * m_dpiScale);

            var logX = screenPos.X / m_dpiScale - Left;
            var logY = screenPos.Y / m_dpiScale - Top;

            CrosshairH.Y1 = logY;
            CrosshairH.Y2 = logY;
            CrosshairH.X2 = Width;

            CrosshairV.X1 = logX;
            CrosshairV.X2 = logX;
            CrosshairV.Y2 = Height;

            CoordText.Text = $"X: {physX}  Y: {physY}";

            var panelX = logX + 20;
            var panelY = logY + 20;

            CoordPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var panelSize = CoordPanel.DesiredSize;

            if (panelX + panelSize.Width > Width - 10)
            {
                panelX = logX - panelSize.Width - 15;
            }
            if (panelY + panelSize.Height > Height - 10)
            {
                panelY = logY - panelSize.Height - 15;
            }

            Canvas.SetLeft(CoordPanel, Math.Max(5, panelX));
            Canvas.SetTop(CoordPanel, Math.Max(5, panelY));
        }

#region 信息显示

        /// <summary>显示信息项</summary>
        /// <param name="key">信息项的唯一标识</param>
        /// <param name="text">显示的文本</param>
        /// <param name="foreground">前景色，为null时使用默认白色</param>
        public void ShowInfo(string key, string text, Brush? foreground = null)
        {
            Dispatcher.Invoke(() =>
            {
                var existing = InfoPanel.Children.OfType<Border>()
                        .FirstOrDefault(b => b.Tag?.ToString() == key);

                if (existing != null)
                {
                    if (existing.Child is TextBlock tb)
                    {
                        tb.Text = text;
                    }
                }
                else
                {
                    var border = new Border
                    {
                            Tag = key,
                            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                            CornerRadius = new CornerRadius(4),
                            Padding = new Thickness(8, 4, 8, 4),
                            Margin = new Thickness(0, 0, 0, 4),
                            Child = new TextBlock
                            {
                                    Text = text,
                                    Foreground = foreground ?? Brushes.White,
                                    FontFamily = new FontFamily("Consolas"),
                                    FontSize = 12
                            }
                    };
                    InfoPanel.Children.Add(border);
                }
            });
        }

        /// <summary>隐藏指定信息项</summary>
        /// <param name="key">信息项的唯一标识</param>
        public void HideInfo(string key)
        {
            Dispatcher.Invoke(() =>
            {
                var existing = InfoPanel.Children.OfType<Border>()
                        .FirstOrDefault(b => b.Tag?.ToString() == key);
                if (existing != null)
                {
                    InfoPanel.Children.Remove(existing);
                }
            });
        }

        /// <summary>清除所有信息项</summary>
        public void ClearInfo()
        {
            Dispatcher.Invoke(() => InfoPanel.Children.Clear());
        }

#endregion

#region 日志

        /// <summary>从任意线程接收屏幕日志事件，过滤后投递到 UI 线程</summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        private void OnScreenLogReceived(string level, string message)
        {
            // 级别过滤
            if (!s_levelPriority.TryGetValue(level, out var msgPriority))
            {
                return;
            }
            if (!s_levelPriority.TryGetValue(m_logMinLevel, out var minPriority))
            {
                return;
            }
            if (msgPriority < minPriority)
            {
                return;
            }

            // 面板不可见时不渲染（节省资源）
            if (LogOverlayPanel.Visibility != Visibility.Visible)
            {
                return;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => AppendLogLine(level, message));
        }

        /// <summary>追加一行日志到显示面板</summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        private void AppendLogLine(string level, string message)
        {
            var color = s_levelColor.GetValueOrDefault(level, s_levelColor["INF"]);

            var line = new TextBlock
            {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Foreground = color,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 1, 0, 0),
                    Text = $"[{level}] {message}"
            };

            LogLines.Children.Add(line);

            // 超出最大行数时从顶部移除旧行
            while (LogLines.Children.Count > MaxLogLines)
            {
                LogLines.Children.RemoveAt(0);
            }
        }

#endregion

#region 绘制功能

        /// <summary>绘制点击标记</summary>
        /// <summary>绘制点击标记</summary>
        /// <param name="x">X坐标（物理像素）</param>
        /// <param name="y">Y坐标（物理像素）</param>
        /// <param name="success">是否成功</param>
        /// <param name="duration">显示时长（毫秒）</param>
        public void DrawClickMarker(int x, int y, bool success, int duration = 2000)
        {
            Brush color = success ? Brushes.LawnGreen : Brushes.Red;
            Dispatcher.Invoke(() =>
            {
                var logX = x / m_dpiScale;
                var logY = y / m_dpiScale;

                // 十字标记
                var cross = new Canvas { Tag = "click" };
                var lineH = new Line
                {
                        X1 = logX - 10, Y1 = logY, X2 = logX + 10, Y2 = logY,
                        Stroke = color, StrokeThickness = 2
                };
                var lineV = new Line
                {
                        X1 = logX, Y1 = logY - 10, X2 = logX, Y2 = logY + 10,
                        Stroke = color, StrokeThickness = 2
                };
                var circle = new Ellipse
                {
                        Width = 16, Height = 16,
                        Stroke = color, StrokeThickness = 2
                };
                Canvas.SetLeft(circle, logX - 8);
                Canvas.SetTop(circle, logY - 8);

                cross.Children.Add(lineH);
                cross.Children.Add(lineV);
                cross.Children.Add(circle);
                DrawCanvas.Children.Add(cross);

                // 自动移除
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(duration) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    DrawCanvas.Children.Remove(cross);
                };
                timer.Start();
            });
        }

        /// <summary>绘制 OCR 结果矩形和文字</summary>
        /// <summary>绘制 OCR 结果矩形和文字</summary>
        /// <param name="roi">ROI区域</param>
        /// <param name="results">OCR识别结果列表</param>
        /// <param name="duration">显示时长（毫秒）</param>
        public void DrawOcrResult(Rect roi, List<(Rect box, string text)> results, int duration = 3000)
        {
            Dispatcher.Invoke(() =>
            {
                var container = new Canvas { Tag = "ocr" };

                // 绘制 ROI 区域
                var roiX = roi.X / m_dpiScale;
                var roiY = roi.Y / m_dpiScale;
                var roiW = roi.Width / m_dpiScale;
                var roiH = roi.Height / m_dpiScale;

                var roiRect = new Rectangle
                {
                        Width = roiW, Height = roiH,
                        Stroke = Brushes.Cyan, StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 4, 2 }
                };
                Canvas.SetLeft(roiRect, roiX);
                Canvas.SetTop(roiRect, roiY);
                container.Children.Add(roiRect);

                // 绘制每个识别结果
                foreach (var (box, text) in results)
                {
                    var bx = (roi.X + box.X) / m_dpiScale;
                    var by = (roi.Y + box.Y) / m_dpiScale;
                    var bw = box.Width / m_dpiScale;
                    var bh = box.Height / m_dpiScale;

                    // 文字框
                    var rect = new Rectangle
                    {
                            Width = bw, Height = bh,
                            Stroke = Brushes.LimeGreen, StrokeThickness = 1,
                            Fill = new SolidColorBrush(Color.FromArgb(40, 0, 255, 0))
                    };
                    Canvas.SetLeft(rect, bx);
                    Canvas.SetTop(rect, by);
                    container.Children.Add(rect);

                    // 文字标签
                    var label = new Border
                    {
                            Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)),
                            CornerRadius = new CornerRadius(2),
                            Padding = new Thickness(4, 2, 4, 2),
                            Child = new TextBlock
                            {
                                    Text = text,
                                    Foreground = Brushes.LimeGreen,
                                    FontSize = 11,
                                    FontFamily = new FontFamily("Microsoft YaHei")
                            }
                    };
                    Canvas.SetLeft(label, bx);
                    Canvas.SetTop(label, by - 20);
                    container.Children.Add(label);
                }

                DrawCanvas.Children.Add(container);

                // 自动移除
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(duration) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    DrawCanvas.Children.Remove(container);
                };
                timer.Start();
            });
        }

        /// <summary>清除所有绘制</summary>
        public void ClearDrawings()
        {
            Dispatcher.Invoke(() => DrawCanvas.Children.Clear());
        }

#endregion

#region 坐标拾取

        /// <summary>开始坐标拾取模式</summary>
        /// <param name="onPicked">坐标拾取完成时的回调函数，参数为 (x, y) 物理像素坐标</param>
        public void StartPickCoord(Action<int, int> onPicked)
        {
            m_onCoordPicked = onPicked;
            m_isPickingCoord = true;

            HideAppWindow();
            SetOverlayReceivesInput(true);

            IsHitTestVisible = true;
            DrawBorder.Visibility = Visibility.Visible;
            PickCoordCanvas.Visibility = Visibility.Visible;
            CrosshairCanvas.Visibility = Visibility.Visible;
            CoordCanvas.Visibility = Visibility.Visible;

            CrosshairH.X1 = 0;
            CrosshairH.X2 = Width;
            CrosshairV.Y1 = 0;
            CrosshairV.Y2 = Height;

            MouseMove += OnPickMouseMove;
            MouseLeftButtonDown += OnPickMouseClick;
            KeyDown += OnPickKeyDown;

            Topmost = true;
            Activate();
            Focus();
        }

        /// <summary>停止坐标拾取模式</summary>
        public void StopPickCoord()
        {
            m_isPickingCoord = false;
            m_onCoordPicked = null;

            ShowAppWindow();
            SetOverlayReceivesInput(false);

            IsHitTestVisible = false;
            DrawBorder.Visibility = Visibility.Collapsed;
            PickCoordCanvas.Visibility = Visibility.Collapsed;
            CrosshairCanvas.Visibility = Visibility.Collapsed;
            CoordCanvas.Visibility = Visibility.Collapsed;

            ClipCursor(IntPtr.Zero);

            MouseMove -= OnPickMouseMove;
            MouseLeftButtonDown -= OnPickMouseClick;
            KeyDown -= OnPickKeyDown;
        }

        /// <summary>坐标拾取模式下的鼠标移动事件处理</summary>
        private void OnPickMouseMove(object sender, MouseEventArgs e) { }

        /// <summary>坐标拾取模式下的鼠标点击事件处理</summary>
        private void OnPickMouseClick(object sender, MouseButtonEventArgs e)
        {
            GetCursorPos(out var screenPos);
            var x = (int)(screenPos.X - Left * m_dpiScale);
            var y = (int)(screenPos.Y - Top * m_dpiScale);

            var callback = m_onCoordPicked;
            StopPickCoord();
            callback?.Invoke(x, y);
        }

        /// <summary>坐标拾取模式下的键盘事件处理</summary>
        private void OnPickKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                StopPickCoord();
            }
        }

#endregion

#region 截图框选

        /// <summary>在目标窗口上拖拽框选区域，完成后回调 (x, y, width, height) 客户区坐标；取消时 (0,0,0,0)。</summary>
        /// <summary>开始截图框选模式</summary>
        /// <param name="onComplete">框选完成时的回调函数，参数为 (x, y, width, height) 物理像素坐标；取消时为 (0,0,0,0)</param>
        public void StartScreenshotRegion(Action<int, int, int, int> onComplete)
        {
            m_onScreenshotRegionComplete = onComplete;
            m_isScreenshotRegion = true;

            HideAppWindow();
            SetOverlayReceivesInput(true);

            IsHitTestVisible = true;
            DrawBorder.Visibility = Visibility.Visible;
            CrosshairCanvas.Visibility = Visibility.Visible;
            CoordCanvas.Visibility = Visibility.Visible;
            CrosshairH.X1 = 0;
            CrosshairH.X2 = Width;
            CrosshairV.Y1 = 0;
            CrosshairV.Y2 = Height;

            ScreenshotCanvas.Visibility = Visibility.Visible;
            ScreenshotRect.Width = 0;
            ScreenshotRect.Height = 0;

            MouseLeftButtonDown += OnScreenshotMouseDown;
            MouseMove += OnScreenshotMouseMove;
            MouseLeftButtonUp += OnScreenshotMouseUp;
            KeyDown += OnScreenshotKeyDown;

            Topmost = true;
            Activate();
            Focus();
        }

        /// <summary>停止截图框选模式</summary>
        public void StopScreenshotRegion()
        {
            m_isScreenshotRegion = false;
            m_onScreenshotRegionComplete = null;

            ShowAppWindow();
            SetOverlayReceivesInput(false);

            IsHitTestVisible = false;
            DrawBorder.Visibility = Visibility.Collapsed;
            CrosshairCanvas.Visibility = Visibility.Collapsed;
            CoordCanvas.Visibility = Visibility.Collapsed;
            ScreenshotCanvas.Visibility = Visibility.Collapsed;

            MouseLeftButtonDown -= OnScreenshotMouseDown;
            MouseMove -= OnScreenshotMouseMove;
            MouseLeftButtonUp -= OnScreenshotMouseUp;
            KeyDown -= OnScreenshotKeyDown;
        }

        /// <summary>截图框选模式下的鼠标按下事件处理</summary>
        private void OnScreenshotMouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);
            m_screenshotStart = pos;
            Canvas.SetLeft(ScreenshotRect, pos.X);
            Canvas.SetTop(ScreenshotRect, pos.Y);
            ScreenshotRect.Width = 0;
            ScreenshotRect.Height = 0;
        }

        /// <summary>截图框选模式下的鼠标移动事件处理</summary>
        private void OnScreenshotMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var pos = e.GetPosition(this);
            var x = Math.Min(m_screenshotStart.X, pos.X);
            var y = Math.Min(m_screenshotStart.Y, pos.Y);
            var w = Math.Abs(pos.X - m_screenshotStart.X);
            var h = Math.Abs(pos.Y - m_screenshotStart.Y);

            Canvas.SetLeft(ScreenshotRect, x);
            Canvas.SetTop(ScreenshotRect, y);
            ScreenshotRect.Width = w;
            ScreenshotRect.Height = h;
        }

        /// <summary>截图框选模式下的鼠标释放事件处理</summary>
        private void OnScreenshotMouseUp(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);
            var x1 = (int)(Math.Min(m_screenshotStart.X, pos.X) * m_dpiScale);
            var y1 = (int)(Math.Min(m_screenshotStart.Y, pos.Y) * m_dpiScale);
            var x2 = (int)(Math.Max(m_screenshotStart.X, pos.X) * m_dpiScale);
            var y2 = (int)(Math.Max(m_screenshotStart.Y, pos.Y) * m_dpiScale);

            var w = x2 - x1;
            var h = y2 - y1;

            var callback = m_onScreenshotRegionComplete;
            StopScreenshotRegion();

            if (w >= 5 && h >= 5)
            {
                callback?.Invoke(x1, y1, w, h);
            }
            else
            {
                callback?.Invoke(0, 0, 0, 0);
            }
        }

        /// <summary>截图框选模式下的键盘事件处理</summary>
        private void OnScreenshotKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                var cb = m_onScreenshotRegionComplete;
                StopScreenshotRegion();
                cb?.Invoke(0, 0, 0, 0);
            }
        }

#endregion

#region Win32 API

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(nint hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(nint hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(nint hWnd, out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(ref RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClipCursor(IntPtr lpRect);

        [DllImport("user32.dll")]
        private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(nint hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(nint hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool EnableWindow(nint hWnd, bool bEnable);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(nint hWnd);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private static readonly nint HWND_TOPMOST = new(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X, Y;
        }

#endregion
    }
}
