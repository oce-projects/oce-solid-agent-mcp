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
        public static double[] SporgenzeDelTelaio(
            ISldWorks sw, string profilo, string configurazione,
            int asseU, int asseV, double rotazioneGradi,
            int trattamentoAngolo, double giocoMm, Action<string> log)
        {
            if (sw == null || string.IsNullOrWhiteSpace(profilo)) return null;
            if (asseU < 0 || asseU > 2 || asseV < 0 || asseV > 2 || asseU == asseV) return null;
            log = log ?? (m => { });

            string chiave = string.Join("|",
                "telaio",
                (profilo ?? "").ToUpperInvariant(),
                configurazione ?? "",
                asseU.ToString(CultureInfo.InvariantCulture),
                asseV.ToString(CultureInfo.InvariantCulture),
                Math.Round(rotazioneGradi, 3).ToString("0.###", CultureInfo.InvariantCulture),
                trattamentoAngolo.ToString(CultureInfo.InvariantCulture),
                Math.Round(giocoMm, 3).ToString("0.###", CultureInfo.InvariantCulture));

            lock (_serratura)
            {
                Carica();
                double[] gia;
                if (_memoria.TryGetValue(chiave, out gia))
                    return gia == null ? null : new[] { gia[0], gia[1] };
            }

            double[] misurato = null;
            try
            {
                misurato = TelaioDiProva(sw, profilo, configurazione, asseU, asseV,
                                         rotazioneGradi, trattamentoAngolo, giocoMm, log);
            }
            catch (Exception ex) { log("[sezione] telaio di prova non riuscito: " + ex.Message); }

            lock (_serratura)
            {
                Carica();
                _memoria[chiave] = misurato == null
                    ? null
                    : new[] { misurato[0], misurato[1], 0.0 };
                if (misurato != null) Salva(log);
            }
            return misurato;
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

            string tornaA = null;
            try
            {
                var attivo = sw.ActiveDoc as IModelDoc2;
                if (attivo != null) tornaA = attivo.GetTitle();
            }
            catch { }

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

                double[] totale = null;
                var corpi = (doc as IPartDoc).GetBodies2(
                    (int)swBodyType_e.swSolidBody, false) as object[];
                if (corpi == null || corpi.Length == 0) return null;
                foreach (var c in corpi)
                {
                    var b = (c as IBody2).GetBodyBox() as double[];
                    if (b == null || b.Length < 6) continue;
                    if (totale == null) { totale = (double[])b.Clone(); continue; }
                    for (int i = 0; i < 3; i++) totale[i] = Math.Min(totale[i], b[i]);
                    for (int i = 3; i < 6; i++) totale[i] = Math.Max(totale[i], b[i]);
                }
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
                if (doc != null) { try { sw.CloseDoc(doc.GetTitle()); } catch { } }
                if (!string.IsNullOrEmpty(tornaA))
                {
                    try { int e = 0; sw.ActivateDoc3(tornaA, false, 0, ref e); } catch { }
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
