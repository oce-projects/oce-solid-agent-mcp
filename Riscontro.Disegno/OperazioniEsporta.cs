using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// FAR USCIRE IL PEZZO DA SOLIDWORKS. (voce GP2)
    ///
    /// ── PERCHE' L'STL E' IL CASO PIU' PERICOLOSO DEL CATALOGO ────────────
    ///
    /// Un file STL non contiene quasi niente: un elenco di triangoli. Non dice
    /// in che unita' sono, non dice se il solido e' chiuso, non dice da quale
    /// pezzo viene. Tutto quello che si puo' sbagliare qui **si apre lo
    /// stesso, si affetta lo stesso e si stampa lo stesso**, e il difetto si
    /// vede quando il pezzo e' in mano.
    ///
    /// Le tre cose che non danno errore da nessuna parte:
    ///
    /// 1. **LE UNITA'.** L'STL non le porta con se': lo slicer assume
    ///    millimetri. Se SOLIDWORKS esporta in pollici, il pezzo esce 25,4
    ///    volte piu' piccolo e niente lo dice. `swExportStlUnits` e' una
    ///    PREFERENZA GLOBALE della macchina: lasciarla com'e' vuol dire che il
    ///    risultato dipende da chi ha usato SOLIDWORKS prima.
    /// 2. **IL CORPO NON CHIUSO.** Una superficie o un solido con una fessura
    ///    esporta un STL che si apre e si stampa con un buco. E' il motivo per
    ///    cui la voce GP1 andava fatta prima di questa: **qui si rifiuta
    ///    invece di scrivere**, e la verifica gira PRIMA, non dopo.
    /// 3. **PIU' DI UN CORPO.** Un STL di due corpi e' due gusci separati:
    ///    lo slicer li stampa tutti e due, e la stampante non sa quale dei due
    ///    era il pezzo.
    ///
    /// ── COME SI IMPOSTA LA QUALITA', VISTO CHE NON C'E' UN'INTERFACCIA ──
    ///
    /// `IExportStlData` **non esiste in questo interop**: cercata per
    /// riflessione fra tutte le 1737 interfacce, nessun tipo con «Stl» nel
    /// nome. L'esportazione si fa con `SaveAs3` e l'estensione `.stl`, e ogni
    /// opzione e' una **preferenza utente globale**:
    ///
    ///   swSTLQuality (int)            swSTLQuality_Coarse=1, _Fine=2, _Custom=3
    ///   swSTLBinaryFormat (toggle)    binario invece che ASCII
    ///   swExportStlUnits (int)        swLengthUnit_e: swMM = 0
    ///   swSTLShowInfoOnSave (toggle)  ** una finestra che blocca la chiamata COM **
    ///   swSTLDontTranslateToPositive  se false, le coordinate vengono traslate
    ///   swSTLComponentsIntoOneFile    solo per gli assiemi
    ///
    /// SI LEGGONO, SI IMPOSTANO, SI RIMETTONO COM'ERANO. Sono impostazioni
    /// della macchina di chi disegna: un'operazione che le cambia e le lascia
    /// cambiate modifica il CAD di qualcun altro senza dirlo.
    ///
    /// `swSTLShowInfoOnSave` a `true` aprirebbe un modale, e **una chiamata COM
    /// che apre un modale non torna**: e' la stessa trappola di
    /// `AddDimension`, gia' pagata in questo progetto.
    ///
    /// ── E POI SI GUARDA IL FILE, NON LA RISPOSTA ─────────────────────────
    ///
    /// Un STL binario ha un'intestazione di 80 byte, poi **4 byte col numero
    /// di triangoli**, poi 50 byte per triangolo. Quindi:
    ///
    ///     dimensione attesa = 84 + 50 * triangoli
    ///
    /// e il numero di triangoli si confronta con quello che la geometria
    /// tassellata dichiara. E' l'unico modo di sapere che il file scritto e'
    /// il pezzo e non un'intestazione vuota: `SaveAs3` che torna `true` dice
    /// solo che SOLIDWORKS non ha protestato.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito EsportaStl(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var parte = modello as IPartDoc;
            if (parte == null)
                return Fallito(
                    "Questo documento non e' un pezzo: lo STL si esporta da una parte. Per mandare un " +
                    "assieme usa «esporta_step», che lo porta intero.");

            // ── 1. LA VERIFICA VIENE PRIMA, e puo' fermare tutto ──────────
            //
            // E' la riga per cui la voce GP1 andava fatta prima di questa. La
            // misura del file — esiste, pesa qualcosa — NON cattura il corpo
            // con la fessura: serve il controllo di integrita' PRIMA, non la
            // verifica del file DOPO.
            var verifica = VerificaGeometriaCorpo(modello, new Dictionary<string, object>(), stato);
            if (!verifica.Riuscito) return verifica;

            bool stampabile = verifica.Misure != null &&
                              verifica.Misure.ContainsKey("stampabile") &&
                              Convert.ToBoolean(verifica.Misure["stampabile"]);
            string giudizio = verifica.Misure != null && verifica.Misure.ContainsKey("giudizio")
                ? Convert.ToString(verifica.Misure["giudizio"]) : "";

            bool forza = Bool(a, "esporta_comunque", false);
            if (!stampabile && !forza)
                return Fallito(
                    "Non esporto: " + giudizio + "\n\n" +
                    "Un STL non porta con se' niente che dica che il pezzo era cosi': " +
                    "si apre, si affetta e si stampa lo stesso. Sistema il pezzo, " +
                    "oppure passa «esporta_comunque»: true se sai quello che fai.");

            // ── 2. DOVE ──────────────────────────────────────────────────
            string dove = Str(a, "percorso", null);
            if (string.IsNullOrWhiteSpace(dove))
            {
                string suo = "";
                try { suo = modello.GetPathName() ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(suo))
                    return Fallito(
                        "Il pezzo non e' mai stato salvato e non mi hai detto dove mettere " +
                        "lo STL. Dimmi «percorso», oppure salva prima il pezzo.");
                dove = Path.ChangeExtension(suo, ".stl");
            }
            if (!".stl".Equals(Path.GetExtension(dove), StringComparison.OrdinalIgnoreCase))
                return Fallito(
                    "Il percorso «" + dove + "» non finisce in .stl. L'estensione NON e' " +
                    "un dettaglio: e' l'unica cosa che dice a SaveAs3 che formato scrivere.");

            string cartella;
            try { cartella = Path.GetDirectoryName(Path.GetFullPath(dove)); }
            catch (Exception ex) { return Fallito("Percorso che non so leggere: " + ex.Message); }
            if (!Directory.Exists(cartella))
                return Fallito("La cartella «" + cartella + "» non esiste: non la creo io.");

            bool binario = Bool(a, "binario", true);
            string qualitaChiesta = (Str(a, "qualita", "fine") ?? "fine").Trim().ToLowerInvariant();
            int qualita;
            if (qualitaChiesta == "fine") qualita = (int)swSTLQuality_e.swSTLQuality_Fine;
            else if (qualitaChiesta == "grossolana") qualita = (int)swSTLQuality_e.swSTLQuality_Coarse;
            else return Fallito(
                "Qualita' «" + qualitaChiesta + "» sconosciuta: ci sono «fine» e «grossolana».");

            // ── 3. LE PREFERENZE: lette, imposte, rimesse ────────────────
            var prima = LeggiPreferenzeStl();
            string posato = null;
            int errori = 0, avvisi = 0;
            bool ok;
            try
            {
                ImpostaPreferenzeStl(qualita, binario);

                if (File.Exists(dove))
                {
                    try { File.Delete(dove); }
                    catch (Exception ex)
                    {
                        return Fallito(
                            "C'e' gia' un file li' e non riesco a sostituirlo (" + ex.Message +
                            "): probabilmente e' aperto in un altro programma.");
                    }
                }

                // LA SELEZIONE SI VUOTA PRIMA, SEMPRE (voce Z-C21.2, rilievo L13-1).
                //
                // IModelDocExtension~SaveAs3.html, Remarks: «Exports the entire
                // model, unless faces or bodies are selected, in which case, it
                // exports only those. Call IModelDoc2::ClearSelection2 before
                // calling this method to clear the selection list and export the
                // entire model.»
                //
                // Una faccia rimasta selezionata — da un passo prima, o a mano da
                // chi sta davanti a SOLIDWORKS — faceva uscire uno STL con quella
                // faccia sola, e i controlli qui sotto non potevano accorgersene:
                // confrontano il file con se stesso (84 + 50 per triangolo), non
                // col pezzo. `triangoli_dalla_geometria` resta un numero riportato
                // accanto e non diventa un vincolo: la tassellatura di schermo e
                // quella di esportazione non hanno la stessa tolleranza, e una
                // soglia sull'ordine di grandezza non si fissa senza averla
                // misurata su pezzi curvi. Il caso
                // `lo_stl_non_esporta_la_faccia_selezionata` prende il difetto.
                modello.ClearSelection2(true);
                try
                {
                    ok = modello.Extension.SaveAs3(
                        dove,
                        (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                        (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                        null, null, ref errori, ref avvisi);
                }
                catch (Exception ex) { return Fallito("STL non scritto: " + ex.Message); }
                posato = dove;
            }
            finally
            {
                // SEMPRE, anche se l'esportazione e' andata male: le
                // preferenze sono della macchina di chi disegna, e lasciarle
                // cambiate vuol dire cambiargli il CAD senza dirglielo.
                RimettiPreferenzeStl(prima);
            }

            if (!ok || !File.Exists(posato))
                return Fallito(
                    "Lo STL non e' stato scritto (codice " + errori + SaveErrorNames(errori) + "). " +
                    "Il file non c'e': " + posato);

            long quantoPesa = new FileInfo(posato).Length;

            // ── 4. SI GUARDA IL FILE, NON LA RISPOSTA ────────────────────
            int triangoliNelFile = TriangoliDelloStl(posato, binario, quantoPesa, out string comeLoSo);
            int triangoliDelPezzo = TriangoliDellaGeometria(parte);

            var misure = new Dictionary<string, object>
            {
                ["percorso"] = posato,
                ["byte"] = quantoPesa,
                ["formato"] = binario ? "binario" : "ASCII",
                ["qualita"] = qualitaChiesta,
                // LE UNITA' SI DICONO SEMPRE. Il file non le porta, e chi apre
                // lo STL non ha nessun modo di saperle: se questa riga non
                // c'e', l'unica risposta possibile e' «boh, prova».
                ["unita"] = "mm",
                ["triangoli"] = triangoliNelFile >= 0 ? (object)triangoliNelFile : null,
                ["triangoli_come_li_so"] = comeLoSo,
                ["triangoli_dalla_geometria"] = triangoliDelPezzo >= 0
                    ? (object)triangoliDelPezzo : null,
                ["verifica"] = giudizio,
            };
            if (!stampabile)
                misure["esportato_comunque"] =
                    "L'hai chiesto tu con «esporta_comunque»: il file c'e' e il pezzo " +
                    "non era a posto. " + giudizio;

            // UN FILE CHE NON TORNA NON E' UN FILE. Su un STL binario la
            // dimensione e' esattamente 84 + 50 per triangolo: se non torna, il
            // file c'e' e non e' quello che serve, ed e' meglio saperlo adesso
            // che dallo slicer.
            if (binario && triangoliNelFile > 0)
            {
                long attesa = 84L + 50L * triangoliNelFile;
                if (attesa != quantoPesa)
                    return Fallito(
                        "Lo STL dice " + triangoliNelFile + " triangoli, che fanno " + attesa +
                        " byte, e il file ne pesa " + quantoPesa + ". Il file c'e' e non " +
                        "torna: guardalo prima di mandarlo alla stampante. (" + posato + ")");
            }
            if (triangoliNelFile == 0)
                return Fallito(
                    "Lo STL e' stato scritto ma non ha nessun triangolo: e' un file vuoto " +
                    "con l'intestazione. (" + posato + ")");

            _log("[agente] STL: " + triangoliNelFile + " triangoli, " + quantoPesa +
                 " byte, mm, " + posato);
            return SenzaFeature(misure);
        }

        // =================================================================
        // LE PREFERENZE STL, lette e rimesse
        // =================================================================

        private sealed class PreferenzeStl
        {
            public int Qualita;
            public int Unita;
            public bool Binario;
            public bool MostraInfo;
            public bool NonTraslare;
        }

        private PreferenzeStl LeggiPreferenzeStl()
        {
            var p = new PreferenzeStl();
            try { p.Qualita = _sw.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality); } catch { }
            try { p.Unita = _sw.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits); } catch { }
            try { p.Binario = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat); } catch { }
            try { p.MostraInfo = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave); } catch { }
            try { p.NonTraslare = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive); } catch { }
            return p;
        }

        private void ImpostaPreferenzeStl(int qualita, bool binario)
        {
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, qualita); } catch { }
            // I MILLIMETRI, ESPLICITI. E' la riga che vale l'operazione: senza,
            // il risultato dipende da chi ha usato SOLIDWORKS prima.
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits, (int)swLengthUnit_e.swMM); } catch { }
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, binario); } catch { }
            // UNA FINESTRA BLOCCHEREBBE LA CHIAMATA COM, e non tornerebbe mai.
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, false); } catch { }
            // LE COORDINATE RESTANO DOVE SONO. Traslare il pezzo nel positivo
            // vuol dire che l'origine dello STL non e' l'origine del modello, e
            // chi confronta due esportazioni non ritrova piu' niente.
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, true); } catch { }
        }

        /// <summary>
        /// The swFileSaveError_e names of the Errors mask of SaveAs3, as ": ..." to append after the
        /// raw code, or "" when the mask is 0 (S13, ErrorCodeDecoder). The raw
        /// code stays in the message: whoever already searches for it still
        /// finds it.
        /// </summary>
        private static string SaveErrorNames(int errors)
        {
            var names = ErrorCodeDecoder.FileSaveErrors(errors);
            return names == null ? "" : ": " + names;
        }

        private void RimettiPreferenzeStl(PreferenzeStl p)
        {
            if (p == null) return;
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, p.Qualita); } catch { }
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits, p.Unita); } catch { }
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, p.Binario); } catch { }
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, p.MostraInfo); } catch { }
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, p.NonTraslare); } catch { }
        }

        // =================================================================
        // I TRIANGOLI: quelli scritti nel file e quelli della geometria
        // =================================================================

        /// <summary>
        /// Quanti triangoli dice il FILE. Su un binario sta nei quattro byte
        /// dopo l'intestazione da 80; su un ASCII si contano le righe «facet».
        /// </summary>
        private static int TriangoliDelloStl(string percorso, bool binario, long quantoPesa,
                                             out string comeLoSo)
        {
            comeLoSo = "";
            try
            {
                if (binario)
                {
                    if (quantoPesa < 84) { comeLoSo = "il file e' piu' corto dell'intestazione"; return -1; }
                    using (var f = File.OpenRead(percorso))
                    {
                        var testa = new byte[84];
                        if (f.Read(testa, 0, 84) != 84)
                        { comeLoSo = "non sono riuscito a leggere l'intestazione"; return -1; }
                        comeLoSo = "dai quattro byte dopo l'intestazione del file binario";
                        return BitConverter.ToInt32(testa, 80);
                    }
                }
                int quanti = 0;
                foreach (var riga in File.ReadLines(percorso))
                    if (riga.TrimStart().StartsWith("facet ", StringComparison.OrdinalIgnoreCase))
                        quanti++;
                comeLoSo = "contando le righe «facet» del file ASCII";
                return quanti;
            }
            catch (Exception ex)
            {
                comeLoSo = "non ho potuto leggere il file: " + ex.Message;
                return -1;
            }
        }

        /// <summary>
        /// Quanti triangoli ha la tassellatura del pezzo, faccia per faccia.
        ///
        /// NON E' LO STESSO NUMERO del file, e non deve esserlo: la
        /// tassellatura di schermo e quella di esportazione hanno tolleranze
        /// diverse. Serve come ORDINE DI GRANDEZZA — un file con dieci
        /// triangoli su un pezzo che ne ha mille e' un file sbagliato — e per
        /// questo si riporta accanto, non si confronta.
        /// </summary>
        private static int TriangoliDellaGeometria(IPartDoc parte)
        {
            try
            {
                int quanti = 0;
                var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (corpi == null) return -1;
                foreach (var c in corpi)
                {
                    var facce = (c as IBody2).GetFaces() as object[];
                    if (facce == null) continue;
                    foreach (var f in facce)
                    {
                        // Una faccia che non dice i suoi triangoli fa «non lo so»
                        // tutto il conto, non un conto piu' basso (voce Z-C8).
                        try { quanti += (f as IFace2).GetTessTriangleCount(); } catch { return -1; }
                    }
                }
                return quanti;
            }
            catch { return -1; }
        }

        // =================================================================
        // STEP e IGES: i formati con cui si manda un pezzo a qualcun altro
        // =================================================================
        //
        // ── PERCHE' NON RIFIUTANO COME LO STL ────────────────────────────
        //
        // `esporta_stl` si rifiuta se il pezzo non e' un solo corpo solido
        // chiuso. Qui NO, ed e' una differenza voluta: un STEP con dieci
        // corpi e' **normale** — un saldato si manda al fornitore cosi' — e
        // uno con delle superfici aperte pure, perche' STEP e IGES le
        // portano. Rifiutarli sarebbe applicare a un formato le regole di un
        // altro.
        //
        // La verifica gira lo stesso e **il suo giudizio viaggia con la
        // risposta**: chi legge sa com'era il pezzo che ha mandato, e non gli
        // si impedisce di mandarlo.
        //
        // ── NIENTE INTERFACCIA DI ESPORTAZIONE, SOLO PREFERENZE ──────────
        //
        // Cercate per riflessione: di STEP e IGES esistono solo le interfacce
        // di IMPORTAZIONE (`IImportStepData`, `IImportIgesData`). Per scrivere
        // c'e' `SaveAs3` con `ExportData` a null, e tutto il resto sono
        // preferenze utente globali:
        //
        //   swStepAP (int)                 203, 214, with no enum (not 242: below)
        //   swIGESRepresentation (int)     swIGES_BREP = 3 (solidi),
        //                                  swIGES_TRMSRF = 0 (superfici)
        //   swIGESExportSolidAndSurface    on: «Specifies whether to export data
        //                                  as solid or surface entities»
        //   swIGESExportAsWireframe        off: «Specifies whether to convert
        //                                  solid body to a 3D wireframe
        //                                  representation in the IGES file»
        //   swIGESExportFreeCurves         off: «Specifies whether to include 3D
        //                                  curve features in the exported file»
        //   swIGESExportSketchEntities     off: «Specifies whether to export
        //                                  sketch entities»
        //
        // (estratto_swconst/FileSaveAsIGESOptions.htm, «System Options > Export
        // > IGES 5.3».) THE THREE «off» ARE Z-C21.10, finding N59: they were
        // left as the user's profile had them. With «IGES wireframe (3D curves)»
        // ticked the page says the solid body is converted to curves (whether
        // next to the solid or in its place is not measured); with sketches or
        // 3D curves ticked, the file carries stray curves the recipient takes
        // for geometry. All four are read before, set, and put back in
        // `finally`, like the representation. After writing, the file's own
        // directory says what is in it (`IgesEntityCensus`): a file with no
        // solid and no surface is refused.
        //
        // `swStepAP` IS THE AWKWARD ONE: its values have no enum in `swconst`,
        // so the number is a literal, and a literal is something one believes.
        // That is why, after writing, the file's own FILE_SCHEMA is read back:
        // it says which protocol really came out. AP203 declares
        // «CONFIG_CONTROL_DESIGN», AP214 «AUTOMOTIVE_DESIGN», AP242 its long
        // name. It is the only way to know that the 214 asked is the 214 got.
        //
        // ── AP242 IS NOT A VALUE OF swStepAP (Z-C21.13, finding C5-7) ────────
        //
        // The page lists two values only (estratto_swconst/
        // FileSaveAsSTEPOptions.htm, «203 = STEP AP203 format», «214 = STEP
        // AP214 format»), and until library 0.143.0 this code wrote 242 anyway.
        // The probe probe_step_ap242 (29/9/2026, SOLIDWORKS 2021 SP5.1): 242 is
        // accepted, read back as 242, SaveAs3 answers true with no error and no
        // warning, and the file is AP203 (FILE_SCHEMA «CONFIG_CONTROL_DESIGN»);
        // the junk value 999 does the same. The FILE_SCHEMA check refused that
        // file, but for the wrong reason: «AP242 asked, AP203 out» sends one
        // looking at the protocol preference, when AP242 has another door.
        //
        // The door is IModelDocExtension.PublishSTEP242File (page
        // IModelDocExtension~PublishSTEP242File.html: «Exports the SOLIDWORKS
        // MBD 3D part or assembly to a STEP 242 file», Path «use .STP for the
        // file name extension», Return «Status as defined in
        // swStep242Error_e»), SOLIDWORKS MBD, since 2017. The same probe: with a
        // .step path it answers 1 (swPublishStep242_InvalidPath), with a .stp
        // path 4 (swPublishStep242_MBDLicenseNotAvailable): the bench machine
        // has no MBD licence. So AP242 never touches swStepAP, wants a .stp
        // path, and on 4 is refused saying that AP214 and AP203 remain. On 0
        // the file goes through the same FILE_SCHEMA check as the other two;
        // that branch is not measured here, for want of the licence.

        private Esito EsportaScambio(
            IModelDoc2 modello, Dictionary<string, object> a, string operazione,
            StatoDocumento stato)
        {
            var parte = modello as IPartDoc;
            var assieme = modello as IAssemblyDoc;   // voce Z-D6: anche l'assieme si manda
            if (parte == null && assieme == null)
                return Fallito(
                    "Questo documento non e' un pezzo ne' un assieme: STEP e IGES si esportano da una " +
                    "parte o da un assieme. Aprimi quello da mandare.");
            if (assieme != null && Bool(a, "riapri_e_confronta", false))
                return Fallito(
                    "Su un assieme «riapri_e_confronta» non lo faccio: rileggere lo STEP di un assieme apre " +
                    "la finestra «Nuovo documento» e la chiamata non torna (misurato). Il controllo che faccio " +
                    "e' il conto dei solidi nel file contro quelli dei pezzi: toglilo e richiedimelo.");

            bool iges = operazione == "esporta_iges";
            // L'IGES DELL'ASSIEME NO (revisione avversariale del 27/9/2026): non ha la
            // verifica dei solidi che ha lo STEP, e dipende da una preferenza globale
            // (swIGESComponentsIntoOneFile) che non si imposta — con quella spenta
            // SOLIDWORKS scrive un file per componente, e quello in «percorso» puo'
            // non contenere l'assieme intero. Finche' non e' misurato, si rifiuta.
            if (iges && assieme != null)
                return Fallito(
                    "L'IGES di un assieme non lo scrivo: non so verificare che dentro ci sia l'assieme intero (con " +
                    "certe impostazioni SOLIDWORKS scrive un file per componente). Per mandare l'assieme usa " +
                    "«esporta_step», che lo porta intero e lo controllo; per un pezzo solo, aprilo e chiedimi l'IGES li'.");

            // THE PROTOCOL BEFORE THE PATH (Z-C21.13, C5-7): AP242 decides which
            // extension the path may have.
            int protocollo = 0;
            string protocolloChiesto = null;
            bool ap242 = false;
            int rappresentazione = 0;
            string rappresentazioneChiesta = null;
            if (iges)
            {
                rappresentazioneChiesta = (Str(a, "rappresentazione", "solidi") ?? "solidi")
                    .Trim().ToLowerInvariant();
                if (rappresentazioneChiesta == "solidi")
                    rappresentazione = (int)swIGESRepresentation_e.swIGES_BREP;
                else if (rappresentazioneChiesta == "superfici")
                    rappresentazione = (int)swIGESRepresentation_e.swIGES_TRMSRF;
                else return Fallito(
                    "Rappresentazione «" + rappresentazioneChiesta + "» sconosciuta: ci sono " +
                    "«solidi» (BREP, quella che serve a chi deve lavorare il pezzo) e " +
                    "«superfici».");
            }
            else
            {
                protocolloChiesto = (Str(a, "protocollo", "AP214") ?? "AP214")
                    .Trim().ToUpperInvariant();
                if (protocolloChiesto == "AP203") protocollo = 203;
                else if (protocolloChiesto == "AP214") protocollo = 214;
                // AP242 HAS NO swStepAP VALUE: written there, it gives an AP203
                // file (the comment above this method). PublishSTEP242File
                // writes it, further down.
                else if (protocolloChiesto == "AP242") ap242 = true;
                else return Fallito(
                    "Protocollo «" + protocolloChiesto + "» sconosciuto: ci sono AP203, " +
                    "AP214 (predefinito) e AP242.");
            }

            string[] estensioniBuone = iges
                ? new[] { ".iges", ".igs" }
                : ap242 ? new[] { ".stp" } : new[] { ".step", ".stp" };

            // LA VERIFICA GIRA LO STESSO, E NON FERMA NIENTE. Il giudizio
            // viaggia con la risposta: chi manda un pezzo a un fornitore ha
            // diritto di sapere che aveva due corpi, non di essere fermato.
            string giudizio = "";
            if (parte != null)
            {
                var verifica = VerificaGeometriaCorpo(modello, new Dictionary<string, object>(), stato);
                if (verifica.Riuscito && verifica.Misure != null && verifica.Misure.ContainsKey("giudizio"))
                    giudizio = Convert.ToString(verifica.Misure["giudizio"]);
            }
            else giudizio = "assieme: la verifica dei corpi si fa sui pezzi, uno per uno";

            string dove = Str(a, "percorso", null);
            if (string.IsNullOrWhiteSpace(dove))
            {
                string suo = "";
                try { suo = modello.GetPathName() ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(suo))
                    return Fallito(
                        "Il pezzo non e' mai stato salvato e non mi hai detto dove mettere " +
                        "il file. Dimmi «percorso», oppure salva prima il pezzo.");
                dove = Path.ChangeExtension(suo, estensioniBuone[0]);
            }

            // AP242 WANTS .stp, and is refused before the call otherwise (C5-7).
            if (ap242)
            {
                string pathProblem = Step242PathProblem(dove);
                if (pathProblem != null) return Fallito(pathProblem);
            }

            string estensione = (Path.GetExtension(dove) ?? "").ToLowerInvariant();
            if (Array.IndexOf(estensioniBuone, estensione) < 0)
                return Fallito(
                    "Il percorso «" + dove + "» finisce in «" + estensione + "», e per «" +
                    operazione + "» ci vuole " + string.Join(" oppure ", estensioniBuone) +
                    ". L'estensione NON e' un dettaglio: e' l'unica cosa che dice a " +
                    "SaveAs3 che formato scrivere.");

            string cartella;
            try { cartella = Path.GetDirectoryName(Path.GetFullPath(dove)); }
            catch (Exception ex) { return Fallito("Percorso che non so leggere: " + ex.Message); }
            if (!Directory.Exists(cartella))
                return Fallito("La cartella «" + cartella + "» non esiste: non la creo io.");

            var prima = LeggiPreferenzeScambio();
            int errori = 0, avvisi = 0;
            bool ok;
            List<string> igesTogglesNotOff = null;
            try
            {
                if (iges) igesTogglesNotOff = ImpostaPreferenzeIges(rappresentazione);
                else if (!ap242) ImpostaPreferenzeStep(protocollo);

                if (ap242)
                {
                    // C5-7: its own method, its own handling of a file already at
                    // the path (see PublishStep242), and swStepAP left alone.
                    string refusal = PublishStep242(modello, dove);
                    if (refusal != null) return Fallito(refusal);
                    ok = true;
                }
                else
                {
                    if (File.Exists(dove))
                    {
                        try { File.Delete(dove); }
                        catch (Exception ex)
                        {
                            return Fallito(
                                "C'e' gia' un file li' e non riesco a sostituirlo (" + ex.Message +
                                "): probabilmente e' aperto in un altro programma.");
                        }
                    }

                    // COME PER LO STL (rilievo L13-1). IModelDocExtension~SaveAs3.html,
                    // Remarks: «Exports the entire model, unless faces or bodies are
                    // selected, in which case, it exports only those». Per lo STEP di
                    // una parte non c'e' nessun conto dei corpi che se ne accorgerebbe,
                    // e quello dell'assieme prenderebbe un file con meno solidi.
                    modello.ClearSelection2(true);
                    try
                    {
                        ok = modello.Extension.SaveAs3(
                            dove,
                            (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                            null, null, ref errori, ref avvisi);
                    }
                    catch (Exception ex) { return Fallito("File non scritto: " + ex.Message); }
                }
            }
            finally { RimettiPreferenzeScambio(prima); }

            if (!ok || !File.Exists(dove))
                return Fallito("Il file non e' stato scritto (codice " + errori + SaveErrorNames(errori) + "): " + dove);

            long quantoPesa = new FileInfo(dove).Length;
            var misure = new Dictionary<string, object>
            {
                ["percorso"] = dove,
                ["byte"] = quantoPesa,
                ["verifica"] = giudizio,
            };

            if (iges)
            {
                misure["formato"] = "IGES";
                misure["rappresentazione"] = rappresentazioneChiesta;
                string perche;
                int record = RecordIges(dove, out perche);
                misure["record_di_parametri"] = record >= 0 ? (object)record : null;
                misure["come_lo_so"] = perche;
                if (record == 0)
                    return Fallito(
                        "L'IGES e' stato scritto ma non ha nessun record di parametri: e' " +
                        "un file con le intestazioni e niente dentro. (" + dove + ")");

                // WHAT IS IN THE FILE, NOT ONLY HOW MUCH (N59). The record count
                // does not tell a solid from its wireframe: both have records.
                // The directory section does, one type number per entity.
                int entities, solids, surfaces;
                string census;
                bool counted = IgesEntityCensus(dove, out entities, out solids, out surfaces, out census);
                misure["entities"] = counted ? (object)entities : null;
                misure["solid_entities"] = counted ? (object)solids : null;
                misure["surface_entities"] = counted ? (object)surfaces : null;
                misure["entity_census"] = census;
                // THE THREE TOGGLES, AS READ BACK after turning them off (N59,
                // review): an empty list is the proof they were off while the
                // file was written; a name in it is an option that may have put
                // curves in the file next to the solid, which the census above
                // cannot tell from the solid's own edges.
                misure["iges_options_not_off"] = igesTogglesNotOff != null
                    ? (object)igesTogglesNotOff.ToArray() : null;
                if (igesTogglesNotOff != null && igesTogglesNotOff.Count > 0)
                    misure["iges_options_warning"] =
                        "I could not turn off " + string.Join(", ", igesTogglesNotOff) + " for this export " +
                        "(read back on, or unreadable): the file may carry sketch entities or 3D curves next " +
                        "to the solid, and whoever opens it may take them for geometry. Turn them off in " +
                        "System Options > Export > IGES 5.3 and export again.";
                if (counted && entities > 0 && solids == 0 && surfaces == 0)
                    return Fallito(
                        "The IGES was written, but none of its " + entities + " entities is a solid " +
                        "(type 186) or a surface: it is a wireframe of the part, curves only, and " +
                        "whoever opens it gets edges instead of a part. (" + dove + ")");
            }
            else
            {
                misure["formato"] = "STEP";
                misure["protocollo_chiesto"] = protocolloChiesto;
                // WHICH DOOR WROTE IT (C5-7): the two do not share their settings.
                misure["written_with"] = ap242
                    ? "IModelDocExtension.PublishSTEP242File (SOLIDWORKS MBD)"
                    : "SaveAs3 with swStepAP = " + protocollo;
                int entita;
                string perche;
                string schema = SchemaDelloStep(dove, out entita, out perche);
                string uscito = DalloSchema(schema);
                misure["file_schema"] = schema;
                misure["protocollo_uscito"] = uscito;
                misure["entita"] = entita >= 0 ? (object)entita : null;
                misure["come_lo_so"] = perche;

                if (schema == null)
                    return Fallito(
                        "Il file non comincia per «ISO-10303-21» o non ha un FILE_SCHEMA: " +
                        "non e' uno STEP, qualunque cosa dica l'estensione. (" + dove + ")");
                if (entita == 0)
                    return Fallito(
                        "Lo STEP e' stato scritto ma la sua sezione DATA e' vuota: e' un " +
                        "file con le intestazioni e niente dentro. (" + dove + ")");
                if (uscito != null && uscito != protocolloChiesto)
                    return Fallito(
                        "Hai chiesto " + protocolloChiesto + " ed e' uscito " + uscito +
                        " (FILE_SCHEMA «" + schema + "»). Il file c'e' e non e' il " +
                        "protocollo che serve: certi fornitori accettano solo il 203. (" +
                        dove + ")");
                // L'ASSIEME INTERO NEL FILE (voce Z-D6): un MANIFOLD_SOLID_BREP per
                // corpo, contro i corpi dei pezzi non soppressi. Rileggerlo non si
                // puo' (apre un modale): si contano i solidi nel testo.
                if (assieme != null)
                {
                    int attesi = SolidiAttesiNellAssieme(assieme);
                    int nelFile = SolidiNelloStep(dove);
                    misure["solidi_attesi"] = attesi >= 0 ? (object)attesi : null;
                    misure["solidi_nel_file"] = nelFile >= 0 ? (object)nelFile : null;
                    if (attesi >= 0 && nelFile >= 0 && nelFile < attesi)
                        return Fallito(
                            "Lo STEP dell'assieme ha " + nelFile + " solidi e i pezzi non soppressi ne hanno " +
                            attesi + ": qualcosa non e' arrivato nel file. (" + dove + ")");
                }
            }

            // ── LA PROVA VERA, SE LA SI CHIEDE ───────────────────────────
            //
            // Riaprire il file e confrontare il volume e' l'unica misura che
            // dice che la GEOMETRIA e' sopravvissuta alla traduzione: la
            // dimensione di un file non lo dice, e nemmeno il numero di
            // entita'. Non e' il predefinito perche' costa un'apertura di
            // documento: su un pezzo grosso sono minuti.
            if (Bool(a, "riapri_e_confronta", false))
            {
                // IL CONFRONTO VUOLE DUE VOLUMI LETTI (voce Z-C8): con lo zero
                // di una lettura fallita da questa parte, «il pezzo ne ha 0: la
                // traduzione ha perso qualcosa» era una frase falsa.
                double? mioLetto = VolumeMm3(modello);
                string comE;
                double suo = VolumeRiaprendo(dove, out comE);
                misure["volume_mm3"] = NumeroOppureNull(mioLetto, 2);
                misure["volume_riletto_mm3"] = suo >= 0 ? (object)Math.Round(suo, 2) : null;
                misure["come_e_andata_la_rilettura"] = comE;
                if (suo < 0)
                    return Fallito(
                        "Il file e' scritto ma non sono riuscito a rileggerlo per " +
                        "confrontarlo: " + comE + " (" + dove + ")");
                if (mioLetto == null)
                    return Fallito(
                        "Il file e' scritto e riletto, ma non riesco a misurare il volume del " +
                        "pezzo di partenza: senza, il confronto non si fa. (" + dove + ")");
                double mio = mioLetto.Value;

                // UN MILLESIMO, non «circa». Una traduzione che perde un
                // millesimo di volume ha perso una faccia, e su un pezzo che
                // va in officina non e' un arrotondamento.
                double scarto = mio > 0 ? Math.Abs(suo - mio) / mio : Math.Abs(suo - mio);
                misure["scarto_relativo"] = Math.Round(scarto, 6);
                if (scarto > 0.001)
                    return Fallito(
                        "Il file riaperto ha volume " + suo.ToString("0.##") + " mm3 e il " +
                        "pezzo ne ha " + mio.ToString("0.##") + ": uno scarto del " +
                        (scarto * 100).ToString("0.###") + "%. La traduzione ha perso " +
                        "qualcosa. (" + dove + ")");
            }

            _log("[agente] " + (iges ? "IGES" : "STEP") + ": " + quantoPesa + " byte, " + dove);
            return SenzaFeature(misure);
        }

        private sealed class PreferenzeScambio
        {
            public int StepAp;
            public int IgesRappresentazione;
            public bool IgesSolidiESuperfici;
            // N59: the IGES toggles turned off for the export, as the user had
            // them. Only the ones actually read are here: a toggle whose read
            // failed is not written back, so that a failed read does not turn
            // into «off» in the user's profile.
            public readonly Dictionary<swUserPreferenceToggle_e, bool> IgesTogglesBefore =
                new Dictionary<swUserPreferenceToggle_e, bool>();
        }

        /// <summary>
        /// The IGES toggles the export turns off (N59), one list for read, set
        /// and restore. FileSaveAsIGESOptions.htm: «IGES wireframe (3D curves)»,
        /// «Export 3D Curve features», «Export sketch entities».
        /// </summary>
        private static readonly swUserPreferenceToggle_e[] IgesTogglesTurnedOff =
        {
            swUserPreferenceToggle_e.swIGESExportAsWireframe,
            swUserPreferenceToggle_e.swIGESExportFreeCurves,
            swUserPreferenceToggle_e.swIGESExportSketchEntities,
        };

        private PreferenzeScambio LeggiPreferenzeScambio()
        {
            var p = new PreferenzeScambio();
            try { p.StepAp = _sw.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP); } catch { }
            try { p.IgesRappresentazione = _sw.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swIGESRepresentation); } catch { }
            try { p.IgesSolidiESuperfici = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swIGESExportSolidAndSurface); } catch { }
            foreach (var t in IgesTogglesTurnedOff)
                try { p.IgesTogglesBefore[t] = _sw.GetUserPreferenceToggle((int)t); } catch { }
            return p;
        }

        private void ImpostaPreferenzeStep(int protocollo)
        {
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, protocollo); } catch { }
        }

        // =================================================================
        // STEP AP242: PublishSTEP242File (Z-C21.13, finding C5-7)
        // =================================================================

        /// <summary>
        /// Writes STEP AP242 at <paramref name="path"/> (a .stp path, checked
        /// before by <see cref="Step242PathProblem"/>) through
        /// IModelDocExtension.PublishSTEP242File. Null when the new file is
        /// there, otherwise the refusal.
        ///
        /// A FILE ALREADY AT THE PATH IS MOVED ASIDE, NOT DELETED. SaveAs3's
        /// branch deletes it first; here a refusal is the expected answer on
        /// every machine without the MBD licence, the bench machine included,
        /// and deleting first would destroy the file the user had there each
        /// time (on 0.143.0 it was replaced by an AP203 file, and then the
        /// export was refused). The previous file is renamed next to itself, put
        /// back when nothing new came out, and deleted once the new one is
        /// written.
        /// </summary>
        private string PublishStep242(IModelDoc2 document, string path)
        {
            string aside = null;
            if (File.Exists(path))
            {
                aside = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".previous";
                try { File.Move(path, aside); }
                catch (Exception ex)
                {
                    return "There is already a file at «" + path + "» and I cannot move it aside (" + ex.Message +
                           "): it is probably open in another program. Nothing was written.";
                }
            }

            string refusal;
            try { refusal = WriteStep242(document, path); }
            catch (Exception ex) { refusal = "STEP AP242 was not written: " + ex.Message; }

            if (refusal == null)
            {
                if (aside != null)
                {
                    // A FileInfo and not File.Delete: the obsolete-call contract
                    // cannot type the receiver «File», and counts it undecided.
                    try { new FileInfo(aside).Delete(); }
                    catch (Exception ex) { _log("[agent] STEP AP242: the previous file stays at " + aside + " (" + ex.Message + ")"); }
                }
                return null;
            }

            // NOTHING NEW CAME OUT. Whatever the refused call left at the path is
            // not a checked AP242, and the file that was there goes back.
            if (aside == null)
            {
                if (File.Exists(path))
                    refusal += " The refused call left a file at «" + path + "»: it is not a checked AP242, do not send it.";
                return refusal;
            }
            try
            {
                if (File.Exists(path)) new FileInfo(path).Delete();
                File.Move(aside, path);
                return refusal + " The file that was already at «" + path + "» is back in its place.";
            }
            catch (Exception ex)
            {
                return refusal + " The file that was already at «" + path + "» is now «" + aside +
                       "»: I could not put it back (" + ex.Message + ").";
            }
        }

        /// <summary>
        /// The call itself, with OUR document in front of SOLIDWORKS while it
        /// runs. The page of PublishSTEP242File does not say whether it works on
        /// a document that is not the active one, the probe brought the part to
        /// the front before calling it, and more than one session can work on
        /// the same SOLIDWORKS: so our document goes to the front first, as for
        /// the flat pattern (ExportToDWG2, OperazioniLamiera.cs), and the one
        /// that was in front goes back there.
        ///
        /// The selection is cleared as for SaveAs3 (finding L13-1, whose page
        /// says a selection limits what is exported). This page says nothing
        /// about a selection; a face left selected must not get the chance to
        /// decide what the file holds.
        /// </summary>
        private string WriteStep242(IModelDoc2 document, string path)
        {
            IModelDoc2 previousFront;
            string activationProblem = BringDocumentToFront(document, out previousFront);
            // The one that was in front goes back there, even when bringing ours
            // failed: a failed attempt may have brought another document
            // forward. By REFERENCE, through DocumentByPath (Z-C21.13, pattern
            // S18): it was the title, which can be another document's too.
            Action putBackTheFront = () =>
            {
                if (previousFront == null) return;
                int activationErrors;
                string problem;
                if (!DocumentByPath.ActivateAndReadBack(_sw, previousFront, out activationErrors, out problem))
                    _log("[agent] STEP AP242: " + problem);
            };
            if (activationProblem != null)
            {
                putBackTheFront();
                return "STEP AP242 was not written: " + activationProblem + ".";
            }
            int code;
            try
            {
                document.ClearSelection2(true);
                code = document.Extension.PublishSTEP242File(path);
            }
            catch (Exception ex)
            {
                return "STEP AP242 was not written: PublishSTEP242File raised an exception: " + ex.Message;
            }
            finally { putBackTheFront(); }
            string refusal = Step242Refusal(code, path);
            if (refusal != null) return refusal;
            if (!File.Exists(path))
                return "PublishSTEP242File answered " + code + " (swPublishStep242_Success) and there is no file at «" +
                       path + "».";
            return null;
        }

        /// <summary>
        /// Brings <paramref name="document"/> to the front of SOLIDWORKS. Null
        /// when the active document is ours afterwards, otherwise what went
        /// wrong; <paramref name="previousFront"/> is the document that was in
        /// front, when it was another one, so that the caller can bring it back.
        ///
        /// The same call as BringToFront (OperazioniLamiera.cs) and the bridge's
        /// PortaDavanti: DocumentByPath.ActivateAndReadBack (Z-C21.13, pattern
        /// S18), which gives ActivateDoc3 the file name with extension and then
        /// the full path of a saved part, and the title of a part never saved only when
        /// no other open document answers to it (ISldWorks~ActivateDoc3.html
        /// warns that a title without extension can name a part and an assembly
        /// at once), with swDontRebuildActiveDoc, and reads back the COM
        /// identity of the active document, which needs neither a path nor a
        /// title: a STEP is also written from a part never saved.
        /// </summary>
        private string BringDocumentToFront(IModelDoc2 document, out IModelDoc2 previousFront)
        {
            previousFront = null;
            IModelDoc2 front = null;
            try { front = _sw.ActiveDoc as IModelDoc2; } catch { }
            if (SameComObject(front, document)) return null;
            previousFront = front;
            int activationErrors;
            string problem;
            if (!DocumentByPath.ActivateAndReadBack(_sw, document, out activationErrors, out problem))
                return problem;
            return null;
        }

        /// <summary>
        /// The same COM object? ReferenceEquals first, since .NET almost always
        /// reuses one wrapper for one COM object; when it does not, the IUnknown
        /// pointers, which are the real identity (as EsecutorePiano.StessoOggetto).
        /// Both Release calls are needed: GetIUnknownForObject adds a reference,
        /// and a count that does not go back is a document SOLIDWORKS cannot close.
        /// </summary>
        private static bool SameComObject(object a, object b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            IntPtr pa = IntPtr.Zero, pb = IntPtr.Zero;
            try
            {
                pa = Marshal.GetIUnknownForObject(a);
                pb = Marshal.GetIUnknownForObject(b);
                return pa == pb;
            }
            catch { return false; }
            finally
            {
                if (pa != IntPtr.Zero) Marshal.Release(pa);
                if (pb != IntPtr.Zero) Marshal.Release(pb);
            }
        }

        /// <summary>
        /// Why <paramref name="path"/> cannot take STEP AP242, or null when it
        /// can. IModelDocExtension~PublishSTEP242File.html, Path: «Full qualified
        /// path to which to export the SOLIDWORKS MBD 3D part or assembly; use
        /// .STP for the file name extension»; the probe probe_step_ap242 measured
        /// a .step path answering 1 (swPublishStep242_InvalidPath). Refused here,
        /// before the call, with the reason. No interop in the signature: the
        /// contract group of Z-C21.13 calls it without SOLIDWORKS.
        /// </summary>
        private static string Step242PathProblem(string path)
        {
            string extension;
            try { extension = (Path.GetExtension(path ?? "") ?? "").ToLowerInvariant(); }
            catch (Exception ex) { return "I cannot read the path «" + path + "»: " + ex.Message; }
            if (extension == ".stp") return null;
            if (extension == ".step")
                return "STEP AP242 is written by PublishSTEP242File, and its page asks for a .stp path " +
                       "(«use .STP for the file name extension»): with a .step path it answers " +
                       "swPublishStep242_InvalidPath (measured on 29/9/2026). Ask again with the same path " +
                       "ending in .stp instead of «" + path + "».";
            return "The path «" + path + "» ends in «" + extension + "», and STEP AP242 is written by " +
                   "PublishSTEP242File, which wants a path ending in .stp.";
        }

        /// <summary>
        /// The refusal for an answer of PublishSTEP242File, or null for success.
        /// swStep242Error_e (its page in estratto_swconst): «Status codes for
        /// exporting SOLIDWORKS MBD 3D PDF files to STEP 242»,
        /// swPublishStep242_Success 0, swPublishStep242_InvalidPath 1,
        /// swPublishStep242_UnknownError 3, swPublishStep242_MBDLicenseNotAvailable
        /// 4. A value the enum does not list is not taken for a success. No
        /// interop in the signature: the contract group of Z-C21.13 calls it
        /// without SOLIDWORKS.
        /// </summary>
        private static string Step242Refusal(int code, string path)
        {
            switch (code)
            {
                case (int)swStep242Error_e.swPublishStep242_Success:
                    return null;
                case (int)swStep242Error_e.swPublishStep242_MBDLicenseNotAvailable:
                    return "STEP AP242 is not available on this machine: PublishSTEP242File answered " + code +
                           " (swPublishStep242_MBDLicenseNotAvailable). In SOLIDWORKS 2021 AP242 is written only " +
                           "by SOLIDWORKS MBD («Publish STEP 242 File»), and it needs the SOLIDWORKS MBD licence, " +
                           "which is not available on this machine. AP214 (the default, which also carries the colours) " +
                           "and AP203 (the oldest and the most widely read) are available: ask for one of those.";
                case (int)swStep242Error_e.swPublishStep242_InvalidPath:
                    return "PublishSTEP242File refused the path «" + path + "»: it answered " + code +
                           " (swPublishStep242_InvalidPath). Its page asks for a full path ending in .stp.";
                case (int)swStep242Error_e.swPublishStep242_UnknownError:
                    return "PublishSTEP242File failed without saying why: it answered " + code +
                           " (swPublishStep242_UnknownError), and there is no AP242 file to check.";
                default:
                    return "PublishSTEP242File answered " + code + ", a value swStep242Error_e does not list " +
                           "(0 success, 1 invalid path, 3 unknown error, 4 no MBD licence): I do not take it " +
                           "for a success.";
            }
        }

        /// <returns>
        /// The IGES toggles of <see cref="IgesTogglesTurnedOff"/> that are NOT
        /// off after setting them (Z-C21.10 N59, review): still on, or
        /// unreadable. Empty when all three read back off.
        ///
        /// ISldWorks~SetUserPreferenceToggle.html gives no return value to read
        /// (C# «void SetUserPreferenceToggle(System.int UserPreferenceValue,
        /// System.bool OnFlag)»), so each toggle is read again with
        /// ISldWorks~GetUserPreferenceToggle.html («True if UserPreferenceToggle
        /// is toggled on; false if toggled off»). The census of the file sees a
        /// solid replaced by curves, but not stray sketch entities or 3D curves
        /// written NEXT TO the solid: for those, this read-back is the only
        /// witness, and the answer carries it.
        /// </returns>
        private List<string> ImpostaPreferenzeIges(int rappresentazione)
        {
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swIGESRepresentation, rappresentazione); } catch { }
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swIGESExportSolidAndSurface, true); } catch { }
            // N59: the solid (or the trimmed surfaces), and nothing next to it.
            foreach (var t in IgesTogglesTurnedOff)
                try { _sw.SetUserPreferenceToggle((int)t, false); } catch { }

            var notOff = new List<string>();
            foreach (var t in IgesTogglesTurnedOff)
            {
                bool on = true;   // unreadable is not off
                try { on = _sw.GetUserPreferenceToggle((int)t); } catch { }
                if (on) notOff.Add(t.ToString());
            }
            return notOff;
        }

        private void RimettiPreferenzeScambio(PreferenzeScambio p)
        {
            if (p == null) return;
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, p.StepAp); } catch { }
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swIGESRepresentation, p.IgesRappresentazione); } catch { }
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swIGESExportSolidAndSurface, p.IgesSolidiESuperfici); } catch { }
            foreach (var t in IgesTogglesTurnedOff)
            {
                bool before;
                if (!p.IgesTogglesBefore.TryGetValue(t, out before)) continue;   // never read: left alone
                try { _sw.SetUserPreferenceToggle((int)t, before); } catch { }
            }
        }

        /// <summary>
        /// Il `FILE_SCHEMA` dello STEP, e quante entita' ha la sezione DATA.
        /// Null se il file non comincia per «ISO-10303-21».
        /// </summary>
        private static string SchemaDelloStep(string percorso, out int entita, out string perche)
        {
            entita = -1;
            perche = "";
            try
            {
                string schema = null;
                int quante = 0;
                bool inizioBuono = false, primaRiga = true, dentroIDati = false;
                foreach (var riga in File.ReadLines(percorso))
                {
                    if (primaRiga)
                    {
                        primaRiga = false;
                        inizioBuono = riga.TrimStart().StartsWith("ISO-10303-21", StringComparison.Ordinal);
                        if (!inizioBuono) break;
                    }
                    if (schema == null)
                    {
                        int i = riga.IndexOf("FILE_SCHEMA", StringComparison.Ordinal);
                        if (i >= 0)
                        {
                            int apre = riga.IndexOf('\'', i);
                            int chiude = apre < 0 ? -1 : riga.IndexOf('\'', apre + 1);
                            if (apre >= 0 && chiude > apre)
                                schema = riga.Substring(apre + 1, chiude - apre - 1);
                        }
                    }
                    if (riga.StartsWith("DATA;", StringComparison.Ordinal)) { dentroIDati = true; continue; }
                    if (riga.StartsWith("ENDSEC;", StringComparison.Ordinal)) { dentroIDati = false; continue; }
                    if (dentroIDati && riga.TrimStart().StartsWith("#", StringComparison.Ordinal)) quante++;
                }
                if (!inizioBuono)
                {
                    perche = "il file non comincia per «ISO-10303-21»";
                    return null;
                }
                entita = quante;
                perche = "letto dal file: il FILE_SCHEMA e le righe «#» della sezione DATA";
                return schema;
            }
            catch (Exception ex)
            {
                perche = "non ho potuto leggere il file: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Da `FILE_SCHEMA` al nome del protocollo. I tre nomi sono quelli
        /// dello STANDARD, non di SOLIDWORKS: AP203 si dichiara
        /// «CONFIG_CONTROL_DESIGN», AP214 «AUTOMOTIVE_DESIGN», AP242 col suo
        /// nome lungo.
        /// </summary>
        private static string DalloSchema(string schema)
        {
            if (string.IsNullOrEmpty(schema)) return null;
            string s = schema.ToUpperInvariant();
            if (s.Contains("AP242")) return "AP242";
            if (s.Contains("AUTOMOTIVE_DESIGN")) return "AP214";
            if (s.Contains("CONFIG_CONTROL_DESIGN")) return "AP203";
            return null;
        }

        /// <summary>
        /// Quanti record di PARAMETRI ha l'IGES. Un IGES e' fatto di righe da
        /// ottanta caratteri, e la colonna 73 dice a che sezione appartengono:
        /// S intestazione, G globale, D directory, P parametri, T coda. Un file
        /// senza nemmeno un record «P» ha le intestazioni e niente dentro.
        /// </summary>
        private static int RecordIges(string percorso, out string perche)
        {
            perche = "";
            try
            {
                int quanti = 0;
                foreach (var riga in File.ReadLines(percorso))
                    if (riga.Length >= 73 && (riga[72] == 'P' || riga[72] == 'p')) quanti++;
                perche = "contando i record con «P» nella colonna 73 del file IGES";
                return quanti;
            }
            catch (Exception ex)
            {
                perche = "non ho potuto leggere il file: " + ex.Message;
                return -1;
            }
        }

        /// <summary>IGES 5.3 type 186, Manifold Solid B-Rep Object: the solid of a BREP file (one for the one body of `iges_solidi`).</summary>
        private const int IgesManifoldSolidType = 186;

        /// <summary>
        /// IGES 5.3 surface entity types: plane 108, parametric spline surface
        /// 114, ruled 118, surface of revolution 120, tabulated cylinder 122,
        /// rational B-spline surface 128, offset 140, bounded 143, trimmed 144,
        /// and the analytic surfaces 190-198 (plane, cylinder, cone, sphere,
        /// torus). Faces (510) and shells (514) are topology, not surfaces, and
        /// are left out: a face always points at one of these.
        /// </summary>
        private static readonly HashSet<int> IgesSurfaceTypes =
            new HashSet<int> { 108, 114, 118, 120, 122, 128, 140, 143, 144, 190, 192, 194, 196, 198 };

        /// <summary>
        /// The entities of the IGES by kind (N59), read from its DIRECTORY
        /// ENTRY section: the lines with «D» in column 73, two per entity, the
        /// first of the two carrying the entity type number in columns 1-8.
        ///
        /// Checked by hand on 28/9/2026 against the file `iges_solidi` writes
        /// (block 100 x 100 x 20, BREP, SOLIDWORKS 2021): 106 «D» lines, 53
        /// entities, one of type 186, six of type 190 (plane surface), six 510
        /// faces, one 514 shell, twelve 110 lines. So: solids 1, surfaces 6.
        /// A wireframe file has lines and points and neither of the two.
        /// </summary>
        private static bool IgesEntityCensus(
            string path, out int entities, out int solids, out int surfaces, out string how)
        {
            entities = 0; solids = 0; surfaces = 0;
            try
            {
                int directoryLines = 0;
                foreach (var line in File.ReadLines(path))
                {
                    if (line.Length < 73 || (line[72] != 'D' && line[72] != 'd')) continue;
                    directoryLines++;
                    if (directoryLines % 2 == 0) continue;   // the second line of the entry
                    int type;
                    if (!int.TryParse(line.Substring(0, 8).Trim(), System.Globalization.NumberStyles.Integer,
                                      System.Globalization.CultureInfo.InvariantCulture, out type))
                    {
                        how = "directory line " + directoryLines + " has no entity type in columns 1-8";
                        return false;
                    }
                    entities++;
                    if (type == IgesManifoldSolidType) solids++;
                    else if (IgesSurfaceTypes.Contains(type)) surfaces++;
                }
                if (directoryLines % 2 != 0)
                {
                    how = "the directory section has " + directoryLines + " lines, and an entry takes two";
                    return false;
                }
                how = "from the directory section: the lines with «D» in column 73, the entity type " +
                      "in columns 1-8 of the first line of each entry";
                return true;
            }
            catch (Exception ex)
            {
                how = "could not read the file: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Il volume del file appena scritto, riaperto in SOLIDWORKS e
        /// richiuso. E' l'unica misura che dice che la GEOMETRIA e'
        /// sopravvissuta alla traduzione: la dimensione di un file non lo dice,
        /// e nemmeno il numero di entita'.
        ///
        /// IL DOCUMENTO SI CHIUDE SEMPRE, e alla fine si riattiva quello di
        /// prima: aprire un file e lasciarlo li' vuol dire che l'operazione
        /// dopo lavora su un documento che non e' quello che il modello crede.
        ///
        /// ── `LoadFile4` E NON `OpenDoc6`, E L'ERRORE DICE UN'ALTRA COSA ──
        ///
        /// `OpenDoc6` su uno STEP o un IGES **non apre niente** e risponde
        /// `swFileRequiresRepairError = 2097152` — «il file richiede una
        /// riparazione». Il file non ha niente da riparare: lo abbiamo appena
        /// scritto noi, e `LoadFile4` lo apre senza un avviso. Misurato il
        /// 15/9/2026 su un cubo da 200.000 mm³: `OpenDoc6` con `swDocPART`
        /// 2097152, con `swDocIMPORTED_PART` 1024 («tipo di file non valido»),
        /// `LoadFile4(percorso, "", null)` **aperto, un corpo, 200000 mm³**.
        ///
        /// E' un messaggio che manda a cercare il guasto dalla parte
        /// sbagliata: chi lo legge va a guardare il file, e il file e' a
        /// posto. Per i formati neutri la porta e' `LoadFile4`.
        /// </summary>
        private double VolumeRiaprendo(string percorso, out string comE)
        {
            comE = "";
            IModelDoc2 riaperto = null;
            // The document to go back to, by REFERENCE (Z-C21.13, pattern S18).
            IModelDoc2 previousFront = null;
            try { previousFront = _sw.IActiveDoc2; }
            catch { }
            bool volumeRead = false;
            try
            {
                int errori = 0;
                riaperto = _sw.LoadFile4(percorso, "", null, ref errori) as IModelDoc2;
                if (riaperto == null)
                {
                    comE = "LoadFile4 non ha aperto niente (codice " + errori + ")";
                    return -1;
                }
                double? v = VolumeMm3(riaperto);
                if (v == null)
                {
                    comE = "riaperto, ma il volume della geometria tradotta non si e' lasciato leggere";
                    return -1;
                }
                comE = "riaperto come pezzo e richiuso; volume letto dalla geometria tradotta";
                volumeRead = true;
                return v.Value;
            }
            catch (Exception ex)
            {
                comE = "eccezione riaprendo: " + ex.Message;
                return -1;
            }
            finally
            {
                // BY REFERENCE, AND READ BACK (Z-C21.13, findings M4 and N11).
                // The imported document was never saved, and its title is the
                // stem of the file: the stem of the part itself when the export
                // sits next to it. CloseDoc(that title) could close the ORIGINAL,
                // without saving. When LoadFile4 leaves it in front,
                // DocumentByPath closes it with the empty name, which names the
                // active document; when it does not, its title is used only if
                // no other open document answers to it. The answer says
                // «closed» only if the read back finds it gone.
                if (riaperto != null)
                {
                    var closing = DocumentByPath.CloseAndReadBack(_sw, riaperto);
                    if (!closing.IsClosed)
                    {
                        string notClosed = "the reopened document is NOT closed: " + closing.Message +
                                           "; close it by hand, without saving";
                        comE = volumeRead
                            ? "reopened as a part, volume read from the translated geometry, and " + notClosed
                            : comE + "; " + notClosed;
                        _log("[agent] " + notClosed);
                    }
                    else if (closing.AlsoClosed.Count > 0)
                        _log("[agent] closing the reopened file: " + closing.Message);
                }
                // Back to the document that was in front, by its path, with
                // swDontRebuildActiveDoc: this was ActivateDoc3(title, false, 0),
                // and 0 is swUserDecision, a dialog the call waits on (L13-3).
                if (previousFront != null)
                {
                    int activationErrors;
                    string problem;
                    if (!DocumentByPath.ActivateAndReadBack(_sw, previousFront, out activationErrors, out problem))
                        _log("[agent] after reopening the file: " + problem);
                }
            }
        }
    }
}
