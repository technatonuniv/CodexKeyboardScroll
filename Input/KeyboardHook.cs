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
        private const uint LlkhfInjected = 0x10;

        private readonly Func<KeyboardStroke, bool> handler;
        private readonly HookProc hookProc;
        private readonly HashSet<uint> suppressedKeyUps = new HashSet<uint>();
        private IntPtr hook;

        internal KeyboardHook(Func<KeyboardStroke, bool> handler, out string error)
        {
            this.handler = handler;
            hookProc = HookCallback;
            hook = SetWindowsHookEx(WhKeyboardLl, hookProc, GetModuleHandle(null), 0);
            error = hook == IntPtr.Zero
                ? "Could not enable automatic composer focus (Win32 "
                    + Marshal.GetLastWin32Error() + ")."
                : null;
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var data = (KbdLlHookStruct)Marshal.PtrToStructure(lParam, typeof(KbdLlHookStruct));
                // Replayed keys are injected deliberately after composer focus. Ignoring
                // them here prevents the hook from recursively handling its own input.
                if ((data.Flags & LlkhfInjected) == 0)
                {
                    int message = wParam.ToInt32();
                    if ((message == WmKeyUp || message == WmSysKeyUp)
                        && suppressedKeyUps.Remove(data.VirtualKey))
                    {
                        return new IntPtr(1);
                    }
                    if (message == WmKeyDown || message == WmSysKeyDown)
                    {
                        var stroke = new KeyboardStroke(data.VirtualKey, data.ScanCode, data.Flags);
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
