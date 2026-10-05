using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// IL REGISTRO DELLE DOMANDE SENZA RISPOSTA (voce Z-G7)
    ///
    /// Il registro esiste per rispondere a una domanda che nessuno sa
    /// rispondere a memoria: **quale operazione costruire dopo.** Le cinque
    /// del catalogo senza casi di banco le abbiamo scelte a intuito, e questo
    /// file e' il modo di smettere.
    ///
    /// ── PERCHE' SI PUO' PROVARE SENZA SOLIDWORKS ────────────────────────
    ///
    /// Perche' non ne sa niente: scrive una riga di testo quando gli dicono
    /// che un nome non c'era. E' la parte del ponte che si puo' misurare per
    /// intero sulla macchina della CI.
    ///
    /// ── LE DUE REGOLE CHE QUI SI PRETENDONO, NON SI SPERANO ─────────────
    ///
    ///  1. **Mai su stdout**, dove passa il JSON-RPC. Si legge il SORGENTE e
    ///     si pretende che la parola `Console` non ci sia. Una regola che
    ///     riguarda un canale si controlla sul canale, non sulle intenzioni.
    ///  2. **Mai un'eccezione.** Si punta il registro su un percorso
    ///     impossibile e si pretende che la chiamata torni lo stesso — e che
    ///     il motivo del silenzio sia rimasto scritto, perche' un `catch`
    ///     vuoto e un `catch` che non e' mai stato eseguito si assomigliano
    ///     troppo.
    ///
    /// ── E IL PUNTO CHE SI DIMENTICA ─────────────────────────────────────
    ///
    /// I posti in cui il ponte sa che gli e' stato chiesto qualcosa che non
    /// c'e' sono quattro. Il difetto prevedibile non e' sbagliarne uno: e'
    /// aggiungere il quinto e non registrarlo. Quindi non si conta a quattro
    /// — si pretende che **ogni** punto che chiede `_catalogo.Simili(` scriva
    /// anche nel registro, cosi' il quinto nasce gia' sorvegliato.
    /// </summary>
    internal static class RegistryTests
    {
        internal static void All()
        {
            Group("il registro delle domande senza risposta (Z-G7)");

            TheLineShape();
            TheSilenceIsWanted();
            EveryPlaceThatKnowsWritesIt();
            DoesNotTouchStdout();
        }

        // ─────────────────────────────────────────────────────────────────
        //  1. LA FORMA DELLA RIGA
        // ─────────────────────────────────────────────────────────────────

        private static void TheLineShape()
        {
            var bridge = BridgeOrNotHere("la riga del registro");
            if (bridge == null) return;

            MethodInfo line;
            Type typ;
            try
            {
                var asm = Load.Bridge(bridge);
                typ = Load.Class(asm, "QuestionRegistry");
                if (typ == null)
                { Check("la classe QuestionRegistry c'e'", false, ""); return; }
                line = typ.GetMethod("Line",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (line == null) { Check("Line() c'e'", false, ""); return; }
            }
            catch (Exception ex)
            { Check("il ponte si carica", false, ex.ToString()); return; }

            var timestamp = new DateTime(2026, 9, 23, 21, 30, 0);
            Func<string, string, string[], string> doIt = (t, n, s) =>
                (string)line.Invoke(null, new object[] { timestamp, t, n, s });

            // ── una riga normale ──────────────────────────────────────────
            var normal = doIt("operation", "ripiega_lamiera",
                              new[] { "piega_lamiera" });
            Check("la riga porta quando, tipo e nome",
                  normal.Contains("\"timestamp\":\"2026-09-23T21:30:00\"") &&
                  normal.Contains("\"kind\":\"operation\"") &&
                  normal.Contains("\"name\":\"ripiega_lamiera\""),
                  normal);
            Check("e i nomi vicini, che sono la meta' utile della misura",
                  normal.Contains("piega_lamiera") && normal.Contains("similar"),
                  "distingue «ha sbagliato a scrivere» da «chiede una cosa che non c'e'»");
            Check("sta su UNA riga sola, che e' cio' che JSONL vuol dire",
                  !normal.Contains("\n") && !normal.Contains("\r"),
                  normal.Length + " caratteri, nessun a capo");

            // ── il nome che un modello puo' davvero inventarsi ────────────
            //
            // Non e' un caso di fantasia: il nome lo scrive un modello, e
            // basta una virgoletta per rendere illeggibile il file DA LI' IN
            // POI -- non la riga, il file. Per questo si serializza con lo
            // stesso `Protocol.InJson` del resto del ponte invece di
            // incollare virgolette a mano.
            var bad = doIt("operation", "taglia\"a\"meta" + "\n" + "riga2", null);
            Check("un nome con le virgolette non spezza la riga",
                  !bad.Contains("\n") && !bad.Contains("\r"),
                  bad);
            Check("e si rilegge come JSON, non come testo rotto",
                  ReadsBack(bad, "taglia\"a\"meta" + "\n" + "riga2"),
                  "il nome torna indietro identico, a capo compreso");

            // ── `similar` c'e' solo quando c'e' qualcosa ───────────────────
            var without = doIt("strumento", "guarda_la_tavola", null);
            Check("senza nomi vicini il campo «similar» non compare",
                  !without.Contains("similar"),
                  "un campo vuoto in ogni riga e' rumore che poi qualcuno conta");
            var empty = doIt("strumento", "guarda_la_tavola", new string[0]);
            Check("e nemmeno con un elenco vuoto",
                  !empty.Contains("similar"),
                  "zero e assente sono la stessa cosa, e si scrivono uguale");
        }

        /// <summary>
        /// La riga si rilegge come JSON e il nome torna indietro identico?
        ///
        /// Non si usa un parser scritto qui: si chiede a chi legge il JSON nel
        /// ponte, perche' la domanda vera e' *«il ponte si rilegge?»* e non
        /// *«assomiglia a JSON?»*.
        /// </summary>
        private static bool ReadsBack(string line, string expectedName)
        {
            try
            {
                var bridge = Path.Combine(Root(), "bin", "oce-solid-agent-mcp.exe");
                var asm = Load.Bridge(bridge);
                var prot = Load.Class(asm, "Protocol");
                if (prot == null) return false;
                var fromJson = prot.GetMethod("FromJson",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (fromJson == null) return false;
                var d = fromJson.Invoke(null, new object[] { line })
                        as Dictionary<string, object>;
                object n;
                return d != null && d.TryGetValue("name", out n) &&
                       Convert.ToString(n) == expectedName;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────────────────
        //  2. IL SILENZIO E' VOLUTO, E SI VEDE
        // ─────────────────────────────────────────────────────────────────

        private static void TheSilenceIsWanted()
        {
            var bridge = BridgeOrNotHere("il registro che non puo' scrivere");
            if (bridge == null) return;

            Type typ;
            try
            {
                var asm = Load.Bridge(bridge);
                typ = Load.Class(asm, "QuestionRegistry");
                if (typ == null) return;   // gia' detto sopra
            }
            catch { return; }

            var constructor = typ.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(string) }, null);
            if (constructor == null)
            { Check("il registro si costruisce con un percorso", false, ""); return; }

            Func<object, string, string, object> annotate = (reg, method, itemName) =>
            {
                var m = ReflectionLog.Global.Method(typ, method,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return m.Invoke(reg, method == "OperationThatDoesNotExist"
                    ? new object[] { itemName, null }
                    : new object[] { itemName });
            };
            Func<object, string, object> read = (reg, prop) =>
                ReflectionLog.Global.Property(typ, prop,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(reg);

            // ── SCRIVE DAVVERO ────────────────────────────────────────────
            //
            // Prima di provare che tace quando deve, si prova che parla
            // quando puo'. Un registro che tace sempre passerebbe ogni prova
            // sul silenzio.
            var file = Path.Combine(Path.GetTempPath(),
                                    "registro-prova-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                var good = constructor.Invoke(new object[] { file });
                annotate(good, "OperationThatDoesNotExist", "fresa_a_candela");
                annotate(good, "ToolThatDoesNotExist", "apri_la_tavola");
                var lines = File.Exists(file) ? File.ReadAllLines(file) : new string[0];
                Check("scrive una riga per domanda",
                      lines.Length == 2, lines.Length + " righe");
                Check("e le scrive in aggiunta, non sovrascrivendo",
                      lines.Length == 2 &&
                      lines[0].Contains("fresa_a_candela") &&
                      lines[1].Contains("apri_la_tavola"),
                      "la prima resta quando arriva la seconda");
                Check("e dice di averle scritte",
                      Convert.ToInt32(read(good, "Written")) == 2 &&
                      read(good, "WhyItWasSilent") == null,
                      "Written=2, nessun motivo di silenzio");
            }
            finally { try { File.Delete(file); } catch { } }

            // ── IL NOME VUOTO NON E' UNA DOMANDA ──────────────────────────
            var file2 = Path.Combine(Path.GetTempPath(),
                                     "registro-prova-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                var reg = constructor.Invoke(new object[] { file2 });
                annotate(reg, "OperationThatDoesNotExist", "   ");
                Check("un nome vuoto non finisce nel registro",
                      !File.Exists(file2) &&
                      Convert.ToInt32(read(reg, "Written")) == 0,
                      "e' un piano malfatto, non una cosa che qualcuno voleva");
            }
            finally { try { File.Delete(file2); } catch { } }

            // ── NON PUO' SCRIVERE: TACE, E NON SOLLEVA ────────────────────
            //
            // Il percorso impossibile e' un file VERO usato come cartella:
            // succede davvero, e non dipende dai permessi della macchina che
            // esegue la prova -- che su un runner sono diversi da qui.
            var obstacle = Path.GetTempFileName();
            try
            {
                var impossible = Path.Combine(obstacle, "dentro-un-file.jsonl");
                var reg = constructor.Invoke(new object[] { impossible });
                bool hasRaised = false;
                try { annotate(reg, "OperationThatDoesNotExist", "fresa_a_candela"); }
                catch { hasRaised = true; }
                Check("se non puo' scrivere NON solleva",
                      !hasRaised,
                      "un registro che fa fallire la chiamata che osserva e' " +
                      "peggio di nessun registro");
                Check("e il motivo del silenzio resta scritto",
                      !hasRaised && read(reg, "WhyItWasSilent") != null &&
                      Convert.ToInt32(read(reg, "Written")) == 0,
                      Convert.ToString(read(reg, "WhyItWasSilent")));
            }
            finally { try { File.Delete(obstacle); } catch { } }

            // ── NESSUN FILE SU CUI SCRIVERE ───────────────────────────────
            var withoutFile = constructor.Invoke(new object[] { null });
            bool raised2 = false;
            try { annotate(withoutFile, "ToolThatDoesNotExist", "x_qualunque"); }
            catch { raised2 = true; }
            Check("e senza nessun file su cui scrivere, idem",
                  !raised2 && read(withoutFile, "WhyItWasSilent") != null,
                  "RISCONTRO_REGISTRO vuota vuol dire «non registrare», " +
                  "e non deve diventare «registra accanto all'eseguibile»");
        }

        // ─────────────────────────────────────────────────────────────────
        //  3. OGNI PUNTO CHE SA, SCRIVE
        // ─────────────────────────────────────────────────────────────────

        private static void EveryPlaceThatKnowsWritesIt()
        {
            var source = Path.Combine(Root(), "Tools", "McpTools.cs");
            if (!File.Exists(source))
            { Check("McpTools.cs si legge", false, source); return; }

            var lines = File.ReadAllLines(source);
            var discovered = new List<int>();
            int monitored = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf("_catalog.Similar(", StringComparison.Ordinal) < 0)
                    continue;
                // La scrittura sta subito accanto: tre righe bastano, e non
                // cosi' tante da prendere quella del punto successivo.
                var window = string.Join("\n",
                    lines.Skip(i).Take(4));
                if (window.IndexOf("_registry.OperationThatDoesNotExist",
                                     StringComparison.Ordinal) >= 0) monitored++;
                else discovered.Add(i + 1);
            }

            Check("ogni punto che cerca un nome vicino scrive anche nel registro",
                  discovered.Count == 0,
                  discovered.Count == 0
                      ? monitored + " punti, tutti sorvegliati"
                      : "scoperti alle righe " + string.Join(", ", discovered));
            Check("e i punti sono piu' di uno, se no la regola sorveglia il vuoto",
                  monitored >= 3,
                  monitored + " punti che chiedono `_catalog.Similar(`");

            var everything = string.Join("\n", lines);
            Check("e lo strumento che non esiste finisce nel registro",
                  everything.IndexOf("_registry.ToolThatDoesNotExist",
                                StringComparison.Ordinal) >= 0,
                  "e' il punto che sapeva e taceva: il `default` di Invoke");

            // ── LA REGOLA SI FA FALLIRE APPOSTA ───────────────────────────
            //
            // Su righe finte, perche' far fallire quella vera vorrebbe dire
            // togliere una chiamata dal ponte.
            var fakeBroken = new[]
            {
                "                var similar = _catalog.Similar(itemName);",
                "                trouble.Add(location + \": non esiste\");",
            };
            var fakeHealthy = new[]
            {
                "                var similar = _catalog.Similar(itemName);",
                "                _registry.OperationThatDoesNotExist(itemName, similar);",
                "                trouble.Add(location + \": non esiste\");",
            };
            Check("la regola prende un punto che NON scrive",
                  Discovered(fakeBroken) == 1,
                  "una riga che cerca i nomi vicini e non registra niente");
            Check("e lascia stare quello che scrive",
                  Discovered(fakeHealthy) == 0,
                  "provata in tutt'e due i versi, su righe finte");
        }

        private static int Discovered(string[] lines)
        {
            int howMany = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf("_catalog.Similar(", StringComparison.Ordinal) < 0)
                    continue;
                var window = string.Join("\n", lines.Skip(i).Take(4));
                if (window.IndexOf("_registry.OperationThatDoesNotExist",
                                     StringComparison.Ordinal) < 0) howMany++;
            }
            return howMany;
        }

        // ─────────────────────────────────────────────────────────────────
        //  4. MAI SU STDOUT
        // ─────────────────────────────────────────────────────────────────

        private static void DoesNotTouchStdout()
        {
            var source = Path.Combine(Root(), "Tools", "QuestionRegistry.cs");
            if (!File.Exists(source))
            { Check("QuestionRegistry.cs si legge", false, source); return; }

            // Si guarda il CODICE, non i commenti: la spiegazione della regola
            // nomina `Console` apposta, e un controllo che punisse la propria
            // documentazione si farebbe cancellare la documentazione.
            var code = WithoutComments(File.ReadAllText(source));
            var foundItems = Regex.Matches(code, @"\bConsole\s*\.").Count;

            Check("il registro non nomina Console da nessuna parte",
                  foundItems == 0,
                  foundItems == 0
                      ? "li' passa il JSON-RPC: una riga di troppo e' il protocollo rotto"
                      : foundItems + " occorrenze");
            Check("e la regola guarda il codice, non i commenti che la spiegano",
                  File.ReadAllText(source).IndexOf("Console", StringComparison.Ordinal) >= 0,
                  "il cappello nomina Console per dire di non usarlo: se questa " +
                  "riga diventasse rossa vorrebbe dire che la spiegazione e' sparita");
        }

        /// <summary>
        /// Via i commenti di riga e di blocco. Non e' un parser di C# e non
        /// deve esserlo: qui serve solo a non leggere la prosa che spiega la
        /// regola come se fosse una violazione della regola.
        /// </summary>
        private static string WithoutComments(string text)
        {
            text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"^\s*///.*$", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*//.*$", "", RegexOptions.Multiline);
            return text;
        }
    }
}
