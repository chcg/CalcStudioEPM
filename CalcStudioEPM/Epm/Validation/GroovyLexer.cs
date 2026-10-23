using System;
using System.Collections.Generic;

namespace CalcStudioEPM.Epm.Validation
{
    internal enum TokKind { Ident, Keyword, Number, String, Op, Newline }

    internal sealed class Tok
    {
        public TokKind Kind;
        public string  Text;
        public int     Pos;
        /// <summary>True for identifiers found inside a GString ("…${x}…" / "…$x…").</summary>
        public bool    InString;
        public override string ToString() => $"{Kind}:{Text}@{Pos}";
    }

    /// <summary>
    /// A forgiving Groovy tokenizer — just enough of the language for local checks.
    /// Understands '…', "…" (with ${…} and $name interpolation), '''…''', """…""",
    /// /slashy/ and $/dollar-slashy/$ strings, // and /* */ comments, numbers,
    /// identifiers, keywords and operators. Comments are dropped; newlines are kept
    /// (statement boundaries matter in Groovy). Unterminated strings and comments are
    /// reported as issues instead of throwing.
    /// </summary>
    internal sealed class GroovyLexer
    {
        public static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "abstract", "as", "assert", "boolean", "break", "byte", "case", "catch", "char", "class",
            "const", "continue", "def", "default", "do", "double", "else", "enum", "extends", "false",
            "final", "finally", "float", "for", "goto", "if", "implements", "import", "in", "instanceof",
            "int", "interface", "long", "native", "new", "null", "package", "private", "protected",
            "public", "return", "short", "static", "strictfp", "super", "switch", "synchronized", "this",
            "throw", "throws", "trait", "transient", "true", "try", "var", "void", "volatile", "while",
        };

        private static readonly string[] MultiCharOps =
        {
            "==~", "<=>", "...", "..<", "===", "!==", "**=", "<<=", ">>=", "?.@",
            "?.", "*.", ".&", ".@", "::", "->", "==", "!=", "<=", ">=", "&&", "||", "++", "--",
            "+=", "-=", "*=", "/=", "%=", "**", "<<", "?:", "=~", "..", "&=", "|=", "^=",
        };

        private readonly string _s;
        private readonly char[] _code;
        public readonly List<Tok>             Tokens = new List<Tok>();
        public readonly List<ValidationIssue> Issues = new List<ValidationIssue>();

        /// <summary>
        /// The script with every comment and string blanked out (newlines kept, so
        /// positions still line up) — for pattern rules that must only see real code.
        /// </summary>
        public string CodeOnly => new string(_code);

        private GroovyLexer(string s)
        {
            _s    = s ?? "";
            _code = _s.ToCharArray();
        }

        private void Blank(int from, int to)
        {
            for (int k = Math.Max(0, from); k < Math.Min(to, _code.Length); k++)
                if (_code[k] != '\n') _code[k] = ' ';
        }

        public static GroovyLexer Run(string script)
        {
            var lexer = new GroovyLexer(script);
            lexer.Lex(0, lexer._s.Length, inString: false);
            return lexer;
        }

        // ------------------------------------------------------------------ //

        private void Lex(int start, int end, bool inString)
        {
            int i = start;
            while (i < end)
            {
                char c = _s[i];
                char n = i + 1 < end ? _s[i + 1] : '\0';

                if (c == '\n') { Add(TokKind.Newline, "\n", i, inString); i++; continue; }
                if (char.IsWhiteSpace(c)) { i++; continue; }

                // Comments
                if (c == '/' && n == '/')
                {
                    int s0 = i;
                    while (i < end && _s[i] != '\n') i++;
                    Blank(s0, i);
                    continue;
                }
                if (c == '/' && n == '*')
                {
                    int close = _s.IndexOf("*/", i + 2, end - (i + 2), StringComparison.Ordinal);
                    if (close < 0)
                    {
                        Issue("syntax.unterminated-comment", i, "This /* comment is never closed with */.");
                        Blank(i, end);
                        return;
                    }
                    // keep the newlines inside the comment so statement boundaries survive
                    for (int k = i; k < close; k++) if (_s[k] == '\n') Add(TokKind.Newline, "\n", k, inString);
                    Blank(i, close + 2);
                    i = close + 2;
                    continue;
                }
                if (c == '#' && n == '!' && i == 0) { while (i < end && _s[i] != '\n') i++; continue; }

                // Strings (blanked in CodeOnly, interpolations included)
                if (c == '\'' || c == '"') { int s0 = i; i = LexQuoted(i, end, inString); Blank(s0, i); continue; }
                if (c == '$' && n == '/') { int s0 = i; i = LexDollarSlashy(i, end, inString); Blank(s0, i); continue; }
                if (c == '/' && RegexAllowedHere())
                {
                    int after = LexSlashy(i, end, inString);
                    if (after > 0) { Blank(i, after); i = after; continue; }
                    // no closing '/' — it was a division after all; fall through to operators
                }

                // Identifiers / keywords
                if (char.IsLetter(c) || c == '_' || c == '$')
                {
                    int s0 = i;
                    while (i < end && (char.IsLetterOrDigit(_s[i]) || _s[i] == '_' || _s[i] == '$')) i++;
                    string word = _s.Substring(s0, i - s0);
                    Add(Keywords.Contains(word) ? TokKind.Keyword : TokKind.Ident, word, s0, inString);
                    continue;
                }

                // Numbers (decimal, hex, exponents, underscores, type suffixes)
                if (char.IsDigit(c) || (c == '.' && char.IsDigit(n)))
                {
                    int s0 = i;
                    i++;
                    while (i < end && (char.IsLetterOrDigit(_s[i]) || _s[i] == '_' ||
                                       (_s[i] == '.' && i + 1 < end && char.IsDigit(_s[i + 1])) ||
                                       ((_s[i] == '+' || _s[i] == '-') && (_s[i - 1] == 'e' || _s[i - 1] == 'E'))))
                        i++;
                    Add(TokKind.Number, _s.Substring(s0, i - s0), s0, inString);
                    continue;
                }

                // Operators / punctuation (longest match first)
                string op = null;
                foreach (string candidate in MultiCharOps)
                    if (i + candidate.Length <= end && string.CompareOrdinal(_s, i, candidate, 0, candidate.Length) == 0)
                    { op = candidate; break; }
                op = op ?? c.ToString();
                Add(TokKind.Op, op, i, inString);
                i += op.Length;
            }
        }

