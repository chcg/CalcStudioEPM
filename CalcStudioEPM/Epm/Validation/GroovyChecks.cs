using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CalcStudioEPM.Config;

namespace CalcStudioEPM.Epm.Validation
{
    /// <summary>
    /// More Groovy checks, on <see cref="GroovyLexer"/> tokens and the EPM Groovy API:
    ///   api.unknown-member      method / property not on the receiver's EPM type
    ///   api.argument-count      no overload takes that many arguments
    ///   api.deprecated          deprecated EPM API
    ///   grid.not-closed         loadGrid(…) result never closed
    ///   rtps.unused             /*RTPS: */ prompt the script never reads
    ///   if.assignment           = instead of == in if ( … )
    ///   string.single-quoted-interpolation   '${x}' stays literal
    ///   uservar.*, subvar.*, dimension.unknown, member.unknown   against the pulled metadata
    /// (Restricted sandbox patterns live in groovy_validation.json.)
    /// </summary>
    internal static class GroovyChecks
    {
        // Groovy / GDK / java.lang.Object members every object has
        private static readonly HashSet<string> Universal = new HashSet<string>(StringComparer.Ordinal)
        {
            "each", "eachWithIndex", "collect", "collectMany", "collectEntries", "find", "findAll", "findResult",
            "findIndexOf", "any", "every", "inject", "sort", "sum", "max", "min", "count", "unique", "groupBy",
            "countBy", "size", "toString", "equals", "hashCode", "getClass", "with", "tap", "is", "isCase", "asType",
            "join", "contains", "first", "last", "iterator", "forEach", "stream", "use", "withCloseable", "close",
            "getAt", "putAt", "get", "put", "add", "addAll", "remove", "isEmpty", "keySet", "values", "entrySet",
            "toList", "toSet", "asList", "getProperty", "setProperty", "invokeMethod", "getMetaClass", "metaClass",
            "class", "properties", "identity", "dump", "inspect", "split", "grep", "reverse", "take", "drop",
            "subList", "indexOf", "flatten", "intersect", "minus", "plus", "clone", "compareTo", "name", "ordinal",
            "values", "valueOf",
        };

        public static void Run(string script, List<Tok> all, string codeOnly, GroovyValidationConfig cfg,
                               ValidationContext ctx, List<ValidationIssue> issues)
        {
            var toks = all.Where(t => t.Kind != TokKind.Newline).ToList();
            CheckApi(toks, codeOnly, cfg, issues);
            CheckGrids(codeOnly, issues);
            CheckUnusedPrompts(script, toks, issues);
            CheckIfConditions(toks, issues);
            CheckSingleQuoted(script, toks, issues);
            CheckMetadata(script, codeOnly, ctx, issues);
        }

        // ------------------------------------------------------------------ //
        //  EPM API: unknown members, argument counts, deprecated
        // ------------------------------------------------------------------ //

