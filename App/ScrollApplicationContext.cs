using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace CodexKeyboardScroll
{
    internal sealed class ScrollApplicationContext : ApplicationContext
    {
        private readonly UtilitySettings settings;
        private readonly LocalizationManager localizer;
        private readonly StartupRegistration startupRegistration;
        private readonly UpdateCheckService updateCheckService;
        private readonly ComposerLocator composerLocator;
        private readonly HotkeyWindow hotkeys;
        private readonly KeyboardHook keyboardHook;
        private readonly AppIconSet icons;
        private readonly TrayMenuView menuView;
        private readonly NotifyIcon trayIcon;
        private readonly Timer pollTimer;
        private readonly Timer focusToggleTimer;

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
        private bool updateCheckInProgress;

        internal ScrollApplicationContext()
        {
            settings = UtilitySettings.Load();
            localizer = new LocalizationManager(settings.LanguageCode);
            settings.LanguageCode = localizer.RequestedLanguageCode;
            startupRegistration = new StartupRegistration(Application.ExecutablePath);
            updateCheckService = new UpdateCheckService();
            composerLocator = new ComposerLocator();
            hotkeys = new HotkeyWindow(HandleScrollHotkey, QueueFocusToggle);
            icons = new AppIconSet();
            menuView = new TrayMenuView(localizer, settings, startupRegistration.IsEnabled);
            WireMenuEvents();

            trayIcon = new NotifyIcon
            {
                ContextMenuStrip = menuView.Menu,
                Icon = icons.Waiting,
                Text = TooltipText(ApplicationMode.Waiting),
                Visible = true
            };
            trayIcon.DoubleClick += ShowEnabledState;

            focusToggleTimer = new Timer { Interval = 20 };
            focusToggleTimer.Tick += CompleteFocusToggle;

            pollTimer = new Timer { Interval = 25 };
            pollTimer.Tick += Poll;
            pollTimer.Start();

            int hookError;
            keyboardHook = new KeyboardHook(HandlePrintableKey, out hookError);
            if (hookError != 0)
            {
                ShowBalloon(localizer.Format(UiText.ErrorKeyboardHookUnavailable, hookError), ToolTipIcon.Warning);
            }

            UpdateUi();
            ShowBalloon(
                localizer.Format(
                    UiText.BalloonRunning,
                    UtilitySettings.ShortcutText(settings.FocusShortcut)),
                ToolTipIcon.Info);
        }

        private ApplicationMode CurrentMode
        {
            get
            {
                return !toolEnabled
                    ? ApplicationMode.Disabled
                    : readingMode ? ApplicationMode.Reading : ApplicationMode.Waiting;
            }
        }

        private void WireMenuEvents()
        {
            menuView.EnabledChanged += ToggleEnabled;
            menuView.ReadingModeChanged += ToggleReadingMode;
            menuView.ShortcutChanged += ChangeShortcut;
            menuView.SpeedChanged += ChangeSpeed;
            menuView.SpaceScrollChanged += ToggleSpaceBehavior;
            menuView.StartupChanged += ToggleStartup;
            menuView.LanguageChanged += ChangeLanguage;
            menuView.OpenRepositoryRequested += OpenRepository;
            menuView.CheckUpdatesRequested += CheckForUpdates;
            menuView.OpenLatestReleaseRequested += OpenLatestRelease;
            menuView.ExitRequested += ExitThread;
            menuView.Opening += UpdateUi;
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
                if (stateChanged) UpdateUi();
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
            HotkeyError error;
            if (!hotkeys.EnableFocusToggle(settings.FocusShortcut, out error))
            {
                hotkeyFailureCount++;
                if (lastHotkeyWarningTick == 0 || unchecked(now - lastHotkeyWarningTick) >= 30000)
                {
                    lastHotkeyWarningTick = now;
                    ShowBalloon(
                        localizer.Format(
                            UiText.BalloonHotkeyRetry,
                            LocalizeHotkeyError(error)),
                        ToolTipIcon.Warning);
                }
                UpdateUi();
                return;
            }

            nextHotkeyRetryTick = 0;
            hotkeyFailureCount = 0;
            UpdateUi();
        }

        private void SetReadingMode(bool enabled)
        {
            if (readingMode == enabled)
            {
                UpdateUi();
                return;
            }

            if (enabled)
            {
                HotkeyError error;
                if (!hotkeys.EnableScrollingHotkeys(settings.SpaceScroll, out error))
                {
                    ShowBalloon(LocalizeHotkeyError(error), ToolTipIcon.Error);
                    UpdateUi();
                    return;
                }
            }
            else
            {
                hotkeys.DisableScrollingHotkeys();
            }

            readingMode = enabled;
            UpdateUi();
        }

        private void HandleScrollHotkey(ScrollCommand command)
        {
            if (!toolEnabled || !readingMode || !NativeInput.IsChatForeground())
            {
                SetReadingMode(false);
                return;
            }

            NativeInput.ScrollForeground(
                ScrollProfile.WheelDelta(command, settings.ScrollSpeedLevel));
        }

        private void QueueFocusToggle()
        {
            if (toolEnabled && NativeInput.IsChatForeground())
            {
                focusToggleTimer.Start();
            }
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

        private void ToggleEnabled(bool enabled)
        {
            toolEnabled = enabled;
            if (!toolEnabled)
            {
                SetReadingMode(false);
                hotkeys.DisableFocusToggle();
                nextHotkeyRetryTick = 0;
            }
            UpdateUi();
        }

        private void ToggleReadingMode(bool enabled)
        {
            if (enabled && (!toolEnabled || !NativeInput.IsChatForeground()))
            {
                ShowBalloon(localizer.Text(UiText.BalloonActivateCodex), ToolTipIcon.Warning);
                UpdateUi();
                return;
            }
            SetReadingMode(enabled);
        }

        private void ChangeShortcut(FocusShortcut shortcut)
        {
            settings.FocusShortcut = shortcut;
            SaveSettings();
            hotkeys.DisableFocusToggle();
            nextHotkeyRetryTick = 0;
            UpdateUi();
        }

        private void ChangeSpeed(decimal level)
        {
            settings.ScrollSpeedLevel = ScrollProfile.NormalizeLevel(level);
            SaveSettings();
            UpdateUi();
        }

        private void OpenRepository()
        {
            OpenLink(RepositoryLinks.RepositoryUrl);
        }

        private void OpenLatestRelease()
        {
            OpenLink(RepositoryLinks.LatestReleaseUrl);
        }

        private async void CheckForUpdates()
        {
            if (updateCheckInProgress)
            {
                return;
            }

            updateCheckInProgress = true;
            menuView.SetUpdateCheckStatus(UpdateCheckStatus.Checking, null);
            Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            UpdateCheckResult result = await updateCheckService.CheckAsync(currentVersion);
            if (disposed)
            {
                return;
            }

            updateCheckInProgress = false;
            menuView.SetUpdateCheckStatus(result.Status, result.LatestVersion);
        }

        private void OpenLink(string url)
        {
            if (!RepositoryLinks.TryOpen(url))
            {
                ShowBalloon(localizer.Text(UiText.ErrorOpenLink), ToolTipIcon.Error);
            }
        }

        private void ToggleSpaceBehavior(bool enabled)
        {
            settings.SpaceScroll = enabled;
            SaveSettings();
            if (readingMode)
            {
                hotkeys.DisableScrollingHotkeys();
                readingMode = false;
                SetReadingMode(true);
            }
            UpdateUi();
        }

        private void ToggleStartup(bool enabled)
        {
            if (!startupRegistration.TrySetEnabled(enabled))
            {
                menuView.SetStartupEnabled(startupRegistration.IsEnabled);
                ShowBalloon(
                    localizer.Text(enabled ? UiText.ErrorStartupEnable : UiText.ErrorStartupDisable),
                    ToolTipIcon.Error);
                return;
            }
            UpdateUi();
        }

        private void ChangeLanguage(string languageCode)
        {
            settings.LanguageCode = languageCode;
            localizer.SetLanguage(languageCode);
            SaveSettings();
            menuView.ApplyLocalization(settings);
            UpdateUi();
        }

        private void ShowEnabledState()
        {
            ShowBalloon(
                localizer.Text(toolEnabled ? UiText.BalloonEnabled : UiText.BalloonDisabled),
                toolEnabled ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }

        private void ShowEnabledState(object sender, EventArgs e)
        {
            ShowEnabledState();
        }

        private void UpdateUi()
        {
            ApplicationMode mode = CurrentMode;
            string shortcut = hotkeys.IsFocusToggleRegistered
                ? hotkeys.FocusToggleText
                : UtilitySettings.ShortcutText(settings.FocusShortcut);
            menuView.UpdateState(new TrayMenuState
            {
                Mode = mode,
                ComposerStatus = composerLocator.Status,
                ShortcutText = shortcut,
                UsingFallbackShortcut = hotkeys.UsingFallbackShortcut,
                HotkeyRetryCount = hotkeyFailureCount,
                StartupEnabled = startupRegistration.IsEnabled
            }, settings);
            trayIcon.Icon = mode == ApplicationMode.Reading ? icons.Active : icons.Waiting;
            trayIcon.Text = TooltipText(mode);
        }

        private string TooltipText(ApplicationMode mode)
        {
            string value = localizer.Text(
                mode == ApplicationMode.Reading
                    ? UiText.TooltipActive
                    : mode == ApplicationMode.Disabled
                        ? UiText.TooltipDisabled
                        : UiText.TooltipWaiting);
            return value.Length <= 63 ? value : value.Substring(0, 63);
        }

        private string LocalizeHotkeyError(HotkeyError error)
        {
            return error.Kind == HotkeyErrorKind.NavigationKeysUnavailable
                ? localizer.Format(UiText.ErrorNavigationKeysUnavailable, error.Win32Error)
                : localizer.Text(UiText.ErrorFocusShortcutsUnavailable);
        }

        private void SaveSettings()
        {
            if (!settings.Save())
            {
                ShowBalloon(localizer.Text(UiText.ErrorSettingsSave), ToolTipIcon.Warning);
            }
        }

        private void ShowBalloon(string message, ToolTipIcon icon)
        {
            trayIcon.BalloonTipTitle = localizer.Text(UiText.AppName);
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
            focusToggleTimer.Stop();
            pollTimer.Dispose();
            focusToggleTimer.Dispose();
            keyboardHook.Dispose();
            updateCheckService.Dispose();
            composerLocator.Dispose();
            hotkeys.Dispose();
            trayIcon.Visible = false;
            trayIcon.ContextMenuStrip = null;
            trayIcon.Dispose();
            menuView.Dispose();
            icons.Dispose();
            base.Dispose();
        }
    }
}
