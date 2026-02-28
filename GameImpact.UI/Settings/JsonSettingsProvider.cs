#region

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameImpact.Utilities.Logging;

#endregion

namespace GameImpact.UI.Settings
{
    /// <summary>基于 JSON 文件的设置持久化实现。 配置文件保存在应用安装根目录下的指定文件中。</summary>
    /// <typeparam name="T">设置模型类型</typeparam>
    public class JsonSettingsProvider<T> : ISettingsProvider<T> where T : class, new()
    {
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new JsonStringEnumConverter() }
        };
        private readonly string m_filePath;

        /// <summary>缓存的设置实例，确保所有消费者共享同一个对象引用</summary>
        private T? m_cached;

        /// <summary>创建 JSON 设置提供者</summary>
        /// <param name="fileName">配置文件名（如 "appsettings.json"），保存在应用安装根目录</param>
        public JsonSettingsProvider(string fileName)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            m_filePath = Path.Combine(baseDir, fileName);
        }

        /// <summary>加载设置。首次调用从 JSON 文件读取，后续调用返回缓存实例。 文件不存在或解析失败时返回默认实例。</summary>
        public T Load()
        {
            if (m_cached != null)
            {
                return m_cached;
            }

            try
            {
                if (!File.Exists(m_filePath))
                {
                    Log.Debug("[Settings] 配置文件不存在，使用默认值: {Path}", m_filePath);
                    m_cached = new T();

                    return m_cached;
                }

                var json = File.ReadAllText(m_filePath);
                var result = JsonSerializer.Deserialize<T>(json, s_jsonOptions);

                if (result != null)
                {
                    Log.Info("[Settings] 已加载配置: {Path}", m_filePath);
                    m_cached = result;

                    return m_cached;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[Settings] 加载配置失败，使用默认值: {m_filePath}");
            }

            m_cached = new T();

            return m_cached;
        }

        /// <summary>将设置序列化为 JSON 并保存到文件</summary>
        public void Save(T settings)
        {
            try
            {
                var directory = Path.GetDirectoryName(m_filePath) ?? string.Empty;
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(settings, s_jsonOptions);
                File.WriteAllText(m_filePath, json);
                Log.Info("[Settings] 已保存配置: {Path}", m_filePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[Settings] 保存配置失败: {m_filePath}");
            }
        }
    }
}
