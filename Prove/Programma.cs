using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Prove.Contratti
{
    /// <summary>
    /// LE PROVE CHE GIRANO SU UNA MACCHINA QUALUNQUE (voce F7).
    ///
    /// ── PERCHE' ESISTONO ──────────────────────────────────────────────────
    ///
    /// Sono le uniche prove che girano **senza licenza, senza CAD, in
    /// secondi**. Il banco vero apre SOLIDWORKS e dura un quarto d'ora: va
    /// benissimo la sera prima di committare, e non serve a niente in una CI
    /// o sulla macchina di chi apre questo repository per la prima volta.
    ///
    /// E il valutatore delle attese **deve** stare qui: se e' rotto lui, tutta
    /// la suite con SOLIDWORKS diventa verde gratis.
    ///
    /// ── COSA PROVA ────────────────────────────────────────────────────────
    ///
    ///   * il **catalogo**: campi, famiglie, nessun percorso di questa macchina
    ///     dentro le descrizioni, e `Simili()` che su «asole» trova «asola»;
    ///   * `Sessione.StessoDocumento()` — la guardia contro il blocco estruso
    ///     nel pezzo di qualcun altro, che fino a oggi non aveva una prova;
    ///   * il **protocollo MCP**, parlando col ponte su stdin/stdout come
    ///     farebbe un cliente;
    ///   * il **valutatore delle attese**, contro stati finti scritti a mano.
    ///
    /// ── E SCRIVE `prove.xml` ─────────────────────────────────────────────
    ///
    /// In formato JUnit, che e' quello che ogni CI sa leggere senza che
    /// nessuno le insegni niente.
    /// </summary>
    internal static class Programma
    {
        internal static int Falliti;
        internal static readonly List<Esito> Esiti = new List<Esito>();

        internal sealed class Esito
        {
            public string Gruppo;
            public string Nome;
            public bool Ok;
            public string Dettaglio;
        }

        private static string _gruppo = "generale";

        internal static void Gruppo(string nome)
        {
            _gruppo = nome;
            Console.WriteLine();
            Console.WriteLine("=== " + nome + " ===");
        }

        internal static void Segna(string nome, bool ok, string dettaglio = "")
        {
            if (!ok) Falliti++;
            Esiti.Add(new Esito { Gruppo = _gruppo, Nome = nome, Ok = ok, Dettaglio = dettaglio });
            // UN VERDE SI ACCORCIA, UN ROSSO NO. Il dettaglio di una prova
            // rossa e' l'unica cosa che si legge davvero, e troncarlo a
            // centodieci caratteri vuol dire rilanciare per vedere il resto.
            Console.WriteLine((ok ? "  OK   " : "  NO   ") + nome.PadRight(58) +
                              (ok ? Breve(dettaglio) : dettaglio));
        }

        internal static string Breve(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 110 ? s.Substring(0, 110) + "..." : s;
        }

        /// <summary>La radice del repository, due livelli sopra `Prove\bin\`.</summary>
        internal static string Radice()
        {
            var bin = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            return Directory.GetParent(Directory.GetParent(bin).FullName).FullName;
        }

        /// <summary>
        /// `Prove/LEGGIMI.md` dice quanti controlli ci sono. Deve essere vero.
        ///
        /// Il 16/9/2026 diceva **54** mentre ne giravano **99**: un numero
        /// giusto quando fu scritto, invecchiato da solo mentre la suite
        /// cresceva. Chi legge quel file non ha modo di accorgersene — e' la
        /// sua unica fonte. E' lo stesso difetto del README che prometteva
        /// trentotto operazioni su sessanta, e la difesa e' la stessa: far
        /// leggere il documento a un controllo.
        ///
        /// SI CONTA ANCHE QUESTO CONTROLLO. Gira per ultimo, quindi quando
        /// guarda `Esiti.Count` se stesso non c'e' ancora: il numero scritto
        /// nel documento deve valere `Esiti.Count + 1`. Non e' una furbizia,
        /// e' l'unico conto che torna — e il documento deve dire il totale che
        /// l'utente legge a schermo, non quello meno uno.
        /// </summary>
        private static void IlLeggimiDiceQuantiSono()
        {
            Gruppo("il LEGGIMI delle prove");
            int veri = Esiti.Count + 1;

            var leggimi = Path.Combine(Radice(), "Prove", "LEGGIMI.md");
            if (!File.Exists(leggimi))
            {
                Segna("il LEGGIMI c'e'", false, leggimi);
                return;
            }

            string testo;
            try { testo = File.ReadAllText(leggimi); }
            catch (Exception ex) { Segna("il LEGGIMI si legge", false, ex.Message); return; }

            // Si cerca SOLO la forma «<numero> controlli»: il file parla anche
            // di secondi e di licenze, e una regola larga griderebbe su una
            // frase giusta.
            var trovati = System.Text.RegularExpressions.Regex.Matches(
                testo, @"(\d+)\s+controlli");
            if (trovati.Count == 0)
            {
                Segna("il LEGGIMI dice quanti controlli ci sono", false,
                      "non trovo nessun «N controlli»: se il numero sparisce, " +
                      "sparisce anche questo controllo");
                return;
            }

            var sbagliati = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in trovati)
                if (Convert.ToInt32(m.Groups[1].Value) != veri)
                    sbagliati.Add("«" + m.Value + "»");

            Segna("il LEGGIMI dice quanti controlli ci sono, e sono quelli veri",
                  sbagliati.Count == 0,
                  sbagliati.Count == 0
                      ? veri + " controlli"
                      : "ne girano " + veri + ", il LEGGIMI dice " +
                        string.Join(" e ", sbagliati));
        }

        private static int Main(string[] argomenti)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("Prove.Contratti — senza SOLIDWORKS, su questa macchina qualunque");
            Console.WriteLine("radice: " + Radice());

            NessunInterop();
            OgniSorgenteStaNelSuoProgetto();
            OgniVariabileDAmbienteStaNelLeggimi();
            IlLeggimiDelBancoContaIFileGiusti();
            OgniFamigliaDelCatalogoHaUnDocumentoAmmesso();
            NessunaVoceDelTodoSiChiamaComeUnAltra();
            IlRifiutoDelleQuoteDiceAncoraQuelloCheServe();
            LaLibreriaDelPonteELaSTESSADelBanco();
            ProveAttese.Tutte();
            ProveCatalogo.Tutte();
            ProveSessione.Tutte();
            ProveProtocollo.Tutte();
            ProveIlBanco.Tutte();
            IlLeggimiDiceQuantiSono();

            Console.WriteLine();
            Console.WriteLine(new string('=', 76));
            var riepilogo = Esiti.Count + " controlli, " + Falliti + " falliti";
            Console.WriteLine(riepilogo);

            // IL MINIMO E' DICHIARATO E SI CONTROLLA. Una suite che si svuota
            // — un file di prove tolto, un gruppo che esce prima — resterebbe
            // verde con tre controlli, e «0 falliti» suonerebbe uguale.
            const int MinimoDichiarato = 25;
            if (Esiti.Count < MinimoDichiarato)
            {
                Console.WriteLine("TROPPO POCHI: ne erano dichiarati almeno " + MinimoDichiarato +
                                  ". Una suite che si svuota resta verde, ed e' il modo piu' " +
                                  "silenzioso di smettere di provare.");
                Falliti++;
            }

            ScriviJUnit();
            return Falliti == 0 ? 0 : 1;
        }

        /// <summary>
        /// QUESTO PROGETTO NON REFERENZIA L'INTEROP, E LO CONTROLLA DA SOLO.
        ///
        /// E' la ragione per cui esiste: girare dove SOLIDWORKS non c'e'. Se
        /// qualcuno aggiunge un riferimento al csproj — perche' «serviva un
        /// tipo» — la promessa cade, e su questa macchina non lo noterebbe
        /// nessuno: qui SOLIDWORKS c'e', e compila lo stesso.
        ///
        /// Si guardano i riferimenti dell'assembly, non il testo del csproj: il
        /// csproj si puo' leggere in dieci modi, i riferimenti veri sono uno.
        /// </summary>
        private static void NessunInterop()
        {
            Gruppo("questo progetto gira dove SOLIDWORKS non c'e'");
            var mio = typeof(Programma).Assembly;
            var dassault = mio.GetReferencedAssemblies()
                .Where(a => a.Name.StartsWith("SolidWorks", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Name)
                .ToList();
            Segna("nessun riferimento a SolidWorks.Interop",
                  dassault.Count == 0,
                  dassault.Count == 0
                      ? mio.GetReferencedAssemblies().Length + " riferimenti, nessuno di Dassault"
                      : "trovati: " + string.Join(", ", dassault));
        }

        /// <summary>
        /// OGNI SORGENTE STA NEL SUO `.csproj`.
        ///
        /// ── IL DIFETTO CHE QUESTO CONTROLLO PRENDE ───────────────────────
        ///
        /// Un file `.cs` che non compare fra i `<Compile Include>` **non viene
        /// compilato**. Se qualcuno lo referenzia, MSBuild lo dice subito ed e'
        /// successo davvero il 16/9/2026 con `OperazioniAssieme.cs`: due errori,
        /// riparato in trenta secondi.
        ///
        /// **Il caso pericoloso e' l'altro.** Se nessuno lo referenzia ancora —
        /// una classe nuova, un'operazione appena scritta e non ancora
        /// collegata al dispatcher — **la build resta verde e il codice non
        /// esiste**. Si scrive un file, si compila, tutto passa, e quel file non
        /// e' nel programma. Nessun errore, da nessuna parte.
        ///
        /// E' il difetto che non da' errore applicato al PROGETTO invece che al
        /// pezzo, e non c'era nessun controllo che lo prendesse.
        ///
        /// ── PERCHE' STA QUI E NON IN UNO SCRIPT ──────────────────────────
        ///
        /// Uno script bisogna ricordarsi di lanciarlo, ed e' esattamente il
        /// modo in cui il gancio `pre-push` e' nato (voce W2.10): il controllo
        /// c'era, mancava che qualcuno lo eseguisse. Qui gira a ogni giro.
        /// </summary>
        private static void OgniSorgenteStaNelSuoProgetto()
        {
            Gruppo("i sorgenti stanno nei progetti");

            // cartella (relativa alla radice) -> csproj che la deve elencare.
            // I `Controlli\<attrezzo>\` hanno un csproj per attrezzo e stanno
            // fuori dalla soluzione apposta: non si controllano qui.
            var mappa = new[]
            {
                new { Dove = new[] { @"Riscontro.Disegno", @"Riscontro.Disegno\Catalogo",
                                     @"Riscontro.Disegno\Properties" },
                      Progetto = @"Riscontro.Disegno\Riscontro.Disegno.csproj" },
                new { Dove = new[] { @"Ponte", @"Strumenti", @"Mcp" },
                      Progetto = @"Oce.Solid.Agent.csproj" },
                new { Dove = new[] { @"Banco" }, Progetto = @"Banco\ProvaSaldatura.csproj" },
                new { Dove = new[] { @"Prove" }, Progetto = @"Prove\Prove.Contratti.csproj" },
                new { Dove = new[] { @"Installer" }, Progetto = @"Installer\Installa.csproj" },
            };

            int guardati = 0;
            var fuori = new List<string>();

            foreach (var m in mappa)
            {
                var csproj = Path.Combine(Radice(), m.Progetto);
                if (!File.Exists(csproj))
                {
                    Segna("il progetto «" + m.Progetto + "» c'e'", false, csproj);
                    continue;
                }
                string testo;
                try { testo = File.ReadAllText(csproj); }
                catch (Exception ex) { Segna("«" + m.Progetto + "» si legge", false, ex.Message); continue; }

                foreach (var cartella in m.Dove)
                {
                    var piena = Path.Combine(Radice(), cartella);
                    if (!Directory.Exists(piena)) continue;
                    // SOLO il primo livello: `obj\` e `bin\` sono pieni di
                    // sorgenti generati che nessun csproj elenca, e che non
                    // devono entrare nel conto.
                    foreach (var f in Directory.GetFiles(piena, "*.cs", SearchOption.TopDirectoryOnly))
                    {
                        var nome = Path.GetFileName(f);
                        // Generati dal compilatore, mai elencati a mano.
                        if (nome.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                            nome.IndexOf("AssemblyAttributes", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        guardati++;
                        // Si cerca il NOME fra virgolette: copre sia
                        // `Include="Pippo.cs"` sia `Include="Ponte\Pippo.cs"`.
                        if (testo.IndexOf(nome + "\"", StringComparison.OrdinalIgnoreCase) < 0)
                            fuori.Add(cartella + "\\" + nome + " → " + m.Progetto);
                    }
                }
            }

            Segna("ci sono sorgenti da controllare", guardati > 10, guardati + " file .cs");
            Segna("ogni sorgente e' elencato nel suo .csproj",
                  fuori.Count == 0,
                  fuori.Count == 0
                      ? "tutti e " + guardati
                      : "FUORI DAL PROGETTO (non vengono compilati): " +
                        string.Join("; ", fuori));
        }

        /// <summary>
        /// OGNI VARIABILE D'AMBIENTE CHE IL PONTE LEGGE STA NEL README.
        ///
        /// ── PERCHE' ESISTE QUESTA PROVA ──────────────────────────────────
        ///
        /// Trovate il 17/9/2026 leggendo il README per un'altra ragione:
        /// **tre variabili su otto non c'erano**, e due delle tre erano
        /// `RISCONTRO_RADICE` e `RISCONTRO_SOLA_LETTURA` — cioe' le uniche due
        /// con cui si dice al ponte dove NON deve scrivere. Chi installa legge
        /// la tabella, non i sorgenti: una difesa che c'e' nel codice e non nel
        /// README **e' una difesa che nessuno accende**, e il pezzo lo scopre
        /// riscrivendo una libreria condivisa.
        ///
        /// Non e' pignoleria documentale: e' lo stesso difetto delle altre
        /// prove di contratto qui dentro — due elenchi scritti in due posti
        /// divergono, e il momento in cui divergono nessuno lo vede.
        ///
        /// ── COSA GUARDA, E COSA NO ───────────────────────────────────────
        ///
        /// Solo i sorgenti del PONTE e della LIBRERIA: `Banco\` e `Prove\`
        /// leggono variabili che servono a chi sviluppa, non a chi installa, e
        /// pretenderle nel README vorrebbe dire riempirlo di roba che a chi
        /// legge non serve.
        ///
        /// Il nome si cerca **in tutto il README**, non solo nella tabella: una
        /// variabile spiegata per esteso in un paragrafo e' documentata. Il
        /// controllo non pretende una forma, pretende che il nome ci sia.
        /// </summary>
        private static void OgniVariabileDAmbienteStaNelLeggimi()
        {
            Gruppo("le variabili d'ambiente stanno nel README");

            var readme = Path.Combine(Radice(), "README.md");
            if (!File.Exists(readme)) { Segna("il README c'e'", false, readme); return; }
            string testo;
            try { testo = File.ReadAllText(readme); }
            catch (Exception ex) { Segna("il README si legge", false, ex.Message); return; }

            // `GetEnvironmentVariable("X")` e la scorciatoia `Variabile("X")`
            // di `Ponte\Percorsi.cs`. La seconda e' proprio quella che nascondeva
            // `RISCONTRO_RADICE` e `RISCONTRO_SOLA_LETTURA` a un grep fatto sul
            // nome lungo: **cercare una sola forma vuol dire trovarne una sola.**
            var forma = new System.Text.RegularExpressions.Regex(
                @"(?:GetEnvironmentVariable|Variabile)\(""([A-Z][A-Z_0-9]+)""\)");

            var trovate = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var cartella in new[] { "Ponte", "Strumenti", "Mcp", "Riscontro.Disegno" })
            {
                var piena = Path.Combine(Radice(), cartella);
                if (!Directory.Exists(piena)) continue;
                foreach (var f in Directory.GetFiles(piena, "*.cs", SearchOption.AllDirectories))
                {
                    if (f.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        f.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string sorgente;
                    try { sorgente = File.ReadAllText(f); } catch { continue; }
                    foreach (System.Text.RegularExpressions.Match m in forma.Matches(sorgente))
                        trovate.Add(m.Groups[1].Value);
                }
            }

            // UNA PROVA CHE NON TROVA NIENTE PASSA SEMPRE, e non prova niente.
            // Se un giorno le cartelle cambiano nome, questa riga diventa rossa
            // invece di lasciare che il controllo si spenga in silenzio.
            Segna("ci sono variabili da controllare", trovate.Count >= 6,
                  trovate.Count + ": " + string.Join(", ", trovate));

            var assenti = trovate.Where(v => testo.IndexOf(v, StringComparison.Ordinal) < 0).ToList();
            Segna("ogni variabile letta dal ponte e' nominata nel README",
                  assenti.Count == 0,
                  assenti.Count == 0
                      ? "tutte e " + trovate.Count
                      : "NEL CODICE E NON NEL README: " + string.Join(", ", assenti));
        }

        /// <summary>
        /// I NUMERI IN `Banco\LEGGIMI.md` SONO QUANTI SONO I FILE DAVVERO.
        ///
        /// ── PERCHE' ESISTE ───────────────────────────────────────────────
        ///
        /// Il LEGGIMI del banco ha una tabella «cartella / quanti casi / cosa
        /// provano». Guardata il 17/9/2026: **quattro numeri su venti erano
        /// sbagliati** — `lavorazioni` diceva 2 e ne aveva 7, `ripeti` 3 e ne
        /// aveva 6. Nessuno se n'era accorto, perche' un numero in un documento
        /// non fallisce: invecchia.
        ///
        /// E' lo stesso difetto che il README aveva sul conto delle operazioni
        /// («trentotto» quando erano sessanta), riparato allo stesso modo: non
        /// ricontando a mano — *un conteggio fatto a mano invecchia mentre lo si
        /// scrive* — ma facendolo ricontare a ogni giro.
        ///
        /// ── LA SOMMA, E NON LA RIGA ──────────────────────────────────────
        ///
        /// Una cartella puo' comparire **due volte** nella tabella, e adesso ce
        /// n'e' una: `casi/tavola/` contiene sia i piani a mano sia i casi
        /// dichiarati, ed e' elencata in tutt'e due i blocchi con due numeri
        /// diversi. Si sommano le righe della stessa cartella e si confronta la
        /// somma: 7 + 1 = 8 file. Pretendere che ogni riga da sola combaci
        /// renderebbe rossa una tabella giusta.
        /// </summary>
        private static void IlLeggimiDelBancoContaIFileGiusti()
        {
            Gruppo("il LEGGIMI del banco conta i file giusti");

            var leggimi = Path.Combine(Radice(), "Banco", "LEGGIMI.md");
            if (!File.Exists(leggimi)) { Segna("il LEGGIMI del banco c'e'", false, leggimi); return; }
            string testo;
            try { testo = File.ReadAllText(leggimi); }
            catch (Exception ex) { Segna("il LEGGIMI del banco si legge", false, ex.Message); return; }

            // `casi/<nome>/` seguito da spazi e da un numero. Le righe di
            // continuazione (la descrizione che va a capo) non hanno il numero
            // e non corrispondono: e' voluto.
            var forma = new System.Text.RegularExpressions.Regex(
                @"casi/([a-z]+)/\s+(\d+)");

            var dichiarati = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in forma.Matches(testo))
            {
                var nome = m.Groups[1].Value;
                int n = Convert.ToInt32(m.Groups[2].Value);
                dichiarati[nome] = (dichiarati.ContainsKey(nome) ? dichiarati[nome] : 0) + n;
            }

            // UNA PROVA CHE NON TROVA NIENTE PASSA SEMPRE. Se la tabella cambia
            // forma, questa riga diventa rossa invece di spegnersi in silenzio.
            Segna("ci sono cartelle da controllare", dichiarati.Count >= 15,
                  dichiarati.Count + " righe «casi/<nome>/ N»");

            var sbagliate = new List<string>();
            var assenti = new List<string>();
            foreach (var v in dichiarati)
            {
                var cartella = Path.Combine(Radice(), "Banco", "casi", v.Key);
                if (!Directory.Exists(cartella)) { assenti.Add(v.Key); continue; }
                int veri = Directory.GetFiles(cartella, "*.json", SearchOption.TopDirectoryOnly).Length;
                if (veri != v.Value)
                    sbagliate.Add(v.Key + ": dice " + v.Value + ", ne ha " + veri);
            }

            Segna("ogni cartella nominata nel LEGGIMI esiste",
                  assenti.Count == 0,
                  assenti.Count == 0 ? dichiarati.Count + " cartelle"
                                     : "nominate e non esistenti: " + string.Join(", ", assenti));
            Segna("i numeri del LEGGIMI sono quanti sono i file",
                  sbagliate.Count == 0,
                  sbagliate.Count == 0
                      ? "tutte e " + dichiarati.Count
                      : string.Join("; ", sbagliate));
        }

        /// <summary>
        /// OGNI FAMIGLIA DEL CATALOGO HA UN TIPO DI DOCUMENTO SU CUI PUO' GIRARE.
        ///
        /// ── PERCHE' ESISTE QUESTA PROVA ──────────────────────────────────
        ///
        /// Trovato il 18/9/2026 leggendo `EsecutorePiano.cs` per un'altra
        /// ragione: `ControlliPreliminari()` — che gira **prima del primo
        /// passo di ogni piano**, e per tutti e quattro gli strumenti del ponte
        /// — ammette solo `swDocPART` e, se `AncheSuTavole`, `swDocDRAWING`.
        /// Gli assiemi no. Intanto il catalogo servito ai client elencava tre
        /// operazioni di famiglia `assiemi`.
        ///
        /// **Il ponte prometteva operazioni che avrebbe rifiutato prima di
        /// cominciare, e nessuna delle 710 prove del banco poteva vederlo: il
        /// banco chiama la LIBRERIA, non il ponte.** Due misure vere di due
        /// cose diverse, e chi legge il numero grande crede che copra anche
        /// l'altra.
        ///
        /// ── COME LO GUARDA ───────────────────────────────────────────────
        ///
        /// Dal sorgente, non da un elenco scritto a mano: i tipi ammessi si
        /// leggono da `ControlliPreliminari` con una regex sui token
        /// `swDoc...`, cosi' il giorno che qualcuno ne aggiunge o ne toglie uno
        /// questa prova se ne accorge invece di restare d'accordo con se stessa.
        ///
        /// ── COSA **NON** PRENDE, e va detto ──────────────────────────────
        ///
        /// Guarda le FAMIGLIE, non le singole operazioni. `verifica_interferenze`
        /// sta in famiglia `verifiche` e vuole un assieme: questa prova la
        /// classifica come «parte» e la lascia passare. Il rimedio vero e' un
        /// campo `DocumentoRichiesto` su `OperazioneDisegno` — voce a se',
        /// perche' tocca la libreria. Finche' non c'e', **questa prova copre la
        /// famiglia e non il caso singolo**, ed e' scritto qui perche' chi la
        /// vede verde non creda che copra di piu'.
        /// </summary>
        private static void OgniFamigliaDelCatalogoHaUnDocumentoAmmesso()
        {
            Gruppo("il catalogo promette solo documenti che l'esecutore ammette");

            // ── 1. i tipi che l'esecutore ammette, letti dal suo sorgente ──
            var sorgente = Path.Combine(Radice(), "Riscontro.Disegno", "EsecutorePiano.cs");
            if (!File.Exists(sorgente))
            {
                Segna("il sorgente dell'esecutore c'e'", false, sorgente);
                return;
            }
            string codice;
            try { codice = File.ReadAllText(sorgente); }
            catch (Exception ex) { Segna("il sorgente si legge", false, ex.Message); return; }

            int da = codice.IndexOf("bool ammesso", StringComparison.Ordinal);
            if (da < 0)
            {
                // Se la riga cambia forma questa prova NON deve passare in
                // silenzio: una prova che non trova cio' che cerca e passa e'
                // peggio di nessuna prova.
                Segna("trovo la riga che decide i tipi ammessi", false,
                      "«bool ammesso» non c'e' piu' in EsecutorePiano.cs: " +
                      "la prova va riscritta, non tolta");
                return;
            }
            int a = codice.IndexOf(';', da);
            var espressione = a > da ? codice.Substring(da, a - da) : codice.Substring(da);

            var ammessi = new SortedSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(espressione, @"swDoc[A-Z]+"))
                ammessi.Add(m.Value);

            Segna("l'esecutore ammette almeno un tipo di documento", ammessi.Count >= 1,
                  string.Join(", ", ammessi));

            // ── 2. le famiglie che il catalogo SERVE ai client ─────────────
            var json = Path.Combine(Radice(), "bin", "catalogo.json");
            if (!File.Exists(json))
            {
                Segna("il catalogo c'e'", false, "non trovo " + json + " — si produce con .\\build.ps1");
                return;
            }
            Dictionary<string, object> radice;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = 40 * 1024 * 1024;
                radice = s.DeserializeObject(File.ReadAllText(json)) as Dictionary<string, object>;
            }
            catch (Exception ex) { Segna("il catalogo si legge", false, ex.Message); return; }

            var elenco = (radice != null && radice.ContainsKey("operazioni")
                ? radice["operazioni"] : null) as object[];
            if (elenco == null) { Segna("il catalogo ha «operazioni»", false, ""); return; }

            var famiglie = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var o in elenco)
            {
                var v = o as Dictionary<string, object>;
                if (v != null && v.ContainsKey("famiglia") && v["famiglia"] != null)
                    famiglie.Add(Convert.ToString(v["famiglia"]));
            }
            Segna("ci sono famiglie da controllare", famiglie.Count >= 5,
                  famiglie.Count + ": " + string.Join(", ", famiglie));

            // ── 3. su che tipo di documento ha senso ognuna ────────────────
            // Tre righe scritte a mano, e il loro compito e' proprio di NON
            // sapere tutto: una famiglia che non conoscono e' ROSSA, non verde.
            // Cosi' l'elenco non puo' invecchiare in silenzio — chi ne aggiunge
            // una deve dire su cosa gira.
            var serve = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "assiemi",      "swDocASSEMBLY" },
                { "tavola",       "swDocDRAWING"  },
                { "estrusioni",   "swDocPART"     },
                { "lavorazioni",  "swDocPART"     },
                { "saldature",    "swDocPART"     },
                { "riferimenti",  "swDocPART"     },
                { "proprieta",    "swDocPART"     },
                { "verifiche",    "swDocPART"     },
                { "esportazioni", "swDocPART"     },
                { "identita",     "swDocPART"     },
                { "documento",    "swDocPART"     },
                { "annullare",    "swDocPART"     },
            };

            var sconosciute = famiglie.Where(f => !serve.ContainsKey(f)).ToList();
            Segna("ogni famiglia del catalogo e' classificata qui dentro",
                  sconosciute.Count == 0,
                  sconosciute.Count == 0
                      ? "tutte e " + famiglie.Count
                      : "FAMIGLIA NUOVA E NON CLASSIFICATA: " + string.Join(", ", sconosciute) +
                        " — dire su che tipo di documento gira, in Prove\\Programma.cs");

            var promesse = famiglie
                .Where(f => serve.ContainsKey(f) && !ammessi.Contains(serve[f]))
                .Select(f => f + " (vuole " + serve[f] + ")")
                .ToList();

            Segna("nessuna famiglia servita e' rifiutata prima del primo passo",
                  promesse.Count == 0,
                  promesse.Count == 0
                      ? famiglie.Count + " famiglie, tutte su un tipo ammesso (" +
                        string.Join(", ", ammessi) + ")"
                      : "PROMESSE E IRRAGGIUNGIBILI: " + string.Join("; ", promesse) +
                        " — l'esecutore ammette solo " + string.Join(", ", ammessi));
        }

        /// <summary>
        /// DUE VOCI DEL TODO NON SI CHIAMANO ALLO STESSO MODO.
        ///
        /// ── PERCHE' ESISTE QUESTA PROVA ──────────────────────────────────
        ///
        /// Il 17/9/2026 sono state innestate 65 voci nuove (la «sezione Z»),
        /// chiamate `A1`, `B3`, `C3`… **Trentasette di quei nomi erano gia'
        /// usati, nello stesso file, da voci diverse — e trentatre' di quelle
        /// erano chiuse.** «Fai C3» portava a una voce verde del 14/9 che non
        /// c'entrava niente: chi ci arrivava concludeva che il lavoro era
        /// fatto, e la voce vera restava invisibile.
        ///
        /// Nessun errore da nessuna parte, che e' la firma di questa famiglia
        /// di difetti — la stessa di `40x40x3` contro `40X40X3` e di `.sldprt`
        /// contro `.SLDPRT`, applicata all'indice del lavoro invece che ai dati.
        ///
        /// ── L'ECCEZIONE, ED E' UNA CONVENZIONE VERA ──────────────────────
        ///
        /// Quando una voce viene rifatta, l'originale si tiene sotto col nome
        /// di prima e la dicitura «traccia originale, tenuta per riferimento»:
        /// li' il nome doppio e' voluto e dice qualcosa. Quindi il doppione si
        /// accetta **solo** se una delle due e' una traccia.
        ///
        /// Non si pretende che il TODO sia in un formato: si pretende che due
        /// cose diverse non si chiamino uguale.
        /// </summary>
        private static void NessunaVoceDelTodoSiChiamaComeUnAltra()
        {
            Gruppo("nel TODO due voci non si chiamano allo stesso modo");

            var todo = Path.Combine(Radice(), "TODO.md");
            if (!File.Exists(todo)) { Segna("il TODO c'e'", false, todo); return; }
            string[] righe;
            try { righe = File.ReadAllLines(todo); }
            catch (Exception ex) { Segna("il TODO si legge", false, ex.Message); return; }

            var intestazione = new System.Text.RegularExpressions.Regex(
                @"^#### \[(.)\] (\S+)\s*(.*)$");
            var traccia = new System.Text.RegularExpressions.Regex(
                @"traccia originale|progetto originale|tenut[ao] per|per riferimento|com'era scritta",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // nome -> quante volte, e quante di quelle erano tracce
            var quante = new Dictionary<string, int>(StringComparer.Ordinal);
            var tracce = new Dictionary<string, int>(StringComparer.Ordinal);
            int totali = 0;

            foreach (var r in righe)
            {
                var m = intestazione.Match(r);
                if (!m.Success) continue;
                totali++;
                var nome = m.Groups[2].Value;
                quante[nome] = (quante.ContainsKey(nome) ? quante[nome] : 0) + 1;
                if (traccia.IsMatch(m.Groups[3].Value))
                    tracce[nome] = (tracce.ContainsKey(nome) ? tracce[nome] : 0) + 1;
            }

            // UNA PROVA CHE NON TROVA NIENTE PASSA SEMPRE. Se un giorno le
            // intestazioni cambiano forma, questa riga diventa rossa invece di
            // lasciare che il controllo si spenga in silenzio.
            Segna("ci sono voci da controllare", totali >= 100, totali + " intestazioni");

            var doppie = new List<string>();
            foreach (var v in quante)
            {
                int t = tracce.ContainsKey(v.Key) ? tracce[v.Key] : 0;
                // una voce viva piu' le sue tracce: va bene. Due vive no.
                if (v.Value - t > 1)
                    doppie.Add(v.Key + " (×" + v.Value + ", di cui tracce: " + t + ")");
            }
            doppie.Sort(StringComparer.Ordinal);

            Segna("due voci diverse non si chiamano allo stesso modo",
                  doppie.Count == 0,
                  doppie.Count == 0
                      ? totali + " intestazioni, nessun nome usato due volte per due cose"
                      : "STESSO NOME, VOCI DIVERSE: " + string.Join("; ", doppie));
        }

        /// <summary>
        /// IL RIFIUTO DI `quote_dal_modello` DICE ANCORA QUELLO CHE SERVE.
        ///
        /// ── PERCHE' ESISTE, E COSA NON E' ────────────────────────────────
        ///
        /// Misurato il 18/9/2026: su un pezzo costruito dal ponte, con **sette
        /// quote tutte marcate per il disegno**, cinque strade di import ne
        /// portano **zero** — mentre nello stesso istante, sulla stessa tavola,
        /// `InsertNote` aggiunge un'annotazione. Non e' la sessione: e' l'import
        /// delle quote, e resta rotto finche' non si prova in processo (Z-E7).
        ///
        /// Finche' dura, **quel messaggio e' tutto cio' che un modello riceve
        /// quando chiede le quote**, ed e' la parte che funziona: dice quante
        /// quote ha il pezzo, **quante sono marcate** — il dato che divide in
        /// due le cause possibili — e l'elenco delle strade provate. Se qualcuno
        /// lo accorcia «perche' era lungo», non se ne accorge nessuno.
        ///
        /// ── COSA GUARDA, E COSA NON PUO' GUARDARE ────────────────────────
        ///
        /// Guarda il **sorgente**, non l'esecuzione: qui SOLIDWORKS non c'e'.
        /// Quindi prova che le frasi ci sono ancora, **non** che il rifiuto
        /// scatta e nemmeno che dice il vero. La prova di comportamento sarebbe
        /// un caso di banco con `deve_fallire`, e **oggi non si puo' scrivere**:
        /// il banco pretende una gemella VERDE con la stessa operazione (voce
        /// F5), e per `quote_dal_modello` una gemella verde non esiste, perche'
        /// l'operazione non riesce mai. La regola ha ragione — *una prova che
        /// verifica solo il rifiuto non distingue «il controllo funziona» da
        /// «non funziona niente»* — e questo controllo non finge di essere
        /// quella prova. E' meno, e lo dice.
        /// </summary>
        private static void IlRifiutoDelleQuoteDiceAncoraQuelloCheServe()
        {
            Gruppo("il rifiuto delle quote dice ancora quello che serve");

            var file = Path.Combine(Radice(), "Riscontro.Disegno", "OperazioniTavola.cs");
            if (!File.Exists(file)) { Segna("il sorgente c'e'", false, file); return; }
            string codice;
            try { codice = File.ReadAllText(file); }
            catch (Exception ex) { Segna("il sorgente si legge", false, ex.Message); return; }

            // L'ANCORA. Se sparisce, questo controllo non ha piu' un soggetto e
            // deve diventare ROSSO: una prova che non trova cio' che cerca e
            // passa e' peggio di nessuna prova.
            const string ancora = "Non e' arrivata nessuna quota";
            if (codice.IndexOf(ancora, StringComparison.Ordinal) < 0)
            {
                Segna("trovo il rifiuto di quote_dal_modello", false,
                      "«" + ancora + "» non c'e' piu' in OperazioniTavola.cs: " +
                      "se l'import e' stato riparato questa prova va RISCRITTA, non tolta");
                return;
            }
            Segna("trovo il rifiuto di quote_dal_modello", true, "");

            // Ogni pezzo, e accanto PERCHE' quel pezzo: un elenco di stringhe
            // senza il motivo e' la prima cosa che qualcuno taglia.
            var pezzi = new[]
            {
                new[] { "marcate per il disegno",
                        "e' il dato che divide in due le cause: marcate e non importate " +
                        "(guasto nell'import) contro non marcate (guasto a monte)" },
                new[] { "[marcata]",
                        "la qualifica accanto a OGNI nome, non solo il totale" },
                new[] { "[NON marcata]",
                        "senza il contrario, «marcata» non e' un'informazione" },
                new[] { "Ho provato: ",
                        "l'elenco delle strade tentate: e' cio' che rende il " +
                        "fallimento un fatto invece di un mistero" },
            };

            var mancanti = new List<string>();
            foreach (var p in pezzi)
                if (codice.IndexOf(p[0], StringComparison.Ordinal) < 0)
                    mancanti.Add("«" + p[0] + "» (" + p[1] + ")");

            Segna("il rifiuto nomina ancora quante quote sono marcate e cosa ha provato",
                  mancanti.Count == 0,
                  mancanti.Count == 0
                      ? "tutti e " + pezzi.Length + " i pezzi ci sono"
                      : "TOLTO DAL MESSAGGIO: " + string.Join("; ", mancanti));
        }

        /// <summary>
        /// LA LIBRERIA CHE CARICA IL PONTE E' LA STESSA CHE MISURA IL BANCO.
        ///
        /// ── PERCHE' ESISTE, E QUANTO E' COSTATA ──────────────────────────
        ///
        /// Ce ne sono **due copie**, e vivono in due posti:
        ///
        ///   Riscontro.Disegno\bin\Release\Riscontro.Disegno.dll   <- il banco
        ///   bin\Riscontro.Disegno.dll                             <- il ponte
        ///
        /// La seconda la mette `build.ps1`. Chi compila **il solo progetto**
        /// della libreria — che e' la cosa piu' naturale del mondo mentre si
        /// itera — aggiorna la prima e **non** la seconda.
        ///
        /// Il 18/9/2026 sono divergute di quattro versioni per nove ore.
        /// Il banco misurava 0.98.4 e il ponte girava 0.98.1: una riparazione
        /// e' stata provata tre volte dal ponte e **non e' mai stata provata**,
        /// perche' in quel binario non c'era. Il sintomo era perfetto —
        /// l'operazione «non rifiutava» — e la causa non era nel codice.
        ///
        /// *Due copie della stessa cosa in due posti divergono, e il giorno in
        /// cui divergono nessuno lo vede: si vede il sintomo, mesi dopo,
        /// travestito da difetto del codice.*
        ///
        /// ── COSA GUARDA ─────────────────────────────────────────────────
        ///
        /// Le due versioni, e basta. Non i byte: un rebuild senza modifiche
        /// cambia i byte e non cambia niente di cio' che conta.
        /// Se una delle due manca lo dice e **non passa**: una prova che non
        /// trova cio' che cerca e passa e' peggio di nessuna prova.
        /// </summary>
        private static void LaLibreriaDelPonteELaSTESSADelBanco()
        {
            Gruppo("il ponte e il banco caricano la stessa libreria");

            var delBanco = Path.Combine(Radice(), "Riscontro.Disegno", "bin", "Release",
                                        "Riscontro.Disegno.dll");
            var delPonte = Path.Combine(Radice(), "bin", "Riscontro.Disegno.dll");

            bool c1 = File.Exists(delBanco), c2 = File.Exists(delPonte);
            Segna("la libreria che misura il banco c'e'", c1, delBanco);
            Segna("la libreria che carica il ponte c'e'", c2,
                  c2 ? delPonte : delPonte + " — si produce con .\\build.ps1");
            if (!c1 || !c2) return;

            string v1 = Versione(delBanco), v2 = Versione(delPonte);
            Segna("le due versioni si leggono", v1 != null && v2 != null,
                  (v1 ?? "?") + " / " + (v2 ?? "?"));
            if (v1 == null || v2 == null) return;

            Segna("il ponte e il banco hanno la STESSA versione della libreria",
                  string.Equals(v1, v2, StringComparison.Ordinal),
                  string.Equals(v1, v2, StringComparison.Ordinal)
                      ? "tutti e due " + v1
                      : "BANCO " + v1 + " contro PONTE " + v2 +
                        " — chi ha compilato il solo progetto ha aggiornato il primo e " +
                        "non il secondo: si rilancia .\\build.ps1, che li allinea");
        }

        /// <summary>
        /// La versione di un assembly, letta senza caricarlo nel processo:
        /// caricarlo vorrebbe dire tenerselo, e questa suite gira anche dove
        /// l'interop di Dassault non c'e'.
        /// </summary>
        private static string Versione(string dll)
        {
            try { return System.Reflection.AssemblyName.GetAssemblyName(dll).Version.ToString(); }
            catch { return null; }
        }

        /// <summary>
        /// `prove.xml` in formato JUnit: e' quello che ogni CI sa leggere senza
        /// che nessuno le insegni niente.
        /// </summary>
        private static void ScriviJUnit()
        {
            try
            {
                var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prove.xml");
                var x = new StringBuilder();
                x.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                x.AppendLine("<testsuites name=\"Prove.Contratti\" tests=\"" + Esiti.Count +
                             "\" failures=\"" + Falliti + "\">");
                foreach (var g in Esiti.GroupBy(e => e.Gruppo))
                {
                    x.AppendLine("  <testsuite name=\"" + Xml(g.Key) + "\" tests=\"" + g.Count() +
                                 "\" failures=\"" + g.Count(e => !e.Ok) + "\">");
                    foreach (var e in g)
                    {
                        x.Append("    <testcase classname=\"" + Xml(g.Key) +
                                 "\" name=\"" + Xml(e.Nome) + "\"");
                        if (e.Ok) x.AppendLine(" />");
                        else
                        {
                            x.AppendLine(">");
                            x.AppendLine("      <failure message=\"" + Xml(e.Dettaglio) + "\" />");
                            x.AppendLine("    </testcase>");
                        }
                    }
                    x.AppendLine("  </testsuite>");
                }
                x.AppendLine("</testsuites>");
                File.WriteAllText(file, x.ToString(), new UTF8Encoding(false));
                Console.WriteLine("prove.xml: " + file);
            }
            catch (Exception ex)
            {
                Console.WriteLine("non ho potuto scrivere prove.xml: " + ex.Message);
            }
        }

        private static string Xml(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;")
                            .Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
