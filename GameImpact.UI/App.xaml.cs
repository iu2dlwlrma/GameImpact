#region

using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using GameImpact.Core;
using GameImpact.UI.Services;
using GameImpact.UI.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using AppLog = GameImpact.Utilities.Logging.Log;

#endregion

namespace GameImpact.UI
{
    /// <summary>GameImpact 通用应用基类。 子项目继承此类即可复用：Serilog 日志、DI 容器、Host 生命周期、主题、Shell 窗口（标题栏/调试面板/状态栏）。</summary>
    public abstract class GameImpactApp : Application
    {
        private IHost? m_host;

        /// <summary>应用显示名称，用于窗口标题和日志前缀。子类覆写此属性自定义名称。</summary>
        public virtual string AppName => "GameImpact";

        /// <summary>DI 容器</summary>
        public IHost Host => m_host ?? throw new InvalidOperationException("Host 尚未初始化");

        /// <summary>是否在启动时请求管理员权限</summary>
        protected virtual bool RequestAdminAtStartup => false;

        /// <summary>请求管理员权限时的弹窗正文。子类可覆写以自定义说明。</summary>
        protected virtual string AdminRequestMessage => "本程序需要管理员权限以支持部分功能（如与游戏窗口通信）。\n\n是否现在提权并重启？";

        /// <summary>请求管理员权限时的弹窗标题。</summary>
        protected virtual string AdminRequestTitle => "需要管理员权限";

        /// <summary>子类覆写以注册自己的服务。 注意：GameImpact 核心服务（GameContext, Input, OCR 等）和 Shell 自身的服务已默认注册。</summary>
        protected virtual void ConfigureServices(IServiceCollection services)
        {
        }

        /// <summary>子类覆写以提供自己的业务内容视图，会被嵌入到 Shell 主窗口的内容区域。 返回 null 则使用 Shell 的默认视图（捕获状态面板）。</summary>
        protected virtual FrameworkElement? CreateContentView(IServiceProvider services)
        {
            return null;
        }

        /// <summary>子类覆写以提供项目设置页签列表，会被嵌入到设置窗口的导航栏中。 返回空列表则设置窗口中不显示项目设置页签。</summary>
        protected virtual IEnumerable<SettingsPage> CreateProjectSettingsPages(IServiceProvider services)
        {
            return Array.Empty<SettingsPage>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (RequestAdminAtStartup && !RunAsAdmin.IsRunningAsAdministrator())
            {
                var result = MessageBox.Show(AdminRequestMessage, AdminRequestTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes && RunAsAdmin.RestartElevated(e.Args))
                {
                    Shutdown();
                    return;
                }
            }

            // 初始化 Serilog
            Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .Enrich.FromLogContext()
                    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                    .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                    .WriteTo.File("logs/app-.log",
                            rollingInterval: RollingInterval.Day,
                            encoding: Encoding.UTF8,
                            outputTemplate:
                            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();

            // 构建 Host
            m_host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                    .UseSerilog()
                    .ConfigureServices((_, services) =>
                    {
                        // 注册核心服务
                        services.AddGameImpact();
                        // UI 层：Overlay 由 UI 提供，供 MainModel 注入
                        services.AddSingleton<IOverlayUiService>(_ => OverlayUiService.Instance);
                        // 注册应用设置服务
                        services.AddSingleton<ISettingsProvider<AppSettings>>(
                                new JsonSettingsProvider<AppSettings>("appsettings.json"));
                        // 注册 Shell 窗口和 MainModel
                        services.AddSingleton<MainWindow>();
                        services.AddSingleton<MainModel>();
                        // 让子类注册自己的服务
                        ConfigureServices(services);
                    })
                    .Build();

            // 初始化日志
            var loggerFactory = m_host.Services.GetRequiredService<ILoggerFactory>();
            AppLog.Initialize(loggerFactory);

            // 从设置中加载主题
            var appSettingsProvider = m_host.Services.GetRequiredService<ISettingsProvider<AppSettings>>();
            var appSettings = appSettingsProvider.Load();
            ThemeService.Instance.SetTheme(appSettings.Theme);

            AppLog.Info("{AppName} starting...", AppName);
            await m_host.StartAsync();

            // 创建并显示 Shell 主窗口
            var mainWindow = m_host.Services.GetRequiredService<MainWindow>();
            mainWindow.ShellTitle = AppName;

            // 获取子类提供的内容视图
            var contentView = CreateContentView(m_host.Services);
            if (contentView != null)
            {
                mainWindow.SetContentView(contentView);
            }

            // 注册设置窗口创建工厂
            mainWindow.SettingsWindowFactory = () =>
            {
                var pages = new List<SettingsPage>();

                // 构建应用设置页签（按分组自动拆分子页签）
                var settingsProvider = m_host.Services.GetRequiredService<ISettingsProvider<AppSettings>>();
                var appPage = SettingsPageBuilder.Build<AppSettings>(
                        settingsProvider,
                        "应用设置",
                        "📱",
                        0,
                        (settings, propertyName) =>
                        {
                            if (propertyName == nameof(AppSettings.Theme))
                            {
                                ThemeService.Instance.SetTheme(settings.Theme);
                            }
                        });
                pages.Add(appPage);

                // 获取子类提供的项目设置页签
                var projectPages = CreateProjectSettingsPages(m_host.Services);
                pages.AddRange(projectPages);

                return new SettingsWindow(pages);
            };

            mainWindow.Show();

            AppLog.Info("{AppName} started", AppName);
        }

        /// <summary>应用退出事件处理</summary>
        protected override async void OnExit(ExitEventArgs e)
        {
            AppLog.Info("{AppName} exiting...", AppName);
            if (m_host != null)
            {
                await m_host.StopAsync();
                m_host.Dispose();
            }

            await Log.CloseAndFlushAsync();
            base.OnExit(e);
        }
    }
}
