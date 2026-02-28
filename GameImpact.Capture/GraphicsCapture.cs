#region

using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using GameImpact.Abstractions.Capture;
using GameImpact.Utilities.Logging;
using OpenCvSharp;
using SharpDX;
using SharpDX.Direct3D11;
using Vanara.PInvoke;

#endregion

namespace GameImpact.Capture
{
    /// <summary>基于 Windows.Graphics.Capture API 的屏幕捕获实现，支持 HDR 和三缓冲</summary>
    public class GraphicsCapture : IScreenCapture
    {
        private readonly bool m_enableHdr;
        private readonly bool m_useGpuHdrConversion;

        private Mat?[] m_buffers = new Mat?[3];
        private GraphicsCaptureItem? m_captureItem;
        private IDirect3DDevice? m_d3dDevice;
        private volatile int m_frameCount;
        private Direct3D11CaptureFramePool? m_framePool;
        private GpuHdrConverter? m_hdrConverter;
        private nint m_hWnd;
        private volatile bool m_isProcessing;
        private DirectXPixelFormat m_pixelFormat;
        private volatile int m_readIndex = -1;
        private volatile int m_readyIndex = -1;
        private ResourceRegion? m_region;
        private GraphicsCaptureSession? m_session;
        private Device? m_sharpDxDevice;

        private Texture2D? m_stagingTexture;
        private int m_surfaceWidth, m_surfaceHeight;
        private volatile int m_writeIndex;

        /// <summary>创建 GraphicsCapture 实例</summary>
        /// <summary>创建 GraphicsCapture 实例</summary>
        /// <param name="enableHdr">是否启用HDR</param>
        /// <param name="useGpuHdrConversion">是否使用GPU进行HDR转换</param>
        public GraphicsCapture(bool enableHdr = false, bool useGpuHdrConversion = false)
        {
            m_enableHdr = enableHdr;
            m_useGpuHdrConversion = useGpuHdrConversion;
        }
        public bool IsCapturing{ get; private set; }
        public int FrameCount => m_frameCount;

        /// <summary>开始捕获指定窗口</summary>
        /// <param name="windowHandle">目标窗口句柄</param>
        /// <param name="options">捕获选项</param>
        public void Start(nint windowHandle, CaptureOptions? options = null)
        {
            m_hWnd = windowHandle;
            m_region = GetGameScreenRegion(windowHandle);
            IsCapturing = true;
            m_frameCount = 0;

            m_captureItem = CaptureHelper.CreateItemForWindow(m_hWnd);
            if (m_captureItem == null)
            {
                throw new InvalidOperationException("Failed to create capture item");
            }

            m_surfaceWidth = m_captureItem.Size.Width;
            m_surfaceHeight = m_captureItem.Size.Height;
            m_d3dDevice = Direct3D11Helper.CreateDevice();

            m_pixelFormat = m_enableHdr ? DirectXPixelFormat.R16G16B16A16Float : DirectXPixelFormat.B8G8R8A8UIntNormalized;

            m_framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(m_d3dDevice, m_pixelFormat, 3, m_captureItem.Size);
            m_captureItem.Closed += (_, _) => Stop();
            m_framePool.FrameArrived += OnFrameArrived;

            m_session = m_framePool.CreateCaptureSession(m_captureItem);

            if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
            {
                m_session.IsCursorCaptureEnabled = false;
            }

            if (ApiInformation.IsWriteablePropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
            {
                m_session.IsBorderRequired = false;
            }

            m_session.StartCapture();
            Log.Info("[GraphicsCapture] Started (HDR: {Hdr}, GPU: {Gpu})", m_enableHdr, m_useGpuHdrConversion);
        }

        /// <summary>获取最新帧的克隆副本（BGRA32 格式）</summary>
        public Mat? Capture()
        {
            var ready = m_readyIndex;
            if (ready < 0)
            {
                return null;
            }

            m_readIndex = ready;
            var mat = m_buffers[ready];
            if (mat == null || mat.IsDisposed)
            {
                return null;
            }

            return mat.Clone();
        }

        /// <summary>零拷贝访问：获取 BGRA32 格式帧数据指针</summary>
        public bool TryGetFrameData(out nint data, out int width, out int height, out int step)
        {
            var ready = m_readyIndex;
            if (ready < 0 || m_buffers[ready] == null)
            {
                data = 0;
                width = height = step = 0;
                return false;
            }

            m_readIndex = ready;
            var mat = m_buffers[ready]!;
            data = mat.Data;
            width = mat.Width;
            height = mat.Height;
            step = (int)mat.Step();
            return true;
        }

        /// <summary>释放当前读取的帧</summary>
        public void ReleaseFrame()
        {
            m_readIndex = -1;
        }

        /// <summary>停止捕获并释放资源</summary>
        public void Stop()
        {
            if (!IsCapturing)
            {
                return;
            }
            IsCapturing = false;

            SpinWait.SpinUntil(() => !m_isProcessing, 100);

            try { m_session?.Dispose(); }
            catch { }
            m_session = null;

            if (m_framePool != null)
            {
                m_framePool.FrameArrived -= OnFrameArrived;
                try { m_framePool.Dispose(); }
                catch { }
                m_framePool = null;
            }

            m_captureItem = null;
            try { m_stagingTexture?.Dispose(); }
            catch { }
            m_stagingTexture = null;
            try { m_hdrConverter?.Dispose(); }
            catch { }
            m_hdrConverter = null;
            m_sharpDxDevice = null;
            try { m_d3dDevice?.Dispose(); }
            catch { }
            m_d3dDevice = null;

            for (var i = 0; i < m_buffers.Length; i++)
            {
                try { m_buffers[i]?.Dispose(); }
                catch { }
                m_buffers[i] = null;
            }
            m_readyIndex = -1;
            m_readIndex = -1;
            m_writeIndex = 0;
            m_hWnd = 0;

            Log.Info("[GraphicsCapture] Stopped (frames: {Count})", m_frameCount);
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            if (!IsCapturing || m_hWnd == 0)
            {
                return;
            }
            if (m_isProcessing)
            {
                return;
            }
            m_isProcessing = true;

            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame == null || !IsCapturing)
                {
                    return;
                }

                m_sharpDxDevice ??= Direct3D11Helper.SharedDevice;
                if (m_sharpDxDevice == null || m_sharpDxDevice.IsDisposed)
                {
                    Log.Error("[GraphicsCapture] SharedDevice unavailable");
                    IsCapturing = false;
                    return;
                }

                if (m_sharpDxDevice.DeviceRemovedReason.Code != 0)
                {
                    Log.Error("[GraphicsCapture] GPU device removed: 0x{Code:X}", m_sharpDxDevice.DeviceRemovedReason.Code);
                    IsCapturing = false;
                    return;
                }

                var captureSize = m_captureItem!.Size;
                if (captureSize.Width != m_surfaceWidth || captureSize.Height != m_surfaceHeight)
                {
                    m_framePool!.Recreate(m_d3dDevice, m_pixelFormat, 3, captureSize);
                    m_stagingTexture?.Dispose();
                    m_stagingTexture = null;
                    m_surfaceWidth = captureSize.Width;
                    m_surfaceHeight = captureSize.Height;
                    m_region = GetGameScreenRegion(m_hWnd);
                    return;
                }

                using var surfaceTexture = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);

