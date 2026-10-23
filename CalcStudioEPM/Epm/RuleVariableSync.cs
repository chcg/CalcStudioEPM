using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CalcStudioEPM.Epm
{
    /// <summary>
    /// Keeps a rule's runtime-prompt XML (HBRRepo/variables + the rule's
    /// variable_references) in step with edits to its script, at Deploy time.
    ///
    /// Only the difference between the originally pulled script and the edited one is
    /// applied, so everything EPM exported stays untouched unless the user actually
    /// added or removed a reference:
    ///   • a newly referenced variable is added — definition and reference copied from
    ///     the connection's variable dictionary (preferring the rule's own cube);
    ///   • a variable whose last reference was deleted is removed.
    /// What counts as a reference: CalcScript {Name}; Groovy {Name} in the
    /// /*RTPS: … */ header or rtps.Name in the code.
    /// </summary>
    internal static class RuleVariableSync
    {
        /// <returns>True when the XML was changed.</returns>
        public static bool Apply(
            XDocument doc,
            string originalScript,
            string editedScript,
            string cube,
            string ruleName,
            Func<VariableDictionary> loadDictionary,
            IProgress<string> log)
        {
            XElement root  = doc.Root;
            XElement rules = root?.Element("rules");
            XElement rule  = rules?.Elements("rule").FirstOrDefault(r =>
                                 string.Equals((string)r.Attribute("name"), ruleName, StringComparison.OrdinalIgnoreCase))
                             ?? rules?.Elements("rule").FirstOrDefault();
            if (rule == null) return false;

            XElement scriptEl = rule.Element("script");
            bool groovy = ((string)scriptEl?.Attribute("type") ?? "")
                .IndexOf("groovy", StringComparison.OrdinalIgnoreCase) >= 0;

            var before  = VariableDictionary.ReferencedNames(originalScript, groovy);
            var after   = VariableDictionary.ReferencedNames(editedScript,   groovy);
            var added   = after.Where(n => !before.Contains(n)).ToList();
            var removed = before.Where(n => !after.Contains(n)).ToList();
            if (added.Count == 0 && removed.Count == 0) return false;

            string ruleId = (string)rule.Attribute("id");
            XElement refsEl = rule.Element("variable_references");
            bool changed = false;

            // ---- Removed: last reference deleted from the script ----
            foreach (string name in removed)
            {
                XElement reference = refsEl?.Elements("variable_reference").FirstOrDefault(r => Is(r, name));
                if (reference == null) continue;   // was never a prompt (e.g. {X} inside a comment)

                reference.Remove();
                if (!root.Descendants("variable_reference").Any(r => Is(r, name)))
                    root.Element("variables")?.Elements("variable").Where(v => Is(v, name)).Remove();

                log?.Report($"Runtime prompt removed: {name} (no references left in the script)");
                changed = true;
            }

            // ---- Added: newly referenced in the script ----
            VariableDictionary dict = null;
            foreach (string name in added)
            {
                if (refsEl?.Elements("variable_reference").Any(r => Is(r, name)) == true)
                    continue;   // already a prompt of this rule

                dict = dict ?? loadDictionary?.Invoke();
                VariableSample sample = dict?.Find(name)?.SampleFor(cube);
                if (sample == null)
                {
                    log?.Report(
                        $"[WARN] '{name}' is referenced in the script but is not in this connection's " +
                        "variable dictionary, so it was not added as a runtime prompt. Create it in " +
                        "Calculation Manager (or Pull again if it is new) and redeploy.");
                    continue;
                }

                // Definition — reuse one already in the file, else copy the dictionary's
                XElement variablesEl = root.Element("variables");
                if (variablesEl == null)
                {
                    variablesEl = new XElement("variables");
                    root.AddFirst(variablesEl);
                }
                XElement def = variablesEl.Elements("variable").FirstOrDefault(v => Is(v, name));
                if (def == null)
                {
                    def = XElement.Parse(sample.DefinitionXml);
                    def.SetAttributeValue("id", NextId(root));
                    SetPropIfPresent(def, "plantype",  cube);
                    SetPropIfPresent(def, "rule_name", ruleName);
                    SetPropIfPresent(def, "rule",      ruleId);
                    variablesEl.Add(def);
                }

                // Reference — on the rule, before its <script>
                if (refsEl == null)
                {
                    refsEl = new XElement("variable_references");
                    if (scriptEl != null) scriptEl.AddBeforeSelf(refsEl);
                    else                  rule.Add(refsEl);
                }
                XElement reference = XElement.Parse(sample.ReferenceXml);
                reference.SetAttributeValue("id", (string)def.Attribute("id"));
                SetPropIfPresent(reference, "plantype",  cube);
                SetPropIfPresent(reference, "rule_name", ruleName);
                SetPropIfPresent(reference, "rule",      ruleId);
                refsEl.Add(reference);

                log?.Report($"Runtime prompt added: {name}");
                changed = true;
            }

            if (changed && refsEl != null)
            {
                var ordered = refsEl.Elements("variable_reference").ToList();

                // Groovy: Calc Manager keeps the prompts in /*RTPS:*/ header order
                if (groovy)
                {
                    List<string> header = RtpsHeader.Names(editedScript);
                    ordered = ordered
                        .Select((r, i) => (r, i))
                        .OrderBy(x => { int h = header.FindIndex(n => Is(x.r, n)); return h < 0 ? int.MaxValue : h; })
                        .ThenBy(x => x.i)
                        .Select(x => x.r)
                        .ToList();
                    foreach (XElement r in ordered) r.Remove();
                    refsEl.Add(ordered);
                }

                // Prompt order = document order; keep seq contiguous after adds/removes
                int seq = 1;
                foreach (XElement r in ordered)
                    SetPropIfPresent(r, "seq", (seq++).ToString());
            }

            return changed;
        }

        private static bool Is(XElement el, string name) =>
            string.Equals((string)el.Attribute("name"), name, StringComparison.OrdinalIgnoreCase);

        /// <summary>One more than the highest numeric id used by any variable or reference.</summary>
        private static string NextId(XElement root)
        {
            int max = root.Descendants()
                .Where(e => e.Name == "variable" || e.Name == "variable_reference")
                .Select(e => int.TryParse((string)e.Attribute("id"), out int n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max();
            return (max + 1).ToString();
        }

        private static void SetPropIfPresent(XElement el, string prop, string value)
        {
            if (value == null) return;
            XElement p = el.Elements("property").FirstOrDefault(x =>
                string.Equals((string)x.Attribute("name"), prop, StringComparison.OrdinalIgnoreCase));
            if (p != null) p.Value = value;
        }
    }
}
