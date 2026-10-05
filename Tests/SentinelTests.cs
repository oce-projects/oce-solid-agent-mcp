using System;
using System.IO;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE GUARD THAT NOBODY COULD EVER SEE TRIGGER (entry K17).
    ///
    /// `SolidWorksSentinel` decides when the bench must stop trying
    /// because SOLIDWORKS is no longer there. The condition that switches it on — three
    /// new documents that are not born in a row — in real life happens after
    /// hours of running and cannot be reproduced on demand: it is exactly the kind of
    /// check that is written, believed, and found broken on the day it
    /// would be needed.
    ///
    /// Here SOLIDWORKS is not there at all, and for this very reason it can be made to
    /// fail on purpose: the sentinel does not touch the CAD, it just counts. Every
    /// run of `Oce.Solid.Agent.ContractTests` makes it trigger, and makes it NOT trigger when
    /// it must not — which is the half that usually nobody tests.
    /// </summary>
    internal static class SentinelTests
    {
        internal static void All()
        {
            Group("la sentinella: quando SOLIDWORKS se n'e' andato");

            // ── TWO FAILURES ARE NOT ENOUGH ───────────────────────────────
            //
            // The half that is usually not tested. A guard that triggered
            // on the first failure would stop a whole run for a document
            // template momentarily busy — and at that point it would be
            // removed, and that is worse than not having it.
            var s1 = new Shared.SolidWorksSentinel(3);
            s1.Failed();
            s1.Failed();
            Check("due fallimenti di fila non bastano", !s1.HasGone,
                  "di fila: " + s1.InARow + " su soglia " + s1.Threshold);

            // ── THREE YES ─────────────────────────────────────────────────
            bool fired = s1.Failed();
            Check("al terzo la sentinella scatta", fired && s1.HasGone,
                  "di fila: " + s1.InARow);

            // ── AND IT DOES NOT GO BACK ───────────────────────────────────
            //
            // After the program is dead, a document that is born by chance
            // does not resurrect it: from there on the run is no longer valid
            // anyway, because in between there are checks not done.
            s1.Succeeded();
            Check("una volta scattata non si spegne", s1.HasGone,
                  "dopo Succeeded(): " + s1.HasGone);

            // ── «IN A ROW» IS THE WORD THAT COUNTS ───────────────────────
            //
            // Three failures SCATTERED over eight hundred checks are normal
            // noise: a case that leaves SOLIDWORKS crooked and the next one
            // that recovers. If the sentinel added them up, it would stop healthy
            // runs — and the first person to see it stop a healthy run switches it off.
            var s2 = new Shared.SolidWorksSentinel(3);
            s2.Failed(); s2.Succeeded();
            s2.Failed(); s2.Succeeded();
            s2.Failed(); s2.Succeeded();
            s2.Failed();
            Check("tre fallimenti SPARSI non la fanno scattare", !s2.HasGone,
                  "quattro fallimenti in tutto, mai due di fila: di fila = " + s2.InARow);

            // ── THE THRESHOLD IS A PARAMETER, NOT THE NUMBER THREE ───────
            var s3 = new Shared.SolidWorksSentinel(1);
            s3.Failed();
            Check("con soglia 1 scatta al primo", s3.HasGone, "soglia " + s3.Threshold);

            bool refused = false;
            try { new Shared.SolidWorksSentinel(0); }
            catch (ArgumentOutOfRangeException) { refused = true; }
            Check("una soglia di zero viene rifiutata", refused,
                  "scatterebbe prima di guardare");

            // ── THE REPORT CARRIES THE NUMBERS ────────────────────────────
            //
            // «SOLIDWORKS is not responding» without numbers cannot be told from
            // an opinion. Whoever reads the report must be able to see how many
            // attempts and on which threshold, without opening the code.
            // NOT «contains a 3»: it contains «3 tentativi». The first version
            // of this line looked for the digit and passed while the report
            // said «0 tentativi di fila andati a vuoto» — the 3 that it found
            // was the threshold, in the same sentence. See `_whenItFires`.
            string report = s1.Report();
            Check("il referto dice quanti tentativi", report.Contains("3 tentativi"), report);
            Check("il referto dice che NON sono rosse",
                  report.IndexOf("NON SONO STATE", StringComparison.Ordinal) >= 0,
                  "la differenza fra «fallita» e «non fatta» e' tutto il punto");

            // ── AND THE BENCH REALLY USES IT ──────────────────────────────
            //
            // A correct sentinel that nobody consults is a correct
            // class and a broken bench. The contract checks cannot
            // launch the bench — SOLIDWORKS is needed — but they can read the
            // source and demand that the three points that create documents go
            // through it, and that the verdict have its exit.
            TheBenchConsultsIt();
        }

        private static void TheBenchConsultsIt()
        {
            string program = Path.Combine(Root(), "Bench", "Program.cs");
            string assemblyDoc = Path.Combine(Root(), "Bench", "TestAssembly.cs");
            string cases = Path.Combine(Root(), "Bench", "DeclaredCases.cs");

            foreach (var f in new[] { program, assemblyDoc, cases })
                if (!File.Exists(f))
                {
                    Check("i sorgenti del banco ci sono", false, "manca " + f);
                    return;
                }

            string p = File.ReadAllText(program);
            string a = File.ReadAllText(assemblyDoc);
            string c = File.ReadAllText(cases);

            Check("il banco costruisce la sentinella",
                  Regex.IsMatch(p, @"new Shared\.SolidWorksSentinel\(\s*3\s*\)"),
                  "soglia 3 in Bench/Program.cs");

            // THE THREE POINTS THAT CREATE DOCUMENTS, one by one. A point that does not
            // go through it is a road along which the bench keeps testing
            // against a dead program — that is, the defect, intact.
            Check("NewPart consulta la sentinella",
                  p.Contains("if (Sentinel.HasGone)") &&
                  p.Contains("Sentinel.Failed()") &&
                  p.Contains("Sentinel.Succeeded()"),
                  "Bench/Program.cs");

            Check("anche l'assieme di prova ci passa",
                  a.Contains("Program.Sentinel.HasGone") &&
                  a.Contains("Program.Sentinel.Failed()"),
                  "Bench/TestAssembly.cs: un assieme comincia da parti nuove");

            Check("i casi dichiarati si SALTANO, non falliscono",
                  c.Contains("Sentinel.HasGone") && c.Contains("Skipped("),
                  "Bench/DeclaredCases.cs: saltata non conta ne' verde ne' rossa");

            // ── AND EVEN BEFORE THE TRIGGER, RED IS THE WRONG WORD ────────
            //
            // The two cases that consume the first two failures are not red:
            // they are not measured. Before, they ended up in a `catch (Exception)` that
            // marked red whatever had happened.
            Check("una preparazione fallita non diventa rossa",
                  c.Contains("catch (Shared.PreparationFailed"),
                  "Bench/DeclaredCases.cs: errore di banco, o saltata se gia' morto");

            // AND IT IS THE TYPE, NOT THE TEXT. A `catch` that recognized the
            // preparation from the message would go back to marking red the day
            // somebody rewrites the sentence — without anything saying so.
            Check("la preparazione si riconosce dal TIPO, non dal messaggio",
                  p.Contains("throw new Shared.PreparationFailed("),
                  "Bench/Program.cs: OrSayIt e la sentinella alzano lo stesso tipo");

            // ── EXIT 4, AND IT COMES BEFORE THE OTHERS ────────────────────
            //
            // If 2 or 1 came out first, whoever reads the exit code
            // would go looking for what broke — and nothing broke: the
            // run was not done.
            int where4 = p.IndexOf("return 4;", StringComparison.Ordinal);
            int where2 = p.LastIndexOf("if (_benchErrors > 0) return 2;", StringComparison.Ordinal);
            Check("il verdetto ha l'uscita 4", where4 >= 0, "Bench/Program.cs");
            Check("la 4 viene prima della 2", where4 >= 0 && where2 >= 0 && where4 < where2,
                  "un giro non fatto non e' un giro con errori");

            // ── AND SOLIDWORKS CAN BE MADE TO DIE ON COMMAND ──────────────
            Check("c'e' un modo di vederla scattare sul banco vero",
                  p.Contains("--fingi-senza-solidworks"),
                  "senza, resterebbe una guardia creduta e mai vista");

            // ── THE DOCUMENT TEMPLATE IS NOT READ FROM THE NETWORK (K19) ──
            //
            // Eight hundred new parts per run means eight hundred readings of the
            // `.prtdot`. When that file is on a share and SOLIDWORKS
            // gets stuck while opening it, it keeps the handle and the file can no
            // longer be read even from outside: on 23/9/2026 three runs in a row died,
            // and it looked like the network.
            Check("il modello di documento si copia in locale",
                  p.Contains("LocalModel("),
                  "Bench/Program.cs");

            Check("la parte nuova nasce dalla copia locale",
                  p.Contains("LocalModel(_sw.GetUserPreferenceStringValue("),
                  "non dal percorso di rete");

            Check("e anche l'assieme di prova",
                  a.Contains("Program.LocalModel("),
                  "Bench/TestAssembly.cs");

            // AND THE COPY ANNOUNCES ITSELF. A bench that silently uses a template
            // different from that of the workstation measures something else.
            Check("la copia si dice, e anche quando non riesce",
                  p.Contains("modello copiato in locale") &&
                  p.Contains("NON ho potuto copiare il modello in locale"),
                  "tutte e due le strade parlano");

            // ── AND IF THE COPY FAILS, WE DO NOT GO BACK TO THE NETWORK FOR NOTHING ──
            //
            // The copy fails precisely when the original cannot be read:
            // falling back to the network path means getting stuck again. If
            // locally there is the copy of a previous run it is used, and it
            // says **from when it is** — an old template passed off as the current one
            // would be the same old silence.
            Check("se la copia fallisce si usa quella del giro prima",
                  p.Contains("uso la copia locale del") &&
                  p.Contains("File.GetLastWriteTime("),
                  "e la sua data si legge nel resoconto");
        }
    }
}
