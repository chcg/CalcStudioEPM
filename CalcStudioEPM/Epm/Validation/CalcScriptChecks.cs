using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CalcStudioEPM.Config;

namespace CalcStudioEPM.Epm.Validation
{
    /// <summary>
    /// More Calc Script checks, on the tokens of <see cref="CalcScriptLexer"/>:
    ///   template.*                        %Template calls: template pulled, arguments present, DTPs declared
    ///   block.command-in-member-block     FIX / SET / DATACOPY / AGG … inside "Mbr"( … )
    ///   if.assignment                     = instead of == in an IF condition
    ///   fixparallel.nested, threadvar.outside-fixparallel
    ///   datacopy.missing-to, fix.empty
    ///   text.smart-characters             “ ” ‘ ’ – — and non-breaking spaces in code
    ///   subvar.unknown, subvar.wrong-cube &amp;variables against the pulled substitution variables
    ///   member.unknown(-unquoted)         member names against the pulled dimensions (when downloaded)
    ///   syntax.missing-semicolon          a statement not ended with ';' (blocks and "Mbr"( … ) need none)
    /// </summary>
    internal static class CalcScriptChecks
    {
        // Commands that can't run inside a member block (only formulas, IF and LOOP can)
        private static readonly HashSet<string> NotInMemberBlock = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FIX", "FIXPARALLEL", "EXCLUDE", "SET", "CALC", "AGG", "CLEARDATA", "CLEARBLOCK", "DATACOPY",
            "DATAEXPORT", "DATAEXPORTCOND", "DATAIMPORTBIN", "DATAMERGE", "CCONV", "CLEARCCTRACK",
        };

