using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CalcStudioEPM.Epm
{
    /// <summary>One runtime prompt of a rule, as EPM Cloud will ask for it.</summary>
    public class RuntimePrompt
    {
        public string Name;
        public string Type;
        public string Dimension;
        public string PromptText;
        public bool   Hidden;
        /// <summary>Value preset on the rule (override value, variable default or validation value).</summary>
        public string DefaultValue;

        public string TypeLabel => VariableEntry.LabelFor(Type);
    }

    /// <summary>
    /// Reads a rule's runtime prompts from its CALC rule XML — the rule's
    /// variable_references in seq order, joined with the HBRRepo/variables definitions.
    /// Reading the deployed XML (not the script being edited) means a prompt added but
    /// not deployed is not asked for, and one removed but not deployed still is.
    /// </summary>
    public static class RulePrompts
    {
        public static List<RuntimePrompt> FromRuleXml(string ruleXml, string ruleName)
        {
            var prompts = new List<RuntimePrompt>();
            if (string.IsNullOrWhiteSpace(ruleXml)) return prompts;

            XDocument doc;
            try { doc = XDocument.Parse(ruleXml); }
            catch { return prompts; }

            XElement root  = doc.Root;
            XElement rules = root?.Element("rules");
            XElement rule  = rules?.Elements("rule").FirstOrDefault(r =>
                                 string.Equals((string)r.Attribute("name"), ruleName, StringComparison.OrdinalIgnoreCase))
                             ?? rules?.Elements("rule").FirstOrDefault();
            if (rule == null) return prompts;

            var references = rule.Element("variable_references")?.Elements("variable_reference")
                                 .OrderBy(r => int.TryParse(Prop(r, "seq"), out int s) ? s : int.MaxValue)
                                 .ToList()
                             ?? new List<XElement>();

            foreach (XElement r in references)
            {
                string name = (string)r.Attribute("name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                XElement def = root.Element("variables")?.Elements("variable").FirstOrDefault(v =>
                    string.Equals((string)v.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));

                prompts.Add(new RuntimePrompt
                {
                    Name         = name,
                    Type         = (string)def?.Attribute("type") ?? "",
                    Dimension    = def == null ? null : Prop(def, "dimension"),
                    PromptText   = (def == null ? null : Prop(def, "prompt_text")) ?? name,
                    Hidden       = string.Equals(Prop(r, "hidden"), "true", StringComparison.OrdinalIgnoreCase),
                    DefaultValue = FirstNonEmpty(Prop(r, "value"), def?.Element("value")?.Value, Prop(r, "validation_value")),
                });
            }
            return prompts;
        }

        private static string Prop(XElement el, string name) =>
            el.Elements("property")
              .FirstOrDefault(p => string.Equals((string)p.Attribute("name"), name, StringComparison.OrdinalIgnoreCase))
              ?.Value;

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }
}
