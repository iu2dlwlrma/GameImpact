using GameImpact.Abstractions.Input;
using GameImpact.Input.Native;
using GameImpact.Utilities.Logging;

namespace GameImpact.Input;

public class PostMessageInput : IWindowInput
{
    private readonly nint _hWnd;

    public PostMessageInput(nint windowHandle)
    {
        _hWnd = windowHandle;
        Log.Debug("[PostMessage] Created for window 0x{Handle:X}", windowHandle);
    }

    public IWindowInput KeyPress(VirtualKey key)
    {
        Log.Debug("[PostMessage] KeyPress: {Key}", key);
        NativeMethods.PostMessage(_hWnd, NativeMethods.WM_KEYDOWN, (nint)key, 0x1e0001);
        NativeMethods.PostMessage(_hWnd, NativeMethods.WM_CHAR, (nint)key, 0x1e0001);
        NativeMethods.PostMessage(_hWnd, NativeMethods.WM_KEYUP, (nint)key, unchecked((nint)0xc01e0001));
        return this;
    }

    public IWindowInput KeyDown(VirtualKey key)
    {
        Log.Debug("[PostMessage] KeyDown: {Key}", key);
        NativeMethods.PostMessage(_hWnd, NativeMethods.WM_KEYDOWN, (nint)key, 0x1e0001);
        return this;
    }

    public IWindowInput KeyUp(VirtualKey key)
    {
        Log.Debug("[PostMessage] KeyUp: {Key}", key);
        NativeMethods.PostMessage(_hWnd, NativeMethods.WM_KEYUP, (nint)key, unchecked((nint)0xc01e0001));
        return this;
    }

    public IWindowInput LeftClick(int x, int y)
    {
        Log.Debug("[PostMessage] LeftClick: ({X}, {Y})", x, y);
        nint lParam = (y << 16) | (x & 0xFFFF);
        NativeMethods.PostMessage(_hWnd, NativeMethods.WM_LBUTTONDOWN, nint.Zero, lParam);
        Thread.Sleep(100);
        NativeMethods.PostMessage(_hWnd, NativeMethods.WM_LBUTTONUP, nint.Zero, lParam);
        return this;
    }

    public IWindowInput RightClick(int x, int y)
    {
        Log.Debug("[PostMessage] RightClick: ({X}, {Y})", x, y);
        nint lParam = (y << 16) | (x & 0xFFFF);
        NativeMethods.PostMessage(_hWnd, NativeMethods.WM_RBUTTONDOWN, nint.Zero, lParam);
        Thread.Sleep(100);
        NativeMethods.PostMessage(_hWnd, NativeMethods.WM_RBUTTONUP, nint.Zero, lParam);
        return this;
    }

    public IWindowInput Sleep(int milliseconds)
    {
        Thread.Sleep(milliseconds);
        return this;
    }
}
