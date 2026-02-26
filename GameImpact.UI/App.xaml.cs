using System;
using System.Text;
using System.Windows;
using GameImpact.Core;
using GameImpact.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using AppLog = GameImpact.Utilities.Logging.Log;

namespace GameImpact.UI;

public partial class App
{
    private readonly IHost _host;

    public App()
    {
        Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Debug(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File("logs/app-.log",
                        rollingInterval: RollingInterval.Day,
                        encoding: Encoding.UTF8,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

        _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices((_, services) =>
                {
                    services.AddGameImpact();
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<MainViewModel>();
                })
                .Build();

        var loggerFactory = _host.Services.GetRequiredService<ILoggerFactory>();
        AppLog.Initialize(loggerFactory);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 初始化主题
        ThemeService.Instance.SetTheme(AppTheme.Dark);

        AppLog.Info("GameImpact starting...");
        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        AppLog.Info("GameImpact started");
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        AppLog.Info("GameImpact exiting...");
        await _host.StopAsync();
        _host.Dispose();
        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }
}
