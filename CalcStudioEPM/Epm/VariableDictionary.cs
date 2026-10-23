using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace CalcStudioEPM.Epm
{
    /// <summary>
    /// One runtime-prompt variable as found across a connection's pulled rules.
    /// </summary>
    public class VariableEntry
    {
        public string Name       { get; set; }
        /// <summary>Calc Manager type: member, members, str, str_as_num, integer, percent, date_as_num…</summary>
        public string Type       { get; set; }
        public string Dimension  { get; set; }
        public string PromptText { get; set; }
        /// <summary>Number of pulled rules / templates / rulesets that reference it.</summary>
        public int    UsedBy     { get; set; }
        public List<string> UsedIn    { get; set; } = new List<string>();
        public List<string> PlanTypes { get; set; } = new List<string>();
        /// <summary>
        /// One verbatim definition + reference per plan type, used as templates when Deploy
        /// adds this variable to a rule that didn't have it before.
        /// </summary>
        public List<VariableSample> Samples { get; set; } = new List<VariableSample>();

        /// <summary>Friendly label for the Type column.</summary>
        [JsonIgnore]
        public string TypeLabel => LabelFor(Type);

        /// <summary>Friendly label for a Calc Manager variable type ("str_as_num" → "Numeric string").</summary>
        public static string LabelFor(string type) =>
            TypeLabels.TryGetValue(type ?? "", out string l) ? l : (type ?? "");

        private static readonly Dictionary<string, string> TypeLabels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["member"]      = "Member",
                ["members"]     = "Members",
                ["str"]         = "String",
                ["str_as_num"]  = "Numeric string",
                ["integer"]     = "Integer",
                ["percent"]     = "Percent",
                ["date_as_num"] = "Date",
            };

        public VariableSample SampleFor(string planType) =>
            Samples.FirstOrDefault(s => string.Equals(s.PlanType, planType, StringComparison.OrdinalIgnoreCase))
            ?? Samples.FirstOrDefault();
    }

    public class VariableSample
    {
        public string PlanType      { get; set; }
        /// <summary>&lt;variable&gt; element from HBRRepo/variables.</summary>
        public string DefinitionXml { get; set; }
        /// <summary>&lt;variable_reference&gt; element from the rule.</summary>
        public string ReferenceXml  { get; set; }
    }

    /// <summary>
    /// All runtime-prompt variables of one connection, compiled by Pull from the
    /// &lt;variables&gt; / &lt;variable_references&gt; sections embedded in every rule.
    /// Stored at <c>{LocalFolderPath}\CalcManager\variables.json</c>.
    /// </summary>
    public class VariableDictionary
    {
        public DateTime GeneratedUtc { get; set; }
        public List<VariableEntry> Variables { get; set; } = new List<VariableEntry>();

        public VariableEntry Find(string name) =>
            Variables.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ //
        //  Persistence
        // ------------------------------------------------------------------ //

        public static string GetPath(string localFolderPath) =>
            Path.Combine(localFolderPath, PathConstants.CalcManagerDirName, PathConstants.VariablesFileName);

        public void Save(string localFolderPath) =>
            File.WriteAllText(GetPath(localFolderPath),
                JsonConvert.SerializeObject(this, Formatting.Indented),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        /// <summary>
        /// Loads the dictionary for a connection. When it is missing (pulled before this
        /// feature existed) but the extracted export is still on disk, it is rebuilt from
        /// that export and saved. Returns null when there is nothing to build it from.
        /// </summary>
        public static VariableDictionary LoadOrRebuild(string localFolderPath)
        {
            if (string.IsNullOrWhiteSpace(localFolderPath)) return null;

            string path = GetPath(localFolderPath);
            if (File.Exists(path))
            {
                try
                {
                    var d = JsonConvert.DeserializeObject<VariableDictionary>(
                        File.ReadAllText(path, Encoding.UTF8));
                    if (d?.Variables != null) return d;
                }
                catch { /* corrupt — rebuild below */ }
            }

            string resourceDir = Path.Combine(localFolderPath, PathConstants.CalcManagerDirName,
                                              "CALC-Calculation Manager", "resource");
            if (!Directory.Exists(resourceDir)) return null;

            var builder = new VariableDictionaryBuilder();
            foreach (string file in Directory.EnumerateFiles(resourceDir, "*", SearchOption.AllDirectories))
                builder.Add(file, PathConstants.FromFileName(Path.GetFileName(file)));

            VariableDictionary built = builder.Build();
            try { built.Save(localFolderPath); } catch { /* read-only folder — still usable */ }
            return built;
        }

        // ------------------------------------------------------------------ //
        //  Script references
        // ------------------------------------------------------------------ //

        private static readonly Regex RtpsHeader   = new Regex(@"/\*\s*RTPS:(.*?)\*/", RegexOptions.Singleline);
        private static readonly Regex BraceToken   = new Regex(@"\{(\w+)\}");
        private static readonly Regex GroovyRtpUse = new Regex(@"\brtps\s*\.\s*(\w+)");

        /// <summary>
        /// Names of the runtime prompts a script refers to.
        /// CalcScript: every {Name} token.
        /// Groovy: every {Name} in the /*RTPS: … */ header plus every rtps.Name in the code.
        /// </summary>
        public static HashSet<string> ReferencedNames(string script, bool groovy)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(script)) return names;

            if (!groovy)
            {
                foreach (Match m in BraceToken.Matches(script)) names.Add(m.Groups[1].Value);
                return names;
            }

            foreach (Match h in RtpsHeader.Matches(script))
                foreach (Match m in BraceToken.Matches(h.Groups[1].Value)) names.Add(m.Groups[1].Value);
            foreach (Match m in GroovyRtpUse.Matches(script)) names.Add(m.Groups[1].Value);
            return names;
        }
    }

    /// <summary>
    /// Accumulates variables while Pull processes each pulled calculation artifact.
    /// </summary>
    public class VariableDictionaryBuilder
    {
        private class Acc
        {
            public string Name;
            public readonly Dictionary<string, int> TypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, (string Dim, string Prompt)> ByType =
                new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            public readonly SortedSet<string> UsedIn    = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly SortedSet<string> PlanTypes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, VariableSample> Samples =
                new Dictionary<string, VariableSample>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Dictionary<string, Acc> _vars = new Dictionary<string, Acc>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Reads one pulled artifact file. Non-XML files are ignored.</summary>
        public void Add(string artifactFile, string artifactName)
        {
            XDocument doc;
            try { doc = XDocument.Load(artifactFile); }
            catch { return; }
            Add(doc, artifactName);
        }

        public void Add(XDocument doc, string artifactName)
        {
            XElement root = doc.Root;
            if (root == null) return;

            var definitions = root.Element("variables")?.Elements("variable").ToList()
                              ?? new List<XElement>();
            var references  = root.Descendants("variable_reference").ToList();

            foreach (XElement def in definitions)
            {
                string name = (string)def.Attribute("name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                Acc acc = GetAcc(name);
                string type = (string)def.Attribute("type") ?? "";
                acc.TypeCounts[type] = acc.TypeCounts.TryGetValue(type, out int n) ? n + 1 : 1;
                if (!acc.ByType.ContainsKey(type))
                    acc.ByType[type] = (Prop(def, "dimension"), Prop(def, "prompt_text"));

                string planType = Prop(def, "plantype") ?? "";
                if (planType.Length > 0) acc.PlanTypes.Add(planType);

                if (!acc.Samples.ContainsKey(planType))
                {
                    XElement reference = references.FirstOrDefault(r =>
                        string.Equals((string)r.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));
                    if (reference != null)
                        acc.Samples[planType] = new VariableSample
                        {
                            PlanType      = planType,
                            DefinitionXml = def.ToString(SaveOptions.DisableFormatting),
                            ReferenceXml  = reference.ToString(SaveOptions.DisableFormatting),
                        };
                }
            }

            foreach (XElement reference in references)
            {
                string name = (string)reference.Attribute("name");
                if (!string.IsNullOrWhiteSpace(name))
                    GetAcc(name).UsedIn.Add(artifactName);
            }
        }

        public int Count => _vars.Count;

        public VariableDictionary Build()
        {
            var dict = new VariableDictionary { GeneratedUtc = DateTime.UtcNow };
            foreach (Acc a in _vars.Values)
            {
                string type = a.TypeCounts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? "";
                a.ByType.TryGetValue(type, out var info);
                dict.Variables.Add(new VariableEntry
                {
                    Name       = a.Name,
                    Type       = type,
                    Dimension  = info.Dim,
                    PromptText = info.Prompt,
                    UsedBy     = a.UsedIn.Count,
                    UsedIn     = a.UsedIn.ToList(),
                    PlanTypes  = a.PlanTypes.ToList(),
                    Samples    = a.Samples.Values.ToList(),
                });
            }
            dict.Variables.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
            return dict;
        }

        private Acc GetAcc(string name)
        {
            if (!_vars.TryGetValue(name, out Acc acc))
                _vars[name] = acc = new Acc { Name = name };
            return acc;
        }

        private static string Prop(XElement el, string propName) =>
            el.Elements("property")
              .FirstOrDefault(p => string.Equals((string)p.Attribute("name"), propName, StringComparison.OrdinalIgnoreCase))
              ?.Value;
    }
}
