using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexKeyboardScroll
{
    internal static class NativeInput
    {
        internal const int VkLeftButton = 0x01;
        internal const int WheelDelta = 120;
        internal static readonly IntPtr SyntheticInputMarker = new IntPtr(0x434B5301);

        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;
        private const uint SpiGetStickyKeys = 0x003A;
        private const uint StickyLeftShiftLatched = 0x01000000;
        private const uint StickyRightShiftLatched = 0x02000000;
        private const uint StickyLeftShiftLocked = 0x00010000;
        private const uint StickyRightShiftLocked = 0x00020000;
        private const uint StickyCommandLatched = 0xFC000000;
        private const uint StickyCommandLocked = 0x00FC0000;
        private const uint MouseMove = 0x0001;
        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;
        private const uint MouseWheel = 0x0800;
        private const uint MouseVirtualDesk = 0x4000;
        private const uint MouseAbsolute = 0x8000;
        private const uint KeyExtended = 0x0001;
        private const uint KeyUp = 0x0002;
        private const uint KeyScanCode = 0x0008;
        private const uint WmMouseWheel = 0x020A;
        private const uint ChildSkipInvisible = 0x0001;
        private const uint ChildSkipDisabled = 0x0002;
        private const uint ChildSkipTransparent = 0x0004;
        private const int VirtualLeft = 76;
        private const int VirtualTop = 77;
        private const int VirtualWidth = 78;
        private const int VirtualHeight = 79;

        // Accessibility refreshes run on workers; never share a partially updated
        // process identity with the UI thread or keyboard hook.
        [ThreadStatic] private static uint cachedProcessId;
        [ThreadStatic] private static ChatApplication cachedApplication;

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

        internal static string WindowTitle(IntPtr window)
        {
            var text = new StringBuilder(512);
            return GetWindowText(window, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
        }

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out PointNative point);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(PointNative point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        internal static bool IsClickInForegroundWindow(IntPtr foreground, PointNative point)
        {
            IntPtr hit = WindowFromPoint(point);
            return IsForegroundClickRoot(foreground, hit == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hit, 2));
        }

        internal static bool IsForegroundClickRoot(IntPtr foreground, IntPtr hitRoot)
        {
            return foreground != IntPtr.Zero && foreground == hitRoot;
        }

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(
            uint action,
            uint parameter,
            ref StickyKeys stickyKeys,
            uint updateFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr window, ref PointNative point);

        [DllImport("user32.dll")]
        private static extern IntPtr ChildWindowFromPointEx(IntPtr parent, PointNative point, uint flags);

        internal static bool IsChatForeground(bool claudeEnabled = false)
        {
            return IsApplicationEnabled(GetChatApplication(GetForegroundWindow()), claudeEnabled);
        }

        internal static ChatApplication ClassifyProcessName(string name)
        {
            if (string.Equals(name, "ChatGPT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Codex", StringComparison.OrdinalIgnoreCase))
                return ChatApplication.Codex;
            if (string.Equals(name, "Claude", StringComparison.OrdinalIgnoreCase))
                return ChatApplication.Claude;
            return string.Equals(name, "chrome", StringComparison.OrdinalIgnoreCase)
                ? ChatApplication.Chrome : ChatApplication.None;
        }

        internal static bool IsApplicationEnabled(ChatApplication application, bool claudeEnabled, bool chromeEnabled = false)
        {
            return application == ChatApplication.Codex
                || (application == ChatApplication.Claude && claudeEnabled)
                || (application == ChatApplication.Chrome && chromeEnabled);
        }

        internal static ChatApplication GetChatApplication(IntPtr window)
        {
            uint processId;
            if (window == IntPtr.Zero
                || GetWindowThreadProcessId(window, out processId) == 0
                || processId == 0)
            {
                return ChatApplication.None;
            }

            if (processId == cachedProcessId)
            {
                return cachedApplication;
            }

            cachedProcessId = processId;
            cachedApplication = ChatApplication.None;
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    cachedApplication = ClassifyProcessName(process.ProcessName);
                }
            }
            catch
            {
            }
            return cachedApplication;
        }

        internal static bool IsCommandModifierDown()
        {
            return IsKeyDown(0x11)
                || IsKeyDown(0x12)
                || IsKeyDown(0x5B)
                || IsKeyDown(0x5C)
                || StickyCommandModifierActive(GetStickyKeyFlags());
        }

        internal static bool IsShiftModifierDown(bool hookShiftDown)
        {
            return hookShiftDown
                || IsKeyDown(0x10)
                || StickyShiftModifierActive(GetStickyKeyFlags());
        }

        internal static bool StickyShiftModifierActive(uint flags)
        {
            const uint mask = StickyLeftShiftLatched
                | StickyRightShiftLatched
                | StickyLeftShiftLocked
                | StickyRightShiftLocked;
            return (flags & mask) != 0;
        }

        internal static bool StickyCommandModifierActive(uint flags)
        {
            return (flags & (StickyCommandLatched | StickyCommandLocked)) != 0;
        }

        internal static bool ScrollForeground(int wheelDelta)
        {
            return ScrollWindow(GetForegroundWindow(), wheelDelta);
        }

        internal static bool ScrollForegroundWithoutShift(
            IntPtr window,
            int wheelDelta,
            uint shiftVirtualKey,
            bool restoreShift)
        {
            if (window == IntPtr.Zero || window != GetForegroundWindow()) return false;
            uint key = shiftVirtualKey == 0 ? 0x10 : shiftVirtualKey;
            var shift = new KeyboardStroke(key, 0, 0, false, 0);
            Input shiftUp = KeyboardInput(shift, true);
            Input shiftDown = KeyboardInput(shift, false);
            Input wheel = MouseInput(MouseWheel, 0, 0, unchecked((uint)wheelDelta));
            Input[] sequence = restoreShift
                ? new[] { shiftUp, wheel, shiftDown }
                : new[] { shiftUp, wheel };
            uint inserted = SendInput(
                (uint)sequence.Length,
                sequence,
                Marshal.SizeOf(typeof(Input)));

            // SendInput normally inserts the complete batch. If the final restore
            // event is ever rejected, make one best-effort attempt to restore Shift.
            if (restoreShift && inserted > 0 && inserted < sequence.Length)
            {
                Input[] restore = { shiftDown };
                SendInput(1, restore, Marshal.SizeOf(typeof(Input)));
            }
            return inserted >= 2;
        }

        internal static bool ScrollWindow(IntPtr window, int wheelDelta, Point? requestedTarget = null)
        {
            Rect rect;
            if (window == IntPtr.Zero || window != GetForegroundWindow()
                || !GetWindowRect(window, out rect))
            {
                return false;
            }

            Point target = requestedTarget.HasValue
                && requestedTarget.Value.X >= rect.Left && requestedTarget.Value.X < rect.Right
                && requestedTarget.Value.Y >= rect.Top && requestedTarget.Value.Y < rect.Bottom
                ? requestedTarget.Value : WindowLayout.ScrollTarget(rect);
            IntPtr targetWindow = DeepestChildAtPoint(window, target);
            // WM_MOUSEWHEEL carries screen coordinates, so it can target Chromium's
            // transcript directly without ever moving or redrawing the system cursor.
            return PostMessage(
                targetWindow,
                WmMouseWheel,
                PackWheelWParam(wheelDelta),
                PackScreenPoint(target));
        }

        internal static IntPtr PackWheelWParam(int wheelDelta)
        {
            uint highWord = (uint)unchecked((ushort)(short)wheelDelta) << 16;
            return new IntPtr(unchecked((int)highWord));
        }

        internal static IntPtr PackScreenPoint(Point point)
        {
            uint packed = unchecked((ushort)point.X)
                | ((uint)unchecked((ushort)point.Y) << 16);
            return new IntPtr(unchecked((int)packed));
        }

        internal static bool FocusTranscript()
        {
            return FocusTarget(false, null);
        }

        internal static bool FocusTranscript(IntPtr window)
        {
            return FocusTarget(window, false, null);
        }

        internal static bool FocusComposer()
        {
            return FocusTarget(true, null);
        }

        internal static bool FocusComposer(IntPtr window)
        {
            return FocusTarget(window, true, null);
        }

        internal static bool FocusComposerAndReplay(KeyboardStroke stroke)
        {
            return FocusTarget(true, stroke);
        }

        internal static bool FocusComposerAndReplay(IntPtr window, KeyboardStroke stroke)
        {
            return FocusTarget(window, true, stroke);
        }

        internal static bool FocusInput(IntPtr window, Rect bounds)
        {
            Rect windowBounds;
            if (window != GetForegroundWindow() || !GetWindowRect(window, out windowBounds)
                || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top
                || bounds.Left < windowBounds.Left || bounds.Right > windowBounds.Right
                || bounds.Top < windowBounds.Top || bounds.Bottom > windowBounds.Bottom)
            {
                return false;
            }
            return FocusPoint(new Point(
                bounds.Left + Math.Min(30, (bounds.Right - bounds.Left) / 2),
                bounds.Top + (bounds.Bottom - bounds.Top) / 2), null);
        }

        internal static bool ReplayNavigationKey(ScrollCommand command)
        {
            uint key = command == ScrollCommand.LineUp ? 0x26u
                : command == ScrollCommand.LineDown ? 0x28u
                : command == ScrollCommand.PageUp ? 0x21u : 0x22u;
            return ReplayKeyboard(new KeyboardStroke(key, 0, 0x01, false, 0));
        }

        internal static KeyboardStroke NavigationStroke(uint virtualKey)
        {
            return new KeyboardStroke(virtualKey, 0, virtualKey == 0x20 ? 0u : 0x01u, false, 0);
        }

        internal static bool ReplayKeyboard(KeyboardStroke stroke)
        {
            Input[] inputs = { KeyboardInput(stroke, false), KeyboardInput(stroke, true) };
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) == (uint)inputs.Length;
        }

        internal static bool AreFocusKeysReleased()
        {
            return !IsKeyDown(0x10) && !IsKeyDown(0x11)
                && !IsKeyDown(0x12) && !IsKeyDown(0x46);
        }

        internal static bool DismissMenuMode(IntPtr window)
        {
            if (window == IntPtr.Zero || window != GetForegroundWindow()) return false;
            var escape = new KeyboardStroke(0x1B, 0, 0, false, 0);
            Input[] inputs = { KeyboardInput(escape, false), KeyboardInput(escape, true) };
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) == (uint)inputs.Length;
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private static uint GetStickyKeyFlags()
        {
            var stickyKeys = new StickyKeys { Size = (uint)Marshal.SizeOf(typeof(StickyKeys)) };
            return SystemParametersInfo(SpiGetStickyKeys, stickyKeys.Size, ref stickyKeys, 0)
                ? stickyKeys.Flags
                : 0;
        }

        private static IntPtr DeepestChildAtPoint(IntPtr root, Point screenPoint)
        {
            IntPtr current = root;
            for (int depth = 0; depth < 12; depth++)
            {
                var clientPoint = new PointNative { X = screenPoint.X, Y = screenPoint.Y };
                if (!ScreenToClient(current, ref clientPoint))
                {
                    break;
                }

                IntPtr child = ChildWindowFromPointEx(
                    current,
                    clientPoint,
                    ChildSkipInvisible | ChildSkipDisabled | ChildSkipTransparent);
                if (child == IntPtr.Zero || child == current)
                {
                    break;
                }
                current = child;
            }
            return current;
        }

        private static bool FocusTarget(bool composer, KeyboardStroke? replay)
        {
            return FocusTarget(GetForegroundWindow(), composer, replay);
        }

        private static bool FocusTarget(IntPtr window, bool composer, KeyboardStroke? replay)
        {
            Rect rect;
            if (window == IntPtr.Zero || window != GetForegroundWindow()
                || !GetWindowRect(window, out rect))
            {
                return false;
            }

            Point target = composer ? WindowLayout.ComposerTarget(rect) : WindowLayout.ScrollTarget(rect);
            return FocusPoint(target, replay);
        }

        private static bool FocusPoint(Point target, KeyboardStroke? replay)
        {
            PointNative original;
            if (!GetCursorPos(out original)) return false;
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
                    Mouse = new MouseInputNative
                    {
                        X = x,
                        Y = y,
                        Data = data,
                        Flags = flags,
                        ExtraInfo = SyntheticInputMarker
                    }
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
                        Flags = flags,
                        ExtraInfo = SyntheticInputMarker
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

        [StructLayout(LayoutKind.Sequential)]
        private struct StickyKeys
        {
            internal uint Size;
            internal uint Flags;
        }
    }
}
