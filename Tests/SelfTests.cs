using System;
using System.Drawing;
using System.Linq;

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
            TestSettingsMigration(ref failures);
            TestLocalization(ref failures);
            TestStartupRegistration(ref failures);
            TestIconResources(ref failures);
            TestWheelMessagePacking(ref failures);
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
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineUp, 1) == 30);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineDown, 5) == -120);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineUp, 10) == 540);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.PageUp, 5) == 840);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.PageDown, 10) == -3240);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineUp, 0) == 30);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineUp, 11) == 540);

            int previous = 0;
            for (int level = ScrollProfile.MinimumLevel; level <= ScrollProfile.MaximumLevel; level++)
            {
                int current = ScrollProfile.WheelDelta(ScrollCommand.LineUp, level);
                Check(ref failures, current > previous);
                previous = current;
            }
        }

        private static void TestSettingsMigration(ref int failures)
        {
            var defaults = new UtilitySettings();
            Check(ref failures, defaults.ScrollSpeedLevel == ScrollProfile.DefaultLevel);
            Check(ref failures, defaults.LanguageCode == LocalizationManager.SystemLanguageCode);
            Check(ref failures, defaults.SpaceScroll);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("Slow") == 2);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("Normal") == 5);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("Fast") == 8);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("0") == 1);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("99") == 10);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("invalid") == ScrollProfile.DefaultLevel);
        }

        private static void TestLocalization(ref int failures)
        {
            var localizer = new LocalizationManager("en");
            string error;
            Check(ref failures, localizer.ValidateAll(out error));
            Check(ref failures, localizer.Languages.Count() >= 18);

            localizer.SetLanguage("ru");
            Check(ref failures, localizer.Text(UiText.MenuExit) == "Выход");
            localizer.SetLanguage("zh-CN");
            Check(ref failures, localizer.EffectiveLanguageCode == "zh-Hans");
            localizer.SetLanguage("unknown");
            Check(ref failures, localizer.EffectiveLanguageCode == "en");
            localizer.SetLanguage(LocalizationManager.SystemLanguageCode);
            Check(ref failures, localizer.RequestedLanguageCode == LocalizationManager.SystemLanguageCode);
        }

        private static void TestStartupRegistration(ref int failures)
        {
            const string path = @"C:\Tools\CodexKeyboardScroll.exe";
            string command = StartupRegistration.CommandForExecutable(path);
            Check(ref failures, command == "\"" + path + "\"");
            Check(ref failures, StartupRegistration.IsCommandForExecutable(command, path));
            Check(ref failures, !StartupRegistration.IsCommandForExecutable(
                "\"C:\\Other\\CodexKeyboardScroll.exe\"",
                path));
        }

        private static void TestIconResources(ref int failures)
        {
            using (var icons = new AppIconSet())
            {
                Check(ref failures, icons.Active != null);
                Check(ref failures, icons.Waiting != null);
                Check(ref failures, icons.Active.Width >= 16);
                Check(ref failures, icons.Waiting.Width >= 16);
            }
        }

        private static void TestWheelMessagePacking(ref int failures)
        {
            Check(ref failures, LowBits(NativeInput.PackWheelWParam(120)) == 0x00780000);
            Check(ref failures, LowBits(NativeInput.PackWheelWParam(-120)) == 0xFF880000);
            Check(ref failures, LowBits(NativeInput.PackScreenPoint(new Point(17, 34))) == 0x00220011);
            Check(ref failures, LowBits(NativeInput.PackScreenPoint(new Point(-10, -20))) == 0xFFECFFF6);
        }

        private static uint LowBits(IntPtr value)
        {
            return unchecked((uint)value.ToInt64());
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
