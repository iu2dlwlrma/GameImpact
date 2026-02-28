namespace GameImpact.Abstractions.Hotkey
{
    public interface IHotkeyService : IDisposable
    {
        event EventHandler<HotkeyEventArgs>? HotkeyPressed;
        int Register(ModifierKeys modifiers, Keys key);
        void Unregister(int hotkeyId);
        void UnregisterAll();
    }

    public class HotkeyEventArgs : EventArgs
    {
        public HotkeyEventArgs(ModifierKeys modifiers, Keys key)
        {
            Modifiers = modifiers;
            Key = key;
        }
        public ModifierKeys Modifiers{ get; }
        public Keys Key{ get; }
    }

    [Flags]
    public enum ModifierKeys
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Win = 8
    }

    public enum Keys
    {
        None = 0,
        A = 65,
        B = 66,
        C = 67,
        D = 68,
        E = 69,
        F = 70,
        G = 71,
        H = 72,
        I = 73,
        J = 74,
        K = 75,
        L = 76,
        M = 77,
        N = 78,
        O = 79,
        P = 80,
        Q = 81,
        R = 82,
        S = 83,
        T = 84,
        U = 85,
        V = 86,
        W = 87,
        X = 88,
        Y = 89,
        Z = 90,
        D0 = 48,
        D1 = 49,
        D2 = 50,
        D3 = 51,
        D4 = 52,
        D5 = 53,
        D6 = 54,
        D7 = 55,
        D8 = 56,
        D9 = 57,
        F1 = 112,
        F2 = 113,
        F3 = 114,
        F4 = 115,
        F5 = 116,
        F6 = 117,
        F7 = 118,
        F8 = 119,
        F9 = 120,
        F10 = 121,
        F11 = 122,
        F12 = 123,
        Space = 32,
        Enter = 13,
        Escape = 27,
        Tab = 9,
        Back = 8,
        Insert = 45,
        Delete = 46,
        Home = 36,
        End = 35,
        PageUp = 33,
        PageDown = 34,
        Left = 37,
        Up = 38,
        Right = 39,
        Down = 40
    }
}
