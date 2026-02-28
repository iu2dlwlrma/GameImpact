#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using GameImpact.Abstractions.Recognition;
using OpenCvSharp;
using WinOcr = Windows.Media.Ocr;

#endregion

namespace GameImpact.OCR
{
    /// <summary>基于 Windows.Media.Ocr 的 OCR 引擎实现</summary>
    /// <summary>基于 Windows.Media.Ocr 的 OCR 引擎实现</summary>
    public class WindowsOcrEngine : IOcrEngine
    {
        private readonly WinOcr.OcrEngine m_engine;
        private bool m_disposed;

        /// <summary>创建 Windows OCR 引擎</summary>
        /// <param name="language">语言代码，默认 zh-Hans（简体中文）</param>
        public WindowsOcrEngine(string language = "zh-Hans")
        {
            var lang = new Language(language);

            if (!WinOcr.OcrEngine.IsLanguageSupported(lang))
            {
                throw new NotSupportedException($"Language '{language}' is not supported. Install it in Windows Settings > Language.");
            }

            m_engine = WinOcr.OcrEngine.TryCreateFromLanguage(lang)
                    ?? throw new InvalidOperationException($"Failed to create OCR engine for '{language}'");
        }

        /// <inheritdoc/>
        public List<OcrResult> Recognize(Mat image)
        {
            if (image.Empty())
            {
                return [];
            }

            using var bitmap = MatToSoftwareBitmap(image);
            var result = m_engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();

            return ConvertResult(result);
        }

        /// <inheritdoc/>
        public List<OcrResult> Recognize(Mat image, Rect roi)
        {
            if (image.Empty())
            {
                return [];
            }

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
            return Recognize(image).Select(r => r.BoundingBox).ToList();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            GC.SuppressFinalize(this);
        }

        private static SoftwareBitmap MatToSoftwareBitmap(Mat image)
        {
            // 确保是 BGRA 格式
            Mat bgra;
            var needDispose = false;

            if (image.Channels() == 4)
            {
                bgra = image;
            }
            else if (image.Channels() == 3)
            {
                bgra = new Mat();
                Cv2.CvtColor(image, bgra, ColorConversionCodes.BGR2BGRA);
                needDispose = true;
            }
            else // 灰度图
            {
                bgra = new Mat();
                Cv2.CvtColor(image, bgra, ColorConversionCodes.GRAY2BGRA);
                needDispose = true;
            }

            try
            {
                // 转为字节数组
                var dataSize = bgra.Rows * bgra.Cols * 4;
                var pixelData = new byte[dataSize];

                unsafe
                {
                    var srcStep = (int)bgra.Step();
                    var dstStep = bgra.Cols * 4;
                    var src = (byte*)bgra.Data;

                    for (var y = 0; y < bgra.Rows; y++)
                    {
                        Marshal.Copy(
                                (nint)(src + y * srcStep),
                                pixelData,
                                y * dstStep,
                                dstStep);
                    }
                }

                var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, bgra.Width, bgra.Height, BitmapAlphaMode.Premultiplied);
                bitmap.CopyFromBuffer(pixelData.AsBuffer());

                return bitmap;
            }
            finally
            {
                if (needDispose)
                {
                    bgra.Dispose();
                }
            }
        }

        private static List<OcrResult> ConvertResult(WinOcr.OcrResult result)
        {
            var results = new List<OcrResult>();

            // 按行合并，而不是按单词拆分
            foreach (var line in result.Lines)
            {
                if (line.Words.Count == 0)
                {
                    continue;
                }

                // 合并整行文本
                var text = line.Text;

                // 计算整行的边界框
                var minX = line.Words.Min(w => w.BoundingRect.X);
                var minY = line.Words.Min(w => w.BoundingRect.Y);
                var maxX = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
                var maxY = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);

                var boundingBox = new Rect(
                        (int)minX, (int)minY,
                        (int)(maxX - minX), (int)(maxY - minY));

                var polygon = new Point2f[]
                {
                        new((float)minX, (float)minY),
                        new((float)maxX, (float)minY),
                        new((float)maxX, (float)maxY),
                        new((float)minX, (float)maxY)
                };

                results.Add(new OcrResult(
                        text,
                        1.0f,
                        boundingBox,
                        polygon
                ));
            }

            return results;
        }
    }
}
