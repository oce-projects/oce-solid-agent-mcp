using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// OGNI CAMPO OBBLIGATORIO E' UNA DECISIONE PRESA, NON UNA DIMENTICANZA
    /// (voce Z-G3).
    ///
    /// ── IL FATTO DA CUI NASCE ────────────────────────────────────────────
    ///
    /// Al modello non mancava il permesso di saltare il materiale: gli mancava
    /// **un valore**. Cinque riscritture della regola non hanno cambiato
    /// niente, perche' il problema non era il permesso.
    ///
    /// Un campo obbligatorio o e' **una quota del pezzo** — e allora si chiede
    /// davvero, perche' nessuno la puo' indovinare — oppure ha **un valore
    /// d'officina**, e allora andrebbe dichiarato invece di essere preteso.
    ///
    /// ── LA MISURA, il 23/9/2026 ──────────────────────────────────────────
    ///
    ///     92  campi obbligatori in tutto il catalogo  (la voce diceva 91)
    ///      0  ne dichiarano un predefinito
    ///     58  sono quote o riferimenti: si chiedono, e va bene cosi'
    ///     34  non sono ne' l'uno ne' l'altro: SERVE UNA DECISIONE
    ///
    /// ── E LA DECISIONE STA SCRITTA QUI SOTTO, UNA PER UNA ────────────────
    ///
    /// Non e' burocrazia: e' la differenza fra «nessuno ci ha pensato» e
    /// «qualcuno ci ha pensato e ha deciso di chiederlo». Le due cose si
    /// leggono uguali nel JSON, e solo una delle due si puo' migliorare.
    ///
    /// Un campo obbligatorio nuovo che non sia una quota fa diventare rosso
    /// questo controllo: la decisione va presa **mentre si scrive**, non sei
    /// mesi dopo.
    /// </summary>
    internal static class ProveICampiObbligatori
    {
        /// <summary>
        /// Un campo il cui nome dice gia' che e' una quota o un riferimento a
        /// qualcosa di geometrico. Per questi la decisione e' automatica: si
        /// chiedono.
        /// </summary>
        private static readonly Regex Quota = new Regex(
            @"_mm$|_gradi$|^denti$|^modulo|^segmenti$|^punto|_uid$|^facce$",
            RegexOptions.Compiled);

        /// <summary>
        /// I campi obbligatori che NON sono quote, e perche' restano
        /// obbligatori. Il testo e' la decisione, e va letto: dove c'e'
        /// «CANDIDATO» la decisione e' stata presa e **non e' ancora
        /// eseguita**.
        /// </summary>
        private static readonly Dictionary<string, string> Decise =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // ── SI CHIEDONO: sono il lavoro, non un contorno ──────────
                { "estrudi_profilo.contorni", "e' la sagoma: senza, non c'e' niente da estrudere" },
                { "rivoluzione.contorni", "idem" },
                { "estrudi_su_faccia.contorni", "idem" },
                { "rivoluzione.asse", "attorno a cosa gira: un predefinito sceglierebbe il pezzo" },
                { "estrudi_sezione.tipo", "quale profilato: IPE e tubo quadro non si somigliano" },
                { "foro_guidato.taglia", "M8 o M10 lo decide il bullone, non l'officina" },
                { "foro_guidato.posizioni", "dove vanno i fori: e' la lavorazione stessa" },
                { "modifica_quota.quota", "quale quota cambiare: indovinarla e' cambiare il pezzo sbagliato" },
                { "ripeti_circolare.numero_istanze", "quante: e' il senso dell'operazione" },
                { "ripeti_lineare.numero_istanze1", "idem" },
                { "specchia.piano", "rispetto a cosa: un predefinito specchierebbe a caso" },
                { "nota_in_tavola.testo", "cosa c'e' scritto: non esiste un testo d'ufficio" },
                { "incidi_testo.testo", "idem" },
                { "attiva_foglio.nome", "quale foglio: sono tutti legittimi" },
                { "filettatura_cosmetica.denominazione", "M10x1.5 lo decide l'accoppiamento" },
                { "misura_fra_entita.uid", "fra cosa e cosa si misura" },
                { "sopprimi_istanza.copie", "quali copie togliere" },
                { "scrivi_proprieta.nome", "quale proprieta'" },
                { "scrivi_proprieta.valore", "e cosa scriverci" },
                { "crea_configurazione.nome", "come si chiama: un nome d'ufficio sarebbe un doppione" },
                { "attiva_configurazione.nome", "quale attivare" },
                { "accoppia.componente_a", "quali due pezzi si accoppiano" },
                { "accoppia.faccia_a", "e su quali facce" },
                { "accoppia.componente_b", "idem" },
                { "accoppia.faccia_b", "idem" },
                { "sostituisci_componente.componente", "quale componente si sostituisce" },
                { "sostituisci_componente.percorso", "e con quale file" },
                { "inserisci_componente.percorso", "quale file si inserisce" },
                { "flangia_base.punti", "il profilo della lamiera: e' la lamiera stessa, e da che parte va lo spessore dipende dal verso" },
                { "rugosita_in_tavola.ra_um", "la rugosita' la decide la funzione della superficie: un Ra d'ufficio si stamperebbe su ogni faccia" },
                { "tolleranza_quota.quota", "quale quota tollerare lo sa solo chi progetta: una tolleranza d'ufficio su una quota scelta da me e' un accoppiamento inventato" },
                { "tolleranza_quota.tipo", "± o H7 e' la domanda stessa: non c'e' un tipo di tolleranza d'ufficio" },
                { "tolleranza_geometrica_in_tavola.caratteristica", "planarita' o perpendicolarita' e' la domanda: non c'e' una tolleranza d'ufficio" },

                // ── DIPENDONO DALL'OFFICINA, NON DA NOI (voce Z-G8) ───────
                { "membro_strutturale.profilo_percorso",
                  "la libreria dei profili e' di QUESTA officina: un predefinito " +
                  "nostro sarebbe il percorso della nostra macchina. Vedi Z-G8" },
                { "membro_strutturale.configurazione", "idem: la misura sta in quella libreria" },
                { "telaio_rettangolare.profilo_percorso", "idem" },
                { "telaio_rettangolare.configurazione", "idem" },

                // ── I DUE CANDIDATI SONO STATI FATTI il 24/9/2026 ─────────
                //
                // `applica_materiale.materiale` -> S235JR e
                // `combina_corpi.operazione` -> «unisci» non sono piu'
                // obbligatori: hanno un predefinito d'officina, e la
                // risposta dichiara di averlo usato (`misure.nota_assunto`).
                //
                // Le loro righe sono uscite da qui, e non e' una perdita:
                // questo elenco spiega perche' un campo SI CHIEDE, e quei
                // due non si chiedono piu'. Una decisione che riguarda un
                // campo che non esiste e' rumore, e c'e' un controllo
                // apposta che lo dice — e' scattato su queste due righe
                // appena il catalogo e' stato rigenerato.
                //
                // Cosa li sorveglia adesso: il controllo in fondo a questo
                // file, che pretende il `default` nel catalogo SERVITO, piu'
                // i quattro casi di banco a coppie.
            };

        internal static void Tutte()
        {
            Gruppo("ogni campo obbligatorio e' una decisione presa (Z-G3)");

            var file = CatalogoServito();
            if (!File.Exists(file))
            {
                Segna("il catalogo servito c'e'", false, "manca " + file);
                return;
            }

            var radice = new JavaScriptSerializer()
                .DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
            object grezzo;
            var voci = radice != null && radice.TryGetValue("operazioni", out grezzo)
                ? grezzo as object[] : null;
            if (voci == null)
            {
                Segna("il catalogo ha le operazioni", false, "«operazioni» non e' un elenco");
                return;
            }

            int obbligatori = 0, quote = 0;
            var senzaDecisione = new List<string>();
            var trovati = new HashSet<string>(StringComparer.Ordinal);

            foreach (var v in voci)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, s;
                o.TryGetValue("nome", out n);
                o.TryGetValue("schema", out s);
                var schema = s as Dictionary<string, object>;
                if (schema == null) continue;
                object r;
                var richiesti = schema.TryGetValue("required", out r) ? r as object[] : null;
                if (richiesti == null) continue;

                foreach (var campo in richiesti)
                {
                    string nome = Convert.ToString(n) + "." + Convert.ToString(campo);
                    obbligatori++;
                    if (Quota.IsMatch(Convert.ToString(campo))) { quote++; continue; }
                    trovati.Add(nome);
                    if (!Decise.ContainsKey(nome)) senzaDecisione.Add(nome);
                }
            }

            Segna("i campi obbligatori si contano", obbligatori > 0,
                  obbligatori + " in tutto: " + quote + " quote o riferimenti (si chiedono), " +
                  (obbligatori - quote) + " da decidere");

            Segna("ogni campo che non e' una quota ha una decisione scritta",
                  senzaDecisione.Count == 0,
                  senzaDecisione.Count == 0
                      ? Decise.Count + " decisioni, tutte in `ProveICampiObbligatori.cs`"
                      : "SENZA DECISIONE: " + string.Join(", ", senzaDecisione));

            // ── E UNA DECISIONE PER UN CAMPO CHE NON C'E' PIU' E' RUMORE ──
            //
            // Stessa ragione dell'elenco delle eccezioni in Z-C7: un permesso
            // scaduto copre il giorno in cui qualcuno riscrive quel punto.
            var fantasmi = Decise.Keys.Where(k => !trovati.Contains(k)).ToList();
            Segna("e nessuna decisione riguarda un campo che non esiste piu'",
                  fantasmi.Count == 0,
                  fantasmi.Count == 0
                      ? "nessuna decisione scaduta"
                      : "non esistono piu': " + string.Join(", ", fantasmi));

            // ── I CANDIDATI RIMASTI, se ce ne sono ───────────────────────
            //
            // Questo contatore pretendeva `> 0` perche' contava lavoro
            // PROMESSO, e finche' ce n'era serviva a non dimenticarlo. Il
            // 24/9/2026 i due candidati sono stati fatti, e un controllo che
            // pretende l'esistenza di lavoro da fare diventerebbe rosso
            // proprio quando il lavoro finisce. Adesso si limita a dirli.
            var candidati = Decise.Where(x => x.Value.StartsWith("CANDIDATO", StringComparison.Ordinal))
                                  .Select(x => x.Key).ToList();
            Segna("i campi che avrebbero un valore d'officina sono nominati", true,
                  candidati.Count == 0
                      ? "nessun candidato rimasto: i due di Z-G3 sono fatti"
                      : candidati.Count + " candidati, non ancora fatti: " +
                        string.Join(", ", candidati));

            // ── E I DUE FATTI SI GUARDANO NEL CATALOGO SERVITO ───────────
            //
            // Non basta che siano usciti da `required`: un campo tolto e
            // basta e' un campo che il ponte accettera' vuoto senza mettere
            // niente. Il `default` nello schema e' cio' che lo ANNUNCIA al
            // modello, ed e' la meta' che si puo' controllare da qui —
            // l'altra meta' (che il valore venga applicato davvero e
            // dichiarato) la misurano i quattro casi di banco.
            PredefinitoDichiarato(voci, "applica_materiale", "materiale", "S235JR");
            PredefinitoDichiarato(voci, "combina_corpi", "operazione", "unisci");
        }

        /// <summary>
        /// Il campo ha un `default` nel catalogo servito, ed e' quel valore,
        /// e NON e' piu' fra gli obbligatori.
        ///
        /// Tutte e tre le cose insieme: un `default` accanto a un campo
        /// ancora obbligatorio non serve a nessuno, e un campo tolto da
        /// `required` senza `default` e' peggio di prima — il modello non
        /// sa che puo' ometterlo, e se lo omette non sa cosa riceve.
        /// </summary>
        private static void PredefinitoDichiarato(
            object[] voci, string operazione, string campo, string atteso)
        {
            foreach (var v in voci)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n;
                o.TryGetValue("nome", out n);
                if (Convert.ToString(n) != operazione) continue;

                object s;
                o.TryGetValue("schema", out s);
                var schema = s as Dictionary<string, object>;
                if (schema == null) break;

                object p;
                schema.TryGetValue("properties", out p);
                var proprieta = p as Dictionary<string, object>;
                object c = null;
                if (proprieta != null) proprieta.TryGetValue(campo, out c);
                var suo = c as Dictionary<string, object>;

                object d = null;
                if (suo != null) suo.TryGetValue("default", out d);

                object r;
                var richiesti = schema.TryGetValue("required", out r) ? r as object[] : null;
                bool ancoraObbligatorio = richiesti != null &&
                    richiesti.Any(x => Convert.ToString(x) == campo);

                Segna(operazione + "." + campo + " ha il suo predefinito, e non e' piu' obbligatorio",
                      Convert.ToString(d) == atteso && !ancoraObbligatorio,
                      "default=" + (d == null ? "(nessuno)" : "«" + Convert.ToString(d) + "»") +
                      ", obbligatorio=" + ancoraObbligatorio);
                return;
            }
            Segna(operazione + "." + campo + " ha il suo predefinito, e non e' piu' obbligatorio",
                  false, "l'operazione «" + operazione + "» non e' in catalogo");
        }
    }
}
