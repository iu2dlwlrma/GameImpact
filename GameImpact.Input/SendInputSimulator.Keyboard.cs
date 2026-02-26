using GameImpact.Abstractions.Input;
using GameImpact.Input.Native;
using GameImpact.Utilities.Logging;

namespace GameImpact.Input;

public partial class SendInputSimulator : IKeyboardInput
{
    public IKeyboardInput KeyDown(VirtualKey key)
    {
        Log.Debug("[Keyboard] KeyDown: {Key}", key);
        var scan = (byte)(NativeMethods.MapVirtualKey((uint)key, 0) & 0xFF);
        SendKeyboardEvent((byte)key, scan, 0, UIntPtr.Zero);
        return this;
    }

    public IKeyboardInput KeyUp(VirtualKey key)
    {
        Log.Debug("[Keyboard] KeyUp: {Key}", key);
        var scan = (byte)(NativeMethods.MapVirtualKey((uint)key, 0) & 0xFF);
        SendKeyboardEvent((byte)key, scan, NativeMethods.KeyEventFlags_KeyUp, UIntPtr.Zero);
        return this;
    }

    public IKeyboardInput KeyPress(VirtualKey key)
    {
        Log.Debug("[Keyboard] KeyPress: {Key}", key);
        var scan = (byte)(NativeMethods.MapVirtualKey((uint)key, 0) & 0xFF);
        SendKeyboardEvent((byte)key, scan, 0, UIntPtr.Zero);
        Thread.Sleep(50);
        SendKeyboardEvent((byte)key, scan, NativeMethods.KeyEventFlags_KeyUp, UIntPtr.Zero);
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
        // 文本输入使用 SendInput + UNICODE 标志，支持任意 Unicode 字符
        foreach (var c in text)
        {
            var inputs = new NativeMethods.Input[2];
            inputs[0] = new NativeMethods.Input(NativeMethods.InputKeyboard, new NativeMethods.InputUnion {
                    Keyboard = new NativeMethods.KeyboardInput {
                            Vk = 0, Scan = c, Flags = NativeMethods.KeyEventFlags_Unicode
                    }
            });
            inputs[1] = new NativeMethods.Input(NativeMethods.InputKeyboard, new NativeMethods.InputUnion {
                    Keyboard = new NativeMethods.KeyboardInput {
                            Vk = 0, Scan = c,
                            Flags = NativeMethods.KeyEventFlags_Unicode | NativeMethods.KeyEventFlags_KeyUp
                    }
            });
            _ = NativeMethods.SendInput(2, inputs, NativeMethods.Input.Size);
        }
        return this;
    }

    IKeyboardInput IKeyboardInput.Sleep(int milliseconds)
    {
        Thread.Sleep(milliseconds);
        return this;
    }

    private static uint SendKeyboardEvent(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo)
    {
        var input = new NativeMethods.Input(NativeMethods.InputKeyboard, new NativeMethods.InputUnion {
                Keyboard = new NativeMethods.KeyboardInput { Vk = bVk, Scan = bScan, Flags = dwFlags, ExtraInfo = dwExtraInfo }
        });
        return SendInputEvent(input);
    }
}
