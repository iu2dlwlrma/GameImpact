#region

using System.Diagnostics;
using System.Security.Principal;

#endregion

namespace GameImpact.UI
{
    /// <summary>启动时检测并可选提权为管理员（用于鼠标/键盘模拟与游戏通信等）。</summary>
    public static class RunAsAdmin
    {
        /// <summary>当前进程是否已以管理员身份运行。</summary>
        public static bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>以管理员身份重新启动当前程序（会触发 UAC 弹窗）。</summary>
        /// <param name="args">命令行参数，会原样传给新进程。</param>
        /// <returns>是否已成功启动新进程；若为 true，调用方应退出当前进程。</returns>
        public static bool RestartElevated(string[]? args = null)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                    FileName = exe,
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = args is { Length: > 0 } ? string.Join(" ", args.Select(a => EscapeArg(a))) : ""
            };

            try
            {
                Process.Start(startInfo);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string EscapeArg(string arg)
        {
            if (arg.Contains(' ') || arg.Contains('"'))
            {
                return "\"" + arg.Replace("\"", "\\\"") + "\"";
            }
            return arg;
        }
    }
}
