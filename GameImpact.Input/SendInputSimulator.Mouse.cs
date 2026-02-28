using System.Runtime.InteropServices;
using GameImpact.Abstractions.Input;
using GameImpact.Input.Native;
using GameImpact.Utilities.Logging;

namespace GameImpact.Input;

public partial class SendInputSimulator : IMouseInput
{
    /// <summary>
    /// 在后台窗口的指定位置执行鼠标点击（使用 PostMessage）
    /// </summary>
    /// <param name="x">X坐标（客户区坐标）</param>
    /// <param name="y">Y坐标（客户区坐标）</param>
    /// <returns>是否成功</returns>
    public bool BackgroundClickAt(int x, int y)
    {
        var lParam = NativeMethods.MakeLParam(x, y);
        var downResult = NativeMethods.PostMessage(m_hWnd, NativeMethods.WindowMessage_LeftButtonDown, NativeMethods.MouseKey_LeftButton, lParam);
        if (!downResult)
        {
            LogWin32Error("[Mouse] PostMessage WM_LBUTTONDOWN failed at ({X}, {Y})", x, y);
        }
        Thread.Sleep(50);
        var upResult = NativeMethods.PostMessage(m_hWnd, NativeMethods.WindowMessage_LeftButtonUp, 0, lParam);
        if (!upResult)
        {
            LogWin32Error("[Mouse] PostMessage WM_LBUTTONUP failed at ({X}, {Y})", x, y);
        }

        Log.Debug("[Mouse] BackgroundClickAt (PostMessage): ({X}, {Y}, {Result})", x, y, downResult && upResult);
        return downResult && upResult;
    }

    /// <summary>
    /// 在前台窗口的指定位置执行鼠标点击（使用 SendInput）
    /// </summary>
    /// <param name="x">X坐标（客户区坐标）</param>
    /// <param name="y">Y坐标（客户区坐标）</param>
    /// <returns>是否成功</returns>
    public bool ForegroundClickAt(int x, int y)
    {
        var pt = new NativeMethods.Point(x, y);
        if (!NativeMethods.ClientToScreen(m_hWnd, ref pt))
        {
            LogWin32Error("[Mouse] ClientToScreen failed for hWnd=0x{Hwnd:X}", m_hWnd);
        }
        if (!NativeMethods.SetCursorPos(pt.X, pt.Y))
        {
            LogWin32Error("[Mouse] SetCursorPos({X}, {Y}) failed", pt.X, pt.Y);
        }
        Thread.Sleep(30);
        var downResult = SendMouseEvent(NativeMethods.MouseEventFlags_LeftDown);
        Thread.Sleep(50);
        var upResult = SendMouseEvent(NativeMethods.MouseEventFlags_LeftUp);

        Log.Debug("[Mouse] ForegroundClickAt (SendInput): ({X}, {Y}, {Result})", x, y, downResult > 0 && upResult > 0);
        return downResult > 0 && upResult > 0;
    }

    /// <summary>
    /// 移动鼠标到指定位置（屏幕坐标）
    /// </summary>
    /// <param name="x">X坐标（屏幕坐标）</param>
    /// <param name="y">Y坐标（屏幕坐标）</param>
    /// <returns>是否成功</returns>
    public bool MoveTo(int x, int y)
    {
        Log.Debug("[Mouse] MoveTo: ({X}, {Y})", x, y);
        var result = NativeMethods.SetCursorPos(x, y);
        if (!result)
        {
            LogWin32Error("[Mouse] SetCursorPos({X}, {Y}) failed", x, y);
        }
        return result;
    }

    /// <summary>
    /// 执行左键点击
    /// </summary>
    /// <returns>当前实例，支持链式调用</returns>
    public IMouseInput LeftClick()
    {
        Log.Debug("[Mouse] LeftClick");
        SendMouseEvent(NativeMethods.MouseEventFlags_LeftDown);
        Thread.Sleep(50);
        SendMouseEvent(NativeMethods.MouseEventFlags_LeftUp);
        return this;
    }

    /// <summary>
    /// 执行右键点击
    /// </summary>
    /// <returns>当前实例，支持链式调用</returns>
    public IMouseInput RightClick()
    {
        Log.Debug("[Mouse] RightClick");
        SendMouseEvent(NativeMethods.MouseEventFlags_RightDown);
        Thread.Sleep(50);
        SendMouseEvent(NativeMethods.MouseEventFlags_RightUp);
        return this;
    }

    /// <summary>
    /// 执行鼠标滚轮滚动
    /// </summary>
    /// <param name="delta">滚动量，正数向上，负数向下</param>
    /// <returns>当前实例，支持链式调用</returns>
    public IMouseInput Scroll(int delta)
    {
        Log.Debug("[Mouse] Scroll: {Delta}", delta);
        var input = new NativeMethods.Input(NativeMethods.InputMouse, new NativeMethods.InputUnion {
                Mouse = new NativeMethods.MouseInput {
                        MouseData = (uint)delta,
                        Flags = NativeMethods.MouseEventFlags_Wheel
                }
        });
        var result = NativeMethods.SendInput(1, [input], NativeMethods.Input.Size);
        if (result == 0)
        {
            LogWin32Error("[Mouse] SendInput Scroll failed");
        }
        return this;
    }

    /// <summary>
    /// 等待指定毫秒数
    /// </summary>
    /// <param name="milliseconds">等待的毫秒数</param>
    /// <returns>当前实例，支持链式调用</returns>
    IMouseInput IMouseInput.Sleep(int milliseconds)
    {
        Thread.Sleep(milliseconds);
        return this;
    }
    
    /// <summary>
    /// 发送鼠标事件
    /// </summary>
    /// <param name="flags">鼠标事件标志</param>
    /// <returns>成功发送的事件数量</returns>
    private static uint SendMouseEvent(uint flags)
    {
        var input = new NativeMethods.Input(NativeMethods.InputMouse, new NativeMethods.InputUnion {
                Mouse = new NativeMethods.MouseInput { Flags = flags }
        });
        return SendInputEvent(input);
    }
}
