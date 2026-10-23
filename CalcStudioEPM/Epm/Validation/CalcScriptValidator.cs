using System;
using System.Collections.Generic;
using System.Linq;
using CalcStudioEPM.Config;

namespace CalcStudioEPM.Epm.Validation
{
    /// <summary>
    /// Local, best-effort checks of an Essbase / Calculation Manager calc script:
    /// FIX/IF/LOOP… blocks open and close in the right order (IF inside a member block),
    /// brackets balance, functions exist with an allowed number of parameters, SET options
    /// are known, and {runtime prompts} are in the variable dictionary. Every function,
    /// block, command and severity comes from the editable <see cref="CalcScriptCatalog"/>.
    /// </summary>
    public static class CalcScriptValidator
    {
        /// <param name="isTemplate">
        /// Calculation Manager templates are fragments — one may open a member block that
        /// the calling rule closes, another may start at ELSEIF — so block and bracket
        /// structure is not checked for them; functions, SET options and prompts still are.
        /// </param>
        /// <param name="context">Templates, metadata and cube for the checks that need them (optional).</param>
        public static List<ValidationIssue> Validate(string script, CalcScriptCatalog catalog,
                                                     VariableDictionary dictionary = null, bool isTemplate = false,
                                                     ValidationContext context = null)
        {
            script  = script ?? "";
            catalog = catalog ?? CalcScriptCatalog.Defaults();

            CalcScriptLexer lexer = CalcScriptLexer.Run(script);
            var raw  = new List<ValidationIssue>(lexer.Issues);
            var toks = lexer.Tokens;

            if (!isTemplate)
            {
                CheckStructure(toks, catalog, raw);
                CheckOtherBrackets(toks, raw);
            }
            CheckFunctions(toks, catalog, raw);
            CheckSetOptions(toks, catalog, raw);
            CheckPrompts(toks, dictionary, raw);
            CalcScriptChecks.Run(script, toks, catalog, context, isTemplate, raw);

            return ValidationResults.Finish(script, raw, catalog.SeverityOf);
        }

        // ------------------------------------------------------------------ //
        //  Blocks + brackets
        // ------------------------------------------------------------------ //

        private sealed class Open
        {
            public CalcBlock Block;
            public CsTok     Tok;
            public int       ParenDepth;   // bracket depth when the block was opened
        }

