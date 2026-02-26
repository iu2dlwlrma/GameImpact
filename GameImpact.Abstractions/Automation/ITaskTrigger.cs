using OpenCvSharp;

namespace GameImpact.Abstractions.Automation;

public interface ITaskTrigger
{
    string Name { get; }
    int Priority { get; }
    bool IsEnabled { get; set; }
    bool IsExclusive { get; }
    void Init();
    void OnFrame(FrameContext context);
}

public record FrameContext(
    Mat Frame,
    int FrameIndex,
    TimeSpan Elapsed,
    CancellationToken CancellationToken
);