        // Functions whose arguments are members (or member lists / dimension names)
        internal static readonly HashSet<string> MemberFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RELATIVE", "CHILDREN", "ICHILDREN", "DESCENDANTS", "IDESCENDANTS", "LDESCENDANTS", "ILDESCENDANTS",
            "RDESCENDANTS", "IRDESCENDANTS", "ANCESTORS", "IANCESTORS", "ALLANCESTORS", "IALLANCESTORS",
            "LANCESTORS", "ILANCESTORS", "SIBLINGS", "ISIBLINGS", "LSIBLINGS", "RSIBLINGS", "ILSIBLINGS",
            "IRSIBLINGS", "PARENT", "LIST", "REMOVE", "INTERSECT", "MERGE", "RANGE", "XRANGE", "ISMBR", "ISDESC",
            "ISIDESC", "ISCHILD", "ISICHILD", "ISANCEST", "ISIANCEST", "ISPARENT", "ISIPARENT", "ISSIBLING",
            "ISISIBLING", "ISSAMEGEN", "ISSAMELEV", "PRIOR", "NEXT", "PRIORS", "NEXTS", "SHIFT", "SUMRANGE",
            "AVGRANGE", "MAXRANGE", "MINRANGE", "MAXSRANGE", "MINSRANGE", "CURRMBRRANGE", "GENMBRS", "LEVMBRS",
            "PARENTVAL", "ANCESTVAL", "SANCESTVAL", "SPARENTVAL", "NAME", "ATTRIBUTE", "ACCUM", "PTD",
        };

        // Statements whose strings are file names, delimiters or options, not members
        internal static readonly HashSet<string> NonMemberStatements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "DATAEXPORT", "DATAEXPORTCOND", "DATAIMPORTBIN", "SET", "VAR", "ARRAY", "THREADVAR" };

        public static void Run(string script, List<CsTok> toks, CalcScriptCatalog catalog, ValidationContext ctx,
                               bool isTemplate, List<ValidationIssue> issues)
        {
            CheckTemplates(toks, ctx, issues);
            CheckMemberBlocks(toks, catalog, issues);
            CheckIfConditions(toks, issues);
            CheckFixParallel(toks, issues);
            CheckCommands(toks, issues);
            CheckSmartCharacters(script, issues);
            CheckSubstitutionVariables(toks, ctx, issues);
            CheckMembers(toks, catalog, ctx, issues);
            CheckStatementEnds(script, toks, catalog, isTemplate, issues);
        }

        // ------------------------------------------------------------------ //
        //  %Template(name:="…", application:="…", plantype:="…", dtps:=( "dtp":=[[…]], … ))
        // ------------------------------------------------------------------ //

        private static readonly Regex DtpDeclaration = new Regex("<template_prompt name=\"([^\"]+)\"");

        private static void CheckTemplates(List<CsTok> toks, ValidationContext ctx, List<ValidationIssue> issues)
        {
            for (int i = 0; i + 2 < toks.Count; i++)
            {
                if (!(IsOp(toks[i], "%") && toks[i + 1].Kind == CsKind.Word &&
                      string.Equals(toks[i + 1].Text, "Template", StringComparison.OrdinalIgnoreCase) && IsOp(toks[i + 2], "(")))
                    continue;
                int close = MatchParen(toks, i + 2);
                if (close < 0) continue;

                // name:="…" arguments at the top level of the call
                var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var passed = new List<CsTok>();
                int depth = 0;
                for (int k = i + 3; k < close; k++)
                {
                    if (IsOp(toks[k], "(")) depth++;
                    else if (IsOp(toks[k], ")")) depth--;
                    else if (depth == 0 && toks[k].Kind == CsKind.Word && k + 3 < close && IsOp(toks[k + 1], ":") &&
                             IsOp(toks[k + 2], "=") && toks[k + 3].Kind == CsKind.String)
                        args[toks[k].Text] = toks[k + 3].Text;
                    else if (depth == 1 && toks[k].Kind == CsKind.String && k + 2 < close && IsOp(toks[k + 1], ":") && IsOp(toks[k + 2], "="))
                        passed.Add(toks[k]);                                  // "dtpName":=[[value]]
                }

                CsTok at = toks[i];
                var missing = new[] { "name", "application", "plantype" }.Where(a => !args.ContainsKey(a)).ToList();
                if (missing.Count > 0)
                {
                    issues.Add(Issue("template.missing-argument", at.Pos,
                        $"%Template needs {string.Join(", ", missing.Select(m => m + ":=\"…\""))}."));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(ctx?.LocalFolder)) continue;

                string calcRoot = Path.Combine(ctx.LocalFolder, PathConstants.CalcManagerDirName, "CALC-Calculation Manager");
                if (!Directory.Exists(calcRoot)) continue;                    // nothing pulled to compare with
                string file = Path.Combine(calcRoot, "resource", PathConstants.PlanningDirName, args["application"],
                                           args["plantype"], "Templates", PathConstants.ToFileName(args["name"]));
                if (!File.Exists(file))
                {
                    issues.Add(Issue("template.not-found", at.Pos,
                        $"Template '{args["name"]}' ({args["application"]} / {args["plantype"]}) is not in the last pull. " +
                        "Check the name, or pull again if it was created since."));
                    continue;
                }

                HashSet<string> declared;
                try
                {
                    declared = new HashSet<string>(DtpDeclaration.Matches(File.ReadAllText(file, Encoding.UTF8))
                                                       .Cast<Match>().Select(m => m.Groups[1].Value), StringComparer.OrdinalIgnoreCase);
                }
                catch { continue; }
                foreach (CsTok dtp in passed.Where(p => !declared.Contains(p.Text)))
                    issues.Add(Issue("template.unknown-dtp", dtp.Pos,
                        $"Template '{args["name"]}' has no design-time prompt '{dtp.Text}'" +
                        (declared.Count == 0 ? " (it declares none)." : $". It declares: {string.Join(", ", declared.OrderBy(x => x))}.") +
                        " The value passed here is ignored."));
            }
        }

        // ------------------------------------------------------------------ //
        //  "Mbr"( … ) member blocks may only hold formulas, IF and LOOP
        // ------------------------------------------------------------------ //

        private static void CheckMemberBlocks(List<CsTok> toks, CalcScriptCatalog catalog, List<ValidationIssue> issues)
        {
            var keywords = Keywords(catalog);
            for (int i = 0; i + 1 < toks.Count; i++)
            {
                CsTok t = toks[i];
                bool head = t.Kind == CsKind.String || (t.Kind == CsKind.Word && !keywords.Contains(t.Text));
                if (!head || !IsOp(toks[i + 1], "(") || (i > 0 && !StatementStart(toks, i))) continue;

                int close = MatchParen(toks, i + 1);
                if (close < 0) continue;
                int depth = 0;
                for (int k = i + 2; k < close; k++)
                {
                    if (IsOp(toks[k], "(")) depth++;
                    else if (IsOp(toks[k], ")")) depth--;
                    else if (depth == 0 && toks[k].Kind == CsKind.Word && NotInMemberBlock.Contains(toks[k].Text) &&
                             StatementStart(toks, k))
                        issues.Add(Issue("block.command-in-member-block", toks[k].Pos,
                            $"{toks[k].Text.ToUpperInvariant()} can't be used inside the member block {Show(t)}( … ): " +
                            "a member block holds formulas, IF and LOOP only."));
                }
                i = close;
            }
        }

        // ------------------------------------------------------------------ //
        //  IF (a = b) — = assigns; comparisons use ==
        // ------------------------------------------------------------------ //

        private static void CheckIfConditions(List<CsTok> toks, List<ValidationIssue> issues)
        {
            for (int i = 0; i + 1 < toks.Count; i++)
            {
                if (!(toks[i].Kind == CsKind.Word && (Is(toks[i], "IF") || Is(toks[i], "ELSEIF")) && IsOp(toks[i + 1], "(")))
                    continue;
                int close = MatchParen(toks, i + 1);
                for (int k = i + 2; k < close; k++)
                    if (IsOp(toks[k], "="))
                        issues.Add(Issue("if.assignment", toks[k].Pos,
                            $"'=' in an {toks[i].Text.ToUpperInvariant()} condition assigns a value; use '==' to compare."));
            }
        }

        // ------------------------------------------------------------------ //
        //  FIXPARALLEL can't nest; THREADVAR only makes sense with FIXPARALLEL
        // ------------------------------------------------------------------ //

        private static void CheckFixParallel(List<CsTok> toks, List<ValidationIssue> issues)
        {
            int depth = 0;
            bool any = false;
            foreach (CsTok t in toks.Where(t => t.Kind == CsKind.Word))
            {
                if (Is(t, "FIXPARALLEL"))
                {
                    any = true;
                    if (depth > 0)
                        issues.Add(Issue("fixparallel.nested", t.Pos, "FIXPARALLEL can't be nested inside another FIXPARALLEL."));
                    depth++;
                }
                else if (Is(t, "ENDFIXPARALLEL") && depth > 0) depth--;
            }
            if (any) return;
            for (int i = 0; i < toks.Count; i++)
                if (toks[i].Kind == CsKind.Word && Is(toks[i], "THREADVAR") && StatementStart(toks, i))
                    issues.Add(Issue("threadvar.outside-fixparallel", toks[i].Pos,
                        "THREADVAR declares variables for FIXPARALLEL threads, but this script has no FIXPARALLEL — use VAR."));
        }

        // ------------------------------------------------------------------ //
        //  DATACOPY a TO b; FIX ( … ) not empty
        // ------------------------------------------------------------------ //

        private static void CheckCommands(List<CsTok> toks, List<ValidationIssue> issues)
        {
            for (int i = 0; i < toks.Count; i++)
            {
                CsTok t = toks[i];
                if (t.Kind != CsKind.Word || !StatementStart(toks, i)) continue;

                if (Is(t, "DATACOPY"))
                {
                    bool to = false;
                    for (int k = i + 1; k < toks.Count && !IsOp(toks[k], ";"); k++)
                        if (toks[k].Kind == CsKind.Word && Is(toks[k], "TO")) { to = true; break; }
                    if (!to)
                        issues.Add(Issue("datacopy.missing-to", t.Pos, "DATACOPY needs a source and a target: DATACOPY \"From\" TO \"To\";"));
                }
                else if ((Is(t, "FIX") || Is(t, "FIXPARALLEL") || Is(t, "EXCLUDE")) &&
                         i + 2 < toks.Count && IsOp(toks[i + 1], "(") && IsOp(toks[i + 2], ")"))
                {
                    issues.Add(Issue("fix.empty", t.Pos, $"{t.Text.ToUpperInvariant()} () has no members."));
                }
            }
        }

        // ------------------------------------------------------------------ //
        //  Every statement ends with ';'. FIX / FIXPARALLEL / EXCLUDE / LOOP / IF blocks,
        //  their ENDs and "Mbr"( … ) member blocks don't need one (it is allowed after them).
        // ------------------------------------------------------------------ //

        private static readonly HashSet<string> BlockOpens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "FIX", "FIXPARALLEL", "EXCLUDE", "LOOP", "IF", "ELSEIF" };
        private static readonly HashSet<string> BlockEnds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "ENDFIX", "ENDFIXPARALLEL", "ENDEXCLUDE", "ENDLOOP", "ENDIF", "ELSE" };
        // Words that join parts of one statement, so a line starting with them continues it
        private static readonly HashSet<string> Joiners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "AND", "OR", "NOT", "TO" };

        /// <summary>
        /// A statement (formula or command) is missing its ';' when, at its own bracket
        /// level, a block word follows it, or a new line starts another statement after a
        /// token that can end one, or its member block closes, or the script ends.
        /// Expressions may span lines: a line starting or ending with an operator continues.
        /// </summary>
        private static void CheckStatementEnds(string script, List<CsTok> toks, CalcScriptCatalog catalog, bool isTemplate,
                                               List<ValidationIssue> issues)
        {
            var commandWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SET" };
            foreach (string cmd in catalog.Commands ?? new List<string>()) commandWords.Add(cmd.Split(' ')[0]);
            var keywords = Keywords(catalog);

            var closers = new Stack<int>();   // the ')' of each open member block
            int open = -1, prev = -1;         // the open statement's first and latest token

            void Missing(string where) =>
                issues.Add(Issue("syntax.missing-semicolon", toks[prev].Pos,
                    $"This statement doesn't end with ';' ({where}). Every formula and command needs one; " +
                    "FIX, IF, LOOP and their ENDs, and \"Member\"( … ) blocks don't."));

            bool CanEnd(CsTok t) =>
                t.Kind == CsKind.String || t.Kind == CsKind.Number || t.Kind == CsKind.Rtp || t.Kind == CsKind.SubVar ||
                t.Kind == CsKind.Missing || t.Kind == CsKind.Planning ||
                (t.Kind == CsKind.Word && !Joiners.Contains(t.Text) && !commandWords.Contains(t.Text)) ||
                IsOp(t, ")") || IsOp(t, "}");

            bool CanStart(CsTok t) =>
                t.Kind == CsKind.String || t.Kind == CsKind.Rtp || t.Kind == CsKind.Function || IsOp(t, "%") ||
                (t.Kind == CsKind.Word && !Joiners.Contains(t.Text));

            int i = 0;
            while (i < toks.Count)
            {
                CsTok t = toks[i];

                if (closers.Count > 0 && i == closers.Peek())          // a member block closes
                {
                    if (open >= 0) Missing("the member block closes on the next ')'");
                    open = -1;
                    closers.Pop();
                    i++;
                    continue;
                }
                if (IsOp(t, ";")) { open = -1; i++; continue; }

                if (open >= 0)
                {
                    bool blockWord = t.Kind == CsKind.Word && (BlockOpens.Contains(t.Text) || BlockEnds.Contains(t.Text)) &&
                                     !IsOp(toks[prev], "->");
                    if (blockWord)
                    {
                        Missing($"{t.Text.ToUpperInvariant()} follows it");
                        open = -1;
                        continue;                                         // t starts the next statement
                    }
                    if (CanEnd(toks[prev]) && CanStart(t) && NewLineBetween(script, toks[prev], t))
                    {
                        Missing("the next line starts another statement");
                        open = -1;
                        continue;
                    }
                    if (IsOp(t, "(") || IsOp(t, "{"))                     // brackets belong to the statement
                    {
                        int close = IsOp(t, "(") ? MatchParen(toks, i) : MatchBrace(toks, i);
                        if (close < 0) return;                            // unbalanced: reported elsewhere
                        prev = close;
                        i = close + 1;
                        continue;
                    }
                    prev = i;
                    i++;
                    continue;
                }

                // ---- a statement starts at t ----
                if (t.Kind == CsKind.Word && BlockOpens.Contains(t.Text))
                {
                    i++;
                    if (i < toks.Count && IsOp(toks[i], "("))             // FIX ( … ) / IF ( … ) header
                    {
                        int close = MatchParen(toks, i);
                        if (close < 0) return;
                        i = close + 1;
                    }
                    continue;
                }
                if (t.Kind == CsKind.Word && BlockEnds.Contains(t.Text)) { i++; continue; }
                if (IsOp(t, "%") && i + 2 < toks.Count && Is(toks[i + 1], "Template") && IsOp(toks[i + 2], "("))
                {
                    int close = MatchParen(toks, i + 2);                  // the template brings its own statements
                    if (close < 0) return;
                    i = close + 1;
                    continue;
                }
                bool head = t.Kind == CsKind.String || t.Kind == CsKind.Rtp || (t.Kind == CsKind.Word && !keywords.Contains(t.Text));
                if (head && i + 1 < toks.Count && IsOp(toks[i + 1], "("))
                {
                    int close = MatchParen(toks, i + 1);                  // "Member"( … ) block
                    if (close < 0) return;
                    closers.Push(close);
                    i += 2;
                    continue;
                }
                open = prev = i;                                          // a formula or command
            }
            if (open >= 0 && !isTemplate) Missing("the script ends");
        }

        private static bool NewLineBetween(string script, CsTok a, CsTok b) =>
            b.Pos > a.Pos && script.IndexOf('\n', a.Pos, b.Pos - a.Pos) >= 0;

        private static int MatchBrace(List<CsTok> toks, int open)
        {
            int depth = 0;
            for (int k = open; k < toks.Count; k++)
            {
                if (IsOp(toks[k], "{")) depth++;
                else if (IsOp(toks[k], "}") && --depth == 0) return k;
            }
            return -1;
        }

        // ------------------------------------------------------------------ //
        //  Characters pasted from Word / Outlook
        // ------------------------------------------------------------------ //

        private static readonly Dictionary<char, string> Smart = new Dictionary<char, string>
        {
            ['“'] = "“ (curly quote) — use \"", ['”'] = "” (curly quote) — use \"",
            ['‘'] = "‘ (curly quote) — use '",  ['’'] = "’ (curly quote) — use '",
            ['–'] = "– (en dash) — use -",       ['—'] = "— (em dash) — use -",
            [' '] = "a non-breaking space — use a normal space",
        };

        private static void CheckSmartCharacters(string script, List<ValidationIssue> issues)
        {
            // Outside comments and "member names" only: there they are just text
            string code = Regex.Replace(script, @"/\*.*?\*/|//[^\n]*|""[^""\n]*""", m => new string(' ', m.Length), RegexOptions.Singleline);
            for (int i = 0; i < code.Length; i++)
                if (Smart.TryGetValue(code[i], out string what))
                    issues.Add(Issue("text.smart-characters", i, $"Found {what}. Essbase doesn't accept it in calc script code."));
        }

        // ------------------------------------------------------------------ //
        //  &Substitution variables
        // ------------------------------------------------------------------ //

        private static void CheckSubstitutionVariables(List<CsTok> toks, ValidationContext ctx, List<ValidationIssue> issues)
        {
            EpmMetadata md = ctx?.Metadata;
            if (md == null || !md.HasVariables) return;
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CsTok t in toks.Where(t => t.Kind == CsKind.SubVar))
            {
                if (!reported.Add(t.Text)) continue;
                string problem = SubVarProblem(md, t.Text, ctx.Cube);
                if (problem != null) issues.Add(Issue(problem.StartsWith("exists") ? "subvar.wrong-cube" : "subvar.unknown", t.Pos,
                                                      $"&{t.Text} {problem}"));
            }
        }

        /// <summary>Null when fine, else what is wrong ("does not exist …" / "exists only for …").</summary>
        internal static string SubVarProblem(EpmMetadata md, string name, string cube)
        {
            var found = md.SubstitutionVariablesNamed(name);
            if (found.Count == 0)
                return $"is not a substitution variable of the application (as of the last pull, {md.Vars.PulledAt:g}).";
            if (string.IsNullOrEmpty(cube) ||
                found.Any(v => string.Equals(v.PlanType, "ALL", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(v.PlanType, cube, StringComparison.OrdinalIgnoreCase)))
                return null;
            return $"exists only for {string.Join(", ", found.Select(v => v.PlanType))}, not for {cube}.";
        }

        // ------------------------------------------------------------------ //
        //  Member names (when dimensions were downloaded)
        // ------------------------------------------------------------------ //

        private static void CheckMembers(List<CsTok> toks, CalcScriptCatalog catalog, ValidationContext ctx, List<ValidationIssue> issues)
        {
            EpmMetadata md = ctx?.Metadata;
            if (md == null || !md.HasDimensions) return;

            var keywords = Keywords(catalog);
            var variables = DeclaredVariables(toks);
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var functions = new Stack<(string Name, int Depth)>();
            int depth = 0;
            bool skipStatement = false;
            int braces = 0;                     // SET DATAEXPORTOPTIONS { …; …; };
            int templateUntil = -1;

            for (int i = 0; i < toks.Count; i++)
            {
                CsTok t = toks[i];
                if (IsOp(t, "{")) { braces++; continue; }
                if (IsOp(t, "}")) { braces = Math.Max(0, braces - 1); continue; }
                if (braces == 0 && StatementStart(toks, i) && t.Kind == CsKind.Word) skipStatement = NonMemberStatements.Contains(t.Text);
                if (IsOp(t, ";")) { if (braces == 0) skipStatement = false; continue; }
                if (braces > 0) continue;
                if (IsOp(t, "%") && i + 2 < toks.Count && Is(toks[i + 1], "Template")) { templateUntil = MatchParen(toks, i + 2); continue; }
                if (i <= templateUntil) continue;

                if (IsOp(t, "("))
                {
                    depth++;
                    if (i > 0 && toks[i - 1].Kind == CsKind.Function) functions.Push((toks[i - 1].Text, depth));
                    continue;
                }
                if (IsOp(t, ")"))
                {
                    if (functions.Count > 0 && functions.Peek().Depth == depth) functions.Pop();
                    depth--;
                    continue;
                }
                if (skipStatement) continue;

                // Only where members belong: outside functions, or inside member functions
                bool memberContext = functions.Count == 0 || MemberFunctions.Contains(functions.Peek().Name);
                if (!memberContext) continue;

                string name;
                string rule;
                if (t.Kind == CsKind.String)
                {
                    name = t.Text;
                    // "[dtp]_Reporting", "{Prompt}", "${groovy}", qualified "[Dim].[Mbr]": not plain member names
                    if (name.Length == 0 || name.IndexOfAny(new[] { '[', '{', '$', '&' }) >= 0) continue;
                    rule = "member.unknown";
                }
                else if (t.Kind == CsKind.Word)
                {
                    name = t.Text;
                    if (keywords.Contains(name) || variables.Contains(name)) continue;
                    if (i > 0 && IsOp(toks[i - 1], "[")) continue;                               // [dtp]
                    if (i + 1 < toks.Count && IsOp(toks[i + 1], ":")) continue;                  // name:=
                    rule = "member.unknown-unquoted";
                }
                else continue;

                bool cubeKnown = md.HasCube(ctx.Cube);
                if (md.DimensionsOfMember(name, cubeKnown ? ctx.Cube : null).Count > 0 || !reported.Add(rule + ":" + name)) continue;
                IReadOnlyList<string> elsewhere = cubeKnown ? md.DimensionsOfMember(name) : new string[0];
                if (elsewhere.Count > 0)
                {
                    var cubes = md.Dims.ByCube.Where(c => c.Value.Values.Any(d => d.Members.Contains(name, StringComparer.OrdinalIgnoreCase)))
                                              .Select(c => c.Key).OrderBy(c => c);
                    issues.Add(Issue("member.not-in-cube", t.Pos,
                        $"{(t.Kind == CsKind.String ? $"\"{name}\"" : name)} is a member of {string.Join(" / ", elsewhere)} " +
                        $"but not valid in {ctx.Cube} (it is in {string.Join(", ", cubes)})."));
                    continue;
                }
                issues.Add(Issue(rule, t.Pos,
                    $"{(t.Kind == CsKind.String ? $"\"{name}\"" : name)} is not a member or alias of any dimension " +
                    $"(dimensions downloaded {md.Dims.PulledAt:g})."));
            }
        }

        /// <summary>Command words, SET options and values, block words, logical operators, function constants.</summary>
        private static HashSet<string> Keywords(CalcScriptCatalog c)
        {
            var k = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SET", "TO", "AND", "OR", "NOT", "ON", "OFF", "HIGH", "LOW", "DEFAULT", "LEVEL0", "INPUT", "ALL",
                "SKIPNONE", "SKIPMISSING", "SKIPZERO", "SKIPBOTH", "CELL", "BLOCK", "TOPDOWN", "BOTTOMUP", "GEN", "LEV",
                "TRUE", "FALSE", "VAR", "ARRAY", "THREADVAR", "Template",
            };
            foreach (string cmd in c.Commands ?? new List<string>()) foreach (string w in cmd.Split(' ')) k.Add(w);
            foreach (string s in c.SetOptions ?? new List<string>()) k.Add(s);
            foreach (CalcBlock b in c.Blocks ?? new List<CalcBlock>())
            {
                k.Add(b.Open); k.Add(b.Close);
                foreach (string m in b.Middle ?? new List<string>()) k.Add(m);
            }
            return k;
        }

        /// <summary>Names declared with VAR / ARRAY / THREADVAR.</summary>
        private static HashSet<string> DeclaredVariables(List<CsTok> toks)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < toks.Count; i++)
            {
                if (!(toks[i].Kind == CsKind.Word && (Is(toks[i], "VAR") || Is(toks[i], "ARRAY") || Is(toks[i], "THREADVAR")))) continue;
                for (int k = i + 1; k < toks.Count && !IsOp(toks[k], ";"); k++)
                {
                    if (toks[k].Kind == CsKind.Word && (IsOp(toks[k - 1], ",") || k == i + 1)) names.Add(toks[k].Text);
                    if (IsOp(toks[k], "=") || IsOp(toks[k], "[")) // skip initialisers / array sizes up to the next comma
                        while (k + 1 < toks.Count && !IsOp(toks[k + 1], ",") && !IsOp(toks[k + 1], ";")) k++;
                }
            }
            return names;
        }

        // ------------------------------------------------------------------ //

        private static bool IsOp(CsTok t, string op) => t.Kind == CsKind.Op && t.Text == op;
        private static bool Is(CsTok t, string word) => string.Equals(t.Text, word, StringComparison.OrdinalIgnoreCase);
        private static string Show(CsTok t) =>
            t.Kind == CsKind.String ? $"\"{t.Text}\"" : t.Kind == CsKind.Function ? "@" + t.Text :
            t.Kind == CsKind.Rtp ? "{" + t.Text + "}" : t.Kind == CsKind.SubVar ? "&" + t.Text : t.Text;

        /// <summary>Token <paramref name="i"/> starts a statement: first, or after ; ( ) or a block keyword line.</summary>
        private static bool StatementStart(List<CsTok> toks, int i)
        {
            if (i == 0) return true;
            CsTok p = toks[i - 1];
            if (p.Kind == CsKind.Op && (p.Text == ";" || p.Text == "(" || p.Text == ")")) return true;
            return p.Kind == CsKind.Word && (Is(p, "ENDFIX") || Is(p, "ENDFIXPARALLEL") || Is(p, "ENDEXCLUDE") ||
                                              Is(p, "ENDLOOP") || Is(p, "ENDIF") || Is(p, "ELSE"));
        }

        private static int MatchParen(List<CsTok> toks, int open)
        {
            int depth = 0;
            for (int k = open; k < toks.Count; k++)
            {
                if (IsOp(toks[k], "(")) depth++;
                else if (IsOp(toks[k], ")") && --depth == 0) return k;
            }
            return -1;
        }

        private static ValidationIssue Issue(string rule, int pos, string message) =>
            new ValidationIssue { RuleId = rule, Pos = pos, Message = message };
    }
}
