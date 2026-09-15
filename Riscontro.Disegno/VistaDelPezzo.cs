using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
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
    /// 16.000 CARATTERI. Se ogni `guarda_il_pezzo` portasse tre viste, il costo
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
    public static class VistaDelPezzo
    {
        /// <summary>
        /// Le viste che si possono chiedere, col nome che usa chi disegna.
        ///
        /// L'isometrica e' la prima perche' e' l'unica che, da sola, dice una
        /// forma: le tre standard dicono tre sagome e lasciano indovinare il
        /// resto.
        /// </summary>
        private static readonly Dictionary<string, int> Viste =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["isometrica"] = (int)swStandardViews_e.swIsometricView,
                ["trimetrica"] = (int)swStandardViews_e.swTrimetricView,
                ["dimetrica"] = (int)swStandardViews_e.swDimetricView,
                ["frontale"] = (int)swStandardViews_e.swFrontView,
                ["posteriore"] = (int)swStandardViews_e.swBackView,
                ["sinistra"] = (int)swStandardViews_e.swLeftView,
                ["destra"] = (int)swStandardViews_e.swRightView,
                ["superiore"] = (int)swStandardViews_e.swTopView,
                ["inferiore"] = (int)swStandardViews_e.swBottomView,
            };

        public static IEnumerable<string> NomiDelleViste { get { return Viste.Keys; } }

        /// <summary>Una vista esiste con questo nome?</summary>
        public static bool Conosciuta(string nome)
        {
            return !string.IsNullOrWhiteSpace(nome) && Viste.ContainsKey(nome);
        }

        /// <summary>
        /// Fotografa il documento e restituisce i byte di un PNG.
        /// Null col motivo se non si e' potuto.
        ///
        /// <param name="quale">
        /// Il nome di una vista, oppure null per fotografare l'orientamento
        /// che c'e' adesso — che e' l'unico modo di NON toccare niente.
        /// </param>
        /// </summary>
        public static byte[] Scatta(
            IModelDoc2 modello, string quale, int larghezza, int altezza, out string motivo)
        {
            motivo = null;
            if (modello == null) { motivo = "Nessun documento aperto."; return null; }

            if (quale != null && !Conosciuta(quale))
            {
                motivo = "Non conosco la vista «" + quale + "». Ci sono: " +
                         string.Join(", ", NomiDelleViste) + ".";
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
            MathTransform orientamentoPrima = null;
            MathVector traslazionePrima = null;
            double scalaPrima = 0;
            IModelView vista = null;
            try
            {
                vista = modello.ActiveView as IModelView;
                if (vista != null)
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
                    var o = vista.Orientation3 as IMathTransform;
                    var rovescio = o == null ? null : o.IInverse() as IMathTransform;
                    orientamentoPrima = rovescio == null ? null : rovescio.IInverse();

                    var t = vista.Translation3 as IMathVector;
                    traslazionePrima = t == null ? null : t.IScale(1.0);

                    scalaPrima = vista.Scale2;
                }
            }
            catch { }

            string temporaneo = null;
            try
            {
                if (quale != null)
                {
                    int codice = Viste[quale];

                    // `ShowNamedView2` NON RISPONDE: torna `void`. Non c'e'
                    // nessun modo di chiederle se ha girato il pezzo, e la
                    // prima scrittura di questo metodo la trattava come se
                    // tornasse un `bool` — un controllo che non poteva
                    // scattare, scritto per un valore che non esiste.
                    //
                    // L'unica prova che resta e' il fotogramma: se
                    // l'orientamento non e' cambiato, la fotografia esce
                    // lo stesso, solo da un'altra parte. Il nome della vista
                    // e' gia' validato qui sopra contro `Viste`, quindi il
                    // caso «nome sbagliato» e' escluso prima di arrivarci.
                    modello.ShowNamedView2("", codice);

                    // LO ZOOM VA DOPO IL RIDISEGNO, E SI RIDISEGNA ANCORA.
                    // Misurato nel banco: al primo giro il pezzo usciva tagliato
                    // sul bordo destro, perche' l'adattamento lavorava sulla
                    // vista vecchia.
                    modello.GraphicsRedraw2();
                    modello.ViewZoomtofit2();
                    modello.GraphicsRedraw2();

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
                    try { StringiSePuoi(vista); modello.GraphicsRedraw2(); } catch { }
                }

                // `SaveBMP` vuole un percorso: non sa restituire byte.
                temporaneo = Path.Combine(
                    Path.GetTempPath(), "oce-vista-" + Guid.NewGuid().ToString("N") + ".bmp");

                // ── IL FOTOGRAMMA PRENDE LA FORMA DELL'AREA GRAFICA ─────
                //
                // **Il difetto (voce V5).** `ViewZoomtofit2` inquadra dentro
                // l'area grafica di SOLIDWORKS; `SaveBMP` rende poi nel
                // fotogramma che gli si chiede. Se le due forme non
                // coincidono, quello che avanza **viene tagliato via**, e non
                // lo dice nessuno.
                //
                // Misurato il 15/9/2026 da `Controlli\ProvaInquadratura`:
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
                int lw = larghezza, lh = altezza;
                Inquadra(vista, larghezza, altezza, out lw, out lh);

                if (!modello.SaveBMP(temporaneo, lw, lh) || !File.Exists(temporaneo))
                {
                    motivo = "SOLIDWORKS non ha scritto l'immagine.";
                    return null;
                }

                return InPng(temporaneo, out motivo);
            }
            catch (Exception ex)
            {
                motivo = "Non sono riuscito a fotografare il pezzo: " + ex.Message;
                return null;
            }
            finally
            {
                // ── TUTTI E TRE, E L'ORDINE E' ORIENTAMENTO, SCALA, POSIZIONE
                //
                // **L'ordine non e' uno stile: e' la differenza fra rimettere e
                // non rimettere.** Misurato da `Controlli\ProvaVista`, che prova
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
                if (vista != null)
                {
                    try { if (orientamentoPrima != null) vista.Orientation3 = orientamentoPrima; }
                    catch { }
                    try { if (scalaPrima > 0) vista.Scale2 = scalaPrima; }
                    catch { }
                    try { if (traslazionePrima != null) vista.Translation3 = traslazionePrima; }
                    catch { }
                    try { modello.GraphicsRedraw2(); } catch { }
                }
                if (temporaneo != null)
                {
                    try { if (File.Exists(temporaneo)) File.Delete(temporaneo); } catch { }
                }
            }
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
        /// Si cambia solo rimisurando con `Controlli\ProvaInquadratura`, che
        /// costa secondi.
        /// </summary>
        private const double QuantoSiStringe = 1.15;

        private static void StringiSePuoi(IModelView vista)
        {
            if (vista == null) return;
            vista.ZoomByFactor(QuantoSiStringe);
        }

        /// <summary>
        /// Il fotogramma piu' grande che sta dentro le misure chieste E ha la
        /// stessa forma dell'area grafica.
        ///
        /// Se l'area grafica non si lascia leggere, si tengono le misure
        /// chieste: e' il comportamento di prima, che taglia — ma **tagliare e'
        /// meglio che non scattare**, e il caso non si e' mai visto.
        /// </summary>
        private static void Inquadra(
            IModelView vista, int larghezza, int altezza, out int lw, out int lh)
        {
            lw = larghezza;
            lh = altezza;
            try
            {
                if (vista == null) return;
                double w = vista.FrameWidth, h = vista.FrameHeight;
                if (w <= 0 || h <= 0) return;

                double forma = w / h;
                // Si parte dalla larghezza piena e si ricava l'altezza; se
                // l'altezza sfora, si fa il contrario. Cosi' il risultato tocca
                // sempre uno dei due tetti e non e' mai piu' piccolo del
                // necessario.
                int provaAltezza = (int)Math.Round(larghezza / forma);
                if (provaAltezza <= altezza && provaAltezza > 0)
                {
                    lw = larghezza;
                    lh = provaAltezza;
                    return;
                }
                int provaLarghezza = (int)Math.Round(altezza * forma);
                if (provaLarghezza > 0)
                {
                    lw = provaLarghezza;
                    lh = altezza;
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
        private static byte[] InPng(string bmp, out string motivo)
        {
            motivo = null;
            try
            {
                byte[] grezzi = File.ReadAllBytes(bmp);
                using (var dentro = new MemoryStream(grezzi))
                using (var immagine = Image.FromStream(dentro))
                using (var fuori = new MemoryStream())
                {
                    immagine.Save(fuori, ImageFormat.Png);
                    return fuori.ToArray();
                }
            }
            catch (Exception ex)
            {
                motivo = "L'immagine non si e' lasciata convertire: " + ex.Message;
                return null;
            }
        }
    }
}
