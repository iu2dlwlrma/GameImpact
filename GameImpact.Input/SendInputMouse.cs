using System.Runtime.InteropServices;
using GameImpact.Abstractions.Input;
using GameImpact.Input.Native;
using GameImpact.Utilities.Logging;

namespace GameImpact.Input;

public class SendInputMouse : IMouseInput
{
    public IMouseInput MoveTo(int x, int y)
    {
        Log.Debug("[Mouse] MoveTo: ({X}, {Y})", x, y);
        NativeMethods.SetCursorPos(x, y);
        return this;
    }

    public IMouseInput MoveBy(int dx, int dy)
    {
        Log.Debug("[Mouse] MoveBy: ({Dx}, {Dy})", dx, dy);
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = NativeMethods.MOUSEEVENTF_MOVE
                }
            }
        };
        NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
        return this;
    }

    public IMouseInput LeftClick()
    {
        Log.Debug("[Mouse] LeftClick");
        LeftDown();
        Thread.Sleep(50);
        LeftUp();
        return this;
    }

    public IMouseInput LeftDown()
    {
        SendMouseEvent(NativeMethods.MOUSEEVENTF_LEFTDOWN);
        return this;
    }

    public IMouseInput LeftUp()
    {
        SendMouseEvent(NativeMethods.MOUSEEVENTF_LEFTUP);
        return this;
    }

    public IMouseInput RightClick()
    {
        Log.Debug("[Mouse] RightClick");
        RightDown();
        Thread.Sleep(50);
        RightUp();
        return this;
    }

    public IMouseInput RightDown()
    {
        SendMouseEvent(NativeMethods.MOUSEEVENTF_RIGHTDOWN);
        return this;
    }

    public IMouseInput RightUp()
    {
        SendMouseEvent(NativeMethods.MOUSEEVENTF_RIGHTUP);
        return this;
    }

    public IMouseInput MiddleClick()
    {
        Log.Debug("[Mouse] MiddleClick");
        SendMouseEvent(NativeMethods.MOUSEEVENTF_MIDDLEDOWN);
        Thread.Sleep(50);
        SendMouseEvent(NativeMethods.MOUSEEVENTF_MIDDLEUP);
        return this;
    }

    public IMouseInput Scroll(int delta)
    {
        Log.Debug("[Mouse] Scroll: {Delta}", delta);
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    mouseData = (uint)delta,
                    dwFlags = NativeMethods.MOUSEEVENTF_WHEEL
                }
            }
        };
        NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
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

    private static void SendMouseEvent(uint flags)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            U = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT { dwFlags = flags }
            }
        };
        NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
