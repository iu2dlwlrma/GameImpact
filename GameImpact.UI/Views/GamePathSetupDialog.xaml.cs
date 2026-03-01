#region

using System.IO;
using System.Windows;
using GameImpact.UI.Settings;

#endregion

namespace GameImpact.UI.Views
{
    /// <summary>游戏路径设置小窗口，仅用于设置 AppSettings.GameRootPath。</summary>
    public partial class GamePathSetupDialog : Window
    {
        private readonly ISettingsProvider<AppSettings> m_settingsProvider;

        public GamePathSetupDialog(ISettingsProvider<AppSettings> settingsProvider, Window? owner = null)
        {
            InitializeComponent();
            m_settingsProvider = settingsProvider;
            Owner = owner;
            LoadCurrentPath();
        }

        private void LoadCurrentPath()
        {
            PathBox.Text = m_settingsProvider.Load().GameRootPath ?? string.Empty;
        }

        private void PathBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(PathBox.Text?.Trim());
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                    Description = "选择游戏根目录（包含游戏主程序的文件夹）",
                    UseDescriptionForTitle = true,
                    SelectedPath = string.IsNullOrWhiteSpace(PathBox.Text) ? "" : PathBox.Text.Trim()
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                    && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                PathBox.Text = dialog.SelectedPath;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var path = PathBox.Text?.Trim();
            if (!Directory.Exists(path))
            {
                MessageBox.Show("所选路径不存在，请重新选择。", "路径无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var settings = m_settingsProvider.Load();
            settings.GameRootPath = path;
            m_settingsProvider.Save(settings);
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
