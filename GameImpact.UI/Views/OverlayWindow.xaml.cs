using System;
using System.Collections.Generic;
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
using OpenCvSharp;

namespace GameImpact.UI.Views;

public partial class OverlayWindow : System.Windows.Window
{
    private static OverlayWindow? _instance;
    public static OverlayWindow Instance => _instance ??= new OverlayWindow();

    private nint _targetHwnd;
    private nint _overlayHwnd;
    private readonly DispatcherTimer _positionTimer;
    private bool _isPickingCoord;
    private Action<int, int>? _onCoordPicked;
    private double _dpiScale = 1.0;

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int GWL_EXSTYLE = -20;

    // ── 叠加日志 ──────────────────────────────────────────────
    /// <summary>日志级别优先级，数值越大越严重</summary>
    private static readonly Dictionary<string, int> LevelPriority = new()
    {
        ["DBG"] = 0, ["INF"] = 1, ["WRN"] = 2, ["ERR"] = 3
    };

    private static readonly Dictionary<string, Brush> LevelColor = new()
    {
        ["DBG"] = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
        ["INF"] = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
        ["WRN"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
        ["ERR"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55)),
    };

    private const int MaxLogLines = 18;
    private string _logMinLevel = "INF";

    /// <summary>
    /// 显示/隐藏叠加日志面板，并设置最低显示级别（"DBG"/"INF"/"WRN"/"ERR"）。
    /// </summary>
    public void SetOverlayLog(bool visible, string minLevel = "INF")
    {
        _logMinLevel = LevelPriority.ContainsKey(minLevel) ? minLevel : "INF";

        Dispatcher.Invoke(() =>
        {
            LogOverlayPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            LogLevelBadge.Text = visible ? $"[{_logMinLevel}+]" : string.Empty;
            if (!visible) LogLines.Children.Clear();
        });
    }
    // ─────────────────────────────────────────────────────────

