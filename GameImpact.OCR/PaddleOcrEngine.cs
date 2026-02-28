using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameImpact.Abstractions.Recognition;
using GameImpact.ImageProcessing;
using OpenCvSharp;
using PaddleOCRSharp;

namespace GameImpact.OCR;

/// <summary>
/// 基于 PaddleOCRSharp 的 OCR 引擎实现
/// </summary>
public class PaddleOcrEngine : IOcrEngine
{
    private readonly PaddleOCREngine m_engine;
    private bool m_disposed;

    /// <summary>
    /// 创建 PaddleOCR 引擎（使用默认中英文模型）
    /// </summary>
    public PaddleOcrEngine()
    {
        // 使用内置的中英文 V4 模型
        m_engine = new PaddleOCREngine(null, new OCRParameter
        {
            cpu_math_library_num_threads = 4,
            enable_mkldnn = true,
            det_db_score_mode = true,
            det_db_unclip_ratio = 1.5f,
            rec_img_h = 48,
            rec_img_w = 320
        });
    }

    /// <inheritdoc/>
    public List<OcrResult> Recognize(Mat image)
    {
        if (image.Empty()) return [];
        
        // 使用游戏 UI 专用预处理
        using var processed = GameTextPreprocessor.PreprocessGameUI(image);
        var bytes = processed.ToBytes(".bmp");
        var result = m_engine.DetectText(bytes);
        
        return ConvertResult(result);
    }

    /// <inheritdoc/>
    public List<OcrResult> Recognize(Mat image, Rect roi)
    {
        if (image.Empty()) return [];
        
        // 裁剪 ROI 区域
        using var roiMat = new Mat(image, roi);
        var results = Recognize(roiMat);
        
        // 调整坐标到原图
        return results.Select(r => r with
        {
            BoundingBox = new Rect(
                r.BoundingBox.X + roi.X,
                r.BoundingBox.Y + roi.Y,
                r.BoundingBox.Width,
                r.BoundingBox.Height),
            Polygon = r.Polygon.Select(p => new Point2f(p.X + roi.X, p.Y + roi.Y)).ToArray()
        }).ToList();
    }

    /// <inheritdoc/>
    public List<Rect> Detect(Mat image)
    {
        if (image.Empty()) return [];
        
        var bytes = image.ToBytes(".bmp");
        var result = m_engine.DetectText(bytes);
        
        if (result?.TextBlocks == null) return [];
        
        return result.TextBlocks
            .Select(b => GetBoundingRect(b.BoxPoints))
            .ToList();
    }

    private static List<OcrResult> ConvertResult(OCRResult? result)
    {
        if (result?.TextBlocks == null) return [];
        
        return result.TextBlocks.Select(block =>
        {
            var polygon = block.BoxPoints.Select(p => new Point2f(p.X, p.Y)).ToArray();
            var boundingBox = GetBoundingRect(block.BoxPoints);
            
            return new OcrResult(
                Text: block.Text,
                Confidence: block.Score,
                BoundingBox: boundingBox,
                Polygon: polygon
            );
        }).ToList();
    }

    private static Rect GetBoundingRect(List<OCRPoint> points)
    {
        if (points.Count == 0) return new Rect();
        
        int minX = (int)points.Min(p => p.X);
        int minY = (int)points.Min(p => p.Y);
        int maxX = (int)points.Max(p => p.X);
        int maxY = (int)points.Max(p => p.Y);
        
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }
        m_disposed = true;
        m_engine.Dispose();
        GC.SuppressFinalize(this);
    }
}
