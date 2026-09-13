using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorksMcp.Mcp;
using SolidWorksMcp.Ponte;

namespace SolidWorksMcp.Strumenti
{
    /// <summary>
    /// I quattro strumenti che il ponte espone.
    ///
    /// PERCHE' QUATTRO E NON TRENTASEI.
    /// Le operazioni di disegno sono trentasei, e la tentazione e' di esporne
    /// una per strumento. Sarebbe sbagliato per due ragioni misurate nel
    /// progetto:
    ///
    ///   1. **il costo.** Gli schemi interi delle trentasei operazioni sono
    ///      decine di migliaia di caratteri in ogni singola richiesta. Il
    ///      catalogo a due livelli del server esiste esattamente per questo.
    ///   2. **l'ordine giusto.** Il difetto piu' caro di questo progetto non e'
    ///      un'operazione sbagliata: e' un'operazione giusta fatta ALLA CIECA.
    ///      Quattro strumenti in fila — guarda, cerca, approfondisci, fai —
    ///      rendono difficile saltare il primo.
    ///
    /// `guarda_il_pezzo` non e' un accessorio: e' il primo, e senza di lui gli
    /// identificativi che gli altri vogliono (`F1`, `E4`, `Corpo1`) non
    /// esistono.
    /// </summary>
    internal sealed class Attrezzi
    {
        private readonly Sessione _sessione;
        private readonly Catalogo _catalogo;

        public Attrezzi(Sessione sessione, Catalogo catalogo)
        {
            _sessione = sessione;
            _catalogo = catalogo;
        }

        public object Elenco()
        {
            return new object[]
            {
                Strumento(
                    "guarda_il_pezzo",
                    "GUARDA il documento aperto in SolidWorks prima di toccarlo: " +
                    "ingombro, volume, corpi, lavorazioni con le loro quote, facce " +
                    "e spigoli con il loro identificativo. Gli identificativi (F1, " +
                    "E4, Corpo1) che le altre operazioni vogliono nascono QUI e " +
                    "valgono per questa lettura: dopo ogni modifica si rilegge.",
                    Oggetto(new Dictionary<string, object>(), new string[0])),

                Strumento(
                    "elenco_operazioni",
                    "L'elenco corto di cosa si puo' chiedere, per famiglia: un " +
                    "nome e una riga ciascuna. Per lo schema completo di una, usa " +
                    "«descrivi_operazione».",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["famiglia"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "solo una famiglia: estrusioni, lavorazioni, " +
                                    "riferimenti, corpi, saldature, proprieta', tavola",
                            },
                        },
                        new string[0])),

