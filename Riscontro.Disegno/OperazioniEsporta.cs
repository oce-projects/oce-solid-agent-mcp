using System;
using System.Collections.Generic;
using System.IO;
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
                    "Questo documento non e' un pezzo: lo STL si esporta da una parte.");

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
                    "Lo STL non e' stato scritto (codice " + errori + "). " +
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
        //   swStepAP (int)                 203, 214, 242 — SENZA un enum
        //   swIGESRepresentation (int)     swIGES_BREP = 3 (solidi),
        //                                  swIGES_TRMSRF = 0 (superfici)
        //   swIGESExportSolidAndSurface    esporta anche i solidi
        //
        // `swStepAP` E' IL CASO PIU' SCOMODO: i tre valori leciti NON hanno un
        // enum in `swconst`, quindi il numero e' un letterale — e un letterale
        // e' una cosa che si crede. Per questo dopo aver scritto il file si
        // **rilegge il suo FILE_SCHEMA**, che dice quale protocollo e'
        // davvero uscito: AP203 si dichiara «CONFIG_CONTROL_DESIGN», AP214
        // «AUTOMOTIVE_DESIGN», AP242 col suo nome lungo. E' l'unico modo di
        // sapere che il 214 chiesto e' il 214 ottenuto.

        private Esito EsportaScambio(
            IModelDoc2 modello, Dictionary<string, object> a, string operazione,
            StatoDocumento stato)
        {
            var parte = modello as IPartDoc;
            if (parte == null)
                return Fallito(
                    "Questo documento non e' un pezzo: STEP e IGES si esportano da una parte.");

            bool iges = operazione == "esporta_iges";
            string[] estensioniBuone = iges
                ? new[] { ".iges", ".igs" }
                : new[] { ".step", ".stp" };

            // LA VERIFICA GIRA LO STESSO, E NON FERMA NIENTE. Il giudizio
            // viaggia con la risposta: chi manda un pezzo a un fornitore ha
            // diritto di sapere che aveva due corpi, non di essere fermato.
            string giudizio = "";
            var verifica = VerificaGeometriaCorpo(modello, new Dictionary<string, object>(), stato);
            if (verifica.Riuscito && verifica.Misure != null && verifica.Misure.ContainsKey("giudizio"))
                giudizio = Convert.ToString(verifica.Misure["giudizio"]);

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

            int protocollo = 0;
            string protocolloChiesto = null;
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
                else if (protocolloChiesto == "AP242") protocollo = 242;
                else return Fallito(
                    "Protocollo «" + protocolloChiesto + "» sconosciuto: ci sono AP203, " +
                    "AP214 (predefinito) e AP242.");
            }

            var prima = LeggiPreferenzeScambio();
            int errori = 0, avvisi = 0;
            bool ok;
            try
            {
                if (iges) ImpostaPreferenzeIges(rappresentazione);
                else ImpostaPreferenzeStep(protocollo);

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
            finally { RimettiPreferenzeScambio(prima); }

            if (!ok || !File.Exists(dove))
                return Fallito("Il file non e' stato scritto (codice " + errori + "): " + dove);

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
            }
            else
            {
                misure["formato"] = "STEP";
                misure["protocollo_chiesto"] = protocolloChiesto;
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
        }

        private PreferenzeScambio LeggiPreferenzeScambio()
        {
            var p = new PreferenzeScambio();
            try { p.StepAp = _sw.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP); } catch { }
            try { p.IgesRappresentazione = _sw.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swIGESRepresentation); } catch { }
            try { p.IgesSolidiESuperfici = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swIGESExportSolidAndSurface); } catch { }
            return p;
        }

        private void ImpostaPreferenzeStep(int protocollo)
        {
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, protocollo); } catch { }
        }

        private void ImpostaPreferenzeIges(int rappresentazione)
        {
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swIGESRepresentation, rappresentazione); } catch { }
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swIGESExportSolidAndSurface, true); } catch { }
        }

        private void RimettiPreferenzeScambio(PreferenzeScambio p)
        {
            if (p == null) return;
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, p.StepAp); } catch { }
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swIGESRepresentation, p.IgesRappresentazione); } catch { }
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swIGESExportSolidAndSurface, p.IgesSolidiESuperfici); } catch { }
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
            string titoloPrima = null;
            try
            {
                var attivo = _sw.IActiveDoc2;
                titoloPrima = attivo == null ? null : attivo.GetTitle();
            }
            catch { }
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
                return v.Value;
            }
            catch (Exception ex)
            {
                comE = "eccezione riaprendo: " + ex.Message;
                return -1;
            }
            finally
            {
                if (riaperto != null)
                {
                    try { _sw.CloseDoc(riaperto.GetTitle()); } catch { }
                }
                if (!string.IsNullOrEmpty(titoloPrima))
                {
                    try { _sw.ActivateDoc3(titoloPrima, false, 0, 0); } catch { }
                }
            }
        }
    }
}
