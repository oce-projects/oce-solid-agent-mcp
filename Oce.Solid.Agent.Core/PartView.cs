using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// UNA FOTOGRAFIA DEL PEZZO, per chi non ha occhi.
    ///
    /// PERCHE' ESISTE. Fino alla 0.31.0 il modello non vedeva il pezzo e non
    /// l'aveva mai visto: riceveva un elenco di sessanta facce con area,
    /// normale e verso. Per una piastra forata basta; per capire che FORMA ha
    /// un guscio con le alette, no — sessanta facce con un'area non sono una
    /// forma, sono un elenco.
    ///
    /// La capacita' c'era ed era provata: il banco salva quattro viste su
    /// quattro con `SaveBMP`. Ma viveva nel banco di prova e non arrivava mai a
    /// chi legge. E' la stessa forma del difetto della voce E8 — misurato e
    /// buttato via — con l'aggravante che qui non era mai stato collegato.
    ///
    /// ── DUE REGOLE CHE NON SI NEGOZIANO ───────────────────────────────────
    ///
    /// **NON SI TOCCA LA VISTA DI CHI LAVORA.** Questo ponte si aggancia al
    /// documento che un progettista ha davanti, sul suo schermo. Girare il
    /// pezzo per fotografarlo e lasciarlo girato vuol dire spostargli la roba
    /// sotto le mani mentre lavora. L'orientamento si salva prima e si rimette
    /// dopo, **anche quando la fotografia fallisce**.
    ///
    /// **SU RICHIESTA, MAI DI SERIE.** Un'immagine 1024x768 costa piu' di mille
    /// token: piu' di dieci volte l'intero stato del documento, che sta sotto i
    /// 16.000 CARATTERI. Se ogni `inspect_part` portasse tre viste, il costo
    /// del ponte cambierebbe di un ordine di grandezza — e lo pagherebbe anche
    /// chi voleva solo sapere quanti corpi ci sono.
    ///
    /// ── PERCHE' PNG E NON IL BMP CHE SOLIDWORKS DA' ───────────────────────
    ///
    /// `SaveBMP` scrive un BMP non compresso: 1024x768 sono **2,3 MB**, che in
    /// base64 diventano 3. Lo stesso fotogramma in PNG sta in poche decine di
    /// KB, e i pixel sono identici — un BMP mandato cosi' com'e' sarebbe
    /// cinquanta volte il peso per la stessa immagine.
    /// </summary>
    public static class PartView
    {
        /// <summary>
        /// Le viste che si possono chiedere, col nome che usa chi disegna.
        ///
        /// L'isometrica e' la prima perche' e' l'unica che, da sola, dice una
        /// forma: le tre standard dicono tre sagome e lasciano indovinare il
        /// resto.
        /// </summary>
        private static readonly Dictionary<string, int> Views =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["isometric"] = (int)swStandardViews_e.swIsometricView,
                ["trimetric"] = (int)swStandardViews_e.swTrimetricView,
                ["dimetric"] = (int)swStandardViews_e.swDimetricView,
                ["front"] = (int)swStandardViews_e.swFrontView,
                ["back"] = (int)swStandardViews_e.swBackView,
                ["left"] = (int)swStandardViews_e.swLeftView,
                ["right"] = (int)swStandardViews_e.swRightView,
                ["top"] = (int)swStandardViews_e.swTopView,
                ["bottom"] = (int)swStandardViews_e.swBottomView,
            };

        public static IEnumerable<string> ViewNames { get { return Views.Keys; } }

        /// <summary>Una vista esiste con questo nome?</summary>
        public static bool Conosciuta(string itemName)
        {
            return !string.IsNullOrWhiteSpace(itemName) && Views.ContainsKey(itemName);
        }

        /// <summary>
        /// Fotografa il documento e restituisce i byte di un PNG.
        /// Null col motivo se non si e' potuto.
        ///
        /// <param name="which">
        /// Il nome di una vista, oppure null per fotografare l'orientamento
        /// che c'e' adesso — che e' l'unico modo di NON toccare niente.
        /// </param>
        /// </summary>
        public static byte[] Fire(
            IModelDoc2 model, string which, int width, int height, out string reason)
        {
            reason = null;
            if (model == null) { reason = "Nessun documento aperto."; return null; }

            if (which != null && !Conosciuta(which))
            {
                reason = "Non conosco la vista «" + which + "». Ci sono: " +
                         string.Join(", ", ViewNames) + ".";
                return null;
            }

            // L'ORIENTAMENTO DI ADESSO, PER RIMETTERLO DOPO.
            // Si prende PRIMA di toccare qualunque cosa, e si rimette nel
            // `finally`: se la fotografia fallisce a meta', il pezzo resta
            // girato sullo schermo di chi stava lavorando, e quello e' un
            // danno vero fatto da un'operazione di sola lettura.
            //
            // ── E NON BASTA `Transform`: LA VISTA E' UNA TERNA ───────────
            //
            // Tre tentativi, e i primi due li ha bocciati il banco:
            //
            //   1. tenere `IModelView.Transform` e riassegnarlo. **Non
            //      rimette niente**, misurato.
            //   2. `IModelDoc2.ViewOrientationUndo()`, che sembra fatto
            //      apposta. **Nemmeno quello**, misurato.
            //   3. la terna `Orientation3` + `Translation3` + `Scale2`.
            //
            // La prima spiegazione che mi ero dato del rosso — «`Transform`
            // torna l'oggetto vivo, quindi salvarlo non salva niente» — era
            // **falsa**, e l'ha smentita una misura scritta apposta: i sedici
            // numeri dello stesso riferimento NON cambiano dopo
            // `ShowNamedView2`. E' una copia, ed e' una copia inutile.
            //
            // `Transform` DESCRIVE come si vede il pezzo; non e' lo stato da
            // cui la vista si ricostruisce. Quello e' la terna: da dove guardi
            // (`Orientation3`), dove sei centrato (`Translation3`), quanto sei
            // vicino (`Scale2`). `ViewZoomtofit2` cambia gli ultimi due, e
            // rimettere il solo orientamento lascerebbe il pezzo giusto ma
            // inquadrato da un'altra parte.
            //
            // > Il verso di una riparazione non si ricava ragionando
            // > sull'officina: si chiede al pezzo. Qui il pezzo ha detto no
            // > due volte.
            MathTransform orientationBefore = null;
            MathVector translationBefore = null;
            double scaleBefore = 0;
            IModelView view = null;
            try
            {
                view = model.ActiveView as IModelView;
                if (view != null)
                {
                    // COPIE, NON I RIFERIMENTI CHE LA VISTA DA'.
                    //
                    // `Transform` e' una copia — misurato. Di `Orientation3` e
                    // `Translation3` non si sa niente, e il terzo tentativo di
                    // rimessa a posto e' fallito **esattamente come farebbe un
                    // oggetto vivo**: tienilo, gira la vista, riassegnalo, e
                    // hai riassegnato l'orientamento nuovo.
                    //
                    // Non si assume: si copia comunque. `IInverse()` due volte
                    // da' un `MathTransform` nuovo con la stessa matrice;
                    // `IScale(1.0)` da' un `MathVector` nuovo con le stesse
                    // componenti. Costano due chiamate COM e tolgono di mezzo
                    // una domanda.
                    var o = view.Orientation3 as IMathTransform;
                    var reverse = o == null ? null : o.IInverse() as IMathTransform;
                    orientationBefore = reverse == null ? null : reverse.IInverse();

                    var t = view.Translation3 as IMathVector;
                    translationBefore = t == null ? null : t.IScale(1.0);

                    scaleBefore = view.Scale2;
                }
            }
            catch { }

            string temporary = null;
            try
            {
                if (which != null)
                {
                    int code = Views[which];

                    // `ShowNamedView2` NON RISPONDE: torna `void`. Non c'e'
                    // nessun modo di chiederle se ha girato il pezzo, e la
                    // prima scrittura di questo metodo la trattava come se
                    // tornasse un `bool` — un controllo che non poteva
                    // scattare, scritto per un valore che non esiste.
                    //
                    // L'unica prova che resta e' il fotogramma: se
                    // l'orientamento non e' cambiato, la fotografia esce
                    // lo stesso, solo da un'altra parte. Il nome della vista
                    // e' gia' validato qui sopra contro `Views`, quindi il
                    // caso «nome sbagliato» e' escluso prima di arrivarci.
                    model.ShowNamedView2("", code);

                    // LO ZOOM VA DOPO IL RIDISEGNO, E SI RIDISEGNA ANCORA.
                    // Misurato nel banco: al primo giro il pezzo usciva tagliato
                    // sul bordo destro, perche' l'adattamento lavorava sulla
                    // vista vecchia.
                    model.GraphicsRedraw2();
                    model.ViewZoomtofit2();
                    model.GraphicsRedraw2();

                    // ── E POI SI STRINGE ────────────────────────────────
                    //
                    // `ViewZoomtofit2` lascia un margine largo: misurato, il
                    // pezzo occupava **meta'** della larghezza del fotogramma.
                    // Meta' dei pixel di un'immagine che costa piu' di mille
                    // token non mostra niente.
                    //
                    // Il fattore e' prudente apposta. Il criterio della voce V5
                    // e' asimmetrico: **tagliare e' un difetto, sprecare e' uno
                    // spreco**, e fra i due non c'e' partita. Un numero che
                    // riempie di piu' ma ogni tanto taglia sarebbe peggio di
                    // adesso.
                    try { NarrowIfYouCan(view); model.GraphicsRedraw2(); } catch { }
                }

                // `SaveBMP` vuole un percorso: non sa restituire byte.
                temporary = Path.Combine(
                    Path.GetTempPath(), "oce-vista-" + Guid.NewGuid().ToString("N") + ".bmp");

                // ── IL FOTOGRAMMA PRENDE LA FORMA DELL'AREA GRAFICA ─────
                //
                // **Il difetto (voce V5).** `ViewZoomtofit2` inquadra dentro
                // l'area grafica di SOLIDWORKS; `SaveBMP` rende poi nel
                // fotogramma che gli si chiede. Se le due forme non
                // coincidono, quello che avanza **viene tagliato via**, e non
                // lo dice nessuno.
                //
                // Misurato il 15/9/2026 da `Checks\FramingTest`:
                //
                //     area grafica  1845 x 924  ->  1,997
                //     chiesto       1024 x 768  ->  1,333
                //
                // Su sette viste su dodici il pezzo usciva dal bordo DESTRO —
                // sempre a destra, mai in verticale. Due modelli se ne erano
                // accorti da soli durante la misura di V2: «la terza dimensione
                // non e' determinabile perche' esce dall'inquadratura».
                //
                // **Un pezzo tagliato e' un pezzo diverso**, e un'immagine che
                // ne nasconde meta' e' peggio di nessuna immagine: chi la
                // guarda ci ragiona sopra lo stesso.
                //
                // Quindi il fotogramma non impone piu' la sua forma: prende
                // quella dell'area grafica, dentro le misure massime chieste.
                // Con 1024x768 chiesti e un'area 2:1 escono 1024x513 — piu'
                // piccolo, e **intero**. La larghezza e l'altezza passate
                // restano un TETTO, non una pretesa.
                int lw = width, lh = height;
                FrameView(view, width, height, out lw, out lh);

                if (!model.SaveBMP(temporary, lw, lh) || !File.Exists(temporary))
                {
                    reason = "SOLIDWORKS non ha scritto l'immagine.";
                    return null;
                }

                return InPng(temporary, out reason);
            }
            catch (Exception ex)
            {
                reason = "Non sono riuscito a fotografare il pezzo: " + ex.Message;
                return null;
            }
            finally
            {
                // ── TUTTI E TRE, E L'ORDINE E' ORIENTAMENTO, SCALA, POSIZIONE
                //
                // **L'ordine non e' uno stile: e' la differenza fra rimettere e
                // non rimettere.** Misurato da `Checks\ViewTest`, che prova
                // nove strade in tre secondi:
                //
                //   riassegnare Transform                 scarto 2598 sull'indice 12 (la scala)
                //   Transform copiato                     idem
                //   ViewOrientationUndo()                 scarto 212 sull'indice 9
                //   terna, ordine O -> T -> S             scarto 4,77 sull'indice 9
                //   terna, ordine O -> S -> T             **torna**
                //
                // Con l'ordine sbagliato la ROTAZIONE torna esatta (indici 0-8
                // della matrice) e sbaglia solo la traslazione: `Translation3`
                // vale per la scala che c'e' **in quel momento**, quindi
                // rimetterla prima di `Scale2` vuol dire rimettere una
                // posizione buona per un'altra inquadratura. Il pezzo usciva
                // dritto e spostato — che e' peggio di uscire storto, perche'
                // sembra giusto.
                //
                // Ognuno nel suo `try`: se uno dei tre non si lascia rimettere,
                // gli altri vanno rimessi lo stesso.
                if (view != null)
                {
                    try { if (orientationBefore != null) view.Orientation3 = orientationBefore; }
                    catch { }
                    try { if (scaleBefore > 0) view.Scale2 = scaleBefore; }
                    catch { }
                    try { if (translationBefore != null) view.Translation3 = translationBefore; }
                    catch { }
                    try { model.GraphicsRedraw2(); } catch { }
                }
                if (temporary != null)
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                }
            }
        }

        /// <summary>
        /// LO SPACCATO: il pezzo tagliato da un piano, fotografato (voce V4).
        ///
        /// ── PERCHE' UNA SEZIONE E NON UN'ALTRA VISTA ──────────────────────
        ///
        /// Uno spaccato dice di un guscio piu' di dieci viste esterne: e'
        /// esattamente il gesto che fa un progettista per capire uno spessore.
        /// Dall'esterno una scatola svuotata e una scatola piena sono lo stesso
        /// parallelepipedo.
        ///
        /// ── NON TOCCA IL PEZZO, E NON LO TOCCA DAVVERO ────────────────────
        ///
        /// `GraphicsOnlySection = true`: taglia **il disegno a schermo**, non la
        /// geometria. Nessuna feature nell'albero, nessun corpo diviso, niente
        /// da annullare. E' la differenza fra guardare e modificare, e questo
        /// ponte sta dalla parte del guardare.
        ///
        /// E la sezione **si toglie nel `finally`**, insieme all'orientamento:
        /// lasciarla accesa cambierebbe cio' che il progettista vede sul suo
        /// schermo, che e' il rischio scritto nella voce.
        ///
        /// ── E NON PASSA DA `RunCommand` ──────────────────────────────────
        ///
        /// Il comando di menu «Vista in sezione» apre un pannello e resta li' ad
        /// aspettare: una chiamata COM che apre un modale **non torna** — la
        /// trappola di `AddDimension`, gia' pagata una volta.
        /// `IModelViewManager.CreateSectionView` risponde un bool e non chiede
        /// niente a nessuno.
        /// </summary>
        public static byte[] CaptureInSection(
            IModelDoc2 model, string which, string plane, double offsetMm,
            int width, int height, out string reason)
        {
            reason = null;
            if (model == null) { reason = "Nessun documento aperto."; return null; }

            var handler = model.ModelViewManager;
            if (handler == null)
            {
                reason = "Questo documento non sa fare le sezioni.";
                return null;
            }

            // AN ASSEMBLY OR A DRAWING IS SAID AS SUCH (Z-C21.8, finding N21).
            // ReferencePlane walks the features of a PART (IPartDoc), and on
            // any other document it found no plane: the refusal then said «I
            // cannot find the front plane; there are: front, top, right», and
            // contradicted itself. The document is named instead.
            if (!(model is IPartDoc))
            {
                int kind = -1;
                try { kind = model.GetType(); } catch { kind = -1; }
                reason = "The section is taken on a part, and the document in front is " +
                         (kind == (int)swDocumentTypes_e.swDocASSEMBLY ? "an assembly"
                          : kind == (int)swDocumentTypes_e.swDocDRAWING ? "a drawing" : "not a part") +
                         ": open the part and ask again.";
                return null;
            }

            var onWhich = ReferencePlane(model, plane, out string planeProblem);
            if (onWhich == null)
            {
                // A name that is one of the three, and still no plane: no
                // reference plane of the part has that plane's normal through
                // the origin (Z-C21.5, scheme S19), and the reason says what was
                // read (N21: never the self-contradiction of a list).
                reason = Array.IndexOf(References.BasePlaneNames, plane) >= 0
                    ? "I cannot find the «" + plane + "» plane: " + planeProblem
                    : "Non trovo il piano «" + plane + "». Ci sono: " +
                      "frontale, superiore, destro.";
                return null;
            }

            bool sectionOn = false;
            try
            {
                var data = handler.CreateSectionViewData();
                if (data == null)
                {
                    reason = "SOLIDWORKS non ha preparato la sezione.";
                    return null;
                }
                data.FirstPlane = onWhich;
                data.FirstOffset = Units.M(offsetMm);
                // SOLO GRAFICA: e' la riga che rende questa operazione di sola
                // lettura. Senza, SOLIDWORKS taglia il modello per davvero.
                data.GraphicsOnlySection = true;
                data.Redraw = true;

                sectionOn = handler.CreateSectionView(data);
                if (!sectionOn)
                {
                    reason = "SOLIDWORKS non ha aperto la sezione sul piano «" +
                             plane + "».";
                    return null;
                }

                return Fire(model, which, width, height, out reason);
            }
            catch (Exception ex)
            {
                reason = "Non sono riuscito a sezionare il pezzo: " + ex.Message;
                return null;
            }
            finally
            {
                // SI TOGLIE SEMPRE, anche se la fotografia e' fallita a meta'.
                // Una sezione lasciata accesa sullo schermo di chi lavora e' lo
                // stesso danno dell'orientamento lasciato girato, e per la
                // stessa ragione: l'ha fatto un'operazione che doveva solo
                // guardare.
                if (sectionOn)
                {
                    try { handler.RemoveSectionView(); } catch { }
                    try { model.GraphicsRedraw2(); } catch { }
                }
            }
        }

        /// <summary>
        /// One of the three base planes, by name. **An unknown name is null**,
        /// not the front plane: a silent fallback here would section on a plane
        /// other than the one asked and still call it «the section». It is the
        /// lesson of `FindBasePlane`, where a final `: 1` sent any name to the
        /// front plane.
        ///
        /// BY GEOMETRY SINCE Z-C21.5 (scheme S19, finding C3-6). Until then the
        /// RefPlanes were counted in tree order and the first three were front,
        /// top and right. IPartDoc~IFirstFeature.html warns that the order of
        /// the features changes when the part is edited, and the same page
        /// forbids trusting the names («Plane1»). The Z-C21.3 probe of C3-6
        /// (28/9/2026, SOLIDWORKS 29.5.1) found the position rule and the
        /// geometry rule on the SAME plane all nine times on the template as it
        /// comes, because every ReorderFeature that tried to put a plane or a
        /// sketch before the three base planes answered false: only a part
        /// TEMPLATE built with an extra plane first breaks the position rule.
        /// The plane is now the one with the canonical normal through the
        /// origin (References.BasePlane); <paramref name="reason"/> says why
        /// when there is none.
        /// </summary>
        private static IFeature ReferencePlane(IModelDoc2 model, string which, out string reason)
        {
            reason = "this document is not a part.";
            if (!(model is IPartDoc)) return null;
            return References.BasePlane(model, which, out reason);
        }

        /// <summary>I nomi dei piani su cui si puo' sezionare.</summary>
        public static IEnumerable<string> PlaneNames
        {
            get { return new[] { "front", "top", "right" }; }
        }

        /// <summary>
        /// Di quanto si stringe dopo l'adattamento automatico.
        ///
        /// **1,15, e il tetto lo detta la direzione GIA' PIENA.** Dopo
        /// l'adattamento il pezzo riempiva ~50% in larghezza ma gia' **78% in
        /// altezza**: il margine che sembra sprecato guardando la larghezza non
        /// c'e' in verticale. Provato 1,35 e misurato — la staffa a L e la
        /// piastra uscivano dal bordo di SOPRA e di SOTTO (scatola y 0-512 su
        /// 513), cioe' si guadagnava larghezza tagliando altezza.
        ///
        /// 0,78 x 1,15 = 0,90: resta un decimo di margine per l'antialiasing e
        /// per i pezzi che l'adattamento inquadra un po' piu' stretto.
        ///
        /// **Uno zoom uniforme non puo' fare di meglio**, perche' i due margini
        /// sono diversi e il fattore e' uno solo. Inquadrare bene per davvero
        /// vuol dire calcolare l'ingombro del pezzo PROIETTATO nella vista e
        /// adattare le due direzioni per conto loro: e' un'altra voce, non una
        /// costante da alzare.
        ///
        /// Si cambia solo rimisurando con `Checks\FramingTest`, che
        /// costa secondi.
        /// </summary>
        private const double HowMuchItNarrows = 1.15;

        private static void NarrowIfYouCan(IModelView view)
        {
            if (view == null) return;
            view.ZoomByFactor(HowMuchItNarrows);
        }

        /// <summary>
        /// Il fotogramma piu' grande che sta dentro le misure chieste E ha la
        /// stessa forma dell'area grafica.
        ///
        /// Se l'area grafica non si lascia leggere, si tengono le misure
        /// chieste: e' il comportamento di prima, che taglia — ma **tagliare e'
        /// meglio che non scattare**, e il caso non si e' mai visto.
        /// </summary>
        private static void FrameView(
            IModelView view, int width, int height, out int lw, out int lh)
        {
            lw = width;
            lh = height;
            try
            {
                if (view == null) return;
                double w = view.FrameWidth, h = view.FrameHeight;
                if (w <= 0 || h <= 0) return;

                double shape = w / h;
                // Si parte dalla larghezza piena e si ricava l'altezza; se
                // l'altezza sfora, si fa il contrario. Cosi' il risultato tocca
                // sempre uno dei due tetti e non e' mai piu' piccolo del
                // necessario.
                int testHeight = (int)Math.Round(width / shape);
                if (testHeight <= height && testHeight > 0)
                {
                    lw = width;
                    lh = testHeight;
                    return;
                }
                int testWidth = (int)Math.Round(height * shape);
                if (testWidth > 0)
                {
                    lw = testWidth;
                    lh = height;
                }
            }
            catch { }
        }

        /// <summary>
        /// Il BMP di SOLIDWORKS diventa un PNG. Null col motivo se non si puo'.
        ///
        /// Il file temporaneo si legge TUTTO in memoria prima di chiudere, e il
        /// `Bitmap` si costruisce da un `MemoryStream`: costruirlo dal file lo
        /// tiene aperto finche' il Bitmap vive, e la cancellazione nel
        /// `finally` fallirebbe lasciando spazzatura nella cartella temporanea
        /// a ogni scatto.
        /// </summary>
        /// <summary>
        /// Quanto sfondo si lascia attorno al pezzo, ritagliando.
        ///
        /// Il 4% del lato maggiore, e **mai meno di dodici pixel**. Il minimo
        /// non e' un vezzo: la prova `FramingTest` considera «tagliato»
        /// un pezzo che arriva a tre pixel dal bordo, e un margine piu' stretto
        /// della sua franchigia farebbe fallire la prova su un'immagine
        /// perfetta.
        /// </summary>
        private const double CutoutMargin = 0.04;
        private const int MinimumMargin = 12;

        /// <summary>
        /// Il fotogramma **ritagliato attorno al pezzo** (voce V6). Null quando
        /// non conviene o non si puo', e allora si tiene quello intero.
        ///
        /// ── PERCHE' SI RITAGLIA INVECE DI ZOOMARE ─────────────────────────
        ///
        /// Dopo l'adattamento, misurato, il pezzo riempiva il **58% in
        /// larghezza e l'89% in altezza**. Sembra un'inquadratura da stringere,
        /// e non lo e': **il verticale e' il vincolo**. `ViewZoomtofit2` scala
        /// finche' il pezzo ci sta in tutte e due le direzioni, e quella che
        /// tocca per prima finisce a ~90%; l'altra resta indietro di quanto la
        /// forma del pezzo differisce dalla forma del fotogramma.
        ///
        /// Uno zoom non puo' riparare questo, perche' agisce sulle due
        /// direzioni insieme: provato con 1,35 e il pezzo usciva dal bordo di
        /// sopra e di sotto per guadagnare larghezza. **Non si adatta il pezzo
        /// al fotogramma: si adatta il fotogramma al pezzo.**
        ///
        /// ── E NON PUO' TAGLIARE IL PEZZO, PER COSTRUZIONE ─────────────────
        ///
        /// Il ritaglio si fa sulla scatola dei pixel che NON sono sfondo, piu'
        /// un margine. Tagliare via un pezzo di pezzo vorrebbe dire che quei
        /// pixel non erano pezzo — cioe' che la soglia mente, ed e' la stessa
        /// soglia che `FramingTest` prova su un documento vuoto.
        ///
        /// Costa anche MENO: meno pixel, quindi meno token, per piu' pezzo.
        /// </summary>
        private static Bitmap AroundPart(Bitmap whole)
        {
            try
            {
                int width = whole.Width, height = whole.Height;
                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;

                // LockBits E UNA RIGA PER VOLTA, non GetPixel: su 1024x513 sono
                // mezzo milione di letture, e `GetPixel` le paga una chiamata
                // l'una.
                //
                // E `Marshal.Copy` invece di un blocco `unsafe`: accendere
                // `AllowUnsafeBlocks` cambia il modo in cui si compila **tutta
                // la libreria**, ed e' un impegno piu' grosso di quanto valga
                // un ritaglio. La copia per riga costa una allocazione sola e
                // la stessa velocita'.
                var data = whole.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    var line = new byte[Math.Abs(data.Stride)];
                    for (int y = 0; y < height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(
                            data.Scan0 + y * data.Stride, line, 0, line.Length);
                        for (int x = 0; x < width; x++)
                        {
                            // SFONDO = CHIARO SU TUTTI E TRE I CANALI.
                            // Il grigio del pezzo sta sui 128-150, la sfumatura
                            // di SOLIDWORKS non scende sotto ~215. Si guardano i
                            // canali e non la somma: un grigio medio e un
                            // azzurro chiaro possono avere la stessa somma.
                            int i = x * 3;
                            if (line[i] >= 200 && line[i + 1] >= 200 && line[i + 2] >= 200)
                                continue;
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            maxY = y;
                        }
                    }
                }
                finally { whole.UnlockBits(data); }

                if (maxX < 0) return null;   // fotogramma vuoto: si tiene intero

                int margin = Math.Max(
                    MinimumMargin,
                    (int)Math.Round(Math.Max(maxX - minX, maxY - minY) * CutoutMargin));

                int x0 = Math.Max(0, minX - margin);
                int y0 = Math.Max(0, minY - margin);
                int x1 = Math.Min(width - 1, maxX + margin);
                int y1 = Math.Min(height - 1, maxY + margin);
                int w = x1 - x0 + 1, h = y1 - y0 + 1;
                if (w <= 0 || h <= 0) return null;

                // SE NON GUADAGNA, NON SI FA. Ritagliare il 2% costa una copia
                // del fotogramma per non cambiare niente, e in cambio toglie
                // margine a chi guarda.
                if (w > width * 0.95 && h > height * 0.95) return null;

                return whole.Clone(new Rectangle(x0, y0, w, h), whole.PixelFormat);
            }
            catch { return null; }
        }

        private static byte[] InPng(string bmp, out string reason)
        {
            reason = null;
            try
            {
                byte[] rawItems = File.ReadAllBytes(bmp);
                using (var inside = new MemoryStream(rawItems))
                using (var whole = new Bitmap(inside))
                using (var outside = new MemoryStream())
                {
                    using (var narrow = AroundPart(whole))
                        (narrow ?? whole).Save(outside, ImageFormat.Png);
                    return outside.ToArray();
                }
            }
            catch (Exception ex)
            {
                reason = "L'immagine non si e' lasciata convertire: " + ex.Message;
                return null;
            }
        }
    }
}
