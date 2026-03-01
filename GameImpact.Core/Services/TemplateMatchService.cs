#region

using GameImpact.Abstractions.Recognition;
using GameImpact.Utilities.Images;
using GameImpact.Utilities.Logging;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;

#endregion

namespace GameImpact.Core.Services
{
    /// <summary>模板匹配结果及供 Overlay 绘制的数据。</summary>
    public sealed class TemplateMatchResult
    {
        public bool Found{ get; init; }
        public int CenterX{ get; init; }
        public int CenterY{ get; init; }
        public double Confidence{ get; init; }
        public string? Text{ get; init; }
        /// <summary>匹配框绘制：(roi, 框列表)。</summary>
        public (Rect roi, List<(Rect box, string text)> draw) MatchDraw{ get; init; }
        /// <summary>文字区域绘制，有 OCR 时非 null。</summary>
        public (Rect roi, List<(Rect box, string text)> draw)? TextDraw{ get; init; }
    }

    /// <summary>模板匹配结果，包含处理后的图像。</summary>
    public sealed class TemplateMatchResultWithImages
    {
        public bool Found{ get; init; }
        public int CenterX{ get; init; }
        public int CenterY{ get; init; }
        public double Confidence{ get; init; }
        public string? Text{ get; init; }
        /// <summary>匹配框绘制：(roi, 框列表)。</summary>
        public (Rect roi, List<(Rect box, string text)> draw) MatchDraw{ get; init; }
        /// <summary>文字区域绘制，有 OCR 时非 null。</summary>
        public (Rect roi, List<(Rect box, string text)> draw)? TextDraw{ get; init; }
        /// <summary>处理后的匹配图（调用方负责 Dispose）。</summary>
        public Mat? ProcessedMatchImage{ get; init; }
        /// <summary>处理后的捕获图（调用方负责 Dispose）。</summary>
        public Mat? ProcessedCaptureImage{ get; init; }
    }

    /// <summary>模板匹配与区域截取，不依赖 UI。</summary>
    public interface ITemplateMatchService
    {
        /// <summary>用当前帧与模板匹配，可选提取文字区域 OCR。返回结果及 Overlay 绘制数据。</summary>
        TemplateMatchResult MatchWithTemplateAndText(string? fileName, Rect? textRegion = null);

        /// <summary>用当前帧与模板匹配，并返回处理后的图像。返回结果及处理后的图像。</summary>
        TemplateMatchResultWithImages MatchWithTemplateAndGetImages(string? fileName, Rect? textRegion = null);

        /// <summary>截取当前帧指定区域，返回克隆的 Mat（调用方负责 Dispose）。</summary>
        Mat? CaptureRegion(int x, int y, int width, int height);
    }

    /// <summary>模板匹配服务实现。</summary>
    public sealed class TemplateMatchService : ITemplateMatchService
    {
        private readonly GameContext m_context;
        private readonly ITemplateService m_templates;

        public TemplateMatchService(GameContext context, ITemplateService templates)
        {
            m_context = context;
            m_templates = templates;
        }

