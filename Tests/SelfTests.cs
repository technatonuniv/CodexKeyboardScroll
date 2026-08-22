using System;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexKeyboardScroll
{
    internal static class SelfTests
    {
        internal static int Run()
        {
            int failures = 0;
            TestWindowLayout(ref failures);
            TestTypingKeys(ref failures);
            TestShortcutLabels(ref failures);
            TestScrollProfile(ref failures);
            TestSettingsMigration(ref failures);
            TestLocalization(ref failures);
            TestUpdateVersionParsing(ref failures);
            TestUpdateRuntime(ref failures);
            TestUpdateCheckResults(ref failures);
            TestUpdateSchedule(ref failures);
            TestDropDownPlacement(ref failures);
            TestTrayMenuEvents(ref failures);
            TestStartupRegistration(ref failures);
            TestIconResources(ref failures);
            TestWheelMessagePacking(ref failures);
            return failures;
        }

        private static void TestWindowLayout(ref int failures)
        {
            var normal = new NativeInput.Rect(100, 100, 1300, 900);
            Check(ref failures, WindowLayout.IsTranscriptClick(normal, new Point(700, 400)));
            Check(ref failures, !WindowLayout.IsTranscriptClick(normal, new Point(200, 400)));
            Check(ref failures, !WindowLayout.IsTranscriptClick(normal, new Point(700, 120)));
            Check(ref failures, !WindowLayout.IsTranscriptClick(normal, new Point(700, 850)));

            var narrow = new NativeInput.Rect(20, 30, 720, 730);
            Check(ref failures, WindowLayout.IsTranscriptClick(narrow, new Point(200, 300)));
            Check(ref failures, !WindowLayout.IsTranscriptClick(narrow, new Point(10, 300)));

            Point scroll = WindowLayout.ScrollTarget(normal);
            Point composer = WindowLayout.ComposerTarget(normal);
            Check(ref failures, Contains(normal, scroll));
            Check(ref failures, Contains(normal, composer));
            Check(ref failures, composer.Y > scroll.Y);
        }

        private static void TestTypingKeys(ref int failures)
        {
            Check(ref failures, PrintableKeyClassifier.IsTypingKey(0x41, true));
            Check(ref failures, PrintableKeyClassifier.IsTypingKey(0x30, true));
            Check(ref failures, PrintableKeyClassifier.IsTypingKey(0x60, true));
            Check(ref failures, PrintableKeyClassifier.IsTypingKey(0xBA, true));
            Check(ref failures, PrintableKeyClassifier.IsTypingKey(0x20, false));
            Check(ref failures, !PrintableKeyClassifier.IsTypingKey(0x20, true));
            Check(ref failures, !PrintableKeyClassifier.IsTypingKey(0x26, true));
            Check(ref failures, !PrintableKeyClassifier.IsTypingKey(0x70, true));
        }

        private static void TestShortcutLabels(ref int failures)
        {
            Check(ref failures, UtilitySettings.ShortcutText(FocusShortcut.AltF) == "Alt+F");
            Check(ref failures, UtilitySettings.ShortcutText(FocusShortcut.CtrlAltF) == "Ctrl+Alt+F");
            Check(ref failures, UtilitySettings.ShortcutText(FocusShortcut.CtrlShiftF) == "Ctrl+Shift+F");
            Check(ref failures, UtilitySettings.ShortcutUsesAlt(FocusShortcut.AltF));
            Check(ref failures, UtilitySettings.ShortcutUsesAlt(FocusShortcut.CtrlAltF));
            Check(ref failures, !UtilitySettings.ShortcutUsesAlt(FocusShortcut.CtrlShiftF));
        }

        private static void TestScrollProfile(ref int failures)
        {
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineUp, 0.25m) == 8);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineUp, 0.5m) == 16);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineUp, 1m) == 32);
            Check(ref failures,
                ScrollProfile.WheelDelta(ScrollCommand.LineUp, 0.5m) * 2
                    == ScrollProfile.WheelDelta(ScrollCommand.LineUp, 1m));
            Check(ref failures,
                ScrollProfile.WheelDelta(ScrollCommand.LineUp, 0.25m) * 4
                    == ScrollProfile.WheelDelta(ScrollCommand.LineUp, 1m));
            Check(ref failures,
                ScrollProfile.WheelDelta(ScrollCommand.PageUp, 0.5m) * 2
                    == ScrollProfile.WheelDelta(ScrollCommand.PageUp, 1m));
            Check(ref failures,
                ScrollProfile.WheelDelta(ScrollCommand.PageUp, 0.25m) * 4
                    == ScrollProfile.WheelDelta(ScrollCommand.PageUp, 1m));
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineDown, 5) == -120);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineUp, 10) == 540);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.PageUp, 5) == 840);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.PageDown, 10) == -3240);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineUp, 0) == 8);
            Check(ref failures, ScrollProfile.WheelDelta(ScrollCommand.LineUp, 11) == 540);
            Check(ref failures, ScrollProfile.DisplayLevel(0.25m) == "0.25");
            Check(ref failures, ScrollProfile.DisplayLevel(0.5m) == "0.5");

            int previous = 0;
            foreach (decimal level in ScrollProfile.SupportedLevels)
            {
                int current = ScrollProfile.WheelDelta(ScrollCommand.LineUp, level);
                Check(ref failures, current > previous);
                previous = current;
            }
        }

        private static void TestSettingsMigration(ref int failures)
        {
            var defaults = new UtilitySettings();
            Check(ref failures, defaults.ScrollSpeedLevel == ScrollProfile.DefaultLevel);
            Check(ref failures, defaults.LanguageCode == LocalizationManager.SystemLanguageCode);
            Check(ref failures, defaults.SpaceScroll);
            Check(ref failures, !defaults.AutomaticUpdateChecks);
            Check(ref failures, !defaults.LastUpdateCheckUtc.HasValue);
            Check(ref failures, defaults.LatestKnownVersion.Length == 0);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("Slow") == 2);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("Normal") == 5);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("Fast") == 8);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("0.25") == 0.25m);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("0.5") == 0.5m);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("0") == 0.25m);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("99") == 10);
            Check(ref failures, UtilitySettings.ParseScrollSpeed("invalid") == ScrollProfile.DefaultLevel);

            var persisted = new UtilitySettings();
            UtilitySettings.Apply(persisted, "AutomaticUpdateChecks", "True");
            UtilitySettings.Apply(persisted, "LastUpdateCheckUtc", "2026-08-21T10:15:30.0000000Z");
            UtilitySettings.Apply(persisted, "LatestKnownVersion", "2.1.0");
            Check(ref failures, persisted.AutomaticUpdateChecks);
            Check(ref failures, persisted.LastUpdateCheckUtc == new DateTime(
                2026, 8, 21, 10, 15, 30, DateTimeKind.Utc));
            Check(ref failures, persisted.LatestKnownVersion == "2.1.0");
            string[] serialized = persisted.Serialize();
            Check(ref failures, serialized.Contains("AutomaticUpdateChecks=True"));
            Check(ref failures, serialized.Contains(
                "LastUpdateCheckUtc=2026-08-21T10:15:30.0000000Z"));
            Check(ref failures, serialized.Contains("LatestKnownVersion=2.1.0"));
        }

        private static void TestUpdateVersionParsing(ref int failures)
        {
            Version version;
            Check(ref failures, UpdateCheckService.TryParseReleaseTag("v2.1.0", out version));
            Check(ref failures, version != null && version.Equals(new Version(2, 1, 0)));
            Check(ref failures, UpdateCheckService.TryParseReleaseTag("v2.1", out version));
            Check(ref failures, version != null && version.Equals(new Version(2, 1, 0)));
            Check(ref failures, version.ToString(3) == "2.1.0");
            Check(ref failures, UpdateCheckService.TryParseReleaseTag("v2.1-beta", out version));
            Check(ref failures, version != null && version.Equals(new Version(2, 1, 0)));
            Check(ref failures, version.ToString(3) == "2.1.0");
            Check(ref failures, version.CompareTo(new Version(2, 0, 1, 0)) > 0);
            Check(ref failures, UpdateCheckService.TryParseReleaseTag("2.0.1+build.4", out version));
            Check(ref failures, version != null && version.Equals(new Version(2, 0, 1)));
            Check(ref failures, !UpdateCheckService.TryParseReleaseTag("release", out version));
            Check(ref failures,
                RepositoryLinks.LatestReleaseApiUrl.StartsWith(
                    "https://api.github.com/",
                    StringComparison.Ordinal));
            Check(ref failures, RepositoryLinks.LatestReleaseUrl.EndsWith(
                "/releases/latest",
                StringComparison.Ordinal));
        }

        private static void TestUpdateCheckResults(ref int failures)
        {
            int attempts = 0;
            using (var service = new UpdateCheckService(delegate
            {
                attempts++;
                return new StubHttpHandler(delegate
                {
                    if (attempts == 1)
                    {
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    }
                    return JsonResponse("{\"tag_name\":\"v2.0.0\"}");
                });
            }))
            {
                UpdateCheckResult result = service.CheckAsync(
                    new Version(2, 0, 1, 0)).GetAwaiter().GetResult();
                Check(ref failures, attempts == 2);
                Check(ref failures, result.Status == UpdateCheckStatus.UpToDate);
                Check(ref failures, result.LatestVersion.Equals(new Version(2, 0, 0)));
                Check(ref failures, result.FailureKind == UpdateCheckFailureKind.None);
            }

            attempts = 0;
            using (var service = new UpdateCheckService(delegate
            {
                attempts++;
                return new StubHttpHandler(delegate
                {
                    return new HttpResponseMessage(HttpStatusCode.Forbidden);
                });
            }))
            {
                UpdateCheckResult result = service.CheckAsync(
                    new Version(2, 0, 1, 0)).GetAwaiter().GetResult();
                Check(ref failures, attempts == 1);
                Check(ref failures, result.Status == UpdateCheckStatus.Failed);
                Check(ref failures, result.FailureKind == UpdateCheckFailureKind.HttpResponse);
                Check(ref failures, result.HttpStatusCode == 403);
                Check(ref failures, !result.IsTransient);
            }

            var secureFailure = new HttpRequestException(
                "request failed",
                new WebException(
                    "secure channel failed",
                    WebExceptionStatus.SecureChannelFailure));
            Check(ref failures,
                UpdateCheckService.ClassifyRequestFailure(secureFailure)
                    == UpdateCheckFailureKind.SecureConnection);
            Check(ref failures,
                UpdateCheckService.ClassifyRequestFailure(new HttpRequestException("offline"))
                    == UpdateCheckFailureKind.Network);
        }

        private static void TestUpdateRuntime(ref int failures)
        {
            var target = (TargetFrameworkAttribute)Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
                .SingleOrDefault();
            Check(ref failures, target != null);
            Check(ref failures,
                target != null && target.FrameworkName == ".NETFramework,Version=v4.8");
            Check(ref failures,
                ServicePointManager.SecurityProtocol == SecurityProtocolType.SystemDefault);
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private static void TestUpdateSchedule(ref int failures)
        {
            DateTime now = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
            Check(ref failures, !UpdateCheckSchedule.IsDue(false, null, now));
            Check(ref failures, UpdateCheckSchedule.IsDue(true, null, now));
            Check(ref failures, !UpdateCheckSchedule.IsDue(
                true,
                now.Add(UpdateCheckSchedule.Interval).AddMinutes(-1),
                now.Add(UpdateCheckSchedule.Interval)));
            Check(ref failures, UpdateCheckSchedule.IsDue(
                true,
                now,
                now.Add(UpdateCheckSchedule.Interval)));
            Check(ref failures, UpdateCheckSchedule.IsDue(true, now.AddMinutes(1), now));
        }

        private static void TestDropDownPlacement(ref int failures)
        {
            var workingArea = new Rectangle(0, 0, 1920, 1080);
            var centeredOwner = new Rectangle(600, 100, 360, 500);
            Check(ref failures, TrayMenuView.AlignedDropDownX(
                centeredOwner,
                320,
                workingArea,
                true) == centeredOwner.Right - 1);
            Check(ref failures, TrayMenuView.AlignedDropDownX(
                centeredOwner,
                320,
                workingArea,
                false) == centeredOwner.Left - 320 + 1);

            var rightEdgeOwner = new Rectangle(1700, 100, 220, 500);
            Check(ref failures, TrayMenuView.AlignedDropDownX(
                rightEdgeOwner,
                320,
                workingArea,
                true) == rightEdgeOwner.Left - 320 + 1);
        }

        private static void TestTrayMenuEvents(ref int failures)
        {
            var settings = new UtilitySettings();
            var localizer = new LocalizationManager("en");
            using (var view = new TrayMenuView(localizer, settings, false))
            {
                bool repositoryRequested = false;
                bool updateRequested = false;
                bool automaticUpdatesEnabled = false;
                bool latestReleaseRequested = false;
                decimal selectedSpeed = 0m;
                view.OpenRepositoryRequested += delegate { repositoryRequested = true; };
                view.CheckUpdatesRequested += delegate { updateRequested = true; };
                view.AutomaticUpdateChecksChanged += delegate(bool enabled)
                {
                    automaticUpdatesEnabled = enabled;
                };
                view.OpenLatestReleaseRequested += delegate { latestReleaseRequested = true; };
                view.SpeedChanged += delegate(decimal level) { selectedSpeed = level; };

                var service = (ToolStripMenuItem)view.Menu.Items["serviceMenu"];
                ((ToolStripMenuItem)service.DropDownItems["versionItem"]).PerformClick();
                ((ToolStripMenuItem)service.DropDownItems["checkUpdatesItem"]).PerformClick();
                ((ToolStripMenuItem)service.DropDownItems["automaticUpdatesItem"]).PerformClick();
                view.SetUpdateCheckStatus(
                    UpdateCheckStatus.UpdateAvailable,
                    new Version(2, 1, 0));
                var available = (ToolStripMenuItem)view.Menu.Items["updateAvailableItem"];
                available.PerformClick();

                var speed = (ToolStripMenuItem)view.Menu.Items["speedMenu"];
                ((ToolStripMenuItem)speed.DropDownItems[0]).PerformClick();
                Check(ref failures, repositoryRequested);
                Check(ref failures, updateRequested);
                Check(ref failures, automaticUpdatesEnabled);
                Check(ref failures, latestReleaseRequested);
                Check(ref failures, selectedSpeed == 0.25m);
                Check(ref failures, available.Available);
                Check(ref failures, speed.DropDownItems[0].Text == "0.25");
                Check(ref failures, speed.DropDownItems[1].Text == "0.5");
                Check(ref failures, service.DropDownDirection == ToolStripDropDownDirection.Right);
                Check(ref failures, service.DropDown.Padding.Top <= 2);
                Check(ref failures, service.DropDown.Padding.Bottom <= 2);
                Check(ref failures, service.DropDown.Margin == Padding.Empty);
                Check(ref failures, ((ToolStripDropDownMenu)service.DropDown).ShowCheckMargin);
                Check(ref failures, ((ToolStripDropDownMenu)speed.DropDown).ShowCheckMargin);

                var status = (ToolStripMenuItem)service.DropDownItems["updateStatusItem"];
                view.SetUpdateCheckStatus(
                    UpdateCheckStatus.Failed,
                    null,
                    "HTTP 403");
                Check(ref failures, status.Text.Contains("HTTP 403"));
                view.SetUpdateCheckStatus(
                    UpdateCheckStatus.UpToDate,
                    new Version(2, 0, 1));
                Check(ref failures, status.Text.Contains("2.0.1"));
            }
        }

        private static void TestLocalization(ref int failures)
        {
            var localizer = new LocalizationManager("en");
            string error;
            Check(ref failures, localizer.ValidateAll(out error));
            Check(ref failures, localizer.Languages.Count() >= 18);

            localizer.SetLanguage("ru");
            Check(ref failures, localizer.Text(UiText.MenuExit) == "Выход");
            localizer.SetLanguage("zh-CN");
            Check(ref failures, localizer.EffectiveLanguageCode == "zh-Hans");
            localizer.SetLanguage("unknown");
            Check(ref failures, localizer.EffectiveLanguageCode == "en");
            localizer.SetLanguage(LocalizationManager.SystemLanguageCode);
            Check(ref failures, localizer.RequestedLanguageCode == LocalizationManager.SystemLanguageCode);
        }

        private static void TestStartupRegistration(ref int failures)
        {
            const string path = @"C:\Tools\CodexKeyboardScroll.exe";
            string command = StartupRegistration.CommandForExecutable(path);
            Check(ref failures, command == "\"" + path + "\"");
            Check(ref failures, StartupRegistration.IsCommandForExecutable(command, path));
            Check(ref failures, !StartupRegistration.IsCommandForExecutable(
                "\"C:\\Other\\CodexKeyboardScroll.exe\"",
                path));
        }

        private static void TestIconResources(ref int failures)
        {
            using (var icons = new AppIconSet())
            {
                Check(ref failures, icons.Active != null);
                Check(ref failures, icons.Waiting != null);
                Check(ref failures, icons.Active.Width >= 16);
                Check(ref failures, icons.Waiting.Width >= 16);
            }
        }

        private static void TestWheelMessagePacking(ref int failures)
        {
            Check(ref failures, LowBits(NativeInput.PackWheelWParam(120)) == 0x00780000);
            Check(ref failures, LowBits(NativeInput.PackWheelWParam(-120)) == 0xFF880000);
            Check(ref failures, LowBits(NativeInput.PackScreenPoint(new Point(17, 34))) == 0x00220011);
            Check(ref failures, LowBits(NativeInput.PackScreenPoint(new Point(-10, -20))) == 0xFFECFFF6);
        }

        private static uint LowBits(IntPtr value)
        {
            return unchecked((uint)value.ToInt64());
        }

        private static bool Contains(NativeInput.Rect rect, Point point)
        {
            return point.X >= rect.Left && point.X < rect.Right
                && point.Y >= rect.Top && point.Y < rect.Bottom;
        }

        private static void Check(ref int failures, bool condition)
        {
            if (!condition)
            {
                failures++;
            }
        }

        private sealed class StubHttpHandler : HttpMessageHandler
        {
            private readonly Func<HttpResponseMessage> responseFactory;

            internal StubHttpHandler(Func<HttpResponseMessage> responseFactory)
            {
                this.responseFactory = responseFactory;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(responseFactory());
            }
        }
    }
}
