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

    internal enum ScrollSpeed
    {
        Slow,
        Normal,
        Fast
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
