using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace CalcStudioEPM.Config
{
    /// <summary>
    /// Last value entered for each runtime prompt, per connection (keyed by
    /// LocalFolderPath) and variable name — so RTP_Year entered for one rule is offered
    /// again for the next rule that prompts for it. Stored in
    /// %AppData%\Notepad++\plugins\CalcStudioEPM\prompt_values.json.
    /// </summary>
    public static class PromptValues
    {
        private static string FilePath => Path.Combine(ConfigManager.SettingsDir, "prompt_values.json");

        public static Dictionary<string, string> Load(string connectionKey)
        {
            var all = LoadAll();
            return connectionKey != null && all.TryGetValue(connectionKey, out var values)
                ? new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Merges <paramref name="values"/> into what is stored for the connection.</summary>
        public static void Save(string connectionKey, IDictionary<string, string> values)
        {
            if (connectionKey == null || values == null || values.Count == 0) return;
            try
            {
                var all = LoadAll();
                if (!all.TryGetValue(connectionKey, out var stored))
                    all[connectionKey] = stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in values) stored[kv.Key] = kv.Value;

                Directory.CreateDirectory(ConfigManager.SettingsDir);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(all, Formatting.Indented),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch { /* convenience only — never block an execution over it */ }
        }

        /// <summary>Number of remembered values, over all connections.</summary>
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

        private static Dictionary<string, Dictionary<string, string>> LoadAll()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
                        File.ReadAllText(FilePath, Encoding.UTF8));
                    if (d != null)
                    {
                        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in d)
                            result[kv.Key] = new Dictionary<string, string>(
                                kv.Value ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                        return result;
                    }
                }
            }
            catch { }
            return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
