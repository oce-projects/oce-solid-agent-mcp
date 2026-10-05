using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.cosworks;

// =============================================================================
//  Bracket — the measurement that sizes section S (entry S3)
// =============================================================================
//
//  WHY A BRACKET AND NOT A REAL PART
//
//  Because for a bracket the answer is known **without the computer**. A real
//  part would give a number that cannot be contradicted, and a number that
//  cannot be contradicted is not a check: it is something you look at and believe.
//
//  Beam 200 x 20 x 10 mm, fixed at one end, 100 N at the tip:
//
//      M = F * L   = 100 * 0,200          = 20 N*m
//      I = b*h^3/12 = 0,020 * 0,010^3 /12 = 1,6667e-9 m^4
//      c = h/2                             = 0,005 m
//      sigma = M*c/I = 20 * 0,005 / 1,6667e-9 = 6,0e7 Pa = 60 MPa
//
//  Exactly sixty. If the API says 60 +/- 10%, the chain holds and section S
//  can be budgeted. If it says anything else, **the family is not built**
//  until we know why — it is written in the entry, and it is the biggest risk
//  of the project: here a silent defect does not make a wrong part,
//  it makes a wrong calculation that somebody signs.
//
//  ── WHAT IS ALREADY KNOWN, AND WHAT IS NOT ──────────────────────────────────
//
//  The chain of signatures is established by reflection (entry S3, and 17 expectations in
//  `Checks\signatures-api.ps1`). What reflection **cannot** tell is
//  the format of `Object DispArray` and `Object RefGeom` of `AddRestraint` and
//  `AddForce`: the signature does not declare it, and no reading will find out.
//  That is why this probe exists.
//
//  USAGE
//      .\Checks\Bracket\bin\Bracket.exe
//      .\Checks\Bracket\bin\Bracket.exe --tieni     leaves the part open
// =============================================================================

internal static class Program
{
    // The CLSID of the Simulation add-in, from `HKLM\SOFTWARE\Classes\SldWorks.Simulation\CLSID`.
    private const string ClsidSimulation = "{1CC93C1D-B0C9-4641-A2AD-D19A5700052E}";

    // swsStressComponent_e / swsStrengthUnit_e / swsAnalysisStudyType_e /
    // swsRestraintType_e / swsForceType_e / swsLinearUnit_e — measured by
    // reflection, not remembered.
    private const int VonMises = 9;
    private const int NewtonSuMm2 = 3;
    private const int StaticStudy = 0;
    private const int FixedGeometry = 0;
    private const int ForceOrMoment = 0;
    private const int Millimeters = 0;
    private const int NewtonAndMeters = 0;   // swsForceUnit_e.swsForceUnitNOrNm

    private const double LengthMm = 200.0;
    private const double WidthMm = 20.0;
    private const double HeightMm = 10.0;
    private const double ForceN = 100.0;

    /// <summary>The exact name in the DIN library, brackets included.</summary>
    private const string MaterialName = "1.0037 (S235JR)";

    private static int _step;

    private static void Says(string what)
    {
        _step++;
        Console.WriteLine("  " + _step.ToString("00") + ". " + what);
    }

