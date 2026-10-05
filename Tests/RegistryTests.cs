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
    /// THE REGISTER OF THE QUESTIONS WITHOUT AN ANSWER (entry Z-G7)
    ///
    /// The register exists to answer a question that nobody can answer
    /// from memory: **which operation to build next.** The five
    /// of the catalogue without bench cases we chose by intuition, and this
    /// file is the way to stop doing so.
    ///
    /// ── WHY IT CAN BE TESTED WITHOUT SOLIDWORKS ──────────────────────────
    ///
    /// Because it knows nothing about it: it writes a line of text when it is told
    /// that a name was not there. It is the part of the bridge that can be measured
    /// in full on the CI machine.
    ///
    /// ── THE TWO RULES THAT HERE ARE DEMANDED, NOT HOPED FOR ──────────────
    ///
    ///  1. **Never on stdout**, where the JSON-RPC passes. The SOURCE is read and
    ///     it is demanded that the word `Console` not be there. A rule that
    ///     concerns a channel is checked on the channel, not on intentions.
    ///  2. **Never an exception.** The register is pointed at an impossible
    ///     path and it is demanded that the call come back all the same — and that
    ///     the reason for the silence has stayed written, because an empty
    ///     `catch` and a `catch` that has never been executed look too much
    ///     alike.
    ///
    /// ── AND THE POINT THAT GETS FORGOTTEN ────────────────────────────────
    ///
    /// The places where the bridge knows it was asked for something that is not
    /// there are four. The foreseeable defect is not getting one wrong: it is
    /// adding the fifth and not registering it. So we do not count to four
    /// — it is demanded that **every** point that asks `_catalogo.Simili(` also
    /// write in the register, so that the fifth is born already watched.
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
        //  1. THE SHAPE OF THE LINE
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

            // ── a normal line ────────────────────────────────────────────
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

            // ── the name that a model can really invent ──────────────────
            //
            // It is not a fanciful case: the name is written by a model, and
            // one quotation mark is enough to make the file unreadable FROM THEN ON
            // -- not the line, the file. That is why it is serialized with the
            // same `Protocol.InJson` as the rest of the bridge instead of
            // pasting quotation marks by hand.
            var bad = doIt("operation", "taglia\"a\"meta" + "\n" + "riga2", null);
            Check("un nome con le virgolette non spezza la riga",
                  !bad.Contains("\n") && !bad.Contains("\r"),
                  bad);
            Check("e si rilegge come JSON, non come testo rotto",
                  ReadsBack(bad, "taglia\"a\"meta" + "\n" + "riga2"),
                  "il nome torna indietro identico, a capo compreso");

            // ── `similar` is there only when there is something ─────────────
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
        /// Is the line read back as JSON and does the name come back identical?
        ///
        /// A parser written here is not used: the one that reads JSON in the
        /// bridge is asked, because the real question is *«can the bridge read it back?»* and not
        /// *«does it look like JSON?»*.
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
        //  2. THE SILENCE IS INTENDED, AND IT SHOWS
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
                if (typ == null) return;   // already said above
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

            // ── IT REALLY WRITES ──────────────────────────────────────────
            //
            // Before proving that it is silent when it must be, it is proved that it speaks
            // when it can. A register that is always silent would pass every check
            // on silence.
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

            // ── THE EMPTY NAME IS NOT A QUESTION ──────────────────────────
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

            // ── IT CANNOT WRITE: IT STAYS SILENT, AND DOES NOT RAISE ──────
            //
            // The impossible path is a REAL file used as a folder:
            // it really happens, and it does not depend on the permissions of the machine that
            // runs the check -- which on a runner are different from here.
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

            // ── NO FILE TO WRITE ON ───────────────────────────────────────
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
        //  3. EVERY POINT THAT KNOWS, WRITES
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
                // The write is right next to it: three lines are enough, and not
                // so many as to catch the one of the next point.
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

            // ── THE RULE IS MADE TO FAIL ON PURPOSE ───────────────────────
            //
            // On fake lines, because making the real one fail would mean
            // removing a call from the bridge.
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

            // The CODE is looked at, not the comments: the explanation of the rule
            // names `Console` on purpose, and a check that punished its own
            // documentation would get its documentation deleted.
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
        /// Away with the line and block comments. It is not a C# parser and must not
        /// be: here it serves only not to read the prose that explains the
        /// rule as if it were a violation of the rule.
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
