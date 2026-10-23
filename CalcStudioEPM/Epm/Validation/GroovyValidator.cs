using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CalcStudioEPM.Config;

namespace CalcStudioEPM.Epm.Validation
{
    public sealed class ValidationIssue
    {
        public string RuleId;
        /// <summary>"error" or "warning" (rules set to "off" never produce issues).</summary>
        public string Severity;
        public int    Pos;
        public int    Line;     // 1-based
        public int    Column;   // 1-based, in characters
        public string Message;

        public bool IsError => Severity == "error";
    }

    /// <summary>
    /// Local, best-effort checks of a Groovy rule script before it goes to EPM Cloud.
    /// Not a compiler: it tokenizes the script and follows Groovy's scoping rules closely
    /// enough to catch the usual mistakes, with every rule's severity, the known global
    /// names and the restricted patterns taken from <see cref="GroovyValidationConfig"/>.
    /// </summary>
    public static class GroovyValidator
    {
        private static readonly HashSet<string> PrimitiveTypes = new HashSet<string>
            { "int", "long", "double", "float", "boolean", "char", "byte", "short", "void" };

        private static readonly HashSet<string> MemberAccessOps = new HashSet<string>
            { ".", "?.", "*.", ".&", ".@", "?.@", "::" };

        private static readonly HashSet<string> Modifiers = new HashSet<string>
            { "public", "private", "protected", "static", "final", "synchronized", "abstract", "transient", "volatile" };

        /// <param name="context">Metadata and cube for the checks that need them (optional).</param>
        public static List<ValidationIssue> Validate(string script, GroovyValidationConfig cfg,
                                                     VariableDictionary dictionary = null,
                                                     ValidationContext context = null)
        {
            script = script ?? "";
            cfg    = cfg ?? GroovyValidationConfig.Defaults();

            // Rules built in Calculation Manager's graphical designer have no script; Pull
            // then saves the rule's raw XML in the .js file. Nothing to check as Groovy.
            string head = script.TrimStart();
            if (head.StartsWith("<?xml", StringComparison.Ordinal) || head.StartsWith("<HBRRepo", StringComparison.Ordinal))
                return new List<ValidationIssue>
                {
                    new ValidationIssue
                    {
                        RuleId = "not-groovy", Severity = "warning", Pos = 0, Line = 1, Column = 1,
                        Message = "This file holds the rule's XML, not a Groovy script (graphical rule, or an older pull) — there is nothing to validate.",
                    },
                };

            GroovyLexer lexer = GroovyLexer.Run(script);
            var raw = new List<ValidationIssue>(lexer.Issues);

            CheckBrackets(lexer.Tokens, raw);
            new NameChecker(lexer.Tokens, cfg, raw).Run();
            CheckRtps(script, lexer.Tokens, dictionary, raw);
            CheckRestricted(lexer.CodeOnly, cfg, raw);
            GroovyChecks.Run(script, lexer.Tokens, lexer.CodeOnly, cfg, context, raw);

            // Apply severities, drop "off" rules, add line/column, de-duplicate, sort
            int[] lineStarts = LineStarts(script);
            var result = new List<ValidationIssue>();
            var seen   = new HashSet<string>();
            foreach (ValidationIssue issue in raw.OrderBy(x => x.Pos))
            {
                string severity = issue.Severity ?? cfg.SeverityOf(issue.RuleId);
                if (severity != "error" && severity != "warning") continue;
                if (!seen.Add(issue.RuleId + "@" + issue.Pos)) continue;

                issue.Severity = severity;
                int line = Array.BinarySearch(lineStarts, issue.Pos);
                if (line < 0) line = ~line - 1;
                issue.Line   = line + 1;
                issue.Column = issue.Pos - lineStarts[Math.Max(line, 0)] + 1;
                result.Add(issue);
            }
            return result;
        }

        // ------------------------------------------------------------------ //
        //  Brackets
        // ------------------------------------------------------------------ //

