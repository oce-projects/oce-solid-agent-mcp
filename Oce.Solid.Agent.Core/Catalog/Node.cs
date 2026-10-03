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
    public sealed class Nodo
    {
        private enum Che { Valore, Oggetto, Elenco }

        private readonly Che _che;
        private readonly object _valore;
        private readonly List<KeyValuePair<string, Nodo>> _campi;
        private readonly List<Nodo> _elementi;

        private Nodo(object valore)
        {
            _che = Che.Valore;
            _valore = valore;
        }

        private Nodo(List<KeyValuePair<string, Nodo>> campi)
        {
            _che = Che.Oggetto;
            _campi = campi;
        }

        private Nodo(List<Nodo> elementi)
        {
            _che = Che.Elenco;
            _elementi = elementi;
        }

        /// <summary>Un valore: testo, numero, vero/falso, o niente.</summary>
        public static Nodo V(object valore)
        {
            return valore is Nodo ? (Nodo)valore : new Nodo(valore);
        }

        /// <summary>
        /// Un oggetto, scritto chiave-valore-chiave-valore. Le chiavi restano
        /// nell'ordine in cui sono scritte.
        /// </summary>
        public static Nodo O(params object[] coppie)
        {
            if (coppie.Length % 2 != 0)
                throw new ArgumentException(
                    "Un oggetto si scrive chiave, valore, chiave, valore: ne ho " +
                    coppie.Length + ", che e' dispari. Manca un valore.");

            var campi = new List<KeyValuePair<string, Nodo>>(coppie.Length / 2);
            for (int i = 0; i < coppie.Length; i += 2)
            {
                var chiave = coppie[i] as string;
                if (chiave == null)
                    throw new ArgumentException(
                        "La chiave in posizione " + i + " non e' un testo.");
                campi.Add(new KeyValuePair<string, Nodo>(chiave, V(coppie[i + 1])));
            }
            return new Nodo(campi);
        }

        /// <summary>Un elenco.</summary>
        public static Nodo A(params object[] elementi)
        {
            var dentro = new List<Nodo>(elementi.Length);
            foreach (var e in elementi) dentro.Add(V(e));
            return new Nodo(dentro);
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
        public static string Json(Nodo radice)
        {
            var b = new StringBuilder();
            Scrivi(b, radice, 0);
            return b.ToString();
        }

        private static void Scrivi(StringBuilder b, Nodo n, int livello)
        {
            switch (n._che)
            {
                case Che.Valore:
                    ScriviValore(b, n._valore);
                    break;

                case Che.Oggetto:
                    if (n._campi.Count == 0) { b.Append("{}"); break; }
                    b.Append('{');
                    for (int i = 0; i < n._campi.Count; i++)
                    {
                        if (i > 0) b.Append(',');
                        b.Append('\n').Append(' ', livello + 1);
                        ScriviTesto(b, n._campi[i].Key);
                        b.Append(": ");
                        Scrivi(b, n._campi[i].Value, livello + 1);
                    }
                    b.Append('\n').Append(' ', livello).Append('}');
                    break;

                case Che.Elenco:
                    if (n._elementi.Count == 0) { b.Append("[]"); break; }
                    b.Append('[');
                    for (int i = 0; i < n._elementi.Count; i++)
                    {
                        if (i > 0) b.Append(',');
                        b.Append('\n').Append(' ', livello + 1);
                        Scrivi(b, n._elementi[i], livello + 1);
                    }
                    b.Append('\n').Append(' ', livello).Append(']');
                    break;
            }
        }

        private static void ScriviValore(StringBuilder b, object v)
        {
            if (v == null) { b.Append("null"); return; }
            if (v is bool) { b.Append((bool)v ? "true" : "false"); return; }
            if (v is string) { ScriviTesto(b, (string)v); return; }

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

        private static void ScriviTesto(StringBuilder b, string s)
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
