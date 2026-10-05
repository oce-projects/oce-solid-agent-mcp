using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  probe_fazzoletto — entry GP20: "the gusset is not born"
// =============================================================================
//
//  WHAT THIS PROBE MEASURES AND WHY
//
//  The catalogue (Oce.Solid.Agent.Core/Catalog/Operations.cs, entry "gusset") and
//  the TODO (entry GP20) list 16 variants tried blindly, ALL null:
//
//    mark: none, 1/1, 1/2, reversed order               (6)
//    thickness: inside, outside, on both sides           (3)
//    profile: triangular, polygonal                      (\
//    profile: sides (BUseLenDim=true), angle (=false)     ) 4 (crossed in pairs)
//    position: start, centre                              /  (2)
//                                                    1 base + 15 = 16
//
//  NONE of these 16 variants appears as "the value of the third parameter
//  of the profile" (ProfileAngle when BUseLenDim=false, ProfileD4 when it is
//  true). The API guide (Insert_Gusset_Feature_Example_CSharp.htm and
//  Insert_Weldment_Features_Example_CSharp.htm, IFeatureManager~
//  InsertGussetFeature3) ALWAYS shows that third value different from zero
//  (45 degrees, or 15 mm) in the TWO only working examples known. The
//  code shipped in the bridge (WeldmentOperations.cs, Gusset) passes
//  ProfileD3=0, ProfileAngle=0, ProfileD4=0 ALWAYS: a profile with angle or
//  side zero is degenerate (it closes on itself), and SOLIDWORKS rejects it
//  silently — the same defect "null without error on invalid
//  geometry" already paid for elsewhere in this project (see memory
//  api-sw-array-tipizzati).
//
//  CORRECTION FROM THE REVIEW (28/9/2026), read in the historic code
//  (prova0089, Gusset(), variant "polygonal", never entered in the repo): a
//  non-degenerate TRIANGLE (BIsProfile=false, D1=D2=60) HAS ALREADY been tried,
//  but WITHOUT a mark, and gave null; the mark 1/1 was tried only with the
//  degenerate polygon (Gusset(), "marca1"). The combination never tried is
//  therefore valid profile + mark 1 on both faces: it is variant 1.
//
//  This is HYPOTHESIS 1, the most probable. The other two probes isolate the
//  neighbouring variables, so as not to confuse "the profile was degenerate" with "the
//  mark was wrong" or "the selection mechanism (mark) is different
//  from the explicit array":
//
//    variant 0 — CONTROL: the exact recipe shipped in the bridge, on the
//                 SAME test T of this probe. It must fail (null) as
//                 always: if it were born here, it would mean that the defect
//                 is not in the parameters but in the bridge's test T.
//    variant 1 — RECIPE OF THE GUIDE: InsertGussetFeature3, TRIANGULAR
//                 profile (BIsProfile=false), angle 45 degrees
//                 (BUseLenDim=false), mark 1 on BOTH faces (the
//                 reference page says so in Remarks: "select two
//                 faces ... with a Mark of 1").
//    variant 2 — HYPOTHESIS "the degenerate profile is the only cause": same
//                 POLYGONAL shape (BIsProfile=true) that the bridge intended,
//                 but with REAL ProfileD3/ProfileD4 (15 mm, not zero).
//    variant 3 — HYPOTHESIS "the mark was enough": the EXACT recipe of the bridge
//                 (degenerate profile included) but with a real mark 1/1 instead of
//                 no mark — to isolate the mark alone.
//    variant 4 — HYPOTHESIS "it is the array, not the selection": same recipe
//                 as variant 1 but passed as an explicit Face2[] to
//                 InsertGussetFeature2 (no selection, no mark:
//                 the reference page of InsertGussetFeature2 does not
//                 require it).
//
//  Each variant builds a FRESH T (two structural members 80x80x5,
//  600 + 300 mm, trimmed) so an attempt leaves no residue that
//  confuses the next one, and closes the part without saving.
//
//  TEST T — the same as Bench/cases/weldments/weld_bead_goes_between_touching_faces.json:
//    long bar:   segment (0,0,0) -> (0.6, 0, 0)         [along X, section in Y-Z]
//    short bar:  segment (0.3,0,0) -> (0.3, 0, 0.3)     [along Z, section in X-Y]
//    trim:       the short one trimmed by the long one (EndCond = swEndConditionTrim)
//  The two overlap in Z between 0 and 0.04 (the thickness of the long one): the
//  trim removes that slice FROM THE SHORT ONE, which restarts from Z=+0.04. After the
//  trim, the face Z=+0.04 of the long one (normal Z, expected area
//  0.6 x 0.068 = 0.0408 m^2) and the side face of the short one (normal +-X,
//  at X=0.26 or X=0.34, expected area 0.26 x 0.068 = 0.01768 m^2) touch
//  along an edge of 68 mm at a right angle — the same two faces on which
//  `weld_bead` is already born.
//
//  NO EditDelete, no AddDimension (never called), no windows:
//  the profile is checked with File.Exists BEFORE calling
//  InsertStructuralWeldment5. Every part is closed without saving.
//
//  USAGE
//      set PROFILI_SALDATURA=<cartella coi .SLDLFP>
//      Checks\Gusset\bin\Gusset.exe            closes every test part
//      Checks\Gusset\bin\Gusset.exe --tieni    leaves them all open, to look
//
//  BUILD: built by Checks\build-checks.ps1 (and by build.ps1, step 1c), with the bench
//  stopped. The script copies the two interop DLLs next to the executable.
//
//  OUTCOME OF 28/9/2026, SOLIDWORKS 29.5.1 (the output of the probe, as it was):
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

