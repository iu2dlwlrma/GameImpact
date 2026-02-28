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

            Log.Debug("[Recognition] MatchTemplate: source={W}x{H}, template={TW}x{TH}, threshold={T}",
                    source.Width, source.Height, template.Width, template.Height, options.Threshold);

            using var searchArea = roi.HasValue && roi.Value.Width > 0 && roi.Value.Height > 0 ? new Mat(source, roi.Value) : source;

            using var graySource = new Mat();
            using var grayTemplate = new Mat();

            if (searchArea.Channels() == 3)
            {
                Cv2.CvtColor(searchArea, graySource, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                searchArea.CopyTo(graySource);
            }

            if (template.Channels() == 3)
            {
                Cv2.CvtColor(template, grayTemplate, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                template.CopyTo(grayTemplate);
            }

            Mat srcMatch = graySource, tplMatch = grayTemplate;

            if (options.UseBinaryMatch)
            {
                srcMatch = new Mat();
                tplMatch = new Mat();
                Cv2.Threshold(graySource, srcMatch, options.BinaryThreshold, 255, ThresholdTypes.Binary);
                Cv2.Threshold(grayTemplate, tplMatch, options.BinaryThreshold, 255, ThresholdTypes.Binary);
            }

            using var result = new Mat();
            Cv2.MatchTemplate(srcMatch, tplMatch, result, options.MatchMode);

            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

            if (options.UseBinaryMatch)
            {
                srcMatch.Dispose();
                tplMatch.Dispose();
            }

            var offsetX = roi?.X ?? 0;
            var offsetY = roi?.Y ?? 0;
            var location = new Point(maxLoc.X + offsetX, maxLoc.Y + offsetY);
            var center = new Point(location.X + template.Width / 2, location.Y + template.Height / 2);

            var success = maxVal >= options.Threshold;
            Log.Debug("[Recognition] MatchTemplate result: success={Success}, confidence={Conf:F3}, location=({X},{Y})",
                    success, maxVal, location.X, location.Y);

            return new TemplateMatchResult(
                    success,
                    location,
                    center,
                    new Size(template.Width, template.Height),
                    maxVal
            );
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

            if (searchArea.Channels() == 3)
            {
                Cv2.CvtColor(searchArea, graySource, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                searchArea.CopyTo(graySource);
            }

            if (template.Channels() == 3)
            {
                Cv2.CvtColor(template, grayTemplate, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                template.CopyTo(grayTemplate);
            }

            using var result = new Mat();
            Cv2.MatchTemplate(graySource, grayTemplate, result, options.MatchMode);

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
