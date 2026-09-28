using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Oce.Solid.Agent.Ponte
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
        ///
        /// IT ALSO REWRITES, IN <paramref name="argomenti"/> ITSELF, every enum
        /// value spelled differently from the catalogue (Z-C21.10, pattern
        /// S20): «Superiore» is accepted and goes on as «superiore». The caller
        /// hands the same dictionary to the library, so whatever passes here
        /// reaches the library spelled as the catalogue spells it. See the
        /// enum block in <see cref="Sbaglia"/> for why.
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
            //
            // OVER A COPY OF THE ENTRIES, because an enum value can be
            // rewritten in `argomenti` on the way (see Sbaglia). On .NET
            // Framework even overwriting an existing key bumps the
            // dictionary's version, and the next MoveNext of a foreach over
            // that same dictionary throws InvalidOperationException.
            foreach (var voce in argomenti.ToList())
            {
                // Le chiavi che cominciano con il trattino basso non sono
                // parametri dell'operazione: sono roba nostra («_firme»), e lo
                // schema giustamente non le nomina.
                if (voce.Key.StartsWith("_")) continue;

                object grezzo;
                if (!proprieta.TryGetValue(voce.Key, out grezzo)) continue;
                var regola = grezzo as Dictionary<string, object>;
                if (regola == null || voce.Value == null) continue;

                string catalogueSpelling;
                var storto = Sbaglia(operazione, voce.Key, regola, voce.Value, 0, out catalogueSpelling);
                if (storto != null) return storto;
                if (catalogueSpelling != null) argomenti[voce.Key] = catalogueSpelling;
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
        private const int ProfonditaMassima = 3;

        /// <summary>
        /// Controlla un valore contro la sua regola. Scende anche DENTRO gli
        /// oggetti annidati, e fino alla 0.25.2 non lo faceva.
        ///
        /// LA PROMESSA CHE NON ERA MANTENUTA. La descrizione dello strumento
        /// `disegna` dice al modello: «Il piano viene controllato TUTTO prima di
        /// cominciare: se un passo qualunque ha un nome sbagliato o argomenti
        /// che non rispettano lo schema, non viene eseguito niente e il
        /// documento resta com'era». `PianoInMemoria` ripete la stessa
        /// garanzia.
        ///
        /// Ma il controllo si fermava al primo livello: per `type: "object"`
        /// verificava che fosse un oggetto e basta, senza guardare il `required`
        /// e le `properties` dentro. Lo schema di `ripeti_lineare` dichiara
        /// `feature_rif` come oggetto con `required: ["passo", "genere"]`, e un
        /// piano di quattro passi il cui quarto scrive `feature_rif: {}`
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
        /// Nested objects are rewritten inside <see cref="Annidato"/>.
        /// </summary>
        private static string Sbaglia(
            string operazione, string nome, Dictionary<string, object> regola,
            object valore, int profondita, out string catalogueSpelling)
        {
            catalogueSpelling = null;
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
                    var dentro = valore as Dictionary<string, object>;
                    if (dentro == null)
                        return "Per «" + operazione + "», «" + nome + "» vuole un oggetto e " +
                               "invece e' " + ComeSiChiama(valore) + ".";
                    if (profondita < ProfonditaMassima)
                    {
                        var storto = Annidato(operazione, nome, regola, dentro, profondita + 1);
                        if (storto != null) return storto;
                    }
                    break;

                case "string":
                    // Un numero dove va una stringa NON si rifiuta: chi scrive
                    // «configurazione: 80» intende «"80"», e rifiutarlo sarebbe
                    // pignoleria che costa un giro a chi disegna.
                    break;
            }

            // ── i valori ammessi ─────────────────────────────────────────
            //
            // THE VALUE GOES ON SPELLED AS THE CATALOGUE SPELLS IT (Z-C21.10,
            // pattern S20). The case of a word is form, not merit, so
            // «Superiore» is accepted here, as it always was. Until 0.140.0
            // it then went on to the library untouched, and the library
            // compares Ordinal: `SelezionaPiano` read «Superiore» as an
            // unknown name and fell back to the front plane (the box on the
            // wrong plane, riuscita:true, finding N33), and
            // `telaio_rettangolare` read «Ingombro» as not-«ingombro», that
            // is the centreline branch (a 1080x880 frame for 1000x800, N32).
            // Teaching every comparison in the library to ignore case would
            // leave the next one to be forgotten; rewriting the value here
            // covers them all, because this is the one door every step of
            // «disegna» comes through (Strumenti/Attrezzi.cs).
            //
            // An exact match wins. Otherwise the one value that differs only
            // in case. Two such values (a catalogue spelling one word two
            // ways) leave nothing to choose from, and that stays a rejection.
            var ammessi = Elenco(Prendi(regola, "enum"));
            if (ammessi.Count > 0)
            {
                var scritto = Convert.ToString(valore);
                if (ammessi.Contains(scritto, StringComparer.Ordinal)) return null;
                var sameWord = ammessi
                    .Where(x => string.Equals(x, scritto, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (sameWord.Count == 1)
                {
                    catalogueSpelling = sameWord[0];
                    return null;
                }
                return "Per «" + operazione + "», «" + nome + "» puo' essere solo " +
                       string.Join(", ", ammessi.Select(x => "«" + x + "»")) +
                       ", e invece dice «" + scritto + "».";
            }

            return null;
        }

        /// <summary>
        /// Dentro un oggetto annidato: prima cio' che manca, poi cio' che c'e'
        /// della forma sbagliata. E' la stessa forma di <see cref="Controlla"/>,
        /// un livello piu' giu'.
        ///
        /// Il nome del campo si scrive per esteso — «feature_rif.passo» — o chi
        /// legge il messaggio non sa dove guardare: lo stesso nome «passo» puo'
        /// stare sotto tre riferimenti diversi nello stesso schema.
        /// </summary>
        private static string Annidato(
            string operazione, string percorso, Dictionary<string, object> regola,
            Dictionary<string, object> valore, int profondita)
        {
            var proprieta = Prendi(regola, "properties") as Dictionary<string, object>;
            if (proprieta == null) return null;

            // ── quello che manca ──
            var obbligatori = Elenco(Prendi(regola, "required"));
            var mancanti = obbligatori
                .Where(n => !valore.ContainsKey(n) || valore[n] == null)
                .ToList();

            if (mancanti.Count == 1)
                return "Per «" + operazione + "», «" + percorso + "» deve dire anche «" +
                       mancanti[0] + "», e non lo dice.";
            if (mancanti.Count > 1)
                return "Per «" + operazione + "», «" + percorso + "» deve dire anche " +
                       string.Join(", ", mancanti.Select(x => "«" + x + "»")) +
                       ", e non li dice.";

            // ── quello che c'e', ma della forma sbagliata ──
            // Over a copy, as in Controlla: an enum value such as
            // «faccia_rif.genere» can be rewritten in `valore` on the way.
            foreach (var voce in valore.ToList())
            {
                if (voce.Key.StartsWith("_")) continue;
                object grezzo;
                if (!proprieta.TryGetValue(voce.Key, out grezzo)) continue;
                var dentro = grezzo as Dictionary<string, object>;
                if (dentro == null || voce.Value == null) continue;

                string catalogueSpelling;
                var storto = Sbaglia(
                    operazione, percorso + "." + voce.Key, dentro, voce.Value, profondita,
                    out catalogueSpelling);
                if (storto != null) return storto;
                if (catalogueSpelling != null) valore[voce.Key] = catalogueSpelling;
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
