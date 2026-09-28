using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// Le saldature: barre vere, non estrusioni che ci somigliano.
    ///
    /// IL DIFETTO DA CUI NASCE QUESTO FILE
    /// L'8 settembre 2026 il progettista ha chiesto «disegna un tubolare
    /// 80x80x5 lungo 200» e ha ricevuto un corpo estruso. Ha detto subito che
    /// era sbagliato, e la ragione non e' estetica: senza membro strutturale
    /// NON NASCE LA DISTINTA DI TAGLIO. `Services/DistintaTaglio.cs` sa
    /// leggerla dal 2 settembre, e ha sempre letto il vuoto perche' nessuno la
    /// creava.
    ///
    /// LA SEQUENZA, E PERCHE' STA TUTTA QUI DENTRO
    /// Un membro strutturale vuole uno SCHIZZO 3D con dentro i segmenti lungo
    /// cui corrono le barre, e i segmenti vanno passati a un
    /// IStructuralMemberGroup. Sono tre gesti che devono riuscire tutti e tre:
    /// spezzarli in tre operazioni del piano vorrebbe dire tre conferme per una
    /// cosa sola, e tre modi di restare a meta'. Qui si fa tutto in un passo, e
    /// se qualcosa non riesce si dice cosa e' rimasto nel documento.
    ///
    /// FIRME VERIFICATE PER RIFLESSIONE il 9 settembre 2026 sull'interop
    /// 29.5.1.1 (SOLIDWORKS 2021 SP5):
    ///   IFeatureManager.InsertStructuralWeldment5(String Path, Int32
    ///       ConnectedSegmentsOption, Boolean AllowProtrusion, Object Groups,
    ///       String ConfigurationName) -> Feature
    ///   IFeatureManager.CreateStructuralMemberGroup() -> StructuralMemberGroup
    ///   IFeatureManager.InsertWeldmentTrimFeature2(Int32 EndCond, Int32
    ///       Options, Double GapValue, Object BodiesToTrim, Object
    ///       BodiesOrFaces) -> Feature
    ///   IFeatureManager.InsertWeldmentCutList2(Object Bodies) -> Feature
    ///   IFeatureManager.InsertEndCapFeature3(...10 parametri...) -> Feature
    ///   IFeatureManager.InsertCosmeticWeldBead2(Int32 WeldMode, Object
    ///       WeldFromFaceOrEdgeSet, Object WeldToFaceOrEdgeSet, Double
    ///       WeldSize) -> Object
    ///   ISketchManager.Insert3DSketch(Boolean UpdateEditRebuild)
    ///   IStructuralMemberGroup: Segments, Angle, ApplyCornerTreatment,
    ///       CornerTreatmentType, GapWithinGroup, GapForOtherGroups,
    ///       LocateProfilePoint, MiterMergeCondition
    ///   swSolidworksWeldmentEndCondOptions_e: None=0, Miter=1, Butt1=2,
    ///       Butt2=3, Trim=4, UseDefault=5
    ///
    /// UNITA': tutto cio' che arriva dal piano e' in MILLIMETRI. La conversione
    /// la fa Unita, e solo Unita. Un millimetro passato per metro da' un telaio
    /// mille volte piu' grande, senza nessun errore.
    /// </summary>
    public partial class OperazioniSw
    {
        // ─────────────────────────────────────────────────────────────────
        // Membri strutturali
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// C'e' gia' un documento aperto su quel file?
        ///
        /// Il confronto e' sul percorso, normalizzato: SolidWorks scrive i
        /// percorsi con le barre all'indietro e chi manda il piano puo'
        /// scriverli in un altro modo. Un confronto fra stringhe grezze
        /// direbbe di no su due scritture dello stesso file, e allora si
        /// chiuderebbe una libreria che era di qualcun altro — cioe'
        /// esattamente il danno che questo controllo evita.
        /// </summary>
        /// <summary>
        /// Com'era la libreria prima che la toccassimo. Tre stati e non due,
        /// e la differenza e' quella che mancava:
        ///
        ///   Chiusa       — non c'era: la si richiude senza pensarci
        ///   ApertaPulita — c'era e nessuno l'aveva modificata
        ///   ApertaSporca — c'era e qualcuno ci stava gia' lavorando
        /// </summary>
        private enum ComEra { Chiusa, ApertaPulita, ApertaSporca }

        private ComEra StatoDellaLibreria(string percorso)
        {
            var doc = DocumentoConPercorso(percorso);
            if (doc == null) return ComEra.Chiusa;
            bool sporca = false;
            try { sporca = doc.GetSaveFlag(); } catch { }
            return sporca ? ComEra.ApertaSporca : ComEra.ApertaPulita;
        }

        private IModelDoc2 DocumentoConPercorso(string percorso)
        {
            if (string.IsNullOrWhiteSpace(percorso)) return null;
            try
            {
                var voluto = percorso.Trim().Replace('/', '\\');
                var d = _sw.GetFirstDocument() as IModelDoc2;
                int guardia = 0;
                while (d != null && guardia++ < 200)
                {
                    string suo = "";
                    try { suo = (d.GetPathName() ?? "").Trim().Replace('/', '\\'); } catch { }
                    if (!string.IsNullOrWhiteSpace(suo) &&
                        string.Equals(suo, voluto, StringComparison.OrdinalIgnoreCase))
                        return d;
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Richiude il file di libreria, se e' stato questo inserimento ad
        /// aprirlo. Non salva MAI: `CloseDoc` scarta le modifiche, e le
        /// modifiche qui sono soltanto lo sporco che l'inserimento lascia.
        ///
        /// Non solleva e non fa fallire l'operazione: il membro strutturale
        /// e' gia' nel pezzo, e un problema nel richiudere la libreria non
        /// deve trasformare un lavoro riuscito in un errore. Si scrive nel
        /// log e si va avanti.
        /// </summary>
        private void RichiudiLaLibreria(string profilo, ComEra comEra)
        {
            var nome = Path.GetFileName(profilo);

            // ERA APERTA E QUALCUNO CI STAVA LAVORANDO: non si tocca.
            // Era gia' a rischio prima di noi, e chiuderla vorrebbe dire
            // buttare via il lavoro di qualcun altro.
            if (comEra == ComEra.ApertaSporca)
            {
                _log("[agente] la libreria «" + nome + "» era gia' aperta E " +
                     "modificata prima di questo inserimento: non la tocco. " +
                     "ATTENZIONE: un salvataggio la riscriverebbe, e non e' " +
                     "colpa di questa operazione.");
                return;
            }

            var doc = DocumentoConPercorso(profilo);
            if (doc == null) return;

            string titolo = "";
            try { titolo = doc.GetTitle(); } catch { }
            if (string.IsNullOrWhiteSpace(titolo)) return;

            // ERA APERTA E PULITA, E ADESSO E' SPORCA.
            //
            // Qui si chiude lo stesso, ed e' un cambio di idea rispetto a
            // prima. La versione precedente diceva «era gia' aperta, la
            // lascio com'era» — ma «com'era» voleva dire PULITA, e
            // l'inserimento l'ha sporcata. Conservare lo stato aperto e non
            // quello pulito lascia esattamente il rischio che tutto questo
            // esiste per togliere: un file condiviso a un Ctrl+S di distanza
            // dall'essere riscritto.
            //
            // Chiudendola non si perde niente — era pulita — e chi ce
            // l'aveva aperta la riapre com'era. Il flag non si puo'
            // ripulire: `SetSaveFlag()` non prende argomenti, sporca e basta.
            bool eraAperta = comEra == ComEra.ApertaPulita;
            bool sporcaAdesso = false;
            try { sporcaAdesso = doc.GetSaveFlag(); } catch { }

            if (eraAperta && !sporcaAdesso)
            {
                _log("[agente] la libreria «" + titolo + "» era gia' aperta e " +
                     "l'inserimento non l'ha sporcata: la lascio dov'era.");
                return;
            }

            try
            {
                _sw.CloseDoc(titolo);
                _log(eraAperta
                    ? "[agente] richiusa senza salvare la libreria «" + titolo +
                      "»: era gia' aperta ma PULITA, e l'inserimento l'ha " +
                      "sporcata. Lasciarla li' vorrebbe dire lasciarla a un " +
                      "Ctrl+S dall'essere riscritta. Non hai perso niente: " +
                      "riaprila e la ritrovi identica."
                    : "[agente] richiusa senza salvare la libreria «" + titolo +
                      "»: l'inserimento l'aveva aperta e lasciata sporca");
            }
            catch (Exception ex)
            {
                _log("[agente] non riesco a richiudere la libreria «" + titolo + "»: " +
                     ex.Message + ". ATTENZIONE: resta aperta e modificata, un salvataggio la riscriverebbe.");
            }
        }

        private Esito MembroStrutturale(IModelDoc2 modello, Dictionary<string, object> a)
        {
            string profilo = Str(a, "profilo_percorso", "");
            string configurazione = Str(a, "configurazione", "");
            if (string.IsNullOrWhiteSpace(profilo))
                return Fallito("Manca il percorso del profilo di saldatura.");
            if (string.IsNullOrWhiteSpace(configurazione))
                return Fallito("Manca la misura (configurazione) del profilo.");

            // IL CONTROLLO CHE IL SERVER NON PUO' FARE.
            //
            // Il server ha un indice di nomi mandato da una postazione, che
            // puo' essere un'altra e puo' essere di ieri. Qui c'e' il disco
            // vero. Se il file non c'e', InsertStructuralWeldment5 non solleva:
            // ritorna null, e senza questo controllo l'utente leggerebbe «il
            // membro non e' stato creato» senza sapere che il profilo era il
            // problema.
            // LA LIBRERIA ERA GIA' APERTA PRIMA DI NOI?
            //
            // Serve saperlo ADESSO, perche' dopo non si distingue piu' e la
            // differenza conta: se il progettista stava lavorando sul file
            // di libreria, chiuderglielo sotto e' un danno. Si richiude solo
            // cio' che si e' aperto.
            var libreriaComEra = StatoDellaLibreria(profilo);

            if (!File.Exists(profilo))
                return Fallito(
                    "Il profilo «" + profilo + "» non esiste su questa postazione. " +
                    "O il percorso e' sbagliato, o la libreria dei profili non e' " +
                    "raggiungibile da qui.");

            // E la configurazione? Stessa storia, peggiore: se la misura non
            // c'e', SolidWorks NON fallisce — usa la configurazione attiva del
            // profilo. Il pezzo esce con una parete diversa da quella
            // confermata, il rebuild e' pulito, e nessuno se ne accorge fino
            // all'officina.
            try
            {
                var disponibili = _sw.GetConfigurationNames(profilo) as string[];
                if (disponibili != null && disponibili.Length > 0)
                {
                    if (!disponibili.Contains(configurazione, StringComparer.Ordinal))
                    {
                        var simile = disponibili.FirstOrDefault(
                            x => string.Equals(x, configurazione, StringComparison.OrdinalIgnoreCase));
                        string aiuto = simile != null
                            ? " Nel file quella misura si scrive «" + simile + "»: " +
                              "maiuscole e virgole contano."
                            : " Misure disponibili: " +
                              string.Join(", ", disponibili.Take(12)) +
                              (disponibili.Length > 12 ? " ..." : "");
                        return Fallito(
                            "Il profilo non ha la misura «" + configurazione + "»." + aiuto);
                    }
                }
            }
            catch (Exception ex)
            {
                // Non poter controllare non e' come aver controllato: si va
                // avanti, ma si scrive nel log che il controllo e' saltato.
                _log("[agente] non ho potuto verificare le misure del profilo: " + ex.Message);
            }

            // I SEGMENTI SI POSSONO NON AVERE, SE C'E' UN TELAIO.
            //
            // Un telaio rettangolare e' descritto da due quote e un piano: i
            // quattro segmenti sono una conseguenza, non un dato. Il server
            // li calcola e li manda insieme, quindi da quella parte non si
            // notava; dal ponte l'operazione rispondeva «Il piano non porta
            // nessun segmento» senza nemmeno guardare il telaio — cioe'
            // chiedeva di calcolare a mano cio' che sa fare da sola, e lo
            // chiedeva con un messaggio che non lo diceva.
            //
            // Chi li porta lo stesso viene servito: sono il ripiego per chi
            // vuole le quote d'asse invece dell'ingombro esterno.
            var telaioChiesto = TelaioChiesto(a);
            var segmenti = SegmentiDa(a, out string motivo);
            if (segmenti == null && telaioChiesto == null) return Fallito(motivo);

            double rotazione = Num(a, "rotazione_gradi", 0);
            double gioco = Num(a, "gioco_mm", 0);
            string trattamento = Str(a, "trattamento_angolo", "");

            // ── IL TELAIO A QUOTE ESTERNE ────────────────────────────────
            //
            // IL DIFETTO. Il 9 settembre 2026 il progettista ha chiesto un
            // telaio 1000x800 in tubolare 80x80x5 e sul pezzo ha misurato
            // 1080x880. Il percorso di un membro strutturale e' la LINEA
            // D'ASSE e SolidWorks centra il profilo su di essa: un percorso
            // sugli spigoli di 1000x800 fa sporgere le barre di 40 mm per
            // lato. Il riassunto, intanto, prometteva «1000x800».
            //
            // PERCHE' IL CONTO SI FA QUI E NON SUL SERVER. Per arretrare il
            // percorso serve sapere di quanto il profilo sporge, e quello sta
            // nel file .sldlfp su questa postazione. Il server ha solo un
            // indice di nomi. Il nome della configurazione non e' una fonte:
            // nella stessa libreria convivono «80X80X3» e «20x20x1,5», e un IPE
            // 200 e' 200x100. E non basta nemmeno misurare una barra isolata:
            // dentro il gruppo del telaio il profilo si orienta diversamente
            // (un 80X50X5 sporgeva 50 invece di 80). Percio'
            // SezioneProfilo costruisce un TELAIO di prova con le
            // stesse opzioni e misura di quanto sporge.
            //
            // Il server manda `telaio` INSIEME ai segmenti gia' calcolati sul
            // nominale: quelli servono al riassunto e restano il ripiego se
            // qualcuno chiede esplicitamente le quote d'asse.
            double[] ingombroPromesso = null;
            double[] sporgenze = null;
            int asseU = -1, asseV = -1;
            var telaio = telaioChiesto;
            string comeSiMisura = Str(telaio, "misure", "ingombro");

            // ── IL TELAIO A INTERASSE ────────────────────────────────────
            //
            // Le due quote sono la LINEA D'ASSE del percorso, non l'ingombro
            // finito: e' il caso di chi ragiona sugli interassi — fori,
            // piastre, attacchi — e la sporgenza del profilo se la tiene.
            // Il percorso e' allora il rettangolo nominale, senza
            // arretramento.
            //
            // Il catalogo lo documenta da sempre fra i valori ammessi di
            // «misure». Il C# no: cadeva nel ripiego dei segmenti, che pero'
            // questa operazione esiste apposta per non far scrivere.
            if (telaio != null && segmenti == null &&
                !string.Equals(comeSiMisura, "ingombro", StringComparison.Ordinal))
            {
                string pianoAsse = Str(telaio, "piano", "frontale");
                int aU, aV;
                if (!AssiDelPiano(pianoAsse, out aU, out aV))
                    return Fallito("Non conosco il piano «" + pianoAsse + "» per un telaio.");

                double lungoAsse = Num(telaio, "lunghezza_mm", 0);
                double largoAsse = Num(telaio, "larghezza_mm", 0);
                if (lungoAsse <= 0 || largoAsse <= 0)
                    return Fallito("Il telaio non ha due quote utilizzabili.");

                segmenti = SegmentiTelaio(
                    lungoAsse, largoAsse, aU, aV,
                    Bool(telaio, "centrato", true), new[] { 0.0, 0.0 });
                _log(string.Format(
                    "[agente] telaio a interasse {0:0.#}x{1:0.#}: il percorso e' " +
                    "quello, e l'ingombro finito sara' piu' grande di quanto " +
                    "sporge il profilo.", lungoAsse, largoAsse));
            }

            if (telaio != null &&
                string.Equals(comeSiMisura, "ingombro", StringComparison.Ordinal))
            {
                string piano = Str(telaio, "piano", "frontale");
                if (!AssiDelPiano(piano, out asseU, out asseV))
                    return Fallito("Non conosco il piano «" + piano + "» per un telaio.");

                double lungo = Num(telaio, "lunghezza_mm", 0);
                double largo = Num(telaio, "larghezza_mm", 0);
                if (lungo <= 0 || largo <= 0)
                    return Fallito("Il telaio non ha due quote utilizzabili.");

                int codiceAngolo;
                if (!TrattamentiAngolo.TryGetValue(trattamento ?? "", out codiceAngolo))
                    codiceAngolo = 0;
                sporgenze = SezioneProfilo.SporgenzeDelTelaio(
                    _sw, profilo, configurazione, asseU, asseV, rotazione,
                    codiceAngolo, gioco, _log);
                if (sporgenze == null)
                    return Fallito(
                        "Non sono riuscito a misurare quanto sporge il profilo «" +
                        Path.GetFileNameWithoutExtension(profilo) + "» " + configurazione +
                        ", e senza quella misura non so di quanto arretrare il percorso: " +
                        "un telaio costruito lo stesso uscirebbe piu' grande di quanto " +
                        "promesso. Meglio fermarsi.");

                // Una sezione piu' larga del telaio non e' un telaio: le barre
                // si compenetrerebbero e il percorso avrebbe lunghezza nulla o
                // negativa. Va detto prima, non lasciato fallire dentro
                // SolidWorks con un messaggio che non spiega niente.
                if (sporgenze[0] >= lungo - 0.01 || sporgenze[1] >= largo - 0.01)
                    return Fallito(string.Format(
                        "Con questo profilo il telaio sporge di {0:0.#}x{1:0.#} mm oltre " +
                        "il percorso: un telaio di {2:0.#}x{3:0.#} mm di ingombro esterno " +
                        "non ci sta. Serve un telaio piu' grande o un profilo piu' piccolo.",
                        sporgenze[0], sporgenze[1], lungo, largo));

                segmenti = SegmentiTelaio(
                    lungo - sporgenze[0], largo - sporgenze[1],
                    asseU, asseV, Bool(telaio, "centrato", true), sporgenze);
                ingombroPromesso = new[] { lungo, largo };
                _log(string.Format(
                    "[agente] telaio a quote esterne {0:0.#}x{1:0.#}: sporgenza misurata " +
                    "{2:0.##}x{3:0.##}, percorso arretrato a {4:0.##}x{5:0.##}",
                    lungo, largo, sporgenze[0], sporgenze[1],
                    lungo - sporgenze[0], largo - sporgenze[1]));
            }

            // SE ARRIVATI QUI I SEGMENTI ANCORA NON CI SONO, il telaio c'era
            // ma non era utilizzabile — «misure» diverso da «ingombro», o un
            // piano che non si riconosce. Si dice quello, non «manca il
            // percorso»: chi ha scritto un telaio ha chiesto un telaio.
            if (segmenti == null)
                return Fallito(
                    "Hai chiesto un telaio ma non ho potuto ricavarne il " +
                    "percorso, e non mi hai dato dei segmenti su cui ripiegare. " +
                    "Un telaio vuole «lunghezza_mm», «larghezza_mm» e «piano»; " +
                    "se invece vuoi dare tu il percorso, passa «segmenti».");

            // ── lo schizzo 3D del percorso ──────────────────────────────
            modello.ClearSelection2(true);
            var schizzi = modello.SketchManager;
            var creati = new List<ISketchSegment>();
            try
            {
                schizzi.Insert3DSketch(true);
                foreach (var s in segmenti)
                {
                    var seg = schizzi.CreateLine(
                        Unita.M(s.Da[0]), Unita.M(s.Da[1]), Unita.M(s.Da[2]),
                        Unita.M(s.A[0]), Unita.M(s.A[1]), Unita.M(s.A[2])) as ISketchSegment;
                    if (seg == null)
                    {
                        try { schizzi.Insert3DSketch(true); } catch { }
                        return Fallito(
                            "Non sono riuscito a disegnare il segmento da (" +
                            Terna(s.Da) + ") a (" + Terna(s.A) + ")." + SenzaLoSchizzo(modello));
                    }
                    creati.Add(seg);
                }
                schizzi.Insert3DSketch(true);
            }
            catch (Exception ex)
            {
                try { schizzi.Insert3DSketch(true); } catch { }
                return Fallito("Lo schizzo del percorso non e' riuscito: " + ex.Message);
            }

            if (creati.Count == 0)
                return Fallito("Nessun segmento disegnato: non c'e' percorso su cui mettere le barre.");

            // ── il gruppo ────────────────────────────────────────────────
            IStructuralMemberGroup gruppo;
            try
            {
                gruppo = modello.FeatureManager.CreateStructuralMemberGroup()
                    as IStructuralMemberGroup;
            }
            catch (Exception ex)
            {
                return Fallito("Non sono riuscito a creare il gruppo: " + ex.Message + SenzaLoSchizzo(modello));
            }
            if (gruppo == null)
                return Fallito("Non sono riuscito a creare il gruppo di membri." + SenzaLoSchizzo(modello));

            try
            {
                // I SEGMENTI VANNO PASSATI COME SketchSegment[], NON object[].
                //
                // La firma COM dice Object, e sembra che vada bene qualunque
                // array. Non e' cosi': misurato il 9 settembre 2026 su
                // SOLIDWORKS 2021 SP5, con object[] il gruppo resta a ZERO
                // segmenti — GetSegmentsCount() lo dice — e
                // InsertStructuralWeldment5 riceve un gruppo vuoto e torna
                // null senza spiegare niente. Con l'array tipizzato i
                // segmenti entrano.
                gruppo.Segments = creati.Cast<SketchSegment>().ToArray();
                if (Math.Abs(rotazione) > 1e-9) gruppo.Angle = Unita.Rad(rotazione);

                int codice;
                if (TrattamentiAngolo.TryGetValue(trattamento ?? "", out codice))
                {
                    // «nessuno» non e' un trattamento: e' l'assenza. Chiederlo a
                    // SolidWorks come tipo 0 CON ApplyCornerTreatment acceso
                    // darebbe un risultato diverso da non chiederlo affatto.
                    gruppo.ApplyCornerTreatment = codice != 0;
                    if (codice != 0) gruppo.CornerTreatmentType = codice;
                }
                if (gioco > 0)
                {
                    gruppo.GapWithinGroup = Unita.M(gioco);
                    gruppo.GapForOtherGroups = Unita.M(gioco);
                }
            }
            catch (Exception ex)
            {
                return Fallito("Non sono riuscito a impostare il gruppo: " + ex.Message + SenzaLoSchizzo(modello));
            }

            // ── il membro strutturale ────────────────────────────────────
            IFeature feature;
            try
            {
                // ConnectedSegmentsOption = 1, e il numero e' MISURATO.
                //
                // Con 0 — il valore che passava la 0.29.0 — la feature non
                // nasce: InsertStructuralWeldment5 torna null, senza
                // eccezione e senza messaggio d'errore (GetErrorMessages non
                // dice niente). Provati tutti i valori il 9 settembre 2026 su
                // SOLIDWORKS 2021 SP5: 0 e 3 danno null, 1 e 2 costruiscono, e
                // su un percorso chiuso di quattro segmenti danno tutti e due
                // QUATTRO corpi — una barra per segmento, che e' quello che
                // serve alla distinta di taglio.
                //
                // Anche i gruppi vanno tipizzati, per la stessa ragione dei
                // segmenti qui sopra.
                feature = modello.FeatureManager.InsertStructuralWeldment5(
                    Path: profilo,
                    ConnectedSegmentsOption: 1,
                    AllowProtrusion: false,
                    Groups: new[] { (StructuralMemberGroup)gruppo },
                    ConfigurationName: configurazione) as IFeature;
            }
            catch (Exception ex)
            {
                RichiudiLaLibreria(profilo, libreriaComEra);
                return Fallito("Il membro strutturale non e' riuscito: " + ex.Message + SenzaLoSchizzo(modello));
            }

            // SI RICHIUDE SUBITO, RIUSCITO O NO.
            //
            // Inserendo un membro strutturale SolidWorks apre da solo il file
            // di libreria del profilo e lo lascia marcato come MODIFICATO.
            // Non e' una nostra chiamata: succede anche inserendolo a mano.
            //
            // Finche' resta cosi', un Ctrl+S distratto di chiunque riscrive
            // la libreria dei profili dell'azienda — un file che sta su un
            // disco condiviso e da cui dipendono tutti i saldati gia' fatti.
            // Il danno non si vede il giorno stesso: si vede quando un
            // telaio vecchio si ricostruisce con una sezione diversa.
            RichiudiLaLibreria(profilo, libreriaComEra);

            if (feature == null)
                return Fallito(
                    "Il membro strutturale non e' stato creato. Di solito e' il " +
                    "percorso: segmenti che non si toccano, o che si incrociano " +
                    "in un modo che il profilo non riesce a girare." + SenzaLoSchizzo(modello));

            double lunghezza = segmenti.Sum(s => Distanza(s.Da, s.A));
            var misure = new Dictionary<string, object>
            {
                ["profilo"] = Path.GetFileNameWithoutExtension(profilo),
                ["configurazione"] = configurazione,
                ["barre"] = segmenti.Count,
                ["lunghezza_percorso_mm"] = Math.Round(lunghezza, 2),
                ["trattamento_angolo"] = string.IsNullOrEmpty(trattamento) ? "non detto" : trattamento,
            };
            if (sporgenze != null)
                misure["sporgenza_profilo_mm"] =
                    Math.Round(sporgenze[0], 2) + "x" + Math.Round(sporgenze[1], 2);

            // ── SI MISURA CIO' CHE E' STATO FATTO ────────────────────────
            //
            // La sezione misurata su una barra di prova e il pezzo vero sono
            // due cose diverse: fra loro ci sono il trattamento d'angolo, il
            // gioco, la rotazione del profilo e le scelte che SolidWorks fa da
            // solo. Promettere un ingombro e non ricontrollarlo e' esattamente
            // il difetto del 9 settembre, spostato di un passo.
            //
            // Quindi: si legge la scatola dei corpi che QUESTA feature ha
            // creato — non del documento, che puo' contenerne altri — e se non
            // torna il passo FALLISCE dicendo i due numeri. Un pezzo sbagliato
            // che si annulla con Ctrl+Z costa un minuto; uno che arriva in
            // officina costa una carpenteria.
            var scatola = IngombroDeiCorpi(feature);
            if (scatola != null)
            {
                misure["ingombro_mm"] = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.##}x{1:0.##}x{2:0.##}",
                    scatola[3] - scatola[0], scatola[4] - scatola[1], scatola[5] - scatola[2]);
            }
            if (ingombroPromesso != null)
            {
                if (scatola == null)
                {
                    misure["ingombro_verificato"] = false;
                    _log("[agente] non ho potuto misurare l'ingombro del telaio appena fatto.");
                }
                else
                {
                    double vistoU = scatola[3 + asseU] - scatola[asseU];
                    double vistoV = scatola[3 + asseV] - scatola[asseV];
                    double erroreU = Math.Abs(vistoU - ingombroPromesso[0]);
                    double erroreV = Math.Abs(vistoV - ingombroPromesso[1]);
                    if (erroreU > 0.5 || erroreV > 0.5)
                        return Fallito(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "Il telaio doveva essere {0:0.#}x{1:0.#} mm di ingombro esterno " +
                            "ed e' venuto {2:0.##}x{3:0.##}. Non te lo lascio passare per " +
                            "buono: ritiralo — " + Frasi.CosiSiRitira +
                            ". (Sporgenza misurata del " +
                            "profilo: {4:0.##}x{5:0.##} mm.)",
                            ingombroPromesso[0], ingombroPromesso[1], vistoU, vistoV,
                            sporgenze[0], sporgenze[1]));
                    misure["ingombro_verificato"] = true;
                    misure["ingombro_esterno_mm"] =
                        Math.Round(vistoU, 2) + "x" + Math.Round(vistoV, 2);
                }
            }
            return Riuscito(feature, misure);
        }

        // ─────────────────────────────────────────────────────────────────
        // Lavorazioni dopo le barre
        // ─────────────────────────────────────────────────────────────────

        private Esito TagliaMembri(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            int nDa, nContro;
            string perche;
            var daTagliare = CorpiDalRiferimento(a, "corpi_da_tagliare_rif", stato, out nDa, out perche);
            if (daTagliare == null) return Fallito(perche);
            var contro = CorpiDalRiferimento(a, "corpi_di_taglio_rif", stato, out nContro, out perche);
            if (contro == null) return Fallito(perche);

            int codice;
            if (!TrattamentiAngolo.TryGetValue(Str(a, "condizione", "taglio"), out codice))
                codice = 4; // swEndConditionTrim
            double gioco = Num(a, "gioco_mm", 0);

            // IL BIT DEL GIOCO SI ACCENDE SEMPRE, ANCHE CON ZERO (voce Z-C21.2,
            // rilievo L9-2).
            //
            // IFeatureManager~InsertWeldmentTrimFeature2.html, Remarks: «If you
            // include swWeldmentTrimExtendOption_WeldGap in the Options parameter,
            // then the GapValue parameter is used. Specify 0 for GapValue to
            // ensure that there is no weld gap. If you exclude
            // swWeldmentTrimExtendOption_WeldGap from the Options parameter, then
            // the weld gap defaults to the last value specified in the SOLIDWORKS
            // user-interface.»
            //
            // Il commento che stava qui ragionava al contrario: temeva un
            // GapValue positivo ignorato, e accendeva il bit solo con `gioco > 0`.
            // Il danno vero era l'altro: con `gioco_mm` a 0 — il predefinito — il
            // bit restava spento e SOLIDWORKS usava l'ultimo gioco dato a mano
            // da Accorcia/Estendi, mentre il riassunto diceva `gioco_mm: 0`. Il
            // ponte gira sullo stesso SOLIDWORKS del lavoro vero: basta che chi
            // disegna abbia usato un gioco da interfaccia perche' le chiamate dopo
            // lo ereditino. Il caso `il_taglio_senza_gioco_non_eredita` lo guarda.
            int opzioni = (int)swWeldmentTrimExtendOptionType_e
                .swWeldmentTrimExtendOption_WeldGap;

            // GLI ARRAY VANNO TIPIZZATI, e non e' pignoleria: e' il difetto per
            // cui questa operazione ha mentito dalla nascita fino alla libreria
            // 0.113.0. Misurato il 25/9/2026 su una T di due tubolari 80x80x5
            // (la seconda barra entra di 40 mm nella prima):
            //
            //   object[]         -> torna una feature «Accorcia/Estendi1», che
            //                       NELL'ALBERO NON C'E'; volume invariato
            //   Body2[]          -> la feature c'e', volume -58798,23 mm3
            //   DispatchWrapper[]-> uguale a Body2[]
            //
            // 58798,23 = 40 mm x 1469,956 mm2, la sezione del tubo: il taglio
            // giusto, al centesimo. Con `object[]` l'interop impacchetta un
            // array di VARIANT e SOLIDWORKS ne fa una feature fantasma senza
            // dire niente. Anche su `condizione: testa1`, e con le opzioni di
            // estensione accese: sempre fantasma.
            //
            // `distinta_taglio` passava gia' `Body2[]` ed e' per questo che
            // funzionava.
            Body2[] daTagliareTipizzati, controTipizzati;
            try
            {
                daTagliareTipizzati = daTagliare.Cast<Body2>().ToArray();
                controTipizzati = contro.Cast<Body2>().ToArray();
            }
            catch (InvalidCastException)
            {
                return Fallito("Fra i corpi indicati c'e' qualcosa che non e' un corpo solido.");
            }

            double? volumePrima = VolumeMm3(modello);
            int featurePrima = ContaFeatureDellAlbero(modello);
            if (volumePrima == null || featurePrima < 0) return Fallito(VolumeNonLettoPrima("tagliare le barre"));

            IFeature feature;
            try
            {
                feature = modello.FeatureManager.InsertWeldmentTrimFeature2(
                    EndCond: codice,
                    Options: opzioni,
                    GapValue: Unita.M(gioco),
                    BodiesToTrim: daTagliareTipizzati,
                    BodiesOrFaces: controTipizzati) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("Il taglio fra le barre non e' riuscito: " + ex.Message);
            }
            if (feature == null)
                return Fallito(
                    "Il taglio non e' stato creato. Di solito le barre da " +
                    "tagliare e quelle di taglio non si incontrano davvero.");

            // LA FEATURE TORNATA NON BASTA: bisogna che sia nell'albero e che
            // il pezzo sia cambiato. E' la guardia che avrebbe preso il
            // difetto qui sopra il primo giorno — l'operazione diceva
            // «corpi_tagliati: 1» su un pezzo identico a prima.
            try { modello.EditRebuild3(); } catch { }
            int featureDopo = ContaFeatureDellAlbero(modello);
            // Con l'albero illeggibile non si puo' dire che la feature non c'e':
            // decide il volume, qui sotto (voce Z-C8).
            bool nellAlbero = featureDopo < 0 || featureDopo > featurePrima;
            double? volumeDopo = VolumeMm3(modello);
            if (!nellAlbero)
                // Con gli array tipizzati succede ancora, e il 25/9/2026 e'
                // successo sempre e solo quando le barre non si incontrano: due
                // parallele a 200 mm, o una barra lontana. E' la risposta di
                // SOLIDWORKS a «non c'e' niente da tagliare».
                return Fallito(
                    "SOLIDWORKS ha risposto con una feature «" + feature.Name + "» che " +
                    "nell'albero non c'e', e il pezzo e' quello di prima: di solito vuol " +
                    "dire che le barre da tagliare non incontrano quelle di taglio. Non " +
                    "ti dico che le barre sono tagliate.");
            if (volumeDopo == null)
                return Riuscito(feature, new Dictionary<string, object>
                {
                    ["corpi_tagliati"] = nDa,
                    ["corpi_di_taglio"] = nContro,
                    ["gioco_mm"] = gioco,
                    ["variazione_volume_mm3"] = null,
                    ["volume_non_riletto"] = NotaVolumeNonRiletto,
                });
            double variazione = volumeDopo.Value - volumePrima.Value;
            if (Math.Abs(variazione) < 0.01)
                return Fallito(
                    "Il taglio e' nell'albero ma non ha cambiato niente: volume " +
                    volumePrima.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    " mm3 prima e dopo. Le barre da tagliare non entrano in quelle di " +
                    "taglio, o gia' ci arrivano esatte. Non ti confermo un pezzo diverso " +
                    "da quello che ti ho mostrato — " + Frasi.CosiSiRitira + ".");

            return Riuscito(feature, new Dictionary<string, object>
            {
                ["corpi_tagliati"] = nDa,
                ["corpi_di_taglio"] = nContro,
                ["gioco_mm"] = gioco,
                // Negativo = tolto (taglio), positivo = aggiunto (estensione).
                ["variazione_volume_mm3"] = Math.Round(variazione, 2),
            });
        }

        /// <summary>
        /// Quante feature ha l'albero, al primo livello. Serve a una cosa sola:
        /// sapere se una feature che SOLIDWORKS ha restituito ci e' entrata
        /// davvero (vedi `TagliaMembri`). -1 se non si e' potuto leggere (voce
        /// Z-C8): un conteggio a meta' faceva dire «la feature nell'albero non
        /// c'e'» a un taglio riuscito.
        /// </summary>
        private static int ContaFeatureDellAlbero(IModelDoc2 modello)
        {
            int n = 0;
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null) { n++; f = f.GetNextFeature() as IFeature; }
            }
            catch { return -1; }
            return n;
        }

        /// <summary>
        /// LA DISTINTA DI TAGLIO: una voce per ogni gruppo di barre UGUALI.
        ///
        /// ── IL DIFETTO TROVATO IL 26/9/2026 (voce GT2) ───────────────────────
        ///
        /// Fino alla libreria 0.124.0 questa operazione chiamava
        /// `InsertWeldmentCutList2` con TUTTI i corpi: nasceva UNA voce con dentro
        /// tutte le barre. Il telaio 1000 x 800 del banco usciva «QUANTITY 4,
        /// LUNGHEZZA 1000» — in officina, quattro barre da 1000 dove due sono da
        /// 800. Il caso di banco era verde: chiedeva solo che i corpi fossero 4.
        ///
        /// La causa, misurata (`prova0089 distinta2`): il pezzo non era un
        /// SALDATO (`IsWeldment` falso: i membri strutturali non mettono la
        /// lavorazione «Saldatura»), e la distinta automatica non si accende su un
        /// pezzo che non lo e'. Con la lavorazione Saldatura — messa anche DOPO le
        /// barre — e `SetAutomaticCutList(true)` sulla cartella «Corpi solidi»,
        /// SOLIDWORKS separa le voci da solo: 2 x 1000 e 2 x 800. E la stessa
        /// causa faceva tornare null `InsertWeldmentTable` in tavola.
        ///
        /// Le voci si LEGGONO dalle proprieta' delle cartelle (descrizione,
        /// lunghezza, quantita': si riconoscono dall'espressione, `LENGTH@@@` e
        /// `QUANTITY@@@`, perche' i NOMI sono tradotti) e la somma delle quantita'
        /// deve essere il numero dei corpi.
        /// </summary>
        private Esito DistintaTaglio(IModelDoc2 modello, Dictionary<string, object> a)
        {
            bool aggiorna = Bool(a, "aggiorna_se_esiste", true);
            var parte = modello as IPartDoc;
            if (parte == null) return Fallito("La distinta di taglio si fa su una parte.");

            // UNA SECONDA DISTINTA NON E' UN DOPPIONE INNOCUO: si aggiorna quella
            // che c'e', o si rifiuta se non si vuole.
            bool giaEsisteva = CartelleDistinta(modello).Count > 0;
            if (giaEsisteva && !aggiorna)
                return Fallito(
                    "Nel pezzo c'e' gia' una distinta di taglio. Crearne una " +
                    "seconda vorrebbe dire due elenchi della stessa " +
                    "carpenteria: se vuoi ricalcolare quella che c'e', " +
                    "chiedi «aggiorna_se_esiste».");

            object[] corpiSolidi;
            try { corpiSolidi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[]; }
            catch (Exception ex) { return Fallito("Non riesco a leggere i corpi del pezzo: " + ex.Message); }
            if (corpiSolidi == null || corpiSolidi.Length == 0)
                return Fallito(
                    "Nel pezzo non c'e' nessun corpo solido: non c'e' niente da " +
                    "mettere in distinta. Se le barre le doveva fare un passo " +
                    "prima, quel passo non e' riuscito.");

            // ── 1. IL PEZZO DIVENTA UN SALDATO, se non lo e' ────────────────
            bool? eraSaldato = null;
            try { eraSaldato = parte.IsWeldment(); } catch { }
            var lavorazioniPrima = new HashSet<string>(NomiDelleLavorazioni(modello));
            if (eraSaldato != true)
            {
                try { modello.FeatureManager.InsertWeldmentFeature(); } catch { }
                try { modello.ForceRebuild3(false); } catch { }
            }
            // SE DA QUI SI FALLISCE, la lavorazione «Saldatura» messa adesso si
            // toglie: un rifiuto non lascia il pezzo trasformato (revisione del
            // 26/9/2026).
            Func<string, Esito> rifiuta = testo =>
            {
                if (eraSaldato != true) TogliLavorazioniNuove(modello, lavorazioniPrima);
                return Fallito(testo);
            };
            bool saldato = false;
            try { saldato = parte.IsWeldment(); } catch { }
            if (!saldato)
                return rifiuta(
                    "Non riesco a fare del pezzo un saldato (la lavorazione «Saldatura» non nasce): senza, " +
                    "SOLIDWORKS non separa le barre per lunghezza, e una distinta con tutte le barre in una " +
                    "voce sola manderebbe in officina le lunghezze sbagliate.");

            // ── 2. LA DISTINTA AUTOMATICA ───────────────────────────────────
            bool automatica = false;
            try
            {
                var f0 = modello.FirstFeature() as IFeature;
                while (f0 != null)
                {
                    if (f0.GetTypeName2() == "SolidBodyFolder")
                    {
                        var bf = f0.GetSpecificFeature2() as IBodyFolder;
                        if (bf != null)
                        {
                            bf.SetAutomaticCutList(true);
                            bf.SetAutomaticUpdate(true);
                            bf.UpdateCutList();
                            automatica = bf.GetAutomaticCutList();
                        }
                    }
                    f0 = f0.GetNextFeature() as IFeature;
                }
            }
            catch { }
            foreach (var c in CartelleDistinta(modello))
                try { c.UpdateCutList(); } catch { }
            try { modello.ForceRebuild3(false); } catch { }
            if (!automatica)
                return rifiuta(
                    "Non riesco ad accendere la distinta automatica sulla cartella «Corpi solidi»: senza, le " +
                    "barre non si separano per lunghezza.");

            // ── 3. LE VOCI, LETTE ───────────────────────────────────────────
            var cartelle = CartelleDistinta(modello);
            if (cartelle.Count == 0)
                return rifiuta("La distinta automatica e' accesa ma non ha prodotto nessuna voce.");
            int? corpiInDistinta = 0;
            int? quantitaTotale = 0;
            var voci = new List<object>();
            var lunghezze = new List<object>();
            IFeature primaCartella = null;
            foreach (var c in cartelle)
            {
                int corpi = -1;
                try { corpi = c.GetBodyCount(); } catch { }
                if (corpi < 0) corpiInDistinta = null; else if (corpiInDistinta.HasValue) corpiInDistinta += corpi;
                IFeature fc = null;
                try { fc = c.GetFeature() as IFeature; } catch { }
                if (primaCartella == null) primaCartella = fc;
                string descrizione = null, lunghezza = null, quantita = null;
                try
                {
                    var cpm = fc?.CustomPropertyManager;
                    foreach (var o in (cpm?.GetNames() as object[]) ?? new object[0])
                    {
                        string nome = Convert.ToString(o), val, ris; bool usato, collegato;
                        cpm.Get6(nome, false, out val, out ris, out usato, out collegato);
                        string v = val ?? "";
                        if (v.IndexOf("TOTAL LENGTH@@@", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (v.IndexOf("LENGTH@@@", StringComparison.OrdinalIgnoreCase) >= 0) lunghezza = ris;
                        else if (v.IndexOf("QUANTITY@@@", StringComparison.OrdinalIgnoreCase) >= 0) quantita = ris;
                        else if (nome.Equals("Descrizione", StringComparison.OrdinalIgnoreCase) ||
                                 nome.Equals("Description", StringComparison.OrdinalIgnoreCase)) descrizione = ris;
                    }
                }
                catch { }
                double l;
                bool lNum = double.TryParse((lunghezza ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out l);
                int q;
                bool qNum = int.TryParse((quantita ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out q);
                if (!qNum) quantitaTotale = null; else if (quantitaTotale.HasValue) quantitaTotale += q;
                if (lNum) lunghezze.Add(Math.Round(l, 2));
                voci.Add(new Dictionary<string, object>
                {
                    ["descrizione"] = descrizione,
                    ["lunghezza_mm"] = lNum ? (object)Math.Round(l, 2) : lunghezza,
                    ["quantita"] = qNum ? (object)q : quantita,
                    ["corpi"] = NumeroOppureNull(corpi),
                });
            }

            var misure = new Dictionary<string, object>
            {
                ["corpi_in_distinta"] = corpiInDistinta,
                ["voci_distinte"] = voci.Count,
                ["voci"] = voci.ToArray(),
                ["lunghezze_mm"] = lunghezze.ToArray(),
                ["quantita_totale"] = quantitaTotale,
                ["gia_esisteva"] = giaEsisteva,
                ["reso_saldato"] = eraSaldato.HasValue ? (object)!eraSaldato.Value : null,
            };
            // LA SOMMA DELLE QUANTITA' E' IL NUMERO DELLE BARRE: se no, qualcosa
            // manca o e' contato due volte, e lo si dice.
            if (quantitaTotale.HasValue && quantitaTotale.Value != corpiSolidi.Length)
                misure["attenzione"] = "La distinta dice " + quantitaTotale.Value + " barre in tutto e il pezzo ha " +
                                       corpiSolidi.Length + " corpi: prima di andare in officina, controlla.";
            // Una distinta che c'era gia' non e' una feature creata adesso.
            if (primaCartella != null && !giaEsisteva) return Riuscito(primaCartella, misure);
            return SenzaFeature(misure);
        }

        private Esito TappoEstremita(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            int quante;
            string perche;
            var facce = FacceDalRiferimento(a, "estremita_rif", stato, out quante, out perche);
            if (facce == null) return Fallito(perche);

            double spessore = Num(a, "spessore_mm", 0);
            if (spessore <= 0) return Fallito("Il tappo non ha spessore.");
            double rientro = Num(a, "rientro_mm", 0);
            double smusso = Num(a, "smusso_mm", 0);

            modello.ClearSelection2(true);
            int selezionate = 0;
            foreach (var f in facce)
            {
                var ent = f as IEntity;
                if (ent != null && ent.Select4(true, null)) selezionate++;
            }
            if (selezionate == 0)
                return Fallito("Le estremita' indicate non si sono lasciate selezionare.");

            IFeature feature;
            try
            {
                feature = modello.FeatureManager.InsertEndCapFeature3(
                    Depth: Unita.M(spessore),
                    BIsGivenOffset: rientro > 0,
                    BIsChamfer: smusso > 0,
                    OffsetValue: Unita.M(rientro),
                    WallThicknessRatio: 0.5,
                    ChamferValue: Unita.M(smusso),
                    BIsCornerTreatment: false,
                    DepthOffset: 0,
                    BIsReverse: false,
                    BIsEndCapInward: (int)swEndCapThicknessDirection_e.swExtendInward) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("Il tappo non e' riuscito: " + ex.Message);
            }
            if (feature == null)
                return Fallito(
                    "Il tappo non e' stato creato. Le facce indicate devono " +
                    "essere estremita' APERTE di una barra cava: su un profilo " +
                    "pieno non c'e' niente da tappare.");

            return Riuscito(feature, new Dictionary<string, object>
            {
                ["estremita_tappate"] = selezionate,
                ["spessore_mm"] = spessore,
            });
        }

        private Esito Fazzoletto(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            // IL FAZZOLETTO VUOLE DUE FACCE, E NON SI INDOVINANO.
            //
            // Un fazzoletto sta nell'angolo fra DUE barre, appoggiato su una
            // faccia di ciascuna. Quali siano non si deduce dal telaio: in un
            // angolo di tubolari le facce candidate sono quattro, e sceglierne
            // due «probabili» vorrebbe dire mettere il rinforzo dalla parte
            // sbagliata — cosa che si vede solo guardando il pezzo.
            //
            // Percio' qui le facce si indicano per identificativo, prese dallo
            // stato del documento: e' un'operazione che si chiede DOPO aver
            // fatto il telaio, guardandolo.
            var uid = Lista(a, "facce_uid");
            if (uid.Count != 2)
                return Fallito(
                    "Il fazzoletto va appoggiato su DUE facce, una per barra. " +
                    "Ne hai indicate " + uid.Count + ".");

            double spessore = Num(a, "spessore_mm", 0);
            double lato1 = Num(a, "lato1_mm", 0);
            double lato2 = Num(a, "lato2_mm", 0);
            if (spessore <= 0) return Fallito("Il fazzoletto non ha spessore.");
            if (lato1 <= 0 || lato2 <= 0)
                return Fallito("Il fazzoletto non ha i due lati.");

            modello.ClearSelection2(true);
            foreach (var u in uid)
            {
                StatoDocumento.FirmaEntita firma;
                if (stato == null || !stato.Firme.TryGetValue(u, out firma) || firma.Tipo != "FACE")
                    return Fallito("La faccia «" + u + "» non c'e' piu' in questo documento.");
                string scostamento;
                if (!FirmaCombacia(a, u, firma, out scostamento)) return Fallito(scostamento);
                var ent = firma.Entita as IEntity;
                if (ent == null || !ent.Select4(true, null))
                    return Fallito("La faccia «" + u + "» non si e' lasciata selezionare.");
            }

            string posizione = Str(a, "posizione", "centro");
            short dove;
            switch (posizione)
            {
                case "inizio": dove = (short)swGussetProfileLocationType_e.swGussetProfileLocationStart; break;
                case "fine": dove = (short)swGussetProfileLocationType_e.swGussetProfileLocationEnd; break;
                default: dove = (short)swGussetProfileLocationType_e.swGussetProfileLocationCenter; break;
            }

            IFeature feature;
            try
            {
                feature = modello.FeatureManager.InsertGussetFeature3(
                    Depth: Unita.M(spessore),
                    DirType: (short)swGussetThicknessType_e.swGussetThicknessBothSides,
                    LocType: dove,
                    BIsProfile: true,
                    ProfileD1: Unita.M(lato1),
                    ProfileD2: Unita.M(lato2),
                    ProfileD3: 0,
                    ProfileAngle: 0,
                    ProfileD4: 0,
                    BOffset: false,
                    DProfileOffset: 0,
                    CrvIndex: 0,
                    BReverseDir: false,
                    BReverseFace: false,
                    BUseLenDim: true,
                    ProfileD5: 0,
                    ProfileD6: 0,
                    ProfileChamferAngle: 0,
                    BUseLenDimForChamfer: false,
                    BNeedChamferInGusset: false) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("Il fazzoletto non e' riuscito: " + ex.Message);
            }
            if (feature == null)
                return Fallito(
                    "Il fazzoletto non e' stato creato. Le due facce devono " +
                    "appartenere a barre che si incontrano, e formare un angolo.");

            return Riuscito(feature, new Dictionary<string, object>
            {
                ["spessore_mm"] = spessore,
                ["lato1_mm"] = lato1,
                ["lato2_mm"] = lato2,
                ["posizione"] = posizione,
            });
        }

        // IL CORDONE, E PERCHE' ADESSO VUOLE DUE FACCE E NON DUE CORPI.
        //
        // Fino alla libreria 0.113.0 prendeva due insiemi di corpi, ne passava
        // TUTTE le facce a `InsertCosmeticWeldBead2`, e non ha mai creato un
        // cordone. Misurato il 25/9/2026 su una T di tubolari 80x80x5 gia'
        // tagliata (voce Z-C6):
        //
        //   InsertCosmeticWeldBead2, facce come object[], Face2[] o
        //   DispatchWrapper[], tutte o solo le due che si toccano, modo
        //   «geometria» o «percorso» sugli spigoli        -> null, sempre
        //   InsertCosmeticWeldBead (la vecchia, per selezione), UNA faccia
        //   per parte                                      -> cordone vero,
        //                                    lunghezza 68 mm = il lato piatto
        //   la stessa, tutte le facce dei due corpi        -> null
        //   la stessa, tre facce contro una               -> null
        //   la stessa, due facce che NON si toccano        -> cordone creato
        //                                    lo stesso, lato riletto 40 e non 4
        //
        // Quindi: una coppia di facce per chiamata, e il contatto si controlla
        // QUI, prima, perche' SOLIDWORKS non lo controlla — e un cordone fra
        // facce lontane finirebbe nella distinta come saldatura vera.
        private Esito CordoneSaldatura(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            double lato = Num(a, "lato_mm", 0);
            if (lato <= 0) return Fallito("Il cordone non ha lato.");

            var facce = new IFace2[2];
            var nomi = new[] { "da_faccia_uid", "a_faccia_uid" };
            for (int i = 0; i < 2; i++)
            {
                string u = Str(a, nomi[i], "");
                if (u.Length == 0)
                    return Fallito(
                        "Manca «" + nomi[i] + "». Il cordone va fra DUE facce, una per " +
                        "corpo, prese dallo stato del documento (F1, F2...).");
                StatoDocumento.FirmaEntita firma;
                if (stato == null || !stato.Firme.TryGetValue(u, out firma) || firma.Tipo != "FACE")
                    return Fallito("La faccia «" + u + "» non c'e' piu' in questo documento.");
                string scostamento;
                if (!FirmaCombacia(a, u, firma, out scostamento)) return Fallito(scostamento);
                facce[i] = firma.Entita as IFace2;
                if (facce[i] == null)
                    return Fallito("La faccia «" + u + "» non si lascia leggere.");
            }

            string corpoDa = NomeDelCorpo(facce[0]), corpoA = NomeDelCorpo(facce[1]);
            if (corpoDa != null && corpoDa == corpoA)
                return Fallito(
                    "Le due facce sono dello stesso corpo («" + corpoDa + "»). Un " +
                    "cordone unisce DUE corpi: indica una faccia per parte.");

            // S17: a curved face gives null (it used to give 0,0,0, a zero
            // cosine): either way it is not "parallel", as before.
            var nDa = ApiHelpers.NormalIfPlanar(facce[0]);
            var nA = ApiHelpers.NormalIfPlanar(facce[1]);
            if (nDa != null && nA != null && nDa.Length >= 3 && nA.Length >= 3
                && Math.Abs(nDa[0] * nA[0] + nDa[1] * nA[1] + nDa[2] * nA[2]) > 0.999)
                return Fallito(
                    "Le due facce sono parallele: si appoggiano una sull'altra o stanno " +
                    "affiancate, e non formano un angolo. Il cordone d'angolo va fra due " +
                    "facce che si incontrano lungo uno spigolo e fanno un angolo — per " +
                    "una barra in battuta su un'altra, e' la faccia LATERALE della barra " +
                    "contro la faccia su cui arriva, non la sua testa.");

            // Basta un verso che tocca; ma «non si toccano» si puo' dire solo
            // se tutti e due i versi hanno risposto.
            double l1 = LunghezzaCheStaSu(facce[0], facce[1]), l2 = LunghezzaCheStaSu(facce[1], facce[0]);
            if (Math.Max(l1, l2) < 0.01 && (l1 < 0 || l2 < 0))
                return Fallito(
                    "Non riesco a leggere gli spigoli delle due facce, e senza non so dirti " +
                    "se si toccano. Non ho messo il cordone.");
            double contatto = Math.Max(l1, l2);
            if (contatto < 0.01)
                return Fallito(
                    "Le due facce non si toccano: nessuno spigolo dell'una sta " +
                    "sull'altra. SOLIDWORKS il cordone lo creerebbe lo stesso (misurato: " +
                    "fra due facce lontane nasce, con un lato diverso da quello chiesto), " +
                    "e in distinta finirebbe una saldatura che non esiste.");

            modello.ClearSelection2(true);
            for (int i = 0; i < 2; i++)
                if (!(facce[i] as IEntity).Select4(true, null))
                    return Fallito("La faccia «" + Str(a, nomi[i], "") + "» non si e' lasciata selezionare.");

            IFeature feature;
            try
            {
                feature = modello.FeatureManager.InsertCosmeticWeldBead(Unita.M(lato)) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("Il cordone non e' riuscito: " + ex.Message);
            }
            if (feature == null)
                return Fallito(
                    "Il cordone non e' stato creato, e SOLIDWORKS non dice perche'. " +
                    "Le due facce si toccano per " +
                    contatto.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    " mm: il lato potrebbe essere troppo grande per le facce.");

            // RILETTO, NON CREDUTO: e' il lato che SOLIDWORKS ha scritto nella
            // feature, ed e' la misura che fra facce lontane veniva diversa.
            double latoLetto = -1;
            try
            {
                var def = feature.GetDefinition() as ICosmeticWeldBeadFeatureData;
                if (def != null) latoLetto = def.BeadSize * 1000.0;
            }
            catch { }
            if (latoLetto >= 0 && Math.Abs(latoLetto - lato) > 0.001)
                return Fallito(
                    "Il cordone e' stato creato con un lato di " +
                    latoLetto.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " mm invece di " + lato.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    ". Non te lo lascio passare per buono — " + Frasi.CosiSiRitira + ".");

            return Riuscito(feature, new Dictionary<string, object>
            {
                ["lato_mm"] = lato,
                ["lato_riletto_mm"] = latoLetto >= 0 ? (object)Math.Round(latoLetto, 3) : null,
                // La lunghezza e' quella dello spigolo in comune, contata qui
                // sulla geometria: non dipende dalle unita' del documento.
                ["lunghezza_mm"] = Math.Round(contatto, 2),
                ["corpo_da"] = corpoDa,
                ["corpo_a"] = corpoA,
            });
        }

        /// <summary>I nomi delle lavorazioni dell'albero, nell'ordine.</summary>
        private static List<string> NomiDelleLavorazioni(IModelDoc2 modello)
        {
            var fuori = new List<string>();
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    try { fuori.Add(f.Name); } catch { }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return fuori;
        }

        /// <summary>
        /// Toglie le lavorazioni nate dopo `prima` (la «Saldatura» e le cartelle
        /// di distinta messe da un'operazione che poi rifiuta). DeleteSelection2,
        /// non EditDelete. Al meglio: quelle che non si lasciano togliere restano,
        /// e l'albero lo dice.
        /// </summary>
        private static void TogliLavorazioniNuove(IModelDoc2 modello, HashSet<string> prima)
        {
            try
            {
                var nuove = new List<IFeature>();
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    string n = null;
                    try { n = f.Name; } catch { }
                    if (n != null && !prima.Contains(n)) nuove.Add(f);
                    f = f.GetNextFeature() as IFeature;
                }
                for (int i = nuove.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        modello.ClearSelection2(true);
                        if (nuove[i].Select2(false, 0))
                            modello.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                    }
                    catch { }
                }
                modello.ClearSelection2(true);
                modello.ForceRebuild3(false);
            }
            catch { }
        }

        private static string NomeDelCorpo(IFace2 faccia)
        {
            try { return (faccia.GetBody() as IBody2)?.Name; }
            catch { return null; }
        }

        /// <summary>
        /// Quanti millimetri di spigoli di `faccia` stanno sopra `altra`: uno
        /// spigolo conta se il suo inizio, il suo punto medio e la sua fine
        /// stanno sull'altra faccia entro un millesimo di millimetro.
        ///
        /// E' il contatto che fa un cordone d'angolo: la faccia laterale di
        /// una barra in battuta tocca la faccia su cui arriva lungo lo
        /// spigolo di fondo. Tre punti e non uno, perche' uno spigolo che
        /// sfiora l'altra faccia in un punto solo non e' una linea di
        /// saldatura.
        ///
        /// **-1 quando non si e' potuto leggere** (voce Z-C8): lo zero voleva
        /// dire «non si toccano», e il chiamante rifiutava il cordone con una
        /// frase falsa. ZERO-E-ZERO: una faccia senza spigoli (`GetEdges` null,
        /// una sfera intera) non tocca niente lungo uno spigolo, e quello zero
        /// e' vero. Uno spigolo che non si lascia valutare non si conta, ma se
        /// alla fine non sta sopra niente e qualcuno non ha risposto, il
        /// risultato e' «non lo so», non zero.
        /// </summary>
        private static double LunghezzaCheStaSu(IFace2 faccia, IFace2 altra)
        {
            double totale = 0;
            int muti = 0;
            object[] spigoli;
            try { spigoli = faccia.GetEdges() as object[]; }
            catch { return -1; }
            if (spigoli == null) return 0;
            foreach (var o in spigoli)
            {
                try
                {
                    var spigolo = o as IEdge;
                    var parametri = ApiHelpers.EdgeParameters(spigolo, out ICurve curva);
                    if (curva == null || parametri == null) continue;
                    double u0 = parametri.UMinValue, u1 = parametri.UMaxValue;
                    bool sta = true;
                    foreach (double t in new[] { u0, (u0 + u1) / 2, u1 })
                    {
                        var p = curva.Evaluate2(t, 0) as double[];
                        var q = altra.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                        if (q == null) { sta = false; break; }
                        double dx = p[0] - q[0], dy = p[1] - q[1], dz = p[2] - q[2];
                        if (Math.Sqrt(dx * dx + dy * dy + dz * dz) > 1e-6) { sta = false; break; }
                    }
                    if (sta) totale += curva.GetLength3(u0, u1) * 1000.0;
                }
                catch { muti++; }
            }
            return totale <= 0 && muti > 0 ? -1 : totale;
        }

        // ─────────────────────────────────────────────────────────────────
        // Sostegno
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// I trattamenti d'angolo, come li chiama il piano e come li vuole
        /// SolidWorks. I numeri sono swSolidworksWeldmentEndCondOptions_e,
        /// letti per riflessione il 9 settembre 2026.
        /// </summary>
        private static readonly Dictionary<string, int> TrattamentiAngolo =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "nessuno", (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionNone },
                { "quartabuono", (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionMiter },
                { "testa1", (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionButt1 },
                { "testa2", (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionButt2 },
                { "taglio", (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionTrim },
            };

        private class Segmento
        {
            public double[] Da;
            public double[] A;
        }

        private static double Distanza(double[] p, double[] q)
        {
            double dx = p[0] - q[0], dy = p[1] - q[1], dz = p[2] - q[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// Su quali due assi giace un piano principale.
        ///
        /// Stessa convenzione del server (`COORDINATE` in ai_disegno.py):
        /// frontale = XY, superiore = XZ, destro = YZ. Se le due tabelle
        /// divergessero, un telaio nascerebbe su un piano e verrebbe
        /// verificato su un altro — e la verifica fallirebbe su un pezzo
        /// giusto, che e' il modo peggiore di sbagliare.
        /// </summary>
        private static bool AssiDelPiano(string piano, out int u, out int v)
        {
            switch ((piano ?? "").Trim().ToLowerInvariant())
            {
                case "superiore": u = 0; v = 2; return true;   // XZ
                case "destro": u = 1; v = 2; return true;      // YZ
                case "frontale":
                case "": u = 0; v = 1; return true;            // XY
                default: u = -1; v = -1; return false;
            }
        }

        /// <summary>
        /// I quattro segmenti del percorso di un telaio, sugli assi del piano.
        ///
        /// Le quote che arrivano qui sono gia' quelle del PERCORSO, cioe'
        /// l'ingombro esterno meno la sporgenza del profilo. Le sporgenze
        /// servono ancora per il caso «angolo sull'origine»: li' l'ingombro
        /// deve partire da zero, quindi l'asse parte da mezza sporgenza.
        /// </summary>
        private static List<Segmento> SegmentiTelaio(
            double percorsoU, double percorsoV, int asseU, int asseV,
            bool centrato, double[] sporgenze)
        {
            double u0 = centrato ? -percorsoU / 2 : sporgenze[0] / 2;
            double v0 = centrato ? -percorsoV / 2 : sporgenze[1] / 2;
            double u1 = u0 + percorsoU;
            double v1 = v0 + percorsoV;

            Func<double, double, double[]> punto = (u, v) =>
            {
                var p = new double[3];
                p[asseU] = Math.Round(u, 6);
                p[asseV] = Math.Round(v, 6);
                return p;
            };

            var angoli = new[] { punto(u0, v0), punto(u1, v0), punto(u1, v1), punto(u0, v1) };
            var fuori = new List<Segmento>();
            for (int i = 0; i < 4; i++)
                fuori.Add(new Segmento { Da = angoli[i], A = angoli[(i + 1) % 4] });
            return fuori;
        }

        /// <summary>
        /// La scatola che contiene i corpi creati da una feature, in mm:
        /// {minX, minY, minZ, maxX, maxY, maxZ}. null se non si puo' leggere.
        ///
        /// Si passa dalle facce della feature, non dai corpi del documento:
        /// se il pezzo ne conteneva gia' altri, misurare il documento
        /// direbbe l'ingombro di tutto e la verifica del telaio sarebbe una
        /// bugia con l'aria di un controllo.
        ///
        /// E SI USA GetBodyBox, NON GetExtremePoint, DI PROPOSITO.
        /// GetBodyBox e' approssimata per le superfici non piane, e altrove
        /// (StatoDocumento) infatti si usa GetExtremePoint. Qui no: questo
        /// numero va confrontato con la sporgenza misurata da
        /// SezioneProfilo, che usa lo STESSO GetBodyBox sul telaio di
        /// prova. Due misure con la stessa approssimazione si annullano —
        /// percorso = quota - sporgenza, ingombro = percorso + sporgenza —
        /// mentre due misure diverse farebbero fallire la verifica su un
        /// telaio giusto, con un profilo tondo. Chi cambia una delle due deve
        /// cambiare l'altra.
        /// </summary>
        private static double[] IngombroDeiCorpi(IFeature feature)
        {
            try
            {
                var facce = feature.GetFaces() as object[];
                if (facce == null || facce.Length == 0) return null;

                var corpi = new List<IBody2>();
                foreach (var o in facce)
                {
                    IBody2 corpo;
                    try { corpo = (o as IFace2).GetBody() as IBody2; }
                    catch { continue; }
                    if (corpo == null) continue;
                    bool gia = false;
                    foreach (var c in corpi)
                        if (ReferenceEquals(c, corpo) || Equals(c, corpo)) { gia = true; break; }
                    if (!gia) corpi.Add(corpo);
                }
                if (corpi.Count == 0) return null;

                double[] totale = null;
                foreach (var c in corpi)
                {
                    // Approximate ON PURPOSE, see the summary above (paired with
                    // SezioneProfilo): ApiHelpers.ApproximateBoundingBox.
                    var b = ApiHelpers.ApproximateBoundingBox(c);
                    if (b == null || b.Length < 6) continue;
                    if (totale == null) { totale = (double[])b.Clone(); continue; }
                    for (int i = 0; i < 3; i++) totale[i] = Math.Min(totale[i], b[i]);
                    for (int i = 3; i < 6; i++) totale[i] = Math.Max(totale[i], b[i]);
                }
                if (totale == null) return null;
                for (int i = 0; i < 6; i++) totale[i] *= 1000.0;
                return totale;
            }
            catch { return null; }
        }

        /// <summary>
        /// Un blocco annidato degli argomenti.
        ///
        /// JavaScriptSerializer, che e' quello che legge il piano, restituisce
        /// ogni oggetto JSON come Dictionary&lt;string, object&gt;: un cast
        /// diretto basta, ma va fatto senza sollevare se il campo non c'e' o
        /// se un giorno arrivasse di un altro tipo.
        /// </summary>
        private static Dictionary<string, object> Mappa(
            Dictionary<string, object> a, string chiave)
        {
            object grezzo;
            if (a == null || !a.TryGetValue(chiave, out grezzo) || grezzo == null) return null;
            return grezzo as Dictionary<string, object>;
        }

        private static string Terna(double[] p)
        {
            return string.Format("{0:0.##}, {1:0.##}, {2:0.##}", p[0], p[1], p[2]);
        }

        /// <summary>
        /// I segmenti del piano, gia' validati dal server ma riletti qui.
        ///
        /// Il server li ha controllati, ma fra quel controllo e questa riga c'e'
        /// il giro di rete, il salvataggio nel piano e la conferma dell'utente.
        /// Una coordinata che diventasse una stringa qui verrebbe letta come
        /// ZERO da Convert.ToDouble, e la barra finirebbe altrove senza che
        /// nessuno veda un errore.
        /// </summary>
        /// <summary>
        /// Il telaio, scritto in una delle due forme che esistono.
        ///
        /// IL CATALOGO NE DOCUMENTA UNA E IL SERVER NE MANDA UN'ALTRA, e
        /// vanno servite tutte e due:
        ///
        ///   dal catalogo:  { lunghezza_mm, larghezza_mm, piano, centrato, misure }
        ///   dal server:    { telaio: { lunghezza_mm, ... }, segmenti: [...] }
        ///
        /// La prima e' quella che il catalogo dichiara nel suo `required` e
        /// che la descrizione promette («si chiede con due quote invece che
        /// con otto coordinate»). La seconda la costruisce `valida()` nel
        /// server, che reimpacchetta prima di mandare — ed e' per questo che
        /// la divergenza non si vedeva da quella parte: un modello che legge
        /// il catalogo e scrive quello che c'e' scritto arrivava qui e non
        /// veniva servito.
        ///
        /// Se ci sono tutte e due vince quella annidata: chi la manda l'ha
        /// costruita apposta, e di solito porta anche i segmenti gia' fatti.
        /// </summary>
        private static Dictionary<string, object> TelaioChiesto(Dictionary<string, object> a)
        {
            var annidato = Mappa(a, "telaio");
            if (annidato != null) return annidato;
            if (a == null) return null;

            // Senza le due quote non c'e' nessun telaio da ricavare: sara'
            // un membro strutturale con i suoi segmenti, che passa da qui
            // perche' condivide il `case`.
            if (!a.ContainsKey("lunghezza_mm") || !a.ContainsKey("larghezza_mm"))
                return null;

            var fuori = new Dictionary<string, object>
            {
                ["lunghezza_mm"] = a["lunghezza_mm"],
                ["larghezza_mm"] = a["larghezza_mm"],
                // I predefiniti sono quelli che il catalogo promette: il
                // piano frontale, centrato, e le quote lette come ingombro
                // esterno. Quest'ultimo e' il punto dell'operazione: chi
                // chiede un telaio 1000x800 vuole misurare 1000x800 sul
                // pezzo, non sulla linea d'asse.
                ["piano"] = a.ContainsKey("piano") ? a["piano"] : "frontale",
                ["centrato"] = a.ContainsKey("centrato") ? a["centrato"] : true,
                ["misure"] = a.ContainsKey("misure") ? a["misure"] : "ingombro",
            };
            return fuori;
        }

        private List<Segmento> SegmentiDa(Dictionary<string, object> a, out string motivo)
        {
            motivo = null;
            object grezzo;
            if (a == null || !a.TryGetValue("segmenti", out grezzo) || grezzo == null)
            {
                motivo = "Il piano non porta nessun segmento: non c'e' percorso.";
                return null;
            }
            var lista = grezzo as System.Collections.IEnumerable;
            if (lista == null)
            {
                motivo = "I segmenti del piano non sono leggibili.";
                return null;
            }

            var fuori = new List<Segmento>();
            int i = 0;
            foreach (var voce in lista)
            {
                i++;
                var mappa = voce as Dictionary<string, object>;
                if (mappa == null)
                {
                    motivo = "Il segmento " + i + " non e' leggibile.";
                    return null;
                }
                double[] da = Punto(mappa, "da", i, out motivo);
                if (da == null) return null;
                double[] b = Punto(mappa, "a", i, out motivo);
                if (b == null) return null;
                if (Distanza(da, b) < 0.001)
                {
                    motivo = "Il segmento " + i + " ha i due estremi nello stesso punto.";
                    return null;
                }
                fuori.Add(new Segmento { Da = da, A = b });
            }
            if (fuori.Count == 0)
            {
                motivo = "Il percorso e' vuoto.";
                return null;
            }
            return fuori;
        }

        private static double[] Punto(
            Dictionary<string, object> mappa, string chiave, int i, out string motivo)
        {
            motivo = null;
            object grezzo;
            if (!mappa.TryGetValue(chiave, out grezzo) || grezzo == null)
            {
                motivo = "Al segmento " + i + " manca «" + chiave + "».";
                return null;
            }
            var lista = (grezzo as System.Collections.IEnumerable);
            if (lista == null)
            {
                motivo = "Al segmento " + i + " il punto «" + chiave + "» non e' leggibile.";
                return null;
            }
            var valori = new List<double>();
            foreach (var v in lista)
            {
                try { valori.Add(Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture)); }
                catch
                {
                    motivo = "Al segmento " + i + ", punto «" + chiave +
                             "», una coordinata non e' un numero.";
                    return null;
                }
            }
            if (valori.Count != 3)
            {
                motivo = "Al segmento " + i + " il punto «" + chiave + "» non ha tre coordinate.";
                return null;
            }
            return valori.ToArray();
        }

        /// <summary>
        /// I corpi indicati da un riferimento a un passo precedente, OPPURE
        /// dagli identificativi dello stato del documento.
        ///
        /// PERCHE' DUE STRADE
        /// Il riferimento a un passo funziona solo dentro lo stesso piano. In
        /// un messaggio nuovo — «tappa le estremita' del telaio che hai fatto
        /// prima» — non c'e' nessun passo da citare, e fino alla 0.29.1 quella
        /// richiesta non aveva NESSUNA forma valida: l'unica via che restava
        /// al modello era ridisegnare le barre, ottenendo barre sovrapposte
        /// senza che niente desse errore.
        ///
        /// Adesso lo stato porta i corpi con il loro identificativo, e si puo'
        /// dire «C1, C3». Il riferimento ha la precedenza: se arrivassero tutti
        /// e due — il server lo impedisce, ma questa e' la seconda serratura —
        /// meglio fallire su un identificativo che non c'e' che lavorare in
        /// silenzio sul corpo sbagliato.
        /// </summary>
        private object[] CorpiDalRiferimento(
            Dictionary<string, object> a, string campo, StatoDocumento stato,
            out int quanti, out string motivo)
        {
            quanti = 0;
            motivo = null;

            if (HaRiferimento(a, campo))
            {
                int numero;
                var passo = PassoCitato(a, campo, out numero, out motivo);
                if (passo == null) return null;

                var daPasso = Riferimenti.CorpiDi(passo, out motivo);
                if (daPasso == null) return null;
                quanti = daPasso.Count;
                if (quanti == 0)
                {
                    motivo = "Il passo " + numero + " non ha lasciato nessun corpo da usare.";
                    return null;
                }
                return daPasso.Cast<object>().ToArray();
            }

            string campoUid = campo.EndsWith("_rif", StringComparison.Ordinal)
                ? campo.Substring(0, campo.Length - 4) + "_uid"
                : campo + "_uid";
            var uid = Lista(a, campoUid);
            if (uid.Count == 0)
            {
                motivo =
                    "Non hai detto su quali corpi lavorare. Indica «" + campoUid +
                    "» con gli identificativi dello stato del documento (C1, C2...), " +
                    "oppure «" + campo + "» se le barre le crea un passo precedente " +
                    "di questo stesso piano.";
                return null;
            }

            var corpi = CorpiDaUid(stato, uid, a, out motivo);
            if (corpi == null) return null;
            quanti = corpi.Count;
            return corpi.Cast<object>().ToArray();
        }

        /// <summary>
        /// I corpi che portano quegli identificativi, controllando che siano
        /// ancora quelli.
        ///
        /// La firma di un corpo e' centro e volume della scatola: se qualcuno
        /// ha modificato il pezzo fra la proposta e l'esecuzione, «C1» indica
        /// un altro corpo e il passo si ferma invece di lavorare al posto
        /// sbagliato. E' lo stesso controllo che si fa sulle facce.
        ///
        /// QUESTO COMMENTO E' STATO A LUNGO UNA PROMESSA E NON UN FATTO.
        /// Diceva gia' cosi', e il codice `FirmaCombacia` non lo chiamava
        /// affatto: i corpi erano controllati per ESISTENZA e non per
        /// IDENTITA'. Misurato il 14/9/2026 (voce E4): due blocchi, un taglio
        /// che spezza il primo in due, poi «sposta C2». Il taglio rinumera i
        /// corpi, «C2» diventa mezzo blocco grande, e viene spostato quello —
        /// **`riuscita: true`, nessun avviso**, e il blocco piccolo resta dov'e'.
        ///
        /// Un commento che descrive un controllo che non c'e' e' peggio di
        /// nessun commento: chi legge smette di cercare il buco proprio dove
        /// sta.
        /// </summary>
        private List<IBody2> CorpiDaUid(
            StatoDocumento stato, List<string> uid,
            Dictionary<string, object> argomenti, out string motivo)
        {
            motivo = null;
            if (stato == null)
            {
                motivo = "Non ho lo stato del documento: non so a cosa corrisponda «" +
                         string.Join(", ", uid) + "».";
                return null;
            }

            var fuori = new List<IBody2>();
            foreach (var u in uid)
            {
                StatoDocumento.FirmaEntita firma;
                if (!stato.Firme.TryGetValue(u, out firma) || firma.Tipo != "BODY")
                {
                    motivo = "Il corpo «" + u + "» non c'e' piu' in questo documento.";
                    return null;
                }
                var corpo = firma.Entita as IBody2;
                if (corpo == null)
                {
                    motivo = "Il corpo «" + u + "» non e' piu' utilizzabile.";
                    return null;
                }

                // E ADESSO IL CONTROLLO CHE IL COMMENTO SOPRA PROMETTEVA.
                if (!FirmaCombacia(argomenti, u, firma, out motivo)) return null;
                bool gia = false;
                foreach (var c in fuori)
                    if (ReferenceEquals(c, corpo) || Equals(c, corpo)) { gia = true; break; }
                if (!gia) fuori.Add(corpo);
            }
            if (fuori.Count == 0)
            {
                motivo = "Nessun corpo indicato.";
                return null;
            }
            return fuori;
        }

        /// <summary>Le facce di estremita' indicate da un riferimento.</summary>
        private IFace2[] FacceDalRiferimento(
            Dictionary<string, object> a, string campo, StatoDocumento stato,
            out int quante, out string motivo)
        {
            quante = 0;
            motivo = null;
            List<IFace2> facce;
            string dove;

            if (HaRiferimento(a, campo))
            {
                int numero;
                var passo = PassoCitato(a, campo, out numero, out motivo);
                if (passo == null) return null;
                facce = Riferimenti.EstremitaLibere(passo, out motivo);
                dove = "del passo " + numero;
            }
            else
            {
                // In un messaggio nuovo non c'e' nessun passo da citare: si
                // parte dai corpi che lo stato del documento elenca.
                var uid = Lista(a, "corpi_uid");
                if (uid.Count == 0)
                {
                    motivo =
                        "Non hai detto quali barre tappare. Indica «corpi_uid» con " +
                        "gli identificativi dello stato (C1, C2...), oppure " +
                        "«" + campo + "» se le barre le crea un passo precedente " +
                        "di questo stesso piano.";
                    return null;
                }
                var corpi = CorpiDaUid(stato, uid, a, out motivo);
                if (corpi == null) return null;
                facce = Riferimenti.EstremitaLibereDi(corpi, out motivo);
                dove = "dei corpi " + string.Join(", ", uid);
            }

            if (facce == null) return null;
            quante = facce.Count;
            if (quante == 0)
            {
                motivo =
                    "Le barre " + dove + " non hanno estremita' aperte da tappare: " +
                    "o sono di profilo pieno, o le estremita' sono gia' chiuse dal " +
                    "trattamento d'angolo.";
                return null;
            }
            return facce.ToArray();
        }

        /// <summary>
        /// Questa feature e' una cartella di distinta di saldatura?
        ///
        /// Due controlli e non uno: `Type` dice che genere di cartella e'
        /// (5 = distinta di taglio), `GetCutListType` che genere di distinta
        /// (3 = saldatura). La cartella «Corpi solidi» risponde al secondo e
        /// non al primo, e senza il doppio controllo finirebbe in elenco.
        /// Misurato il 9 settembre 2026: Corpi solidi ha Type=1 CutListType=0,
        /// «Elenco elementi1» ha Type=5 CutListType=3.
        /// </summary>
        private static void Riconosci(
            IFeature f, List<IBodyFolder> fuori, HashSet<string> gia)
        {
            try
            {
                var cartella = f.GetSpecificFeature2() as IBodyFolder;
                if (cartella == null) return;
                if (cartella.Type != (int)swBodyFolderFeatureType_e.swWeldmentCutListFolder)
                    return;
                if (cartella.GetCutListType() != (int)swCutListType_e.swWeldmentCutlist)
                    return;

                // LA STESSA CARTELLA COMPARE DUE VOLTE NELL'ALBERO.
                //
                // «Elenco elementi1» sta sia dentro «Corpi solidi» sia al
                // primo livello: scorrendo feature e sotto-feature la si
                // incontra due volte. Misurato il 9 settembre 2026 — senza
                // questo controllo un telaio da quattro barre risultava di
                // OTTO, e il numero finiva nel riassunto che l'utente legge.
                //
                // Il nome della feature e' unico nel documento, e regge dove
                // il confronto fra due oggetti COM non e' garantito.
                string chiave;
                try { chiave = f.Name; }
                catch { chiave = null; }
                if (chiave != null && !gia.Add(chiave)) return;

                fuori.Add(cartella);
            }
            catch { }
        }

        /// <summary>Le cartelle della distinta di taglio gia' presenti.</summary>
        private static List<IBodyFolder> CartelleDistinta(IModelDoc2 modello)
        {
            var fuori = new List<IBodyFolder>();
            var parte = modello as IPartDoc;
            if (parte == null) return fuori;
            // LE CARTELLE DELLA DISTINTA SONO SOTTO-FEATURE.
            //
            // Stanno dentro «Corpi solidi», e cercarle solo fra le feature di
            // primo livello vuol dire non trovarle mai — e allora si crede
            // che una distinta non ci sia e se ne crea una seconda, con due
            // elenchi della stessa carpenteria in officina.
            // Services/DistintaTaglio.cs le legge cosi' dal 2 settembre.
            var gia = new HashSet<string>(StringComparer.Ordinal);
            IFeature f;
            try { f = parte.IFirstFeature(); }
            catch { return fuori; }
            while (f != null)
            {
                Riconosci(f, fuori, gia);
                try
                {
                    IFeature sotto = f.IGetFirstSubFeature();
                    while (sotto != null)
                    {
                        Riconosci(sotto, fuori, gia);
                        sotto = sotto.IGetNextSubFeature();
                    }
                }
                catch { }
                try { f = f.IGetNextFeature(); }
                catch { break; }
            }
            return fuori;
        }
    }
}
