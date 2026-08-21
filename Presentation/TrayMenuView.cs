using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CodexKeyboardScroll
{
    internal sealed class TrayMenuView : IDisposable
    {
        private readonly LocalizationManager localizer;
        private readonly ContextMenuStrip menu;
        private readonly Font menuFont;
        private readonly ToolStripMenuItem updateAvailableItem;
        private readonly ToolStripMenuItem summaryItem;
        private readonly ToolStripMenuItem serviceMenu;
        private readonly ToolStripMenuItem diagnosticModeItem;
        private readonly ToolStripMenuItem diagnosticShortcutItem;
        private readonly ToolStripMenuItem diagnosticAutomationItem;
        private readonly ToolStripMenuItem diagnosticVersionItem;
        private readonly ToolStripMenuItem checkUpdatesItem;
        private readonly ToolStripMenuItem automaticUpdatesItem;
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
        private Version availableVersion;
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

            updateAvailableItem = new ToolStripMenuItem
            {
                ForeColor = Color.FromArgb(37, 99, 235),
                Name = "updateAvailableItem",
                Visible = false
            };
            summaryItem = DiagnosticItem();
            serviceMenu = new ToolStripMenuItem { Name = "serviceMenu" };
            diagnosticModeItem = DiagnosticItem();
            diagnosticShortcutItem = DiagnosticItem();
            diagnosticAutomationItem = DiagnosticItem();
            diagnosticVersionItem = new ToolStripMenuItem { Name = "versionItem" };
            checkUpdatesItem = new ToolStripMenuItem { Name = "checkUpdatesItem" };
            automaticUpdatesItem = CheckItem(settings.AutomaticUpdateChecks);
            automaticUpdatesItem.Name = "automaticUpdatesItem";
            updateStatusItem = new ToolStripMenuItem
            {
                Name = "updateStatusItem",
                Visible = false
            };
            serviceMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                diagnosticModeItem,
                diagnosticShortcutItem,
                diagnosticAutomationItem,
                new ToolStripSeparator(),
                diagnosticVersionItem,
                checkUpdatesItem,
                automaticUpdatesItem,
                updateStatusItem
            });

            enabledItem = CheckItem(true);
            readingModeItem = CheckItem(false);
            shortcutMenu = new ToolStripMenuItem { Name = "shortcutMenu" };
            shortcutItems = new[]
            {
                ShortcutItem(FocusShortcut.AltF, settings.FocusShortcut),
                ShortcutItem(FocusShortcut.CtrlAltF, settings.FocusShortcut),
                ShortcutItem(FocusShortcut.CtrlShiftF, settings.FocusShortcut)
            };
            shortcutMenu.DropDownItems.AddRange(shortcutItems);

            speedMenu = new ToolStripMenuItem { Name = "speedMenu" };
            speedItems = new ToolStripMenuItem[ScrollProfile.SupportedLevels.Count];
            for (int index = 0; index < ScrollProfile.SupportedLevels.Count; index++)
            {
                decimal level = ScrollProfile.SupportedLevels[index];
                speedItems[index] = SpeedItem(level, settings.ScrollSpeedLevel);
            }
            speedMenu.DropDownItems.AddRange(speedItems);

            spaceScrollItem = CheckItem(settings.SpaceScroll);
            startupItem = CheckItem(startupEnabled);
            languageMenu = new ToolStripMenuItem { Name = "languageMenu" };
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
                updateAvailableItem,
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
            automaticUpdatesItem.Click += delegate
            {
                Raise(AutomaticUpdateChecksChanged, automaticUpdatesItem.Checked);
            };
            diagnosticVersionItem.Click += delegate
            {
                if (OpenRepositoryRequested != null) OpenRepositoryRequested();
            };
            checkUpdatesItem.Click += delegate
            {
                if (CheckUpdatesRequested != null) CheckUpdatesRequested();
            };
            updateAvailableItem.Click += RaiseOpenLatestRelease;
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
            ConfigureDropDown(serviceMenu, true);
            ConfigureDropDown(shortcutMenu, true);
            ConfigureDropDown(speedMenu, true);
            ConfigureDropDown(languageMenu, true);
            ApplyItemStyle(menu.Items, false);
            ApplyLocalization(settings);
        }

        internal ContextMenuStrip Menu { get { return menu; } }

        internal event Action<bool> EnabledChanged;
        internal event Action<bool> ReadingModeChanged;
        internal event Action<FocusShortcut> ShortcutChanged;
        internal event Action<decimal> SpeedChanged;
        internal event Action<bool> SpaceScrollChanged;
        internal event Action<bool> StartupChanged;
        internal event Action<bool> AutomaticUpdateChecksChanged;
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
            ApplyDropDownDirection(menu.Items, localizer.IsRightToLeft);
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
            automaticUpdatesItem.Text = localizer.Text(UiText.MenuAutomaticUpdateChecks);
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
            automaticUpdatesItem.Checked = settings.AutomaticUpdateChecks;
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
            if (status == UpdateCheckStatus.UpdateAvailable && version != null)
            {
                availableVersion = version;
            }
            else if (status == UpdateCheckStatus.UpToDate)
            {
                availableVersion = null;
            }
            ApplyUpdateStatus();
        }

        internal void SetAvailableUpdate(Version version)
        {
            availableVersion = version;
            ApplyAvailableUpdate();
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
            ApplyAvailableUpdate();
        }

        private void ApplyAvailableUpdate()
        {
            updateAvailableItem.Visible = availableVersion != null;
            if (availableVersion != null)
            {
                updateAvailableItem.Text = localizer.Format(
                    UiText.UpdateAvailable,
                    availableVersion.ToString(3));
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

        private static void ConfigureDropDown(ToolStripMenuItem item, bool showCheckMargin)
        {
            var dropDown = item.DropDown as ToolStripDropDownMenu;
            if (dropDown == null)
            {
                return;
            }

            dropDown.ShowCheckMargin = showCheckMargin;
            dropDown.ShowImageMargin = false;
            dropDown.Padding = new Padding(2);
            dropDown.Margin = Padding.Empty;
            dropDown.Opened += delegate { AlignDropDown(item); };
        }

        private static void AlignDropDown(ToolStripMenuItem item)
        {
            ToolStripDropDown dropDown = item.DropDown;
            Rectangle ownerBounds = item.Owner.Bounds;
            Rectangle workingArea = Screen.FromRectangle(ownerBounds).WorkingArea;
            bool preferRight = item.DropDownDirection != ToolStripDropDownDirection.Left;
            int x = AlignedDropDownX(
                ownerBounds,
                dropDown.Width,
                workingArea,
                preferRight);

            // WinForms offsets submenus by the check-gutter width when custom menu
            // padding is used. Reposition after opening so the visible borders meet.
            WindowPositioning.TryMove(dropDown.Handle, x, dropDown.Bounds.Top);
        }

        internal static int AlignedDropDownX(
            Rectangle ownerBounds,
            int dropDownWidth,
            Rectangle workingArea,
            bool preferRight)
        {
            int right = ownerBounds.Right - 1;
            int left = ownerBounds.Left - dropDownWidth + 1;
            int preferred = preferRight ? right : left;
            int fallback = preferRight ? left : right;
            int x = preferred >= workingArea.Left
                && preferred + dropDownWidth <= workingArea.Right
                    ? preferred
                    : fallback;
            return Math.Max(
                workingArea.Left,
                Math.Min(x, workingArea.Right - dropDownWidth));
        }

        private static void ApplyDropDownDirection(ToolStripItemCollection items, bool rightToLeft)
        {
            foreach (ToolStripMenuItem item in items.OfType<ToolStripMenuItem>())
            {
                if (!item.HasDropDownItems)
                {
                    continue;
                }
                item.DropDownDirection = rightToLeft
                    ? ToolStripDropDownDirection.Left
                    : ToolStripDropDownDirection.Right;
                ApplyDropDownDirection(item.DropDownItems, rightToLeft);
            }
        }

        private static void ApplyItemStyle(ToolStripItemCollection items, bool isSubmenu)
        {
            foreach (ToolStripItem item in items)
            {
                if (item is ToolStripSeparator)
                {
                    item.Margin = isSubmenu
                        ? new Padding(2, 3, 2, 3)
                        : new Padding(6, 4, 6, 4);
                    continue;
                }
                item.Padding = isSubmenu
                    ? new Padding(10, 8, 12, 8)
                    : new Padding(14, 8, 16, 8);
                item.Margin = isSubmenu
                    ? new Padding(0, 1, 0, 1)
                    : new Padding(4, 2, 4, 2);
                item.TextAlign = item.RightToLeft == RightToLeft.Yes
                    ? ContentAlignment.MiddleRight
                    : ContentAlignment.MiddleLeft;
                item.ImageAlign = ContentAlignment.MiddleCenter;
                var menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.HasDropDownItems)
                {
                    menuItem.DropDown.Font = item.Owner == null ? SystemFonts.MenuFont : item.Owner.Font;
                    menuItem.DropDown.Renderer = new ModernMenuRenderer();
                    ApplyItemStyle(menuItem.DropDownItems, true);
                }
            }
        }

        private void RaiseOpenLatestRelease(object sender, EventArgs e)
        {
            if (OpenLatestReleaseRequested != null) OpenLatestReleaseRequested();
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
