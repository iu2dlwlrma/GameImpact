namespace GameImpact.Abstractions.Input;

/// <summary>
/// 统一输入模拟器。调用 <see cref="SetWindowHandle"/> 后，键盘和鼠标操作均通过
/// PostMessage 投递到目标窗口，无需前台焦点，也不会移动鼠标光标。
/// </summary>
public interface IInputSimulator
{
    IKeyboardInput Keyboard { get; }
    IMouseInput Mouse { get; }

    /// <summary>设置后台操作的目标窗口句柄（BackgroundClickAt / KeyPress 等 PostMessage 操作的目标）</summary>
    void SetWindowHandle(nint windowHandle);
}

public interface IKeyboardInput
{
    IKeyboardInput KeyDown(VirtualKey key);
    IKeyboardInput KeyUp(VirtualKey key);
    IKeyboardInput KeyPress(VirtualKey key);
    IKeyboardInput KeyPress(params VirtualKey[] keys);
    IKeyboardInput ModifiedKeyStroke(VirtualKey modifier, VirtualKey key);
    IKeyboardInput TextEntry(string text);
    IKeyboardInput Sleep(int milliseconds);
}

public interface IMouseInput
{
    /// <summary>
    /// 后台点击（PostMessage）：向目标窗口发送 WM_LBUTTONDOWN/UP，无需移动光标。
    /// 适用于处理 WM 消息的窗口；对 DirectInput / Raw Input 游戏无效。
    /// </summary>
    bool BackgroundClickAt(int x, int y);

    /// <summary>
    /// 前台点击（SendInput）：激活目标窗口，将鼠标移动到客户端坐标处后执行真实点击。
    /// 对所有游戏均有效，但会切换窗口焦点并移动鼠标光标。
    /// </summary>
    bool ForegroundClickAt(int x, int y);

    /// <summary>移动鼠标光标到屏幕绝对坐标。</summary>
    IMouseInput MoveTo(int x, int y);

    /// <summary>在当前光标位置执行左键单击（SendInput）。</summary>
    IMouseInput LeftClick();

    /// <summary>在当前光标位置执行右键单击（SendInput）。</summary>
    IMouseInput RightClick();

    /// <summary>滚动鼠标滚轮（正值向上，负值向下）。</summary>
    IMouseInput Scroll(int delta);

    IMouseInput Sleep(int milliseconds);
}