        private static void CheckBrackets(List<Tok> toks, List<ValidationIssue> issues)
        {
            var stack = new Stack<Tok>();
            foreach (Tok t in toks)
            {
                if (t.Kind != TokKind.Op) continue;
                switch (t.Text)
                {
                    case "(": case "[": case "{":
                        stack.Push(t);
                        break;

                    case ")": case "]": case "}":
                        string open = t.Text == ")" ? "(" : t.Text == "]" ? "[" : "{";
                        if (stack.Count > 0 && stack.Peek().Text == open) { stack.Pop(); break; }

                        if (stack.Any(s => s.Text == open))
                        {
                            // Something in between was never closed — report it, then resync
                            while (stack.Peek().Text != open)
                            {
                                Tok unclosed = stack.Pop();
                                issues.Add(Issue("syntax.unbalanced-brackets", unclosed.Pos,
                                    $"'{unclosed.Text}' is never closed (expected '{Closer(unclosed.Text)}' before this '{t.Text}').", t));
                            }
                            stack.Pop();
                        }
                        else
                        {
                            issues.Add(Issue("syntax.unbalanced-brackets", t.Pos,
                                $"'{t.Text}' has no matching '{open}'."));
                        }
                        break;
                }
            }
            foreach (Tok unclosed in stack)
                issues.Add(Issue("syntax.unbalanced-brackets", unclosed.Pos,
                    $"'{unclosed.Text}' is never closed with '{Closer(unclosed.Text)}'."));
        }

        private static string Closer(string open) => open == "(" ? ")" : open == "[" ? "]" : "}";

        // ------------------------------------------------------------------ //
        //  Names: undeclared variables, unknown functions, binding assignments
        // ------------------------------------------------------------------ //

        private sealed class Scope
        {
            public readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal);
            public bool IsMethod;   // a method body: script-level locals are not visible past it
            public bool IsClass;    // a class body: its fields stay visible inside its methods
        }

        private sealed class NameChecker
        {
            private readonly List<Tok>             _all;
            private readonly List<Tok>             _t;        // without newlines
            private readonly List<bool>            _lineStart;// _t[i] is first on its line
            private readonly GroovyValidationConfig _cfg;
            private readonly List<ValidationIssue> _issues;

            private readonly HashSet<string> _globals   = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _functions = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _reported  = new HashSet<string>(StringComparer.Ordinal);
            private readonly Dictionary<int, List<string>> _methodParams = new Dictionary<int, List<string>>();
            private readonly HashSet<int> _declSites = new HashSet<int>();
            private readonly List<Scope>  _scopes    = new List<Scope>();
            private bool _nextBraceIsClass;

            public NameChecker(List<Tok> toks, GroovyValidationConfig cfg, List<ValidationIssue> issues)
            {
                _all       = toks;
                _cfg       = cfg;
                _issues    = issues;
                _t         = new List<Tok>();
                _lineStart = new List<bool>();
                bool newLine = true;
                foreach (Tok tok in toks)
                {
                    if (tok.Kind == TokKind.Newline) { newLine = true; continue; }
                    _t.Add(tok);
                    _lineStart.Add(newLine);
                    newLine = false;
                }
                foreach (string g in cfg.KnownGlobals ?? new List<string>())   _globals.Add(g);
                foreach (string f in cfg.KnownFunctions ?? new List<string>()) _functions.Add(f);
            }

            public void Run()
            {
                FindMethods();
                _scopes.Add(new Scope());   // script body

                for (int i = 0; i < _t.Count; i++)
                {
                    Tok tok = _t[i];

                    if (tok.Kind == TokKind.Op && tok.Text == "{" && !tok.InString) { OpenScope(i); continue; }
                    if (tok.Kind == TokKind.Op && tok.Text == "}" && !tok.InString)
                    {
                        if (_scopes.Count > 1) _scopes.RemoveAt(_scopes.Count - 1);
                        continue;
                    }

                    if (tok.Kind == TokKind.Keyword)
                    {
                        switch (tok.Text)
                        {
                            case "import": case "package":
                                while (i + 1 < _t.Count && !_lineStart[i + 1] && _t[i + 1].Text != ";") i++;
                                continue;
                            case "class": case "interface": case "enum": case "trait":
                                _nextBraceIsClass = true;
                                if (Is(i + 1, TokKind.Ident)) { _globals.Add(_t[i + 1].Text); _declSites.Add(i + 1); }
                                continue;
                            case "def": case "var": case "final":
                                DeclareAfterDef(i);
                                continue;
                        }
                        if (PrimitiveTypes.Contains(tok.Text)) DeclareTyped(i);
                        continue;
                    }

                    if (tok.Kind != TokKind.Ident) continue;

                    if (!tok.InString && char.IsUpper(tok.Text[0])) { DeclareTyped(i); continue; }
                    if (_declSites.Contains(i)) continue;
                    CheckReference(i);
                }
            }

