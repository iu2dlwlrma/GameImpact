#region

using OpenCvSharp;

#endregion

namespace GameImpact.Abstractions.Recognition
{
    public interface IRecognitionService
    {
        TemplateMatchResult MatchTemplate(Mat source, Mat template, MatchOptions? options = null);
        List<TemplateMatchResult> MatchTemplateAll(Mat source, Mat template, MatchOptions? options = null);
        ColorMatchResult MatchColor(Mat image, ColorRange range, Rect? roi = null);
    }

    public record MatchOptions(
            double Threshold = 0.8,
            Rect? RegionOfInterest = null,
            Rect? TemplateRegionOfInterest = null,
            bool UseBinaryMatch = false,
            int BinaryThreshold = 128,
            bool UseEdgeMatch = false,
            int CannyThreshold1 = 50,
            int CannyThreshold2 = 150,
            TemplateMatchModes MatchMode = TemplateMatchModes.CCoeffNormed);

    public record TemplateMatchResult(
            bool Success,
            Point Location,
            Point Center,
            Size Size,
            double Confidence);

    public record ColorRange(Scalar Lower, Scalar Upper);

    public record ColorMatchResult(
            bool Success,
            int MatchCount,
            List<Point> Points);
}
