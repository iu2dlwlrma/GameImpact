using GameImpact.Abstractions.Input;
using GameImpact.Input.Native;

namespace GameImpact.Input;

public class MouseEventInput : IMouseInput
{
    public IMouseInput MoveTo(int x, int y)
    {
        NativeMethods.SetCursorPos(x, y);
        return this;
    }

    public IMouseInput MoveBy(int dx, int dy)
    {
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MOVE, dx, dy, 0, UIntPtr.Zero);
        return this;
    }

    public IMouseInput LeftClick()
    {
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(50);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        return this;
    }

    public IMouseInput LeftDown()
    {
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        return this;
    }

    public IMouseInput LeftUp()
    {
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        return this;
    }

    public IMouseInput RightClick()
    {
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(50);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        return this;
    }

    public IMouseInput RightDown()
    {
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
        return this;
    }

    public IMouseInput RightUp()
    {
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        return this;
    }

    public IMouseInput MiddleClick()
    {
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(50);
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
        return this;
    }

    public IMouseInput Scroll(int delta)
    {
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, (uint)delta, UIntPtr.Zero);
        return this;
    }

    public IMouseInput Sleep(int milliseconds)
    {
        Thread.Sleep(milliseconds);
        return this;
    }

    public (int X, int Y) GetPosition()
    {
        NativeMethods.GetCursorPos(out var point);
        return (point.X, point.Y);
    }
}
