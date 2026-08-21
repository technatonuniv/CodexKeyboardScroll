using System;
using System.Runtime.InteropServices;

namespace CodexKeyboardScroll
{
    internal static class WindowPositioning
    {
        private const uint NoSize = 0x0001;
        private const uint NoZOrder = 0x0004;
        private const uint NoActivate = 0x0010;

        internal static bool TryMove(IntPtr handle, int x, int y)
        {
            return SetWindowPos(
                handle,
                IntPtr.Zero,
                x,
                y,
                0,
                0,
                NoSize | NoZOrder | NoActivate);
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}
