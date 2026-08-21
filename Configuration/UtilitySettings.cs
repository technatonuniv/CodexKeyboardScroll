using System;
using System.IO;

namespace CodexKeyboardScroll
{
    internal sealed class UtilitySettings
    {
        private const string FileName = "CodexKeyboardScroll.settings.ini";

        internal FocusShortcut FocusShortcut = FocusShortcut.AltF;
        internal ScrollSpeed ScrollSpeed = ScrollSpeed.Normal;
        internal bool SpaceScroll = true;

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

        private static void Apply(UtilitySettings settings, string key, string value)
        {
            FocusShortcut shortcut;
            ScrollSpeed speed;
            bool flag;
            if (key.Equals("FocusShortcut", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse(value, true, out shortcut))
            {
                settings.FocusShortcut = shortcut;
            }
            else if (key.Equals("ScrollSpeed", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse(value, true, out speed))
            {
                settings.ScrollSpeed = speed;
            }
            else if (key.Equals("SpaceScroll", StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(value, out flag))
            {
                settings.SpaceScroll = flag;
            }
        }

        internal void Save()
        {
            string temporary = SettingsPath + ".tmp";
            try
            {
                File.WriteAllLines(temporary, new[]
                {
                    "FocusShortcut=" + FocusShortcut,
                    "ScrollSpeed=" + ScrollSpeed,
                    "SpaceScroll=" + SpaceScroll
                });
                if (File.Exists(SettingsPath))
                {
                    File.Replace(temporary, SettingsPath, null);
                }
                else
                {
                    File.Move(temporary, SettingsPath);
                }
            }
            catch
            {
                try { File.Delete(temporary); }
                catch { }
            }
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
    }
}
