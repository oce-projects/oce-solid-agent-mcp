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
