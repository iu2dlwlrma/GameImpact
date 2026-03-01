#region

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

#endregion

namespace GameImpact.Core.Windowing
{
    /// <summary>按进程名、路径等条件查找窗口的辅助方法。</summary>
    public static class WindowFinder
    {
        /// <summary>规范化路径便于比较：统一斜杠、去掉末尾分隔符、转小写；失败返回 null。</summary>
        private static string? NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            try
            {
                var p = Path.GetFullPath(path.Trim());
                p = p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return p.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>按进程名查找第一个匹配的窗口（不区分大小写，支持部分匹配）。</summary>
        public static WindowInfo? FindByProcessName(IWindowEnumerator enumerator, string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }
            var key = processName.Trim().ToLowerInvariant();
            return enumerator.GetAllWindows()
                    .FirstOrDefault(w => w.ProcessName.Trim().ToLowerInvariant().Contains(key));
        }

        /// <summary>按进程名 + 窗口标题查找窗口（同一进程可能有多窗口如 登录/燕云十六声/MpayAgeTipsForm，用标题锁定主窗口）。</summary>
        public static WindowInfo? FindByProcessNameAndProcessTitle(IWindowEnumerator enumerator, string processName, string processTitle)
        {
            if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(processTitle))
            {
                return null;
            }
            var nameKey = processName.Trim().ToLowerInvariant();
            var titleKey = processTitle.Trim();
            return enumerator.GetAllWindows()
                    .FirstOrDefault(w =>
                            w.ProcessName.Trim().ToLowerInvariant().Contains(nameKey)
                            && !string.IsNullOrWhiteSpace(w.Title)
                            && w.Title.Trim().Contains(titleKey, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>按进程可执行文件路径查找第一个匹配的窗口（路径规范化后不区分大小写比较；单条路径异常不影响其他窗口）。</summary>
        public static WindowInfo? FindByProcessPath(IWindowEnumerator enumerator, string expectedPath)
        {
            var expectedKey = NormalizePath(expectedPath);
            if (string.IsNullOrEmpty(expectedKey))
            {
                return null;
            }
            return enumerator.GetAllWindows().FirstOrDefault(w =>
            {
                var processKey = NormalizePath(w.ProcessPath);
                return !string.IsNullOrEmpty(processKey) && string.Equals(processKey, expectedKey, StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    /// <summary>窗口信息模型（供 Core 和 UI 等模块共享）。</summary>
    public class WindowInfo
    {
        public nint Handle{ get; init; }
        public string Title{ get; init; } = string.Empty;
        public string ProcessName{ get; init; } = string.Empty;
        /// <summary>进程主模块可执行文件完整路径。</summary>
        public string ProcessPath{ get; init; } = string.Empty;
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
                    var processPath = string.Empty;
                    try
                    {
                        processPath = process.MainModule?.FileName ?? string.Empty;
                    }
                    catch
                    {
                        // 无权限或进程已退出
                    }
                    windows.Add(new WindowInfo
                    {
                            Handle = hWnd,
                            Title = title,
                            ProcessName = process.ProcessName,
                            ProcessPath = processPath,
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
