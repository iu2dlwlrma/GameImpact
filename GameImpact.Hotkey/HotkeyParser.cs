using GameImpact.Abstractions.Hotkey;
using HotkeyKeys = GameImpact.Abstractions.Hotkey.Keys;

namespace GameImpact.Hotkey;

public static class HotkeyParser
{
    public static (ModifierKeys Modifiers, HotkeyKeys Key) Parse(string hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
        {
            throw new ArgumentException("Hotkey string cannot be empty");
        }

        var modifiers = ModifierKeys.None;
        var key = HotkeyKeys.None;
        
        var parts = hotkeyString.Replace(" ", "").Split('+');
        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            switch (upper)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModifierKeys.Win;
                    break;
                default:
                    if (Enum.TryParse<HotkeyKeys>(part, true, out var k))
                    {
                        key = k;
                    }
                    break;
            }
        }

        if (key == HotkeyKeys.None)
        {
            throw new ArgumentException($"Invalid hotkey: {hotkeyString}");
        }

        return (modifiers, key);
    }

    public static string ToString(ModifierKeys modifiers, HotkeyKeys key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Win)) parts.Add("Win");
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (key != HotkeyKeys.None) parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }
}