            // ---- declarations ----

            /// <summary>Method definitions: their names are callable anywhere; their params live in the body.</summary>
            private void FindMethods()
            {
                for (int i = 0; i + 1 < _t.Count; i++)
                {
                    if (!Is(i, TokKind.Ident) || !IsOp(i + 1, "(")) continue;
                    int prev = i - 1;
                    bool typed = prev >= 0 &&
                        ((_t[prev].Kind == TokKind.Keyword && (_t[prev].Text == "def" || PrimitiveTypes.Contains(_t[prev].Text) || Modifiers.Contains(_t[prev].Text))) ||
                         (_t[prev].Kind == TokKind.Ident && char.IsUpper(_t[prev].Text[0]) && !IsMemberAccess(prev - 1)) ||
                         IsOp(prev, ">") || IsOp(prev, "]"));
                    if (!typed) continue;

                    int close = MatchParen(i + 1);
                    if (close < 0) continue;
                    int body = close + 1;
                    if (Is(body, TokKind.Keyword) && _t[body].Text == "throws")
                        while (body < _t.Count && !IsOp(body, "{")) body++;
                    if (!IsOp(body, "{")) continue;

                    _functions.Add(_t[i].Text);
                    _declSites.Add(i);

                    var names = new List<string>();
                    int depth = 0;
                    for (int k = i + 2; k < close; k++)
                    {
                        if (IsOp(k, "(") || IsOp(k, "[") || IsOp(k, "{") || IsOp(k, "<")) depth++;
                        else if (IsOp(k, ")") || IsOp(k, "]") || IsOp(k, "}") || IsOp(k, ">")) depth--;
                        else if (depth == 0 && Is(k, TokKind.Ident) &&
                                 (IsOp(k + 1, ",") || k + 1 == close || IsOp(k + 1, "=")))
                        {
                            names.Add(_t[k].Text);
                            _declSites.Add(k);
                        }
                    }
                    _methodParams[body] = names;
                }
            }

            private void OpenScope(int i)
            {
                var scope = new Scope { IsClass = _nextBraceIsClass };
                _nextBraceIsClass = false;
                scope.Names.Add("it");

                if (_methodParams.TryGetValue(i, out var ps))
                {
                    scope.IsMethod = true;
                    foreach (string p in ps) scope.Names.Add(p);
                }
                else
                {
                    // Closure parameters: { a, b -> …} / { String s, int n -> …}
                    var candidate = new List<int>();
                    for (int k = i + 1; k < _t.Count; k++)
                    {
                        Tok t = _t[k];
                        if (t.Kind == TokKind.Op && t.Text == "->")
                        {
                            foreach (int c in candidate) { scope.Names.Add(_t[c].Text); _declSites.Add(c); }
                            break;
                        }
                        bool allowed = t.Kind == TokKind.Ident ||
                                       (t.Kind == TokKind.Keyword && (PrimitiveTypes.Contains(t.Text) || t.Text == "def" || t.Text == "final")) ||
                                       (t.Kind == TokKind.Op && (t.Text == "," || t.Text == "<" || t.Text == ">" || t.Text == "[" || t.Text == "]" || t.Text == "." || t.Text == "?"));
                        if (!allowed) break;
                        if (t.Kind == TokKind.Ident && (IsOp(k + 1, ",") || IsOp(k + 1, "->"))) candidate.Add(k);
                    }
                }
                _scopes.Add(scope);
            }

