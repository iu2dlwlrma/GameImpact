#region

using GameImpact.Abstractions.Hotkey;
using HotkeyKeys = GameImpact.Abstractions.Hotkey.Keys;

#endregion

namespace GameImpact.Hotkey
{
    /// <summary>热键字符串解析器</summary>
    public static class HotkeyParser
    {
        /// <summary>解析热键字符串</summary>
        /// <param name="hotkeyString">热键字符串，格式如 "Ctrl+Alt+F1"</param>
        /// <returns>修饰键和主键的元组</returns>
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

        /// <summary>将修饰键和主键转换为字符串</summary>
        /// <param name="modifiers">修饰键</param>
        /// <param name="key">主键</param>
        /// <returns>热键字符串</returns>
        public static string ToString(ModifierKeys modifiers, HotkeyKeys key)
        {
            var parts = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Win))
            {
                parts.Add("Win");
            }
            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                parts.Add("Ctrl");
            }
            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                parts.Add("Shift");
            }
            if (modifiers.HasFlag(ModifierKeys.Alt))
            {
                parts.Add("Alt");
            }
            if (key != HotkeyKeys.None)
            {
                parts.Add(key.ToString());
            }
            return string.Join(" + ", parts);
        }
    }
}
