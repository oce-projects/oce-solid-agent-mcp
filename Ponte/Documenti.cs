using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Ponte
{
    /// <summary>
    /// IL CICLO DI VITA DEL DOCUMENTO: crearne uno, aprirne uno, sapere quali
    /// sono aperti, portarne uno davanti (voci Z-B6, Z-B1, Z-B5, Z-B2).
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
                string titolo = doc.GetTitle();
                // Appena nato e vuoto: chiuderlo non butta via lavoro di nessuno.
                try { sw.CloseDoc(titolo); } catch { }
                errore = "Il modello predefinito per un " + nomeTipo + " («" + modello +
                         "») ha creato un documento di tipo " + NomeDelTipo(vero) +
                         ". L'ho chiuso subito: era vuoto.";
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

            IModelDoc2 trovato = null;
            var titoli = new List<string>();
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
            if (trovato == null)
            {
                errore = "«" + quale + "» non e' fra i documenti aperti. Aperti adesso: " +
                         (titoli.Count == 0 ? "nessuno" : "«" + string.Join("», «", titoli) + "»") +
                         ". Il documento davanti non e' cambiato.";
                return null;
            }
            return PortaDavanti(sw, trovato, out errore);
        }

        // ── attrezzi ────────────────────────────────────────────────────────

        private Dictionary<string, object> PortaDavanti(ISldWorks sw, IModelDoc2 doc, out string errore)
        {
            errore = null;
            int err = 0;
            try
            {
                sw.ActivateDoc3(doc.GetTitle(), false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref err);
            }
            catch (Exception ex)
            {
                errore = "Non sono riuscito a portare davanti «" + doc.GetTitle() + "»: " + ex.Message;
                return null;
            }
            // RILETTO: davanti c'e' davvero lui?
            var davanti = DocumentoAttivo();
            if (!StessoDocumento(Identita(davanti), Identita(doc)))
            {
                errore = "Ho chiesto di portare davanti «" + doc.GetTitle() + "» e davanti " +
                         "c'e' «" + Convert.ToString(Identita(davanti)?["titolo"] ?? "niente") +
                         "» (errore di attivazione " + err + ").";
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
