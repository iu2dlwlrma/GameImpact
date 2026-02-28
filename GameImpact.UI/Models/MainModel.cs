using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameImpact.Abstractions.Recognition;
using GameImpact.Core;
using GameImpact.UI.Views;
using GameImpact.Utilities.Logging;
using OpenCvSharp;

namespace GameImpact.UI;

/// <summary>
/// 主视图模型，管理窗口选择、屏幕捕获、OCR识别等核心功能
/// </summary>
public partial class MainModel : ObservableObject
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    private readonly GameContext m_context;
    private readonly DispatcherTimer m_logTimer;
    private readonly Queue<string> m_logQueue = new();

    [ObservableProperty] private string _windowDisplayText = "未选择窗口";
    [ObservableProperty] private string _captureButtonText = "启动";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private bool _isIdle = true;
    [ObservableProperty] private bool _useGpuHdrConversion = true;
    [ObservableProperty] private bool _isPickingCoord;

    // Debug 面板属性
    [ObservableProperty] private ImageSource? _previewSource;
    [ObservableProperty] private string _previewResolution = "-";
    [ObservableProperty] private string _previewFps = "-";
    [ObservableProperty] private bool _enablePreview = true;
    [ObservableProperty] private bool _autoScrollLog = true;

    private nint m_hWnd;
    private string m_windowTitle = "";
    private int m_lastFrameCount;
    private readonly Stopwatch m_fpsTimer = new();
    private WriteableBitmap? m_writeableBitmap;
    private bool m_isRendering;

    // Overlay 窗口
    public OverlayWindow Overlay => OverlayWindow.Instance;

    /// <summary>
    /// 构造函数
    /// </summary>
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

    /// <summary>
    /// 接收日志消息（仅DEBUG模式）
    /// </summary>
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

    /// <summary>
    /// 接收屏幕日志消息
    /// </summary>
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

    /// <summary>
    /// 日志定时器回调：更新日志文本显示
    /// </summary>
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

    /// <summary>
    /// 预览开关变更时的处理
    /// </summary>
    /// <param name="value">是否启用预览</param>
    partial void OnEnablePreviewChanged(bool value)
    {
        if (!value)
        {
            PreviewSource = null;
            m_writeableBitmap = null;
        }
    }

    /// <summary>
    /// 渲染回调：更新预览图像
    /// </summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        if (m_context.Capture?.IsCapturing != true || !m_isRendering) return;

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
                        int copyBytes = width * 4;
                        byte* src = (byte*)data;
                        byte* dst = (byte*)backBuffer;

                        if (backBufferStride == step)
                        {
                            Buffer.MemoryCopy(src, dst, (long)height * copyBytes, (long)height * copyBytes);
                        }
                        else
                        {
                            for (int y = 0; y < height; y++)
                            {
                                Buffer.MemoryCopy(src, dst, copyBytes, copyBytes);
                                src += step;
                                dst += backBufferStride;
                            }
                        }
                    }

                    m_writeableBitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
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

    /// <summary>
    /// 将目标窗口切回前台。因为用户刚点击了 GameImpact 的 UI 按钮，
    /// 此时 GameImpact 是前台进程，SetForegroundWindow 必定成功。
    /// </summary>
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

            if (Enum.TryParse<GameImpact.Abstractions.Input.VirtualKey>(key, true, out var vk))
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

    /// <summary>
    /// 发送按键（支持组合键），从 WPF Key + ModifierKeys 转换为 VirtualKey
    /// </summary>
    public async void TestKeyPress(System.Windows.Input.Key wpfKey, System.Windows.Input.ModifierKeys modifiers)
    {
        try
        {
            await BringTargetToForeground();

            var vk = WpfKeyToVirtualKey(wpfKey);
            if (vk == GameImpact.Abstractions.Input.VirtualKey.None)
            {
                Log.WarnScreen("[Input] 无法识别按键: {Key}", wpfKey);
                return;
            }

            // 按下修饰键
            var modifierKeys = new List<GameImpact.Abstractions.Input.VirtualKey>();
            if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
                modifierKeys.Add(GameImpact.Abstractions.Input.VirtualKey.Control);
            if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt))
                modifierKeys.Add(GameImpact.Abstractions.Input.VirtualKey.Menu);
            if (modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
                modifierKeys.Add(GameImpact.Abstractions.Input.VirtualKey.Shift);

            if (modifierKeys.Count > 0)
            {
                // 组合键：按下修饰键 -> 按目标键 -> 释放修饰键
                foreach (var mk in modifierKeys)
                    m_context.Input.Keyboard.KeyDown(mk);

                m_context.Input.Keyboard.KeyPress(vk);

                foreach (var mk in modifierKeys)
                    m_context.Input.Keyboard.KeyUp(mk);

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

    private static GameImpact.Abstractions.Input.VirtualKey WpfKeyToVirtualKey(System.Windows.Input.Key wpfKey)
    {
        // WPF Key 枚举值与 Win32 VK 码可通过 KeyInterop 转换
        var vkCode = System.Windows.Input.KeyInterop.VirtualKeyFromKey(wpfKey);
        if (vkCode == 0) return GameImpact.Abstractions.Input.VirtualKey.None;

        if (Enum.IsDefined(typeof(GameImpact.Abstractions.Input.VirtualKey), (ushort)vkCode))
            return (GameImpact.Abstractions.Input.VirtualKey)(ushort)vkCode;

        return GameImpact.Abstractions.Input.VirtualKey.None;
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

    /// <summary>
    /// 在整个窗口中查找指定文本，返回中心坐标
    /// </summary>
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
                    int centerX = match.BoundingBox.X + match.BoundingBox.Width / 2;
                    int centerY = match.BoundingBox.Y + match.BoundingBox.Height / 2;

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

    /// <summary>
    /// 识别整个窗口的文字
    /// </summary>
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

    /// <summary>
    /// 模板图片保存目录，与 logs 同级。
    /// </summary>
    public static string TemplatesFolderPath => Path.Combine(AppContext.BaseDirectory, "templates");

    /// <summary>
    /// 启动截图工具：在目标窗口上框选区域，截取后保存到模板文件夹。
    /// </summary>
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
                    using var crop = new Mat(frame, roi);
                    Directory.CreateDirectory(TemplatesFolderPath);
                    var name = $"template_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    var path = Path.Combine(TemplatesFolderPath, name);
                    Cv2.ImWrite(path, crop);
                    Log.InfoScreen("[截图] 已保存: {Name}", name);
                    onSaved?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[截图] 保存失败");
            }
        });
    }

    /// <summary>
    /// 获取模板文件夹中的模板文件名列表（按时间倒序）。
    /// </summary>
    public List<string> GetTemplateFileNames()
    {
        if (!Directory.Exists(TemplatesFolderPath))
            return new List<string>();
        return Directory.GetFiles(TemplatesFolderPath, "*.png")
            .Select(Path.GetFileName)
            .Where(f => f != null)
            .Cast<string>()
            .OrderByDescending(f => f)
            .ToList();
    }

    /// <summary>
    /// 用当前画面与指定模板匹配，返回是否找到及中心坐标、置信度。
    /// </summary>
    public (bool found, int centerX, int centerY, double confidence) MatchWithTemplate(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return (false, 0, 0, 0);
        if (!IsCapturing || m_context.Capture == null)
        {
            Log.WarnScreen("[识别] 请先启动捕获");
            return (false, 0, 0, 0);
        }

        var path = Path.Combine(TemplatesFolderPath, fileName);
        if (!File.Exists(path))
        {
            Log.WarnScreen("[识别] 模板不存在: {File}", fileName);
            return (false, 0, 0, 0);
        }

        try
        {
            using var template = Cv2.ImRead(path);
            if (template.Empty())
            {
                Log.WarnScreen("[识别] 无法读取模板: {File}", fileName);
                return (false, 0, 0, 0);
            }

            var frame = m_context.Capture.Capture();
            if (frame == null)
            {
                Log.WarnScreen("[识别] 无法获取当前帧");
                return (false, 0, 0, 0);
            }

            using (frame)
            {
                var result = m_context.Recognition.MatchTemplate(frame, template);
                if (result.Success)
                {
                    var rect = new Rect(result.Location.X, result.Location.Y, result.Size.Width, result.Size.Height);
                    OverlayWindow.Instance.DrawOcrResult(rect, [(new OpenCvSharp.Rect(0, 0, result.Size.Width, result.Size.Height), $"匹配 {result.Confidence:P0}")]);
                    Log.InfoScreen("[识别] 找到模板 '{File}' 中心=({X},{Y}) 置信度={Conf:P0}", fileName, result.Center.X, result.Center.Y, result.Confidence);
                    return (true, result.Center.X, result.Center.Y, result.Confidence);
                }

                Log.InfoScreen("[识别] 未匹配到模板 '{File}'", fileName);
                return (false, 0, 0, 0);
            }
        }
        catch (Exception ex)
        {
            Log.ErrorScreen(ex, "[识别] 模板匹配失败");
            return (false, 0, 0, 0);
        }
    }

    public void Cleanup()
    {
        OverlayWindow.Instance.ForceClose();
    }
}
