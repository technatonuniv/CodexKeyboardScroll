using System;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

namespace CodexKeyboardScroll
{
    internal sealed class UpdateCheckService : IDisposable
    {
        private readonly HttpClient client;

        internal UpdateCheckService()
        {
            client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexKeyboardScroll/2.0");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        }

        internal async Task<UpdateCheckResult> CheckAsync(Version currentVersion)
        {
            try
            {
                using (HttpResponseMessage response = await client.GetAsync(
                    RepositoryLinks.LatestReleaseApiUrl).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
                        var release = serializer.ReadObject(stream) as GitHubRelease;
                        Version latestVersion;
                        if (release == null || !TryParseReleaseTag(release.TagName, out latestVersion))
                        {
                            return UpdateCheckResult.Failed();
                        }

                        return latestVersion.CompareTo(currentVersion) > 0
                            ? UpdateCheckResult.Available(latestVersion)
                            : UpdateCheckResult.Current(latestVersion);
                    }
                }
            }
            catch
            {
                // A manual update check should fail quietly and leave the utility usable.
                return UpdateCheckResult.Failed();
            }
        }

        internal static bool TryParseReleaseTag(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return false;
            }

            string value = tag.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(1);
            }

            int suffix = value.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0)
            {
                value = value.Substring(0, suffix);
            }
            return Version.TryParse(value, out version);
        }

        public void Dispose()
        {
            client.Dispose();
        }

        [DataContract]
        private sealed class GitHubRelease
        {
            [DataMember(Name = "tag_name")]
            internal string TagName { get; set; }
        }
    }

    internal sealed class UpdateCheckResult
    {
        private UpdateCheckResult(UpdateCheckStatus status, Version latestVersion)
        {
            Status = status;
            LatestVersion = latestVersion;
        }

        internal UpdateCheckStatus Status { get; private set; }
        internal Version LatestVersion { get; private set; }

        internal static UpdateCheckResult Available(Version version)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, version);
        }

        internal static UpdateCheckResult Current(Version version)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, version);
        }

        internal static UpdateCheckResult Failed()
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, null);
        }
    }
}
