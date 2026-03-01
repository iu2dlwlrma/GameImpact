#region

using System.Collections.ObjectModel;

#endregion

namespace GameImpact.UI.Services
{
    /// <summary>右下角状态 Tips 服务：接收短消息，以最多 3 条、自动上滚、定时消失的方式展示。</summary>
    public interface IStatusTipsService
    {
        /// <summary>当前可见的 Tips（最多 3 条，新在末尾、自动移除）。</summary>
        ObservableCollection<StatusTipItem> Tips{ get; }

        /// <summary>推送一条 Tip，显示在右下角，约 3 秒后自动移除。</summary>
        void Push(string message);
    }

    /// <summary>单条 Tip 项</summary>
    public class StatusTipItem
    {
        public string Message{ get; init; } = string.Empty;
    }
}
