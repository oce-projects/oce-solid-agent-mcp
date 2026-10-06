using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.BrokenBody
{
    /// <summary>
    /// WHAT `Check3` ANSWERS ON A HEALTHY BODY, AND WHAT ON ONE THAT
    /// IS NOT. (entry GP1)
    ///
    /// ── THE OPEN QUESTION THIS TOOL CLOSES ────────────────────────────────
    ///
    /// The API census (RAG 80p, point 4) left it written like this:
    ///
    ///   «`IBody2.Check()` and `Check2()` return `Int32` and I do not know whether zero
    ///    means *healthy* or *zero defects checked*. The wrong answer
    ///    cannot be told apart from a right one.»
    ///
    /// A `check_body_geometry` operation built on top of a zero whose
    /// meaning is unknown is the defect that raises no error in
    /// its most expensive form: it says «healthy» about anything, and whoever
    /// calls it exports an STL with a hole.
    ///
    /// ── WHY AN EXECUTABLE AND NOT A SCRIPT ────────────────────────────────
    ///
    /// Same reason as `NewPart`: `ISldWorks.NewDocument` by late
    /// binding from PowerShell 5.1 answers `TYPE_E_ELEMENTNOTFOUND`. Tried
    /// today again, so as not to remember it wrong.
    ///
    /// ── THE SIGNATURES, READ FROM INTEROP 29.5.1.1 ────────────────────────
    ///
    ///   IBody2.Check()  -> Int32
    ///   IBody2.Check2() -> Int32
    ///   IBody2.Check3   -> FaultEntity     ** IT IS A PROPERTY, NOT A METHOD **
    ///   IBody2.Diagnose() -> DiagnoseResult
    ///   IFaultEntity.Count, get_ErrorCode(i), get_Entity(i), get_Entity2(i)
    ///   IDiagnoseResult.GetGapsCount(), GetCoEdgesAtGap(i)
    ///
    /// `Check3` being a property and not a method is a trap: written `Check3()` in
    /// late binding it gives no compile error, it gives a runtime failure
    /// inside a `catch` that swallows it. The codes have a name —
    /// `swFaultEntityErrorCode_e`, 36 values from `swBodyCorrupt = 1` to
    /// `swEdgeTouchEdge = 36` — and zero is NOT among them.
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException)
            {
                Console.Error.WriteLine("SOLIDWORKS is not open.");
                return 2;
            }
            var sw = (ISldWorks)raw;

            string model = Preferred(sw) ?? SearchedOnDisk();
            if (model == null)
            {
                Console.Error.WriteLine("No part template (.prtdot) found.");
                return 2;
            }

            var doc = (IModelDoc2)sw.NewDocument(model, 0, 0, 0);
            if (doc == null)
            {
                Console.Error.WriteLine("NewDocument returned null.");
                return 2;
            }
            Console.WriteLine("test part: " + doc.GetTitle());

            // ── 1. THE HEALTHY ONE: a 100 x 100 x 20 box ──────────────────
            if (!Extrude(doc, -0.05, -0.05, 0.05, 0.05, 0.02, false))
            {
                Console.Error.WriteLine("could not extrude the healthy body.");
                return 2;
            }
            var healthy = Solids(doc);
            Tell("the healthy one: 100 x 100 x 20 extruded", healthy);
            Console.WriteLine();
            Expect("the healthy solid: Check() says 1, not 0", One(healthy[0], c => c.Check()) == 1,
                     "Check() = " + One(healthy[0], c => c.Check()));
            Expect("the healthy solid: Check2() says 0, not 1", One(healthy[0], c => c.Check2()) == 0,
                     "Check2() = " + One(healthy[0], c => c.Check2()));
            Expect("the healthy solid: no defect in Check3", Defects(healthy[0]) == 0,
                     "Check3.Count = " + Defects(healthy[0]));
            Expect("the healthy solid: no gap", Gaps(healthy[0]) == 0,
                     "gaps = " + Gaps(healthy[0]));

            // ── 2. TWO BODIES TOUCHING ALONG AN EDGE ──────────────────────
            //
            // It is the case the entry GP1 names. The second block starts
            // exactly where the first ends, offset: the two touch
            // along an edge and do not fuse, because `Merge` is false.
            if (!Extrude(doc, 0.05, 0.05, 0.15, 0.15, 0.02, true))
                Console.WriteLine("(the second block did not come out: case 2 is skipped)");
            else
                Tell("two blocks that touch along an edge", Solids(doc));

            // ── 3. A SHEET BODY, WHICH IS NOT A SOLID ─────────────────────
            //
            // It is not broken: it is OPEN. For a 3D print it is the same trouble —
            // an STL coming out of a sheet encloses nothing — and the
            // question is whether `Check3` tells it apart from a solid.
            var sheet = Surface(doc);
            if (sheet == null)
            {
                // IT IS NOT A SKIPPED CHECK, IT IS THE TOOL THAT DID NOT DO ITS
                // JOB: without the sheet nothing is measured of what
                // it exists for, and calling it green would be the worst lie.
                Console.Error.WriteLine("the surface did not come out: without the sheet body " +
                                        "this tool measures nothing.");
                return 2;
            }
            Tell("a sheet body (flat surface)", new[] { sheet });
            Console.WriteLine();

            // ── THE RED THAT THIS TOOL EXISTS TO KEEP ─────────────────────
            //
            // An OPEN body passes three checks out of four. If one day
            // `Check3` learned to see it, these three lines would turn
            // red — and that is exactly when the operation must be rewritten.
            Expect("the sheet is a sheet body, not a solid",
                     TypeName(sheet) == "sheet", "type " + TypeName(sheet));
            Expect("the sheet: Check() declares it healthy all the same", One(sheet, c => c.Check()) == 1,
                     "Check() = " + One(sheet, c => c.Check()));
            Expect("the sheet: Check2() declares it healthy all the same", One(sheet, c => c.Check2()) == 0,
                     "Check2() = " + One(sheet, c => c.Check2()));
            Expect("the sheet: Check3 finds NOTHING on an open body", Defects(sheet) == 0,
                     "Check3.Count = " + Defects(sheet));
            Expect("ONLY Diagnose() sees the gap", Gaps(sheet) >= 1,
                     "gaps = " + Gaps(sheet));

            Console.WriteLine();
            Console.WriteLine("The part stays open: look at it, then close it without saving.");
            return _broken == 0 ? 0 : 1;
        }

        // ── WHAT CAME OUT, ON 15/9/2026, ON SOLIDWORKS 2021 ───────────────
        //
        //                          Check()  Check2()  Check3.Count  gaps
        //   healthy solid             1        0           0           0
        //   two solids that touch     1        0           0           0   (each)
        //   SHEET BODY (open)         1        0           0           1
        //
        // TWO THINGS, AND BOTH CHANGE HOW THE OPERATION IS WRITTEN.
        //
        // 1. `Check()` AND `Check2()` HAVE OPPOSITE CONVENTIONS. On the same
        //    healthy body the first says 1 and the second says 0. Whoever reads
        //    a zero from `Check2()` as «false, that is broken» — or a 1 from
        //    `Check()` as «a defect» — gets a result that is consistent,
        //    stable and upside down. It was the open question of RAG 80p point 4,
        //    and the answer is worse than the uncertainty: the two conventions
        //    live together in the same interface.
        //
        // 2. AN OPEN BODY PASSES THREE CHECKS OUT OF FOUR. The sheet is not
        //    «broken»: it is a valid body that encloses nothing. `Check`,
        //    `Check2` and `Check3` all three declare it healthy, and **only
        //    `Diagnose().GetGapsCount()` sees it**. An STL coming out of there
        //    does not contain a volume, and prints as a foil.
        //
        //    A `check_body_geometry` operation built on `Check3` —
        //    which is the way entry GP1 named first — would say
        //    «no problem» exactly on the case it exists for.
        private static int _broken;

        /// <summary>
        /// The check, not the story. Without this the tool prints a
        /// table that nobody compares with last month's.
        /// </summary>
        private static void Expect(string what, bool real, string seen)
        {
            Console.WriteLine((real ? "  OK   " : "  NO   ") + what.PadRight(56) + seen);
            if (!real) _broken++;
        }

        /// <summary>
        /// What the four calls say, without interpreting them: the raw
        /// numbers next to the enum names, because the interpretation is the
        /// thing this tool exists to NOT take for granted.
        /// </summary>
        private static void Tell(string label, IBody2[] bodies)
        {
            Console.WriteLine();
            Console.WriteLine("=== " + label + " — " + bodies.Length + " corpi");
            for (int i = 0; i < bodies.Length; i++)
            {
                var c = bodies[i];
                Console.WriteLine("  corpo " + (i + 1) + "  tipo " + TypeName(c));
                Console.WriteLine("    facce            " + Number(() => c.GetFaceCount()));
                Console.WriteLine("    Check()          " + Number(() => c.Check()));
                Console.WriteLine("    Check2()         " + Number(() => c.Check2()));

                try
                {
                    var f = c.Check3 as IFaultEntity;
                    if (f == null) Console.WriteLine("    Check3           null");
                    else
                    {
                        int n = f.Count;
                        Console.WriteLine("    Check3.Count     " + n);
                        for (int k = 0; k < n; k++)
                        {
                            int code = f.get_ErrorCode(k);
                            Console.WriteLine("      defect " + k + ": code " + code +
                                              " (" + CodeName(code) + ")");
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine("    Check3           ERROR " + ex.Message); }

                try
                {
                    var d = c.Diagnose() as IDiagnoseResult;
                    if (d == null) Console.WriteLine("    Diagnose()       null");
                    else Console.WriteLine("    Diagnose gaps " + d.GetGapsCount());
                }
                catch (Exception ex) { Console.WriteLine("    Diagnose()       ERROR " + ex.Message); }
            }
        }

        private static string Number(Func<int> f)
        {
            try { return f().ToString(); }
            catch (Exception ex) { return "ERRORE " + ex.Message; }
        }

        /// <summary>An integer from a COM call, or -999 if it could not be had.</summary>
        private static int One(IBody2 c, Func<IBody2, int> f)
        {
            try { return f(c); }
            catch { return -999; }
        }

        private static int Defects(IBody2 c)
        {
            try
            {
                var f = c.Check3 as IFaultEntity;
                return f == null ? -1 : f.Count;
            }
            catch { return -999; }
        }

        private static int Gaps(IBody2 c)
        {
            try
            {
                var d = c.Diagnose() as IDiagnoseResult;
                return d == null ? -1 : d.GetGapsCount();
            }
            catch { return -999; }
        }

        private static string TypeName(IBody2 c)
        {
            try
            {
                int t = c.GetType();
                if (t == (int)swBodyType_e.swSolidBody) return "solid";
                if (t == (int)swBodyType_e.swSheetBody) return "foglio";
                if (t == (int)swBodyType_e.swWireBody)  return "filo";
                return t.ToString();
            }
            catch { return "?"; }
        }

        /// <summary>
        /// The enum name for a defect code. Zero is not in the
        /// enum: if it shows up, it is a value the documentation does not
        /// foresee and it must be said so instead of calling it «no problem».
        /// </summary>
        private static string CodeName(int code)
        {
            try
            {
                if (Enum.IsDefined(typeof(swFaultEntityErrorCode_e), code))
                    return ((swFaultEntityErrorCode_e)code).ToString();
            }
            catch { }
            return "IS NOT A VALUE OF swFaultEntityErrorCode_e";
        }

        private static IBody2[] Solids(IModelDoc2 doc)
        {
            var partDoc = doc as IPartDoc;
            if (partDoc == null) return new IBody2[0];
            var rawItems = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (rawItems == null) return new IBody2[0];
            var outside = new IBody2[rawItems.Length];
            for (int i = 0; i < rawItems.Length; i++) outside[i] = rawItems[i] as IBody2;
            return outside;
        }

        private static bool Extrude(IModelDoc2 doc, double x1, double y1,
                                    double x2, double y2, double thickness, bool separate)
        {
            try
            {
                doc.ClearSelection2(true);
                if (!SelectFrontPlane(doc))
                {
                    Console.WriteLine("  the front plane would not let itself be selected: " +
                                      PlaneNames(doc));
                    return false;
                }
                var sm = doc.SketchManager;
                sm.InsertSketch(true);
                var rect = sm.CreateCornerRectangle(x1, y1, 0, x2, y2, 0) as object[];
                Console.WriteLine("  rettangolo: " + (rect == null ? "null" : rect.Length + " segmenti"));
                sm.InsertSketch(false);
                if (!SelectLastSketch(doc))
                {
                    Console.WriteLine("  the sketch just made would not let itself be selected");
                    return false;
                }

                var f = doc.FeatureManager.FeatureExtrusion3(
                    Sd: true, Flip: false, Dir: false,
                    T1: (int)swEndConditions_e.swEndCondBlind,
                    T2: 0, D1: thickness, D2: 0,
                    Dchk1: false, Dchk2: false, Ddir1: false, Ddir2: false,
                    Dang1: 0, Dang2: 0,
                    OffsetReverse1: false, OffsetReverse2: false,
                    TranslateSurface1: false, TranslateSurface2: false,
                    // MERGE: false means «leave it a body of its own», and it is
                    // what is needed to have two solids that touch
                    // without fusing.
                    Merge: !separate,
                    UseFeatScope: true, UseAutoSelect: true,
                    T0: 0, StartOffset: 0, FlipStartOffset: false);
                return f != null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("extrusion failed: " + ex.Message);
                return false;
            }
        }

        private static IBody2 Surface(IModelDoc2 doc)
        {
            try
            {
                doc.ClearSelection2(true);
                if (!SelectTopPlane(doc)) return null;
                var sm = doc.SketchManager;
                sm.InsertSketch(true);
                sm.CreateCornerRectangle(-0.03, -0.03, 0, 0.03, 0.03, 0);
                sm.InsertSketch(false);
                if (!SelectLastSketch(doc))
                {
                    Console.WriteLine("  the surface sketch would not let itself be selected");
                    return null;
                }
                // IT LIVES ON `IModelDoc2` AND RETURNS A BOOLEAN, not on the
                // FeatureManager and not a feature: all the other
                // `Insert*RefSurface` that return a `Feature` live on the
                // FeatureManager, this one does not. It is the same family of traps
                // already paid for — «the family cannot be guessed from the name».
                if (!doc.InsertPlanarRefSurface()) return null;
                var partDoc = doc as IPartDoc;
                var rawItems = partDoc.GetBodies2((int)swBodyType_e.swSheetBody, false) as object[];
                if (rawItems == null || rawItems.Length == 0) return null;
                return rawItems[0] as IBody2;
            }
            catch (Exception ex)
            {
                Console.WriteLine("surface failed: " + ex.Message);
                return null;
            }
        }

        // W3.1-bis.2 point 10: this used to try the plane's Italian name and
        // then its English one, because the name only exists in the SOLIDWORKS
        // UI language of the machine that created the part. It now selects by
        // tree position instead (SelectBasePlane below), which needs no
        // language at all.
        private static bool SelectFrontPlane(IModelDoc2 doc)
        {
            return SelectBasePlane(doc, 1);
        }

        private static bool SelectTopPlane(IModelDoc2 doc)
        {
            return SelectBasePlane(doc, 2);
        }

        // The first three RefPlane features of the tree, same rule as
        // Checks/Rib, Checks/Fill and Checks/ExtrusionType.
        // Since Z-C21.5 the library itself (Oce.Solid.Agent.Core/References.cs,
        // BasePlane) recognises a base plane by its canonical normal through the
        // world origin instead, because a template with an extra plane ahead of
        // the three defaults fools a position-based rule with no error; this
        // probe stays positional because it is a throwaway part on SOLIDWORKS'
        // own default template and cannot reference the library (single-file
        // compile, Checks/build-checks.ps1).
        private static bool SelectBasePlane(IModelDoc2 doc, int which)
        {
            try
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
            catch { return false; }
        }

        /// <summary>
        /// The sketch just closed, taken from the tree and not by name: after the
        /// first «Schizzo1» the names change, and a wrong name here means
        /// an extrusion that does not start without saying why.
        /// </summary>
        private static bool SelectLastSketch(IModelDoc2 doc)
        {
            try
            {
                IFeature last = null;
                var f = (doc as IPartDoc).IFirstFeature();
                while (f != null)
                {
                    try { if (f.GetTypeName2() == "ProfileFeature") last = f; } catch { }
                    f = f.IGetNextFeature();
                }
                if (last == null) return false;
                doc.ClearSelection2(true);
                return doc.Extension.SelectByID2(last.Name, "SKETCH", 0, 0, 0, false, 0, null, 0);
            }
            catch { return false; }
        }

        /// <summary>The names of the planes the tree really has, for a message.</summary>
        private static string PlaneNames(IModelDoc2 doc)
        {
            try
            {
                var names = new System.Collections.Generic.List<string>();
                var f = (doc as IPartDoc).IFirstFeature();
                while (f != null)
                {
                    try { if (f.GetTypeName2() == "RefPlane") names.Add("«" + f.Name + "»"); } catch { }
                    f = f.IGetNextFeature();
                }
                return names.Count == 0 ? "the tree has no planes" : string.Join(", ", names);
            }
            catch (Exception ex) { return "could not read the tree: " + ex.Message; }
        }

        private static string Preferred(ISldWorks sw)
        {
            try
            {
                var p = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        private static string SearchedOnDisk()
        {
            // `C:\ProgramData\SolidWorks` is where SOLIDWORKS puts its
            // templates on any installation. Next to it there was the path of
            // a network drive of ONE workstation: it was of no use to anybody else and
            // it told how that drive is laid out. Now it is a variable, and whoever
            // keeps the templates elsewhere says so.
            var folders = new System.Collections.Generic.List<string>
            {
                @"C:\ProgramData\SolidWorks",
            };
            // `System.` spelled out: the SOLIDWORKS interop has its own
            // `Environment`, and with the two `using`s the bare name does not compile.
            var its = System.Environment.GetEnvironmentVariable("OCE_DOCUMENT_TEMPLATES");
            if (!string.IsNullOrWhiteSpace(its)) folders.Add(its);
            foreach (var c in folders)
            {
                if (!Directory.Exists(c)) continue;
                string[] foundItems;
                try { foundItems = Directory.GetFiles(c, "*.prtdot", SearchOption.AllDirectories); }
                catch { continue; }
                foreach (var f in foundItems)
                {
                    string b = f.ToLowerInvariant();
                    if (b.Contains(@"\mbd\")) continue;
                    if (b.Contains("0001mm")) continue;
                    if (b.Contains("ansi-inch") || b.Contains("inch")) continue;
                    return f;
                }
                if (foundItems.Length > 0) return foundItems[0];
            }
            return null;
        }
    }
}
