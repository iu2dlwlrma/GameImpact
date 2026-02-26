using OpenCvSharp;

namespace GameImpact.Abstractions.Recognition;

public interface IRecognitionService
{
    TemplateMatchResult MatchTemplate(Mat source, Mat template, MatchOptions? options = null);
    List<TemplateMatchResult> MatchTemplateAll(Mat source, Mat template, MatchOptions? options = null);
    ColorMatchResult MatchColor(Mat image, ColorRange range, Rect? roi = null);
}

public record MatchOptions(
    double Threshold = 0.8,
    Rect? RegionOfInterest = null,
    bool UseBinaryMatch = false,
    int BinaryThreshold = 128,
    TemplateMatchModes MatchMode = TemplateMatchModes.CCoeffNormed
);

public record TemplateMatchResult(
    bool Success,
    Point Location,
    Point Center,
    Size Size,
    double Confidence
);

public record ColorRange(Scalar Lower, Scalar Upper);

public record ColorMatchResult(
    bool Success,
    int MatchCount,
    List<Point> Points
);
