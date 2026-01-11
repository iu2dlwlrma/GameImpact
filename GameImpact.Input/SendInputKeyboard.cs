using System.Runtime.InteropServices;
using GameImpact.Abstractions.Input;
using GameImpact.Input.Native;
using GameImpact.Utilities.Logging;

namespace GameImpact.Input;

public class SendInputKeyboard : IKeyboardInput
{
    private static readonly HashSet<VirtualKey> ExtendedKeys =
    [
        VirtualKey.Menu, VirtualKey.LMenu, VirtualKey.RMenu,
        VirtualKey.Control, VirtualKey.RControl,
        VirtualKey.Insert, VirtualKey.Delete, VirtualKey.Home, VirtualKey.End,
        VirtualKey.Prior, VirtualKey.Next,
        VirtualKey.Right, VirtualKey.Up, VirtualKey.Left, VirtualKey.Down,
        VirtualKey.NumLock, VirtualKey.Cancel, VirtualKey.Snapshot, VirtualKey.Divide
    ];

    public IKeyboardInput KeyDown(VirtualKey key)
    {
        Log.Debug("[Keyboard] KeyDown: {Key}", key);
        var input = CreateKeyInput(key, false);
        SendInput(input);
        return this;
    }

    public IKeyboardInput KeyUp(VirtualKey key)
    {
        Log.Debug("[Keyboard] KeyUp: {Key}", key);
        var input = CreateKeyInput(key, true);
        SendInput(input);
        return this;
    }

    public IKeyboardInput KeyPress(VirtualKey key)
    {
        Log.Debug("[Keyboard] KeyPress: {Key}", key);
        KeyDown(key);
        KeyUp(key);
        return this;
    }

    public IKeyboardInput KeyPress(params VirtualKey[] keys)
    {
        foreach (var key in keys)
            KeyPress(key);
        return this;
    }

    public IKeyboardInput ModifiedKeyStroke(VirtualKey modifier, VirtualKey key)
    {
        Log.Debug("[Keyboard] ModifiedKeyStroke: {Modifier}+{Key}", modifier, key);
        KeyDown(modifier);
        KeyPress(key);
        KeyUp(modifier);
        return this;
    }

    public IKeyboardInput TextEntry(string text)
    {
        Log.Debug("[Keyboard] TextEntry: {Length} chars", text.Length);
        foreach (var c in text)
        {
            var inputs = new NativeMethods.INPUT[2];
            inputs[0] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                U = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE
                    }
                }
            };
            inputs[1] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                U = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP
                    }
                }
            };
            NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }
        return this;
    }

    public IKeyboardInput Sleep(int milliseconds)
    {
        Thread.Sleep(milliseconds);
        return this;
    }

    private static NativeMethods.INPUT CreateKeyInput(VirtualKey key, bool keyUp)
    {
        var flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : NativeMethods.KEYEVENTF_KEYDOWN;
        if (ExtendedKeys.Contains(key))
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;

        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)key,
                    wScan = (ushort)(NativeMethods.MapVirtualKey((uint)key, 0) & 0xFF),
                    dwFlags = flags
                }
            }
        };
    }

    private static void SendInput(NativeMethods.INPUT input)
    {
        NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
