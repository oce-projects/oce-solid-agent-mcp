using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SolidWorksMcp.Ponte
{
    /// <summary>
    /// GLI ARGOMENTI RISPETTANO LO SCHEMA CHE IL CATALOGO DICHIARA?
    ///
    /// Il ponte offre trentasei operazioni con schemi dettagliati e poi, fino
    /// alla 0.9.0, non guardava se gli argomenti li rispettassero: lo schema
    /// serviva solo a suggerire. Uno `spessore_mm` dimenticato arrivava a `Num()`
    /// come zero e diventava un'estrusione di spessore nullo, che SOLIDWORKS
    /// rifiuta con un messaggio suo — nel migliore dei casi.
    ///
    /// SI CONTROLLA LA FORMA, MAI IL MERITO. E' la riga da tenere in mente
    /// leggendo questo file, e il confine e' preciso:
    ///
    ///   forma  = c'e' il parametro obbligatorio? e' un numero dove serve un
    ///            numero? e' uno dei valori ammessi?
    ///   merito = quel numero ha senso? quel centro sta dentro il materiale?
    ///
    /// Il merito non si giudica qui, e non e' pigrizia: allargare un foro ha il
    /// centro **esattamente nel vuoto**, e un controllo zelante lo rifiuterebbe.
    /// Rifiutare cio' che e' soltanto insolito e' il difetto peggiore di tutti,
    /// perche' un rifiuto sbagliato non lo rivede nessuno mentre una
    /// spiegazione sbagliata la corregge chi legge in tre secondi.
    ///
    /// UN INTERO E' UN NUMERO. Il difetto piu' facile da scrivere qui e' un
    /// `type: number` che rifiuta il `20` di un JSON perche' arriva come intero:
    /// bloccherebbe operazioni che oggi funzionano, e lo farebbe con un
    /// messaggio che parla di tipi.
    /// </summary>
    internal static class Forma
    {
        /// <summary>
        /// Ritorna il motivo del rifiuto, o null se gli argomenti vanno bene.
        ///
        /// Uno schema che non si capisce **non e' un rifiuto**: se il catalogo
        /// e' scritto in un modo che questo controllo non sa leggere, il
        /// problema e' del controllo, e fermare l'operazione vorrebbe dire far
        /// pagare a chi disegna un difetto nostro.
        /// </summary>
        public static string Controlla(
            string operazione,
            Dictionary<string, object> schema,
            Dictionary<string, object> argomenti)
        {
            if (schema == null) return null;
            var proprieta = Prendi(schema, "properties") as Dictionary<string, object>;
            if (proprieta == null) return null;

            argomenti = argomenti ?? new Dictionary<string, object>();

            // ── 1. quello che deve esserci ───────────────────────────────
            var mancanti = new List<string>();
            foreach (var nome in Elenco(Prendi(schema, "required")))
            {
                object v;
                if (!argomenti.TryGetValue(nome, out v) || v == null) mancanti.Add(nome);
            }
            if (mancanti.Count == 1)
                return "Per «" + operazione + "» serve «" + mancanti[0] + "», e non c'e'." +
                       Suggerisci(proprieta, mancanti[0]);
            if (mancanti.Count > 1)
                return "Per «" + operazione + "» servono " +
                       string.Join(", ", mancanti.Select(x => "«" + x + "»")) +
                       ", e non ci sono.";

            // ── 2. quello che c'e', ma della forma sbagliata ─────────────
            foreach (var voce in argomenti)
            {
                // Le chiavi che cominciano con il trattino basso non sono
                // parametri dell'operazione: sono roba nostra («_firme»), e lo
                // schema giustamente non le nomina.
                if (voce.Key.StartsWith("_")) continue;

                object grezzo;
                if (!proprieta.TryGetValue(voce.Key, out grezzo)) continue;
                var regola = grezzo as Dictionary<string, object>;
                if (regola == null || voce.Value == null) continue;

                var storto = Sbaglia(operazione, voce.Key, regola, voce.Value);
                if (storto != null) return storto;
            }

            return null;
        }

        private static string Sbaglia(
            string operazione, string nome, Dictionary<string, object> regola, object valore)
        {
            var tipo = Convert.ToString(Prendi(regola, "type") ?? "");

            switch (tipo)
            {
                case "number":
                case "integer":
                    if (!UnNumero(valore))
                        return "Per «" + operazione + "», «" + nome + "» vuole un numero e " +
                               "invece e' " + ComeSiChiama(valore) + ".";
                    break;

                case "boolean":
                    if (!(valore is bool))
                        return "Per «" + operazione + "», «" + nome + "» vuole vero o falso e " +
                               "invece e' " + ComeSiChiama(valore) + ".";
                    break;

                case "array":
                    if (valore is string || !(valore is IEnumerable))
                        return "Per «" + operazione + "», «" + nome + "» vuole un elenco e " +
                               "invece e' " + ComeSiChiama(valore) + ".";
                    break;

                case "object":
                    if (!(valore is Dictionary<string, object>))
                        return "Per «" + operazione + "», «" + nome + "» vuole un oggetto e " +
                               "invece e' " + ComeSiChiama(valore) + ".";
                    break;

                case "string":
                    // Un numero dove va una stringa NON si rifiuta: chi scrive
                    // «configurazione: 80» intende «"80"», e rifiutarlo sarebbe
                    // pignoleria che costa un giro a chi disegna.
                    break;
            }

            // ── i valori ammessi ─────────────────────────────────────────
            var ammessi = Elenco(Prendi(regola, "enum"));
            if (ammessi.Count > 0)
            {
                var scritto = Convert.ToString(valore);
                if (!ammessi.Any(x => string.Equals(x, scritto, StringComparison.OrdinalIgnoreCase)))
                    return "Per «" + operazione + "», «" + nome + "» puo' essere solo " +
                           string.Join(", ", ammessi.Select(x => "«" + x + "»")) +
                           ", e invece dice «" + scritto + "».";
            }

            return null;
        }

        /// <summary>
        /// Un intero JSON e' un numero. Il difetto piu' facile da scrivere in
        /// questo file e' un `type: number` che rifiuta il `20` di
        /// `"spessore_mm": 20` perche' il deserializzatore l'ha fatto `int`.
        /// </summary>
        private static bool UnNumero(object v)
        {
            if (v is bool) return false;
            if (v is sbyte || v is byte || v is short || v is ushort ||
                v is int || v is uint || v is long || v is ulong ||
                v is float || v is double || v is decimal) return true;

            // Anche un numero scritto fra virgolette: chi manda «"20"» ha detto
            // venti, e fermarlo per le virgolette sarebbe rifiutare cio' che si
            // e' capito benissimo.
            var s = v as string;
            double _;
            return s != null &&
                   double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        private static string ComeSiChiama(object v)
        {
            if (v == null) return "vuoto";
            if (v is bool) return "vero/falso";
            if (v is string) return "un testo («" + v + "»)";
            if (v is Dictionary<string, object>) return "un oggetto";
            if (v is IEnumerable) return "un elenco";
            return "un " + v.GetType().Name;
        }

        /// <summary>La descrizione del parametro mancante, se il catalogo ne ha una.</summary>
        private static string Suggerisci(Dictionary<string, object> proprieta, string nome)
        {
            object grezzo;
            if (!proprieta.TryGetValue(nome, out grezzo)) return "";
            var regola = grezzo as Dictionary<string, object>;
            if (regola == null) return "";
            var d = Convert.ToString(Prendi(regola, "description") ?? "").Trim();
            if (string.IsNullOrEmpty(d)) return "";
            if (d.Length > 160) d = d.Substring(0, 160).TrimEnd() + "…";
            return " (" + d + ")";
        }

        private static List<string> Elenco(object grezzo)
        {
            var fuori = new List<string>();
            var e = grezzo as IEnumerable;
            if (e == null || grezzo is string) return fuori;
            foreach (var x in e)
            {
                var s = Convert.ToString(x);
                if (!string.IsNullOrWhiteSpace(s)) fuori.Add(s);
            }
            return fuori;
        }

        private static object Prendi(Dictionary<string, object> d, string chiave)
        {
            object v;
            return d != null && d.TryGetValue(chiave, out v) ? v : null;
        }
    }
}
