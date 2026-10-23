using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace CalcStudioEPM.Config
{
    /// <summary>One sort key of the Variables window: a column and a direction.</summary>
    public class SortKey
    {
        public string Column    { get; set; }
        public bool   Ascending { get; set; }
    }

    /// <summary>
    /// Remembers the Variables window's sort order per connection (keyed by the
    /// connection's LocalFolderPath) in
    /// %AppData%\Notepad++\plugins\CalcStudioEPM\variables_view.json.
    /// </summary>
    public static class VariablesViewSettings
    {
        private static string FilePath => Path.Combine(ConfigManager.SettingsDir, "variables_view.json");

        /// <summary>Default: grouped by type, most used first.</summary>
        public static List<SortKey> DefaultSort() => new List<SortKey>
        {
            new SortKey { Column = "Type",   Ascending = true  },
            new SortKey { Column = "UsedBy", Ascending = false },
        };

        public static List<SortKey> LoadSort(string connectionKey)
        {
            var all = LoadAll();
            return connectionKey != null && all.TryGetValue(connectionKey, out var keys) && keys?.Count > 0
                ? keys
                : DefaultSort();
        }

        public static void SaveSort(string connectionKey, List<SortKey> keys)
        {
            if (connectionKey == null) return;
            try
            {
                var all = LoadAll();
                all[connectionKey] = keys;
                Directory.CreateDirectory(ConfigManager.SettingsDir);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(all, Formatting.Indented),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch { /* preference only — never fail the window over it */ }
        }

        /// <summary>Connections with a remembered sort order.</summary>
        public static int Count() => LoadAll().Count;

        /// <summary>Back to the default sort for every connection (deletes the file).</summary>
        public static void ClearAll()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }

        private static Dictionary<string, List<SortKey>> LoadAll()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = JsonConvert.DeserializeObject<Dictionary<string, List<SortKey>>>(
                        File.ReadAllText(FilePath, Encoding.UTF8));
                    if (d != null)
                        return new Dictionary<string, List<SortKey>>(d, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }
            return new Dictionary<string, List<SortKey>>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
