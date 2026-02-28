using System;
using System.Windows;
using System.Windows.Input;
using GameImpact.UI.Services;
using GameImpact.UI.Settings;
using GameImpact.UI.Views;

namespace GameImpact.UI;

/// <summary>
/// 主窗口
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainModel m_model;
    private DebugWindow? m_debugWindow;
    private SettingsWindow? m_settingsWindow;

    /// <summary>
    /// 设置窗口创建工厂，由 GameImpactApp 基类在启动时注入
    /// </summary>
    public Func<SettingsWindow>? SettingsWindowFactory { get; set; }

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

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="model">主视图模型</param>
    public MainWindow(MainModel model)
    {
        InitializeComponent();
        m_model = model;
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
        {
            ToggleMaximize();
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
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
        if (m_debugWindow is { IsLoaded: true })
        {
            m_debugWindow.Activate();
            return;
        }

        // 创建新的调试窗口
        m_debugWindow = new DebugWindow(m_model)
        {
            Owner = this
        };
        m_debugWindow.Closed += (_, _) => m_debugWindow = null;
        m_debugWindow.Show();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        // 如果设置窗口已存在且未关闭，则激活它
        if (m_settingsWindow is { IsLoaded: true })
        {
            m_settingsWindow.Activate();
            return;
        }

        if (SettingsWindowFactory == null)
        {
            return;
        }

        m_settingsWindow = SettingsWindowFactory();
        m_settingsWindow.Owner = this;
        m_settingsWindow.Closed += (_, _) => m_settingsWindow = null;
        m_settingsWindow.Show();
    }

    /// <summary>
    /// 窗口关闭事件处理
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        // 关闭主窗口时同时关闭子窗口
        m_debugWindow?.Close();
        m_settingsWindow?.Close();
        m_model.Cleanup();
        ThemeService.Instance.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }
}