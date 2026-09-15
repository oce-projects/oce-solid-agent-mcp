using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oce.Solid.Agent.Mcp;
using Oce.Solid.Agent.Ponte;

namespace Oce.Solid.Agent.Strumenti
{
    /// <summary>
    /// I cinque strumenti che il ponte espone.
    ///
    /// PERCHE' CINQUE E NON TRENTOTTO.
    /// Le operazioni di disegno sono trentotto, e la tentazione e' di esporne
    /// una per strumento. Sarebbe sbagliato per due ragioni misurate nel
    /// progetto:
    ///
    ///   1. **il costo.** Gli schemi interi delle trentotto operazioni sono
    ///      decine di migliaia di caratteri in ogni singola richiesta. Il
    ///      catalogo a due livelli del server esiste esattamente per questo.
    ///   2. **l'ordine giusto.** Il difetto piu' caro di questo progetto non e'
    ///      un'operazione sbagliata: e' un'operazione giusta fatta ALLA CIECA.
    ///      Gli strumenti in fila — guarda, cerca, approfondisci, fai —
    ///      rendono difficile saltare il primo.
    ///
    /// `guarda_il_pezzo` non e' un accessorio: e' il primo, e senza di lui gli
    /// identificativi che gli altri vogliono (`F1`, `E4`, `Corpo1`) non
    /// esistono.
    ///
    /// **IL QUINTO E' `annulla`, ed e' arrivato dopo (voce R1).** Non e'
    /// un'operazione di disegno, e sta fuori dal catalogo per una ragione
    /// precisa: le operazioni si eseguono dentro `disegna`, cioe' dentro un
    /// piano, e annullare un piano da dentro il piano stesso e' un nodo. Il
    /// motivo per cui gli strumenti non sono trentotto — non esporre le
    /// operazioni una per una — non riguarda un gesto che operazione non e'.
    ///
    /// **Togliere una lavorazione invece SI'**, ed e' nel catalogo
    /// (`sopprimi_lavorazione`, `elimina_lavorazione`, voce R2): quelle sono
    /// operazioni sul pezzo come tutte le altre, si scrivono dentro un piano e
    /// hanno senso in mezzo a una sequenza. La differenza con `annulla` non e'
    /// «toglie» contro «aggiunge»: e' che `annulla` disfa **un piano**, e un
    /// piano non e' geometria.
    ///
    /// Fino alla 0.31.0 questo ponte diceva **ventuno volte** «annullalo con
    /// Ctrl+Z» a un destinatario che non ha una tastiera.
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
                    "valgono per questa lettura: dopo ogni modifica si rilegge.\n\n" +
                    "Con «viste» torna anche una FOTOGRAFIA del pezzo, che e' " +
                    "l'unico modo di sapere che FORMA ha: l'elenco delle facce " +
                    "dice quante sono e quanto misurano, non com'e' fatto. " +
                    "Costa: ogni vista pesa piu' dell'intero stato scritto, " +
                    "quindi si chiede quando la forma e' in dubbio — dopo un " +
                    "guscio, dopo uno smusso, prima di dire che il pezzo e' " +
                    "finito — non a ogni lettura.",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["viste"] = new Dictionary<string, object>
                            {
                                ["type"] = "array",
                                ["description"] =
                                    "quali viste fotografare, al massimo " +
                                    MassimoDiViste + ". Se non sai quale, chiedi " +
                                    "«isometrica»: e' la sola che da sola mostra una " +
                                    "forma. Ci sono: " +
                                    string.Join(", ", Sessione.NomiDelleViste) + ".",
                                ["items"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string",
                                    ["enum"] = Nomi(Sessione.NomiDelleViste),
                                },
                            },
                            ["sezione"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["description"] =
                                    "UNO SPACCATO: il pezzo tagliato da un piano. Dice di un " +
                                    "guscio piu' di dieci viste esterne — dall'esterno una " +
                                    "scatola svuotata e una piena sono lo stesso " +
                                    "parallelepipedo. Taglia solo il DISEGNO, non il pezzo, e " +
                                    "si toglie da sola appena scattata.",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["piano"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["enum"] = Nomi(Sessione.NomiDeiPiani),
                                        ["description"] = "su quale piano tagliare",
                                    },
                                    ["offset_mm"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "number",
                                        ["description"] =
                                            "di quanto spostare il piano di taglio, in mm. " +
                                            "Predefinito 0, cioe' il piano stesso — che passa " +
                                            "per l'origine, non per il centro del pezzo.",
                                    },
                                },
                                ["required"] = new[] { "piano" },
                            },
                        },
                        new string[0]),
                    soloLettura: true, distruttivo: false, idempotente: true),

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
                        new string[0]),
                    soloLettura: true, distruttivo: false, idempotente: true),

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
                        new[] { "nome" }),
                    soloLettura: true, distruttivo: false, idempotente: true),

                Strumento(
                    "disegna",
                    "Esegue in SolidWorks una sequenza di operazioni e RIPORTA " +
                    "COS'E' VENUTO: le misure di ogni passo e lo stato del " +
                    "documento dopo.\n\n" +
                    "Il piano viene controllato TUTTO prima di cominciare: se un " +
                    "passo qualunque ha un nome sbagliato o argomenti che non " +
                    "rispettano lo schema, non viene eseguito niente e il " +
                    "documento resta com'era.\n\n" +
                    "Se invece un passo fallisce mentre viene eseguito, ci si " +
                    "ferma li': i passi seguenti quasi sempre nominano cio' che " +
                    "il passo fallito avrebbe dovuto creare. Quello che era gia' " +
                    "stato costruito resta, e «annulla» lo toglie tutto in una " +
                    "volta.\n\n" +
                    "Un passo puo' nominare la geometria creata da un passo " +
                    "PRECEDENTE dello stesso piano (per esempio faccia_rif: " +
                    "{passo: 1, genere: \"faccia_superiore\"}): e' l'unico modo, " +
                    "perche' quella faccia non ha ancora nessun identificativo.",
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
                            ["documento"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "Su quale documento disegnare: il titolo o il " +
                                    "percorso, copiato da «guarda_il_pezzo». Serve " +
                                    "SOLO se il documento davanti e' cambiato da " +
                                    "quando l'hai guardato: normalmente si omette. " +
                                    "Nominarlo vuol dire «so quale pezzo sto " +
                                    "toccando», e un nome che non corrisponde viene " +
                                    "rifiutato.",
                            },
                        },
                        new[] { "passi" }),
                    // Rieseguire lo stesso piano NON rimette il pezzo
                    // com'era: fa un secondo foro. Dichiararlo idempotente
                    // inviterebbe un client a riprovare da solo dopo un
                    // errore di rete.
                    soloLettura: false, distruttivo: true, idempotente: false),

                Strumento(
                    "annulla",
                    "RITIRA l'ultimo piano eseguito da «disegna», tutto intero. " +
                    "E' il gesto che questo ponte chiedeva di fare con Ctrl+Z, " +
                    "che tu non hai.\n\n" +
                    "Si annulla SOLO se il pezzo e' ancora come «disegna» lo " +
                    "aveva lasciato: se dopo il piano qualcuno ha lavorato a " +
                    "mano, mi fermo e dico cosa e' cambiato — annullare " +
                    "allora toglierebbe il lavoro di qualcun altro.\n\n" +
                    "Dopo, il pezzo torna al volume che aveva prima del piano, e " +
                    "gli identificativi delle facce NON sono piu' quelli: la " +
                    "risposta porta lo stato nuovo, ed e' quello che vale.",
                    Oggetto(new Dictionary<string, object>(), new string[0]),
                    // NON e' idempotente: chiamarlo due volte non annulla due
                    // volte lo stesso piano — la seconda toglierebbe quello
                    // che c'era PRIMA, cioe' lavoro che questo ponte non ha
                    // fatto. Il ricordo si cancella dopo il primo, ma
                    // dichiararlo idempotente inviterebbe un client a
                    // riprovare da solo dopo un errore di rete.
                    soloLettura: false, distruttivo: true, idempotente: false),
            };
        }

        public Dictionary<string, object> Chiama(
            string nome, Dictionary<string, object> argomenti)
        {
            switch (nome)
            {
                case "guarda_il_pezzo": return GuardaIlPezzo(argomenti);
                case "elenco_operazioni": return ElencoOperazioni(argomenti);
                case "descrivi_operazione": return DescriviOperazione(argomenti);
                case "disegna": return Disegna(argomenti);
                case "annulla": return Annulla();
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

        /// <summary>
        /// Com'erano fatte le entita' quando «guarda_il_pezzo» le ha
        /// descritte: uid -> punto, normale, area, lunghezza.
        ///
        /// GLI UID SONO POSIZIONALI. L'add-in li riassegna a ogni lettura
        /// ordinando le facce per area, quindi basta un passo che cambia
        /// il pezzo perche' «F1» indichi un'altra faccia. Un piano pero'
        /// nasce da UNA lettura: i suoi uid parlano di quel momento li'.
        ///
        /// MISURATO il 14 settembre 2026, tre passi in una sola chiamata a
        /// «disegna» su un blocco 200x100x20:
        ///   1. estrudi il blocco          -> F1 = faccia di sopra, 20.000 mm2
        ///   2. tasca Ø60 profonda 5 su F1 -> la faccia di sopra scende a
        ///      17.172,57 mm2 e passa DIETRO a quella di sotto nell'ordine
        ///   3. tasca Ø10 profonda 5 su F1 -> «F1» adesso e' quella di SOTTO
        /// Tutti e tre i passi hanno risposto «riuscita: true». La tasca
        /// piccola e' finita sulla faccia sbagliata, le profondita' erano
        /// quelle chieste, e il volume tornava: 385.470,1 mm3, esatto. Non
        /// c'era niente, in quella risposta, che facesse sospettare.
        ///
        /// L'add-in sa gia' difendersi: legge «_firme» dagli argomenti e
        /// confronta. Ma quella chiave la scriveva solo il server di
        /// OpenPDM, e qui non la scriveva nessuno — quindi il confronto
        /// non aveva termini e passava sempre. Questo campo e' il termine
        /// che mancava.
        /// </summary>
        private Dictionary<string, object> _firmeViste;

        /// <summary>
        /// Quante viste al massimo in una sola risposta.
        ///
        /// Tre bastano a definire una forma — isometrica piu' due — e sono
        /// gia' tre volte il costo dello stato scritto. Nove (tutte quelle
        /// che esistono) sarebbero un ordine di grandezza in piu' per un
        /// pezzo, e nessuno le guarderebbe tutte.
        /// </summary>
        private const int MassimoDiViste = 3;

        /// <summary>Larghezza e altezza del fotogramma.</summary>
        private const int LarghezzaVista = 1024;
        private const int AltezzaVista = 768;

        private Dictionary<string, object> GuardaIlPezzo(
            Dictionary<string, object> argomenti)
        {
            var stato = _sessione.Stato(out var errore);
            if (stato == null) return Protocollo.Contenuto(errore, true);
            _guardato = IdentitaDelloStato(stato);
            _firmeViste = FirmeDelloStato(stato);
            TogliLeAncore(stato);

            var chieste = VisteChieste(argomenti);
            var sezione = Prendi(argomenti, "sezione") as Dictionary<string, object>;
            if (chieste.Count == 0 && sezione == null)
                return Protocollo.Contenuto(Protocollo.InJson(stato));

            // LO STATO ESCE COMUNQUE, anche se nessuna fotografia riesce.
            // Un `guarda_il_pezzo` che fallisce perche' non si e' potuto
            // scattare toglierebbe a chi legge anche l'elenco delle facce —
            // cioe' l'unica cosa che gli serviva davvero. I motivi delle
            // fotografie mancate si dicono nel testo, accanto allo stato.
            var immagini = new List<KeyValuePair<string, byte[]>>();
            var mancate = new List<string>();
            var riuscite = new List<string>();

            foreach (var quale in chieste)
            {
                // LO STESSO DOCUMENTO DELLO STATO, PRIMA DI OGNI SCATTO.
                //
                // `Stato` e `Fotografia` chiedono a SOLIDWORKS qual e' il
                // documento davanti, ognuna per conto suo: fra le due
                // chiamate basta che chi lavora porti avanti un'altra
                // finestra e il ponte risponde con la fotografia di un pezzo
                // e le misure di un altro. Nessun errore, nessun avviso,
                // un'immagine che contraddice l'elenco accanto a cui sta.
                //
                // E' esattamente il difetto costato «Parte4» l'11 settembre,
                // rifatto nella forma piu' pura: due letture di `ActiveDoc`
                // trattate come se fossero una sola.
                //
                // Si controlla A OGNI GIRO e non una volta sola: tre viste
                // sono tre ridisegni e un paio di secondi, e la finestra
                // puo' cambiare durante.
                var adesso = _sessione.Identita(_sessione.DocumentoAttivo());
                if (!Sessione.StessoDocumento(_guardato, adesso))
                {
                    mancate.Add(quale + ": davanti adesso c'e' «" +
                                NomeLeggibile(adesso) + "», non «" +
                                NomeLeggibile(_guardato) + "». Non fotografo " +
                                "un pezzo diverso da quello che ho appena " +
                                "misurato.");
                    continue;
                }

                var png = _sessione.Fotografia(
                    quale, LarghezzaVista, AltezzaVista, out var perche);
                if (png != null && png.Length > 0)
                {
                    immagini.Add(new KeyValuePair<string, byte[]>("image/png", png));
                    riuscite.Add(quale);
                }
                else
                {
                    mancate.Add(quale + ": " + (perche ?? "non so perche'"));
                }
            }

            // ── LO SPACCATO, se lo hanno chiesto (voce V4) ───────────────
            //
            // Viene DOPO le viste esterne e prima del testo: nell'ordine in cui
            // si guarda un pezzo, prima da fuori e poi dentro.
            if (sezione != null)
            {
                var adessoSez = _sessione.Identita(_sessione.DocumentoAttivo());
                if (!Sessione.StessoDocumento(_guardato, adessoSez))
                {
                    mancate.Add("sezione: davanti adesso c'e' «" +
                                NomeLeggibile(adessoSez) + "», non «" +
                                NomeLeggibile(_guardato) + "».");
                }
                else
                {
                    string piano = Testo(sezione, "piano");
                    double offset = 0;
                    object grezzo = Prendi(sezione, "offset_mm");
                    if (grezzo != null)
                        try { offset = Convert.ToDouble(grezzo,
                                  System.Globalization.CultureInfo.InvariantCulture); }
                        catch { }

                    // UNA SEZIONE SI GUARDA DI FRONTE, e non e' una preferenza.
                    //
                    // Il primo giro la fotografava nell'orientamento corrente,
                    // cioe' in isometrica. Misurato: un modello a cui e' stata
                    // data quella immagine ha letto la parete «6 volte
                    // nell'altezza» e il fondo «8», quando il vero e' **12,5**
                    // per tutti e due — sbagliato del 40-50%.
                    //
                    // Non era colpa sua: in isometrica uno spessore sottile e'
                    // scorciato, e sembra piu' grosso di quello che e'. Guardata
                    // perpendicolarmente al piano di taglio, la sezione e' in
                    // scala vera nelle due direzioni che contano.
                    //
                    // La vista si ricava dal piano, e non si chiede: chi chiede
                    // uno spaccato sul piano destro vuole guardarlo da destra.
                    // Poterla scegliere sarebbe un modo in piu' di sbagliare.
                    var png = _sessione.Spaccato(
                        VistaDiFronteAl(piano), piano, offset,
                        LarghezzaVista, AltezzaVista, out var perche);
                    if (png != null && png.Length > 0)
                    {
                        immagini.Add(new KeyValuePair<string, byte[]>("image/png", png));
                        riuscite.Add("sezione sul piano " + piano +
                                     (Math.Abs(offset) > 1e-9
                                         ? " a " + offset.ToString("0.##",
                                             System.Globalization.CultureInfo.InvariantCulture) + " mm"
                                         : ""));
                    }
                    else mancate.Add("sezione: " + (perche ?? "non so perche'"));
                }
            }

            var testo = new StringBuilder();
            if (riuscite.Count > 0)
            {
                // LA DIDASCALIA PRIMA DELLE IMMAGINI, E NELL'ORDINE.
                // Le immagini arrivano senza nome: sapere che la prima e'
                // l'isometrica e la seconda la frontale e' la differenza fra
                // una vista e un indovinello.
                testo.Append(riuscite.Count == 1
                    ? "Qui sotto la vista " + riuscite[0] + " del pezzo."
                    : "Qui sotto " + riuscite.Count + " viste del pezzo, in " +
                      "quest'ordine: " + string.Join(", ", riuscite) + ".");
                testo.Append(" Le fotografie mostrano la FORMA; le misure sono " +
                             "nello stato qui di seguito, ed e' li' che stanno " +
                             "gli identificativi.\n\n");
            }
            if (mancate.Count > 0)
                testo.Append("Viste non riuscite — " +
                             string.Join("; ", mancate) + "\n\n");

            testo.Append(Protocollo.InJson(stato));

            return Protocollo.ContenutoConImmagini(testo.ToString(), immagini);
        }

        /// <summary>
        /// Quali viste ha chiesto chi legge: nomi veri, senza doppioni, al
        /// massimo <see cref="MassimoDiViste"/>.
        ///
        /// UN NOME SBAGLIATO NON SI SCARTA IN SILENZIO: arriva a
        /// `VistaDelPezzo.Scatta`, che risponde con l'elenco di quelli veri.
        /// Scartarlo qui vorrebbe dire rispondere «ecco il pezzo» a chi ha
        /// chiesto una vista che non ha mai avuto — e non capirebbe perche'
        /// manca.
        /// </summary>
        private static List<string> VisteChieste(Dictionary<string, object> argomenti)
        {
            // `Stringhe` C'ERA GIA', e fa esattamente la cosa difficile: una
            // stringa e' un `IEnumerable` **di caratteri**, quindi
            // `viste: "isometrica"` — l'errore piu' naturale del mondo, e lo
            // schema chiede un elenco — scorso come sequenza darebbe tre viste
            // che si chiamano «i», «s», «o». Tre rifiuti incomprensibili al
            // posto della fotografia chiesta.
            //
            // Scriverne una seconda copia qui sarebbe stato il difetto che
            // questo progetto ha visto tre volte oggi: la regola giusta gia'
            // scritta da un'altra parte, e una riga nuova che non la fa.
            var fuori = new List<string>();
            foreach (var x in Stringhe(Prendi(argomenti, "viste")))
            {
                var nome = x.Trim();
                if (nome.Length == 0) continue;
                if (fuori.Contains(nome, StringComparer.OrdinalIgnoreCase)) continue;
                fuori.Add(nome);
                if (fuori.Count >= MassimoDiViste) break;
            }
            return fuori;
        }

        /// <summary>
        /// La vista da cui si guarda uno spaccato: quella perpendicolare al
        /// piano di taglio.
        ///
        /// Null per un piano che non si conosce — e allora si scatta
        /// nell'orientamento corrente, che e' meglio di niente: il rifiuto per
        /// il nome sbagliato lo fa `ScattaInSezione`, che conosce i piani veri,
        /// e farlo anche qui vorrebbe dire scrivere due volte lo stesso elenco.
        /// </summary>
        private static string VistaDiFronteAl(string piano)
        {
            switch (piano)
            {
                case "frontale": return "frontale";
                case "superiore": return "superiore";
                case "destro": return "destra";
                default: return null;
            }
        }

        /// <summary>
        /// I nomi come vettore, che e' la forma che JSON Schema vuole per
        /// `enum`: un `IEnumerable&lt;string&gt;` serializzato diventa
        /// un oggetto, non un elenco.
        /// </summary>
        private static string[] Nomi(IEnumerable<string> nomi)
        {
            return new List<string>(nomi).ToArray();
        }

        /// <summary>
        /// L'ANCORA NON SI MANDA A CHI LEGGE, e non e' per risparmiare posto —
        /// anche se ne risparmia.
        ///
        /// `punto_mm` e `ancora` fanno due mestieri diversi e hanno due
        /// destinatari diversi:
        ///
        ///   * `punto_mm` serve a **selezionare**: e' un punto garantito sulla
        ///     faccia, e chi legge lo usa per dire dove lavorare;
        ///   * `ancora` serve a **riconoscere**: e' il baricentro d'area, che
        ///     non sta necessariamente sulla faccia (quello di una faccia a
        ///     ferro di cavallo cade nel vuoto) e non si puo' usare per
        ///     selezionare niente.
        ///
        /// Mandarli tutti e due vorrebbe dire offrire due punti a chi ne deve
        /// usare uno, e la scelta sbagliata non darebbe errore: darebbe un foro
        /// fuori dal materiale.
        ///
        /// E il posto conta lo stesso: su un telaio da quattro barre l'ancora
        /// su sessanta facce sono **1.400 caratteri**, e lo stato passava da
        /// 15.700 a 17.171 — oltre il tetto dei 16 KB che il banco sorveglia.
        ///
        /// Si toglie DOPO aver costruito le firme, che se la portano dietro.
        /// </summary>
        private static void TogliLeAncore(Dictionary<string, object> stato)
        {
            if (stato == null) return;
            var facce = (stato.TryGetValue("facce", out var g) ? g : null)
                        as System.Collections.IEnumerable;
            if (facce == null) return;
            foreach (var v in facce)
            {
                var voce = v as Dictionary<string, object>;
                if (voce == null) continue;
                voce.Remove("ancora");
                // IL PERSIST ID SI TOGLIE PER DUE RAGIONI, e la seconda basta
                // da sola. Non e' leggibile — e' un blob opaco, e chi legge non
                // ci puo' fare niente. E pesa: 375-423 byte per faccia, cioe'
                // ~530 caratteri in base64, che su sessanta facce sono ~32 KB,
                // il DOPPIO dell'intero tetto dello stato.
                voce.Remove("persist_id");
            }
        }

        /// <summary>
        /// Cosa basta per riconoscere la stessa entita' fra due letture.
        ///
        /// Gli stessi quattro campi che usa il server di OpenPDM, cosi' le
        /// due strade dicono la stessa cosa all'add-in. Chi confronta
        /// davvero e' lui, e guarda punto e normale: l'area di una faccia
        /// cambia in modo legittimo — ci si fa un foro — mentre
        /// l'orientamento no.
        /// </summary>
        private static Dictionary<string, object> FirmeDelloStato(
            Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            var fuori = new Dictionary<string, object>();

            // LA SCATOLA DEL PEZZO, la stessa per tutte le entita'.
            //
            // Sta nella firma di ognuna e non una volta sola in fondo perche'
            // `_firme` e' una mappa uid -> firma, e chi la legge cerca per uid:
            // una chiave che uid non e' sarebbe un caso speciale da ricordarsi
            // in tre posti. Sei numeri per entita' in un allegato che porta gia'
            // ~530 caratteri di persist ID a faccia non si notano.
            //
            // SERVE SOLO A SCRIVERE UNA FRASE (voce E10): dove stava la faccia
            // rispetto al pezzo, quando il pezzo e' cambiato. Il motivo per cui
            // non puo' diventare un criterio di identita' sta per esteso in
            // `Riscontro.Disegno/Frasi.cs`.
            double[] scatolaPezzo = ScatolaDelPezzo(stato);

            // I CORPI SONO ENTITA' COME LE ALTRE: per un saldato sono LE
            // entita', e «tappa le estremita' di C1» nomina corpi.
            foreach (var famiglia in new[] { "facce", "spigoli", "feature", "assi", "corpi" })
            {
                // SI SCORRE COME IEnumerable, NON SI CASTA A object[].
                // Lo stato arriva dall'add-in con i suoi tipi veri, e la' le
                // famiglie sono List<Dictionary<string, object>>: un
                // «as object[]» torna null in silenzio, e il 14 settembre 2026
                // questo controllo e' rimasto spento per quello. Passando da
                // IEnumerable va bene tutte e due le forme, anche quella che
                // arriva da un JSON.
                var voci = (stato.TryGetValue(famiglia, out var g) ? g : null)
                           as System.Collections.IEnumerable;
                if (voci == null) continue;
                foreach (var v in voci)
                {
                    var voce = v as Dictionary<string, object>;
                    if (voce == null || !voce.TryGetValue("uid", out var uid) || uid == null)
                        continue;
                    var firma = new Dictionary<string, object>();
                    // `lavorazione` e `lavorazione_id` servono alla
                    // corroborazione del ritrovamento (voce E3): senza, una
                    // faccia di un'altra lavorazione finita nello stesso posto
                    // e' indistinguibile da quella che si cercava.
                    // `centro_mm` e `ingombro_mm` sono la firma dei CORPI, e
                    // fino alla 0.24.0 non venivano copiati: per «C1» la firma
                    // usciva vuota, `FirmaCombacia` non aveva niente da
                    // confrontare e rispondeva sempre di si'. I corpi erano
                    // controllati per ESISTENZA e non per IDENTITA'.
                    foreach (var campo in new[]
                             {
                                 "punto_mm", "normale", "area_mm2", "lunghezza_mm",
                                 "lavorazione_id", "centro_mm", "ingombro_mm", "ancora",
                                 "feature_id",
                                 // IL PERSIST ID VIAGGIA QUI E SOLO QUI.
                                 // E' il CANDIDATO del ritrovamento (voce E6):
                                 // ~530 caratteri in base64 per faccia, che
                                 // nello stato sarebbero ~32 KB su sessanta
                                 // facce — il doppio del tetto. La firma non va
                                 // a nessun modello, e' un allegato interno al
                                 // passo, e li' il posto non e' un problema.
                                 "persist_id",
                             })
                        if (voce.TryGetValue(campo, out var x) && x != null)
                            firma[campo] = x;

                    // IL NOME DELLA LAVORAZIONE SI RISOLVE QUI.
                    //
                    // Lo stato lo scrive una volta sola, in una mappa in fondo,
                    // perche' ripeterlo su sessanta facce faceva sforare il
                    // tetto dei 16 KB. La firma invece se lo porta esteso: non
                    // va a nessun modello — e' un allegato interno al passo — e
                    // serve a scrivere un rifiuto che nomina la lavorazione
                    // attesa invece di darne il numero.
                    if (firma.TryGetValue("lavorazione_id", out var idLav) && idLav != null)
                    {
                        var nome = NomeLavorazione(stato, idLav);
                        if (nome != null) firma["lavorazione"] = nome;
                    }
                    // DOPO il conteggio di sopra, mai prima: una firma che
                    // avesse SOLO la scatola del pezzo non direbbe niente di
                    // quell'entita', e finirebbe lo stesso in `_firme` facendo
                    // credere che una firma ci sia.
                    if (firma.Count > 0 && scatolaPezzo != null)
                        firma["scatola_pezzo_mm"] = scatolaPezzo;
                    if (firma.Count > 0) fuori[Convert.ToString(uid)] = firma;
                }
            }
            return fuori.Count > 0 ? fuori : null;
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
        /// <summary>
        /// La frase che si aggiunge a ogni rifiuto di FORMA.
        ///
        /// Il piano si controlla tutto prima di cominciare, quindi un errore al
        /// passo 3 ferma anche i primi due. La risposta pero' elenca solo il
        /// passo che ha sbagliato, e senza questa frase chi legge puo' credere
        /// che i precedenti siano stati eseguiti — e mettersi a cercare nel
        /// documento una piastra che non c'e'.
        /// </summary>
        private const string NienteToccato =
            " Il piano non e' stato eseguito per niente: il documento e' rimasto " +
            "com'era. Correggi questo passo e rimanda il piano intero.";

        private Dictionary<string, object> Disegna(Dictionary<string, object> argomenti)
        {
            if (!_sessione.Agganciata)
                return Protocollo.Contenuto("Il ponte non e' agganciato a SolidWorks.", true);

            var passi = Prendi(argomenti, "passi") as object[];
            if (passi == null || passi.Length == 0)
                return Protocollo.Contenuto(
                    "Il piano e' vuoto: «passi» deve essere un elenco di " +
                    "{operazione, argomenti}.", true);

            // SENZA ANCORA: questa lettura serve a sapere su QUALE documento si
            // lavora, quanto volume c'era e quante lavorazioni erano gia' in
            // errore. Non se ne costruiscono firme (quelle si fanno su «dopo»,
            // piu' sotto) e non torna a chi legge. L'ancora costa un
            // GetTessTriangles per faccia, e qui si pagava per buttarla.
            var prima = _sessione.StatoSenzaAncora(out var erroreLettura);
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
            var nominato = Testo(argomenti, "documento");

            // SI NOMINA IL DOCUMENTO, NON SI SPEGNE IL CONTROLLO.
            //
            // La prima versione aveva un `qualunque_documento: true` per
            // scavalcare il rifiuto. Era un difetto: un argomento che
            // disattiva una difesa e' un invito a disattivarla, e chi lo
            // incontra ha tutti gli incentivi per riprovare con quello
            // acceso — cioe' la difesa sparisce proprio nel caso in cui
            // serviva.
            //
            // Nominare il documento chiede la stessa cosa in una forma che
            // non si puo' dare a caso: per scriverne il nome bisogna averlo
            // guardato. Un booleano lo mette chiunque; un nome sbagliato
            // viene rifiutato come tutto il resto.
            bool loHaNominato =
                !string.IsNullOrWhiteSpace(nominato) &&
                Ponte.Sessione.StessoDocumento(
                    adesso,
                    new Dictionary<string, object>
                    {
                        [nominato.IndexOfAny(new[] { '\\', '/' }) >= 0
                            ? "percorso" : "titolo"] = nominato,
                    });

            if (!loHaNominato)
            {
                if (_guardato == null)
                    return Protocollo.Contenuto(
                        "Non ho ancora guardato nessun pezzo, quindi non so su quale " +
                        "documento vuoi che disegni: adesso davanti c'e' «" +
                        NomeLeggibile(adesso) + "». Chiama prima «guarda_il_pezzo» — " +
                        "serve comunque, perche' gli identificativi delle facce " +
                        "nascono li'.", true);

                if (!Ponte.Sessione.StessoDocumento(_guardato, adesso))
                    return Protocollo.Contenuto(
                        "Mi fermo: il documento e' cambiato. Avevi guardato «" +
                        NomeLeggibile(_guardato) + "», adesso davanti c'e' «" +
                        NomeLeggibile(adesso) + "». Disegnare qui vorrebbe dire " +
                        "scrivere nel pezzo sbagliato, e gli identificativi del tuo " +
                        "piano non valgono su questo.\n\n" +
                        "Se e' cambiato per una ragione che conosci, rileggilo con " +
                        "«guarda_il_pezzo». Se sai gia' che vuoi lavorare proprio su " +
                        "quello che c'e' adesso, ripeti passando «documento»: \"" +
                        NomeLeggibile(adesso) + "\".", true);
            }
            // LE FIRME VALGONO SOLO SUL DOCUMENTO CHE LE HA PRODOTTE.
            // Se il piano nomina un altro documento, gli uid del piano non
            // parlano di questo e allegare le firme vecchie produrrebbe
            // rifiuti che non c'entrano niente. Meglio nessun confronto che
            // un confronto fra due pezzi diversi.
            var firme =
                _guardato != null && Ponte.Sessione.StessoDocumento(_guardato, adesso)
                    ? _firmeViste
                    : null;

            _guardato = adesso;

            // IL PIANO SI CONTROLLA TUTTO PRIMA DI TOCCARE IL DOCUMENTO.
            //
            // Nome dell'operazione, percorsi e forma degli argomenti si vedono
            // leggendo, senza aprire niente. Prima si controllavano a mano a
            // mano: un errore di forma al passo 3 fermava il piano con i primi
            // due GIA' COSTRUITI, e nel documento restava mezzo pezzo per una
            // cosa che si poteva dire prima di cominciare.
            //
            // Adesso o il piano e' scrivibile tutto, o non si tocca niente.
            var daFare = new List<Riscontro.Disegno.PassoDelPiano>();
            var esiti = new List<object>();
            bool tuttoBene = true;
            int ordine = 0;

            foreach (var p in passi)
            {
                var passo = p as Dictionary<string, object>;
                if (passo == null) continue;
                ordine++;

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
                                : "") + NienteToccato,
                    });
                    tuttoBene = false;
                    break;
                }

                // I PERCORSI SI CONTROLLANO PRIMA DI ESEGUIRE.
                // Sono l'unica cosa che questo ponte scrive fuori dal
                // documento aperto, e arrivano da un testo generato.
                var percorsoStorto = Ponte.Percorsi.Controlla(suoi);
                if (percorsoStorto != null)
                {
                    esiti.Add(new Dictionary<string, object>
                    {
                        ["operazione"] = nome,
                        ["riuscita"] = false,
                        ["errore"] = percorsoStorto + NienteToccato,
                    });
                    tuttoBene = false;
                    break;
                }

                // GLI ARGOMENTI RISPETTANO LO SCHEMA CHE ABBIAMO
                // DICHIARATO NOI? Il ponte offre trentasei operazioni con
                // schemi dettagliati: non guardarli vuol dire che lo
                // schema serve solo a suggerire, e che uno «spessore_mm»
                // dimenticato arriva a valle come zero — un'estrusione di
                // spessore nullo, rifiutata da SOLIDWORKS con un messaggio
                // suo, nel migliore dei casi.
                //
                // SOLO LA FORMA, MAI IL MERITO: vedi Ponte/Forma.cs.
                var formaStorta = Ponte.Forma.Controlla(
                    nome, _catalogo.Schema(nome), suoi);
                if (formaStorta != null)
                {
                    esiti.Add(new Dictionary<string, object>
                    {
                        ["operazione"] = nome,
                        ["riuscita"] = false,
                        ["errore"] = formaStorta + NienteToccato,
                    });
                    tuttoBene = false;
                    break;
                }

                // GLI IDENTIFICATIVI CITATI ESISTONO DAVVERO?
                //
                // E' l'altra meta' della difesa anti-deriva, e prende un caso
                // che le FIRME non possono prendere: un piano che crea il pezzo
                // da zero e poi cita «F1» non ha nessuna firma da confrontare,
                // perche' quando lo stato e' stato letto quella faccia non
                // c'era. Nessuna firma, nessun confronto, e il piano passa.
                //
                // Misurato il 14/9/2026: `[estrudi_rettangolo, taglio_cilindrico
                // su F1]` su un documento vuoto riusciva, volume 117.738,1. Il
                // foro e' finito sulla faccia piu' grande — che in quel caso era
                // anche quella giusta, ma **non l'aveva scelta nessuno**: «F1»
                // e' posizionale e si rinumera per area a ogni lettura.
                var uidStorto = UidCheNonEsistono(
                    _catalogo.Schema(nome), suoi, prima);
                if (uidStorto != null)
                {
                    esiti.Add(new Dictionary<string, object>
                    {
                        ["operazione"] = nome,
                        ["riuscita"] = false,
                        ["errore"] = uidStorto + NienteToccato,
                    });
                    tuttoBene = false;
                    break;
                }

                // LE FIRME DI QUANDO IL PIANO E' NATO, ALLEGATE AL PASSO.
                // Non si sovrascrive quello che il chiamante ha gia' messo:
                // un piano che porta le sue firme sa meglio di noi di cosa
                // sta parlando.
                if (firme != null && !suoi.ContainsKey("_firme"))
                    suoi["_firme"] = firme;

                daFare.Add(new Riscontro.Disegno.PassoDelPiano
                {
                    Ordine = ordine,
                    Operazione = nome,
                    Argomenti = suoi,
                    Riassunto = nome,
                });
            }

            // IL GIRO E' QUELLO DELLA LIBRERIA, NON UNO NOSTRO.
            // Da qui arrivano i riferimenti fra passi («la faccia in cima a
            // quello che hai fatto al passo 1») e la voce di annullamento
            // unica: un solo Ctrl+Z disfa il piano intero.
            if (tuttoBene)
            {
                var fatti = _sessione.EseguiPiano(daFare, out var erroreGiro);
                if (fatti != null) foreach (var e in fatti) esiti.Add(e);
                if (!string.IsNullOrEmpty(erroreGiro))
                {
                    tuttoBene = false;
                    // Un piano fermato PRIMA del primo passo non lascia nessun
                    // esito: senza questo, la risposta sarebbe un elenco vuoto
                    // e un «riuscita» implicito che nessuno ha detto.
                    if (fatti == null || fatti.Count == 0)
                        esiti.Add(new Dictionary<string, object>
                        {
                            ["operazione"] = daFare.Count > 0 ? daFare[0].Operazione : null,
                            ["riuscita"] = false,
                            ["errore"] = erroreGiro,
                        });
                }
            }

            var dopo = _sessione.Stato(out var erroreStato);

            // «stato_dopo» E' LA LETTURA SUCCESSIVA, E VA FIRMATA COME TALE.
            //
            // Questa risposta porta con se' lo stato del documento dopo i
            // passi, con gli identificativi nuovi: chi legge scrivera' il
            // piano seguente su QUESTI, non su quelli di «guarda_il_pezzo».
            // Se le firme restassero quelle di prima, il piano seguente —
            // scritto giusto — verrebbe rifiutato perche' confrontato con una
            // lettura che il suo autore non ha mai visto. Sarebbe un rifiuto
            // di cio' che e' corretto, che e' il difetto peggiore dei due.
            if (dopo != null) { _firmeViste = FirmeDelloStato(dopo); TogliLeAncore(dopo); }

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

            // IL CONTEGGIO DEGLI ERRORI SI DA' COME DELTA, NON COME NUMERO.
            //
            // «lavorazioni_in_errore: 1» dopo un piano sembra dire «il piano ha
            // rotto qualcosa». Puo' voler dire l'opposto: il pezzo era gia'
            // rotto quando e' arrivato, e il piano non c'entra. Il numero
            // assoluto mente su chi sia stato, ed e' il rischio scritto nella
            // voce D5.
            //
            // Si dice quanti ce n'erano prima solo quando cambia qualcosa: su
            // un pezzo sano che resta sano non c'e' niente da dire, e ripeterlo
            // a ogni risposta e' posto tolto a cio' che conta.
            int erroriPrima = LavorazioniInErrore(prima);
            int erroriDopo = LavorazioniInErrore(dopo);
            if (erroriPrima > 0 || erroriDopo > 0)
            {
                risultato["lavorazioni_in_errore_prima"] = erroriPrima;
                risultato["nota_errori"] =
                    erroriDopo > erroriPrima
                        ? "Il pezzo ha " + erroriDopo + " lavorazioni in errore, e prima " +
                          "ne aveva " + erroriPrima + ": questo piano ne ha rotte " +
                          (erroriDopo - erroriPrima) + ". Guarda «stato_dopo» per sapere quali."
                        : erroriDopo < erroriPrima
                            ? "Le lavorazioni in errore sono passate da " + erroriPrima +
                              " a " + erroriDopo + ": questo piano ne ha riparate " +
                              (erroriPrima - erroriDopo) + "."
                            : "Il pezzo aveva gia' " + erroriPrima + " lavorazioni in errore " +
                              "prima di questo piano, e ne ha ancora " + erroriDopo + ": " +
                              "non le ha rotte questo piano.";
            }

            // ── IL RICORDO PER «annulla» (voce R1) ───────────────────────
            //
            // Si tiene com'era il pezzo quando questo piano ha finito: e'
            // l'unica cosa che permettera' di ritirarlo senza rischiare di
            // togliere il lavoro di qualcun altro.
            //
            // SI SCRIVE ANCHE QUANDO IL PIANO E' FALLITO A META', ed e' il caso
            // che conta di piu': un piano fermato al passo 3 lascia i primi due
            // costruiti, e sono proprio quelli che chi legge vuole ritirare.
            // Il messaggio di errore glielo dice gia' («quello che era gia'
            // stato costruito resta»).
            //
            // SI CANCELLA quando il pezzo non e' cambiato, e cancella anche il
            // ricordo di un piano PRECEDENTE che sarebbe ancora ritirabile.
            //
            // E' una perdita, ed e' voluta. `EsecutorePiano` apre la voce di
            // annullamento PRIMA del primo passo e la chiude comunque: un piano
            // fallito subito ne lascia una vuota, e se SOLIDWORKS la mette in
            // pila lo stesso, un passo indietro toglie quella — non il piano di
            // prima. Non c'e' nessuna API per guardare la pila e togliersi il
            // dubbio.
            //
            // Di solito, in questo progetto, **rifiutare cio' che e' corretto e'
            // il peggiore dei due modi di sbagliare**. Qui no, e vale la pena
            // dire perche': un rifiuto di troppo costa a chi legge una
            // rilettura e un piano nuovo; un annullamento di troppo cancella il
            // lavoro di una persona, e nessun messaggio lo rimette. Quando i due
            // errori non costano uguale, il criterio si rovescia.
            double volPrima = NumeroDi(prima, "volume_mm3");
            double volDopo = NumeroDi(dopo, "volume_mm3");
            int featPrima = QuanteFeature(prima);
            int featDopo = QuanteFeature(dopo);

            bool haCostruito =
                dopo != null &&
                (featDopo != featPrima || Math.Abs(volDopo - volPrima) > ScartoVolumeMm3);

            _ultimoPiano = haCostruito
                ? new UltimoPiano
                {
                    Documento = adesso,
                    VolumePrima = volPrima,
                    VolumeDopo = volDopo,
                    FeaturePrima = featPrima,
                    FeatureDopo = featDopo,
                }
                : null;

            return Protocollo.Contenuto(Protocollo.InJson(risultato), !tuttoBene);
        }

        // ─────────────────────────────────────────────────────────────────
        // R1 — ritirare quello che si e' fatto
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Com'era il pezzo quando l'ultimo piano ha finito.
        ///
        /// E' l'unica cosa che permette di annullare senza rischiare di
        /// togliere il lavoro di qualcun altro: se il pezzo non e' piu' cosi',
        /// fra il piano e adesso e' successo qualcosa, e quel qualcosa non
        /// l'ha fatto questo ponte.
        /// </summary>
        private sealed class UltimoPiano
        {
            public Dictionary<string, object> Documento;
            public double VolumePrima;
            public double VolumeDopo;
            public int FeaturePrima;
            public int FeatureDopo;
        }

        private UltimoPiano _ultimoPiano;

        /// <summary>
        /// Quanto si puo' sbagliare un volume e considerarlo lo stesso.
        ///
        /// Un millesimo di mm³: SOLIDWORKS ricalcola il volume a ogni rebuild e
        /// l'ultima cifra balla. Piu' stretto di cosi' rifiuterebbe pezzi
        /// identici; piu' largo lascerebbe passare una modifica vera — un foro
        /// Ø1 profondo 1 sono 0,79 mm³, quasi mille volte questa soglia.
        /// </summary>
        private const double ScartoVolumeMm3 = 0.001;

        /// <summary>
        /// RITIRA L'ULTIMO PIANO.
        ///
        /// PERCHE' ESISTE. Fino alla 0.31.0 questo ponte diceva **ventuno
        /// volte** al modello «annullalo con Ctrl+Z». Il destinatario di quei
        /// messaggi e' un modello linguistico, che non ha una tastiera: era una
        /// riparazione offerta a qualcuno che non poteva prenderla. E le
        /// trentasei operazioni sono tutte additive o parametriche, quindi
        /// l'albero poteva solo crescere: un modello che si accorgeva di aver
        /// sbagliato aveva due strade, tutte e due sbagliate — costruirci sopra,
        /// o rifare il pezzo da capo.
        ///
        /// LA MACCHINA C'ERA GIA'. `EsecutorePiano` apre e chiude una voce di
        /// annullamento intorno a ogni piano (voce B8, misurata: «un solo
        /// Ctrl+Z riporta il documento al valore di volume_prima_mm3»). Si
        /// azionava solo con una tastiera umana. E' la stessa forma della
        /// sezione V — la capacita' c'e', e non arriva a chi legge.
        ///
        /// ── IL RIFIUTO VIENE PRIMA ────────────────────────────────────────
        ///
        /// Fra il piano e questa chiamata il progettista puo' aver lavorato a
        /// mano. Premere annulla allora gli toglie il SUO lavoro, ed e' un
        /// danno che nessun messaggio ripara. Quindi si confrontano DUE
        /// numeri che la risposta di `disegna` gia' conteneva — il volume e
        /// quante lavorazioni ci sono — e se non tornano **ci si ferma e si
        /// dice cosa e' cambiato**.
        ///
        /// Sono due numeri, non una prova: un gesto che non cambia ne' il
        /// volume ne' il conteggio passa inosservato. Per quello si controlla
        /// anche DOPO, e se l'annullamento ha disfatto una cosa diversa da
        /// quella attesa, si RIMETTE (`UnPassoAvanti`) e si dice cos'e'
        /// successo. Non c'e' nessuna API per guardare la pila degli
        /// annullamenti prima di premere: provare, controllare, e disfare la
        /// prova sbagliata e' l'unica strada onesta.
        /// </summary>
        private Dictionary<string, object> Annulla()
        {
            if (!_sessione.Agganciata)
                return Protocollo.Contenuto("Il ponte non e' agganciato a SolidWorks.", true);

            var memoria = _ultimoPiano;
            if (memoria == null)
                return Protocollo.Contenuto(
                    "Non ho nessun piano da ritirare: «annulla» disfa l'ultimo " +
                    "«disegna» di questa conversazione, e qui non ne ho ancora " +
                    "eseguito nessuno che abbia costruito qualcosa.\n\n" +
                    "Non premo annulla alla cieca: toglierei qualcosa che non ho " +
                    "fatto io, e non saprei nemmeno dire cosa.", true);

            var prima = _sessione.StatoSenzaAncora(out var erroreLettura);
            if (prima == null) return Protocollo.Contenuto(erroreLettura, true);

            var adesso = IdentitaDelloStato(prima);
            if (!Ponte.Sessione.StessoDocumento(memoria.Documento, adesso))
                return Protocollo.Contenuto(
                    "Mi fermo: il documento e' cambiato. Il piano l'avevo eseguito " +
                    "su «" + NomeLeggibile(memoria.Documento) + "», adesso davanti " +
                    "c'e' «" + NomeLeggibile(adesso) + "». Annullare qui " +
                    "toglierebbe l'ultima cosa fatta su un pezzo che non c'entra.\n\n" +
                    "Porta davanti il pezzo giusto e richiamami.", true);

            double volumeOra = NumeroDi(prima, "volume_mm3");
            int featureOra = QuanteFeature(prima);

            if (Math.Abs(volumeOra - memoria.VolumeDopo) > ScartoVolumeMm3 ||
                featureOra != memoria.FeatureDopo)
                return Protocollo.Contenuto(
                    "Mi fermo: dopo il piano il pezzo e' cambiato, e non l'ho " +
                    "cambiato io.\n\n" +
                    Confronto("volume", memoria.VolumeDopo, volumeOra, "mm³") +
                    Confronto("lavorazioni", memoria.FeatureDopo, featureOra, "") +
                    "\nUn passo indietro adesso non disferebbe il mio piano: " +
                    "disferebbe l'ultima cosa fatta, che e' di qualcun altro. " +
                    "Se vuoi comunque tornare indietro, e' un gesto che deve fare " +
                    "chi sta davanti allo schermo, perche' solo lui sa cosa sta " +
                    "buttando via.", true);

            // ── SI PREME ─────────────────────────────────────────────────
            var erroreAnnulla = _sessione.UnPassoIndietro();
            if (erroreAnnulla != null)
                return Protocollo.Contenuto(erroreAnnulla, true);

            var dopo = _sessione.Stato(out var erroreStato);
            if (dopo == null)
                return Protocollo.Contenuto(
                    "Ho premuto annulla e poi non sono riuscito a rileggere il " +
                    "documento: " + erroreStato + "\n\n" +
                    "Non posso dirti se ha funzionato. Guarda il pezzo prima di " +
                    "fare altro.", true);

            double volumeDopo = NumeroDi(dopo, "volume_mm3");
            int featureDopo = QuanteFeature(dopo);
            bool tornato =
                Math.Abs(volumeDopo - memoria.VolumePrima) <= ScartoVolumeMm3 &&
                featureDopo == memoria.FeaturePrima;

            if (!tornato)
            {
                // HA DISFATTO UN'ALTRA COSA: SI RIMETTE.
                // Il volume e il conteggio non erano cambiati, quindi il gesto
                // che stava in cima alla pila non era geometrico — un nome
                // cambiato, una configurazione, una vista. L'ho tolto io senza
                // volerlo, e lo rimetto io.
                var erroreRedo = _sessione.UnPassoAvanti();
                var riletto = _sessione.Stato(out _);

                _guardato = IdentitaDelloStato(riletto);
                _firmeViste = FirmeDelloStato(riletto);
                if (riletto != null) TogliLeAncore(riletto);

                return Protocollo.Contenuto(Protocollo.InJson(
                    new Dictionary<string, object>
                    {
                        ["annullato"] = false,
                        ["documento"] = NomeLeggibile(adesso),
                        ["motivo"] =
                            "Ho premuto annulla e il pezzo non e' tornato dov'era: " +
                            "il volume doveva essere " + Numero(memoria.VolumePrima) +
                            " mm³ e le lavorazioni " + memoria.FeaturePrima +
                            ", invece sono " + Numero(volumeDopo) + " mm³ e " +
                            featureDopo + ". Vuol dire che in cima alla pila degli " +
                            "annullamenti non c'era il mio piano, ma un gesto fatto " +
                            "dopo che non cambiava ne' il volume ne' il conteggio — " +
                            "quindi non potevo vederlo prima di premere." +
                            (erroreRedo == null
                                ? " L'ho rimesso a posto subito (un passo avanti)."
                                : " E NON sono riuscito a rimetterlo: " + erroreRedo +
                                  " Guarda il documento prima di fare altro."),
                        ["stato_dopo"] = riletto,
                    }), true);
            }

            // RIUSCITO: IL RICORDO SI CANCELLA.
            // Un secondo «annulla» non deve disfare quello che c'era PRIMA del
            // piano: quello non l'ha fatto questo ponte, e la pila degli
            // annullamenti non sa distinguere.
            _ultimoPiano = null;

            // GLI IDENTIFICATIVI NON SONO PIU' QUELLI, e chi legge deve
            // saperlo dalla risposta invece di scoprirlo al piano seguente:
            // gli uid sono posizionali e si rinumerano per area a ogni
            // lettura. Si firma questa lettura, esattamente come fa `disegna`.
            _guardato = IdentitaDelloStato(dopo);
            _firmeViste = FirmeDelloStato(dopo);
            TogliLeAncore(dopo);

            return Protocollo.Contenuto(Protocollo.InJson(
                new Dictionary<string, object>
                {
                    ["annullato"] = true,
                    ["documento"] = NomeLeggibile(adesso),
                    ["volume_prima_mm3"] = Math.Round(volumeOra, 2),
                    ["volume_dopo_mm3"] = Math.Round(volumeDopo, 2),
                    ["lavorazioni_prima"] = featureOra,
                    ["lavorazioni_dopo"] = featureDopo,
                    ["nota"] =
                        "Il piano e' stato ritirato tutto intero e il pezzo e' " +
                        "tornato com'era. Gli identificativi di «stato_dopo» sono " +
                        "quelli validi adesso: quelli di prima non valgono piu'.",
                    ["stato_dopo"] = dopo,
                }));
        }

        /// <summary>Una riga di confronto, solo se i due numeri sono diversi.</summary>
        private static string Confronto(string cosa, double atteso, double trovato, string unita)
        {
            if (Math.Abs(atteso - trovato) <= ScartoVolumeMm3) return "";
            return "  " + cosa + ": era " + Numero(atteso) +
                   (unita.Length > 0 ? " " + unita : "") + " quando ho finito, " +
                   "adesso e' " + Numero(trovato) +
                   (unita.Length > 0 ? " " + unita : "") + "\n";
        }

        private static string Numero(double v)
        {
            return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static double NumeroDi(Dictionary<string, object> stato, string chiave)
        {
            if (stato == null) return 0;
            object v;
            if (!stato.TryGetValue(chiave, out v) || v == null) return 0;
            try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        /// <summary>
        /// Quante lavorazioni ha il pezzo, comprese quelle che il tetto ha
        /// lasciato fuori.
        ///
        /// SENZA `feature_non_elencate` QUESTO CONTEGGIO MENTIREBBE su ogni
        /// pezzo con piu' di ottanta lavorazioni: si fermerebbe a ottanta e
        /// direbbe «non e' cambiato niente» anche dopo dieci fori nuovi. E'
        /// esattamente il difetto della voce E12, ripetuto da questa parte.
        /// </summary>
        private static int QuanteFeature(Dictionary<string, object> stato)
        {
            if (stato == null) return 0;
            int quante = 0;
            foreach (var v in VociDi(stato, "feature")) { if (v != null) quante++; }
            object fuori;
            if (stato.TryGetValue("feature_non_elencate", out fuori) && fuori != null)
                try { quante += Convert.ToInt32(fuori); } catch { }
            return quante;
        }

        // ─────────────────────────────────────────────────────────────────

        private static object Volume(Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            return stato.TryGetValue("volume_mm3", out var v) ? v : null;
        }

        /// <summary>
        /// GLI IDENTIFICATIVI CHE IL PASSO CITA E CHE NELLO STATO NON C'ERANO.
        /// Null se e' tutto a posto.
        ///
        /// Si guardano solo i campi che lo schema chiama `*_uid`: sono quelli
        /// che nominano geometria **che c'era gia'**. La geometria creata dal
        /// piano stesso si nomina con `*_rif`, che cita il PASSO e non
        /// l'identificativo — ed e' l'unico modo, perche' al momento in cui il
        /// piano si scrive quella faccia non esiste e nessuno le puo' dare un
        /// nome.
        ///
        /// PERCHE' NON BASTAVANO LE FIRME. La firma confronta un identificativo
        /// con la geometria che descriveva quando il piano e' nato. Se la
        /// faccia **non esisteva affatto**, non c'e' nessuna firma: niente da
        /// confrontare, nessun rifiuto, e il piano passa. La firma protegge
        /// dallo spostamento; questo protegge dall'invenzione.
        ///
        /// NON SI RIFIUTA IL LEGITTIMO. Un piano che crea geometria e poi ci
        /// lavora sopra e' normale, e passa: usa `*_rif`. Il messaggio lo dice,
        /// perche' un rifiuto che non indica la strada buona insegna soltanto a
        /// riprovare a caso.
        /// </summary>
        private static string UidCheNonEsistono(
            Dictionary<string, object> schema,
            Dictionary<string, object> argomenti,
            Dictionary<string, object> stato)
        {
            if (schema == null || argomenti == null || stato == null) return null;
            object grezzoProp;
            if (!schema.TryGetValue("properties", out grezzoProp)) return null;
            var proprieta = grezzoProp as Dictionary<string, object>;
            if (proprieta == null) return null;

            var esistenti = UidDelloStato(stato);

            foreach (var campo in proprieta.Keys)
            {
                if (!campo.EndsWith("_uid", StringComparison.Ordinal)) continue;
                object valore;
                if (!argomenti.TryGetValue(campo, out valore) || valore == null) continue;

                foreach (var uid in Stringhe(valore))
                {
                    if (esistenti.Contains(uid)) continue;

                    // SI DICE COSA C'E', non solo cosa manca. Su un documento
                    // vuoto l'elenco e' vuoto, ed e' proprio quello il caso in
                    // cui chi legge deve capire che deve cambiare strada.
                    string cosaCe = esistenti.Count == 0
                        ? "Nel pezzo che ho guardato non c'era nessun identificativo: " +
                          "era vuoto."
                        : "Quelli che c'erano sono: " + Elenca(esistenti) + ".";

                    return
                        "«" + campo + "» cita «" + uid + "», che nel pezzo che ho " +
                        "guardato non c'era. " + cosaCe + "\n\n" +
                        "Gli identificativi nascono da «guarda_il_pezzo» e valgono per " +
                        "QUELLA lettura. Se la geometria la crea un passo precedente di " +
                        "questo stesso piano, non ha ancora nessun identificativo e va " +
                        "nominata con il riferimento al passo — per esempio " +
                        "\"" + campo.Replace("_uid", "_rif") + "\": {\"passo\": 1, " +
                        "\"genere\": \"faccia_superiore\"}. Se invece c'era gia', " +
                        "rileggi il pezzo e copia l'identificativo da li'.";
                }
            }
            return null;
        }

        /// <summary>
        /// Tutti i nomi che quello stato ha davvero MOSTRATO: gli uid di facce,
        /// spigoli, corpi, assi e lavorazioni, piu' i nomi di lavorazione che lo
        /// stato scrive in giro per altre vie.
        ///
        /// LA REGOLA: SI RACCOGLIE CIO' CHE LO STATO HA SCRITTO, NON CIO' CHE
        /// AVEVA IN MENTE DI SCRIVERE.
        ///
        /// Questo controllo blocca un piano PRIMA di eseguirlo quando cita un
        /// nome che non c'era. Se raccoglie meno nomi di quanti lo stato ne ha
        /// mostrati, rifiuta lavoro legittimo — e lo rifiuta nel modo peggiore,
        /// perche' chi ha scritto il piano quel nome l'ha letto nella risposta
        /// precedente e ha ragione a fidarsene.
        ///
        /// Fino alla 0.25.2 guardava solo il campo `uid` delle cinque famiglie.
        /// Ma lo stato pubblica nomi di lavorazione anche in tre altri posti:
        ///
        ///   * `da_lavorazione` su ogni faccia — ed e' messo li' apposta per
        ///     «quel foro portalo a M10»;
        ///   * `fatto_da` su ogni corpo;
        ///   * la mappa `lavorazioni` in fondo.
        ///
        /// E l'elenco `feature` invece e' FILTRATO per tipo e tagliato a
        /// `MaxFeature`. Quindi bastava un pezzo con piu' di ottanta
        /// lavorazioni, o una lavorazione di un tipo che non entra nell'elenco,
        /// perche' lo stato mostrasse un nome e il controllo lo dichiarasse
        /// inesistente.
        ///
        /// SI ALLARGA E NON SI STRINGE. L'insieme e' unico e non distingue le
        /// famiglie: `faccia_uid` accetta anche un nome di lavorazione. E'
        /// voluto — questo e' un controllo di ESISTENZA, non di tipo, e chi
        /// sbaglia famiglia viene fermato dopo con un messaggio che sa dire di
        /// che tipo e' quell'entita' (`NonLavoroSu`). Stringere qui vorrebbe
        /// dire rischiare un rifiuto sbagliato per guadagnare un messaggio
        /// leggermente migliore.
        /// </summary>
        private static HashSet<string> UidDelloStato(Dictionary<string, object> stato)
        {
            var fuori = new HashSet<string>(StringComparer.Ordinal);

            // 1. Gli uid veri e propri.
            foreach (var famiglia in new[] { "facce", "spigoli", "corpi", "assi", "feature" })
                foreach (var voce in VociDi(stato, famiglia))
                {
                    object uid;
                    if (voce.TryGetValue("uid", out uid) && uid != null)
                        fuori.Add(Convert.ToString(uid));
                }

            // 2. I nomi di lavorazione scritti sulle facce e sui corpi.
            foreach (var voce in VociDi(stato, "facce"))
            {
                object nome;
                if (voce.TryGetValue("da_lavorazione", out nome) && nome != null)
                    fuori.Add(Convert.ToString(nome));
            }
            foreach (var voce in VociDi(stato, "corpi"))
            {
                object nome;
                if (voce.TryGetValue("fatto_da", out nome) && nome != null)
                    fuori.Add(Convert.ToString(nome));
            }

            // 3. La mappa delle lavorazioni: le CHIAVI sono i numeri, i VALORI
            //    sono i nomi. Chi scrive un piano copia il nome, non il numero.
            object grezzo;
            if (stato.TryGetValue("lavorazioni", out grezzo))
            {
                var mappa = grezzo as Dictionary<string, object>;
                if (mappa != null)
                    foreach (var v in mappa.Values)
                        if (v != null) fuori.Add(Convert.ToString(v));
            }

            fuori.Remove("");
            return fuori;
        }

        /// <summary>
        /// Le voci di una famiglia dello stato, come dizionari.
        ///
        /// Le famiglie sono `List&lt;Dictionary&lt;...&gt;&gt;`, non `object[]`: un
        /// `as object[]` qui tornerebbe null in silenzio e il controllo direbbe
        /// «non esiste» su tutto. E' gia' successo una volta, con le firme.
        /// </summary>
        private static IEnumerable<Dictionary<string, object>> VociDi(
            Dictionary<string, object> stato, string famiglia)
        {
            object grezzo;
            if (!stato.TryGetValue(famiglia, out grezzo) || grezzo == null) yield break;
            var elenco = grezzo as System.Collections.IEnumerable;
            if (elenco == null) yield break;
            foreach (var v in elenco)
            {
                var voce = v as Dictionary<string, object>;
                if (voce != null) yield return voce;
            }
        }

        /// <summary>Un valore che puo' essere una stringa o un elenco di stringhe.</summary>
        private static IEnumerable<string> Stringhe(object valore)
        {
            if (valore is string s) { if (s.Length > 0) yield return s; yield break; }
            if (valore is System.Collections.IEnumerable elenco)
            {
                foreach (var v in elenco)
                {
                    var t = Convert.ToString(v);
                    if (!string.IsNullOrEmpty(t)) yield return t;
                }
            }
        }

        /// <summary>
        /// L'elenco, ma corto. Su un pezzo con quattrocento facce scriverli
        /// tutti non aiuta nessuno: i primi dodici bastano a far capire che
        /// forma hanno i nomi.
        /// </summary>
        private static string Elenca(HashSet<string> uid)
        {
            var ordinati = uid.ToList();
            ordinati.Sort(StringComparer.Ordinal);
            if (ordinati.Count <= 12) return string.Join(", ", ordinati);
            return string.Join(", ", ordinati.Take(12)) +
                   " e altri " + (ordinati.Count - 12);
        }

        /// <summary>
        /// Il nome di una lavorazione, dalla mappa che lo stato scrive in
        /// fondo. Null se non c'e': meglio una firma senza nome che una firma
        /// con un nome inventato.
        /// </summary>
        private static string NomeLavorazione(Dictionary<string, object> stato, object id)
        {
            if (stato == null || id == null) return null;
            if (!stato.TryGetValue("lavorazioni", out var g) || g == null) return null;
            var mappa = g as Dictionary<string, object>;
            if (mappa == null) return null;
            var chiave = Convert.ToString(id);
            return mappa.TryGetValue(chiave, out var nome) && nome != null
                ? Convert.ToString(nome)
                : null;
        }

        /// <summary>
        /// La scatola del pezzo — {minX,minY,minZ,maxX,maxY,maxZ} in mm —
        /// ricavata dai due campi che lo stato pubblica: l'angolo
        /// (`ingombro_da_mm`) e i lati (`ingombro_mm`).
        ///
        /// Null se uno dei due manca. NON si inventa l'angolo mettendolo a
        /// zero: uno stato che dichiarasse una scatola appoggiata all'origine
        /// quando il pezzo e' centrato su di essa farebbe scrivere «stava a
        /// meta' del lato» di una faccia che stava all'estremo, e una
        /// spiegazione sbagliata e' peggio di nessuna spiegazione.
        /// </summary>
        private static double[] ScatolaDelPezzo(Dictionary<string, object> stato)
        {
            double[] da = TreNumeri(stato, "ingombro_da_mm");
            double[] lati = TreNumeri(stato, "ingombro_mm");
            if (da == null || lati == null) return null;
            return new[]
            {
                da[0], da[1], da[2],
                da[0] + lati[0], da[1] + lati[1], da[2] + lati[2],
            };
        }

        /// <summary>Tre numeri da un campo dello stato, o null.</summary>
        private static double[] TreNumeri(Dictionary<string, object> stato, string chiave)
        {
            if (stato == null || !stato.TryGetValue(chiave, out var v) || v == null) return null;
            var elenco = v as System.Collections.IEnumerable;
            if (elenco == null) return null;
            var fuori = new List<double>();
            foreach (var x in elenco)
            {
                try
                {
                    fuori.Add(Convert.ToDouble(
                        x, System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { return null; }
            }
            return fuori.Count == 3 ? fuori.ToArray() : null;
        }

        /// <summary>
        /// Quante lavorazioni in errore aveva quello stato. Zero anche quando
        /// il campo non c'e' — ed e' giusto: lo stato lo scrive solo quando ce
        /// ne sono.
        /// </summary>
        private static int LavorazioniInErrore(Dictionary<string, object> stato)
        {
            if (stato == null) return 0;
            object v;
            if (!stato.TryGetValue("lavorazioni_in_errore", out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
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

        /// <summary>
        /// Uno strumento, con le sue ANNOTAZIONI.
        ///
        /// PERCHE' LE ANNOTAZIONI NON SONO UN DETTAGLIO DI PROTOCOLLO.
        /// Il client MCP le usa per decidere se fermarsi a chiedere conferma a
        /// chi sta lavorando. Senza, `guarda_il_pezzo` — che non tocca niente —
        /// e `disegna` — che modifica il pezzo in modo che nessuno annulla per
        /// lui — arrivano al client identici, e il client tratta identiche due
        /// cose che non lo sono.
        ///
        /// Per un progetto la cui tesi e' «nessuna geometria esiste senza che
        /// qualcuno l'abbia confermata», dichiararle e' il minimo.
        ///
        /// `idempotente` merita una riga: `disegna` NON lo e'. Rieseguire lo
        /// stesso piano non rimette il pezzo com'era — fa un SECONDO foro.
        /// Dichiararlo idempotente inviterebbe un client a riprovare da solo
        /// dopo un errore di rete, e il pezzo uscirebbe con il doppio delle
        /// lavorazioni senza che nessuno l'abbia chiesto.
        /// </summary>
        private static Dictionary<string, object> Strumento(
            string nome,
            string descrizione,
            Dictionary<string, object> schema,
            bool soloLettura,
            bool distruttivo,
            bool idempotente)
        {
            return new Dictionary<string, object>
            {
                ["name"] = nome,
                ["description"] = descrizione,
                ["inputSchema"] = schema,
                ["annotations"] = new Dictionary<string, object>
                {
                    ["title"] = TitoloUmano(nome),
                    ["readOnlyHint"] = soloLettura,
                    ["destructiveHint"] = distruttivo,
                    ["idempotentHint"] = idempotente,
                    // Il mondo esterno qui e' SolidWorks: un programma che non
                    // controlliamo, con documenti che cambiano sotto i piedi.
                    ["openWorldHint"] = true,
                },
            };
        }

        private static string TitoloUmano(string nome)
        {
            switch (nome)
            {
                case "guarda_il_pezzo": return "Guarda il pezzo aperto";
                case "elenco_operazioni": return "Elenco delle operazioni";
                case "descrivi_operazione": return "Descrivi una operazione";
                case "disegna": return "Disegna nel pezzo";
                case "annulla": return "Ritira l'ultimo piano";
                default: return nome;
            }
        }
    }
}
