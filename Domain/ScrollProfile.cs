using System;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CodexKeyboardScroll
{
    internal static class ScrollProfile
    {
        internal const decimal MinimumLevel = 0.25m;
        internal const decimal MaximumLevel = 10m;
        internal const decimal DefaultLevel = 5m;

        private static readonly decimal[] Levels =
        {
            0.25m, 0.5m, 1m, 2m, 3m, 4m, 5m, 6m, 7m, 8m, 9m, 10m
        };

        // Level 1 uses integer-friendly deltas so 0.5 and 0.25 remain exact ratios.
        private static readonly int[] LineDeltas =
        {
            8, 16, 32, 45, 60, 80, 120, 160, 220, 300, 400, 540
        };

        private static readonly int[] PageDeltas =
        {
            60, 120, 240, 360, 480, 600, 840, 1080, 1440, 1800, 2400, 3240
        };

        internal static readonly ReadOnlyCollection<decimal> SupportedLevels =
            Array.AsReadOnly(Levels);

        internal static decimal NormalizeLevel(decimal level)
        {
            decimal closest = Levels[0];
            decimal distance = Math.Abs(level - closest);
            for (int index = 1; index < Levels.Length; index++)
            {
                decimal candidateDistance = Math.Abs(level - Levels[index]);
                if (candidateDistance < distance)
                {
                    closest = Levels[index];
                    distance = candidateDistance;
                }
            }
            return closest;
        }

        internal static int WheelDelta(ScrollCommand command, decimal speedLevel)
        {
            decimal normalized = NormalizeLevel(speedLevel);
            int index = Array.IndexOf(Levels, normalized);
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

        internal static string DisplayLevel(decimal level)
        {
            return NormalizeLevel(level).ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
