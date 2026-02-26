using System.Configuration;
using System.Data;
using System.Windows;
using GameImpact.UI;

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
    protected override void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        // TODO: 在此注册 YYSLS 的业务服务
        // services.AddSingleton<YYSLSService>();
    }

    /// <summary>
    /// 创建 YYSLS 的业务内容视图，嵌入到 Shell 主窗口的内容区域
    /// </summary>
    protected override FrameworkElement? CreateContentView(System.IServiceProvider services)
    {
        // 返回业务内容视图（UserControl），会嵌入到 Shell 的主内容区
        return new MainPage();
    }
}
