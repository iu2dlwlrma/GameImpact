using GameImpact.Abstractions.Capture;
using GameImpact.Utilities.Logging;
using OpenCvSharp;
using SharpDX;
using SharpDX.Direct3D11;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace GameImpact.Capture;

/// <summary>
/// 基于 Windows.Graphics.Capture API 的屏幕捕获实现，支持 HDR 和三缓冲
/// </summary>
public class GraphicsCapture : IScreenCapture
{
    public bool IsCapturing { get; private set; }
    public int FrameCount => _frameCount;

    private readonly bool _enableHdr;
    private readonly bool _useGpuHdrConversion;
    private nint _hWnd;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureItem? _captureItem;
    private GraphicsCaptureSession? _session;
    private IDirect3DDevice? _d3dDevice;
    private Device? _sharpDxDevice;
    private DirectXPixelFormat _pixelFormat;
    private GpuHdrConverter? _hdrConverter;
    
    private Mat?[] _buffers = new Mat?[3];
    private volatile int _writeIndex;
    private volatile int _readyIndex = -1;
    private volatile int _readIndex = -1;
    
    private Texture2D? _stagingTexture;
    private int _surfaceWidth, _surfaceHeight;
    private ResourceRegion? _region;
    private volatile int _frameCount;
    private volatile bool _isProcessing;

    /// <summary>
    /// 创建 GraphicsCapture 实例
    /// </summary>
    public GraphicsCapture(bool enableHdr = false, bool useGpuHdrConversion = false)
    {
        _enableHdr = enableHdr;
        _useGpuHdrConversion = useGpuHdrConversion;
    }

