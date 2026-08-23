namespace CodexKeyboardScroll
{
    internal static class PrintableKeyClassifier
    {
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
