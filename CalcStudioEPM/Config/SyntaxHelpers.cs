using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace CalcStudioEPM.Config
{
    /// <summary>
    /// One way of writing a runtime prompt into a Groovy rule, e.g. "rtps.{name}".
    /// {name} is replaced by the variable name. A helper has a default template and
    /// optional per-type overrides keyed by Calc Manager variable type
    /// (member, members, str, str_as_num, integer, percent, date_as_num, …).
    /// </summary>
    public class SyntaxHelper
    {
        public string Name     { get; set; }
        /// <summary>Used for every type without its own entry in <see cref="TypeTemplates"/>.</summary>
        public string Template { get; set; }
        public Dictionary<string, string> TypeTemplates { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Legacy (single Member override) — read from older syntax_helpers.json files and
        /// folded into <see cref="TypeTemplates"/>; never written back.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string MemberTemplate
        {
            get => null;
            set
            {
                if (!string.IsNullOrWhiteSpace(value) && !HasTypeTemplate("member"))
                    TypeTemplates["member"] = value;
            }
        }

        public string TemplateFor(string variableType)
        {
            string specific = TypeTemplates?
                .FirstOrDefault(kv => string.Equals(kv.Key, variableType, StringComparison.OrdinalIgnoreCase) &&
                                      !string.IsNullOrWhiteSpace(kv.Value))
                .Value;
            return specific ?? Template ?? "";
        }

        public string Format(string variableName, string variableType) =>
            TemplateFor(variableType).Replace("{name}", variableName);

        public SyntaxHelper Clone() => new SyntaxHelper
        {
            Name          = Name,
            Template      = Template,
            TypeTemplates = new Dictionary<string, string>(
                TypeTemplates ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
        };

        private bool HasTypeTemplate(string type) =>
            TypeTemplates != null && TypeTemplates.Keys.Any(k => string.Equals(k, type, StringComparison.OrdinalIgnoreCase));

        public override string ToString() => Name;
    }

    /// <summary>
    /// The user-extensible list of Groovy syntax helpers, shared by all connections and
    /// stored in %AppData%\Notepad++\plugins\CalcStudioEPM\syntax_helpers.json.
    /// </summary>
    public static class SyntaxHelpers
    {
        private static string FilePath => Path.Combine(ConfigManager.SettingsDir, "syntax_helpers.json");

        /// <summary>Calc Manager variable types offered in the helper editor.</summary>
        public static readonly string[] KnownTypes =
            { "member", "members", "str", "str_as_num", "integer", "percent", "date_as_num" };

        public static List<SyntaxHelper> Defaults() => new List<SyntaxHelper>
        {
            new SyntaxHelper { Name = "Standard",  Template = "rtps.{name}" },
            new SyntaxHelper
            {
                Name          = "To String",
                Template      = "rtps.{name}.toString()",
                TypeTemplates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["member"] = "rtps.{name}.member.toString()",
                },
            },
        };

        public static List<SyntaxHelper> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var list = JsonConvert.DeserializeObject<List<SyntaxHelper>>(
                        File.ReadAllText(FilePath, Encoding.UTF8));
                    if (list != null && list.Count > 0)
                    {
                        // Json.NET may have replaced the dictionary with a case-sensitive one
                        foreach (var h in list)
                            h.TypeTemplates = new Dictionary<string, string>(
                                h.TypeTemplates ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                        return list;
                    }
                }
            }
            catch { /* unreadable — fall back to defaults */ }
            return Defaults();
        }

        public static void Save(List<SyntaxHelper> helpers)
        {
            Directory.CreateDirectory(ConfigManager.SettingsDir);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(helpers, Formatting.Indented),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
