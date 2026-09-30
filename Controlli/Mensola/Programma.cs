using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.cosworks;

// =============================================================================
//  Mensola — la misura che dimensiona la sezione S (voce S3)
// =============================================================================
//
//  PERCHE' UNA MENSOLA E NON UN PEZZO VERO
//
//  Perche' di una mensola la risposta si sa **senza il calcolatore**. Un pezzo
//  vero darebbe un numero che non si puo' contraddire, e un numero che non si
//  puo' contraddire non e' una verifica: e' una cosa che si guarda e si crede.
//
//  Trave 200 x 20 x 10 mm, incastrata a un'estremita', 100 N in punta:
//
//      M = F * L   = 100 * 0,200          = 20 N*m
//      I = b*h^3/12 = 0,020 * 0,010^3 /12 = 1,6667e-9 m^4
//      c = h/2                             = 0,005 m
//      sigma = M*c/I = 20 * 0,005 / 1,6667e-9 = 6,0e7 Pa = 60 MPa
//
//  Sessanta esatti. Se l'API dice 60 +/- 10%, la catena regge e la sezione S
//  si puo' preventivare. Se dice altro, **la famiglia non si costruisce**
//  finche' non si sa perche' — e' scritto nella voce, ed e' il rischio piu'
//  grande del progetto: qui un difetto silenzioso non fa un pezzo sbagliato,
//  fa un calcolo sbagliato che qualcuno firma.
//
//  ── COSA SI SA GIA', E COSA NO ──────────────────────────────────────────────
//
//  La catena delle firme e' accertata per riflessione (voce S3, e 17 attese in
//  `Controlli\firme-api.ps1`). Quello che la riflessione **non** puo' dire e'
//  il formato di `Object DispArray` e `Object RefGeom` di `AddRestraint` e
//  `AddForce`: la firma non lo dichiara, e nessuna lettura lo scoprira'.
//  E' per quello che questa sonda esiste.
//
//  USO
//      .\Controlli\Mensola\bin\Mensola.exe
//      .\Controlli\Mensola\bin\Mensola.exe --tieni     lascia il pezzo aperto
// =============================================================================

internal static class Programma
{
    // Il CLSID dell'add-in Simulation, da `HKLM\SOFTWARE\Classes\SldWorks.Simulation\CLSID`.
    private const string ClsidSimulation = "{1CC93C1D-B0C9-4641-A2AD-D19A5700052E}";

    // swsStressComponent_e / swsStrengthUnit_e / swsAnalysisStudyType_e /
    // swsRestraintType_e / swsForceType_e / swsLinearUnit_e — misurati per
    // riflessione, non ricordati.
    private const int VonMises = 9;
    private const int NewtonSuMm2 = 3;
    private const int StudioStatico = 0;
    private const int IncastroFisso = 0;
    private const int ForzaOMomento = 0;
    private const int Millimetri = 0;
    private const int NewtonEMetri = 0;   // swsForceUnit_e.swsForceUnitNOrNm

    private const double LunghezzaMm = 200.0;
    private const double LarghezzaMm = 20.0;
    private const double AltezzaMm = 10.0;
    private const double ForzaN = 100.0;

    /// <summary>Il nome esatto nell'archivio DIN, parentesi comprese.</summary>
    private const string NomeMateriale = "1.0037 (S235JR)";

    private static int _passo;

    private static void Dice(string cosa)
    {
        _passo++;
        Console.WriteLine("  " + _passo.ToString("00") + ". " + cosa);
    }

