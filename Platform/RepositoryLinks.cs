using System.Diagnostics;

namespace CodexKeyboardScroll
{
    internal static class RepositoryLinks
    {
        internal const string RepositoryUrl =
            "https://github.com/technatonuniv/CodexKeyboardScroll";
        internal const string LatestReleaseApiUrl =
            "https://api.github.com/repos/technatonuniv/CodexKeyboardScroll/releases/latest";
        internal const string LatestReleaseUrl = RepositoryUrl + "/releases/latest";

        internal static bool TryOpen(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
