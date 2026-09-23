using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// IL CATALOGO, che e' l'interfaccia vera verso il modello (voce C1/F7).
    ///
    /// Si legge per RIFLESSIONE dal ponte gia' compilato — `Catalogo` e la sua
    /// `Simili()` vivono li' — e il file `catalogo.json` glielo si indica con
    /// `OPENPDM_CATALOGO`, che e' la stessa variabile che userebbe chi installa
    /// il pacchetto altrove.
    ///
    /// Niente di tutto questo tocca SOLIDWORKS: `Catalogo` legge un file JSON e
    /// confronta stringhe.
    /// </summary>
    internal static class ProveCatalogo
    {
        internal static void Tutte()
        {
            Gruppo("il catalogo");

            var json = Path.Combine(Radice(), "bin", "catalogo.json");

            // IL CONTENUTO DEL CATALOGO SI PUO' LEGGERE ANCHE QUI (voce F9-bis).
            //
            // `bin\\catalogo.json` lo produce `build.ps1`, che senza interop non
            // gira: su un runner senza SOLIDWORKS questo gruppo si fermava alla
            // prima riga e trentadue controlli sul CONTENUTO — famiglie, campi
            // obbligatori, nomi doppi — non venivano fatti benche' non
            // c'entrino niente con SOLIDWORKS. Leggono un JSON.
            //
            // `Strumenti\\catalogo.json` e' lo stesso file, sta in repository ed
            // e' quello che `build.ps1` COPIA in `bin\\`. Si ripiega li', e si dice.
            // Il confronto fra i due resta impossibile, e piu' sotto lo dichiara.
            bool dalRepository = false;
            if (!ProdottoDaBuild(json))
            {
                var inRepository = Path.Combine(Radice(), "Strumenti", "catalogo.json");
                if (!IlPonteSiPotevaCompilare() && File.Exists(inRepository))
                {
                    json = inRepository;
                    dalRepository = true;
                }
                else
                {
                    Segna("il catalogo c'e'", false,
                          "non trovo " + json + " — si produce con .\\build.ps1");
                    return;
                }
            }
            Segna("il catalogo c'e'", true,
                  dalRepository
                      ? json + " (quello in repository: qui SOLIDWORKS non c'e', " +
                        "quindi bin\\catalogo.json non si poteva produrre)"
                      : json);

            Dictionary<string, object> radice;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = 40 * 1024 * 1024;
                radice = s.DeserializeObject(File.ReadAllText(json)) as Dictionary<string, object>;
            }
            catch (Exception ex) { Segna("il catalogo si legge", false, ex.Message); return; }

            var operazioni = (radice != null && radice.ContainsKey("operazioni")
                ? radice["operazioni"] : null) as object[];
            if (operazioni == null) { Segna("il catalogo ha «operazioni»", false, ""); return; }
            Segna("il catalogo ha almeno trenta operazioni", operazioni.Length >= 30,
                  operazioni.Length + " operazioni");

            IlReadmeDiceIlNumeroGiusto(operazioni.Length);

            // ── OGNI OPERAZIONE HA I CAMPI CHE SERVONO ───────────────────
            var senza = new List<string>();
            var famiglie = new HashSet<string>(StringComparer.Ordinal);
            var nomiCatalogo = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in operazioni)
            {
                var v = o as Dictionary<string, object>;
                if (v == null) { senza.Add("(una voce non e' un oggetto)"); continue; }
                string nome = v.ContainsKey("nome") ? Convert.ToString(v["nome"]) : null;
                if (nome != null) nomiCatalogo.Add(nome);
                foreach (var campo in new[] { "nome", "famiglia", "sommario", "descrizione", "schema" })
                    if (!v.ContainsKey(campo) || v[campo] == null)
                        senza.Add((nome ?? "?") + " senza «" + campo + "»");
                if (v.ContainsKey("famiglia")) famiglie.Add(Convert.ToString(v["famiglia"]));
            }
            Segna("ogni operazione ha nome, famiglia, sommario, descrizione, schema",
                  senza.Count == 0, string.Join("; ", senza.Take(4)));

            // ── LE FAMIGLIE DICHIARATE SONO QUELLE USATE ─────────────────
            //
            // Una famiglia usata e non dichiarata e' un'operazione che nel
            // menu non compare: il modello non la vede, e nessuno se ne
            // accorge finche' non la cerca per nome.
            var dichiarate = (radice.ContainsKey("famiglie") ? radice["famiglie"] : null)
                             as Dictionary<string, object>;
            if (dichiarate == null) Segna("il catalogo dichiara le famiglie", false, "");
            else
            {
                var orfane = famiglie.Where(f => !dichiarate.ContainsKey(f)).ToList();
                Segna("ogni famiglia usata e' dichiarata", orfane.Count == 0,
                      orfane.Count == 0 ? dichiarate.Count + " famiglie"
                                        : "usate e non dichiarate: " + string.Join(", ", orfane));
            }

            // ── NESSUN PERCORSO DI QUESTA MACCHINA ───────────────────────
            //
            // Il catalogo lo legge un modello, e finira' in un pacchetto che
            // va altrove. Un «S:\PDM\...» dentro una descrizione e' un
            // indirizzo che da quella macchina non esiste, e chi legge non ha
            // modo di saperlo.
            // ── E IL CONTROLLO HA GIA' SCATTATO PER IL MOTIVO SBAGLIATO ──
            //
            // La prima scrittura cercava «O:\» senza distinguere maiuscole e
            // minuscole, e ha trovato **«tipo:\n»**: due lettere, i due punti e
            // la barra di un a-capo. Un rosso che scatta per un'altra ragione
            // non prova niente e sembra identico a uno che funziona.
            //
            // La lettera di unita' e' una lettera **non preceduta da un'altra
            // lettera**: cosi' «NO:\n» e «tipo:\n» non la sono, e «S:\PDM» si'.
            var testo = File.ReadAllText(json);
            var brutti = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(testo, @"(?<![A-Za-z])[A-Za-z]:\\"))
                brutti.Add(testo.Substring(m.Index, Math.Min(40, testo.Length - m.Index)));
            foreach (var p in new[] { "scratchpad", "\\\\\\\\" })
                if (testo.IndexOf(p, StringComparison.Ordinal) >= 0) brutti.Add(p);
            Segna("nessun percorso di questa macchina nel catalogo",
                  brutti.Count == 0,
                  brutti.Count == 0 ? "" : "trovati: " + string.Join(" | ", brutti.Take(3)));

            OgniEseguibileEDichiarata(nomiCatalogo);
            IlCatalogoNonERimastoIndietro();

            Simili();
        }

        // ================================================================
        // IL JSON E' UN PRODOTTO, E UN PRODOTTO PUO' ESSERE VECCHIO
        // ================================================================

        /// <summary>
        /// `catalogo.json` combacia ancora con il catalogo C# che lo genera?
        ///
        /// COSTATO UN GIRO IL 22/9/2026. Scritta l'operazione
        /// `nota_in_tavola`, compilata, il ponte l'ha rifiutata con «non
        /// esiste nessuna operazione «nota_in_tavola». Forse cercavi:
        /// quota_in_tavola» — mentre esisteva, era nel dispatcher ed era
        /// dentro la DLL che il ponte aveva appena caricato. Mancava solo dal
        /// JSON, che e' l'unica cosa che il ponte guarda per sapere cosa
        /// esiste.
        ///
        /// E `Strumenti/aggiorna.md` diceva: *«`build.ps1` rigenera il file a
        /// ogni pacchetto, quindi non puo' restare indietro senza che qualcuno
        /// cancelli quel passo»*. **Non e' vero con `-SoloPonte`**, che e' il
        /// modo in cui si compila mentre si itera. Una garanzia scritta in un
        /// documento non e' una garanzia: e' una speranza con una fonte.
        ///
        /// Lo strumento per accorgersene c'era gia' — `--controlla` esce 1 se
        /// non combacia, e dice DOVE — e nessuno lo chiamava.
        ///
        /// SI GUARDANO DUE COPPIE, e sono difetti diversi:
        /// il C# contro `Strumenti/catalogo.json` (qualcuno ha toccato le
        /// operazioni e non ha riesportato), e `Strumenti/` contro `bin/`
        /// (qualcuno ha riesportato e non ha rifatto il pacchetto, quindi il
        /// ponte carica ancora quello vecchio — ed e' esattamente il caso di
        /// oggi).
        /// </summary>
        private static void IlCatalogoNonERimastoIndietro()
        {
            var strumento = Path.Combine(Radice(), "bin", "Riscontro.Catalogo.exe");
            var sorgente = Path.Combine(Radice(), "Strumenti", "catalogo.json");
            var pacchetto = Path.Combine(Radice(), "bin", "catalogo.json");

            if (!ProdottoDaBuild(strumento))
            {
                if (!IlPonteSiPotevaCompilare())
                {
                    NonQui("il catalogo esportato combacia col C#",
                           "qui SOLIDWORKS non c'e': Riscontro.Catalogo.exe dipende da " +
                           "Riscontro.Disegno, che senza interop non si compila.");
                    return;
                }
                Segna("l'esportatore del catalogo c'e'", false,
                      "non trovo " + strumento + " — si produce con .\\build.ps1");
                return;
            }

            // 1. IL C# CONTRO IL JSON ESPORTATO.
            string detto = "";
            int uscita = -1;
            try
            {
                var p = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo(strumento,
                        "--controlla \"" + sorgente + "\"")
                    {
                        WorkingDirectory = Radice(),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };
                p.Start();
                detto = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
                p.WaitForExit(30000);
                uscita = p.ExitCode;
            }
            catch (Exception ex)
            {
                Segna("il catalogo esportato combacia col C#", false, ex.Message);
                return;
            }

            // La riga utile e' quella che dice DOVE differiscono: un «sono
            // diversi» su settantamila caratteri manda a cercare a occhio.
            string riga = detto.Replace("\r", " ").Replace("\n", " ");
            if (riga.Length > 160) riga = riga.Substring(0, 160) + "...";

            Segna("il catalogo esportato combacia col C#", uscita == 0,
                  uscita == 0 ? "Strumenti\\catalogo.json e' aggiornato"
                              : "NON combacia: si rigenera con "
                                + ".\\bin\\Riscontro.Catalogo.exe Strumenti\\catalogo.json — " + riga);

            // 2. E QUELLO CHE CARICA IL PONTE E' LO STESSO?
            if (!File.Exists(pacchetto))
            {
                if (!IlPonteSiPotevaCompilare())
                {
                    NonQui("il ponte carica il catalogo di adesso",
                           "qui SOLIDWORKS non c'e': bin\\catalogo.json non esiste " +
                           "perche' build.ps1 non poteva girare, e un file si confronta " +
                           "con un altro file, non con la sua assenza.");
                    return;
                }
                Segna("il ponte carica il catalogo di adesso", false,
                      "non trovo " + pacchetto + " — si produce con .\\build.ps1");
                return;
            }

            string a = null, b = null;
            try { a = Impronta(sorgente); b = Impronta(pacchetto); }
            catch (Exception ex)
            {
                Segna("il ponte carica il catalogo di adesso", false, ex.Message);
                return;
            }

            Segna("il ponte carica il catalogo di adesso",
                  string.Equals(a, b, StringComparison.Ordinal),
                  string.Equals(a, b, StringComparison.Ordinal)
                      ? "Strumenti\\ e bin\\ sono lo stesso file"
                      : "bin\\catalogo.json e' rimasto indietro rispetto a Strumenti\\: "
                        + "si rilancia .\\build.ps1, che lo ricopia. Finche' non lo si fa, "
                        + "il ponte RIFIUTA le operazioni nuove dicendo che non esistono");
        }

        private static string Impronta(string percorso)
        {
            using (var h = System.Security.Cryptography.MD5.Create())
            using (var f = File.OpenRead(percorso))
                return BitConverter.ToString(h.ComputeHash(f));
        }

        // ================================================================
        // QUELLO CHE SI ESEGUE E QUELLO CHE SI VEDE
        // ================================================================

        /// <summary>
        /// Ogni operazione che il dispatcher sa eseguire deve stare o nel
        /// catalogo, o nell'elenco delle non provate. In nessun altro posto.
        ///
        /// IL BUCO CHE QUESTO CONTROLLO PRENDE, misurato il 22/9/2026.
        /// `NomiEseguibili` ne contava **70**, `catalogo.json` ne dichiarava
        /// **68**, e le due che mancavano — `croci_di_centro` e
        /// `distinta_taglio_in_tavola` — erano fuori **per una buona ragione**
        /// (`Provata=false`: le API dicono di si' e non creano niente, 0 -> 0).
        ///
        /// Ma fino a qui nessuno confrontava i due elenchi, e la stessa
        /// differenza l'avrebbe prodotta un'operazione **dimenticata**:
        /// scritta, funzionante, mai aggiunta a `Operazioni.cs`. Quella non
        /// darebbe nessun errore da nessuna parte — il banco la proverebbe
        /// (legge `NomiEseguibili`), il catalogo non la direbbe, e nessun
        /// modello potrebbe chiamarla. **Il difetto che non da' errore**,
        /// applicato all'unica cosa che il modello usa per sapere cosa esiste.
        ///
        /// SI LEGGE `NomiEseguibili` DAL SORGENTE, e non per riflessione:
        /// quella costante vive in `OperazioniSw`, che tocca l'interop di
        /// Dassault — caricarne il tipo qui dentro vorrebbe dire pretendere
        /// SOLIDWORKS in un giro che deve girare senza. Le non provate invece
        /// si chiedono a `Esporta.NonProvate()`, che e' esattamente cio' che
        /// l'esportatore usa per decidere: non una seconda copia della regola.
        ///
        /// Leggerne ZERO e' un fallimento, non un successo: un elenco vuoto
        /// farebbe passare ogni confronto.
        /// </summary>
        private static void OgniEseguibileEDichiarata(HashSet<string> nelCatalogo)
        {
            var sorgente = Path.Combine(Radice(), "Riscontro.Disegno", "OperazioniSw.cs");
            if (!File.Exists(sorgente))
            {
                Segna("il sorgente del dispatcher si legge", false, "non trovo " + sorgente);
                return;
            }

            var testo = File.ReadAllText(sorgente);
            var blocco = System.Text.RegularExpressions.Regex.Match(
                testo, @"NomiEseguibili\s*=\s*\{(.*?)\};",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var eseguibili = new HashSet<string>(StringComparer.Ordinal);
            if (blocco.Success)
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(blocco.Groups[1].Value, "\"([a-z0-9_]+)\""))
                    eseguibili.Add(m.Groups[1].Value);

            Segna("l'elenco delle operazioni eseguibili si legge",
                  eseguibili.Count >= 30,
                  eseguibili.Count + " nomi in NomiEseguibili");
            if (eseguibili.Count < 30) return;

            // Le non provate, chieste a chi decide chi resta fuori dal file.
            //
            // Si chiedono ALLA LIBRERIA COMPILATA, e li' la macchina conta: senza
            // interop non esiste nessuna DLL da interrogare. Il conto
            // «eseguibili = nel catalogo + non provate» non si puo' fare, e un
            // conto che non si puo' fare non si finge riuscito.
            if (!ProdottoDaBuild(Path.Combine(Radice(), "bin", "Riscontro.Disegno.dll")) &&
                !IlPonteSiPotevaCompilare())
            {
                NonQui("ogni operazione eseguibile e' nel catalogo o dichiarata non provata",
                       "qui SOLIDWORKS non c'e': l'elenco delle non provate si chiede a " +
                       "Riscontro.Disegno.dll, che senza interop non si compila.");
                return;
            }

            List<string> nonProvate;
            try
            {
                var ponte = Path.Combine(Radice(), "bin", "oce-solid-agent-mcp.exe");
                Carica.Ponte(ponte);   // installa il risolutore per bin\
                var dll = Path.Combine(Radice(), "bin", "Riscontro.Disegno.dll");
                var libreria = Assembly.Load(File.ReadAllBytes(dll));
                var esporta = Carica.Classe(libreria, "Esporta");
                if (esporta == null)
                {
                    Segna("l'elenco delle non provate si chiede all'esportatore", false,
                          "non trovo la classe Esporta");
                    return;
                }
                nonProvate = (esporta.GetMethod("NonProvate",
                                  BindingFlags.Public | BindingFlags.Static)
                                  ?.Invoke(null, null) as System.Collections.IEnumerable)
                             ?.Cast<object>().Select(Convert.ToString).ToList();
                if (nonProvate == null)
                {
                    Segna("l'elenco delle non provate si chiede all'esportatore", false,
                          "NonProvate() non ha risposto");
                    return;
                }
            }
            catch (Exception ex)
            {
                Segna("l'elenco delle non provate si chiede all'esportatore", false, ex.Message);
                return;
            }

            var fuori = new HashSet<string>(nonProvate, StringComparer.Ordinal);

            // 1. Eseguibile e invisibile: il difetto che questo controllo esiste per prendere.
            var invisibili = eseguibili.Where(n => !nelCatalogo.Contains(n) && !fuori.Contains(n))
                                       .OrderBy(n => n, StringComparer.Ordinal).ToList();
            Segna("ogni operazione eseguibile e' nel catalogo o dichiarata non provata",
                  invisibili.Count == 0,
                  invisibili.Count == 0
                      ? eseguibili.Count + " eseguibili = " + nelCatalogo.Count +
                        " nel catalogo + " + fuori.Count + " non provate"
                      : "eseguibili e invisibili al modello: " + string.Join(", ", invisibili));

            // 2. Il verso opposto: una voce di catalogo che il dispatcher non sa eseguire
            //    e' una promessa che si scopre falsa solo chiamandola.
            var promesse = nelCatalogo.Where(n => !eseguibili.Contains(n))
                                      .OrderBy(n => n, StringComparer.Ordinal).ToList();
            Segna("ogni operazione del catalogo e' davvero eseguibile",
                  promesse.Count == 0,
                  promesse.Count == 0 ? "" : "nel catalogo e non nel dispatcher: " +
                                             string.Join(", ", promesse));

            // 3. E una non provata non deve essere finita nel file lo stesso.
            var sfuggite = fuori.Where(nelCatalogo.Contains)
                                .OrderBy(n => n, StringComparer.Ordinal).ToList();
            Segna("nessuna operazione non provata e' finita nel catalogo",
                  sfuggite.Count == 0,
                  sfuggite.Count == 0 ? fuori.Count + " fuori: " + string.Join(", ", fuori)
                                      : "non provate ma esposte: " + string.Join(", ", sfuggite));
        }

        /// <summary>
        /// `Catalogo.Simili()`: il caso per cui la funzione e' stata riscritta.
        ///
        /// Il primo tentativo cercava la parola intera dentro il nome, e su
        /// «asole» **non trovava «asola»** — cioe' falliva proprio dove
        /// serviva: chi sbaglia un nome quasi sempre ne ha in mente uno vicino,
        /// e lo sbaglia di una lettera.
        /// </summary>
        private static void Simili()
        {
            var ponte = Path.Combine(Radice(), "bin", "oce-solid-agent-mcp.exe");
            if (!File.Exists(ponte))
            {
                Segna("Catalogo.Simili: il ponte c'e'", false,
                      "non trovo " + ponte + " — si produce con .\\build.ps1");
                return;
            }

            object catalogo;
            MethodInfo simili;
            try
            {
                Environment.SetEnvironmentVariable(
                    "OPENPDM_CATALOGO", Path.Combine(Radice(), "bin", "catalogo.json"));
                // SI CARICA DAI BYTE: `LoadFrom` su un'unita' di rete risponde
                // NotSupportedException, e questo repository sta su S:.
                var asm = Carica.Ponte(ponte);
                var tipo = Carica.Classe(asm, "Catalogo");
                if (tipo == null) { Segna("Catalogo.Simili: la classe c'e'", false, "Catalogo assente"); return; }
                catalogo = Activator.CreateInstance(tipo, true);
                simili = tipo.GetMethod("Simili");
                if (simili == null) { Segna("Catalogo.Simili: il metodo c'e'", false, ""); return; }
            }
            catch (Exception ex) { Segna("Catalogo.Simili: si carica", false, ex.ToString()); return; }

            Func<string, List<string>> di = n =>
                (simili.Invoke(catalogo, new object[] { n }) as System.Collections.IEnumerable)
                    ?.Cast<object>().Select(Convert.ToString).ToList() ?? new List<string>();

            Segna("«asole» trova «asola»", di("asole").Contains("asola"),
                  string.Join(", ", di("asole")));
            Segna("«foro» trova «foro_guidato»", di("foro").Contains("foro_guidato"),
                  string.Join(", ", di("foro")));
            Segna("un nome vuoto non esplode e non inventa", di("").Count == 0);
            Segna("un nome che non somiglia a niente non inventa",
                  di("qwertyuiop").Count == 0, string.Join(", ", di("qwertyuiop")));
            Segna("non ne offre piu' di cinque", di("a_asola_foro_taglio_piano").Count <= 5,
                  di("a_asola_foro_taglio_piano").Count + " offerti");
        }

        // ================================================================
        // IL NUMERO SCRITTO NEL README E' UNA MISURA, E INVECCHIA COME TUTTE
        //
        // Il 16/9/2026 il README diceva «trentotto operazioni» in due punti
        // mentre il catalogo ne aveva sessanta. Nessun controllo poteva
        // accorgersene: i documenti non si compilano, non si eseguono, e
        // nessuno li rilegge finche' non servono a qualcuno che non c'era.
        //
        // E' ESATTAMENTE IL DIFETTO CHE NON DA' ERRORE, applicato ai
        // documenti: il README sembra giusto, si legge bene, e dice il falso.
        // Chi lo legge non ha modo di accorgersene — e' la sua unica fonte.
        //
        // Il controllo cerca solo DUE forme, non tutti i numeri che compaiono:
        // «<numero> operazioni» e «operazioni di disegno sono <numero>». Cosi'
        // «Cinque, non sessanta» (che parla degli STRUMENTI) non lo fa
        // scattare: un controllo che grida su una frase giusta si impara a
        // ignorare, ed e' peggio di nessun controllo.
        // ================================================================
        private static void IlReadmeDiceIlNumeroGiusto(int quante)
        {
            var readme = Path.Combine(Radice(), "README.md");
            if (!File.Exists(readme))
            {
                Segna("il README c'e'", false, readme);
                return;
            }

            string testo;
            try { testo = File.ReadAllText(readme); }
            catch (Exception ex) { Segna("il README si legge", false, ex.Message); return; }

            var trovati = new List<string>();
            foreach (var forma in new[]
            {
                @"([a-zàèéìòù]+)\s+operazioni",
                @"operazioni\s+di\s+disegno\s+sono\s+([a-zàèéìòù]+)",
            })
            {
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(
                             testo, forma,
                             System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    int n = Numero(m.Groups[1].Value);
                    // Non e' un numero scritto a parole: e' un aggettivo
                    // qualunque («altre operazioni», «sessanta operazioni»).
                    // Si tace, non si inventa un difetto.
                    if (n < 0) continue;
                    if (n != quante) trovati.Add("«" + m.Value.Replace("\n", " ") + "»");
                }
            }

            Segna("il README dice quante sono le operazioni, e sono quelle vere",
                  trovati.Count == 0,
                  trovati.Count == 0
                      ? "il catalogo ne ha " + quante
                      : "il catalogo ne ha " + quante + ", il README dice " +
                        string.Join(" e ", trovati));
        }

        /// <summary>
        /// Un numero italiano scritto a parole, da 0 a 99. `-1` quando la
        /// parola non e' un numero — che e' il caso normale e non un errore.
        /// </summary>
        private static int Numero(string parola)
        {
            if (string.IsNullOrWhiteSpace(parola)) return -1;
            var p = parola.Trim().ToLowerInvariant();

            var unita = new[]
            {
                "zero", "uno", "due", "tre", "quattro", "cinque", "sei", "sette",
                "otto", "nove", "dieci", "undici", "dodici", "tredici",
                "quattordici", "quindici", "sedici", "diciassette", "diciotto",
                "diciannove",
            };
            for (int i = 0; i < unita.Length; i++)
                if (p == unita[i]) return i;

            var decine = new[]
            {
                "venti", "trenta", "quaranta", "cinquanta", "sessanta",
                "settanta", "ottanta", "novanta",
            };
            for (int d = 0; d < decine.Length; d++)
            {
                int valore = (d + 2) * 10;
                if (p == decine[d]) return valore;

                // L'ELISIONE. «venti» + «uno» fa «ventuno», non «ventiuno»:
                // davanti a «uno» e «otto» la decina perde la vocale finale.
                // Senza questa riga «trentotto» — il numero sbagliato che ha
                // fatto nascere il controllo — non verrebbe nemmeno letto.
                var radice = decine[d].Substring(0, decine[d].Length - 1);
                for (int u = 1; u <= 9; u++)
                {
                    bool elide = u == 1 || u == 8;
                    if (p == (elide ? radice : decine[d]) + unita[u]) return valore + u;
                }
            }
            return -1;
        }
    }
}
