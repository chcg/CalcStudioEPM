using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CalcStudioEPM.Epm
{
    /// <summary>
    /// Works out the EPM type of a Groovy expression from the text before it — shared by
    /// autocomplete and the local validation. Not a Groovy compiler: it follows the script
    /// bindings (operation, rule), typed declarations (Cube c = …), def x = &lt;expression&gt;,
    /// chained calls / properties through the <see cref="GroovyApi"/>, and closure
    /// parameters of each/find/collect… over grid iterators and lists.
    /// </summary>
    public static class GroovyTypeInference
    {

        public struct Segment
        {
            public string Name;
            public bool   Call;    // name(...)
        }

        /// <summary>
        /// The dotted expression ending at the end of <paramref name="text"/>, outermost first:
        /// "operation.application.getCube(\"Plan1\")" → operation · application · getCube().
        /// Empty when the text doesn't end in such an expression.
        /// </summary>
        public static List<Segment> ParseChain(string text)
        {
            var segs = new List<Segment>();
            int i = text.Length - 1;
            while (true)
            {
                bool call = false;
                if (i >= 0 && text[i] == ')')
                {
                    i = MatchOpen(text, i, '(', ')');
                    if (i < 0) return new List<Segment>();
                    i--;
                    call = true;
                }
                else if (i >= 0 && text[i] == ']')
                {
                    return new List<Segment>();                       // indexing: element type unknown
                }
                int end = i;
                while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '$')) i--;
                string name = text.Substring(i + 1, end - i);
                if (name.Length == 0 || char.IsDigit(name[0])) return new List<Segment>();
                segs.Insert(0, new Segment { Name = name, Call = call });

                int j = i;
                while (j >= 0 && (text[j] == ' ' || text[j] == '\t')) j--;
                if (j >= 0 && text[j] == '.')
                {
                    i = j - 1;
                    if (i >= 0 && text[i] == '?') i--;                 // safe navigation ?.
                    while (i >= 0 && char.IsWhiteSpace(text[i])) i--;
                    continue;
                }
                return segs;
            }
        }

        /// <summary>Index of the opener matching the closer at <paramref name="close"/>, skipping strings.</summary>
        public static int MatchOpen(string s, int close, char open, char closer)
        {
            int depth = 0;
            char quote = '\0';
            for (int i = close; i >= 0; i--)
            {
                char c = s[i];
                if (quote != '\0') { if (c == quote && (i == 0 || s[i - 1] != '\\')) quote = '\0'; continue; }
                if (c == '"' || c == '\'') { quote = c; continue; }
                if (c == closer) depth++;
                else if (c == open && --depth == 0) return i;
            }
            return -1;
        }

        public static string ResolveChain(List<Segment> chain, string context, GroovyApi api, int depth)
        {
            if (chain.Count == 0 || depth > 4) return null;
            string type = ResolveHead(chain[0], context, api, depth);
            for (int k = 1; k < chain.Count && type != null; k++)
                type = api.MemberType(api.Find(type), chain[k].Name, chain[k].Call);
            return type;
        }

        private static string ResolveHead(Segment head, string context, GroovyApi api, int depth)
        {
            if (head.Call) return null;
            string name = head.Name;

            string local = LocalType(name, context, api, depth);
            if (local != null) return local;
            if (api.Bindings.TryGetValue(name, out string binding)) return binding;
            if (api.Find(name) != null && char.IsUpper(name[0])) return api.Find(name).Name;   // static access
            return null;
        }

        public static bool IsLocalName(string name, string context) =>
            Regex.IsMatch(context, $@"\b(?:def|var|final|[A-Z][\w.]*(?:<[^>]*>)?)\s+{Regex.Escape(name)}\s*[=;,)]");

        /// <summary>The type of a local variable or closure parameter, from the text before the caret.</summary>
        private static string LocalType(string name, string context, GroovyApi api, int depth)
        {
            string n = Regex.Escape(name);

            // Typed: Cube cube = …, DataGrid.DataCell cell ->, (Member m)
            Match typed = Regex.Matches(context, $@"(?<![\w.])([A-Z][\w]*(?:\.[A-Z]\w*)*)(?:<[^<>]*(?:<[^<>]*>)?[^<>]*>)?(?:\[\])?\s+{n}\s*(?:=|;|,|\)|->|:|$)",
                                        RegexOptions.Multiline).Cast<Match>().LastOrDefault();
            if (typed != null && api.Find(typed.Groups[1].Value) != null) return api.Find(typed.Groups[1].Value).Name;
            if (typed != null && typed.Groups[1].Value != "String") return typed.Groups[1].Value;

            // def x = <expression>
            Match def = Regex.Matches(context, $@"\b(?:def|var|final)\s+{n}\s*=\s*([^;\n]+)").Cast<Match>().LastOrDefault();
            if (def != null)
            {
                string rhs = def.Groups[1].Value.Trim().TrimEnd(';').Trim();
                return ResolveChain(ParseChain(rhs), context.Substring(0, def.Index), api, depth + 1);
            }

            // Closure parameter: receiver.each { x -> … }, or it
            string pattern = name == "it"
                ? @"\.(each|eachWithIndex|find|findAll|collect|any|every|forEach|sort)\s*\{(?![^{}]*->)"
                : $@"\.(each|eachWithIndex|find|findAll|collect|any|every|forEach|sort)\s*\{{\s*(?:[\w.]+\s+)?{n}\s*(?:,\s*\w+\s*)?->";
            Match closure = Regex.Matches(context, pattern).Cast<Match>().LastOrDefault();
            if (closure != null)
            {
                string receiverType = ResolveChain(ParseChain(context.Substring(0, closure.Index)), context.Substring(0, closure.Index), api, depth + 1);
                if (receiverType == null) return null;
                if (GroovyApi.BaseName(receiverType) == "GridIterator") return "DataGrid.DataCell";
                return GroovyApi.ElementType(receiverType);
            }
            return null;
        }
    }
}
