namespace CodexKeyboardScroll
{
    internal enum ScrollCommand
    {
        LineUp,
        LineDown,
        PageUp,
        PageDown
    }

    internal enum FocusShortcut
    {
        AltF,
        CtrlAltF,
        CtrlShiftF
    }

    internal enum ComposerStatus
    {
        Locating,
        Found,
        CoordinateFallback
    }

    internal enum ApplicationMode
    {
        Disabled,
        Waiting,
        Reading
    }

    internal enum HotkeyErrorKind
    {
        None,
        FocusShortcutsUnavailable,
        NavigationKeysUnavailable
    }

    internal struct HotkeyError
    {
        internal readonly HotkeyErrorKind Kind;
        internal readonly int Win32Error;

        internal HotkeyError(HotkeyErrorKind kind, int win32Error)
        {
            Kind = kind;
            Win32Error = win32Error;
        }
    }

    internal struct KeyboardStroke
    {
        internal readonly uint VirtualKey;
        internal readonly uint ScanCode;
        internal readonly uint Flags;

        internal KeyboardStroke(uint virtualKey, uint scanCode, uint flags)
        {
            VirtualKey = virtualKey;
            ScanCode = scanCode;
            Flags = flags;
        }
    }
}