            /// <summary>def x / def (a, b) = … / final x = … / var x</summary>
            private void DeclareAfterDef(int i)
            {
                int k = i + 1;
                while (Is(k, TokKind.Keyword) && Modifiers.Contains(_t[k].Text)) k++;

                if (IsOp(k, "("))   // multiple assignment: def (a, b) = …
                {
                    int close = MatchParen(k);
                    for (int m = k + 1; m < close; m++)
                        if (Is(m, TokKind.Ident) && (IsOp(m + 1, ",") || m + 1 == close)) Declare(m);
                    return;
                }
                // "def x" at the end of a line is a declaration even when the next line starts
                // with a name; "def Type x" on one line is left to the typed rule.
                bool endsLine = k + 1 >= _t.Count || _lineStart[k + 1];
                if (Is(k, TokKind.Ident) && (endsLine || (!IsOp(k + 1, "(") && !Is(k + 1, TokKind.Ident))))
                    DeclareList(k);
                // "final String x" / "def String x" → the typed rule handles it at the type token
            }

            /// <summary>Type name (Capitalised identifier or primitive) followed by a variable name.</summary>
            private void DeclareTyped(int i)
            {
                if (IsMemberAccess(i - 1)) return;
                if (i > 0 && _t[i - 1].Kind == TokKind.Keyword &&
                    (_t[i - 1].Text == "new" || _t[i - 1].Text == "as" || _t[i - 1].Text == "instanceof")) return;

                int k = i + 1;
                while (IsOp(k, ".") && Is(k + 1, TokKind.Ident)) k += 2;          // qualified: java.util.Date
                if (IsOp(k, "<"))                                                // generics: List<Map<String, X>>
                {
                    int depth = 0;
                    for (; k < _t.Count; k++)
                    {
                        if (IsOp(k, "<")) depth++;
                        else if (IsOp(k, ">"))
                        {
                            if (--depth == 0) { k++; break; }   // ">>" closes two levels, one token each
                        }
                        else if (!(Is(k, TokKind.Ident) || IsOp(k, ",") || IsOp(k, "?") || IsOp(k, ".") ||
                                   IsOp(k, "[") || IsOp(k, "]") || (Is(k, TokKind.Keyword) && (_t[k].Text == "extends" || _t[k].Text == "super" || PrimitiveTypes.Contains(_t[k].Text)))))
                            return;   // a comparison, not generics
                    }
                }
                while (IsOp(k, "[") && IsOp(k + 1, "]")) k += 2;                 // arrays: String[]

                if (!Is(k, TokKind.Ident) || _t[k].InString) return;
                int after = k + 1;
                bool ends = after >= _t.Count || _lineStart[after] ||
                            IsOp(after, "=") || IsOp(after, ";") || IsOp(after, ",") || IsOp(after, ")") ||
                            IsOp(after, ":") || IsOp(after, "->") || IsOp(after, "}") ||
                            (Is(after, TokKind.Keyword) && _t[after].Text == "in");
                if (ends && !IsOp(after, "(")) DeclareList(k);
            }

            private void Declare(int k)
            {
                _scopes[_scopes.Count - 1].Names.Add(_t[k].Text);
                _declSites.Add(k);
            }

            /// <summary>
            /// Declares the variable at <paramref name="k"/> plus any further declarators of
            /// the same statement: String a = x, b = y, c   /  def p = 1, q = 2
            /// </summary>
            private void DeclareList(int k)
            {
                Declare(k);
                int depth = 0;
                for (int m = k + 1; m < _t.Count; m++)
                {
                    if (depth == 0 && _lineStart[m]) break;                 // statement ended
                    if (IsOp(m, "(") || IsOp(m, "[") || IsOp(m, "{")) depth++;
                    else if (IsOp(m, ")") || IsOp(m, "]") || IsOp(m, "}")) { if (depth == 0) break; depth--; }
                    else if (depth == 0 && (IsOp(m, ";") || IsOp(m, "->"))) break;
                    else if (depth == 0 && IsOp(m, ",") && Is(m + 1, TokKind.Ident) &&
                             (IsOp(m + 2, "=") || IsOp(m + 2, ",") || IsOp(m + 2, ";") ||
                              m + 2 >= _t.Count || _lineStart[m + 2]))
                        Declare(m + 1);
                }
            }

            // ---- references ----

