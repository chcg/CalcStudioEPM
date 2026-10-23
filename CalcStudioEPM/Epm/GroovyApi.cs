using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace CalcStudioEPM.Epm
{
    /// <summary>
    /// The EPM Groovy API (types, methods with parameters and return types, enum constants,
    /// supertypes), compiled from Oracle's EPM Groovy Scripting Reference into the embedded
    /// groovy_api.json — signatures only. Regenerate it with tools\groovy_api\*.py.
    /// </summary>
    public sealed class GroovyApi
    {
        public sealed class Method
        {
            public string Name    { get; set; }
            public string Params  { get; set; }   // "(String name, boolean flag)"
            public string Returns { get; set; }   // "Cube", "List<Member>", "void"
            public bool   Static  { get; set; }
            public bool   Deprecated { get; set; }

            public override string ToString() => $"{Returns} {Name}{Params}";
        }

        public sealed class TypeInfo
        {
            public string       Name      { get; set; }   // "Cube", "DataGrid.DataCell"
            public string       Package   { get; set; }
            public string       Kind      { get; set; }   // class, interface, enum
            public List<string> Parents   { get; set; } = new List<string>();
            public List<Method> Methods   { get; set; } = new List<Method>();
            public List<string> Constants { get; set; } = new List<string>();
        }

        private sealed class File_
        {
            public Dictionary<string, string>   Bindings { get; set; }
            public Dictionary<string, TypeInfo> Types    { get; set; }
        }

        public Dictionary<string, string>   Bindings { get; private set; }
        public Dictionary<string, TypeInfo> Types    { get; private set; }

        private static readonly Lazy<GroovyApi> _instance = new Lazy<GroovyApi>(Load);
        public static GroovyApi Instance => _instance.Value;

        private static GroovyApi Load()
        {
            var api = new GroovyApi
            {
                Bindings = new Dictionary<string, string>(StringComparer.Ordinal),
                Types    = new Dictionary<string, TypeInfo>(StringComparer.Ordinal),
            };
            try
            {
                using (Stream s = typeof(GroovyApi).Assembly.GetManifestResourceStream("CalcStudioEPM.groovy_api.json"))
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    var f = JsonConvert.DeserializeObject<File_>(r.ReadToEnd());
                    foreach (var kv in f.Bindings ?? new Dictionary<string, string>()) api.Bindings[kv.Key] = kv.Value;
                    foreach (var kv in f.Types ?? new Dictionary<string, TypeInfo>())
                    {
                        kv.Value.Name = kv.Key;
                        api.Types[kv.Key] = kv.Value;
                    }
                }
            }
            catch { /* no API: Groovy completion offers nothing type-specific */ }
            return api;
        }

        // ------------------------------------------------------------------ //

        /// <summary>"List&lt;Member&gt;" → List (not an EPM type: null); "Cube" → Cube; "DataCell" → DataGrid.DataCell.</summary>
        public TypeInfo Find(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            string t = BaseName(typeName);
            if (Types.TryGetValue(t, out TypeInfo ti)) return ti;
            // A nested type written without its owner ("DataCell" for DataGrid.DataCell)
            return Types.Values.FirstOrDefault(x => x.Name.EndsWith("." + t, StringComparison.Ordinal));
        }

        /// <summary>The type without generics and array brackets.</summary>
        public static string BaseName(string typeName)
        {
            string t = (typeName ?? "").Trim();
            int lt = t.IndexOf('<');
            if (lt >= 0) t = t.Substring(0, lt);
            return t.Replace("[]", "").Replace("...", "").Trim();
        }

        /// <summary>"List&lt;Member&gt;" → "Member"; null when there is no single type argument.</summary>
        public static string ElementType(string typeName)
        {
            string t = (typeName ?? "").Trim();
            int lt = t.IndexOf('<'), gt = t.LastIndexOf('>');
            if (lt < 0 || gt < lt) return t.EndsWith("[]") ? t.Substring(0, t.Length - 2) : null;
            string inner = t.Substring(lt + 1, gt - lt - 1).Trim();
            if (inner.Contains(",")) return null;
            return inner.Replace("? extends ", "").Replace("? super ", "").Trim();
        }

        /// <summary>The type's own methods, then its supertypes' (each method name/params once).</summary>
        public IEnumerable<Method> AllMethods(TypeInfo t)
        {
            var seen  = new HashSet<string>(StringComparer.Ordinal);
            var types = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<TypeInfo>();
            if (t != null) queue.Enqueue(t);
            while (queue.Count > 0)
            {
                TypeInfo cur = queue.Dequeue();
                if (!types.Add(cur.Name)) continue;
                foreach (Method m in cur.Methods)
                    if (seen.Add(m.Name + m.Params)) yield return m;
                foreach (string p in cur.Parents)
                {
                    TypeInfo pt = Find(p);
                    if (pt != null) queue.Enqueue(pt);
                }
            }
        }

        /// <summary>Groovy properties a type exposes through zero-argument getters: getCube() → cube.</summary>
        public static string PropertyOf(Method m)
        {
            if (m.Params != "()" || m.Returns == "void") return null;
            string n = m.Name;
            if (n.Length > 3 && n.StartsWith("get") && char.IsUpper(n[3])) return char.ToLowerInvariant(n[3]) + n.Substring(4);
            if (n.Length > 2 && n.StartsWith("is")  && char.IsUpper(n[2]) && (m.Returns == "boolean" || m.Returns == "Boolean"))
                return char.ToLowerInvariant(n[2]) + n.Substring(3);
            return null;
        }

        /// <summary>The type of <c>receiver.member</c> (a property) or <c>receiver.member(…)</c> (a call).</summary>
        public string MemberType(TypeInfo receiver, string member, bool isCall)
        {
            if (receiver == null) return null;
            List<Method> all = AllMethods(receiver).ToList();
            IEnumerable<Method> match = isCall
                ? all.Where(m => m.Name == member)
                : all.Where(m => PropertyOf(m) == member);
            // Overloads nearly always share their return type; take the most common one
            string ret = match.GroupBy(m => m.Returns).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            if (ret != null) return ret;
            if (!isCall && receiver.Constants.Contains(member)) return receiver.Name;   // enum constant
            return null;
        }

        /// <summary>Every method name in the API (for "other options").</summary>
        public IEnumerable<string> AllMethodNames() =>
            Types.Values.SelectMany(t => t.Methods).Select(m => m.Name).Distinct(StringComparer.Ordinal);
    }
}
