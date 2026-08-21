using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexKeyboardScroll
{
    internal sealed class UpdateCheckService : IDisposable
    {
        private const int MaximumAttempts = 2;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private readonly Func<HttpMessageHandler> handlerFactory;
        private readonly CancellationTokenSource lifetimeCancellation;
        private bool disposed;

        internal UpdateCheckService()
            : this(delegate { return new HttpClientHandler(); })
        {
        }

        internal UpdateCheckService(Func<HttpMessageHandler> handlerFactory)
        {
            if (handlerFactory == null) throw new ArgumentNullException("handlerFactory");
            this.handlerFactory = handlerFactory;
            lifetimeCancellation = new CancellationTokenSource();
        }

        internal async Task<UpdateCheckResult> CheckAsync(Version currentVersion)
        {
            if (currentVersion == null) throw new ArgumentNullException("currentVersion");
            if (disposed) throw new ObjectDisposedException(GetType().FullName);

            UpdateCheckResult result = null;
            for (int attempt = 0; attempt < MaximumAttempts; attempt++)
            {
                result = await CheckOnceAsync(currentVersion).ConfigureAwait(false);
                if (result.Status != UpdateCheckStatus.Failed
                    || !result.IsTransient
                    || attempt == MaximumAttempts - 1)
                {
                    return result;
                }
            }

            return result ?? UpdateCheckResult.Failed(
                UpdateCheckFailureKind.Unexpected,
                null,
                false);
        }

        private async Task<UpdateCheckResult> CheckOnceAsync(Version currentVersion)
        {
            // A fresh handler prevents stale proxy or connection state from surviving
            // for the lifetime of the tray process. Transient failures get one retry.
            using (HttpMessageHandler handler = handlerFactory())
            using (var client = new HttpClient(handler))
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeCancellation.Token))
            {
                timeout.CancelAfter(RequestTimeout);
                ConfigureClient(client);
                try
                {
                    using (HttpResponseMessage response = await client.GetAsync(
                        RepositoryLinks.LatestReleaseApiUrl,
                        timeout.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            int statusCode = (int)response.StatusCode;
                            return UpdateCheckResult.Failed(
                                UpdateCheckFailureKind.HttpResponse,
                                statusCode,
                                IsTransientStatus(response.StatusCode));
                        }

                        using (var stream = await response.Content.ReadAsStreamAsync()
                            .ConfigureAwait(false))
                        {
                            var serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
                            var release = serializer.ReadObject(stream) as GitHubRelease;
                            Version latestVersion;
                            if (release == null
                                || !TryParseReleaseTag(release.TagName, out latestVersion))
                            {
                                return UpdateCheckResult.Failed(
                                    UpdateCheckFailureKind.InvalidResponse,
                                    null,
                                    true);
                            }

                            return latestVersion.CompareTo(currentVersion) > 0
                                ? UpdateCheckResult.Available(latestVersion)
                                : UpdateCheckResult.Current(latestVersion);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return UpdateCheckResult.Failed(
                        UpdateCheckFailureKind.Timeout,
                        null,
                        true);
                }
                catch (HttpRequestException)
                {
                    return UpdateCheckResult.Failed(
                        UpdateCheckFailureKind.Network,
                        null,
                        true);
                }
                catch (SerializationException)
                {
                    return UpdateCheckResult.Failed(
                        UpdateCheckFailureKind.InvalidResponse,
                        null,
                        true);
                }
                catch
                {
                    return UpdateCheckResult.Failed(
                        UpdateCheckFailureKind.Unexpected,
                        null,
                        false);
                }
            }
        }

        private static void ConfigureClient(HttpClient client)
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "CodexKeyboardScroll/"
                    + Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
            client.DefaultRequestHeaders.ConnectionClose = true;
        }

        private static bool IsTransientStatus(HttpStatusCode statusCode)
        {
            int value = (int)statusCode;
            return statusCode == HttpStatusCode.RequestTimeout
                || value == 429
                || value >= 500;
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
            if (disposed) return;
            disposed = true;
            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
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
        private UpdateCheckResult(
            UpdateCheckStatus status,
            Version latestVersion,
            UpdateCheckFailureKind failureKind,
            int? httpStatusCode,
            bool isTransient)
        {
            Status = status;
            LatestVersion = latestVersion;
            FailureKind = failureKind;
            HttpStatusCode = httpStatusCode;
            IsTransient = isTransient;
        }

        internal UpdateCheckStatus Status { get; private set; }
        internal Version LatestVersion { get; private set; }
        internal UpdateCheckFailureKind FailureKind { get; private set; }
        internal int? HttpStatusCode { get; private set; }
        internal bool IsTransient { get; private set; }

        internal static UpdateCheckResult Available(Version version)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                version,
                UpdateCheckFailureKind.None,
                null,
                false);
        }

        internal static UpdateCheckResult Current(Version version)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.UpToDate,
                version,
                UpdateCheckFailureKind.None,
                null,
                false);
        }

        internal static UpdateCheckResult Failed(
            UpdateCheckFailureKind failureKind,
            int? httpStatusCode,
            bool isTransient)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                null,
                failureKind,
                httpStatusCode,
                isTransient);
        }
    }
}