    private OverlayWindow()
    {
        InitializeComponent();

        _positionTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _positionTimer.Tick += UpdatePosition;

        Log.OnScreenLogMessage += OnScreenLogReceived;

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Detach();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _overlayHwnd = new WindowInteropHelper(this).Handle;
        
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
            _dpiScale = source.CompositionTarget.TransformToDevice.M11;
        
        var exStyle = GetWindowLong(_overlayHwnd, GWL_EXSTYLE);
        SetWindowLong(_overlayHwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    public void AttachTo(nint targetHwnd)
    {
        _targetHwnd = targetHwnd;
        
        var monitor = MonitorFromWindow(targetHwnd, 2);
        if (GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0)
            _dpiScale = dpiX / 96.0;
        
        UpdateWindowPosition();
        Show();
        _positionTimer.Start();
    }

    public void Detach()
    {
        if (_isPickingCoord) StopPickCoord();
        _positionTimer.Stop();
        _targetHwnd = 0;
        Hide();
        ClearInfo();
        ClearDrawings();
    }

    public void ForceClose()
    {
        Log.OnScreenLogMessage -= OnScreenLogReceived;
        _positionTimer.Stop();
        Closing -= OnClosing;
        Close();
        _instance = null;
    }

    // ── 叠加日志实现 ──────────────────────────────────────────

    /// <summary>从任意线程接收屏幕日志事件，过滤后投递到 UI 线程</summary>
    private void OnScreenLogReceived(string level, string message)
    {
        // 级别过滤
        if (!LevelPriority.TryGetValue(level, out var msgPriority)) return;
        if (!LevelPriority.TryGetValue(_logMinLevel, out var minPriority)) return;
        if (msgPriority < minPriority) return;

        // 面板不可见时不渲染（节省资源）
        if (LogOverlayPanel.Visibility != Visibility.Visible) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => AppendLogLine(level, message));
    }

    private void AppendLogLine(string level, string message)
    {
        var color = LevelColor.GetValueOrDefault(level, LevelColor["INF"]);

        var line = new TextBlock
        {
            FontFamily   = new FontFamily("Consolas"),
            FontSize     = 11,
            Foreground   = color,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin       = new Thickness(0, 1, 0, 0),
            Text         = $"[{level}] {message}"
        };

        LogLines.Children.Add(line);

        // 超出最大行数时从顶部移除旧行
        while (LogLines.Children.Count > MaxLogLines)
            LogLines.Children.RemoveAt(0);
    }

    // ─────────────────────────────────────────────────────────

    private void UpdatePosition(object? sender, EventArgs e)
    {
        if (_targetHwnd == 0) return;
        
        if (!IsWindow(_targetHwnd))
        {
            Detach();
            return;
        }
        
        UpdateWindowPosition();
        
        if (_isPickingCoord)
            UpdateCrosshair();
    }

    private void UpdateWindowPosition()
    {
        if (_targetHwnd == 0) return;
        
        if (GetClientRect(_targetHwnd, out var clientRect) && 
            ClientToScreen(_targetHwnd, out var point))
        {
            Left = point.X / _dpiScale;
            Top = point.Y / _dpiScale;
            Width = (clientRect.Right - clientRect.Left) / _dpiScale;
            Height = (clientRect.Bottom - clientRect.Top) / _dpiScale;
        }
    }

    #region 绘制功能

    /// <summary>
    /// 绘制点击标记
    /// </summary>
    public void DrawClickMarker(int x, int y, int duration = 2000)
    {
        Dispatcher.Invoke(() =>
        {
            double logX = x / _dpiScale;
            double logY = y / _dpiScale;

            // 十字标记
            var cross = new Canvas { Tag = "click" };
            var lineH = new Line
            {
                X1 = logX - 10, Y1 = logY, X2 = logX + 10, Y2 = logY,
                Stroke = Brushes.Red, StrokeThickness = 2
            };
            var lineV = new Line
            {
                X1 = logX, Y1 = logY - 10, X2 = logX, Y2 = logY + 10,
                Stroke = Brushes.Red, StrokeThickness = 2
            };
            var circle = new Ellipse
            {
                Width = 16, Height = 16,
                Stroke = Brushes.Red, StrokeThickness = 2
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

    /// <summary>
    /// 绘制 OCR 结果矩形和文字
    /// </summary>
    public void DrawOcrResult(OpenCvSharp.Rect roi, List<(OpenCvSharp.Rect box, string text)> results, int duration = 3000)
    {
        Dispatcher.Invoke(() =>
        {
            var container = new Canvas { Tag = "ocr" };

            // 绘制 ROI 区域
            double roiX = roi.X / _dpiScale;
            double roiY = roi.Y / _dpiScale;
            double roiW = roi.Width / _dpiScale;
            double roiH = roi.Height / _dpiScale;

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
                double bx = (roi.X + box.X) / _dpiScale;
                double by = (roi.Y + box.Y) / _dpiScale;
                double bw = box.Width / _dpiScale;
                double bh = box.Height / _dpiScale;

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

    /// <summary>
    /// 清除所有绘制
    /// </summary>
    public void ClearDrawings()
    {
        Dispatcher.Invoke(() => DrawCanvas.Children.Clear());
    }

    #endregion

    #region 信息显示

    public void ShowInfo(string key, string text, Brush? foreground = null)
    {
        Dispatcher.Invoke(() =>
        {
            var existing = InfoPanel.Children.OfType<Border>()
                .FirstOrDefault(b => b.Tag?.ToString() == key);
            
            if (existing != null)
            {
                if (existing.Child is TextBlock tb) tb.Text = text;
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

    public void HideInfo(string key)
    {
        Dispatcher.Invoke(() =>
        {
            var existing = InfoPanel.Children.OfType<Border>()
                .FirstOrDefault(b => b.Tag?.ToString() == key);
            if (existing != null)
                InfoPanel.Children.Remove(existing);
        });
    }

    public void ClearInfo()
    {
        Dispatcher.Invoke(() => InfoPanel.Children.Clear());
    }

    #endregion

    #region 坐标拾取

    public void StartPickCoord(Action<int, int> onPicked)
    {
        _onCoordPicked = onPicked;
        _isPickingCoord = true;
        
        BringTargetWindowToFront();
        
        var exStyle = GetWindowLong(_overlayHwnd, GWL_EXSTYLE);
        SetWindowLong(_overlayHwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
        
        IsHitTestVisible = true;
        PickBorder.Visibility = Visibility.Visible;
        CrosshairCanvas.Visibility = Visibility.Visible;
        CoordCanvas.Visibility = Visibility.Visible;
        
        CrosshairH.X1 = 0;
        CrosshairH.X2 = Width;
        CrosshairV.Y1 = 0;
        CrosshairV.Y2 = Height;
        
        ClipCursorToWindow();
        
        MouseMove += OnPickMouseMove;
        MouseLeftButtonDown += OnPickMouseClick;
        KeyDown += OnPickKeyDown;
        
        Topmost = true;
        Activate();
        Focus();
    }

    public void StopPickCoord()
    {
        _isPickingCoord = false;
        _onCoordPicked = null;
        
        var exStyle = GetWindowLong(_overlayHwnd, GWL_EXSTYLE);
        SetWindowLong(_overlayHwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
        
        IsHitTestVisible = false;
        PickBorder.Visibility = Visibility.Collapsed;
        CrosshairCanvas.Visibility = Visibility.Collapsed;
        CoordCanvas.Visibility = Visibility.Collapsed;
        
        ClipCursor(IntPtr.Zero);
        
        MouseMove -= OnPickMouseMove;
        MouseLeftButtonDown -= OnPickMouseClick;
        KeyDown -= OnPickKeyDown;
    }

    private void BringTargetWindowToFront()
    {
        if (_targetHwnd == 0) return;
        
        if (IsIconic(_targetHwnd))
            ShowWindow(_targetHwnd, 9);
        
        SetForegroundWindow(_targetHwnd);
    }

    private void ClipCursorToWindow()
    {
        var rect = new RECT
        {
            Left = (int)(Left * _dpiScale),
            Top = (int)(Top * _dpiScale),
            Right = (int)((Left + Width) * _dpiScale),
            Bottom = (int)((Top + Height) * _dpiScale)
        };
        ClipCursor(ref rect);
    }

    private void UpdateCrosshair()
    {
        GetCursorPos(out var screenPos);
        
        int physX = (int)(screenPos.X - Left * _dpiScale);
        int physY = (int)(screenPos.Y - Top * _dpiScale);
        
        double logX = screenPos.X / _dpiScale - Left;
        double logY = screenPos.Y / _dpiScale - Top;
        
        CrosshairH.Y1 = logY;
        CrosshairH.Y2 = logY;
        CrosshairH.X2 = Width;
        
        CrosshairV.X1 = logX;
        CrosshairV.X2 = logX;
        CrosshairV.Y2 = Height;
        
        CoordText.Text = $"X: {physX}  Y: {physY}";
        
        double panelX = logX + 20;
        double panelY = logY + 20;
        
        CoordPanel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var panelSize = CoordPanel.DesiredSize;
        
        if (panelX + panelSize.Width > Width - 10)
            panelX = logX - panelSize.Width - 15;
        if (panelY + panelSize.Height > Height - 10)
            panelY = logY - panelSize.Height - 15;
        
        Canvas.SetLeft(CoordPanel, Math.Max(5, panelX));
        Canvas.SetTop(CoordPanel, Math.Max(5, panelY));
    }

    private void OnPickMouseMove(object sender, MouseEventArgs e) { }

    private void OnPickMouseClick(object sender, MouseButtonEventArgs e)
    {
        GetCursorPos(out var screenPos);
        int x = (int)(screenPos.X - Left * _dpiScale);
        int y = (int)(screenPos.Y - Top * _dpiScale);
        
        var callback = _onCoordPicked;
        StopPickCoord();
        callback?.Invoke(x, y);
    }

    private void OnPickKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            StopPickCoord();
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    #endregion
}
