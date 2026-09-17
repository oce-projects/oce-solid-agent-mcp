using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// CROCI DI CENTRO SUI FORI IN TAVOLA (voce W2.11).
    ///
    /// ── PERCHE' E' UN'OPERAZIONE E NON UNA RIFINITURA ───────────────────
    ///
    /// Una tavola con dodici fori e nessuna croce di centro esce, si stampa, e
    /// sembra completa. In officina manca la cosa che serve per forare: **dove
    /// sta il centro.** E' la forma da tavola del difetto che questo progetto
    /// esiste per prendere — nessun errore, risultato inutilizzabile.
    ///
    /// ── LA FIRMA STA SU `IView`, NON SU `IDrawingDoc` ───────────────────
    ///
    ///     Boolean IView.AutoInsertCenterMarks2(
    ///         Int32 InsertType, Int32 InsertOption,
    ///         Boolean LinearSlotCenter, Boolean ArcSlotCenter,
    ///         Boolean UseDocumentDefaults, Double Size, Double Gap,
    ///         Boolean ExtendedLines, Boolean CenterLineFont, Double Angle)
    ///
    /// Mette le croci su **tutti** i fori della vista in un colpo e **non
    /// chiede nessuna selezione**: e' esattamente cio' che serve qui, perche'
    /// una tavola con dodici fori non si fa selezionando dodici cerchi.
    ///
    /// `InsertType` e' una MASCHERA DI BIT — `swAutoInsertCenterMarkTypes_e`:
    /// Hole = 1, Fillets = 2, Slots = 4 — non un valore esclusivo. Letto, non
    /// ricordato: sono le potenze di due a dirlo.
    ///
    /// ── E TRE NOMI VICINI CHE SONO BOTTONI ──────────────────────────────
    ///
    /// `IDrawingDoc.CenterMark()` ha zero argomenti e ritorna `Void`. Ma una
    /// croce di centro ha una dimensione, uno stile e delle linee: se nessuna
    /// di queste e' un argomento, quella non e' l'API che la crea — **e'
    /// l'entrata della finestra.** Da qui non c'e' nessuno che possa premere
    /// OK, e una chiamata COM che aspetta un modale appende il ponte e il
    /// client dietro (memoria: «API SolidWorks che aprono finestre»).
    /// `AddCenterMark` e `InsertCenterMark3` lavorano invece sulla SELEZIONE, e
    /// vanno bene per «questo foro qui»: non e' il caso di questa operazione.
    ///
    /// ── LA MISURA NON E' IL `Boolean` ───────────────────────────────────
    ///
    /// `IView.GetCenterMarkCount()` **prima e dopo**, vista per vista. Il
    /// valore di ritorno dice che la chiamata e' andata, non che sia nata una
    /// croce — ed e' il difetto tipico: *il valore che torna da chi ha fatto la
    /// cosa non e' la misura della cosa.*
    ///
    /// ── L'UNICO ARGOMENTO CHE NON HO POTUTO LEGGERE, E COSA CI FACCIO ───
    ///
    /// `InsertOption` non ha un enum dedicato nell'interop: non c'e' nessun
    /// `swAutoInsertCenterMarkOptions_e`. Il candidato e' `swCenterMarkStyle_e`
    /// (Single = 2, LinearGroup = 3, CircularGroup = 4), **e questo e' un
    /// sospetto, non un fatto.** Percio' non si indovina: si prova lo stile
    /// singolo, **si contano le croci**, e se non ne e' nata nessuna si provano
    /// gli altri due. E' la stessa tecnica di `quote_dal_modello` con le sue
    /// maschere: *quando non si sa quale valore sia quello giusto, si prova e
    /// si RICONTA — l'unica cosa che non si fa e' dichiarare fatto.*
    /// Quale stile abbia funzionato finisce nelle misure, cosi' la prossima
    /// volta e' un fatto e non piu' un tentativo.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito CrociDiCentro(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito(
                    "Il documento attivo non e' una tavola: le croci di centro stanno " +
                    "su una vista di disegno. Crea prima la tavola con «crea_disegno».");

            var nomi = NomiDelleViste(disegno);
            if (nomi.Count == 0)
                return Fallito(
                    "La tavola non ha viste: le croci non avrebbero dove attaccarsi. " +
                    "Metti prima le viste con «viste_standard» o «vista_modello».");

            string voluta = Str(a, "vista", null);
            if (!string.IsNullOrWhiteSpace(voluta) && !NomeDiVistaConosciuto(nomi, voluta))
                return Fallito(
                    "Non c'e' nessuna vista che si chiami «" + voluta + "». Ci sono: " +
                    string.Join(", ", nomi) + ".");

            int tipo = TipoDiCentrino(Str(a, "su", "fori"));
            if (tipo == 0)
                return Fallito(
                    "«su» puo' essere «fori», «raccordi», «asole» oppure «tutti». " +
                    "Predefinito: «fori».");

            // SENZA `dimensione_mm` si usano le impostazioni del documento, e
            // non un numero scelto da me. La dimensione delle croci e' una
            // convenzione di studio: metterne una mia vorrebbe dire produrre
            // tavole che non somigliano alle altre tavole della stessa officina.
            bool suaDimensione = a != null && a.ContainsKey("dimensione_mm");
            double dimensioneMm = Num(a, "dimensione_mm", 2.5);
            double gapMm = Num(a, "distacco_mm", 0.0);
            bool estese = Bool(a, "linee_estese", false);

            // Gli stili da provare, nell'ordine. Il primo e' il piu' probabile;
            // gli altri due esistono perche' `InsertOption` non e' documentato
            // dall'interop e il conteggio e' l'unico giudice (vedi il commento
            // in testa alla classe).
            var stili = new[]
            {
                (int)swCenterMarkStyle_e.swCenterMark_Single,
                (int)swCenterMarkStyle_e.swCenterMark_LinearGroup,
                (int)swCenterMarkStyle_e.swCenterMark_CircularGroup,
            };

            var perVista = new List<object>();
            int totalePrima = 0, totaleDopo = 0;
            string stileCheHaFunzionato = null;

            var foglio = disegno.GetFirstView() as IView;      // il primo e' IL FOGLIO
            var vista = foglio == null ? null : foglio.GetNextView() as IView;
            while (vista != null)
            {
                string nome = null;
                try { nome = vista.Name; } catch { }

                if (!string.IsNullOrWhiteSpace(voluta) && !StessoNome(nome, voluta))
                {
                    vista = vista.GetNextView() as IView;
                    continue;
                }

                int prima = QuanteCroci(vista);
                int dopo = prima;
                string stileQui = null;

                foreach (var stile in stili)
                {
                    try
                    {
                        vista.AutoInsertCenterMarks2(
                            tipo, stile,
                            true,                 // LinearSlotCenter
                            true,                 // ArcSlotCenter
                            !suaDimensione,       // UseDocumentDefaults
                            Unita.M(dimensioneMm),
                            Unita.M(gapMm),
                            estese,
                            false,                // CenterLineFont
                            0.0);                 // Angle
                    }
                    catch (Exception ex)
                    {
                        return Fallito(
                            "Croci di centro non inserite sulla vista «" + (nome ?? "?") +
                            "»: " + ex.Message);
                    }

                    dopo = QuanteCroci(vista);
                    if (dopo > prima) { stileQui = NomeDelloStile(stile); break; }
                }

                if (stileQui != null && stileCheHaFunzionato == null)
                    stileCheHaFunzionato = stileQui;

                totalePrima += prima;
                totaleDopo += dopo;
                perVista.Add(new Dictionary<string, object>
                {
                    ["vista"] = nome ?? "(senza nome)",
                    ["croci_prima"] = prima,
                    ["croci_dopo"] = dopo,
                    ["croci_aggiunte"] = Math.Max(0, dopo - prima),
                });

                vista = vista.GetNextView() as IView;
            }

            try { modello.EditRebuild3(); } catch { }

            int aggiunte = Math.Max(0, totaleDopo - totalePrima);
            var misure = new Dictionary<string, object>
            {
                ["su"] = Str(a, "su", "fori"),
                ["viste_guardate"] = perVista.Count,
                ["croci_prima"] = totalePrima,
                ["croci_dopo"] = totaleDopo,
                ["croci_aggiunte"] = aggiunte,
                ["per_vista"] = perVista.ToArray(),
                ["dimensione"] = suaDimensione
                    ? dimensioneMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " mm"
                    : "quella del documento",
            };
            if (stileCheHaFunzionato != null) misure["stile_che_ha_funzionato"] = stileCheHaFunzionato;

            // ── ZERO CROCI NON E' UN SUCCESSO SILENZIOSO ────────────────
            //
            // La chiamata puo' rispondere `true` e non aver disegnato niente:
            // succede quando la vista non ha cerchi. Detto come «fatto» sarebbe
            // il difetto che questa operazione esiste per prevenire, spostato
            // di un passo — chi legge crede di avere i centri e non li ha.
            if (aggiunte == 0)
            {
                misure["giudizio"] =
                    "NESSUNA CROCE AGGIUNTA. " +
                    (totaleDopo > 0
                        ? "Le " + totaleDopo + " che ci sono c'erano gia': niente di nuovo da mettere."
                        : "Su " + perVista.Count + (perVista.Count == 1 ? " vista" : " viste") +
                          " non e' nato niente, e quasi sempre vuol dire che quella vista non " +
                          "ha " + CosaCercava(tipo) + ": e' la vista sbagliata, oppure il pezzo " +
                          "non ne ha.") +
                    " Non l'ho considerata fatta a meta': il numero e' zero e te lo dico.";
                _log("[agente] " + misure["giudizio"]);
                return RiuscitoSenzaCreare(misure);
            }

            misure["giudizio"] =
                "Messe " + aggiunte + (aggiunte == 1 ? " croce di centro" : " croci di centro") +
                " su " + perVista.Count + (perVista.Count == 1 ? " vista" : " viste") +
                " (" + totalePrima + " -> " + totaleDopo + "), contate sulla tavola e non " +
                "dedotte dalla risposta.";
            _log("[agente] " + misure["giudizio"]);
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// Quante croci di centro ha questa vista. Zero anche quando non si e'
        /// potuto leggere — ed e' accettabile **solo** perche' il numero serve
        /// per un confronto prima/dopo: se la lettura fallisse in tutti e due i
        /// momenti la differenza sarebbe zero, cioe' «non e' nato niente», che
        /// e' il verso prudente.
        /// </summary>
        private static int QuanteCroci(IView vista)
        {
            try { return vista.GetCenterMarkCount(); }
            catch { return 0; }
        }

        /// <summary>
        /// La maschera di `swAutoInsertCenterMarkTypes_e`, o 0 se il nome non
        /// si riconosce. **Non c'e' un valore predefinito silenzioso**: un nome
        /// sbagliato deve dare un rifiuto, non le croci su qualcos'altro.
        /// </summary>
        private static int TipoDiCentrino(string come)
        {
            const int FORI = (int)swAutoInsertCenterMarkTypes_e.swAutoInsertCenterMarkType_Hole;
            const int RACCORDI = (int)swAutoInsertCenterMarkTypes_e.swAutoInsertCenterMarkType_Fillets;
            const int ASOLE = (int)swAutoInsertCenterMarkTypes_e.swAutoInsertCenterMarkType_Slots;

            switch ((come ?? "").Trim().ToLowerInvariant())
            {
                case "":
                case "fori": return FORI;
                case "raccordi": return RACCORDI;
                case "asole": return ASOLE;
                case "tutti": return FORI | RACCORDI | ASOLE;
                default: return 0;
            }
        }

        private static string CosaCercava(int tipo)
        {
            var pezzi = new List<string>();
            if ((tipo & (int)swAutoInsertCenterMarkTypes_e.swAutoInsertCenterMarkType_Hole) != 0)
                pezzi.Add("fori");
            if ((tipo & (int)swAutoInsertCenterMarkTypes_e.swAutoInsertCenterMarkType_Fillets) != 0)
                pezzi.Add("raccordi");
            if ((tipo & (int)swAutoInsertCenterMarkTypes_e.swAutoInsertCenterMarkType_Slots) != 0)
                pezzi.Add("asole");
            return pezzi.Count == 0 ? "niente di cercabile" : string.Join(" ne' ", pezzi);
        }

        private static string NomeDelloStile(int stile)
        {
            if (stile == (int)swCenterMarkStyle_e.swCenterMark_Single) return "singolo";
            if (stile == (int)swCenterMarkStyle_e.swCenterMark_LinearGroup) return "gruppo lineare";
            if (stile == (int)swCenterMarkStyle_e.swCenterMark_CircularGroup) return "gruppo circolare";
            return "sconosciuto (" + stile + ")";
        }

        private static bool StessoNome(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(),
                                 StringComparison.OrdinalIgnoreCase);
        }

        private static bool NomeDiVistaConosciuto(List<string> nomi, string voluta)
        {
            foreach (var n in nomi) if (StessoNome(n, voluta)) return true;
            return false;
        }
    }
}
