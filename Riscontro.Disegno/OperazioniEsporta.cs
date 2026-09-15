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
                        try { quanti += (f as IFace2).GetTessTriangleCount(); } catch { }
                    }
                }
                return quanti;
            }
            catch { return -1; }
        }
    }
}
