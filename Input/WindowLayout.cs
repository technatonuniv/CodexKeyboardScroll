using System;
using System.Drawing;

namespace CodexKeyboardScroll
{
    internal static class WindowLayout
    {
        internal static bool IsTranscriptClick(NativeInput.Rect rect, Point point)
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 320 || height < 300
                || point.X < rect.Left || point.X >= rect.Right
                || point.Y < rect.Top || point.Y >= rect.Bottom)
            {
                return false;
            }

            // Exclude the navigation rail and composer while keeping the central
            // transcript usable across both narrow and maximized window layouts.
            int leftGuard = width < 900 ? 24 : Math.Max(220, (int)(width * 0.16));
            int bottomGuard = Math.Max(150, Math.Min(280, (int)(height * 0.22)));
            return point.X >= rect.Left + leftGuard
                && point.X < rect.Right - 20
                && point.Y >= rect.Top + 55
                && point.Y < rect.Bottom - bottomGuard;
        }

        internal static Point ScrollTarget(NativeInput.Rect rect)
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            return new Point(rect.Left + (int)(width * 0.55), rect.Top + (int)(height * 0.42));
        }

        internal static Point ComposerTarget(NativeInput.Rect rect)
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            int offset = Math.Max(70, Math.Min(115, (int)(height * 0.10)));
            return new Point(rect.Left + (int)(width * 0.55), rect.Bottom - offset);
        }
    }
}
