#region

using GameImpact.Abstractions.Recognition;
using GameImpact.Utilities.Logging;
using OpenCvSharp;

#endregion

namespace GameImpact.Recognition
{
    /// <summary>图像识别服务实现，提供模板匹配和颜色匹配功能</summary>
    public class RecognitionService : IRecognitionService
    {
        /// <inheritdoc/>
        public TemplateMatchResult MatchTemplate(Mat source, Mat template, MatchOptions? options = null)
        {
            options ??= new MatchOptions();
            var roi = options.RegionOfInterest;
            var templateRoi = options.TemplateRegionOfInterest;

            Log.Debug("[Recognition] MatchTemplate: source={W}x{H}, template={TW}x{TH}, threshold={T}, templateROI={TR}",
                    source.Width, source.Height, template.Width, template.Height, options.Threshold,
                    templateRoi.HasValue ? $"{templateRoi.Value.X},{templateRoi.Value.Y},{templateRoi.Value.Width}x{templateRoi.Value.Height}" : "none");

            using var searchArea = roi.HasValue && roi.Value.Width > 0 && roi.Value.Height > 0 ? new Mat(source, roi.Value) : source;
            
            // 如果指定了模板ROI，只使用模板的特定区域进行匹配（例如只匹配左侧的F框，忽略右侧文字）
            // 注意：当 templateRoi 不存在时，直接使用 template，不要用 using var，避免释放原始 template
            Mat templateArea;
            Mat? templateAreaToDispose = null;
            if (templateRoi.HasValue && templateRoi.Value.Width > 0 && templateRoi.Value.Height > 0)
            {
                templateAreaToDispose = new Mat(template, templateRoi.Value);
                templateArea = templateAreaToDispose;
            }
            else
            {
                templateArea = template; // 直接引用，不释放
            }

            try
            {
                // 在创建 templateArea 之前，先保存原始模板的尺寸（避免后续访问时 templateArea 已释放导致的问题）
                var templateWidth = template.Width;
                var templateHeight = template.Height;

                using var graySource = new Mat();
                using var grayTemplate = new Mat();

                // 统一转换为灰度图，确保深度和类型一致
                if (searchArea.Channels() == 3)
            {
                Cv2.CvtColor(searchArea, graySource, ColorConversionCodes.BGR2GRAY);
            }
            else if (searchArea.Channels() == 4)
            {
                Cv2.CvtColor(searchArea, graySource, ColorConversionCodes.BGRA2GRAY);
            }
            else if (searchArea.Channels() == 1)
            {
                // 已经是灰度图，直接复制
                searchArea.CopyTo(graySource);
            }
            else
            {
                // 其他情况，尝试转换为灰度图
                Cv2.CvtColor(searchArea, graySource, ColorConversionCodes.BGR2GRAY);
            }

            if (templateArea.Channels() == 3)
            {
                Cv2.CvtColor(templateArea, grayTemplate, ColorConversionCodes.BGR2GRAY);
            }
            else if (templateArea.Channels() == 4)
            {
                Cv2.CvtColor(templateArea, grayTemplate, ColorConversionCodes.BGRA2GRAY);
            }
            else if (templateArea.Channels() == 1)
            {
                // 已经是灰度图，直接复制
                templateArea.CopyTo(grayTemplate);
            }
            else
            {
                // 其他情况，尝试转换为灰度图
                Cv2.CvtColor(templateArea, grayTemplate, ColorConversionCodes.BGR2GRAY);
            }

            // 确保两个图像有相同的深度和类型
            if (graySource.Depth() != grayTemplate.Depth())
            {
                // 如果深度不同，转换为相同的深度（通常转换为 CV_8U）
                if (graySource.Depth() != MatType.CV_8U)
                {
                    graySource.ConvertTo(graySource, MatType.CV_8U);
                }
                if (grayTemplate.Depth() != MatType.CV_8U)
                {
                    grayTemplate.ConvertTo(grayTemplate, MatType.CV_8U);
                }
            }

            Mat srcMatch = graySource, tplMatch = grayTemplate;
            bool needDisposeSrcMatch = false;
            bool needDisposeTplMatch = false;

            if (options.UseBinaryMatch)
            {
                srcMatch = new Mat();
                tplMatch = new Mat();
                needDisposeSrcMatch = true;
                needDisposeTplMatch = true;
                Cv2.Threshold(graySource, srcMatch, options.BinaryThreshold, 255, ThresholdTypes.Binary);
                Cv2.Threshold(grayTemplate, tplMatch, options.BinaryThreshold, 255, ThresholdTypes.Binary);
            }
            else if (options.UseEdgeMatch)
            {
                // 使用边缘检测匹配，忽略背景颜色和特效，只关注形状轮廓
                srcMatch = new Mat();
                tplMatch = new Mat();
                needDisposeSrcMatch = true;
                needDisposeTplMatch = true;
                Cv2.Canny(graySource, srcMatch, options.CannyThreshold1, options.CannyThreshold2);
                Cv2.Canny(grayTemplate, tplMatch, options.CannyThreshold1, options.CannyThreshold2);
            }

            using var result = new Mat();
            Cv2.MatchTemplate(srcMatch, tplMatch, result, options.MatchMode);

                Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

                    if (needDisposeSrcMatch)
                {
                    srcMatch.Dispose();
                }
                if (needDisposeTplMatch)
                {
                    tplMatch.Dispose();
                }

                var offsetX = roi?.X ?? 0;
                var offsetY = roi?.Y ?? 0;
                var location = new Point(maxLoc.X + offsetX, maxLoc.Y + offsetY);
                
                // 如果使用了模板ROI，匹配位置是相对于裁剪后的模板的左上角
                // 但返回的位置应该是原始模板的左上角位置
                // 由于我们只匹配了模板的一部分，返回的中心点应该基于原始模板的尺寸
                var center = new Point(location.X + templateWidth / 2, location.Y + templateHeight / 2);

                var success = maxVal >= options.Threshold;
                Log.Debug("[Recognition] MatchTemplate result: success={Success}, confidence={Conf:F3}, location=({X},{Y})",
                        success, maxVal, location.X, location.Y);

                return new TemplateMatchResult(
                        success,
                        location,
                        center,
                        new Size(templateWidth, templateHeight),
                        maxVal
                );
            }
            finally
            {
                // 只释放新创建的 templateArea，不释放原始 template
                templateAreaToDispose?.Dispose();
            }
        }

