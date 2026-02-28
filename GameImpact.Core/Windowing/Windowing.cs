#region

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

#endregion

namespace GameImpact.Core.Windowing
{
    /// <summary>窗口信息模型（供 Core 和 UI 等模块共享）。</summary>
    public class WindowInfo
    {
        public nint Handle{ get; init; }
        public string Title{ get; init; } = string.Empty;
        public string ProcessName{ get; init; } = string.Empty;
        public int ProcessId{ get; init; }
        public string DisplayText => $"{ProcessName} - {Title}";
        public string HandleText => $"0x{Handle:X}";
    }

    /// <summary>窗口枚举抽象。</summary>
    public interface IWindowEnumerator
    {
        List<WindowInfo> GetAllWindows();
    }

    /// <summary>基于 Win32 API 的窗口枚举实现。</summary>
    public sealed class Win32WindowEnumerator : IWindowEnumerator
    {
        public List<WindowInfo> GetAllWindows()
        {
            var windows = new List<WindowInfo>();

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                var length = GetWindowTextLength(hWnd);
                if (length == 0)
                {
                    return true;
                }

                if (!GetWindowRect(hWnd, out var rect))
                {
                    return true;
                }
                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                if (width < 100 || height < 100)
                {
                    return true;
                }

                var sb = new StringBuilder(length + 1);
                _ = GetWindowText(hWnd, sb, sb.Capacity);
                var title = sb.ToString();

                GetWindowThreadProcessId(hWnd, out var processId);

                try
                {
                    var process = Process.GetProcessById((int)processId);
                    windows.Add(new WindowInfo
                    {
                            Handle = hWnd,
                            Title = title,
                            ProcessName = process.ProcessName,
                            ProcessId = (int)processId
                    });
                }
                catch
                {
                    // 进程可能已退出
                }

                return true;
            }, nint.Zero);

            return windows;
        }
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(nint hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    }
}