    private static int Main(string[] argomenti)
    {
        bool tieni = Array.IndexOf(argomenti, "--tieni") >= 0;

        double atteso = SigmaAttesa();
        Console.WriteLine();
        Console.WriteLine("MENSOLA — la misura che dimensiona la sezione S (voce S3)");
        Console.WriteLine("  trave " + LunghezzaMm + " x " + LarghezzaMm + " x " + AltezzaMm +
                          " mm, incastrata, " + ForzaN + " N in punta");
        Console.WriteLine("  sigma = M*c/I = " + atteso.ToString("0.0") + " MPa   <- calcolata a mano");
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex)
        {
            Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message);
            Console.WriteLine("Aprilo e rilancia.");
            return 2;
        }
        Dice("agganciato a SOLIDWORKS " + sw.RevisionNumber());

        // ── L'ADD-IN, PRIMA DI TUTTO IL RESTO ────────────────────────────
        //
        // Se non e' caricato si esce SUBITO, con un messaggio che dice cosa
        // fare. Scoprirlo dopo aver creato il pezzo vuol dire lasciare in giro
        // un documento per niente.
        object addin = sw.GetAddInObject(ClsidSimulation);
        if (addin == null)
        {
            Console.WriteLine();
            Console.WriteLine("SIMULATION NON E' CARICATO.");
            Console.WriteLine("  Non vuol dire che non c'e': vuol dire che la sua casella nella");
            Console.WriteLine("  finestra Strumenti > Aggiunte non e' spuntata in questa sessione.");
            Console.WriteLine("  Spuntala (anche la colonna «Avvio», se lo vuoi sempre) e rilancia.");
            return 3;
        }
        Dice("Simulation caricato");

        IModelDoc2 doc = null;
        try
        {
            doc = CreaLaTrave(sw);
            if (doc == null) return 4;

            // ── IL MATERIALE PRIMA DELLO STUDIO, E NON DOPO ──────────────
            //
            // Questo blocco stava DOPO `CreateNewStudy3`, ed era sbagliato
            // di ordine. Il materiale veniva applicato, `GetMaterialPropertyName2`
            // lo rileggeva, tutto sembrava a posto — e `RunAnalysis`
            // rispondeva **19 = swsRunAnalysisErrorMaterialNotDefined**.
            //
            // Lo studio fotografa il pezzo quando nasce. Nato su un pezzo
            // senza materiale, senza materiale resta, e cio' che si attacca
            // al documento dopo non lo raggiunge.
            //
            // *La rilettura diceva la verita': il materiale c'era davvero. Ma
            // rispondeva a una domanda diversa da quella che contava — «ce
            // l'ha il pezzo?» invece di «ce l'ha lo studio?».*
            // ── IL MATERIALE SI METTE SULLA PARTE, E SI RILEGGE ──────────
            //
            // Il primo tentativo passava da Simulation:
            // `SolidManager -> componente -> corpo -> SetLibraryMaterial("", nome)`.
            // Ha risposto **0**, che sembra riuscito, e poi `RunAnalysis` ha
            // dato **19 = swsRunAnalysisErrorMaterialNotDefined**: il
            // materiale non c'era. Archivio vuoto non vuol dire «quello
            // predefinito», e lo zero di ritorno non era una conferma.
            //
            // *Un valore di ritorno che dice zero mentre non ha fatto niente
            // e' esattamente il difetto che questo progetto insegue* — qui
            // in casa d'altri, e si e' visto solo perche' il passo dopo ha
            // avuto il coraggio di lamentarsi.
            //
            // Adesso si applica con l'API di SOLIDWORKS, col percorso pieno
            // dell'archivio, **e si rilegge**: `SetMaterialPropertyName2`
            // torna void, quindi l'unico modo di sapere se ha funzionato e'
            // chiedere al pezzo cos'ha addosso.
            var parte = doc as IPartDoc;
            string archivio = ArchivioDin();
            if (archivio == null) { Console.WriteLine("  non trovo l'archivio DIN"); return 5; }
            parte.SetMaterialPropertyName2("", archivio, NomeMateriale);

            string db2;
            string messo = parte.GetMaterialPropertyName2("", out db2);
            if (string.IsNullOrEmpty(messo))
            {
                Console.WriteLine("  il materiale NON si e' attaccato: rileggendo trovo il vuoto.");
                Console.WriteLine("    archivio: " + archivio);
                Console.WriteLine("    nome:     " + NomeMateriale);
                return 5;
            }
            Dice("materiale «" + messo + "» (riletto, non assunto)");

            // ── I TIPI VERI, E NON LE CHIAMATE PER NOME ──────────────────
            //
            // Il primo abbozzo di questa sonda chiamava tutto per nome con
            // `InvokeMember`, per poter dire QUALE nome mancava invece di non
            // compilare. `CreateNewStudy3` l'ha rifiutata:
            // **DISP_E_TYPEMISMATCH**, perche' il late-binding non sa
            // marshalare un `out Int32`. Con l'interop referenziato il
            // problema non esiste, e il compilatore controlla le firme —
            // che e' meglio di un messaggio a runtime.
            var richiamo = addin as CwAddincallback;
            if (richiamo == null)
            {
                Console.WriteLine("  l'add-in non e' un CwAddincallback: " + addin.GetType().FullName);
                return 5;
            }
            CosmosWorks cw = richiamo.CosmosWorks;
            if (cw == null) { Console.WriteLine("  .CosmosWorks e' null"); return 5; }
            CWModelDoc attivo = cw.ActiveDoc;
            if (attivo == null) { Console.WriteLine("  .ActiveDoc e' null: Simulation non vede il pezzo"); return 5; }
            CWStudyManager gestore = attivo.StudyManager;
            if (gestore == null) { Console.WriteLine("  .StudyManager e' null"); return 5; }
            Dice("Simulation vede il documento");

            // ── 1. LO STUDIO ─────────────────────────────────────────────
            int errStudio;
            CWStudy studio = gestore.CreateNewStudy3("mensola-s3", StudioStatico, 0, out errStudio);
            if (studio == null)
            {
                Console.WriteLine("  CreateNewStudy3 -> null, errore " + errStudio);
                return 5;
            }
            Dice("studio statico creato (errore " + errStudio + ")");

            // ── 2. IL MATERIALE, CHE STA SUL CORPO ───────────────────────
            //
            // Tre salti: studio -> SolidManager -> componente -> corpo. Il
            // materiale NON sta sullo studio, ed e' una delle cose che il
            // censimento per riflessione aveva gia' corretto.

            // ── 3. VINCOLO E CARICO: QUI STA L'INCOGNITA ─────────────────
            //
            // `AddRestraint(Int32, Object DispArray, Object RefGeom, out Int32)`
            // — la firma NON dice cosa mettere in `DispArray`. La riflessione
            // e' arrivata fin qui e si e' fermata, ed e' il motivo per cui
            // questa sonda esiste.
            //
            // L'ipotesi che si prova per prima: un `object[]` di `IFace2`,
            // che e' la forma che tutta l'API di SOLIDWORKS usa per «un
            // insieme di entita'».
            CWLoadsAndRestraintsManager gestoreCarichi = studio.LoadsAndRestraintsManager;
            if (gestoreCarichi == null) { Console.WriteLine("  LoadsAndRestraintsManager e' null"); return 5; }

            IFace2 facciaIncastro, facciaCarico;
            if (!DueFacceInPunta(doc, out facciaIncastro, out facciaCarico)) return 4;
            Dice("trovate le due facce di testa");

            int errV;
            CWRestraint vincolo = gestoreCarichi.AddRestraint(
                IncastroFisso, new object[] { facciaIncastro }, null, out errV);
            Console.WriteLine("      AddRestraint -> " + (vincolo == null ? "null" : "oggetto") +
                              ", errore " + errV);
            if (vincolo == null)
            {
                Console.WriteLine();
                Console.WriteLine("  IL FORMATO DI DispArray NON E' UN object[] DI IFace2.");
                Console.WriteLine("  E' l'incognita che questa sonda doveva togliere, e non l'ha tolta.");
                Console.WriteLine("  Prossima ipotesi da provare: selezionare la faccia nel modello e");
                Console.WriteLine("  passare null, oppure un array di Dispatch presi da SelectionMgr.");
                return 6;
            }
            Dice("incastro applicato");

            // La forza: verso -Y, in punta.
            // ── LA FORZA VUOLE UNA DIREZIONE, L'INCASTRO NO ──────────────
            //
            // `AddRestraint` ha accettato `object[] { IFace2 }` con `RefGeom`
            // a null: per un incastro «fisso» non c'e' nessuna direzione da
            // dare. `AddForce` con la stessa forma ha risposto **errore 2**,
            // e il motivo e' quello: una forza senza direzione non e' una
            // forza.
            //
            // Il riferimento e' il PIANO SUPERIORE, lo stesso su cui la trave
            // e' stata disegnata: la sua normale e' l'asse Y, cioe' la
            // direzione in cui la mensola flette. Prenderne un altro
            // darebbe un carico assiale, che su questa trave non produce
            // flessione e farebbe leggere un numero piccolo e plausibile —
            // *il modo peggiore di sbagliare, perche' non somiglia a un
            // errore.*
            object pianoRif = PianoDiRiferimento(doc, 2);
            if (pianoRif == null) { Console.WriteLine("  non trovo il piano di riferimento"); return 6; }

            int errF;
            CWForce forza = gestoreCarichi.AddForce(
                ForzaOMomento, new object[] { facciaCarico }, pianoRif, out errF);
            Console.WriteLine("      AddForce -> " + (forza == null ? "null" : "oggetto") +
                              ", errore " + errF);
            if (forza == null) { Console.WriteLine("  AddForce non ha accettato nemmeno col piano."); return 6; }

            // IL VALORE SI METTE DOPO, sull'oggetto tornato: `AddForce` a
            // quattro argomenti non ha nessun posto dove metterlo. Accertato
            // leggendo `ICWForce`, che ha `SetForceComponentValues2` e la
            // coppia `ForceBeginEdit` / `ForceEndEdit`.
            forza.ForceBeginEdit();
            forza.Unit = NewtonEMetri;
            //                    X      Y     Z        valori
            forza.SetForceComponentValues2(false, true, false, 0.0, ForzaN, 0.0);
            int esitoForza = forza.ForceEndEdit();
            Dice("carico di " + ForzaN + " N lungo Y (esito " + esitoForza + ")");
            if (esitoForza != 0)
            {
                Console.WriteLine("  ForceEndEdit non e' tornato 0: il carico non e' stato accettato.");
                return 6;
            }

            // ── 4. MESH ED ESECUZIONE ────────────────────────────────────
            //
            // `RunAnalysis()` e NON `MeshAndRun()`: il secondo torna **void**,
            // e non puo' dire se e' andata. Accertato per riflessione, ed e'
            // la stessa regola che questo progetto ha gia' pagato altrove —
            // *il valore che torna da chi ha fatto la cosa non e' la misura
            // della cosa*, ma qui non torna nemmeno un valore.
            // ── LA CONVERGENZA, E NON UNA MISURA SOLA ────────────────────
            //
            // Il primo giro ha dato **30,33 MPa** contro 60 attesi: la meta'
            // esatta. Con una mesh da 3,4 mm su uno spessore di 10 ci sono
            // tre elementi nello spessore, e tre elementi non sanno
            // descrivere una flessione: la tensione di picco esce bassa.
            //
            // *Un numero solo non prova niente, e questo vale in tutt'e due
            // i versi: se avesse dato 60 al primo colpo non avrei saputo se
            // era giusto o fortunato.* Si infittisce e si guarda DOVE VA.
            double[] lati = { 4.0, 2.0, 1.0, 0.6 };
            double ultimo = double.NaN;

            Console.WriteLine();
            Console.WriteLine("  lato mesh    sigma max    scarto da " + atteso.ToString("0.0") + " MPa");
            Console.WriteLine("  ---------    ---------    -------------------");

            foreach (double lato in lati)
            {
                int eMesh = studio.CreateMesh(Millimetri, lato, lato / 20.0);
                if (eMesh != 0)
                {
                    Console.WriteLine("  " + lato.ToString("0.0").PadLeft(7) + " mm    mesh fallita (esito " + eMesh + ")");
                    continue;
                }
                int eGiro = studio.RunAnalysis();
                if (eGiro != 0)
                {
                    Console.WriteLine("  " + lato.ToString("0.0").PadLeft(7) + " mm    giro fallito (esito " + eGiro + ")");
                    continue;
                }

                int errS;
                object letto = studio.Results.GetMinMaxStress(
                    VonMises, 0, 0, null, NewtonSuMm2, out errS);
                double sigma = MassimoVero(letto);
                if (double.IsNaN(sigma))
                {
                    Console.WriteLine("  " + lato.ToString("0.0").PadLeft(7) + " mm    non si legge (errore " + errS + ")");
                    continue;
                }
                ultimo = sigma;
                double sc = (sigma - atteso) / atteso * 100.0;
                Console.WriteLine("  " + lato.ToString("0.0").PadLeft(7) + " mm  " +
                                  sigma.ToString("0.00").PadLeft(9) + "    " +
                                  (sc >= 0 ? "+" : "") + sc.ToString("0.0") + " %");
            }

            // ── IL VERDETTO, SULL'ULTIMA E PIU' FITTA ────────────────────
            if (double.IsNaN(ultimo))
            {
                Console.WriteLine();
                Console.WriteLine("Nessuna mesh e' arrivata in fondo: non c'e' un numero da giudicare.");
                return 7;
            }

            double scarto = Math.Abs(ultimo - atteso) / atteso * 100.0;
            Console.WriteLine();
            Console.WriteLine("  a mano:            " + atteso.ToString("0.0") + " MPa");
            Console.WriteLine("  l'API, mesh fitta: " + ultimo.ToString("0.0") + " MPa");
            Console.WriteLine("  scarto:            " + scarto.ToString("0.0") + " %");
            Console.WriteLine();
            if (scarto <= 10.0)
            {
                Console.WriteLine("ENTRO IL 10%. La catena regge e la sezione S si puo' preventivare.");
                Console.WriteLine("E la colonna degli scarti dice ANCHE come ci si arriva: e' quella");
                Console.WriteLine("che rende il numero una misura invece di una coincidenza.");
                return 0;
            }
            Console.WriteLine("FUORI DAL 10%. La voce S3 dice che qui la famiglia NON si costruisce");
            Console.WriteLine("finche' non si sa perche'. Guarda la colonna degli scarti: se scende");
            Console.WriteLine("verso zero, serve solo una mesh piu' fitta. Se si ferma altrove, il");
            Console.WriteLine("problema non e' la mesh ed e' molto piu' interessante.");
            return 1;
        }
        finally
        {
            if (doc != null && !tieni)
            {
                try { sw.CloseDoc(doc.GetTitle()); } catch { }
            }
        }
    }

    /// <summary>
    /// La tensione massima dall'array di `GetMinMaxStress`.
    ///
    /// LA FORMA E' `[nodoMin, valoreMin, nodoMax, valoreMax]`, e la prima
    /// scrittura di questa sonda prendeva il **massimo dei quattro numeri**.
    /// Cosi' beccava `11875`, che e' il NUMERO DEL NODO, e lo stampava come
    /// MPa: 11.875 MPa contro 60 attesi, scarto del 19.691%.
    ///
    /// *E' andata bene che fosse assurdo.* Se il nodo di picco avesse avuto
    /// un numero basso — diciamo 58 — avrei letto «58 MPa contro 60 attesi,
    /// scarto 3%» e avrei scritto che la catena regge. Un numero sbagliato e
    /// plausibile non si distingue da una misura.
    /// </summary>
    private static double MassimoVero(object letto)
    {
        var a = letto as Array;
        if (a == null || a.Length < 4) return double.NaN;
        return Convert.ToDouble(a.GetValue(3));
    }

    private static double SigmaAttesa()
    {
        double L = LunghezzaMm / 1000.0, b = LarghezzaMm / 1000.0, h = AltezzaMm / 1000.0;
        double M = ForzaN * L;
        double I = b * h * h * h / 12.0;
        return M * (h / 2.0) / I / 1e6;   // Pa -> MPa
    }

    /// <summary>
    /// L'archivio DIN, cercato sul disco invece che scritto a mano.
    ///
    /// Sta sotto `lang\<lingua>\sldmaterials`, e la lingua cambia da
    /// installazione a installazione: qui ce ne sono due, italiano e
    /// inglese. Un percorso costante funzionerebbe su questa macchina e su
    /// nessun'altra.
    /// </summary>
    private static string ArchivioDin()
    {
        string radice = @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\lang";
        var suo = System.Environment.GetEnvironmentVariable("SW_INSTALL_PATH");
        if (!string.IsNullOrEmpty(suo)) radice = System.IO.Path.Combine(suo, "lang");
        if (!System.IO.Directory.Exists(radice)) return null;
        foreach (var lingua in System.IO.Directory.GetDirectories(radice))
        {
            var p = System.IO.Path.Combine(lingua, "sldmaterials", "SolidWorks DIN Materials.sldmat");
            if (System.IO.File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// Il piano di riferimento, preso come OGGETTO e non come nome.
    ///
    /// `AddForce` vuole la geometria, non la stringa: si seleziona per nome
    /// e si ripesca l'oggetto dal gestore della selezione. E si deseleziona
    /// subito: una selezione lasciata in giro cambia cosa fa la chiamata
    /// successiva, ed e' il genere di legame che non si vede.
    /// </summary>
    // W3.1-bis.2 point 10: `which` is now the tree position of the base plane
    // (1 = front, 2 = top, 3 = right), not its localized name — see
    // SelectBasePlane below.
    private static object PianoDiRiferimento(IModelDoc2 doc, int which)
    {
        if (!SelectBasePlane(doc, which))
        {
            Console.WriteLine("      could not select base plane #" + which);
            return null;
        }
        var sel = doc.SelectionManager as ISelectionMgr;
        object piano = sel == null ? null : sel.GetSelectedObject6(1, -1);
        doc.ClearSelection2(true);
        return piano;
    }

    /// <summary>La trave, disegnata e estrusa. Null se non e' venuta.</summary>
    private static IModelDoc2 CreaLaTrave(ISldWorks sw)
    {
        string modello = sw.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        var doc = (IModelDoc2)sw.NewDocument(modello, 0, 0, 0);
        if (doc == null) { Console.WriteLine("  NewDocument -> null"); return null; }

        SelectBasePlane(doc, 2);
        doc.SketchManager.InsertSketch(true);
        doc.SketchManager.CreateCornerRectangle(
            0, 0, 0, LunghezzaMm / 1000.0, LarghezzaMm / 1000.0, 0);
        doc.SketchManager.InsertSketch(true);

        var f = doc.FeatureManager.FeatureExtrusion3(
            true, false, false, 0, 0, AltezzaMm / 1000.0, 0.0,
            false, false, false, false, 0, 0, false, false, false, false,
            true, true, true, 0, 0, false);
        if (f == null) { Console.WriteLine("  l'estrusione non e' venuta"); return null; }
        doc.ViewZoomtofit2();
        Dice("trave creata: " + LunghezzaMm + " x " + LarghezzaMm + " x " + AltezzaMm + " mm");
        return doc;
    }

    /// <summary>
    /// Le due facce di testa: quella a x=0 (incastro) e quella a x=L (carico).
    ///
    /// Si scelgono per POSIZIONE e non per indice: l'ordine delle facce non e'
    /// promesso da nessuna parte, e prenderle per indice vorrebbe dire che la
    /// prova cambia significato se SOLIDWORKS le rinumera.
    /// </summary>
    private static bool DueFacceInPunta(IModelDoc2 doc, out IFace2 incastro, out IFace2 carico)
    {
        incastro = null; carico = null;
        var parte = doc as IPartDoc;
        var corpi = parte == null ? null : parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (corpi == null || corpi.Length == 0) { Console.WriteLine("  nessun corpo solido"); return false; }

        double L = LunghezzaMm / 1000.0;
        var faccia = (corpi[0] as IBody2).GetFirstFace() as IFace2;
        while (faccia != null)
        {
            var scatola = faccia.GetBox() as double[];
            if (scatola != null && scatola.Length >= 6)
            {
                bool sottile = Math.Abs(scatola[3] - scatola[0]) < 1e-6;   // spessore nullo in X
                if (sottile && Math.Abs(scatola[0]) < 1e-6) incastro = faccia;
                if (sottile && Math.Abs(scatola[0] - L) < 1e-6) carico = faccia;
            }
            faccia = faccia.GetNextFace() as IFace2;
        }
        if (incastro == null || carico == null)
        {
            Console.WriteLine("  non trovo le due facce di testa (x=0 e x=" + L + ")");
            return false;
        }
        return true;
    }

    // ── Le chiamate tardive ─────────────────────────────────────────────
    //
    // Gli oggetti di Simulation arrivano come `__ComObject`: senza l'interop
    // `cosworks` referenziato non hanno un tipo, e si chiamano per nome.
    // Referenziarlo si puo' — ma cosi' la sonda dice chiaramente QUALE nome
    // sta cercando quando non lo trova, invece di non compilare.

    private static object Prendi(object o, string nome)
    {
        if (o == null) return null;
        try
        {
            return o.GetType().InvokeMember(
                nome, System.Reflection.BindingFlags.GetProperty, null, o, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine("      ." + nome + " alza: " + Radice(ex).Message);
            return null;
        }
    }

    private static object Chiama(object o, string nome, object[] arg)
    {
        if (o == null) return null;
        try
        {
            return o.GetType().InvokeMember(
                nome, System.Reflection.BindingFlags.InvokeMethod, null, o, arg);
        }
        catch (Exception ex)
        {
            Console.WriteLine("      ." + nome + "(...) alza: " + Radice(ex).Message);
            return null;
        }
    }

    private static Exception Radice(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex;
    }

    // W3.1-bis.2 point 10: base planes by position, not by their localized name.
    // The first three RefPlane features of the tree, same rule as
    // Controlli/Nervatura, Controlli/Riempimento and Controlli/TipoDellEstrusione.
    // Since Z-C21.5 the library itself (Riscontro.Disegno/Riferimenti.cs,
    // BasePlane) recognises a base plane by its canonical normal through the
    // world origin instead, because a template with an extra plane ahead of the
    // three defaults fools a position-based rule with no error; this probe
    // stays positional because it is a throwaway part on SOLIDWORKS' own
    // default template and cannot reference the library (single-file compile,
    // Controlli/costruisci-controlli.ps1).
    private static bool SelectBasePlane(IModelDoc2 doc, int which)
    {
        int seen = 0;
        var f = doc.FirstFeature() as IFeature;
        while (f != null)
        {
            if (f.GetTypeName2() == "RefPlane" && ++seen == which) { doc.ClearSelection2(true); return f.Select2(false, 0); }
            f = f.GetNextFeature() as IFeature;
        }
        return false;
    }
}
