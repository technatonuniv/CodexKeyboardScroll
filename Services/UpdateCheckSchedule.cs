using System;

namespace CodexKeyboardScroll
{
    internal static class UpdateCheckSchedule
    {
        internal static readonly TimeSpan Interval = TimeSpan.FromDays(1);

        internal static bool IsDue(bool enabled, DateTime? lastCheckUtc, DateTime utcNow)
        {
            if (!enabled)
            {
                return false;
            }
            if (!lastCheckUtc.HasValue)
            {
                return true;
            }

            DateTime normalizedLastCheck = lastCheckUtc.Value.ToUniversalTime();
            DateTime normalizedNow = utcNow.ToUniversalTime();
            // A future timestamp usually means the system clock moved backwards.
            // Checking once repairs the persisted schedule instead of pausing indefinitely.
            return normalizedLastCheck > normalizedNow
                || normalizedNow - normalizedLastCheck >= Interval;
        }
    }
}
