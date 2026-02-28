#region

using OpenCvSharp;

#endregion

namespace GameImpact.Abstractions.Recognition
{
    /// <summary>OCR 识别结果</summary>
    public record OcrResult(
            string Text,
            float Confidence,
            Rect BoundingBox,
            Point2f[] Polygon);

    /// <summary>OCR 引擎接口（类似 RapidOCR）</summary>
    public interface IOcrEngine : IDisposable
    {
        /// <summary>识别图像中的文字</summary>
        /// <param name="image">输入图像（BGR 或 BGRA）</param>
        /// <returns>识别结果列表</returns>
        List<OcrResult> Recognize(Mat image);

        /// <summary>识别图像中的文字（指定区域）</summary>
        List<OcrResult> Recognize(Mat image, Rect roi);

        /// <summary>仅检测文字区域（不识别）</summary>
        List<Rect> Detect(Mat image);
    }
}
