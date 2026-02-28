#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameImpact.Abstractions.Input;
using GameImpact.Abstractions.Recognition;
using GameImpact.Core;
using GameImpact.UI.Views;
using GameImpact.Utilities.Logging;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;

#endregion

namespace GameImpact.UI
{
    /// <summary>主视图模型，管理窗口选择、屏幕捕获、OCR识别等核心功能</summary>
    public partial class MainModel : ObservableObject
    {
        private readonly GameContext m_context;
        private readonly Stopwatch m_fpsTimer = new();
        private readonly Queue<string> m_logQueue = new();
        private readonly DispatcherTimer m_logTimer;
        [ObservableProperty] private bool _autoScrollLog = true;
        [ObservableProperty] private string _captureButtonText = "启动";
        [ObservableProperty] private bool _enablePreview = true;
        [ObservableProperty] private bool _isCapturing;
        [ObservableProperty] private bool _isIdle = true;
        [ObservableProperty] private bool _isPickingCoord;
        [ObservableProperty] private string _logText = "";
        [ObservableProperty] private string _previewFps = "-";
        [ObservableProperty] private string _previewResolution = "-";

        // Debug 面板属性
        [ObservableProperty] private ImageSource? _previewSource;
        [ObservableProperty] private string _statusMessage = "就绪";
        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private bool _useGpuHdrConversion = true;

        [ObservableProperty] private string _windowDisplayText = "未选择窗口";

        private nint m_hWnd;
        private bool m_isRendering;
        private int m_lastFrameCount;
        private string m_windowTitle = "";
        private WriteableBitmap? m_writeableBitmap;

        /// <summary>构造函数</summary>
        /// <param name="context">游戏上下文</param>
        public MainModel(GameContext context)
        {
            m_context = context;

            m_logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            m_logTimer.Tick += OnLogTick;

            // 监听屏幕日志（业务关键信息）
            Log.OnScreenLogMessage += OnScreenLogReceived;
            m_logTimer.Start();
        }

        // Overlay 窗口
        public OverlayWindow Overlay => OverlayWindow.Instance;

        /// <summary>模板图片保存目录，位于项目目录下。开发环境查找包含 .csproj 的目录，打包后使用入口程序集所在目录。</summary>
        public static string TemplatesFolderPath
        {
            get
            {
                var projectDir = GetProjectDirectory();
                return Path.Combine(projectDir, "Templates");
            }
        }

        /// <summary>获取项目目录。优先查找包含 .csproj 的目录（开发环境），找不到则使用入口程序集所在目录（打包环境）。</summary>
        private static string GetProjectDirectory()
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly == null)
            {
                return AppContext.BaseDirectory;
            }

            var assemblyLocation = entryAssembly.Location;
            if (string.IsNullOrEmpty(assemblyLocation))
            {
                return AppContext.BaseDirectory;
            }

