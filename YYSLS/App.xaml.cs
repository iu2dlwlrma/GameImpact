#region

using GameImpact.UI;

#endregion

namespace YYSLS
{
    /// <summary>YYSLS 应用入口，继承 GameImpactApp 以复用 Shell 框架。</summary>
    public partial class App : GameImpactApp
    {
        /// <summary>应用名称，显示在标题栏和日志中</summary>
        public override string AppName => "yysls";
        public override string GameName => "燕云十六声";

        /// <summary>相对于游戏根目录的启动路径</summary>
        protected override string? GetGameExecutFilePath() => "yysls_medium\\Engine\\Binaries\\Win64rh\\yysls.exe";
    }
}