                var textureToRead = surfaceTexture;
                if (m_enableHdr && m_useGpuHdrConversion)
                {
                    m_hdrConverter ??= new GpuHdrConverter(m_sharpDxDevice);
                    var converted = m_hdrConverter.Convert(surfaceTexture);
                    if (converted != null)
                    {
                        textureToRead = converted;
                    }
                }

                int targetW, targetH;
                if (m_region != null)
                {
                    targetW = m_region.Value.Right - m_region.Value.Left;
                    targetH = m_region.Value.Bottom - m_region.Value.Top;
                }
                else
                {
                    targetW = textureToRead.Description.Width;
                    targetH = textureToRead.Description.Height;
                }

                if (targetW <= 0 || targetH <= 0 || targetW > 8192 || targetH > 8192)
                {
                    return;
                }

                var texDesc = textureToRead.Description;
                var stagingW = m_region != null ? targetW : texDesc.Width;
                var stagingH = m_region != null ? targetH : texDesc.Height;

                if (m_stagingTexture == null ||
                        m_stagingTexture.Description.Width != stagingW ||
                        m_stagingTexture.Description.Height != stagingH ||
                        m_stagingTexture.Description.Format != texDesc.Format)
                {
                    m_stagingTexture?.Dispose();
                    m_stagingTexture = Direct3D11Helper.CreateStagingTexture(
                            m_sharpDxDevice, stagingW, stagingH, texDesc.Format);
                }

                if (!IsCapturing)
                {
                    return;
                }

                var writeIdx = m_writeIndex;
                if (writeIdx == m_readIndex)
                {
                    writeIdx = (writeIdx + 1) % 3;
                }
                if (writeIdx == m_readyIndex && m_readyIndex == m_readIndex)
                {
                    writeIdx = (writeIdx + 1) % 3;
                }

                ref var buffer = ref m_buffers[writeIdx];
                if (buffer == null || buffer.Width != targetW || buffer.Height != targetH)
                {
                    buffer?.Dispose();
                    buffer = new Mat(targetH, targetW, MatType.CV_8UC4);
                }

                var success = m_stagingTexture.FillMat(m_sharpDxDevice, textureToRead, buffer, m_region);

                if (success)
                {
                    m_readyIndex = writeIdx;
                    m_writeIndex = (writeIdx + 1) % 3;
                    m_frameCount++;
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
                {
                    Log.Debug("[GraphicsCapture] Frame error: {Error}", ex.Message);
                }
            }
            finally
            {
                m_isProcessing = false;
            }
        }

        private static ResourceRegion? GetGameScreenRegion(nint hWnd)
        {
            var exStyle = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
            if ((exStyle & (int)User32.WindowStylesEx.WS_EX_TOPMOST) != 0)
            {
                return null;
            }

            DwmApi.DwmGetWindowAttribute<RECT>(hWnd,
                    DwmApi.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, out var windowRect);
            User32.GetClientRect(hWnd, out var clientRect);
            POINT point = default;
            User32.ClientToScreen(hWnd, ref point);

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
}
