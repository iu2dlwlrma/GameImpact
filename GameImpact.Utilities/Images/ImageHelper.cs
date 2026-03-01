#region

using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

#endregion

namespace GameImpact.Utilities.Images
{
    /// <summary>通用的图像 IO / 基础处理工具。</summary>
    public static class ImageHelper
    {
        /// <summary>从文件加载图像（带 FileNotFound 检查）。</summary>
        public static Mat LoadFromFile(string path, ImreadModes mode = ImreadModes.Color)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Image file not found", path);
            }
            return Cv2.ImRead(path, mode);
        }

        /// <summary>保存图像到文件（自动创建目录）。</summary>
        public static void SaveToFile(Mat mat, string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            Cv2.ImWrite(path, mat);
        }

        /// <summary>从 Base64 字符串加载图像。</summary>
        public static Mat LoadFromBase64(string base64)
        {
            var bytes = Convert.FromBase64String(base64);
            return Cv2.ImDecode(bytes, ImreadModes.Color);
        }

        /// <summary>将图像编码为 Base64 字符串（默认 PNG）。</summary>
        public static string ToBase64(Mat mat, string ext = ".png")
        {
            Cv2.ImEncode(ext, mat, out var bytes);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>转换为灰度图像（若已是灰度则 Clone）。</summary>
        public static Mat ToGray(Mat mat)
        {
            if (mat.Channels() == 1)
            {
                return mat.Clone();
            }
            var gray = new Mat();
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
            return gray;
        }

        /// <summary>按比例缩放图像。</summary>
        public static Mat Resize(Mat mat, double scale)
        {
            var result = new Mat();
            Cv2.Resize(mat, result, new Size(), scale, scale);
            return result;
        }

        /// <summary>裁剪图像（不做边界检查）。</summary>
        public static Mat Crop(Mat mat, Rect rect)
        {
            return new Mat(mat, rect);
        }

        /// <summary>将 Mat 转换为 BitmapSource</summary>
        public static BitmapSource MatToBitmapSource(Mat? mat)
        {
            if (mat == null || mat.Empty())
            {
                return null!;
            }

            Mat? rgba = null;
            try
            {
                if (mat.Channels() == 3)
                {
                    rgba = new Mat();
                    Cv2.CvtColor(mat, rgba, ColorConversionCodes.BGR2BGRA);
                }
                else if (mat.Channels() == 4)
                {
                    rgba = mat;
                }
                else
                {
                    rgba = new Mat();
                    Cv2.CvtColor(mat, rgba, ColorConversionCodes.GRAY2BGRA);
                }

                var width = rgba.Width;
                var height = rgba.Height;
                var stride = rgba.Step();

                var data = new byte[height * stride];
                Marshal.Copy(rgba.Data, data, 0, data.Length);

                var bitmap = BitmapSource.Create(
                        width, height,
                        96, 96,
                        PixelFormats.Bgra32,
                        null,
                        data,
                        (int)stride);

                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                if (rgba != null && rgba != mat)
                {
                    rgba.Dispose();
                }
            }
        }
    }
}
