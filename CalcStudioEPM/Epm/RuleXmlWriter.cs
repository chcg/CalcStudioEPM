using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CalcStudioEPM.Epm
{
    /// <summary>
    /// Writes rule XML byte-for-byte the way EPM does, instead of the way .NET's XmlWriter
    /// does (which escapes '>' as "&amp;gt;", adds CRs and re-indents). Two styles, both taken
    /// from files EPM produced:
    ///
    ///  CALC (Calculation Manager artifact, also what its UI exports):
    ///    &lt;?xml version = '1.0' encoding = 'UTF-8'?&gt; + "\n" + the document on one line;
    ///    empty elements as &lt;x/&gt;; script as text with only '&lt;' and '&amp;' escaped.
    ///
    ///  HP (Planning artifact, HP-{App}/resource/Cube/{cube}/Calculation Manager Rules):
    ///    &lt;?xml version="1.0" encoding="UTF-8" ?&gt; + "\n" + two-space indentation, "\n"
    ///    line breaks; empty elements as &lt;x&gt;&lt;/x&gt;; script in CDATA (nothing escaped).
    ///
    /// Neither style ends with a newline.
    /// </summary>
    internal static class RuleXmlWriter
    {
        public static string Calc(XDocument doc) =>
            "<?xml version = '1.0' encoding = 'UTF-8'?>\n" + Write(doc.Root, indent: false, emptyAsPair: false);

        public static string Hp(XDocument doc) =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\n" + Write(doc.Root, indent: true, emptyAsPair: true);

        private static string Write(XElement root, bool indent, bool emptyAsPair)
        {
            var sb = new StringBuilder();
            WriteElement(sb, root, 0, indent, emptyAsPair);
            return sb.ToString();
        }

        private static void WriteElement(StringBuilder sb, XElement el, int depth, bool indent, bool emptyAsPair)
        {
            string name = el.Name.LocalName;
            sb.Append('<').Append(name);
            foreach (XAttribute a in el.Attributes())
                sb.Append(' ').Append(a.Name.LocalName).Append("=\"").Append(EscapeAttribute(a.Value)).Append('"');

            List<XNode> nodes = el.Nodes().ToList();
            if (nodes.Count == 0)
            {
                sb.Append(emptyAsPair ? $"></{name}>" : "/>");
                return;
            }
            sb.Append('>');

            bool onlyElements = nodes.All(n => n is XElement || n is XComment);
            if (indent && onlyElements)
            {
                // Children each on their own line, one level deeper
                foreach (XNode n in nodes)
                {
                    sb.Append('\n').Append(' ', 2 * (depth + 1));
                    WriteNode(sb, n, depth + 1, indent, emptyAsPair);
                }
                sb.Append('\n').Append(' ', 2 * depth);
            }
            else
            {
                // Text (or mixed) content stays inline: <property name="x">Vision</property>
                foreach (XNode n in nodes)
                    WriteNode(sb, n, depth, indent: false, emptyAsPair: emptyAsPair);
            }
            sb.Append("</").Append(name).Append('>');
        }

        private static void WriteNode(StringBuilder sb, XNode node, int depth, bool indent, bool emptyAsPair)
        {
            switch (node)
            {
                case XElement e:
                    WriteElement(sb, e, depth, indent, emptyAsPair);
                    break;
                case XCData cdata:
                    // "]]>" can't appear inside CDATA — split it across two sections
                    sb.Append("<![CDATA[").Append(cdata.Value.Replace("]]>", "]]]]><![CDATA[>")).Append("]]>");
                    break;
                case XText text:
                    sb.Append(EscapeText(text.Value));
                    break;
                case XComment comment:
                    sb.Append("<!--").Append(comment.Value).Append("-->");
                    break;
            }
        }

        /// <summary>Text the way EPM writes it: only '&amp;' and '&lt;' (plus the '&gt;' of "]]&gt;", which XML requires).</summary>
        private static string EscapeText(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace("]]>", "]]&gt;");

        private static string EscapeAttribute(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");
    }
}
