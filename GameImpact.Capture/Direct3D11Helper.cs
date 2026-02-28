#region

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using OpenCvSharp;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Vanara.PInvoke;
using WinRT;
using Buffer = System.Buffer;
using Device = SharpDX.Direct3D11.Device;
using Device3 = SharpDX.DXGI.Device3;
using MapFlags = SharpDX.Direct3D11.MapFlags;

#endregion

namespace GameImpact.Capture
{
    /// <summary>Direct3D 11 辅助类，提供设备创建、纹理操作和格式转换功能</summary>
    public static class Direct3D11Helper
    {
        private static readonly Guid ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        /// <summary>获取共享的 SharpDX Device（用于 GPU HDR 转换等操作）</summary>
        public static Device? SharedDevice
        {
            get;
            private set;
        }

        /// <summary>检测窗口所在显示器是否开启了 HDR</summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <returns>是否启用 HDR</returns>
        public static bool IsHdrEnabledForWindow(nint hWnd)
        {
            try
            {
                var hMonitor = User32.MonitorFromWindow(
                        hWnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
                if (hMonitor == IntPtr.Zero)
                {
                    return false;
                }

                using var factory = new Factory1();

                for (var adapterIdx = 0;; adapterIdx++)
                {
                    Adapter1? adapter;
                    try { adapter = factory.GetAdapter1(adapterIdx); }
                    catch (SharpDXException) { break; }

                    using (adapter)
                    {
                        for (var outputIdx = 0;; outputIdx++)
                        {
                            Output? output;
                            try { output = adapter.GetOutput(outputIdx); }
                            catch (SharpDXException) { break; }

                            using (output)
                            {
                                if (output.Description.MonitorHandle != hMonitor)
                                {
                                    continue;
                                }

                                try
                                {
                                    using var output6 = output.QueryInterface<Output6>();
                                    return output6.Description1.ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020;
                                }
                                catch { return false; }
                            }
                        }
                    }
                }
            }
            catch
            {
                /* 忽略异常，返回 false */
            }
            return false;
        }

        /// <summary>创建或获取共享的 D3D11 设备</summary>
        /// <param name="useWarp">是否使用软件渲染</param>
        /// <returns>WinRT IDirect3DDevice 接口</returns>
        public static IDirect3DDevice CreateDevice(bool useWarp = false)
        {
            if (SharedDevice == null || SharedDevice.IsDisposed)
            {
                SharedDevice = new Device(
                        useWarp ? DriverType.Software : DriverType.Hardware,
                        DeviceCreationFlags.BgraSupport);
            }
            return CreateDirect3DDeviceFromSharpDXDevice(SharedDevice);
        }

        /// <summary>从 IDirect3DSurface 创建 SharpDX Texture2D</summary>
        public static Texture2D CreateSharpDXTexture2D(IDirect3DSurface surface)
        {
            var access = surface.As<IDirect3DDxgiInterfaceAccess>();
            var d3dPointer = access.GetInterface(ID3D11Texture2D);
            return new Texture2D(d3dPointer);
        }

        /// <summary>创建用于 CPU 读取的 Staging 纹理</summary>
        public static Texture2D CreateStagingTexture(Device device, int width, int height, Format format = Format.B8G8R8A8_UNorm)
        {
            return new Texture2D(device, new Texture2DDescription
            {
                    Width = width,
                    Height = height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = format,
                    Usage = ResourceUsage.Staging,
                    SampleDescription = new SampleDescription(1, 0),
                    BindFlags = BindFlags.None,
                    CpuAccessFlags = CpuAccessFlags.Read
            });
        }

        /// <summary>将 GPU 纹理数据填充到 OpenCV Mat（BGRA32 格式）</summary>
        public static bool FillMat(this Texture2D staging, Device device, Texture2D source, Mat destMat, ResourceRegion? region = null)
        {
            if (device.IsDisposed || device.DeviceRemovedReason.Code != 0)
            {
                return false;
            }

            try
            {
                var ctx = device.ImmediateContext;

                if (region != null)
                {
                    ctx.CopySubresourceRegion(source, 0, region, staging, 0);
                }
                else
                {
                    ctx.CopyResource(staging, source);
                }

                var dataBox = ctx.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
                try
                {
                    var w = staging.Description.Width;
                    var h = staging.Description.Height;

                    if (staging.Description.Format == Format.R16G16B16A16_Float)
                    {
                        ConvertHdrToBgra(dataBox, destMat, w, h);
                    }
                    else
                    {
                        CopyBgra(dataBox, destMat, w, h);
                    }

                    return true;
                }
                finally
                {
                    ctx.UnmapSubresource(staging, 0);
                }
            }
            catch (SharpDXException)
            {
                return false;
            }
        }

#region 私有方法

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
        private static extern uint CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

        private static IDirect3DDevice CreateDirect3DDeviceFromSharpDXDevice(Device d3dDevice)
        {
            using var dxgiDevice = d3dDevice.QueryInterface<Device3>();
            CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var pUnknown);
            var device = MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
            Marshal.Release(pUnknown);
            return device;
        }

