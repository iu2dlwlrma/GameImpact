using System;
using System.Windows;

namespace GameImpact.UI.Services;

public enum AppTheme { Dark, Light }

public class ThemeService
{
    private static ThemeService? _instance;
    public static ThemeService Instance => _instance ??= new ThemeService();

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
    public event Action<AppTheme>? ThemeChanged;

    public void SetTheme(AppTheme theme)
    {
        if (CurrentTheme == theme) return;
        CurrentTheme = theme;

        var app = Application.Current;
        var resources = app.Resources.MergedDictionaries;

        // 移除旧主题
        for (int i = resources.Count - 1; i >= 0; i--)
        {
            var source = resources[i].Source?.ToString() ?? "";
            if (source.Contains("Theme.xaml"))
                resources.RemoveAt(i);
        }

        // 添加新主题
        var themeUri = theme == AppTheme.Dark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);
        resources.Add(new ResourceDictionary { Source = themeUri });

        // 同步 WPF UI 主题
        var wpfTheme = theme == AppTheme.Dark
            ? Wpf.Ui.Appearance.ApplicationTheme.Dark
            : Wpf.Ui.Appearance.ApplicationTheme.Light;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(wpfTheme);

        ThemeChanged?.Invoke(theme);
    }

    public void ToggleTheme() => SetTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
}
