using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexKeyboardScroll
{
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModNoRepeat = 0x4000;
        private static readonly IntPtr MessageOnlyWindow = new IntPtr(-3);

        private const int FocusToggleId = 100;
        private const int UpId = 101;
        private const int DownId = 102;
        private const int PageUpId = 103;
        private const int PageDownId = 104;
        private const int SpaceId = 105;
        private const int ShiftSpaceId = 106;

        private readonly Action<ScrollCommand> scrollHandler;
        private readonly Action focusHandler;
        private readonly List<int> scrollIds = new List<int>();
        private bool disposed;

        internal HotkeyWindow(Action<ScrollCommand> scrollHandler, Action focusHandler)
        {
            this.scrollHandler = scrollHandler;
            this.focusHandler = focusHandler;
            FocusToggleText = "Alt+F";
            CreateHandle(new CreateParams
            {
                Caption = "CodexKeyboardScrollMessageWindow",
                Parent = MessageOnlyWindow
            });
        }

        internal bool IsFocusToggleRegistered { get; private set; }
        internal bool FocusToggleUsesAlt { get; private set; }
        internal string FocusToggleText { get; private set; }
        internal bool UsingFallbackShortcut { get; private set; }

        internal bool EnableFocusToggle(FocusShortcut preferred, out HotkeyError error)
        {
            DisableFocusToggle();
            var attempted = new HashSet<FocusShortcut>();
            foreach (FocusShortcut shortcut in new[]
            {
                preferred,
                FocusShortcut.AltF,
                FocusShortcut.CtrlAltF,
                FocusShortcut.CtrlShiftF
            })
            {
                if (!attempted.Add(shortcut))
                {
                    continue;
                }

                uint modifiers = ShortcutModifiers(shortcut) | ModNoRepeat;
                if (NativeInput.RegisterHotKey(Handle, FocusToggleId, modifiers, (uint)Keys.F))
                {
                    IsFocusToggleRegistered = true;
                    FocusToggleUsesAlt = UtilitySettings.ShortcutUsesAlt(shortcut);
                    FocusToggleText = UtilitySettings.ShortcutText(shortcut);
                    UsingFallbackShortcut = shortcut != preferred;
                    error = new HotkeyError(HotkeyErrorKind.None, 0);
                    return true;
                }
            }

            error = new HotkeyError(HotkeyErrorKind.FocusShortcutsUnavailable, 0);
            return false;
        }

        internal void DisableFocusToggle()
        {
            if (IsFocusToggleRegistered)
            {
                NativeInput.UnregisterHotKey(Handle, FocusToggleId);
                IsFocusToggleRegistered = false;
                FocusToggleUsesAlt = false;
                UsingFallbackShortcut = false;
            }
        }

        internal bool EnableScrollingHotkeys(bool captureSpace, out HotkeyError error)
        {
            DisableScrollingHotkeys();
            var definitions = new List<HotkeyDefinition>
            {
                new HotkeyDefinition(UpId, 0, Keys.Up),
                new HotkeyDefinition(DownId, 0, Keys.Down),
                new HotkeyDefinition(PageUpId, 0, Keys.PageUp),
                new HotkeyDefinition(PageDownId, 0, Keys.PageDown)
            };
            if (captureSpace)
            {
                definitions.Add(new HotkeyDefinition(SpaceId, 0, Keys.Space));
                definitions.Add(new HotkeyDefinition(ShiftSpaceId, ModShift, Keys.Space));
            }

            foreach (HotkeyDefinition definition in definitions)
            {
                if (NativeInput.RegisterHotKey(
                    Handle, definition.Id, definition.Modifiers, (uint)definition.Key))
                {
                    scrollIds.Add(definition.Id);
                    continue;
                }

                int win32Error = Marshal.GetLastWin32Error();
                DisableScrollingHotkeys();
                error = new HotkeyError(HotkeyErrorKind.NavigationKeysUnavailable, win32Error);
                return false;
            }

            error = new HotkeyError(HotkeyErrorKind.None, 0);
            return true;
        }

        internal void DisableScrollingHotkeys()
        {
            foreach (int id in scrollIds)
            {
                NativeInput.UnregisterHotKey(Handle, id);
            }
            scrollIds.Clear();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg != WmHotkey)
            {
                base.WndProc(ref message);
                return;
            }

            switch (message.WParam.ToInt32())
            {
                case FocusToggleId: focusHandler(); break;
                case UpId: scrollHandler(ScrollCommand.LineUp); break;
                case DownId: scrollHandler(ScrollCommand.LineDown); break;
                case PageUpId:
                case ShiftSpaceId: scrollHandler(ScrollCommand.PageUp); break;
                case PageDownId:
                case SpaceId: scrollHandler(ScrollCommand.PageDown); break;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            DisableScrollingHotkeys();
            DisableFocusToggle();
            DestroyHandle();
        }

        private static uint ShortcutModifiers(FocusShortcut shortcut)
        {
            if (shortcut == FocusShortcut.CtrlAltF) return ModControl | ModAlt;
            if (shortcut == FocusShortcut.CtrlShiftF) return ModControl | ModShift;
            return ModAlt;
        }

        private struct HotkeyDefinition
        {
            internal readonly int Id;
            internal readonly uint Modifiers;
            internal readonly Keys Key;

            internal HotkeyDefinition(int id, uint modifiers, Keys key)
            {
                Id = id;
                Modifiers = modifiers;
                Key = key;
            }
        }
    }
}
