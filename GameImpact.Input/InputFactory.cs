using GameImpact.Abstractions.Input;

namespace GameImpact.Input;

public static class InputFactory
{
    public static IInputSimulator CreateSendInput() => new SendInputSimulator();
    public static IMouseInput CreateMouseEvent() => new MouseEventInput();
    public static IWindowInput CreatePostMessage(nint windowHandle) => new PostMessageInput(windowHandle);
}
