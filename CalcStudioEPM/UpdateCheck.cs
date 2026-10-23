using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CalcStudioEPM
{
    /// <summary>
    /// Asks GitHub for the latest published release of <see cref="AboutInfo.RepositoryUrl"/>
    /// (drafts and pre-releases don't count) and compares its tag (v0.015.5) with this build's
    /// version. Uses GitHub's public REST API without signing in; the request carries nothing
    /// about the user, the connections or the rules.
    /// </summary>
    internal static class UpdateCheck
    {
        public sealed class Result
        {
            /// <summary>The latest release is newer than this build.</summary>
            public bool   IsNewer;
            /// <summary>The latest release's version, without the tag's "v" (e.g. 0.015.5).</summary>
            public string LatestVersion;
            /// <summary>Its release page, where the zip is downloaded.</summary>
            public string ReleaseUrl;
            /// <summary>Why the check didn't work; null when it did.</summary>
            public string Error;
        }

        /// <summary>True when <see cref="AboutInfo.RepositoryUrl"/> names a GitHub repository.</summary>
        public static bool Available => ApiUrl(AboutInfo.RepositoryUrl) != null;

        /// <summary>https://github.com/owner/repo → https://api.github.com/repos/owner/repo/releases/latest</summary>
        public static string ApiUrl(string repositoryUrl)
        {
            if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                return null;
            string[] parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) return null;
            string repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1].Substring(0, parts[1].Length - 4) : parts[1];
            return $"https://api.github.com/repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(repo)}/releases/latest";
        }

        public static async Task<Result> CheckAsync()
        {
            string api = ApiUrl(AboutInfo.RepositoryUrl);
            if (api == null) return new Result { Error = "No GitHub repository is set (AboutInfo.RepositoryUrl)." };
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    // GitHub's API refuses requests without a User-Agent
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("CalcStudioEPM/" + BuildInfo.Version);
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                    using (HttpResponseMessage resp = await http.GetAsync(api).ConfigureAwait(false))
                    {
                        if (resp.StatusCode == HttpStatusCode.NotFound)
                            return new Result { Error = "No release has been published on GitHub yet." };
                        if (!resp.IsSuccessStatusCode)
                            return new Result { Error = $"GitHub answered {(int)resp.StatusCode} {resp.ReasonPhrase}." };

                        JObject release = JObject.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                        string tag    = ((string)release["tag_name"] ?? "").Trim();
                        string latest = tag.TrimStart('v', 'V');
                        int[] theirs = Parse(latest), ours = Parse(BuildInfo.Version);
                        if (theirs == null || ours == null)
                            return new Result { Error = $"The latest release's tag '{tag}' is not a version number." };

                        // Only ever opened from the project's own repository
                        string page = (string)release["html_url"];
                        if (page == null || !page.StartsWith(AboutInfo.RepositoryUrl.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                            page = AboutInfo.RepositoryUrl.TrimEnd('/') + "/releases/latest";

                        return new Result { IsNewer = Compare(theirs, ours) > 0, LatestVersion = latest, ReleaseUrl = page };
                    }
                }
            }
            catch (TaskCanceledException) { return new Result { Error = "GitHub did not answer in time." }; }
            catch (Exception ex) when (ex is HttpRequestException || ex is JsonException)
            {
                return new Result { Error = "Could not reach GitHub: " + ex.GetBaseException().Message };
            }
        }

        /// <summary>"0.015.4" → {0, 15, 4, 0}; "0.015" → {0, 15, 0, 0}; null when it isn't a version.</summary>
        public static int[] Parse(string version)
        {
            string[] parts = (version ?? "").Split('.');
            if (parts.Length < 2 || parts.Length > 4) return null;
            var n = new int[4];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return null;
            return n;
        }

        public static int Compare(int[] a, int[] b)
        {
            for (int i = 0; i < 4; i++)
                if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return 0;
        }
    }
}
