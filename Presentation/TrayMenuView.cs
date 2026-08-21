using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace CodexKeyboardScroll
{
    internal sealed class TrayMenuView : IDisposable
    {
        private readonly LocalizationManager localizer;
        private readonly ContextMenuStrip menu;
        private readonly Font menuFont;
        private readonly ToolStripMenuItem summaryItem;
        private readonly ToolStripMenuItem serviceMenu;
        private readonly ToolStripMenuItem diagnosticModeItem;
        private readonly ToolStripMenuItem diagnosticShortcutItem;
        private readonly ToolStripMenuItem diagnosticAutomationItem;
        private readonly ToolStripMenuItem diagnosticVersionItem;
        private readonly ToolStripMenuItem checkUpdatesItem;
        private readonly ToolStripMenuItem updateStatusItem;
        private readonly ToolStripMenuItem enabledItem;
        private readonly ToolStripMenuItem readingModeItem;
        private readonly ToolStripMenuItem shortcutMenu;
        private readonly ToolStripMenuItem speedMenu;
        private readonly ToolStripMenuItem spaceScrollItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem languageMenu;
        private readonly ToolStripMenuItem systemLanguageItem;
        private readonly ToolStripMenuItem exitItem;
        private readonly ToolStripMenuItem[] shortcutItems;
        private readonly ToolStripMenuItem[] speedItems;
        private readonly List<ToolStripMenuItem> languageItems = new List<ToolStripMenuItem>();
        private UpdateCheckStatus updateCheckStatus;
        private Version latestVersion;
        private bool suppressEvents;

        internal TrayMenuView(LocalizationManager localizer, UtilitySettings settings, bool startupEnabled)
        {
            this.localizer = localizer;
            menuFont = new Font("Segoe UI", 11.0f, FontStyle.Regular, GraphicsUnit.Point);
            menu = new ContextMenuStrip
            {
                AutoSize = true,
                BackColor = Color.FromArgb(248, 250, 252),
                DropShadowEnabled = true,
                Font = menuFont,
                ImageScalingSize = new Size(22, 22),
                MinimumSize = new Size(360, 0),
                Padding = new Padding(6),
                Renderer = new ModernMenuRenderer(),
                ShowCheckMargin = true,
                ShowImageMargin = false
            };

            summaryItem = DiagnosticItem();
            serviceMenu = new ToolStripMenuItem();
            diagnosticModeItem = DiagnosticItem();
            diagnosticShortcutItem = DiagnosticItem();
            diagnosticAutomationItem = DiagnosticItem();
            diagnosticVersionItem = new ToolStripMenuItem();
            checkUpdatesItem = new ToolStripMenuItem();
            updateStatusItem = new ToolStripMenuItem { Visible = false };
            serviceMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                diagnosticModeItem,
                diagnosticShortcutItem,
                diagnosticAutomationItem,
                new ToolStripSeparator(),
                diagnosticVersionItem,
                checkUpdatesItem,
                updateStatusItem
            });

            enabledItem = CheckItem(true);
            readingModeItem = CheckItem(false);
            shortcutMenu = new ToolStripMenuItem();
            shortcutItems = new[]
            {
                ShortcutItem(FocusShortcut.AltF, settings.FocusShortcut),
                ShortcutItem(FocusShortcut.CtrlAltF, settings.FocusShortcut),
                ShortcutItem(FocusShortcut.CtrlShiftF, settings.FocusShortcut)
            };
            shortcutMenu.DropDownItems.AddRange(shortcutItems);

            speedMenu = new ToolStripMenuItem();
            speedItems = new ToolStripMenuItem[ScrollProfile.SupportedLevels.Count];
            for (int index = 0; index < ScrollProfile.SupportedLevels.Count; index++)
            {
                decimal level = ScrollProfile.SupportedLevels[index];
                speedItems[index] = SpeedItem(level, settings.ScrollSpeedLevel);
            }
            speedMenu.DropDownItems.AddRange(speedItems);

            spaceScrollItem = CheckItem(settings.SpaceScroll);
            startupItem = CheckItem(startupEnabled);
            languageMenu = new ToolStripMenuItem();
            systemLanguageItem = LanguageItem(LocalizationManager.SystemLanguageCode);
            languageMenu.DropDownItems.Add(systemLanguageItem);
            languageMenu.DropDownItems.Add(new ToolStripSeparator());
            foreach (LanguageDefinition language in localizer.Languages)
            {
                ToolStripMenuItem item = LanguageItem(language.Code);
                languageItems.Add(item);
                languageMenu.DropDownItems.Add(item);
            }

            exitItem = new ToolStripMenuItem();
            menu.Items.AddRange(new ToolStripItem[]
            {
                summaryItem,
                serviceMenu,
                new ToolStripSeparator(),
                enabledItem,
                readingModeItem,
                new ToolStripSeparator(),
                shortcutMenu,
                speedMenu,
                spaceScrollItem,
                startupItem,
                languageMenu,
                new ToolStripSeparator(),
                exitItem
            });

            enabledItem.Click += delegate { Raise(EnabledChanged, enabledItem.Checked); };
            readingModeItem.Click += delegate { Raise(ReadingModeChanged, readingModeItem.Checked); };
            spaceScrollItem.Click += delegate { Raise(SpaceScrollChanged, spaceScrollItem.Checked); };
            startupItem.Click += delegate { Raise(StartupChanged, startupItem.Checked); };
            diagnosticVersionItem.Click += delegate
            {
                if (OpenRepositoryRequested != null) OpenRepositoryRequested();
            };
            checkUpdatesItem.Click += delegate
            {
                if (CheckUpdatesRequested != null) CheckUpdatesRequested();
            };
            updateStatusItem.Click += delegate
            {
                if (updateCheckStatus == UpdateCheckStatus.UpdateAvailable
                    && OpenLatestReleaseRequested != null)
                {
                    OpenLatestReleaseRequested();
                }
            };
            exitItem.Click += delegate { if (ExitRequested != null) ExitRequested(); };
            menu.Opening += delegate { if (Opening != null) Opening(); };
            ApplyItemStyle(menu.Items);
            ApplyLocalization(settings);
        }

        internal ContextMenuStrip Menu { get { return menu; } }

        internal event Action<bool> EnabledChanged;
        internal event Action<bool> ReadingModeChanged;
        internal event Action<FocusShortcut> ShortcutChanged;
        internal event Action<decimal> SpeedChanged;
        internal event Action<bool> SpaceScrollChanged;
        internal event Action<bool> StartupChanged;
        internal event Action<string> LanguageChanged;
        internal event Action OpenRepositoryRequested;
        internal event Action CheckUpdatesRequested;
        internal event Action OpenLatestReleaseRequested;
        internal event Action ExitRequested;
        internal event Action Opening;

        internal void ApplyLocalization(UtilitySettings settings)
        {
            suppressEvents = true;
            menu.RightToLeft = localizer.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
            serviceMenu.Text = localizer.Text(UiText.MenuService);
            enabledItem.Text = localizer.Text(UiText.MenuUtilityEnabled);
            readingModeItem.Text = localizer.Text(UiText.MenuReadingMode);
            shortcutMenu.Text = localizer.Text(UiText.MenuFocusShortcut);
            speedMenu.Text = localizer.Format(
                UiText.MenuScrollSpeed,
                ScrollProfile.DisplayLevel(settings.ScrollSpeedLevel));
            spaceScrollItem.Text = localizer.Text(UiText.MenuSpaceScroll);
            startupItem.Text = localizer.Text(UiText.MenuStartWithWindows);
            languageMenu.Text = localizer.Text(UiText.MenuLanguage);
            systemLanguageItem.Text = localizer.Format(
                UiText.MenuSystemDefault,
                localizer.SystemLanguageName);
            exitItem.Text = localizer.Text(UiText.MenuExit);
            diagnosticVersionItem.Text = localizer.Format(
                UiText.DiagnosticVersion,
                Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            checkUpdatesItem.Text = localizer.Text(UiText.MenuCheckForUpdates);
            ApplyUpdateStatus();

            for (int index = 0; index < ScrollProfile.SupportedLevels.Count; index++)
            {
                speedItems[index].Text = SpeedLabel(ScrollProfile.SupportedLevels[index]);
            }
            foreach (ToolStripMenuItem item in languageItems)
            {
                item.Text = localizer.LanguageName((string)item.Tag);
            }
            SetLanguageChecks(settings.LanguageCode);
            suppressEvents = false;
        }

        internal void UpdateState(TrayMenuState state, UtilitySettings settings)
        {
            suppressEvents = true;
            enabledItem.Checked = state.Mode != ApplicationMode.Disabled;
            readingModeItem.Checked = state.Mode == ApplicationMode.Reading;
            readingModeItem.Enabled = state.Mode != ApplicationMode.Disabled;
            spaceScrollItem.Checked = settings.SpaceScroll;
            startupItem.Checked = state.StartupEnabled;
            speedMenu.Text = localizer.Format(
                UiText.MenuScrollSpeed,
                ScrollProfile.DisplayLevel(settings.ScrollSpeedLevel));
            CheckOnly(shortcutItems, settings.FocusShortcut);
            CheckOnly(speedItems, settings.ScrollSpeedLevel);
            SetLanguageChecks(settings.LanguageCode);

            string mode = ModeText(state.Mode);
            string compactAutomation = state.ComposerStatus == ComposerStatus.Found
                ? "UIA"
                : localizer.Text(UiText.CompactFallback);
            summaryItem.Text = localizer.Format(
                UiText.SummaryFormat,
                mode,
                state.ShortcutText,
                compactAutomation);
            diagnosticModeItem.Text = localizer.Text(ModeDiagnosticKey(state.Mode));
            diagnosticShortcutItem.Text = state.HotkeyRetryCount > 0
                ? localizer.Format(UiText.DiagnosticFocusRetry, state.HotkeyRetryCount)
                : localizer.Format(
                    UiText.DiagnosticFocusShortcut,
                    state.UsingFallbackShortcut
                        ? localizer.Format(UiText.ShortcutFallback, state.ShortcutText)
                        : state.ShortcutText);
            diagnosticAutomationItem.Text = localizer.Text(AutomationDiagnosticKey(state.ComposerStatus));
            suppressEvents = false;
        }

        internal void SetStartupEnabled(bool enabled)
        {
            startupItem.Checked = enabled;
        }

        internal void SetUpdateCheckStatus(UpdateCheckStatus status, Version version)
        {
            updateCheckStatus = status;
            latestVersion = version;
            ApplyUpdateStatus();
        }

        public void Dispose()
        {
            menu.Dispose();
            menuFont.Dispose();
        }

        private ToolStripMenuItem ShortcutItem(FocusShortcut shortcut, FocusShortcut selected)
        {
            var item = new ToolStripMenuItem(UtilitySettings.ShortcutText(shortcut))
            {
                Checked = shortcut == selected,
                Tag = shortcut
            };
            item.Click += delegate
            {
                if (suppressEvents) return;
                CheckOnly(shortcutItems, shortcut);
                if (ShortcutChanged != null) ShortcutChanged(shortcut);
            };
            return item;
        }

        private ToolStripMenuItem SpeedItem(decimal level, decimal selected)
        {
            var item = new ToolStripMenuItem
            {
                Checked = level == selected,
                Tag = level
            };
            item.Click += delegate
            {
                if (suppressEvents) return;
                CheckOnly(speedItems, level);
                if (SpeedChanged != null) SpeedChanged(level);
            };
            return item;
        }

        private ToolStripMenuItem LanguageItem(string code)
        {
            var item = new ToolStripMenuItem { Tag = code };
            item.Click += delegate
            {
                if (!suppressEvents && LanguageChanged != null) LanguageChanged(code);
            };
            return item;
        }

        private string SpeedLabel(decimal level)
        {
            string display = ScrollProfile.DisplayLevel(level);
            if (level == 0.25m)
            {
                return display + " — " + localizer.Text(UiText.SpeedQuarter);
            }
            if (level == 0.5m)
            {
                return display + " — " + localizer.Text(UiText.SpeedHalf);
            }
            if (level == 1m)
            {
                return display + " — " + localizer.Text(UiText.SpeedComfortable);
            }
            if (level == ScrollProfile.DefaultLevel)
            {
                return display + " — " + localizer.Text(UiText.SpeedBalanced);
            }
            if (level == ScrollProfile.MaximumLevel)
            {
                return display + " — " + localizer.Text(UiText.SpeedVeryFast);
            }
            return display;
        }

        private void ApplyUpdateStatus()
        {
            checkUpdatesItem.Enabled = updateCheckStatus != UpdateCheckStatus.Checking;
            checkUpdatesItem.Text = updateCheckStatus == UpdateCheckStatus.Checking
                ? localizer.Text(UiText.UpdateChecking)
                : localizer.Text(UiText.MenuCheckForUpdates);

            updateStatusItem.Visible = updateCheckStatus == UpdateCheckStatus.UpToDate
                || updateCheckStatus == UpdateCheckStatus.UpdateAvailable
                || updateCheckStatus == UpdateCheckStatus.Failed;
            updateStatusItem.Enabled = updateCheckStatus == UpdateCheckStatus.UpdateAvailable;
            if (updateCheckStatus == UpdateCheckStatus.UpdateAvailable && latestVersion != null)
            {
                updateStatusItem.Text = localizer.Format(
                    UiText.UpdateAvailable,
                    latestVersion.ToString(3));
            }
            else if (updateCheckStatus == UpdateCheckStatus.UpToDate)
            {
                updateStatusItem.Text = localizer.Text(UiText.UpdateUpToDate);
            }
            else if (updateCheckStatus == UpdateCheckStatus.Failed)
            {
                updateStatusItem.Text = localizer.Text(UiText.UpdateCheckFailed);
            }
        }

        private string ModeText(ApplicationMode mode)
        {
            if (mode == ApplicationMode.Disabled) return localizer.Text(UiText.ModeDisabled);
            if (mode == ApplicationMode.Reading) return localizer.Text(UiText.ModeReading);
            return localizer.Text(UiText.ModeWaiting);
        }

        private static UiText ModeDiagnosticKey(ApplicationMode mode)
        {
            if (mode == ApplicationMode.Disabled) return UiText.DiagnosticModeDisabled;
            if (mode == ApplicationMode.Reading) return UiText.DiagnosticModeReading;
            return UiText.DiagnosticModeWaiting;
        }

        private static UiText AutomationDiagnosticKey(ComposerStatus status)
        {
            if (status == ComposerStatus.Found) return UiText.DiagnosticAutomationFound;
            if (status == ComposerStatus.CoordinateFallback) return UiText.DiagnosticAutomationFallback;
            return UiText.DiagnosticAutomationLocating;
        }

        private void SetLanguageChecks(string selectedCode)
        {
            systemLanguageItem.Checked = selectedCode.Equals(
                LocalizationManager.SystemLanguageCode,
                StringComparison.OrdinalIgnoreCase);
            foreach (ToolStripMenuItem item in languageItems)
            {
                item.Checked = ((string)item.Tag).Equals(selectedCode, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static ToolStripMenuItem CheckItem(bool isChecked)
        {
            return new ToolStripMenuItem { Checked = isChecked, CheckOnClick = true };
        }

        private static ToolStripMenuItem DiagnosticItem()
        {
            return new ToolStripMenuItem { Enabled = false };
        }

        private static void CheckOnly(ToolStripMenuItem[] items, object selected)
        {
            foreach (ToolStripMenuItem item in items)
            {
                item.Checked = Equals(item.Tag, selected);
            }
        }

        private static void ApplyItemStyle(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                if (item is ToolStripSeparator)
                {
                    item.Margin = new Padding(6, 4, 6, 4);
                    continue;
                }
                item.Padding = new Padding(14, 8, 16, 8);
                item.Margin = new Padding(4, 2, 4, 2);
                item.TextAlign = item.RightToLeft == RightToLeft.Yes
                    ? ContentAlignment.MiddleRight
                    : ContentAlignment.MiddleLeft;
                item.ImageAlign = ContentAlignment.MiddleCenter;
                var menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.HasDropDownItems)
                {
                    menuItem.DropDown.Font = item.Owner == null ? SystemFonts.MenuFont : item.Owner.Font;
                    menuItem.DropDown.Renderer = new ModernMenuRenderer();
                    menuItem.DropDown.Padding = new Padding(6);
                    ApplyItemStyle(menuItem.DropDownItems);
                }
            }
        }

        private void Raise(Action<bool> action, bool value)
        {
            if (!suppressEvents && action != null) action(value);
        }
    }

    internal sealed class TrayMenuState
    {
        internal ApplicationMode Mode;
        internal ComposerStatus ComposerStatus;
        internal string ShortcutText;
        internal bool UsingFallbackShortcut;
        internal int HotkeyRetryCount;
        internal bool StartupEnabled;
    }
}