        /// <summary>'…' "…" '''…''' """…""" — returns the index after the closing quote.</summary>
        private int LexQuoted(int i, int end, bool inString)
        {
            char q = _s[i];
            bool triple = i + 2 < end && _s[i + 1] == q && _s[i + 2] == q;
            bool interpolates = q == '"';
            int start = i;
            Add(TokKind.String, q.ToString(), start, inString);
            i += triple ? 3 : 1;

            while (true)
            {
                if (i >= end)
                {
                    Issue("syntax.unterminated-string", start, $"This string starting with {(triple ? new string(q, 3) : q.ToString())} is never closed.");
                    return end;
                }
                char c = _s[i];
                if (c == '\\') { i += 2; continue; }
                if (!triple && c == '\n')
                {
                    Issue("syntax.unterminated-string", start, $"This string starting with {q} is not closed before the end of the line.");
                    return i;   // resume at the newline
                }
                if (c == q && (!triple || (i + 2 < end && _s[i + 1] == q && _s[i + 2] == q)))
                    return i + (triple ? 3 : 1);

                if (interpolates && c == '$' && i + 1 < end)
                {
                    char n = _s[i + 1];
                    if (n == '{')
                    {
                        int close = MatchingBrace(i + 1, end);
                        if (close < 0)
                        {
                            Issue("syntax.unterminated-string", i, "This ${…} inside a string is never closed with }.");
                            return end;
                        }
                        Lex(i + 2, close, inString: true);   // the expression is real code
                        i = close + 1;
                        continue;
                    }
                    if (char.IsLetter(n) || n == '_')
                    {
                        int s0 = i + 1, k = s0;
                        while (k < end && (char.IsLetterOrDigit(_s[k]) || _s[k] == '_')) k++;
                        Add(TokKind.Ident, _s.Substring(s0, k - s0), s0, inString: true);
                        i = k;   // ".property" after it is just text for our purposes
                        continue;
                    }
                }
                i++;
            }
        }

        /// <summary>/regex/ — returns the index after the closing '/', or -1 when there is none.</summary>
        private int LexSlashy(int i, int end, bool inString)
        {
            for (int k = i + 1; k < end; k++)
            {
                if (_s[k] == '\\') { k++; continue; }
                if (_s[k] == '\n') return -1;   // keep it simple: one-line slashy strings
                if (_s[k] == '/')
                {
                    if (k == i + 1) return -1;  // "//" would have been a comment
                    Add(TokKind.String, "/", i, inString);
                    return k + 1;
                }
            }
            return -1;
        }

        private int LexDollarSlashy(int i, int end, bool inString)
        {
            int close = _s.IndexOf("/$", i + 2, end - (i + 2), StringComparison.Ordinal);
            if (close < 0)
            {
                Issue("syntax.unterminated-string", i, "This $/ string is never closed with /$.");
                return end;
            }
            Add(TokKind.String, "$/", i, inString);
            return close + 2;
        }

        /// <summary>Index of the '}' matching the '{' at <paramref name="open"/>, or -1.</summary>
        private int MatchingBrace(int open, int end)
        {
            int depth = 0;
            for (int k = open; k < end; k++)
            {
                char c = _s[k];
                if (c == '\\') { k++; continue; }
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return k;
                else if (c == '\'' || c == '"')
                {
                    // skip a nested simple string
                    for (k++; k < end && _s[k] != c; k++)
                        if (_s[k] == '\\') k++;
                }
            }
            return -1;
        }

        /// <summary>A '/' starts a regex unless it follows something that can end an expression.</summary>
        private bool RegexAllowedHere()
        {
            if (Tokens.Count == 0) return true;
            Tok t = Tokens[Tokens.Count - 1];
            if (t.Kind == TokKind.Newline) return true;
            if (t.Kind == TokKind.Ident || t.Kind == TokKind.Number || t.Kind == TokKind.String) return false;
            if (t.Kind == TokKind.Keyword) return t.Text != "this" && t.Text != "super" && t.Text != "true" &&
                                                  t.Text != "false" && t.Text != "null";
            return t.Text != ")" && t.Text != "]" && t.Text != "}" && t.Text != "++" && t.Text != "--";
        }

        private void Add(TokKind kind, string text, int pos, bool inString) =>
            Tokens.Add(new Tok { Kind = kind, Text = text, Pos = pos, InString = inString });

        private void Issue(string rule, int pos, string message) =>
            Issues.Add(new ValidationIssue { RuleId = rule, Pos = pos, Message = message });
    }
}
