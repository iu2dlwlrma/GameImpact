namespace GameImpact.UI.Models
{
    /// <summary>窗口信息模型</summary>
    public class WindowInfo
    {
        /// <summary>窗口句柄</summary>
        public nint Handle{ get; init; }

        /// <summary>窗口标题</summary>
        public string Title{ get; init; } = "";

        /// <summary>进程名称</summary>
        public string ProcessName{ get; init; } = "";

        /// <summary>进程ID</summary>
        public int ProcessId{ get; init; }

        /// <summary>显示文本（进程名 - 标题）</summary>
        public string DisplayText => $"{ProcessName} - {Title}";

        /// <summary>句柄文本（十六进制格式）</summary>
        public string HandleText => $"0x{Handle:X}";
    }
}
