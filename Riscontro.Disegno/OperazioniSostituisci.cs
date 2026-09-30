using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// SOSTITUIRE UN COMPONENTE (voce GA7).
    ///
    /// ── LA FIRMA C'ERA, IL CENSIMENTO GUARDAVA L'INTERFACCIA SBAGLIATA ───
    ///
    /// La voce diceva: *«il censimento 80q NON elenca una firma dedicata;
    /// `IComponent2` ha 170 metodi e quasi certamente esiste qualcosa tipo
    /// `ReplaceComponent`, ma va cercata e letta, non assunta»*, e da li' il
    /// «non stimabile».
    ///
    /// **Su `IComponent2` non c'e', e non c'e' mai stata.** Sta su
    /// `IAssemblyDoc`:
    ///
    ///     Boolean ReplaceComponents2(String FileName, String ConfigName,
    ///                                Boolean ReplaceAllInstance,
    ///                                Int32 UseConfigChoice,
    ///                                Boolean ReAttachMates)
    ///
    /// E' la stessa lezione gia' pagata due volte in questo progetto:
    /// *un nome dice come si chiama un metodo, non su quale interfaccia sta*
    /// (nota `81s`, `AddConfiguration3`). Cercare per centosettanta metodi
    /// sull'oggetto sbagliato non trova niente, e «non l'ho trovata» diventa
    /// «non esiste».
    ///
    /// ── IL RISCHIO CHE LA VOCE NOMINA, E CHE QUI SI MISURA ───────────────
    ///
    /// *«Se l'API sostituisce il documento ma NON conserva gli accoppiamenti, si
    /// ripresenta identico il difetto tipico: pezzo diverso, nessun errore.»*
    ///
    /// `ReAttachMates` e' un parametro, quindi si puo' **chiedere** che li
    /// conservi. Chiedere non e' ottenere: si contano gli accoppiamenti **prima
    /// e dopo**, e se sono calati lo si dice a voce alta invece di dichiarare
    /// fatto. Un coperchio sostituito che ha perso i suoi vincoli e' un assieme
    /// che sembra montato e non lo e'.
    ///
    /// ── E IL CONTEGGIO NON BASTA: **RESTARE NON E' FUNZIONARE** ──────────
    ///
    /// Misurato dal banco, ed e' la cosa che questa voce ha trovato: i vincoli
    /// del pezzo vecchio **non si perdono** — due prima, due dopo — **e vanno
    /// in errore**, perche' puntavano a facce che non ci sono piu'. Il
    /// conteggio degli accoppiamenti non lo puo' vedere: da solo direbbe
    /// «tutto a posto». Lo vede `GetWhatsWrongCount`, che e' la stessa domanda
    /// che si fa un progettista guardando l'albero rosso.
    ///
    /// Percio' le misure sono DUE coppie prima/dopo e non una: quanti
    /// accoppiamenti, e quanti problemi. **Un vincolo rotto e' peggio di un
    /// vincolo assente**: assente si vede, rotto sembra che tenga.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito SostituisciComponente(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var assieme = modello as IAssemblyDoc;
            if (assieme == null)
                return Fallito(
                    "Questo non e' un assieme, e' un documento di tipo «" +
                    NomeTipoDocumento(TipoDi(modello)) + "». I componenti si " +
                    "sostituiscono dentro un assieme.");

            object[] grezzi = null;
            try { grezzi = assieme.GetComponents(false) as object[]; } catch { }
            var componenti = StatoDocumento.ComponentiInOrdine(grezzi);
            if (componenti.Count == 0)
                return Fallito("L'assieme non ha componenti da sostituire.");

            string uid = Str(a, "componente", null);
            var comp = PerUid(componenti, uid);
            if (comp == null) return Fallito(NonTrovato(uid, "componente", componenti));

            string nuovo = Str(a, "percorso", null);
            if (string.IsNullOrWhiteSpace(nuovo))
                return Fallito(
                    "Non mi hai detto con cosa sostituirlo: «percorso» e' il percorso " +
                    "completo del file nuovo, e dev'essere gia' aperto. Li trovi in " +
                    "«documenti_aperti» dello stato.");
            if (!File.Exists(nuovo))
                return Fallito("Il file «" + nuovo + "» non esiste. " + CosaEAperto());
            if (!EAperto(nuovo))
                return Fallito(
                    "«" + Path.GetFileName(nuovo) + "» non e' aperto in SOLIDWORKS, e la " +
                    "sostituzione vuole un documento gia' aperto. " + CosaEAperto());

            // SOSTITUIRE UN PEZZO CON SE STESSO NON E' UN'OPERAZIONE.
            // Riuscirebbe, non cambierebbe niente, e chi l'ha chiesta credeva di
            // aver cambiato il pezzo.
            string vecchio = null;
            try { vecchio = comp.GetPathName(); } catch { }
            if (!string.IsNullOrWhiteSpace(vecchio) &&
                string.Equals(Path.GetFullPath(vecchio), Path.GetFullPath(nuovo),
                              StringComparison.OrdinalIgnoreCase))
                return Fallito(
                    "«" + uid + "» usa gia' quel file. Sostituirlo con se stesso " +
                    "riuscirebbe e non cambierebbe niente: se volevi un altro pezzo, " +
                    "e' il percorso a essere sbagliato.");

            bool tutte = Bool(a, "tutte_le_istanze", true);
            string configurazione = Str(a, "configurazione", "");

            // ── `ReAttachMates` NON E' UNA SCELTA: E' SEMPRE `false` ─────
            //
            // Misurato il 17/9/2026, sei condizioni. Con `true` **e** il
            // componente che ha davvero degli accoppiamenti da riagganciare,
            // `ReplaceComponents2` solleva `RPC_E_SERVERFAULT` — sempre:
            // assieme salvato o no, file nuovo aperto o chiuso. Con `false`
            // passa sempre.
            //
            // **E qui mi ero sbagliato**, e va scritto dove sta il codice e non
            // solo nel changelog: da «con `false` passa» avevo DEDOTTO «con
            // `false` i vincoli si perdono». Il banco ha misurato il contrario —
            // due prima, due dopo, zero persi — e poi la cosa vera: **i vincoli
            // restano e vanno in ERRORE.** `ReAttachMates: false` non vuol dire
            // «scartali», vuol dire «non provare a riagganciarli in modo
            // interattivo». *Restare non e' funzionare*, ed e' esattamente
            // percio' che qui sotto non basta contarli.
            //
            // Quindi non si espone una manopola che puo' stare in una posizione
            // sola: *un argomento che non cambia niente e' peggio di un
            // argomento che non c'e', perche' chi lo mette crede di aver
            // deciso qualcosa.* Si passa `false` e si DICE cosa comporta.
            const bool riaggancia = false;

            // ── SI CONTA PRIMA, SU TUTTO L'ASSIEME ──────────────────────
            //
            // Non solo sul componente toccato: un accoppiamento lega DUE pezzi,
            // e perderlo si vede da tutti e due. Il totale e' la misura che non
            // dipende da quale maniglia sopravvive alla sostituzione.
            int accPrima = AccoppiamentiInTutto(componenti);
            int quantiPrima = componenti.Count;
            int problemiPrima = QuantiProblemi(modello);

            bool risposta;
            try
            {
                modello.ClearSelection2(true);
                if (!SelezionaIlComponente(comp))
                    return Fallito(
                        "Non sono riuscito a selezionare «" + uid + "»: senza selezione " +
                        "la sostituzione lavorerebbe su quello che capita, o su niente.");
                // `MatchName`, MAI `ManuallySelect`. L'altro valore di
                // `swReplaceComponentsConfiguration_e` apre una finestra per far
                // scegliere la configurazione a mano — e **una finestra modale
                // appende la chiamata COM all'infinito**, con il ponte e il
                // client dietro. E' la trappola gia' pagata con `AddDimension`
                // (memoria: «API SolidWorks che aprono finestre»), e da qui non
                // c'e' nessuno che possa premere OK.
                //
                // UNSAVED EDITS ARE NOT LOST (finding L11-4, NOT REAL). The doubt
                // was that replacing a component whose part, old or new, has
                // unsaved changes would drop them. The Z-C21.3 probe of L11-4
                // (28/9/2026) edited the old part (16000 -> 32000 mm3), the new
                // one (9000 -> 18000), or both, without saving, and called
                // ReplaceComponents2 as here: true every time, the assembly took
                // the new part's unsaved volume, and both parts kept their edits
                // in memory, still dirty (the files on disk untouched). So
                // nothing is saved first.
                risposta = assieme.ReplaceComponents2(
                    nuovo, configurazione ?? "", tutte,
                    (int)swReplaceComponentsConfiguration_e.swReplaceComponentsConfiguration_MatchName,
                    riaggancia);
            }
            catch (Exception ex)
            {
                return Fallito("Non sono riuscito a sostituire il componente: " + ex.Message);
            }
            finally { try { modello.ClearSelection2(true); } catch { } }

            try { modello.EditRebuild3(); } catch { }

            // ── E SI CONTA DOPO ─────────────────────────────────────────
            var dopoComp = StatoDocumento.ComponentiInOrdine(
                assieme.GetComponents(false) as object[]);
            int accDopo = AccoppiamentiInTutto(dopoComp);

            // LA MISURA NON E' IL `Boolean`. Si guarda se **qualche componente
            // usa davvero il file nuovo**: e' l'unica cosa che dice che la
            // sostituzione e' avvenuta.
            bool ceLaFatta = false;
            foreach (var v in dopoComp)
            {
                string suo = null;
                try { suo = v.Value.GetPathName(); } catch { }
                if (!string.IsNullOrWhiteSpace(suo) &&
                    string.Equals(Path.GetFullPath(suo), Path.GetFullPath(nuovo),
                                  StringComparison.OrdinalIgnoreCase))
                { ceLaFatta = true; break; }
            }

            if (!ceLaFatta)
                return Fallito(
                    "Mi e' stato risposto «" + (risposta ? "si'" : "no") + "» ma nessun " +
                    "componente dell'assieme usa «" + Path.GetFileName(nuovo) + "». " +
                    "Non l'ho considerata fatta.");

            var misure = new Dictionary<string, object>
            {
                ["componente"] = uid,
                ["da"] = string.IsNullOrWhiteSpace(vecchio) ? "(senza percorso)" : Path.GetFileName(vecchio),
                ["a"] = Path.GetFileName(nuovo),
                ["componenti_prima"] = quantiPrima,
                ["componenti_dopo"] = dopoComp.Count,
                ["accoppiamenti_prima"] = accPrima,
                ["accoppiamenti_dopo"] = accDopo,
                ["accoppiamenti_persi"] = Math.Max(0, accPrima - accDopo),
            };

            // ── I VINCOLI CI SONO ANCORA. FUNZIONANO? ───────────────────
            //
            // Sono due domande diverse, e il conteggio risponde solo alla
            // prima. Un vincolo che puntava a una faccia del pezzo vecchio
            // resta nell'albero e va in ERRORE: il conteggio lo vede ancora e
            // direbbe «due prima, due adesso».
            int problemiDopo = QuantiProblemi(modello);
            if (problemiPrima >= 0 && problemiDopo >= 0)
            {
                misure["problemi_prima"] = problemiPrima;
                misure["problemi_dopo"] = problemiDopo;
            }
            bool rotti = problemiPrima >= 0 && problemiDopo > problemiPrima;

            // ── IL GIUDIZIO, che e' il punto della voce ─────────────────
            string giudizio;
            string fatto = "Sostituito «" + misure["da"] + "» con «" + misure["a"] +
                           "» su " + uid + ". ";
            if (rotti)
                // IL CASO PEGGIORE, E NON E' «PERSI»: i vincoli ci sono ancora e
                // sono ROTTI. Zero vincoli si vede; due vincoli in errore
                // sembrano vincolare.
                giudizio = fatto + "ATTENZIONE: gli accoppiamenti ci sono ancora (" +
                           accDopo + ") ma il documento ha " + problemiDopo +
                           " problemi contro i " + problemiPrima + " di prima: **qualcuno " +
                           "di quei vincoli e' in ERRORE**, perche' puntava a una faccia " +
                           "del pezzo vecchio. Un vincolo rotto e' peggio di un vincolo " +
                           "assente: sembra che tenga. Toglilo e rimettilo con «accoppia», " +
                           "oppure «annulla».";
            else if (accPrima == 0)
                giudizio = fatto + "Non c'erano accoppiamenti da conservare.";
            else if (accDopo >= accPrima)
                giudizio = fatto + "Gli accoppiamenti sono rimasti e sono sani: " +
                           accDopo + (accDopo == 1 ? " vincolo" : " vincoli") +
                           ", e il documento non ha problemi nuovi.";
            else
                giudizio = fatto + "**GLI ACCOPPIAMENTI SI SONO PERSI**: ne aveva " +
                           accPrima + ", adesso ne ha " + accDopo + ". Il pezzo nuovo e' " +
                           "dentro e **non e' vincolato**: l'assieme sembra montato e non " +
                           "lo e'. Rimettili con «accoppia», oppure «annulla».";
            misure["giudizio"] = giudizio;
            _log("[agente] " + giudizio);

            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// Seleziona un COMPONENTE. Non e' come selezionare una faccia.
        ///
        /// ── L'ERRORE CHE QUESTO METODO ESISTE PER NON RIPETERE ──────────
        ///
        /// La prima stesura faceva `(comp as IEntity).Select4(false, null)`,
        /// che e' come si selezionano le facce e gli spigoli in tutto il resto
        /// di questo progetto. **`IComponent2` non si casta a `IEntity`:** il
        /// cast torna `null`, e la chiamata esplode con «Riferimento a un
        /// oggetto non impostato» — un messaggio che parla di tutt'altro.
        ///
        /// `IComponent2` ha un `Select4` **suo**, con tre parametri invece di
        /// due:
        ///
        ///     Boolean Select4(Boolean Append, SelectData Data, Boolean ShowPopup)
        ///
        /// **Il terzo va `false`.** `ShowPopup: true` aprirebbe una finestra, e
        /// una finestra modale appende la chiamata COM con il ponte dietro: da
        /// qui non c'e' nessuno che possa chiuderla. E' la stessa trappola di
        /// `AddDimension` e di `ManuallySelect` qui sopra — **tre chiamate
        /// diverse, nella stessa operazione, che sanno aprire una finestra.**
        ///
        /// *Due metodi con lo stesso nome su due interfacce diverse, con firme
        /// diverse: il compilatore non aiuta, perche' il cast sbagliato compila
        /// benissimo e fallisce a runtime.*
        /// </summary>
        private static bool SelezionaIlComponente(IComponent2 c)
        {
            if (c == null) return false;
            try { return c.Select4(false, null, false); }
            catch { return false; }
        }

        /// <summary>
        /// Quanti accoppiamenti in tutto l'assieme, contando per componente.
        ///
        /// **E' un conteggio di ESTREMI, non di accoppiamenti**: ogni vincolo
        /// lega due pezzi e viene contato due volte. Non si divide per due,
        /// perche' non tutti i vincoli legano esattamente due componenti e
        /// dividere introdurrebbe un numero che a volte e' mezzo. Qui serve solo
        /// per il CONFRONTO prima/dopo, e per quello un conteggio doppio va bene
        /// quanto uno singolo — purche' si sappia che e' doppio, ed e' per
        /// questo che c'e' scritto.
        /// </summary>
        private static int AccoppiamentiInTutto(
            List<KeyValuePair<string, IComponent2>> componenti)
        {
            int n = 0;
            foreach (var v in componenti)
            {
                int suoi = AccoppiamentiDi(v.Value);
                if (suoi > 0) n += suoi;
            }
            return n;
        }
    }
}
