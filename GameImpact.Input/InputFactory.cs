using GameImpact.Abstractions.Input;

namespace GameImpact.Input;

public static class InputFactory
{
    public static IInputSimulator CreateSendInput() => new SendInputSimulator();
}
