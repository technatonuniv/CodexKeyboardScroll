using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodexKeyboardScroll
{
    internal static class NativeInput
    {
        internal const int VkLeftButton = 0x01;
        internal const int WheelDelta = 120;

        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;
        private const uint MouseMove = 0x0001;
        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;
        private const uint MouseWheel = 0x0800;
        private const uint MouseVirtualDesk = 0x4000;
        private const uint MouseAbsolute = 0x8000;
        private const uint KeyExtended = 0x0001;
        private const uint KeyUp = 0x0002;
        private const uint KeyScanCode = 0x0008;
        private const int VirtualLeft = 76;
        private const int VirtualTop = 77;
        private const int VirtualWidth = 78;
        private const int VirtualHeight = 79;

        private static uint cachedProcessId;
        private static bool cachedIsChat;

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out PointNative point);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        internal static bool IsChatForeground()
        {
            IntPtr window = GetForegroundWindow();
            uint processId;
            if (window == IntPtr.Zero
                || GetWindowThreadProcessId(window, out processId) == 0
                || processId == 0)
            {
                return false;
            }

            if (processId == cachedProcessId)
            {
                return cachedIsChat;
            }

            cachedProcessId = processId;
            cachedIsChat = false;
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    cachedIsChat = process.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase)
                        || process.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
            }
            return cachedIsChat;
        }

        internal static bool IsCommandModifierDown()
        {
            return IsKeyDown(0x11) || IsKeyDown(0x12) || IsKeyDown(0x5B) || IsKeyDown(0x5C);
        }

        internal static bool ScrollForeground(int wheelDelta)
        {
            IntPtr window = GetForegroundWindow();
            Rect rect;
            PointNative original;
            if (window == IntPtr.Zero || !GetWindowRect(window, out rect) || !GetCursorPos(out original))
            {
                return false;
            }

            Point target = WindowLayout.ScrollTarget(rect);
            if (!SetCursorPos(target.X, target.Y))
            {
                return false;
            }

            // Chromium routes wheel input to the element under the pointer. Restore the
            // original pointer immediately so keyboard scrolling does not move the mouse.
            Input wheel = MouseInput(MouseWheel, 0, 0, unchecked((uint)wheelDelta));
            uint sent = SendInput(1, new[] { wheel }, Marshal.SizeOf(typeof(Input)));
            Thread.Sleep(1);
            SetCursorPos(original.X, original.Y);
            return sent == 1;
        }

        internal static bool FocusTranscript()
        {
            return FocusTarget(false, null);
        }

        internal static bool FocusComposer()
        {
            return FocusTarget(true, null);
        }

        internal static bool FocusComposerAndReplay(KeyboardStroke stroke)
        {
            return FocusTarget(true, stroke);
        }

        internal static bool ReplayKeyboard(KeyboardStroke stroke)
        {
            Input[] inputs = { KeyboardInput(stroke, false), KeyboardInput(stroke, true) };
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) == (uint)inputs.Length;
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private static bool FocusTarget(bool composer, KeyboardStroke? replay)
        {
            IntPtr window = GetForegroundWindow();
            Rect rect;
            PointNative original;
            if (window == IntPtr.Zero || !GetWindowRect(window, out rect) || !GetCursorPos(out original))
            {
                return false;
            }

            Point target = composer ? WindowLayout.ComposerTarget(rect) : WindowLayout.ScrollTarget(rect);
            // Mouse focus, pointer restoration, and optional key replay are submitted as
            // one ordered input batch so the first typed character cannot beat the click.
            var inputs = new List<Input>
            {
                AbsoluteMove(target.X, target.Y),
                MouseInput(MouseLeftDown, 0, 0, 0),
                MouseInput(MouseLeftUp, 0, 0, 0),
                AbsoluteMove(original.X, original.Y)
            };
            if (replay.HasValue)
            {
                inputs.Add(KeyboardInput(replay.Value, false));
                inputs.Add(KeyboardInput(replay.Value, true));
            }

            Input[] sequence = inputs.ToArray();
            return SendInput((uint)sequence.Length, sequence, Marshal.SizeOf(typeof(Input))) == (uint)sequence.Length;
        }

        private static Input AbsoluteMove(int x, int y)
        {
            int left = GetSystemMetrics(VirtualLeft);
            int top = GetSystemMetrics(VirtualTop);
            int width = Math.Max(2, GetSystemMetrics(VirtualWidth));
            int height = Math.Max(2, GetSystemMetrics(VirtualHeight));
            int absoluteX = (int)Math.Round((x - left) * 65535.0 / (width - 1));
            int absoluteY = (int)Math.Round((y - top) * 65535.0 / (height - 1));
            return MouseInput(MouseMove | MouseAbsolute | MouseVirtualDesk, absoluteX, absoluteY, 0);
        }

        private static Input MouseInput(uint flags, int x, int y, uint data)
        {
            return new Input
            {
                Type = InputMouse,
                Data = new InputUnion
                {
                    Mouse = new MouseInputNative { X = x, Y = y, Data = data, Flags = flags }
                }
            };
        }

        private static Input KeyboardInput(KeyboardStroke stroke, bool keyUp)
        {
            uint flags = stroke.ScanCode == 0 ? 0 : KeyScanCode;
            if ((stroke.Flags & 0x01) != 0) flags |= KeyExtended;
            if (keyUp) flags |= KeyUp;
            return new Input
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInputNative
                    {
                        VirtualKey = stroke.ScanCode == 0 ? (ushort)stroke.VirtualKey : (ushort)0,
                        ScanCode = (ushort)stroke.ScanCode,
                        Flags = flags
                    }
                }
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PointNative
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;

            internal Rect(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            internal uint Type;
            internal InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] internal MouseInputNative Mouse;
            [FieldOffset(0)] internal KeyboardInputNative Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInputNative
        {
            internal int X;
            internal int Y;
            internal uint Data;
            internal uint Flags;
            internal uint Time;
            internal IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInputNative
        {
            internal ushort VirtualKey;
            internal ushort ScanCode;
            internal uint Flags;
            internal uint Time;
            internal IntPtr ExtraInfo;
        }
    }
}
