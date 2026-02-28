using System.Runtime.InteropServices;
using GameImpact.Abstractions.Input;
using GameImpact.Input.Native;
using GameImpact.Utilities.Logging;

namespace GameImpact.Input;

public partial class SendInputSimulator : IKeyboardInput
{
    /// <summary>
    /// 按下指定按键
    /// </summary>
    /// <param name="key">虚拟键码</param>
    /// <returns>当前实例，支持链式调用</returns>
    public IKeyboardInput KeyDown(VirtualKey key)
    {
        Log.Debug("[Keyboard] KeyDown: {Key}", key);
        SendKeyAction(key, false);
        return this;
    }

    /// <summary>
    /// 释放指定按键
    /// </summary>
    /// <param name="key">虚拟键码</param>
    /// <returns>当前实例，支持链式调用</returns>
    public IKeyboardInput KeyUp(VirtualKey key)
    {
        Log.Debug("[Keyboard] KeyUp: {Key}", key);
        SendKeyAction(key, true);
        return this;
    }

    /// <summary>
    /// 按下并释放指定按键（单次按键）
    /// </summary>
    /// <param name="key">虚拟键码</param>
    /// <returns>当前实例，支持链式调用</returns>
    public IKeyboardInput KeyPress(VirtualKey key)
    {
        Log.Debug("[Keyboard] KeyPress: {Key}", key);
        SendKeyAction(key, false);
        Thread.Sleep(50);
        SendKeyAction(key, true);
        return this;
    }

    /// <summary>
    /// 依次按下并释放多个按键
    /// </summary>
    /// <param name="keys">虚拟键码数组</param>
    /// <returns>当前实例，支持链式调用</returns>
    public IKeyboardInput KeyPress(params VirtualKey[] keys)
    {
        foreach (var key in keys)
        {
            KeyPress(key);
        }
        return this;
    }

    /// <summary>
    /// 执行组合键操作（修饰键+主键）
    /// </summary>
    /// <param name="modifier">修饰键（如 Ctrl、Alt）</param>
    /// <param name="key">主键</param>
    /// <returns>当前实例，支持链式调用</returns>
    public IKeyboardInput ModifiedKeyStroke(VirtualKey modifier, VirtualKey key)
    {
        Log.Debug("[Keyboard] ModifiedKeyStroke: {Modifier}+{Key}", modifier, key);
        KeyDown(modifier);
        KeyPress(key);
        KeyUp(modifier);
        return this;
    }

    /// <summary>
    /// 输入文本字符串（使用 Unicode 编码）
    /// </summary>
    /// <param name="text">要输入的文本</param>
    /// <returns>当前实例，支持链式调用</returns>
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
            {
                LogWin32Error("[Keyboard] SendInput TextEntry failed for char '{Char}'", c);
            }
        }
        return this;
    }

    /// <summary>
    /// 等待指定毫秒数
    /// </summary>
    /// <param name="milliseconds">等待的毫秒数</param>
    /// <returns>当前实例，支持链式调用</returns>
    IKeyboardInput IKeyboardInput.Sleep(int milliseconds)
    {
        Thread.Sleep(milliseconds);
        return this;
    }

    /// <summary>
    /// 发送按键动作（按下或释放）
    /// </summary>
    /// <param name="key">虚拟键码</param>
    /// <param name="isKeyUp">是否为释放动作</param>
    /// <returns>成功发送的事件数量</returns>
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

    /// <summary>
    /// 通过 SendInput 发送虚拟键码
    /// </summary>
    /// <param name="key">虚拟键码</param>
    /// <param name="scan">扫描码</param>
    /// <param name="isKeyUp">是否为释放动作</param>
    /// <returns>成功发送的事件数量</returns>
    private static uint SendInputVk(VirtualKey key, ushort scan, bool isKeyUp)
    {
        uint flags = isKeyUp ? NativeMethods.KeyEventFlags_KeyUp : 0;
        var input = new NativeMethods.Input(NativeMethods.InputKeyboard, new NativeMethods.InputUnion {
                Keyboard = new NativeMethods.KeyboardInput { Vk = (ushort)key, Scan = scan, Flags = flags }
        });
        return SendInputEvent(input);
    }

    /// <summary>
    /// 通过 SendInput 发送扫描码
    /// </summary>
    /// <param name="scan">扫描码</param>
    /// <param name="isKeyUp">是否为释放动作</param>
    /// <returns>成功发送的事件数量</returns>
    private static uint SendInputScanCode(ushort scan, bool isKeyUp)
    {
        uint flags = NativeMethods.KeyEventFlags_Scancode;
        if (isKeyUp)
        {
            flags |= NativeMethods.KeyEventFlags_KeyUp;
        }
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
    /// <param name="key">虚拟键码</param>
    /// <param name="scan">扫描码</param>
    /// <param name="isKeyUp">是否为释放动作</param>
    /// <returns>是否成功</returns>
    private bool PostMessageKey(VirtualKey key, ushort scan, bool isKeyUp)
    {
        var msg = isKeyUp ? NativeMethods.WindowMessage_KeyUp : NativeMethods.WindowMessage_KeyDown;

        nint lParam = 1 | ((nint)scan << 16);
        if (isKeyUp)
        {
            lParam |= (nint)0xC0000000; // bits 30 + 31
        }
        
        var result = NativeMethods.PostMessage(m_hWnd, msg, (nint)key, lParam);
        if (!result)
        {
            LogWin32Error("[Keyboard] PostMessage WM_KEY{Action} failed for {Key}", isKeyUp ? "UP" : "DOWN", key);
        }
        return result;
    }
}
