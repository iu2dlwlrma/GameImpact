#region

using System.Diagnostics;
using System.Runtime.InteropServices;
using GameImpact.Input.Native;
using GameImpact.Utilities.Logging;

#endregion

namespace GameImpact.Input
{
    /// <summary>输入相关诊断，用于排查 SetCursorPos / SendInput 被拦截时可能的原因。</summary>
    public static class InputDiagnostics
    {
        /// <summary>将当前进程已加载的模块（DLL）列表写入日志。 用于排查鼠标/键盘被拦截：常见拦截者包括罗技/雷蛇驱动、游戏覆盖层、远程桌面、安全软件等。</summary>
        public static void LogLoadedModules()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                Log.Info("[InputDiagnostics] Process: {Name} (PID={Pid})", process.ProcessName, process.Id);

                var modules = new List<(string ModuleName, string FileName)>();
                foreach (ProcessModule mod in process.Modules)
                {
                    if (mod.ModuleName is { } name && mod.FileName is { } path)
                    {
                        modules.Add((name, path));
                    }
                }

                // 按模块名排序，便于阅读
                modules.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.ModuleName, b.ModuleName));

                foreach (var (name, path) in modules)
                {
                    // 只记录非系统路径的 DLL，系统目录的通常不是拦截者
                    var isLikelySystem = path.StartsWith(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase)
                            || path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)
                            || path.Contains("Microsoft.NET", StringComparison.OrdinalIgnoreCase);
                    if (!isLikelySystem)
                    {
                        Log.Info("[InputDiagnostics] Loaded: {Module} -> {Path}", name, path);
                    }
                }

                // 常见“嫌疑”关键字，单独提醒（排除误报如 DependencyInjection）
                var suspects = modules.Where(m =>
                {
                    var n = m.ModuleName;
                    return n.Contains("hook", StringComparison.OrdinalIgnoreCase)
                            || (n.Contains("inject", StringComparison.OrdinalIgnoreCase) && !n.Contains("DependencyInjection", StringComparison.OrdinalIgnoreCase))
                            || n.Contains("overlay", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("logitech", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("razer", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("sunlogin", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("todesk", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("Huorong", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("360", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("discord", StringComparison.OrdinalIgnoreCase)
                            || n.Contains("geforce", StringComparison.OrdinalIgnoreCase)
                            || (n.Contains("nvidia", StringComparison.OrdinalIgnoreCase) && !n.Contains("nvcuda", StringComparison.OrdinalIgnoreCase));
                }).ToList();
                if (suspects.Count > 0)
                {
                    Log.Warn("[InputDiagnostics] Possible input-related modules (may hook mouse/kb): {Count}", suspects.Count);
                    foreach (var (name, path) in suspects)
                    {
                        Log.Warn("[InputDiagnostics]   -> {Module}: {Path}", name, path);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("[InputDiagnostics] LogLoadedModules failed {ex}", ex);
            }
        }

        /// <summary>测试 SetCursorPos 是否可用，并记录结果与 GetLastError。</summary>
        public static bool TestSetCursorPos(int x, int y)
        {
            var ok = NativeMethods.SetCursorPos(x, y);
            var err = Marshal.GetLastWin32Error();
            Log.Info("[InputDiagnostics] SetCursorPos({X}, {Y}) = {Result}, GetLastError = {Error} (0x{ErrorHex:X8})", x, y, ok, err, err);
            return ok;
        }
    }
}
