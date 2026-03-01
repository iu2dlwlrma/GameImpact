#region

using System;

#endregion

namespace GameImpact.UI.Events
{
    /// <summary>点击「启动」但尚未选择窗口时触发，供宿主（如 YYSLS）尝试自动查找进程并设置窗口。</summary>
    public class StartRequestedWhenNoWindowEventArgs : EventArgs
    {
        private readonly Action<nint, string, string> m_setWindow;

        internal StartRequestedWhenNoWindowEventArgs(Action<nint, string, string> setWindow)
        {
            m_setWindow = setWindow;
        }

        /// <summary>由宿主在找到或启动游戏后调用，将目标窗口设为当前选择并继续启动捕获。</summary>
        public void SetWindow(nint hWnd, string title, string processName)
        {
            m_setWindow(hWnd, title, processName);
        }
    }
}
