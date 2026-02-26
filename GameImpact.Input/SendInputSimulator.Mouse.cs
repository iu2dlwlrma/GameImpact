using GameImpact.Abstractions.Input;
using GameImpact.Input.Native;
using GameImpact.Utilities.Logging;

namespace GameImpact.Input;

public partial class SendInputSimulator : IMouseInput
{
    public bool BackgroundClickAt(int x, int y)
    {
        var lParam = NativeMethods.MakeLParam(x, y);
        var downResult = NativeMethods.PostMessage(_hWnd, NativeMethods.WindowMessage_LeftButtonDown, NativeMethods.MouseKey_LeftButton, lParam);
        Thread.Sleep(50);
        var upResult = NativeMethods.PostMessage(_hWnd, NativeMethods.WindowMessage_LeftButtonUp, 0, lParam);

        Log.Debug("[Mouse] BackgroundClickAt (PostMessage): ({X}, {Y}, {Result})", x, y, downResult && upResult);
        return downResult && upResult;
    }

    public bool ForegroundClickAt(int x, int y)
    {
        var pt = new NativeMethods.Point(x, y);
        NativeMethods.ClientToScreen(_hWnd, ref pt);
        NativeMethods.SetCursorPos(pt.X, pt.Y);
        Thread.Sleep(30);
        var downResult = SendMouseEvent(NativeMethods.MouseEventFlags_LeftDown);
        Thread.Sleep(50);
        var upResult = SendMouseEvent(NativeMethods.MouseEventFlags_LeftUp);

        Log.Debug("[Mouse] ForegroundClickAt (SendInput): ({X}, {Y}, {Result})", x, y, downResult > 0 && upResult > 0);
        return downResult > 0 && upResult > 0;
    }

    public IMouseInput MoveTo(int x, int y)
    {
        Log.Debug("[Mouse] MoveTo: ({X}, {Y})", x, y);
        NativeMethods.SetCursorPos(x, y);
        return this;
    }

    public IMouseInput LeftClick()
    {
        Log.Debug("[Mouse] LeftClick");
        SendMouseEvent(NativeMethods.MouseEventFlags_LeftDown);
        Thread.Sleep(50);
        SendMouseEvent(NativeMethods.MouseEventFlags_LeftUp);
        return this;
    }

    public IMouseInput RightClick()
    {
        Log.Debug("[Mouse] RightClick");
        SendMouseEvent(NativeMethods.MouseEventFlags_RightDown);
        Thread.Sleep(50);
        SendMouseEvent(NativeMethods.MouseEventFlags_RightUp);
        return this;
    }

    public IMouseInput Scroll(int delta)
    {
        Log.Debug("[Mouse] Scroll: {Delta}", delta);
        var input = new NativeMethods.Input(NativeMethods.InputMouse, new NativeMethods.InputUnion {
                Mouse = new NativeMethods.MouseInput {
                        MouseData = (uint)delta,
                        Flags = NativeMethods.MouseEventFlags_Wheel
                }
        });
        _ = NativeMethods.SendInput(1, [input], NativeMethods.Input.Size);
        return this;
    }

    IMouseInput IMouseInput.Sleep(int milliseconds)
    {
        Thread.Sleep(milliseconds);
        return this;
    }
    
    private static uint SendMouseEvent(uint flags)
    {
        var input = new NativeMethods.Input(NativeMethods.InputMouse, new NativeMethods.InputUnion {
                Mouse = new NativeMethods.MouseInput { Flags = flags }
        });
        return SendInputEvent(input);
    }
}
