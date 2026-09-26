using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// UN LIMITE MISURATO CHE LA DESCRIZIONE NON DICE COSTA UN TURNO A VUOTO
    /// (voci Z-G5 e Z-C11).
    ///
    /// ── LE DUE META' DELLA STESSA COSA ───────────────────────────────────
    ///
    /// Quando un'operazione rifiuta, qualcuno ha gia' misurato quel limite e
    /// ci ha scritto un caso di banco. Ma **il modello non legge i casi di
    /// banco**: legge il catalogo. Se la descrizione tace, l'unico modo che ha
    /// di scoprire il limite e' sbatterci contro — e un rifiuto costa un giro,
    /// mentre un limite dichiarato costa zero.
    ///
    /// *Dichiarare un limite prima di provare vale piu' di un rifiuto.*
    ///
    /// E l'altra meta' (Z-C11): **ogni rifiuto nasce con la sua gemella
    /// verde**. Il rischio speculare — rifiutare un pezzo legittimo — non lo
    /// rivede nessuno, perche' un rifiuto sbagliato *sembra* prudenza. La
    /// gemella e' cio' che distingue una guardia da un'operazione che dice
    /// sempre di no, ed e' capitato davvero: la prima versione di
    /// `sezione_in_tavola` rifiutava tutto, e il difetto e' uscito al primo
    /// uso vero.
    ///
    /// ── PERCHE' QUI E NON NEL BANCO ──────────────────────────────────────
    ///
    /// Il banco le gemelle le controlla gia', **ma dopo un'ora di giro e con
    /// SOLIDWORKS acceso**. Un caso di rifiuto scritto senza gemella e' un
    /// errore di scrittura, non di misura: si vede leggendo il JSON, in un
    /// decimo di secondo, e chi lo scrive deve saperlo prima di lanciare il
    /// banco, non dopo.
    /// </summary>
    internal static class ProveILimiti
    {
        /// <summary>
        /// Quante operazioni hanno un rifiuto provato dal banco e **non lo
        /// dicono** nella loro descrizione. **Zero, dal 23/9/2026.**
        ///
        /// Erano dodici quella mattina — asola, attiva_configurazione,
        /// attiva_foglio, crea_configurazione, quota_in_tavola, ripeti_lineare,
        /// rivoluzione, smusso_spigoli, sopprimi_lavorazione, specchia,
        /// taglio_cilindrico, taglio_rettangolare — e ognuna aveva un limite
        /// gia' misurato, con tanto di caso di banco che lo pretende, che il
        /// modello poteva scoprire **solo sbattendoci contro**.
        ///
        /// Le frasi nuove non sono inventate: sono il messaggio che il caso
        /// pretende, girato al futuro. *Un limite dichiarato costa zero turni;
        /// lo stesso limite scoperto da un rifiuto ne costa uno.*
        ///
        /// E' un TETTO, e da zero non puo' che salire — cioe' non puo'. Una
        /// guardia nuova senza la frase che la dichiara fa diventare rosso
        /// questo controllo **il giorno in cui la si scrive**, che e' l'unico
        /// giorno in cui qualcuno la sta guardando.
        /// </summary>
        private const int Mute = 0;

        /// <summary>
        /// Le parole con cui una descrizione dichiara un limite. Sono quelle
        /// che il catalogo usa davvero, non quelle che «dovrebbe» usare: un
        /// elenco inventato a tavolino direbbe che tutte tacciono.
        /// </summary>
        private static readonly string[] Spie =
        {
            "rifiut", "non posso", "limite", "non guarda", "non sa",
            "non copre", "fallisc", "sbord", "piu' grande",
        };

        /// <summary>
        /// Quanti casi dichiarati citano un corpo PER NUMERO («C1», «C2») in un
        /// campo `_uid`. Misurato il 25/9/2026: l'ordine dei corpi nello stato
        /// viene da `GetBodies2`, **non segue la creazione e cambia fra un giro
        /// e l'altro** (tre membri strutturali: C1 = la corta in un giro, la
        /// lunga nell'altro). Molti di questi casi hanno un corpo solo quando
        /// lo citano, e sono innocui; gli altri sono l'elenco della voce Z-C16.
        /// Il banco ha `@corpo(nome=...)`, che segue l'ordine di creazione.
        ///
        /// **12 al 25/9/2026**, visto rosso a tetto zero prima di fissarlo (i due
        /// rifiuti su `C9`, che non esiste in nessun ordine, restano fuori).
        /// </summary>
        private const int CorpiPerNumeroTetto = 12;

        /// <summary>I «C\d+» scritti a mano nei campi `_uid` degli argomenti.</summary>
        private static IEnumerable<string> CorpiPerNumero(object argomenti)
        {
            var d = argomenti as Dictionary<string, object>;
            if (d == null) yield break;
            foreach (var kv in d)
            {
                if (!kv.Key.EndsWith("_uid", StringComparison.Ordinal)) continue;
                var valori = kv.Value as object[] ?? new[] { kv.Value };
                foreach (var v in valori)
                {
                    var s = v as string;
                    if (s != null && System.Text.RegularExpressions.Regex.IsMatch(s, @"^C\d+$"))
                        yield return s;
                }
            }
        }

        internal static void Tutte()
        {
            Gruppo("i limiti misurati e le descrizioni che li dichiarano (Z-G5, Z-C11)");

            var cartella = Path.Combine(Radice(), "Banco", "casi");
            if (!Directory.Exists(cartella))
            {
                Segna("la cartella dei casi c'e'", false, "manca " + cartella);
                return;
            }

            var serializzatore = new JavaScriptSerializer();
            int dichiarati = 0, piani = 0;
            var illeggibili = new List<string>();
            var senzaGemella = new List<string>();
            var gemelleAssenti = new List<string>();
            var senzaMessaggio = new List<string>();
            var operazioniRifiutate = new Dictionary<string, int>(StringComparer.Ordinal);

            var file = Directory.GetFiles(cartella, "*.json", SearchOption.AllDirectories);
            var nomi = new HashSet<string>(
                file.Select(f => Path.GetFileNameWithoutExtension(f)), StringComparer.Ordinal);
            // LA TOLLERANZA SENZA IL PERCHE'. Il banco la ferma come errore di
            // banco — ma dopo un'ora di giro. Il 24/9/2026 sera un caso nuovo
            // l'ha fatto: un giro intero perso per una riga che si vede leggendo
            // il JSON. Qui si vede prima di lanciare.
            var senzaPerche = new List<string>();
            var passiMuti = new List<string>();
            // LA GEMELLA FINTA: stessa operazione e argomenti IDENTICI al rifiuto.
            // Il banco la ferma come errore di banco (voce F5) — dopo un'ora. Il
            // 25/9/2026 e' successo con `sviluppo_dxf`, dove il rifiuto dipende
            // dallo stato e non da un argomento. Qui si confronta prima.
            var rifiutiDaConfrontare = new List<Tuple<string, string, string, string>>(); // caso, operazione, argomenti, gemella
            var argomentiPerCaso = new Dictionary<string, List<Tuple<string, string>>>(StringComparer.Ordinal);
            // LA GEMELLA COME LA GUARDA IL BANCO (25/9/2026): il primo passo
            // RIUSCITO della stessa operazione, e almeno un argomento in comune.
            var passiDelCaso = new Dictionary<string, List<Tuple<string, Dictionary<string, object>, bool>>>(StringComparer.Ordinal);
            var rifiutiConGemella = new List<Tuple<string, string, Dictionary<string, object>, string>>();
            var js = new JavaScriptSerializer();
            var casiCoiNumeri = new SortedSet<string>(StringComparer.Ordinal);
            // Z-C6: operazione -> casi dichiarati che la usano in un passo.
            var casiPerOp = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var f in file)
            {
                string nome = Path.GetFileNameWithoutExtension(f);
                object radice;
                try { radice = serializzatore.DeserializeObject(File.ReadAllText(f)); }
                catch (Exception ex) { illeggibili.Add(nome + ": " + ex.Message); continue; }

                var caso = radice as Dictionary<string, object>;
                if (caso == null) { piani++; continue; }
                dichiarati++;

                // Le attese stanno nei passi («attese») e, per la regressione,
                // in «dopo». Si guardano tutte e due.
                Action<object, string> guardaLeAttese = (elenco, dove) =>
                {
                    foreach (var at in (elenco as object[]) ?? new object[0])
                    {
                        var attesa = at as Dictionary<string, object>;
                        if (attesa == null || !attesa.ContainsKey("tolleranza")) continue;
                        if (!attesa.ContainsKey("perche_questa_tolleranza"))
                            senzaPerche.Add(nome + " / " + dove + " / " +
                                            (attesa.ContainsKey("cosa") ? Convert.ToString(attesa["cosa"]) : "?"));
                    }
                };
                object dopoGrezzo;
                if (caso.TryGetValue("dopo", out dopoGrezzo)) guardaLeAttese(dopoGrezzo, "dopo");

                object grezzo;
                if (!caso.TryGetValue("passi", out grezzo)) continue;
                var passi = grezzo as object[];
                if (passi == null) continue;

                int numero = 0;
                foreach (var p in passi)
                {
                    var passo = p as Dictionary<string, object>;
                    numero++;
                    if (passo == null) continue;
                    object at0;
                    if (passo.TryGetValue("attese", out at0)) guardaLeAttese(at0, "passo " + numero);
                    // UN PASSO CHE NON MISURA NIENTE (25/9/2026): il banco lo
                    // ferma come errore di banco — dopo un'ora e mezza di giro, e
                    // fermando tutto il caso li'. E' successo col caso della
                    // tavola guardata: tre fori senza attese, e le attese della
                    // tavola non sono mai girate. Si vede leggendo il JSON.
                    else if (!passo.ContainsKey("deve_fallire")) passiMuti.Add(nome + " / passo " + numero);
                    object opN, argN;
                    string opNome = passo.TryGetValue("operazione", out opN) ? Convert.ToString(opN) : "";
                    string argTesto = passo.TryGetValue("argomenti", out argN) ? js.Serialize(argN) : "{}";
                    List<Tuple<string, string>> suoi;
                    if (!argomentiPerCaso.TryGetValue(nome, out suoi)) argomentiPerCaso[nome] = suoi = new List<Tuple<string, string>>();
                    suoi.Add(Tuple.Create(opNome, argTesto));
                    List<Tuple<string, Dictionary<string, object>, bool>> passiQui;
                    if (!passiDelCaso.TryGetValue(nome, out passiQui))
                        passiDelCaso[nome] = passiQui = new List<Tuple<string, Dictionary<string, object>, bool>>();
                    passiQui.Add(Tuple.Create(opNome,
                        argN as Dictionary<string, object> ?? new Dictionary<string, object>(),
                        passo.ContainsKey("deve_fallire")));
                    HashSet<string> casiDellOp;
                    if (!casiPerOp.TryGetValue(opNome, out casiDellOp))
                        casiPerOp[opNome] = casiDellOp = new HashSet<string>(StringComparer.Ordinal);
                    casiDellOp.Add(nome);
                    // IL CORPO PER NUMERO (Z-C16). Un «C2» scritto a mano in un
                    // campo `_uid`: si esclude solo il numero che il rifiuto
                    // stesso nomina nel messaggio atteso (`C9` che non esiste
                    // e' il punto del caso, e non dipende dall'ordine).
                    foreach (var citato in CorpiPerNumero(argN))
                    {
                        object dvC; string attesoNelMessaggio = "";
                        if (passo.TryGetValue("deve_fallire", out dvC) && dvC is Dictionary<string, object>)
                            attesoNelMessaggio = js.Serialize(dvC);
                        if (attesoNelMessaggio.Contains("\"" + citato + "\"")) continue;
                        casiCoiNumeri.Add(nome);
                    }
                    object dv0;
                    if (passo.TryGetValue("deve_fallire", out dv0) && dv0 is Dictionary<string, object>)
                    {
                        object g0;
                        var gem = ((Dictionary<string, object>)dv0).TryGetValue("gemella", out g0) ? Convert.ToString(g0) : null;
                        if (!string.IsNullOrEmpty(gem))
                        {
                            rifiutiDaConfrontare.Add(Tuple.Create(nome, opNome, argTesto, gem));
                            rifiutiConGemella.Add(Tuple.Create(nome, opNome,
                                argN as Dictionary<string, object> ?? new Dictionary<string, object>(), gem));
                        }
                    }
                    object dv;
                    if (!passo.TryGetValue("deve_fallire", out dv)) continue;
                    var rifiuto = dv as Dictionary<string, object>;
                    if (rifiuto == null) continue;

                    object op;
                    string operazione = passo.TryGetValue("operazione", out op)
                        ? Convert.ToString(op) : "(senza nome)";
                    int quanti;
                    operazioniRifiutate.TryGetValue(operazione, out quanti);
                    operazioniRifiutate[operazione] = quanti + 1;

                    // ── LA GEMELLA (Z-C11) ───────────────────────────────
                    object g;
                    string gemella = rifiuto.TryGetValue("gemella", out g)
                        ? Convert.ToString(g) : null;
                    if (string.IsNullOrEmpty(gemella)) senzaGemella.Add(nome + " / " + operazione);
                    else if (!nomi.Contains(gemella))
                        gemelleAssenti.Add(nome + " -> «" + gemella + "» non esiste");

                    // ── E IL MESSAGGIO DEV'ESSERE PRETESO ────────────────
                    //
                    // «deve fallire» senza dire con quale messaggio e' quasi
                    // niente: qualunque errore lo soddisfa, compreso quello
                    // sbagliato. Il banco lo pretende gia'; qui si vede subito.
                    object m;
                    var messaggi = rifiuto.TryGetValue("il_messaggio_contiene", out m)
                        ? m as object[] : null;
                    if (messaggi == null || messaggi.Length == 0)
                        senzaMessaggio.Add(nome + " / " + operazione);
                }
            }

            // IL TETTO CALA E NON SALE (Z-C16). I casi che li citano ancora
            // sono l'elenco da guardare uno per uno; un caso NUOVO che cita un
            // corpo per numero fa diventare rosso questo controllo il giorno in
            // cui lo si scrive: si usa `@corpo(nome=...)`.
            Segna("i casi che citano un corpo per numero non aumentano (Z-C16)",
                  casiCoiNumeri.Count <= CorpiPerNumeroTetto,
                  casiCoiNumeri.Count + " casi (tetto " + CorpiPerNumeroTetto + "): " +
                  string.Join(", ", casiCoiNumeri));
            Segna("e il tetto dei corpi per numero e' quello vero",
                  casiCoiNumeri.Count == CorpiPerNumeroTetto,
                  casiCoiNumeri.Count == CorpiPerNumeroTetto
                      ? "tetto " + CorpiPerNumeroTetto
                      : "sono " + casiCoiNumeri.Count + ": abbassa il tetto a questo numero");

            Segna("ogni passo di un caso misura qualcosa (come pretende il banco)", passiMuti.Count == 0,
                  passiMuti.Count == 0 ? "nessun passo senza attese" : string.Join(" | ", passiMuti));

            Segna("tutti i file di caso si leggono", illeggibili.Count == 0,
                  illeggibili.Count == 0
                      ? file.Length + " file (" + dichiarati + " casi, " + piani + " piani)"
                      : string.Join(" | ", illeggibili));

            var finte = new List<string>();
            foreach (var r in rifiutiDaConfrontare)
            {
                List<Tuple<string, string>> passiGemella;
                if (!argomentiPerCaso.TryGetValue(r.Item4, out passiGemella)) continue;
                // UN CASO PUO' ESSERE GEMELLA DI SE STESSO (il passo verde e il
                // rifiuto nello stesso file): li' il rifiuto si trova da solo fra
                // i passi, e va contato una volta di piu'. Visto al primo giro di
                // questo controllo, su `due_fogli_e_lo_stesso_diedro`.
                int uguali = 0;
                foreach (var p in passiGemella)
                    if (p.Item1 == r.Item2 && p.Item2 == r.Item3) uguali++;
                if (uguali > (r.Item4 == r.Item1 ? 1 : 0))
                    finte.Add(r.Item1 + " -> " + r.Item4 + " (" + r.Item2 + " " + r.Item3 + ")");
            }
            // E NON BASTA CHE NON SIA IDENTICA: il banco la confronta col PRIMO
            // passo riuscito della stessa operazione e pretende almeno un
            // argomento in comune con lo stesso valore (`Attese.SiSomigliano`,
            // la stessa funzione che usa lui). Il 25/9/2026 il rifiuto su «V9»
            // aveva in comune «quante» col SECONDO passo della gemella e niente
            // col primo: errore di banco dopo cento minuti di giro.
            var lontane = new List<string>();
            foreach (var r in rifiutiConGemella)
            {
                List<Tuple<string, Dictionary<string, object>, bool>> passiG;
                if (!passiDelCaso.TryGetValue(r.Item4, out passiG)) continue;   // gemella assente: si dice qui sotto
                Dictionary<string, object> suoi = null;
                foreach (var p in passiG)
                    if (p.Item1 == r.Item2 && !p.Item3) { suoi = p.Item2; break; }
                if (suoi == null)
                {
                    lontane.Add(r.Item1 + " -> " + r.Item4 + ": nessun passo «" + r.Item2 + "» che riesce");
                    continue;
                }
                string perche;
                if (!ProvaSaldatura.Attese.SiSomigliano(r.Item3, suoi, out perche))
                    lontane.Add(r.Item1 + " -> " + r.Item4 + ": " + perche);
            }
            Segna("ogni gemella somiglia al suo rifiuto come la vuole il banco (F5, vista prima del giro)",
                  lontane.Count == 0,
                  lontane.Count == 0 ? rifiutiConGemella.Count + " coppie guardate col criterio del banco" : string.Join(" | ", lontane));

            Segna("nessuna gemella e' identica al suo rifiuto (voce F5, vista prima del giro)",
                  finte.Count == 0,
                  finte.Count == 0 ? rifiutiDaConfrontare.Count + " rifiuti con gemella confrontati" : string.Join(" | ", finte));

            Segna("ogni tolleranza dice perche' (la regola del banco, vista prima del giro)",
                  senzaPerche.Count == 0,
                  senzaPerche.Count == 0
                      ? "nessuna tolleranza muta"
                      : string.Join(" | ", senzaPerche));

            Segna("ogni rifiuto dichiara la sua gemella", senzaGemella.Count == 0,
                  senzaGemella.Count == 0
                      ? operazioniRifiutate.Values.Sum() + " rifiuti, tutti con gemella"
                      : string.Join(" | ", senzaGemella));

            Segna("e la gemella esiste davvero", gemelleAssenti.Count == 0,
                  gemelleAssenti.Count == 0
                      ? "nessun rimando a un caso che non c'e'"
                      : string.Join(" | ", gemelleAssenti));

            Segna("ogni rifiuto pretende un messaggio", senzaMessaggio.Count == 0,
                  senzaMessaggio.Count == 0
                      ? "nessun «deve_fallire» che accetti qualunque errore"
                      : string.Join(" | ", senzaMessaggio));

            // ── E LE DESCRIZIONI? (Z-G5) ────────────────────────────────
            var catalogo = CatalogoServito();
            if (!File.Exists(catalogo))
            {
                Segna("il catalogo servito c'e'", false, "manca " + catalogo);
                return;
            }

            var radiceCat = serializzatore.DeserializeObject(File.ReadAllText(catalogo))
                            as Dictionary<string, object>;
            object vociGrezze;
            var voci = radiceCat != null && radiceCat.TryGetValue("operazioni", out vociGrezze)
                ? vociGrezze as object[] : null;
            if (voci == null)
            {
                Segna("il catalogo ha le operazioni", false, "«operazioni» non e' un elenco");
                return;
            }

            var testoDi = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var v in voci)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, d, s;
                o.TryGetValue("nome", out n);
                o.TryGetValue("descrizione", out d);
                o.TryGetValue("sommario", out s);
                if (n == null) continue;
                testoDi[Convert.ToString(n)] =
                    (Convert.ToString(d) + " " + Convert.ToString(s)).ToLowerInvariant();
            }

            var mute = new List<string>();
            var fuoriCatalogo = new List<string>();
            foreach (var op in operazioniRifiutate.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                string testo;
                if (!testoDi.TryGetValue(op, out testo)) { fuoriCatalogo.Add(op); continue; }
                bool parla = false;
                foreach (var spia in Spie) if (testo.Contains(spia)) { parla = true; break; }
                if (!parla) mute.Add(op + " (" + operazioniRifiutate[op] + ")");
            }

            Segna("ogni operazione col rifiuto provato sta a catalogo",
                  fuoriCatalogo.Count == 0,
                  fuoriCatalogo.Count == 0
                      ? operazioniRifiutate.Count + " operazioni con almeno un rifiuto provato"
                      : "fuori catalogo: " + string.Join(", ", fuoriCatalogo));

            // IL TETTO CALA E NON SALE. Una guardia nuova senza la frase che la
            // dichiara fa diventare rosso questo controllo, ed e' il momento in
            // cui serve: dopo, nessuno ci torna piu'.
            Segna("le descrizioni mute non sono aumentate", mute.Count <= Mute,
                  mute.Count + " mute su " + operazioniRifiutate.Count +
                  " (tetto " + Mute + "): " + string.Join(", ", mute));

            Segna("e il tetto e' quello vero", mute.Count == Mute,
                  mute.Count == Mute
                      ? "zero: ogni operazione col rifiuto provato lo dichiara"
                      : "sono " + mute.Count + ": abbassa il tetto a questo numero");

            OgniOperazioneHaUnCasoVerde(
                new HashSet<string>(testoDi.Keys, StringComparer.Ordinal), casiPerOp,
                Path.Combine(Radice(), "Banco", "resoconto", "ultimo.json"));
        }

        /// <summary>
        /// Le operazioni a catalogo che oggi NON possono avere un caso verde, e
        /// perche'. Ognuna si ricontrolla: se esce dal catalogo o trova un caso
        /// che la usa, il permesso va tolto — come in Z-C7, dove un'eccezione per
        /// una riga che non c'e' piu' e' un buco che resta aperto.
        /// </summary>
        private static readonly Dictionary<string, string> SenzaCasoVerde =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // (26/9/2026) `quote_dal_modello` non e' piu' qui: fuori processo
                // porta le quote (7 sullo stesso pezzo di Z-C1, due sonde), e ha la
                // sua coppia vera: `quote/le_quote_arrivano_dal_modello`.
            };

        /// <summary>
        /// LA SECONDA META' DI Z-C6: `Provata = true` deve puntare a un caso che
        /// ha GIRATO, verde, nell'ultimo giro — non a un caso che esiste.
        ///
        /// Il 25/9/2026, scrivendo i casi che mancavano, due operazioni «provate»
        /// si sono scoperte mai funzionanti (`taglia_membri`, `cordone_saldatura`)
        /// e una e' uscita dal catalogo (`fazzoletto`). «Provata» voleva dire che
        /// qualcuno ci aveva scritto true. Adesso lo dice il banco: scrive i nomi
        /// dei casi verdi in `ultimo.json` («casi_verdi»), e qui si incrociano
        /// col catalogo servito.
        /// </summary>
        private static void OgniOperazioneHaUnCasoVerde(
            HashSet<string> aCatalogo, Dictionary<string, HashSet<string>> casiPerOp, string ultimoJson)
        {
            if (!File.Exists(ultimoJson))
            {
                Segna("il banco dice quali casi sono verdi (Z-C6)", false, "manca " + ultimoJson);
                return;
            }
            var ultimo = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(ultimoJson))
                         as Dictionary<string, object>;
            object grezzi;
            var elenco = ultimo != null && ultimo.TryGetValue("casi_verdi", out grezzi) ? grezzi as object[] : null;
            if (elenco == null)
            {
                Segna("il banco dice quali casi sono verdi (Z-C6)", false,
                      "`ultimo.json` non ha «casi_verdi»: l'ultimo giro e' di un banco che non " +
                      "li scriveva ancora. Rilancia il banco.");
                return;
            }
            var verdi = new HashSet<string>(elenco.Select(x => Convert.ToString(x)), StringComparer.Ordinal);
            Segna("il banco dice quali casi sono verdi (Z-C6)", verdi.Count > 0,
                  verdi.Count + " casi verdi nell'ultimo giro");

            var senza = new List<string>();
            foreach (var op in aCatalogo.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (SenzaCasoVerde.ContainsKey(op)) continue;
                HashSet<string> suoi;
                if (!casiPerOp.TryGetValue(op, out suoi)) { senza.Add(op + " (nessun caso)"); continue; }
                if (!suoi.Any(verdi.Contains))
                    senza.Add(op + " (" + string.Join(", ", suoi.OrderBy(x => x, StringComparer.Ordinal)) +
                              ": nessuno verde nell'ultimo giro)");
            }
            Segna("ogni operazione a catalogo ha un caso VERDE nell'ultimo giro (Z-C6)",
                  senza.Count == 0,
                  senza.Count == 0
                      ? (aCatalogo.Count - SenzaCasoVerde.Count) + " su " + aCatalogo.Count + ", piu' " +
                        SenzaCasoVerde.Count + " eccezioni dichiarate col perche'"
                      : string.Join(" | ", senza));

            var scadute = new List<string>();
            foreach (var kv in SenzaCasoVerde)
            {
                if (!aCatalogo.Contains(kv.Key)) scadute.Add(kv.Key + ": non e' piu' a catalogo, togli l'eccezione");
                else if (casiPerOp.ContainsKey(kv.Key)) scadute.Add(kv.Key + ": adesso ha un caso, togli l'eccezione");
            }
            Segna("e le eccezioni valgono ancora (Z-C6)", scadute.Count == 0,
                  scadute.Count == 0 ? string.Join(", ", SenzaCasoVerde.Keys) : string.Join(" | ", scadute));
        }
    }
}
