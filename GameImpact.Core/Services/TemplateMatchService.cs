#region

using GameImpact.Abstractions.Recognition;
using GameImpact.Utilities.Images;
using GameImpact.Utilities.Logging;
using GameImpact.Utilities.Timing;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;

#endregion

namespace GameImpact.Core.Services
{
    /// <summary>匹配设置接口（用于从设置中读取匹配算法配置）</summary>
    public interface IMatchSettings
    {
        MatchAlgorithm MatchAlgorithms{ get; }
        MatchCombineMode MatchCombineMode{ get; }
        double RecognitionConfidenceThreshold{ get; }
    }

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
        private readonly IMatchSettings? m_matchSettings;
        private readonly ITemplateService m_templates;

        public TemplateMatchService(GameContext context, ITemplateService templates, IMatchSettings? matchSettings = null)
        {
            m_context = context;
            m_templates = templates;
            m_matchSettings = matchSettings;
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
                    // 从设置中读取匹配算法配置，如果没有设置则使用默认值
                    var matchAlgorithms = m_matchSettings?.MatchAlgorithms ?? MatchAlgorithm.NCC;
                    var combineMode = m_matchSettings?.MatchCombineMode ?? MatchCombineMode.Average;
                    var threshold = m_matchSettings?.RecognitionConfidenceThreshold ?? 0.6;

                    var matchOptions = new MatchOptions(
                            threshold,
                            TemplateRegionOfInterest: matchRoi,
                            MatchAlgorithms: matchAlgorithms,
                            CombineMode: combineMode);
                    // 克隆 frame 以避免 MatchTemplate 内部可能释放原始 frame 的问题
                    using var frameClone = frame.Clone();

                    // 计时：模板匹配耗时
                    var (result, elapsed) = PerfTimer.Measure(() => m_context.Recognition.MatchTemplate(frameClone, template, matchOptions));
                    Log.DebugScreen(
                            "[识别] 模板匹配耗时: {Elapsed} ms (算法: {Algorithms}, 阈值: {Threshold:F2})",
                            elapsed.TotalMilliseconds,
                            matchAlgorithms,
                            threshold);
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
                // 从设置中读取匹配算法配置，如果没有设置则使用默认值
                var matchAlgorithms = m_matchSettings?.MatchAlgorithms ?? MatchAlgorithm.NCC;
                var combineMode = m_matchSettings?.MatchCombineMode ?? MatchCombineMode.Average;
                var threshold = m_matchSettings?.RecognitionConfidenceThreshold ?? 0.6;

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
                            threshold,
                            TemplateRegionOfInterest: matchRoi,
                            MatchAlgorithms: matchAlgorithms,
                            CombineMode: combineMode);

                    // 克隆 frame 以避免 MatchTemplate 内部可能释放原始 frame 的问题
                    using var frameClone = frame.Clone();

                    // 计时：模板匹配耗时
                    var (result, elapsed) = PerfTimer.Measure(() => m_context.Recognition.MatchTemplate(frameClone, template, matchOptions));
                    Log.DebugScreen(
                            "[识别] 模板匹配耗时: {Elapsed} ms (算法: {Algorithms}, 阈值: {Threshold:F2})",
                            elapsed.TotalMilliseconds,
                            matchAlgorithms,
                            threshold);

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

                    // 直接使用 MatchTemplate 返回的处理后图像，避免重复处理
                    processedCaptureImage = result.ProcessedSourceImage;
                    processedMatchImage = result.ProcessedTemplateImage;

                    if (processedCaptureImage != null)
                    {
                        Log.DebugScreen("[识别] 处理后的捕获图创建成功: {Width}x{Height}", processedCaptureImage.Width, processedCaptureImage.Height);
                    }
                    else
                    {
                        Log.DebugScreen("[识别] 处理后的捕获图为 null", Array.Empty<object>());
                    }

                    if (processedMatchImage != null)
                    {
                        Log.DebugScreen("[识别] 处理后的匹配图创建成功: {Width}x{Height}", processedMatchImage.Width, processedMatchImage.Height);
                    }
                    else
                    {
                        Log.DebugScreen("[识别] 处理后的匹配图为 null", Array.Empty<object>());
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

        /// <summary>根据当前帧生成"处理后的捕获图"：根据匹配算法处理图像，用于调试预览。</summary>
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

            // 根据 MatchAlgorithms 决定最终用于匹配的图像形态（优先使用第一个算法）
            var algorithms = options.MatchAlgorithms == MatchAlgorithm.None ? MatchAlgorithm.NCC : options.MatchAlgorithms;

            if ((algorithms & MatchAlgorithm.Edge) == MatchAlgorithm.Edge)
            {
                var edges = new Mat();
                Cv2.Canny(gray, edges, options.CannyThreshold1, options.CannyThreshold2);
                gray.Dispose();
                return edges;
            }

            // 默认直接返回灰度图
            return gray;
        }

        /// <summary>根据模板生成"处理后的匹配图"：根据匹配算法处理图像（可选模板 ROI），用于调试预览。</summary>
        private static Mat CreateProcessedTemplateImage(Mat template, MatchOptions options)
        {
            var source = template;
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

            // 根据 MatchAlgorithms 决定最终用于匹配的图像形态（优先使用第一个算法）
            var algorithms = options.MatchAlgorithms == MatchAlgorithm.None ? MatchAlgorithm.NCC : options.MatchAlgorithms;

            if ((algorithms & MatchAlgorithm.Edge) == MatchAlgorithm.Edge)
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
