#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using GameImpact.Core;
using GameImpact.Core.Services;
using GameImpact.Core.Windowing;
using GameImpact.UI.Models;
using GameImpact.UI.Services;
using GameImpact.UI.Settings;
using GameImpact.UI.Views;
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
        private bool m_isStartingGame;

        /// <summary>应用显示名称，用于窗口标题和日志前缀。子类覆写此属性自定义名称。</summary>
        public virtual string AppName => "GameImpact";

        public virtual string GameName => "GameImpact";

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
                    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                    .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
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
                        // 先注册应用设置服务（需要在 AddGameImpact 之前）
                        services.AddSingleton<ISettingsProvider<AppSettings>>(
                                new JsonSettingsProvider<AppSettings>("appsettings.json"));
                        // 注册匹配设置适配器
                        services.AddSingleton<IMatchSettings, MatchSettingsAdapter>();

                        // 注册核心服务
                        services.AddGameImpact();

                        // 重新注册 TemplateMatchService 以注入 IMatchSettings
                        services.Remove(services.FirstOrDefault(s => s.ServiceType == typeof(ITemplateMatchService)));
                        services.AddSingleton<ITemplateMatchService>(sp =>
                        {
                            var context = sp.GetRequiredService<GameContext>();
                            var templates = sp.GetRequiredService<ITemplateService>();
                            var matchSettings = sp.GetService<IMatchSettings>();
                            return new TemplateMatchService(context, templates, matchSettings);
                        });

                        // UI 层：Overlay 与右下角 Tips 由 UI 提供
                        services.AddSingleton<IOverlayUiService>(_ => OverlayUiService.Instance);
                        services.AddSingleton<IStatusTipsService, StatusTipsService>();
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
                var appPage = SettingsPageBuilder.Build(
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
            OnMainWindowShown(mainWindow);

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

#region MainWindow

        /// <summary>主窗口显示后调用。父类实现：启动时按游戏路径自动查找窗口；订阅「启动且未选窗口」时弹路径设置、启动游戏并查找窗口。子类覆写时请调用 base.OnMainWindowShown(mainWindow)。</summary>
        protected virtual void OnMainWindowShown(Window mainWindow)
        {
            if (mainWindow is not MainWindow shell || shell.DataContext is not MainModel model)
            {
                return;
            }

            var enumerator = Host.Services.GetRequiredService<IWindowEnumerator>();
            var appSettingsProvider = Host.Services.GetRequiredService<ISettingsProvider<AppSettings>>();

            if (!string.IsNullOrWhiteSpace(AppName) || !string.IsNullOrWhiteSpace(GameName))
            {
                if (model.SetProcess(enumerator, AppName, GameName))
                {
                    return;
                }
            }

            // 点击「启动」且未选窗口时：未设置路径则弹窗设置，否则启动游戏并查找窗口
            model.StartRequestedWhenNoWindow += (_, args) =>
            {
                // 防止快速点击启动多个进程
                if (m_isStartingGame)
                {
                    model.StatusMessage = "游戏正在启动中，请稍候...";
                    return;
                }

                var gamePath = GetGamePath();
                if (string.IsNullOrWhiteSpace(gamePath))
                {
                    var pathDialog = new GamePathSetupDialog(appSettingsProvider, shell);
                    if (pathDialog.ShowDialog() != true)
                    {
                        model.StatusMessage = "请设置游戏路径后再启动";
                        return;
                    }
                    gamePath = GetGamePath();
                }

                if (string.IsNullOrWhiteSpace(gamePath))
                {
                    return;
                }

                try
                {
                    m_isStartingGame = true;
                    model.StatusMessage = "正在启动游戏...";

                    var startInfo = new ProcessStartInfo
                    {
                            FileName = gamePath,
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(gamePath) ?? ""
                    };
                    var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        m_isStartingGame = false;
                        model.StatusMessage = "启动游戏失败：无法创建进程";
                        return;
                    }

                    // 在后台任务中等待应用真正启动并获取窗口信息
                    Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Yield();

                            // 等待进程真正启动，获取到有效的窗口句柄
                            const int maxWaitTime = 30000; // 30秒超时
                            const int checkInterval = 1000; // 每1s检查一次
                            var hWnd = nint.Zero;
                            var title = "";
                            var processName = "";
                            var elapsed = 0;

                            while (elapsed < maxWaitTime)
                            {
                                try
                                {
                                    // 刷新进程信息
                                    process.Refresh();
                                    hWnd = process.MainWindowHandle;
                                    processName = process.ProcessName ?? "";
                                    title = process.MainWindowTitle ?? "";
                                    AppLog.Info("Refresh Process [Title:{Title}] - [GameName:{GameName}]...", title, GameName);

                                    // 如果获取到有效的窗口句柄
                                    if (hWnd != nint.Zero && !string.IsNullOrWhiteSpace(title))
                                    {
                                        // 如果标题只是进程名，说明还没有真正的应用标题，继续等待
                                        var isProcessName = string.Equals(title, processName, StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(title, processName + ".exe", StringComparison.OrdinalIgnoreCase);
                                        if (!isProcessName)
                                        {
                                            // 如果指定了 GameName，必须等待标题包含 GameName 才认为匹配成功
                                            if (!string.IsNullOrWhiteSpace(GameName))
                                            {
                                                if (title.Contains(GameName, StringComparison.Ordinal))
                                                {
                                                    break;
                                                }
                                            }
                                            else
                                            {
                                                break;
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                    // 进程可能已退出
                                    if (process.HasExited)
                                    {
                                        break;
                                    }
                                }

                                await Task.Delay(checkInterval);
                                elapsed += checkInterval;
                            }

                            // 如果进程已退出，说明启动失败
                            if (process.HasExited)
                            {
                                await Current.Dispatcher.InvokeAsync(() =>
                                {
                                    m_isStartingGame = false;
                                    model.StatusMessage = "游戏进程已退出";
                                });
                                return;
                            }

                            // 如果超时仍未获取到窗口句柄
                            if (hWnd == nint.Zero)
                            {
                                await Current.Dispatcher.InvokeAsync(() =>
                                {
                                    m_isStartingGame = false;
                                    model.StatusMessage = "启动超时：无法获取游戏窗口";
                                });
                                return;
                            }

                            // 设置窗口信息
                            await Current.Dispatcher.InvokeAsync(() =>
                            {
                                m_isStartingGame = false;
                                args.SetWindow(hWnd, title, processName);
                            });
                        }
                        catch (Exception ex)
                        {
                            await Current.Dispatcher.InvokeAsync(() =>
                            {
                                m_isStartingGame = false;
                                model.StatusMessage = $"等待游戏启动时出错: {ex.Message}";
                            });
                        }
                    });
                }
                catch (Exception ex)
                {
                    m_isStartingGame = false;
                    model.StatusMessage = $"启动游戏失败: {ex.Message}";
                }
            };
        }

        /// <summary>从 AppSettings.GameRootPath 与子类 GetGameExecutFilePath 拼接得到完整游戏路径。</summary>
        protected string? GetGamePath()
        {
            var root = Host.Services.GetRequiredService<ISettingsProvider<AppSettings>>().Load().GameRootPath;
            var start = GetGameExecutFilePath();
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(start))
            {
                return null;
            }
            return Path.Combine(root, start);
        }

        /// <summary>子类覆写以提供相对于游戏根目录的启动路径</summary>
        protected virtual string? GetGameExecutFilePath()
        {
            return null;
        }

#endregion
    }
}