    /// <summary>
    /// 开始捕获指定窗口
    /// </summary>
    public void Start(nint windowHandle, CaptureOptions? options = null)
    {
        _hWnd = windowHandle;
        _region = GetGameScreenRegion(windowHandle);
        IsCapturing = true;
        _frameCount = 0;

        _captureItem = CaptureHelper.CreateItemForWindow(_hWnd);
        if (_captureItem == null)
            throw new InvalidOperationException("Failed to create capture item");

        _surfaceWidth = _captureItem.Size.Width;
        _surfaceHeight = _captureItem.Size.Height;
        _d3dDevice = Direct3D11Helper.CreateDevice();

        _pixelFormat = _enableHdr 
            ? DirectXPixelFormat.R16G16B16A16Float 
            : DirectXPixelFormat.B8G8R8A8UIntNormalized;
        
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(_d3dDevice, _pixelFormat, 3, _captureItem.Size);
        _captureItem.Closed += (_, _) => Stop();
        _framePool.FrameArrived += OnFrameArrived;

        _session = _framePool.CreateCaptureSession(_captureItem);
        
        if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
            _session.IsCursorCaptureEnabled = false;
        
        if (ApiInformation.IsWriteablePropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
            _session.IsBorderRequired = false;

        _session.StartCapture();
        Log.Info("[GraphicsCapture] Started (HDR: {Hdr}, GPU: {Gpu})", _enableHdr, _useGpuHdrConversion);
    }

    /// <summary>
    /// 获取最新帧的克隆副本（BGRA32 格式）
    /// </summary>
    public Mat? Capture()
    {
        int ready = _readyIndex;
        if (ready < 0) return null;
        
        _readIndex = ready;
        var mat = _buffers[ready];
        if (mat == null || mat.IsDisposed) return null;
        
        return mat.Clone();
    }

    /// <summary>
    /// 零拷贝访问：获取 BGRA32 格式帧数据指针
    /// </summary>
    public bool TryGetFrameData(out nint data, out int width, out int height, out int step)
    {
        int ready = _readyIndex;
        if (ready < 0 || _buffers[ready] == null)
        {
            data = 0; width = height = step = 0;
            return false;
        }
        
        _readIndex = ready;
        var mat = _buffers[ready]!;
        data = mat.Data;
        width = mat.Width;
        height = mat.Height;
        step = (int)mat.Step();
        return true;
    }
    
    /// <summary>
    /// 释放当前读取的帧
    /// </summary>
    public void ReleaseFrame() => _readIndex = -1;

    /// <summary>
    /// 停止捕获并释放资源
    /// </summary>
    public void Stop()
    {
        if (!IsCapturing) return;
        IsCapturing = false;
        
        SpinWait.SpinUntil(() => !_isProcessing, 100);
        
        try { _session?.Dispose(); } catch { }
        _session = null;
        
        if (_framePool != null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
            try { _framePool.Dispose(); } catch { }
            _framePool = null;
        }
        
        _captureItem = null;
        try { _stagingTexture?.Dispose(); } catch { }
        _stagingTexture = null;
        try { _hdrConverter?.Dispose(); } catch { }
        _hdrConverter = null;
        _sharpDxDevice = null;
        try { _d3dDevice?.Dispose(); } catch { }
        _d3dDevice = null;
        
        for (int i = 0; i < _buffers.Length; i++)
        {
            try { _buffers[i]?.Dispose(); } catch { }
            _buffers[i] = null;
        }
        _readyIndex = -1;
        _readIndex = -1;
        _writeIndex = 0;
        _hWnd = 0;
        
        Log.Info("[GraphicsCapture] Stopped (frames: {Count})", _frameCount);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (!IsCapturing || _hWnd == 0) return;
        if (_isProcessing) return;
        _isProcessing = true;
        
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null || !IsCapturing) return;

            _sharpDxDevice ??= Direct3D11Helper.SharedDevice;
            if (_sharpDxDevice == null || _sharpDxDevice.IsDisposed)
            {
                Log.Error("[GraphicsCapture] SharedDevice unavailable");
                IsCapturing = false;
                return;
            }

            if (_sharpDxDevice.DeviceRemovedReason.Code != 0)
            {
                Log.Error("[GraphicsCapture] GPU device removed: 0x{Code:X}", _sharpDxDevice.DeviceRemovedReason.Code);
                IsCapturing = false;
                return;
            }

            var captureSize = _captureItem!.Size;
            if (captureSize.Width != _surfaceWidth || captureSize.Height != _surfaceHeight)
            {
                _framePool!.Recreate(_d3dDevice, _pixelFormat, 3, captureSize);
                _stagingTexture?.Dispose();
                _stagingTexture = null;
                _surfaceWidth = captureSize.Width;
                _surfaceHeight = captureSize.Height;
                _region = GetGameScreenRegion(_hWnd);
                return;
            }

            using var surfaceTexture = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
            
            Texture2D textureToRead = surfaceTexture;
            if (_enableHdr && _useGpuHdrConversion)
            {
                _hdrConverter ??= new GpuHdrConverter(_sharpDxDevice);
                var converted = _hdrConverter.Convert(surfaceTexture);
                if (converted != null)
                    textureToRead = converted;
            }

            int targetW, targetH;
            if (_region != null)
            {
                targetW = _region.Value.Right - _region.Value.Left;
                targetH = _region.Value.Bottom - _region.Value.Top;
            }
            else
            {
                targetW = textureToRead.Description.Width;
                targetH = textureToRead.Description.Height;
            }

            if (targetW <= 0 || targetH <= 0 || targetW > 8192 || targetH > 8192)
                return;

            var texDesc = textureToRead.Description;
            int stagingW = _region != null ? targetW : texDesc.Width;
            int stagingH = _region != null ? targetH : texDesc.Height;
            
            if (_stagingTexture == null || 
                _stagingTexture.Description.Width != stagingW ||
                _stagingTexture.Description.Height != stagingH ||
                _stagingTexture.Description.Format != texDesc.Format)
            {
                _stagingTexture?.Dispose();
                _stagingTexture = Direct3D11Helper.CreateStagingTexture(
                    _sharpDxDevice, stagingW, stagingH, texDesc.Format);
            }

            if (!IsCapturing) return;
            
            int writeIdx = _writeIndex;
            if (writeIdx == _readIndex)
                writeIdx = (writeIdx + 1) % 3;
            if (writeIdx == _readyIndex && _readyIndex == _readIndex)
                writeIdx = (writeIdx + 1) % 3;
            
            ref var buffer = ref _buffers[writeIdx];
            if (buffer == null || buffer.Width != targetW || buffer.Height != targetH)
            {
                buffer?.Dispose();
                buffer = new Mat(targetH, targetW, MatType.CV_8UC4);
            }

            bool success = _stagingTexture.FillMat(_sharpDxDevice, textureToRead, buffer, _region);

            if (success)
            {
                _readyIndex = writeIdx;
                _writeIndex = (writeIdx + 1) % 3;
                _frameCount++;
            }
        }
        catch (SharpDXException ex)
        {
            if (ex.ResultCode.Code == unchecked((int)0x887A0005) ||
                ex.ResultCode.Code == unchecked((int)0x887A0006))
            {
                Log.Error("[GraphicsCapture] GPU error: 0x{Code:X}", ex.ResultCode.Code);
                IsCapturing = false;
            }
        }
        catch (Exception ex)
        {
            if (IsCapturing)
                Log.Debug("[GraphicsCapture] Frame error: {Error}", ex.Message);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private static ResourceRegion? GetGameScreenRegion(nint hWnd)
    {
        var exStyle = Vanara.PInvoke.User32.GetWindowLong(hWnd, Vanara.PInvoke.User32.WindowLongFlags.GWL_EXSTYLE);
        if ((exStyle & (int)Vanara.PInvoke.User32.WindowStylesEx.WS_EX_TOPMOST) != 0)
            return null;

        Vanara.PInvoke.DwmApi.DwmGetWindowAttribute<Vanara.PInvoke.RECT>(hWnd,
            Vanara.PInvoke.DwmApi.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, out var windowRect);
        Vanara.PInvoke.User32.GetClientRect(hWnd, out var clientRect);
        Vanara.PInvoke.POINT point = default;
        Vanara.PInvoke.User32.ClientToScreen(hWnd, ref point);

        return new ResourceRegion
        {
            Left = point.X > windowRect.Left ? point.X - windowRect.Left : 0,
            Top = point.Y > windowRect.Top ? point.Y - windowRect.Top : 0,
            Right = (point.X > windowRect.Left ? point.X - windowRect.Left : 0) + clientRect.Width,
            Bottom = (point.Y > windowRect.Top ? point.Y - windowRect.Top : 0) + clientRect.Height,
            Front = 0,
            Back = 1
        };
    }
}
