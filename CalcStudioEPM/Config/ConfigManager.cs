using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace CalcStudioEPM.Config
{
    /// <summary>
    /// Manages all EPM connection configurations stored in a single central
    /// <c>connections.json</c> file in the plugin's AppData folder.
    ///
    /// Each connection is uniquely identified by its <see cref="ConnectionConfig.LocalFolderPath"/>
    /// (the primary key).  Multiple project folders can be configured simultaneously and
    /// the "active" connection is used by all plugin commands (Pull, Deploy, Execute …).
    ///
    /// On first startup after upgrading from the single-connection format the manager
    /// automatically migrates the old per-folder <c>config.dat</c> + <c>lastproject.txt</c>
    /// to the new central file so existing users see no disruption.
    /// </summary>
    public static class ConfigManager
    {
        // ------------------------------------------------------------------ //
        //  File paths
        // ------------------------------------------------------------------ //

        private static readonly string _pluginSettingsDir = InitSettingsDir();

        /// <summary>%AppData%\Notepad++\plugins\CalcStudioEPM — home of all plugin settings files.</summary>
        public static string SettingsDir => _pluginSettingsDir;

        /// <summary>The plugin's name before 0.015, and so its old settings folder's name.</summary>
        public const string LegacyName = "NppEPMPlugin";

        /// <summary>
        /// The settings folder. On the first start after the rename from NppEPMPlugin its
        /// settings (connections, saved passwords, rules, window sizes …) are copied over from
        /// %AppData%\Notepad++\plugins\NppEPMPlugin, which is left as it was. Runs before any
        /// settings file is read, since every one of them lives in this folder.
        /// </summary>
        private static string InitSettingsDir()
        {
            string plugins = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notepad++", "plugins");
            string dir    = Path.Combine(plugins, Assembly.GetExecutingAssembly().GetName().Name);
            string legacy = Path.Combine(plugins, LegacyName);
            try
            {
                if (!Directory.Exists(dir) && Directory.Exists(legacy) &&
                    !string.Equals(dir, legacy, StringComparison.OrdinalIgnoreCase))
                {
                    // Copied aside and then moved in, so a failed copy is retried next start
                    string partial = dir + ".copying";
                    if (Directory.Exists(partial)) Directory.Delete(partial, recursive: true);
                    CopyFolder(legacy, partial);
                    Directory.Move(partial, dir);
                }
            }
            catch { /* start with fresh settings rather than not at all */ }
            return dir;
        }

        private static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
            foreach (string sub in Directory.GetDirectories(from))
                CopyFolder(sub, Path.Combine(to, Path.GetFileName(sub)));
        }

        private static readonly string _connectionsFile = Path.Combine(
            _pluginSettingsDir, "connections.json");

        // Legacy paths — read-only; used only by the one-time migration.
        private static readonly string _lastProjectFile = Path.Combine(
            _pluginSettingsDir, "lastproject.txt");

        // ------------------------------------------------------------------ //
        //  Public API
        // ------------------------------------------------------------------ //

        /// <summary>Returns all configured connections (may be empty).</summary>
        public static List<ConnectionConfig> GetAll()
        {
            return LoadStore().Connections;
        }

        /// <summary>
        /// Returns the active connection, or <c>null</c> if none are configured.
        /// The active connection is the one most recently saved/set via
        /// <see cref="UpsertConfig"/> or <see cref="SetActive"/>.
        /// Falls back to the first connection if the stored active path is stale.
        /// </summary>
        public static ConnectionConfig GetActive()
        {
            var store = LoadStore();
            if (store.Connections.Count == 0) return null;

            if (!string.IsNullOrEmpty(store.ActiveFolderPath))
            {
                var active = store.Connections.Find(c =>
                    string.Equals(c.LocalFolderPath, store.ActiveFolderPath,
                        StringComparison.OrdinalIgnoreCase));
                if (active != null) return active;
            }

            // ActiveFolderPath is stale or unset — fall back to first entry
            return store.Connections[0];
        }

        /// <summary>
        /// Adds a new connection or updates an existing one (matched by
        /// <see cref="ConnectionConfig.LocalFolderPath"/>) and marks it as active.
        /// </summary>
        /// <param name="replacingFolder">The folder the connection had before, when it was
        /// moved to another folder: that entry is replaced instead of kept as a second one.</param>
        public static void UpsertConfig(ConnectionConfig cfg, string replacingFolder = null)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (string.IsNullOrWhiteSpace(cfg.LocalFolderPath))
                throw new InvalidOperationException("LocalFolderPath must be set before saving.");

            var store = LoadStore();
            if (!string.IsNullOrWhiteSpace(replacingFolder) && !SameFolder(replacingFolder, cfg.LocalFolderPath))
                store.Connections.RemoveAll(c => SameFolder(c.LocalFolderPath, replacingFolder));

            int idx = store.Connections.FindIndex(c => SameFolder(c.LocalFolderPath, cfg.LocalFolderPath));

            if (idx >= 0)
                store.Connections[idx] = cfg;
            else
                store.Connections.Add(cfg);

            store.ActiveFolderPath = cfg.LocalFolderPath;
            SaveStore(store);
        }

        /// <summary>
        /// Why <paramref name="folder"/> can't be a connection folder: it is, is inside, or
        /// contains another connection's folder (files would belong to two connections).
        /// Null when it is fine. <paramref name="ignoreFolder"/>: the connection being edited.
        /// <paramref name="connections"/>: the ones to check against (default: all saved).
        /// </summary>
        public static string FolderConflict(string folder, string ignoreFolder = null,
                                            IEnumerable<ConnectionConfig> connections = null)
        {
            if (string.IsNullOrWhiteSpace(folder)) return null;
            string f = NormalizeFolder(folder);
            foreach (ConnectionConfig c in connections ?? GetAll())
            {
                if (string.IsNullOrWhiteSpace(c.LocalFolderPath)) continue;
                if (ignoreFolder != null && SameFolder(c.LocalFolderPath, ignoreFolder)) continue;
                string o = NormalizeFolder(c.LocalFolderPath);
                string name = string.IsNullOrWhiteSpace(c.Name) ? c.LocalFolderPath : $"'{c.Name}'";
                if (string.Equals(f, o, StringComparison.OrdinalIgnoreCase))
                    return $"The folder is already used by the connection {name}.";
                if (f.StartsWith(o + "\\", StringComparison.OrdinalIgnoreCase))
                    return $"The folder is inside the folder of the connection {name} ({c.LocalFolderPath}). Connection folders can't be nested.";
                if (o.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase))
                    return $"The folder contains the folder of the connection {name} ({c.LocalFolderPath}). Connection folders can't be nested.";
            }
            return null;
        }

        public static bool SameFolder(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
            string.Equals(NormalizeFolder(a), NormalizeFolder(b), StringComparison.OrdinalIgnoreCase);

        private static string NormalizeFolder(string path)
        {
            string p = path.Trim().Replace('/', '\\');
            try { p = Path.GetFullPath(p); } catch { /* keep as typed */ }
            return p.TrimEnd('\\');
        }

        /// <summary>
        /// Removes the connection whose <see cref="ConnectionConfig.LocalFolderPath"/>
        /// matches <paramref name="localFolderPath"/>.
        /// If it was the active connection the active path is changed to the first
        /// remaining connection (or cleared if the list is now empty).
        /// No files on disk are deleted.
        /// </summary>
        public static void DeleteConfig(string localFolderPath)
        {
            if (string.IsNullOrWhiteSpace(localFolderPath)) return;

            var store = LoadStore();
            store.Connections.RemoveAll(c =>
                string.Equals(c.LocalFolderPath, localFolderPath,
                    StringComparison.OrdinalIgnoreCase));

            if (string.Equals(store.ActiveFolderPath, localFolderPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                store.ActiveFolderPath = store.Connections.Count > 0
                    ? store.Connections[0].LocalFolderPath
                    : null;
            }

            SaveStore(store);
        }

        /// <summary>
        /// Sets the active connection without changing any connection fields.
        /// </summary>
        public static void SetActive(string localFolderPath)
        {
            var store = LoadStore();
            store.ActiveFolderPath = localFolderPath;
            SaveStore(store);
        }

        /// <summary>
        /// Tries to read a <see cref="ConnectionConfig"/> from a legacy per-folder
        /// <c>{folder}\.epm\config.dat</c> file.
        /// Returns <c>null</c> when the file does not exist or cannot be decrypted
        /// (e.g. encrypted by a different Windows user account).
        /// The returned config's <see cref="ConnectionConfig.LocalFolderPath"/> is
        /// always overwritten with <paramref name="folderPath"/> and a default
        /// <see cref="ConnectionConfig.Name"/> is derived from the folder name when
        /// the original config has none.
        /// </summary>
        public static ConnectionConfig TryImportFromFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return null;

            string configPath = Path.Combine(folderPath,
                PathConstants.EpmDirName, "config.dat");
            if (!File.Exists(configPath)) return null;

            try
            {
                byte[] encryptedBytes = File.ReadAllBytes(configPath);
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null,
                    DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(plainBytes);
                var cfg = JsonConvert.DeserializeObject<ConnectionConfig>(json);
                if (cfg == null) return null;

                cfg.LocalFolderPath = folderPath;
                if (string.IsNullOrWhiteSpace(cfg.Name))
                    cfg.Name = Path.GetFileName(folderPath.TrimEnd('\\', '/'));

                return cfg;
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ //
        //  Encryption helpers
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Encrypts a plain-text password using DPAPI (current user scope)
        /// and returns it as a base64 string.
        /// </summary>
        public static string EncryptPassword(string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword)) return string.Empty;

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainPassword);
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }

        /// <summary>
        /// Decrypts a base64 DPAPI-encrypted password back to plain text.
        /// </summary>
        public static string DecryptPassword(string base64EncryptedPassword)
        {
            if (string.IsNullOrEmpty(base64EncryptedPassword)) return string.Empty;

            try
            {
                byte[] encryptedBytes = Convert.FromBase64String(base64EncryptedPassword);
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to decrypt password: " + ex.Message, ex);
            }
        }

        // ------------------------------------------------------------------ //
        //  Internal helpers
        // ------------------------------------------------------------------ //

        private static ConnectionStore LoadStore()
        {
            if (File.Exists(_connectionsFile))
            {
                try
                {
                    string json = File.ReadAllText(_connectionsFile, Encoding.UTF8);
                    var store = JsonConvert.DeserializeObject<ConnectionStore>(json);
                    if (store != null)
                    {
                        if (store.Connections == null)
                            store.Connections = new List<ConnectionConfig>();
                        return store;
                    }
                }
                catch
                {
                    // Keep an unreadable file (a typo in a hand edit, a damaged disk) instead of
                    // letting the next save overwrite every connection with an empty list
                    try { File.Move(_connectionsFile, _connectionsFile + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); }
                    catch { }
                }
            }

            return MigrateFromLegacy();
        }

        private static void SaveStore(ConnectionStore store)
        {
            Directory.CreateDirectory(_pluginSettingsDir);
            string json = JsonConvert.SerializeObject(store, Formatting.Indented);

            // Written beside the file, then swapped in: a crash mid-write leaves the old file whole
            string tmp = _connectionsFile + ".tmp";
            File.WriteAllText(tmp, json, Encoding.UTF8);
            try
            {
                if (File.Exists(_connectionsFile)) File.Replace(tmp, _connectionsFile, null);
                else File.Move(tmp, _connectionsFile);
            }
            catch (IOException)
            {
                // File.Replace isn't supported everywhere (some network drives)
                File.Copy(tmp, _connectionsFile, overwrite: true);
                File.Delete(tmp);
            }
        }

        /// <summary>
        /// One-time migration: reads the old single-connection format
        /// (<c>lastproject.txt</c> + per-folder <c>config.dat</c>) and converts it
        /// to the new <c>connections.json</c> format.
        /// Silently returns an empty store on any error so the plugin starts clean
        /// rather than crashing.
        /// </summary>
        private static ConnectionStore MigrateFromLegacy()
        {
            var store = new ConnectionStore();

            try
            {
                if (!File.Exists(_lastProjectFile)) return store;

                string lastFolder = File.ReadAllText(_lastProjectFile, Encoding.UTF8).Trim();
                if (string.IsNullOrWhiteSpace(lastFolder)) return store;

                var cfg = TryImportFromFolder(lastFolder);
                if (cfg == null) return store;

                store.Connections.Add(cfg);
                store.ActiveFolderPath = lastFolder;

                // Persist immediately so migration runs only once
                SaveStore(store);
            }
            catch { /* ignore — return empty store */ }

            return store;
        }
    }

    // ------------------------------------------------------------------ //
    //  Internal DTO serialised to connections.json
    // ------------------------------------------------------------------ //

    internal class ConnectionStore
    {
        public string ActiveFolderPath { get; set; }
        public List<ConnectionConfig> Connections { get; set; } = new List<ConnectionConfig>();
    }
}
