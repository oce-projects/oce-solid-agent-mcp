using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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

            var operazioni = (radice != null && radice.ContainsKey("operations")
                ? radice["operations"] : null) as object[];
            if (operazioni == null) { Segna("il catalogo ha «operations»", false, ""); return; }
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
                string nome = v.ContainsKey("name") ? Convert.ToString(v["name"]) : null;
                if (nome != null) nomiCatalogo.Add(nome);
                foreach (var campo in new[] { "name", "family", "summary", "details", "schema" })
                    if (!v.ContainsKey(campo) || v[campo] == null)
                        senza.Add((nome ?? "?") + " senza «" + campo + "»");
                if (v.ContainsKey("family")) famiglie.Add(Convert.ToString(v["family"]));
            }
            Segna("ogni operazione ha nome, famiglia, sommario, descrizione, schema",
                  senza.Count == 0, string.Join("; ", senza.Take(4)));

            // ── LE FAMIGLIE DICHIARATE SONO QUELLE USATE ─────────────────
            //
            // Una famiglia usata e non dichiarata e' un'operazione che nel
            // menu non compare: il modello non la vede, e nessuno se ne
            // accorge finche' non la cerca per nome.
            var dichiarate = (radice.ContainsKey("families") ? radice["families"] : null)
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
        /// `note`, compilata, il ponte l'ha rifiutata con «non
        /// esiste nessuna operazione «note». Forse cercavi:
        /// drawing_dimension» — mentre esisteva, era nel dispatcher ed era
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
        /// **68**, e le due che mancavano — `center_marks` e
        /// `drawing_cut_list` — erano fuori **per una buona ragione**
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
        /// «asole» **non trovava «slot»** — cioe' falliva proprio dove
        /// serviva: chi sbaglia un nome quasi sempre ne ha in mente uno vicino,
        /// e lo sbaglia di una lettera.
        /// </summary>
        private static void Simili()
        {
            // «NON QUI» SE LA MACCHINA NON PUO' COMPILARE IL PONTE, ROSSO SE PUO'
            // E NESSUNO HA LANCIATO build.ps1: la regola sta in un posto solo.
            // Prima qui c'era un rosso secco, e in CI (dove il ponte non si
            // compila mai) teneva rossa la CI a ogni push.
            var ponte = PonteOppureNonQui("Catalogo.Simili: i nomi vicini a quello sbagliato");
            if (ponte == null) return;

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

            Segna("«slots» trova «slot»", di("slots").Contains("slot"),
                  string.Join(", ", di("slots")));
            Segna("«hole» trova «hole_wizard»", di("hole").Contains("hole_wizard"),
                  string.Join(", ", di("hole")));
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
                Segna("the README exists", false, readme);
                return;
            }

            string testo;
            try { testo = File.ReadAllText(readme); }
            catch (Exception ex) { Segna("the README can be read", false, ex.Message); return; }

            // BILINGUAL SINCE W3.1-bis.2 (30/9/2026). The README is going to be
            // translated, and a reader that knew only Italian number words
            // would read «ninety-nine drawing operations» as no number at all:
            // the check would have stopped protecting without failing. So the
            // reader knows English too, and two silences are red now:
            //   * a word that LOOKS like a number and cannot be read (it used
            //     to be skipped by «if (n < 0) continue»);
            //   * a README in which no count can be read at all.
            // The forms stay closed: «N operations», «N drawing operations»
            // and «drawing operations are N» in English, and the two Italian
            // ones above. «Six assembly operations» still does not count: the
            // one English qualifier allowed between number and noun is
            // «drawing».
            NumberReaderReadsBothLanguages();
            CountCheckSeenRed();

            int claims;
            var findings = ReadmeCountFindings(testo, quante, out claims);
            Segna("the README says how many operations there are, and they are the real ones",
                  findings.Count == 0,
                  findings.Count == 0
                      ? "the catalogue has " + quante + ", the README says so " + claims + " times"
                      : "the catalogue has " + quante + "; " + string.Join("; ", findings));
            Segna("the README states the number of operations in a form this check reads",
                  claims > 0,
                  claims > 0 ? claims + " counts read"
                             : "no «N operations» and no «drawing operations are N» in README.md: if the " +
                               "README stops saying how many there are, this check must be changed on purpose");
        }

        private const string ItalianNoun = "operazioni";
        private const string EnglishNoun = "operations";

        // A word of the window: letters, digits and apostrophes, with inner
        // hyphens («ninety-nine»).
        private const string Word = @"[\p{L}\p{Nd}'’]+(?:-[\p{L}\p{Nd}'’]+)*";

        // Up to six words right before the noun: an English number takes up to
        // four («one hundred and sixty-four») and the qualifier one more. A
        // Markdown asterisk counts as a space.
        private static readonly Regex CountBefore = new Regex(
            @"(?<![\p{L}\p{Nd}'’-])(?<words>(?:" + Word + @"[\s*]+){1,6}?)(?<noun>" + ItalianNoun + "|" + EnglishNoun + @")\b",
            RegexOptions.IgnoreCase);

        private static readonly Regex CountAfter = new Regex(
            @"(?<noun>" + ItalianNoun + @"\s+di\s+disegno\s+sono|drawing\s+operations\s+are)[\s*]+(?<words>" + Word +
            @"(?:[\s*]+" + Word + @"){0,5})",
            RegexOptions.IgnoreCase);

        /// <summary>
        /// What the README says about the number of operations and is wrong
        /// or unreadable; `claims` is how many counts it could read.
        /// </summary>
        private static List<string> ReadmeCountFindings(string text, int actual, out int claims)
        {
            var findings = new List<string>();
            int read = 0;
            Action<Match, List<string>, bool> judge = (m, words, before) =>
            {
                // the longest run of words next to the noun that is a number
                for (int k = words.Count; k >= 1; k--)
                {
                    var candidate = before ? words.Skip(words.Count - k) : words.Take(k);
                    int n = Numero(string.Join(" ", candidate));
                    if (n < 0) continue;
                    read++;
                    if (n != actual) findings.Add("«" + Flat(m.Value) + "» says " + n);
                    return;
                }
                var next = before ? words[words.Count - 1] : words[0];
                if (LooksLikeNumber(next))
                    findings.Add("«" + Flat(m.Value) + "»: «" + next + "» looks like a number and cannot be read");
            };

            foreach (Match m in CountBefore.Matches(text))
            {
                var words = Words(m.Groups["words"].Value);
                if (m.Groups["noun"].Value.Equals(EnglishNoun, StringComparison.OrdinalIgnoreCase) && words.Count > 1 &&
                    words[words.Count - 1].Equals("drawing", StringComparison.OrdinalIgnoreCase))
                    words.RemoveAt(words.Count - 1);
                if (words.Count > 0) judge(m, words, true);
            }
            foreach (Match m in CountAfter.Matches(text))
            {
                var words = Words(m.Groups["words"].Value);
                if (words.Count > 0) judge(m, words, false);
            }
            claims = read;
            return findings;
        }

        private static List<string> Words(string s)
        {
            return Regex.Split(s, @"[\s*]+").Where(w => w.Length > 0).ToList();
        }

        private static string Flat(string s)
        {
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        // ── THE SELF-TESTS ───────────────────────────────────────────────

        private static void NumberReaderReadsBothLanguages()
        {
            // The Italian words are built from the reader's own lists: the
            // Italian branch is transitional, and its tests add no Italian to
            // the repository.
            string it73accent = "settantatré";
            string it73apostrophe = ItalianTens[5] + ItalianUnits[3] + "'";
            string it38 = ItalianTens[1].Substring(0, ItalianTens[1].Length - 1) + ItalianUnits[8];
            string it21 = ItalianTens[0].Substring(0, ItalianTens[0].Length - 1) + ItalianUnits[1];
            string it94 = ItalianTens[7] + ItalianUnits[4];
            string glued = ItalianUnits[2] + ItalianTens[4]; // a unit glued to a ten: not a number
            var cases = new[]
            {
                Tuple.Create(it73accent, 73), Tuple.Create(it73apostrophe, 73), Tuple.Create(it38, 38),
                Tuple.Create(it21, 21), Tuple.Create(it94, 94), Tuple.Create(glued, -1),
                Tuple.Create("ninety-six", 96), Tuple.Create("ninety four", 94), Tuple.Create("twenty-one", 21),
                Tuple.Create("thirty-eight", 38), Tuple.Create("seventy-three", 73), Tuple.Create("thirteen", 13),
                Tuple.Create("one hundred and sixty-four", 164), Tuple.Create("a hundred", 100),
                Tuple.Create("two hundred", 200), Tuple.Create("99", 99),
                // not numbers: -1, and never a guess
                Tuple.Create("ninety-fuor", -1), Tuple.Create(it38.Remove(it38.Length - 2, 1), -1),
                Tuple.Create("the", -1), Tuple.Create("drawing", -1), Tuple.Create("two ninety-nine", -1),
                Tuple.Create("hundred and", -1), Tuple.Create("zero hundred", -1),
            };
            var wrong = cases.Where(c => Numero(c.Item1) != c.Item2)
                             .Select(c => c.Item1 + "=" + Numero(c.Item1) + " (expected " + c.Item2 + ")").ToList();
            Segna("the number reader reads Italian and English number words, and nothing else",
                  wrong.Count == 0,
                  wrong.Count == 0 ? cases.Length + " of " + cases.Length + " words read as expected" : string.Join("; ", wrong));
        }

        private static void CountCheckSeenRed()
        {
            string it94 = ItalianTens[7] + ItalianUnits[4];
            string it96 = ItalianTens[7] + ItalianUnits[6];
            string it94misspelled = it94.Remove(it94.Length - 3, 1);
            var cases = new[]
            {
                // text, actual count, red expected
                Tuple.Create("It exposes ninety-six drawing operations as tools.", 94, true),
                Tuple.Create("It exposes ninety-four drawing operations as tools.", 94, false),
                Tuple.Create("Nine, not ninety-nine. The drawing operations are ninety-six, but", 94, true),
                Tuple.Create("The drawing operations are **ninety-four**, but", 94, false),
                Tuple.Create("There are one hundred and sixty-four operations.", 164, false),
                Tuple.Create("There are one hundred and sixty-four operations.", 64, true),
                Tuple.Create("The bench prints 99 operations in its header.", 94, true),
                Tuple.Create("exposes " + it96 + "\n" + ItalianNoun + " as tools", 94, true),
                Tuple.Create("exposes " + it94 + " " + ItalianNoun + " as tools", 94, false),
                // the silence that used to be green: a number word that cannot be read
                Tuple.Create("exposes " + it94misspelled + " " + ItalianNoun + " as tools", 94, true),
                Tuple.Create("It exposes ninety-fuor drawing operations.", 94, true),
                Tuple.Create("It exposes fourty drawing operations.", 40, true),
                Tuple.Create("exposes " + ItalianUnits[2] + ItalianTens[4] + " " + ItalianNoun, 260, true),
                Tuple.Create("exposes ultra" + ItalianTens[4] + " " + ItalianNoun, 160, true),
                // words that are not numbers stay silent
                Tuple.Create("the other operations, all operations, the drawing operations", 94, false),
                Tuple.Create("many " + ItalianNoun + " and six assembly operations", 94, false),
            };
            var wrong = new List<string>();
            foreach (var c in cases)
            {
                int claims;
                bool red = ReadmeCountFindings(c.Item1, c.Item2, out claims).Count > 0;
                if (red != c.Item3) wrong.Add("«" + Flat(c.Item1) + "» against " + c.Item2 + ": " + (red ? "red" : "green"));
            }
            int none;
            bool blind = ReadmeCountFindings("A README that never says how many there are.", 94, out none).Count == 0 && none == 0;
            if (!blind) wrong.Add("a README without any count was not seen as one");
            Segna("the README count check turns red on fakes («ninety-six operations» against 94, a number it cannot read)",
                  wrong.Count == 0,
                  wrong.Count == 0 ? (cases.Length + 1) + " fakes ended as expected" : string.Join("; ", wrong));
        }

        // ── THE NUMBER READER ────────────────────────────────────────────

        private static readonly string[] ItalianUnits =
        {
            "zero", "uno", "due", "tre", "quattro", "cinque", "sei", "sette",
            "otto", "nove", "dieci", "undici", "dodici", "tredici",
            "quattordici", "quindici", "sedici", "diciassette", "diciotto",
            "diciannove",
        };

        private static readonly string[] ItalianTens =
        {
            "venti", "trenta", "quaranta", "cinquanta", "sessanta",
            "settanta", "ottanta", "novanta",
        };

        private static readonly string[] EnglishUnits =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
            "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
            "seventeen", "eighteen", "nineteen",
        };

        private static readonly string[] EnglishTens =
        {
            "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
        };

        /// <summary>
        /// A number written in words, in Italian from 0 to 99 as before and in
        /// English from 0 to 999, or in digits. `-1` when the word is not a
        /// number, which is the normal case and not an error. The Italian
        /// hundreds are left out on purpose: the Italian branch only has to
        /// last until the README is translated, and a hundred written in
        /// Italian words is red below (it looks like a number and cannot be
        /// read), not silent.
        /// </summary>
        private static int Numero(string parola)
        {
            if (string.IsNullOrWhiteSpace(parola)) return -1;
            var p = parola.Trim().ToLowerInvariant().Replace('’', '\'');
            if (Regex.IsMatch(p, @"^\d{1,4}$")) return int.Parse(p, CultureInfo.InvariantCulture);
            int n = ItalianNumber(p);
            return n >= 0 ? n : EnglishNumber(p);
        }

        private static int ItalianNumber(string p)
        {
            if (p.IndexOf(' ') >= 0 || p.IndexOf('-') >= 0) return -1;

            // A COMPOUND «TRE» IS WRITTEN WITH AN ACCENT: without this the
            // number written properly came back as «not a number», and the
            // README check kept QUIET. On 25/9/2026 the README was about to go
            // from seventy-four to seventy-three, and a wrong seventy-three
            // would have gone through. This repository also writes the accent
            // as a vowel and an apostrophe, so that form is read too.
            if (p.Length > 4 && p.EndsWith(ItalianUnits[3] + "'", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 1);
            else if (p.Length > 3 && p.EndsWith("tré", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 1) + "e";
            if (p.IndexOf('\'') >= 0) return -1;

            return ItalianBelowHundred(p);
        }

        private static int ItalianBelowHundred(string p)
        {
            for (int i = 0; i < ItalianUnits.Length; i++)
                if (p == ItalianUnits[i]) return i;
            for (int d = 0; d < ItalianTens.Length; d++)
            {
                int value = (d + 2) * 10;
                if (p == ItalianTens[d]) return value;

                // THE ELISION. Before «uno» and «otto» the tens lose their last
                // vowel: without this the thirty-eight that gave birth to the
                // check would not even be read.
                var root = ItalianTens[d].Substring(0, ItalianTens[d].Length - 1);
                for (int u = 1; u <= 9; u++)
                {
                    bool elides = u == 1 || u == 8;
                    if (p == (elides ? root : ItalianTens[d]) + ItalianUnits[u]) return value + u;
                }
            }
            return -1;
        }

        private static int EnglishNumber(string p)
        {
            var t = p.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            int i = 0, total = 0;
            bool hundreds = false;
            if (t.Length >= 2 && t[1] == "hundred")
            {
                int h = t[0] == "a" ? 1 : Array.IndexOf(EnglishUnits, t[0]);
                if (h < 1 || h > 9) return -1;
                total = h * 100;
                hundreds = true;
                i = 2;
                if (i < t.Length && t[i] == "and") { i++; if (i == t.Length) return -1; }
            }
            int left = t.Length - i;
            if (left == 0) return hundreds ? total : -1;
            if (left == 1)
            {
                int u = Array.IndexOf(EnglishUnits, t[i]);
                int d = Array.IndexOf(EnglishTens, t[i]);
                int v = u >= 0 ? u : d >= 0 ? (d + 2) * 10 : -1;
                if (v < 0 || (hundreds && v == 0)) return -1;
                return total + v;
            }
            if (left == 2)
            {
                int d = Array.IndexOf(EnglishTens, t[i]);
                int u = Array.IndexOf(EnglishUnits, t[i + 1]);
                if (d < 0 || u < 1 || u > 9) return -1;
                return total + (d + 2) * 10 + u;
            }
            return -1;
        }

        /// <summary>
        /// A word that is meant as a number even if the reader cannot read it:
        /// a digit, the root of a ten (at the start, or inside a longer Italian
        /// word), an Italian unit glued to more letters, an English «-teen», a
        /// hyphen joined to a number word, or one letter away from a number
        /// word (six letters or more, so that short words such as «the» are
        /// never taken for one). What it misses, measured: an Italian hundred
        /// with nothing of a ten in it («a hundred and five» in Italian words);
        /// the README says those in English or in digits.
        /// </summary>
        private static bool LooksLikeNumber(string word)
        {
            var w = word.ToLowerInvariant().Replace('’', '\'').Trim('\'');
            if (w.Any(char.IsDigit)) return true;
            var parts = w.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (NumberRoots.Any(r => part.StartsWith(r, StringComparison.Ordinal))) return true;
                // inside a longer Italian word, the tens that English never
                // writes («vent» is in «event», «quarant» in «quarantine»)
                if (InnerRoots.Any(r => part.IndexOf(r, 1, StringComparison.Ordinal) > 0)) return true;
                // a unit glued to more letters: the Italian hundreds
                if (part.Length >= 6 && ItalianUnits.Skip(2).Take(8).Any(u => part.StartsWith(u, StringComparison.Ordinal))) return true;
                if (part.EndsWith("teen", StringComparison.Ordinal) || part == "hundred" || part == "thousand") return true;
                if (parts.Length > 1 && (EnglishUnits.Contains(part) || EnglishTens.Contains(part))) return true;
                if (part.Length >= 6 && NumberWords.Any(n => NearlyEqual(part, n))) return true;
            }
            return false;
        }

        // The roots of the tens and of the teens, in both languages.
        private static readonly string[] NumberRoots =
            ItalianTens.Select(t => t.Substring(0, t.Length - 1))
                       .Concat(ItalianUnits.Skip(11).Select(u => u.Substring(0, 5)))
                       .Concat(EnglishTens)
                       .Distinct().ToArray();

        // thirty, fifty, sixty, seventy, eighty, ninety: twenty and forty are
        // inside English words
        private static readonly string[] InnerRoots =
            new[] { 1, 3, 4, 5, 6, 7 }.Select(i => ItalianTens[i].Substring(0, ItalianTens[i].Length - 1)).ToArray();

        private static readonly string[] NumberWords =
            Enumerable.Range(0, 100).Select(ItalianWord).Concat(EnglishUnits).Concat(EnglishTens).Distinct().ToArray();

        private static string ItalianWord(int n)
        {
            if (n < 20) return ItalianUnits[n];
            var tens = ItalianTens[n / 10 - 2];
            int u = n % 10;
            if (u == 0) return tens;
            return (u == 1 || u == 8 ? tens.Substring(0, tens.Length - 1) : tens) + ItalianUnits[u];
        }

        /// <summary>One edit apart: a letter changed, added, dropped, or two next ones swapped.</summary>
        private static bool NearlyEqual(string a, string b)
        {
            if (a == b) return false;
            if (Math.Abs(a.Length - b.Length) > 1) return false;
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            return d[a.Length, b.Length] == 1;
        }
    }
}
