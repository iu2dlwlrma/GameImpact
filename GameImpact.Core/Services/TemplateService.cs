#region

using System.Reflection;
using System.Text.Json;
using OpenCvSharp;

#endregion

namespace GameImpact.Core.Services
{
    /// <summary>模板文件及 ROI 配置管理，与 UI 无关。</summary>
    public interface ITemplateService
    {
        string TemplatesFolderPath{ get; }
        void EnsureTemplatesFolder();
        List<string> GetTemplateFileNames();
        string GetTemplatePath(string fileName);
        void SaveTemplate(Mat image, string relativeFilePath);
        (Rect? matchRoi, Rect? textRoi) LoadTemplateRoi(string templateFileName);
        void SaveTemplateRoi(string templateFileName, Rect? matchRoi, Rect? textRoi);
    }

    /// <summary>模板服务实现。</summary>
    public sealed class TemplateService : ITemplateService
    {
        public string TemplatesFolderPath => Path.Combine(GetProjectDirectory(), "Templates");

        public void EnsureTemplatesFolder()
        {
            Directory.CreateDirectory(TemplatesFolderPath);
        }

        public List<string> GetTemplateFileNames()
        {
            if (!Directory.Exists(TemplatesFolderPath))
            {
                return new List<string>();
            }
            return Directory.GetFiles(TemplatesFolderPath, "*.png")
                    .Select(Path.GetFileName)
                    .Where(f => f != null)
                    .Cast<string>()
                    .OrderByDescending(f => f)
                    .ToList();
        }

        public string GetTemplatePath(string fileName)
        {
            return Path.Combine(TemplatesFolderPath, fileName);
        }

        public void SaveTemplate(Mat image, string relativeFilePath)
        {
            EnsureTemplatesFolder();
            Cv2.ImWrite(Path.Combine(TemplatesFolderPath, relativeFilePath), image);
        }

        public (Rect? matchRoi, Rect? textRoi) LoadTemplateRoi(string templateFileName)
        {
            var roiFilePath = GetRoiFilePath(templateFileName);
            if (!File.Exists(roiFilePath))
            {
                return (null, null);
            }
            try
            {
                var json = File.ReadAllText(roiFilePath);
                var roiData = JsonSerializer.Deserialize<JsonElement>(json);
                Rect? matchRoi = null, textRoi = null;
                if (roiData.TryGetProperty("MatchRoi", out var matchElem) && matchElem.ValueKind == JsonValueKind.Object)
                {
                    matchRoi = new Rect(matchElem.GetProperty("X").GetInt32(), matchElem.GetProperty("Y").GetInt32(), matchElem.GetProperty("Width").GetInt32(), matchElem.GetProperty("Height").GetInt32());
                }
                if (roiData.TryGetProperty("TextRoi", out var textElem) && textElem.ValueKind == JsonValueKind.Object)
                {
                    textRoi = new Rect(textElem.GetProperty("X").GetInt32(), textElem.GetProperty("Y").GetInt32(), textElem.GetProperty("Width").GetInt32(), textElem.GetProperty("Height").GetInt32());
                }
                return (matchRoi, textRoi);
            }
            catch
            {
                return (null, null);
            }
        }

        public void SaveTemplateRoi(string templateFileName, Rect? matchRoi, Rect? textRoi)
        {
            var roiFilePath = GetRoiFilePath(templateFileName);
            var roiData = new
            {
                    MatchRoi = matchRoi.HasValue ? new { X = matchRoi.Value.X, Y = matchRoi.Value.Y, Width = matchRoi.Value.Width, Height = matchRoi.Value.Height } : null,
                    TextRoi = textRoi.HasValue ? new { X = textRoi.Value.X, Y = textRoi.Value.Y, Width = textRoi.Value.Width, Height = textRoi.Value.Height } : null
            };
            File.WriteAllText(roiFilePath, JsonSerializer.Serialize(roiData, new JsonSerializerOptions { WriteIndented = true }));
        }

        private string GetRoiFilePath(string templateFileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(templateFileName);
            return Path.Combine(TemplatesFolderPath, $"{baseName}.roi.json");
        }

        private static string GetProjectDirectory()
        {
            var entry = Assembly.GetEntryAssembly();
            if (entry?.Location is not { } loc || string.IsNullOrEmpty(loc))
            {
                return AppContext.BaseDirectory;
            }
            var currentDir = Path.GetDirectoryName(loc);
            if (string.IsNullOrEmpty(currentDir))
            {
                return AppContext.BaseDirectory;
            }
            for (var d = new DirectoryInfo(currentDir); d != null; d = d.Parent)
            {
                if (d.GetFiles("*.csproj").Length > 0)
                {
                    return d.FullName;
                }
            }
            return currentDir;
        }
    }
}
