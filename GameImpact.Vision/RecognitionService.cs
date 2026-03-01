#region

using System;
using System.Collections.Generic;
using System.Linq;
using GameImpact.Abstractions.Recognition;
using GameImpact.Utilities.Logging;
using GameImpact.Utilities.Timing;
using OpenCvSharp;

#endregion

namespace GameImpact.Vision
{
    /// <summary>图像识别服务实现，提供模板匹配和颜色匹配功能</summary>
    public class RecognitionService : IRecognitionService
    {
        /// <inheritdoc/>
        public TemplateMatchResult MatchTemplate(Mat source, Mat template, MatchOptions? options = null)
        {
            options ??= new MatchOptions();

            // 获取要使用的算法列表
            var algorithms = new List<MatchAlgorithm>();
            foreach (MatchAlgorithm algo in Enum.GetValues(typeof(MatchAlgorithm)))
            {
                if ((options.MatchAlgorithms & algo) == algo && algo != 0)
                {
                    algorithms.Add(algo);
                }
            }

            if (algorithms.Count == 0)
            {
                algorithms.Add(MatchAlgorithm.NCC); // 默认使用标准相关性匹配
            }

            Log.Debug("[Recognition] MatchTemplate: source={W}x{H}, template={TW}x{TH}, threshold={T}, algorithms=[{Algos}]",
                    source.Width, source.Height, template.Width, template.Height, options.Threshold,
                    string.Join(", ", algorithms));

            // 执行多个算法的匹配，并记录每个算法的耗时
            var results = new List<(MatchAlgorithm algo, TemplateMatchResult result)>();
            foreach (var algo in algorithms)
            {
                try
                {
                    var (result, elapsed) = PerfTimer.Measure(() => MatchTemplateWithAlgorithm(source, template, options, algo));
                    results.Add((algo, result));
                    Log.Debug("[Recognition] Algorithm {Algo} result: success={Success}, confidence={Conf:F3}, elapsed={Elapsed} ms",
                            algo, result.Success, result.Confidence, elapsed.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    Log.Debug("[Recognition] Algorithm {Algo} failed: {Error}", algo, ex.Message);
                }
            }

            if (results.Count == 0)
            {
                return new TemplateMatchResult(false, new Point(0, 0), new Point(0, 0), new Size(template.Width, template.Height), 0, null, null);
            }

            // 根据 CombineMode 综合结果
            return CombineMatchResults(results, options, template.Width, template.Height);
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

            // MatchTemplateAll 使用 NCC 匹配（如果需要其他算法，应该使用 MatchTemplate）
            Mat srcMatch = graySource, tplMatch = grayTemplate;
            var needDisposeSrcMatch = false;
            var needDisposeTplMatch = false;

            // 检查是否使用了 Edge 算法
            if ((options.MatchAlgorithms & MatchAlgorithm.Edge) == MatchAlgorithm.Edge)
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
            Cv2.MatchTemplate(srcMatch, tplMatch, result, TemplateMatchModes.CCoeffNormed);

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

                results.Add(new TemplateMatchResult(true, location, center, new Size(template.Width, template.Height), maxVal, null, null));

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

        /// <summary>使用指定算法执行模板匹配</summary>
        private TemplateMatchResult MatchTemplateWithAlgorithm(Mat source, Mat template, MatchOptions options, MatchAlgorithm algorithm)
        {
            var roi = options.RegionOfInterest;
            var templateRoi = options.TemplateRegionOfInterest;

            // 注意：不能直接 using source 作为 searchArea，否则会在无 ROI 时意外释放调用方传入的 Mat
            Mat searchArea;
            var needDisposeSearchArea = false;
            if (roi.HasValue && roi.Value.Width > 0 && roi.Value.Height > 0)
            {
                searchArea = new Mat(source, roi.Value);
                needDisposeSearchArea = true;
            }
            else
            {
                // 无 ROI 时直接使用源 Mat，但不负责释放
                searchArea = source;
            }

            Mat templateArea;
            Mat? templateAreaToDispose = null;
            if (templateRoi.HasValue && templateRoi.Value.Width > 0 && templateRoi.Value.Height > 0)
            {
                templateAreaToDispose = new Mat(template, templateRoi.Value);
                templateArea = templateAreaToDispose;
            }
            else
            {
                templateArea = template;
            }

            try
            {
                var templateWidth = template.Width;
                var templateHeight = template.Height;

                return algorithm switch
                {
                        MatchAlgorithm.NCC => MatchWithNCC(searchArea, templateArea, options, roi, templateWidth, templateHeight),
                        MatchAlgorithm.Edge => MatchWithEdge(searchArea, templateArea, options, roi, templateWidth, templateHeight),
                        MatchAlgorithm.FeaturePoints => MatchWithFeaturePoints(searchArea, templateArea, options, roi, templateWidth, templateHeight),
                        MatchAlgorithm.PHash => MatchWithPHash(searchArea, templateArea, options, roi, templateWidth, templateHeight),
                        MatchAlgorithm.Masked => MatchWithMasked(searchArea, templateArea, options, roi, templateWidth, templateHeight),
                        _ => MatchWithNCC(searchArea, templateArea, options, roi, templateWidth, templateHeight) // 默认使用NCC
                };
            }
            finally
            {
                if (needDisposeSearchArea)
                {
                    searchArea.Dispose();
                }
                templateAreaToDispose?.Dispose();
            }
        }

        /// <summary>标准相关性匹配（NCC）- 最推荐，定位首选</summary>
        private TemplateMatchResult MatchWithNCC(Mat source, Mat template, MatchOptions options, Rect? roi, int templateWidth, int templateHeight)
        {
            try
            {
                using var graySrc = new Mat();
                using var grayTpl = new Mat();
                ConvertToGrayScale(source, graySrc);
                ConvertToGrayScale(template, grayTpl);
                EnsureSameDepth(graySrc, grayTpl);

                using var result = new Mat();
                Cv2.MatchTemplate(graySrc, grayTpl, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

                var offsetX = roi?.X ?? 0;
                var offsetY = roi?.Y ?? 0;
                var location = new Point(maxLoc.X + offsetX, maxLoc.Y + offsetY);
                var center = new Point(location.X + templateWidth / 2, location.Y + templateHeight / 2);

                var processedSource = graySrc.Clone();
                var processedTemplate = grayTpl.Clone();

                return new TemplateMatchResult(
                        maxVal >= options.Threshold,
                        location,
                        center,
                        new Size(templateWidth, templateHeight),
                        maxVal,
                        processedSource,
                        processedTemplate
                );
            }
            catch
            {
                return new TemplateMatchResult(false, new Point(0, 0), new Point(0, 0), new Size(templateWidth, templateHeight), 0, null, null);
            }
        }

        /// <summary>边缘特征匹配（Edge）- 适合背景透明、动态变化的UI</summary>
        private TemplateMatchResult MatchWithEdge(Mat source, Mat template, MatchOptions options, Rect? roi, int templateWidth, int templateHeight)
        {
            try
            {
                using var graySrc = new Mat();
                using var grayTpl = new Mat();
                ConvertToGrayScale(source, graySrc);
                ConvertToGrayScale(template, grayTpl);
                EnsureSameDepth(graySrc, grayTpl);

                using var edgeSrc = new Mat();
                using var edgeTpl = new Mat();
                Cv2.Canny(graySrc, edgeSrc, options.CannyThreshold1, options.CannyThreshold2);
                Cv2.Canny(grayTpl, edgeTpl, options.CannyThreshold1, options.CannyThreshold2);

                using var result = new Mat();
                Cv2.MatchTemplate(edgeSrc, edgeTpl, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

                var offsetX = roi?.X ?? 0;
                var offsetY = roi?.Y ?? 0;
                var location = new Point(maxLoc.X + offsetX, maxLoc.Y + offsetY);
                var center = new Point(location.X + templateWidth / 2, location.Y + templateHeight / 2);

                var processedSource = edgeSrc.Clone();
                var processedTemplate = edgeTpl.Clone();

                return new TemplateMatchResult(
                        maxVal >= options.Threshold,
                        location,
                        center,
                        new Size(templateWidth, templateHeight),
                        maxVal,
                        processedSource,
                        processedTemplate
                );
            }
            catch
            {
                return new TemplateMatchResult(false, new Point(0, 0), new Point(0, 0), new Size(templateWidth, templateHeight), 0, null, null);
            }
        }

        /// <summary>转换为灰度图</summary>
        private void ConvertToGrayScale(Mat input, Mat output)
        {
            if (input.Channels() == 3)
            {
                Cv2.CvtColor(input, output, ColorConversionCodes.BGR2GRAY);
            }
            else if (input.Channels() == 4)
            {
                Cv2.CvtColor(input, output, ColorConversionCodes.BGRA2GRAY);
            }
            else if (input.Channels() == 1)
            {
                input.CopyTo(output);
            }
            else
            {
                // 其他情况尽量转换为灰度
                Cv2.CvtColor(input, output, ColorConversionCodes.BGR2GRAY);
            }
        }

        /// <summary>转换为 3 通道 BGR 图像（用于颜色匹配）</summary>
        private void ConvertToBgr(Mat input, Mat output)
        {
            if (input.Channels() == 3)
            {
                // 已经是 BGR，直接复制
                input.CopyTo(output);
            }
            else if (input.Channels() == 4)
            {
                Cv2.CvtColor(input, output, ColorConversionCodes.BGRA2BGR);
            }
            else if (input.Channels() == 1)
            {
                Cv2.CvtColor(input, output, ColorConversionCodes.GRAY2BGR);
            }
            else
            {
                // 非常规通道数，尽量直接复制，避免抛异常
                input.CopyTo(output);
            }
        }

        /// <summary>确保两个图像有相同的深度</summary>
        private void EnsureSameDepth(Mat img1, Mat img2)
        {
            if (img1.Depth() != img2.Depth())
            {
                if (img1.Depth() != MatType.CV_8U)
                {
                    img1.ConvertTo(img1, MatType.CV_8U);
                }
                if (img2.Depth() != MatType.CV_8U)
                {
                    img2.ConvertTo(img2, MatType.CV_8U);
                }
            }
        }

        /// <summary>使用特征点匹配</summary>
        private TemplateMatchResult MatchWithFeaturePoints(Mat source, Mat template, MatchOptions options, Rect? roi, int templateWidth, int templateHeight)
        {
            try
            {
                using var graySrc = new Mat();
                using var grayTpl = new Mat();
                ConvertToGrayScale(source, graySrc);
                ConvertToGrayScale(template, grayTpl);
                EnsureSameDepth(graySrc, grayTpl);

                // 使用ORB检测器
                using var orb = ORB.Create(500);
                var keypointsSrc = new KeyPoint[] { };
                var keypointsTpl = new KeyPoint[] { };
                using var descriptorsSrc = new Mat();
                using var descriptorsTpl = new Mat();

                orb.DetectAndCompute(graySrc, null, out keypointsSrc, descriptorsSrc);
                orb.DetectAndCompute(grayTpl, null, out keypointsTpl, descriptorsTpl);

                Log.Debug("[Recognition] FeaturePoints: src keypoints={SrcKP}, tpl keypoints={TplKP}", 
                        keypointsSrc.Length, keypointsTpl.Length);

                if (descriptorsSrc.Rows == 0 || descriptorsTpl.Rows == 0 || keypointsSrc.Length == 0 || keypointsTpl.Length == 0)
                {
                    Log.Debug("[Recognition] FeaturePoints: No descriptors or keypoints detected");
                    return new TemplateMatchResult(false, new Point(0, 0), new Point(0, 0), new Size(templateWidth, templateHeight), 0, null, null);
                }

                // 使用BFMatcher进行双向匹配
                using var matcher = new BFMatcher();
                
                // 正向匹配：模板 -> 源图像
                var matches1 = matcher.Match(descriptorsTpl, descriptorsSrc);
                
                // 反向匹配：源图像 -> 模板
                var matches2 = matcher.Match(descriptorsSrc, descriptorsTpl);

                // 计算距离统计信息，用于自适应阈值
                var distances1 = matches1.Select(m => m.Distance).OrderBy(d => d).ToArray();
                var distances2 = matches2.Select(m => m.Distance).OrderBy(d => d).ToArray();
                
                // 使用中位数作为距离阈值的基础
                var medianDist1 = distances1.Length > 0 ? distances1[distances1.Length / 2] : 100;
                var medianDist2 = distances2.Length > 0 ? distances2[distances2.Length / 2] : 100;
                
                // 自适应阈值：使用中位数的1.5倍
                var distanceThreshold = Math.Min(medianDist1, medianDist2) * 1.5;
                
                Log.Debug("[Recognition] FeaturePoints: median distance={MedDist:F2}, threshold={Thresh:F2}", 
                        Math.Min(medianDist1, medianDist2), distanceThreshold);

                // 使用交叉验证：只保留双向匹配都存在的点
                var goodMatches = new List<DMatch>();
                foreach (var match1 in matches1)
                {
                    // 检查反向匹配中是否有对应的匹配
                    var reverseMatch = matches2.FirstOrDefault(m => m.TrainIdx == match1.QueryIdx && m.QueryIdx == match1.TrainIdx);
                    if (reverseMatch.Distance > 0) // 找到了反向匹配
                    {
                        // 使用两个匹配的平均距离
                        var avgDistance = (match1.Distance + reverseMatch.Distance) / 2.0;
                        if (avgDistance < distanceThreshold)
                        {
                            goodMatches.Add(match1);
                        }
                    }
                    else if (match1.Distance < distanceThreshold * 0.8) // 如果没有反向匹配，使用更严格的阈值
                    {
                        goodMatches.Add(match1);
                    }
                }

                // 如果匹配点仍然太少，使用更宽松的策略：只使用正向匹配
                if (goodMatches.Count < 4)
                {
                    Log.Debug("[Recognition] FeaturePoints: Cross-validation too strict, using forward matches only");
                    goodMatches.Clear();
                    var relaxedThreshold = distanceThreshold * 1.5;
                    foreach (var match in matches1)
                    {
                        if (match.Distance < relaxedThreshold)
                        {
                            goodMatches.Add(match);
                        }
                    }
                }

                var goodMatchesCount = goodMatches.Count;
                Log.Debug("[Recognition] FeaturePoints: total forward matches={Total}, good matches={Good}",
                        matches1.Length, goodMatchesCount);

                if (goodMatchesCount < 4)
                {
                    Log.Debug("[Recognition] FeaturePoints: Not enough good matches (need >=4, got {Count})", goodMatchesCount);
                    return new TemplateMatchResult(false, new Point(0, 0), new Point(0, 0), new Size(templateWidth, templateHeight), 0, null, null);
                }

                // 提取匹配点的坐标
                var srcPoints = new List<Point2f>();
                var tplPoints = new List<Point2f>();
                foreach (var match in goodMatches)
                {
                    srcPoints.Add(keypointsSrc[match.TrainIdx].Pt);
                    tplPoints.Add(keypointsTpl[match.QueryIdx].Pt);
                }

                // 使用RANSAC计算单应性矩阵
                var tplPointsArray = tplPoints.ToArray();
                var srcPointsArray = srcPoints.ToArray();
                using var tplPointsMat = InputArray.Create(tplPointsArray);
                using var srcPointsMat = InputArray.Create(srcPointsArray);
                
                var homography = Cv2.FindHomography(
                        tplPointsMat, 
                        srcPointsMat, 
                        HomographyMethods.Ransac, 
                        5.0);

                Point location;
                Point center;
                double confidence;
                Mat processedSource;
                Mat processedTemplate;

                if (homography == null || homography.Empty())
                {
                    Log.Debug("[Recognition] FeaturePoints: Failed to compute homography, using average offset");
                    // 如果单应性计算失败，使用平均偏移作为后备方案
                    var avgX = goodMatches.Select(m => (double)(keypointsSrc[m.TrainIdx].Pt.X - keypointsTpl[m.QueryIdx].Pt.X)).Average();
                    var avgY = goodMatches.Select(m => (double)(keypointsSrc[m.TrainIdx].Pt.Y - keypointsTpl[m.QueryIdx].Pt.Y)).Average();
                    var fallbackOffsetX = roi?.X ?? 0;
                    var fallbackOffsetY = roi?.Y ?? 0;
                    location = new Point((int)(avgX + fallbackOffsetX), (int)(avgY + fallbackOffsetY));
                    center = new Point(location.X + templateWidth / 2, location.Y + templateHeight / 2);
                    
                    // 置信度基于匹配点数量和距离
                    var avgDistance = goodMatches.Average(m => m.Distance);
                    var maxDistance = goodMatches.Max(m => m.Distance);
                    // 归一化置信度：距离越小、匹配点越多，置信度越高
                    var distanceConfidence = Math.Max(0, 1.0 - (avgDistance / Math.Max(maxDistance, 1.0)));
                    var countConfidence = Math.Min(1.0, (double)goodMatchesCount / Math.Max(keypointsTpl.Length, 10));
                    confidence = (distanceConfidence * 0.5 + countConfidence * 0.5);

                    processedSource = graySrc.Clone();
                    processedTemplate = grayTpl.Clone();
                }
                else
                {
                    // 使用单应性矩阵计算模板四个角点在源图像中的位置
                    var templateCorners = new Point2f[]
                    {
                            new Point2f(0, 0),
                            new Point2f(templateWidth, 0),
                            new Point2f(templateWidth, templateHeight),
                            new Point2f(0, templateHeight)
                    };

                    using var cornersMat = InputArray.Create(templateCorners);
                    using var transformedCornersMat = new Mat();
                    Cv2.PerspectiveTransform(cornersMat, transformedCornersMat, homography);
                    
                    // 从 Mat 中提取 Point2f 数组
                    var transformedCorners = new Point2f[4];
                    var indexer = transformedCornersMat.GetGenericIndexer<Vec2f>();
                    for (int i = 0; i < 4; i++)
                    {
                        var vec = indexer[i];
                        transformedCorners[i] = new Point2f(vec.Item0, vec.Item1);
                    }

                    // 计算边界框
                    var minX = transformedCorners.Min(p => p.X);
                    var minY = transformedCorners.Min(p => p.Y);
                    var maxX = transformedCorners.Max(p => p.X);
                    var maxY = transformedCorners.Max(p => p.Y);

                    var resultOffsetX = roi?.X ?? 0;
                    var resultOffsetY = roi?.Y ?? 0;
                    location = new Point((int)(minX + resultOffsetX), (int)(minY + resultOffsetY));
                    center = new Point((int)((minX + maxX) / 2 + resultOffsetX), (int)((minY + maxY) / 2 + resultOffsetY));

                    // 计算置信度：基于匹配点数量、距离和内点比例
                    var avgDistance = goodMatches.Average(m => m.Distance);
                    var maxDistance = goodMatches.Max(m => m.Distance);
                    var distanceConfidence = Math.Max(0, 1.0 - (avgDistance / Math.Max(maxDistance, 1.0)));
                    var countConfidence = Math.Min(1.0, (double)goodMatchesCount / Math.Max(keypointsTpl.Length, 10));
                    confidence = (distanceConfidence * 0.4 + countConfidence * 0.6);

                    processedSource = graySrc.Clone();
                    processedTemplate = grayTpl.Clone();
                }

                Log.Debug("[Recognition] FeaturePoints: confidence={Conf:F3}, location=({X},{Y}), good matches={Good}", 
                        confidence, location.X, location.Y, goodMatchesCount);

                return new TemplateMatchResult(
                        confidence >= options.Threshold,
                        location,
                        center,
                        new Size(templateWidth, templateHeight),
                        confidence,
                        processedSource,
                        processedTemplate
                );
            }
            catch (Exception ex)
            {
                Log.Debug("[Recognition] FeaturePoints failed: {Error}", ex.Message);
                return new TemplateMatchResult(false, new Point(0, 0), new Point(0, 0), new Size(templateWidth, templateHeight), 0, null, null);
            }
        }

        /// <summary>感知哈希验证（PHash）- 适合快速验证两张图是否相似，不用于定位</summary>
        private TemplateMatchResult MatchWithPHash(Mat source, Mat template, MatchOptions options, Rect? roi, int templateWidth, int templateHeight)
        {
            try
            {
                // PHash 不用于定位，只验证相似度
                // 使用全图进行哈希计算
                using var graySrc = new Mat();
                using var grayTpl = new Mat();
                ConvertToGrayScale(source, graySrc);
                ConvertToGrayScale(template, grayTpl);

                // 计算感知哈希
                var hashSrc = ComputePHash(graySrc);
                var hashTpl = ComputePHash(grayTpl);

                // 计算汉明距离
                var hammingDistance = HammingDistance(hashSrc, hashTpl);
                // 转换为相似度（0-1），距离越小相似度越高
                var confidence = 1.0 - (hammingDistance / 64.0); // 64位哈希，最大距离64

                // PHash 不提供位置信息，返回ROI中心
                var offsetX = roi?.X ?? 0;
                var offsetY = roi?.Y ?? 0;
                var location = new Point(offsetX, offsetY);
                var center = new Point(location.X + templateWidth / 2, location.Y + templateHeight / 2);

                var processedSource = graySrc.Clone();
                var processedTemplate = grayTpl.Clone();

                return new TemplateMatchResult(
                        confidence >= options.Threshold,
                        location,
                        center,
                        new Size(templateWidth, templateHeight),
                        confidence,
                        processedSource,
                        processedTemplate
                );
            }
            catch
            {
                return new TemplateMatchResult(false, new Point(0, 0), new Point(0, 0), new Size(templateWidth, templateHeight), 0, null, null);
            }
        }

        /// <summary>
        /// 带掩模匹配（Masked）
        /// - 使用 3 通道 BGR 颜色图进行匹配
        /// - 结合模板掩模，同时利用形状 + 颜色信息，特别适合不规则彩色按钮
        /// </summary>
        private TemplateMatchResult MatchWithMasked(Mat source, Mat template, MatchOptions options, Rect? roi, int templateWidth, int templateHeight)
        {
            try
            {
                // 使用颜色图匹配，而不是灰度图，这样可以同时利用颜色信息
                using var colorSrc = new Mat();
                using var colorTpl = new Mat();
                ConvertToBgr(source, colorSrc);
                ConvertToBgr(template, colorTpl);
                EnsureSameDepth(colorSrc, colorTpl);

                Mat? mask = options.TemplateMask;
                if (mask == null || mask.Empty())
                {
                    // 如果没有提供掩模，从模板的alpha通道创建掩模
                    if (template.Channels() == 4)
                    {
                        var channels = Cv2.Split(template);
                        mask = channels[3]; // Alpha通道
                        channels[0].Dispose();
                        channels[1].Dispose();
                        channels[2].Dispose();
                    }
                    else
                    {
                        // 如果没有alpha通道，创建全白掩模
                        mask = new Mat(template.Size(), MatType.CV_8UC1, Scalar.All(255));
                    }
                }

                using var result = new Mat();
                Cv2.MatchTemplate(colorSrc, colorTpl, result, TemplateMatchModes.CCoeffNormed, mask);
                Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

                var offsetX = roi?.X ?? 0;
                var offsetY = roi?.Y ?? 0;
                var location = new Point(maxLoc.X + offsetX, maxLoc.Y + offsetY);
                var center = new Point(location.X + templateWidth / 2, location.Y + templateHeight / 2);

                var processedSource = colorSrc.Clone();
                var processedTemplate = colorTpl.Clone();

                // 清理临时掩模
                if (mask != options.TemplateMask && mask != null)
                {
                    mask.Dispose();
                }

                return new TemplateMatchResult(
                        maxVal >= options.Threshold,
                        location,
                        center,
                        new Size(templateWidth, templateHeight),
                        maxVal,
                        processedSource,
                        processedTemplate
                );
            }
            catch
            {
                return new TemplateMatchResult(false, new Point(0, 0), new Point(0, 0), new Size(templateWidth, templateHeight), 0, null, null);
            }
        }

        /// <summary>计算感知哈希（64位）</summary>
        private ulong ComputePHash(Mat image)
        {
            // 缩放为8x8
            using var resized = new Mat();
            Cv2.Resize(image, resized, new Size(8, 8));

            // 计算DCT
            using var floatMat = new Mat();
            resized.ConvertTo(floatMat, MatType.CV_32F);
            using var dct = new Mat();
            Cv2.Dct(floatMat, dct);

            // 取左上角8x8的DCT系数
            using var dctLow = new Mat(dct, new Rect(0, 0, 8, 8));

            // 计算中位数
            var median = Cv2.Mean(dctLow).Val0;

            // 生成哈希
            ulong hash = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var val = dctLow.At<float>(y, x);
                    if (val > median)
                    {
                        hash |= (1UL << (y * 8 + x));
                    }
                }
            }

            return hash;
        }

        /// <summary>计算汉明距离</summary>
        private int HammingDistance(ulong hash1, ulong hash2)
        {
            var xor = hash1 ^ hash2;
            int distance = 0;
            while (xor != 0)
            {
                distance++;
                xor &= xor - 1; // 清除最低位的1
            }
            return distance;
        }

        /// <summary>综合多个匹配结果</summary>
        private TemplateMatchResult CombineMatchResults(List<(MatchAlgorithm algo, TemplateMatchResult result)> results, MatchOptions options, int templateWidth, int templateHeight)
        {
            if (results.Count == 1)
            {
                return results[0].result;
            }

            var weights = options.AlgorithmWeights ?? new Dictionary<MatchAlgorithm, double>();
            var validResults = results.Where(r => r.result.Success).ToList();

            if (validResults.Count == 0)
            {
                // 如果没有成功的匹配，返回置信度最高的结果
                var bestResult = results.OrderByDescending(r => r.result.Confidence).First();
                return bestResult.result;
            }

            double combinedConfidence;
            Point combinedLocation;
            Point combinedCenter;

            switch (options.CombineMode)
            {
                case MatchCombineMode.Max:
                    var maxResult = validResults.OrderByDescending(r => r.result.Confidence).First();
                    combinedConfidence = maxResult.result.Confidence;
                    combinedLocation = maxResult.result.Location;
                    combinedCenter = maxResult.result.Center;
                    break;

                case MatchCombineMode.Min:
                    var minResult = validResults.OrderBy(r => r.result.Confidence).First();
                    combinedConfidence = minResult.result.Confidence;
                    combinedLocation = minResult.result.Location;
                    combinedCenter = minResult.result.Center;
                    break;

                case MatchCombineMode.WeightedAverage:
                    var totalWeight = 0.0;
                    var weightedX = 0.0;
                    var weightedY = 0.0;
                    var weightedConf = 0.0;

                    foreach (var (algo, result) in validResults)
                    {
                        var weight = weights.TryGetValue(algo, out var w) ? w : 1.0;
                        totalWeight += weight;
                        weightedX += result.Location.X * weight;
                        weightedY += result.Location.Y * weight;
                        weightedConf += result.Confidence * weight;
                    }

                    combinedConfidence = weightedConf / totalWeight;
                    combinedLocation = new Point((int)(weightedX / totalWeight), (int)(weightedY / totalWeight));
                    combinedCenter = new Point(combinedLocation.X + templateWidth / 2, combinedLocation.Y + templateHeight / 2);
                    break;

                case MatchCombineMode.Average:
                default:
                    combinedConfidence = validResults.Average(r => r.result.Confidence);
                    combinedLocation = new Point(
                            (int)validResults.Average(r => r.result.Location.X),
                            (int)validResults.Average(r => r.result.Location.Y)
                    );
                    combinedCenter = new Point(combinedLocation.X + templateWidth / 2, combinedLocation.Y + templateHeight / 2);
                    break;
            }

            // 返回第一个成功结果的处理图像（如果有多个算法，使用第一个）
            Mat? processedSource = null;
            Mat? processedTemplate = null;
            if (validResults.Count > 0)
            {
                var firstResult = validResults.First();
                processedSource = firstResult.result.ProcessedSourceImage?.Clone();
                processedTemplate = firstResult.result.ProcessedTemplateImage?.Clone();
            }
            else if (results.Count > 0)
            {
                // 如果没有成功的匹配，使用第一个结果的图像
                var firstResult = results.First();
                processedSource = firstResult.result.ProcessedSourceImage?.Clone();
                processedTemplate = firstResult.result.ProcessedTemplateImage?.Clone();
            }

            return new TemplateMatchResult(
                    combinedConfidence >= options.Threshold,
                    combinedLocation,
                    combinedCenter,
                    new Size(templateWidth, templateHeight),
                    combinedConfidence,
                    processedSource,
                    processedTemplate
            );
        }
    }
}