    private static int Main(string[] arguments)
    {
        bool keep = Array.IndexOf(arguments, "--tieni") >= 0;

        double expected = ExpectedSigma();
        Console.WriteLine();
        Console.WriteLine("MENSOLA — la misura che dimensiona la sezione S (voce S3)");
        Console.WriteLine("  trave " + LengthMm + " x " + WidthMm + " x " + HeightMm +
                          " mm, incastrata, " + ForceN + " N in punta");
        Console.WriteLine("  sigma = M*c/I = " + expected.ToString("0.0") + " MPa   <- calcolata a mano");
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex)
        {
            Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message);
            Console.WriteLine("Aprilo e rilancia.");
            return 2;
        }
        Says("agganciato a SOLIDWORKS " + sw.RevisionNumber());

        // ── THE ADD-IN, BEFORE ANYTHING ELSE ─────────────────────────────
        //
        // If it is not loaded we exit AT ONCE, with a message that says what to
        // do. Finding out after creating the part means leaving a document
        // lying around for nothing.
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
        Says("Simulation caricato");

        IModelDoc2 doc = null;
        try
        {
            doc = CreateBeam(sw);
            if (doc == null) return 4;

            // ── THE MATERIAL BEFORE THE STUDY, NOT AFTER ─────────────────
            //
            // This block used to come AFTER `CreateNewStudy3`, and it was in the
            // wrong order. The material was applied, `GetMaterialPropertyName2`
            // read it back, everything looked fine — and `RunAnalysis`
            // answered **19 = swsRunAnalysisErrorMaterialNotDefined**.
            //
            // The study photographs the part when it is born. Born on a part
            // with no material, it stays without material, and whatever is attached
            // to the document afterwards does not reach it.
            //
            // *The read-back told the truth: the material really was there. But it
            // answered a different question from the one that mattered — «does
            // the part have it?» instead of «does the study have it?».*
            // ── THE MATERIAL GOES ON THE PART, AND IS READ BACK ──────────
            //
            // The first attempt went through Simulation:
            // `SolidManager -> componente -> corpo -> SetLibraryMaterial("", nome)`.
            // It answered **0**, which looks like success, and then `RunAnalysis`
            // gave **19 = swsRunAnalysisErrorMaterialNotDefined**: the
            // material was not there. An empty library does not mean «the
            // default one», and the zero returned was not a confirmation.
            //
            // *A return value that says zero while it has done nothing
            // is exactly the defect this project hunts* — here
            // in somebody else's house, and it showed only because the next step
            // had the courage to complain.
            //
            // Now it is applied with the SOLIDWORKS API, with the full path of
            // the library, **and read back**: `SetMaterialPropertyName2`
            // returns void, so the only way to know whether it worked is
            // to ask the part what it is wearing.
            var partDoc = doc as IPartDoc;
            string archive = ArchiveDin();
            if (archive == null) { Console.WriteLine("  non trovo l'archivio DIN"); return 5; }
            partDoc.SetMaterialPropertyName2("", archive, MaterialName);

            string db2;
            string put = partDoc.GetMaterialPropertyName2("", out db2);
            if (string.IsNullOrEmpty(put))
            {
                Console.WriteLine("  il materiale NON si e' attaccato: rileggendo trovo il vuoto.");
                Console.WriteLine("    archivio: " + archive);
                Console.WriteLine("    nome:     " + MaterialName);
                return 5;
            }
            Says("materiale «" + put + "» (riletto, non assunto)");

            // ── THE REAL TYPES, NOT CALLS BY NAME ────────────────────────
            //
            // The first draft of this probe called everything by name with
            // `InvokeMember`, to be able to say WHICH name was missing instead of
            // failing to compile. `CreateNewStudy3` rejected it:
            // **DISP_E_TYPEMISMATCH**, because late binding cannot
            // marshal an `out Int32`. With the interop referenced the
            // problem does not exist, and the compiler checks the signatures —
            // which is better than a message at runtime.
            var callout = addin as CwAddincallback;
            if (callout == null)
            {
                Console.WriteLine("  l'add-in non e' un CwAddincallback: " + addin.GetType().FullName);
                return 5;
            }
            CosmosWorks cw = callout.CosmosWorks;
            if (cw == null) { Console.WriteLine("  .CosmosWorks e' null"); return 5; }
            CWModelDoc active = cw.ActiveDoc;
            if (active == null) { Console.WriteLine("  .ActiveDoc e' null: Simulation non vede il pezzo"); return 5; }
            CWStudyManager handler = active.StudyManager;
            if (handler == null) { Console.WriteLine("  .StudyManager e' null"); return 5; }
            Says("Simulation vede il documento");

            // ── 1. LO STUDIO ─────────────────────────────────────────────
            int errStudio;
            CWStudy studio = handler.CreateNewStudy3("mensola-s3", StaticStudy, 0, out errStudio);
            if (studio == null)
            {
                Console.WriteLine("  CreateNewStudy3 -> null, errore " + errStudio);
                return 5;
            }
            Says("studio statico creato (errore " + errStudio + ")");

            // ── 2. THE MATERIAL, WHICH LIVES ON THE BODY ─────────────────
            //
            // Three hops: study -> SolidManager -> component -> body. The
            // material is NOT on the study, and it is one of the things the
            // census by reflection had already corrected.

            // ── 3. RESTRAINT AND LOAD: HERE IS THE UNKNOWN ───────────────
            //
            // `AddRestraint(Int32, Object DispArray, Object RefGeom, out Int32)`
            // — the signature does NOT say what to put in `DispArray`. Reflection
            // got this far and stopped, and it is the reason this
            // probe exists.
            //
            // The hypothesis tried first: an `object[]` of `IFace2`,
            // which is the shape the whole SOLIDWORKS API uses for «a
            // set of entities».
            CWLoadsAndRestraintsManager loadsHandler = studio.LoadsAndRestraintsManager;
            if (loadsHandler == null) { Console.WriteLine("  LoadsAndRestraintsManager e' null"); return 5; }

            IFace2 fixedFace, loadFace;
            if (!TwoFacesAtTip(doc, out fixedFace, out loadFace)) return 4;
            Says("trovate le due facce di testa");

            int errV;
            CWRestraint constraint = loadsHandler.AddRestraint(
                FixedGeometry, new object[] { fixedFace }, null, out errV);
            Console.WriteLine("      AddRestraint -> " + (constraint == null ? "null" : "oggetto") +
                              ", errore " + errV);
            if (constraint == null)
            {
                Console.WriteLine();
                Console.WriteLine("  IL FORMATO DI DispArray NON E' UN object[] DI IFace2.");
                Console.WriteLine("  E' l'incognita che questa sonda doveva togliere, e non l'ha tolta.");
                Console.WriteLine("  Prossima ipotesi da provare: selezionare la faccia nel modello e");
                Console.WriteLine("  passare null, oppure un array di Dispatch presi da SelectionMgr.");
                return 6;
            }
            Says("incastro applicato");

            // The force: direction -Y, at the tip.
            // ── THE FORCE NEEDS A DIRECTION, THE FIXED SUPPORT DOES NOT ──
            //
            // `AddRestraint` accepted `object[] { IFace2 }` with `RefGeom`
            // null: for a «grounded» fixed support there is no direction to
            // give. `AddForce` with the same shape answered **error 2**,
            // and the reason is that one: a force without a direction is not a
            // force.
            //
            // The reference is the TOP PLANE, the same one the beam
            // was drawn on: its normal is the Y axis, that is the
            // direction in which the bracket bends. Taking another one
            // would give an axial load, which on this beam produces no
            // bending and would make us read a small, plausible number —
            // *the worst way to be wrong, because it does not look like
            // an error.*
            object planeRef = ReferencePlane(doc, 2);
            if (planeRef == null) { Console.WriteLine("  non trovo il piano di riferimento"); return 6; }

            int errF;
            CWForce force = loadsHandler.AddForce(
                ForceOrMoment, new object[] { loadFace }, planeRef, out errF);
            Console.WriteLine("      AddForce -> " + (force == null ? "null" : "oggetto") +
                              ", errore " + errF);
            if (force == null) { Console.WriteLine("  AddForce non ha accettato nemmeno col piano."); return 6; }

            // THE VALUE IS SET AFTERWARDS, on the returned object: `AddForce` with
            // four arguments has no place to put it. Established by
            // reading `ICWForce`, which has `SetForceComponentValues2` and the
            // pair `ForceBeginEdit` / `ForceEndEdit`.
            force.ForceBeginEdit();
            force.Unit = NewtonAndMeters;
            //                    X      Y     Z        values
            force.SetForceComponentValues2(false, true, false, 0.0, ForceN, 0.0);
            int forceOutcome = force.ForceEndEdit();
            Says("carico di " + ForceN + " N lungo Y (esito " + forceOutcome + ")");
            if (forceOutcome != 0)
            {
                Console.WriteLine("  ForceEndEdit non e' tornato 0: il carico non e' stato accettato.");
                return 6;
            }

            // ── 4. MESH AND RUN ──────────────────────────────────────────
            //
            // `RunAnalysis()` and NOT `MeshAndRun()`: the second returns **void**,
            // and cannot say whether it went well. Established by reflection, and it is
            // the same rule this project has already paid for elsewhere —
            // *the value returned by whoever did the thing is not the measure
            // of the thing*, but here not even a value comes back.
            // ── CONVERGENCE, NOT A SINGLE MEASUREMENT ────────────────────
            //
            // The first run gave **30.33 MPa** against 60 expected: exactly half.
            // With a 3.4 mm mesh over a thickness of 10 there are
            // three elements through the thickness, and three elements cannot
            // describe a bending: the peak stress comes out low.
            //
            // *A single number proves nothing, and this holds both
            // ways: if it had given 60 at the first shot I would not have known whether
            // it was right or lucky.* We refine the mesh and look at WHERE IT GOES.
            double[] sides = { 4.0, 2.0, 1.0, 0.6 };
            double last = double.NaN;

            Console.WriteLine();
            Console.WriteLine("  lato mesh    sigma max    scarto da " + expected.ToString("0.0") + " MPa");
            Console.WriteLine("  ---------    ---------    -------------------");

            foreach (double side in sides)
            {
                int eMesh = studio.CreateMesh(Millimeters, side, side / 20.0);
                if (eMesh != 0)
                {
                    Console.WriteLine("  " + side.ToString("0.0").PadLeft(7) + " mm    mesh fallita (esito " + eMesh + ")");
                    continue;
                }
                int runCode = studio.RunAnalysis();
                if (runCode != 0)
                {
                    Console.WriteLine("  " + side.ToString("0.0").PadLeft(7) + " mm    giro fallito (esito " + runCode + ")");
                    continue;
                }

                int errS;
                object read = studio.Results.GetMinMaxStress(
                    VonMises, 0, 0, null, NewtonSuMm2, out errS);
                double sigma = TrueMaximum(read);
                if (double.IsNaN(sigma))
                {
                    Console.WriteLine("  " + side.ToString("0.0").PadLeft(7) + " mm    non si legge (errore " + errS + ")");
                    continue;
                }
                last = sigma;
                double sc = (sigma - expected) / expected * 100.0;
                Console.WriteLine("  " + side.ToString("0.0").PadLeft(7) + " mm  " +
                                  sigma.ToString("0.00").PadLeft(9) + "    " +
                                  (sc >= 0 ? "+" : "") + sc.ToString("0.0") + " %");
            }

            // ── THE VERDICT, ON THE LAST AND FINEST MESH ─────────────────
            if (double.IsNaN(last))
            {
                Console.WriteLine();
                Console.WriteLine("Nessuna mesh e' arrivata in fondo: non c'e' un numero da giudicare.");
                return 7;
            }

            double deviation = Math.Abs(last - expected) / expected * 100.0;
            Console.WriteLine();
            Console.WriteLine("  a mano:            " + expected.ToString("0.0") + " MPa");
            Console.WriteLine("  l'API, mesh fitta: " + last.ToString("0.0") + " MPa");
            Console.WriteLine("  scarto:            " + deviation.ToString("0.0") + " %");
            Console.WriteLine();
            if (deviation <= 10.0)
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
            if (doc != null && !keep)
            {
                try { sw.CloseDoc(doc.GetTitle()); } catch { }
            }
        }
    }

    /// <summary>
    /// The maximum stress from the array of `GetMinMaxStress`.
    ///
    /// THE SHAPE IS `[nodoMin, valoreMin, nodoMax, valoreMax]`, and the first
    /// version of this probe took the **maximum of the four numbers**.
    /// So it caught `11875`, which is the NODE NUMBER, and printed it as
    /// MPa: 11,875 MPa against 60 expected, a deviation of 19,691%.
    ///
    /// *It was lucky that it was absurd.* Had the peak node had
    /// a low number — say 58 — I would have read «58 MPa against 60 expected,
    /// deviation 3%» and would have written that the chain holds. A wrong and
    /// plausible number cannot be told apart from a measurement.
    /// </summary>
    private static double TrueMaximum(object read)
    {
        var a = read as Array;
        if (a == null || a.Length < 4) return double.NaN;
        return Convert.ToDouble(a.GetValue(3));
    }

    private static double ExpectedSigma()
    {
        double L = LengthMm / 1000.0, b = WidthMm / 1000.0, h = HeightMm / 1000.0;
        double M = ForceN * L;
        double I = b * h * h * h / 12.0;
        return M * (h / 2.0) / I / 1e6;   // Pa -> MPa
    }

    /// <summary>
    /// The DIN library, looked up on disk instead of written by hand.
    ///
    /// It lives under `lang\<lingua>\sldmaterials`, and the language changes from
    /// one installation to another: here there are two, Italian and
    /// English. A constant path would work on this machine and on
    /// no other.
    /// </summary>
    private static string ArchiveDin()
    {
        string root = @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\lang";
        var its = System.Environment.GetEnvironmentVariable("SW_INSTALL_PATH");
        if (!string.IsNullOrEmpty(its)) root = System.IO.Path.Combine(its, "lang");
        if (!System.IO.Directory.Exists(root)) return null;
        foreach (var language in System.IO.Directory.GetDirectories(root))
        {
            var p = System.IO.Path.Combine(language, "sldmaterials", "SolidWorks DIN Materials.sldmat");
            if (System.IO.File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// The reference plane, taken as an OBJECT and not as a name.
    ///
    /// `AddForce` wants the geometry, not the string: we select by name
    /// and fish the object back out of the selection manager. And we deselect
    /// at once: a selection left lying around changes what the next call
    /// does, and it is the kind of coupling that cannot be seen.
    /// </summary>
    // W3.1-bis.2 point 10: `which` is now the tree position of the base plane
    // (1 = front, 2 = top, 3 = right), not its localized name — see
    // SelectBasePlane below.
    private static object ReferencePlane(IModelDoc2 doc, int which)
    {
        if (!SelectBasePlane(doc, which))
        {
            Console.WriteLine("      could not select base plane #" + which);
            return null;
        }
        var sel = doc.SelectionManager as ISelectionMgr;
        object plane = sel == null ? null : sel.GetSelectedObject6(1, -1);
        doc.ClearSelection2(true);
        return plane;
    }

    /// <summary>The beam, drawn and extruded. Null if it did not come out.</summary>
    private static IModelDoc2 CreateBeam(ISldWorks sw)
    {
        string model = sw.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        var doc = (IModelDoc2)sw.NewDocument(model, 0, 0, 0);
        if (doc == null) { Console.WriteLine("  NewDocument -> null"); return null; }

        SelectBasePlane(doc, 2);
        doc.SketchManager.InsertSketch(true);
        doc.SketchManager.CreateCornerRectangle(
            0, 0, 0, LengthMm / 1000.0, WidthMm / 1000.0, 0);
        doc.SketchManager.InsertSketch(true);

        var f = doc.FeatureManager.FeatureExtrusion3(
            true, false, false, 0, 0, HeightMm / 1000.0, 0.0,
            false, false, false, false, 0, 0, false, false, false, false,
            true, true, true, 0, 0, false);
        if (f == null) { Console.WriteLine("  l'estrusione non e' venuta"); return null; }
        doc.ViewZoomtofit2();
        Says("trave creata: " + LengthMm + " x " + WidthMm + " x " + HeightMm + " mm");
        return doc;
    }

    /// <summary>
    /// The two end faces: the one at x=0 (fixed support) and the one at x=L (load).
    ///
    /// They are chosen by POSITION and not by index: the order of the faces is not
    /// promised anywhere, and taking them by index would mean that the
    /// check changes meaning if SOLIDWORKS renumbers them.
    /// </summary>
    private static bool TwoFacesAtTip(IModelDoc2 doc, out IFace2 fixture, out IFace2 forceLoad)
    {
        fixture = null; forceLoad = null;
        var partDoc = doc as IPartDoc;
        var bodies = partDoc == null ? null : partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies == null || bodies.Length == 0) { Console.WriteLine("  nessun corpo solido"); return false; }

        double L = LengthMm / 1000.0;
        var face = (bodies[0] as IBody2).GetFirstFace() as IFace2;
        while (face != null)
        {
            var box = face.GetBox() as double[];
            if (box != null && box.Length >= 6)
            {
                bool slender = Math.Abs(box[3] - box[0]) < 1e-6;   // zero thickness in X
                if (slender && Math.Abs(box[0]) < 1e-6) fixture = face;
                if (slender && Math.Abs(box[0] - L) < 1e-6) forceLoad = face;
            }
            face = face.GetNextFace() as IFace2;
        }
        if (fixture == null || forceLoad == null)
        {
            Console.WriteLine("  non trovo le due facce di testa (x=0 e x=" + L + ")");
            return false;
        }
        return true;
    }

    // ── Late calls ──────────────────────────────────────────────────────
    //
    // The Simulation objects arrive as `__ComObject`: without the `cosworks`
    // interop referenced they have no type, and are called by name.
    // We could reference it — but this way the probe says clearly WHICH name
    // it is looking for when it does not find it, instead of failing to compile.

    private static object Take(object o, string itemName)
    {
        if (o == null) return null;
        try
        {
            return o.GetType().InvokeMember(
                itemName, System.Reflection.BindingFlags.GetProperty, null, o, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine("      ." + itemName + " alza: " + Root(ex).Message);
            return null;
        }
    }

    private static object Invoke(object o, string itemName, object[] arg)
    {
        if (o == null) return null;
        try
        {
            return o.GetType().InvokeMember(
                itemName, System.Reflection.BindingFlags.InvokeMethod, null, o, arg);
        }
        catch (Exception ex)
        {
            Console.WriteLine("      ." + itemName + "(...) alza: " + Root(ex).Message);
            return null;
        }
    }

    private static Exception Root(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex;
    }

    // W3.1-bis.2 point 10: base planes by position, not by their localized name.
    // The first three RefPlane features of the tree, same rule as
    // Checks/Rib, Checks/Fill and Checks/ExtrusionType.
    // Since Z-C21.5 the library itself (Oce.Solid.Agent.Core/References.cs,
    // BasePlane) recognises a base plane by its canonical normal through the
    // world origin instead, because a template with an extra plane ahead of the
    // three defaults fools a position-based rule with no error; this probe
    // stays positional because it is a throwaway part on SOLIDWORKS' own
    // default template and cannot reference the library (single-file compile,
    // Checks/build-checks.ps1).
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
