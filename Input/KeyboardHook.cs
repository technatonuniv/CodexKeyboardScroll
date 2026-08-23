using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CodexKeyboardScroll
{
    internal sealed class KeyboardHook : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const uint VkShift = 0x10;
        private const uint VkLeftShift = 0xA0;
        private const uint VkRightShift = 0xA1;

        private readonly Func<KeyboardStroke, bool> handler;
        private readonly HookProc hookProc;
        private readonly HashSet<uint> suppressedKeyUps = new HashSet<uint>();
        private IntPtr hook;
        private bool shiftDown;
        private uint shiftVirtualKey;

        internal KeyboardHook(Func<KeyboardStroke, bool> handler, out int win32Error)
        {
            this.handler = handler;
            hookProc = HookCallback;
            hook = SetWindowsHookEx(WhKeyboardLl, hookProc, GetModuleHandle(null), 0);
            win32Error = hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
        }

        internal bool IsAvailable
        {
            get { return hook != IntPtr.Zero; }
        }

        internal static bool ShouldProcess(IntPtr extraInfo)
        {
            return extraInfo != NativeInput.SyntheticInputMarker;
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var data = (KbdLlHookStruct)Marshal.PtrToStructure(lParam, typeof(KbdLlHookStruct));
                // Accessibility and remapping tools can legitimately inject input. Only
                // events marked by this process are skipped to prevent replay recursion.
                if (ShouldProcess(data.ExtraInfo))
                {
                    int message = wParam.ToInt32();
                    bool keyDown = message == WmKeyDown || message == WmSysKeyDown;
                    bool keyUp = message == WmKeyUp || message == WmSysKeyUp;
                    if (IsShiftKey(data.VirtualKey))
                    {
                        shiftDown = keyDown ? true : keyUp ? false : shiftDown;
                        shiftVirtualKey = keyDown ? data.VirtualKey : keyUp ? 0 : shiftVirtualKey;
                    }
                    if (keyUp && suppressedKeyUps.Remove(data.VirtualKey))
                    {
                        return new IntPtr(1);
                    }
                    if (keyDown)
                    {
                        var stroke = new KeyboardStroke(
                            data.VirtualKey,
                            data.ScanCode,
                            data.Flags,
                            shiftDown,
                            shiftVirtualKey);
                        if (handler(stroke))
                        {
                            // Suppress the matching physical key-up as well as key-down;
                            // otherwise Chromium can observe an unmatched release event.
                            suppressedKeyUps.Add(data.VirtualKey);
                            return new IntPtr(1);
                        }
                    }
                }
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        private static bool IsShiftKey(uint virtualKey)
        {
            return virtualKey == VkShift
                || virtualKey == VkLeftShift
                || virtualKey == VkRightShift;
        }

        public void Dispose()
        {
            if (hook == IntPtr.Zero)
            {
                return;
            }
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hookId, HookProc hookProc, IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            internal uint VirtualKey;
            internal uint ScanCode;
            internal uint Flags;
            internal uint Time;
            internal IntPtr ExtraInfo;
        }
    }
}