internal static class Program
{
    private const string ProfileFileName = "TUBO STRUTTURALE QUADRO.SLDLFP";
    private const string Configuration = "80X80X5";

    // Geometry of the test T, in metres.
    private const double LongX0 = 0.0, LongX1 = 0.6;
    private const double CortaZ0 = 0.0, CortaZ1 = 0.3;
    private const double CortaX = 0.3;

    // The two faces expected after the trim (see the comment at the top of the file).
    private const double TallLongArea = 0.0408;      // m^2 : 0.6 x 0.068
    private const double AreaLateraleCorta = 0.01768; // m^2 : 0.26 x 0.068
    private const double AreaTolerance = 0.0006;      // +-6 cm^2, generosa
    private const double DimensionTolerance = 0.0003;     // +-0.3 mm on the constant coordinate of the face

    private static bool _keep;
    private static int _step;

    private static void Says(string what)
    {
        _step++;
        Console.WriteLine("    " + _step.ToString("00") + ". " + what);
    }

    private static int Main(string[] arguments)
    {
        _keep = Array.IndexOf(arguments, "--tieni") >= 0;

        Console.WriteLine();
        Console.WriteLine("PROBE FAZZOLETTO — voce GP20 (il fazzoletto di saldatura non nasce)");
        Console.WriteLine("  T di due tubolari 80x80x5 (lunga 600 mm + corta 300 mm, tagliata)");
        Console.WriteLine("  una T fresca per ciascuna variante, chiusa senza salvare a fine giro");
        Console.WriteLine();

        string profilesFolder = System.Environment.GetEnvironmentVariable("PROFILI_SALDATURA");
        if (string.IsNullOrWhiteSpace(profilesFolder))
        {
            Console.WriteLine("PROFILI_SALDATURA non e' impostata: non so dove sta " + ProfileFileName + ".");
            Console.WriteLine("Impostala (la cartella coi .SLDLFP) e rilancia.");
            return 2;
        }
        string profilePath = Path.Combine(profilesFolder, ProfileFileName);
        if (!File.Exists(profilePath))
        {
            Console.WriteLine("Non trovo il profilo: " + profilePath);
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
                Title = "0 — CONTROLLO: la ricetta esatta spedita nel ponte (WeldmentOperations.cs)",
                Call = "InsertGussetFeature3",
                Depth = 0.005,
                DirType = (short)swGussetThicknessType_e.swGussetThicknessBothSides,
                LocType = (short)swGussetProfileLocationType_e.swGussetProfileLocationCenter,
                IsProfile = true,
                D1 = 0.060, D2 = 0.060, D3 = 0.0, Angle = 0.0, D4 = 0.0,
                UseLenDim = true,
                Mark = 0,
                FailureExpected = true,
                WhyItFailsOrMaybe =
                    "profilo degenere: BIsProfile=true (poligono) con D3=0 e D4=0 (BUseLenDim=true) e' un " +
                    "poligono a lato zero — la stessa forma spedita nel ponte, mai cambiata in 16 varianti.",
            },
            new Ricetta
            {
                Title = "1 — RICETTA DELLA GUIDA (Insert_Gusset_Feature_Example_CSharp.htm): triangolo, marca 1/1",
                Call = "InsertGussetFeature3",
                Depth = 0.005,
                DirType = (short)swGussetThicknessType_e.swGussetThicknessBothSides,
                LocType = (short)swGussetProfileLocationType_e.swGussetProfileLocationCenter,
                IsProfile = false,
                D1 = 0.060, D2 = 0.060, D3 = 0.015, Angle = 0.785398163397448, D4 = 0.015,
                UseLenDim = false,
                Mark = 1,
                FailureExpected = false,
                WhyItFailsOrMaybe =
                    "ricetta letta parola per parola dalla pagina d'esempio (stessi valori, lati 60 mm invece di " +
                    "25/25 mm per pesare quanto la voce GP20 chiede: 60x60/2x5 = 9000 mm3 a lati pieni).",
            },
            new Ricetta
            {
                Title = "2 — IPOTESI 'basta un profilo non degenere': poligono con D3/D4 reali",
                Call = "InsertGussetFeature3",
                Depth = 0.005,
                DirType = (short)swGussetThicknessType_e.swGussetThicknessBothSides,
                LocType = (short)swGussetProfileLocationType_e.swGussetProfileLocationCenter,
                IsProfile = true,
                D1 = 0.060, D2 = 0.060, D3 = 0.015, D4 = 0.015, Angle = 0.0,
                UseLenDim = true,
                Mark = 1,
                FailureExpected = false,
                WhyItFailsOrMaybe =
                    "stessa forma POLIGONALE che il ponte intendeva (BIsProfile=true), ma con D3 e D4 " +
                    "diversi da zero: isola 'il profilo era degenere' da 'serviva il triangolo'.",
            },
            new Ricetta
            {
                Title = "3 — IPOTESI 'bastava la marca': ricetta del ponte (profilo degenere incluso) con marca 1/1",
                Call = "InsertGussetFeature3",
                Depth = 0.005,
                DirType = (short)swGussetThicknessType_e.swGussetThicknessBothSides,
                LocType = (short)swGussetProfileLocationType_e.swGussetProfileLocationCenter,
                IsProfile = true,
                D1 = 0.060, D2 = 0.060, D3 = 0.0, Angle = 0.0, D4 = 0.0,
                UseLenDim = true,
                Mark = 1,
                FailureExpected = false,
                WhyItFailsOrMaybe =
                    "identica alla variante 0, sola differenza la marca 1 su entrambe le facce invece di " +
                    "nessuna: se fallisce come la 0, la marca da sola non e' la causa (il TODO gia' la dava " +
                    "provata, ma non necessariamente con QUESTI stessi parametri messi insieme).",
            },
            new Ricetta
            {
                Title = "4 — IPOTESI 'e' l'array, non la selezione': stessa ricetta della 1, via InsertGussetFeature2",
                Call = "InsertGussetFeature2",
                Depth = 0.005,
                DirType = (short)swGussetThicknessType_e.swGussetThicknessBothSides,
                LocType = (short)swGussetProfileLocationType_e.swGussetProfileLocationCenter,
                IsProfile = false,
                D1 = 0.060, D2 = 0.060, D3 = 0.015, Angle = 0.785398163397448, D4 = 0.015,
                UseLenDim = false,
                Mark = 0,
                FailureExpected = false,
                WhyItFailsOrMaybe =
                    "stessi parametri della variante 1 ma le facce passate come Face2[] esplicito, senza " +
                    "nessuna selezione/marca: la pagina di InsertGussetFeature2 non la richiede.",
            },
        };

