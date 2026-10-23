using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CalcStudioEPM.Config;

namespace CalcStudioEPM.Epm
{
    /// <summary>
    /// Warns before Deploy overwrites a rule someone changed in EPM after the last pull.
    ///
    /// Uses List Updated Artifacts (POST /interop/rest/v2/migration/artifactupdates), which
    /// reports each changed artifact's modifiedDate ("2026-10-22 14:05:31", in EPM's time) and
    /// modifiedBy. Those times are only ever compared with each other, never with the PC's
    /// clock, so the server's time zone doesn't matter:
    ///   • Pull records what EPM reports for Calculation Manager (the baseline);
    ///   • before Deploy, a newer modifiedDate for the rule than the baseline = changed since;
    ///   • after a successful deploy from here, the rule's new modifiedDate becomes its baseline
    ///     (when EPM doesn't list it yet, the next change by this user is taken as that deploy).
    /// Kept in {LocalFolder}\.epm\rule_changes.json. Any failure only means "not checked".
    /// </summary>
    internal static class RuleChangeCheck
    {
        private const string Category = "Calculation Manager";
        private const int BaselineDays = 180;

        public sealed class ArtifactUpdate
        {
            public string ArtifactName { get; set; }
            public string ArtifactType { get; set; }
            public string ModifiedBy { get; set; }
            public string ModifiedDate { get; set; }
            public string Path { get; set; }
        }

