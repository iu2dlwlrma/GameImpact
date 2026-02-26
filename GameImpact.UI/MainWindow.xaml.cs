using System;
using System.Windows;
using System.Windows.Input;
using GameImpact.UI.Services;
using GameImpact.UI.Views;

namespace GameImpact.UI;

public partial class MainWindow : Window
{
    private readonly MainModel model;
    private DebugWindow? _debugWindow;

    /// <summary>
    /// Shell 窗口标题，由 GameImpactApp 基类设置
    /// </summary>
    public string ShellTitle
    {
        get => TitleText.Text;
        set
        {
            TitleText.Text = value;
            Title = value;
        }
    }

    public MainWindow(MainModel model)
    {
        InitializeComponent();
        this.model = model;
        DataContext = model;
        
        ThemeService.Instance.ThemeChanged += OnThemeChanged;
        UpdateThemeIcon();
        StateChanged += OnStateChanged;
    }

    /// <summary>
    /// 设置子项目自定义的内容视图，替换默认视图区域
    /// </summary>
    public void SetContentView(FrameworkElement content)
    {
        CustomContentHost.Content = content;
        CustomContentHost.Visibility = Visibility.Visible;
        DefaultView.Visibility = Visibility.Collapsed;
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
        // 如果调试窗口已存在且未关闭，则激活它
        if (_debugWindow is { IsLoaded: true })
        {
            _debugWindow.Activate();
            return;
        }

        // 创建新的调试窗口
        _debugWindow = new DebugWindow(model)
        {
            Owner = this
        };
        _debugWindow.Closed += (_, _) => _debugWindow = null;
        _debugWindow.Show();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 设置面板
    }

    protected override void OnClosed(EventArgs e)
    {
        // 关闭主窗口时同时关闭调试窗口
        _debugWindow?.Close();
        model.Cleanup();
        ThemeService.Instance.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }
}