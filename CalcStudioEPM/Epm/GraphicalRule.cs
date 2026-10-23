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
    /// A graphical Calculation Manager rule — a flow of steps built in the designer, not one
    /// script — as a folder of files that can be read, edited and written back:
    ///
    ///   Rules\{Rule}\
    ///      00 - Rule.ini                the flow (comments) and the values of its global
    ///                                   range, FIXes (loops), member blocks and IF tests
    ///      01 - {caption}.csc           a script step, exactly as Calculation Manager holds it
    ///      02 - {caption}.formula.csc   a formula step: one  "Dest" = expression;  per row
    ///      01.01.01 1-THEN 01 - ….csc   steps inside FIXes, member blocks and IF branches
    ///      .flow.json                   file → component id / step path (bookkeeping)
    ///
    /// Steps are numbered in flow order. The order and kind of steps, and template steps,
    /// are changed in Calculation Manager only. A rule is graphical when its &lt;rule&gt; holds
    /// &lt;statement&gt; elements instead of a &lt;script&gt;.
    /// </summary>
    internal static class GraphicalRule
    {
        public const string ManifestName = ".flow.json";
        public const string RuleIniName  = "00 - Rule.ini";
        private const string FormulaExt = ".formula.csc";

        // ------------------------------------------------------------------ //
        //  Detection
        // ------------------------------------------------------------------ //

        public static XElement RuleElement(XDocument doc) => doc.Root?.Element("rules")?.Element("rule");

        public static bool IsGraphical(XDocument doc) => RuleElement(doc)?.Element("statement") != null;

        /// <summary>The graphical rule folder holding <paramref name="path"/> (a step file or
        /// 00 - Rule.ini), or null when the file isn't in one.</summary>
        public static string RuleFolderOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string dir = Path.GetDirectoryName(path);
            return dir != null && File.Exists(Path.Combine(dir, ManifestName)) ? dir : null;
        }

        // ------------------------------------------------------------------ //
        //  Manifest
        // ------------------------------------------------------------------ //

        private sealed class Manifest
        {
            public string Rule { get; set; }
            public int    Version { get; set; } = 1;
            public Dictionary<string, FileEntry> Files { get; set; } = new Dictionary<string, FileEntry>();
            public Dictionary<string, SectionEntry> Sections { get; set; } = new Dictionary<string, SectionEntry>();
        }

        private sealed class FileEntry
        {
            public string Step { get; set; }
            public string Type { get; set; }          // script | formula
            public string ComponentId { get; set; }
            public bool   Editable { get; set; } = true;
        }

        private sealed class SectionEntry
        {
            public string Kind { get; set; }          // globalrange | loop | memberblock | if
            public string Path { get; set; }          // statement path, e.g. "1/1/then/2"
        }

        // ------------------------------------------------------------------ //
        //  Pull: XML → folder
        // ------------------------------------------------------------------ //

        /// <summary>Writes the rule's folder; returns the files written (00 - Rule.ini included).</summary>
        /// <param name="cubeDimensions">The rule's cube's dimensions, in order (from the downloaded
        /// metadata): every FIX and the global range list them all, empty when not used. Null =
        /// only the dimensions the rule uses.</param>
        public static int Extract(XDocument doc, string ruleName, string folder, IProgress<string> log,
                                  IList<string> cubeDimensions = null)
        {
            XElement rule = RuleElement(doc);
            if (folder.Length > 230)
                throw new EpmException($"the folder path is too long for Windows ({folder.Length} characters): use a shorter local folder.");
            Directory.CreateDirectory(folder);
            var w = new Extractor { Folder = folder, RuleName = ruleName, Log = log, CubeDimensions = cubeDimensions };
            w.Manifest.Rule = ruleName;

            XElement global = rule.Element("globalrange");
            if (global != null)
            {
                var dims = w.AllDimensions(SliceOf(global));
                w.Flow.Add($";  Global range  FIX ({string.Join(", ", dims.Where(d => d.Value.Length > 0).Select(d => $"{d.Key} = {d.Value}"))})");
                w.Sections.Add(("Global range", dims));
                w.Manifest.Sections["Global range"] = new SectionEntry { Kind = "globalrange", Path = "globalrange" };
            }
            w.Walk(rule.Elements("statement"), "", "", 0);

            var ini = new List<string>
            {
                $"; {ruleName} — graphical rule · {Prop(rule, "application")} / {Prop(rule, "plantype")}",
                "; The steps and their order are changed in Calculation Manager. Here you can change",
                "; the values in the [sections] below; each step's code is in its numbered file.",
                "; Under each FIX, one line per dimension of the cube: Dimension = members (empty = not fixed).",
                "; Deploy (from any of these files) sends the rule to Calculation Manager only:",
                "; deploy it to Planning from Calculation Manager.",
                ";",
                "; Flow",
            };
            ini.AddRange(w.Flow);
            ini.Add("");
            foreach (var (title, values) in w.Sections)
            {
                ini.Add($"[{title}]");
                int width = values.Count == 0 ? 0 : values.Max(v => v.Key.Length);
                ini.AddRange(values.Select(v => $"{v.Key.PadRight(width)} = {v.Value}"));
                ini.Add("");
            }
            WriteText(Path.Combine(folder, RuleIniName), string.Join("\n", ini));
            WriteText(Path.Combine(folder, ManifestName), JsonConvert.SerializeObject(w.Manifest, Formatting.Indented));
            foreach (string ro in w.ReadOnlyFiles)
                try { File.SetAttributes(ro, File.GetAttributes(ro) | FileAttributes.ReadOnly); } catch { }
            return w.Manifest.Files.Count + 1;
        }

        private sealed class Extractor
        {
            public string Folder, RuleName;
            public IProgress<string> Log;
            public IList<string> CubeDimensions;

            /// <summary>The cube's dimensions in order with the rule's selections ("" = none),
            /// then any dimension of the rule the cube list doesn't have.</summary>
            public List<KeyValuePair<string, string>> AllDimensions(List<KeyValuePair<string, string>> used)
            {
                if (CubeDimensions == null || CubeDimensions.Count == 0) return used;
                var byName = used.GroupBy(u => u.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
                var all = CubeDimensions.Select(d => Kv(d, byName.TryGetValue(d, out string v) ? v : "")).ToList();
                all.AddRange(used.Where(u => !CubeDimensions.Contains(u.Key, StringComparer.OrdinalIgnoreCase)));
                return all;
            }
            public readonly Manifest Manifest = new Manifest();
            public readonly List<string> Flow = new List<string>();
            public readonly List<(string Title, List<KeyValuePair<string, string>> Values)> Sections =
                new List<(string, List<KeyValuePair<string, string>>)>();
            public readonly List<string> ReadOnlyFiles = new List<string>();

            /// <summary>"{num} - {label}{ext}", the label shortened to keep the full path under
            /// Windows' 260-character limit (just "{num}{ext}" when even that is too long).</summary>
            private string FileName(string num, string label, string ext)
            {
                int room = 259 - Folder.Length - 1 - num.Length - 3 - ext.Length;
                string safe = SafeName(label);
                if (room < 8) return num + ext;
                return $"{num} - {(safe.Length > room ? safe.Substring(0, room).TrimEnd() : safe)}{ext}";
            }

            public void Walk(IEnumerable<XElement> statements, string prefix, string path, int depth)
            {
                foreach (XElement st in statements)
                {
                    string seq  = (string)st.Attribute("seq") ?? "0";
                    // seq comes from the rule XML and becomes part of a file name
                    string num  = prefix + (int.TryParse(seq, out int n) ? n.ToString("00") : SafeName(seq));
                    string here = path + seq;
                    string pad  = new string(' ', depth * 2);
                    string type = (string)st.Attribute("type");
                    string kind = type == "component" ? (string)st.Attribute("kind") : type;

                    if (kind == "script" || kind == "formula")
                    {
                        XElement comp = st.Element("component");
                        string caption = Prop(comp, "caption");
                        string label = string.IsNullOrWhiteSpace(caption) ? (kind == "script" ? "Script" : "Formula") : caption;
                        string file = FileName(num, label, kind == "script" ? ".csc" : FormulaExt);
                        var entry = new FileEntry { Step = num, Type = kind, ComponentId = (string)comp.Attribute("id") };
                        if (kind == "script")
                            WriteText(Path.Combine(Folder, file), comp.Element("script")?.Value ?? "");
                        else
                        {
                            FormulaModel model = FormulaModel.FromXml(comp);
                            string text = model.Render(RuleName);
                            entry.Editable = FormulaModel.RoundTrips(comp, text);
                            if (!entry.Editable)
                            {
                                text = "/* READ ONLY: this formula step has settings this file format can't hold, so it is edited\n" +
                                       "   in Calculation Manager only. */\n" + text;
                                ReadOnlyFiles.Add(Path.Combine(Folder, file));
                                Log?.Report($"  Graphical rule '{RuleName}': formula step {num} is read only (settings this format can't hold).");
                            }
                            WriteText(Path.Combine(Folder, file), text);
                        }
                        Manifest.Files[file] = entry;
                        Flow.Add($";  {pad}{num}  {(kind == "script" ? "Script" : "Formula")}" +
                                 (string.IsNullOrWhiteSpace(caption) ? "" : $" «{caption}»") + $"  →  {file}" +
                                 (entry.Editable ? "" : "  (read only)"));
                    }
                    else if (kind == "loop")
                    {
                        XElement loop = st.Element("loop");
                        var used = SliceOf(loop?.Element("test")?.Element("expression")?.Element("source"));
                        string caption = Prop(loop, "caption");
                        Flow.Add($";  {pad}{num}  FIX" + (string.IsNullOrWhiteSpace(caption) ? "" : $" «{caption}»") +
                                 $"  ({string.Join(", ", used.Where(d => d.Value.Length > 0).Select(d => d.Key))})");
                        AddSection($"{num} FIX", AllDimensions(used), "loop", here);
                        Walk(loop?.Elements("statement") ?? Enumerable.Empty<XElement>(), num + ".", here + "/", depth + 1);
                    }
                    else if (kind == "memberblock")
                    {
                        XElement mb = st.Element("memberblock");
                        string member = Prop(mb, "memberblock");
                        Flow.Add($";  {pad}{num}  Member block  {member.Trim()}( … )");
                        AddSection($"{num} Member block", new List<KeyValuePair<string, string>> { Kv("Member", member.Trim()) }, "memberblock", here);
                        Walk(mb?.Elements("statement") ?? Enumerable.Empty<XElement>(), num + ".", here + "/", depth + 1);
                    }
                    else if (kind == "if")
                    {
                        XElement iff = st.Element("if");
                        string test = UserInput(iff?.Element("test")) ?? "";
                        Flow.Add($";  {pad}{num}  IF ({test.Trim()})");
                        AddSection($"{num} IF", new List<KeyValuePair<string, string>> { Kv("Test", test.Trim()) }, "if", here);
                        foreach (string branch in new[] { "then", "else" })
                        {
                            XElement br = iff?.Element(branch);
                            if (br == null) continue;
                            Flow.Add($";  {pad}    {branch.ToUpperInvariant()}");
                            // "1-THEN" sorts before "2-ELSE" in the folder
                            Walk(br.Elements("statement"), $"{num} {(branch == "then" ? "1-THEN" : "2-ELSE")} ", here + "/" + branch + "/", depth + 2);
                        }
                        Flow.Add($";  {pad}    ENDIF");
                    }
                    else if (type == "template")
                    {
                        Flow.Add($";  {pad}{num}  Template «{(string)st.Element("template_ref")?.Attribute("name")}»  (edited in Calculation Manager)");
                    }
                    else
                    {
                        Flow.Add($";  {pad}{num}  {kind ?? type} (edited in Calculation Manager)");
                    }
                }
            }

            private void AddSection(string title, List<KeyValuePair<string, string>> values, string kind, string path)
            {
                Sections.Add((title, values));
                Manifest.Sections[title] = new SectionEntry { Kind = kind, Path = path };
            }
        }

        // ------------------------------------------------------------------ //
        //  Deploy: folder → XML
        // ------------------------------------------------------------------ //

        /// <summary>The result of applying a rule folder to its rule XML.</summary>
        public sealed class Applied
        {
            public XDocument Doc;
            /// <summary>What changed, one line each ("02 - Formula.formula.csc", "00 - Rule.ini [01 Loop]").</summary>
            public List<string> Changes = new List<string>();
            /// <summary>Files in the folder that aren't steps of the rule (ignored).</summary>
            public List<string> Ignored = new List<string>();
            /// <summary>All script and expression text before / after — for runtime prompt sync.</summary>
            public string TextBefore, TextAfter;
        }

        /// <summary>
        /// Applies the folder's files to <paramref name="ruleXml"/> (the deployed version):
        /// each script / formula file to its component (by id), 00 - Rule.ini to the global
        /// range, loops, member blocks and IF tests. Unchanged parts stay byte for byte.
        /// Throws <see cref="EpmException"/> listing every problem found.
        /// </summary>
        public static Applied Apply(string ruleXml, string folder)
        {
            XDocument doc = XDocument.Parse(ruleXml);
            XElement rule = RuleElement(doc) ?? throw new EpmException("The pulled rule XML has no <rule> element.");
            if (!IsGraphical(doc)) throw new EpmException("The pulled rule is not a graphical rule any more: pull again.");

            Manifest manifest;
            try { manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(Path.Combine(folder, ManifestName), Encoding.UTF8)); }
            catch (Exception ex) { throw new EpmException($"Could not read {ManifestName} in {folder}: {ex.Message}. Pull again."); }

            var result = new Applied { Doc = doc, TextBefore = AllText(rule) };
            var problems = new List<string>();

            // ---- Script and formula steps, by component id ----
            foreach (var kv in manifest.Files)
            {
                string file = Path.Combine(folder, kv.Key);
                XElement comp = rule.Descendants("component").FirstOrDefault(c => (string)c.Attribute("id") == kv.Value.ComponentId);
                if (comp == null) { problems.Add($"{kv.Key}: its step (component {kv.Value.ComponentId}) is no longer in the rule — pull again."); continue; }
                if (!File.Exists(file)) { problems.Add($"{kv.Key} is missing: every step needs its file (restore it, or pull again)."); continue; }
                string text = File.ReadAllText(file, Encoding.UTF8);

                if (kv.Value.Type == "script")
                {
                    XElement script = comp.Element("script");
                    if (script == null) { problems.Add($"{kv.Key}: the step has no script in the pulled rule."); continue; }
                    if (Same(text, script.Value)) continue;
                    bool cdata = script.Nodes().OfType<XCData>().Any();
                    script.Nodes().Remove();
                    string lf = Lf(text);
                    script.Add(cdata ? (XNode)new XCData(lf) : new XText(lf));
                    result.Changes.Add(kv.Key);
                }
                else
                {
                    FormulaModel current = FormulaModel.FromXml(comp);
                    FormulaModel edited;
                    try { edited = FormulaModel.Parse(text); }
                    catch (FormatException ex) { problems.Add($"{kv.Key}: {ex.Message}"); continue; }
                    if (edited.SameAs(current)) continue;
                    if (!kv.Value.Editable) { problems.Add($"{kv.Key} is read only (edit this formula step in Calculation Manager)."); continue; }
                    edited.ToXml(comp);
                    result.Changes.Add(kv.Key);
                }
            }

            // ---- 00 - Rule.ini ----
            string iniPath = Path.Combine(folder, RuleIniName);
            if (manifest.Sections.Count > 0)
            {
                if (!File.Exists(iniPath)) problems.Add($"{RuleIniName} is missing (restore it, or pull again).");
                else
                {
                    Dictionary<string, List<KeyValuePair<string, string>>> ini;
                    try { ini = ParseIni(File.ReadAllText(iniPath, Encoding.UTF8)); }
                    catch (FormatException ex) { problems.Add($"{RuleIniName}: {ex.Message}"); ini = null; }
                    if (ini != null)
                    {
                        foreach (string extra in ini.Keys.Where(k => !manifest.Sections.ContainsKey(k)))
                            problems.Add($"{RuleIniName}: [{extra}] is not a section of this rule (steps are added in Calculation Manager).");
                        foreach (var sec in manifest.Sections)
                        {
                            if (!ini.TryGetValue(sec.Key, out var values)) { problems.Add($"{RuleIniName}: section [{sec.Key}] is missing."); continue; }
                            try { if (ApplySection(rule, sec.Value, values)) result.Changes.Add($"{RuleIniName} [{sec.Key}]"); }
                            catch (FormatException ex) { problems.Add($"{RuleIniName} [{sec.Key}]: {ex.Message}"); }
                        }
                    }
                }
            }

            foreach (string f in Directory.EnumerateFiles(folder).Select(Path.GetFileName))
                if (!manifest.Files.ContainsKey(f) && f != RuleIniName && f != ManifestName)
                    result.Ignored.Add(f);

            if (problems.Count > 0)
                throw new EpmException("The graphical rule can't be deployed:\n\n• " + string.Join("\n• ", problems));
            result.TextAfter = AllText(rule);
            return result;
        }

        /// <summary>The step files to validate (script and formula steps).</summary>
        public static IEnumerable<string> StepFiles(string folder)
        {
            Manifest m;
            try { m = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(Path.Combine(folder, ManifestName), Encoding.UTF8)); }
            catch { yield break; }
            foreach (string f in m.Files.Keys.OrderBy(k => k, StringComparer.Ordinal))
                yield return Path.Combine(folder, f);
        }

        private static bool ApplySection(XElement rule, SectionEntry sec, List<KeyValuePair<string, string>> values)
        {
            Dictionary<string, string> One(params string[] keys)
            {
                var d = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
                foreach (string k in d.Keys) if (!keys.Contains(k, StringComparer.OrdinalIgnoreCase)) throw new FormatException($"unknown key '{k}' (expected {string.Join(", ", keys)}).");
                foreach (string k in keys) if (!d.ContainsKey(k)) throw new FormatException($"'{k} = …' is missing.");
                return d;
            }

            if (sec.Kind == "globalrange")
                return ApplySlice(rule.Element("globalrange"), values);

            XElement st = Resolve(rule, sec.Path) ?? throw new FormatException("its step is no longer in the rule — pull again.");
            switch (sec.Kind)
            {
                case "loop":
                    XElement source = st.Element("loop")?.Element("test")?.Element("expression")?.Element("source");
                    return ApplySlice(source, values);
                case "memberblock":
                {
                    string member = One("Member")["Member"];
                    XElement p = st.Element("memberblock")?.Elements("property").FirstOrDefault(x => (string)x.Attribute("name") == "memberblock");
                    if (p == null || p.Value.Trim() == member.Trim()) return false;
                    p.Value = member.Trim();
                    return true;
                }
                case "if":
                {
                    string test = One("Test")["Test"];
                    XElement ui = st.Element("if")?.Element("test")?.Element("expression")?.Element("user_inp");
                    if (ui == null || ui.Value.Trim() == test.Trim()) return false;
                    ui.Value = test.Trim();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Sets a FIX's / the global range's dimensions from "Dimension = members" lines (in
        /// order; empty = not fixed); false when the selections are unchanged. A dimension left
        /// empty that the rule had without members keeps its (empty) element.
        /// </summary>
        private static bool ApplySlice(XElement holder, List<KeyValuePair<string, string>> values)
        {
            XElement slice = DimensionsOf(holder);
            if (slice == null) throw new FormatException("the pulled rule has no member selection here — pull again.");
            var dupes = values.GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0) throw new FormatException($"{string.Join(", ", dupes)} appears more than once.");

            Dictionary<string, string> Selected(IEnumerable<KeyValuePair<string, string>> kv) =>
                kv.Where(x => x.Value.Trim().Length > 0).ToDictionary(x => x.Key, x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase);
            var before = Selected(SliceOf(holder));
            var after  = Selected(values);
            if (before.Count == after.Count && before.All(x => after.TryGetValue(x.Key, out string v) && v == x.Value))
                return false;

            var existing = slice.Elements("dimension").ToDictionary(d => (string)d.Attribute("name"), StringComparer.OrdinalIgnoreCase);
            var rebuilt = new List<XElement>();
            foreach (var v in values)
            {
                string members = v.Value.Trim();
                existing.TryGetValue(v.Key, out XElement d);
                if (members.Length == 0)
                {
                    if (d == null) continue;                       // not fixed, and never was
                    d.Element("expression")?.Remove();
                    rebuilt.Add(d);
                    continue;
                }
                if (d == null) d = new XElement("dimension", new XAttribute("name", v.Key));
                XElement ui = d.Element("expression")?.Element("user_inp");
                if (ui != null) { if (ui.Value.Trim() != members) ui.Value = members; }
                else { d.Element("expression")?.Remove(); d.Add(new XElement("expression", new XElement("user_inp", members))); }
                rebuilt.Add(d);
            }
            int seq = 1;
            foreach (XElement d in rebuilt) d.SetAttributeValue("seq", seq++);
            slice.Elements("dimension").Remove();
            slice.Add(rebuilt);
            return true;
        }

        /// <summary>The statement at a path like "1/1/then/2" (seq numbers and IF branches).</summary>
        private static XElement Resolve(XElement rule, string path)
        {
            XElement container = rule, statement = null;
            foreach (string part in path.Split('/'))
            {
                if (part == "then" || part == "else")
                {
                    container = statement?.Element("if")?.Element(part);
                    if (container == null) return null;
                    continue;
                }
                statement = container.Elements("statement").FirstOrDefault(s => (string)s.Attribute("seq") == part);
                if (statement == null) return null;
                string type = (string)statement.Attribute("type");
                container = statement.Element(type) ?? statement;   // loop / memberblock hold their steps
            }
            return statement;
        }

        private static Dictionary<string, List<KeyValuePair<string, string>>> ParseIni(string text)
        {
            var result = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase);
            List<KeyValuePair<string, string>> current = null;
            string[] lines = Lf(text).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.StartsWith("["))
                {
                    if (!line.EndsWith("]")) throw new FormatException($"line {i + 1}: a section header must end with ']'.");
                    string name = line.Substring(1, line.Length - 2).Trim();
                    if (result.ContainsKey(name)) throw new FormatException($"line {i + 1}: [{name}] appears twice.");
                    result[name] = current = new List<KeyValuePair<string, string>>();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) throw new FormatException($"line {i + 1}: expected 'Name = value'.");
                if (current == null) throw new FormatException($"line {i + 1}: a value before any [section].");
                current.Add(Kv(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
            }
            return result;
        }

        // ------------------------------------------------------------------ //
        //  Formula steps: rows of  "Destination" = expression;
        // ------------------------------------------------------------------ //

        internal sealed class FormulaModel
        {
            public string Caption = "", Description = "", Comment = "";
            public List<Block> Blocks = new List<Block>();

            internal sealed class Block
            {
                public string Label = "", Member = "", Condition = "", ConditionComment = "", Test = "";
                public List<Row> Rows = new List<Row>();
            }

            internal sealed class Row
            {
                public string Comment = "", Destination = "";
                public string Expression;   // null = member formula ("Member";)
            }

            public static FormulaModel FromXml(XElement comp)
            {
                var m = new FormulaModel { Caption = Prop(comp, "caption"), Description = Prop(comp, "description"), Comment = Prop(comp, "comment") };
                foreach (XElement cb in comp.Element("formulae")?.Elements("conditionblock") ?? Enumerable.Empty<XElement>())
                {
                    var b = new Block
                    {
                        Label = Prop(cb, "commentblock"), Member = Prop(cb, "memberblock").Trim(), Condition = Prop(cb, "condition").Trim(),
                        ConditionComment = Prop(cb, "comment"), Test = (UserInput(cb.Element("test")) ?? "").Trim(),
                    };
                    foreach (XElement f in cb.Elements("formula"))
                        b.Rows.Add(new Row
                        {
                            Comment = Prop(f, "comment"),
                            Destination = (f.Element("destination")?.Element("user_inp")?.Value ?? "").Trim(),
                            Expression = f.Element("expression") == null ? null : (f.Element("expression").Element("user_inp")?.Value ?? "").Trim(),
                        });
                    m.Blocks.Add(b);
                }
                return m;
            }

            public string Render(string ruleName)
            {
                var o = new List<string>
                {
                    $"/* Formula step \"{Esc(Caption)}\" of the graphical rule {Esc(ruleName)}.",
                    "   One  \"Destination\" = expression;  per row, or  \"Member\";  to calculate a member's formula.",
                    "   A comment just above a row (or an IF) is its comment. Keep the lines starting /*# : they",
                    "   map this file back to the step. Inside comments, *\\/ stands for the characters * and /. */",
                };
                if (Caption.Length > 0)     o.Add($"/*# caption: {Esc(Caption)} */");
                if (Description.Length > 0) o.Add($"/*# description: {Esc(OneLine(Description))} */");
                if (Comment.Length > 0)     o.Add($"/*# comment: {Esc(OneLine(Comment))} */");
                for (int i = 0; i < Blocks.Count; i++)
                {
                    Block b = Blocks[i];
                    o.Add("");
                    o.Add($"/*# block {i + 1}" + (b.Label.Length > 0 ? ": " + Esc(OneLine(b.Label)) : "") + " */");
                    string ind = "";
                    if (b.Member.Length > 0) { o.Add(b.Member + " ("); ind = "    "; }
                    if (b.Condition.Length > 0)
                    {
                        if (b.ConditionComment.Length > 0) o.AddRange(CommentLines(b.ConditionComment, ind));
                        o.Add(ind + (string.Equals(b.Condition, "ELSE", StringComparison.OrdinalIgnoreCase) ? b.Condition : $"{b.Condition} ({b.Test})"));
                        ind += "    ";
                    }
                    foreach (Row r in b.Rows)
                    {
                        if (r.Comment.Length > 0) o.AddRange(CommentLines(r.Comment, ind));
                        o.Add(ind + (r.Expression == null ? $"{r.Destination};" : $"{r.Destination} = {r.Expression};"));
                    }
                    bool chainContinues = i + 1 < Blocks.Count &&
                        (string.Equals(Blocks[i + 1].Condition, "ELSEIF", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Blocks[i + 1].Condition, "ELSE", StringComparison.OrdinalIgnoreCase));
                    if (b.Condition.Length > 0 && !chainContinues) o.Add(ind.Substring(4) + "ENDIF");
                    if (b.Member.Length > 0) o.Add(")");
                }
                return string.Join("\n", o) + "\n";
            }

            /// <summary>"/* line 1", "   line 2", …, the last one ending " */" — at the given indent.</summary>
            private static IEnumerable<string> CommentLines(string comment, string indent)
            {
                string[] lines = Lf(comment).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                    yield return indent + (i == 0 ? "/* " : "   ") + Esc(lines[i]) + (i == lines.Length - 1 ? " */" : "");
            }

            /// <summary>Text inside a /* comment */: "*/" would end it, so it is written "*\/".</summary>
            private static string Esc(string s) => (s ?? "").Replace("*/", "*\\/");
            private static string Unesc(string s) => (s ?? "").Replace("*\\/", "*/");

            /// <summary>Reads a formula file back; throws FormatException("line N: …").</summary>
            public static FormulaModel Parse(string text)
            {
                var m = new FormulaModel();
                string[] lines = Lf(text).Split('\n');
                Block b = null;
                string pending = null;          // the comment waiting for its row / IF
                int pendingLine = 0;
                bool seenContent = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string raw = lines[i], line = raw.Trim();
                    if (line.Length == 0) continue;

                    if (line.StartsWith("/*"))
                    {
                        // The whole comment, possibly over several lines
                        int start = i;
                        int col = raw.IndexOf("/*", StringComparison.Ordinal);
                        string indent = raw.Substring(0, col);
                        var parts = new List<string>();
                        string rest = raw.Substring(col + 2);
                        while (true)
                        {
                            int close = rest.IndexOf("*/", StringComparison.Ordinal);
                            if (close >= 0)
                            {
                                if (rest.Substring(close + 2).Trim().Length > 0)
                                    throw new FormatException($"line {i + 1}: put nothing after */ on the same line.");
                                parts.Add(rest.Substring(0, close));
                                break;
                            }
                            parts.Add(rest);
                            if (++i >= lines.Length) throw new FormatException($"line {start + 1}: this /* comment is never closed with */.");
                            rest = lines[i];
                            // continuation lines are indented 3 past the "/*"
                            string lead = indent + "   ";
                            if (rest.StartsWith(lead)) rest = rest.Substring(lead.Length);
                            else if (rest.StartsWith(indent)) rest = rest.Substring(indent.Length);
                        }
                        // first part starts after "/* ", last part ends before " */"
                        if (parts[0].StartsWith(" ")) parts[0] = parts[0].Substring(1);
                        int last = parts.Count - 1;
                        if (parts[last].EndsWith(" ")) parts[last] = parts[last].Substring(0, parts[last].Length - 1);
                        string body = Unesc(string.Join("\n", parts));

                        if (body.StartsWith("#"))                        // /*# key: value */
                        {
                            Match meta = Regex.Match(body, @"^#\s*(caption|description|comment):\s?(.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                            Match blk  = Regex.Match(body, @"^#\s*block\s*\d*\s*(?::\s?(.*))?$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                            if (blk.Success)
                            {
                                if (pending != null) throw new FormatException($"line {pendingLine}: a comment must be followed by a row or an IF.");
                                m.Blocks.Add(b = new Block { Label = blk.Groups[1].Success ? blk.Groups[1].Value : "" });
                                seenContent = true;
                            }
                            else if (meta.Success && b == null)
                            {
                                string v = meta.Groups[2].Value;
                                switch (meta.Groups[1].Value.ToLowerInvariant())
                                {
                                    case "caption": m.Caption = v.Trim(); break;
                                    case "description": m.Description = v; break;
                                    default: m.Comment = v; break;
                                }
                            }
                            else throw new FormatException($"line {start + 1}: unknown /*# line (use caption:, description:, comment: or block).");
                            continue;
                        }
                        // The explanation header (or a read-only note) before anything else
                        if (!seenContent && m.Caption.Length == 0 && m.Blocks.Count == 0 &&
                            (body.StartsWith("Formula step") || body.StartsWith("READ ONLY")))
                            continue;
                        if (pending != null) throw new FormatException($"line {pendingLine}: a comment must be followed by a row or an IF.");
                        pending = body;
                        pendingLine = start + 1;
                        continue;
                    }
                    if (line.StartsWith("//"))
                        throw new FormatException($"line {i + 1}: write comments as /* … */ (a // line is not a comment in Calc Script).");

                    seenContent = true;
                    if (b == null) m.Blocks.Add(b = new Block());

                    Match cond = Regex.Match(line, @"^(IF|ELSEIF)\s*\((.*)\)$|^(ELSE)$", RegexOptions.IgnoreCase);
                    if (cond.Success)
                    {
                        if (b.Condition.Length > 0 || b.Rows.Count > 0)
                            throw new FormatException($"line {i + 1}: one IF / ELSEIF / ELSE per block — start a new block with /*# block */.");
                        b.Condition = (cond.Groups[1].Success ? cond.Groups[1].Value : cond.Groups[3].Value).ToUpperInvariant();
                        b.Test = cond.Groups[2].Value.Trim();
                        b.ConditionComment = pending ?? "";
                        pending = null;
                        continue;
                    }
                    if (string.Equals(line, "ENDIF", StringComparison.OrdinalIgnoreCase) || string.Equals(line, "ENDIF;", StringComparison.OrdinalIgnoreCase) ||
                        line == ")" || line == ");") continue;
                    Match member = Regex.Match(line, @"^(.*\S)\s*\($");
                    if (member.Success && b.Member.Length == 0 && b.Condition.Length == 0 && b.Rows.Count == 0)
                    {
                        if (pending != null) throw new FormatException($"line {pendingLine}: a comment must be followed by a row or an IF.");
                        b.Member = member.Groups[1].Value.Trim();
                        continue;
                    }
                    if (!line.EndsWith(";")) throw new FormatException($"line {i + 1}: a row ends with ';' — one  \"Destination\" = expression;  per line.");
                    string row = line.Substring(0, line.Length - 1).Trim();
                    Match assign = Regex.Match(row, @"^(.*?)(?<![=!<>])=(?!=)(.*)$");
                    var r = new Row
                    {
                        Comment = pending ?? "",
                        Destination = (assign.Success ? assign.Groups[1].Value : row).Trim(),
                        Expression = assign.Success ? assign.Groups[2].Value.Trim() : null,
                    };
                    pending = null;
                    if (r.Destination.Length == 0) throw new FormatException($"line {i + 1}: the row has no destination member.");
                    b.Rows.Add(r);
                }
                if (pending != null) throw new FormatException($"line {pendingLine}: a comment must be followed by a row or an IF.");
                return m;
            }

            /// <summary>Writes the model into the component's properties and &lt;formulae&gt;.</summary>
            public void ToXml(XElement comp)
            {
                SetProp(comp, "caption", Caption);
                SetProp(comp, "description", Description);
                SetProp(comp, "comment", Comment);
                XElement formulae = comp.Element("formulae");
                if (formulae == null) comp.Add(formulae = new XElement("formulae"));
                formulae.RemoveNodes();
                foreach (Block b in Blocks)
                {
                    var cb = new XElement("conditionblock");
                    AddProp(cb, "comment", b.ConditionComment);
                    AddProp(cb, "commentblock", b.Label);
                    AddProp(cb, "condition", b.Condition);
                    AddProp(cb, "memberblock", b.Member);
                    if (b.Condition.Length > 0 && !string.Equals(b.Condition, "ELSE", StringComparison.OrdinalIgnoreCase))
                        cb.Add(new XElement("test", new XElement("expression", new XElement("user_inp", b.Test))));
                    foreach (Row r in b.Rows)
                    {
                        var f = new XElement("formula");
                        AddProp(f, "comment", r.Comment);
                        AddProp(f, "formulatype", r.Expression == null ? "Member Formula" : "Formula");
                        f.Add(new XElement("destination", new XElement("user_inp", r.Destination)));
                        if (r.Expression != null) f.Add(new XElement("expression", new XElement("user_inp", r.Expression)));
                        cb.Add(f);
                    }
                    formulae.Add(cb);
                }
            }

            public bool SameAs(FormulaModel o) => Key() == o.Key();

            private string Key()
            {
                string N(string s) => string.Join("\n", Lf(s ?? "").Split('\n').Select(l => l.TrimEnd())).Trim();
                var sb = new StringBuilder();
                sb.Append(N(Caption)).Append('\u0001').Append(N(Description)).Append('\u0001').Append(N(Comment));
                foreach (Block b in Blocks)
                {
                    sb.Append('\u0002').Append(N(b.Label)).Append('\u0001').Append(N(b.Member)).Append('\u0001')
                      .Append(N(b.Condition).ToUpperInvariant()).Append('\u0001').Append(N(b.ConditionComment)).Append('\u0001').Append(N(b.Test));
                    foreach (Row r in b.Rows)
                        sb.Append('\u0003').Append(N(r.Comment)).Append('\u0001').Append(N(r.Destination)).Append('\u0001')
                          .Append(r.Expression == null ? "\u0004" : N(r.Expression));
                }
                return sb.ToString();
            }

            /// <summary>
            /// True when the rendered file reads back to the same model, and writing that model
            /// recreates the component's formulae exactly (no setting this format doesn't hold).
            /// </summary>
            public static bool RoundTrips(XElement comp, string rendered)
            {
                try
                {
                    FormulaModel original = FromXml(comp);
                    if (!Parse(rendered).SameAs(original)) return false;
                    // Anything on the component besides properties and formulae, or a property we don't keep?
                    var known = new HashSet<string> { "caption", "description", "comment", "application", "plantype" };
                    if (comp.Elements().Any(e => e.Name != "property" && e.Name != "formulae")) return false;
                    if (comp.Elements("property").Any(p => !known.Contains((string)p.Attribute("name")))) return false;
                    var copy = new XElement(comp);
                    original.ToXml(copy);
                    return Canon(copy.Element("formulae")) == Canon(comp.Element("formulae"));
                }
                catch { return false; }
            }
        }

        // ------------------------------------------------------------------ //
        //  Helpers
        // ------------------------------------------------------------------ //

        private static string Prop(XElement e, string name) =>
            e?.Elements("property").FirstOrDefault(p => (string)p.Attribute("name") == name)?.Value ?? "";

        private static void AddProp(XElement e, string name, string value)
        {
            if (!string.IsNullOrEmpty(value)) e.Add(new XElement("property", new XAttribute("name", name), Lf(value)));
        }

        private static void SetProp(XElement e, string name, string value)
        {
            XElement p = e.Elements("property").FirstOrDefault(x => (string)x.Attribute("name") == name);
            if (string.IsNullOrEmpty(value)) { p?.Remove(); return; }
            if (p == null)
            {
                p = new XElement("property", new XAttribute("name", name));
                XElement last = e.Elements("property").LastOrDefault();
                if (last != null) last.AddAfterSelf(p); else e.AddFirst(p);
            }
            if (p.Value.Trim() != value.Trim()) p.Value = Lf(value);
        }

        private static string UserInput(XElement holder) => holder?.Element("expression")?.Element("user_inp")?.Value;

        /// <summary>The dimension holder: a loop's &lt;source&gt;&lt;slice&gt;, or &lt;globalrange&gt; itself.</summary>
        private static XElement DimensionsOf(XElement holder) =>
            holder?.Element("slice") ?? (holder?.Name == "globalrange" ? holder : null);

        private static List<KeyValuePair<string, string>> SliceOf(XElement holder) =>
            (DimensionsOf(holder)?.Elements("dimension") ?? Enumerable.Empty<XElement>())
                .Select(d => Kv((string)d.Attribute("name"), (UserInput(d) ?? "").Trim())).ToList();

        private static KeyValuePair<string, string> Kv(string k, string v) => new KeyValuePair<string, string>(k, v);

        private static string AllText(XElement rule) =>
            string.Join("\n", rule.Descendants("script").Select(s => s.Value)
                                  .Concat(rule.Descendants("user_inp").Select(u => u.Value)));

        private static string Canon(XElement e)
        {
            if (e == null) return "";
            var sb = new StringBuilder("<" + e.Name.LocalName);
            foreach (XAttribute a in e.Attributes().OrderBy(a => a.Name.LocalName)) sb.Append($" {a.Name.LocalName}={a.Value}");
            sb.Append('>');
            if (!e.HasElements) sb.Append(string.Join("\n", Lf(e.Value).Split('\n').Select(l => l.TrimEnd())).Trim());
            foreach (XElement c in e.Elements()) sb.Append(Canon(c));
            return sb.Append("</>").ToString();
        }

        private static bool Same(string a, string b) => Lf(a).TrimEnd() == Lf(b).TrimEnd();
        private static string Lf(string s) => (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        private static string OneLine(string s) => Lf(s).Replace("\n", " ");

        private static string SafeName(string s)
        {
            string r = new string(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return r.Length == 0 ? "Step" : (r.Length > 80 ? r.Substring(0, 80).TrimEnd() : r);
        }

        private static void WriteText(string path, string text) =>
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
