using System;
using OpenCvSharp;

namespace GameImpact.ImageProcessing;

/// <summary>
/// 游戏文字图像预处理器，用于提升 OCR 识别率
/// </summary>
public static class GameTextPreprocessor
{
    /// <summary>
    /// 游戏 UI 综合预处理（推荐用于游戏界面）
    /// 通过提取多种颜色通道并合并，提升各种颜色文字的识别率
    /// </summary>
    public static Mat PreprocessGameUI(Mat image)
    {
        if (image.Empty()) return new Mat();

        using var bgr = EnsureBgr(image);
        
        // 放大小图以提升小字识别率
        var scale = Math.Max(1.0, 1500.0 / Math.Max(bgr.Width, bgr.Height));
        using var scaled = scale > 1.0 
            ? bgr.Resize(new Size(), scale, scale, InterpolationFlags.Cubic) 
            : bgr.Clone();

        // 提取白色/浅色文字
        using var whiteMask = ExtractBrightPixels(scaled, 180);
        
        // 提取绿色文字（游戏中常见的高亮色）
        using var greenMask = ExtractColorRange(scaled, 
            new Scalar(35, 80, 80), new Scalar(85, 255, 255));
        
        // 提取黄色/金色文字
        using var yellowMask = ExtractColorRange(scaled,
            new Scalar(15, 80, 80), new Scalar(35, 255, 255));

        // 合并所有蒙版
        var combined = new Mat();
        Cv2.BitwiseOr(whiteMask, greenMask, combined);
        Cv2.BitwiseOr(combined, yellowMask, combined);

        // 轻微膨胀连接断开的笔画
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
        Cv2.Dilate(combined, combined, kernel);

        return combined;
    }

    /// <summary>
    /// 提取亮度高于阈值的像素（白色/浅色文字）
    /// </summary>
    private static Mat ExtractBrightPixels(Mat bgr, int threshold)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        
        var mask = new Mat();
        Cv2.Threshold(gray, mask, threshold, 255, ThresholdTypes.Binary);
        return mask;
    }

    /// <summary>
    /// 提取指定 HSV 颜色范围的像素
    /// </summary>
    private static Mat ExtractColorRange(Mat bgr, Scalar lowerHsv, Scalar upperHsv)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        
        var mask = new Mat();
        Cv2.InRange(hsv, lowerHsv, upperHsv, mask);
        return mask;
    }

    /// <summary>
    /// 确保图像是 BGR 格式
    /// </summary>
    private static Mat EnsureBgr(Mat image)
    {
        if (image.Channels() == 4)
        {
            var bgr = new Mat();
            Cv2.CvtColor(image, bgr, ColorConversionCodes.BGRA2BGR);
            return bgr;
        }
        if (image.Channels() == 1)
        {
            var bgr = new Mat();
            Cv2.CvtColor(image, bgr, ColorConversionCodes.GRAY2BGR);
            return bgr;
        }
        return image.Clone();
    }

    /// <summary>
    /// 简单预处理（灰度 + CLAHE + 二值化）
    /// </summary>
    public static Mat PreprocessSimple(Mat image)
    {
        if (image.Empty()) return new Mat();

        var gray = new Mat();
        if (image.Channels() > 1)
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        else
            image.CopyTo(gray);

        using var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8));
        clahe.Apply(gray, gray);

        var result = new Mat();
        Cv2.Threshold(gray, result, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        
        gray.Dispose();
        return result;
    }
}