            var currentDir = Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrEmpty(currentDir))
            {
                return AppContext.BaseDirectory;
            }

            // 先尝试向上查找包含 .csproj 文件的目录（开发环境）
            var directory = new DirectoryInfo(currentDir);
            while (directory != null)
            {
                if (directory.GetFiles("*.csproj").Length > 0)
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }

            // 如果找不到 .csproj（打包环境），使用入口程序集所在目录
            return currentDir;
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(nint hWnd);

        /// <summary>接收日志消息（仅DEBUG模式）</summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        private void OnLogReceived(string level, string message)
        {
#if DEBUG
            lock (m_logQueue)
            {
                m_logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
                while (m_logQueue.Count > 300)
                {
                    m_logQueue.Dequeue();
                }
            }
#endif
        }

        /// <summary>接收屏幕日志消息</summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        private void OnScreenLogReceived(string level, string message)
        {
            lock (m_logQueue)
            {
                m_logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
                while (m_logQueue.Count > 300)
                {
                    m_logQueue.Dequeue();
                }
            }
        }

        /// <summary>日志定时器回调：更新日志文本显示</summary>
        private void OnLogTick(object? sender, EventArgs e)
        {
            lock (m_logQueue)
            {
                if (m_logQueue.Count > 0)
                {
                    LogText = string.Join("\n", m_logQueue);
                }
            }
        }

        /// <summary>预览开关变更时的处理</summary>
        /// <param name="value">是否启用预览</param>
        partial void OnEnablePreviewChanged(bool value)
        {
            if (!value)
            {
                PreviewSource = null;
                m_writeableBitmap = null;
            }
        }

        /// <summary>渲染回调：更新预览图像</summary>
        private void OnRendering(object? sender, EventArgs e)
        {
            if (m_context.Capture?.IsCapturing != true || !m_isRendering)
            {
                return;
            }

            try
            {
                if (!m_context.Capture.TryGetFrameData(out var data, out var width, out var height, out var step))
                {
                    return;
                }

                try
                {
                    // 更新 FPS
                    if (m_fpsTimer.ElapsedMilliseconds >= 1000)
                    {
                        var currentFrameCount = m_context.Capture.FrameCount;
                        var framesDelta = currentFrameCount - m_lastFrameCount;
                        var fps = framesDelta * 1000.0 / m_fpsTimer.ElapsedMilliseconds;
                        StatusText = $"{fps:F1} FPS";
                        PreviewFps = $"{fps:F1}";
                        m_lastFrameCount = currentFrameCount;
                        m_fpsTimer.Restart();
                    }

                    PreviewResolution = $"{width} × {height}";

                    // 预览渲染
                    if (!EnablePreview)
                    {
                        return;
                    }

                    if (m_writeableBitmap == null ||
                            m_writeableBitmap.PixelWidth != width ||
                            m_writeableBitmap.PixelHeight != height)
                    {
                        m_writeableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        PreviewSource = m_writeableBitmap;
                    }

                    m_writeableBitmap.Lock();
                    try
                    {
                        var backBuffer = m_writeableBitmap.BackBuffer;
                        var backBufferStride = m_writeableBitmap.BackBufferStride;

                        unsafe
                        {
                            var copyBytes = width * 4;
                            var src = (byte*)data;
                            var dst = (byte*)backBuffer;

                            if (backBufferStride == step)
                            {
                                Buffer.MemoryCopy(src, dst, (long)height * copyBytes, (long)height * copyBytes);
                            }
                            else
                            {
                                for (var y = 0; y < height; y++)
                                {
                                    Buffer.MemoryCopy(src, dst, copyBytes, copyBytes);
                                    src += step;
                                    dst += backBufferStride;
                                }
                            }
                        }

                        m_writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                    }
                    finally
                    {
                        m_writeableBitmap.Unlock();
                    }
                }
                finally
                {
                    m_context.Capture.ReleaseFrame();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("[Preview] Frame error: {Error}", ex.Message);
            }
        }

        [RelayCommand]
        private void SelectWindow()
        {
            var dialog = new WindowSelectDialog();
            
            // 设置 Owner 为主窗口，使对话框居中显示
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                dialog.Owner = mainWindow;
            }
            
            if (dialog.ShowDialog() == true && dialog.SelectedWindow != null)
            {
                var window = dialog.SelectedWindow;
                m_hWnd = window.Handle;
                m_windowTitle = window.Title;
                WindowDisplayText = $"{window.ProcessName}";
                StatusMessage = $"已选择: {window.ProcessName}";
                Log.InfoScreen("[UI] 选择窗口: {Process} ({Handle})", window.ProcessName, window.HandleText);
            }
        }

        [RelayCommand]
        private void ToggleCapture()
        {
            if (IsCapturing)
            {
                StopCapture();
            }
            else
            {
                StartCapture();
            }
        }

        private void StartCapture()
        {
            if (m_hWnd == nint.Zero)
            {
                StatusMessage = "请先选择窗口";
                return;
            }

            try
            {
                if (m_isRendering)
                {
                    m_isRendering = false;
                    CompositionTarget.Rendering -= OnRendering;
                }

                m_context.Initialize(m_hWnd, useGpuHdrConversion: UseGpuHdrConversion);

                IsCapturing = true;
                IsIdle = false;
                CaptureButtonText = "停止";
                StatusMessage = "捕获中";
                Log.InfoScreen("[UI] 开始捕获 (GPU HDR: {UseGpu})", UseGpuHdrConversion ? "开启" : "关闭");

                // 启动 Overlay 窗口
                OverlayWindow.Instance.AttachTo(m_hWnd);

                m_lastFrameCount = 0;
                m_fpsTimer.Restart();
                m_isRendering = true;
                CompositionTarget.Rendering += OnRendering;
            }
            catch (Exception ex)
            {
                StatusMessage = $"启动失败: {ex.Message}";
                Log.ErrorScreen(ex, "[UI] 启动捕获失败");
            }
        }

        private void StopCapture()
        {
            m_isRendering = false;
            CompositionTarget.Rendering -= OnRendering;
            m_context.Capture?.Stop();

            // 关闭 Overlay
            OverlayWindow.Instance.Detach();

            IsCapturing = false;
            IsIdle = true;
            CaptureButtonText = "启动";
            StatusText = "";
            StatusMessage = "已停止";
            PreviewSource = null;
            PreviewFps = "-";
            PreviewResolution = "-";
            m_writeableBitmap = null;
            Log.InfoScreen("[UI] 停止捕获");
        }

        public void ClearLog()
        {
            lock (m_logQueue)
            {
                m_logQueue.Clear();
                LogText = "";
            }
        }

        public void StartPickCoord(Action<int, int> onPicked)
        {
            if (!IsCapturing)
            {
                Log.WarnScreen("[拾取] 请先启动捕获");
                return;
            }

            IsPickingCoord = true;
            Log.InfoScreen("[拾取] 开始拾取坐标，点击目标位置或按 ESC 取消");

            OverlayWindow.Instance.StartPickCoord((x, y) =>
            {
                IsPickingCoord = false;
                Log.InfoScreen("[拾取] 坐标: ({X}, {Y})", x, y);
                onPicked(x, y);
            });
        }

        public void ShowOverlayInfo(string key, string text)
        {
            OverlayWindow.Instance.ShowInfo(key, text);
        }

        public void HideOverlayInfo(string key)
        {
            OverlayWindow.Instance.HideInfo(key);
        }

        /// <summary>将目标窗口切回前台。因为用户刚点击了 GameImpact 的 UI 按钮， 此时 GameImpact 是前台进程，SetForegroundWindow 必定成功。</summary>
        private async Task BringTargetToForeground()
        {
            if (m_context.WindowHandle != nint.Zero)
            {
                SetForegroundWindow(m_context.WindowHandle);
                await Task.Delay(300); // 等待窗口切换完成
            }
        }

        public async void TestMouseClick(int x, int y)
        {
            try
            {
                await BringTargetToForeground();

                var result = m_context.Input.Mouse.ForegroundClickAt(x, y);
                Log.DebugScreen("[Input] 鼠标点击 ({X}, {Y})", x, y);

                // 在 Overlay 上绘制点击标记
                OverlayWindow.Instance.DrawClickMarker(x, y, result);
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[Input] 点击失败");
            }
        }

        public async void TestMouseMove(int x, int y)
        {
            try
            {
                await BringTargetToForeground();

                m_context.Input.Mouse.MoveTo(x, y);
                Log.DebugScreen("[Input] 鼠标移动 ({X}, {Y})", x, y);
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[Input] 移动失败");
            }
        }

        public async void TestKeyPress(string key)
        {
            try
            {
                await BringTargetToForeground();

                if (Enum.TryParse<VirtualKey>(key, true, out var vk))
                {
                    m_context.Input.Keyboard.KeyPress(vk);
                    Log.DebugScreen("[Input] 按键 {Key}", key);
                }
                else
                {
                    m_context.Input.Keyboard.TextEntry(key);
                    Log.DebugScreen("[Input] 文本输入 {Key}", key);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[Input] 按键失败");
            }
        }

        /// <summary>发送按键（支持组合键），从 WPF Key + ModifierKeys 转换为 VirtualKey</summary>
        public async void TestKeyPress(Key wpfKey, ModifierKeys modifiers)
        {
            try
            {
                await BringTargetToForeground();

                var vk = WpfKeyToVirtualKey(wpfKey);
                if (vk == VirtualKey.None)
                {
                    Log.WarnScreen("[Input] 无法识别按键: {Key}", wpfKey);
                    return;
                }

                // 按下修饰键
                var modifierKeys = new List<VirtualKey>();
                if (modifiers.HasFlag(ModifierKeys.Control))
                {
                    modifierKeys.Add(VirtualKey.Control);
                }
                if (modifiers.HasFlag(ModifierKeys.Alt))
                {
                    modifierKeys.Add(VirtualKey.Menu);
                }
                if (modifiers.HasFlag(ModifierKeys.Shift))
                {
                    modifierKeys.Add(VirtualKey.Shift);
                }

                if (modifierKeys.Count > 0)
                {
                    // 组合键：按下修饰键 -> 按目标键 -> 释放修饰键
                    foreach (var mk in modifierKeys)
                    {
                        m_context.Input.Keyboard.KeyDown(mk);
                    }

                    m_context.Input.Keyboard.KeyPress(vk);

                    foreach (var mk in modifierKeys)
                    {
                        m_context.Input.Keyboard.KeyUp(mk);
                    }

                    var modStr = string.Join("+", modifierKeys.Select(k => k.ToString()));
                    Log.DebugScreen("[Input] 组合键 {Mods}+{Key}", modStr, vk);
                }
                else
                {
                    m_context.Input.Keyboard.KeyPress(vk);
                    Log.DebugScreen("[Input] 按键 {Key}", vk);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[Input] 按键失败");
            }
        }

        private static VirtualKey WpfKeyToVirtualKey(Key wpfKey)
        {
            // WPF Key 枚举值与 Win32 VK 码可通过 KeyInterop 转换
            var vkCode = KeyInterop.VirtualKeyFromKey(wpfKey);
            if (vkCode == 0)
            {
                return VirtualKey.None;
            }

            if (Enum.IsDefined(typeof(VirtualKey), (ushort)vkCode))
            {
                return (VirtualKey)(ushort)vkCode;
            }

            return VirtualKey.None;
        }

        public string? TestOcr(int x, int y, int width, int height)
        {
            if (!IsCapturing || m_context.Capture == null)
            {
                Log.WarnScreen("[OCR] 请先启动捕获");
                return null;
            }

            try
            {
                var frame = m_context.Capture.Capture();
                if (frame == null)
                {
                    Log.WarnScreen("[OCR] 无法获取帧");
                    return null;
                }

                using (frame)
                {
                    if (x < 0 || y < 0 || x + width > frame.Width || y + height > frame.Height)
                    {
                        Log.WarnScreen("[OCR] ROI 超出边界 (图像: {W}x{H})", frame.Width, frame.Height);
                        return null;
                    }

                    var roi = new Rect(x, y, width, height);
                    var results = m_context.Ocr.Recognize(frame, roi);

                    // 在 Overlay 上绘制结果
                    var drawResults = results.Select(r => (r.BoundingBox, r.Text)).ToList();
                    OverlayWindow.Instance.DrawOcrResult(roi, drawResults);

                    if (results.Count == 0)
                    {
                        Log.InfoScreen("[OCR] 未识别到文字");
                        return null;
                    }

                    var text = string.Join(" ", results.Select(r => r.Text));
                    var confidence = results.Average(r => r.Confidence);
                    Log.InfoScreen("[OCR] 识别结果: {Text} (置信度: {Confidence})", text, confidence.ToString("P0"));

                    return text;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[OCR] 识别失败");
                return null;
            }
        }

        /// <summary>在整个窗口中查找指定文本，返回中心坐标</summary>
        public (int x, int y)? FindText(string searchText)
        {
            if (!IsCapturing || m_context.Capture == null)
            {
                Log.WarnScreen("[OCR] 请先启动捕获");
                return null;
            }

            try
            {
                var frame = m_context.Capture.Capture();
                if (frame == null)
                {
                    Log.WarnScreen("[OCR] 无法获取帧");
                    return null;
                }

                using (frame)
                {
                    var results = m_context.Ocr.Recognize(frame);

                    // 查找匹配的文本
                    var match = results.FirstOrDefault(r =>
                            r.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        // 只绘制匹配的结果
                        var fullRoi = new Rect(0, 0, frame.Width, frame.Height);
                        OverlayWindow.Instance.DrawOcrResult(fullRoi, [(match.BoundingBox, match.Text)]);

                        // 返回文本框的中心坐标
                        var centerX = match.BoundingBox.X + match.BoundingBox.Width / 2;
                        var centerY = match.BoundingBox.Y + match.BoundingBox.Height / 2;

                        Log.InfoScreen("[OCR] 找到 '{Text}' 在 ({X}, {Y})", searchText, centerX, centerY);
                        return (centerX, centerY);
                    }

                    Log.InfoScreen("[OCR] 未找到 '{Text}'，共识别 {Count} 个文本", searchText, results.Count);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[OCR] 查找失败");
                return null;
            }
        }

        /// <summary>识别整个窗口的文字</summary>
        public List<(int x, int y, string text)>? RecognizeFullScreen()
        {
            if (!IsCapturing || m_context.Capture == null)
            {
                Log.WarnScreen("[OCR] 请先启动捕获");
                return null;
            }

            try
            {
                var frame = m_context.Capture.Capture();
                if (frame == null)
                {
                    Log.WarnScreen("[OCR] 无法获取帧");
                    return null;
                }

                using (frame)
                {
                    var results = m_context.Ocr.Recognize(frame);

                    // 在 Overlay 上绘制所有结果
                    var fullRoi = new Rect(0, 0, frame.Width, frame.Height);
                    var drawResults = results.Select(r => (r.BoundingBox, r.Text)).ToList();
                    OverlayWindow.Instance.DrawOcrResult(fullRoi, drawResults);

                    Log.InfoScreen("[OCR] 全屏识别完成，共 {Count} 个文本", results.Count);

                    return results.Select(r => (
                            r.BoundingBox.X + r.BoundingBox.Width / 2,
                            r.BoundingBox.Y + r.BoundingBox.Height / 2,
                            r.Text
                    )).ToList();
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[OCR] 全屏识别失败");
                return null;
            }
        }

        /// <summary>启动截图工具：在目标窗口上框选区域，截取后弹出命名对话框保存到模板文件夹。</summary>
        public void StartScreenshotTool(Action? onSaved = null)
        {
            if (!IsCapturing || m_context.Capture == null)
            {
                Log.WarnScreen("[截图] 请先启动捕获");
                return;
            }

            OverlayWindow.Instance.StartScreenshotRegion((x, y, w, h) =>
            {
                if (w <= 0 || h <= 0)
                {
                    Log.InfoScreen("[截图] 已取消");
                    return;
                }

                try
                {
                    var frame = m_context.Capture.Capture();
                    if (frame == null)
                    {
                        Log.WarnScreen("[截图] 无法获取帧");
                        return;
                    }

                    using (frame)
                    {
                        if (x + w > frame.Width || y + h > frame.Height)
                        {
                            Log.WarnScreen("[截图] 选区超出边界");
                            return;
                        }

                        var roi = new Rect(x, y, w, h);
                        // 克隆 Mat，因为需要在对话框中使用
                        var crop = new Mat(frame, roi).Clone();
                        
                        try
                        {
                            // 在 UI 线程上显示对话框（同步调用以确保 Mat 在使用期间不被释放）
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                ShowScreenshotNameDialog(crop, onSaved);
                            });
                        }
                        finally
                        {
                            // 对话框关闭后释放 Mat
                            crop.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.ErrorScreen(ex, "[截图] 保存失败");
                }
            });
        }

        /// <summary>显示截图命名对话框</summary>
        private void ShowScreenshotNameDialog(Mat screenshot, Action? onSaved)
        {
            try
            {
                var defaultFileName = $"template_{DateTime.Now:yyyyMMdd_HHmmss}";
                var dialog = new ScreenshotNameDialog(screenshot, defaultFileName);
                
                // 设置对话框的所有者窗口
                var mainWindow = Application.Current.MainWindow;
                if (mainWindow != null)
                {
                    dialog.Owner = mainWindow;
                }

                if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SavedFilePath))
                {
                    try
                    {
                        Directory.CreateDirectory(TemplatesFolderPath);
                        var path = Path.Combine(TemplatesFolderPath, dialog.SavedFilePath);
                        Cv2.ImWrite(path, screenshot);
                        Log.InfoScreen("[截图] 已保存: {Name}", dialog.SavedFilePath);
                        onSaved?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.ErrorScreen(ex, "[截图] 保存失败");
                    }
                }
                else
                {
                    Log.InfoScreen("[截图] 已取消");
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[截图] 对话框显示失败");
            }
        }

        /// <summary>获取模板文件夹中的模板文件名列表（按时间倒序）。</summary>
        public List<string> GetTemplateFileNames()
        {
            if (!Directory.Exists(TemplatesFolderPath))
            {
                return new List<string>();
            }
            return Directory.GetFiles(TemplatesFolderPath, "*.png")
                    .Select(Path.GetFileName)
                    .Where(f => f != null)
                    .Cast<string>()
                    .OrderByDescending(f => f)
                    .ToList();
        }

        /// <summary>用当前画面与指定模板匹配，返回是否找到及中心坐标、置信度。</summary>
        public (bool found, int centerX, int centerY, double confidence) MatchWithTemplate(string? fileName)
        {
            var result = MatchWithTemplateAndText(fileName, null);
            return (result.found, result.centerX, result.centerY, result.confidence);
        }

        /// <summary>用当前画面与指定模板匹配，并提取文字区域进行OCR识别。返回是否找到、中心坐标、置信度和识别的文字。</summary>
        /// <param name="fileName">模板文件名</param>
        /// <param name="textRegion">文字区域（相对于匹配位置的偏移和大小），null表示不识别文字</param>
        /// <returns>(是否找到, 中心X, 中心Y, 置信度, 识别的文字)</returns>
        public (bool found, int centerX, int centerY, double confidence, string? text) MatchWithTemplateAndText(
            string? fileName, 
            Rect? textRegion = null)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return (false, 0, 0, 0, null);
            }
            if (!IsCapturing || m_context.Capture == null)
            {
                Log.WarnScreen("[识别] 请先启动捕获");
                return (false, 0, 0, 0, null);
            }

            var path = Path.Combine(TemplatesFolderPath, fileName);
            if (!File.Exists(path))
            {
                Log.WarnScreen("[识别] 模板不存在: {File}", fileName);
                return (false, 0, 0, 0, null);
            }

            try
            {
                // 自动加载与模板同名的 ROI 配置（*.roi.json）
                Rect? matchRoi = null;
                Rect? textRoiFromConfig = null;
                try
                {
                    var baseName = Path.GetFileNameWithoutExtension(fileName);
                    var roiFilePath = Path.Combine(TemplatesFolderPath, $"{baseName}.roi.json");
                    if (File.Exists(roiFilePath))
                    {
                        var json = File.ReadAllText(roiFilePath);
                        var roiElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

                        if (roiElement.TryGetProperty("MatchRoi", out var matchElem) &&
                            matchElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            matchRoi = new Rect(
                                matchElem.GetProperty("X").GetInt32(),
                                matchElem.GetProperty("Y").GetInt32(),
                                matchElem.GetProperty("Width").GetInt32(),
                                matchElem.GetProperty("Height").GetInt32());
                        }

                        if (roiElement.TryGetProperty("TextRoi", out var textElem) &&
                            textElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            textRoiFromConfig = new Rect(
                                textElem.GetProperty("X").GetInt32(),
                                textElem.GetProperty("Y").GetInt32(),
                                textElem.GetProperty("Width").GetInt32(),
                                textElem.GetProperty("Height").GetInt32());
                        }
                    }
                }
                catch (Exception ex)
                {
                    // ROI 配置解析失败不影响匹配流程，只做调试日志
                    Log.DebugScreen("[识别] 读取 ROI 配置失败: {Error}", ex.Message);
                }

                // 如果调用方未显式传入文字区域，但配置中存在 TextRoi，则自动使用
                if (!textRegion.HasValue && textRoiFromConfig.HasValue)
                {
                    textRegion = textRoiFromConfig;
                }

                using var template = Cv2.ImRead(path);
                if (template.Empty())
                {
                    Log.WarnScreen("[识别] 无法读取模板: {File}", fileName);
                    return (false, 0, 0, 0, null);
                }

                var frame = m_context.Capture.Capture();
                if (frame == null)
                {
                    Log.WarnScreen("[识别] 无法获取当前帧");
                    return (false, 0, 0, 0, null);
                }

                using (frame)
                {
                    // 使用边缘检测匹配，忽略背景颜色和特效，只关注形状轮廓；
                    // 如存在 MatchRoi 配置，则只在模板的该区域内进行匹配
                    var matchOptions = new MatchOptions(
                        Threshold: 0.6, // 边缘检测匹配可能需要较低的阈值
                        TemplateRegionOfInterest: matchRoi,
                        UseEdgeMatch: true,
                        CannyThreshold1: 50,
                        CannyThreshold2: 150
                    );
                    var result = m_context.Recognition.MatchTemplate(frame, template, matchOptions);
                    if (result.Success)
                    {
                        var rect = new Rect(result.Location.X, result.Location.Y, result.Size.Width, result.Size.Height);
                        OverlayWindow.Instance.DrawOcrResult(rect, [(new Rect(0, 0, result.Size.Width, result.Size.Height), $"匹配 {result.Confidence:P0}")]);
                        Log.InfoScreen("[识别] 找到模板 '{File}' 中心=({X},{Y}) 置信度={Conf:P0}", fileName, result.Center.X, result.Center.Y, result.Confidence);
                        
                        // 如果指定了文字区域，提取并识别文字
                        string? recognizedText = null;
                        if (textRegion.HasValue)
                        {
                            try
                            {
                                // 计算文字区域在源图像中的实际位置
                                var textX = result.Location.X + textRegion.Value.X;
                                var textY = result.Location.Y + textRegion.Value.Y;
                                var textWidth = textRegion.Value.Width;
                                var textHeight = textRegion.Value.Height;
                                
                                // 检查边界
                                if (textX >= 0 && textY >= 0 && 
                                    textX + textWidth <= frame.Width && 
                                    textY + textHeight <= frame.Height)
                                {
                                    var textRoi = new Rect(textX, textY, textWidth, textHeight);
                                    var ocrResults = m_context.Ocr.Recognize(frame, textRoi);
                                    
                                    if (ocrResults.Count > 0)
                                    {
                                        recognizedText = string.Join("", ocrResults.Select(r => r.Text));
                                        var avgConfidence = ocrResults.Average(r => r.Confidence);
                                        
                                        // 在 Overlay 上绘制文字识别结果
                                        var drawResults = ocrResults.Select(r => (r.BoundingBox, r.Text)).ToList();
                                        OverlayWindow.Instance.DrawOcrResult(textRoi, drawResults);
                                        
                                        Log.InfoScreen("[识别] 提取文字: {Text} (置信度: {Conf:P0})", recognizedText, avgConfidence);
                                    }
                                    else
                                    {
                                        Log.DebugScreen("[识别] 文字区域未识别到文字");
                                    }
                                }
                                else
                                {
                                    Log.WarnScreen("[识别] 文字区域超出边界");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.WarnScreen("[识别] 文字识别失败: {Error}", ex.Message);
                            }
                        }
                        
                        return (true, result.Center.X, result.Center.Y, result.Confidence, recognizedText);
                    }

                    Log.InfoScreen("[识别] 未匹配到模板 '{File}'", fileName);
                    return (false, 0, 0, 0, null);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[识别] 模板匹配失败");
                return (false, 0, 0, 0, null);
            }
        }

        public void Cleanup()
        {
            OverlayWindow.Instance.ForceClose();
        }
    }
}
