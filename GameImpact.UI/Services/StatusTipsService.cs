#region

using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

#endregion

namespace GameImpact.UI.Services
{
    /// <summary>右下角状态 Tips 实现：最多 3 条，新条在下方、自动向上挤，约 3 秒后移除。</summary>
    public sealed class StatusTipsService : IStatusTipsService
    {
        private const int MaxTips = 3;
        private const double AutoHideSeconds = 3;
        private readonly Dispatcher m_dispatcher;

        public StatusTipsService()
        {
            m_dispatcher = Application.Current.Dispatcher;
        }

        public ObservableCollection<StatusTipItem> Tips{ get; } = new();

        public void Push(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            m_dispatcher.Invoke(() =>
            {
                var item = new StatusTipItem { Message = message.Trim() };
                Tips.Add(item);

                while (Tips.Count > MaxTips)
                {
                    Tips.RemoveAt(0);
                }

                var timer = new DispatcherTimer(DispatcherPriority.Normal, m_dispatcher)
                {
                    Interval = TimeSpan.FromSeconds(AutoHideSeconds)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    Tips.Remove(item);
                };
                timer.Start();
            });
        }
    }
}
