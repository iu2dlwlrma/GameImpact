using System.Runtime.InteropServices;
using GameImpact.Abstractions.Input;
using GameImpact.Input.Native;
using GameImpact.Utilities.Logging;

namespace GameImpact.Input;

public partial class SendInputSimulator : IKeyboardInput
{
    public IKeyboardInput KeyDown(VirtualKey key)
    {
        Log.Debug("[Keyboard] KeyDown: {Key}", key);
        SendKeyAction(key, false);
        return this;
    }

    public IKeyboardInput KeyUp(VirtualKey key)
    {
        Log.Debug("[Keyboard] KeyUp: {Key}", key);
        SendKeyAction(key, true);
        return this;
    }

    public IKeyboardInput KeyPress(VirtualKey key)
    {
        Log.Debug("[Keyboard] KeyPress: {Key}", key);
        SendKeyAction(key, false);
        Thread.Sleep(50);
        SendKeyAction(key, true);
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
            var sent = NativeMethods.SendInput(2, inputs, NativeMethods.Input.Size);
            if (sent == 0)
                LogWin32Error("[Keyboard] SendInput TextEntry failed for char '{Char}'", c);
        }
        return this;
    }

    IKeyboardInput IKeyboardInput.Sleep(int milliseconds)
    {
        Thread.Sleep(milliseconds);
        return this;
    }

    private uint SendKeyAction(VirtualKey key, bool isKeyUp)
    {
        var scan = (ushort)(NativeMethods.MapVirtualKey((uint)key, 0) & 0xFF);

        return KeyboardMode switch
        {
            KeyboardInputMode.SendInputScanCode => SendInputScanCode(scan, isKeyUp),
            KeyboardInputMode.PostMessage => PostMessageKey(key, scan, isKeyUp) ? 1u : 0u,
            _ => SendInputVk(key, scan, isKeyUp),
        };
    }

    private static uint SendInputVk(VirtualKey key, ushort scan, bool isKeyUp)
    {
        uint flags = isKeyUp ? NativeMethods.KeyEventFlags_KeyUp : 0;
        var input = new NativeMethods.Input(NativeMethods.InputKeyboard, new NativeMethods.InputUnion {
                Keyboard = new NativeMethods.KeyboardInput { Vk = (ushort)key, Scan = scan, Flags = flags }
        });
        return SendInputEvent(input);
    }

    private static uint SendInputScanCode(ushort scan, bool isKeyUp)
    {
        uint flags = NativeMethods.KeyEventFlags_Scancode;
        if (isKeyUp) flags |= NativeMethods.KeyEventFlags_KeyUp;
        var input = new NativeMethods.Input(NativeMethods.InputKeyboard, new NativeMethods.InputUnion {
                Keyboard = new NativeMethods.KeyboardInput { Vk = 0, Scan = scan, Flags = flags }
        });
        return SendInputEvent(input);
    }

    /// <summary>
    /// 通过 PostMessage 投递 WM_KEYDOWN / WM_KEYUP 到目标窗口。
    /// lParam 格式 (32 bit):
    ///   bits  0-15 : repeat count (1)
    ///   bits 16-23 : scan code
    ///   bit  24    : extended key flag (0)
    ///   bits 25-28 : reserved (0)
    ///   bit  29    : context code (0)
    ///   bit  30    : previous key state (0=down, 1=up)
    ///   bit  31    : transition state (0=down, 1=up)
    /// </summary>
    private bool PostMessageKey(VirtualKey key, ushort scan, bool isKeyUp)
    {
        var msg = isKeyUp ? NativeMethods.WindowMessage_KeyUp : NativeMethods.WindowMessage_KeyDown;

        nint lParam = 1 | ((nint)scan << 16);
        if (isKeyUp)
            lParam |= (nint)0xC0000000; // bits 30 + 31
        
        var result = NativeMethods.PostMessage(_hWnd, msg, (nint)key, lParam);
        if (!result)
            LogWin32Error("[Keyboard] PostMessage WM_KEY{Action} failed for {Key}", isKeyUp ? "UP" : "DOWN", key);
        return result;
    }
}
