namespace CodexKeyboardScroll
{
    internal static class ScrollProfile
    {
        internal static int WheelDelta(ScrollCommand command, ScrollSpeed speed)
        {
            int line = speed == ScrollSpeed.Slow ? 80
                : speed == ScrollSpeed.Fast ? NativeInput.WheelDelta * 2
                : NativeInput.WheelDelta;
            int page = speed == ScrollSpeed.Slow ? 4 : speed == ScrollSpeed.Fast ? 10 : 7;

            switch (command)
            {
                case ScrollCommand.LineUp: return line;
                case ScrollCommand.LineDown: return -line;
                case ScrollCommand.PageUp: return NativeInput.WheelDelta * page;
                default: return -NativeInput.WheelDelta * page;
            }
        }
    }
}
