using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Oce.Solid.Agent.Core.Catalog
{
    /// <summary>
    /// Un pezzo di catalogo: un valore, un oggetto con le chiavi IN ORDINE, o
    /// un elenco.
    ///
    /// PERCHE' NON `Dictionary<string, object>`.
    /// Perche' un dizionario non promette l'ordine delle chiavi, e qui l'ordine
    /// e' contenuto: `catalog.json` e' un file che si legge, si confronta con
    /// `diff` e si versiona. Se le chiavi ballano a ogni compilazione, ogni
    /// commit mostra un file intero cambiato e nessuno vede piu' la riga che e'
    /// cambiata davvero.
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

        /// <summary>Un valore: testo, numero, vero/falso, o niente.</summary>
        public static Node V(object val)
        {
            return val is Node ? (Node)val : new Node(val);
        }

        /// <summary>
        /// Un oggetto, scritto chiave-valore-chiave-valore. Le chiavi restano
        /// nell'ordine in cui sono scritte.
        /// </summary>
        public static Node O(params object[] pairs)
        {
            if (pairs.Length % 2 != 0)
                throw new ArgumentException(
                    "Un oggetto si scrive chiave, valore, chiave, valore: ne ho " +
                    pairs.Length + ", che e' dispari. Manca un valore.");

            var fields = new List<KeyValuePair<string, Node>>(pairs.Length / 2);
            for (int i = 0; i < pairs.Length; i += 2)
            {
                var key = pairs[i] as string;
                if (key == null)
                    throw new ArgumentException(
                        "La chiave in posizione " + i + " non e' un testo.");
                fields.Add(new KeyValuePair<string, Node>(key, V(pairs[i + 1])));
            }
            return new Node(fields);
        }

        /// <summary>Un elenco.</summary>
        public static Node A(params object[] elements)
        {
            var inside = new List<Node>(elements.Length);
            foreach (var e in elements) inside.Add(V(e));
            return new Node(inside);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Scrivere il JSON
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Il JSON, scritto **esattamente** come lo scriveva il generatore
        /// Python di prima: rientro di UNO spazio, `": "` fra chiave e valore,
        /// caratteri accentati per esteso e non in `\u`, terminatori a solo
        /// avanzamento riga, e nessun a capo finale.
        ///
        /// SEMBRA PIGNOLERIA E NON LO E'. Il criterio con cui si e' verificata
        /// questa traduzione e' che il file uscito dal C# fosse **identico byte
        /// per byte** a quello che c'era. Un criterio piu' morbido —
        /// «equivalente una volta riletto» — avrebbe lasciato passare una
        /// descrizione accorciata, e le descrizioni sono l'interfaccia vera
        /// verso il modello, molto piu' degli schemi.
        ///
        /// Da qui in avanti la stessa forma serve a un'altra cosa: tenere il
        /// `diff` di `catalog.json` leggibile quando cambia una riga sola.
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
                // `R` e non `G`: un numero che va e torna deve dare lo stesso
                // numero, e con la virgola inglese sempre, che qui la macchina
                // e' italiana.
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
                        // GLI ACCENTI RESTANO ACCENTI. Il generatore di prima
                        // scriveva UTF-8 per esteso, e riscriverli in `à`
                        // renderebbe il file illeggibile a chi lo apre — che e'
                        // il motivo per cui esiste.
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
        }
    }
}
