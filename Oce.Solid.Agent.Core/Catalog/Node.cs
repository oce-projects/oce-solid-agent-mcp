using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Oce.Solid.Agent.Core.Catalog
{
    /// <summary>
    /// A piece of catalog: a value, an object with the keys IN ORDER, or
    /// a list.
    ///
    /// WHY NOT `Dictionary<string, object>`.
    /// Because a dictionary does not promise the order of the keys, and here the order
    /// is content: `catalog.json` is a file that is read, compared with
    /// `diff` and versioned. If the keys dance at every compilation, every
    /// commit shows a whole file changed and nobody sees any more the line that
    /// really changed.
    /// </summary>
    public sealed class Node
    {
        private enum NodeKind { Value, Object, Items }

        private readonly NodeKind _kind;
        private readonly object _value;
        private readonly List<KeyValuePair<string, Node>> _fields;
        private readonly List<Node> _elements;

        private Node(object val)
        {
            _kind = NodeKind.Value;
            _value = val;
        }

        private Node(List<KeyValuePair<string, Node>> fields)
        {
            _kind = NodeKind.Object;
            _fields = fields;
        }

        private Node(List<Node> elements)
        {
            _kind = NodeKind.Items;
            _elements = elements;
        }

        /// <summary>A value: text, number, true/false, or nothing.</summary>
        public static Node V(object val)
        {
            return val is Node ? (Node)val : new Node(val);
        }

        /// <summary>
        /// An object, written key-value-key-value. The keys stay
        /// in the order in which they are written.
        /// </summary>
        public static Node O(params object[] pairs)
        {
            if (pairs.Length % 2 != 0)
                throw new ArgumentException(
                    "An object is written key, value, key, value: this one has " +
                    pairs.Length + ", which is odd. A value is missing.");

            var fields = new List<KeyValuePair<string, Node>>(pairs.Length / 2);
            for (int i = 0; i < pairs.Length; i += 2)
            {
                var key = pairs[i] as string;
                if (key == null)
                    throw new ArgumentException(
                        "The key at position " + i + " is not a text.");
                fields.Add(new KeyValuePair<string, Node>(key, V(pairs[i + 1])));
            }
            return new Node(fields);
        }

        /// <summary>A list.</summary>
        public static Node A(params object[] elements)
        {
            var inside = new List<Node>(elements.Length);
            foreach (var e in elements) inside.Add(V(e));
            return new Node(inside);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Writing the JSON
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The JSON, written **exactly** as the previous Python generator
        /// wrote it: indentation of ONE space, `": "` between key and value,
        /// accented characters in full and not in `\u`, line-feed-only
        /// terminators, and no final newline.
        ///
        /// IT LOOKS LIKE PEDANTRY AND IT IS NOT. The criterion with which this translation
        /// was verified is that the file coming out of the C# was **identical byte
        /// for byte** to the one that was there. A softer criterion —
        /// «equivalent once read back» — would have let through a shortened
        /// description, and the descriptions are the real interface
        /// towards the model, much more than the schemas.
        ///
        /// From here on the same form serves another purpose: keeping the
        /// `diff` of `catalog.json` readable when a single line changes.
        /// </summary>
        public static string Json(Node root)
        {
            var b = new StringBuilder();
            Write(b, root, 0);
            return b.ToString();
        }

        private static void Write(StringBuilder b, Node n, int level)
        {
            switch (n._kind)
            {
                case NodeKind.Value:
                    WriteValue(b, n._value);
                    break;

                case NodeKind.Object:
                    if (n._fields.Count == 0) { b.Append("{}"); break; }
                    b.Append('{');
                    for (int i = 0; i < n._fields.Count; i++)
                    {
                        if (i > 0) b.Append(',');
                        b.Append('\n').Append(' ', level + 1);
                        WriteText(b, n._fields[i].Key);
                        b.Append(": ");
                        Write(b, n._fields[i].Value, level + 1);
                    }
                    b.Append('\n').Append(' ', level).Append('}');
                    break;

                case NodeKind.Items:
                    if (n._elements.Count == 0) { b.Append("[]"); break; }
                    b.Append('[');
                    for (int i = 0; i < n._elements.Count; i++)
                    {
                        if (i > 0) b.Append(',');
                        b.Append('\n').Append(' ', level + 1);
                        Write(b, n._elements[i], level + 1);
                    }
                    b.Append('\n').Append(' ', level).Append(']');
                    break;
            }
        }

        private static void WriteValue(StringBuilder b, object v)
        {
            if (v == null) { b.Append("null"); return; }
            if (v is bool) { b.Append((bool)v ? "true" : "false"); return; }
            if (v is string) { WriteText(b, (string)v); return; }

            if (v is double || v is float || v is decimal)
            {
                // `R` and not `G`: a number that goes and comes back must give the same
                // number, and with the English decimal point always, since here the machine
                // is Italian.
                b.Append(Convert.ToDouble(v).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            b.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        private static void WriteText(StringBuilder b, string s)
        {
            b.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    default:
                        // ACCENTS STAY ACCENTS. The previous generator
                        // wrote UTF-8 in full, and rewriting them as `à`
                        // would make the file unreadable to whoever opens it — which is
                        // the reason why it exists.
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
        }
    }
}