        int failedInExpectations = 0;
        foreach (var r in varianti)
        {
            _step = 0;
            Console.WriteLine();
            Console.WriteLine("──────────────────────────────────────────────────────────────────");
            Console.WriteLine(r.Title);
            Console.WriteLine("  " + r.WhyItFailsOrMaybe);
            bool asExpected = RunVariant(sw, profilePath, r);
            if (!asExpected) failedInExpectations++;
        }

        Console.WriteLine();
        Console.WriteLine("══════════════════════════════════════════════════════════════════");
        if (failedInExpectations == 0)
            Console.WriteLine("Tutte le varianti si sono comportate come previsto (compreso il controllo).");
        else
            Console.WriteLine(failedInExpectations + " variante/i non si sono comportate come previsto: guarda sopra.");
        return failedInExpectations == 0 ? 0 : 1;
    }

    /// <summary>A recipe of arguments for InsertGussetFeature2/3, plus how to select.</summary>
    private sealed class Ricetta
    {
        public string Title;
        public string WhyItFailsOrMaybe;
        public string Call;   // "InsertGussetFeature3" or "InsertGussetFeature2"
        public double Depth;
        public short DirType;
        public short LocType;
        public bool IsProfile;
        public double D1, D2, D3, Angle, D4;
        public bool UseLenDim;
        public int Mark;                 // 0 = no mark (Select4(true, null)); otherwise Select4 with that Mark on BOTH faces
        public bool FailureExpected;  // true only for the control (variant 0)
    }

    /// <summary>Builds a fresh T, tries the recipe, measures, closes the part. Returns true if the outcome is the expected one.</summary>
    private static bool RunVariant(ISldWorks sw, string profilePath, Ricetta r)
    {
        IModelDoc2 doc = null;
        try
        {
            doc = BuildT(sw, profilePath);
            if (doc == null) return false;

            IBody2 longBody, shortBody;
            if (!ClassifyBodies(doc, out longBody, out shortBody)) return false;
            Says("due corpi, classificati lungo/corto per ingombro in X");

            if (!Cut(doc, shortBody, longBody)) return false;
            Says("corta tagliata dalla lunga (InsertWeldmentTrimFeature2, EndCond=Trim)");

            // After the trim the references to the bodies stay valid (same Body2),
            // but I fish them out again anyway: more faithful to "what the bridge would do".
            if (!ClassifyBodies(doc, out longBody, out shortBody)) return false;

            IFace2 tallLongFace, shortSideFace;
            if (!FindTwoFaces(longBody, shortBody, out tallLongFace, out shortSideFace))
            {
                Console.WriteLine("    NON TROVO le due facce attese (alta della lunga / laterale della corta).");
                Console.WriteLine("    La T di prova non e' venuta come previsto: nessun verdetto sul fazzoletto qui.");
                return false;
            }
            Says("trovate le due facce: alta della lunga (" + FormattaArea(tallLongFace) +
                 ") e laterale della corta (" + FormattaArea(shortSideFace) + ")");

            double? volumeBefore = VolumeMm3(doc);
            int bodiesBefore = CountBodies(doc);
            int featureBefore = CountTreeFeatures(doc);

            IFeature feature = null;
            string callError = null;
            try
            {
                if (r.Call == "InsertGussetFeature2")
                {
                    // No selection: the faces go as a typed array Face2[].
                    // (memory api-sw-array-tipizzati: object[] gives a phantom feature)
                    var faces = new Face2[] { (Face2)tallLongFace, (Face2)shortSideFace };
                    feature = doc.FeatureManager.InsertGussetFeature2(
                        r.Depth, r.DirType, r.LocType, r.IsProfile,
                        r.D1, r.D2, r.D3, r.Angle, r.D4,
                        false, 0.0, 0,
                        false, false, r.UseLenDim,
                        faces) as IFeature;
                }
                else
                {
                    doc.ClearSelection2(true);
                    if (!SelectFace(doc, tallLongFace, r.Mark)) { callError = "selezione della faccia alta non riuscita"; }
                    else if (!SelectFace(doc, shortSideFace, r.Mark)) { callError = "selezione della faccia laterale non riuscita"; }
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
                callError = ex.Message;
            }
            doc.ClearSelection2(true);

            double? volumeAfter = VolumeMm3(doc);
            int bodiesAfter = CountBodies(doc);
            int featureAfter = CountTreeFeatures(doc);

            bool born = feature != null && featureAfter > featureBefore;
            string typ = born ? SafeGetTypeName(feature) : "-";
            double? delta = (volumeBefore.HasValue && volumeAfter.HasValue) ? volumeAfter.Value - volumeBefore.Value : (double?)null;

            Console.WriteLine();
            Console.WriteLine("    RISULTATO — " + r.Call + (r.Call == "InsertGussetFeature3" ? " (marca " + r.Mark + ")" : " (Face2[])"));
            Console.WriteLine("      feature creata:  " + (born ? "SI'" : "NO") + (callError != null ? "   [eccezione: " + callError + "]" : ""));
            Console.WriteLine("      tipo feature:    " + typ);
            Console.WriteLine("      corpi:           " + bodiesBefore + " -> " + bodiesAfter);
            Console.WriteLine("      feature albero:  " + featureBefore + " -> " + featureAfter);
            Console.WriteLine("      volume mm3:      " +
                (volumeBefore.HasValue ? volumeBefore.Value.ToString("0.##", CultureInfo.InvariantCulture) : "?") + " -> " +
                (volumeAfter.HasValue ? volumeAfter.Value.ToString("0.##", CultureInfo.InvariantCulture) : "?") +
                (delta.HasValue ? "   (delta " + delta.Value.ToString("+0.##;-0.##", CultureInfo.InvariantCulture) + ")" : ""));
            if (born && delta.HasValue)
                Console.WriteLine("      attesa GP20:     ~9000 mm3 (60x60/2 x 5, meno lo smusso se c'e')");

            bool asExpected = r.FailureExpected ? !born : born;
            Console.WriteLine("      VERDETTO:        " + (asExpected ? "come previsto" : "DIVERSO DAL PREVISTO — guarda qui sopra"));

            return asExpected;
        }
        catch (Exception ex)
        {
            Console.WriteLine("    ERRORE non gestito in questa variante: " + ex.Message);
            return false;
        }
        finally
        {
            if (doc != null && !_keep)
            {
                try { sw.CloseDoc(doc.GetTitle()); } catch { }
            }
        }
    }

    /// <summary>
    /// Selects the face with Select4. If mark &lt;= 0, no mark
    /// (Select4(true, null): the recipe shipped in the bridge). If mark &gt; 0,
    /// it BUILDS the SelectData from the SelectionMgr of the document and sets
    /// .Mark = mark on it — same scheme as DraftOperations.cs and
    /// Checks/EdgeFlange/Program.cs: the mark lives on the selection DATA, not
    /// on the entity, and without building it `Select4(true, null)`
    /// marks nothing even if mark &gt; 0 were passed by mistake as null.
    /// </summary>
    private static bool SelectFace(IModelDoc2 doc, IFace2 face, int mark)
    {
        var ent = face as IEntity;
        if (ent == null) return false;
        if (mark <= 0) return ent.Select4(true, null);

        var selMgr = doc.SelectionManager as ISelectionMgr;
        if (selMgr == null) return false;
        var sd = selMgr.CreateSelectData() as SelectData;
        if (sd == null) return false;
        sd.Mark = mark;
        return ent.Select4(true, sd);
    }

    /// <summary>The two fresh structural members: 3D sketch + StructuralMemberGroup + InsertStructuralWeldment5, twice.</summary>
    private static IModelDoc2 BuildT(ISldWorks sw, string profilePath)
    {
        string model = sw.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        if (string.IsNullOrWhiteSpace(model) || !File.Exists(model))
        {
            Console.WriteLine("    non c'e' un modello di parte predefinito.");
            return null;
        }
        var doc = sw.NewDocument(model, 0, 0, 0) as IModelDoc2;
        if (doc == null) { Console.WriteLine("    NewDocument -> null"); return null; }

        if (!AddMember(doc, profilePath, 0, 0, 0, LongX1, 0, 0))
        {
            Console.WriteLine("    la barra lunga non e' nata.");
            return null;
        }
        if (!AddMember(doc, profilePath, CortaX, 0, CortaZ0, CortaX, 0, CortaZ1))
        {
            Console.WriteLine("    la barra corta non e' nata.");
            return null;
        }
        doc.ViewZoomtofit2();
        Says("T fresca: lunga 600 mm (X) + corta 300 mm (Z), profilo " + Configuration);
        return doc;
    }

    private static bool AddMember(
        IModelDoc2 doc, string profilePath,
        double xa, double ya, double za, double xb, double yb, double zb)
    {
        doc.ClearSelection2(true);
        var sketches = doc.SketchManager;
        sketches.Insert3DSketch(true);
        var seg = sketches.CreateLine(xa, ya, za, xb, yb, zb) as ISketchSegment;
        sketches.Insert3DSketch(true);
        if (seg == null) return false;

        var theGroup = doc.FeatureManager.CreateStructuralMemberGroup() as IStructuralMemberGroup;
        if (theGroup == null) return false;
        theGroup.Segments = new SketchSegment[] { (SketchSegment)seg };

        // SOLIDWORKS opens the profile library by itself and leaves it DIRTY
        // (OperazioniSaldatura.CloseLibraryAgain): a stray Ctrl+S would rewrite
        // the shared company library. Close it again, but only if it was not
        // already open before this insert.
        var sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
        bool libraryWasOpen = sw.GetOpenDocumentByName(profilePath) != null;
        var feature = doc.FeatureManager.InsertStructuralWeldment5(
            Path: profilePath,
            ConnectedSegmentsOption: 1,
            AllowProtrusion: false,
            Groups: new StructuralMemberGroup[] { (StructuralMemberGroup)theGroup },
            ConfigurationName: Configuration) as IFeature;
        doc.ClearSelection2(true);
        if (!libraryWasOpen)
        {
            var lib = sw.GetOpenDocumentByName(profilePath) as IModelDoc2;
            if (lib != null) { try { sw.CloseDoc(lib.GetTitle()); } catch { } }
        }
        return feature != null;
    }

    private static bool Cut(IModelDoc2 doc, IBody2 toCut, IBody2 against)
    {
        try
        {
            var feature = doc.FeatureManager.InsertWeldmentTrimFeature2(
                EndCond: (int)swSolidworksWeldmentEndCondOptions_e.swEndConditionTrim,
                Options: 0,
                GapValue: 0.0,
                BodiesToTrim: new Body2[] { (Body2)toCut },
                BodiesOrFaces: new Body2[] { (Body2)against }) as IFeature;
            return feature != null;
        }
        catch (Exception ex)
        {
            Console.WriteLine("    il taglio non e' riuscito: " + ex.Message);
            return false;
        }
    }

    /// <summary>Classifies the two solid bodies by bounding box in X: the longer one is the long bar.</summary>
    private static bool ClassifyBodies(IModelDoc2 doc, out IBody2 along, out IBody2 shorter)
    {
        along = null; shorter = null;
        var partDoc = doc as IPartDoc;
        var bodies = partDoc == null ? null : partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies == null || bodies.Length != 2)
        {
            Console.WriteLine("    mi aspettavo 2 corpi solidi, ne trovo " + (bodies == null ? 0 : bodies.Length) + ".");
            return false;
        }
        var a = bodies[0] as IBody2;
        var b = bodies[1] as IBody2;
        var boxA = a.GetBodyBox() as double[];
        var boxB = b.GetBodyBox() as double[];
        if (boxA == null || boxB == null || boxA.Length < 6 || boxB.Length < 6)
        {
            Console.WriteLine("    non riesco a leggere l'ingombro di uno dei due corpi.");
            return false;
        }
        double extensionXA = boxA[3] - boxA[0];
        double extensionXB = boxB[3] - boxB[0];
        if (extensionXA >= extensionXB) { along = a; shorter = b; }
        else { along = b; shorter = a; }
        return true;
    }

    /// <summary>
    /// The TOP face of the long one and the SIDE face of the short one, found
    /// by geometry (area + constant coordinate on an axis), not by index:
    /// same principle as Checks/Bracket/Program.cs (TwoFacesAtTip).
    ///
    /// THE AXES, DERIVED FROM THE GEOMETRY OF THE T (not assumed):
    /// the long one runs along X (section in the Y-Z plane, centred on Y=0,Z=0:
    /// Y and Z in [-0.04; +0.04]); the short one runs along Z, at X=0.3 (section in
    /// the X-Y plane, centred on X=0.3: X in [0.26; 0.34]) and before the trim
    /// it reaches Z=0. The two overlap in Z in [0; 0.04] — the
    /// trim removes that slice FROM THE SHORT ONE, which therefore restarts from Z=+0.04:
    /// it is the face Z=+0.04 of the LONG one (normal Z, not Y: the long one has
    /// four planar faces of the same area by the symmetry of the square
    /// section — Y=+-0.04 and Z=+-0.04 — and only Z=+0.04 is the one on which the
    /// short one is left resting) that touches the side face of the short one
    /// (normal X, at X=0.26 or X=0.34: the two faces Y=+-0.04 of the short one
    /// have the SAME area for the same reason, and must be excluded by fixing
    /// the axis at 0/X, not leaving it free).
    /// </summary>
    private static bool FindTwoFaces(
        IBody2 longBody, IBody2 shortBody,
        out IFace2 tallLongFace, out IFace2 shortSideFace)
    {
        tallLongFace = FindFace(longBody, TallLongArea, 2 /* Z */, +0.04);
        shortSideFace = FindFace(shortBody, AreaLateraleCorta, 0 /* X */, null);
        return tallLongFace != null && shortSideFace != null;
    }

    /// <summary>
    /// Looks among the faces of the body for a PLANAR face with the expected area (within
    /// tolerance) and, if requested, with the coordinate of the given axis constant and
    /// equal to the expected value. If the expected value is null, it accepts the first
    /// planar face with the right area and constant coordinate on THAT axis
    /// (whichever it is): it is needed for the short one, which has two candidate
    /// side faces (x=0.26 and x=0.34) equally valid.
    /// </summary>
    private static IFace2 FindFace(IBody2 body, double expectedArea, int axis, double? expectedDimension)
    {
        var face = body.GetFirstFace() as IFace2;
        while (face != null)
        {
            double area;
            try { area = face.GetArea(); }
            catch { area = -1; }
            if (Math.Abs(area - expectedArea) <= AreaTolerance)
            {
                var box = face.GetBox() as double[];
                if (box != null && box.Length >= 6)
                {
                    double min = box[axis];
                    double max = box[axis + 3];
                    bool planar = Math.Abs(max - min) < 1e-6;
                    bool dimensionOk = expectedDimension == null || Math.Abs(min - expectedDimension.Value) <= DimensionTolerance;
                    if (planar && dimensionOk) return face;
                }
            }
            face = face.GetNextFace() as IFace2;
        }
        return null;
    }

    private static string FormattaArea(IFace2 face)
    {
        try { return (face.GetArea() * 1e6).ToString("0", CultureInfo.InvariantCulture) + " mm2"; }
        catch { return "? mm2"; }
    }

    private static int CountBodies(IModelDoc2 doc)
    {
        var partDoc = doc as IPartDoc;
        var bodies = partDoc == null ? null : partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        return bodies == null ? 0 : bodies.Length;
    }

    private static int CountTreeFeatures(IModelDoc2 doc)
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
        var partDoc = doc as IPartDoc;
        if (partDoc == null) return null;
        double v = 0;
        try
        {
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null) return 0;
            foreach (var c in bodies)
            {
                var mp = (c as IBody2)?.GetMassProperties(1000.0) as double[];
                if (mp == null || mp.Length <= 4) return null;
                v += mp[3] * 1e9;
            }
        }
        catch { return null; }
        return v;
    }

    private static string SafeGetTypeName(IFeature f)
    {
        try { return f.GetTypeName2(); }
        catch { return "?"; }
    }
}
