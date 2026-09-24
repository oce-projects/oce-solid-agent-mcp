using System;
using System.IO;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// QUANTE VOLTE IL PIANO CHIEDE «QUAL E' IL DOCUMENTO ATTIVO?» (voce Z-C5).
    ///
    /// ── PERCHE' IL NUMERO E' IL CONTROLLO ────────────────────────────────
    ///
    /// `_sw.IActiveDoc2` e' un globale, e la risposta cambia da sola: basta che
    /// qualcuno clicchi su un'altra finestra di SOLIDWORKS. **Due letture dello
    /// stesso globale non sono una lettura fatta due volte: sono due domande
    /// diverse**, e fra l'una e l'altra c'e' una finestra in cui la risposta
    /// puo' cambiare.
    ///
    /// Il 18/9/2026 la voce Z-C5 le ha contate a mano — sei, e una sola seguita
    /// dal confronto — e quel conto e' rimasto in un file di testo, dove
    /// invecchia da solo. Qui lo fa il giro, ogni volta, in un decimo di
    /// secondo e senza SOLIDWORKS.
    ///
    /// **Non e' un divieto: e' un tetto.** Una lettura in piu' non e'
    /// necessariamente sbagliata — ma dev'essere una decisione, non una svista,
    /// e questo controllo costringe chi la aggiunge a passare di qui e
    /// aggiornare il numero sapendo cosa sta aggiornando.
    /// </summary>
    internal static class ProveIlDocumento
    {
        /// <summary>
        /// Quante letture di `_sw.IActiveDoc2` ci sono in `EsecutorePiano.cs`,
        /// senza contare i commenti.
        ///
        /// Cinque, dal 23/9/2026 (erano sei):
        ///
        ///   riga 195  prende `documentoAtteso` — E' LA LETTURA BUONA
        ///   riga 274  il documento su cui si SCRIVE, confrontato subito dopo
        ///   riga 296  lo stato mandato alla fonte del piano (sola lettura)
        ///   riga 335  ci apre sopra la voce di annullamento
        ///   riga 444  il confronto «e' ancora quello?»
        ///
        /// Quella tolta era in `ControlliPreliminari`, che adesso **riceve** il
        /// documento invece di ripescarlo.
        /// </summary>
        private const int Tetto = 5;

        internal static void Tutte()
        {
            Gruppo("il documento si tiene per mano (Z-C5)");

            string file = Path.Combine(Radice(), "Riscontro.Disegno", "EsecutorePiano.cs");
            if (!File.Exists(file))
            {
                Segna("EsecutorePiano.cs c'e'", false, "manca " + file);
                return;
            }

            var righe = File.ReadAllLines(file);
            int quante = 0;
            var dove = new System.Collections.Generic.List<string>();
            for (int i = 0; i < righe.Length; i++)
            {
                var r = righe[i];
                // I COMMENTI PARLANO DI QUESTE LETTURE, e sono tanti: contarli
                // vorrebbe dire che spiegare il problema lo peggiora.
                var senzaSpazi = r.TrimStart();
                if (senzaSpazi.StartsWith("//", StringComparison.Ordinal) ||
                    senzaSpazi.StartsWith("///", StringComparison.Ordinal)) continue;
                if (r.Contains("_sw.IActiveDoc2")) { quante++; dove.Add((i + 1).ToString()); }
            }

            Segna("le letture del documento attivo non sono aumentate",
                  quante <= Tetto,
                  quante + " letture (tetto " + Tetto + "), alle righe " +
                  string.Join(", ", dove));

            // E IL TETTO NON DEV'ESSERE PIU' LARGO DEL VERO. Un tetto messo
            // alto «per stare tranquilli» non e' un tetto: e' un numero che non
            // ha mai detto di no, e il giorno in cui servirebbe non lo dira'.
            Segna("il tetto e' quello vero, non uno comodo",
                  quante == Tetto,
                  quante == Tetto
                      ? "cinque, e sono le cinque elencate nel commento"
                      : "ne sono rimaste " + quante + ": se e' voluto, abbassa il tetto");

            // ── E CHI DECIDE NON SE LO RIPESCA ──────────────────────────
            //
            // Il difetto vero non era il numero: era che `ControlliPreliminari`
            // leggeva il globale per conto suo e poi il piano lo rileggeva.
            // Approvavano due documenti diversi senza che niente lo dicesse.
            string testo = File.ReadAllText(file);
            Segna("i controlli preliminari RICEVONO il documento",
                  Regex.IsMatch(testo,
                      @"private string ControlliPreliminari\(\s*IModelDoc2 \w+\s*\)"),
                  "non `ControlliPreliminari()`");

            // E che li riceva non basta se poi se lo ripesca lo stesso: si
            // guarda il corpo del metodo, non solo la firma.
            int inizio = testo.IndexOf("private string ControlliPreliminari(",
                                       StringComparison.Ordinal);
            string corpo = inizio < 0 ? "" : testo.Substring(inizio);
            Segna("e dentro non se lo ripesca",
                  inizio >= 0 && !CorpoLegge(corpo),
                  "nessun `_sw.IActiveDoc2` nel corpo di ControlliPreliminari");
        }

        /// <summary>Il corpo del metodo, fino alla chiusura della graffa —
        /// contata, non indovinata da un'indentazione.</summary>
        private static bool CorpoLegge(string daQui)
        {
            int graffe = 0;
            bool cominciato = false;
            for (int i = 0; i < daQui.Length; i++)
            {
                char c = daQui[i];
                if (c == '{') { graffe++; cominciato = true; }
                else if (c == '}')
                {
                    graffe--;
                    if (cominciato && graffe == 0)
                        return daQui.Substring(0, i).Contains("_sw.IActiveDoc2");
                }
            }
            return daQui.Contains("_sw.IActiveDoc2");
        }
    }
}
