namespace CodexKeyboardScroll
{
    internal static class ScrollProfile
    {
        internal const int MinimumLevel = 1;
        internal const int MaximumLevel = 10;
        internal const int DefaultLevel = 5;

        private static readonly int[] LineDeltas =
        {
            30, 45, 60, 80, 120, 160, 220, 300, 400, 540
        };

        private static readonly int[] PageDeltas =
        {
            240, 360, 480, 600, 840, 1080, 1440, 1800, 2400, 3240
        };

        internal static int ClampLevel(int level)
        {
            return level < MinimumLevel ? MinimumLevel
                : level > MaximumLevel ? MaximumLevel
                : level;
        }

        internal static int WheelDelta(ScrollCommand command, int speedLevel)
        {
            int index = ClampLevel(speedLevel) - MinimumLevel;
            int line = LineDeltas[index];
            int page = PageDeltas[index];

            switch (command)
            {
                case ScrollCommand.LineUp: return line;
                case ScrollCommand.LineDown: return -line;
                case ScrollCommand.PageUp: return page;
                default: return -page;
            }
        }
    }
}
