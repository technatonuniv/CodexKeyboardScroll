using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Automation;

[assembly: AssemblyTitle("Codex Keyboard Scroll")]
[assembly: AssemblyDescription("Restores keyboard scrolling in the ChatGPT/Codex conversation area on Windows.")]
[assembly: AssemblyCompany("Local utility")]
[assembly: AssemblyProduct("Codex Keyboard Scroll")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace CodexKeyboardScroll
{
    internal static class Program
    {
        private const string MutexName = "Local\\CodexKeyboardScroll-7D35DB7D-3062-43BD-8D23-85802F58C820";

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                return SelfTest.Run();
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var context = new ScrollApplicationContext())
                {
                    Application.Run(context);
                }
            }

            return 0;
        }
    }

    internal sealed class ScrollApplicationContext : ApplicationContext
    {
        private readonly HotkeyWindow hotkeyWindow;
        private readonly KeyboardHook keyboardHook;
        private readonly UiAutomationBridge automation;
        private readonly UtilitySettings settings;
        private readonly NotifyIcon trayIcon;
        private readonly ToolStripMenuItem enabledItem;
        private readonly ToolStripMenuItem readingModeItem;
        private readonly ToolStripMenuItem summaryItem;
        private readonly ToolStripMenuItem diagnosticsItem;
        private readonly ToolStripMenuItem diagnosticModeItem;
        private readonly ToolStripMenuItem diagnosticShortcutItem;
        private readonly ToolStripMenuItem diagnosticAutomationItem;
        private readonly ToolStripMenuItem diagnosticVersionItem;
        private readonly ToolStripMenuItem spaceScrollItem;
        private readonly ToolStripMenuItem[] shortcutChoiceItems;
        private readonly ToolStripMenuItem[] speedChoiceItems;
        private readonly System.Windows.Forms.Timer pollTimer;
        private readonly System.Windows.Forms.Timer deferredFocusTimer;

        private bool toolEnabled = true;
        private bool readingMode;
        private bool previousLeftButtonDown;
        private Point pendingClick;
        private int pendingClickTick;
        private bool hasPendingClick;
        private bool disposed;
        private int nextFocusToggleRetryTick;
        private int lastFocusToggleWarningTick;
        private int focusToggleFailureCount;

        internal ScrollApplicationContext()
        {
            settings = UtilitySettings.Load();
            automation = new UiAutomationBridge();
            hotkeyWindow = new HotkeyWindow(HandleScrollHotkey, ToggleFocusMode);

            summaryItem = new ToolStripMenuItem("Ready · Alt+F · UIA")
            {
                Enabled = false
            };

            diagnosticsItem = new ToolStripMenuItem("Status and diagnostics");
            diagnosticModeItem = CreateDiagnosticItem("Mode: waiting for transcript click");
            diagnosticShortcutItem = CreateDiagnosticItem("Focus shortcut: Alt+F");
            diagnosticAutomationItem = CreateDiagnosticItem("UI Automation: locating elements...");
            diagnosticVersionItem = CreateDiagnosticItem("Version: 1.3.0");
            diagnosticsItem.DropDownItems.Add(diagnosticModeItem);
            diagnosticsItem.DropDownItems.Add(diagnosticShortcutItem);
            diagnosticsItem.DropDownItems.Add(diagnosticAutomationItem);
            diagnosticsItem.DropDownItems.Add(diagnosticVersionItem);

            enabledItem = new ToolStripMenuItem("Utility enabled")
            {
                Checked = true,
                CheckOnClick = true
            };
            enabledItem.CheckedChanged += delegate
            {
                toolEnabled = enabledItem.Checked;
                if (!toolEnabled)
                {
                    SetReadingMode(false);
                    hotkeyWindow.DisableFocusToggle();
                    nextFocusToggleRetryTick = 0;
                }
                UpdateMenuText();
            };

            readingModeItem = new ToolStripMenuItem("Reading mode")
            {
                Checked = false,
                CheckOnClick = true
            };
            readingModeItem.CheckedChanged += delegate
            {
                if (readingModeItem.Checked == readingMode)
                {
                    return;
                }

                if (readingModeItem.Checked && (!toolEnabled || !NativeMethods.IsChatGptForeground()))
                {
                    readingModeItem.Checked = false;
                    ShowBalloon("Activate the ChatGPT/Codex window first.", ToolTipIcon.Warning);
                    return;
                }

                SetReadingMode(readingModeItem.Checked);
            };

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { ExitThread(); };

            var shortcutMenu = new ToolStripMenuItem("Focus shortcut");
            shortcutChoiceItems = new[]
            {
                CreateShortcutChoice("Alt+F", FocusShortcut.AltF),
                CreateShortcutChoice("Ctrl+Alt+F", FocusShortcut.CtrlAltF),
                CreateShortcutChoice("Ctrl+Shift+F", FocusShortcut.CtrlShiftF)
            };
            shortcutMenu.DropDownItems.AddRange(shortcutChoiceItems);

            var speedMenu = new ToolStripMenuItem("Scroll speed");
            speedChoiceItems = new[]
            {
                CreateSpeedChoice("Slow", ScrollSpeed.Slow),
                CreateSpeedChoice("Normal", ScrollSpeed.Normal),
                CreateSpeedChoice("Fast", ScrollSpeed.Fast)
            };
            speedMenu.DropDownItems.AddRange(speedChoiceItems);

            spaceScrollItem = new ToolStripMenuItem("Space scrolls the page")
            {
                Checked = settings.SpaceScroll,
                CheckOnClick = true
            };
            spaceScrollItem.CheckedChanged += delegate
            {
                settings.SpaceScroll = spaceScrollItem.Checked;
                settings.Save();
                ReapplyScrollingHotkeys();
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add(summaryItem);
            menu.Items.Add(diagnosticsItem);
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
            trayIcon.DoubleClick += delegate
            {
                string message = toolEnabled
                    ? "The utility is enabled. Right-click the icon for settings."
                    : "The utility is disabled. Enable it from the right-click menu.";
                ShowBalloon(message, toolEnabled ? ToolTipIcon.Info : ToolTipIcon.Warning);
            };

            pollTimer = new System.Windows.Forms.Timer { Interval = 25 };
            pollTimer.Tick += Poll;
            pollTimer.Start();

            deferredFocusTimer = new System.Windows.Forms.Timer { Interval = 60 };
            deferredFocusTimer.Tick += delegate
            {
                deferredFocusTimer.Stop();
                FocusComposerNow();
            };

            string hookError;
            keyboardHook = new KeyboardHook(HandlePrintableKey, out hookError);
            if (hookError != null)
            {
                ShowBalloon(hookError, ToolTipIcon.Warning);
            }

            ShowBalloon("Running. Alt+F switches between the transcript and composer.", ToolTipIcon.Info);
        }

        private static ToolStripMenuItem CreateDiagnosticItem(string text)
        {
            return new ToolStripMenuItem(text) { Enabled = false };
        }

        private ToolStripMenuItem CreateShortcutChoice(string text, FocusShortcut shortcut)
        {
            var item = new ToolStripMenuItem(text)
            {
                Checked = settings.FocusShortcut == shortcut,
                Tag = shortcut
            };
            item.Click += delegate
            {
                settings.FocusShortcut = (FocusShortcut)item.Tag;
                settings.Save();
                foreach (ToolStripMenuItem choice in shortcutChoiceItems)
                {
                    choice.Checked = ReferenceEquals(choice, item);
                }
                hotkeyWindow.DisableFocusToggle();
                nextFocusToggleRetryTick = 0;
            };
            return item;
        }

        private ToolStripMenuItem CreateSpeedChoice(string text, ScrollSpeed speed)
        {
            var item = new ToolStripMenuItem(text)
            {
                Checked = settings.ScrollSpeed == speed,
                Tag = speed
            };
            item.Click += delegate
            {
                settings.ScrollSpeed = (ScrollSpeed)item.Tag;
                settings.Save();
                foreach (ToolStripMenuItem choice in speedChoiceItems)
                {
                    choice.Checked = ReferenceEquals(choice, item);
                }
            };
            return item;
        }

        private void Poll(object sender, EventArgs e)
        {
            bool chatForeground = NativeMethods.IsChatGptForeground();
            UpdateFocusToggleRegistration(toolEnabled && chatForeground);
            if (chatForeground)
            {
                automation.RequestRefresh(NativeMethods.GetForegroundWindow());
            }

            bool leftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
            if (leftDown && !previousLeftButtonDown)
            {
                NativeMethods.POINT point;
                if (NativeMethods.GetCursorPos(out point))
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
                if (!toolEnabled || !chatForeground)
                {
                    SetReadingMode(false);
                }
                else
                {
                    NativeMethods.RECT rect;
                    IntPtr foreground = NativeMethods.GetForegroundWindow();
                    bool inTranscript = NativeMethods.GetWindowRect(foreground, out rect)
                        && RegionClassifier.IsTranscriptClick(rect, pendingClick);
                    SetReadingMode(inTranscript);
                }
            }

            if (readingMode && !chatForeground)
            {
                SetReadingMode(false);
            }
        }

        private void UpdateFocusToggleRegistration(bool shouldBeActive)
        {
            if (!shouldBeActive)
            {
                hotkeyWindow.DisableFocusToggle();
                nextFocusToggleRetryTick = 0;
                focusToggleFailureCount = 0;
                diagnosticShortcutItem.Text = "Focus shortcut: " + UtilitySettings.ShortcutText(settings.FocusShortcut);
                UpdateMenuText();
                return;
            }

            if (hotkeyWindow.IsFocusToggleRegistered)
            {
                focusToggleFailureCount = 0;
                return;
            }

            int now = Environment.TickCount;
            if (nextFocusToggleRetryTick != 0
                && unchecked(now - nextFocusToggleRetryTick) < 0)
            {
                return;
            }

            nextFocusToggleRetryTick = unchecked(now + 2000);
            string error;
            if (!hotkeyWindow.EnableFocusToggle(settings.FocusShortcut, out error))
            {
                focusToggleFailureCount++;
                diagnosticShortcutItem.Text = "Focus shortcut: registration retry "
                    + focusToggleFailureCount;
                if (lastFocusToggleWarningTick == 0
                    || unchecked(now - lastFocusToggleWarningTick) >= 30000)
                {
                    lastFocusToggleWarningTick = now;
                    ShowBalloon(error + " The utility will retry automatically.", ToolTipIcon.Warning);
                }
                UpdateMenuText();
                return;
            }

            nextFocusToggleRetryTick = 0;
            focusToggleFailureCount = 0;
            diagnosticShortcutItem.Text = "Focus shortcut: " + hotkeyWindow.FocusToggleText;
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
                if (!hotkeyWindow.EnableScrollingHotkeys(settings.SpaceScroll, out error))
                {
                    readingMode = false;
                    readingModeItem.Checked = false;
                    ShowBalloon(error, ToolTipIcon.Error);
                    UpdateMenuText();
                    return;
                }
            }
            else
            {
                hotkeyWindow.DisableScrollingHotkeys();
            }

            readingMode = enabled;
            if (readingModeItem.Checked != enabled)
            {
                readingModeItem.Checked = enabled;
            }
            UpdateMenuText();
        }

        private void ReapplyScrollingHotkeys()
        {
            if (!readingMode)
            {
                return;
            }

            hotkeyWindow.DisableScrollingHotkeys();
            string error;
            if (!hotkeyWindow.EnableScrollingHotkeys(settings.SpaceScroll, out error))
            {
                readingMode = false;
                readingModeItem.Checked = false;
                ShowBalloon(error, ToolTipIcon.Error);
            }
            UpdateMenuText();
        }

        private void HandleScrollHotkey(ScrollCommand command)
        {
            if (!toolEnabled || !readingMode || !NativeMethods.IsChatGptForeground())
            {
                SetReadingMode(false);
                return;
            }

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (automation.TryScroll(foreground, command, settings.ScrollSpeed))
            {
                return;
            }

            int delta;
            int lineDelta = settings.ScrollSpeed == ScrollSpeed.Slow ? 80
                : settings.ScrollSpeed == ScrollSpeed.Fast ? NativeMethods.WHEEL_DELTA * 2 : NativeMethods.WHEEL_DELTA;
            int pageMultiplier = settings.ScrollSpeed == ScrollSpeed.Slow ? 4
                : settings.ScrollSpeed == ScrollSpeed.Fast ? 10 : 7;
            switch (command)
            {
                case ScrollCommand.LineUp:
                    delta = lineDelta;
                    break;
                case ScrollCommand.LineDown:
                    delta = -lineDelta;
                    break;
                case ScrollCommand.PageUp:
                    delta = NativeMethods.WHEEL_DELTA * pageMultiplier;
                    break;
                case ScrollCommand.PageDown:
                    delta = -NativeMethods.WHEEL_DELTA * pageMultiplier;
                    break;
                default:
                    return;
            }

            NativeMethods.ScrollForegroundChat(delta);
        }

        private void ToggleFocusMode()
        {
            if (!toolEnabled || !NativeMethods.IsChatGptForeground())
            {
                return;
            }

            if (readingMode)
            {
                SetReadingMode(false);
                deferredFocusTimer.Stop();
                deferredFocusTimer.Start();
            }
            else
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (!automation.TryFocusTranscript(foreground))
                {
                    NativeMethods.FocusTranscript();
                }
                SetReadingMode(true);
            }
        }

        private void FocusComposerNow()
        {
            if (!toolEnabled || !NativeMethods.IsChatGptForeground())
            {
                return;
            }

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (!automation.TryFocusComposer(foreground))
            {
                NativeMethods.FocusComposer();
            }
        }

        private bool HandlePrintableKey(KeyboardStroke stroke)
        {
            if (!toolEnabled || !readingMode || !NativeMethods.IsChatGptForeground())
            {
                return false;
            }

            if (!PrintableKeyClassifier.IsTypingKey(stroke.VirtualKey, settings.SpaceScroll)
                || NativeMethods.IsCommandModifierDown())
            {
                return false;
            }

            SetReadingMode(false);
            if (automation.TryFocusComposer(NativeMethods.GetForegroundWindow()))
            {
                return NativeMethods.ReplayKeyboard(stroke);
            }
            return NativeMethods.FocusComposerAndReplay(stroke);
        }

        private void UpdateMenuText()
        {
            if (!toolEnabled)
            {
                diagnosticModeItem.Text = "Mode: disabled";
            }
            else if (readingMode)
            {
                diagnosticModeItem.Text = "Mode: reading (navigation keys captured)";
            }
            else
            {
                diagnosticModeItem.Text = "Mode: ready for transcript focus";
            }

            diagnosticAutomationItem.Text = automation.StatusText;
            string state = !toolEnabled ? "Disabled" : readingMode ? "Reading" : "Ready";
            string shortcut = hotkeyWindow.IsFocusToggleRegistered
                ? hotkeyWindow.FocusToggleText.Replace(" (fallback)", string.Empty)
                : focusToggleFailureCount > 0 ? "Hotkey retry" : UtilitySettings.ShortcutText(settings.FocusShortcut);
            summaryItem.Text = state + " · " + shortcut + " · " + automation.CompactStatus;

            if (readingModeItem.Checked != readingMode)
            {
                readingModeItem.Checked = readingMode;
            }
        }

        private void ShowBalloon(string message, ToolTipIcon icon)
        {
            trayIcon.BalloonTipTitle = "Codex Keyboard Scroll";
            trayIcon.BalloonTipText = message;
            trayIcon.BalloonTipIcon = icon;
            trayIcon.ShowBalloonTip(3500);
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
            pollTimer.Dispose();
            deferredFocusTimer.Stop();
            deferredFocusTimer.Dispose();
            keyboardHook.Dispose();
            automation.Dispose();
            hotkeyWindow.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            base.Dispose();
        }
    }

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

    internal sealed class UtilitySettings
    {
        private static readonly string SettingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "CodexKeyboardScroll.settings.ini");

        internal FocusShortcut FocusShortcut = FocusShortcut.AltF;
        internal ScrollSpeed ScrollSpeed = ScrollSpeed.Normal;
        internal bool SpaceScroll = true;

        internal static UtilitySettings Load()
        {
            var settings = new UtilitySettings();
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return settings;
                }

                foreach (string rawLine in File.ReadAllLines(SettingsPath))
                {
                    string[] parts = rawLine.Split(new[] { '=' }, 2);
                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();
                    FocusShortcut shortcut;
                    ScrollSpeed speed;
                    bool flag;
                    if (string.Equals(key, "FocusShortcut", StringComparison.OrdinalIgnoreCase)
                        && Enum.TryParse(value, true, out shortcut))
                    {
                        settings.FocusShortcut = shortcut;
                    }
                    else if (string.Equals(key, "ScrollSpeed", StringComparison.OrdinalIgnoreCase)
                        && Enum.TryParse(value, true, out speed))
                    {
                        settings.ScrollSpeed = speed;
                    }
                    else if (string.Equals(key, "SpaceScroll", StringComparison.OrdinalIgnoreCase)
                        && bool.TryParse(value, out flag))
                    {
                        settings.SpaceScroll = flag;
                    }
                }
            }
            catch
            {
                // A missing, unavailable, or malformed settings file must not block startup.
            }
            return settings;
        }

        internal void Save()
        {
            string temporary = SettingsPath + ".tmp";
            try
            {
                File.WriteAllLines(temporary, new[]
                {
                    "FocusShortcut=" + FocusShortcut,
                    "ScrollSpeed=" + ScrollSpeed,
                    "SpaceScroll=" + SpaceScroll
                });
                if (File.Exists(SettingsPath))
                {
                    File.Replace(temporary, SettingsPath, null);
                }
                else
                {
                    File.Move(temporary, SettingsPath);
                }
            }
            catch
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch
                {
                }
            }
        }

        internal static string ShortcutText(FocusShortcut shortcut)
        {
            switch (shortcut)
            {
                case FocusShortcut.CtrlAltF:
                    return "Ctrl+Alt+F";
                case FocusShortcut.CtrlShiftF:
                    return "Ctrl+Shift+F";
                default:
                    return "Alt+F";
            }
        }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;

        private const int HotkeyFocusToggle = 100;

        private const int HotkeyUp = 101;
        private const int HotkeyDown = 102;
        private const int HotkeyPageUp = 103;
        private const int HotkeyPageDown = 104;
        private const int HotkeySpace = 105;
        private const int HotkeyShiftSpace = 106;

        private readonly Action<ScrollCommand> handler;
        private readonly Action focusHandler;
        private readonly List<int> registeredIds = new List<int>();
        private bool disposed;
        private bool focusToggleRegistered;
        private string focusToggleText = "Alt+F";

        internal HotkeyWindow(Action<ScrollCommand> handler, Action focusHandler)
        {
            this.handler = handler;
            this.focusHandler = focusHandler;
            var parameters = new CreateParams
            {
                Caption = "CodexKeyboardScrollMessageWindow",
                Parent = new IntPtr(-3)
            };
            CreateHandle(parameters);
        }

        internal bool IsFocusToggleRegistered { get { return focusToggleRegistered; } }
        internal string FocusToggleText { get { return focusToggleText; } }

        internal bool EnableFocusToggle(FocusShortcut preferred, out string error)
        {
            DisableFocusToggle();

            var order = new[] { preferred, FocusShortcut.AltF, FocusShortcut.CtrlAltF, FocusShortcut.CtrlShiftF };
            var attempted = new HashSet<FocusShortcut>();
            foreach (FocusShortcut shortcut in order)
            {
                if (!attempted.Add(shortcut))
                {
                    continue;
                }

                uint modifiers = GetShortcutModifiers(shortcut) | MOD_NOREPEAT;
                if (NativeMethods.RegisterHotKey(Handle, HotkeyFocusToggle, modifiers, (uint)Keys.F))
                {
                    focusToggleRegistered = true;
                    focusToggleText = UtilitySettings.ShortcutText(shortcut);
                    if (shortcut != preferred)
                    {
                        focusToggleText += " (fallback)";
                    }
                    error = null;
                    return true;
                }
            }

            error = "All available F-based shortcuts are currently in use by another application.";
            return false;
        }

        private static uint GetShortcutModifiers(FocusShortcut shortcut)
        {
            switch (shortcut)
            {
                case FocusShortcut.CtrlAltF:
                    return MOD_CONTROL | MOD_ALT;
                case FocusShortcut.CtrlShiftF:
                    return MOD_CONTROL | MOD_SHIFT;
                default:
                    return MOD_ALT;
            }
        }

        internal void DisableFocusToggle()
        {
            if (!focusToggleRegistered)
            {
                return;
            }

            NativeMethods.UnregisterHotKey(Handle, HotkeyFocusToggle);
            focusToggleRegistered = false;
        }

        internal bool EnableScrollingHotkeys(bool captureSpace, out string error)
        {
            DisableScrollingHotkeys();

            var definitions = new List<HotkeyDefinition>
            {
                new HotkeyDefinition(HotkeyUp, 0, Keys.Up),
                new HotkeyDefinition(HotkeyDown, 0, Keys.Down),
                new HotkeyDefinition(HotkeyPageUp, 0, Keys.PageUp),
                new HotkeyDefinition(HotkeyPageDown, 0, Keys.PageDown)
            };
            if (captureSpace)
            {
                definitions.Add(new HotkeyDefinition(HotkeySpace, 0, Keys.Space));
                definitions.Add(new HotkeyDefinition(HotkeyShiftSpace, MOD_SHIFT, Keys.Space));
            }

            foreach (HotkeyDefinition definition in definitions)
            {
                if (!NativeMethods.RegisterHotKey(Handle, definition.Id, definition.Modifiers, (uint)definition.Key))
                {
                    int win32Error = Marshal.GetLastWin32Error();
                    DisableScrollingHotkeys();
                    error = "Could not capture the navigation keys (Win32 " + win32Error
                        + "). Another application may be using them.";
                    return false;
                }
                registeredIds.Add(definition.Id);
            }

            error = null;
            return true;
        }

        internal void DisableScrollingHotkeys()
        {
            foreach (int id in registeredIds)
            {
                NativeMethods.UnregisterHotKey(Handle, id);
            }
            registeredIds.Clear();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WM_HOTKEY)
            {
                switch (message.WParam.ToInt32())
                {
                    case HotkeyFocusToggle:
                        focusHandler();
                        return;
                    case HotkeyUp:
                        handler(ScrollCommand.LineUp);
                        return;
                    case HotkeyDown:
                        handler(ScrollCommand.LineDown);
                        return;
                    case HotkeyPageUp:
                        handler(ScrollCommand.PageUp);
                        return;
                    case HotkeyPageDown:
                    case HotkeySpace:
                        handler(ScrollCommand.PageDown);
                        return;
                    case HotkeyShiftSpace:
                        handler(ScrollCommand.PageUp);
                        return;
                }
            }

            base.WndProc(ref message);
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

    internal sealed class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const uint LLKHF_INJECTED = 0x10;

        private readonly Func<KeyboardStroke, bool> handler;
        private readonly HookProc hookProc;
        private readonly HashSet<uint> suppressedKeyUps = new HashSet<uint>();
        private IntPtr hook;
        private bool disposed;

        internal KeyboardHook(Func<KeyboardStroke, bool> handler, out string error)
        {
            this.handler = handler;
            hookProc = HookCallback;
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                error = "Could not enable automatic composer focus (Win32 "
                    + Marshal.GetLastWin32Error() + ").";
            }
            else
            {
                error = null;
            }
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                KBDLLHOOKSTRUCT data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                if ((data.Flags & LLKHF_INJECTED) == 0)
                {
                    int message = wParam.ToInt32();
                    if (message == WM_KEYUP || message == WM_SYSKEYUP)
                    {
                        if (suppressedKeyUps.Remove(data.VirtualKey))
                        {
                            return new IntPtr(1);
                        }
                    }
                    else if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                    {
                        var stroke = new KeyboardStroke(data.VirtualKey, data.ScanCode, data.Flags);
                        if (handler(stroke))
                        {
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
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
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
        private struct KBDLLHOOKSTRUCT
        {
            internal uint VirtualKey;
            internal uint ScanCode;
            internal uint Flags;
            internal uint Time;
            internal IntPtr ExtraInfo;
        }
    }

    internal static class PrintableKeyClassifier
    {
        internal static bool IsTypingKey(uint virtualKey, bool spaceScroll)
        {
            if (virtualKey == 0x20)
            {
                return !spaceScroll;
            }

            if ((virtualKey >= 0x30 && virtualKey <= 0x39)
                || (virtualKey >= 0x41 && virtualKey <= 0x5A)
                || (virtualKey >= 0x60 && virtualKey <= 0x69))
            {
                return true;
            }

            if ((virtualKey >= 0xBA && virtualKey <= 0xC0)
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

    internal sealed class UiAutomationBridge : IDisposable
    {
        private readonly object sync = new object();
        private AutomationElement composer;
        private AutomationElement transcript;
        private ScrollPattern scrollPattern;
        private IntPtr cachedWindow;
        private int lastRefreshTick;
        private int refreshRunning;
        private bool scrollVerified;
        private bool disposed;

        internal string StatusText
        {
            get
            {
                lock (sync)
                {
                    if (composer != null && scrollPattern != null)
                    {
                        return scrollVerified
                            ? "UI Automation: composer and scrolling active"
                            : "UI Automation: composer found, scrolling uses fallback";
                    }
                    if (composer != null)
                    {
                        return "UI Automation: composer found";
                    }
                    return "UI Automation: coordinate fallback active";
                }
            }
        }

        internal string CompactStatus
        {
            get
            {
                lock (sync)
                {
                    return composer != null ? "UIA" : "Fallback";
                }
            }
        }

        internal void RequestRefresh(IntPtr window)
        {
            if (disposed || window == IntPtr.Zero)
            {
                return;
            }

            int now = Environment.TickCount;
            lock (sync)
            {
                if (cachedWindow == window && unchecked(now - lastRefreshTick) < 2000)
                {
                    return;
                }
                lastRefreshTick = now;
            }

            if (Interlocked.CompareExchange(ref refreshRunning, 1, 0) != 0)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Refresh(window);
                }
                finally
                {
                    Interlocked.Exchange(ref refreshRunning, 0);
                }
            });
        }

        private void Refresh(IntPtr window)
        {
            AutomationElement foundComposer = null;
            AutomationElement foundTranscript = null;
            ScrollPattern foundScrollPattern = null;

            try
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                if (root == null)
                {
                    return;
                }

                System.Windows.Rect rootRect = root.Current.BoundingRectangle;
                AutomationElementCollection edits = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                double composerScore = double.MinValue;
                foreach (AutomationElement element in edits)
                {
                    try
                    {
                        System.Windows.Rect rect = element.Current.BoundingRectangle;
                        if (rect.IsEmpty || rect.Width < 140 || rect.Height < 18
                            || rect.Top < rootRect.Top + rootRect.Height * 0.50
                            || rect.Bottom > rootRect.Bottom + 2
                            || !element.Current.IsEnabled)
                        {
                            continue;
                        }

                        string name = element.Current.Name ?? string.Empty;
                        double score = rect.Top + rect.Width;
                        if (element.Current.IsKeyboardFocusable)
                        {
                            score += 10000;
                        }
                        if (ContainsComposerHint(name))
                        {
                            score += 20000;
                        }
                        if (score > composerScore)
                        {
                            composerScore = score;
                            foundComposer = element;
                        }
                    }
                    catch (ElementNotAvailableException)
                    {
                    }
                }

                AutomationElementCollection scrollables = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));
                double scrollScore = double.MinValue;
                foreach (AutomationElement element in scrollables)
                {
                    try
                    {
                        object rawPattern;
                        if (!element.TryGetCurrentPattern(ScrollPattern.Pattern, out rawPattern))
                        {
                            continue;
                        }
                        var pattern = (ScrollPattern)rawPattern;
                        if (!pattern.Current.VerticallyScrollable)
                        {
                            continue;
                        }

                        System.Windows.Rect rect = element.Current.BoundingRectangle;
                        if (rect.IsEmpty || rect.Width < 250 || rect.Height < 180)
                        {
                            continue;
                        }

                        double score = rect.Width * rect.Height;
                        if (rect.Top < rootRect.Top + rootRect.Height * 0.35)
                        {
                            score += rootRect.Width * rootRect.Height;
                        }
                        if (score > scrollScore)
                        {
                            scrollScore = score;
                            foundTranscript = element;
                            foundScrollPattern = pattern;
                        }
                    }
                    catch (ElementNotAvailableException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            catch
            {
                foundComposer = null;
                foundTranscript = null;
                foundScrollPattern = null;
            }

            if (disposed)
            {
                return;
            }
            lock (sync)
            {
                cachedWindow = window;
                composer = foundComposer;
                transcript = foundTranscript;
                scrollPattern = foundScrollPattern;
                scrollVerified = false;
            }
        }

        internal bool TryFocusComposer(IntPtr window)
        {
            AutomationElement element;
            lock (sync)
            {
                element = cachedWindow == window ? composer : null;
            }
            if (element == null)
            {
                RequestRefresh(window);
                return false;
            }

            try
            {
                element.SetFocus();
                Thread.Sleep(10);
                if (element.Current.HasKeyboardFocus)
                {
                    return true;
                }
                RequestRefresh(window);
                return false;
            }
            catch
            {
                RequestRefresh(window);
                return false;
            }
        }

        internal bool TryFocusTranscript(IntPtr window)
        {
            AutomationElement element;
            lock (sync)
            {
                element = cachedWindow == window ? transcript : null;
            }
            if (element == null)
            {
                RequestRefresh(window);
                return false;
            }

            try
            {
                if (!element.Current.IsKeyboardFocusable)
                {
                    return false;
                }
                element.SetFocus();
                Thread.Sleep(10);
                if (element.Current.HasKeyboardFocus)
                {
                    return true;
                }
                RequestRefresh(window);
                return false;
            }
            catch
            {
                RequestRefresh(window);
                return false;
            }
        }

        internal bool TryScroll(IntPtr window, ScrollCommand command, ScrollSpeed speed)
        {
            ScrollPattern pattern;
            lock (sync)
            {
                pattern = cachedWindow == window ? scrollPattern : null;
            }
            if (pattern == null)
            {
                RequestRefresh(window);
                return false;
            }

            ScrollAmount amount = command == ScrollCommand.LineUp || command == ScrollCommand.PageUp
                ? (command == ScrollCommand.LineUp ? ScrollAmount.SmallDecrement : ScrollAmount.LargeDecrement)
                : (command == ScrollCommand.LineDown ? ScrollAmount.SmallIncrement : ScrollAmount.LargeIncrement);
            int repeats = speed == ScrollSpeed.Fast ? 2 : 1;
            try
            {
                double before = pattern.Current.VerticalScrollPercent;
                if (before < 0)
                {
                    return false;
                }
                for (int index = 0; index < repeats; index++)
                {
                    pattern.Scroll(ScrollAmount.NoAmount, amount);
                }
                Thread.Sleep(15);
                double after = pattern.Current.VerticalScrollPercent;
                if (Math.Abs(after - before) > 0.001)
                {
                    lock (sync)
                    {
                        scrollVerified = true;
                    }
                    return true;
                }
                return false;
            }
            catch
            {
                RequestRefresh(window);
                return false;
            }
        }

        private static bool ContainsComposerHint(string value)
        {
            string normalized = value.ToLowerInvariant();
            return normalized.Contains("message")
                || normalized.Contains("prompt")
                || normalized.Contains("ask")
                || normalized.Contains("\u0441\u043e\u043e\u0431\u0449")
                || normalized.Contains("\u0432\u043e\u043f\u0440\u043e\u0441");
        }

        public void Dispose()
        {
            disposed = true;
            lock (sync)
            {
                composer = null;
                transcript = null;
                scrollPattern = null;
                scrollVerified = false;
                cachedWindow = IntPtr.Zero;
            }
        }
    }

    internal static class RegionClassifier
    {
        internal static bool IsTranscriptClick(NativeMethods.RECT rect, Point point)
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 320 || height < 300)
            {
                return false;
            }

            if (point.X < rect.Left || point.X >= rect.Right || point.Y < rect.Top || point.Y >= rect.Bottom)
            {
                return false;
            }

            int leftGuard = width < 900 ? 24 : Math.Max(220, (int)(width * 0.16));
            int topGuard = 55;
            int bottomGuard = Math.Max(150, Math.Min(280, (int)(height * 0.22)));

            return point.X >= rect.Left + leftGuard
                && point.X < rect.Right - 20
                && point.Y >= rect.Top + topGuard
                && point.Y < rect.Bottom - bottomGuard;
        }

        internal static Point GetScrollTarget(NativeMethods.RECT rect)
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            return new Point(
                rect.Left + (int)(width * 0.55),
                rect.Top + (int)(height * 0.42));
        }

        internal static Point GetComposerTarget(NativeMethods.RECT rect)
        {
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            int offset = Math.Max(70, Math.Min(115, (int)(height * 0.10)));
            return new Point(
                rect.Left + (int)(width * 0.55),
                rect.Bottom - offset);
        }
    }

    internal static class NativeMethods
    {
        internal const int VK_LBUTTON = 0x01;
        internal const int WHEEL_DELTA = 120;

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        internal static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr window, out RECT rect);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

        internal static bool IsChatGptForeground()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return false;
            }

            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId == 0)
            {
                return false;
            }

            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    return string.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(process.ProcessName, "Codex", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsCommandModifierDown()
        {
            return IsKeyDown(0x11)
                || IsKeyDown(0x12)
                || IsKeyDown(0x5B)
                || IsKeyDown(0x5C);
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        internal static bool ScrollForegroundChat(int wheelDelta)
        {
            IntPtr window = GetForegroundWindow();
            RECT rect;
            POINT original;
            if (window == IntPtr.Zero || !GetWindowRect(window, out rect) || !GetCursorPos(out original))
            {
                return false;
            }

            Point target = RegionClassifier.GetScrollTarget(rect);
            if (!SetCursorPos(target.X, target.Y))
            {
                return false;
            }

            var mouseInput = new INPUT
            {
                Type = INPUT_MOUSE,
                Data = new InputUnion
                {
                    Mouse = new MOUSEINPUT
                    {
                        MouseData = unchecked((uint)wheelDelta),
                        Flags = MOUSEEVENTF_WHEEL
                    }
                }
            };

            uint sent = SendInput(1, new[] { mouseInput }, Marshal.SizeOf(typeof(INPUT)));
            Thread.Sleep(1);
            SetCursorPos(original.X, original.Y);
            return sent == 1;
        }

        internal static bool FocusTranscript()
        {
            return FocusForegroundTarget(false, null);
        }

        internal static bool FocusComposer()
        {
            return FocusForegroundTarget(true, null);
        }

        internal static bool FocusComposerAndReplay(KeyboardStroke stroke)
        {
            return FocusForegroundTarget(true, stroke);
        }

        internal static bool ReplayKeyboard(KeyboardStroke stroke)
        {
            INPUT[] sequence =
            {
                CreateKeyboard(stroke, false),
                CreateKeyboard(stroke, true)
            };
            uint sent = SendInput((uint)sequence.Length, sequence, Marshal.SizeOf(typeof(INPUT)));
            return sent == (uint)sequence.Length;
        }

        private static bool FocusForegroundTarget(bool composer, KeyboardStroke? replay)
        {
            IntPtr window = GetForegroundWindow();
            RECT rect;
            POINT original;
            if (window == IntPtr.Zero || !GetWindowRect(window, out rect) || !GetCursorPos(out original))
            {
                return false;
            }

            Point target = composer
                ? RegionClassifier.GetComposerTarget(rect)
                : RegionClassifier.GetScrollTarget(rect);

            var inputs = new List<INPUT>();
            inputs.Add(CreateAbsoluteMove(target.X, target.Y));
            inputs.Add(CreateMouseButton(MOUSEEVENTF_LEFTDOWN));
            inputs.Add(CreateMouseButton(MOUSEEVENTF_LEFTUP));
            inputs.Add(CreateAbsoluteMove(original.X, original.Y));

            if (replay.HasValue)
            {
                KeyboardStroke stroke = replay.Value;
                inputs.Add(CreateKeyboard(stroke, false));
                inputs.Add(CreateKeyboard(stroke, true));
            }

            INPUT[] sequence = inputs.ToArray();
            uint sent = SendInput((uint)sequence.Length, sequence, Marshal.SizeOf(typeof(INPUT)));
            return sent == (uint)sequence.Length;
        }

        private static INPUT CreateAbsoluteMove(int x, int y)
        {
            int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int width = Math.Max(2, GetSystemMetrics(SM_CXVIRTUALSCREEN));
            int height = Math.Max(2, GetSystemMetrics(SM_CYVIRTUALSCREEN));
            int absoluteX = (int)Math.Round((x - left) * 65535.0 / (width - 1));
            int absoluteY = (int)Math.Round((y - top) * 65535.0 / (height - 1));

            return new INPUT
            {
                Type = INPUT_MOUSE,
                Data = new InputUnion
                {
                    Mouse = new MOUSEINPUT
                    {
                        Dx = absoluteX,
                        Dy = absoluteY,
                        Flags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK
                    }
                }
            };
        }

        private static INPUT CreateMouseButton(uint flags)
        {
            return new INPUT
            {
                Type = INPUT_MOUSE,
                Data = new InputUnion
                {
                    Mouse = new MOUSEINPUT { Flags = flags }
                }
            };
        }

        private static INPUT CreateKeyboard(KeyboardStroke stroke, bool keyUp)
        {
            uint flags = 0;
            if (stroke.ScanCode != 0)
            {
                flags |= KEYEVENTF_SCANCODE;
            }
            if ((stroke.Flags & 0x01) != 0)
            {
                flags |= KEYEVENTF_EXTENDEDKEY;
            }
            if (keyUp)
            {
                flags |= KEYEVENTF_KEYUP;
            }

            return new INPUT
            {
                Type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    Keyboard = new KEYBDINPUT
                    {
                        VirtualKey = stroke.ScanCode == 0 ? (ushort)stroke.VirtualKey : (ushort)0,
                        ScanCode = (ushort)stroke.ScanCode,
                        Flags = flags
                    }
                }
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;

            internal RECT(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            internal uint Type;
            internal InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            internal MOUSEINPUT Mouse;

            [FieldOffset(0)]
            internal KEYBDINPUT Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            internal int Dx;
            internal int Dy;
            internal uint MouseData;
            internal uint Flags;
            internal uint Time;
            internal IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            internal ushort VirtualKey;
            internal ushort ScanCode;
            internal uint Flags;
            internal uint Time;
            internal IntPtr ExtraInfo;
        }
    }

    internal static class SelfTest
    {
        internal static int Run()
        {
            int failures = 0;
            var normal = new NativeMethods.RECT(100, 100, 1700, 1100);
            failures += Expect(RegionClassifier.IsTranscriptClick(normal, new Point(900, 500)), true);
            failures += Expect(RegionClassifier.IsTranscriptClick(normal, new Point(150, 500)), false);
            failures += Expect(RegionClassifier.IsTranscriptClick(normal, new Point(900, 1030)), false);
            failures += Expect(RegionClassifier.IsTranscriptClick(normal, new Point(900, 120)), false);
            failures += Expect(RegionClassifier.IsTranscriptClick(normal, new Point(50, 500)), false);

            var narrow = new NativeMethods.RECT(0, 0, 700, 800);
            failures += Expect(RegionClassifier.IsTranscriptClick(narrow, new Point(300, 300)), true);
            failures += Expect(RegionClassifier.IsTranscriptClick(narrow, new Point(300, 750)), false);

            Point target = RegionClassifier.GetScrollTarget(normal);
            failures += Expect(target.X > normal.Left && target.X < normal.Right, true);
            failures += Expect(target.Y > normal.Top && target.Y < normal.Bottom, true);

            Point composer = RegionClassifier.GetComposerTarget(normal);
            failures += Expect(composer.X > normal.Left && composer.X < normal.Right, true);
            failures += Expect(composer.Y > normal.Top && composer.Y < normal.Bottom, true);
            failures += Expect(composer.Y > target.Y, true);

            failures += Expect(PrintableKeyClassifier.IsTypingKey(0x41, true), true);
            failures += Expect(PrintableKeyClassifier.IsTypingKey(0x35, true), true);
            failures += Expect(PrintableKeyClassifier.IsTypingKey(0xBA, true), true);
            failures += Expect(PrintableKeyClassifier.IsTypingKey(0x20, true), false);
            failures += Expect(PrintableKeyClassifier.IsTypingKey(0x20, false), true);
            failures += Expect(PrintableKeyClassifier.IsTypingKey(0x25, true), false);
            failures += Expect(UtilitySettings.ShortcutText(FocusShortcut.CtrlAltF) == "Ctrl+Alt+F", true);

            return failures == 0 ? 0 : 1;
        }

        private static int Expect(bool actual, bool expected)
        {
            return actual == expected ? 0 : 1;
        }
    }
}
