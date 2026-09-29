using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// Quanto e' grossa la sezione di un profilo di saldatura.
    ///
    /// IL DIFETTO DA CUI NASCE QUESTO FILE
    /// Il 9 settembre 2026 il progettista ha chiesto un telaio 1000x800 in
    /// tubolare 80x80x5 e ha misurato sul pezzo finito 1080x880. Non e' un
    /// errore di calcolo: il percorso di un membro strutturale e' la LINEA
    /// D'ASSE, e SolidWorks centra il profilo su quella linea. Un percorso
    /// disegnato sugli spigoli di un rettangolo 1000x800 fa sporgere le barre
    /// di mezza sezione per lato, cioe' 40 mm, cioe' +80 per dimensione. E il
    /// riassunto mostrato prima di eseguire diceva «1000x800».
    ///
    /// Per arretrare il percorso bisogna sapere quanto misura la sezione. E
    /// non si puo' leggere:
    ///   - il nome della configurazione NON e' una fonte: nella stessa
    ///     libreria convivono «80X80X3», «50x50x3» e «20x20x1,5», e per un
    ///     IPE 200 il nome non dice affatto 200x100;
    ///   - lo schizzo del profilo non ha un ingombro leggibile: ISketch non
    ///     espone nessun GetBox (verificato per riflessione sull'interop
    ///     29.5.1.1: ci sono solo GetSketchPoints, GetSketchPoints2,
    ///     GetSketchSegments, GetSketchPointsCount, IsBoundaryBoxSketch);
    ///   - Document Manager, che leggerebbe il .sldlfp senza aprirlo, su
    ///     questa postazione non ha licenza (CLASS_E_NOTLICENSED).
    ///
    /// COSA SI FA ALLORA: si costruisce un TELAIO DI PROVA in un documento usa
    /// e getta e si misura di quanto sporge oltre il proprio percorso. Non
    /// interpreta niente: guarda il pezzo.
    ///
    /// E SI COSTRUISCE UN TELAIO, NON UNA BARRA. La prima versione sondava una
    /// barra per direzione e ne prendeva l'ingombro. Funziona per i profili
    /// simmetrici e SBAGLIA per gli altri: misurato il 9 settembre 2026, un
    /// telaio 1200x600 in tubo rettangolare 80X50X5 usciva 1170x600. La barra
    /// isolata distesa su Y occupa 80 in X; la stessa barra DENTRO IL GRUPPO
    /// del telaio ne occupa 50. L'orientamento del profilo non dipende solo
    /// dalla direzione del segmento: dipende dal gruppo. Quindi si misura la
    /// cosa vera, con le stesse opzioni — piano, rotazione, trattamento
    /// d'angolo, gioco.
    ///
    /// La sporgenza non dipende da quanto e' grande il telaio: una misura sola
    /// vale per tutte le quote.
    ///
    /// COSTO E RIMEDIO: una prova vuole un documento nuovo, uno schizzo, un
    /// membro strutturale e una chiusura — qualche secondo, non zero. Il
    /// risultato si tiene in memoria e su disco, per macchina: la seconda volta
    /// che si chiede lo stesso telaio non si misura piu' niente.
    /// </summary>
    public static class SezioneProfilo
    {
        private static readonly object _serratura = new object();
        private static Dictionary<string, double[]> _memoria;

        /// <summary>
        /// Di quanto sporge un TELAIO oltre il proprio percorso, sui due assi
        /// del piano. Serve ad arretrare il percorso per avere le quote
        /// esterne che il progettista ha chiesto.
        ///
        /// PERCHE' SI COSTRUISCE UN TELAIO DI PROVA E NON SI MISURA UNA BARRA
        /// La prima versione sondava una barra per direzione e ne prendeva
        /// l'ingombro. Funziona per i profili simmetrici e SBAGLIA per gli
        /// altri: misurato il 9 settembre 2026, un telaio 1200x600 in tubo
        /// rettangolare 80X50X5 usciva 1170x600. La barra isolata distesa su Y
        /// occupa 80 in X, ma la stessa barra DENTRO IL GRUPPO del telaio ne
        /// occupa 50: l'orientamento del profilo non dipende solo dalla
        /// direzione del segmento, dipende dal gruppo.
        ///
        /// Quindi non si indovina: si costruisce un telaio di prova con le
        /// stesse opzioni — stesso piano, stessa rotazione, stesso trattamento
        /// d'angolo, stesso gioco — e si misura di quanto sporge. La
        /// sporgenza non dipende da quanto e' grande il telaio, quindi una
        /// misura sola vale per tutte le quote.
        ///
        /// Costa un documento usa e getta, e si tiene: la seconda volta che si
        /// chiede lo stesso telaio non si misura piu' niente.
        /// </summary>
        /// <returns>
        /// {sporgenzaU, sporgenzaV} in mm, oppure null se non si e' potuto
        /// misurare. In quel caso CHI CHIAMA DEVE FALLIRE, non tirare a
        /// indovinare: un telaio con le quote sbagliate e' il difetto che
        /// questo file esiste per chiudere.
        /// </returns>
        /// <remarks>
        /// THIS SIGNATURE DOES NOT CHANGE: the bench calls this method by
        /// reflection (Banco/Program.cs, Sezioni), and an overload would make
        /// `GetMethod("SporgenzeDelTelaio")` ambiguous. The work is in
        /// <see cref="FrameOverhang"/>, which also says where the overhang
        /// came from.
        /// </remarks>
        public static double[] SporgenzeDelTelaio(
            ISldWorks sw, string profilo, string configurazione,
            int asseU, int asseV, double rotazioneGradi,
            int trattamentoAngolo, double giocoMm, Action<string> log)
        {
            string source;
            return FrameOverhang(sw, profilo, configurazione, asseU, asseV,
                                 rotazioneGradi, trattamentoAngolo, giocoMm, log, out source);
        }

        /// <summary>The overhang was measured now, on a trial frame.</summary>
        public const string OverhangMeasured = "measured";

        /// <summary>The overhang was remembered: this session or the archive file.</summary>
        public const string OverhangFromArchive = "archive";

        /// <summary>
        /// <see cref="SporgenzeDelTelaio"/>, saying where the answer came from
        /// (<see cref="OverhangMeasured"/> or <see cref="OverhangFromArchive"/>;
        /// null when there is no answer).
        ///
        /// WHAT IS REMEMBERED, AND FOR HOW LONG (Z-C21.10 N36). Until library
        /// 0.140.0 two things went wrong with the memory:
        ///   - a FAILED probe was kept as null and returned as null for the
        ///     rest of the session: one passing hiccup (a network drive that
        ///     stutters, a template that does not open) blocked that profile
        ///     until SOLIDWORKS was closed;
        ///   - the key had no date and no size of the library file: a library
        ///     corrected afterwards kept its old overhang forever, in memory
        ///     and in the archive on disk.
        /// Now a failure is not remembered (the next call measures again), and
        /// the key carries the file's LastWriteTimeUtc and length: a library
        /// that changes is a new key. When the file cannot be read for its
        /// date, nothing is remembered and the probe runs every time — slower,
        /// never stale. A frame that comes out wrong on a remembered overhang
        /// drops it with <see cref="ForgetFrameOverhang"/>.
        /// </summary>
        public static double[] FrameOverhang(
            ISldWorks sw, string profile, string configuration,
            int axisU, int axisV, double rotationDegrees,
            int cornerTreatment, double gapMm, Action<string> log, out string source)
        {
            source = null;
            if (sw == null || string.IsNullOrWhiteSpace(profile)) return null;
            if (axisU < 0 || axisU > 2 || axisV < 0 || axisV > 2 || axisU == axisV) return null;
            log = log ?? (m => { });

            string key = FrameKeyOnDisk(profile, configuration, axisU, axisV,
                                        rotationDegrees, cornerTreatment, gapMm);
            return RememberOrMeasure(key,
                () => TelaioDiProva(sw, profile, configuration, axisU, axisV,
                                    rotationDegrees, cornerTreatment, gapMm, log),
                log, out source);
        }

        /// <summary>
        /// The memory of <see cref="FrameOverhang"/>, without SOLIDWORKS: the
        /// remembered overhang for <paramref name="key"/>, or else what
        /// <paramref name="measure"/> returns — remembered only when it is not
        /// null (Z-C21.10 N36). A null key remembers nothing. It takes the
        /// measurement as a delegate so that a contract test can drive it
        /// with no CAD at all: a measurement that fails, then one that
        /// succeeds, must be asked twice.
        /// </summary>
        internal static double[] RememberOrMeasure(
            string key, Func<double[]> measure, Action<string> log, out string source)
        {
            source = null;
            log = log ?? (m => { });
            if (key != null)
            {
                lock (_serratura)
                {
                    Carica();
                    double[] known;
                    if (_memoria.TryGetValue(key, out known) && known != null)
                    {
                        source = OverhangFromArchive;
                        log(string.Format(CultureInfo.InvariantCulture,
                            "[sezione] overhang {0:0.##}/{1:0.##} remembered, not measured now " +
                            "(archive: {2})", known[0], known[1], PercorsoArchivio()));
                        return new[] { known[0], known[1] };
                    }
                }
            }

            double[] measured = null;
            try { measured = measure == null ? null : measure(); }
            catch (Exception ex) { log("[sezione] the trial frame failed: " + ex.Message); }

            // A FAILURE IS NOT REMEMBERED: the next call measures again.
            if (measured == null || measured.Length < 2) return null;
            source = OverhangMeasured;

            if (key != null)
            {
                lock (_serratura)
                {
                    Carica();
                    _memoria[key] = new[] { measured[0], measured[1], 0.0 };
                    Salva(log);
                }
            }
            return new[] { measured[0], measured[1] };
        }

        /// <summary>
        /// Drops a remembered overhang, from memory and from the archive file
        /// (Z-C21.10 N36): the frame built with it came out wrong, and keeping
        /// it would rebuild the same wrong frame at every attempt. The
        /// arguments are those of <see cref="FrameOverhang"/>.
        /// </summary>
        /// <returns>True if there was an entry and it is gone.</returns>
        public static bool ForgetFrameOverhang(
            string profile, string configuration, int axisU, int axisV,
            double rotationDegrees, int cornerTreatment, double gapMm, Action<string> log)
        {
            string key = FrameKeyOnDisk(profile, configuration, axisU, axisV,
                                        rotationDegrees, cornerTreatment, gapMm);
            return ForgetKey(key, log);
        }

        /// <summary>
        /// <see cref="ForgetFrameOverhang"/> by key, without the disk read of
        /// the library's date and size: what a contract test calls.
        /// </summary>
        internal static bool ForgetKey(string key, Action<string> log)
        {
            log = log ?? (m => { });
            if (key == null) return false;
            lock (_serratura)
            {
                Carica();
                if (!_memoria.Remove(key)) return false;
                Salva(log);
            }
            log("[sezione] overhang dropped from the archive, it will be measured again: " + key);
            return true;
        }

        /// <summary>
        /// Where the archive of measured overhangs lives, to name it in a
        /// message: a rejection that blames a remembered number must say
        /// where it is remembered.
        /// </summary>
        public static string ArchivePath
        {
            get { return PercorsoArchivio(); }
        }

        /// <summary>
        /// The first field of every key of this version. An archive line that
        /// does not start with it was written before date and size were part
        /// of the key, can never be matched again, and is left out on load.
        /// </summary>
        private const string KeyPrefix = "frame-v2";

        /// <summary>
        /// The key of a frame overhang, from the library file's date and
        /// length as well as the frame options (Z-C21.10 N36). Pure: no disk,
        /// no SOLIDWORKS — a contract test can call it.
        /// </summary>
        internal static string FrameKey(
            string profile, long lastWriteUtcTicks, long length, string configuration,
            int axisU, int axisV, double rotationDegrees, int cornerTreatment, double gapMm)
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join("|",
                KeyPrefix,
                (profile ?? "").ToUpperInvariant(),
                lastWriteUtcTicks.ToString(c),
                length.ToString(c),
                configuration ?? "",
                axisU.ToString(c),
                axisV.ToString(c),
                Math.Round(rotationDegrees, 3).ToString("0.###", c),
                cornerTreatment.ToString(c),
                Math.Round(gapMm, 3).ToString("0.###", c));
        }

        /// <summary>True for a key written by this version (see <see cref="KeyPrefix"/>).</summary>
        internal static bool IsCurrentKey(string key)
        {
            return key != null && key.StartsWith(KeyPrefix + "|", StringComparison.Ordinal);
        }

        /// <summary>
        /// <see cref="FrameKey"/> with the date and length read from the disk
        /// now; null when the file cannot be read, and then nothing is
        /// remembered.
        /// </summary>
        private static string FrameKeyOnDisk(
            string profile, string configuration, int axisU, int axisV,
            double rotationDegrees, int cornerTreatment, double gapMm)
        {
            try
            {
                var file = new FileInfo(profile);
                if (!file.Exists) return null;
                return FrameKey(profile, file.LastWriteTimeUtc.Ticks, file.Length, configuration,
                                axisU, axisV, rotationDegrees, cornerTreatment, gapMm);
            }
            catch { return null; }
        }

        /// <summary>Il lato del telaio di prova, in mm.</summary>
        private const double LATO_PROVA_U = 1000;
        private const double LATO_PROVA_V = 700;

        /// <summary>
        /// Costruisce un telaio di prova e restituisce di quanto sporge oltre
        /// il percorso, sui due assi: {sporgenzaU, sporgenzaV} in mm.
        /// </summary>
        private static double[] TelaioDiProva(
            ISldWorks sw, string profilo, string configurazione,
            int asseU, int asseV, double rotazioneGradi,
            int trattamentoAngolo, double giocoMm, Action<string> log)
        {
            if (!File.Exists(profilo))
            {
                log("[sezione] il profilo non esiste: " + profilo);
                return null;
            }

            // The document to go back to is kept by REFERENCE, not by title
            // (Z-C21.13, pattern S18): DocumentByPath.ActivateAndReadBack
            // brings it back by its path and reads back that it is in front.
            IModelDoc2 previousFront = null;
            try { previousFront = sw.ActiveDoc as IModelDoc2; }
            catch { }

            // THE TRIAL MEMBER OPENS THE PROFILE LIBRARY TOO (Z-C21.10 N31).
            // InsertStructuralWeldment5 below is a real insertion, and
            // SOLIDWORKS opens the library by itself and leaves it modified,
            // as OperazioniSaldatura measured for the real one. Closing the
            // trial document may or may not take the library with it: the
            // guide says «This method also closes any non-active hidden
            // documents» (ISldWorks~CloseDoc.html), and whether a library
            // opened this way is one of them is not measured. So the state
            // is read before, and the same rule as the real insertion is
            // applied after (OperazioniSw.RichiudiLaLibreria): what we opened
            // or dirtied is closed without saving, then read back. It matters
            // beyond this probe: a library left modified here looks, to the
            // next frame, like one somebody was working on, and that frame
            // then leaves it alone.
            var libraryBefore = OperazioniSw.StatoDellaLibreria(sw, profilo);

            IModelDoc2 doc = null;
            try
            {
                string modello = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                if (string.IsNullOrWhiteSpace(modello) || !File.Exists(modello))
                {
                    log("[sezione] non c'e' un modello di parte predefinito.");
                    return null;
                }
                doc = sw.NewDocument(modello, 0, 0, 0) as IModelDoc2;
                if (doc == null) { log("[sezione] documento di prova non creato."); return null; }

                double u = LATO_PROVA_U / 2000.0, v = LATO_PROVA_V / 2000.0;
                var angoli = new[]
                {
                    Punto(asseU, -u, asseV, -v),
                    Punto(asseU,  u, asseV, -v),
                    Punto(asseU,  u, asseV,  v),
                    Punto(asseU, -u, asseV,  v),
                };

                var schizzi = doc.SketchManager;
                doc.ClearSelection2(true);
                schizzi.Insert3DSketch(true);
                var creati = new List<ISketchSegment>();
                for (int i = 0; i < 4; i++)
                {
                    var a = angoli[i];
                    var b = angoli[(i + 1) % 4];
                    var seg = schizzi.CreateLine(a[0], a[1], a[2], b[0], b[1], b[2])
                        as ISketchSegment;
                    if (seg == null) { log("[sezione] segmento di prova non creato."); return null; }
                    creati.Add(seg);
                }
                schizzi.Insert3DSketch(true);

                var gruppo = doc.FeatureManager.CreateStructuralMemberGroup()
                    as IStructuralMemberGroup;
                if (gruppo == null) { log("[sezione] gruppo di prova non creato."); return null; }
                gruppo.Segments = creati.Cast<SketchSegment>().ToArray();
                if (Math.Abs(rotazioneGradi) > 1e-9)
                    gruppo.Angle = rotazioneGradi * Math.PI / 180.0;
                gruppo.ApplyCornerTreatment = trattamentoAngolo != 0;
                if (trattamentoAngolo != 0) gruppo.CornerTreatmentType = trattamentoAngolo;
                if (giocoMm > 0)
                {
                    gruppo.GapWithinGroup = giocoMm / 1000.0;
                    gruppo.GapForOtherGroups = giocoMm / 1000.0;
                }

                var feature = doc.FeatureManager.InsertStructuralWeldment5(
                    Path: profilo,
                    ConnectedSegmentsOption: 1,
                    AllowProtrusion: false,
                    Groups: new[] { (StructuralMemberGroup)gruppo },
                    ConfigurationName: configurazione) as IFeature;
                if (feature == null)
                {
                    log("[sezione] il telaio di prova non e' nato: profilo «" + profilo +
                        "» misura «" + configurazione + "».");
                    return null;
                }
                try { doc.EditRebuild3(); } catch { }

                var corpi = (doc as IPartDoc).GetBodies2(
                    (int)swBodyType_e.swSolidBody, false) as object[];
                if (corpi == null || corpi.Length == 0) return null;
                // Approximate ON PURPOSE: paired with the same approximation in
                // OperazioniSaldatura.IngombroDeiCorpi, so that the two cancel out.
                var totale = ApiHelpers.UnionOfBoxes(corpi, c => ApiHelpers.ApproximateBoundingBox(c as IBody2));
                if (totale == null) return null;

                double ingombroU = (totale[3 + asseU] - totale[asseU]) * 1000.0;
                double ingombroV = (totale[3 + asseV] - totale[asseV]) * 1000.0;
                double sporgeU = ingombroU - LATO_PROVA_U;
                double sporgeV = ingombroV - LATO_PROVA_V;

                // Una sporgenza negativa o assurda vuol dire che si e' misurato
                // qualcos'altro: meglio non saperlo che saperlo sbagliato.
                if (sporgeU < -0.01 || sporgeV < -0.01 ||
                    sporgeU > LATO_PROVA_U || sporgeV > LATO_PROVA_V)
                {
                    log(string.Format(CultureInfo.InvariantCulture,
                        "[sezione] sporgenze fuori scala sul telaio di prova: {0:0.##} / {1:0.##}",
                        sporgeU, sporgeV));
                    return null;
                }
                log(string.Format(CultureInfo.InvariantCulture,
                    "[sezione] telaio di prova {0}x{1}: ingombro {2:0.##}x{3:0.##}, " +
                    "sporgenze {4:0.##}/{5:0.##}",
                    LATO_PROVA_U, LATO_PROVA_V, ingombroU, ingombroV, sporgeU, sporgeV));
                return new[] { sporgeU, sporgeV };
            }
            finally
            {
                // THE TRIAL DOCUMENT, BY REFERENCE (Z-C21.13 N11). It was never
                // saved, so it has no path: it is closed as the active document
                // it is, or by a title no other open document answers to, and
                // read back. Until library 0.143.0 it was CloseDoc(its title),
                // and a new part's title can be the stem of an open file.
                if (doc != null)
                {
                    var closing = DocumentByPath.CloseAndReadBack(sw, doc);
                    if (!closing.IsClosed || closing.AlsoClosed.Count > 0)
                        log("[section] trial frame: " + closing.Message);
                }
                string leftModified = OperazioniSw.RichiudiLaLibreria(sw, profilo, libraryBefore, log);
                if (leftModified != null) log("[sezione] " + leftModified);
                // BACK TO THE DOCUMENT THAT WAS IN FRONT, by its path, with
                // swDontRebuildActiveDoc: this was ActivateDoc3(title, false, 0),
                // and 0 is swUserDecision, a dialog the call waits on (N8).
                if (previousFront != null)
                {
                    int activationErrors;
                    string problem;
                    if (!DocumentByPath.ActivateAndReadBack(sw, previousFront, out activationErrors, out problem))
                        log("[section] " + problem);
                }
            }
        }

        private static double[] Punto(int asseU, double u, int asseV, double v)
        {
            var p = new double[3];
            p[asseU] = u;
            p[asseV] = v;
            return p;
        }
        // ─────────────────────────────────────────────────────────────────
        // il ricordo, in memoria e su disco
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Il nome della cartella sotto %LOCALAPPDATA% dove vive l'archivio
        /// delle sezioni misurate.
        ///
        /// Era scritto fisso «OpenPDM», ed e' sfuggito all'audit A1 perche'
        /// cercava percorsi e nomi di persone, non il nome del prodotto dentro
        /// una stringa. In una libreria che esiste per non presupporre OpenPDM,
        /// scrivere nella cartella di OpenPDM e' sbagliato due volte: sporca
        /// casa d'altri, e lega la libreria a un prodotto che potrebbe non
        /// essere installato.
        ///
        /// Chi ospita la libreria puo' rimetterci il nome che vuole. L'add-in
        /// OpenPDM lo fa, cosi' l'archivio gia' misurato non viene abbandonato:
        /// non e' un dato prezioso — si ricostruisce misurando — ma buttarlo
        /// senza dirlo costerebbe una riapertura di libreria per profilo.
        /// </summary>
        public static string CartellaArchivio
        {
            get { return _cartellaArchivio; }
            set
            {
                _cartellaArchivio =
                    string.IsNullOrWhiteSpace(value) ? "Riscontro" : value.Trim();
            }
        }
        private static string _cartellaArchivio = "Riscontro";

        private static string PercorsoArchivio()
        {
            string cartella = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                _cartellaArchivio);
            return Path.Combine(cartella, "sezioni-profili.txt");
        }

        /// <summary>
        /// Il formato e' una riga per misura: chiave TAB dx TAB dy TAB dz.
        /// Niente JSON: questo file lo legge solo lui, e una riga rotta deve
        /// far perdere una misura, non l'archivio.
        /// </summary>
        private static void Carica()
        {
            if (_memoria != null) return;
            _memoria = new Dictionary<string, double[]>(StringComparer.Ordinal);
            try
            {
                string f = PercorsoArchivio();
                if (!File.Exists(f)) return;
                foreach (var riga in File.ReadAllLines(f, Encoding.UTF8))
                {
                    var pezzi = riga.Split('\t');
                    if (pezzi.Length != 4) continue;
                    // A key without the file's date and size (before
                    // Z-C21.10 N36) is never asked again: left out here, and
                    // the next save does not write it back.
                    if (!IsCurrentKey(pezzi[0])) continue;
                    double a, b, c;
                    if (!double.TryParse(pezzi[1], NumberStyles.Float, CultureInfo.InvariantCulture, out a)) continue;
                    if (!double.TryParse(pezzi[2], NumberStyles.Float, CultureInfo.InvariantCulture, out b)) continue;
                    if (!double.TryParse(pezzi[3], NumberStyles.Float, CultureInfo.InvariantCulture, out c)) continue;
                    _memoria[pezzi[0]] = new[] { a, b, c };
                }
            }
            catch { /* un archivio illeggibile costa una misura, non un guasto */ }
        }

        private static void Salva(Action<string> log)
        {
            try
            {
                string f = PercorsoArchivio();
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                var righe = _memoria
                    .Where(v => v.Value != null)
                    .Select(v => string.Join("\t",
                        v.Key,
                        v.Value[0].ToString("0.####", CultureInfo.InvariantCulture),
                        v.Value[1].ToString("0.####", CultureInfo.InvariantCulture),
                        v.Value[2].ToString("0.####", CultureInfo.InvariantCulture)));
                File.WriteAllLines(f, righe, Encoding.UTF8);
            }
            catch (Exception ex) { log("[sezione] non ho potuto salvare l'archivio: " + ex.Message); }
        }
    }
}