                Strumento(
                    "descrivi_operazione",
                    "Lo schema completo di una operazione: cosa vuole, in che " +
                    "unita', cosa deve esserci gia' nel documento.",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["nome"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "il nome esatto, da «elenco_operazioni»",
                            },
                        },
                        new[] { "nome" })),

                Strumento(
                    "disegna",
                    "Esegue in SolidWorks una sequenza di operazioni e RIPORTA " +
                    "COS'E' VENUTO: le misure di ogni passo e lo stato del " +
                    "documento dopo. Si ferma al primo passo fallito, perche' i " +
                    "passi seguenti quasi sempre nominano cio' che il passo " +
                    "fallito avrebbe dovuto creare.",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["passi"] = new Dictionary<string, object>
                            {
                                ["type"] = "array",
                                ["description"] =
                                    "le operazioni da fare, in ordine",
                                ["items"] = Oggetto(
                                    new Dictionary<string, object>
                                    {
                                        ["operazione"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "string",
                                            ["description"] = "il nome dell'operazione",
                                        },
                                        ["argomenti"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "object",
                                            ["description"] =
                                                "gli argomenti, come da «descrivi_operazione»",
                                        },
                                    },
                                    new[] { "operazione" }),
                            },
                            ["qualunque_documento"] = new Dictionary<string, object>
                            {
                                ["type"] = "boolean",
                                ["description"] =
                                    "Normalmente si disegna sul documento che hai " +
                                    "guardato con «guarda_il_pezzo», e se davanti ce " +
                                    "n'e' un altro l'operazione si ferma. Metti true " +
                                    "SOLO se sai che il documento e' cambiato e vuoi " +
                                    "lavorare su quello che c'e' adesso.",
                            },
                        },
                        new[] { "passi" })),
            };
        }

        public Dictionary<string, object> Chiama(
            string nome, Dictionary<string, object> argomenti)
        {
            switch (nome)
            {
                case "guarda_il_pezzo": return GuardaIlPezzo();
                case "elenco_operazioni": return ElencoOperazioni(argomenti);
                case "descrivi_operazione": return DescriviOperazione(argomenti);
                case "disegna": return Disegna(argomenti);
                default:
                    return Protocollo.Contenuto(
                        "Non conosco lo strumento «" + nome + "».", true);
            }
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// L'ultimo documento che «guarda_il_pezzo» ha descritto.
        ///
        /// Non e' un ricordo di comodo: e' il contratto fra i due strumenti.
        /// Un piano nasce guardando UN documento, e va eseguito su QUELLO.
        /// </summary>
        private Dictionary<string, object> _guardato;

        private Dictionary<string, object> GuardaIlPezzo()
        {
            var stato = _sessione.Stato(out var errore);
            if (stato == null) return Protocollo.Contenuto(errore, true);
            _guardato = IdentitaDelloStato(stato);
            return Protocollo.Contenuto(Protocollo.InJson(stato));
        }

        /// <summary>Titolo e percorso presi dallo stato, senza ripassare dal COM.</summary>
        private static Dictionary<string, object> IdentitaDelloStato(
            Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            return new Dictionary<string, object>
            {
                ["titolo"] = stato.TryGetValue("titolo", out var t) ? t : "",
                ["percorso"] = stato.TryGetValue("percorso", out var p) ? p : "",
            };
        }

        private static string NomeLeggibile(Dictionary<string, object> identita)
        {
            if (identita == null) return "(nessuno)";
            var p = Convert.ToString(identita.TryGetValue("percorso", out var x) ? x : "");
            if (!string.IsNullOrWhiteSpace(p)) return p;
            var t = Convert.ToString(identita.TryGetValue("titolo", out var y) ? y : "");
            return string.IsNullOrWhiteSpace(t) ? "(senza nome)" : t;
        }

        private Dictionary<string, object> ElencoOperazioni(
            Dictionary<string, object> argomenti)
        {
            if (_catalogo.ErroreDiCaricamento != null)
                return Protocollo.Contenuto(_catalogo.ErroreDiCaricamento, true);
            var famiglia = Testo(argomenti, "famiglia");
            return Protocollo.Contenuto(Protocollo.InJson(_catalogo.Elenco(famiglia)));
        }

        private Dictionary<string, object> DescriviOperazione(
            Dictionary<string, object> argomenti)
        {
            if (_catalogo.ErroreDiCaricamento != null)
                return Protocollo.Contenuto(_catalogo.ErroreDiCaricamento, true);

            var nome = Testo(argomenti, "nome");
            var descrizione = _catalogo.Descrizione(nome);
            if (descrizione == null)
            {
                // UN ERRORE CHE AIUTA invece di uno che constata: chi ha
                // sbagliato nome quasi sempre ne ha in mente uno vicino.
                var simili = _catalogo.Simili(nome);
                return Protocollo.Contenuto(
                    "Non c'e' nessuna operazione che si chiama «" + nome + "»." +
                    (simili.Count > 0
                        ? " Forse cercavi: " + string.Join(", ", simili) + "."
                        : " Chiedi «elenco_operazioni» per vedere quali ci sono."),
                    true);
            }
            return Protocollo.Contenuto(Protocollo.InJson(descrizione));
        }

        /// <summary>
        /// Il giro vero: si esegue, e SI GUARDA COS'E' VENUTO.
        ///
        /// Il risultato non e' «riuscito». Sono le misure di ogni passo — il
        /// volume tolto, il diametro vero del foro, quante pareti ha forato —
        /// e lo stato del documento DOPO. E' l'unica forma che permette a chi
        /// ha chiesto di accorgersi che il pezzo non e' quello che voleva.
        /// </summary>
        private Dictionary<string, object> Disegna(Dictionary<string, object> argomenti)
        {
            if (!_sessione.Agganciata)
                return Protocollo.Contenuto("Il ponte non e' agganciato a SolidWorks.", true);

            var passi = Prendi(argomenti, "passi") as object[];
            if (passi == null || passi.Length == 0)
                return Protocollo.Contenuto(
                    "Il piano e' vuoto: «passi» deve essere un elenco di " +
                    "{operazione, argomenti}.", true);

            var prima = _sessione.Stato(out var erroreLettura);
            if (prima == null)
                return Protocollo.Contenuto(erroreLettura, true);

            // SI DISEGNA SUL DOCUMENTO CHE SI E' GUARDATO, NON SU QUELLO DAVANTI.
            //
            // Il ponte lavora sul documento ATTIVO, e il documento attivo e'
            // quello la cui finestra sta davanti: cambia se qualcuno clicca,
            // se un'altra operazione ne apre uno, se SolidWorks ne porta uno in
            // primo piano da solo. Il 14 settembre 2026 e' successo davvero —
            // un blocco 314x274x52 estruso dentro il pezzo di qualcun altro,
            // senza un errore, senza un avviso.
            //
            // Qui non si indovina e non si cambia finestra da soli: si
            // CONFRONTA con quello che «guarda_il_pezzo» aveva descritto, e se
            // non combacia ci si ferma. Chi sa cosa sta facendo puo' dire
            // esplicitamente «si', quello davanti» con `qualunque_documento`.
            var adesso = IdentitaDelloStato(prima);
            if (_guardato == null)
            {
                if (!Bool(argomenti, "qualunque_documento"))
                    return Protocollo.Contenuto(
                        "Non ho ancora guardato nessun pezzo, quindi non so su quale " +
                        "documento vuoi che disegni: adesso davanti c'e' «" +
                        NomeLeggibile(adesso) + "». Chiama prima «guarda_il_pezzo» — " +
                        "serve comunque, perche' gli identificativi delle facce " +
                        "nascono li'.", true);
                _guardato = adesso;
            }
            else if (!Ponte.Sessione.StessoDocumento(_guardato, adesso))
            {
                if (!Bool(argomenti, "qualunque_documento"))
                    return Protocollo.Contenuto(
                        "Mi fermo: il documento e' cambiato. Avevi guardato «" +
                        NomeLeggibile(_guardato) + "», adesso davanti c'e' «" +
                        NomeLeggibile(adesso) + "». Disegnare qui vorrebbe dire " +
                        "scrivere nel pezzo sbagliato, e gli identificativi del tuo " +
                        "piano non valgono su questo. Riguarda il pezzo giusto con " +
                        "«guarda_il_pezzo», oppure — se e' proprio questo che vuoi — " +
                        "ripeti con «qualunque_documento»: true.", true);
                _guardato = adesso;
            }

            var esiti = new List<object>();
            bool tuttoBene = true;

            foreach (var p in passi)
            {
                var passo = p as Dictionary<string, object>;
                if (passo == null) continue;

                var nome = Testo(passo, "operazione");
                var suoi = Prendi(passo, "argomenti") as Dictionary<string, object>
                           ?? new Dictionary<string, object>();

                // SI CONTROLLA IL NOME PRIMA DI ESEGUIRE. L'add-in, davanti a
                // un nome che non conosce ma con dei «contorni» dentro, ricade
                // sull'estrusione di profilo: comodo per le ricette del server,
                // pericoloso qui, dove un nome storto diventerebbe un pezzo
                // storto senza che nessuno dica niente.
                if (!_catalogo.Conosce(nome))
                {
                    var simili = _catalogo.Simili(nome);
                    esiti.Add(new Dictionary<string, object>
                    {
                        ["operazione"] = nome,
                        ["riuscita"] = false,
                        ["errore"] =
                            "Non esiste nessuna operazione «" + nome + "»." +
                            (simili.Count > 0
                                ? " Forse cercavi: " + string.Join(", ", simili) + "."
                                : ""),
                    });
                    tuttoBene = false;
                    break;
                }

                var misure = _sessione.Esegui(nome, suoi, out var errore);
                bool ok = misure != null;
                esiti.Add(new Dictionary<string, object>
                {
                    ["operazione"] = nome,
                    ["riuscita"] = ok,
                    ["errore"] = ok ? null : errore,
                    ["misure"] = misure,
                });
                if (!ok) { tuttoBene = false; break; }
            }

            var dopo = _sessione.Stato(out var erroreStato);
            var risultato = new Dictionary<string, object>
            {
                // SI DICE SEMPRE SU COSA SI E' LAVORATO. Anche quando e' ovvio:
                // e' l'unico modo perche' chi legge se ne accorga quando ovvio
                // non era.
                ["documento"] = NomeLeggibile(adesso),
                ["esiti"] = esiti,
                ["stato_dopo"] = dopo,
                ["nota_stato"] = dopo == null ? erroreStato : null,
                ["volume_prima_mm3"] = Volume(prima),
                ["volume_dopo_mm3"] = Volume(dopo),
            };

            return Protocollo.Contenuto(Protocollo.InJson(risultato), !tuttoBene);
        }

        // ─────────────────────────────────────────────────────────────────

        private static object Volume(Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            return stato.TryGetValue("volume_mm3", out var v) ? v : null;
        }

        private static object Prendi(Dictionary<string, object> d, string chiave)
        {
            if (d == null) return null;
            return d.TryGetValue(chiave, out var v) ? v : null;
        }

        private static string Testo(Dictionary<string, object> d, string chiave)
        {
            var v = Prendi(d, chiave);
            return v == null ? null : Convert.ToString(v);
        }

        private static bool Bool(Dictionary<string, object> d, string chiave)
        {
            var v = Prendi(d, chiave);
            if (v == null) return false;
            if (v is bool b) return b;
            return string.Equals(Convert.ToString(v), "true",
                StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, object> Oggetto(
            Dictionary<string, object> proprieta, string[] obbligatori)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = proprieta,
                ["required"] = obbligatori,
            };
        }

        private static Dictionary<string, object> Strumento(
            string nome, string descrizione, Dictionary<string, object> schema)
        {
            return new Dictionary<string, object>
            {
                ["name"] = nome,
                ["description"] = descrizione,
                ["inputSchema"] = schema,
            };
        }
    }
}