            private void CheckReference(int i)
            {
                Tok tok = _t[i];
                string name = tok.Text;

                if (IsMemberAccess(i - 1)) return;                       // obj.name
                if (i > 0 && _t[i - 1].Kind == TokKind.Op && _t[i - 1].Text == "@") return;   // annotation
                if (IsOp(i + 1, ":") && !tok.InString) return;          // map key / named arg / label
                if (Is(i + 1, TokKind.Keyword) && _t[i + 1].Text == "in" && i > 0 && IsOp(i - 1, "("))
                { Declare(i); return; }                                  // for (x in list)

                // Calls: name(…) or command style at the start of a statement: println "x"
                bool call = IsOp(i + 1, "(") ||
                            (!tok.InString && (_lineStart[i] || IsOp(i - 1, ";") || IsOp(i - 1, "{") || IsOp(i - 1, "->")) &&
                             i + 1 < _t.Count && !_lineStart[i + 1] &&
                             (_t[i + 1].Kind == TokKind.String || _t[i + 1].Kind == TokKind.Number ||
                              (_t[i + 1].Kind == TokKind.Ident && !IsOp(i + 2, "="))));
                if (call)
                {
                    if (_functions.Contains(name) || IsVisible(name) || _globals.Contains(name)) return;
                    ReportOnce("unknown-function", tok, name,
                        $"'{name}(…)' is not defined in this script and is not a known function.");
                    return;
                }

                // Assignment without def creates a script-wide binding variable
                if (IsOp(i + 1, "=") && !tok.InString && !IsVisible(name))
                {
                    _globals.Add(name);
                    ReportOnce("binding-assignment", tok, name,
                        $"'{name}' is assigned without def — it becomes a script-wide binding variable. Use 'def {name} = …'.");
                    return;
                }

                if (IsVisible(name)) return;

                bool hiddenScriptLocal = _scopes[0].Names.Contains(name);
                ReportOnce("undeclared-variable", tok, name, hiddenScriptLocal
                    ? $"'{name}' is a script-level variable; it is not visible inside a method. Pass it as a parameter."
                    : $"'{name}' is not declared — no def, type or parameter defines it before this line.");
            }

            /// <summary>Visible from the current position: inner scopes outward, stopping at a method boundary (class fields excepted).</summary>
            private bool IsVisible(string name)
            {
                bool crossedMethod = false;
                for (int s = _scopes.Count - 1; s >= 0; s--)
                {
                    Scope scope = _scopes[s];
                    if ((!crossedMethod || scope.IsClass) && scope.Names.Contains(name)) return true;
                    if (scope.IsMethod) crossedMethod = true;
                }
                return _globals.Contains(name);
            }

            private void ReportOnce(string rule, Tok tok, string name, string message)
            {
                if (!_reported.Add(rule + ":" + name)) return;
                _issues.Add(new ValidationIssue { RuleId = rule, Pos = tok.Pos, Message = message });
            }

            // ---- token helpers ----

            private bool Is(int k, TokKind kind) => k >= 0 && k < _t.Count && _t[k].Kind == kind;
            private bool IsOp(int k, string op)  => k >= 0 && k < _t.Count && _t[k].Kind == TokKind.Op && _t[k].Text == op;
            private bool IsMemberAccess(int k)   => k >= 0 && k < _t.Count && _t[k].Kind == TokKind.Op && MemberAccessOps.Contains(_t[k].Text);

            private int MatchParen(int open)
            {
                int depth = 0;
                for (int k = open; k < _t.Count; k++)
                {
                    if (IsOp(k, "(")) depth++;
                    else if (IsOp(k, ")") && --depth == 0) return k;
                }
                return -1;
            }
        }

        // ------------------------------------------------------------------ //
        //  EPM runtime prompts
        // ------------------------------------------------------------------ //

