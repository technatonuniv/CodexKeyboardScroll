namespace CodexKeyboardScroll
{
    internal static class PrintableKeyClassifier
    {
        internal static bool NeedsTypingHook(uint virtualKey)
        {
            return virtualKey == 0x20 || IsTypingKey(virtualKey, true);
        }

        internal static bool IsModifierKey(uint virtualKey)
        {
            return virtualKey == 0x10 || virtualKey == 0x11 || virtualKey == 0x12
                || virtualKey == 0x5B || virtualKey == 0x5C
                || (virtualKey >= 0xA0 && virtualKey <= 0xA5);
        }

        internal static ScrollCommand? NavigationScrollCommand(
            uint virtualKey, bool shiftDown, bool commandModifierDown)
        {
            if (shiftDown || commandModifierDown) return null;
            switch (virtualKey)
            {
                case 0x26: return ScrollCommand.LineUp;
                case 0x28: return ScrollCommand.LineDown;
                case 0x21: return ScrollCommand.PageUp;
                case 0x22: return ScrollCommand.PageDown;
                default: return null;
            }
        }

        internal static ScrollCommand? SpaceScrollCommand(
            uint virtualKey,
            bool spaceScroll,
            bool shiftDown,
            bool commandModifierDown)
        {
            if (virtualKey != 0x20 || !spaceScroll || commandModifierDown)
            {
                return null;
            }

            return shiftDown ? ScrollCommand.PageUp : ScrollCommand.PageDown;
        }

        internal static bool IsTypingKey(uint virtualKey, bool spaceScroll)
        {
            if (virtualKey == 0x20) return !spaceScroll;

            if ((virtualKey >= 0x30 && virtualKey <= 0x39)
                || (virtualKey >= 0x41 && virtualKey <= 0x5A)
                || (virtualKey >= 0x60 && virtualKey <= 0x69)
                || (virtualKey >= 0xBA && virtualKey <= 0xC0)
                || (virtualKey >= 0xDB && virtualKey <= 0xDF))
            {
                return true;
            }

            return virtualKey == 0x6A
                || virtualKey == 0x6B
                || virtualKey == 0x6D
                || virtualKey == 0x6E
                || virtualKey == 0x6F
                || virtualKey == 0xE2;
        }
    }
}
