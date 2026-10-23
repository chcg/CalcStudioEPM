using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace CalcStudioEPM.Config
{
    /// <summary>
    /// Plugin-wide options, edited in Plugins › CalcStudio for Oracle EPM › Configure… and
    /// stored in %AppData%\Notepad++\plugins\CalcStudioEPM\settings.json.
    /// </summary>
    public class PluginSettings
    {
        /// <summary>Install the "Calc Script - Essbase" user language at startup when it is missing.
        /// Off: the plugin never writes it, so a user's own .csc language stays in charge.</summary>
        public bool InstallCalcScriptLanguage { get; set; } = true;

        /// <summary>Handle Ctrl+K / Ctrl+Shift+K / Ctrl+Shift+Q for .csc files (the language
        /// defines /* */ as a delimiter, so Notepad++ has no comment markers for it).</summary>
        public bool HandleCalcScriptComments { get; set; } = true;

        /// <summary>Autocomplete in .csc files: @ functions, SET options, {runtime prompts}.</summary>
        public bool AutoCompleteCalcScript { get; set; } = true;

        /// <summary>Autocomplete in Groovy rules: EPM API members after a dot, rtps. prompts.</summary>
        public bool AutoCompleteGroovy { get; set; } = true;

        /// <summary>At startup (at most once a day) ask GitHub whether a newer release exists.</summary>
        public bool CheckForUpdates { get; set; } = true;

        private static string FilePath => Path.Combine(ConfigManager.SettingsDir, "settings.json");

        private static PluginSettings _current;

        /// <summary>The settings in effect (loaded once, replaced by <see cref="Save"/>).</summary>
        public static PluginSettings Current => _current ?? (_current = Load());

        public static PluginSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonConvert.DeserializeObject<PluginSettings>(File.ReadAllText(FilePath, Encoding.UTF8))
                           ?? new PluginSettings();
            }
            catch { /* unreadable — defaults */ }
            return new PluginSettings();
        }

        public static void Save(PluginSettings settings)
        {
            Directory.CreateDirectory(ConfigManager.SettingsDir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(settings, Formatting.Indented),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _current = settings;
        }

        public PluginSettings Clone() => (PluginSettings)MemberwiseClone();
    }
}
