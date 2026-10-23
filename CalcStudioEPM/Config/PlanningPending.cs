using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace CalcStudioEPM.Config
{
    /// <summary>
    /// Rules whose latest deploy from Notepad++ reached Calculation Manager only, so
    /// Planning may still run an older version until someone deploys the rule from
    /// Calculation Manager. Execute warns about these. Per connection (LocalFolderPath),
    /// keyed "{planType}/{cube}/{rule}"; stored in
    /// %AppData%\Notepad++\plugins\CalcStudioEPM\planning_pending.json.
    /// </summary>
    public static class PlanningPending
    {
        private static string FilePath => Path.Combine(ConfigManager.SettingsDir, "planning_pending.json");

        private static string Key(string planType, string cube, string ruleName) =>
            $"{planType}/{cube}/{ruleName}";

        public static bool IsPending(string connectionKey, string planType, string cube, string ruleName)
        {
            var all = LoadAll();
            return connectionKey != null && all.TryGetValue(connectionKey, out var rules) &&
                   rules.Contains(Key(planType, cube, ruleName));
        }

        public static void Set(string connectionKey, string planType, string cube, string ruleName, bool pending)
        {
            if (connectionKey == null) return;
            try
            {
                var all = LoadAll();
                if (!all.TryGetValue(connectionKey, out var rules))
                    all[connectionKey] = rules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                string key = Key(planType, cube, ruleName);
                if (pending ? !rules.Add(key) : !rules.Remove(key)) return; // unchanged
                if (rules.Count == 0) all.Remove(connectionKey);

                Directory.CreateDirectory(ConfigManager.SettingsDir);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(all, Formatting.Indented),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch { /* a reminder only — never fail a deploy or execution over it */ }
        }

        /// <summary>Rules waiting for a Planning deploy, over all connections.</summary>
        public static int Count()
        {
            int n = 0;
            foreach (var kv in LoadAll()) n += kv.Value.Count;
            return n;
        }

        /// <summary>Forgets everything stored here (deletes the file).</summary>
        public static void ClearAll()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }

        private static Dictionary<string, HashSet<string>> LoadAll()
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(
                        File.ReadAllText(FilePath, Encoding.UTF8));
                    if (d != null)
                        foreach (var kv in d)
                            result[kv.Key] = new HashSet<string>(kv.Value ?? new List<string>(),
                                                                 StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }
            return result;
        }
    }
}
