using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swdimxpert;

// =============================================================================
//  DimXpert — can it be reached from the core, without the add-in? (entry Z-H3, and with it GT8)
// =============================================================================
//
//  The entry said «the same tick is needed» as Utilities. The reflection of the
//  evening of 24/9/2026 says something else: `IModelDocExtension.get_DimXpertManager`
//  sits in the interop of the CORE, and from there one reaches `IDimXpertPart` with
//  AutoDimensionScheme, InsertDatum, InsertGtol. This probe tests it on a
//  part, instead of believing it.
//
//  THE PART: a plate 100 x 60 x 10 with a through hole of 20 in the centre,
//  made with ONE sketch (rectangle and circle together): no face to
//  choose. The hole is the thing that a dimensioning scheme must recognise.
//
//  THE CHECK IN BOTH DIRECTIONS: before the scheme the DimXpert annotations are
//  ZERO; after they are more than zero, and among the recognised features there is the
//  hole. A scheme that «succeeds» without adding anything is not a scheme.
//
//  USAGE
//      .\Checks\DimXpert\bin\DimXpert.exe            [--keep]
// =============================================================================

internal static class Program
{
    private static int _step;
    private static void Says(string what)
    {
        _step++;
        Console.WriteLine("  " + _step.ToString("00") + ". " + what);
    }

