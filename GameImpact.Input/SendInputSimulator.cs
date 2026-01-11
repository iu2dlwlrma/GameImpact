using GameImpact.Abstractions.Input;

namespace GameImpact.Input;

public class SendInputSimulator : IInputSimulator
{
    public IKeyboardInput Keyboard { get; }
    public IMouseInput Mouse { get; }

    public SendInputSimulator()
    {
        Keyboard = new SendInputKeyboard();
        Mouse = new SendInputMouse();
    }
}
