#region

using GameImpact.Abstractions.Input;

#endregion

namespace GameImpact.Input
{
    public static class InputFactory
    {
        public static IInputSimulator CreateSendInput()
        {
            return new SendInputSimulator();
        }
    }
}