        private static void CheckStructure(List<CsTok> toks, CalcScriptCatalog catalog, List<ValidationIssue> issues)
        {
            var opens   = new Dictionary<string, CalcBlock>(StringComparer.OrdinalIgnoreCase);
            var closes  = new Dictionary<string, CalcBlock>(StringComparer.OrdinalIgnoreCase);
            var middles = new Dictionary<string, CalcBlock>(StringComparer.OrdinalIgnoreCase);
            foreach (CalcBlock b in catalog.Blocks)
            {
                if (!string.IsNullOrWhiteSpace(b.Open))  opens[b.Open]   = b;
                if (!string.IsNullOrWhiteSpace(b.Close)) closes[b.Close] = b;
                foreach (string m in b.Middle ?? new List<string>()) middles[m] = b;
            }

            var blocks = new Stack<Open>();
            var parens = new Stack<CsTok>();

            for (int i = 0; i < toks.Count; i++)
            {
                CsTok t = toks[i];

                if (t.Kind == CsKind.Op && t.Text == "(") { parens.Push(t); continue; }
                if (t.Kind == CsKind.Op && t.Text == ")")
                {
                    if (parens.Count == 0)
                    {
                        issues.Add(Issue("syntax.unbalanced-brackets", t.Pos, "')' has no matching '('."));
                        continue;
                    }
                    // A block opened inside these brackets must end before them
                    while (blocks.Count > 0 && blocks.Peek().ParenDepth >= parens.Count)
                    {
                        Open o = blocks.Pop();
                        issues.Add(Issue("block.unbalanced", o.Tok.Pos,
                            $"{o.Block.Open} is not closed with {o.Block.Close} before the ')' on this member block."));
                    }
                    parens.Pop();
                    continue;
                }

                if (t.Kind != CsKind.Word) continue;
                // "Mbr"->FIX would be a member named FIX, not the command
                if (i > 0 && toks[i - 1].Kind == CsKind.Op && toks[i - 1].Text == "->") continue;

                if (opens.TryGetValue(t.Text, out CalcBlock opened))
                {
                    if (opened.RequiresMemberBlock && parens.Count == 0)
                        issues.Add(Issue("block.outside-member-block", t.Pos,
                            $"{opened.Open} must be inside a member block, e.g. \"Account\" ( {opened.Open} (…) … {opened.Close}; )."));
                    blocks.Push(new Open { Block = opened, Tok = t, ParenDepth = parens.Count });
                }
                else if (middles.TryGetValue(t.Text, out CalcBlock mid))
                {
                    if (blocks.Count == 0 || !string.Equals(blocks.Peek().Block.Open, mid.Open, StringComparison.OrdinalIgnoreCase))
                        issues.Add(Issue("block.unbalanced", t.Pos,
                            $"{t.Text.ToUpperInvariant()} without an open {mid.Open}" +
                            (blocks.Count > 0 ? $" (the innermost open block is {blocks.Peek().Block.Open} on line {{line:{blocks.Peek().Tok.Pos}}})." : ".")));
                }
                else if (closes.TryGetValue(t.Text, out CalcBlock closed))
                {
                    if (blocks.Count > 0 && string.Equals(blocks.Peek().Block.Open, closed.Open, StringComparison.OrdinalIgnoreCase))
                    {
                        blocks.Pop();
                    }
                    else if (blocks.Any(o => string.Equals(o.Block.Open, closed.Open, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Something in between was never closed — report it, then resync
                        while (!string.Equals(blocks.Peek().Block.Open, closed.Open, StringComparison.OrdinalIgnoreCase))
                        {
                            Open o = blocks.Pop();
                            issues.Add(Issue("block.unbalanced", o.Tok.Pos,
                                $"{o.Block.Open} is never closed with {o.Block.Close} (found {closed.Close} first)."));
                        }
                        blocks.Pop();
                    }
                    else
                    {
                        issues.Add(Issue("block.unbalanced", t.Pos,
                            $"{closed.Close} without a matching {closed.Open}."));
                    }
                }
            }

            foreach (Open o in blocks)
                issues.Add(Issue("block.unbalanced", o.Tok.Pos, $"{o.Block.Open} is never closed with {o.Block.Close}."));
            foreach (CsTok p in parens)
                issues.Add(Issue("syntax.unbalanced-brackets", p.Pos, "'(' is never closed with ')'."));
        }

        /// <summary>{ } (e.g. SET DATAEXPORTOPTIONS) and [ ] (design-time prompts) must pair up too.</summary>
        private static void CheckOtherBrackets(List<CsTok> toks, List<ValidationIssue> issues)
        {
            var stack = new Stack<CsTok>();
            foreach (CsTok t in toks.Where(t => t.Kind == CsKind.Op))
            {
                if (t.Text == "{" || t.Text == "[") { stack.Push(t); continue; }
                if (t.Text != "}" && t.Text != "]") continue;

                string open = t.Text == "}" ? "{" : "[";
                if (stack.Count > 0 && stack.Peek().Text == open) { stack.Pop(); continue; }
                issues.Add(Issue("syntax.unbalanced-brackets", t.Pos, $"'{t.Text}' has no matching '{open}'."));
            }
            foreach (CsTok o in stack)
                issues.Add(Issue("syntax.unbalanced-brackets", o.Pos,
                    $"'{o.Text}' is never closed with '{(o.Text == "{" ? "}" : "]")}'."));
        }

        // ------------------------------------------------------------------ //
        //  Functions
        // ------------------------------------------------------------------ //

        private static void CheckFunctions(List<CsTok> toks, CalcScriptCatalog catalog, List<ValidationIssue> issues)
        {
            var functions = new Dictionary<string, CalcFunction>(StringComparer.OrdinalIgnoreCase);
            foreach (CalcFunction f in catalog.Functions)
                if (!string.IsNullOrWhiteSpace(f.Name))
                    functions[f.Name.StartsWith("@") ? f.Name : "@" + f.Name] = f;
            var constants = new HashSet<string>(
                (catalog.Constants ?? new List<string>()).Select(c => c.StartsWith("@") ? c : "@" + c),
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < toks.Count; i++)
            {
                if (toks[i].Kind != CsKind.Function) continue;
                CsTok  t    = toks[i];
                string name = "@" + t.Text;
                if (constants.Contains(name)) continue;

                if (!functions.TryGetValue(name, out CalcFunction fn))
                {
                    issues.Add(Issue("function.unknown", t.Pos,
                        $"{name.ToUpperInvariant()} is not a known function. If it exists, add it to calcscript_catalog.json (Edit rules…)."));
                    continue;
                }

                if (!(i + 1 < toks.Count && toks[i + 1].Kind == CsKind.Op && toks[i + 1].Text == "("))
                {
                    issues.Add(Issue("function.missing-parentheses", t.Pos,
                        $"{name.ToUpperInvariant()} must be followed by its parameters in brackets: {fn.Syntax}"));
                    continue;
                }

                int args = CountArguments(toks, i + 1, out bool closed);
                if (!closed) continue;   // the bracket check reports it

                bool tooFew  = args < fn.MinArgs;
                bool tooMany = fn.MaxArgs >= 0 && args > fn.MaxArgs;
                if (tooFew || tooMany)
                {
                    string expected = fn.MaxArgs < 0       ? $"at least {fn.MinArgs}"
                                    : fn.MinArgs == fn.MaxArgs ? $"{fn.MinArgs}"
                                    :                           $"{fn.MinArgs} to {fn.MaxArgs}";
                    issues.Add(Issue("function.argument-count", t.Pos,
                        $"{name.ToUpperInvariant()} takes {expected} parameter{(expected == "1" ? "" : "s")} but has {args}. Syntax: {fn.Syntax}"));
                }
            }
        }

        /// <summary>Number of top-level comma-separated arguments in the bracket at <paramref name="open"/>.</summary>
        private static int CountArguments(List<CsTok> toks, int open, out bool closed)
        {
            int depth = 0, commas = 0;
            bool any = false;
            for (int k = open; k < toks.Count; k++)
            {
                CsTok t = toks[k];
                if (t.Kind == CsKind.Op && t.Text == "(") { depth++; if (depth > 1) any = true; continue; }
                if (t.Kind == CsKind.Op && t.Text == ")")
                {
                    if (--depth == 0) { closed = true; return any ? commas + 1 : 0; }
                    continue;
                }
                if (depth == 1 && t.Kind == CsKind.Op && t.Text == ",") { commas++; continue; }
                if (depth >= 1) any = true;
            }
            closed = false;
            return 0;
        }

        // ------------------------------------------------------------------ //
        //  SET commands
        // ------------------------------------------------------------------ //

        private static void CheckSetOptions(List<CsTok> toks, CalcScriptCatalog catalog, List<ValidationIssue> issues)
        {
            var options = new HashSet<string>(catalog.SetOptions ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < toks.Count; i++)
            {
                if (toks[i].Kind != CsKind.Word || !string.Equals(toks[i].Text, "SET", StringComparison.OrdinalIgnoreCase)) continue;
                if (i > 0 && !IsStatementStart(toks[i - 1])) continue;   // "SET" as a member name
                CsTok option = toks[i + 1];
                if (option.Kind == CsKind.Word && !options.Contains(option.Text))
                    issues.Add(Issue("command.unknown-set", option.Pos,
                        $"SET {option.Text.ToUpperInvariant()} is not a known SET command. If it is valid, add it to SetOptions in calcscript_catalog.json."));
            }
        }

        private static bool IsStatementStart(CsTok previous) =>
            previous.Kind == CsKind.Op && (previous.Text == ";" || previous.Text == ")" || previous.Text == "(");

        // ------------------------------------------------------------------ //
        //  Runtime prompts
        // ------------------------------------------------------------------ //

        private static void CheckPrompts(List<CsTok> toks, VariableDictionary dict, List<ValidationIssue> issues)
        {
            if (dict == null || dict.Variables.Count == 0) return;
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CsTok t in toks.Where(t => t.Kind == CsKind.Rtp))
                if (dict.Find(t.Text) == null && reported.Add(t.Text))
                    issues.Add(Issue("rtp.unknown-variable", t.Pos,
                        $"{{{t.Text}}} is not in this connection's variable dictionary — Deploy can't add it as a runtime prompt."));
        }

