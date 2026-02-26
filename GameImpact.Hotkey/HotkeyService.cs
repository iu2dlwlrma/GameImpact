using System.Runtime.InteropServices;
using System.Windows.Forms;
using GameImpact.Abstractions.Hotkey;
using GameImpact.Utilities.Logging;
using Vanara.PInvoke;
using Keys = GameImpact.Abstractions.Hotkey.Keys;
using ModifierKeys = GameImpact.Abstractions.Hotkey.ModifierKeys;

namespace GameImpact.Hotkey;

public class HotkeyService : IHotkeyService
{
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    private readonly HotkeyWindow _window;
    private int _currentId;

    public HotkeyService()
    {
        _window = new HotkeyWindow();
        _window.HotkeyPressed += (_, e) =>
        {
            Log.Debug("[Hotkey] Pressed: {Modifiers}+{Key}", e.Modifiers, e.Key);
            HotkeyPressed?.Invoke(this, e);
        };
        Log.Debug("[Hotkey] Service initialized");
    }

    public int Register(ModifierKeys modifiers, Keys key)
    {
        _currentId++;
        var mod = ToNativeModifiers(modifiers);
        if (!User32.RegisterHotKey(_window.Handle, _currentId, mod, (uint)key))
        {
            var error = Marshal.GetLastWin32Error();
            Log.Error("[Hotkey] Registration failed: {Modifiers}+{Key}, error={Error}", modifiers, key, error);
            throw new InvalidOperationException(error == 1409
                ? "Hotkey already registered"
                : $"Hotkey registration failed: {error}");
        }
        Log.Info("[Hotkey] Registered: {Modifiers}+{Key}, id={Id}", modifiers, key, _currentId);
        return _currentId;
    }

    public void Unregister(int hotkeyId)
    {
        Log.Debug("[Hotkey] Unregistering id={Id}", hotkeyId);
        User32.UnregisterHotKey(_window.Handle, hotkeyId);
    }

    public void UnregisterAll()
    {
        Log.Debug("[Hotkey] Unregistering all ({Count} hotkeys)", _currentId);
        for (var i = _currentId; i > 0; i--)
            User32.UnregisterHotKey(_window.Handle, i);
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.Dispose();
        Log.Debug("[Hotkey] Service disposed");
        GC.SuppressFinalize(this);
    }

    private static User32.HotKeyModifiers ToNativeModifiers(ModifierKeys modifiers)
    {
        var result = User32.HotKeyModifiers.MOD_NONE;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= User32.HotKeyModifiers.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= User32.HotKeyModifiers.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= User32.HotKeyModifiers.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Win)) result |= User32.HotKeyModifiers.MOD_WIN;
        return result;
    }

    private class HotkeyWindow : NativeWindow, IDisposable
    {
        public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == (int)User32.WindowMessage.WM_HOTKEY)
            {
                var key = (Keys)(((int)m.LParam >> 16) & 0xFFFF);
                var mod = (ModifierKeys)((int)m.LParam & 0xFFFF);
                HotkeyPressed?.Invoke(this, new HotkeyEventArgs(mod, key));
            }
        }

        public void Dispose()
        {
            DestroyHandle();
        }
    }
}