        private static void CheckApi(List<Tok> toks, string codeOnly, GroovyValidationConfig cfg, List<ValidationIssue> issues)
        {
            GroovyApi api = GroovyApi.Instance;
            if (api.Types.Count == 0) return;
            var allowed = new HashSet<string>(cfg.KnownApiMembers ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var reported = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 1; i < toks.Count; i++)
            {
                Tok name = toks[i];
                if (name.Kind != TokKind.Ident || name.InString) continue;
                Tok dot = toks[i - 1];
                if (dot.Kind != TokKind.Op || (dot.Text != "." && dot.Text != "?.")) continue;

                string before = codeOnly.Substring(0, dot.Pos);
                List<GroovyTypeInference.Segment> chain = GroovyTypeInference.ParseChain(before);
                if (chain.Count == 0) continue;
                GroovyApi.TypeInfo type = api.Find(GroovyTypeInference.ResolveChain(chain, before, api, 0));
                if (type == null) continue;

                bool call = i + 1 < toks.Count && toks[i + 1].Kind == TokKind.Op && (toks[i + 1].Text == "(" || toks[i + 1].Text == "{");
                List<GroovyApi.Method> methods = api.AllMethods(type).Where(m => m.Name == name.Text).ToList();

                if (methods.Count == 0)
                {
                    bool known = Universal.Contains(name.Text) || type.Constants.Contains(name.Text) ||
                                 (!call && api.MemberType(type, name.Text, false) != null) ||
                                 allowed.Contains(type.Name + "." + name.Text) || allowed.Contains("*." + name.Text);
                    if (!known && reported.Add("u:" + type.Name + "." + name.Text))
                        issues.Add(Issue("api.unknown-member", name.Pos,
                            $"{type.Name} has no {(call ? "method" : "property")} '{name.Text}' in the EPM Groovy API. " +
                            $"If it does exist, add \"{type.Name}.{name.Text}\" to Known API members (Configure… › Groovy validation)."));
                    continue;
                }

                if (methods.All(m => m.Deprecated) && reported.Add("d:" + type.Name + "." + name.Text))
                    issues.Add(Issue("api.deprecated", name.Pos, $"{type.Name}.{name.Text} is deprecated in the EPM Groovy API."));

                if (!call || toks[i + 1].Text != "(") continue;
                int args = CountArguments(toks, i + 1, out int close);
                if (close < 0) continue;
                if (close + 1 < toks.Count && toks[close + 1].Kind == TokKind.Op && toks[close + 1].Text == "{") args++;   // trailing closure
                bool fits = methods.Any(m =>
                {
                    int n = ParamCount(m.Params);
                    return m.Params.Contains("...") ? args >= n - 1 : args == n;
                });
                if (!fits)
                    issues.Add(Issue("api.argument-count", name.Pos,
                        $"{type.Name}.{name.Text} is called with {args} argument{(args == 1 ? "" : "s")}; it takes: " +
                        string.Join("  |  ", methods.Select(m => m.Name + m.Params).Distinct().Take(4))));
            }
        }

        private static int CountArguments(List<Tok> toks, int open, out int close)
        {
            int depth = 0, commas = 0;
            bool any = false;
            for (int k = open; k < toks.Count; k++)
            {
                Tok t = toks[k];
                if (t.Kind == TokKind.Op && (t.Text == "(" || t.Text == "[" || t.Text == "{")) { depth++; if (depth > 1) any = true; continue; }
                if (t.Kind == TokKind.Op && (t.Text == ")" || t.Text == "]" || t.Text == "}"))
                {
                    if (--depth == 0) { close = k; return any ? commas + 1 : 0; }
                    continue;
                }
                if (depth == 1 && t.Kind == TokKind.Op && t.Text == ",") { commas++; continue; }
                if (depth >= 1) any = true;
            }
            close = -1;
            return 0;
        }

