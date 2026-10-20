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
        private readonly Timer updateCheckTimer;

        private bool toolEnabled = true;
        private bool readingMode;
        private bool previousLeftButtonDown;
        private bool hasPendingClick;
        private Point pendingClick;
        private int pendingClickTick;
        private int focusToggleTick;
        private int nextHotkeyRetryTick;
        private int lastHotkeyWarningTick;
        private int hotkeyFailureCount;
        private bool disposed;
        private bool updateCheckInProgress;
        private IntPtr observedWindow;
        private IntPtr readingWindow;
        private IntPtr focusToggleWindow;
        private IntPtr pendingClickWindow;
        private bool pendingTranscriptFocus;
        private int transcriptFocusTick;

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

            focusToggleTimer = new Timer { Interval = 20 };
            focusToggleTimer.Tick += CompleteFocusToggle;

            updateCheckTimer = new Timer { Interval = 60 * 60 * 1000 };
            updateCheckTimer.Tick += CheckAutomaticUpdates;
            updateCheckTimer.Start();

            pollTimer = new Timer { Interval = 25 };
            pollTimer.Tick += Poll;
            pollTimer.Start();

            int hookError;
            keyboardHook = new KeyboardHook(HandleKeyboardStroke, out hookError);
            if (hookError != 0)
            {
                ShowBalloon(localizer.Format(UiText.ErrorKeyboardHookUnavailable, hookError), ToolTipIcon.Warning);
            }

            RestoreKnownUpdate();
            UpdateUi();
            ShowBalloon(
                localizer.Format(
                    UiText.BalloonRunning,
                    UtilitySettings.ShortcutText(settings.FocusShortcut)),
                ToolTipIcon.Info);
            BeginAutomaticUpdateCheckIfDue(false);
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
            menuView.ClaudeEnabledChanged += ToggleClaude;
            menuView.ShortcutChanged += ChangeShortcut;
            menuView.SpeedChanged += ChangeSpeed;
            menuView.SpaceScrollChanged += ToggleSpaceBehavior;
            menuView.StartupChanged += ToggleStartup;
            menuView.AutomaticUpdateChecksChanged += ToggleAutomaticUpdateChecks;
            menuView.LanguageChanged += ChangeLanguage;
            menuView.OpenRepositoryRequested += OpenRepository;
            menuView.CheckUpdatesRequested += CheckForUpdates;
            menuView.OpenLatestReleaseRequested += OpenLatestRelease;
            menuView.ExitRequested += ExitThread;
            menuView.Opening += UpdateUi;
        }

        private void Poll(object sender, EventArgs e)
        {
            IntPtr window = NativeInput.GetForegroundWindow();
            ObserveWindow(window);
            bool chatForeground = IsEnabledWindow(window);
            if (chatForeground)
            {
                composerLocator.RequestRefresh(window);
            }
            UpdateFocusHotkey(toolEnabled && chatForeground && CanControlWindow(window));

            bool leftDown = (NativeInput.GetAsyncKeyState(NativeInput.VkLeftButton) & 0x8000) != 0;
            if (leftDown && !previousLeftButtonDown)
            {
                if (pendingTranscriptFocus) CancelPendingInput();
                NativeInput.PointNative point;
                if (NativeInput.GetCursorPos(out point))
                {
                    pendingClick = new Point(point.X, point.Y);
                    pendingClickTick = Environment.TickCount;
                    pendingClickWindow = window;
                    hasPendingClick = true;
                }
            }
            previousLeftButtonDown = leftDown;

            if (hasPendingClick && unchecked(Environment.TickCount - pendingClickTick) >= 50)
            {
                hasPendingClick = false;
                SetReadingMode(toolEnabled && chatForeground && pendingClickWindow == window
                    && CanControlWindow(window) && ClickWasInTranscript(pendingClick));
            }

            if (readingMode && (!chatForeground
                || composerLocator.HasFocusedInput(NativeInput.GetForegroundWindow())))
            {
                SetReadingMode(false);
            }
        }

        private bool IsEnabledWindow(IntPtr window)
        {
            return window != IntPtr.Zero && window == NativeInput.GetForegroundWindow()
                && NativeInput.IsApplicationEnabled(
                    NativeInput.GetChatApplication(window), settings.ClaudeEnabled);
        }

        private bool CanControlWindow(IntPtr window)
        {
            NativeInput.Rect bounds;
            return NativeInput.GetChatApplication(window) != ChatApplication.Claude
                || composerLocator.TryGetBounds(window, out bounds);
        }

        private void ObserveWindow(IntPtr window)
        {
            if (observedWindow == window) return;
            observedWindow = window;
            CancelPendingInput();
            SetReadingMode(false);
        }

        private void CancelPendingInput()
        {
            focusToggleTimer.Stop();
            focusToggleWindow = IntPtr.Zero;
            pendingTranscriptFocus = false;
            hasPendingClick = false;
            pendingClickWindow = IntPtr.Zero;
        }

        private bool ClickWasInTranscript(Point click)
        {
            NativeInput.Rect rect;
            IntPtr window = NativeInput.GetForegroundWindow();
            if (window == IntPtr.Zero || !NativeInput.GetWindowRect(window, out rect))
            {
                return false;
            }

            NativeInput.Rect composerBounds;
            return composerLocator.TryGetBounds(window, out composerBounds)
                ? WindowLayout.IsTranscriptClick(rect, click, composerBounds,
                    composerLocator.HasReplyInput(window)
                        || NativeInput.GetChatApplication(window) == ChatApplication.Claude)
                : WindowLayout.IsTranscriptClick(rect, click);
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
                bool captureSpaceFallback = settings.SpaceScroll && !keyboardHook.IsAvailable;
                if (!hotkeys.EnableScrollingHotkeys(captureSpaceFallback, out error))
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
            readingWindow = enabled ? NativeInput.GetForegroundWindow() : IntPtr.Zero;
            UpdateUi();
        }

        private void HandleScrollHotkey(ScrollCommand command, uint virtualKey)
        {
            IntPtr window = NativeInput.GetForegroundWindow();
            if (!toolEnabled || !readingMode || readingWindow != window
                || !IsEnabledWindow(window) || !CanControlWindow(window))
            {
                SetReadingMode(false);
                NativeInput.ReplayKeyboard(NativeInput.NavigationStroke(virtualKey));
                return;
            }

            if (composerLocator.HasFocusedInput(NativeInput.GetForegroundWindow()))
            {
                SetReadingMode(false);
                NativeInput.ReplayKeyboard(NativeInput.NavigationStroke(virtualKey));
                return;
            }

            NativeInput.ScrollWindow(window,
                ScrollProfile.WheelDelta(command, settings.ScrollSpeedLevel));
        }

        private void QueueFocusToggle()
        {
            IntPtr window = NativeInput.GetForegroundWindow();
            ObserveWindow(window);
            if (toolEnabled && IsEnabledWindow(window) && CanControlWindow(window))
            {
                CancelPendingInput();
                focusToggleTick = Environment.TickCount;
                focusToggleWindow = window;
                composerLocator.RequestRefresh(window, true);
                focusToggleTimer.Start();
            }
        }

        private void CompleteFocusToggle(object sender, EventArgs e)
        {
            IntPtr window = NativeInput.GetForegroundWindow();
            if (pendingTranscriptFocus)
            {
                CompleteTranscriptFocus(window);
                return;
            }
            if (!toolEnabled || focusToggleWindow != window || !IsEnabledWindow(window)
                || !CanControlWindow(window))
            {
                focusToggleTimer.Stop();
                return;
            }
            if (!NativeInput.AreFocusKeysReleased())
            {
                return;
            }
            if (composerLocator.IsRefreshing
                && unchecked(Environment.TickCount - focusToggleTick) < 2000)
            {
                return;
            }

            focusToggleTimer.Stop();
            focusToggleWindow = IntPtr.Zero;
            bool hasReply = composerLocator.HasReplyInput(window);
            // Alt release can leave Chromium in menu mnemonic mode even though the
            // modifier is no longer down. Escape dismisses that mode before focusing.
            if (hotkeys.FocusToggleUsesAlt && !hasReply)
            {
                NativeInput.DismissMenuMode(window);
            }

            if (!ShouldFocusInput(readingMode, hasReply, composerLocator.IsReplyFocused(window)))
            {
                hasPendingClick = false;
                if (NativeInput.FocusTranscript(window))
                {
                    // SendInput returns before Chromium publishes its new focus.
                    // Wait for confirmed transcript focus instead of immediately
                    // entering reading mode and tearing it down on the stale edit.
                    focusToggleWindow = window;
                    transcriptFocusTick = Environment.TickCount;
                    pendingTranscriptFocus = true;
                    focusToggleTimer.Start();
                }
                return;
            }

            SetReadingMode(false);
            FocusComposer(window);
        }

        private void CompleteTranscriptFocus(IntPtr window)
        {
            int elapsed = unchecked(Environment.TickCount - transcriptFocusTick);
            bool validTarget = toolEnabled && window == focusToggleWindow
                && IsEnabledWindow(window) && CanControlWindow(window);
            bool focusedInput = false;
            bool focusKnown = validTarget && elapsed >= 100 && elapsed < 1000
                && composerLocator.TryGetFocusedInput(window, out focusedInput);
            TranscriptFocusState state = GetTranscriptFocusState(
                elapsed, validTarget, focusKnown, focusedInput);
            if (state == TranscriptFocusState.Waiting) return;

            CancelPendingInput();
            if (state == TranscriptFocusState.Ready && IsEnabledWindow(window))
            {
                SetReadingMode(true);
            }
        }

        internal static TranscriptFocusState GetTranscriptFocusState(
            int elapsed, bool validTarget, bool focusKnown, bool focusedInput)
        {
            if (!validTarget || elapsed < 0 || elapsed >= 1000)
                return TranscriptFocusState.Cancelled;
            return elapsed >= 100 && focusKnown && !focusedInput
                ? TranscriptFocusState.Ready : TranscriptFocusState.Waiting;
        }

        internal static bool ShouldFocusInput(bool reading, bool hasReply, bool replyFocused)
        {
            return reading || (hasReply && !replyFocused);
        }

        private void FocusComposer(IntPtr window)
        {
            if (!toolEnabled || !IsEnabledWindow(window))
            {
                return;
            }
            if (!composerLocator.TryFocus(window)
                && NativeInput.GetChatApplication(window) == ChatApplication.Codex)
            {
                NativeInput.FocusComposer(window);
            }
        }

        private bool HandleKeyboardStroke(KeyboardStroke stroke)
        {
            IntPtr window = NativeInput.GetForegroundWindow();
            if (pendingTranscriptFocus && toolEnabled && focusToggleWindow == window
                && IsEnabledWindow(window) && CanControlWindow(window))
            {
                if (PrintableKeyClassifier.IsModifierKey(stroke.VirtualKey)) return false;
                bool commandDown = NativeInput.IsCommandModifierDown();
                bool shift = NativeInput.IsShiftModifierDown(stroke.ShiftDown);
                ScrollCommand? navigation = PrintableKeyClassifier.NavigationScrollCommand(
                    stroke.VirtualKey, shift, commandDown)
                    ?? PrintableKeyClassifier.SpaceScrollCommand(
                        stroke.VirtualKey, settings.SpaceScroll, shift, commandDown);
                if (navigation.HasValue)
                {
                    // An immediate arrow after Alt+F already expresses transcript
                    // intent. Route it as a wheel event while focus is settling so
                    // Claude never receives it as message-card navigation.
                    return NativeInput.ScrollWindow(window,
                        ScrollProfile.WheelDelta(navigation.Value, settings.ScrollSpeedLevel));
                }

                CancelPendingInput();
                if (!commandDown
                    && PrintableKeyClassifier.IsTypingKey(stroke.VirtualKey, settings.SpaceScroll)
                    && composerLocator.TryFocus(window))
                {
                    return IsEnabledWindow(window) && NativeInput.ReplayKeyboard(stroke);
                }
                return false;
            }
            if (!toolEnabled || !readingMode || readingWindow != window
                || !IsEnabledWindow(window) || !CanControlWindow(window))
            {
                if (readingMode) SetReadingMode(false);
                return false;
            }

            if (composerLocator.HasFocusedInput(NativeInput.GetForegroundWindow()))
            {
                SetReadingMode(false);
                return false;
            }

            bool commandModifierDown = NativeInput.IsCommandModifierDown();
            bool shiftDown = NativeInput.IsShiftModifierDown(stroke.ShiftDown);
            ScrollCommand? scrollCommand = PrintableKeyClassifier.SpaceScrollCommand(
                stroke.VirtualKey,
                settings.SpaceScroll,
                shiftDown,
                commandModifierDown);
            if (scrollCommand.HasValue)
            {
                if (shiftDown && scrollCommand.Value == ScrollCommand.PageUp)
                {
                    return NativeInput.ScrollForegroundWithoutShift(
                        window,
                        ScrollProfile.WheelDelta(
                            scrollCommand.Value,
                            settings.ScrollSpeedLevel),
                        stroke.ShiftVirtualKey,
                        stroke.ShiftDown);
                }
                HandleScrollHotkey(scrollCommand.Value, stroke.VirtualKey);
                return true;
            }

            if (commandModifierDown
                || !PrintableKeyClassifier.IsTypingKey(stroke.VirtualKey, settings.SpaceScroll))
            {
                return false;
            }

            SetReadingMode(false);
            // The hook suppresses the physical key only when this method returns true.
            // Replay after focusing preserves the first character the user intended to type.
            if (composerLocator.TryFocus(window))
            {
                return IsEnabledWindow(window) && NativeInput.ReplayKeyboard(stroke);
            }
            return NativeInput.GetChatApplication(window) == ChatApplication.Codex
                && NativeInput.FocusComposerAndReplay(window, stroke);
        }

        private void ToggleEnabled(bool enabled)
        {
            toolEnabled = enabled;
            if (!toolEnabled)
            {
                CancelPendingInput();
                SetReadingMode(false);
                hotkeys.DisableFocusToggle();
                nextHotkeyRetryTick = 0;
            }
            UpdateUi();
        }

        private void ToggleClaude(bool enabled)
        {
            settings.ClaudeEnabled = enabled;
            CancelPendingInput();
            SetReadingMode(false);
            SaveSettings();
            IntPtr window = NativeInput.GetForegroundWindow();
            UpdateFocusHotkey(toolEnabled && IsEnabledWindow(window) && CanControlWindow(window));
            UpdateUi();
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

        private void CheckForUpdates()
        {
            BeginUpdateCheck(false, true);
        }

        private void CheckAutomaticUpdates(object sender, EventArgs e)
        {
            BeginAutomaticUpdateCheckIfDue(false);
        }

        private void BeginAutomaticUpdateCheckIfDue(bool notifyUser)
        {
            if (UpdateCheckSchedule.IsDue(
                settings.AutomaticUpdateChecks,
                settings.LastUpdateCheckUtc,
                DateTime.UtcNow))
            {
                BeginUpdateCheck(true, notifyUser);
            }
        }

        private async void BeginUpdateCheck(bool automatic, bool notifyUser)
        {
            if (updateCheckInProgress)
            {
                if (notifyUser)
                {
                    ShowBalloon(localizer.Text(UiText.UpdateChecking), ToolTipIcon.Info);
                }
                return;
            }
            if (automatic && !UpdateCheckSchedule.IsDue(
                settings.AutomaticUpdateChecks,
                settings.LastUpdateCheckUtc,
                DateTime.UtcNow))
            {
                return;
            }

            updateCheckInProgress = true;
            if (automatic || settings.AutomaticUpdateChecks)
            {
                settings.LastUpdateCheckUtc = DateTime.UtcNow;
                SaveSettings();
            }
            menuView.SetUpdateCheckStatus(UpdateCheckStatus.Checking, null);
            if (notifyUser)
            {
                ShowBalloon(localizer.Text(UiText.UpdateChecking), ToolTipIcon.Info);
            }
            Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            UpdateCheckResult result = await updateCheckService.CheckAsync(currentVersion);
            if (disposed)
            {
                return;
            }

            updateCheckInProgress = false;
            if (result.Status == UpdateCheckStatus.UpdateAvailable
                && result.LatestVersion != null)
            {
                settings.LatestKnownVersion = result.LatestVersion.ToString(3);
                SaveSettings();
            }
            else if (result.Status == UpdateCheckStatus.UpToDate
                && settings.LatestKnownVersion.Length != 0)
            {
                settings.LatestKnownVersion = string.Empty;
                SaveSettings();
            }
            string failureReason = result.Status == UpdateCheckStatus.Failed
                ? LocalizeUpdateFailure(result)
                : null;
            Version statusVersion = result.Status == UpdateCheckStatus.UpToDate
                ? currentVersion
                : result.LatestVersion;
            menuView.SetUpdateCheckStatus(result.Status, statusVersion, failureReason);
            if (notifyUser || result.Status == UpdateCheckStatus.UpdateAvailable)
            {
                ShowUpdateCheckResult(result, currentVersion, failureReason);
            }
        }

        private void ToggleAutomaticUpdateChecks(bool enabled)
        {
            settings.AutomaticUpdateChecks = enabled;
            if (enabled)
            {
                // Enabling the option is an explicit request for an immediate first check.
                settings.LastUpdateCheckUtc = null;
            }
            SaveSettings();
            UpdateUi();
            if (enabled)
            {
                BeginAutomaticUpdateCheckIfDue(true);
            }
        }

        private string LocalizeUpdateFailure(UpdateCheckResult result)
        {
            switch (result.FailureKind)
            {
                case UpdateCheckFailureKind.Timeout:
                    return localizer.Text(UiText.UpdateFailureTimeout);
                case UpdateCheckFailureKind.Network:
                    return localizer.Text(UiText.UpdateFailureNetwork);
                case UpdateCheckFailureKind.SecureConnection:
                    return localizer.Text(UiText.UpdateFailureSecureConnection);
                case UpdateCheckFailureKind.HttpResponse:
                    return localizer.Format(
                        UiText.UpdateFailureHttp,
                        result.HttpStatusCode.HasValue ? result.HttpStatusCode.Value : 0);
                case UpdateCheckFailureKind.InvalidResponse:
                    return localizer.Text(UiText.UpdateFailureInvalidResponse);
                default:
                    return localizer.Text(UiText.UpdateFailureUnexpected);
            }
        }

        private void ShowUpdateCheckResult(
            UpdateCheckResult result,
            Version currentVersion,
            string failureReason)
        {
            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.LatestVersion != null)
            {
                ShowBalloon(
                    localizer.Format(UiText.UpdateAvailable, result.LatestVersion.ToString(3)),
                    ToolTipIcon.Info);
            }
            else if (result.Status == UpdateCheckStatus.UpToDate && result.LatestVersion != null)
            {
                ShowBalloon(
                    localizer.Format(UiText.UpdateUpToDate, currentVersion.ToString(3)),
                    ToolTipIcon.Info);
            }
            else if (result.Status == UpdateCheckStatus.Failed)
            {
                ShowBalloon(
                    localizer.Format(
                        UiText.UpdateCheckFailed,
                        failureReason ?? localizer.Text(UiText.UpdateFailureUnexpected)),
                    ToolTipIcon.Warning);
            }
        }

        private void RestoreKnownUpdate()
        {
            Version knownVersion;
            Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (UpdateCheckService.TryParseReleaseTag(
                    settings.LatestKnownVersion,
                    out knownVersion)
                && knownVersion.CompareTo(currentVersion) > 0)
            {
                menuView.SetAvailableUpdate(knownVersion);
                return;
            }

            if (!string.IsNullOrEmpty(settings.LatestKnownVersion))
            {
                settings.LatestKnownVersion = string.Empty;
                SaveSettings();
            }
            menuView.SetAvailableUpdate(null);
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
            updateCheckTimer.Stop();
            pollTimer.Dispose();
            focusToggleTimer.Dispose();
            updateCheckTimer.Dispose();
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
