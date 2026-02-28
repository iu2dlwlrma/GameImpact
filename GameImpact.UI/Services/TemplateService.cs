using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Reflection;
using OpenCvSharp;

namespace GameImpact.UI.Services
{
    /// <summary>模板文件及其 ROI 配置的管理服务。</summary>
    public sealed class TemplateService
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
            var path = Path.Combine(TemplatesFolderPath, relativeFilePath);
            Cv2.ImWrite(path, image);
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

                Rect? matchRoi = null;
                Rect? textRoi = null;

                if (roiData.TryGetProperty("MatchRoi", out var matchElem) &&
                    matchElem.ValueKind == JsonValueKind.Object)
                {
                    matchRoi = new Rect(
                            matchElem.GetProperty("X").GetInt32(),
                            matchElem.GetProperty("Y").GetInt32(),
                            matchElem.GetProperty("Width").GetInt32(),
                            matchElem.GetProperty("Height").GetInt32());
                }

                if (roiData.TryGetProperty("TextRoi", out var textElem) &&
                    textElem.ValueKind == JsonValueKind.Object)
                {
                    textRoi = new Rect(
                            textElem.GetProperty("X").GetInt32(),
                            textElem.GetProperty("Y").GetInt32(),
                            textElem.GetProperty("Width").GetInt32(),
                            textElem.GetProperty("Height").GetInt32());
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
                MatchRoi = matchRoi.HasValue ? new
                {
                    X = (int)matchRoi.Value.X,
                    Y = (int)matchRoi.Value.Y,
                    Width = (int)matchRoi.Value.Width,
                    Height = (int)matchRoi.Value.Height
                } : null,
                TextRoi = textRoi.HasValue ? new
                {
                    X = (int)textRoi.Value.X,
                    Y = (int)textRoi.Value.Y,
                    Width = (int)textRoi.Value.Width,
                    Height = (int)textRoi.Value.Height
                } : null
            };

            var json = JsonSerializer.Serialize(roiData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(roiFilePath, json);
        }

        private string GetRoiFilePath(string templateFileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(templateFileName);
            return Path.Combine(TemplatesFolderPath, $"{baseName}.roi.json");
        }

        /// <summary>获取项目目录。优先查找包含 .csproj 的目录（开发环境），找不到则使用入口程序集所在目录（打包环境）。</summary>
        private static string GetProjectDirectory()
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly == null)
            {
                return AppContext.BaseDirectory;
            }

            var assemblyLocation = entryAssembly.Location;
            if (string.IsNullOrEmpty(assemblyLocation))
            {
                return AppContext.BaseDirectory;
            }

            var currentDir = Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrEmpty(currentDir))
            {
                return AppContext.BaseDirectory;
            }

            var directory = new DirectoryInfo(currentDir);
            while (directory != null)
            {
                if (directory.GetFiles("*.csproj").Length > 0)
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }

            return currentDir;
        }
    }
}

