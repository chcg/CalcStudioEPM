using System.Collections.Generic;

namespace CalcStudioEPM.Epm.Validation
{
    internal enum CsKind
    {
        Word,       // command keyword or unquoted member name: FIX, ENDIF, Jan, FY26
        Function,   // @RELATIVE
        String,     // "Member Name"
        Number,
        Rtp,        // {RTP_Name}            — runtime prompt
        SubVar,     // &CurYear              — substitution variable
        Planning,   // [[Period("FIRST")]]   — Planning formula expression (opaque)
        Missing,    // #Missing / #MI
        Op,
    }

    internal sealed class CsTok
    {
        public CsKind Kind;
        public string Text;   // for Rtp/SubVar/Function: the name without its marker
        public int    Pos;
        public override string ToString() => $"{Kind}:{Text}@{Pos}";
    }

    /// <summary>
    /// Tokenizer for Essbase / Calculation Manager calc scripts. /* */ and // comments
    /// are dropped; "quoted members", {runtime prompts}, &amp;substitution variables and
    /// [[Planning expressions]] each become a single token. Unterminated strings,
    /// comments, prompts and expressions are reported as issues.
    /// </summary>
    internal sealed class CalcScriptLexer
    {
        private static readonly string[] MultiCharOps = { "->", "==", "<>", "!=", "<=", ">=" };

        private readonly string _s;
        public readonly List<CsTok>           Tokens = new List<CsTok>();
        public readonly List<ValidationIssue> Issues = new List<ValidationIssue>();

        private CalcScriptLexer(string s) { _s = s ?? ""; }

        public static CalcScriptLexer Run(string script)
        {
            var lexer = new CalcScriptLexer(script);
            lexer.Lex();
            return lexer;
        }

        private void Lex()
        {
            int i = 0, end = _s.Length;
            while (i < end)
            {
                char c = _s[i];
                char n = i + 1 < end ? _s[i + 1] : '\0';

                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (c == '/' && n == '*')
                {
                    int close = _s.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                    if (close < 0) { Issue("syntax.unterminated-comment", i, "This /* comment is never closed with */."); return; }
                    i = close + 2;
                    continue;
                }
                if (c == '/' && n == '/') { while (i < end && _s[i] != '\n') i++; continue; }

                if (c == '"')
                {
                    int k = i + 1;
                    while (k < end && _s[k] != '"' && _s[k] != '\n') k++;
                    if (k >= end || _s[k] != '"')
                    {
                        Issue("syntax.unterminated-string", i, "This \"quoted name\" is not closed before the end of the line.");
                        i = k;
                        continue;
                    }
                    Add(CsKind.String, _s.Substring(i + 1, k - i - 1), i);
                    i = k + 1;
                    continue;
                }

                // {Name} on one line is a runtime prompt; any other brace (e.g. the
                // SET DATAEXPORTOPTIONS { … }; block) is ordinary punctuation
                if (c == '{')
                {
                    int k = i + 1;
                    while (k < end && (char.IsLetterOrDigit(_s[k]) || _s[k] == '_' || _s[k] == ' ' || _s[k] == '\t')) k++;
                    string inner = _s.Substring(i + 1, k - i - 1).Trim();
                    if (k < end && _s[k] == '}' && inner.Length > 0 && inner.IndexOf(' ') < 0 && inner.IndexOf('\t') < 0)
                    {
                        Add(CsKind.Rtp, inner, i);
                        i = k + 1;
                        continue;
                    }
                }

                if (c == '[' && n == '[')
                {
                    int close = _s.IndexOf("]]", i + 2, System.StringComparison.Ordinal);
                    if (close < 0) { Issue("syntax.unterminated-expression", i, "This [[Planning expression is never closed with ]]."); return; }
                    Add(CsKind.Planning, _s.Substring(i + 2, close - i - 2), i);
                    i = close + 2;
                    continue;
                }

                if (c == '&' && (char.IsLetter(n) || n == '_')) { i = Word(i + 1, CsKind.SubVar, i); continue; }
                if (c == '@' && (char.IsLetter(n) || n == '_')) { i = Word(i + 1, CsKind.Function, i); continue; }
                if (c == '#' && char.IsLetter(n))              { i = Word(i + 1, CsKind.Missing, i);  continue; }

                if (char.IsDigit(c) || (c == '.' && char.IsDigit(n)))
                {
                    int s0 = i;
                    while (i < end && (char.IsDigit(_s[i]) || _s[i] == '.' ||
                                       ((_s[i] == 'e' || _s[i] == 'E') && i + 1 < end && (char.IsDigit(_s[i + 1]) || _s[i + 1] == '-' || _s[i + 1] == '+')) ||
                                       ((_s[i] == '-' || _s[i] == '+') && (_s[i - 1] == 'e' || _s[i - 1] == 'E'))))
                        i++;
                    // Unquoted member names may start with a digit (e.g. 2024_Plan)
                    if (i < end && (char.IsLetter(_s[i]) || _s[i] == '_')) { i = Word(s0, CsKind.Word, s0); continue; }
                    Add(CsKind.Number, _s.Substring(s0, i - s0), s0);
                    continue;
                }

                if (char.IsLetter(c) || c == '_') { i = Word(i, CsKind.Word, i); continue; }

                string op = null;
                foreach (string candidate in MultiCharOps)
                    if (i + candidate.Length <= end && string.CompareOrdinal(_s, i, candidate, 0, candidate.Length) == 0)
                    { op = candidate; break; }
                op = op ?? c.ToString();
                Add(CsKind.Op, op, i);
                i += op.Length;
            }
        }

        /// <summary>Reads [A-Za-z0-9_.]* from <paramref name="start"/>; adds a token at <paramref name="tokPos"/>.</summary>
        private int Word(int start, CsKind kind, int tokPos)
        {
            int i = start;
            while (i < _s.Length && (char.IsLetterOrDigit(_s[i]) || _s[i] == '_')) i++;
            Add(kind, _s.Substring(start, i - start), tokPos);
            return i;
        }

        private void Add(CsKind kind, string text, int pos) =>
            Tokens.Add(new CsTok { Kind = kind, Text = text, Pos = pos });

        private void Issue(string rule, int pos, string message) =>
            Issues.Add(new ValidationIssue { RuleId = rule, Pos = pos, Message = message });
    }
}
