using System;
using System.Drawing;

namespace CodexKeyboardScroll
{
    internal static class SelfTests
    {
        internal static int Run()
        {
            int failures = 0;
            TestWindowLayout(ref failures);
            TestTypingKeys(ref failures);
            TestShortcutLabels(ref failures);
            TestScrollProfile(ref failures);
            return failures;
        }

        private static void TestWindowLayout(ref int failures)
        {
            var normal = new NativeInput.Rect(100, 100, 1300, 900);
            Check(ref failures, WindowLayout.IsTranscriptClick(normal, new Point(700, 400)));
            Check(ref failures, !WindowLayout.IsTranscriptClick(normal, new Point(200, 400)));
            Check(ref failures, !WindowLayout.IsTranscriptClick(normal, new Point(700, 120)));
            Check(ref failures, !WindowLayout.IsTranscriptClick(normal, new Point(700, 850)));

            var narrow = new NativeInput.Rect(20, 30, 720, 730);
            Check(ref failures, WindowLayout.IsTranscriptClick(narrow, new Point(200, 300)));
            Check(ref failures, !WindowLayout.IsTranscriptClick(narrow, new Point(10, 300)));

            Point scroll = WindowLayout.ScrollTarget(normal);
            Point composer = WindowLayout.ComposerTarget(normal);
            Check(ref failures, Contains(normal, scroll));
            Check(ref failures, Contains(normal, composer));
            Check(ref failures, composer.Y > scroll.Y);
        }

        private static void TestTypingKeys(ref int failures)
        {
            Check(ref failures, PrintableKeyClassifier.IsTypingKey(0x41, true));
            Check(ref failures, PrintableKeyClassifier.IsTypingKey(0x30, true));
            Check(ref failures, PrintableKeyClassifier.IsTypingKey(0x60, true));
            Check(ref failures, PrintableKeyClassifier.IsTypingKey(0xBA, true));
            Check(ref failures, PrintableKeyClassifier.IsTypingKey(0x20, false));
            Check(ref failures, !PrintableKeyClassifier.IsTypingKey(0x20, true));
            Check(ref failures, !PrintableKeyClassifier.IsTypingKey(0x26, true));
            Check(ref failures, !PrintableKeyClassifier.IsTypingKey(0x70, true));
        }

        private static void TestShortcutLabels(ref int failures)
        {
            Check(ref failures, UtilitySettings.ShortcutText(FocusShortcut.AltF) == "Alt+F");
            Check(ref failures, UtilitySettings.ShortcutText(FocusShortcut.CtrlAltF) == "Ctrl+Alt+F");
            Check(ref failures, UtilitySettings.ShortcutText(FocusShortcut.CtrlShiftF) == "Ctrl+Shift+F");
            Check(ref failures, UtilitySettings.ShortcutUsesAlt(FocusShortcut.AltF));
            Check(ref failures, UtilitySettings.ShortcutUsesAlt(FocusShortcut.CtrlAltF));
            Check(ref failures, !UtilitySettings.ShortcutUsesAlt(FocusShortcut.CtrlShiftF));
        }

        private static void TestScrollProfile(ref int failures)
        {
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineUp, ScrollSpeed.Slow) == 80);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineDown, ScrollSpeed.Normal) == -120);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineUp, ScrollSpeed.Fast) == 240);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.PageUp, ScrollSpeed.Normal) == 840);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.PageDown, ScrollSpeed.Fast) == -1200);
        }

        private static bool Contains(NativeInput.Rect rect, Point point)
        {
            return point.X >= rect.Left && point.X < rect.Right
                && point.Y >= rect.Top && point.Y < rect.Bottom;
        }

        private static void Check(ref int failures, bool condition)
        {
            if (!condition)
            {
                failures++;
            }
        }
    }
}
