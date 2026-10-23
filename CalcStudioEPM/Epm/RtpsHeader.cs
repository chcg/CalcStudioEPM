using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CalcStudioEPM.Epm
{
    /// <summary>
    /// The "/*RTPS: {A} {B} */" comment that declares a Groovy rule's runtime prompts
    /// to Calculation Manager (it always mirrors the rule's variable_references).
    /// </summary>
    public static class RtpsHeader
    {
        private static readonly Regex Header = new Regex(@"/\*\s*RTPS:(.*?)\*/", RegexOptions.Singleline);
        private static readonly Regex Token  = new Regex(@"\{(\w+)\}");

        /// <summary>Prompt names declared in the header, in header order (empty when none).</summary>
        public static List<string> Names(string script)
        {
            Match m = Header.Match(script ?? "");
            return m.Success
                ? Token.Matches(m.Groups[1].Value).Cast<Match>().Select(t => t.Groups[1].Value).ToList()
                : new List<string>();
        }

        /// <summary>
        /// Works out the edit that declares <paramref name="name"/> in the header.
        /// Returns null when it is already declared. Otherwise returns the character
        /// range [Start, End) to replace and the replacement text: the existing header
        /// rewritten with the new token appended, or — when the script has no header —
        /// a new one inserted as the first line.
        /// </summary>
        public static (int Start, int End, string Text)? PlanAdd(string script, string name, string eol)
        {
            script = script ?? "";
            Match m = Header.Match(script);

            if (!m.Success)
                return (0, 0, $"/*RTPS: {{{name}}} */{eol}");

            var tokens = Token.Matches(m.Groups[1].Value).Cast<Match>()
                              .Select(t => t.Groups[1].Value).ToList();
            if (tokens.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
                return null;

            tokens.Add(name);
            string rebuilt = "/*RTPS: " + string.Join(" ", tokens.Select(t => "{" + t + "}")) + " */";
            return (m.Index, m.Index + m.Length, rebuilt);
        }
    }
}
