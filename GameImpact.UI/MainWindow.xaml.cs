using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GameImpact.UI.Services;
using GameImpact.UI.Views;

namespace GameImpact.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isDebugMode;
    private bool _isCapturingKey;
    private Key _capturedKey = Key.None;
    private ModifierKeys _capturedModifiers = ModifierKeys.None;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        
        ThemeService.Instance.ThemeChanged += OnThemeChanged;
        UpdateThemeIcon();
        StateChanged += OnStateChanged;
    }

    private void OnThemeChanged(AppTheme theme) => UpdateThemeIcon();

    private void UpdateThemeIcon()
    {
        ThemeIcon.Text = ThemeService.Instance.CurrentTheme == AppTheme.Dark ? "🌙" : "☀";
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            ToggleMaximize();
        else if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.Instance.ToggleTheme();
    }

    private void DebugPanel_Click(object sender, RoutedEventArgs e)
    {
        _isDebugMode = !_isDebugMode;
        DefaultView.Visibility = _isDebugMode ? Visibility.Collapsed : Visibility.Visible;
        DebugView.Visibility = _isDebugMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 设置面板
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearLog();
    }

    private void PickCoord_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StartPickCoord((x, y) =>
        {
            Dispatcher.Invoke(() =>
            {
                MouseX.Text = x.ToString();
                MouseY.Text = y.ToString();
                AppendInputLog($"拾取坐标: ({x}, {y})");
            });
        });
    }

    private void TestMouseClick_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MouseX.Text, out int x) && int.TryParse(MouseY.Text, out int y))
        {
            AppendInputLog($"鼠标点击: ({x}, {y})");
            _viewModel.TestMouseClick(x, y);
        }
        else
        {
            AppendInputLog("错误: 无效的坐标");
        }
    }

    private void TestMouseMove_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MouseX.Text, out int x) && int.TryParse(MouseY.Text, out int y))
        {
            AppendInputLog($"鼠标移动: ({x}, {y})");
            _viewModel.TestMouseMove(x, y);
        }
        else
        {
            AppendInputLog("错误: 无效的坐标");
        }
    }

    private void KeyCapture_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isCapturingKey = true;
        _capturedKey = Key.None;
        _capturedModifiers = ModifierKeys.None;
        KeyCaptureText.Text = "请按下按键...";
        KeyCaptureText.Foreground = (System.Windows.Media.Brush)FindResource("AppAccent");
        KeyCaptureBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("AppAccent");
        KeyCaptureBorder.Focus();
        e.Handled = true;
    }

    private void KeyCapture_KeyDown(object sender, KeyEventArgs e)
    {
        if (!_isCapturingKey) return;

        // 获取实际按键（忽略系统键如 Alt 时的 Key.System）
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // 如果只按了修饰键，先记录修饰键，等待实际按键
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LWin || key == Key.RWin)
        {
            _capturedModifiers = Keyboard.Modifiers;
            KeyCaptureText.Text = FormatKeyDisplay(ModifierKeys.None, key) + " + ...";
            e.Handled = true;
            return;
        }

        // 捕获到实际按键
        _capturedModifiers = Keyboard.Modifiers;
        _capturedKey = key;
        _isCapturingKey = false;

        var displayText = FormatKeyDisplay(_capturedModifiers, _capturedKey);
        KeyCaptureText.Text = displayText;
        KeyCaptureText.Foreground = (System.Windows.Media.Brush)FindResource("AppText");
        KeyCaptureBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("AppBorderSubtle");

        AppendInputLog($"已捕获按键: {displayText}");
        e.Handled = true;
    }

    private void KeyCapture_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isCapturingKey)
        {
            _isCapturingKey = false;
            if (_capturedKey == Key.None)
            {
                KeyCaptureText.Text = "点击捕获按键";
                KeyCaptureText.Foreground = (System.Windows.Media.Brush)FindResource("AppTextMuted");
            }
            KeyCaptureBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("AppBorderSubtle");
        }
    }

    private static string FormatKeyDisplay(ModifierKeys modifiers, Key key)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        // 将 WPF Key 转换为友好的显示名称
        var keyName = key switch
        {
            Key.Back => "Backspace",
            Key.Return => "Enter",
            Key.Capital => "CapsLock",
            Key.Escape => "Esc",
            Key.Prior => "PageUp",
            Key.Next => "PageDown",
            Key.Snapshot => "PrintScreen",
            Key.Oem3 => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.Oem6 => "]",
            Key.Oem5 => "\\",
            Key.Oem1 => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.LeftCtrl or Key.RightCtrl => "Ctrl",
            Key.LeftShift or Key.RightShift => "Shift",
            Key.LeftAlt or Key.RightAlt => "Alt",
            Key.LWin or Key.RWin => "Win",
            _ => key.ToString()
        };
        parts.Add(keyName);
        return string.Join(" + ", parts);
    }

    private void TestKeyPress_Click(object sender, RoutedEventArgs e)
    {
        if (_capturedKey == Key.None)
        {
            AppendInputLog("错误: 请先捕获按键");
            return;
        }

        var displayText = FormatKeyDisplay(_capturedModifiers, _capturedKey);
        AppendInputLog($"按键发送: {displayText}");
        _viewModel.TestKeyPress(_capturedKey, _capturedModifiers);
    }

    private void TestOcrFind_Click(object sender, RoutedEventArgs e)
    {
        var searchText = OcrSearchText.Text?.Trim();
        if (string.IsNullOrEmpty(searchText))
        {
            AppendInputLog("错误: 请输入要查找的文本");
            return;
        }

        var result = _viewModel.FindText(searchText);
        if (result.HasValue)
        {
            MouseX.Text = result.Value.x.ToString();
            MouseY.Text = result.Value.y.ToString();
            AppendInputLog($"找到 '{searchText}' 坐标: ({result.Value.x}, {result.Value.y})");
        }
        else
        {
            AppendInputLog($"未找到 '{searchText}'");
        }
    }

    private void TestOcrFull_Click(object sender, RoutedEventArgs e)
    {
        var results = _viewModel.RecognizeFullScreen();
        if (results != null && results.Count > 0)
        {
            AppendInputLog($"识别到 {results.Count} 个文本区域:");
            foreach (var r in results.Take(10))
            {
                AppendInputLog($"  [{r.x},{r.y}] {r.text}");
            }
            if (results.Count > 10)
                AppendInputLog($"  ... 还有 {results.Count - 10} 个");
        }
        else
        {
            AppendInputLog("未识别到文字");
        }
    }

    private void AppendInputLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
        InputTestLog.Text = line + InputTestLog.Text;
        if (InputTestLog.Text.Length > 5000)
            InputTestLog.Text = InputTestLog.Text[..5000];
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Cleanup();
        ThemeService.Instance.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }
}
