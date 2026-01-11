using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    private readonly GameContext _context;
    private readonly DispatcherTimer _logTimer;
    private readonly System.Collections.Generic.Queue<string> _logQueue = new();

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

#if DEBUG
        Log.OnLogMessage += OnLogReceived;
        _logTimer.Start();
#endif
        AppendLog("GameImpact 已启动");
    }

    private void OnLogReceived(string level, string message)
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
            AppendLog($"选择窗口: {window.ProcessName} ({window.HandleText})");
            Log.Info("[UI] Window selected: {Process} {Handle}", window.ProcessName, window.HandleText);
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

            Log.Info("[UI] Starting capture (GPU HDR: {UseGpu})", UseGpuHdrConversion);
            _context.Initialize(_hWnd, useGpuHdrConversion: UseGpuHdrConversion);

            IsCapturing = true;
            IsIdle = false;
            CaptureButtonText = "停止";
            StatusMessage = "捕获中";
            AppendLog($"开始捕获 (GPU HDR: {(UseGpuHdrConversion ? "开启" : "关闭")})");

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
            AppendLog($"错误: {ex.Message}");
            Log.Error(ex, "[UI] Start capture failed");
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
        AppendLog("停止捕获");
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
            AppendLog("[拾取] 请先启动捕获");
            return;
        }
        
        IsPickingCoord = true;
        AppendLog("[拾取] 开始拾取坐标，点击目标位置或按 ESC 取消");
        
        OverlayWindow.Instance.StartPickCoord((x, y) =>
        {
            IsPickingCoord = false;
            AppendLog($"[拾取] 坐标: ({x}, {y})");
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

    public void TestMouseClick(int x, int y)
    {
        try
        {
            _context.Input.Mouse.MoveTo(x, y).LeftClick();
            AppendLog($"[Input] 鼠标点击 ({x}, {y})");
            
            // 在 Overlay 上绘制点击标记
            OverlayWindow.Instance.DrawClickMarker(x, y);
        }
        catch (Exception ex)
        {
            AppendLog($"[Input] 点击失败: {ex.Message}");
        }
    }

    public void TestMouseMove(int x, int y)
    {
        try
        {
            _context.Input.Mouse.MoveTo(x, y);
            AppendLog($"[Input] 鼠标移动 ({x}, {y})");
        }
        catch (Exception ex)
        {
            AppendLog($"[Input] 移动失败: {ex.Message}");
        }
    }

    public void TestKeyPress(string key)
    {
        try
        {
            if (Enum.TryParse<GameImpact.Abstractions.Input.VirtualKey>(key, true, out var vk))
            {
                _context.Input.Keyboard.KeyPress(vk);
                AppendLog($"[Input] 按键 {key}");
            }
            else
            {
                _context.Input.Keyboard.TextEntry(key);
                AppendLog($"[Input] 文本输入 {key}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[Input] 按键失败: {ex.Message}");
        }
    }

    public string? TestOcr(int x, int y, int width, int height)
    {
        if (!IsCapturing || _context.Capture == null)
        {
            AppendLog("[OCR] 请先启动捕获");
            return null;
        }

        try
        {
            var frame = _context.Capture.Capture();
            if (frame == null)
            {
                AppendLog("[OCR] 无法获取帧");
                return null;
            }

            using (frame)
            {
                if (x < 0 || y < 0 || x + width > frame.Width || y + height > frame.Height)
                {
                    AppendLog($"[OCR] ROI 超出边界 (图像: {frame.Width}x{frame.Height})");
                    return null;
                }

                var roi = new Rect(x, y, width, height);
                var results = _context.Ocr.Recognize(frame, roi);

                // 在 Overlay 上绘制结果
                var drawResults = results.Select(r => (r.BoundingBox, r.Text)).ToList();
                OverlayWindow.Instance.DrawOcrResult(roi, drawResults);

                if (results.Count == 0)
                {
                    AppendLog($"[OCR] 未识别到文字");
                    return null;
                }

                var text = string.Join(" ", results.Select(r => r.Text));
                var confidence = results.Average(r => r.Confidence);
                AppendLog($"[OCR] 识别结果: {text} (置信度: {confidence:P0})");
                
                return text;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[OCR] 识别失败: {ex.Message}");
            Log.Error(ex, "[OCR] Recognition failed");
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
            AppendLog("[OCR] 请先启动捕获");
            return null;
        }

        try
        {
            var frame = _context.Capture.Capture();
            if (frame == null)
            {
                AppendLog("[OCR] 无法获取帧");
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
                    
                    AppendLog($"[OCR] 找到 '{searchText}' 在 ({centerX}, {centerY})");
                    return (centerX, centerY);
                }
                
                AppendLog($"[OCR] 未找到 '{searchText}'，共识别 {results.Count} 个文本");
                return null;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[OCR] 查找失败: {ex.Message}");
            Log.Error(ex, "[OCR] Find text failed");
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
            AppendLog("[OCR] 请先启动捕获");
            return null;
        }

        try
        {
            var frame = _context.Capture.Capture();
            if (frame == null)
            {
                AppendLog("[OCR] 无法获取帧");
                return null;
            }

            using (frame)
            {
                var results = _context.Ocr.Recognize(frame);
                
                // 在 Overlay 上绘制所有结果
                var fullRoi = new Rect(0, 0, frame.Width, frame.Height);
                var drawResults = results.Select(r => (r.BoundingBox, r.Text)).ToList();
                OverlayWindow.Instance.DrawOcrResult(fullRoi, drawResults);

                AppendLog($"[OCR] 全屏识别完成，共 {results.Count} 个文本");
                
                return results.Select(r => (
                    r.BoundingBox.X + r.BoundingBox.Width / 2,
                    r.BoundingBox.Y + r.BoundingBox.Height / 2,
                    r.Text
                )).ToList();
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[OCR] 全屏识别失败: {ex.Message}");
            Log.Error(ex, "[OCR] Full screen OCR failed");
            return null;
        }
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_logQueue)
        {
            _logQueue.Enqueue(line);
        }
    }
    
    public void Cleanup()
    {
        OverlayWindow.Instance.ForceClose();
    }
}
