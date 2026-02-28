namespace GameImpact.UI.Settings
{
    /// <summary>设置存储提供者接口，负责设置的加载与持久化</summary>
    /// <typeparam name="T">设置模型类型</typeparam>
    public interface ISettingsProvider<T> where T : class, new()
    {
        /// <summary>加载设置，如果文件不存在则返回默认值</summary>
        T Load();

        /// <summary>保存设置到持久化存储</summary>
        void Save(T settings);
    }
}
