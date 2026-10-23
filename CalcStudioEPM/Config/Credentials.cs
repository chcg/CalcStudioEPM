using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace CalcStudioEPM.Config
{
    /// <summary>
    /// Where a connection's password comes from:
    ///  1. saved on the connection (<see cref="ConnectionConfig.SavePassword"/>) — DPAPI-encrypted
    ///     in connections.json, see <see cref="ConfigManager.EncryptPassword"/>;
    ///  2. otherwise remembered for this Notepad++ session only — held in memory encrypted with
    ///     <see cref="ProtectedMemory"/> (SameProcess), gone when Notepad++ closes;
    ///  3. otherwise given for the current action only — dropped by <see cref="EndAction"/>
    ///     when the plugin command finishes (also encrypted in memory);
    ///  4. otherwise none: the user is asked (Forms.PasswordForm) before talking to EPM Cloud.
    ///
    /// Session entries are keyed by connection folder + service URL + username, so changing
    /// either of those in the connection settings asks again.
    /// </summary>
    public static class Credentials
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, byte[]> _session =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, byte[]> _action =
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        private static string Key(ConnectionConfig cfg) =>
            $"{cfg.LocalFolderPath}|{cfg.ServiceBaseUrl}|{cfg.Username}";

        /// <summary>True when the connection keeps its password in connections.json.</summary>
        public static bool IsSaved(ConnectionConfig cfg) =>
            cfg != null && cfg.SavePassword && !string.IsNullOrEmpty(cfg.EncryptedPassword);

        /// <summary>True when a password is available without asking.</summary>
        public static bool IsAvailable(ConnectionConfig cfg) => GetPassword(cfg) != null;

        /// <summary>The saved or session password; null when the user must be asked.</summary>
        public static string GetPassword(ConnectionConfig cfg)
        {
            if (cfg == null) return null;
            if (IsSaved(cfg))
            {
                // Saved by another Windows user or PC (DPAPI can't decrypt it): ask instead
                try { return ConfigManager.DecryptPassword(cfg.EncryptedPassword); }
                catch (InvalidOperationException) { }
            }

            lock (_lock)
            {
                if (_session.TryGetValue(Key(cfg), out byte[] blob)) return Unprotect(blob);
                if (_action.TryGetValue(Key(cfg), out blob))         return Unprotect(blob);
                return null;
            }
        }

        /// <summary>Keeps <paramref name="password"/> (encrypted) until <see cref="EndAction"/>.</summary>
        public static void UseForThisAction(ConnectionConfig cfg, string password)
        {
            if (cfg == null || string.IsNullOrEmpty(password)) return;
            lock (_lock)
                _action[Key(cfg)] = Protect(password);
        }

        /// <summary>Drops every "this action only" password. Called when a plugin command ends.</summary>
        public static void EndAction()
        {
            lock (_lock)
            {
                foreach (byte[] blob in _action.Values) Array.Clear(blob, 0, blob.Length);
                _action.Clear();
            }
        }

        /// <summary>Keeps <paramref name="password"/> (encrypted) until Notepad++ closes.</summary>
        public static void RememberForSession(ConnectionConfig cfg, string password)
        {
            if (cfg == null || string.IsNullOrEmpty(password)) return;
            lock (_lock)
                _session[Key(cfg)] = Protect(password);
        }

        /// <summary>Drops the session password, e.g. after EPM Cloud rejected it (HTTP 401).</summary>
        public static void ForgetSession(ConnectionConfig cfg)
        {
            if (cfg == null) return;
            lock (_lock)
            {
                _session.Remove(Key(cfg));
                _action.Remove(Key(cfg));
            }
        }

        // ------------------------------------------------------------------ //
        //  ProtectedMemory needs whole 16-byte blocks: [length:int32][utf8][padding]
        // ------------------------------------------------------------------ //

        private static byte[] Protect(string password)
        {
            byte[] text = Encoding.UTF8.GetBytes(password);
            int size = (4 + text.Length + 15) / 16 * 16;
            var blob = new byte[size];
            BitConverter.GetBytes(text.Length).CopyTo(blob, 0);
            text.CopyTo(blob, 4);
            Array.Clear(text, 0, text.Length);
            ProtectedMemory.Protect(blob, MemoryProtectionScope.SameProcess);
            return blob;
        }

        private static string Unprotect(byte[] blob)
        {
            var copy = (byte[])blob.Clone();
            try
            {
                ProtectedMemory.Unprotect(copy, MemoryProtectionScope.SameProcess);
                int length = BitConverter.ToInt32(copy, 0);
                return Encoding.UTF8.GetString(copy, 4, length);
            }
            finally
            {
                Array.Clear(copy, 0, copy.Length);
            }
        }
    }
}