        private sealed class Store
        {
            public DateTime PulledAtUtc { get; set; }
            public List<ArtifactUpdate> Baseline { get; set; } = new List<ArtifactUpdate>();
            /// <summary>Rule key → what EPM listed after this PC's deploy of it.</summary>
            public Dictionary<string, ArtifactUpdate> Deployed { get; set; } = new Dictionary<string, ArtifactUpdate>(StringComparer.OrdinalIgnoreCase);
            /// <summary>Rules deployed from here whose new modifiedDate EPM hadn't listed yet.</summary>
            public HashSet<string> PendingOwn { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The outcome of a check: a newer change (Conflict), or why there is none.</summary>
        public sealed class Result
        {
            public ArtifactUpdate Conflict;
            public DateTime PulledAtUtc;
            public string Note;          // "not checked: …" / "no change since the pull"
        }

        // ------------------------------------------------------------------ //

        /// <summary>Pull: records what EPM reports for Calculation Manager now. Never throws.</summary>
        public static async Task RecordBaselineAsync(EpmClient client, ConnectionConfig cfg, IProgress<string> log)
        {
            try
            {
                DateTime today = DateTime.UtcNow.Date;
                List<ArtifactUpdate> items = await QueryAsync(client, "*", today.AddDays(-BaselineDays), today.AddDays(1)).ConfigureAwait(false);
                Save(cfg.LocalFolderPath, new Store { PulledAtUtc = DateTime.UtcNow, Baseline = items });
                log?.Report($"Change tracking: {items.Count} Calculation Manager change(s) in the last {BaselineDays} days recorded (Deploy warns about newer ones).");
            }
            catch (Exception ex)
            {
                log?.Report($"[WARN] Change tracking unavailable ({ex.Message}): Deploy can't warn about rules changed in EPM after this pull.");
                TryDelete(cfg.LocalFolderPath);    // an older baseline would no longer match the pulled files
            }
        }

        /// <summary>Before Deploy: was the rule changed in EPM after the pull (or this PC's last deploy)?</summary>
        public static async Task<Result> CheckAsync(ConnectionConfig cfg, string planType, string cube, string ruleName)
        {
            Store store = Load(cfg.LocalFolderPath);
            if (store == null) return new Result { Note = "not checked: no change-tracking data — pull once with this version" };

            string key = Key(planType, cube, ruleName);
            List<ArtifactUpdate> now;
            using (var client = new EpmClient(cfg))
                now = await QueryAsync(client, ruleName, store.PulledAtUtc.Date.AddDays(-2), DateTime.UtcNow.Date.AddDays(1)).ConfigureAwait(false);
            ArtifactUpdate latest = Latest(now, ruleName, cube);
            var result = new Result { PulledAtUtc = store.PulledAtUtc };
            if (latest == null) { result.Note = "no change since the pull"; return result; }

            // Deployed from here, but EPM hadn't listed it then: this user's change is that deploy
            if (store.PendingOwn.Contains(key) && SameUser(latest.ModifiedBy, cfg.Username))
            {
                store.PendingOwn.Remove(key);
                store.Deployed[key] = latest;
                Save(cfg.LocalFolderPath, store);
                result.Note = "the latest change is this PC's deploy";
                return result;
            }

            ArtifactUpdate baseline = store.Deployed.TryGetValue(key, out ArtifactUpdate d) ? d : Latest(store.Baseline, ruleName, cube);
            if (baseline == null || Newer(latest.ModifiedDate, baseline.ModifiedDate))
                result.Conflict = latest;
            else
                result.Note = "no change since the pull";
            return result;
        }

        /// <summary>What a new pull would bring: Calculation Manager changes since the last pull.</summary>
        public sealed class PullCheck
        {
            public DateTime PulledAtUtc;
            /// <summary>Artifacts changed in EPM since the pull, this PC's own deploys left out.</summary>
            public List<ArtifactUpdate> Changed = new List<ArtifactUpdate>();
        }

        /// <summary>
        /// Before Pull: the Calculation Manager artifacts changed in EPM since the last pull,
        /// leaving out this PC's own deploys (the local files already have them). Null when it
        /// can't be told (never pulled with change tracking). Throws when EPM can't be asked.
        /// </summary>
        public static async Task<PullCheck> ChangesSincePullAsync(ConnectionConfig cfg)
        {
            Store store = Load(cfg.LocalFolderPath);
            if (store == null) return null;
            List<ArtifactUpdate> now;
            using (var client = new EpmClient(cfg))
                now = await QueryAsync(client, "*", store.PulledAtUtc.Date.AddDays(-2), DateTime.UtcNow.Date.AddDays(1)).ConfigureAwait(false);

            var result = new PullCheck { PulledAtUtc = store.PulledAtUtc };
            foreach (var group in now.GroupBy(u => (u.ArtifactName ?? "").ToLowerInvariant() + "\u0001" + (u.Path ?? "").ToLowerInvariant()))
            {
                ArtifactUpdate latest = null;
                foreach (var u in group) if (latest == null || Newer(u.ModifiedDate, latest.ModifiedDate)) latest = u;

                ArtifactUpdate baseline = null;
                foreach (var b in store.Baseline.Where(b => string.Equals(b.ArtifactName, latest.ArtifactName, StringComparison.OrdinalIgnoreCase) &&
                                                            string.Equals(b.Path ?? "", latest.Path ?? "", StringComparison.OrdinalIgnoreCase)))
                    if (baseline == null || Newer(b.ModifiedDate, baseline.ModifiedDate)) baseline = b;
                if (baseline != null && !Newer(latest.ModifiedDate, baseline.ModifiedDate)) continue;   // nothing newer

                // This PC's deploy of it (recorded, or still waiting to be listed)?
                bool own = store.Deployed.Values.Any(d =>
                               string.Equals(d.ArtifactName, latest.ArtifactName, StringComparison.OrdinalIgnoreCase) &&
                               !Newer(latest.ModifiedDate, d.ModifiedDate)) ||
                           (SameUser(latest.ModifiedBy, cfg.Username) &&
                            store.PendingOwn.Any(k => k.EndsWith("/" + latest.ArtifactName, StringComparison.OrdinalIgnoreCase)));
                if (!own) result.Changed.Add(latest);
            }
            return result;
        }

        /// <summary>After a successful deploy from here: its change becomes the rule's baseline. Never throws.</summary>
        public static async Task RecordOwnDeployAsync(EpmClient client, ConnectionConfig cfg, string planType, string cube, string ruleName, IProgress<string> log)
        {
            Store store = Load(cfg.LocalFolderPath);
            if (store == null) return;
            string key = Key(planType, cube, ruleName);
            try
            {
                DateTime today = DateTime.UtcNow.Date;
                ArtifactUpdate latest = Latest(await QueryAsync(client, ruleName, today.AddDays(-1), today.AddDays(1)).ConfigureAwait(false), ruleName, cube);
                if (latest != null && SameUser(latest.ModifiedBy, cfg.Username))
                {
                    store.Deployed[key] = latest;
                    store.PendingOwn.Remove(key);
                }
                else store.PendingOwn.Add(key);
            }
            catch (Exception ex)
            {
                store.PendingOwn.Add(key);
                log?.Report($"  Change tracking: could not read the rule's new change date ({ex.Message}).");
            }
            Save(cfg.LocalFolderPath, store);
        }

        // ------------------------------------------------------------------ //

        private static async Task<List<ArtifactUpdate>> QueryAsync(EpmClient client, string artifactName, DateTime start, DateTime end)
        {
            var body = new
            {
                artifactName,
                artifactType = "*",
                modifiedBy   = "*",
                startDate    = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                endDate      = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                categories   = new[] { Category },
            };
            JObject resp = await client.PostAsync<object, JObject>("/interop/rest/v2/migration/artifactupdates", body).ConfigureAwait(false);
            if (resp == null) throw new EpmException("empty response from List Updated Artifacts");
            int status = (int?)resp["status"] ?? 0;
            if (status != 0) throw new EpmException($"List Updated Artifacts returned status {status}: {(string)resp["details"]}");
            return (resp["items"] as JArray ?? new JArray())
                .Select(i => new ArtifactUpdate
                {
                    ArtifactName = (string)i["artifactName"], ArtifactType = (string)(i["artifactType"] ?? i["artifacttype"]),
                    ModifiedBy = (string)i["modifiedBy"], ModifiedDate = (string)i["modifiedDate"], Path = (string)i["path"],
                })
                .Where(u => !string.IsNullOrEmpty(u.ArtifactName))
                .ToList();
        }

        /// <summary>The rule's most recent entry: same name, and in its cube when the paths say so.</summary>
        private static ArtifactUpdate Latest(IEnumerable<ArtifactUpdate> items, string ruleName, string cube)
        {
            var named = (items ?? Enumerable.Empty<ArtifactUpdate>())
                .Where(u => string.Equals(u.ArtifactName, ruleName, StringComparison.OrdinalIgnoreCase)).ToList();
            var inCube = named.Where(u => PathHasSegment(u.Path, cube)).ToList();
            if (inCube.Count > 0) named = inCube;
            ArtifactUpdate best = null;
            foreach (var u in named)
                if (best == null || Newer(u.ModifiedDate, best.ModifiedDate)) best = u;
            return best;
        }

        private static bool PathHasSegment(string path, string segment) =>
            !string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(segment) &&
            path.Split('/').Any(p => string.Equals(p, segment, StringComparison.OrdinalIgnoreCase));

        /// <summary>a later than b (EPM time strings; unparsable ones compare as text).</summary>
        private static bool Newer(string a, string b)
        {
            if (Parse(a) is DateTime da && Parse(b) is DateTime db) return da > db;
            return string.CompareOrdinal(a ?? "", b ?? "") > 0;
        }

        /// <summary>EPM's modifiedDate: "10/23/2026 11:37:25" (what tenants return), or the documented
        /// "2026-10-23 11:37:25". It is UTC.</summary>
        private static DateTime? Parse(string s) =>
            DateTime.TryParseExact((s ?? "").Trim(), new[]
                {
                    "MM/dd/yyyy HH:mm:ss", "M/d/yyyy H:mm:ss", "MM/dd/yyyy hh:mm:ss tt", "M/d/yyyy h:mm:ss tt", "MM/dd/yyyy HH:mm", "MM/dd/yyyy",
                    "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
                },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) ? d : (DateTime?)null;

        private static bool SameUser(string a, string b)
        {
            string N(string s) { s = (s ?? "").Trim(); int at = s.IndexOf('@'); return at > 0 ? s.Substring(0, at) : s; }
            return string.Equals(N(a), N(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>EPM's time (UTC) without seconds: "2026-10-22 14:05 UTC".</summary>
        public static string ShowDate(string s) => Parse(s) is DateTime d ? d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : s;

        private static string Key(string planType, string cube, string rule) => $"{planType}/{cube}/{rule}";

        private static string FilePath(string localFolder) => Path.Combine(EpmMetadata.Folder(localFolder), "rule_changes.json");

        private static Store Load(string localFolder)
        {
            try
            {
                string f = FilePath(localFolder);
                if (!File.Exists(f)) return null;
                var s = JsonConvert.DeserializeObject<Store>(File.ReadAllText(f, Encoding.UTF8));
                if (s == null) return null;
                s.Deployed = new Dictionary<string, ArtifactUpdate>(s.Deployed ?? new Dictionary<string, ArtifactUpdate>(), StringComparer.OrdinalIgnoreCase);
                s.PendingOwn = new HashSet<string>(s.PendingOwn ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
                s.Baseline = s.Baseline ?? new List<ArtifactUpdate>();
                return s;
            }
            catch { return null; }
        }

        private static void Save(string localFolder, Store s)
        {
            Directory.CreateDirectory(EpmMetadata.Folder(localFolder));
            File.WriteAllText(FilePath(localFolder), JsonConvert.SerializeObject(s, Formatting.Indented),
                              new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static void TryDelete(string localFolder)
        {
            try { File.Delete(FilePath(localFolder)); } catch { }
        }
    }
}
