using System;
using System.Collections.Generic;
using System.IO;
using Riscontro.Disegno;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Ponte
{
    /// <summary>
    /// IL CICLO DI VITA DEL DOCUMENTO: crearne uno, aprirne uno, sapere quali
    /// sono aperti, portarne uno davanti, chiuderne uno che non ha niente da
    /// salvare (voci Z-B6, Z-B1, Z-B5, Z-B2).
    ///
    /// ── PERCHE' STA NEL PONTE E NON NEL CATALOGO ────────────────────────────
    ///
    /// Le operazioni del catalogo sono passi di un piano, e un piano lavora SU
    /// un documento: l'esecutore lo tiene per mano dall'inizio alla fine, lo
    /// confronta passo per passo, e chiude tutto dentro una voce di
    /// annullamento. Creare o aprire un documento non e' un passo di quel
    /// genere — e' cambiare il documento su cui si lavora. E chi sa «quale
    /// pezzo ho guardato» e' il ponte (`_guardato`, le firme viste): la voce
    /// Z-B6 lo diceva gia', «cambia il documento attivo da dentro il ponte, e
    /// deve invalidare le firme nello stesso istante».
    ///
    /// Fino a qui, senza nessun documento aperto il ponte rispondeva «non c'e'
    /// nessun documento aperto» e si fermava: per cominciare un pezzo da zero
    /// serviva una persona che cliccasse File > Nuovo. La prova del nove (P1)
    /// non si poteva nemmeno cominciare da sola.
    ///
    /// ── LE TRAPPOLE, scritte nelle voci e tenute qui ───────────────────────
    ///
    ///  - Un modello di documento sbagliato NON da' errore: da' un documento del
    ///    TIPO sbagliato, che si scopre quando un cast fallisce piu' avanti. Il
    ///    tipo si RILEGGE dopo la creazione.
    ///  - `OpenDoc6` riuscito non vuol dire «aperto il file chiesto»: si rilegge
    ///    `GetPathName()` e si confronta.
    ///  - Un file gia' aperto da un collega su una cartella di rete si apre in
    ///    sola lettura SENZA chiedere niente (Z-B2). Si dice, sempre.
    ///  - `ActivateDoc3` riuscito non vuol dire «davanti c'e' quello»: si
    ///    rilegge il documento attivo.
    /// </summary>
    internal sealed partial class Sessione
    {
        /// <summary>Crea una parte o un assieme vuoti dal modello predefinito.</summary>
        public Dictionary<string, object> NuovoDocumento(string tipo, out string errore)
        {
            errore = null;
            var sw = _sw as ISldWorks;
            if (sw == null) { errore = "Il ponte non e' agganciato a SolidWorks."; return null; }

            int preferenza, atteso;
            string nomeTipo;
            switch ((tipo ?? "").Trim().ToLowerInvariant())
            {
                case "parte":
                    preferenza = (int)swUserPreferenceStringValue_e.swDefaultTemplatePart;
                    atteso = (int)swDocumentTypes_e.swDocPART;
                    nomeTipo = "parte";
                    break;
                case "assieme":
                    preferenza = (int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly;
                    atteso = (int)swDocumentTypes_e.swDocASSEMBLY;
                    nomeTipo = "assieme";
                    break;
                default:
                    errore = "«tipo» vale «parte» o «assieme». Una tavola nasce da un " +
                             "pezzo, con «crea_disegno» dentro un piano.";
                    return null;
            }

            string modello = "";
            try { modello = sw.GetUserPreferenceStringValue(preferenza) ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(modello))
            {
                errore = "SOLIDWORKS non ha un modello predefinito per un " + nomeTipo +
                         " (Opzioni > Posizioni dei file > Modelli di documento). Senza, " +
                         "NewDocument aprirebbe una finestra di scelta che nessuno chiude.";
                return null;
            }
            if (!File.Exists(modello))
            {
                errore = "Il modello predefinito per un " + nomeTipo + " non c'e': «" +
                         modello + "». Se sta su un disco di rete, potrebbe non " +
                         "rispondere in questo momento.";
                return null;
            }

            var doc = sw.NewDocument(modello, 0, 0, 0) as IModelDoc2;
            if (doc == null)
            {
                errore = "NewDocument non ha creato niente, col modello «" + modello + "».";
                return null;
            }

            // IL TIPO SI RILEGGE: un modello sbagliato da' un documento del tipo
            // sbagliato senza nessun errore.
            int vero = doc.GetType();
            if (vero != atteso)
            {
                // Just born and empty: closing it throws away nobody's work. By
                // reference, not by title (Z-C21.13 N11): it is the active
                // document, and a new document's title can be the stem of an
                // open file. Read back before saying it is closed.
                var closing = DocumentByPath.CloseAndReadBack(sw, doc);
                errore = "The default template for a new " + TypeNameInEnglish(atteso) + " («" + modello +
                         "») made a document of type " + TypeNameInEnglish(vero) + ". " +
                         (closing.IsClosed
                             ? "I closed it at once: it was empty."
                             : "It was empty, and it is NOT closed: " + closing.Message + ".");
                return null;
            }

            var fuori = Scheda(doc, DocumentoAttivo());
            fuori["modello"] = modello;
            return fuori;
        }

        /// <summary>Apre un file SOLIDWORKS dal disco e lo porta davanti.</summary>
        public Dictionary<string, object> ApriDocumento(string percorso, out string errore)
        {
            errore = null;
            var sw = _sw as ISldWorks;
            if (sw == null) { errore = "Il ponte non e' agganciato a SolidWorks."; return null; }
            if (string.IsNullOrWhiteSpace(percorso))
            {
                errore = "«percorso» e' obbligatorio: il file da aprire, col percorso intero.";
                return null;
            }
            percorso = percorso.Trim().Trim('"');

            int tipo;
            switch (Path.GetExtension(percorso).ToLowerInvariant())
            {
                case ".sldprt": tipo = (int)swDocumentTypes_e.swDocPART; break;
                case ".sldasm": tipo = (int)swDocumentTypes_e.swDocASSEMBLY; break;
                case ".slddrw": tipo = (int)swDocumentTypes_e.swDocDRAWING; break;
                default:
                    errore = "Apro solo file SOLIDWORKS: .sldprt, .sldasm, .slddrw. Uno " +
                             "STEP o un IGES si importano, non si aprono: e' un'altra strada.";
                    return null;
            }
            if (!Path.IsPathRooted(percorso))
            {
                errore = "Voglio il percorso intero, non «" + percorso + "»: un percorso " +
                         "relativo dipende da dove SOLIDWORKS crede di essere.";
                return null;
            }
            if (!File.Exists(percorso))
            {
                errore = "Il file non esiste: «" + percorso + "». Il documento davanti " +
                         "non e' cambiato.";
                return null;
            }

            // GIA' APERTO: non si riapre, si porta davanti. Riaprirlo darebbe un
            // avviso «gia' aperto» e nessun cambiamento, che si legge come un
            // successo solo se si sa cosa guardare.
            //
            // A PART LOADED HIDDEN BY AN ASSEMBLY IS FOUND AND SHOWN TOO
            // (finding C4-4, measured: it works). ISldWorks~GetOpenDocumentByName.html
            // says «Only call this method for a document that has already been
            // opened in its own window», and the doubt was a false refusal for a
            // part the assembly holds without a window. The Z-C21.3 probe of
            // C4-4 (28/9/2026) closed the part's window with the assembly
            // holding it: GetOpenDocumentByName gave the same object (Visible
            // False), and ActivateDoc3 in PortaDavanti brought it to the front,
            // errors 0, Visible True afterwards. The
            // guide's way (a Visible filter, then OpenDoc6) ended the same, with
            // swFileLoadWarning_AlreadyOpen. So no filter here.
            var gia = sw.GetOpenDocumentByName(percorso) as IModelDoc2;
            if (gia != null)
            {
                var portato = PortaDavanti(sw, gia, out errore);
                if (portato == null) return null;
                portato["era_gia_aperto"] = true;
                return portato;
            }

            int errori = 0, avvisi = 0;
            var doc = sw.OpenDoc6(percorso, tipo,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errori, ref avvisi) as IModelDoc2;
            if (doc == null || errori != 0)
            {
                errore = "SOLIDWORKS non ha aperto «" + percorso + "»: " + ErroriDiApertura(errori) +
                         (avvisi != 0 ? " Avvisi: " + AvvisiDiApertura(avvisi) + "." : "");
                return null;
            }

            // IL FILE APERTO E' QUELLO CHIESTO? Si rilegge, non si crede.
            string aperto = doc.GetPathName() ?? "";
            if (!string.Equals(Normale(aperto), Normale(percorso), StringComparison.OrdinalIgnoreCase))
            {
                errore = "Ho chiesto «" + percorso + "» e SOLIDWORKS dice di aver aperto «" +
                         aperto + "». Non lo uso: potrebbe essere un altro file con lo " +
                         "stesso nome.";
                return null;
            }

            // E DAVANTI C'E' LUI? OpenDoc6 di solito lo attiva; si controlla.
            var fuori = PortaDavanti(sw, doc, out errore);
            if (fuori == null) return null;
            fuori["era_gia_aperto"] = false;
            if (avvisi != 0) fuori["avvisi_di_apertura"] = AvvisiDiApertura(avvisi);
            return fuori;
        }

        /// <summary>I documenti aperti e visibili, con quale e' davanti.</summary>
        public List<Dictionary<string, object>> Documenti(out int nascosti, out string errore)
        {
            errore = null;
            nascosti = 0;
            var sw = _sw as ISldWorks;
            if (sw == null) { errore = "Il ponte non e' agganciato a SolidWorks."; return null; }

            var attivo = DocumentoAttivo();
            var fuori = new List<Dictionary<string, object>>();
            var doc = sw.GetFirstDocument() as IModelDoc2;
            int giro = 0;
            while (doc != null && giro++ < 500)
            {
                bool visibile = true;
                try { visibile = doc.Visible; } catch { }
                // I NASCOSTI SONO I PEZZI CARICATI DENTRO UN ASSIEME: sono
                // documenti per SOLIDWORKS e non per chi lavora. Si contano e
                // si dicono, non si elencano.
                if (visibile) fuori.Add(Scheda(doc, attivo));
                else nascosti++;
                doc = doc.GetNext() as IModelDoc2;
            }
            return fuori;
        }

        /// <summary>Porta davanti un documento gia' aperto, per percorso o titolo.</summary>
        public Dictionary<string, object> AttivaDocumento(string quale, out string errore)
        {
            errore = null;
            var sw = _sw as ISldWorks;
            if (sw == null) { errore = "Il ponte non e' agganciato a SolidWorks."; return null; }
            if (string.IsNullOrWhiteSpace(quale))
            {
                errore = "«percorso» e' obbligatorio: il percorso o il titolo di un documento aperto.";
                return null;
            }
            quale = quale.Trim().Trim('"');

            List<string> titoli;
            var trovato = TrovaAperto(sw, quale, out titoli);
            if (trovato == null)
            {
                errore = "«" + quale + "» non e' fra i documenti aperti. Aperti adesso: " +
                         (titoli.Count == 0 ? "nessuno" : "«" + string.Join("», «", titoli) + "»") +
                         ". Il documento davanti non e' cambiato.";
                return null;
            }
            return PortaDavanti(sw, trovato, out errore);
        }

        /// <summary>
        /// Chiude un documento aperto, per percorso o titolo, e RIFIUTA se ha
        /// modifiche non salvate (voce Z-B1).
        ///
        /// Non c'e' un «chiudi buttando le modifiche»: chi lavora per conto di
        /// qualcuno non decide di buttare il suo lavoro, e un'opzione che lo fa
        /// e' un'opzione che prima o poi si passa per sbaglio. Chi vuole
        /// buttarle chiude a mano, e la finestra di SOLIDWORKS glielo chiede.
        ///
        /// SI RILEGGE che sia chiuso davvero: `CloseDoc` torna `void`.
        /// </summary>
        public Dictionary<string, object> ChiudiDocumento(string quale, out string errore)
        {
            errore = null;
            var sw = _sw as ISldWorks;
            if (sw == null) { errore = "Il ponte non e' agganciato a SolidWorks."; return null; }
            if (string.IsNullOrWhiteSpace(quale))
            {
                errore = "«percorso» e' obbligatorio: il percorso o il titolo del documento da chiudere.";
                return null;
            }
            quale = quale.Trim().Trim('"');

            List<string> titoli;
            var doc = TrovaAperto(sw, quale, out titoli);
            if (doc == null)
            {
                errore = "«" + quale + "» non e' fra i documenti aperti. Aperti adesso: " +
                         (titoli.Count == 0 ? "nessuno" : "«" + string.Join("», «", titoli) + "»") + ".";
                return null;
            }

            bool sporco = false;
            try { sporco = doc.GetSaveFlag(); } catch { }
            string titolo = doc.GetTitle() ?? "";
            if (sporco)
            {
                errore = "«" + titolo + "» ha modifiche non salvate: non lo chiudo, le butterei. " +
                         "Salvalo prima («salva_documento», dentro un piano), oppure chiudilo a " +
                         "mano se quelle modifiche sono da buttare.";
                return null;
            }

            // THE HIDDEN DOCUMENTS IT WOULD TAKE WITH IT (Z-C21.13 N11).
            // ISldWorks~CloseDoc.html: «This method also closes any non-active
            // hidden documents», and the N11 probe (29/9/2026) saw CloseDoc of
            // one document close a hidden part with an unsaved edit, which was
            // lost (H4). The close action promises to throw away nobody's work,
            // so the hidden documents with unsaved changes that no other open
            // document holds are a refusal too.
            var atRisk = DocumentByPath.UnsavedHiddenDocumentsAtRisk(sw, doc);
            if (atRisk.Count > 0)
            {
                errore = "Not closing «" + titolo + "»: CloseDoc also closes the hidden documents that no " +
                         "other open document uses, without saving them, and these have unsaved changes: «" +
                         string.Join("», «", atRisk) + "». Save them first (a component is saved with the " +
                         "assembly that uses it), or close by hand if those changes are to be thrown away.";
                return null;
            }

            // BY PATH, OR FOR A DOCUMENT NEVER SAVED BY REFERENCE, NEVER BY TITLE
            // (Z-C21.13 N11): with the extensions hidden Base.SLDPRT and
            // Base.SLDASM have the same title, and nothing documents which one
            // CloseDoc closes. DocumentByPath reads back whether it is gone.
            var id = Identita(doc);
            string percorso = Convert.ToString(id["percorso"] ?? "");
            var closing = DocumentByPath.CloseAndReadBack(sw, doc);
            if (closing.Result != DocumentByPath.CloseResult.Closed &&
                closing.Result != DocumentByPath.CloseResult.StillLoadedHidden)
            {
                errore = "«" + titolo + "» is not closed: " + closing.Message + ".";
                return null;
            }

            // READ BACK from the bridge's side too: not among the visible documents.
            List<string> dopo;
            if (TrovaAperto(sw, string.IsNullOrEmpty(percorso) ? titolo : percorso, out dopo) != null)
            {
                errore = "I asked to close «" + titolo + "» and it is still open (" + closing.Message + ").";
                return null;
            }
            var davanti = DocumentoAttivo();
            var answer = new Dictionary<string, object>
            {
                ["chiuso"] = titolo,
                ["percorso"] = string.IsNullOrEmpty(percorso) ? null : percorso,
                ["aperti_adesso"] = dopo.Count,
                ["davanti_adesso"] = davanti == null ? null : Identita(davanti)["titolo"],
                ["closed_by"] = closing.NamePassed == ""
                    ? "the active document (CloseDoc with an empty name)"
                    : closing.NamePassed,
            };
            // ITS WINDOW IS GONE, ITS MODEL IS NOT: another open document
            // references it (the N11 probe, H1). It had no unsaved changes, or
            // it would have been refused above, so nothing is at risk; it is said.
            if (closing.Result == DocumentByPath.CloseResult.StillLoadedHidden)
            {
                answer["still_loaded_hidden"] = true;
                answer["still_loaded_reason"] = closing.Message;
            }
            if (closing.AlsoClosed.Count > 0) answer["also_closed"] = closing.AlsoClosed;
            if (closing.AlsoClosedUnsaved.Count > 0) answer["also_closed_unsaved"] = closing.AlsoClosedUnsaved;
            return answer;
        }

        /// <summary>
        /// Un documento aperto e visibile, per percorso o — se non e' mai stato
        /// salvato — per titolo. `titoli` riporta cosa c'era, per dirlo in un
        /// rifiuto.
        /// </summary>
        private IModelDoc2 TrovaAperto(ISldWorks sw, string quale, out List<string> titoli)
        {
            IModelDoc2 trovato = null;
            titoli = new List<string>();
            var doc = sw.GetFirstDocument() as IModelDoc2;
            int giro = 0;
            while (doc != null && giro++ < 500)
            {
                bool visibile = true;
                try { visibile = doc.Visible; } catch { }
                if (visibile)
                {
                    string p = doc.GetPathName() ?? "", t = doc.GetTitle() ?? "";
                    titoli.Add(string.IsNullOrEmpty(p) ? t : p);
                    bool combacia = !string.IsNullOrEmpty(p)
                        ? string.Equals(Normale(p), Normale(quale), StringComparison.OrdinalIgnoreCase)
                        : string.Equals(t, quale, StringComparison.Ordinal);
                    if (combacia && trovato == null) trovato = doc;
                }
                doc = doc.GetNext() as IModelDoc2;
            }
            return trovato;
        }

        // ── attrezzi ────────────────────────────────────────────────────────

        /// <summary>
        /// Brings <paramref name="doc"/> to the front by its file name or path,
        /// or by title only when it was never saved and no other open document
        /// answers to that title (Z-C21.13, pattern S18: ActivateDoc3.html warns
        /// that a name without extension can fit a part and an assembly at once).
        /// DocumentByPath reads back the active document by reference.
        /// </summary>
        private Dictionary<string, object> PortaDavanti(ISldWorks sw, IModelDoc2 doc, out string errore)
        {
            errore = null;
            int activationErrors;
            string problem;
            if (!DocumentByPath.ActivateAndReadBack(sw, doc, out activationErrors, out problem))
            {
                errore = problem + ".";
                return null;
            }
            // READ BACK from the bridge's side too: the identity it compares.
            var davanti = DocumentoAttivo();
            if (!StessoDocumento(Identita(davanti), Identita(doc)))
            {
                errore = "I asked to bring «" + doc.GetTitle() + "» to the front, and the active document " +
                         "is «" + Convert.ToString(Identita(davanti)?["titolo"] ?? "nothing") + "».";
                return null;
            }
            return Scheda(doc, davanti);
        }

        private Dictionary<string, object> Scheda(IModelDoc2 doc, object attivo)
        {
            bool solaLettura = false, modificato = false;
            try { solaLettura = doc.IsOpenedReadOnly(); } catch { }
            try { modificato = doc.GetSaveFlag(); } catch { }
            var id = Identita(doc);
            var scheda = new Dictionary<string, object>
            {
                ["titolo"] = id["titolo"],
                ["percorso"] = id["percorso"],
                ["tipo"] = NomeDelTipo(doc.GetType()),
                ["sola_lettura"] = solaLettura,
                ["modifiche_non_salvate"] = modificato,
                ["davanti"] = attivo != null && StessoDocumento(Identita(attivo), id),
            };
            if (solaLettura)
                scheda["attenzione"] =
                    "SOLA LETTURA: di solito vuol dire che un collega ha il file aperto. " +
                    "«disegna» non ci scrive: le modifiche resterebbero in memoria e il " +
                    "salvataggio fallirebbe, o finirebbe in una copia.";
            return scheda;
        }

        private static string NomeDelTipo(int tipo)
        {
            switch (tipo)
            {
                case (int)swDocumentTypes_e.swDocPART: return "parte";
                case (int)swDocumentTypes_e.swDocASSEMBLY: return "assieme";
                case (int)swDocumentTypes_e.swDocDRAWING: return "tavola";
                default: return "altro (" + tipo + ")";
            }
        }

        /// <summary>The document type for the English messages of Z-C21.13.</summary>
        private static string TypeNameInEnglish(int type)
        {
            switch (type)
            {
                case (int)swDocumentTypes_e.swDocPART: return "part";
                case (int)swDocumentTypes_e.swDocASSEMBLY: return "assembly";
                case (int)swDocumentTypes_e.swDocDRAWING: return "drawing";
                default: return "type " + type;
            }
        }

        private static string Normale(string p)
        {
            return (p ?? "").Trim().Trim('"').Replace('/', '\\');
        }

        private static string ErroriDiApertura(int e)
        {
            var nomi = new List<string>();
            if ((e & (int)swFileLoadError_e.swFileNotFoundError) != 0) nomi.Add("file non trovato");
            if ((e & (int)swFileLoadError_e.swInvalidFileTypeError) != 0) nomi.Add("tipo di file non valido");
            if ((e & (int)swFileLoadError_e.swFutureVersion) != 0) nomi.Add("salvato da una versione piu' nuova di SOLIDWORKS");
            if ((e & (int)swFileLoadError_e.swGenericError) != 0) nomi.Add("errore generico");
            return nomi.Count == 0 ? "codice " + e : string.Join(", ", nomi) + " (codice " + e + ")";
        }

        private static string AvvisiDiApertura(int a)
        {
            var nomi = new List<string>();
            if ((a & (int)swFileLoadWarning_e.swFileLoadWarning_ReadOnly) != 0) nomi.Add("aperto in sola lettura");
            if ((a & (int)swFileLoadWarning_e.swFileLoadWarning_SharingViolation) != 0) nomi.Add("il file e' in uso da un altro");
            if ((a & (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen) != 0) nomi.Add("era gia' aperto");
            if ((a & (int)swFileLoadWarning_e.swFileLoadWarning_NeedsRegen) != 0) nomi.Add("va ricostruito");
            if ((a & (int)swFileLoadWarning_e.swFileLoadWarning_ModelOutOfDate) != 0) nomi.Add("modello non aggiornato");
            return nomi.Count == 0 ? "codice " + a : string.Join(", ", nomi) + " (codice " + a + ")";
        }
    }
}
