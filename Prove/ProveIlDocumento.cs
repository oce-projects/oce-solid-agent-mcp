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
        /// How many reads of `_sw.IActiveDoc2` `EsecutorePiano.cs` has,
        /// comments left out.
        ///
        /// Two, since finding M19 (lot Z-C21.5; five before, six before 23/9/2026):
        ///
        ///   documentoAtteso    taken ONCE at the start: THE GOOD READ
        ///   modello            the document a step writes on, compared with
        ///                      documentoAtteso right after («still the same?»)
        ///
        /// The three M19 removed: the undo recording was STARTED on a fresh read
        /// (and finished on documentoAtteso, two documents whenever the window
        /// changed); the state sent to the plan source was read from the active
        /// document; and the closing of the recording waited for the active
        /// document to be documentoAtteso. All three now use the document held.
        /// The one removed on 23/9/2026 was in `ControlliPreliminari`, which now
        /// RECEIVES the document.
        /// </summary>
        private const int Tetto = 2;

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
                      ? quante + ", the ones listed in the comment"
                      : quante + " left: if that is meant, lower the ceiling");

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

            UndoOnTheDocumentHeld(righe);
        }

        /// <summary>
        /// THE UNDO RECORDING AND THE STATE GO WITH THE DOCUMENT HELD (finding
        /// M19, lot Z-C21.5). On library 0.145.0 the recording was started on
        /// a fresh read of the active document and finished on documentoAtteso,
        /// the state sent to the plan source was read from the active document,
        /// and a step that moved the plan to a new document retargeted the
        /// finish without closing the first recording. Checked on the source,
        /// comments left out: no SOLIDWORKS can switch windows in the middle of
        /// a plan on command, so a bench case cannot build it.
        /// </summary>
        private static void UndoOnTheDocumentHeld(string[] lines)
        {
            var code = new System.Text.StringBuilder();
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                code.Append(line).Append('\n');
            }
            string text = code.ToString();

            int starts = Regex.Matches(text, @"\.\s*StartRecordingUndoObject\s*\(").Count;
            int finishes = Regex.Matches(text, @"\.\s*FinishRecordingUndoObject2?\s*\(").Count;
            string startBody = Body(text, "private static IModelDoc2 StartUndoRecording(");
            string finishBody = Body(text, "private void FinishUndoRecording(");
            Segna("the undo recording is started and finished in one place each",
                  starts == 1 && finishes == 1 &&
                  startBody != null && startBody.Contains("StartRecordingUndoObject") &&
                  finishBody != null && Regex.IsMatch(finishBody, @"FinishRecordingUndoObject2?\s*\("),
                  starts + " start and " + finishes + " finish call(s); StartUndoRecording " +
                  (startBody == null ? "missing" : "found") + ", FinishUndoRecording " +
                  (finishBody == null ? "missing" : "found"));

            // With the two reads of the ceiling above, a state read that is not
            // on the active document can only be on the document held.
            Segna("it starts on the document held, and the state is not read from the active one (M19)",
                  text.Contains("undoRecordedOn = StartUndoRecording(documentoAtteso)") &&
                  !Regex.IsMatch(text, @"StatoDocumento\.Leggi\(\s*_sw\.IActiveDoc2"),
                  "not on a fresh _sw.IActiveDoc2");

            // The change of document: the old recording is closed and a new one
            // opened BEFORE documentoAtteso moves.
            int block = text.IndexOf("if (e.DocumentoNuovo != null)", StringComparison.Ordinal);
            int move = block < 0 ? -1 : text.IndexOf("documentoAtteso = e.DocumentoNuovo;", block, StringComparison.Ordinal);
            string between = move < 0 ? "" : text.Substring(block, move - block);
            Segna("a step that changes document closes the recording and opens a new one",
                  move > block &&
                  between.Contains("FinishUndoRecording(undoRecordedOn)") &&
                  between.Contains("undoRecordedOn = StartUndoRecording(e.DocumentoNuovo)"),
                  move < 0 ? "the DocumentoNuovo block or its reassignment is missing"
                           : "FinishUndoRecording, then StartUndoRecording on the new document, before the reassignment");

            // The only finish call sits in FinishUndoRecording (checked above),
            // so these two calls are all the closings there are.
            Segna("and the plan closes the recording on the document that holds it",
                  Regex.Matches(text, @"FinishUndoRecording\(undoRecordedOn\)").Count == 2,
                  "FinishUndoRecording(undoRecordedOn) at the change of document and at the end, " +
                  "not a finish on another document behind an active-document test");
        }

        /// <summary>The body of the method whose declaration starts with <paramref name="declaration"/>, braces counted; null if absent.</summary>
        private static string Body(string text, string declaration)
        {
            int start = text.IndexOf(declaration, StringComparison.Ordinal);
            if (start < 0) return null;
            int depth = 0;
            bool open = false;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '{') { depth++; open = true; }
                else if (text[i] == '}')
                {
                    depth--;
                    if (open && depth == 0) return text.Substring(start, i - start + 1);
                }
            }
            return null;
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
