using GameImpact.Abstractions.Recognition;
using GameImpact.Utilities.Logging;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;

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

    /// <summary>模板匹配与区域截取，不依赖 UI。</summary>
    public interface ITemplateMatchService
    {
        /// <summary>用当前帧与模板匹配，可选提取文字区域 OCR。返回结果及 Overlay 绘制数据。</summary>
        TemplateMatchResult MatchWithTemplateAndText(string? fileName, Rect? textRegion = null);

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

                using var template = Cv2.ImRead(path);
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
                    var result = m_context.Recognition.MatchTemplate(frame, template, matchOptions);
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
    }
}
