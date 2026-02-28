#region

using GameImpact.Abstractions.Capture;
using GameImpact.Abstractions.Hotkey;
using GameImpact.Abstractions.Input;
using GameImpact.Abstractions.Recognition;
using GameImpact.Automation;
using GameImpact.Capture;
using GameImpact.Core.Services;
using GameImpact.Core.Windowing;
using GameImpact.Hotkey;
using GameImpact.Input;
using GameImpact.Recognition;
using Microsoft.Extensions.DependencyInjection;

#endregion

namespace GameImpact.Core
{
    /// <summary>服务集合扩展方法</summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>添加 GameImpact 核心服务</summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddGameImpact(this IServiceCollection services)
        {
            services.AddSingleton<IInputSimulator>(_ => InputFactory.CreateSendInput());
            services.AddSingleton<IHotkeyService, HotkeyService>();
            services.AddSingleton<IRecognitionService, RecognitionService>();
            services.AddSingleton<IWindowEnumerator, Win32WindowEnumerator>();
            services.AddSingleton<ITemplateService, TemplateService>();
            services.AddSingleton<ITemplateMatchService, TemplateMatchService>();
            services.AddSingleton<IDebugActionsService, DebugActionsService>();
            services.AddSingleton<ICapturePreviewProvider, CapturePreviewProvider>();
            services.AddSingleton<TaskEngine>();
            services.AddSingleton<GameContext>();
            return services;
        }

        /// <summary>添加屏幕捕获服务</summary>
        /// <param name="services">服务集合</param>
        /// <param name="enableHdr">是否启用HDR</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddScreenCapture(this IServiceCollection services, bool enableHdr = false)
        {
            services.AddSingleton<IScreenCapture>(_ => CaptureFactory.Create(enableHdr));
            return services;
        }
    }
}
