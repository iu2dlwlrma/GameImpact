using GameImpact.Abstractions.Input;
using GameImpact.Input.Native;

namespace GameImpact.Input;

public partial class SendInputSimulator : IInputSimulator
{
    private nint _hWnd;

    public IKeyboardInput Keyboard => this;
    public IMouseInput Mouse => this;

    /// <summary>
    /// 设置后台操作的目标窗口句柄。
    /// </summary>
    public void SetWindowHandle(nint windowHandle)
    {
        _hWnd = windowHandle;
    }
    
    private static uint SendInputEvent(NativeMethods.Input input)
    {
        return NativeMethods.SendInput(1, [input], NativeMethods.Input.Size);
    }
}
