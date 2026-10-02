using System;
using System.Collections.Generic;
using Oce.Solid.Agent.Strumenti;

namespace Oce.Solid.Agent.Mcp
{
    /// <summary>
    /// IL CATALOGO COME **RISORSA**, non solo come strumento (voce W2.12).
    ///
    /// ── CHE DIFFERENZA FA, VISTO CHE IL CONTENUTO E' LO STESSO ──────────
    ///
    /// Uno strumento lo chiama il MODELLO, quando decide di chiamarlo. Una
    /// risorsa la attacca **chi usa il client**, prima ancora che il modello
    /// parli: si sceglie dall'elenco e finisce nel contesto. Sono due momenti
    /// diversi e due decisioni di due persone diverse.
    ///
    /// Per questo catalogo la differenza e' concreta: `list_operations` costa
    /// un giro di strumento — il modello deve prima capire che gli serve, poi
    /// chiederlo, poi leggere la risposta. Chi lavora sa gia' che gli serve, e
    /// con una risorsa lo mette li' e basta. **Non e' un'altra copia del
    /// catalogo: e' un'altra porta sulla stessa.**
    ///
    /// ── E QUI C'ERA IL PERICOLO VERO ────────────────────────────────────
    ///
    /// Due porte sulla stessa cosa diventano due cose il giorno in cui una
    /// delle due si scrive a parte. Percio' questa classe **non sa niente del
    /// catalogo**: prende lo stesso oggetto `Catalogo` che usano gli strumenti
    /// e chiama gli stessi due metodi, `Elenco(null)` e `Descrizione(nome)`.
    /// Non c'e' nessun punto in cui si possa scrivere «anche» qui.
    /// Una prova di contratto lo verifica byte per byte, perche' *una regola
    /// che sta solo in un commento e' una regola che un giorno qualcuno non
    /// legge.*
    ///
    /// ── PERCHE' UNA RISORSA SOLA PIU' UN MODELLO DI URI ─────────────────
    ///
    /// Le operazioni sono piu' di sessanta — **il numero non si scrive qui**,
    /// perche' cambia e un numero fermo in un commento diventa una bugia (e' la
    /// stessa ragione per cui il README e' controllato da una prova invece che
    /// a memoria). Elencarle una per una come risorse
    /// vorrebbe dire mandare sessanta righe a ogni `resources/list`, e chi
    /// guarda l'elenco cerca *il catalogo*, non `fillet_edges`. Percio':
    ///
    ///   - **una risorsa**, `catalog://operations`, che e' l'elenco corto —
    ///     la stessa cosa che risponde `list_operations`;
    ///   - **un modello di URI**, `catalog://operation/{name}`, per lo schema
    ///     completo di una sola. Un modello di URI e' la forma che il
    ///     protocollo ha per dire «ce ne sono tante e si chiamano cosi'»
    ///     invece di elencarle.
    ///
    /// E' lo stesso catalogo a due livelli che regge gli strumenti, detto nel
    /// vocabolario delle risorse.
    /// </summary>
    internal sealed class Risorse
    {
        /// <summary>
        /// L'elenco corto. Scritto una volta sola perche' compare in tre
        /// posti — l'elenco, la lettura e la prova — e tre stringhe uguali
        /// scritte a mano diventano tre stringhe diverse.
        /// </summary>
        public const string UriElenco = "catalog://operations";

        /// <summary>La parte fissa di `catalog://operation/&lt;name&gt;`.</summary>
        public const string PrefissoOperazione = "catalog://operation/";

        private readonly Catalogo _catalogo;

        public Risorse(Catalogo catalogo) { _catalogo = catalogo; }

        /// <summary>
        /// Cosa c'e' da attaccare. Una voce sola: vedi sopra.
        /// </summary>
        public object Elenco()
        {
            return new object[]
            {
                new Dictionary<string, object>
                {
                    ["uri"] = UriElenco,
                    ["name"] = "Operation catalog",
                    ["title"] = "Operation catalog",
                    ["description"] =
                        "Le " + _catalogo.Quante + " operazioni di disegno che questo " +
                        "ponte sa eseguire, raggruppate per famiglia, con una riga " +
                        "ciascuna. Lo schema completo di una sola si legge da " +
                        PrefissoOperazione + "<name>, oppure con lo strumento " +
                        "«describe_operation».",
                    ["mimeType"] = "application/json",
                },
            };
        }

        /// <summary>
        /// I modelli di URI: le operazioni una per una, senza elencarle.
        /// </summary>
        public object Modelli()
        {
            return new object[]
            {
                new Dictionary<string, object>
                {
                    ["uriTemplate"] = PrefissoOperazione + "{name}",
                    ["name"] = "Single operation",
                    ["title"] = "Single operation",
                    ["description"] =
                        "Lo schema completo di UNA operazione: cosa serve prima, cosa " +
                        "fa, e tutti i suoi argomenti. I nomi stanno in " + UriElenco + ".",
                    ["mimeType"] = "application/json",
                },
            };
        }

        /// <summary>
        /// Il contenuto di un URI, o `null` se questo URI non lo conosco.
        ///
        /// **`null` non e' un errore silenzioso**: chi chiama lo trasforma in
        /// un errore di protocollo che dice quali URI esistono. Qui si torna
        /// `null` e basta perche' questa classe non sa come si scrive un
        /// errore JSON-RPC, e non deve saperlo.
        /// </summary>
        public object Leggi(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return null;

            if (string.Equals(uri, UriElenco, StringComparison.Ordinal))
                return Contenuto(uri, _catalogo.Elenco(null));

            if (uri.StartsWith(PrefissoOperazione, StringComparison.Ordinal))
            {
                var nome = uri.Substring(PrefissoOperazione.Length);
                // Un URI puo' arrivare con le lettere non ASCII codificate.
                // I nomi delle operazioni sono tutti ASCII, ma `%20` su un
                // nome sbagliato deve dare «non conosco quel nome», non «URI
                // storto»: sono due messaggi diversi per chi legge.
                try { nome = Uri.UnescapeDataString(nome); } catch { }
                var descrizione = _catalogo.Descrizione(nome);
                if (descrizione == null) return null;
                return Contenuto(uri, descrizione);
            }

            return null;
        }

        /// <summary>
        /// I nomi che somigliano a quello chiesto, per l'errore. Sta qui e non
        /// in chi chiama perche' e' il catalogo a saperlo.
        /// </summary>
        public List<string> Simili(string uri)
        {
            if (uri == null || !uri.StartsWith(PrefissoOperazione, StringComparison.Ordinal))
                return new List<string>();
            var nome = uri.Substring(PrefissoOperazione.Length);
            try { nome = Uri.UnescapeDataString(nome); } catch { }
            return _catalogo.Simili(nome) ?? new List<string>();
        }

        private static object Contenuto(string uri, object dati)
        {
            return new Dictionary<string, object>
            {
                ["contents"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["uri"] = uri,
                        ["mimeType"] = "application/json",
                        // IL TESTO, NON L'OGGETTO. `resources/read` vuole
                        // `text` o `blob`: mandare l'oggetto strutturato
                        // sarebbe una risposta che sembra giusta e che un
                        // client legge vuota.
                        ["text"] = Protocollo.InJson(dati),
                    },
                },
            };
        }
    }
}