        private static int ParamCount(string ps)
        {
            string p = (ps ?? "").Trim().TrimStart('(').TrimEnd(')').Trim();
            if (p.Length == 0) return 0;
            int depth = 0, n = 1;
            foreach (char c in p)
            {
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ //
        //  Grids from loadGrid(…) must be closed
        // ------------------------------------------------------------------ //

        private static void CheckGrids(string code, List<ValidationIssue> issues)
        {
            foreach (Match m in Regex.Matches(code, @"\.\s*loadGrid\s*\("))
            {
                int lineStart = code.LastIndexOf('\n', m.Index) + 1;
                string head = code.Substring(lineStart, m.Index - lineStart);
                if (Regex.IsMatch(head, @"\btry\s*\(")) continue;                         // try-with-resources
                int close = MatchParen(code, m.Index + m.Length - 1);
                if (close > 0 && Regex.IsMatch(code.Substring(close + 1, Math.Min(40, code.Length - close - 1)), @"^\s*\.\s*(use|withCloseable)\b"))
                    continue;                                                              // loadGrid(…).use { … }
                Match target = Regex.Match(head, @"(\w+)\s*=[^=]*$");
                if (target.Success &&
                    Regex.IsMatch(code, $@"\b{Regex.Escape(target.Groups[1].Value)}\s*\??\.\s*(close|use|withCloseable)\b"))
                    continue;
                issues.Add(Issue("grid.not-closed", m.Index + 1,
                    "This grid from loadGrid(…) is never closed: call .close() on it, or use cube.loadGrid(…).withCloseable { … }, " +
                    "so EPM releases it."));
            }
        }

        private static int MatchParen(string s, int open)
        {
            int depth = 0;
            for (int k = open; k < s.Length; k++)
            {
                if (s[k] == '(') depth++;
                else if (s[k] == ')' && --depth == 0) return k;
            }
            return -1;
        }

        // ------------------------------------------------------------------ //
        //  /*RTPS: {X} */ prompts the script never reads
        // ------------------------------------------------------------------ //

        private static void CheckUnusedPrompts(string script, List<Tok> toks, List<ValidationIssue> issues)
        {
            Match h = Regex.Match(script, @"/\*\s*RTPS:(.*?)\*/", RegexOptions.Singleline);
            if (!h.Success) return;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 2 < toks.Count; i++)
                if (toks[i].Kind == TokKind.Ident && toks[i].Text == "rtps" && toks[i + 1].Kind == TokKind.Op &&
                    (toks[i + 1].Text == "." || toks[i + 1].Text == "?.") && toks[i + 2].Kind == TokKind.Ident)
                    used.Add(toks[i + 2].Text);
            // rtps["X"] / rtps.get("X") style
            foreach (Match m in Regex.Matches(script, @"\brtps\s*(?:\[\s*|\.get\s*\(\s*)[""']([^""']+)[""']")) used.Add(m.Groups[1].Value);

            foreach (Match m in Regex.Matches(h.Groups[1].Value, @"\{(\w+)\}"))
                if (!used.Contains(m.Groups[1].Value))
                    issues.Add(Issue("rtps.unused", h.Groups[1].Index + m.Index,
                        $"{{{m.Groups[1].Value}}} is in the /*RTPS: */ header, so users are asked for it, but the script never reads rtps.{m.Groups[1].Value}."));
        }

        // ------------------------------------------------------------------ //
        //  if (a = b)
        // ------------------------------------------------------------------ //

        private static void CheckIfConditions(List<Tok> toks, List<ValidationIssue> issues)
        {
            for (int i = 0; i + 1 < toks.Count; i++)
            {
                if (!(toks[i].Kind == TokKind.Keyword && toks[i].Text == "if" && toks[i + 1].Kind == TokKind.Op && toks[i + 1].Text == "("))
                    continue;
                int depth = 0;
                for (int k = i + 1; k < toks.Count; k++)
                {
                    Tok t = toks[k];
                    if (t.Kind != TokKind.Op || t.InString) continue;
                    if (t.Text == "(") depth++;
                    else if (t.Text == ")" && --depth == 0) break;
                    else if (t.Text == "=" && depth == 1)
                        issues.Add(Issue("if.assignment", t.Pos, "'=' in an if condition assigns a value; use '==' to compare."));
                }
            }
        }

        // ------------------------------------------------------------------ //
        //  '${x}' is not interpolated
        // ------------------------------------------------------------------ //

        private static void CheckSingleQuoted(string script, List<Tok> toks, List<ValidationIssue> issues)
        {
            foreach (Tok t in toks.Where(t => t.Kind == TokKind.String && t.Text == "'" && !t.InString))
            {
                bool triple = t.Pos + 2 < script.Length && script[t.Pos + 1] == '\'' && script[t.Pos + 2] == '\'';
                int start = t.Pos + (triple ? 3 : 1);
                int end = triple ? script.IndexOf("'''", start, StringComparison.Ordinal) : EndOfSingle(script, start);
                if (end < 0) continue;
                string body = script.Substring(start, end - start);
                if (Regex.IsMatch(body, @"\$\{|\$[A-Za-z_]"))
                    issues.Add(Issue("string.single-quoted-interpolation", t.Pos,
                        "'…' strings are not interpolated: ${…} stays as literal text here. Use \"…\" (or \"\"\"…\"\"\")."));
            }
        }

        private static int EndOfSingle(string s, int i)
        {
            for (; i < s.Length && s[i] != '\n'; i++)
            {
                if (s[i] == '\\') { i++; continue; }
                if (s[i] == '\'') return i;
            }
            return -1;
        }

        // ------------------------------------------------------------------ //
        //  User variables, substitution variables, dimensions, members
        // ------------------------------------------------------------------ //

        private static void CheckMetadata(string script, string code, ValidationContext ctx, List<ValidationIssue> issues)
        {
            EpmMetadata md = ctx?.Metadata;
            if (md == null) return;
            bool InCode(int pos) => pos < code.Length && code[pos] != ' ';   // strings and comments are blanked
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (md.HasVariables)
            {
                // getUserVariable("X"), optionally on getDimension("D")
                foreach (Match m in Regex.Matches(script, @"(?:getDimension\s*\(\s*[""']([^""']+)[""'][^)]*\)\s*\.\s*)?getUserVariable\s*\(\s*[""']([^""']+)[""']"))
                {
                    int at = m.Groups[2].Index - 1;
                    if (!InCode(m.Index)) continue;
                    string name = m.Groups[2].Value;
                    EpmMetadata.UserVariable uv = md.UserVariableNamed(name);
                    if (uv == null)
                    {
                        if (reported.Add("uv:" + name))
                            issues.Add(Issue("uservar.unknown", at,
                                $"'{name}' is not a user variable of the application (as of the last pull, {md.Vars.PulledAt:g})."));
                    }
                    else if (m.Groups[1].Success && !string.Equals(uv.Dimension, m.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(Issue("uservar.wrong-dimension", at,
                            $"User variable '{name}' belongs to {uv.Dimension}, not {m.Groups[1].Value}."));
                    }
                }

                // getSubstitutionVariable("X") / getSubstitutionVariableValue("X")
                foreach (Match m in Regex.Matches(script, @"getSubstitutionVariable(?:Value)?\s*\(\s*[""']([^""']+)[""']"))
                {
                    if (!InCode(m.Index)) continue;
                    ReportSubVar(md, m.Groups[1].Value, ctx.Cube, m.Groups[1].Index - 1, reported, issues);
                }

                // &Variables inside strings (calc script text built by the rule)
                foreach (Match m in Regex.Matches(script, @"&([A-Za-z_]\w*)"))
                {
                    if (InCode(m.Index) || InComment(script, m.Index)) continue;
                    ReportSubVar(md, m.Groups[1].Value, ctx.Cube, m.Index, reported, issues);
                }
            }

            if (md.HasDimensions)
            {
                foreach (Match m in Regex.Matches(script, @"getDimension\s*\(\s*[""']([^""']+)[""']"))
                {
                    if (!InCode(m.Index)) continue;
                    string dim = m.Groups[1].Value;
                    if (!md.DimensionExists(dim) && reported.Add("dim:" + dim))
                        issues.Add(Issue("dimension.unknown", m.Groups[1].Index - 1,
                            $"'{dim}' is not a dimension of the application (dimensions downloaded {md.Dims.PulledAt:g})."));
                }
                foreach (Match m in Regex.Matches(script, @"getDimension\s*\(\s*[""']([^""']+)[""'][^)]*\)\s*\.\s*getMember\s*\(\s*[""']([^""']+)[""']"))
                {
                    if (!InCode(m.Index)) continue;
                    string dim = m.Groups[1].Value, mbr = m.Groups[2].Value;
                    if (!md.DimensionExists(dim)) continue;
                    if (!md.DimensionsOfMember(mbr).Any(d => string.Equals(d, dim, StringComparison.OrdinalIgnoreCase)) && reported.Add("m:" + mbr))
                        issues.Add(Issue("member.unknown", m.Groups[2].Index - 1,
                            $"'{mbr}' is not a member or alias of {dim} (dimensions downloaded {md.Dims.PulledAt:g})."));
                }
            }
        }

        private static void ReportSubVar(EpmMetadata md, string name, string cube, int pos, HashSet<string> reported, List<ValidationIssue> issues)
        {
            if (!reported.Add("sv:" + name)) return;
            string problem = CalcScriptChecks.SubVarProblem(md, name, cube);
            if (problem != null)
                issues.Add(Issue(problem.StartsWith("exists") ? "subvar.wrong-cube" : "subvar.unknown", pos,
                                 $"Substitution variable '{name}' {problem}"));
        }

        private static bool InComment(string s, int pos)
        {
            int open = s.LastIndexOf("/*", pos, StringComparison.Ordinal);
            if (open >= 0 && s.IndexOf("*/", open, StringComparison.Ordinal) is int close && (close < 0 || close > pos)) return true;
            int lineStart = s.LastIndexOf('\n', pos) + 1;
            int sl = s.IndexOf("//", lineStart, StringComparison.Ordinal);
            return sl >= 0 && sl < pos && s.Substring(lineStart, sl - lineStart).Count(c => c == '"') % 2 == 0;
        }

        private static ValidationIssue Issue(string rule, int pos, string message) =>
            new ValidationIssue { RuleId = rule, Pos = pos, Message = message };
    }
}
