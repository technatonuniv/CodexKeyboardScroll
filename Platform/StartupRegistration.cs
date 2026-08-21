using System;
using Microsoft.Win32;

namespace CodexKeyboardScroll
{
    internal sealed class StartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CodexKeyboardScroll";
        private readonly string executablePath;

        internal StartupRegistration(string executablePath)
        {
            this.executablePath = executablePath;
        }

        internal bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                    {
                        string command = key == null ? null : key.GetValue(ValueName) as string;
                        return IsCommandForExecutable(command, executablePath);
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        internal bool TrySetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (enabled)
                    {
                        key.SetValue(ValueName, CommandForExecutable(executablePath), RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static string CommandForExecutable(string path)
        {
            return "\"" + path + "\"";
        }

        internal static bool IsCommandForExecutable(string command, string path)
        {
            return string.Equals(
                (command ?? string.Empty).Trim(),
                CommandForExecutable(path),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
