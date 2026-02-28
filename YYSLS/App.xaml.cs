using System;
using System.Collections.Generic;
using System.Windows;
using GameImpact.UI;
using GameImpact.UI.Settings;
using Microsoft.Extensions.DependencyInjection;
using YYSLS.Settings;

namespace YYSLS;

/// <summary>
/// YYSLS 应用入口，继承 GameImpactApp 以复用 Shell 框架。
/// </summary>
public partial class App : GameImpactApp
{
    /// <summary>
    /// 应用名称，显示在标题栏和日志中
    /// </summary>
    public override string AppName => "YYSLS";

    /// <summary>
    /// 注册 YYSLS 自身的业务服务
    /// </summary>
    protected override void ConfigureServices(IServiceCollection services)
    {
        // 注册项目设置服务
        services.AddSingleton<ISettingsProvider<ProjectSettings>>(
            new JsonSettingsProvider<ProjectSettings>("projectsettings.json"));
    }

    /// <summary>
    /// 创建 YYSLS 的业务内容视图，嵌入到 Shell 主窗口的内容区域
    /// </summary>
    protected override FrameworkElement? CreateContentView(System.IServiceProvider services)
    {
        // 返回业务内容视图（UserControl），会嵌入到 Shell 的主内容区
        return new MainPage();
    }

    /// <summary>
    /// 创建 YYSLS 的项目设置页签列表，嵌入到设置窗口的导航栏中
    /// </summary>
    protected override IEnumerable<SettingsPage> CreateProjectSettingsPages(IServiceProvider services)
    {
        var settingsProvider = services.GetRequiredService<ISettingsProvider<ProjectSettings>>();

        var projectPage = SettingsPageBuilder.Build<ProjectSettings>(
            settingsProvider,
            title: "项目设置",
            icon: "📋",
            order: 100);

        return new[] { projectPage };
    }
}