        /// <summary>BGRA32 直接内存拷贝</summary>
        private static unsafe void CopyBgra(DataBox dataBox, Mat dest, int w, int h)
        {
            var src = (byte*)dataBox.DataPointer;
            var dst = (byte*)dest.Data;
            var srcPitch = dataBox.RowPitch;
            var dstStep = (int)dest.Step();
            var rowBytes = w * 4;

            if (srcPitch == dstStep && dstStep == rowBytes)
            {
                Buffer.MemoryCopy(src, dst, (long)h * rowBytes, (long)h * rowBytes);
            }
            else
            {
                for (var y = 0; y < h; y++)
                {
                    Buffer.MemoryCopy(src + y * srcPitch, dst + y * dstStep, rowBytes, rowBytes);
                }
            }
        }

        /// <summary>HDR (R16G16B16A16_Float) → BGRA32，使用 LUT 加速</summary>
        private static unsafe void ConvertHdrToBgra(DataBox dataBox, Mat dest, int w, int h)
        {
            var lut = HdrToSdrLut.Instance;
            var src = (byte*)dataBox.DataPointer;
            var dst = (byte*)dest.Data;
            var srcPitch = dataBox.RowPitch;
            var dstStep = (int)dest.Step();

            Parallel.For(0, h, y =>
            {
                var srcRow = (Half*)(src + y * srcPitch);
                var dstRow = dst + y * dstStep;

                for (var x = 0; x < w; x++)
                {
                    dstRow[x * 4 + 0] = lut.Convert((float)srcRow[x * 4 + 2]); // B
                    dstRow[x * 4 + 1] = lut.Convert((float)srcRow[x * 4 + 1]); // G
                    dstRow[x * 4 + 2] = lut.Convert((float)srcRow[x * 4 + 0]); // R
                    dstRow[x * 4 + 3] = 255; // A
                }
            });
        }

#endregion
    }
    /// <summary>HDR→SDR 查找表（Hable Tone Mapping + sRGB Gamma）</summary>
    internal sealed class HdrToSdrLut
    {
        private const float MaxHdrValue = 16f;
        private const int LutSize = 16384;
        public static readonly HdrToSdrLut Instance = new();
        private readonly byte[] _lut;

        private HdrToSdrLut()
        {
            _lut = new byte[LutSize + 1];

            // Hable/Uncharted2 参数
            const float A = 0.15f, B = 0.50f, C = 0.10f, D = 0.20f, E = 0.02f, F = 0.30f, W = 11.2f;

            float Hable(float x)
            {
                return (x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F) - E / F;
            }

            var whiteScale = 1f / Hable(W);

            for (var i = 0; i <= LutSize; i++)
            {
                var hdr = i * MaxHdrValue / LutSize;
                var mapped = Hable(Math.Max(0f, hdr)) * whiteScale;
                var srgb = mapped <= 0.0031308f ? mapped * 12.92f : 1.055f * MathF.Pow(mapped, 1f / 2.4f) - 0.055f;
                _lut[i] = (byte)(Math.Clamp(srgb, 0f, 1f) * 255f);
            }
        }

        /// <summary>将 HDR 线性值转换为 SDR sRGB 值</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte Convert(float hdrValue)
        {
            if (hdrValue <= 0f)
            {
                return 0;
            }
            if (hdrValue >= MaxHdrValue)
            {
                return _lut[LutSize];
            }
            return _lut[(int)(hdrValue * (LutSize / MaxHdrValue))];
        }
    }
    /// <summary>IDirect3DSurface 的 DXGI 接口访问</summary>
    [ComImport] [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")] [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDirect3DDxgiInterfaceAccess
    {
        nint GetInterface([In] ref Guid iid);
    }
}
