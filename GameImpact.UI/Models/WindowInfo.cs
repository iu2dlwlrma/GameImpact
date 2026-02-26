using System;

namespace GameImpact.UI.Models;

public class WindowInfo
{
    public nint Handle { get; init; }
    public string Title { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public int ProcessId { get; init; }
    public string DisplayText => $"{ProcessName} - {Title}";
    public string HandleText => $"0x{Handle:X}";
}
