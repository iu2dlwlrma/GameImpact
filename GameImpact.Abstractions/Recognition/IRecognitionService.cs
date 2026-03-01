#region

using OpenCvSharp;

#endregion

namespace GameImpact.Abstractions.Recognition
{
    /// <summary>模板匹配算法类型</summary>
    [Flags]
    public enum MatchAlgorithm
    {
        None = 0,
        /// <summary>标准相关性匹配（最推荐，定位首选）</summary>
        NCC = 1 << 0,
        /// <summary>边缘特征匹配（适合背景透明、动态变化的UI）</summary>
        Edge = 1 << 1,
        /// <summary>特征点匹配（适合跨分辨率、旋转图标）</summary>
        FeaturePoints = 1 << 2,
        /// <summary>感知哈希验证（适合快速验证两张图是否相似，不用于定位）</summary>
        PHash = 1 << 3,
        /// <summary>带掩模匹配（处理不规则形状按钮的杀手锏）</summary>
        Masked = 1 << 4
    }

    /// <summary>混合匹配结果的综合方式</summary>
    public enum MatchCombineMode
    {
        /// <summary>取平均值</summary>
        Average,
        /// <summary>取最大值</summary>
        Max,
        /// <summary>取最小值</summary>
        Min,
        /// <summary>加权平均（根据算法权重）</summary>
        WeightedAverage
    }

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
            MatchAlgorithm MatchAlgorithms = MatchAlgorithm.NCC,
            MatchCombineMode CombineMode = MatchCombineMode.Average,
            Dictionary<MatchAlgorithm, double>? AlgorithmWeights = null,
            int CannyThreshold1 = 50,
            int CannyThreshold2 = 150,
            Mat? TemplateMask = null);

    public record TemplateMatchResult(
            bool Success,
            Point Location,
            Point Center,
            Size Size,
            double Confidence,
            Mat? ProcessedSourceImage = null,
            Mat? ProcessedTemplateImage = null);

    public record ColorRange(Scalar Lower, Scalar Upper);

    public record ColorMatchResult(
            bool Success,
            int MatchCount,
            List<Point> Points);
}
