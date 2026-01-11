namespace GameImpact.Abstractions.Input;

public interface IInputSimulator
{
    IKeyboardInput Keyboard { get; }
    IMouseInput Mouse { get; }
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
    IMouseInput MoveTo(int x, int y);
    IMouseInput MoveBy(int dx, int dy);
    IMouseInput LeftClick();
    IMouseInput LeftDown();
    IMouseInput LeftUp();
    IMouseInput RightClick();
    IMouseInput RightDown();
    IMouseInput RightUp();
    IMouseInput MiddleClick();
    IMouseInput Scroll(int delta);
    IMouseInput Sleep(int milliseconds);
    (int X, int Y) GetPosition();
}

public interface IWindowInput
{
    IWindowInput KeyPress(VirtualKey key);
    IWindowInput KeyDown(VirtualKey key);
    IWindowInput KeyUp(VirtualKey key);
    IWindowInput LeftClick(int x, int y);
    IWindowInput RightClick(int x, int y);
    IWindowInput Sleep(int milliseconds);
}
