using OpenCvSharp;

namespace GameImpact.Abstractions.Capture;

public interface IScreenCapture : IDisposable
{
    bool IsCapturing { get; }
    int FrameCount { get; }
    void Start(nint windowHandle, CaptureOptions? options = null);
    Mat? Capture();
    void Stop();
    
    /// <summary>
    /// 零拷贝访问：获取 BGRA32 格式帧数据指针。调用后必须调用 ReleaseFrame()
    /// </summary>
    bool TryGetFrameData(out nint data, out int width, out int height, out int step);
    
    /// <summary>
    /// 释放帧数据访问
    /// </summary>
    void ReleaseFrame();
}

public record CaptureOptions(bool EnableHdr = false);
