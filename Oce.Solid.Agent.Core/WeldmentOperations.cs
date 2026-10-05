using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
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
    public partial class SwOperations
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
        ///   OpenClean — c'era e nessuno l'aveva modificata
        ///   OpenDirty — c'era e qualcuno ci stava gia' lavorando
        /// </summary>
        /// <remarks>
        /// Internal, and the three methods below static, since Z-C21.10 N31:
        /// the overhang probe in <see cref="SectionProfile"/> inserts a real
        /// structural member too, and applies the same rule to the library it
        /// opens.
        /// </remarks>
        internal enum ComEra { Closed, OpenClean, OpenDirty }

        internal static ComEra LibraryState(ISldWorks sw, string filePath)
        {
            var doc = DocumentWithPath(sw, filePath);
            if (doc == null) return ComEra.Closed;
            bool dirty = false;
            try { dirty = doc.GetSaveFlag(); } catch { }
            return dirty ? ComEra.OpenDirty : ComEra.OpenClean;
        }

        internal static IModelDoc2 DocumentWithPath(ISldWorks sw, string filePath)
        {
            if (sw == null || string.IsNullOrWhiteSpace(filePath)) return null;
            try
            {
                var wanted = filePath.Trim().Replace('/', '\\');
                var d = sw.GetFirstDocument() as IModelDoc2;
                int guard = 0;
                while (d != null && guard++ < 200)
                {
                    string its = "";
                    try { its = (d.GetPathName() ?? "").Trim().Replace('/', '\\'); } catch { }
                    if (!string.IsNullOrWhiteSpace(its) &&
                        string.Equals(its, wanted, StringComparison.OrdinalIgnoreCase))
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
        ///
        /// Z-C21.10 N31, what changed: it is static, so the overhang probe
        /// (<see cref="SectionProfile"/>) applies the same rule, and it no
        /// longer writes «closed» on the word of `CloseDoc`. The guide gives
        /// no return value to read (ISldWorks~CloseDoc.html: «void CloseDoc(
        /// System.string Name)»), and names cases in which it does nothing:
        /// «the name of a document that is not open, then this method does
        /// nothing». So the library is looked for again afterwards, and if it
        /// is still there its dirty flag is read (IModelDoc2~GetSaveFlag.html:
        /// «True if this document needs to be saved, false if not»).
        ///
        /// Z-C21.13 N11, what changed: it is closed by its PATH through
        /// <see cref="DocumentByPath.CloseAndReadBack"/>, no longer by its
        /// title, and the read back tells a library still open from one still
        /// LOADED, hidden, because an open document references it. For that one the flag read
        /// after the call is not believed: the N11 probe (29/9/2026, H1d) read
        /// it clean with the edit still in memory, so the flag read before the
        /// call decides.
        /// </summary>
        /// <returns>
        /// Null when the library is not left modified by us: closed, open and
        /// clean, or already modified before we touched it (that one is
        /// logged, it is not this operation's doing). Otherwise the sentence
        /// that the answer must carry: the library is still open and modified,
        /// or still loaded with the changes of the insertion.
        /// </returns>
        internal static string CloseLibraryAgain(
            ISldWorks sw, string profile, ComEra comEra, Action<string> log)
        {
            log = log ?? (m => { });
            // Called on every exit of the operation now, also with a path
            // that never passed File.Exists: GetFileName throws on invalid
            // characters, and this method must not throw.
            string itemName = profile ?? "";
            try { itemName = Path.GetFileName(profile); } catch { }

            // ERA APERTA E QUALCUNO CI STAVA LAVORANDO: non si tocca.
            // Era gia' a rischio prima di noi, e chiuderla vorrebbe dire
            // buttare via il lavoro di qualcun altro.
            if (comEra == ComEra.OpenDirty)
            {
                log("[agente] the library «" + itemName + "» was already open AND " +
                    "modified before this insertion: I do not touch it. " +
                    "ATTENTION: saving it would rewrite it, and it is not " +
                    "this operation's doing.");
                return null;
            }

            var doc = DocumentWithPath(sw, profile);
            if (doc == null) return null;

            // Z-C21.13 N11: the library is found by PATH and closed by path
            // (DocumentByPath); its title only names it in the log. Until
            // library 0.143.0 it was closed by title, and a title with the
            // extensions hidden can be another document's as well.
            string docTitle = "";
            try { docTitle = doc.GetTitle() ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(docTitle)) docTitle = itemName;

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
            bool wasOpen = comEra == ComEra.OpenClean;
            bool dirtyNow = false;
            try { dirtyNow = doc.GetSaveFlag(); } catch { }

            if (wasOpen && !dirtyNow)
            {
                log("[agente] the library «" + docTitle + "» was already open and " +
                    "the insertion did not modify it: I leave it where it was.");
                return null;
            }

            // BY PATH, AND READ BACK (Z-C21.10 N31, Z-C21.13 N11): see the
            // summary and DocumentByPath.
            var closing = DocumentByPath.CloseAndReadBack(sw, doc);
            if (closing.AlsoClosed.Count > 0)
                log("[agent] closing the library «" + docTitle + "» also closed: «" +
                    string.Join("», «", closing.AlsoClosed) + "»" +
                    (closing.AlsoClosedUnsaved.Count > 0
                        ? " (unsaved changes discarded in «" + string.Join("», «", closing.AlsoClosedUnsaved) + "»)"
                        : "") + ".");
            switch (closing.Result)
            {
                case DocumentByPath.CloseResult.Closed:
                    log(wasOpen
                        ? "[agent] closed without saving the library «" + docTitle +
                          "»: it was already open but CLEAN, and the insertion modified " +
                          "it. Leaving it there would leave it one Ctrl+S away from being " +
                          "rewritten. Nothing is lost: reopen it and it is the same."
                        : "[agent] closed without saving the library «" + docTitle +
                          "»: the insertion had opened it and left it modified.");
                    return null;

                case DocumentByPath.CloseResult.StillLoadedHidden:
                    // THE N11 PROBE, H1d: a referenced document stays loaded
                    // after CloseDoc, and its save flag then reads CLEAN with
                    // the edit still in memory. So the flag read before the
                    // call decides, not the one read now.
                    if (!dirtyNow)
                    {
                        log("[agent] " + closing.Message + ". It was clean: nothing to rewrite.");
                        return null;
                    }
                    log("[agent] " + closing.Message + ".");
                    return LibraryLeftLoaded(docTitle);

                case DocumentByPath.CloseResult.StillOpen:
                {
                    // Still visible: the call did nothing, and this flag is
                    // the one it had.
                    bool stillDirty = true;   // unreadable is not clean
                    try { stillDirty = doc.GetSaveFlag(); } catch { }
                    if (!stillDirty)
                    {
                        log("[agent] " + closing.Message + ", but clean: nothing to rewrite.");
                        return null;
                    }
                    log("[agent] " + closing.Message + " and modified: saving it would rewrite it.");
                    return LibraryLeftModified(docTitle);
                }

                default:
                    log("[agent] I cannot close the library «" + docTitle + "»: " + closing.Message +
                        (dirtyNow ? ". ATTENTION: it stays open and modified, saving it would rewrite it." : "."));
                    return dirtyNow ? LibraryLeftModified(docTitle) : null;
            }
        }

        /// <summary>
        /// The sentence the answer carries when the profile library is still
        /// LOADED after CloseDoc, hidden (an open document references it, or,
        /// for a library that was hidden before the call, the call did nothing:
        /// the read back cannot tell the two apart), with the changes the insertion made (Z-C21.13 N11, the probe's H1d:
        /// the save flag then reads clean, and the edit is still in memory).
        /// </summary>
        private static string LibraryLeftLoaded(string name)
        {
            return "ATTENTION: the weldment profile library «" + name + "» is still loaded " +
                   "in SOLIDWORKS, hidden (an open document references it, or the close did " +
                   "nothing), and it " +
                   "keeps the changes the insertion made even if its save flag reads clean. " +
                   "Do not open it and save it: a save would rewrite the shared library. If a " +
                   "document uses it, it is unloaded when that document is closed; otherwise " +
                   "close it by hand, without saving.";
        }

        /// <summary>
        /// The sentence the answer carries when the profile library is still
        /// open and modified after <see cref="CloseLibraryAgain"/> (Z-C21.10
        /// N31). It goes to the reader, not only to the log: the log is
        /// stderr, and the person at the screen is the one who can press
        /// Ctrl+S.
        /// </summary>
        private static string LibraryLeftModified(string name)
        {
            return "ATTENTION: the weldment profile library «" + name + "» is still open " +
                   "in SOLIDWORKS and marked as modified. Do not save it: close it " +
                   "without saving, or a Ctrl+S would rewrite the shared library.";
        }

        /// <summary>
        /// EVERY EXIT CLOSES THE PROFILE LIBRARY AGAIN (Z-C21.10 N31).
        ///
        /// Inserting a structural member makes SOLIDWORKS open the profile
        /// library by itself and leave it marked as MODIFIED. It is not one of
        /// our calls: it happens when inserting by hand too (measured on
        /// 9/9/2026). While it stays like that, a stray Ctrl+S from anyone
        /// rewrites the company's weldment profile library — a file on a
        /// shared disk that every existing weldment depends on — and the
        /// damage shows only when an old frame rebuilds with another section.
        ///
        /// Up to library 0.140.0 only two exits closed it again: the failed
        /// insertion and the line right after it. The rejections that come
        /// after the overhang probe of <see cref="SectionProfile"/> — «the
        /// profile does not fit in the frame», a sketch segment that did not
        /// draw, a group that did not form — returned with the library still
        /// open if the probe had left it so. Now the body runs inside
        /// try/finally, and the library is closed on the way out whatever the
        /// exit, by the rule of <see cref="CloseLibraryAgain"/> (only what we
        /// opened, or what was clean and we dirtied). The probe applies the
        /// same rule to its own trial member.
        ///
        /// If the library is still open and modified after that, the answer
        /// says so: at the end of the rejection message, or in
        /// «profile_library_warning» on success.
        /// </summary>
        private Outcome StructuralMember(IModelDoc2 model, Dictionary<string, object> a)
        {
            string profile = Str(a, "profile_path", "");
            // WAS THE LIBRARY ALREADY OPEN BEFORE US? It must be read NOW,
            // before the probe or the insertion can open it: afterwards «it
            // was already open» and «we opened it» look the same, and closing
            // a library the designer was working on is a damage.
            var libraryBefore = LibraryState(_sw, profile);
            Outcome result = null;
            try
            {
                result = BuildStructuralMember(model, a);
            }
            finally
            {
                string leftModified = CloseLibraryAgain(_sw, profile, libraryBefore, _log);
                if (leftModified != null && result != null)
                {
                    if (result.Succeeded)
                    {
                        if (result.Measurements == null) result.Measurements = new Dictionary<string, object>();
                        result.Measurements["profile_library_warning"] = leftModified;
                    }
                    else result.Error = (result.Error ?? "") + " " + leftModified;
                }
            }
            return result;
        }

        /// <summary>
        /// Whether an EMPTY profile size can be passed to
        /// InsertStructuralWeldment5 (Z-C21.3, finding N25): null when it can,
        /// otherwise the refusal. <paramref name="sizes"/> is what
        /// ISldWorks.GetConfigurationNames returned for the profile file (null
        /// when it could not be read, <paramref name="whyUnread"/> then says
        /// why). The guide gives the empty string for a SOLIDWORKS-supplied
        /// profile; the Z-C21.3 probe measured that on a file with several
        /// sizes it builds the size the file was saved on (the 20 x 20 of the
        /// four in the 2021 «iso\square tube.sldlfp»), so it is accepted only
        /// when the file has ONE size. Pure, no interop in the signature: the
        /// contract group WeldmentConfigurationZc212terContract calls it by
        /// reflection, since no bench case can build a one-size profile that
        /// is known to exist on every station.
        /// </summary>
        internal static string EmptyProfileSizeRefusal(string fileName, string[] sizes, string whyUnread)
        {
            if (sizes == null || sizes.Length == 0)
                return "The profile size («configuration») is empty, and I cannot read " +
                       "the sizes of «" + fileName + "»" +
                       (whyUnread == null ? "" : " (" + whyUnread + ")") + ". An empty size " +
                       "is only safe on a file with one size: on a file with several, " +
                       "SOLIDWORKS builds the size the file was saved on, without an error. " +
                       "Write the size in «configuration», copied from «weldment_profiles».";
            if (sizes.Length > 1)
                return "The profile size («configuration») is empty, and «" + fileName +
                       "» has " + sizes.Length + " sizes: " + string.Join(", ", sizes.Take(12)) +
                       (sizes.Length > 12 ? " and " + (sizes.Length - 12) + " more" : "") +
                       ". With an empty size SOLIDWORKS " +
                       "does not refuse: it builds the size the file was saved on (measured: " +
                       "the 20 x 20 of the four in «square tube»), whatever size you meant. " +
                       "Write one of them in «configuration», exactly as listed.";
            return null;
        }

        /// <summary>
        /// The body of <see cref="StructuralMember"/>, which closes the
        /// profile library on the way out: nothing in here closes it.
        /// </summary>
        private Outcome BuildStructuralMember(IModelDoc2 model, Dictionary<string, object> a)
        {
            string profile = Str(a, "profile_path", "");
            string configuration = Str(a, "configuration", "");
            if (string.IsNullOrWhiteSpace(profile))
                return Failed("Manca il percorso del profilo di saldatura.");
            // An empty size is no longer refused here (Z-C21.3, finding N25):
            // the guide gives the empty string as THE value for a
            // SOLIDWORKS-supplied profile, and whether it can be accepted
            // depends on how many sizes the file has — decided below, after
            // the file is known to exist.
            if (string.IsNullOrWhiteSpace(configuration)) configuration = "";

            // IL CONTROLLO CHE IL SERVER NON PUO' FARE.
            //
            // Il server ha un indice di nomi mandato da una postazione, che
            // puo' essere un'altra e puo' essere di ieri. Qui c'e' il disco
            // vero. Se il file non c'e', InsertStructuralWeldment5 non solleva:
            // ritorna null, e senza questo controllo l'utente leggerebbe «il
            // membro non e' stato creato» senza sapere che il profilo era il
            // problema.
            if (!File.Exists(profile))
                return Failed(
                    "Il profilo «" + profile + "» non esiste su questa postazione. " +
                    "O il percorso e' sbagliato, o la libreria dei profili non e' " +
                    "raggiungibile da qui.");

            // E la configurazione? Stessa storia, peggiore: se la misura non
            // c'e', SolidWorks NON fallisce — usa la configurazione attiva del
            // profilo. Il pezzo esce con una parete diversa da quella
            // confermata, il rebuild e' pulito, e nessuno se ne accorge fino
            // all'officina.
            //
            // THE EMPTY SIZE (Z-C21.3, finding N25). IFeatureManager~
            // InsertStructuralWeldment5.html, Remarks: «If using ...
            // SOLIDWORKS-supplied weldment profile -> Then specify
            // ConfigurationName as ... Empty string; Custom weldment profile ->
            // Name of the configuration in the custom weldment profile». Until
            // library 0.141.0 an empty size was refused outright. The Z-C21.3
            // probe of the member group (28/9/2026, variants N7-N9) measured
            // that the empty string WORKS — the member is born — but on
            // a file with several sizes it builds the size the file was saved
            // on, which is the danger this block exists for: the SOLIDWORKS 2021
            // «iso\square tube.sldlfp» has four sizes (20 x 20 x 2 ... 80 x 80 x
            // 5), and "" gave the 20 x 20 section (N7); the company library with
            // "" gave a 50 x 50 section (N9). So the empty size is accepted when
            // the file has ONE size (the guide's case: one file per size,
            // nothing to choose), and refused with the sizes listed when it has
            // several, or when they cannot be read. A size that is written is
            // checked as before, in the else branch.
            string onlySize = null;
            if (configuration.Length == 0)
            {
                string[] sizes = null;
                string whyUnread = null;
                try { sizes = _sw.GetConfigurationNames(profile) as string[]; }
                catch (Exception ex) { whyUnread = ex.Message; }
                string fileName = Path.GetFileNameWithoutExtension(profile);
                string refusal = EmptyProfileSizeRefusal(fileName, sizes, whyUnread);
                if (refusal != null) return Failed(refusal);
                onlySize = sizes[0];
                _log("[agente] empty profile size accepted: «" + fileName +
                     "» has one size only, «" + onlySize + "».");
            }
            else try
            {
                var available = _sw.GetConfigurationNames(profile) as string[];
                if (available != null && available.Length > 0)
                {
                    if (!available.Contains(configuration, StringComparer.Ordinal))
                    {
                        var similar = available.FirstOrDefault(
                            x => string.Equals(x, configuration, StringComparison.OrdinalIgnoreCase));
                        string help = similar != null
                            ? " Nel file quella misura si scrive «" + similar + "»: " +
                              "maiuscole e virgole contano."
                            : " Misure disponibili: " +
                              string.Join(", ", available.Take(12)) +
                              (available.Length > 12 ? " ..." : "");
                        return Failed(
                            "Il profilo non ha la misura «" + configuration + "»." + help);
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
            var requestedFrame = RequestedFrame(a);
            var segments = SegmentsFrom(a, out string reason);
            if (segments == null && requestedFrame == null) return Failed(reason);

            double rotation = Num(a, "rotation_deg", 0);
            double gap = Num(a, "gap_mm", 0);
            string treatment = Str(a, "corner_treatment", "");

            // THE CORNER DEFAULT IS SET, NOT LEFT TO SOLIDWORKS (Z-C21.3,
            // finding N25). The catalogue promises the 45 degree miter when the
            // corner treatment is not given, for the rectangular frame and for
            // the closed path of a structural member. Until library 0.141.0 the
            // group was left as CreateStructuralMemberGroup makes it, and the
            // answer said the treatment was not given. The Z-C21.3 probe of the
            // member group (28/9/2026, verdicts in the TODO) read a fresh
            // group — ApplyCornerTreatment False, CornerTreatmentType 0, the
            // same on every call — and on an L of 80X80X5 (500 + 400 mm) that
            // gives NO corner treatment: the bars interpenetrate (bar A ends at
            // X 500, bar B at Z 0, volume 1322960.17 mm3), while the miter (N1:
            // ApplyCornerTreatment true, CornerTreatmentType 1) gives A to X
            // 540, B to Z -40, two 45 degree faces, volume 1322967.37 mm3.
            // IStructuralMemberGroup~CornerTreatmentType.html, Remarks: «This
            // property is only available when IStructuralMemberGroup::
            // ApplyCornerTreatment is true»; Insert_Structural_Weldment_Example_
            // CSharp.htm sets both (swEndConditionMiter). So the default is
            // written into the request here, before the frame's overhang probe
            // reads it (the trial frame must have the same corners as the real
            // one), and the answer says it was the default.
            bool cornerAssumed = string.IsNullOrWhiteSpace(treatment);
            if (cornerAssumed) treatment = "miter";

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
            // SectionProfile costruisce un TELAIO di prova con le
            // stesse opzioni e misura di quanto sporge.
            //
            // Il server manda `frame` INSIEME ai segmenti gia' calcolati sul
            // nominale: quelli servono al riassunto e restano il ripiego se
            // qualcuno chiede esplicitamente le quote d'asse.
            double[] promisedBoundingBox = null;
            double[] overhangs = null;
            // Where the overhang came from, and how to drop it from the
            // archive if the frame built with it comes out wrong (Z-C21.10 N36).
            string overhangSource = null;
            Func<bool> forgetOverhang = null;
            int axisU = -1, axisV = -1;
            var frame = requestedFrame;

            // THE READING OF THE TWO SIZES, IN THE CATALOGUE SPELLING
            // (Z-C21.10 N32). The comparisons below are Ordinal, and until
            // library 0.140.0 the value arrived as written: «Ingombro» was not
            // «overall», so it fell into the centre-line branch and a
            // 1000x800 frame came out 1080x880 with riuscito = true — and so
            // did any other word («outer»). The bridge's Forma accepts enum
            // values without regard to case and rewrites the top-level ones in
            // the catalogue spelling (Bridge/Shape.cs, pattern S20), but the
            // nested «frame» the server builds is not in the catalogue schema
            // at all, and the bench and the add-in do not go through the
            // bridge: no check upstream sees every caller. Here the value is
            // matched without regard to case, and anything that is neither
            // reading is refused before a single line is drawn. The reading
            // used goes back in the answer as «measured_as».
            string askedReading = Str(frame, "measured_as", "overall");
            string howItIsMeasured = FrameReading(askedReading);
            if (frame != null && howItIsMeasured == null)
                return Failed(
                    "«measured_as» can only be «overall» (the outside size of the finished " +
                    "frame, the default) or «center_distance» (the distance between the bar " +
                    "axes), and it says «" + askedReading + "». Write one of the two, " +
                    "or leave «measured_as» out for «overall». I do not guess: read the " +
                    "other way, the frame would come out larger or smaller by the size " +
                    "of the profile.");

            // ── IL TELAIO A INTERASSE ────────────────────────────────────
            //
            // Le due quote sono la LINEA D'ASSE del percorso, non l'ingombro
            // finito: e' il caso di chi ragiona sugli interassi — fori,
            // piastre, attacchi — e la sporgenza del profilo se la tiene.
            // Il percorso e' allora il rettangolo nominale, senza
            // arretramento.
            //
            // Il catalogo lo documenta da sempre fra i valori ammessi di
            // «measured_as». Il C# no: cadeva nel ripiego dei segmenti, che pero'
            // questa operazione esiste apposta per non far scrivere.
            if (frame != null && segments == null &&
                !string.Equals(howItIsMeasured, "overall", StringComparison.Ordinal))
            {
                string planeAxis = Str(frame, "plane", "front");
                int aU, aV;
                if (!PlaneAxes(planeAxis, out aU, out aV))
                    return Failed("Non conosco il piano «" + planeAxis + "» per un telaio.");

                double axisLength = Num(frame, "length_mm", 0);
                double axisWidth = Num(frame, "width_mm", 0);
                if (axisLength <= 0 || axisWidth <= 0)
                    return Failed("Il telaio non ha due quote utilizzabili.");

                segments = FrameSegments(
                    axisLength, axisWidth, aU, aV,
                    Bool(frame, "centered", true), new[] { 0.0, 0.0 });
                _log(string.Format(
                    "[agente] telaio a interasse {0:0.#}x{1:0.#}: il percorso e' " +
                    "quello, e l'ingombro finito sara' piu' grande di quanto " +
                    "sporge il profilo.", axisLength, axisWidth));
            }

            if (frame != null &&
                string.Equals(howItIsMeasured, "overall", StringComparison.Ordinal))
            {
                string plane = Str(frame, "plane", "front");
                if (!PlaneAxes(plane, out axisU, out axisV))
                    return Failed("Non conosco il piano «" + plane + "» per un telaio.");

                double along = Num(frame, "length_mm", 0);
                double wide = Num(frame, "width_mm", 0);
                if (along <= 0 || wide <= 0)
                    return Failed("Il telaio non ha due quote utilizzabili.");

                int angleCode;
                if (!CornerTreatments.TryGetValue(treatment ?? "", out angleCode))
                    angleCode = 0;
                overhangs = SectionProfile.FrameOverhang(
                    _sw, profile, configuration, axisU, axisV, rotation,
                    angleCode, gap, _log, out overhangSource);
                int probeU = axisU, probeV = axisV;
                forgetOverhang = () => SectionProfile.ForgetFrameOverhang(
                    profile, configuration, probeU, probeV, rotation, angleCode, gap, _log);
                if (overhangs == null)
                    return Failed(
                        "Non sono riuscito a misurare quanto sporge il profilo «" +
                        Path.GetFileNameWithoutExtension(profile) + "» " + configuration +
                        ", e senza quella misura non so di quanto arretrare il percorso: " +
                        "un telaio costruito lo stesso uscirebbe piu' grande di quanto " +
                        "promesso. Meglio fermarsi.");

                // Una sezione piu' larga del telaio non e' un telaio: le barre
                // si compenetrerebbero e il percorso avrebbe lunghezza nulla o
                // negativa. Va detto prima, non lasciato fallire dentro
                // SolidWorks con un messaggio che non spiega niente.
                if (overhangs[0] >= along - 0.01 || overhangs[1] >= wide - 0.01)
                    return Failed(string.Format(
                        "Con questo profilo il telaio sporge di {0:0.#}x{1:0.#} mm oltre " +
                        "il percorso: un telaio di {2:0.#}x{3:0.#} mm di ingombro esterno " +
                        "non ci sta. Serve un telaio piu' grande o un profilo piu' piccolo.",
                        overhangs[0], overhangs[1], along, wide));

                segments = FrameSegments(
                    along - overhangs[0], wide - overhangs[1],
                    axisU, axisV, Bool(frame, "centered", true), overhangs);
                promisedBoundingBox = new[] { along, wide };
                _log(string.Format(
                    "[agente] telaio a quote esterne {0:0.#}x{1:0.#}: sporgenza misurata " +
                    "{2:0.##}x{3:0.##}, percorso arretrato a {4:0.##}x{5:0.##}",
                    along, wide, overhangs[0], overhangs[1],
                    along - overhangs[0], wide - overhangs[1]));
            }

            // SE ARRIVATI QUI I SEGMENTI ANCORA NON CI SONO, il telaio c'era
            // ma non era utilizzabile — «measured_as» diverso da «overall», o un
            // piano che non si riconosce. Si dice quello, non «manca il
            // percorso»: chi ha scritto un telaio ha chiesto un telaio.
            if (segments == null)
                return Failed(
                    "Hai chiesto un telaio ma non ho potuto ricavarne il " +
                    "percorso, e non mi hai dato dei segmenti su cui ripiegare. " +
                    "Un telaio vuole «length_mm», «width_mm» e «plane»; " +
                    "se invece vuoi dare tu il percorso, passa «segments».");

            // ── lo schizzo 3D del percorso ──────────────────────────────
            model.ClearSelection2(true);
            var sketches = model.SketchManager;
            var created = new List<ISketchSegment>();
            try
            {
                sketches.Insert3DSketch(true);
                foreach (var s in segments)
                {
                    var seg = sketches.CreateLine(
                        Units.M(s.Da[0]), Units.M(s.Da[1]), Units.M(s.Da[2]),
                        Units.M(s.A[0]), Units.M(s.A[1]), Units.M(s.A[2])) as ISketchSegment;
                    if (seg == null)
                    {
                        try { sketches.Insert3DSketch(true); } catch { }
                        return Failed(
                            "Non sono riuscito a disegnare il segmento da (" +
                            Triplet(s.Da) + ") a (" + Triplet(s.A) + ")." + WithoutSketch(model));
                    }
                    created.Add(seg);
                }
                sketches.Insert3DSketch(true);
            }
            catch (Exception ex)
            {
                try { sketches.Insert3DSketch(true); } catch { }
                return Failed("Lo schizzo del percorso non e' riuscito: " + ex.Message);
            }

            if (created.Count == 0)
                return Failed("Nessun segmento disegnato: non c'e' percorso su cui mettere le barre.");

            // ── il gruppo ────────────────────────────────────────────────
            IStructuralMemberGroup theGroup;
            try
            {
                theGroup = model.FeatureManager.CreateStructuralMemberGroup()
                    as IStructuralMemberGroup;
            }
            catch (Exception ex)
            {
                return Failed("Non sono riuscito a creare il gruppo: " + ex.Message + WithoutSketch(model));
            }
            if (theGroup == null)
                return Failed("Non sono riuscito a creare il gruppo di membri." + WithoutSketch(model));

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
                theGroup.Segments = created.Cast<SketchSegment>().ToArray();
                if (Math.Abs(rotation) > 1e-9) theGroup.Angle = Units.Rad(rotation);

                int code;
                if (CornerTreatments.TryGetValue(treatment ?? "", out code))
                {
                    // «none» non e' un trattamento: e' l'assenza. Chiederlo a
                    // SolidWorks come tipo 0 CON ApplyCornerTreatment acceso
                    // darebbe un risultato diverso da non chiederlo affatto.
                    theGroup.ApplyCornerTreatment = code != 0;
                    if (code != 0) theGroup.CornerTreatmentType = code;
                }
                if (gap > 0)
                {
                    theGroup.GapWithinGroup = Units.M(gap);
                    theGroup.GapForOtherGroups = Units.M(gap);
                }
            }
            catch (Exception ex)
            {
                return Failed("Non sono riuscito a impostare il gruppo: " + ex.Message + WithoutSketch(model));
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
                // AND ON THE ROUND TUBE TOO (finding L9-6, NOT REAL). The doubt
                // was that option 2 might differ from 1 on a round profile. The
                // Z-C21.3 probe of 28/9/2026 (saldature probe_connected_segments,
                // TUBO METRICO BHP 42 X 3 against TUBO STRUTTURALE QUADRO
                // 80X80X5) built, with each option, an L with no corner
                // treatment, the default one, miter, butt 1 and butt 2, and a T:
                // option 1 and option 2 gave the same result in every scene, two
                // bodies each, volume difference 0 mm3, the same faces (the round
                // L 257296.44 mm3 with no treatment; the square control gave the
                // same as the measure of 9 September). So 1 stays.
                //
                // Anche i gruppi vanno tipizzati, per la stessa ragione dei
                // segmenti qui sopra.
                feature = model.FeatureManager.InsertStructuralWeldment5(
                    Path: profile,
                    ConnectedSegmentsOption: 1,
                    AllowProtrusion: false,
                    Groups: new[] { (StructuralMemberGroup)theGroup },
                    ConfigurationName: configuration) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("Il membro strutturale non e' riuscito: " + ex.Message + WithoutSketch(model));
            }

            // The profile library that this insertion opened is closed again
            // by StructuralMember on the way out, whatever the exit
            // (Z-C21.10 N31): see its summary.

            if (feature == null)
                return Failed(
                    "Il membro strutturale non e' stato creato. Di solito e' il " +
                    "percorso: segmenti che non si toccano, o che si incrociano " +
                    "in un modo che il profilo non riesce a girare." + WithoutSketch(model));

            double length = segments.Sum(s => Distance(s.Da, s.A));
            var measurements = new Dictionary<string, object>
            {
                ["profile"] = Path.GetFileNameWithoutExtension(profile),
                ["configuration"] = configuration,
                ["bars"] = segments.Count,
                ["path_length_mm"] = Math.Round(length, 2),
                ["corner_treatment"] = treatment,
                // Z-C21.3 N25: always written, like the material's note: a
                // default that is applied without saying so reads the same as
                // a choice somebody made.
                ["corner_treatment_note"] = cornerAssumed
                    ? "the corner treatment is the DEFAULT, the 45 degree miter where the " +
                      "bars of this group touch: you did not ask for it. Name another one " +
                      "in the corner treatment argument if you want it."
                    : "the corner treatment is the one you asked for.",
            };
            // Z-C21.3 N25: an empty size was accepted only because the file has
            // one size; the answer names it.
            if (onlySize != null)
                measurements["configuration_note"] =
                    "empty size accepted: the profile file has one size only, «" + onlySize + "».";
            if (overhangs != null)
                measurements["overhang_profile_mm"] =
                    Math.Round(overhangs[0], 2) + "x" + Math.Round(overhangs[1], 2);
            // Z-C21.10 N32: which reading of the two sizes was used, always,
            // so that a frame read as centre lines cannot pass for an outside
            // size without the answer saying so.
            if (frame != null) measurements["measured_as"] = howItIsMeasured;
            // Z-C21.10 N36: measured now, or taken from the archive.
            if (overhangSource != null) measurements["overhang_source"] = overhangSource;

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
            var box = BodiesBoundingBox(feature);
            if (box != null)
            {
                measurements["bounding_box_mm"] = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.##}x{1:0.##}x{2:0.##}",
                    box[3] - box[0], box[4] - box[1], box[5] - box[2]);
            }
            if (promisedBoundingBox != null)
            {
                if (box == null)
                {
                    measurements["bounding_box_checked"] = false;
                    _log("[agente] non ho potuto misurare l'ingombro del telaio appena fatto.");
                }
                else
                {
                    double seenU = box[3 + axisU] - box[axisU];
                    double seenV = box[3 + axisV] - box[axisV];
                    double uError = Math.Abs(seenU - promisedBoundingBox[0]);
                    double vError = Math.Abs(seenV - promisedBoundingBox[1]);
                    // A WRONG FRAME BUILT ON A REMEMBERED OVERHANG (Z-C21.10
                    // N36). Until library 0.140.0 the archive entry stayed, so
                    // «withdraw it and try again» rebuilt the same wrong frame
                    // from the same line of the same file, and the rejection
                    // did not name that file. Now the entry is dropped here, and
                    // the message says where the overhang came from.
                    bool wrongFrame = uError > 0.5 || vError > 0.5;
                    bool overhangDropped = wrongFrame &&
                        overhangSource == SectionProfile.OverhangFromArchive &&
                        forgetOverhang != null && forgetOverhang();
                    if (wrongFrame)
                        return Failed(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "Il telaio doveva essere {0:0.#}x{1:0.#} mm di ingombro esterno " +
                            "ed e' venuto {2:0.##}x{3:0.##}. Non te lo lascio passare per " +
                            "buono: ritiralo — " + Sentences.HowToWithdrawIt +
                            ". (Sporgenza misurata del " +
                            "profilo: {4:0.##}x{5:0.##} mm.)",
                            promisedBoundingBox[0], promisedBoundingBox[1], seenU, seenV,
                            overhangs[0], overhangs[1]) +
                            OverhangNote(overhangSource, overhangDropped));
                    measurements["bounding_box_checked"] = true;
                    measurements["bounding_box_outer_mm"] =
                        Math.Round(seenU, 2) + "x" + Math.Round(seenV, 2);
                }
            }
            return Succeeded(feature, measurements);
        }

        // ─────────────────────────────────────────────────────────────────
        // Lavorazioni dopo le barre
        // ─────────────────────────────────────────────────────────────────

        private Outcome TrimMembers(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            int nFrom, nAgainst;
            string why;
            var toCut = BodiesFromReference(a, "bodies_to_trim_ref", docState, out nFrom, out why);
            if (toCut == null) return Failed(why);
            var against = BodiesFromReference(a, "trim_boundary_ref", docState, out nAgainst, out why);
            if (against == null) return Failed(why);

            // AN END CONDITION THAT IS NOT ONE OF THE FOUR IS REFUSED (finding
            // N22). Until library 0.144.0 an unknown word fell back to Trim in
            // silence, and the answer did not say which condition was used.
            // «none» is in the shared table of corner treatments (it means
            // «no corner treatment» for a member group), but the catalogue
            // offers four conditions for this operation, the trim/extend of
            // SOLIDWORKS has no «none» corner type, and no probe ever trimmed
            // with swEndConditionNone (the Z-C21.3 trim probe measured 1, 2, 3
            // and 4): so it is refused here too, rather than documented.
            string askedCondition = (Str(a, "end_condition", "cut") ?? "").Trim();
            if (askedCondition.Length == 0) askedCondition = "cut";   // empty reads as absent
            int code = TrimEndCondition(askedCondition);
            if (code < 0)
                return Failed(
                    "«end_condition» can be «cut» (the default), «miter», «butt1» or «butt2», and " +
                    "it says «" + askedCondition + "». I do not fall back to «cut» for you: write one of " +
                    "the four, or leave «end_condition» out for «cut». Nothing was changed.");
            double gap = Num(a, "gap_mm", 0);

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
            // Il danno vero era l'altro: con `gap_mm` a 0 — il predefinito — il
            // bit restava spento e SOLIDWORKS usava l'ultimo gioco dato a mano
            // da Accorcia/Estendi, mentre il riassunto diceva `gap_mm: 0`. Il
            // ponte gira sullo stesso SOLIDWORKS del lavoro vero: basta che chi
            // disegna abbia usato un gioco da interfaccia perche' le chiamate dopo
            // lo ereditino. Il caso `cut_without_gap_does_not_inherit` lo guarda.
            //
            // THE TWO EXTENSION BITS ARE ON TOO (Z-C21.3, finding L9-4). The
            // catalogue says this operation trims the bars OR EXTENDS them until
            // they touch; with the gap bit alone it could only trim. Guide:
            // IFeatureManager~InsertWeldmentTrimFeature2.html, «Options: Logical
            // sum of values as defined in swWeldmentTrimExtendOptionType_e»;
            // swWeldmentTrimExtendOptionType_e.html (swconst): AllowTrimmedExtensionTrim
            // 1, AllowTrimmingExtensionTrim 2, CopedCut 4, WeldGap 8;
            // Insert_Weldment_Features_Example_CSharp.htm passes 1 + 2 + 4 + 8.
            // The Z-C21.3 probe of 28/9/2026 (typed Body2[] arrays, 80X80X5 tube,
            // gap 0) measured, with the default end condition (Trim, 4):
            //   - bar B stopping 20 mm short of bar A's face: Options 8 and 10
            //     return a feature that is NOT in the tree, volume unchanged (the
            //     phantom refused below); 9 and 11 extend B to A's face, +29399.11
            //     mm3 (20 mm x 1469.956 mm2), B from Z 60 to Z 40;
            //   - bar B entering A by 40 mm: 8 and 11 cut the same -58798.23 mm3;
            //     15 cuts -57636.82 (B stops at Z 34, not 40): the coped cut bit 4
            //     changes a plain trim, so it stays OFF;
            //   - an L with a 20 mm gap and the corner end conditions (1, 2, 3):
            //     8 already closes the corner, and 11 gives the same numbers.
            // So 1 | 2 | 8 = 11: the same cuts as before, and the extension the
            // catalogue promises.
            int options =
                (int)swWeldmentTrimExtendOptionType_e.swWeldmentTrimExtendOption_AllowTrimmedExtensionTrim |
                (int)swWeldmentTrimExtendOptionType_e.swWeldmentTrimExtendOption_AllowTrimmingExtensionTrim |
                (int)swWeldmentTrimExtendOptionType_e.swWeldmentTrimExtendOption_WeldGap;

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
            // dire niente. (The old note here, «with the extension options on:
            // always a phantom», was measured with object[] and proved nothing:
            // with Body2[] the extension bits extend, see the Options above.)
            //
            // `cut_list` passava gia' `Body2[]` ed e' per questo che
            // funzionava.
            Body2[] toCutTyped, typedAgainst;
            try
            {
                toCutTyped = toCut.Cast<Body2>().ToArray();
                typedAgainst = against.Cast<Body2>().ToArray();
            }
            catch (InvalidCastException)
            {
                return Failed("Fra i corpi indicati c'e' qualcosa che non e' un corpo solido.");
            }

            double? volumeBefore = VolumeMm3(model);
            int featureBefore = CountTreeFeatures(model);
            if (volumeBefore == null || featureBefore < 0) return Failed(VolumeNotReadBefore("tagliare le barre"));

            IFeature feature;
            try
            {
                feature = model.FeatureManager.InsertWeldmentTrimFeature2(
                    EndCond: code,
                    Options: options,
                    GapValue: Units.M(gap),
                    BodiesToTrim: toCutTyped,
                    BodiesOrFaces: typedAgainst) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("Il taglio fra le barre non e' riuscito: " + ex.Message);
            }
            if (feature == null)
                return Failed(
                    "Il taglio non e' stato creato. Di solito le barre da " +
                    "tagliare e quelle di taglio non si incontrano davvero.");

            // LA FEATURE TORNATA NON BASTA: bisogna che sia nell'albero e che
            // il pezzo sia cambiato. E' la guardia che avrebbe preso il
            // difetto qui sopra il primo giorno — l'operazione diceva
            // «bodies_trimmed: 1» su un pezzo identico a prima.
            try { model.EditRebuild3(); } catch { }
            int featureAfter = CountTreeFeatures(model);
            // Con l'albero illeggibile non si puo' dire che la feature non c'e':
            // decide il volume, qui sotto (voce Z-C8).
            bool inTree = featureAfter < 0 || featureAfter > featureBefore;
            double? volumeAfter = VolumeMm3(model);
            if (!inTree)
                // Con gli array tipizzati succede ancora, e il 25/9/2026 e'
                // successo sempre e solo quando le barre non si incontrano: due
                // parallele a 200 mm, o una barra lontana. E' la risposta di
                // SOLIDWORKS a «non c'e' niente da tagliare».
                return Failed(
                    "SOLIDWORKS ha risposto con una feature «" + feature.Name + "» che " +
                    "nell'albero non c'e', e il pezzo e' quello di prima: di solito vuol " +
                    "dire che le barre da tagliare non incontrano quelle di taglio. Non " +
                    "ti dico che le barre sono tagliate.");
            if (volumeAfter == null)
                return Succeeded(feature, new Dictionary<string, object>
                {
                    ["bodies_trimmed"] = nFrom,
                    ["trim_boundary_bodies"] = nAgainst,
                    ["gap_mm"] = gap,
                    ["end_condition"] = askedCondition.ToLowerInvariant(),   // N22: the one used
                    ["volume_change_mm3"] = null,
                    ["volume_not_reread"] = NoteVolumeNotReread,
                });
            double variation = volumeAfter.Value - volumeBefore.Value;
            if (Math.Abs(variation) < 0.01)
                return Failed(
                    "Il taglio e' nell'albero ma non ha cambiato niente: volume " +
                    volumeBefore.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    " mm3 prima e dopo. Le barre da tagliare non entrano in quelle di " +
                    "taglio, o gia' ci arrivano esatte. Non ti confermo un pezzo diverso " +
                    "da quello che ti ho mostrato — " + Sentences.HowToWithdrawIt + ".");

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["bodies_trimmed"] = nFrom,
                ["trim_boundary_bodies"] = nAgainst,
                ["gap_mm"] = gap,
                ["end_condition"] = askedCondition.ToLowerInvariant(),   // N22: the one used
                // Negativo = tolto (taglio), positivo = aggiunto (estensione).
                ["volume_change_mm3"] = Math.Round(variation, 2),
            });
        }

        /// <summary>
        /// Quante feature ha l'albero, al primo livello. Serve a una cosa sola:
        /// sapere se una feature che SOLIDWORKS ha restituito ci e' entrata
        /// davvero (vedi `TrimMembers`). -1 se non si e' potuto leggere (voce
        /// Z-C8): un conteggio a meta' faceva dire «la feature nell'albero non
        /// c'e'» a un taglio riuscito.
        /// </summary>
        private static int CountTreeFeatures(IModelDoc2 model)
        {
            int n = 0;
            try
            {
                var f = model.FirstFeature() as IFeature;
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
        private Outcome CutList(IModelDoc2 model, Dictionary<string, object> a)
        {
            bool update = Bool(a, "update_if_exists", true);
            var partDoc = model as IPartDoc;
            if (partDoc == null) return Failed("La distinta di taglio si fa su una parte.");

            // UNA SECONDA DISTINTA NON E' UN DOPPIONE INNOCUO: si aggiorna quella
            // che c'e', o si rifiuta se non si vuole.
            bool alreadyExisted = CutListFolders(model).Count > 0;
            if (alreadyExisted && !update)
                return Failed(
                    "Nel pezzo c'e' gia' una distinta di taglio. Crearne una " +
                    "seconda vorrebbe dire due elenchi della stessa " +
                    "carpenteria: se vuoi ricalcolare quella che c'e', " +
                    "chiedi «update_if_exists».");

            object[] solidBodies;
            try { solidBodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[]; }
            catch (Exception ex) { return Failed("Non riesco a leggere i corpi del pezzo: " + ex.Message); }
            if (solidBodies == null || solidBodies.Length == 0)
                return Failed(
                    "Nel pezzo non c'e' nessun corpo solido: non c'e' niente da " +
                    "mettere in distinta. Se le barre le doveva fare un passo " +
                    "prima, quel passo non e' riuscito.");

            // ── 1. IL PEZZO DIVENTA UN SALDATO, se non lo e' ────────────────
            bool? wasWelded = null;
            try { wasWelded = partDoc.IsWeldment(); } catch { }
            var featuresBefore = new HashSet<string>(FeatureNames(model));
            // THE FEATURE InsertWeldmentFeature RETURNS IS KEPT (finding N30):
            // IFeatureManager~InsertWeldmentFeature.html, Return Value: «Pointer
            // to the IFeature object». It is the one thing a refusal below takes
            // away again.
            IFeature weldmentAdded = null;
            if (wasWelded != true)
            {
                try { weldmentAdded = model.FeatureManager.InsertWeldmentFeature() as IFeature; } catch { }
                try { model.ForceRebuild3(false); } catch { }
            }
            // IF A STEP BELOW FAILS, the weldment feature added here is removed
            // again, and the message says whether that worked, read back from
            // IsWeldment (review of 26/9/2026; N30).
            Func<string, Outcome> refuse = text =>
            {
                string undo = wasWelded != true
                    ? RemoveAddedWeldment(model, partDoc, weldmentAdded, featuresBefore, solidBodies.Length)
                    : "";
                return Failed(text + undo);
            };
            bool welded = false;
            try { welded = partDoc.IsWeldment(); } catch { }
            if (!welded)
                return refuse(
                    "Non riesco a fare del pezzo un saldato (la lavorazione «Saldatura» non nasce): senza, " +
                    "SOLIDWORKS non separa le barre per lunghezza, e una distinta con tutte le barre in una " +
                    "voce sola manderebbe in officina le lunghezze sbagliate.");

            // ── 2. LA DISTINTA AUTOMATICA ───────────────────────────────────
            //
            // THE FOUR ANSWERS ARE READ (findings N15, N41). Until library
            // 0.144.0 SetAutomaticCutList, SetAutomaticUpdate and both
            // UpdateCutList calls were dropped:
            //   - IBodyFolder~SetAutomaticCutList.html: «True if enabled, false
            //     if not»; Remarks: it returns false if the part has no weldment
            //     feature, or if «a cut list folder already exists in the part.
            //     You must delete the cut list before you can enable automatic
            //     generation». It is asked only when GetAutomaticCutList reads
            //     false: a new weldment part has the automatic cut list already
            //     (~GetAutomaticCutList.html, «By default, a cut list is
            //     automatically generated in new weldment parts»), and there the
            //     cut list folders that exist would make it answer false;
            //   - ~SetAutomaticUpdate.html: «True if successful, false if not»;
            //   - ~UpdateCutList.html: «True if the automatically generated cut
            //     list is successfully updated, false if not». A false one means
            //     that the quantities and lengths read below may be those of
            //     before the last change (N41): the answer says so.
            bool automatic = false;
            bool alreadyAutomatic = false;
            bool? automaticCutListSet = null;      // null: not asked, it was on already
            bool? automaticUpdateSet = null;
            bool? cutListUpdated = null;
            try
            {
                var f0 = model.FirstFeature() as IFeature;
                while (f0 != null)
                {
                    if (f0.GetTypeName2() == "SolidBodyFolder")
                    {
                        var bf = f0.GetSpecificFeature2() as IBodyFolder;
                        if (bf != null)
                        {
                            try { alreadyAutomatic = bf.GetAutomaticCutList(); } catch { }
                            if (!alreadyAutomatic) automaticCutListSet = bf.SetAutomaticCutList(true);
                            automaticUpdateSet = bf.SetAutomaticUpdate(true);
                            cutListUpdated = bf.UpdateCutList();
                            automatic = bf.GetAutomaticCutList();
                        }
                    }
                    f0 = f0.GetNextFeature() as IFeature;
                }
            }
            catch { }
            // Each item folder is refreshed too; their answers are counted and
            // given back, not judged: the guide ties UpdateCutList to «the
            // automatically generated cut list», and what an item folder answers
            // was never measured.
            int folderUpdatesFalse = 0, folderUpdatesUnread = 0;
            foreach (var c in CutListFolders(model))
            {
                try { if (!c.UpdateCutList()) folderUpdatesFalse++; }
                catch { folderUpdatesUnread++; }
            }
            // THE REBUILD BEFORE THE Get6 READS BELOW STAYS (Z-C21.4, pattern S8
            // «Get6 without ForceRebuild3»). The Z-C21.3 probe of L8-5 (Get6 on
            // a configuration never activated, case R4) found ForceRebuild3 after Get6
            // not needed on a configuration never activated: the values were
            // already right. That probe did not build a configuration that is
            // really out of date, so the call is kept as a defence and is not
            // removed without a probe that does.
            try { model.ForceRebuild3(false); } catch { }
            if (!automatic)
                return refuse(AutomaticCutListRefusal(automaticCutListSet, alreadyExisted));

            // ── 3. LE VOCI, LETTE ───────────────────────────────────────────
            var folders = CutListFolders(model);
            if (folders.Count == 0)
                return refuse("La distinta automatica e' accesa ma non ha prodotto nessuna voce.");
            int? bodiesInCutList = 0;
            int? totalQuantity = 0;
            var entries = new List<object>();
            var lengths = new List<object>();
            var itemProblems = new List<string>();
            IFeature beforeFolder = null;
            foreach (var c in folders)
            {
                int bodies = -1;
                try { bodies = c.GetBodyCount(); } catch { }
                if (bodies < 0) bodiesInCutList = null; else if (bodiesInCutList.HasValue) bodiesInCutList += bodies;
                // BODY BY BODY, NOT ONLY THE SUM (N15): the volumes of the bodies
                // of this item, null when one of them cannot be read.
                double[] itemVolumes = VolumesOfFolderBodies(c);
                IFeature fc = null;
                try { fc = c.GetFeature() as IFeature; } catch { }
                if (beforeFolder == null) beforeFolder = fc;
                string descriptionText = null, length = null, quantity = null;
                try
                {
                    var cpm = fc?.CustomPropertyManager;
                    foreach (var o in (cpm?.GetNames() as object[]) ?? new object[0])
                    {
                        string itemName = Convert.ToString(o), val, ris; bool used, connected;
                        cpm.Get6(itemName, false, out val, out ris, out used, out connected);
                        string v = val ?? "";
                        if (v.IndexOf("TOTAL LENGTH@@@", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (v.IndexOf("LENGTH@@@", StringComparison.OrdinalIgnoreCase) >= 0) length = ris;
                        else if (v.IndexOf("QUANTITY@@@", StringComparison.OrdinalIgnoreCase) >= 0) quantity = ris;
                        else if (itemName.Equals("Descrizione", StringComparison.OrdinalIgnoreCase) ||
                                 itemName.Equals("Description", StringComparison.OrdinalIgnoreCase)) descriptionText = ris;
                    }
                }
                catch { }
                double l;
                bool lNum = double.TryParse((length ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out l);
                int q;
                bool qNum = int.TryParse((quantity ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out q);
                if (!qNum) totalQuantity = null; else if (totalQuantity.HasValue) totalQuantity += q;
                if (lNum) lengths.Add(Math.Round(l, 2));
                string itemProblem = CutListItemProblem(descriptionText, bodies, qNum ? (int?)q : null, itemVolumes);
                if (itemProblem != null) itemProblems.Add(itemProblem);
                entries.Add(new Dictionary<string, object>
                {
                    ["details"] = descriptionText,
                    ["length_mm"] = lNum ? (object)Math.Round(l, 2) : length,
                    ["quantity"] = qNum ? (object)q : quantity,
                    ["bodies"] = NumberOrNull(bodies),
                    // N15: true when every body of the item has the same volume;
                    // null when a volume could not be read (or the item has none).
                    ["bodies_equal"] = BodiesAreEqual(itemVolumes),
                });
            }

            var measurements = new Dictionary<string, object>
            {
                ["bodies_in_cut_list"] = bodiesInCutList,
                ["distinct_entries"] = entries.Count,
                ["entries"] = entries.ToArray(),
                ["lengths_mm"] = lengths.ToArray(),
                ["quantity_total"] = totalQuantity,
                ["already_existed"] = alreadyExisted,
                ["made_weldment"] = wasWelded.HasValue ? (object)!wasWelded.Value : null,
                // N15, N41: what SOLIDWORKS answered, read and not assumed. A
                // null automatic_cut_list_set means it was not asked: the cut
                // list was automatic already.
                ["automatic_cut_list_set"] = automaticCutListSet,
                ["automatic_update_set"] = automaticUpdateSet,
                ["cut_list_updated"] = cutListUpdated,
                ["item_folders_update_false"] = folderUpdatesFalse,
                ["item_folders_update_unread"] = folderUpdatesUnread,
            };
            // LA SOMMA DELLE QUANTITA' E' IL NUMERO DELLE BARRE: se no, qualcosa
            // manca o e' contato due volte, e lo si dice.
            var warnings = new List<string>();
            if (totalQuantity.HasValue && totalQuantity.Value != solidBodies.Length)
                warnings.Add("La distinta dice " + totalQuantity.GetValueOrDefault() + " barre in tutto e il pezzo ha " +
                             solidBodies.Length + " corpi: prima di andare in officina, controlla.");
            // N15: the bodies the item folders hold, against the bodies of the
            // part (a body in no item, or in two, does not show in the sum of
            // the quantities if the QUANTITY of an item is wrong the other way).
            if (bodiesInCutList is int heldBodies && heldBodies != solidBodies.Length)
                warnings.Add("The cut list folders hold " + heldBodies + " bodies and the part has " +
                             solidBodies.Length + ": check the cut list before it goes to the shop.");
            foreach (var p in itemProblems)
                warnings.Add(p + ": check the cut list before it goes to the shop.");
            // N41: a refresh that did not go through leaves the numbers of before.
            if (cutListUpdated == false)
                warnings.Add("SOLIDWORKS did not update the cut list (UpdateCutList answered false): the " +
                             "quantities and lengths above may be those of before the last change. Rebuild " +
                             "the part (Ctrl+Q) and ask for the cut list again.");
            if (automaticUpdateSet == false)
                warnings.Add("SOLIDWORKS did not turn on the automatic update of the cut list " +
                             "(SetAutomaticUpdate answered false): after a change to the bars, ask for the cut " +
                             "list again before reading it.");
            if (warnings.Count > 0) measurements["warning"] = string.Join(" ", warnings);
            // Una distinta che c'era gia' non e' una feature creata adesso.
            if (beforeFolder != null && !alreadyExisted) return Succeeded(beforeFolder, measurements);
            return WithoutFeature(measurements);
        }

        private Outcome EndCap(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            int howMany, machinedFaces;
            string why;
            var faces = FacesFromReference(a, "ends_ref", docState, out howMany, out machinedFaces, out why);
            if (faces == null) return Failed(why);

            double thickness = Num(a, "thickness_mm", 0);
            if (thickness <= 0) return Failed("Il tappo non ha spessore.");
            double inset = Num(a, "inset_mm", 0);
            double chamfer = Num(a, "chamfer_mm", 0);

            model.ClearSelection2(true);
            int selected = 0;
            foreach (var f in faces)
            {
                var ent = f as IEntity;
                if (ent != null && ent.Select4(true, null)) selected++;
            }
            if (selected == 0)
                return Failed("Le estremita' indicate non si sono lasciate selezionare.");

            IFeature feature;
            try
            {
                feature = model.FeatureManager.InsertEndCapFeature3(
                    Depth: Units.M(thickness),
                    BIsGivenOffset: inset > 0,
                    BIsChamfer: chamfer > 0,
                    OffsetValue: Units.M(inset),
                    WallThicknessRatio: 0.5,
                    ChamferValue: Units.M(chamfer),
                    BIsCornerTreatment: false,
                    DepthOffset: 0,
                    BIsReverse: false,
                    BIsEndCapInward: (int)swEndCapThicknessDirection_e.swExtendInward) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("Il tappo non e' riuscito: " + ex.Message);
            }
            if (feature == null)
                return Failed(
                    "Il tappo non e' stato creato. Le facce indicate devono " +
                    "essere estremita' APERTE di una barra cava: su un profilo " +
                    "pieno non c'e' niente da tappare.");

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["end_capped"] = selected,
                ["thickness_mm"] = thickness,
                // N7: planar faces with an inner loop made by another feature
                // (a drilled wall counts twice, outer and inner face), looked
                // at and NOT capped. A true zero: every face was read, or the
                // step stopped (Riferimenti.OpenEndsOnly).
                ["machined_faces_skipped"] = machinedFaces,
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // Il fazzoletto (voce GP20)
        // ─────────────────────────────────────────────────────────────────
        //
        // NO GUSSET WAS EVER BORN UP TO LIBRARY 0.139.0 (the fix is in 0.140.0,
        // finding N69), AND THE CAUSE WAS THE DEGENERATE PROFILE.
        //
        // Il metodo spedito passava `BIsProfile = true` (poligono) con
        // `ProfileD3 = ProfileAngle = ProfileD4 = 0` e nessuna marca: un
        // poligono con i due lati corti a zero si chiude su se stesso, e
        // SOLIDWORKS torna null senza dire perche'. Sedici varianti alla cieca
        // (25/9/2026) avevano provato il triangolo SENZA marca e la marca 1/1
        // col poligono DEGENERE, mai le due cose giuste insieme.
        //
        // ── LA GUIDA API (sldworksapi, estratta in locale; pagine per nome) ──
        //
        //   SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~InsertGussetFeature3.html,
        //   Remarks: «Before calling this method, you must call
        //   IModelDocExtension::SelectByID2 to select two faces that are the
        //   supporting legs of this gusset with a Mark of 1.»
        //   Parametri: «BIsProfile — True to use a polygon profile, false to
        //   use a triangle profile»; «ProfileAngle — valid only if BUseLenDim
        //   is false»; «ProfileD4 — valid only if BUseLenDim is true».
        //
        //   Insert_Gusset_Feature_Example_CSharp.htm: due facce con marca 1,
        //   poi InsertGussetFeature3(0.005, swGussetThicknessBothSides,
        //   swGussetProfileLocationCenter, false, 0.025, 0.025, 0.015,
        //   0.78539816339745, 0.015, false, 0.005, 0, false, false, false,
        //   0.0125, 0.0125, 0.785398163397452, true, false). Il triangolo porta
        //   comunque D3 = D4 = 15 mm e 45 gradi: qui si passano gli stessi.
        //
        //   SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IGussetFeatureData.html
        //   e Get_Gusset_Feature_Data_Example_CSharp.htm: «swGussetFeatureData
        //   = (GussetFeatureData)swFeature.GetDefinition();», poi
        //   AccessSelections, ProfileType, ThicknessType, Thickness,
        //   GetSupportingFaces, ReleaseSelectionAccess.
        //   ...IGussetFeatureData~AccessSelections.html: «puts the model into a
        //   rollback state ... You must use ... IGussetFeatureData::
        //   ReleaseSelectionAccess if you did not [modify the feature]».
        //
        //   Gli enum, letti per riflessione sull'interop installato (le pagine
        //   di swconst non sono nell'estrazione): swGussetProfileType_e
        //   Polygon/Triangle, swGussetProfileLocationType_e Start/Center/End,
        //   swGussetThicknessType_e Inner/BothSides/Outer. Qui si usano solo
        //   per NOME.
        //
        // ── MISURATO (28/9/2026, sonda Checks\Gusset\Program.cs) ──
        //
        // Sulla T di tubolari 80x80x5 (lunga 600 in X, corta 300 in Z, la corta
        // tagliata dalla lunga), fra la faccia alta della lunga e la faccia
        // laterale della corta — le stesse su cui nasce `weld_bead`:
        //
        //   la ricetta spedita (poligono, D3 = D4 = 0, senza marca)  -> null
        //   la stessa con marca 1/1                                  -> null
        //   la guida: triangolo 60/60, spessore 5, marca 1/1         -> NASCE,
        //                                       +9000 mm3, corpi 2 -> 3
        //   poligono 60/60 con D3 = D4 = 15, marca 1/1               -> NASCE,
        //                                       +12937,5 mm3
        //   triangolo via InsertGussetFeature2 con Face2[]           -> NASCE
        //
        // In tutte le varianti nate la feature e' di tipo «Gusset»
        // (IFeature::GetTypeName2), e si controlla quello.
        //
        // ── I CONTI A MANO ──────────────────────────────────────────────────
        //
        // Triangolo: lato1 x lato2 / 2 x spessore = 60 x 60 / 2 x 5 = 9000.
        // Poligono: il pentagono (0,0) (l1,0) (l1,a) (b,l2) (0,l2), con a e b i
        // due lati corti; per la formula dell'area di Gauss
        //     (l1*l2 + l1*a + b*l2 - a*b) / 2
        // = 60*60 - 45*45/2 = 2587,5 mm2, x 5 = 12937,5: e' il numero misurato.
        // QUALE dei due lati corti sia `a` e quale `b` la guida non lo dice, e
        // con 60/60 e 15/15 non si vede: si accettano tutte e due le letture.
        // Fra due facce che non fanno 90 gradi l'area del triangolo si
        // moltiplica per il seno dell'angolo (i lati corrono lungo le facce);
        // quella del poligono si controlla esatta solo ad angolo retto, che e'
        // il caso misurato, e altrove si pretende almeno il triangolo che il
        // pentagono contiene.
        private const string GussetTypeValue = "Gusset";        // GetTypeName2, misurato dalla sonda
        private const double TriangleD3 = 0.015;              // Insert_Gusset_Feature_Example_CSharp.htm
        private const double TriangleAngle = 0.78539816339745;
        private const double TriangleD4 = 0.015;
        private const double ChamferD5 = 0.0125;                // idem; lo smusso resta spento
        private const double ChamferD6 = 0.0125;

        private const string GussetRemainsInPart =
            " Il fazzoletto pero' e' rimasto nel pezzo: ritiralo — " + Sentences.HowToWithdrawIt + ".";

        private Outcome Gusset(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
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
            var uid = StringList(a, "faces_uid");
            if (uid.Count != 2)
                return Failed(
                    "Il fazzoletto va appoggiato su DUE facce, una per barra. " +
                    "Ne hai indicate " + uid.Count + ": indica in «faces_uid» esattamente due " +
                    "identificativi presi dallo stato del documento (F1, F2...).");

            double thickness = Num(a, "thickness_mm", 0);
            double side1 = Num(a, "leg1_mm", 0);
            double side2 = Num(a, "leg2_mm", 0);
            if (thickness <= 0)
                return Failed("Il fazzoletto non ha spessore: indica «thickness_mm» maggiore di zero, per esempio 5.");
            if (side1 <= 0 || side2 <= 0)
                return Failed("Il fazzoletto non ha i due lati: indica «leg1_mm» e «leg2_mm» maggiori di zero, per esempio 60 e 60.");

            // ── IL PROFILO: triangolare, oppure poligonale NON degenere ─────
            string profile = (Str(a, "profile", "triangular") ?? "").Trim().ToLowerInvariant();
            Func<string, bool> given = k =>
            {
                object g;
                return a != null && a.TryGetValue(k, out g) && g != null;
            };
            bool polygon;
            double side3 = 0, side4 = 0;
            if (profile == "triangular")
            {
                polygon = false;
                // Un ingresso che non si usa non si inghiotte (rilievo C1-1).
                if (given("side3_mm") || given("side4_mm"))
                    return Failed(
                        "«side3_mm» e «side4_mm» valgono solo col profilo «polygonal»: col " +
                        "triangolare li butterei via senza dirtelo. Toglili, oppure chiedi " +
                        "«profile»: «polygonal».");
            }
            else if (profile == "polygonal")
            {
                polygon = true;
                side3 = Num(a, "side3_mm", 0);
                side4 = Num(a, "side4_mm", 0);
                if (side3 <= 0 || side4 <= 0)
                    return Failed(
                        "Un profilo «polygonal» senza «side3_mm» e «side4_mm», o con uno dei due a zero, " +
                        "e' degenere: il poligono si chiude su se stesso e SOLIDWORKS non crea niente, " +
                        "senza dire perche'. This is the defect that kept this operation from ever " +
                        "creating a gusset up to library 0.139.0 (entry GP20). Dai i due lati corti, " +
                        "oppure usa «profile»: «triangular».");
                double shorter = Math.Min(side1, side2);
                if (side3 >= shorter || side4 >= shorter)
                    return Failed(
                        "«side3_mm» e «side4_mm» sono i due lati CORTI del poligono, quelli che tagliano " +
                        "la punta: devono essere piu' corti di «leg1_mm» e «leg2_mm» (qui " +
                        shorter.ToString("0.###", CultureInfo.InvariantCulture) + " mm), se no il " +
                        "poligono non e' piu' un fazzoletto. Se lo vuoi pieno fino in punta, usa " +
                        "«profile»: «triangular».");
            }
            else
                return Failed(
                    "«profile» e' «triangular» o «polygonal», non «" + profile + "»: usa il " +
                    "triangolare (il predefinito), oppure il poligonale con «side3_mm» e «side4_mm».");

            // ── LA POSIZIONE LUNGO LO SPIGOLO (rilievo C1-1) ────────────────
            // Up to library 0.139.0 (N69) an unknown value went to the centre
            // in silence, and the answer repeated the string it was given.
            string position = (Str(a, "position", "center") ?? "").Trim().ToLowerInvariant();
            int location;
            switch (position)
            {
                case "start": location = (int)swGussetProfileLocationType_e.swGussetProfileLocationStart; break;
                case "center": location = (int)swGussetProfileLocationType_e.swGussetProfileLocationCenter; break;
                case "end": location = (int)swGussetProfileLocationType_e.swGussetProfileLocationEnd; break;
                default:
                    return Failed(
                        "«position» e' «start», «center» o «end» lungo lo spigolo fra le due facce, " +
                        "non «" + position + "». Non la metto al centro di nascosto: usa uno dei tre, " +
                        "oppure togli «position» e vale il centro.");
            }

            // ── LE DUE FACCE, dallo stato ───────────────────────────────────
            var faces = new IFace2[2];
            for (int i = 0; i < 2; i++)
            {
                string u = uid[i];
                DocumentState.EntitySignature signature;
                if (docState == null || !docState.Signatures.TryGetValue(u, out signature) || signature.Type != "FACE")
                    return Failed(
                        "La faccia «" + u + "» non c'e' piu' in questo documento: rileggi lo stato e " +
                        "indica di nuovo le due facce.");
                string offset;
                if (!SignatureMatches(a, u, signature, out offset)) return Failed(offset);
                faces[i] = signature.Entity as IFace2;
                if (faces[i] == null)
                    return Failed(
                        "La faccia «" + u + "» non si lascia leggere: indica un'altra faccia della " +
                        "stessa barra, presa dallo stato.");
            }

            // ── E LA GEOMETRIA CHE UN FAZZOLETTO VUOLE, controllata QUI ─────
            // Piane, che fanno angolo, che si toccano lungo uno spigolo: e'
            // l'angolo fra due barre. SOLIDWORKS su facce sbagliate torna null
            // senza un perche', e un null senza perche' e' esattamente cio' che
            // ha tenuto ferma questa voce per tre giorni.
            var normali = new double[2][];
            for (int i = 0; i < 2; i++)
            {
                normali[i] = FaceNormal(faces[i]);
                if (normali[i] == null || normali[i].Length < 3)
                    return Failed(
                        "La faccia «" + uid[i] + "» non e' piana: il fazzoletto si appoggia su due " +
                        "facce PIANE, una per barra (su un tubo tondo non c'e' un piano su cui " +
                        "posarlo). Indica una faccia piana della barra.");
            }
            double[] n1 = normali[0], n2 = normali[1];
            double cosine = n1[0] * n2[0] + n1[1] * n2[1] + n1[2] * n2[2];
            if (Math.Abs(cosine) > 0.999)
                return Failed(
                    "Le due facce sono parallele: si appoggiano una sull'altra o stanno affiancate, " +
                    "e non formano un angolo. Il fazzoletto va nell'angolo fra due facce che si " +
                    "incontrano lungo uno spigolo: per una barra in battuta su un'altra indica la " +
                    "faccia LATERALE della barra e la faccia su cui arriva, non la sua testa.");
            double cx = n1[1] * n2[2] - n1[2] * n2[1];
            double cy = n1[2] * n2[0] - n1[0] * n2[2];
            double cz = n1[0] * n2[1] - n1[1] * n2[0];
            double seno = Math.Sqrt(cx * cx + cy * cy + cz * cz);

            // Basta un verso che tocca; «non si toccano» solo se tutti e due
            // hanno risposto (voce Z-C8, come il cordone).
            double t1 = LengthThatLiesOn(faces[0], faces[1]), t2 = LengthThatLiesOn(faces[1], faces[0]);
            if (Math.Max(t1, t2) < 0.01 && (t1 < 0 || t2 < 0))
                return Failed(
                    "Non riesco a leggere gli spigoli delle due facce, e senza non so dirti se si " +
                    "toccano. Non ho messo il fazzoletto: prova con un'altra faccia della stessa barra.");
            double contact = Math.Max(t1, t2);
            if (contact < 0.01)
                return Failed(
                    "Le due facce non si toccano: nessuno spigolo dell'una sta sull'altra, e il " +
                    "fazzoletto va nell'angolo dove due barre si incontrano. Indica la faccia su cui " +
                    "la barra arriva e la faccia LATERALE della barra che ci arriva sopra.");

            // ── IL PEZZO PRIMA, letto prima di toccare niente ───────────────
            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            int featureBefore = CountFeature(model);
            int brokenBefore = FeaturesInError(model);
            if (volumeBefore == null || bodiesBefore < 0 || featureBefore < 0)
                return Failed(VolumeNotReadBefore("mettere il fazzoletto") +
                               " Prova a ricostruire il pezzo (Ctrl+Q) e a ripetere il passo.");
            var namesBefore = SolidBodyNames(model);

            // ── LE DUE FACCE CON LA MARCA 1, poi la chiamata ────────────────
            IFeature feature = null;
            string exception = null;
            try
            {
                model.ClearSelection2(true);
                var handler = model.SelectionManager as ISelectionMgr;
                if (handler == null)
                    return Failed(
                        "Il gestore della selezione non risponde: non riesco a marcare le due facce. " +
                        "Non ho toccato niente: prova a ripetere il passo.");
                for (int i = 0; i < 2; i++)
                    if (!Select(handler, faces[i], SelectionMarks.GussetSupportFaces))
                        return Failed(
                            "La faccia «" + uid[i] + "» non si e' lasciata selezionare. Non ho toccato " +
                            "niente: rileggi lo stato e indica di nuovo le due facce.");

                feature = model.FeatureManager.InsertGussetFeature3(
                    Depth: Units.M(thickness),
                    DirType: (short)swGussetThicknessType_e.swGussetThicknessBothSides,
                    LocType: (short)location,
                    BIsProfile: polygon,
                    ProfileD1: Units.M(side1),
                    ProfileD2: Units.M(side2),
                    ProfileD3: polygon ? Units.M(side3) : TriangleD3,
                    ProfileAngle: polygon ? 0.0 : TriangleAngle,
                    ProfileD4: polygon ? Units.M(side4) : TriangleD4,
                    BOffset: false,
                    DProfileOffset: 0.0,
                    CrvIndex: 0,
                    BReverseDir: false,
                    BReverseFace: false,
                    // Poligono: lati corti come lunghezze (D4), la forma misurata.
                    // Triangolo: come l'esempio della guida (ProfileAngle).
                    BUseLenDim: polygon,
                    ProfileD5: ChamferD5,
                    ProfileD6: ChamferD6,
                    ProfileChamferAngle: 0.0,
                    BUseLenDimForChamfer: false,
                    BNeedChamferInGusset: false) as IFeature;
            }
            catch (Exception ex)
            {
                exception = ex.Message;
            }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
            }

            // WHAT TO TRY NEXT, WITHOUT BLAMING THE GEOMETRY (finding N19).
            // Until library 0.144.0 this said «usually the sides do not fit on
            // the faces». Nobody measured that: the two causes of a null gusset
            // that WERE measured (GP20, M27: the polygon with zero short sides,
            // and faces selected without mark 1) are parameters, and both are
            // excluded before the call now. Shorter sides stay a suggestion, said
            // as one. The selection is cleared in the finally above on every
            // exit of the call, the refusals inside it included.
            string alternativa =
                " Shorter sides may fit where these did not" +
                (polygon ? ", and so may the «triangular» profile" : "") + ": that is a " +
                "suggestion, not a measured cause. If you only need to document the weld between these " +
                "two faces, there is «weld_bead».";
            if (feature == null)
            {
                int now = CountFeature(model);
                string queue = now > featureBefore
                    ? " Pero' l'albero e' cresciuto lo stesso: guarda il pezzo — " + Sentences.HowToWithdrawIt + "."
                    : "";
                return Failed(
                    (exception != null
                        ? "Il fazzoletto non e' riuscito: " + exception + "."
                        : "SOLIDWORKS did not create the gusset and gives no reason. The two causes measured " +
                          "so far are ruled out before the call: the profile is not a degenerate polygon, and " +
                          "the two faces were selected with mark 1 (entry GP20). The faces are planar, meet " +
                          "at an angle and touch for " +
                          contact.ToString("0.##", CultureInfo.InvariantCulture) + " mm, so the cause is " +
                          "not one I know.") +
                    alternativa + queue);
            }

            try { model.EditRebuild3(); } catch { }

            // ── E ADESSO SI MISURA COSA E' NATO ─────────────────────────────
            // 1. nell'albero: una feature tornata che nell'albero non c'e' e' la
            //    feature fantasma di `trim_members` (array non tipizzati).
            int featureAfter = CountFeature(model);
            if (featureAfter >= 0 && featureAfter <= featureBefore)
                return Failed(
                    "SOLIDWORKS ha risposto con una feature «" + GussetName(feature) + "» che nell'albero " +
                    "non c'e': non ti dico che il fazzoletto e' messo." + alternativa);

            // 2. e' un fazzoletto: il TIPO misurato dalla sonda («Gusset», come
            //    «Fillet», «APattern» e «Rib» nelle altre tre di questa voce), e
            //    la definizione che si rilegge come IGussetFeatureData.
            string createdType = GussetType(feature);
            if (!string.Equals(createdType, GussetTypeValue, StringComparison.Ordinal))
                return Failed(
                    "E' nata una lavorazione «" + GussetName(feature) + "» di tipo «" + createdType +
                    "», non un fazzoletto («" + GussetTypeValue + "», misurato): non ti confermo una cosa " +
                    "diversa da quella che hai chiesto." +
                    RemoveGusset(model, feature, volumeBefore, featureBefore) + alternativa);
            IGussetFeatureData data = null;
            try { data = feature.GetDefinition() as IGussetFeatureData; } catch { }
            if (data == null)
                return Failed(
                    "La lavorazione nata («" + GussetName(feature) + "», tipo «" + GussetType(feature) + "») non " +
                    "si rilegge come fazzoletto, e non posso controllare cosa ho messo." +
                    RemoveGusset(model, feature, volumeBefore, featureBefore));

            // 3. niente di rotto nell'albero.
            int brokenAfter = FeaturesInError(model);
            if (brokenBefore >= 0 && brokenAfter > brokenBefore)
                return Failed(
                    "Col fazzoletto " + (brokenAfter - brokenBefore) +
                    (brokenAfter - brokenBefore > 1 ? " lavorazioni sono andate" : " lavorazione e' andata") +
                    " in errore (" + brokenAfter + " invece di " + brokenBefore + ")." +
                    RemoveGusset(model, feature, volumeBefore, featureBefore));

            // 4. un corpo in piu': il fazzoletto e' un corpo suo (misurato 2 -> 3).
            int bodiesAfter = CountSolidBodies(model);
            if (bodiesAfter >= 0 && bodiesAfter != bodiesBefore + 1)
                return Failed(
                    "Il fazzoletto e' un corpo a se' (misurato: da 2 corpi a 3), e qui il pezzo e' " +
                    "passato da " + bodiesBefore + " a " + bodiesAfter + " corpi." +
                    RemoveGusset(model, feature, volumeBefore, featureBefore));

            // 5. il materiale aggiunto, contro il conto a mano.
            double triangleArea = side1 * side2 / 2.0 * seno;
            bool rightAngle = seno > 0.9995;
            var expectedValues = new List<double>();
            if (!polygon) expectedValues.Add(triangleArea * thickness);
            else if (rightAngle)
            {
                expectedValues.Add((side1 * side2 + side1 * side3 + side4 * side2 - side3 * side4) / 2.0 * thickness);
                expectedValues.Add((side1 * side2 + side1 * side4 + side3 * side2 - side3 * side4) / 2.0 * thickness);
            }
            string volumeCheck = expectedValues.Count > 0 ? "exact" : "at_least_triangle";
            double? volumeAfter = VolumeMm3(model);
            double? added = volumeAfter.HasValue ? volumeAfter.Value - volumeBefore.Value : (double?)null;
            double? expected = null;
            if (added.HasValue)
            {
                if (expectedValues.Count > 0)
                {
                    double best = expectedValues[0];
                    foreach (var v in expectedValues)
                        if (Math.Abs(added.Value - v) < Math.Abs(added.Value - best)) best = v;
                    expected = best;
                    if (Math.Abs(added.Value - best) > Math.Max(0.5, best * 1e-4))
                        return Failed(
                            "Il fazzoletto e' nato ma ha aggiunto " + GussetDigits(added.Value) + " mm3, e il conto " +
                            "a mano ne vuole " + GussetDigits(best) + " (" +
                            (polygon ? "il poligono dei quattro lati" : "lato1 x lato2 / 2") +
                            " x spessore): la piastra non e' quella chiesta, forse i lati non ci stanno " +
                            "sulle facce." + RemoveGusset(model, feature, volumeBefore, featureBefore));
                }
                else
                {
                    expected = triangleArea * thickness;
                    if (added.Value < expected.Value - Math.Max(0.5, expected.Value * 1e-4))
                        return Failed(
                            "Il fazzoletto e' nato ma ha aggiunto " + GussetDigits(added.Value) + " mm3, meno del " +
                            "triangolo che il poligono contiene (" + GussetDigits(expected.Value) + " mm3)." +
                            RemoveGusset(model, feature, volumeBefore, featureBefore));
                }
            }

            // 6. la definizione RILETTA, non gli ingressi ripetuti (rilievo C1-1).
            double? readThickness = null;
            string readProfile = null, readArrangement = null, readPosition = null;
            try { readThickness = data.Thickness * 1000.0; } catch { }
            try { readProfile = ProfileName(data.ProfileType); } catch { }
            try { readArrangement = ThicknessName(data.ThicknessType); } catch { }
            try { readPosition = PositionName(data.ProfileLocation); } catch { }
            string wrong = null;
            if (readThickness.HasValue && Math.Abs(readThickness.Value - thickness) > 0.001)
                wrong = "lo spessore riletto e' " + readThickness.Value.ToString("0.###", CultureInfo.InvariantCulture) +
                            " mm invece di " + thickness.ToString("0.###", CultureInfo.InvariantCulture);
            else if (readProfile != null && readProfile != profile)
                wrong = "il profilo riletto e' «" + readProfile + "» invece di «" + profile + "»";
            else if (readArrangement != null && readArrangement != "both_sides")
                wrong = "lo spessore riletto sta «" + readArrangement + "» invece che da tutte e due le parti";
            else if (readPosition != null && readPosition != position)
                wrong = "la posizione riletta e' «" + readPosition + "» invece di «" + position + "»";
            if (wrong != null)
                return Failed(
                    "Il fazzoletto e' nato, ma " + wrong + ": non te lo lascio passare per buono." +
                    RemoveGusset(model, feature, volumeBefore, featureBefore));

            // 7. le facce su cui SOLIDWORKS dice di averlo appoggiato.
            double[] readAreas = RestingFaceAreas(model, data);
            double[] requestedAreas = AreasInOrder(faces[0], faces[1]);
            if (readAreas != null && requestedAreas != null)
            {
                for (int i = 0; i < 2; i++)
                    if (Math.Abs(readAreas[i] - requestedAreas[i]) > Math.Max(0.5, requestedAreas[i] * 1e-4))
                        return Failed(
                            "Il fazzoletto e' nato, ma SOLIDWORKS dice di averlo appoggiato su facce da " +
                            GussetDigits(readAreas[0]) + " e " + GussetDigits(readAreas[1]) + " mm2, e tu hai indicato facce da " +
                            GussetDigits(requestedAreas[0]) + " e " + GussetDigits(requestedAreas[1]) + " mm2." +
                            RemoveGusset(model, feature, volumeBefore, featureBefore));
            }

            // 8. il corpo nuovo, e dove sta: il volume da solo e' cieco sulla posizione.
            string newBody = null;
            double[] box = null;
            var newItems = NewSolidBodies(model, namesBefore);
            if (newItems.Count == 1)
            {
                try { newBody = newItems[0].Name; } catch { }
                var s = BodyBox(newItems[0]);
                if (s != null) box = s.Select(x => Math.Round(x, 2)).ToArray();
            }

            var measurements = new Dictionary<string, object>
            {
                ["volume_added_mm3"] = NumberOrNull(added, 2),
                ["volume_expected_mm3"] = NumberOrNull(expected, 2),
                ["volume_check"] = volumeCheck,
                ["bodies_before"] = bodiesBefore,
                ["bodies_after"] = NumberOrNull(bodiesAfter),
                ["new_body"] = newBody,
                ["new_body_box_mm"] = box,
                ["thickness_read_mm"] = NumberOrNull(readThickness, 3),
                ["thickness_type_read"] = readArrangement,
                ["profile_read"] = readProfile,
                ["location_read"] = readPosition,
                ["supporting_faces_area_mm2"] = readAreas == null
                    ? null : new object[] { Math.Round(readAreas[0], 2), Math.Round(readAreas[1], 2) },
                ["contact_mm"] = Math.Round(contact, 2),
            };
            if (!added.HasValue) measurements["volume_not_reread"] = NoteVolumeNotReread;
            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] fazzoletto {0} {1:0.###}x{2:0.###} sp {3:0.###}: volume {4}, corpi {5} -> {6}",
                profile, side1, side2, thickness, added.HasValue ? GussetDigits(added.Value) : "?", bodiesBefore, bodiesAfter));
            return Succeeded(feature, measurements);
        }

        /// <summary>
        /// Toglie un fazzoletto appena nato che non ha passato i controlli, e
        /// torna la coda del messaggio. «Ha detto di averlo tolto» non basta,
        /// come in `DeleteFeature`: si ricontano le lavorazioni e si
        /// rimisura il volume. DeleteSelection2 e non EditDelete (apre la
        /// finestra di conferma, e una chiamata COM con un modale non torna).
        /// </summary>
        private static string RemoveGusset(
            IModelDoc2 model, IFeature gusset, double? volumeBefore, int featureBefore)
        {
            bool removed = false;
            try
            {
                model.ClearSelection2(true);
                if (gusset.Select2(false, 0))
                    removed = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch { }
            finally { try { model.ClearSelection2(true); } catch { } }
            if (!removed) return GussetRemainsInPart;
            try { model.EditRebuild3(); } catch { }
            int features = CountFeature(model);
            if (features < 0)
                return " L'ho tolto, ma non ho potuto ricontare l'albero per controllare che il pezzo " +
                       "sia com'era: guardalo.";
            // Removed by DeleteSelection2: from here the gusset is gone, and
            // «still in the part» would be false (Z-C21.9, finding C9-6).
            if (features > featureBefore) return RemovedButTreeLonger(features - featureBefore);
            double? volumeAfter = VolumeMm3(model);
            if (volumeBefore == null || volumeAfter == null)
                return " L'ho tolto, ma non ho potuto rimisurare il volume per controllare che il pezzo " +
                       "sia com'era: guardalo.";
            if (Math.Abs(volumeAfter.GetValueOrDefault() - volumeBefore.GetValueOrDefault()) > 0.01)
                return RemovedButVolumeChanged(volumeAfter.GetValueOrDefault(), volumeBefore.GetValueOrDefault());
            return " L'ho tolto: il pezzo e' com'era prima di questo passo.";
        }

        /// <summary>
        /// Le aree, in mm2 dalla piu' grande, delle due facce su cui la feature
        /// dice di appoggiarsi: `AccessSelections` → `GetSupportingFaces` →
        /// `ReleaseSelectionAccess`, come Get_Gusset_Feature_Data_Example_CSharp.htm.
        /// Null se non si leggono: «non lo so» non accusa. Il rilascio sta nel
        /// `finally`, perche' AccessSelections lascia il modello in rollback.
        /// </summary>
        private static double[] RestingFaceAreas(IModelDoc2 model, IGussetFeatureData data)
        {
            bool access = false;
            try
            {
                access = data.AccessSelections((ModelDoc2)model, null);
                if (!access) return null;
                Face2 before, second;
                if (!data.GetSupportingFaces(out before, out second)) return null;
                return AreasInOrder(before as IFace2, second as IFace2);
            }
            catch { return null; }
            finally
            {
                if (access) { try { data.ReleaseSelectionAccess(); } catch { } }
            }
        }

        /// <summary>Le aree di due facce in mm2, dalla piu' grande; null se una non si legge.</summary>
        private static double[] AreasInOrder(IFace2 before, IFace2 second)
        {
            try
            {
                if (before == null || second == null) return null;
                double x = before.GetArea() * 1e6, y = second.GetArea() * 1e6;
                return x >= y ? new[] { x, y } : new[] { y, x };
            }
            catch { return null; }
        }

        /// <summary>I nomi dei corpi solidi del pezzo; vuoto se non si leggono.</summary>
        private static HashSet<string> SolidBodyNames(IModelDoc2 model)
        {
            var outside = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var bodies = (model as IPartDoc)?.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                foreach (var c in bodies ?? new object[0])
                {
                    string n = null;
                    try { n = (c as IBody2)?.Name; } catch { }
                    if (n != null) outside.Add(n);
                }
            }
            catch { }
            return outside;
        }

        /// <summary>I corpi solidi il cui nome non c'era prima.</summary>
        private static List<IBody2> NewSolidBodies(IModelDoc2 model, HashSet<string> namesBefore)
        {
            var outside = new List<IBody2>();
            try
            {
                var bodies = (model as IPartDoc)?.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                foreach (var c in bodies ?? new object[0])
                {
                    var b = c as IBody2;
                    string n = null;
                    try { n = b?.Name; } catch { }
                    if (b != null && n != null && !namesBefore.Contains(n)) outside.Add(b);
                }
            }
            catch { }
            return outside;
        }

        // I valori degli enum del fazzoletto nelle parole della risposta. Per
        // NOME dell'enum, mai per cifra; null se il valore non e' fra quelli noti.
        private static string ProfileName(int v)
        {
            if (v == (int)swGussetProfileType_e.swGussetProfileTriangle) return "triangular";
            if (v == (int)swGussetProfileType_e.swGussetProfilePolygon) return "polygonal";
            return null;
        }

        private static string ThicknessName(int v)
        {
            if (v == (int)swGussetThicknessType_e.swGussetThicknessBothSides) return "both_sides";
            if (v == (int)swGussetThicknessType_e.swGussetThicknessInner) return "inner";
            if (v == (int)swGussetThicknessType_e.swGussetThicknessOuter) return "outer";
            return null;
        }

        private static string PositionName(int v)
        {
            if (v == (int)swGussetProfileLocationType_e.swGussetProfileLocationStart) return "start";
            if (v == (int)swGussetProfileLocationType_e.swGussetProfileLocationCenter) return "center";
            if (v == (int)swGussetProfileLocationType_e.swGussetProfileLocationEnd) return "end";
            return null;
        }

        private static string GussetName(IFeature f)
        {
            try { return f.Name; } catch { return "?"; }
        }

        private static string GussetType(IFeature f)
        {
            try { return f.GetTypeName2(); } catch { return "?"; }
        }

        private static string GussetDigits(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
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
        //   «geometria» o «path» sugli spigoli        -> null, sempre
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
        //
        // WHAT THE NULL OF 25/9 WAS (the InsertCosmeticWeldBead2 probe of Z-C21.3, 28/9/2026):
        // the marks. None of those calls selected the faces with the marks 4
        // and 8 of the InsertCosmeticWeldBead2 page; with them (and the
        // selection kept) the bead is born, and since Z-C21.7 (L9-3) this
        // operation uses InsertCosmeticWeldBead2 that way. The rest above
        // still holds: one pair of faces, the contact checked here.
        private Outcome WeldBead(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double side = Num(a, "leg_mm", 0);
            if (side <= 0) return Failed("Il cordone non ha lato.");

            var faces = new IFace2[2];
            var names = new[] { "start_face_uid", "end_face_uid" };
            for (int i = 0; i < 2; i++)
            {
                string u = Str(a, names[i], "");
                if (u.Length == 0)
                    return Failed(
                        "Manca «" + names[i] + "». Il cordone va fra DUE facce, una per " +
                        "corpo, prese dallo stato del documento (F1, F2...).");
                DocumentState.EntitySignature signature;
                if (docState == null || !docState.Signatures.TryGetValue(u, out signature) || signature.Type != "FACE")
                    return Failed("La faccia «" + u + "» non c'e' piu' in questo documento.");
                string offset;
                if (!SignatureMatches(a, u, signature, out offset)) return Failed(offset);
                faces[i] = signature.Entity as IFace2;
                if (faces[i] == null)
                    return Failed("La faccia «" + u + "» non si lascia leggere.");
            }

            string fromBody = BodyName(faces[0]), bodyA = BodyName(faces[1]);
            if (fromBody != null && fromBody == bodyA)
                return Failed(
                    "Le due facce sono dello stesso corpo («" + fromBody + "»). Un " +
                    "cordone unisce DUE corpi: indica una faccia per parte.");

            // S17: a curved face gives null (it used to give 0,0,0, a zero
            // cosine): either way it is not "parallel", as before.
            var nFrom = ApiHelpers.NormalIfPlanar(faces[0]);
            var nA = ApiHelpers.NormalIfPlanar(faces[1]);
            if (nFrom != null && nA != null && nFrom.Length >= 3 && nA.Length >= 3
                && Math.Abs(nFrom[0] * nA[0] + nFrom[1] * nA[1] + nFrom[2] * nA[2]) > 0.999)
                return Failed(
                    "Le due facce sono parallele: si appoggiano una sull'altra o stanno " +
                    "affiancate, e non formano un angolo. Il cordone d'angolo va fra due " +
                    "facce che si incontrano lungo uno spigolo e fanno un angolo — per " +
                    "una barra in battuta su un'altra, e' la faccia LATERALE della barra " +
                    "contro la faccia su cui arriva, non la sua testa.");

            // Basta un verso che tocca; ma «non si toccano» si puo' dire solo
            // se tutti e due i versi hanno risposto.
            double l1 = LengthThatLiesOn(faces[0], faces[1]), l2 = LengthThatLiesOn(faces[1], faces[0]);
            if (Math.Max(l1, l2) < 0.01 && (l1 < 0 || l2 < 0))
                return Failed(
                    "Non riesco a leggere gli spigoli delle due facce, e senza non so dirti " +
                    "se si toccano. Non ho messo il cordone.");
            double contact = Math.Max(l1, l2);
            if (contact < 0.01)
                return Failed(
                    "Le due facce non si toccano: nessuno spigolo dell'una sta " +
                    "sull'altra. SOLIDWORKS il cordone lo creerebbe lo stesso (misurato: " +
                    "fra due facce lontane nasce, con un lato diverso da quello chiesto), " +
                    "e in distinta finirebbe una saldatura che non esiste.");

            // Z-C21.7 (L9-3): InsertCosmeticWeldBead2, not the obsolete
            // InsertCosmeticWeldBead (IFeatureManager~InsertCosmeticWeldBead.html:
            // «Obsolete. Superseded by IFeatureManager::InsertCosmeticWeldBead2»).
            // IFeatureManager~InsertCosmeticWeldBead2.html, Remarks, weld
            // geometry mode: «4 for each weld-from entity in
            // WeldFromFaceOrEdgeSet, 8 for each weld-to entity in
            // WeldToFaceOrEdgeSet»; «Return Value: Array of weld bead features».
            // Exactly case W1 of the InsertCosmeticWeldBead2 probe of Z-C21.3 (decision A),
            // on the trimmed T of the bench case: the two faces selected with
            // the marks 4 and 8 on a SelectData, the same faces passed as typed
            // Face2[] arrays, the selection KEPT through the call: one bead,
            // BeadSize 4 mm, 68 mm long, the same bead as the obsolete call
            // (W0). The marks are required: without them (W4) or with the
            // selection cleared before the call (W3) there is no bead, which is
            // what the null of 25/9/2026 was. The return is an Object[1] and
            // `as IFeature` on it gives null, so the bead is taken from the
            // tree, as the probe counted it (sub-features included: it lives in
            // the weld folder).
            model.ClearSelection2(true);
            var selectionManager = model.SelectionManager as ISelectionMgr;
            var marks = new[] { SelectionMarks.WeldBeadFrom, SelectionMarks.WeldBeadTo };
            for (int i = 0; i < 2; i++)
            {
                var selectData = selectionManager?.CreateSelectData() as SelectData;
                if (selectData == null)
                    return Failed("I could not prepare the selection of the two faces (CreateSelectData).");
                selectData.Mark = marks[i];
                if (!(faces[i] as IEntity).Select4(true, selectData))
                    return Failed("La faccia «" + Str(a, names[i], "") + "» non si e' lasciata selezionare.");
            }

            var beadsBefore = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bead in WeldBeadsInTree(model)) beadsBefore.Add(WeldBeadName(bead));
            IFeature feature;
            try
            {
                model.FeatureManager.InsertCosmeticWeldBead2(
                    (int)swCosmeticWeldBeadMode_e.swCosmeticWeldBeadMode_WeldGeometry,
                    new Face2[] { (Face2)faces[0] }, new Face2[] { (Face2)faces[1] }, Units.M(side));
                var born = new List<IFeature>();
                var bornNames = new List<string>();
                foreach (var bead in WeldBeadsInTree(model))
                {
                    string name = WeldBeadName(bead);
                    if (!beadsBefore.Contains(name)) { born.Add(bead); bornNames.Add(name); }
                }
                if (born.Count > 1)
                    return Failed("InsertCosmeticWeldBead2 made " + born.Count + " weld beads instead of one (" +
                                   string.Join(", ", bornNames) + "): take them back with undo, and tell me.");
                feature = born.Count == 1 ? born[0] : null;
            }
            catch (Exception ex)
            {
                return Failed("Il cordone non e' riuscito: " + ex.Message);
            }
            finally { try { model.ClearSelection2(true); } catch { } }
            if (feature == null)
                return Failed(
                    "Il cordone non e' stato creato, e SOLIDWORKS non dice perche'. " +
                    "Le due facce si toccano per " +
                    contact.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    " mm: il lato potrebbe essere troppo grande per le facce.");

            // RILETTO, NON CREDUTO: e' il lato che SOLIDWORKS ha scritto nella
            // feature, ed e' la misura che fra facce lontane veniva diversa.
            double readSide = -1;
            try
            {
                var def = feature.GetDefinition() as ICosmeticWeldBeadFeatureData;
                if (def != null) readSide = def.BeadSize * 1000.0;
            }
            catch { }
            if (readSide >= 0 && Math.Abs(readSide - side) > 0.001)
                return Failed(
                    "Il cordone e' stato creato con un lato di " +
                    readSide.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " mm invece di " + side.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    ". Non te lo lascio passare per buono — " + Sentences.HowToWithdrawIt + ".");

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["leg_mm"] = side,
                ["leg_reread_mm"] = readSide >= 0 ? (object)Math.Round(readSide, 3) : null,
                // La lunghezza e' quella dello spigolo in comune, contata qui
                // sulla geometria: non dipende dalle unita' del documento.
                ["length_mm"] = Math.Round(contact, 2),
                ["body_source"] = fromBody,
                ["body_target"] = bodyA,
            });
        }

        /// <summary>
        /// The cosmetic weld beads of the tree (type «CosmeticWeldBead», IFeature~GetTypeName2.html),
        /// sub-features included: a bead does not sit at the top level but in
        /// the weld folder (the InsertCosmeticWeldBead2 probe of Z-C21.3 found it under a
        /// CosmeticWeldCutList and a CosmeticWeldSubFolder). The same walk as
        /// the probe's, five levels deep at most, and like the probe's count
        /// each name once: a bead reached twice is still one bead.
        /// </summary>
        private static List<IFeature> WeldBeadsInTree(IModelDoc2 model)
        {
            var found = new List<IFeature>();
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    found.AddRange(CollectWeldBeads(f, 0));
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            var beads = new List<IFeature>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bead in found)
                if (seen.Add(WeldBeadName(bead))) beads.Add(bead);
            return beads;
        }

        private static List<IFeature> CollectWeldBeads(IFeature feature, int depth)
        {
            var found = new List<IFeature>();
            string type = "";
            try { type = feature.GetTypeName2() ?? ""; } catch { }
            if (type == "CosmeticWeldBead") found.Add(feature);
            if (depth >= 5) return found;
            IFeature sub = null;
            try { sub = feature.GetFirstSubFeature() as IFeature; } catch { }
            while (sub != null)
            {
                found.AddRange(CollectWeldBeads(sub, depth + 1));
                try { sub = sub.GetNextSubFeature() as IFeature; } catch { sub = null; }
            }
            return found;
        }

        private static string WeldBeadName(IFeature bead)
        {
            try { return bead.Name ?? ""; } catch { return ""; }
        }

        /// <summary>I nomi delle lavorazioni dell'albero, nell'ordine.</summary>
        private static List<string> FeatureNames(IModelDoc2 model)
        {
            var outside = new List<string>();
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    try { outside.Add(f.Name); } catch { }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// Removes the weldment feature that cut_list added before a
        /// refusal, and returns the tail of the refusal message: what was done,
        /// read back (finding N30).
        ///
        /// Until library 0.144.0 the cleanup deleted every top-level feature
        /// whose name was not there before, and dropped the answers of Select2
        /// and DeleteSelection2. A name is not an identity: inserting the
        /// weldment feature renames folders of the tree, and a renamed folder
        /// has a «new» name. Now only the IFeature that InsertWeldmentFeature
        /// returned is removed (or, if it returned none, the one new feature
        /// of type «WeldmentFeature», the type IFeature~GetTypeName2.html lists
        /// for it), both answers are read, and IsWeldment is read again after
        /// the rebuild: the message says the part is as it was only when
        /// IsWeldment reads false. DeleteSelection2, never EditDelete (a modal
        /// dialog would hang the COM call). Guide:
        /// IModelDocExtension~DeleteSelection2.html, «True if the selected item
        /// is deleted, false if not».
        /// </summary>
        private static string RemoveAddedWeldment(
            IModelDoc2 model, IPartDoc partDoc, IFeature added, HashSet<string> namesBefore, int bodiesBefore)
        {
            IFeature target = added ?? NewWeldmentFeature(model, namesBefore);
            if (target == null)
            {
                bool? stillWeldment = null;
                try { stillWeldment = partDoc.IsWeldment(); } catch { }
                if (stillWeldment == false) return "";   // nothing was added: not a weldment, as before
                return " I cannot find the weldment feature I added, so the part stayed a weldment (IsWeldment " +
                       (stillWeldment == true ? "reads true" : "cannot be read") + "): remove that feature from " +
                       "the tree by hand.";
            }
            string name = null;
            try { name = target.Name; } catch { }
            bool selected = false, deleted = false;
            try
            {
                model.ClearSelection2(true);
                selected = target.Select2(false, 0);
                if (selected)
                    deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch { }
            finally { try { model.ClearSelection2(true); } catch { } }
            try { model.ForceRebuild3(false); } catch { }
            bool? weldmentAfter = null;
            try { weldmentAfter = partDoc.IsWeldment(); } catch { }
            return WeldmentRemovalNote(name, selected, deleted, weldmentAfter, bodiesBefore, CountSolidBodies(model));
        }

        /// <summary>The top-level feature of type «WeldmentFeature» that was not there before, or null (N30).</summary>
        private static IFeature NewWeldmentFeature(IModelDoc2 model, HashSet<string> namesBefore)
        {
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    string type = null, name = null;
                    try { type = f.GetTypeName2(); name = f.Name; } catch { }
                    if (type == "WeldmentFeature" && name != null && !namesBefore.Contains(name)) return f;
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// The tail of the refusal of cut_list after the weldment feature
        /// it added was removed, or not (N30). «The part is as it was» only when
        /// the deletion answered true AND IsWeldment reads false again; a count
        /// of bodies that changed is said too. -1 bodies is «not read». No
        /// interop type in the signature: Oce.Solid.Agent.ContractTests calls it by reflection.
        /// </summary>
        internal static string WeldmentRemovalNote(
            string name, bool selected, bool deleted, bool? weldmentAfter, int bodiesBefore, int bodiesAfter)
        {
            string feature = "the weldment feature «" + (name ?? "?") + "» I had added";
            if (!selected)
                return " I could not select " + feature + " (Select2 answered false): the part stayed a " +
                       "weldment. Remove that feature from the tree by hand.";
            if (!deleted)
                return " SOLIDWORKS did not delete " + feature + " (DeleteSelection2 answered false): the part " +
                       "stayed a weldment. Remove that feature from the tree by hand.";
            if (weldmentAfter == true)
                return " I deleted " + feature + ", but IsWeldment still reads true: the part stayed a " +
                       "weldment. Look at the tree.";
            if (weldmentAfter == null)
                return " I deleted " + feature + ", but I could not read IsWeldment again: look at the tree " +
                       "before going on.";
            if (bodiesBefore >= 0 && bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                return " I deleted " + feature + " and IsWeldment reads false again, but the part has " +
                       bodiesAfter + " bodies where it had " + bodiesBefore + ": look at the tree.";
            return " I removed " + feature + ": IsWeldment reads false again" +
                   (bodiesAfter >= 0 ? ", and the part has its " + bodiesAfter + " bodies." : ".");
        }

        /// <summary>
        /// The refusal when the automatic cut list does not turn on (N15): the
        /// answer of SetAutomaticCutList (null when it was not asked) and
        /// whether a cut list was there before this step decide the words.
        /// IBodyFolder~SetAutomaticCutList.html, Remarks: false «if the part
        /// does not contain a weldment feature» or if «a cut list folder already
        /// exists in the part. You must delete the cut list before you can
        /// enable automatic generation». The second is the cut list of a
        /// library older than 0.124.0, made with InsertWeldmentCutList2 and
        /// all the bodies in one item (GT2), or one made by hand.
        /// </summary>
        internal static string AutomaticCutListRefusal(bool? setAnswer, bool cutListExisted)
        {
            if (setAnswer == false && cutListExisted)
                return "This part already has a cut list that is not automatic (made by hand, or by this " +
                       "operation before library 0.124.0), and SetAutomaticCutList answered false, as the " +
                       "guide says it does while a cut list folder exists. Delete that cut list and ask again: " +
                       "I do not read its items, which may put bars of different lengths in one item.";
            if (setAnswer == false)
                return "SOLIDWORKS did not turn on the automatic cut list (SetAutomaticCutList answered false; " +
                       "the guide says it does when the part has no weldment feature or already has a cut list " +
                       "folder). Without it the bars are not split by length, and one item with every bar " +
                       "would send the wrong lengths to the shop.";
            return "The automatic cut list is not on (GetAutomaticCutList reads false" +
                   (setAnswer == true ? " although SetAutomaticCutList answered true" : "") +
                   "): without it the bars are not split by length, and one item with every bar would send " +
                   "the wrong lengths to the shop.";
        }

        /// <summary>
        /// The volumes in mm3 of the bodies of one cut list folder (N15), or null
        /// when the folder gives no array or a body does not answer.
        /// IBody2.GetMassProperties gives the volume in m3 at index 3.
        /// </summary>
        private static double[] VolumesOfFolderBodies(IBodyFolder folder)
        {
            try
            {
                var bodies = folder.GetBodies() as object[];
                if (bodies == null) return null;
                var volumes = new double[bodies.Length];
                for (int i = 0; i < bodies.Length; i++)
                {
                    var mp = (bodies[i] as IBody2)?.GetMassProperties(1000.0) as double[];
                    if (mp == null || mp.Length < 4) return null;
                    volumes[i] = mp[3] * 1e9;
                }
                return volumes;
            }
            catch { return null; }
        }

        /// <summary>
        /// Whether the bodies of one cut list item have the same volume, within
        /// half a mm3 or a ten-thousandth (N15); null when the volumes are not
        /// known or there are none.
        /// </summary>
        internal static bool? BodiesAreEqual(double[] volumesMm3)
        {
            if (volumesMm3 == null || volumesMm3.Length == 0) return null;
            double min = volumesMm3.Min(), max = volumesMm3.Max();
            return max - min <= Math.Max(0.5, Math.Abs(max) * 1e-4);
        }

        /// <summary>
        /// What is wrong with one item of the cut list, body by body (N15), or
        /// null. Two checks the sum of the quantities cannot make: the QUANTITY
        /// of the item against the bodies its folder holds, and bodies of
        /// different volume in one item — the defect of GT2, four bars of 1000
        /// and 800 in one item «QUANTITY 4», where the quantity was right and
        /// the lengths were not. -1 bodies, a null quantity or null volumes are
        /// «not read», and do not accuse.
        /// </summary>
        internal static string CutListItemProblem(string description, int bodies, int? quantity, double[] volumesMm3)
        {
            string item = string.IsNullOrWhiteSpace(description) ? "An item of the cut list" : "The cut list item «" + description + "»";
            if (quantity is int said && bodies >= 0 && said != bodies)
                return item + " says QUANTITY " + said + " and its folder holds " + bodies +
                       (bodies == 1 ? " body" : " bodies");
            if (BodiesAreEqual(volumesMm3) == false)
                return item + " holds bodies of different volume, from " +
                       volumesMm3.Min().ToString("0.##", CultureInfo.InvariantCulture) + " to " +
                       volumesMm3.Max().ToString("0.##", CultureInfo.InvariantCulture) +
                       " mm3: it puts in one item bars that are not equal";
            return null;
        }

        private static string BodyName(IFace2 face)
        {
            try { return (face.GetBody() as IBody2)?.Name; }
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
        private static double LengthThatLiesOn(IFace2 face, IFace2 other)
        {
            double total = 0;
            int mute = 0;
            object[] edges;
            try { edges = face.GetEdges() as object[]; }
            catch { return -1; }
            if (edges == null) return 0;
            foreach (var o in edges)
            {
                try
                {
                    var edge = o as IEdge;
                    var parameters = ApiHelpers.EdgeParameters(edge, out ICurve curva);
                    if (curva == null || parameters == null) continue;
                    double u0 = parameters.UMinValue, u1 = parameters.UMaxValue;
                    bool sta = true;
                    foreach (double t in new[] { u0, (u0 + u1) / 2, u1 })
                    {
                        var p = curva.Evaluate2(t, 0) as double[];
                        var q = other.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                        if (q == null) { sta = false; break; }
                        double dx = p[0] - q[0], dy = p[1] - q[1], dz = p[2] - q[2];
                        if (Math.Sqrt(dx * dx + dy * dy + dz * dz) > 1e-6) { sta = false; break; }
                    }
                    if (sta) total += curva.GetLength3(u0, u1) * 1000.0;
                }
                catch { mute++; }
            }
            return total <= 0 && mute > 0 ? -1 : total;
        }

        // ─────────────────────────────────────────────────────────────────
        // Sostegno
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// I trattamenti d'angolo, come li chiama il piano e come li vuole
        /// SolidWorks. I numeri sono swSolidworksWeldmentEndCondOptions_e,
        /// letti per riflessione il 9 settembre 2026.
        /// </summary>
        private static readonly Dictionary<string, int> CornerTreatments =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "none", (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionNone },
                { "miter", (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionMiter },
                { "butt1", (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionButt1 },
                { "butt2", (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionButt2 },
                { "cut", (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionTrim },
            };

        /// <summary>
        /// The end condition of trim_members, as swSolidworksWeldmentEndCondOptions_e
        /// (IFeatureManager~InsertWeldmentTrimFeature2.html, «End condition as
        /// defined by swSolidworksWeldmentEndCondOptions_e»), for the four words
        /// the catalogue offers, whatever their case; -1 for anything else,
        /// «none» included (finding N22: see TrimMembers). No interop type in
        /// the signature: Oce.Solid.Agent.ContractTests calls it by reflection.
        /// </summary>
        internal static int TrimEndCondition(string asked)
        {
            switch ((asked ?? "").Trim().ToLowerInvariant())
            {
                case "cut": return (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionTrim;
                case "miter": return (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionMiter;
                case "butt1": return (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionButt1;
                case "butt2": return (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionButt2;
                default: return -1;
            }
        }

        private class Segment
        {
            public double[] Da;
            public double[] A;
        }

        private static double Distance(double[] p, double[] q)
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
        private static bool PlaneAxes(string plane, out int u, out int v)
        {
            switch ((plane ?? "").Trim().ToLowerInvariant())
            {
                case "top": u = 0; v = 2; return true;   // XZ
                case "right": u = 1; v = 2; return true;      // YZ
                case "front":
                case "": u = 0; v = 1; return true;            // XY
                default: u = -1; v = -1; return false;
            }
        }

        /// <summary>
        /// The reading of a frame's two sizes in the catalogue spelling
        /// («overall» or «center_distance»), or null when the value is neither
        /// (Z-C21.10 N32). Matched the way <see cref="PlaneAxes"/> matches
        /// the plane: trimmed, without regard to case; empty means the
        /// default, «overall», as a missing value does.
        /// </summary>
        private static string FrameReading(string asked)
        {
            switch ((asked ?? "").Trim().ToLowerInvariant())
            {
                case "":
                case "overall": return "overall";
                case "center_distance": return "center_distance";
                default: return null;
            }
        }

        /// <summary>
        /// Where the overhang of a rejected frame came from, as a sentence to
        /// append to the rejection (Z-C21.10 N36). Empty when nobody said.
        /// </summary>
        private static string OverhangNote(string source, bool dropped)
        {
            if (source == SectionProfile.OverhangMeasured)
                return " The overhang was measured just now on a trial frame, not " +
                       "taken from the archive.";
            if (source != SectionProfile.OverhangFromArchive) return "";
            return dropped
                ? " The overhang was not measured now: it came from the archive of " +
                  "measured sections («" + SectionProfile.ArchivePath + "»). I have " +
                  "dropped that entry, so the next attempt measures the profile again."
                : " The overhang was not measured now: it came from the archive of " +
                  "measured sections («" + SectionProfile.ArchivePath + "»), and I " +
                  "could not drop that entry: delete that file (it is rebuilt by " +
                  "measuring) before trying again.";
        }

        /// <summary>
        /// I quattro segmenti del percorso di un telaio, sugli assi del piano.
        ///
        /// Le quote che arrivano qui sono gia' quelle del PERCORSO, cioe'
        /// l'ingombro esterno meno la sporgenza del profilo. Le sporgenze
        /// servono ancora per il caso «angolo sull'origine»: li' l'ingombro
        /// deve partire da zero, quindi l'asse parte da mezza sporgenza.
        /// </summary>
        private static List<Segment> FrameSegments(
            double pathU, double pathV, int axisU, int axisV,
            bool centered, double[] overhangs)
        {
            double u0 = centered ? -pathU / 2 : overhangs[0] / 2;
            double v0 = centered ? -pathV / 2 : overhangs[1] / 2;
            double u1 = u0 + pathU;
            double v1 = v0 + pathV;

            Func<double, double, double[]> point = (u, v) =>
            {
                var p = new double[3];
                p[axisU] = Math.Round(u, 6);
                p[axisV] = Math.Round(v, 6);
                return p;
            };

            var angles = new[] { point(u0, v0), point(u1, v0), point(u1, v1), point(u0, v1) };
            var outside = new List<Segment>();
            for (int i = 0; i < 4; i++)
                outside.Add(new Segment { Da = angles[i], A = angles[(i + 1) % 4] });
            return outside;
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
        /// (DocumentState) infatti si usa GetExtremePoint. Qui no: questo
        /// numero va confrontato con la sporgenza misurata da
        /// SectionProfile, che usa lo STESSO GetBodyBox sul telaio di
        /// prova. Due misure con la stessa approssimazione si annullano —
        /// percorso = quota - sporgenza, ingombro = percorso + sporgenza —
        /// mentre due misure diverse farebbero fallire la verifica su un
        /// telaio giusto, con un profilo tondo. Chi cambia una delle due deve
        /// cambiare l'altra.
        /// </summary>
        private static double[] BodiesBoundingBox(IFeature feature)
        {
            try
            {
                var faces = feature.GetFaces() as object[];
                if (faces == null || faces.Length == 0) return null;

                var bodies = new List<IBody2>();
                foreach (var o in faces)
                {
                    IBody2 body;
                    try { body = (o as IFace2).GetBody() as IBody2; }
                    catch { continue; }
                    if (body == null) continue;
                    bool already = false;
                    foreach (var c in bodies)
                        if (ReferenceEquals(c, body) || Equals(c, body)) { already = true; break; }
                    if (!already) bodies.Add(body);
                }
                if (bodies.Count == 0) return null;

                // Approximate ON PURPOSE, see the summary above (paired with
                // SectionProfile): ApiHelpers.ApproximateBoundingBox.
                var total = ApiHelpers.UnionOfBoxes(bodies, ApiHelpers.ApproximateBoundingBox);
                if (total == null) return null;
                for (int i = 0; i < 6; i++) total[i] *= 1000.0;
                return total;
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
        private static Dictionary<string, object> Map(
            Dictionary<string, object> a, string key)
        {
            object raw;
            if (a == null || !a.TryGetValue(key, out raw) || raw == null) return null;
            return raw as Dictionary<string, object>;
        }

        private static string Triplet(double[] p)
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
        ///   dal catalogo:  { length_mm, width_mm, piano, centrato, misure }
        ///   dal server:    { telaio: { length_mm, ... }, segmenti: [...] }
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
        private static Dictionary<string, object> RequestedFrame(Dictionary<string, object> a)
        {
            var nested = Map(a, "frame");
            if (nested != null) return nested;
            if (a == null) return null;

            // Senza le due quote non c'e' nessun telaio da ricavare: sara'
            // un membro strutturale con i suoi segmenti, che passa da qui
            // perche' condivide il `case`.
            if (!a.ContainsKey("length_mm") || !a.ContainsKey("width_mm"))
                return null;

            var outside = new Dictionary<string, object>
            {
                ["length_mm"] = a["length_mm"],
                ["width_mm"] = a["width_mm"],
                // I predefiniti sono quelli che il catalogo promette: il
                // piano frontale, centrato, e le quote lette come ingombro
                // esterno. Quest'ultimo e' il punto dell'operazione: chi
                // chiede un telaio 1000x800 vuole misurare 1000x800 sul
                // pezzo, non sulla linea d'asse.
                ["plane"] = a.ContainsKey("plane") ? a["plane"] : "front",
                ["centered"] = a.ContainsKey("centered") ? a["centered"] : true,
                ["measured_as"] = a.ContainsKey("measured_as") ? a["measured_as"] : "overall",
            };
            return outside;
        }

        private List<Segment> SegmentsFrom(Dictionary<string, object> a, out string reason)
        {
            reason = null;
            object raw;
            if (a == null || !a.TryGetValue("segments", out raw) || raw == null)
            {
                reason = "Il piano non porta nessun segmento: non c'e' percorso.";
                return null;
            }
            var itemList = raw as System.Collections.IEnumerable;
            if (itemList == null)
            {
                reason = "I segmenti del piano non sono leggibili.";
                return null;
            }

            var outside = new List<Segment>();
            int i = 0;
            foreach (var entry in itemList)
            {
                i++;
                var map = entry as Dictionary<string, object>;
                if (map == null)
                {
                    reason = "Il segmento " + i + " non e' leggibile.";
                    return null;
                }
                double[] da = Point(map, "start", i, out reason);
                if (da == null) return null;
                double[] b = Point(map, "end", i, out reason);
                if (b == null) return null;
                if (Distance(da, b) < 0.001)
                {
                    reason = "Il segmento " + i + " ha i due estremi nello stesso punto.";
                    return null;
                }
                outside.Add(new Segment { Da = da, A = b });
            }
            if (outside.Count == 0)
            {
                reason = "Il percorso e' vuoto.";
                return null;
            }
            return outside;
        }

        private static double[] Point(
            Dictionary<string, object> map, string key, int i, out string reason)
        {
            reason = null;
            object raw;
            if (!map.TryGetValue(key, out raw) || raw == null)
            {
                reason = "Al segmento " + i + " manca «" + key + "».";
                return null;
            }
            var itemList = (raw as System.Collections.IEnumerable);
            if (itemList == null)
            {
                reason = "Al segmento " + i + " il punto «" + key + "» non e' leggibile.";
                return null;
            }
            var values = new List<double>();
            foreach (var v in itemList)
            {
                try { values.Add(Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture)); }
                catch
                {
                    reason = "Al segmento " + i + ", punto «" + key +
                             "», una coordinata non e' un numero.";
                    return null;
                }
            }
            if (values.Count != 3)
            {
                reason = "Al segmento " + i + " il punto «" + key + "» non ha tre coordinate.";
                return null;
            }
            return values.ToArray();
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
        private object[] BodiesFromReference(
            Dictionary<string, object> a, string theField, DocumentState docState,
            out int howMany, out string reason)
        {
            howMany = 0;
            reason = null;

            if (HasReference(a, theField))
            {
                int number;
                var planStep = CitedStep(a, theField, out number, out reason);
                if (planStep == null) return null;

                var fromStep = References.BodiesOf(planStep, out reason);
                if (fromStep == null) return null;
                howMany = fromStep.Count;
                if (howMany == 0)
                {
                    reason = "Il passo " + number + " non ha lasciato nessun corpo da usare.";
                    return null;
                }
                return fromStep.Cast<object>().ToArray();
            }

            string fieldUid = theField.EndsWith("_ref", StringComparison.Ordinal)
                ? theField.Substring(0, theField.Length - 4) + "_uid"
                : theField + "_uid";
            var uid = StringList(a, fieldUid);
            if (uid.Count == 0)
            {
                reason =
                    "Non hai detto su quali corpi lavorare. Indica «" + fieldUid +
                    "» con gli identificativi dello stato del documento (C1, C2...), " +
                    "oppure «" + theField + "» se le barre le crea un passo precedente " +
                    "di questo stesso piano.";
                return null;
            }

            var bodies = BodiesFromUid(docState, uid, a, out reason);
            if (bodies == null) return null;
            howMany = bodies.Count;
            return bodies.Cast<object>().ToArray();
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
        /// Diceva gia' cosi', e il codice `SignatureMatches` non lo chiamava
        /// affatto: i corpi erano controllati per ESISTENZA e non per
        /// IDENTITA'. Misurato il 14/9/2026 (voce E4): due blocchi, un taglio
        /// che spezza il primo in due, poi «sposta C2». Il taglio rinumera i
        /// corpi, «C2» diventa mezzo blocco grande, e viene spostato quello —
        /// **`succeeded: true`, nessun avviso**, e il blocco piccolo resta dov'e'.
        ///
        /// Un commento che descrive un controllo che non c'e' e' peggio di
        /// nessun commento: chi legge smette di cercare il buco proprio dove
        /// sta.
        /// </summary>
        private List<IBody2> BodiesFromUid(
            DocumentState docState, List<string> uid,
            Dictionary<string, object> arguments, out string reason)
        {
            reason = null;
            if (docState == null)
            {
                reason = "Non ho lo stato del documento: non so a cosa corrisponda «" +
                         string.Join(", ", uid) + "».";
                return null;
            }

            var outside = new List<IBody2>();
            foreach (var u in uid)
            {
                DocumentState.EntitySignature signature;
                if (!docState.Signatures.TryGetValue(u, out signature) || signature.Type != "BODY")
                {
                    reason = "Il corpo «" + u + "» non c'e' piu' in questo documento.";
                    return null;
                }
                var body = signature.Entity as IBody2;
                if (body == null)
                {
                    reason = "Il corpo «" + u + "» non e' piu' utilizzabile.";
                    return null;
                }

                // E ADESSO IL CONTROLLO CHE IL COMMENTO SOPRA PROMETTEVA.
                if (!SignatureMatches(arguments, u, signature, out reason)) return null;
                bool already = false;
                foreach (var c in outside)
                    if (ReferenceEquals(c, body) || Equals(c, body)) { already = true; break; }
                if (!already) outside.Add(body);
            }
            if (outside.Count == 0)
            {
                reason = "Nessun corpo indicato.";
                return null;
            }
            return outside;
        }

        /// <summary>
        /// Le facce di estremita' indicate da un riferimento.
        ///
        /// <paramref name="machinedFaces"/>: the planar faces with an inner loop made
        /// by another feature that were not taken for open ends (finding N7,
        /// Riferimenti.InnerLoopsAreTheProfileVoid). Counted over faces that were
        /// all read: a face that could not be told apart stops the step instead.
        /// </summary>
        private IFace2[] FacesFromReference(
            Dictionary<string, object> a, string theField, DocumentState docState,
            out int howMany, out int machinedFaces, out string reason)
        {
            howMany = 0;
            machinedFaces = 0;
            reason = null;
            List<IFace2> faces;
            string whichBars;

            if (HasReference(a, theField))
            {
                int number;
                var planStep = CitedStep(a, theField, out number, out reason);
                if (planStep == null) return null;
                faces = References.FreeEnds(planStep, out machinedFaces, out reason);
                whichBars = "of step " + number;
            }
            else
            {
                // In un messaggio nuovo non c'e' nessun passo da citare: si
                // parte dai corpi che lo stato del documento elenca.
                var uid = StringList(a, "bodies_uid");
                if (uid.Count == 0)
                {
                    reason =
                        "Non hai detto quali barre tappare. Indica «bodies_uid» con " +
                        "gli identificativi dello stato (C1, C2...), oppure " +
                        "«" + theField + "» se le barre le crea un passo precedente " +
                        "di questo stesso piano.";
                    return null;
                }
                var bodies = BodiesFromUid(docState, uid, a, out reason);
                if (bodies == null) return null;
                faces = References.FreeEndsOf(bodies, out machinedFaces, out reason);
                whichBars = "of bodies " + string.Join(", ", uid);
            }

            if (faces == null) return null;
            howMany = faces.Count;
            if (howMany == 0)
            {
                // Rewritten in English with N7: the faces left out are counted,
                // or a drilled solid bar would read «no open ends» with no word
                // about the holes that were looked at and left out.
                reason =
                    "The bars " + whichBars + " have no open end to cap: either their profile " +
                    "is solid, or their ends are already closed by the corner treatment." +
                    (machinedFaces > 0
                        ? " " + machinedFaces + " planar face(s) whose inner outline was made by a later " +
                          "feature (a hole machined into a wall, or a machined mouth of a tube) were " +
                          "looked at and left out: I do not cap those."
                        : "");
                return null;
            }
            return faces.ToArray();
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
        private static void Recognize(
            IFeature f, List<IBodyFolder> outside, HashSet<string> already)
        {
            try
            {
                var folder = f.GetSpecificFeature2() as IBodyFolder;
                if (folder == null) return;
                if (folder.Type != (int)swBodyFolderFeatureType_e.swWeldmentCutListFolder)
                    return;
                if (folder.GetCutListType() != (int)swCutListType_e.swWeldmentCutlist)
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
                string key;
                try { key = f.Name; }
                catch { key = null; }
                if (key != null && !already.Add(key)) return;

                outside.Add(folder);
            }
            catch { }
        }

        /// <summary>Le cartelle della distinta di taglio gia' presenti.</summary>
        private static List<IBodyFolder> CutListFolders(IModelDoc2 model)
        {
            var outside = new List<IBodyFolder>();
            var partDoc = model as IPartDoc;
            if (partDoc == null) return outside;
            // LE CARTELLE DELLA DISTINTA SONO SOTTO-FEATURE.
            //
            // Stanno dentro «Corpi solidi», e cercarle solo fra le feature di
            // primo livello vuol dire non trovarle mai — e allora si crede
            // che una distinta non ci sia e se ne crea una seconda, con due
            // elenchi della stessa carpenteria in officina.
            // Services/DistintaTaglio.cs le legge cosi' dal 2 settembre.
            var already = new HashSet<string>(StringComparer.Ordinal);
            IFeature f;
            try { f = partDoc.IFirstFeature(); }
            catch { return outside; }
            while (f != null)
            {
                Recognize(f, outside, already);
                try
                {
                    IFeature below = f.IGetFirstSubFeature();
                    while (below != null)
                    {
                        Recognize(below, outside, already);
                        below = below.IGetNextSubFeature();
                    }
                }
                catch { }
                try { f = f.IGetNextFeature(); }
                catch { break; }
            }
            return outside;
        }
    }
}
