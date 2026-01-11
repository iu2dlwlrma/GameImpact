using GameImpact.Utilities.Logging;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace GameImpact.Capture;

/// <summary>
/// GPU 端 HDR→SDR 转换器，使用 Compute Shader
/// 包含多重保护机制防止 GPU 错误导致系统死机
/// </summary>
public class GpuHdrConverter : IDisposable
{
    private readonly Device _device;
    private ComputeShader? _computeShader;
    private Texture2D? _outputTexture;
    private Texture2D? _inputCopyTexture;
    private UnorderedAccessView? _outputUav;
    private ShaderResourceView? _inputSrv;
    private int _width, _height;
    private bool _initialized;
    private bool _initFailed;
    private int _consecutiveErrors;
    private const int MaxConsecutiveErrors = 3;

    public GpuHdrConverter(Device device)
    {
        _device = device;
    }

    private bool EnsureInitialized()
    {
        if (_initialized) return true;
        if (_initFailed) return false;

        try
        {
            // 检查设备状态
            if (_device.IsDisposed)
            {
                _initFailed = true;
                return false;
            }

            using var bytecode = ShaderBytecode.Compile(
                HdrToSdrShader.ShaderSource,
                "CSMain",
                "cs_5_0",
                ShaderFlags.OptimizationLevel3);

            if (bytecode.HasErrors)
            {
                _initFailed = true;
                Log.Error("[GpuHdrConverter] HLSL errors: {Errors}", bytecode.Message);
                return false;
            }

            _computeShader = new ComputeShader(_device, bytecode);
            _initialized = true;
            Log.Info("[GpuHdrConverter] Shader compiled successfully");
            return true;
        }
        catch (Exception ex)
        {
            _initFailed = true;
            Log.Error("[GpuHdrConverter] Init failed: {Error}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 执行 HDR→SDR 转换，带完整的错误保护
    /// </summary>
    public Texture2D? Convert(Texture2D hdrTexture)
    {
        // 连续错误过多，直接禁用 GPU 转换
        if (_consecutiveErrors >= MaxConsecutiveErrors)
        {
            return null;
        }

        if (!EnsureInitialized())
            return null;

        try
        {
            // 检查设备是否被移除
            if (_device.IsDisposed)
            {
                _initFailed = true;
                return null;
            }
            
            var deviceRemovedReason = _device.DeviceRemovedReason;
            if (deviceRemovedReason.Code != 0)
            {
                Log.Error("[GpuHdrConverter] Device removed: 0x{Code:X}", deviceRemovedReason.Code);
                _initFailed = true;
                return null;
            }

            var desc = hdrTexture.Description;
            int width = desc.Width;
            int height = desc.Height;

            // 尺寸合理性检查
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            {
                Log.Debug("[GpuHdrConverter] Invalid texture size: {W}x{H}", width, height);
                return null;
            }
            
            // 验证输入纹理格式
            if (desc.Format != Format.R16G16B16A16_Float)
            {
                Log.Debug("[GpuHdrConverter] Unexpected format: {Format}, expected R16G16B16A16_Float", desc.Format);
                return null;
            }

            if (_outputTexture == null || _width != width || _height != height)
            {
                CreateTextures(width, height, desc.Format);
                _width = width;
                _height = height;
            }

            var context = _device.ImmediateContext;
            
            // 复制输入纹理
            context.CopySubresourceRegion(hdrTexture, 0, null, _inputCopyTexture, 0, 0, 0, 0);
            
            // 执行 Compute Shader
            context.ComputeShader.Set(_computeShader);
            context.ComputeShader.SetShaderResource(0, _inputSrv);
            context.ComputeShader.SetUnorderedAccessView(0, _outputUav);

            int groupsX = (width + 7) / 8;
            int groupsY = (height + 7) / 8;
            context.Dispatch(groupsX, groupsY, 1);

            // 清理绑定
            context.ComputeShader.SetShaderResource(0, null);
            context.ComputeShader.SetUnorderedAccessView(0, null);
            context.ComputeShader.Set(null);
            
            // 不需要显式同步，后续的 CopyResource/MapSubresource 会自动等待 GPU 完成
            _consecutiveErrors = 0;
            return _outputTexture;
        }
        catch (SharpDXException ex)
        {
            _consecutiveErrors++;
            Log.Error("[GpuHdrConverter] SharpDX error: 0x{Code:X} - {Msg}", ex.ResultCode.Code, ex.Message);
            
            if (ex.ResultCode.Code == unchecked((int)0x887A0005) || // DXGI_ERROR_DEVICE_REMOVED
                ex.ResultCode.Code == unchecked((int)0x887A0006))   // DXGI_ERROR_DEVICE_HUNG
            {
                _initFailed = true;
            }
            return null;
        }
        catch (Exception ex)
        {
            _consecutiveErrors++;
            Log.Error("[GpuHdrConverter] Error: {Error}", ex.Message);
            return null;
        }
    }

    private void CreateTextures(int width, int height, Format inputFormat)
    {
        DisposeTextures();

        _inputCopyTexture = new Texture2D(_device, new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = inputFormat,
            Usage = ResourceUsage.Default,
            SampleDescription = new SampleDescription(1, 0),
            BindFlags = BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None
        });

        _inputSrv = new ShaderResourceView(_device, _inputCopyTexture);

        _outputTexture = new Texture2D(_device, new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            Usage = ResourceUsage.Default,
            SampleDescription = new SampleDescription(1, 0),
            BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None
        });

        _outputUav = new UnorderedAccessView(_device, _outputTexture);
    }

    private void DisposeTextures()
    {
        _inputSrv?.Dispose();
        _inputSrv = null;
        _inputCopyTexture?.Dispose();
        _inputCopyTexture = null;
        _outputUav?.Dispose();
        _outputUav = null;
        _outputTexture?.Dispose();
        _outputTexture = null;
    }

    public void Dispose()
    {
        DisposeTextures();
        _computeShader?.Dispose();
    }
}