        private static void CheckRtps(string script, List<Tok> toks, VariableDictionary dict, List<ValidationIssue> issues)
        {
            List<string> header = RtpsHeader.Names(script);
            var headerSet = new HashSet<string>(header, StringComparer.OrdinalIgnoreCase);
            var reported  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var code = toks.Where(t => t.Kind != TokKind.Newline).ToList();
            for (int i = 0; i + 2 < code.Count; i++)
            {
                if (!(code[i].Kind == TokKind.Ident && code[i].Text == "rtps" &&
                      code[i + 1].Kind == TokKind.Op && code[i + 1].Text == "." &&
                      code[i + 2].Kind == TokKind.Ident))
                    continue;
                if (i > 0 && code[i - 1].Kind == TokKind.Op && MemberAccessOps.Contains(code[i - 1].Text)) continue;

                Tok   name    = code[i + 2];
                string varName = name.Text;

                if (!headerSet.Contains(varName) && reported.Add("hdr:" + varName))
                    issues.Add(Issue("rtps.not-declared", name.Pos,
                        $"rtps.{varName} is used but {{{varName}}} is not in the /*RTPS: */ header, so Calculation Manager won't prompt for it."));

                // rtps.X.member / rtps.X.members against the prompt's type
                if (dict != null && i + 4 < code.Count && code[i + 3].Text == "." &&
                    code[i + 4].Kind == TokKind.Ident && (code[i + 4].Text == "member" || code[i + 4].Text == "members"))
                {
                    string accessor = code[i + 4].Text;
                    string type     = dict.Find(varName)?.Type;
                    if (type != null && !string.Equals(type, accessor, StringComparison.OrdinalIgnoreCase) &&
                        reported.Add("type:" + varName + accessor))
                        issues.Add(Issue("rtps.type-usage", code[i + 4].Pos,
                            $"rtps.{varName}.{accessor} — {varName} is a {VariableEntry.LabelFor(type)} prompt" +
                            (type == "member" ? ", use .member" : type == "members" ? ", use .members" : ", it has no member(s)") + "."));
                }
            }

            // Header prompts the connection doesn't know
            if (dict != null && dict.Variables.Count > 0)
            {
                Match h = Regex.Match(script, @"/\*\s*RTPS:(.*?)\*/", RegexOptions.Singleline);
                if (h.Success)
                    foreach (Match m in Regex.Matches(h.Groups[1].Value, @"\{(\w+)\}"))
                        if (dict.Find(m.Groups[1].Value) == null)
                            issues.Add(Issue("rtps.unknown-variable", h.Groups[1].Index + m.Index,
                                $"{{{m.Groups[1].Value}}} in the /*RTPS: */ header is not in this connection's variable dictionary — Deploy can't add it as a prompt."));
            }
        }

        // ------------------------------------------------------------------ //
        //  Restricted patterns
        // ------------------------------------------------------------------ //

        private static void CheckRestricted(string codeOnly, GroovyValidationConfig cfg, List<ValidationIssue> issues)
        {
            foreach (RestrictedPattern p in cfg.Restricted ?? new List<RestrictedPattern>())
            {
                string severity = (p.Severity ?? "warning").Trim().ToLowerInvariant();
                if (severity == "off" || string.IsNullOrWhiteSpace(p.Pattern)) continue;

                // User-editable patterns run on the UI thread: a timeout keeps a pathological one
                // from freezing Notepad++. Matches are read here, where the timeout can be caught.
                List<Match> matches;
                try { matches = Regex.Matches(codeOnly, p.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1)).Cast<Match>().ToList(); }
                catch (Exception ex) when (ex is ArgumentException || ex is RegexMatchTimeoutException)
                {
                    issues.Add(new ValidationIssue
                    {
                        RuleId = "restricted", Severity = "warning", Pos = 0,
                        Message = ex is RegexMatchTimeoutException
                            ? $"Restricted pattern '{p.Pattern}' in groovy_validation.json took too long and was skipped."
                            : $"Invalid restricted pattern '{p.Pattern}' in groovy_validation.json: {ex.Message}",
                    });
                    continue;
                }
                foreach (Match m in matches)
                    issues.Add(new ValidationIssue
                    {
                        RuleId = "restricted", Severity = severity, Pos = m.Index,
                        Message = string.IsNullOrWhiteSpace(p.Message) ? $"'{m.Value}' is restricted." : p.Message,
                    });
            }
        }

        // ------------------------------------------------------------------ //

        private static ValidationIssue Issue(string rule, int pos, string message, Tok _ = null) =>
            new ValidationIssue { RuleId = rule, Pos = pos, Message = message };

        private static int[] LineStarts(string s)
        {
            var starts = new List<int> { 0 };
            for (int i = 0; i < s.Length; i++) if (s[i] == '\n') starts.Add(i + 1);
            return starts.ToArray();
        }
    }
}
