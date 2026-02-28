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
/// <summary>
/// GPU 端 HDR→SDR 转换器，使用 Compute Shader
/// </summary>
public class GpuHdrConverter : IDisposable
{
    private readonly Device m_device;
    private ComputeShader? m_computeShader;
    private Texture2D? m_outputTexture;
    private Texture2D? m_inputCopyTexture;
    private UnorderedAccessView? m_outputUav;
    private ShaderResourceView? m_inputSrv;
    private int m_width, m_height;
    private bool m_initialized;
    private bool m_initFailed;
    private int m_consecutiveErrors;
    private const int MaxConsecutiveErrors = 3;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="device">Direct3D 11 设备</param>
    public GpuHdrConverter(Device device)
    {
        m_device = device;
    }

    /// <summary>
    /// 确保已初始化
    /// </summary>
    /// <returns>是否初始化成功</returns>
    private bool EnsureInitialized()
    {
        if (m_initialized)
        {
            return true;
        }
        if (m_initFailed)
        {
            return false;
        }

        try
        {
            // 检查设备状态
            if (m_device.IsDisposed)
            {
                m_initFailed = true;
                return false;
            }

            using var bytecode = ShaderBytecode.Compile(
                HdrToSdrShader.ShaderSource,
                "CSMain",
                "cs_5_0",
                ShaderFlags.OptimizationLevel3);

            if (bytecode.HasErrors)
            {
                m_initFailed = true;
                Log.Error("[GpuHdrConverter] HLSL errors: {Errors}", bytecode.Message);
                return false;
            }

            m_computeShader = new ComputeShader(m_device, bytecode);
            m_initialized = true;
            Log.Info("[GpuHdrConverter] Shader compiled successfully");
            return true;
        }
        catch (Exception ex)
        {
            m_initFailed = true;
            Log.Error("[GpuHdrConverter] Init failed: {Error}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 执行 HDR→SDR 转换，带完整的错误保护
    /// </summary>
    /// <summary>
    /// 执行 HDR→SDR 转换，带完整的错误保护
    /// </summary>
    /// <param name="hdrTexture">HDR纹理</param>
    /// <returns>转换后的SDR纹理，失败时返回null</returns>
    public Texture2D? Convert(Texture2D hdrTexture)
    {
        // 连续错误过多，直接禁用 GPU 转换
        if (m_consecutiveErrors >= MaxConsecutiveErrors)
        {
            return null;
        }

        if (!EnsureInitialized())
        {
            return null;
        }

        try
        {
            // 检查设备是否被移除
            if (m_device.IsDisposed)
            {
                m_initFailed = true;
                return null;
            }
            
            var deviceRemovedReason = m_device.DeviceRemovedReason;
            if (deviceRemovedReason.Code != 0)
            {
                Log.Error("[GpuHdrConverter] Device removed: 0x{Code:X}", deviceRemovedReason.Code);
                m_initFailed = true;
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

            if (m_outputTexture == null || m_width != width || m_height != height)
            {
                CreateTextures(width, height, desc.Format);
                m_width = width;
                m_height = height;
            }

            var context = m_device.ImmediateContext;
            
            // 复制输入纹理
            context.CopySubresourceRegion(hdrTexture, 0, null, m_inputCopyTexture, 0, 0, 0, 0);
            
            // 执行 Compute Shader
            context.ComputeShader.Set(m_computeShader);
            context.ComputeShader.SetShaderResource(0, m_inputSrv);
            context.ComputeShader.SetUnorderedAccessView(0, m_outputUav);

            int groupsX = (width + 7) / 8;
            int groupsY = (height + 7) / 8;
            context.Dispatch(groupsX, groupsY, 1);

            // 清理绑定
            context.ComputeShader.SetShaderResource(0, null);
            context.ComputeShader.SetUnorderedAccessView(0, null);
            context.ComputeShader.Set(null);
            
            // 不需要显式同步，后续的 CopyResource/MapSubresource 会自动等待 GPU 完成
            m_consecutiveErrors = 0;
            return m_outputTexture;
        }
        catch (SharpDXException ex)
        {
            m_consecutiveErrors++;
            Log.Error("[GpuHdrConverter] SharpDX error: 0x{Code:X} - {Msg}", ex.ResultCode.Code, ex.Message);
            
            if (ex.ResultCode.Code == unchecked((int)0x887A0005) || // DXGI_ERROR_DEVICE_REMOVED
                ex.ResultCode.Code == unchecked((int)0x887A0006))   // DXGI_ERROR_DEVICE_HUNG
            {
                m_initFailed = true;
            }
            return null;
        }
        catch (Exception ex)
        {
            m_consecutiveErrors++;
            Log.Error("[GpuHdrConverter] Error: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 创建输入和输出纹理
    /// </summary>
    /// <param name="width">纹理宽度</param>
    /// <param name="height">纹理高度</param>
    /// <param name="inputFormat">输入纹理格式</param>
    private void CreateTextures(int width, int height, Format inputFormat)
    {
        DisposeTextures();

        m_inputCopyTexture = new Texture2D(m_device, new Texture2DDescription
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

        m_inputSrv = new ShaderResourceView(m_device, m_inputCopyTexture);

        m_outputTexture = new Texture2D(m_device, new Texture2DDescription
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

        m_outputUav = new UnorderedAccessView(m_device, m_outputTexture);
    }

    /// <summary>
    /// 释放纹理资源
    /// </summary>
    private void DisposeTextures()
    {
        m_inputSrv?.Dispose();
        m_inputSrv = null;
        m_inputCopyTexture?.Dispose();
        m_inputCopyTexture = null;
        m_outputUav?.Dispose();
        m_outputUav = null;
        m_outputTexture?.Dispose();
        m_outputTexture = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        DisposeTextures();
        m_computeShader?.Dispose();
    }
}
