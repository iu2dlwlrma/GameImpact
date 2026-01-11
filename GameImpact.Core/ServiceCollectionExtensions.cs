using GameImpact.Abstractions.Capture;
using GameImpact.Abstractions.Hotkey;
using GameImpact.Abstractions.Input;
using GameImpact.Abstractions.Recognition;
using GameImpact.Automation;
using GameImpact.Capture;
using GameImpact.Hotkey;
using GameImpact.Input;
using GameImpact.Recognition;
using Microsoft.Extensions.DependencyInjection;

namespace GameImpact.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGameImpact(this IServiceCollection services)
    {
        services.AddSingleton<IInputSimulator>(_ => InputFactory.CreateSendInput());
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IRecognitionService, RecognitionService>();
        services.AddSingleton<TaskEngine>();
        services.AddSingleton<GameContext>();
        return services;
    }

    public static IServiceCollection AddScreenCapture(this IServiceCollection services, bool enableHdr = false)
    {
        services.AddSingleton<IScreenCapture>(_ => CaptureFactory.Create(enableHdr));
        return services;
    }
}
