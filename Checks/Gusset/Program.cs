using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  probe_fazzoletto — voce GP20: "il fazzoletto non nasce"
// =============================================================================
//
//  COSA MISURA QUESTA SONDA E PERCHE'
//
//  Il catalogo (Oce.Solid.Agent.Core/Catalog/Operations.cs, voce "gusset") e
//  il TODO (voce GP20) elencano 16 varianti provate alla cieca, TUTTE null:
//
//    marca: nessuna, 1/1, 1/2, ordine invertito         (6)
//    spessore: interno, esterno, da tutte e due le parti (3)
//    profilo: triangolare, poligonale                    (\
//    profilo: lati (BUseLenDim=true), angolo (=false)     ) 4 (incrociate a coppie)
//    posizione: inizio, centro                            /  (2)
//                                                    1 base + 15 = 16
//
//  NESSUNA di queste 16 varianti compare come "il valore del terzo parametro
//  del profilo" (ProfileAngle quando BUseLenDim=false, ProfileD4 quando e'
//  true). La guida API (Insert_Gusset_Feature_Example_CSharp.htm e
//  Insert_Weldment_Features_Example_CSharp.htm, IFeatureManager~
//  InsertGussetFeature3) mostra SEMPRE quel terzo valore diverso da zero
//  (45 gradi, oppure 15 mm) nei DUE unici esempi funzionanti conosciuti. Il
//  codice spedito nel ponte (WeldmentOperations.cs, Fazzoletto) passa
//  ProfileD3=0, ProfileAngle=0, ProfileD4=0 SEMPRE: un profilo con angolo o
//  lato zero e' degenere (si chiude su se stesso), e SOLIDWORKS lo rifiuta
//  in silenzio — lo stesso difetto "null senza errore su geometria non
//  valida" gia' pagato altrove in questo progetto (vedi memoria
//  api-sw-array-tipizzati).
//
//  CORREZIONE DELLA REVISIONE (28/9/2026), letta nel codice storico
//  (prova0089, Gusset(), variante "polygonal", mai entrata nel repo): un
//  TRIANGOLO non degenere (BIsProfile=false, D1=D2=60) e' GIA' stato provato,
//  ma SENZA marca, e ha dato null; la marca 1/1 e' stata provata solo col
//  poligono degenere (Fazzoletto(), "marca1"). La combinazione mai provata e'
//  quindi profilo valido + marca 1 su entrambe le facce: e' la variante 1.
//
//  Questa e' l'IPOTESI 1, la piu' probabile. Le altre due sonde isolano le
//  variabili vicine, per non confondere "il profilo era degenere" con "la
//  marca era sbagliata" o "il meccanismo di selezione (marca) e' diverso
//  dall'array esplicito":
//
//    variante 0 — CONTROLLO: la ricetta esatta spedita nel ponte, sulla
//                 STESSA T di prova di questa sonda. Deve fallire (null) come
//                 sempre: se qui nascesse, vorrebbe dire che il difetto non
//                 e' nei parametri ma nella T di prova del ponte.
//    variante 1 — RICETTA DELLA GUIDA: InsertGussetFeature3, profilo
//                 TRIANGOLARE (BIsProfile=false), angolo 45 gradi
//                 (BUseLenDim=false), marca 1 su ENTRAMBE le facce (la
//                 pagina di riferimento lo dice in Remarks: "select two
//                 faces ... with a Mark of 1").
//    variante 2 — IPOTESI "il profilo degenere e' la sola causa": stessa
//                 forma POLIGONALE (BIsProfile=true) che il ponte intendeva,
//                 ma con ProfileD3/ProfileD4 REALI (15 mm, non zero).
//    variante 3 — IPOTESI "bastava la marca": la ricetta ESATTA del ponte
//                 (profilo degenere incluso) ma con marca 1/1 vera invece di
//                 nessuna marca — per isolare la marca da sola.
//    variante 4 — IPOTESI "e' l'array, non la selezione": stessa ricetta
//                 della variante 1 ma passata come Face2[] esplicito a
//                 InsertGussetFeature2 (nessuna selezione, nessuna marca:
//                 la pagina di riferimento di InsertGussetFeature2 non la
//                 richiede).
//
//  Ogni variante costruisce una T FRESCA (due membri strutturali 80x80x5,
//  600 + 300 mm, tagliati) cosi' un tentativo non lascia residui che
//  confondano il successivo, e chiude il pezzo senza salvare.
//
//  T DI PROVA — stessa di Bench/cases/weldments/weld_bead_goes_between_touching_faces.json:
//    barra lunga:  segmento (0,0,0) -> (0.6, 0, 0)         [lungo X, sezione in Y-Z]
//    barra corta:  segmento (0.3,0,0) -> (0.3, 0, 0.3)     [lungo Z, sezione in X-Y]
//    taglio:       la corta tagliata dalla lunga (EndCond = swEndConditionTrim)
//  Le due si sovrappongono in Z fra 0 e 0,04 (lo spessore della lunga): il
//  taglio toglie quella fetta ALLA CORTA, che riparte da Z=+0,04. Dopo il
//  taglio, la faccia Z=+0,04 della lunga (normale Z, area attesa
//  0.6 x 0.068 = 0,0408 m^2) e la faccia laterale della corta (normale +-X,
//  a X=0,26 o X=0,34, area attesa 0.26 x 0.068 = 0,01768 m^2) si toccano
//  lungo uno spigolo di 68 mm ad angolo retto — le stesse due facce su cui
//  gia' nasce `weld_bead`.
//
//  NIENTE EditDelete, niente AddDimension (mai chiamata), niente finestre:
//  il profilo si controlla con File.Exists PRIMA di chiamare
//  InsertStructuralWeldment5. Ogni pezzo si chiude senza salvare.
//
//  USO
//      set PROFILI_SALDATURA=<cartella coi .SLDLFP>
//      Checks\Gusset\bin\Gusset.exe            chiude ogni pezzo di prova
//      Checks\Gusset\bin\Gusset.exe --tieni    li lascia tutti aperti, per guardare
//
//  COMPILAZIONE: costruita da Checks\build-checks.ps1 (e da build.ps1, passo 1c), a banco
//  fermo. Le due DLL dell'interop le copia lo script accanto all'eseguibile.
//
//  ESITO DEL 28/9/2026, SOLIDWORKS 29.5.1 (l'uscita della sonda, com'era):
//
//    PROBE FAZZOLETTO — voce GP20 (il fazzoletto di saldatura non nasce)
//      T di due tubolari 80x80x5 (lunga 600 mm + corta 300 mm, tagliata)
//      una T fresca per ciascuna variante, chiusa senza salvare a fine giro
//    agganciato a SOLIDWORKS 29.5.1
//    0 — CONTROLLO: la ricetta esatta spedita nel ponte (WeldmentOperations.cs)
//      profilo degenere: BIsProfile=true (poligono) con D3=0 e D4=0 (BUseLenDim=true) e' un poligono a lato zero — la stessa forma spedita nel ponte, mai cambiata in 16 varianti.
//        01. T fresca: lunga 600 mm (X) + corta 300 mm (Z), profilo 80X80X5
//        02. due corpi, classificati lungo/corto per ingombro in X
//        03. corta tagliata dalla lunga (InsertWeldmentTrimFeature2, EndCond=Trim)
//        04. trovate le due facce: alta della lunga (40800 mm2) e laterale della corta (17680 mm2)
//        RISULTATO — InsertGussetFeature3 (marca 0)
//          feature creata:  NO
//          tipo feature:    -
//          corpi:           2 -> 2
//          feature albero:  22 -> 22
//          volume mm3:      1264161.94 -> 1264161.94   (delta +0)
//          VERDETTO:        come previsto
//    1 — RICETTA DELLA GUIDA (Insert_Gusset_Feature_Example_CSharp.htm): triangolo, marca 1/1
//      ricetta letta parola per parola dalla pagina d'esempio (stessi valori, lati 60 mm invece di 25/25 mm per pesare quanto la voce GP20 chiede: 60x60/2x5 = 9000 mm3 a lati pieni).
//        01. T fresca: lunga 600 mm (X) + corta 300 mm (Z), profilo 80X80X5
//        02. due corpi, classificati lungo/corto per ingombro in X
//        03. corta tagliata dalla lunga (InsertWeldmentTrimFeature2, EndCond=Trim)
//        04. trovate le due facce: alta della lunga (40800 mm2) e laterale della corta (17680 mm2)
//        RISULTATO — InsertGussetFeature3 (marca 1)
//          feature creata:  SI'
//          tipo feature:    Gusset
//          corpi:           2 -> 3
//          feature albero:  22 -> 23
//          volume mm3:      1264161.94 -> 1273161.94   (delta +9000)
//          attesa GP20:     ~9000 mm3 (60x60/2 x 5, meno lo smusso se c'e')
//          VERDETTO:        come previsto
//    2 — IPOTESI 'basta un profilo non degenere': poligono con D3/D4 reali
//      stessa forma POLIGONALE che il ponte intendeva (BIsProfile=true), ma con D3 e D4 diversi da zero: isola 'il profilo era degenere' da 'serviva il triangolo'.
//        01. T fresca: lunga 600 mm (X) + corta 300 mm (Z), profilo 80X80X5
//        02. due corpi, classificati lungo/corto per ingombro in X
//        03. corta tagliata dalla lunga (InsertWeldmentTrimFeature2, EndCond=Trim)
//        04. trovate le due facce: alta della lunga (40800 mm2) e laterale della corta (17680 mm2)
//        RISULTATO — InsertGussetFeature3 (marca 1)
//          feature creata:  SI'
//          tipo feature:    Gusset
//          corpi:           2 -> 3
//          feature albero:  22 -> 23
//          volume mm3:      1264161.94 -> 1277099.44   (delta +12937.5)
//          attesa GP20:     ~9000 mm3 (60x60/2 x 5, meno lo smusso se c'e')
//          VERDETTO:        come previsto
//    3 — IPOTESI 'bastava la marca': ricetta del ponte (profilo degenere incluso) con marca 1/1
//      identica alla variante 0, sola differenza la marca 1 su entrambe le facce invece di nessuna: se fallisce come la 0, la marca da sola non e' la causa (il TODO gia' la dava provata, ma non necessariamente con QUESTI stessi parametri messi insieme).
//        01. T fresca: lunga 600 mm (X) + corta 300 mm (Z), profilo 80X80X5
//        02. due corpi, classificati lungo/corto per ingombro in X
//        03. corta tagliata dalla lunga (InsertWeldmentTrimFeature2, EndCond=Trim)
//        04. trovate le due facce: alta della lunga (40800 mm2) e laterale della corta (17680 mm2)
//        RISULTATO — InsertGussetFeature3 (marca 1)
//          feature creata:  NO
//          tipo feature:    -
//          corpi:           2 -> 2
//          feature albero:  22 -> 22
//          volume mm3:      1264161.94 -> 1264161.94   (delta +0)
//          VERDETTO:        DIVERSO DAL PREVISTO — guarda qui sopra
//    4 — IPOTESI 'e' l'array, non la selezione': stessa ricetta della 1, via InsertGussetFeature2
//      stessi parametri della variante 1 ma le facce passate come Face2[] esplicito, senza nessuna selezione/marca: la pagina di InsertGussetFeature2 non la richiede.
//        01. T fresca: lunga 600 mm (X) + corta 300 mm (Z), profilo 80X80X5
//        02. due corpi, classificati lungo/corto per ingombro in X
//        03. corta tagliata dalla lunga (InsertWeldmentTrimFeature2, EndCond=Trim)
//        04. trovate le due facce: alta della lunga (40800 mm2) e laterale della corta (17680 mm2)
//        RISULTATO — InsertGussetFeature2 (Face2[])
//          feature creata:  SI'
//          tipo feature:    Gusset
//          corpi:           2 -> 3
//          feature albero:  22 -> 23
//          volume mm3:      1264161.94 -> 1273161.94   (delta +9000)
//          attesa GP20:     ~9000 mm3 (60x60/2 x 5, meno lo smusso se c'e')
//          VERDETTO:        come previsto
//    1 variante/i non si sono comportate come previsto: guarda sopra.
// =============================================================================