        public TemplateMatchResult MatchWithTemplateAndText(string? fileName, Rect? textRegion = null)
        {
            var empty = (new Rect(), new List<(Rect box, string text)>());
            if (string.IsNullOrEmpty(fileName))
            {
                return new TemplateMatchResult { Found = false, MatchDraw = empty };
            }

            if (m_context.Capture == null || !m_context.Capture.IsCapturing)
            {
                Log.WarnScreen("[识别] 请先启动捕获");
                return new TemplateMatchResult { Found = false, MatchDraw = empty };
            }

            var path = m_templates.GetTemplatePath(fileName);
            if (!File.Exists(path))
            {
                Log.WarnScreen("[识别] 模板不存在: {File}", fileName);
                return new TemplateMatchResult { Found = false, MatchDraw = empty };
            }

            try
            {
                Rect? matchRoi = null;
                Rect? textRoiFromConfig = null;
                try
                {
                    var (match, text) = m_templates.LoadTemplateRoi(fileName);
                    matchRoi = match;
                    textRoiFromConfig = text;
                }
                catch (Exception ex)
                {
                    Log.DebugScreen("[识别] 读取 ROI 配置失败: {Error}", ex.Message);
                }

                if (!textRegion.HasValue && textRoiFromConfig.HasValue)
                {
                    textRegion = textRoiFromConfig;
                }

                using var template = ImageHelper.LoadFromFile(path);
                if (template.Empty())
                {
                    Log.WarnScreen("[识别] 无法读取模板: {File}", fileName);
                    return new TemplateMatchResult { Found = false, MatchDraw = empty };
                }

                var frame = m_context.Capture.Capture();
                if (frame == null)
                {
                    Log.WarnScreen("[识别] 无法获取当前帧");
                    return new TemplateMatchResult { Found = false, MatchDraw = empty };
                }

                using (frame)
                {
                    var matchOptions = new MatchOptions(
                            0.6,
                            TemplateRegionOfInterest: matchRoi,
                            UseEdgeMatch: true,
                            CannyThreshold1: 50,
                            CannyThreshold2: 150);
                    // 克隆frame以避免MatchTemplate内部可能释放原始frame的问题
                    using var frameClone = frame.Clone();
                    var result = m_context.Recognition.MatchTemplate(frameClone, template, matchOptions);
                    if (!result.Success)
                    {
                        Log.InfoScreen("[识别] 未匹配到模板 '{File}'", fileName);
                        return new TemplateMatchResult { Found = false, MatchDraw = empty };
                    }

                    var rect = new Rect(result.Location.X, result.Location.Y, result.Size.Width, result.Size.Height);
                    var matchDraw = (roi: rect, draw: new List<(Rect box, string text)> { (new Rect(0, 0, result.Size.Width, result.Size.Height), $"匹配 {result.Confidence:P0}") });
                    Log.InfoScreen("[识别] 找到模板 '{File}' 中心=({X},{Y}) 置信度={Conf:P0}", fileName, result.Center.X, result.Center.Y, result.Confidence);

                    string? recognizedText = null;
                    (Rect roi, List<(Rect box, string text)> draw)? textDraw = null;

                    if (textRegion.HasValue)
                    {
                        try
                        {
                            var textX = result.Location.X + textRegion.Value.X;
                            var textY = result.Location.Y + textRegion.Value.Y;
                            var textWidth = textRegion.Value.Width;
                            var textHeight = textRegion.Value.Height;

                            if (textX >= 0 && textY >= 0 && textX + textWidth <= frameClone.Width && textY + textHeight <= frameClone.Height)
                            {
                                var textRoi = new Rect(textX, textY, textWidth, textHeight);
                                var ocrResults = m_context.Ocr.Recognize(frameClone, textRoi);
                                if (ocrResults.Count > 0)
                                {
                                    recognizedText = string.Join("", ocrResults.Select(r => r.Text));
                                    var avgConfidence = ocrResults.Average(r => r.Confidence);
                                    Log.InfoScreen("[识别] 提取文字: {Text} (置信度: {Conf:P0})", recognizedText, avgConfidence);
                                    textDraw = (textRoi, ocrResults.Select(r => (r.BoundingBox, r.Text)).ToList());
                                }
                                else
                                {
                                    Log.DebugScreen("[识别] 文字区域未识别到文字");
                                }
                            }
                            else
                            {
                                Log.WarnScreen("[识别] 文字区域超出边界");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.WarnScreen("[识别] 文字识别失败: {Error}", ex.Message);
                        }
                    }

                    return new TemplateMatchResult
                    {
                            Found = true,
                            CenterX = result.Center.X,
                            CenterY = result.Center.Y,
                            Confidence = result.Confidence,
                            Text = recognizedText,
                            MatchDraw = matchDraw,
                            TextDraw = textDraw
                    };
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[识别] 模板匹配失败");
                return new TemplateMatchResult { Found = false, MatchDraw = empty };
            }
        }

        public TemplateMatchResultWithImages MatchWithTemplateAndGetImages(string? fileName, Rect? textRegion = null)
        {
            var empty = (new Rect(), new List<(Rect box, string text)>());
            if (string.IsNullOrEmpty(fileName))
            {
                return new TemplateMatchResultWithImages { Found = false, MatchDraw = empty };
            }

            if (m_context.Capture == null || !m_context.Capture.IsCapturing)
            {
                Log.WarnScreen("[识别] 请先启动捕获");
                return new TemplateMatchResultWithImages { Found = false, MatchDraw = empty };
            }

            var path = m_templates.GetTemplatePath(fileName);
            if (!File.Exists(path))
            {
                Log.WarnScreen("[识别] 模板不存在: {File}", fileName);
                return new TemplateMatchResultWithImages { Found = false, MatchDraw = empty };
            }

            Mat? processedMatchImage = null;
            Mat? processedCaptureImage = null;

            try
            {
                Rect? matchRoi = null;
                Rect? textRoiFromConfig = null;
                try
                {
                    var (match, text) = m_templates.LoadTemplateRoi(fileName);
                    matchRoi = match;
                    textRoiFromConfig = text;
                }
                catch (Exception ex)
                {
                    Log.DebugScreen("[识别] 读取 ROI 配置失败: {Error}", ex.Message);
                }

                if (!textRegion.HasValue && textRoiFromConfig.HasValue)
                {
                    textRegion = textRoiFromConfig;
                }

                using var template = ImageHelper.LoadFromFile(path);
                if (template.Empty())
                {
                    Log.WarnScreen("[识别] 无法读取模板: {File}", fileName);
                    return new TemplateMatchResultWithImages { Found = false, MatchDraw = empty };
                }

                var frame = m_context.Capture.Capture();
                if (frame == null)
                {
                    Log.WarnScreen("[识别] 无法获取当前帧");
                    return new TemplateMatchResultWithImages { Found = false, MatchDraw = empty };
                }

                try
                {
                    // 先构造与模板匹配时相同的配置
                    var matchOptions = new MatchOptions(
                            0.6,
                            TemplateRegionOfInterest: matchRoi,
                            UseEdgeMatch: true,
                            CannyThreshold1: 50,
                            CannyThreshold2: 150);

                    // 克隆frame以避免MatchTemplate内部可能释放原始frame的问题
                    // MatchTemplate内部当roi不存在时，searchArea直接引用source，使用using var会释放source
                    using var frameClone = frame.Clone();

                    // 先执行模板匹配，如果失败则立即返回，避免不必要的图像处理
                    var result = m_context.Recognition.MatchTemplate(frameClone, template, matchOptions);
                    
                    if (!result.Success)
                    {
                        Log.InfoScreen("[识别] 未匹配到模板 '{File}'", fileName);
                        // 匹配失败时直接返回，不生成处理后的图像以提升性能
                        return new TemplateMatchResultWithImages
                        {
                                Found = false,
                                CenterX = 0,
                                CenterY = 0,
                                Confidence = result.Confidence,
                                Text = null,
                                MatchDraw = empty,
                                TextDraw = null,
                                ProcessedMatchImage = null,
                                ProcessedCaptureImage = null
                        };
                    }

                    // MatchTemplate 可能会释放传入的 frameClone（当没有 ROI 时，searchArea 直接引用 source 并在 using 结束时释放）
                    // 所以在调用 CreateProcessedSourceImage 之前，需要再次克隆 frame
                    // 只有在匹配成功时才生成处理后的图像，用于调试预览
                    // 注意：这里的 processedCaptureImage / processedMatchImage 即为模板匹配前处理后的单通道图像，
                    // 用于 DebugWindow 中的"匹配图/捕获图预览"。
                    try
                    {
                        // 重新克隆 frame，因为 frameClone 可能已被 MatchTemplate 释放
                        using var frameForProcessing = frame.Clone();
                        if (!frameForProcessing.IsDisposed)
                        {
                            processedCaptureImage = CreateProcessedSourceImage(frameForProcessing, matchOptions);
                        }
                        else
                        {
                            Log.DebugScreen("[识别] frameForProcessing 已被释放，无法创建处理后的捕获图");
                        }
                        if (processedCaptureImage != null)
                        {
                            Log.DebugScreen("[识别] 处理后的捕获图创建成功: {Width}x{Height}", processedCaptureImage.Width, processedCaptureImage.Height);
                        }
                        else
                        {
                            Log.DebugScreen("[识别] 处理后的捕获图为 null");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.DebugScreen("[识别] 生成处理后的捕获图失败: {Error}", ex.Message);
                        Log.DebugScreen("[识别] 异常堆栈: {StackTrace}", ex.StackTrace);
                    }

                    try
                    {
                        processedMatchImage = CreateProcessedTemplateImage(template, matchOptions);
                        if (processedMatchImage != null)
                        {
                            Log.DebugScreen("[识别] 处理后的匹配图创建成功: {Width}x{Height}", processedMatchImage.Width, processedMatchImage.Height);
                        }
                        else
                        {
                            Log.DebugScreen("[识别] 处理后的匹配图为 null");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.DebugScreen("[识别] 生成处理后的匹配图失败: {Error}", ex.Message);
                        Log.DebugScreen("[识别] 异常堆栈: {StackTrace}", ex.StackTrace);
                    }

                    var rect = new Rect(result.Location.X, result.Location.Y, result.Size.Width, result.Size.Height);
                    var matchDraw = (roi: rect, draw: new List<(Rect box, string text)> { (new Rect(0, 0, result.Size.Width, result.Size.Height), $"匹配 {result.Confidence:P0}") });
                    Log.InfoScreen("[识别] 找到模板 '{File}' 中心=({X},{Y}) 置信度={Conf:P0}", fileName, result.Center.X, result.Center.Y, result.Confidence);

                    string? recognizedText = null;
                    (Rect roi, List<(Rect box, string text)> draw)? textDraw = null;

                    if (textRegion.HasValue)
                    {
                        try
                        {
                            var textX = result.Location.X + textRegion.Value.X;
                            var textY = result.Location.Y + textRegion.Value.Y;
                            var textWidth = textRegion.Value.Width;
                            var textHeight = textRegion.Value.Height;

                            // 使用原始的 frame 而不是 frameClone，因为 frameClone 可能已被 MatchTemplate 释放
                            if (textX >= 0 && textY >= 0 && textX + textWidth <= frame.Width && textY + textHeight <= frame.Height)
                            {
                                var textRoi = new Rect(textX, textY, textWidth, textHeight);
                                var ocrResults = m_context.Ocr.Recognize(frame, textRoi);
                                if (ocrResults.Count > 0)
                                {
                                    recognizedText = string.Join("", ocrResults.Select(r => r.Text));
                                    var avgConfidence = ocrResults.Average(r => r.Confidence);
                                    Log.InfoScreen("[识别] 提取文字: {Text} (置信度: {Conf:P0})", recognizedText, avgConfidence);
                                    textDraw = (textRoi, ocrResults.Select(r => (r.BoundingBox, r.Text)).ToList());
                                }
                                else
                                {
                                    Log.DebugScreen("[识别] 文字区域未识别到文字");
                                }
                            }
                            else
                            {
                                Log.WarnScreen("[识别] 文字区域超出边界");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.WarnScreen("[识别] 文字识别失败: {Error}", ex.Message);
                        }
                    }

                    return new TemplateMatchResultWithImages
                    {
                            Found = true,
                            CenterX = result.Center.X,
                            CenterY = result.Center.Y,
                            Confidence = result.Confidence,
                            Text = recognizedText,
                            MatchDraw = matchDraw,
                            TextDraw = textDraw,
                            ProcessedMatchImage = processedMatchImage,
                            ProcessedCaptureImage = processedCaptureImage
                    };
                }
                finally
                {
                    frame.Dispose();
                    // frameForProcessing 会在 processedCaptureImage 创建后由调用方负责 Dispose
                    // 但如果创建失败，需要在这里释放
                    // 注意：processedCaptureImage 是 frameForProcessing 的克隆，所以可以安全释放 frameForProcessing
                    // 但为了安全，我们不在 finally 中释放，而是让调用方负责
                }
            }
            catch (Exception ex)
            {
                Log.ErrorScreen(ex, "[识别] 模板匹配失败");
                processedMatchImage?.Dispose();
                processedCaptureImage?.Dispose();
                return new TemplateMatchResultWithImages { Found = false, MatchDraw = empty };
            }
        }

        public Mat? CaptureRegion(int x, int y, int width, int height)
        {
            if (m_context.Capture == null || !m_context.Capture.IsCapturing)
            {
                Log.WarnScreen("[截图] 请先启动捕获");
                return null;
            }
            var frame = m_context.Capture.Capture();
            if (frame == null)
            {
                Log.WarnScreen("[截图] 无法获取帧");
                return null;
            }
            using (frame)
            {
                if (x + width > frame.Width || y + height > frame.Height)
                {
                    Log.WarnScreen("[截图] 选区超出边界");
                    return null;
                }
                var roi = new Rect(x, y, width, height);
                return new Mat(frame, roi).Clone();
            }
        }

        /// <summary>
        /// 根据当前帧生成“处理后的捕获图”：灰度 + Canny 边缘，用于调试预览。
        /// </summary>
        private static Mat CreateProcessedSourceImage(Mat frame, MatchOptions options)
        {
            var gray = new Mat();

            // 先统一转换为灰度
            if (frame.Channels() == 3)
            {
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            }
            else if (frame.Channels() == 4)
            {
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGRA2GRAY);
            }
            else if (frame.Channels() == 1)
            {
                frame.CopyTo(gray);
            }
            else
            {
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            }

            // 根据 MatchOptions 决定最终用于匹配的图像形态
            if (options.UseBinaryMatch)
            {
                var binary = new Mat();
                Cv2.Threshold(gray, binary, options.BinaryThreshold, 255, ThresholdTypes.Binary);
                gray.Dispose();
                return binary;
            }

            if (options.UseEdgeMatch)
            {
                var edges = new Mat();
                Cv2.Canny(gray, edges, options.CannyThreshold1, options.CannyThreshold2);
                gray.Dispose();
                return edges;
            }

            // 默认直接返回灰度图
            return gray;
        }

        /// <summary>
        /// 根据模板生成“处理后的匹配图”：灰度 + Canny 边缘（可选模板 ROI），用于调试预览。
        /// </summary>
        private static Mat CreateProcessedTemplateImage(Mat template, MatchOptions options)
        {
            Mat source = template;
            Mat? roiMat = null;

            // 如果指定了模板 ROI，则只处理模板中的该区域
            if (options.TemplateRegionOfInterest.HasValue &&
                options.TemplateRegionOfInterest.Value.Width > 0 &&
                options.TemplateRegionOfInterest.Value.Height > 0)
            {
                roiMat = new Mat(template, options.TemplateRegionOfInterest.Value);
                source = roiMat;
            }

            var gray = new Mat();

            if (source.Channels() == 3)
            {
                Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
            }
            else if (source.Channels() == 4)
            {
                Cv2.CvtColor(source, gray, ColorConversionCodes.BGRA2GRAY);
            }
            else if (source.Channels() == 1)
            {
                source.CopyTo(gray);
            }
            else
            {
                Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
            }

            roiMat?.Dispose();

            if (options.UseBinaryMatch)
            {
                var binary = new Mat();
                Cv2.Threshold(gray, binary, options.BinaryThreshold, 255, ThresholdTypes.Binary);
                gray.Dispose();
                return binary;
            }

            if (options.UseEdgeMatch)
            {
                var edges = new Mat();
                Cv2.Canny(gray, edges, options.CannyThreshold1, options.CannyThreshold2);
                gray.Dispose();
                return edges;
            }

            return gray;
        }
    }
}
