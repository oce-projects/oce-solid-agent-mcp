using System;
using System.IO;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
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
    internal static class ProveIlGuinzaglio
    {
        internal static void Tutte()
        {
            Gruppo("la chiamata che non torna (K18)");

            var t0 = new DateTime(2026, 9, 23, 15, 36, 0);
            var g = new Condiviso.Guinzaglio(90000);

            // ── PRIMA: TACE QUANDO NON C'E' NIENTE DA DIRE ───────────────
            //
            // La meta' che di solito non si prova. Un guardiano che urla fra
            // una prova e l'altra viene spento il giorno dopo.
            Segna("senza nessuna chiamata in corso, tace",
                  g.Appesa(t0.AddHours(3)) == null,
                  "tre ore di silenzio, nessun allarme");

            // ── UNA CHIAMATA NORMALE NON LO FA SCATTARE ──────────────────
            g.Comincia("extrude_rectangle", t0);
            Segna("una chiamata di 30 secondi non e' appesa",
                  g.Appesa(t0.AddSeconds(30)) == null,
                  "soglia 90 s");
            Segna("e nemmeno una di 89",
                  g.Appesa(t0.AddSeconds(89)) == null, "");

            // ── A NOVANTA SCATTA ─────────────────────────────────────────
            var referto = g.Appesa(t0.AddSeconds(90));
            Segna("a novanta secondi scatta", referto != null,
                  referto == null ? "(niente)"
                                  : referto.Substring(0, Math.Min(90, referto.Length)));

            // E IL REFERTO DICE QUALE, che e' tutto il punto: il 23/9/2026 il
            // giro e' rimasto appeso e nessuno sapeva dentro cosa.
            Segna("e il referto NOMINA la chiamata",
                  referto != null && referto.Contains("extrude_rectangle"),
                  "senza il nome sarebbe un allarme senza indirizzo");

            Segna("e dice da quanto, coi secondi",
                  referto != null && referto.Contains("90"),
                  "«e' partita 90 secondi fa»");

            // E NON PROMETTE CIO' CHE NON PUO' FARE. Un messaggio che dicesse
            // «riprovo» o «annullo» sarebbe falso: una chiamata COM piantata
            // non si annulla.
            // SENZA IGNORARE LE MAIUSCOLE questa riga e' nata rossa: il referto
            // dice «**N**on si puo' annullare» perche' li' comincia una frase.
            // E' la terza volta che un confronto fra stringhe cade cosi' in
            // questo repository — si aggiusta il CONFRONTO, non il messaggio.
            Segna("e non promette di annullarla",
                  referto != null &&
                  referto.IndexOf("non si puo' annullare",
                                  StringComparison.OrdinalIgnoreCase) >= 0,
                  "dice quale era, e si ferma");

            // ── QUANDO TORNA, IL GUARDIANO DEVE TACERE ───────────────────
            //
            // Senza questo, la prima pausa fra due prove diventerebbe un
            // allarme e il giro morirebbe da solo a meta'.
            g.Finita();
            Segna("finita la chiamata, torna a tacere",
                  g.Appesa(t0.AddHours(1)) == null,
                  "un'ora dopo, nessun allarme");

            // ── UNA SOGLIA RIDICOLA VIENE RIFIUTATA ──────────────────────
            bool rifiutata = false;
            try { new Condiviso.Guinzaglio(500); }
            catch (ArgumentOutOfRangeException) { rifiutata = true; }
            Segna("mezzo secondo non e' un guinzaglio, e viene rifiutato",
                  rifiutata, "qualunque prova vera lo farebbe scattare");

            // ── E IL BANCO LO USA DAVVERO ────────────────────────────────
            IlBancoLoUsa();
        }

        private static void IlBancoLoUsa()
        {
            var file = Path.Combine(Radice(), "Banco", "Program.cs");
            if (!File.Exists(file))
            {
                Segna("il sorgente del banco c'e'", false, "manca " + file);
                return;
            }
            var p = File.ReadAllText(file);

            Segna("il banco costruisce il guinzaglio a 90 secondi",
                  p.Contains("new Condiviso.Guinzaglio(90000)"), "Banco/Program.cs");

            // UN GUARDIANO DEFINITO E MAI ACCESO e' la malattia che questo
            // repository insegue, applicata a se stesso.
            Segna("e lo ACCENDE",
                  p.Contains("AccendiIlGuardiano();"),
                  "definirlo e non chiamarlo sarebbe un `Provata = true` che nessuno esercita");

            Segna("ogni operazione passa dal guinzaglio",
                  p.Contains("Guinzaglio.Comincia(operazione,") &&
                  p.Contains("finally { Guinzaglio.Finita(); }"),
                  "con un `finally`: se la chiamata alza, il guinzaglio si slaccia comunque");

            // E LA PARTE NUOVA, che e' dove si e' piantato davvero.
            Segna("e anche NewDocument, che e' dove si e' piantato il 23/9",
                  p.Contains("Guinzaglio.Comincia(\"NewDocument"),
                  "il modello di documento letto da una condivisione di rete");

            Segna("e l'uscita e' un codice suo, il 5",
                  p.Contains("System.Environment.Exit(5)"),
                  "non 1 e non 2: un giro appeso non e' un giro con prove rosse");

            LaLibreriaLoUsa();
        }

        /// <summary>
        /// L'ALTRA META': il guinzaglio dentro la libreria (voce Z-C4).
        ///
        /// Stessa classe, due chiamanti, due reazioni diverse — e le due
        /// reazioni sono la cosa da guardare. Il banco e' un giro che o
        /// finisce o non serve: muore. Il ponte e' un servizio di qualcun
        /// altro: **dice** e va avanti.
        /// </summary>
        private static void LaLibreriaLoUsa()
        {
            var file = Path.Combine(Radice(), "Riscontro.Disegno", "EsecutorePiano.cs");
            if (!File.Exists(file))
            {
                Segna("il sorgente dell'esecutore c'e'", false, "manca " + file);
                return;
            }
            var p = File.ReadAllText(file);

            Segna("anche la libreria ha il guinzaglio",
                  p.Contains("new Condiviso.Guinzaglio("),
                  "la STESSA classe del banco: due scadenze che si comportano " +
                  "diversamente sono peggio di una sola");

            Segna("e ogni passo del piano ci passa",
                  p.Contains("_guinzaglio.Comincia(passo.Operazione") &&
                  p.Contains("finally { _guinzaglio.Finita(); }"),
                  "col `finally`: un passo che alza non deve lasciare il guinzaglio teso");

            // ── LA RIGA CHE PROTEGGE IL PROTOCOLLO ──────────────────────
            //
            // Su stdio, stdout E' il canale JSON-RPC. Un referto stampato li'
            // non e' un fastidio: rompe la conversazione, e il client risponde
            // -32700 a una richiesta che non c'entra niente.
            Segna("il referto va su stderr, MAI su stdout",
                  p.Contains("Console.Error.WriteLine(\"[ponte] \"") &&
                  !p.Contains("Console.WriteLine(referto)"),
                  "stdout e' il canale JSON-RPC");

            // ── E NON UCCIDE IL PROCESSO DI QUALCUN ALTRO ───────────────
            int doveGuardiano = p.IndexOf("AccendiIlGuardiano()", StringComparison.Ordinal);
            string corpo = doveGuardiano < 0 ? "" : p.Substring(doveGuardiano);
            Segna("e non uccide il ponte, che e' un servizio di qualcun altro",
                  !corpo.Contains("Environment.Exit"),
                  "il banco muore, il ponte dice e va avanti");
        }
    }
}
