using OpenCvSharp;

namespace GameImpact.Recognition;

public static class ImageHelper
{
    public static Mat LoadFromFile(string path, ImreadModes mode = ImreadModes.Color)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Image file not found", path);
        return Cv2.ImRead(path, mode);
    }

    public static void SaveToFile(Mat mat, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        Cv2.ImWrite(path, mat);
    }

    public static Mat LoadFromBase64(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        return Cv2.ImDecode(bytes, ImreadModes.Color);
    }

    public static string ToBase64(Mat mat, string ext = ".png")
    {
        Cv2.ImEncode(ext, mat, out var bytes);
        return Convert.ToBase64String(bytes);
    }

    public static Mat ToGray(Mat mat)
    {
        if (mat.Channels() == 1) return mat.Clone();
        var gray = new Mat();
        Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    public static Mat Resize(Mat mat, double scale)
    {
        var result = new Mat();
        Cv2.Resize(mat, result, new Size(), scale, scale);
        return result;
    }

    public static Mat Crop(Mat mat, Rect rect)
    {
        return new Mat(mat, rect);
    }
}
