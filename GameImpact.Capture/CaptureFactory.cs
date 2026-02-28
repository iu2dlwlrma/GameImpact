using GameImpact.Abstractions.Capture;

namespace GameImpact.Capture;

/// <summary>
/// 屏幕捕获工厂
/// </summary>
public static class CaptureFactory
{
    /// <summary>
    /// 创建屏幕捕获实例
    /// </summary>
    /// <param name="enableHdr">是否启用 HDR 捕获</param>
    /// <param name="useGpuHdrConversion">是否使用 GPU 进行 HDR→SDR 转换</param>
    /// <returns>屏幕捕获实例</returns>
    public static IScreenCapture Create(bool enableHdr = true, bool useGpuHdrConversion = false) 
        => new GraphicsCapture(enableHdr, useGpuHdrConversion);
}