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

    private void TestKeyPress_Click(object sender, RoutedEventArgs e)
    {
        var key = KeyInput.Text;
        if (!string.IsNullOrEmpty(key))
        {
            AppendInputLog($"按键发送: {key}");
            _viewModel.TestKeyPress(key);
        }
        else
        {
            AppendInputLog("错误: 请输入按键");
        }
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
