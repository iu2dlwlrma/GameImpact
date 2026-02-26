using System;
using System.Collections.ObjectModel;
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

        // 移除旧主题（在顶层和嵌套的 MergedDictionaries 中递归查找）
        RemoveThemeDictionaries(resources);

        // 添加新主题（使用 pack URI，确保从 GameImpact.UI 程序集加载资源）
        var themeName = theme == AppTheme.Dark ? "DarkTheme" : "LightTheme";
        var themeUri = new Uri($"pack://application:,,,/GameImpact.UI;component/Themes/{themeName}.xaml", UriKind.Absolute);
        resources.Add(new ResourceDictionary { Source = themeUri });

        // 同步 WPF UI 主题
        var wpfTheme = theme == AppTheme.Dark
            ? Wpf.Ui.Appearance.ApplicationTheme.Dark
            : Wpf.Ui.Appearance.ApplicationTheme.Light;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(wpfTheme);

        ThemeChanged?.Invoke(theme);
    }

    /// <summary>
    /// 递归移除包含 "Theme.xaml" 的资源字典
    /// </summary>
    private static void RemoveThemeDictionaries(Collection<ResourceDictionary> dictionaries)
    {
        for (int i = dictionaries.Count - 1; i >= 0; i--)
        {
            var dict = dictionaries[i];
            var source = dict.Source?.ToString() ?? "";
            if (source.Contains("Theme.xaml") && !source.Contains("ThemesDictionary"))
            {
                dictionaries.RemoveAt(i);
            }
            else if (dict.MergedDictionaries.Count > 0)
            {
                RemoveThemeDictionaries(dict.MergedDictionaries);
            }
        }
    }

    public void ToggleTheme() => SetTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
}
