using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace CalcStudioEPM.Config
{
    /// <summary>
    /// Small per-user UI memory (one-off notices and the like), stored in
    /// %AppData%\Notepad++\plugins\CalcStudioEPM\ui_state.json.
    /// </summary>
    public class UiState
    {
        /// <summary>How many times the "Local Syntax Validation is not an EPM validation" notice was shown.</summary>
        public int  LocalValidationNoticeCount     { get; set; }
        /// <summary>The user ticked "Don't show me again" on that notice.</summary>
        public bool LocalValidationNoticeDismissed { get; set; }

        /// <summary>A language file CalcStudio wrote: its hash then, and the style version.</summary>
        public class GeneratedFile
        {
            public string Hash    { get; set; }
            public int    Version { get; set; }
        }

        /// <summary>Calc Script language files written by CalcStudio, by lower-case path, so a newer
        /// version regenerates them only while nobody changed them.</summary>
        public System.Collections.Generic.Dictionary<string, GeneratedFile> LanguageFiles { get; set; } =
            new System.Collections.Generic.Dictionary<string, GeneratedFile>();

        /// <summary>Last size of each resizable dialog, [width, height] at 100 % scaling.</summary>
        public System.Collections.Generic.Dictionary<string, int[]> WindowSizes { get; set; } =
            new System.Collections.Generic.Dictionary<string, int[]>();

        /// <summary>The password prompt's last "keep the password" choice, per connection
        /// (see PasswordForm.KeyOf): ThisAction, Session or Connection.</summary>
        public System.Collections.Generic.Dictionary<string, string> PasswordKeep { get; set; } =
            new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>When the startup update check last reached GitHub (UTC).</summary>
        public DateTime LastUpdateCheckUtc { get; set; }
        /// <summary>The newer release the startup check last announced, so each is announced once.</summary>
        public string UpdateNotifiedVersion { get; set; }

        private static string FilePath => Path.Combine(ConfigManager.SettingsDir, "ui_state.json");

        public static UiState Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonConvert.DeserializeObject<UiState>(File.ReadAllText(FilePath, Encoding.UTF8)) ?? new UiState();
            }
            catch { /* unreadable — start fresh */ }
            return new UiState();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigManager.SettingsDir);
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch { /* preference only */ }
        }
    }
}