    private static int Main(string[] arguments)
    {
        bool keep = Array.IndexOf(arguments, "--keep") >= 0;
        Console.WriteLine();
        Console.WriteLine("DIMXPERT — from the core, without the add-in? (entry Z-H3)");
        Console.WriteLine("  plate 100 x 60 x 10 with a 20 through hole");
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS does not respond: " + ex.Message); return 2; }
        Says("attached to SOLIDWORKS " + sw.RevisionNumber());

        IModelDoc2 doc = null;
        try
        {
            string model = sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            doc = (IModelDoc2)sw.NewDocument(model, 0, 0, 0);
            if (doc == null) { Console.WriteLine("  NewDocument -> null"); return 4; }

            doc.ClearSelection2(true);
            if (!SelectBasePlane(doc, 2))
            { Console.WriteLine("  cannot find «Piano superiore»"); return 4; }
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCornerRectangle(0, 0, 0, 0.100, 0.060, 0);
            doc.SketchManager.CreateCircleByRadius(0.050, 0.030, 0, 0.010);
            doc.SketchManager.InsertSketch(true);
            var f = doc.FeatureManager.FeatureExtrusion3(
                true, false, false, 0, 0, 0.010, 0.0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false);
            if (f == null) { Console.WriteLine("  the extrusion did not come out"); return 4; }
            double vol = doc.Extension.CreateMassProperty().Volume * 1e9;
            double expected = 100 * 60 * 10 - Math.PI * 10 * 10 * 10;
            Says("plate with hole: volume " + vol.ToString("0.0") + " mm^3 (expected " + expected.ToString("0.0") + ")");

            // ── THE DIMXPERT MANAGER, FROM THE CORE ──────────────────────
            string config = doc.ConfigurationManager.ActiveConfiguration.Name;
            var handler = doc.Extension.get_DimXpertManager(config, true) as IDimXpertManager;
            if (handler == null)
            {
                Console.WriteLine("  get_DimXpertManager -> null: it CANNOT be reached from the core.");
                return 5;
            }
            Says("DimXpertManager from the core: schema «" + handler.SchemaName + "»");

            var partDoc = handler.DimXpertPart as IDimXpertPart;
            if (partDoc == null) { Console.WriteLine("  DimXpertPart -> null"); return 5; }

            int annBefore = partDoc.GetAnnotationCount();
            int loadBefore = partDoc.GetFeatureCount();
            Says("before the schema: " + loadBefore + " features, " + annBefore + " annotations");

            // MEASURED on the evening of 24/9: with the default options and nothing
            // selected, `AutoDimensionScheme` answers False and does nothing.
            // The options have no fields for the references: the references are
            // SELECTED first, with a mark for the primary, the secondary
            // and the tertiary. Which marks, nobody knows: they are tried.
            //
            // The plate sits in X 0..100, Y 0..10, Z -60..0 (the sketch on the
            // top plane has the sketch Y towards the model -Z).
            var options = partDoc.GetAutoDimSchemeOption();
            bool done = false;
            int annAfter = annBefore, loadAfter = loadBefore;
            if (options != null)
            {
                options.PartType = (int)swDimXpertAutoDimSchemePartType_e.swDimXpertAutoDimSchemePartType_Prismatic;
                options.ToleranceType = (int)swDimXpertAutoDimSchemeToleranceType_e.swDimXpertAutoDimSchemeToleranceType_PlusMinus;
                options.ScopeAllFeature = true;
                options.FeatureFilters = 0xFFFF;
                foreach (var marks in new[] { new[] { 0, 0, 0 }, new[] { 1, 2, 4 }, new[] { 1, 2, 3 } })
                {
                    doc.ClearSelection2(true);
                    bool s1 = marks[0] == 0 || doc.Extension.SelectByID2("", "FACE", 0.020, 0.0, -0.010, true, marks[0], null, 0);
                    bool s2 = marks[1] == 0 || doc.Extension.SelectByID2("", "FACE", 0.0, 0.005, -0.010, true, marks[1], null, 0);
                    bool s3 = marks[2] == 0 || doc.Extension.SelectByID2("", "FACE", 0.020, 0.005, 0.0, true, marks[2], null, 0);
                    try { done = partDoc.AutoDimensionScheme(options); } catch (Exception ex) { Console.WriteLine("      raised: " + ex.Message); }
                    annAfter = partDoc.GetAnnotationCount();
                    loadAfter = partDoc.GetFeatureCount();
                    Says("marks " + string.Join("/", marks) + " (selections " + s1 + "/" + s2 + "/" + s3 +
                         "): AutoDimensionScheme -> " + done + ", " + loadAfter + " features, " + annAfter + " annotations");
                    // WE DO NOT LOOK AT `done`: measured on the evening of 24/9, it answers
                    // False even when it has put six right dimensions. We look at
                    // whether the annotations are there.
                    if (annAfter > 3) break;
                }
                doc.ClearSelection2(true);
            }

            // What it recognised: the types of the features.
            bool aHole = false;
            var car = partDoc.GetFeatures() as object[];
            if (car != null)
                foreach (var o in car)
                {
                    var c = o as IDimXpertFeature;
                    if (c == null) continue;
                    string typ = c.Type.ToString();
                    Console.WriteLine("      feature «" + c.Name + "»: " + typ);
                    if (typ.IndexOf("Hole", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typ.IndexOf("Cylinder", StringComparison.OrdinalIgnoreCase) >= 0) aHole = true;
                }
            // THE VALUES, not only the count: the name carries the value in
            // brackets («Distance2@Plane10(60)» on an English installation). Six dimensions with random
            // numbers would be six dimensions, and none of them right.
            var expectedValues = new System.Collections.Generic.List<double> { 20, 10, 60, 100, 50, 30 };
            var ann = partDoc.GetAnnotations() as object[];
            if (ann != null)
                foreach (var o in ann)
                {
                    var a = o as IDimXpertAnnotation;
                    if (a == null) continue;
                    Console.WriteLine("      annotation «" + a.Name + "»: " + a.Type);
                    int ap = a.Name.LastIndexOf('('), ch = a.Name.LastIndexOf(')');
                    double v;
                    if (ap > 0 && ch > ap && double.TryParse(a.Name.Substring(ap + 1, ch - ap - 1),
                            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                        expectedValues.RemoveAll(x => Math.Abs(x - v) < 1e-6);
                }
            Console.WriteLine("      expected values not found: " + (expectedValues.Count == 0 ? "none" : string.Join(", ", expectedValues)));

            Console.WriteLine();
            if (expectedValues.Count > 0)
            {
                Console.WriteLine("THE SCHEMA DIMENSIONED, BUT NOT EVERYTHING: missing " + string.Join(", ", expectedValues) + ".");
                return 8;
            }
            if (annAfter <= annBefore)
            {
                Console.WriteLine("THE SCHEMA ADDED NOTHING: " + annBefore + " -> " + annAfter +
                                  " annotations. DimXpert from the core responds but does not dimension.");
                return 6;
            }
            if (!aHole)
            {
                Console.WriteLine("The schema added annotations but the hole is not among the features:");
                Console.WriteLine("something was dimensioned, we do not know what.");
                return 7;
            }
            Console.WriteLine("HOLDS: from the core, without the add-in, a DimXpert schema recognizes the hole and adds");
            Console.WriteLine(annAfter + " annotations where before there were " + annBefore + ", with the right values — and");
            Console.WriteLine("AutoDimensionScheme answered " + done + ". Z-H3 does not wait for any checkbox.");
            return 0;
        }
        catch (Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            Console.WriteLine();
            Console.WriteLine("EXCEPTION: " + ex.GetType().Name + ": " + ex.Message);
            return 9;
        }
        finally
        {
            if (doc != null && !keep)
            {
                try { sw.CloseDoc(doc.GetTitle()); Says("part closed without saving"); } catch { }
            }
        }
    }

    // W3.1-bis.2 point 10: base planes by position, not by their localized name
    // (a plane name is only valid in the SOLIDWORKS UI language of the machine
    // that created the part). The first three RefPlane features of the
    // tree, same rule as Checks/Rib, Checks/Fill and
    // Checks/ExtrusionType. NOTE for whoever touches this next: since
    // Z-C21.5 the bridge itself (Oce.Solid.Agent.Core/References.cs, BasePlane)
    // no longer trusts tree position either — it recognises a base plane by its
    // canonical normal through the world origin, because a template with one
    // extra plane ahead of the three defaults fools a position-based rule with
    // no error. This probe still uses position because it is a throwaway part
    // built by SOLIDWORKS' own default template and it cannot reference the
    // library (single-file compile, see Checks/build-checks.ps1);
    // if the template ever grows a leading plane, this rule silently picks the
    // wrong one.
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
