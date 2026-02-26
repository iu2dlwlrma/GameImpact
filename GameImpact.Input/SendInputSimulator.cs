using System.Runtime.InteropServices;
using GameImpact.Abstractions.Input;
using GameImpact.Input.Native;
using GameImpact.Utilities.Logging;

namespace GameImpact.Input;

/// <summary>
/// 键盘输入的发送方式。
/// </summary>
public enum KeyboardInputMode
{
    /// <summary>通过 SendInput API 注入虚拟键码（默认）。</summary>
    SendInputVk,
    /// <summary>通过 SendInput API 注入硬件扫描码。</summary>
    SendInputScanCode,
    /// <summary>通过 PostMessage 直接向目标窗口投递 WM_KEYDOWN/WM_KEYUP 消息。
    /// 绕过 Raw Input，适用于 SendInput 被游戏忽略的场景。</summary>
    PostMessage,
}

public partial class SendInputSimulator : IInputSimulator
{
    private nint _hWnd;

    public IKeyboardInput Keyboard => this;
    public IMouseInput Mouse => this;

    /// <summary>
    /// 键盘输入的发送方式，默认 <see cref="KeyboardInputMode.SendInputVk"/>。
    /// 如果游戏不响应 SendInput，可切换为 <see cref="KeyboardInputMode.PostMessage"/>。
    /// </summary>
    public KeyboardInputMode KeyboardMode { get; set; } = KeyboardInputMode.SendInputVk;

    /// <summary>
    /// 设置后台操作的目标窗口句柄。
    /// </summary>
    public void SetWindowHandle(nint windowHandle)
    {
        _hWnd = windowHandle;
    }

    private static uint SendInputEvent(NativeMethods.Input input)
    {
        var result = NativeMethods.SendInput(1, [input], NativeMethods.Input.Size);
        if (result == 0)
            LogWin32Error("[SendInput] Failed");
        return result;
    }

    private static void LogWin32Error(string message, params object[] args)
    {
        var error = Marshal.GetLastWin32Error();
        var fullArgs = new object[args.Length + 2];
        args.CopyTo(fullArgs, 0);
        fullArgs[^2] = error;
        fullArgs[^1] = error;
        Log.Warn(message + ", Win32 error: {ErrorCode} (0x{ErrorHex:X8})", fullArgs);
        InputDiagnostics.LogLoadedModules();
    }
}
