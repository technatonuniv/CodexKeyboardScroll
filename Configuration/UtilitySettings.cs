using System;
using System.Globalization;
using System.IO;

namespace CodexKeyboardScroll
{
    internal sealed class UtilitySettings
    {
        private const string FileName = "CodexKeyboardScroll.settings.ini";

        internal FocusShortcut FocusShortcut = FocusShortcut.AltF;
        internal decimal ScrollSpeedLevel = ScrollProfile.DefaultLevel;
        internal bool SpaceScroll = true;
        internal string LanguageCode = LocalizationManager.SystemLanguageCode;
        internal bool AutomaticUpdateChecks;
        internal DateTime? LastUpdateCheckUtc;
        internal string LatestKnownVersion = string.Empty;

        private static string SettingsPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName); }
        }

        internal static UtilitySettings Load()
        {
            var settings = new UtilitySettings();
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return settings;
                }

                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    string[] parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length == 2)
                    {
                        Apply(settings, parts[0].Trim(), parts[1].Trim());
                    }
                }
            }
            catch
            {
                // Invalid settings must never prevent the utility from starting.
            }
            return settings;
        }

        internal static void Apply(UtilitySettings settings, string key, string value)
        {
            FocusShortcut shortcut;
            bool flag;
            DateTime timestamp;
            if (key.Equals("FocusShortcut", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse(value, true, out shortcut))
            {
                settings.FocusShortcut = shortcut;
            }
            else if (key.Equals("ScrollSpeed", StringComparison.OrdinalIgnoreCase))
            {
                settings.ScrollSpeedLevel = ParseScrollSpeed(value);
            }
            else if (key.Equals("SpaceScroll", StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(value, out flag))
            {
                settings.SpaceScroll = flag;
            }
            else if (key.Equals("Language", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(value))
            {
                settings.LanguageCode = value;
            }
            else if (key.Equals("AutomaticUpdateChecks", StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(value, out flag))
            {
                settings.AutomaticUpdateChecks = flag;
            }
            else if (key.Equals("LastUpdateCheckUtc", StringComparison.OrdinalIgnoreCase)
                && DateTime.TryParseExact(
                    value,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out timestamp))
            {
                settings.LastUpdateCheckUtc = timestamp.ToUniversalTime();
            }
            else if (key.Equals("LatestKnownVersion", StringComparison.OrdinalIgnoreCase))
            {
                settings.LatestKnownVersion = value;
            }
        }

        internal bool Save()
        {
            string temporary = SettingsPath + ".tmp";
            try
            {
                File.WriteAllLines(temporary, Serialize());
                if (File.Exists(SettingsPath))
                {
                    File.Replace(temporary, SettingsPath, null);
                }
                else
                {
                    File.Move(temporary, SettingsPath);
                }
                return true;
            }
            catch
            {
                try { File.Delete(temporary); }
                catch { }
                return false;
            }
        }

        internal string[] Serialize()
        {
            return new[]
            {
                "FocusShortcut=" + FocusShortcut,
                "ScrollSpeed=" + ScrollProfile.DisplayLevel(ScrollSpeedLevel),
                "SpaceScroll=" + SpaceScroll,
                "Language=" + LanguageCode,
                "AutomaticUpdateChecks=" + AutomaticUpdateChecks,
                "LastUpdateCheckUtc=" + (LastUpdateCheckUtc.HasValue
                    ? LastUpdateCheckUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                    : string.Empty),
                "LatestKnownVersion=" + (LatestKnownVersion ?? string.Empty)
            };
        }

        internal static decimal ParseScrollSpeed(string value)
        {
            decimal level;
            if (decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out level))
            {
                return ScrollProfile.NormalizeLevel(level);
            }

            // Preserve settings written by versions earlier than 2.0.
            if (value.Equals("Slow", StringComparison.OrdinalIgnoreCase)) return 2m;
            if (value.Equals("Fast", StringComparison.OrdinalIgnoreCase)) return 8m;
            return ScrollProfile.DefaultLevel;
        }

        internal static string ShortcutText(FocusShortcut shortcut)
        {
            switch (shortcut)
            {
                case FocusShortcut.CtrlAltF: return "Ctrl+Alt+F";
                case FocusShortcut.CtrlShiftF: return "Ctrl+Shift+F";
                default: return "Alt+F";
            }
        }

        internal static bool ShortcutUsesAlt(FocusShortcut shortcut)
        {
            return shortcut != FocusShortcut.CtrlShiftF;
        }
    }
}