        private static ValidationIssue Issue(string rule, int pos, string message) =>
            new ValidationIssue { RuleId = rule, Pos = pos, Message = message };
    }

    /// <summary>Shared finishing step for both validators: severities, positions, order.</summary>
    internal static class ValidationResults
    {
        public static List<ValidationIssue> Finish(string script, List<ValidationIssue> raw, Func<string, string> severityOf)
        {
            int[] lineStarts = LineStarts(script);
            int LineOf(int pos)
            {
                int line = Array.BinarySearch(lineStarts, pos);
                return line < 0 ? ~line - 1 : line;
            }

            var result = new List<ValidationIssue>();
            var seen   = new HashSet<string>();
            foreach (ValidationIssue issue in raw.OrderBy(x => x.Pos))
            {
                string severity = issue.Severity ?? severityOf(issue.RuleId);
                if (severity != "error" && severity != "warning") continue;
                if (!seen.Add(issue.RuleId + "@" + issue.Pos)) continue;

                int line = Math.Max(LineOf(issue.Pos), 0);
                issue.Severity = severity;
                issue.Line     = line + 1;
                issue.Column   = issue.Pos - lineStarts[line] + 1;
                // "{line:<pos>}" placeholders in messages → the line number of that position
                issue.Message  = System.Text.RegularExpressions.Regex.Replace(issue.Message ?? "", @"\{line:(\d+)\}",
                                     m => (LineOf(int.Parse(m.Groups[1].Value)) + 1).ToString());
                result.Add(issue);
            }
            return result;
        }

        private static int[] LineStarts(string s)
        {
            var starts = new List<int> { 0 };
            for (int i = 0; i < s.Length; i++) if (s[i] == '\n') starts.Add(i + 1);
            return starts.ToArray();
        }
    }
}
