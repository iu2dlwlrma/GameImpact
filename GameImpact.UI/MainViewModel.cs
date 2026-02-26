using System;
using System.Collections.Generic;
using System.Diagnostics;
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

public partial class MainViewModel : ObservableObject
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    private readonly GameContext _context;
    private readonly DispatcherTimer _logTimer;
    private readonly Queue<string> _logQueue = new();

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

    private nint _hWnd;
    private string _windowTitle = "";
    private int _lastFrameCount;
    private readonly Stopwatch _fpsTimer = new();
    private WriteableBitmap? _writeableBitmap;
    private bool _isRendering;
    
    // Overlay 窗口
    public OverlayWindow Overlay => OverlayWindow.Instance;

    public MainViewModel(GameContext context)
    {
        _context = context;

        _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _logTimer.Tick += OnLogTick;

        // 监听屏幕日志（业务关键信息）
        Log.OnScreenLogMessage += OnScreenLogReceived;
        _logTimer.Start();

        Log.InfoScreen("GameImpact 已启动");
    }

    private void OnLogReceived(string level, string message)
    {
#if DEBUG
        lock (_logQueue)
        {
            _logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
            while (_logQueue.Count > 300) _logQueue.Dequeue();
        }
#endif
    }

    private void OnScreenLogReceived(string level, string message)
    {
        lock (_logQueue)
        {
            _logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
            while (_logQueue.Count > 300) _logQueue.Dequeue();
        }
    }

    private void OnLogTick(object? sender, EventArgs e)
    {
        lock (_logQueue)
        {
            if (_logQueue.Count > 0)
                LogText = string.Join("\n", _logQueue);
        }
    }

    partial void OnEnablePreviewChanged(bool value)
    {
        if (!value)
        {
            PreviewSource = null;
            _writeableBitmap = null;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_context.Capture?.IsCapturing != true || !_isRendering) return;

        try
        {
            if (!_context.Capture.TryGetFrameData(out var data, out var width, out var height, out var step))
                return;

            try
            {
                // 更新 FPS
                if (_fpsTimer.ElapsedMilliseconds >= 1000)
                {
                    var currentFrameCount = _context.Capture.FrameCount;
                    var framesDelta = currentFrameCount - _lastFrameCount;
                    var fps = framesDelta * 1000.0 / _fpsTimer.ElapsedMilliseconds;
                    StatusText = $"{fps:F1} FPS";
                    PreviewFps = $"{fps:F1}";
                    _lastFrameCount = currentFrameCount;
                    _fpsTimer.Restart();
                }

                PreviewResolution = $"{width} × {height}";

                // 预览渲染
                if (!EnablePreview) return;

                if (_writeableBitmap == null ||
                    _writeableBitmap.PixelWidth != width ||
                    _writeableBitmap.PixelHeight != height)
                {
                    _writeableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                    PreviewSource = _writeableBitmap;
                }

                _writeableBitmap.Lock();
                try
                {
                    var backBuffer = _writeableBitmap.BackBuffer;
                    var backBufferStride = _writeableBitmap.BackBufferStride;

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

                    _writeableBitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
                }
                finally
                {
                    _writeableBitmap.Unlock();
                }
            }
            finally
            {
                _context.Capture.ReleaseFrame();
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
            _hWnd = window.Handle;
            _windowTitle = window.Title;
            WindowDisplayText = $"{window.ProcessName}";
            StatusMessage = $"已选择: {window.ProcessName}";
            Log.InfoScreen("[UI] 选择窗口: {Process} ({Handle})", window.ProcessName, window.HandleText);
        }
    }

    [RelayCommand]
    private void ToggleCapture()
    {
        if (IsCapturing)
            StopCapture();
        else
            StartCapture();
    }

    private void StartCapture()
    {
        if (_hWnd == nint.Zero)
        {
            StatusMessage = "请先选择窗口";
            return;
        }

        try
        {
            if (_isRendering)
            {
                _isRendering = false;
                CompositionTarget.Rendering -= OnRendering;
            }

            _context.Initialize(_hWnd, useGpuHdrConversion: UseGpuHdrConversion);

            IsCapturing = true;
            IsIdle = false;
            CaptureButtonText = "停止";
            StatusMessage = "捕获中";
            Log.InfoScreen("[UI] 开始捕获 (GPU HDR: {UseGpu})", UseGpuHdrConversion ? "开启" : "关闭");

            // 启动 Overlay 窗口
            OverlayWindow.Instance.AttachTo(_hWnd);

            _lastFrameCount = 0;
            _fpsTimer.Restart();
            _isRendering = true;
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
        _isRendering = false;
        CompositionTarget.Rendering -= OnRendering;
        _context.Capture?.Stop();
        
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
        _writeableBitmap = null;
        Log.InfoScreen("[UI] 停止捕获");
    }

    public void ClearLog()
    {
        lock (_logQueue)
        {
            _logQueue.Clear();
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
        if (_context.WindowHandle != nint.Zero)
        {
            SetForegroundWindow(_context.WindowHandle);
            await Task.Delay(300); // 等待窗口切换完成
        }
    }

    public async void TestMouseClick(int x, int y)
    {
        try
        {
            await BringTargetToForeground();

            _context.Input.Mouse.ForegroundClickAt(x, y);
            Log.DebugScreen("[Input] 鼠标点击 ({X}, {Y})", x, y);
            
            // 在 Overlay 上绘制点击标记
            OverlayWindow.Instance.DrawClickMarker(x, y);
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

            _context.Input.Mouse.MoveTo(x, y);
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
                _context.Input.Keyboard.KeyPress(vk);
                Log.DebugScreen("[Input] 按键 {Key}", key);
            }
            else
            {
                _context.Input.Keyboard.TextEntry(key);
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
                    _context.Input.Keyboard.KeyDown(mk);

                _context.Input.Keyboard.KeyPress(vk);

                foreach (var mk in modifierKeys)
                    _context.Input.Keyboard.KeyUp(mk);

                var modStr = string.Join("+", modifierKeys.Select(k => k.ToString()));
                Log.DebugScreen("[Input] 组合键 {Mods}+{Key}", modStr, vk);
            }
            else
            {
                _context.Input.Keyboard.KeyPress(vk);
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
        if (!IsCapturing || _context.Capture == null)
        {
            Log.WarnScreen("[OCR] 请先启动捕获");
            return null;
        }

        try
        {
            var frame = _context.Capture.Capture();
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
                var results = _context.Ocr.Recognize(frame, roi);

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
        if (!IsCapturing || _context.Capture == null)
        {
            Log.WarnScreen("[OCR] 请先启动捕获");
            return null;
        }

        try
        {
            var frame = _context.Capture.Capture();
            if (frame == null)
            {
                Log.WarnScreen("[OCR] 无法获取帧");
                return null;
            }

            using (frame)
            {
                var results = _context.Ocr.Recognize(frame);

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
        if (!IsCapturing || _context.Capture == null)
        {
            Log.WarnScreen("[OCR] 请先启动捕获");
            return null;
        }

        try
        {
            var frame = _context.Capture.Capture();
            if (frame == null)
            {
                Log.WarnScreen("[OCR] 无法获取帧");
                return null;
            }

            using (frame)
            {
                var results = _context.Ocr.Recognize(frame);
                
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

    public void Cleanup()
    {
        OverlayWindow.Instance.ForceClose();
    }
}
