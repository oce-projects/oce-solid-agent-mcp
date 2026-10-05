using System;
using System.IO;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// LA CHIAMATA CHE NON TORNA (voci K18 e Z-C4).
    ///
    /// ── PERCHE' SI PUO' PROVARE SENZA SOLIDWORKS ─────────────────────────
    ///
    /// Perche' il guinzaglio **non chiama niente**: riceve l'ora da fuori e
    /// risponde a una domanda sola — «e' passato troppo tempo da quando e'
    /// cominciata?». Un orologio passato come argomento e' un orologio che si
    /// puo' far mentire, e *una guardia che non si puo' far scattare non si
    /// puo' credere*.
    ///
    /// ── E LA META' CHE QUESTE PROVE NON COPRONO ──────────────────────────
    ///
    /// Che il guardiano del banco stampi e muoia davvero, con una chiamata COM
    /// piantata vera, queste prove **non lo dicono**: leggono il sorgente e
    /// verificano che sia cablato. La prova vera e' il giorno in cui SOLIDWORKS
    /// si pianta di nuovo — e a quel punto o si legge il referto, o non si e'
    /// fatto niente.
    /// </summary>
    internal static class LeashTests
    {
        internal static void All()
        {
            Group("la chiamata che non torna (K18)");

            var t0 = new DateTime(2026, 9, 23, 15, 36, 0);
            var g = new Shared.Leash(90000);

            // ── PRIMA: TACE QUANDO NON C'E' NIENTE DA DIRE ───────────────
            //
            // La meta' che di solito non si prova. Un guardiano che urla fra
            // una prova e l'altra viene spento il giorno dopo.
            Check("senza nessuna chiamata in corso, tace",
                  g.Hanging(t0.AddHours(3)) == null,
                  "tre ore di silenzio, nessun allarme");

            // ── UNA CHIAMATA NORMALE NON LO FA SCATTARE ──────────────────
            g.Begin("extrude_rectangle", t0);
            Check("una chiamata di 30 secondi non e' appesa",
                  g.Hanging(t0.AddSeconds(30)) == null,
                  "soglia 90 s");
            Check("e nemmeno una di 89",
                  g.Hanging(t0.AddSeconds(89)) == null, "");

            // ── A NOVANTA SCATTA ─────────────────────────────────────────
            var report = g.Hanging(t0.AddSeconds(90));
            Check("a novanta secondi scatta", report != null,
                  report == null ? "(niente)"
                                  : report.Substring(0, Math.Min(90, report.Length)));

            // E IL REFERTO DICE QUALE, che e' tutto il punto: il 23/9/2026 il
            // giro e' rimasto appeso e nessuno sapeva dentro cosa.
            Check("e il referto NOMINA la chiamata",
                  report != null && report.Contains("extrude_rectangle"),
                  "senza il nome sarebbe un allarme senza indirizzo");

            Check("e dice da quanto, coi secondi",
                  report != null && report.Contains("90"),
                  "«e' partita 90 secondi fa»");

            // E NON PROMETTE CIO' CHE NON PUO' FARE. Un messaggio che dicesse
            // «riprovo» o «annullo» sarebbe falso: una chiamata COM piantata
            // non si annulla.
            // SENZA IGNORARE LE MAIUSCOLE questa riga e' nata rossa: il referto
            // dice «**N**on si puo' annullare» perche' li' comincia una frase.
            // E' la terza volta che un confronto fra stringhe cade cosi' in
            // questo repository — si aggiusta il CONFRONTO, non il messaggio.
            Check("e non promette di annullarla",
                  report != null &&
                  report.IndexOf("non si puo' annullare",
                                  StringComparison.OrdinalIgnoreCase) >= 0,
                  "dice quale era, e si ferma");

            // ── QUANDO TORNA, IL GUARDIANO DEVE TACERE ───────────────────
            //
            // Senza questo, la prima pausa fra due prove diventerebbe un
            // allarme e il giro morirebbe da solo a meta'.
            g.Finished();
            Check("finita la chiamata, torna a tacere",
                  g.Hanging(t0.AddHours(1)) == null,
                  "un'ora dopo, nessun allarme");

            // ── UNA SOGLIA RIDICOLA VIENE RIFIUTATA ──────────────────────
            bool refused = false;
            try { new Shared.Leash(500); }
            catch (ArgumentOutOfRangeException) { refused = true; }
            Check("mezzo secondo non e' un guinzaglio, e viene rifiutato",
                  refused, "qualunque prova vera lo farebbe scattare");

            // ── E IL BANCO LO USA DAVVERO ────────────────────────────────
            TheBenchUsesIt();
        }

        private static void TheBenchUsesIt()
        {
            var file = Path.Combine(Root(), "Bench", "Program.cs");
            if (!File.Exists(file))
            {
                Check("il sorgente del banco c'e'", false, "manca " + file);
                return;
            }
            var p = File.ReadAllText(file);

            Check("il banco costruisce il guinzaglio a 90 secondi",
                  p.Contains("new Shared.Leash(90000)"), "Bench/Program.cs");

            // UN GUARDIANO DEFINITO E MAI ACCESO e' la malattia che questo
            // repository insegue, applicata a se stesso.
            Check("e lo ACCENDE",
                  p.Contains("TurnOnGuardian();"),
                  "definirlo e non chiamarlo sarebbe un `Tested = true` che nessuno esercita");

            Check("ogni operazione passa dal guinzaglio",
                  p.Contains("Leash.Begin(operation,") &&
                  p.Contains("finally { Leash.Finished(); }"),
                  "con un `finally`: se la chiamata alza, il guinzaglio si slaccia comunque");

            // E LA PARTE NUOVA, che e' dove si e' piantato davvero.
            Check("e anche NewDocument, che e' dove si e' piantato il 23/9",
                  p.Contains("Leash.Begin(\"NewDocument"),
                  "il modello di documento letto da una condivisione di rete");

            Check("e l'uscita e' un codice suo, il 5",
                  p.Contains("System.Environment.Exit(5)"),
                  "non 1 e non 2: un giro appeso non e' un giro con prove rosse");

            TheLibraryUsesIt();
        }

        /// <summary>
        /// L'ALTRA META': il guinzaglio dentro la libreria (voce Z-C4).
        ///
        /// Stessa classe, due chiamanti, due reazioni diverse — e le due
        /// reazioni sono la cosa da guardare. Il banco e' un giro che o
        /// finisce o non serve: muore. Il ponte e' un servizio di qualcun
        /// altro: **dice** e va avanti.
        /// </summary>
        private static void TheLibraryUsesIt()
        {
            var file = Path.Combine(Root(), "Oce.Solid.Agent.Core", "PlanExecutor.cs");
            if (!File.Exists(file))
            {
                Check("il sorgente dell'esecutore c'e'", false, "manca " + file);
                return;
            }
            var p = File.ReadAllText(file);

            Check("anche la libreria ha il guinzaglio",
                  p.Contains("new Shared.Leash("),
                  "la STESSA classe del banco: due scadenze che si comportano " +
                  "diversamente sono peggio di una sola");

            Check("e ogni passo del piano ci passa",
                  p.Contains("_leash.Begin(planStep.Operation") &&
                  p.Contains("finally { _leash.Finished(); }"),
                  "col `finally`: un passo che alza non deve lasciare il guinzaglio teso");

            // ── LA RIGA CHE PROTEGGE IL PROTOCOLLO ──────────────────────
            //
            // Su stdio, stdout E' il canale JSON-RPC. Un referto stampato li'
            // non e' un fastidio: rompe la conversazione, e il client risponde
            // -32700 a una richiesta che non c'entra niente.
            Check("il referto va su stderr, MAI su stdout",
                  p.Contains("Console.Error.WriteLine(\"[ponte] \"") &&
                  !p.Contains("Console.WriteLine(report)"),
                  "stdout e' il canale JSON-RPC");

            // ── E NON UCCIDE IL PROCESSO DI QUALCUN ALTRO ───────────────
            int guardianIndex = p.IndexOf("TurnOnGuardian()", StringComparison.Ordinal);
            string body = guardianIndex < 0 ? "" : p.Substring(guardianIndex);
            Check("e non uccide il ponte, che e' un servizio di qualcun altro",
                  !body.Contains("Environment.Exit"),
                  "il banco muore, il ponte dice e va avanti");
        }
    }
}