        /// <inheritdoc/>
        public List<TemplateMatchResult> MatchTemplateAll(Mat source, Mat template, MatchOptions? options = null)
        {
            options ??= new MatchOptions();
            var results = new List<TemplateMatchResult>();
            var roi = options.RegionOfInterest;

            Log.Debug("[Recognition] MatchTemplateAll: source={W}x{H}, template={TW}x{TH}",
                    source.Width, source.Height, template.Width, template.Height);

            using var searchArea = roi.HasValue && roi.Value.Width > 0 && roi.Value.Height > 0 ? new Mat(source, roi.Value) : source;

            using var graySource = new Mat();
            using var grayTemplate = new Mat();

            // 统一转换为灰度图，确保深度和类型一致
            if (searchArea.Channels() == 3)
            {
                Cv2.CvtColor(searchArea, graySource, ColorConversionCodes.BGR2GRAY);
            }
            else if (searchArea.Channels() == 4)
            {
                Cv2.CvtColor(searchArea, graySource, ColorConversionCodes.BGRA2GRAY);
            }
            else if (searchArea.Channels() == 1)
            {
                // 已经是灰度图，直接复制
                searchArea.CopyTo(graySource);
            }
            else
            {
                // 其他情况，尝试转换为灰度图
                Cv2.CvtColor(searchArea, graySource, ColorConversionCodes.BGR2GRAY);
            }

            if (template.Channels() == 3)
            {
                Cv2.CvtColor(template, grayTemplate, ColorConversionCodes.BGR2GRAY);
            }
            else if (template.Channels() == 4)
            {
                Cv2.CvtColor(template, grayTemplate, ColorConversionCodes.BGRA2GRAY);
            }
            else if (template.Channels() == 1)
            {
                // 已经是灰度图，直接复制
                template.CopyTo(grayTemplate);
            }
            else
            {
                // 其他情况，尝试转换为灰度图
                Cv2.CvtColor(template, grayTemplate, ColorConversionCodes.BGR2GRAY);
            }

            // 确保两个图像有相同的深度和类型
            if (graySource.Depth() != grayTemplate.Depth())
            {
                // 如果深度不同，转换为相同的深度（通常转换为 CV_8U）
                if (graySource.Depth() != MatType.CV_8U)
                {
                    graySource.ConvertTo(graySource, MatType.CV_8U);
                }
                if (grayTemplate.Depth() != MatType.CV_8U)
                {
                    grayTemplate.ConvertTo(grayTemplate, MatType.CV_8U);
                }
            }

            Mat srcMatch = graySource, tplMatch = grayTemplate;
            bool needDisposeSrcMatch = false;
            bool needDisposeTplMatch = false;

            if (options.UseBinaryMatch)
            {
                srcMatch = new Mat();
                tplMatch = new Mat();
                needDisposeSrcMatch = true;
                needDisposeTplMatch = true;
                Cv2.Threshold(graySource, srcMatch, options.BinaryThreshold, 255, ThresholdTypes.Binary);
                Cv2.Threshold(grayTemplate, tplMatch, options.BinaryThreshold, 255, ThresholdTypes.Binary);
            }
            else if (options.UseEdgeMatch)
            {
                // 使用边缘检测匹配，忽略背景颜色和特效，只关注形状轮廓
                srcMatch = new Mat();
                tplMatch = new Mat();
                needDisposeSrcMatch = true;
                needDisposeTplMatch = true;
                Cv2.Canny(graySource, srcMatch, options.CannyThreshold1, options.CannyThreshold2);
                Cv2.Canny(grayTemplate, tplMatch, options.CannyThreshold1, options.CannyThreshold2);
            }

            using var result = new Mat();
            Cv2.MatchTemplate(srcMatch, tplMatch, result, options.MatchMode);

            if (needDisposeSrcMatch)
            {
                srcMatch.Dispose();
            }
            if (needDisposeTplMatch)
            {
                tplMatch.Dispose();
            }

            var offsetX = roi?.X ?? 0;
            var offsetY = roi?.Y ?? 0;

            while (true)
            {
                Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);
                if (maxVal < options.Threshold)
                {
                    break;
                }

                var location = new Point(maxLoc.X + offsetX, maxLoc.Y + offsetY);
                var center = new Point(location.X + template.Width / 2, location.Y + template.Height / 2);

                results.Add(new TemplateMatchResult(true, location, center, new Size(template.Width, template.Height), maxVal));

                Cv2.FloodFill(result, maxLoc, new Scalar(0));
            }

            Log.Debug("[Recognition] MatchTemplateAll found {Count} matches", results.Count);
            return results;
        }

        /// <inheritdoc/>
        public ColorMatchResult MatchColor(Mat image, ColorRange range, Rect? roi = null)
        {
            Log.Debug("[Recognition] MatchColor: image={W}x{H}", image.Width, image.Height);

            using var searchArea = roi.HasValue && roi.Value.Width > 0 && roi.Value.Height > 0 ? new Mat(image, roi.Value) : image;

            using var mask = new Mat();
            Cv2.InRange(searchArea, range.Lower, range.Upper, mask);

            var points = new List<Point>();
            for (var y = 0; y < mask.Rows; y++)
            {
                for (var x = 0; x < mask.Cols; x++)
                {
                    if (mask.At<byte>(y, x) > 0)
                    {
                        var offsetX = roi?.X ?? 0;
                        var offsetY = roi?.Y ?? 0;
                        points.Add(new Point(x + offsetX, y + offsetY));
                    }
                }
            }

            Log.Debug("[Recognition] MatchColor found {Count} points", points.Count);
            return new ColorMatchResult(points.Count > 0, points.Count, points);
        }
    }
}
