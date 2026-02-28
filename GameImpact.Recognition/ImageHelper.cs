using OpenCvSharp;

namespace GameImpact.Recognition;

/// <summary>
/// 图像处理辅助类
/// </summary>
public static class ImageHelper
{
    /// <summary>
    /// 从文件加载图像
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="mode">读取模式</param>
    /// <returns>图像矩阵</returns>
    public static Mat LoadFromFile(string path, ImreadModes mode = ImreadModes.Color)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Image file not found", path);
        return Cv2.ImRead(path, mode);
    }

    /// <summary>
    /// 保存图像到文件
    /// </summary>
    /// <param name="mat">图像矩阵</param>
    /// <param name="path">保存路径</param>
    public static void SaveToFile(Mat mat, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        Cv2.ImWrite(path, mat);
    }

    /// <summary>
    /// 从Base64字符串加载图像
    /// </summary>
    /// <param name="base64">Base64编码的图像数据</param>
    /// <returns>图像矩阵</returns>
    public static Mat LoadFromBase64(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }

    /// <summary>
    /// 将图像转换为Base64字符串
    /// </summary>
    /// <param name="mat">图像矩阵</param>
    /// <param name="ext">文件扩展名</param>
    /// <returns>Base64编码的字符串</returns>
    public static string ToBase64(Mat mat, string ext = ".png")
    {
        Cv2.ImEncode(ext, mat, out var bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// 转换为灰度图像
    /// </summary>
    /// <param name="mat">输入图像</param>
    /// <returns>灰度图像</returns>
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

    /// <summary>
    /// 缩放图像
    /// </summary>
    /// <param name="mat">输入图像</param>
    /// <param name="scale">缩放比例</param>
    /// <returns>缩放后的图像</returns>
    public static Mat Resize(Mat mat, double scale)
    {
        var result = new Mat();
        Cv2.Resize(mat, result, new Size(), scale, scale);
        return result;
    }

    /// <summary>
    /// 裁剪图像
    /// </summary>
    /// <param name="mat">输入图像</param>
    /// <param name="rect">裁剪区域</param>
    /// <returns>裁剪后的图像</returns>
    public static Mat Crop(Mat mat, Rect rect)
    {
        return new Mat(mat, rect);
    }
}
