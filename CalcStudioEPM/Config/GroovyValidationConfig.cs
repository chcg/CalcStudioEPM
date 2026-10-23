using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace CalcStudioEPM.Config
{
    /// <summary>A forbidden code pattern (regex, matched against code only — not strings or comments).</summary>
    public class RestrictedPattern
    {
        public string Pattern  { get; set; }
        public string Message  { get; set; }
        /// <summary>"error", "warning" or "off".</summary>
        public string Severity { get; set; } = "warning";
    }

    /// <summary>
    /// User-editable settings of the local Groovy validation, stored in
    /// %AppData%\Notepad++\plugins\CalcStudioEPM\groovy_validation.json and created with
    /// defaults on first use. "Edit rules" in the validation window opens it in Notepad++.
    /// </summary>
    public class GroovyValidationConfig
    {
        /// <summary>Rule id → "error" | "warning" | "off".</summary>
        public Dictionary<string, string> Rules { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Names that exist without a declaration (EPM bindings, Groovy built-ins…).</summary>
        public List<string> KnownGlobals { get; set; } = new List<string>();

        /// <summary>Functions callable without a receiver (println, EPM script helpers…).</summary>
        public List<string> KnownFunctions { get; set; } = new List<string>();

        public List<RestrictedPattern> Restricted { get; set; } = new List<RestrictedPattern>();

        /// <summary>"Type.member" (or "*.member") the EPM API reference doesn't list but EPM accepts.</summary>
        public List<string> KnownApiMembers { get; set; }

        /// <summary>Which built-in restricted patterns this file has had added (new ones are added once).</summary>
        public int RestrictedDefaultsVersion { get; set; }

        /// <summary>Bump when patterns are added to <see cref="Defaults"/>; existing files get the new ones once.</summary>
        public const int CurrentRestrictedDefaultsVersion = 2;

        // ------------------------------------------------------------------ //

        public static readonly Dictionary<string, string> RuleDescriptions = new Dictionary<string, string>
        {
            ["syntax.unbalanced-brackets"]  = "( [ { without a matching ) ] } (or the reverse)",
            ["syntax.unterminated-string"]  = "a string that is never closed",
            ["syntax.unterminated-comment"] = "a /* comment that is never closed",
            ["undeclared-variable"]         = "a variable used without def / a type / a parameter",
            ["unknown-function"]            = "a call to a function that is neither defined in the script nor a known function",
            ["binding-assignment"]          = "x = … without def, which creates a script-wide binding variable",
            ["rtps.not-declared"]           = "rtps.X used but {X} missing from the /*RTPS: */ header (no prompt is shown)",
            ["rtps.unknown-variable"]       = "a prompt in the /*RTPS: */ header that is not in the connection's variable dictionary",
            ["rtps.type-usage"]             = "rtps.X.member / .members used on a prompt of a different type",
            ["api.unknown-member"]          = "a method or property that the receiver's EPM type doesn't have (EPM Groovy API)",
            ["api.argument-count"]          = "an EPM method called with a number of arguments no overload takes",
            ["api.deprecated"]              = "a deprecated EPM method",
            ["grid.not-closed"]             = "a grid from loadGrid(…) that is never closed",
            ["rtps.unused"]                 = "a /*RTPS: */ prompt the script never reads (users are asked for nothing)",
            ["if.assignment"]               = "= (assignment) instead of == in an if condition",
            ["string.single-quoted-interpolation"] = "${…} inside a '…' string, where it stays literal text",
            ["uservar.unknown"]             = "getUserVariable(\"X\") with a name that is not a user variable (needs a pull)",
            ["uservar.wrong-dimension"]     = "getDimension(\"D\").getUserVariable(\"X\") where X belongs to another dimension",
            ["subvar.unknown"]              = "a substitution variable that doesn't exist (getSubstitutionVariable…(\"X\") or &X in calc text)",
            ["subvar.wrong-cube"]           = "a substitution variable defined only for other cubes",
            ["dimension.unknown"]           = "getDimension(\"X\") with a name that is not a dimension (needs dimensions on Pull)",
            ["member.unknown"]              = "getDimension(\"D\").getMember(\"X\") where X is not a member or alias of D",
        };

        public string SeverityOf(string ruleId) =>
            Rules.TryGetValue(ruleId, out string s) ? (s ?? "off").Trim().ToLowerInvariant() : "warning";

        public static GroovyValidationConfig Defaults() => new GroovyValidationConfig
        {
            Rules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["syntax.unbalanced-brackets"]  = "error",
                ["syntax.unterminated-string"]  = "error",
                ["syntax.unterminated-comment"] = "error",
                ["undeclared-variable"]         = "error",
                ["unknown-function"]            = "warning",
                ["binding-assignment"]          = "warning",
                ["rtps.not-declared"]           = "warning",
                ["rtps.unknown-variable"]       = "warning",
                ["rtps.type-usage"]             = "warning",
                ["api.unknown-member"]          = "warning",
                ["api.argument-count"]          = "warning",
                ["api.deprecated"]              = "warning",
                ["grid.not-closed"]             = "warning",
                ["rtps.unused"]                 = "warning",
                ["if.assignment"]               = "warning",
                ["string.single-quoted-interpolation"] = "warning",
                ["uservar.unknown"]             = "warning",
                ["uservar.wrong-dimension"]     = "warning",
                ["subvar.unknown"]              = "warning",
                ["subvar.wrong-cube"]           = "warning",
                ["dimension.unknown"]           = "warning",
                ["member.unknown"]              = "warning",
            },
            KnownApiMembers = new List<string>
            {
                "DataGridDefinitionBuilder.addPov",   // used by working rules, missing from the reference
            },
            RestrictedDefaultsVersion = CurrentRestrictedDefaultsVersion,
            KnownGlobals = new List<string>
            {
                // EPM Groovy rule bindings
                "operation", "rtps", "rule",
                // Groovy / script built-ins
                "it", "this", "super", "binding", "args", "out", "owner", "delegate", "metaClass",
                // common package roots in fully-qualified names (java.util.Date …)
                "java", "javax", "groovy", "oracle",
            },
            KnownFunctions = new List<string>
            {
                "println", "print", "printf", "sprintf", "sleep", "evaluate",
                // EPM Groovy script helpers
                "throwVetoException", "messageBundle", "messageBundleLoader",
                "cscParams", "executeJob", "currentTimeMillis", "csvWriter", "csvIterator",
                "json", "getEpmAutomate",
            },
            Restricted = new List<RestrictedPattern>
            {
                new RestrictedPattern { Pattern = @"\bSystem\s*\.\s*exit\b",          Message = "System.exit is not allowed in EPM Groovy rules." },
                new RestrictedPattern { Pattern = @"\bRuntime\s*\.\s*getRuntime\b",    Message = "Runtime access is blocked by the EPM Groovy sandbox." },
                new RestrictedPattern { Pattern = @"\bProcessBuilder\b",                Message = "Starting processes is blocked by the EPM Groovy sandbox." },
                new RestrictedPattern { Pattern = @"\bnew\s+File\s*\(",                 Message = "File system access is blocked by the EPM Groovy sandbox." },
                new RestrictedPattern { Pattern = @"\bThread\s*\.",                     Message = "Threads are blocked by the EPM Groovy sandbox." },
                new RestrictedPattern { Pattern = @"\bClass\s*\.\s*forName\b",          Message = "Reflection (Class.forName) is blocked by the EPM Groovy sandbox." },
                new RestrictedPattern { Pattern = @"@Grab\b",                           Message = "@Grab (downloading libraries) is not available in EPM." },
                new RestrictedPattern { Pattern = @"\.execute\s*\(\s*\)",               Message = "Executing commands is blocked by the EPM Groovy sandbox." },
                // added in defaults version 2
                new RestrictedPattern { Pattern = @"\bnew\s+(?:java\.net\.)?URL\s*\(",  Message = "Network access (URL) is blocked by the EPM Groovy sandbox; use the EPM connection APIs." },
                new RestrictedPattern { Pattern = @"\b(?:Server)?Socket\b",              Message = "Sockets are blocked by the EPM Groovy sandbox." },
                new RestrictedPattern { Pattern = @"\bGroovyShell\b|\bEval\s*\.",         Message = "Evaluating code at run time (GroovyShell / Eval) is blocked by the EPM Groovy sandbox." },
                new RestrictedPattern { Pattern = @"\bSystem\s*\.\s*(?:getProperty|getenv|setProperty)\b", Message = "System properties and environment variables are blocked by the EPM Groovy sandbox." },
                new RestrictedPattern { Pattern = @"\.metaClass\s*\.\s*\w+\s*=",          Message = "Changing classes through metaClass is blocked by the EPM Groovy sandbox." },
            },
        };

        // ------------------------------------------------------------------ //
        //  Persistence
        // ------------------------------------------------------------------ //

        public static string FilePath => Path.Combine(ConfigManager.SettingsDir, "groovy_validation.json");

        /// <summary>
        /// Loads the user's settings, filling in rules added in newer plugin versions with
        /// their defaults. Writes the defaults out when the file doesn't exist yet so the
        /// user has something to edit.
        /// </summary>
        public static GroovyValidationConfig Load()
        {
            GroovyValidationConfig defaults = Defaults();
            try
            {
                if (File.Exists(FilePath))
                {
                    var cfg = JsonConvert.DeserializeObject<GroovyValidationConfig>(
                        File.ReadAllText(FilePath, Encoding.UTF8));
                    if (cfg != null)
                    {
                        cfg.Rules = new Dictionary<string, string>(
                            cfg.Rules ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in defaults.Rules.Where(kv => !cfg.Rules.ContainsKey(kv.Key)))
                            cfg.Rules[kv.Key] = kv.Value;
                        cfg.KnownGlobals   = cfg.KnownGlobals   ?? defaults.KnownGlobals;
                        cfg.KnownFunctions = cfg.KnownFunctions ?? defaults.KnownFunctions;
                        cfg.Restricted     = cfg.Restricted     ?? defaults.Restricted;
                        cfg.KnownApiMembers = cfg.KnownApiMembers ?? defaults.KnownApiMembers;
                        if (cfg.RestrictedDefaultsVersion < CurrentRestrictedDefaultsVersion)
                        {
                            // Patterns added since this file was written; ones removed later on purpose stay removed
                            var have = new HashSet<string>(cfg.Restricted.Select(r => r.Pattern), StringComparer.Ordinal);
                            int from = cfg.RestrictedDefaultsVersion < 2 ? 8 : defaults.Restricted.Count;   // first 8 = version 1
                            cfg.Restricted.AddRange(defaults.Restricted.Skip(from).Where(r => !have.Contains(r.Pattern)));
                            cfg.RestrictedDefaultsVersion = CurrentRestrictedDefaultsVersion;
                            try { Save(cfg); } catch { }
                        }
                        return cfg;
                    }
                }
                else
                {
                    Save(defaults);
                }
            }
            catch { /* unreadable — validate with the defaults */ }
            return defaults;
        }

        public static void Save(GroovyValidationConfig cfg)
        {
            Directory.CreateDirectory(ConfigManager.SettingsDir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(cfg, Formatting.Indented),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
