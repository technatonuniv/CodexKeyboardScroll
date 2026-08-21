using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexKeyboardScroll
{
    internal sealed class ScrollApplicationContext : ApplicationContext
    {
        private readonly UtilitySettings settings;
        private readonly ComposerLocator composerLocator;
        private readonly HotkeyWindow hotkeys;
        private readonly KeyboardHook keyboardHook;
        private readonly NotifyIcon trayIcon;
        private readonly Timer pollTimer;
        private readonly Timer focusToggleTimer;

        private readonly ToolStripMenuItem enabledItem;
        private readonly ToolStripMenuItem readingModeItem;
        private readonly ToolStripMenuItem spaceScrollItem;
        private readonly ToolStripMenuItem summaryItem;
        private readonly ToolStripMenuItem diagnosticModeItem;
        private readonly ToolStripMenuItem diagnosticShortcutItem;
        private readonly ToolStripMenuItem diagnosticAutomationItem;
        private readonly ToolStripMenuItem[] shortcutItems;
        private readonly ToolStripMenuItem[] speedItems;

        private bool toolEnabled = true;
        private bool readingMode;
        private bool previousLeftButtonDown;
        private bool hasPendingClick;
        private Point pendingClick;
        private int pendingClickTick;
        private int nextHotkeyRetryTick;
        private int lastHotkeyWarningTick;
        private int hotkeyFailureCount;
        private bool disposed;

        internal ScrollApplicationContext()
        {
            settings = UtilitySettings.Load();
            composerLocator = new ComposerLocator();
            hotkeys = new HotkeyWindow(HandleScrollHotkey, QueueFocusToggle);

            summaryItem = DiagnosticItem("Ready · Alt+F · UIA");
            diagnosticModeItem = DiagnosticItem("Mode: ready for transcript focus");
            diagnosticShortcutItem = DiagnosticItem("Focus shortcut: Alt+F");
            diagnosticAutomationItem = DiagnosticItem("UI Automation: locating composer...");
            var diagnosticsMenu = new ToolStripMenuItem("Status and diagnostics");
            diagnosticsMenu.DropDownItems.Add(diagnosticModeItem);
            diagnosticsMenu.DropDownItems.Add(diagnosticShortcutItem);
            diagnosticsMenu.DropDownItems.Add(diagnosticAutomationItem);
            diagnosticsMenu.DropDownItems.Add(DiagnosticItem("Version: 1.4.0"));

            enabledItem = new ToolStripMenuItem("Utility enabled", null, ToggleEnabled)
            {
                Checked = true,
                CheckOnClick = true
            };
            readingModeItem = new ToolStripMenuItem("Reading mode", null, ToggleReadingMode)
            {
                CheckOnClick = true
            };

            var shortcutMenu = new ToolStripMenuItem("Focus shortcut");
            shortcutItems = new[]
            {
                ShortcutItem("Alt+F", FocusShortcut.AltF),
                ShortcutItem("Ctrl+Alt+F", FocusShortcut.CtrlAltF),
                ShortcutItem("Ctrl+Shift+F", FocusShortcut.CtrlShiftF)
            };
            shortcutMenu.DropDownItems.AddRange(shortcutItems);

            var speedMenu = new ToolStripMenuItem("Scroll speed");
            speedItems = new[]
            {
                SpeedItem("Slow", ScrollSpeed.Slow),
                SpeedItem("Normal", ScrollSpeed.Normal),
                SpeedItem("Fast", ScrollSpeed.Fast)
            };
            speedMenu.DropDownItems.AddRange(speedItems);

            spaceScrollItem = new ToolStripMenuItem("Space scrolls the page", null, ToggleSpaceBehavior)
            {
                Checked = settings.SpaceScroll,
                CheckOnClick = true
            };

            var exitItem = new ToolStripMenuItem("Exit", null, delegate { ExitThread(); });
            var menu = new ContextMenuStrip();
            menu.Items.Add(summaryItem);
            menu.Items.Add(diagnosticsMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(enabledItem);
            menu.Items.Add(readingModeItem);
            menu.Items.Add(shortcutMenu);
            menu.Items.Add(speedMenu);
            menu.Items.Add(spaceScrollItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);
            menu.Opening += delegate { UpdateMenuText(); };

            trayIcon = new NotifyIcon
            {
                ContextMenuStrip = menu,
                Icon = SystemIcons.Information,
                Text = "Codex Keyboard Scroll",
                Visible = true
            };
            trayIcon.DoubleClick += ShowEnabledState;

            focusToggleTimer = new Timer { Interval = 20 };
            focusToggleTimer.Tick += CompleteFocusToggle;

            pollTimer = new Timer { Interval = 25 };
            pollTimer.Tick += Poll;
            pollTimer.Start();

            string hookError;
            keyboardHook = new KeyboardHook(HandlePrintableKey, out hookError);
            if (hookError != null)
            {
                ShowBalloon(hookError, ToolTipIcon.Warning);
            }

            ShowBalloon(
                "Running. " + UtilitySettings.ShortcutText(settings.FocusShortcut)
                    + " switches between the transcript and composer.",
                ToolTipIcon.Info);
        }

        private void Poll(object sender, EventArgs e)
        {
            bool chatForeground = NativeInput.IsChatForeground();
            UpdateFocusHotkey(toolEnabled && chatForeground);
            if (chatForeground)
            {
                composerLocator.RequestRefresh(NativeInput.GetForegroundWindow());
            }

            bool leftDown = (NativeInput.GetAsyncKeyState(NativeInput.VkLeftButton) & 0x8000) != 0;
            if (leftDown && !previousLeftButtonDown)
            {
                NativeInput.PointNative point;
                if (NativeInput.GetCursorPos(out point))
                {
                    pendingClick = new Point(point.X, point.Y);
                    pendingClickTick = Environment.TickCount;
                    hasPendingClick = true;
                }
            }
            previousLeftButtonDown = leftDown;

            if (hasPendingClick && unchecked(Environment.TickCount - pendingClickTick) >= 50)
            {
                hasPendingClick = false;
                SetReadingMode(toolEnabled && chatForeground && ClickWasInTranscript(pendingClick));
            }

            if (readingMode && !chatForeground)
            {
                SetReadingMode(false);
            }
        }

        private static bool ClickWasInTranscript(Point click)
        {
            NativeInput.Rect rect;
            IntPtr window = NativeInput.GetForegroundWindow();
            return window != IntPtr.Zero
                && NativeInput.GetWindowRect(window, out rect)
                && WindowLayout.IsTranscriptClick(rect, click);
        }

        private void UpdateFocusHotkey(bool shouldBeActive)
        {
            if (!shouldBeActive)
            {
                bool stateChanged = hotkeys.IsFocusToggleRegistered || hotkeyFailureCount != 0;
                hotkeys.DisableFocusToggle();
                nextHotkeyRetryTick = 0;
                hotkeyFailureCount = 0;
                if (stateChanged) UpdateMenuText();
                return;
            }

            if (hotkeys.IsFocusToggleRegistered)
            {
                return;
            }

            int now = Environment.TickCount;
            if (nextHotkeyRetryTick != 0 && unchecked(now - nextHotkeyRetryTick) < 0)
            {
                return;
            }

            // Hotkeys can be unavailable briefly while another app owns them. Retrying
            // keeps a transient conflict from disabling the utility until restart.
            nextHotkeyRetryTick = unchecked(now + 2000);
            string error;
            if (!hotkeys.EnableFocusToggle(settings.FocusShortcut, out error))
            {
                hotkeyFailureCount++;
                diagnosticShortcutItem.Text = "Focus shortcut: registration retry " + hotkeyFailureCount;
                if (lastHotkeyWarningTick == 0 || unchecked(now - lastHotkeyWarningTick) >= 30000)
                {
                    lastHotkeyWarningTick = now;
                    ShowBalloon(error + " The utility will retry automatically.", ToolTipIcon.Warning);
                }
                UpdateMenuText();
                return;
            }

            nextHotkeyRetryTick = 0;
            hotkeyFailureCount = 0;
            diagnosticShortcutItem.Text = "Focus shortcut: " + hotkeys.FocusToggleText;
            UpdateMenuText();
        }

        private void SetReadingMode(bool enabled)
        {
            if (readingMode == enabled)
            {
                UpdateMenuText();
                return;
            }

            if (enabled)
            {
                string error;
                if (!hotkeys.EnableScrollingHotkeys(settings.SpaceScroll, out error))
                {
                    readingModeItem.Checked = false;
                    ShowBalloon(error, ToolTipIcon.Error);
                    UpdateMenuText();
                    return;
                }
            }
            else
            {
                hotkeys.DisableScrollingHotkeys();
            }

            readingMode = enabled;
            readingModeItem.Checked = enabled;
            UpdateMenuText();
        }

        private void HandleScrollHotkey(ScrollCommand command)
        {
            if (!toolEnabled || !readingMode || !NativeInput.IsChatForeground())
            {
                SetReadingMode(false);
                return;
            }

            NativeInput.ScrollForeground(ScrollProfile.WheelDelta(command, settings.ScrollSpeed));
        }

        private void QueueFocusToggle()
        {
            if (!toolEnabled || !NativeInput.IsChatForeground())
            {
                return;
            }

            focusToggleTimer.Start();
        }

        private void CompleteFocusToggle(object sender, EventArgs e)
        {
            if (!toolEnabled || !NativeInput.IsChatForeground())
            {
                focusToggleTimer.Stop();
                return;
            }
            if (!NativeInput.AreFocusKeysReleased())
            {
                return;
            }

            focusToggleTimer.Stop();
            // Alt release can leave Chromium in menu mnemonic mode even though the
            // modifier is no longer down. Escape dismisses that mode before focusing.
            if (hotkeys.FocusToggleUsesAlt)
            {
                NativeInput.DismissMenuMode();
            }

            if (!readingMode)
            {
                NativeInput.FocusTranscript();
                SetReadingMode(true);
                return;
            }

            SetReadingMode(false);
            FocusComposer();
        }

        private void FocusComposer()
        {
            if (!toolEnabled || !NativeInput.IsChatForeground())
            {
                return;
            }
            if (!composerLocator.TryFocus(NativeInput.GetForegroundWindow()))
            {
                NativeInput.FocusComposer();
            }
        }

        private bool HandlePrintableKey(KeyboardStroke stroke)
        {
            if (!toolEnabled || !readingMode || !NativeInput.IsChatForeground()
                || NativeInput.IsCommandModifierDown()
                || !PrintableKeyClassifier.IsTypingKey(stroke.VirtualKey, settings.SpaceScroll))
            {
                return false;
            }

            SetReadingMode(false);
            // The hook suppresses the physical key only when this method returns true.
            // Replay after focusing preserves the first character the user intended to type.
            if (composerLocator.TryFocus(NativeInput.GetForegroundWindow()))
            {
                return NativeInput.ReplayKeyboard(stroke);
            }
            return NativeInput.FocusComposerAndReplay(stroke);
        }

        private void ToggleEnabled(object sender, EventArgs e)
        {
            toolEnabled = enabledItem.Checked;
            if (!toolEnabled)
            {
                SetReadingMode(false);
                hotkeys.DisableFocusToggle();
                nextHotkeyRetryTick = 0;
            }
            UpdateMenuText();
        }

        private void ToggleReadingMode(object sender, EventArgs e)
        {
            if (readingModeItem.Checked == readingMode)
            {
                return;
            }
            if (readingModeItem.Checked && (!toolEnabled || !NativeInput.IsChatForeground()))
            {
                readingModeItem.Checked = false;
                ShowBalloon("Activate the ChatGPT/Codex window first.", ToolTipIcon.Warning);
                return;
            }
            SetReadingMode(readingModeItem.Checked);
        }

        private void ToggleSpaceBehavior(object sender, EventArgs e)
        {
            settings.SpaceScroll = spaceScrollItem.Checked;
            settings.Save();
            if (readingMode)
            {
                hotkeys.DisableScrollingHotkeys();
                readingMode = false;
                SetReadingMode(true);
            }
        }

        private ToolStripMenuItem ShortcutItem(string text, FocusShortcut shortcut)
        {
            var item = new ToolStripMenuItem(text) { Checked = settings.FocusShortcut == shortcut, Tag = shortcut };
            item.Click += delegate
            {
                settings.FocusShortcut = (FocusShortcut)item.Tag;
                settings.Save();
                CheckOnly(shortcutItems, item);
                hotkeys.DisableFocusToggle();
                nextHotkeyRetryTick = 0;
            };
            return item;
        }

        private ToolStripMenuItem SpeedItem(string text, ScrollSpeed speed)
        {
            var item = new ToolStripMenuItem(text) { Checked = settings.ScrollSpeed == speed, Tag = speed };
            item.Click += delegate
            {
                settings.ScrollSpeed = (ScrollSpeed)item.Tag;
                settings.Save();
                CheckOnly(speedItems, item);
            };
            return item;
        }

        private static void CheckOnly(ToolStripMenuItem[] items, ToolStripMenuItem selected)
        {
            foreach (ToolStripMenuItem item in items)
            {
                item.Checked = ReferenceEquals(item, selected);
            }
        }

        private void ShowEnabledState(object sender, EventArgs e)
        {
            ShowBalloon(
                toolEnabled
                    ? "The utility is enabled. Right-click the icon for settings."
                    : "The utility is disabled. Enable it from the right-click menu.",
                toolEnabled ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }

        private void UpdateMenuText()
        {
            string mode = !toolEnabled ? "Disabled" : readingMode ? "Reading" : "Ready";
            diagnosticModeItem.Text = readingMode
                ? "Mode: reading (navigation keys captured)"
                : toolEnabled ? "Mode: ready for transcript focus" : "Mode: disabled";
            diagnosticAutomationItem.Text = composerLocator.StatusText;
            diagnosticShortcutItem.Text = hotkeyFailureCount > 0
                ? "Focus shortcut: registration retry " + hotkeyFailureCount
                : "Focus shortcut: " + (hotkeys.IsFocusToggleRegistered
                    ? hotkeys.FocusToggleText
                    : UtilitySettings.ShortcutText(settings.FocusShortcut));

            string shortcut = hotkeys.IsFocusToggleRegistered
                ? hotkeys.FocusToggleText.Replace(" (fallback)", string.Empty)
                : hotkeyFailureCount > 0 ? "Hotkey retry" : UtilitySettings.ShortcutText(settings.FocusShortcut);
            summaryItem.Text = mode + " · " + shortcut + " · " + composerLocator.CompactStatus;
            readingModeItem.Checked = readingMode;
        }

        private void ShowBalloon(string message, ToolTipIcon icon)
        {
            trayIcon.BalloonTipTitle = "Codex Keyboard Scroll";
            trayIcon.BalloonTipText = message;
            trayIcon.BalloonTipIcon = icon;
            trayIcon.ShowBalloonTip(3500);
        }

        private static ToolStripMenuItem DiagnosticItem(string text)
        {
            return new ToolStripMenuItem(text) { Enabled = false };
        }

        protected override void ExitThreadCore()
        {
            Dispose();
            base.ExitThreadCore();
        }

        public new void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            pollTimer.Stop();
            focusToggleTimer.Stop();
            pollTimer.Dispose();
            focusToggleTimer.Dispose();
            keyboardHook.Dispose();
            composerLocator.Dispose();
            hotkeys.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            base.Dispose();
        }
    }
}
