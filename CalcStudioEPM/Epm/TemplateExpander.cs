using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CalcStudioEPM.Epm
{
    /// <summary>A %Template call that stops the rule from being deployed to Planning (HP).</summary>
    internal sealed class TemplateIssue
    {
        public string TemplateName { get; set; }
        public int    Line         { get; set; }   // 1-based, in the rule's script; 0 = inside a nested template
        public string Reason       { get; set; }

        public override string ToString() =>
            (Line > 0 ? $"Line {Line}: " : "") + $"'{TemplateName}' — {Reason}";
    }

    /// <summary>Result of <see cref="TemplateExpander.Expand"/>.</summary>
    internal sealed class TemplateExpansion
    {
        /// <summary>The script with every simple %Template call inlined. Only meaningful
        /// when <see cref="CanDeployToPlanning"/>.</summary>
        public string Script { get; set; }

        /// <summary>Distinct templates that were inlined ("{app}.{plantype}.{name}").</summary>
        public List<string> Inlined { get; } = new List<string>();

        public List<TemplateIssue> Issues { get; } = new List<TemplateIssue>();

        public bool CanDeployToPlanning => Issues.Count == 0;
    }

    /// <summary>
    /// Inlines %Template calls the way Calculation Manager does when it deploys a rule to
    /// Planning. The Planning (HP) copy of a rule holds no %Template calls — each one is
    /// replaced by the template's script between two marker comments:
    ///
    ///   /* Start Template:{application}.{plantype}.{name}*/
    ///   …template script…
    ///   /* End Template:{application}.{plantype}.{name}*/
    ///
    /// We can only do that faithfully for SIMPLE templates: called with no design-time
    /// prompts (<c>dtps:=()</c>), and whose definition is one plain script — no DTP
    /// declarations, no graphical components or conditions. Anything else (or a template
    /// that was never pulled) is reported in <see cref="TemplateExpansion.Issues"/>, and
    /// the rule can then only be deployed to Calculation Manager.
    ///
    /// The template script comes from the pulled CALC artifact (what EPM Cloud has), not
    /// from the editable .Scripts copy, because that is what Calculation Manager would use.
    /// Calls inside /* … */ comments are left alone.
    /// </summary>
    internal static class TemplateExpander
    {
        private const string CallStart = "%Template(";
        private const int    MaxDepth  = 10;

        public static TemplateExpansion Expand(string localFolder, string script)
        {
            var result = new TemplateExpansion();
            var cache  = new Dictionary<string, (string Script, string Problem)>(StringComparer.OrdinalIgnoreCase);
            result.Script = ExpandText(localFolder, script ?? "", result, cache,
                                       new Stack<string>(), topLevel: true);
            return result;
        }

        /// <summary>Quick check used before deploying: does the script call any template?</summary>
        public static bool HasTemplateCalls(string script) =>
            FindCalls(script ?? "").Any();

        // ------------------------------------------------------------------ //

        private static string ExpandText(string localFolder, string text, TemplateExpansion result,
                                         Dictionary<string, (string Script, string Problem)> cache,
                                         Stack<string> chain, bool topLevel)
        {
            var calls = FindCalls(text).ToList();
            if (calls.Count == 0) return text;

            var sb = new StringBuilder(text.Length);
            int pos = 0;
            foreach (Call call in calls)
            {
                sb.Append(text, pos, call.Start - pos);
                pos = call.End;

                string original = text.Substring(call.Start, call.End - call.Start);
                int line = topLevel ? LineOf(text, call.Start) : 0;
                string display = call.Name ?? "(unnamed)";

                string problem = null;
                string body    = null;
                string key     = $"{call.Application}.{call.PlanType}.{call.Name}";

                if (call.Name == null || call.Application == null || call.PlanType == null)
                    problem = "could not read the template's name, application and plan type";
                else if (!call.DtpsEmpty)
                    problem = "it is called with design-time prompts (DTPs)";
                else if (chain.Count >= MaxDepth || chain.Contains(key, StringComparer.OrdinalIgnoreCase))
                    problem = "templates call each other in a loop";
                else
                {
                    if (!cache.TryGetValue(key, out var tpl))
                    {
                        tpl = LoadSimpleTemplate(localFolder, call.Application, call.PlanType, call.Name);
                        cache[key] = tpl;
                    }
                    problem = tpl.Problem;
                    if (problem == null)
                    {
                        chain.Push(key);
                        body = ExpandText(localFolder, tpl.Script, result, cache, chain, topLevel: false);
                        chain.Pop();
                    }
                }

                if (problem != null)
                {
                    if (!topLevel) display += $" (called from template '{chain.Peek().Split('.').Last()}')";
                    if (!result.Issues.Any(i => i.TemplateName == display && i.Reason == problem && i.Line == line))
                        result.Issues.Add(new TemplateIssue { TemplateName = display, Line = line, Reason = problem });
                    sb.Append(original);
                    continue;
                }

                if (!result.Inlined.Contains(key, StringComparer.OrdinalIgnoreCase))
                    result.Inlined.Add(key);
                sb.Append(Inline(key, body));
            }
            sb.Append(text, pos, text.Length - pos);
            return sb.ToString();
        }

        /// <summary>
        /// EPM's replacement text, checked against a rule EPM deployed to Planning: the
        /// template script loses one trailing line break, then always ends with one.
        /// </summary>
        internal static string Inline(string key, string templateScript)
        {
            string body = (templateScript ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            if (body.EndsWith("\n")) body = body.Substring(0, body.Length - 1);
            if (!body.EndsWith("\n")) body += "\n";
            return $"/* Start Template:{key}*/\n{body}/* End Template:{key}*/\n";
        }

        /// <summary>
        /// The script of a simple template, or why it can't be inlined. Read from
        /// CalcManager\CALC-Calculation Manager\resource\Planning\{app}\{plantype}\Templates\{name}.
        /// </summary>
        private static (string Script, string Problem) LoadSimpleTemplate(
            string localFolder, string application, string planType, string name)
        {
            if (string.IsNullOrWhiteSpace(localFolder))
                return (null, "no local folder is configured, so the template can't be read");

            string path = Path.Combine(localFolder, PathConstants.CalcManagerDirName, "CALC-Calculation Manager",
                                       "resource", PathConstants.PlanningDirName, application, planType,
                                       "Templates", PathConstants.ToFileName(name));
            if (!File.Exists(path))
                return (null, "the template was not found locally (run 'Pull Calculations' to fetch it)");

            XDocument doc;
            try { doc = XDocument.Parse(File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception ex) { return (null, "the pulled template could not be read: " + ex.Message); }

            XElement template = doc.Descendants("template").FirstOrDefault(t =>
                string.Equals((string)t.Attribute("name"), name, StringComparison.OrdinalIgnoreCase))
                ?? doc.Descendants("template").FirstOrDefault();
            if (template == null)
                return (null, "the pulled file holds no template definition");

            if (template.Descendants("template_prompt").Any())
                return (null, "the template declares design-time prompts (DTPs)");
            if (template.Descendants("component").Any() || template.Descendants("statement").Any())
                return (null, "it is a graphical template (components / conditions)");

            List<XElement> scripts = template.Descendants("script").ToList();
            if (scripts.Count != 1 || scripts[0].Parent != template)
                return (null, "the template is not a single plain script");

            return (scripts[0].Value, null);
        }

        // ------------------------------------------------------------------ //
        //  Finding %Template(...) calls
        // ------------------------------------------------------------------ //

        private sealed class Call
        {
            public int Start, End;                // End is exclusive (just after the closing ')')
            public string Name, Application, PlanType;
            public bool DtpsEmpty;
        }

        private static readonly Regex ArgName = new Regex(@"\bname\s*:=\s*""([^""]*)""",        RegexOptions.IgnoreCase);
        private static readonly Regex ArgApp  = new Regex(@"\bapplication\s*:=\s*""([^""]*)""", RegexOptions.IgnoreCase);
        private static readonly Regex ArgPt   = new Regex(@"\bplantype\s*:=\s*""([^""]*)""",    RegexOptions.IgnoreCase);
        private static readonly Regex ArgDtps = new Regex(@"\bdtps\s*:=\s*\(",                   RegexOptions.IgnoreCase);

        private static IEnumerable<Call> FindCalls(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                // Skip /* … */ comments — a call in a comment is inert, and EPM leaves it
                if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = close < 0 ? text.Length : close + 2;
                    continue;
                }

                if (text[i] == '%' && string.Compare(text, i, CallStart, 0, CallStart.Length,
                                                     StringComparison.OrdinalIgnoreCase) == 0)
                {
                    int end = MatchingParen(text, i + CallStart.Length - 1);
                    if (end < 0) { i += CallStart.Length; continue; }

                    string args = text.Substring(i + CallStart.Length, end - (i + CallStart.Length));
                    yield return new Call
                    {
                        Start       = i,
                        End         = end + 1,
                        Name        = Arg(ArgName, args),
                        Application = Arg(ArgApp, args),
                        PlanType    = Arg(ArgPt, args),
                        DtpsEmpty   = DtpsAreEmpty(args),
                    };
                    i = end + 1;
                    continue;
                }
                i++;
            }
        }

        private static string Arg(Regex re, string args)
        {
            Match m = re.Match(args);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>True for "dtps:=()" (blanks allowed) and for a call with no dtps argument.</summary>
        private static bool DtpsAreEmpty(string args)
        {
            Match m = ArgDtps.Match(args);
            if (!m.Success) return true;
            int open  = m.Index + m.Length - 1;
            int close = MatchingParen(args, open);
            string inner = close < 0 ? args.Substring(open + 1) : args.Substring(open + 1, close - open - 1);
            return inner.Trim().Length == 0;
        }

        /// <summary>Index of the ')' closing the '(' at <paramref name="open"/>, skipping "quoted" text.</summary>
        private static int MatchingParen(string s, int open)
        {
            int depth = 0;
            bool inQuote = false;
            for (int i = open; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') inQuote = !inQuote;
                else if (inQuote) continue;
                else if (c == '(') depth++;
                else if (c == ')' && --depth == 0) return i;
            }
            return -1;
        }

        private static int LineOf(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++)
                if (text[i] == '\n') line++;
            return line;
        }
    }
}