internal static class Programma
{
    private const string NomeFileProfilo = "TUBO STRUTTURALE QUADRO.SLDLFP";
    private const string Configurazione = "80X80X5";

    // Geometria della T di prova, in metri.
    private const double LungaX0 = 0.0, LungaX1 = 0.6;
    private const double CortaZ0 = 0.0, CortaZ1 = 0.3;
    private const double CortaX = 0.3;

    // Le due facce attese dopo il taglio (vedi il commento in testa al file).
    private const double AreaAltaLunga = 0.0408;      // m^2 : 0.6 x 0.068
    private const double AreaLateraleCorta = 0.01768; // m^2 : 0.26 x 0.068
    private const double TolleranzaArea = 0.0006;      // +-6 cm^2, generosa
    private const double TolleranzaQuota = 0.0003;     // +-0.3 mm sulla quota costante della faccia

    private static bool _tieni;
    private static int _passo;

    private static void Dice(string cosa)
    {
        _passo++;
        Console.WriteLine("    " + _passo.ToString("00") + ". " + cosa);
    }

    private static int Main(string[] argomenti)
    {
        _tieni = Array.IndexOf(argomenti, "--tieni") >= 0;

        Console.WriteLine();
        Console.WriteLine("PROBE FAZZOLETTO — voce GP20 (il fazzoletto di saldatura non nasce)");
        Console.WriteLine("  T di due tubolari 80x80x5 (lunga 600 mm + corta 300 mm, tagliata)");
        Console.WriteLine("  una T fresca per ciascuna variante, chiusa senza salvare a fine giro");
        Console.WriteLine();

        string cartellaProfili = System.Environment.GetEnvironmentVariable("PROFILI_SALDATURA");
        if (string.IsNullOrWhiteSpace(cartellaProfili))
        {
            Console.WriteLine("PROFILI_SALDATURA non e' impostata: non so dove sta " + NomeFileProfilo + ".");
            Console.WriteLine("Impostala (la cartella coi .SLDLFP) e rilancia.");
            return 2;
        }
        string percorsoProfilo = Path.Combine(cartellaProfili, NomeFileProfilo);
        if (!File.Exists(percorsoProfilo))
        {
            Console.WriteLine("Non trovo il profilo: " + percorsoProfilo);
            return 2;
        }

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex)
        {
            Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message);
            Console.WriteLine("Aprilo e rilancia. (Se il banco sta girando, aspetta che finisca: usa gia' SOLIDWORKS.)");
            return 2;
        }
        Console.WriteLine("agganciato a SOLIDWORKS " + sw.RevisionNumber());

        var varianti = new List<Ricetta>
        {
            new Ricetta
            {
                Titolo = "0 — CONTROLLO: la ricetta esatta spedita nel ponte (WeldmentOperations.cs)",
                Chiamata = "InsertGussetFeature3",
                Depth = 0.005,
                DirType = (short)swGussetThicknessType_e.swGussetThicknessBothSides,
                LocType = (short)swGussetProfileLocationType_e.swGussetProfileLocationCenter,
                IsProfile = true,
                D1 = 0.060, D2 = 0.060, D3 = 0.0, Angle = 0.0, D4 = 0.0,
                UseLenDim = true,
                Marca = 0,
                SiAttendeFallimento = true,
                PercheFallisceOForse =
                    "profilo degenere: BIsProfile=true (poligono) con D3=0 e D4=0 (BUseLenDim=true) e' un " +
                    "poligono a lato zero — la stessa forma spedita nel ponte, mai cambiata in 16 varianti.",
            },
            new Ricetta
            {
                Titolo = "1 — RICETTA DELLA GUIDA (Insert_Gusset_Feature_Example_CSharp.htm): triangolo, marca 1/1",
                Chiamata = "InsertGussetFeature3",
                Depth = 0.005,
                DirType = (short)swGussetThicknessType_e.swGussetThicknessBothSides,
                LocType = (short)swGussetProfileLocationType_e.swGussetProfileLocationCenter,
                IsProfile = false,
                D1 = 0.060, D2 = 0.060, D3 = 0.015, Angle = 0.785398163397448, D4 = 0.015,
                UseLenDim = false,
                Marca = 1,
                SiAttendeFallimento = false,
                PercheFallisceOForse =
                    "ricetta letta parola per parola dalla pagina d'esempio (stessi valori, lati 60 mm invece di " +
                    "25/25 mm per pesare quanto la voce GP20 chiede: 60x60/2x5 = 9000 mm3 a lati pieni).",
            },
            new Ricetta
            {
                Titolo = "2 — IPOTESI 'basta un profilo non degenere': poligono con D3/D4 reali",
                Chiamata = "InsertGussetFeature3",
                Depth = 0.005,
                DirType = (short)swGussetThicknessType_e.swGussetThicknessBothSides,
                LocType = (short)swGussetProfileLocationType_e.swGussetProfileLocationCenter,
                IsProfile = true,
                D1 = 0.060, D2 = 0.060, D3 = 0.015, D4 = 0.015, Angle = 0.0,
                UseLenDim = true,
                Marca = 1,
                SiAttendeFallimento = false,
                PercheFallisceOForse =
                    "stessa forma POLIGONALE che il ponte intendeva (BIsProfile=true), ma con D3 e D4 " +
                    "diversi da zero: isola 'il profilo era degenere' da 'serviva il triangolo'.",
            },
            new Ricetta
            {
                Titolo = "3 — IPOTESI 'bastava la marca': ricetta del ponte (profilo degenere incluso) con marca 1/1",
                Chiamata = "InsertGussetFeature3",
                Depth = 0.005,
                DirType = (short)swGussetThicknessType_e.swGussetThicknessBothSides,
                LocType = (short)swGussetProfileLocationType_e.swGussetProfileLocationCenter,
                IsProfile = true,
                D1 = 0.060, D2 = 0.060, D3 = 0.0, Angle = 0.0, D4 = 0.0,
                UseLenDim = true,
                Marca = 1,
                SiAttendeFallimento = false,
                PercheFallisceOForse =
                    "identica alla variante 0, sola differenza la marca 1 su entrambe le facce invece di " +
                    "nessuna: se fallisce come la 0, la marca da sola non e' la causa (il TODO gia' la dava " +
                    "provata, ma non necessariamente con QUESTI stessi parametri messi insieme).",
            },
            new Ricetta
            {
                Titolo = "4 — IPOTESI 'e' l'array, non la selezione': stessa ricetta della 1, via InsertGussetFeature2",
                Chiamata = "InsertGussetFeature2",
                Depth = 0.005,
                DirType = (short)swGussetThicknessType_e.swGussetThicknessBothSides,
                LocType = (short)swGussetProfileLocationType_e.swGussetProfileLocationCenter,
                IsProfile = false,
                D1 = 0.060, D2 = 0.060, D3 = 0.015, Angle = 0.785398163397448, D4 = 0.015,
                UseLenDim = false,
                Marca = 0,
                SiAttendeFallimento = false,
                PercheFallisceOForse =
                    "stessi parametri della variante 1 ma le facce passate come Face2[] esplicito, senza " +
                    "nessuna selezione/marca: la pagina di InsertGussetFeature2 non la richiede.",
            },
        };

        int falliteInAttese = 0;
        foreach (var r in varianti)
        {
            _passo = 0;
            Console.WriteLine();
            Console.WriteLine("──────────────────────────────────────────────────────────────────");
            Console.WriteLine(r.Titolo);
            Console.WriteLine("  " + r.PercheFallisceOForse);
            bool comeAtteso = EseguiVariante(sw, percorsoProfilo, r);
            if (!comeAtteso) falliteInAttese++;
        }

        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════════════════════════════");
        if (falliteInAttese == 0)
            Console.WriteLine("Tutte le varianti si sono comportate come previsto (compreso il controllo).");
        else
            Console.WriteLine(falliteInAttese + " variante/i non si sono comportate come previsto: guarda sopra.");
        return falliteInAttese == 0 ? 0 : 1;
    }

    /// <summary>Una ricetta di argomenti per InsertGussetFeature2/3, piu' come selezionare.</summary>
    private sealed class Ricetta
    {
        public string Titolo;
        public string PercheFallisceOForse;
        public string Chiamata;   // "InsertGussetFeature3" oppure "InsertGussetFeature2"
        public double Depth;
        public short DirType;
        public short LocType;
        public bool IsProfile;
        public double D1, D2, D3, Angle, D4;
        public bool UseLenDim;
        public int Marca;                 // 0 = nessuna marca (Select4(true, null)); altrimenti Select4 con Mark = Marca su ENTRAMBE le facce
        public bool SiAttendeFallimento;  // true solo per il controllo (variante 0)
    }

    /// <summary>Costruisce una T fresca, prova la ricetta, misura, chiude il pezzo. Torna true se l'esito e' quello atteso.</summary>
    private static bool EseguiVariante(ISldWorks sw, string percorsoProfilo, Ricetta r)
    {
        IModelDoc2 doc = null;
        try
        {
            doc = CostruisciT(sw, percorsoProfilo);
            if (doc == null) return false;

            IBody2 corpoLungo, corpoCorto;
            if (!ClassificaCorpi(doc, out corpoLungo, out corpoCorto)) return false;
            Dice("due corpi, classificati lungo/corto per ingombro in X");

            if (!Taglia(doc, corpoCorto, corpoLungo)) return false;
            Dice("corta tagliata dalla lunga (InsertWeldmentTrimFeature2, EndCond=Trim)");

            // Dopo il taglio i riferimenti ai corpi restano validi (stesso Body2),
            // ma li ripesco comunque: piu' fedele a "cosa farebbe il ponte".
            if (!ClassificaCorpi(doc, out corpoLungo, out corpoCorto)) return false;

            IFace2 facciaAltaLunga, facciaLateraleCorta;
            if (!TrovaLeDueFacce(corpoLungo, corpoCorto, out facciaAltaLunga, out facciaLateraleCorta))
            {
                Console.WriteLine("    NON TROVO le due facce attese (alta della lunga / laterale della corta).");
                Console.WriteLine("    La T di prova non e' venuta come previsto: nessun verdetto sul fazzoletto qui.");
                return false;
            }
            Dice("trovate le due facce: alta della lunga (" + FormattaArea(facciaAltaLunga) +
                 ") e laterale della corta (" + FormattaArea(facciaLateraleCorta) + ")");

            double? volumePrima = VolumeMm3(doc);
            int corpiPrima = ContaCorpi(doc);
            int featurePrima = ContaFeatureAlbero(doc);

            IFeature feature = null;
            string erroreChiamata = null;
            try
            {
                if (r.Chiamata == "InsertGussetFeature2")
                {
                    // Nessuna selezione: le facce vanno come array tipizzato Face2[].
                    // (memoria api-sw-array-tipizzati: object[] da' feature fantasma)
                    var facce = new Face2[] { (Face2)facciaAltaLunga, (Face2)facciaLateraleCorta };
                    feature = doc.FeatureManager.InsertGussetFeature2(
                        r.Depth, r.DirType, r.LocType, r.IsProfile,
                        r.D1, r.D2, r.D3, r.Angle, r.D4,
                        false, 0.0, 0,
                        false, false, r.UseLenDim,
                        facce) as IFeature;
                }
                else
                {
                    doc.ClearSelection2(true);
                    if (!SelezionaFaccia(doc, facciaAltaLunga, r.Marca)) { erroreChiamata = "selezione della faccia alta non riuscita"; }
                    else if (!SelezionaFaccia(doc, facciaLateraleCorta, r.Marca)) { erroreChiamata = "selezione della faccia laterale non riuscita"; }
                    else
                    {
                        feature = doc.FeatureManager.InsertGussetFeature3(
                            r.Depth, r.DirType, r.LocType, r.IsProfile,
                            r.D1, r.D2, r.D3, r.Angle, r.D4,
                            false, 0.0, 0,
                            false, false, r.UseLenDim,
                            0.0125, 0.0125, 0.0,
                            false, false) as IFeature;
                    }
                }
            }
            catch (Exception ex)
            {
                erroreChiamata = ex.Message;
            }
            doc.ClearSelection2(true);

            double? volumeDopo = VolumeMm3(doc);
            int corpiDopo = ContaCorpi(doc);
            int featureDopo = ContaFeatureAlbero(doc);

            bool nata = feature != null && featureDopo > featurePrima;
            string tipo = nata ? SicuroGetTypeName(feature) : "-";
            double? delta = (volumePrima.HasValue && volumeDopo.HasValue) ? volumeDopo.Value - volumePrima.Value : (double?)null;

            Console.WriteLine();
            Console.WriteLine("    RISULTATO — " + r.Chiamata + (r.Chiamata == "InsertGussetFeature3" ? " (marca " + r.Marca + ")" : " (Face2[])"));
            Console.WriteLine("      feature creata:  " + (nata ? "SI'" : "NO") + (erroreChiamata != null ? "   [eccezione: " + erroreChiamata + "]" : ""));
            Console.WriteLine("      tipo feature:    " + tipo);
            Console.WriteLine("      corpi:           " + corpiPrima + " -> " + corpiDopo);
            Console.WriteLine("      feature albero:  " + featurePrima + " -> " + featureDopo);
            Console.WriteLine("      volume mm3:      " +
                (volumePrima.HasValue ? volumePrima.Value.ToString("0.##", CultureInfo.InvariantCulture) : "?") + " -> " +
                (volumeDopo.HasValue ? volumeDopo.Value.ToString("0.##", CultureInfo.InvariantCulture) : "?") +
                (delta.HasValue ? "   (delta " + delta.Value.ToString("+0.##;-0.##", CultureInfo.InvariantCulture) + ")" : ""));
            if (nata && delta.HasValue)
                Console.WriteLine("      attesa GP20:     ~9000 mm3 (60x60/2 x 5, meno lo smusso se c'e')");

            bool comeAtteso = r.SiAttendeFallimento ? !nata : nata;
            Console.WriteLine("      VERDETTO:        " + (comeAtteso ? "come previsto" : "DIVERSO DAL PREVISTO — guarda qui sopra"));

            return comeAtteso;
        }
        catch (Exception ex)
        {
            Console.WriteLine("    ERRORE non gestito in questa variante: " + ex.Message);
            return false;
        }
        finally
        {
            if (doc != null && !_tieni)
            {
                try { sw.CloseDoc(doc.GetTitle()); } catch { }
            }
        }
    }

    /// <summary>
    /// Seleziona la faccia con Select4. Se marca &lt;= 0, nessuna marca
    /// (Select4(true, null): la ricetta spedita nel ponte). Se marca &gt; 0,
    /// COSTRUISCE il SelectData dal SelectionMgr del documento e vi mette
    /// .Mark = marca — stesso schema di DraftOperations.cs e di
    /// Checks/EdgeFlange/Program.cs: la marca sta sui DATI di
    /// selezione, non sull'entita', e senza costruirla `Select4(true, null)`
    /// non marca niente anche se marca &gt; 0 fosse passato per sbaglio a null.
    /// </summary>
    private static bool SelezionaFaccia(IModelDoc2 doc, IFace2 faccia, int marca)
    {
        var ent = faccia as IEntity;
        if (ent == null) return false;
        if (marca <= 0) return ent.Select4(true, null);

        var selMgr = doc.SelectionManager as ISelectionMgr;
        if (selMgr == null) return false;
        var sd = selMgr.CreateSelectData() as SelectData;
        if (sd == null) return false;
        sd.Mark = marca;
        return ent.Select4(true, sd);
    }

    /// <summary>Le due strutturali fresche: 3D sketch + StructuralMemberGroup + InsertStructuralWeldment5, due volte.</summary>
    private static IModelDoc2 CostruisciT(ISldWorks sw, string percorsoProfilo)
    {
        string modello = sw.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        if (string.IsNullOrWhiteSpace(modello) || !File.Exists(modello))
        {
            Console.WriteLine("    non c'e' un modello di parte predefinito.");
            return null;
        }
        var doc = sw.NewDocument(modello, 0, 0, 0) as IModelDoc2;
        if (doc == null) { Console.WriteLine("    NewDocument -> null"); return null; }

        if (!AggiungiMembro(doc, percorsoProfilo, 0, 0, 0, LungaX1, 0, 0))
        {
            Console.WriteLine("    la barra lunga non e' nata.");
            return null;
        }
        if (!AggiungiMembro(doc, percorsoProfilo, CortaX, 0, CortaZ0, CortaX, 0, CortaZ1))
        {
            Console.WriteLine("    la barra corta non e' nata.");
            return null;
        }
        doc.ViewZoomtofit2();
        Dice("T fresca: lunga 600 mm (X) + corta 300 mm (Z), profilo " + Configurazione);
        return doc;
    }

    private static bool AggiungiMembro(
        IModelDoc2 doc, string percorsoProfilo,
        double xa, double ya, double za, double xb, double yb, double zb)
    {
        doc.ClearSelection2(true);
        var schizzi = doc.SketchManager;
        schizzi.Insert3DSketch(true);
        var seg = schizzi.CreateLine(xa, ya, za, xb, yb, zb) as ISketchSegment;
        schizzi.Insert3DSketch(true);
        if (seg == null) return false;

        var gruppo = doc.FeatureManager.CreateStructuralMemberGroup() as IStructuralMemberGroup;
        if (gruppo == null) return false;
        gruppo.Segments = new SketchSegment[] { (SketchSegment)seg };

        // SOLIDWORKS opens the profile library by itself and leaves it DIRTY
        // (OperazioniSaldatura.RichiudiLaLibreria): a stray Ctrl+S would rewrite
        // the shared company library. Close it again, but only if it was not
        // already open before this insert.
        var sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
        bool libraryWasOpen = sw.GetOpenDocumentByName(percorsoProfilo) != null;
        var feature = doc.FeatureManager.InsertStructuralWeldment5(
            Path: percorsoProfilo,
            ConnectedSegmentsOption: 1,
            AllowProtrusion: false,
            Groups: new StructuralMemberGroup[] { (StructuralMemberGroup)gruppo },
            ConfigurationName: Configurazione) as IFeature;
        doc.ClearSelection2(true);
        if (!libraryWasOpen)
        {
            var lib = sw.GetOpenDocumentByName(percorsoProfilo) as IModelDoc2;
            if (lib != null) { try { sw.CloseDoc(lib.GetTitle()); } catch { } }
        }
        return feature != null;
    }

    private static bool Taglia(IModelDoc2 doc, IBody2 daTagliare, IBody2 contro)
    {
        try
        {
            var feature = doc.FeatureManager.InsertWeldmentTrimFeature2(
                EndCond: (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionTrim,
                Options: 0,
                GapValue: 0.0,
                BodiesToTrim: new Body2[] { (Body2)daTagliare },
                BodiesOrFaces: new Body2[] { (Body2)contro }) as IFeature;
            return feature != null;
        }
        catch (Exception ex)
        {
            Console.WriteLine("    il taglio non e' riuscito: " + ex.Message);
            return false;
        }
    }

    /// <summary>Classifica i due corpi solidi per ingombro in X: il piu' lungo e' la barra lunga.</summary>
    private static bool ClassificaCorpi(IModelDoc2 doc, out IBody2 lungo, out IBody2 corto)
    {
        lungo = null; corto = null;
        var parte = doc as IPartDoc;
        var corpi = parte == null ? null : parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (corpi == null || corpi.Length != 2)
        {
            Console.WriteLine("    mi aspettavo 2 corpi solidi, ne trovo " + (corpi == null ? 0 : corpi.Length) + ".");
            return false;
        }
        var a = corpi[0] as IBody2;
        var b = corpi[1] as IBody2;
        var boxA = a.GetBodyBox() as double[];
        var boxB = b.GetBodyBox() as double[];
        if (boxA == null || boxB == null || boxA.Length < 6 || boxB.Length < 6)
        {
            Console.WriteLine("    non riesco a leggere l'ingombro di uno dei due corpi.");
            return false;
        }
        double estensioneXA = boxA[3] - boxA[0];
        double estensioneXB = boxB[3] - boxB[0];
        if (estensioneXA >= estensioneXB) { lungo = a; corto = b; }
        else { lungo = b; corto = a; }
        return true;
    }

    /// <summary>
    /// La faccia ALTA della lunga e la faccia LATERALE della corta, trovate
    /// per geometria (area + quota costante su un asse), non per indice:
    /// stesso principio di Checks/Bracket/Program.cs (DueFacceInPunta).
    ///
    /// GLI ASSI, DERIVATI DALLA GEOMETRIA DELLA T (non assunti):
    /// la lunga corre lungo X (sezione nel piano Y-Z, centrata su Y=0,Z=0:
    /// Y e Z in [-0,04; +0,04]); la corta corre lungo Z, a X=0,3 (sezione nel
    /// piano X-Y, centrata su X=0,3: X in [0,26; 0,34]) e prima del taglio
    /// arriva fino a Z=0. Le due si sovrappongono in Z in [0; 0,04] — il
    /// taglio toglie quella fetta ALLA CORTA, che quindi riparte da Z=+0,04:
    /// e' la faccia Z=+0,04 della LUNGA (normale Z, non Y: la lunga ha
    /// quattro facce piane della stessa area per la simmetria della sezione
    /// quadra — Y=+-0,04 e Z=+-0,04 — e solo Z=+0,04 e' quella su cui la
    /// corta e' rimasta appoggiata) a toccare la faccia laterale della corta
    /// (normale X, a X=0,26 o X=0,34: le due facce Y=+-0,04 della corta
    /// hanno la STESSA area per la stessa ragione, e vanno escluse fissando
    /// l'asse a 0/X, non lasciandolo libero).
    /// </summary>
    private static bool TrovaLeDueFacce(
        IBody2 corpoLungo, IBody2 corpoCorto,
        out IFace2 facciaAltaLunga, out IFace2 facciaLateraleCorta)
    {
        facciaAltaLunga = TrovaFaccia(corpoLungo, AreaAltaLunga, 2 /* Z */, +0.04);
        facciaLateraleCorta = TrovaFaccia(corpoCorto, AreaLateraleCorta, 0 /* X */, null);
        return facciaAltaLunga != null && facciaLateraleCorta != null;
    }

    /// <summary>
    /// Cerca fra le facce del corpo una faccia PIANA con l'area attesa (entro
    /// tolleranza) e, se richiesto, con la quota dell'asse indicato costante e
    /// pari al valore atteso. Se il valore atteso e' null, accetta la prima
    /// faccia piana con l'area giusta e la quota costante su QUELL'asse
    /// (qualunque essa sia): serve per la corta, che ha due facce laterali
    /// candidate (x=0.26 e x=0.34) ugualmente valide.
    /// </summary>
    private static IFace2 TrovaFaccia(IBody2 corpo, double areaAttesa, int asse, double? quotaAttesa)
    {
        var faccia = corpo.GetFirstFace() as IFace2;
        while (faccia != null)
        {
            double area;
            try { area = faccia.GetArea(); }
            catch { area = -1; }
            if (Math.Abs(area - areaAttesa) <= TolleranzaArea)
            {
                var box = faccia.GetBox() as double[];
                if (box != null && box.Length >= 6)
                {
                    double min = box[asse];
                    double max = box[asse + 3];
                    bool piana = Math.Abs(max - min) < 1e-6;
                    bool quotaOk = quotaAttesa == null || Math.Abs(min - quotaAttesa.Value) <= TolleranzaQuota;
                    if (piana && quotaOk) return faccia;
                }
            }
            faccia = faccia.GetNextFace() as IFace2;
        }
        return null;
    }

    private static string FormattaArea(IFace2 faccia)
    {
        try { return (faccia.GetArea() * 1e6).ToString("0", CultureInfo.InvariantCulture) + " mm2"; }
        catch { return "? mm2"; }
    }

    private static int ContaCorpi(IModelDoc2 doc)
    {
        var parte = doc as IPartDoc;
        var corpi = parte == null ? null : parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        return corpi == null ? 0 : corpi.Length;
    }

    private static int ContaFeatureAlbero(IModelDoc2 doc)
    {
        int n = 0;
        try
        {
            var f = doc.FirstFeature() as IFeature;
            while (f != null) { n++; f = f.GetNextFeature() as IFeature; }
        }
        catch { return -1; }
        return n;
    }

    private static double? VolumeMm3(IModelDoc2 doc)
    {
        var parte = doc as IPartDoc;
        if (parte == null) return null;
        double v = 0;
        try
        {
            var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (corpi == null) return 0;
            foreach (var c in corpi)
            {
                var mp = (c as IBody2)?.GetMassProperties(1000.0) as double[];
                if (mp == null || mp.Length <= 4) return null;
                v += mp[3] * 1e9;
            }
        }
        catch { return null; }
        return v;
    }

    private static string SicuroGetTypeName(IFeature f)
    {
        try { return f.GetTypeName2(); }
        catch { return "?"; }
    }
}
