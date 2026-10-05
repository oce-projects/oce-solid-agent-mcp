using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Oce.Solid.Agent.Bridge
{
    /// <summary>
    /// GLI ARGOMENTI RISPETTANO LO SCHEMA CHE IL CATALOGO DICHIARA?
    ///
    /// Il ponte offre trentasei operazioni con schemi dettagliati e poi, fino
    /// alla 0.9.0, non guardava se gli argomenti li rispettassero: lo schema
    /// serviva solo a suggerire. Uno `thickness_mm` dimenticato arrivava a `Num()`
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
    internal static class Shape
    {
        /// <summary>
        /// Ritorna il motivo del rifiuto, o null se gli argomenti vanno bene.
        ///
        /// Uno schema che non si capisce **non e' un rifiuto**: se il catalogo
        /// e' scritto in un modo che questo controllo non sa leggere, il
        /// problema e' del controllo, e fermare l'operazione vorrebbe dire far
        /// pagare a chi disegna un difetto nostro.
        ///
        /// IT ALSO REWRITES, IN <paramref name="arguments"/> ITSELF, every enum
        /// value spelled differently from the catalogue (Z-C21.10, pattern
        /// S20): «Superiore» is accepted and goes on as «top». The caller
        /// hands the same dictionary to the library, so whatever passes here
        /// reaches the library spelled as the catalogue spells it. See the
        /// enum block in <see cref="Fail"/> for why.
        /// </summary>
        public static string Check(
            string operation,
            Dictionary<string, object> schema,
            Dictionary<string, object> arguments)
        {
            if (schema == null) return null;
            var property = Take(schema, "properties") as Dictionary<string, object>;
            if (property == null) return null;

            arguments = arguments ?? new Dictionary<string, object>();

            // ── 1. quello che deve esserci ───────────────────────────────
            var missingItems = new List<string>();
            foreach (var itemName in Items(Take(schema, "required")))
            {
                object v;
                if (!arguments.TryGetValue(itemName, out v) || v == null) missingItems.Add(itemName);
            }
            if (missingItems.Count == 1)
                return "Per «" + operation + "» serve «" + missingItems[0] + "», e non c'e'." +
                       Suggest(property, missingItems[0]);
            if (missingItems.Count > 1)
                return "Per «" + operation + "» servono " +
                       string.Join(", ", missingItems.Select(x => "«" + x + "»")) +
                       ", e non ci sono.";

            // ── 2. quello che c'e', ma della forma sbagliata ─────────────
            //
            // OVER A COPY OF THE ENTRIES, because an enum value can be
            // rewritten in `arguments` on the way (see Sbaglia). On .NET
            // Framework even overwriting an existing key bumps the
            // dictionary's version, and the next MoveNext of a foreach over
            // that same dictionary throws InvalidOperationException.
            foreach (var entry in arguments.ToList())
            {
                // Le chiavi che cominciano con il trattino basso non sono
                // parametri dell'operazione: sono roba nostra («_signatures»), e lo
                // schema giustamente non le nomina.
                if (entry.Key.StartsWith("_")) continue;

                object raw;
                if (!property.TryGetValue(entry.Key, out raw)) continue;
                var rule = raw as Dictionary<string, object>;
                if (rule == null || entry.Value == null) continue;

                string catalogueSpelling;
                var crooked = Fail(operation, entry.Key, rule, entry.Value, 0, out catalogueSpelling);
                if (crooked != null) return crooked;
                if (catalogueSpelling != null) arguments[entry.Key] = catalogueSpelling;
            }

            return null;
        }

        /// <summary>
        /// Quanto in profondita' si scende dentro gli oggetti annidati.
        ///
        /// Gli schemi di questo catalogo arrivano a due livelli
        /// (`feature_rif.passo`). Tre e' un margine, e il tetto c'e' perche' un
        /// controllo che si chiama da solo senza fondo e' un modo di piantarsi.
        /// </summary>
        private const int MaximumDepth = 3;

        /// <summary>
        /// Controlla un valore contro la sua regola. Scende anche DENTRO gli
        /// oggetti annidati, e fino alla 0.25.2 non lo faceva.
        ///
        /// LA PROMESSA CHE NON ERA MANTENUTA. La descrizione dello strumento
        /// `build` dice al modello: «Il piano viene controllato TUTTO prima di
        /// cominciare: se un passo qualunque ha un nome sbagliato o argomenti
        /// che non rispettano lo schema, non viene eseguito niente e il
        /// documento resta com'era». `PlanInMemory` ripete la stessa
        /// garanzia.
        ///
        /// Ma il controllo si fermava al primo livello: per `type: "object"`
        /// verificava che fosse un oggetto e basta, senza guardare il `required`
        /// e le `properties` dentro. Lo schema di `linear_pattern` dichiara
        /// `feature_ref` come oggetto con `required: ["step", "kind"]`, e un
        /// piano di quattro passi il cui quarto scrive `feature_ref: {}`
        /// passava tutta la validazione preventiva. I primi tre passi venivano
        /// costruiti nel documento, poi il quarto falliva.
        ///
        /// Il documento restava a meta'. La garanzia di tutto-o-niente e'
        /// esattamente cio' per cui questo controllo esiste: mantenerla solo al
        /// primo livello vuol dire non mantenerla.
        ///
        /// <paramref name="catalogueSpelling"/> is not null only when the value
        /// is an enum written with a different case: it is the catalogue's
        /// spelling, and the caller writes it back in place of the value.
        /// Nested objects are rewritten inside <see cref="Nested"/>.
        /// </summary>
        private static string Fail(
            string operation, string itemName, Dictionary<string, object> rule,
            object val, int depth, out string catalogueSpelling)
        {
            catalogueSpelling = null;
            var typ = Convert.ToString(Take(rule, "type") ?? "");

            switch (typ)
            {
                case "number":
                case "integer":
                    if (!ANumber(val))
                        return "Per «" + operation + "», «" + itemName + "» vuole un numero e " +
                               "invece e' " + WhatItIsCalled(val) + ".";
                    break;

                case "boolean":
                    if (!(val is bool))
                        return "Per «" + operation + "», «" + itemName + "» vuole vero o falso e " +
                               "invece e' " + WhatItIsCalled(val) + ".";
                    break;

                case "array":
                    if (val is string || !(val is IEnumerable))
                        return "Per «" + operation + "», «" + itemName + "» vuole un elenco e " +
                               "invece e' " + WhatItIsCalled(val) + ".";
                    break;

                case "object":
                    var inside = val as Dictionary<string, object>;
                    if (inside == null)
                        return "Per «" + operation + "», «" + itemName + "» vuole un oggetto e " +
                               "invece e' " + WhatItIsCalled(val) + ".";
                    if (depth < MaximumDepth)
                    {
                        var crooked = Nested(operation, itemName, rule, inside, depth + 1);
                        if (crooked != null) return crooked;
                    }
                    break;

                case "string":
                    // Un numero dove va una stringa NON si rifiuta: chi scrive
                    // «configuration: 80» intende «"80"», e rifiutarlo sarebbe
                    // pignoleria che costa un giro a chi disegna.
                    break;
            }

            // ── i valori ammessi ─────────────────────────────────────────
            //
            // THE VALUE GOES ON SPELLED AS THE CATALOGUE SPELLS IT (Z-C21.10,
            // pattern S20). The case of a word is form, not merit, so
            // «Superiore» is accepted here, as it always was. Until 0.140.0
            // it then went on to the library untouched, and the library
            // compares Ordinal: `SelectPlane` read «Superiore» as an
            // unknown name and fell back to the front plane (the box on the
            // wrong plane, riuscita:true, finding N33), and
            // `rectangular_frame` read «Ingombro» as not-«overall», that
            // is the centreline branch (a 1080x880 frame for 1000x800, N32).
            // Teaching every comparison in the library to ignore case would
            // leave the next one to be forgotten; rewriting the value here
            // covers them all, because this is the one door every step of
            // «build» comes through (Tools/McpTools.cs).
            //
            // An exact match wins. Otherwise the one value that differs only
            // in case. Two such values (a catalogue spelling one word two
            // ways) leave nothing to choose from, and that stays a rejection.
            var allowed = Items(Take(rule, "enum"));
            if (allowed.Count > 0)
            {
                var written = Convert.ToString(val);
                if (allowed.Contains(written, StringComparer.Ordinal)) return null;
                var sameWord = allowed
                    .Where(x => string.Equals(x, written, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (sameWord.Count == 1)
                {
                    catalogueSpelling = sameWord[0];
                    return null;
                }
                return "Per «" + operation + "», «" + itemName + "» puo' essere solo " +
                       string.Join(", ", allowed.Select(x => "«" + x + "»")) +
                       ", e invece dice «" + written + "».";
            }

            return null;
        }

        /// <summary>
        /// Dentro un oggetto annidato: prima cio' che manca, poi cio' che c'e'
        /// della forma sbagliata. E' la stessa forma di <see cref="Check"/>,
        /// un livello piu' giu'.
        ///
        /// Il nome del campo si scrive per esteso — «feature_rif.passo» — o chi
        /// legge il messaggio non sa dove guardare: lo stesso nome «step» puo'
        /// stare sotto tre riferimenti diversi nello stesso schema.
        /// </summary>
        private static string Nested(
            string operation, string filePath, Dictionary<string, object> rule,
            Dictionary<string, object> val, int depth)
        {
            var property = Take(rule, "properties") as Dictionary<string, object>;
            if (property == null) return null;

            // ── quello che manca ──
            var mandatory = Items(Take(rule, "required"));
            var missingItems = mandatory
                .Where(n => !val.ContainsKey(n) || val[n] == null)
                .ToList();

            if (missingItems.Count == 1)
                return "Per «" + operation + "», «" + filePath + "» deve dire anche «" +
                       missingItems[0] + "», e non lo dice.";
            if (missingItems.Count > 1)
                return "Per «" + operation + "», «" + filePath + "» deve dire anche " +
                       string.Join(", ", missingItems.Select(x => "«" + x + "»")) +
                       ", e non li dice.";

            // ── quello che c'e', ma della forma sbagliata ──
            // Over a copy, as in Controlla: an enum value such as
            // «faccia_rif.genere» can be rewritten in `value_text` on the way.
            foreach (var entry in val.ToList())
            {
                if (entry.Key.StartsWith("_")) continue;
                object raw;
                if (!property.TryGetValue(entry.Key, out raw)) continue;
                var inside = raw as Dictionary<string, object>;
                if (inside == null || entry.Value == null) continue;

                string catalogueSpelling;
                var crooked = Fail(
                    operation, filePath + "." + entry.Key, inside, entry.Value, depth,
                    out catalogueSpelling);
                if (crooked != null) return crooked;
                if (catalogueSpelling != null) val[entry.Key] = catalogueSpelling;
            }

            return null;
        }

        /// <summary>
        /// Un intero JSON e' un numero. Il difetto piu' facile da scrivere in
        /// questo file e' un `type: number` che rifiuta il `20` di
        /// `"thickness_mm": 20` perche' il deserializzatore l'ha fatto `int`.
        /// </summary>
        private static bool ANumber(object v)
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

        private static string WhatItIsCalled(object v)
        {
            if (v == null) return "vuoto";
            if (v is bool) return "vero/falso";
            if (v is string) return "un testo («" + v + "»)";
            if (v is Dictionary<string, object>) return "un oggetto";
            if (v is IEnumerable) return "un elenco";
            return "un " + v.GetType().Name;
        }

        /// <summary>La descrizione del parametro mancante, se il catalogo ne ha una.</summary>
        private static string Suggest(Dictionary<string, object> property, string itemName)
        {
            object raw;
            if (!property.TryGetValue(itemName, out raw)) return "";
            var rule = raw as Dictionary<string, object>;
            if (rule == null) return "";
            var d = Convert.ToString(Take(rule, "description") ?? "").Trim();
            if (string.IsNullOrEmpty(d)) return "";
            if (d.Length > 160) d = d.Substring(0, 160).TrimEnd() + "…";
            return " (" + d + ")";
        }

        private static List<string> Items(object raw)
        {
            var outside = new List<string>();
            var e = raw as IEnumerable;
            if (e == null || raw is string) return outside;
            foreach (var x in e)
            {
                var s = Convert.ToString(x);
                if (!string.IsNullOrWhiteSpace(s)) outside.Add(s);
            }
            return outside;
        }

        private static object Take(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }
    }
}
