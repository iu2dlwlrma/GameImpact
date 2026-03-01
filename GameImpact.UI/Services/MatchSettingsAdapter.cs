#region

using GameImpact.Abstractions.Recognition;
using GameImpact.Core.Services;
using GameImpact.UI.Settings;

#endregion

namespace GameImpact.UI.Services
{
    /// <summary>匹配设置适配器，将 AppSettings 适配为 IMatchSettings</summary>
    public sealed class MatchSettingsAdapter : IMatchSettings
    {
        private readonly ISettingsProvider<AppSettings> m_settingsProvider;

        public MatchSettingsAdapter(ISettingsProvider<AppSettings> settingsProvider)
        {
            m_settingsProvider = settingsProvider;
        }

        public MatchAlgorithm MatchAlgorithms => m_settingsProvider.Load().MatchAlgorithms;
        public MatchCombineMode MatchCombineMode => m_settingsProvider.Load().MatchCombineMode;
        public double RecognitionConfidenceThreshold => m_settingsProvider.Load().RecognitionConfidenceThreshold;
    }
}
