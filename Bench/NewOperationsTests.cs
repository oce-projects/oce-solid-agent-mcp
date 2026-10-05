using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// THE SEVEN OPERATIONS NOT YET TESTED.
    ///
    /// WHY THE FIELD «provata» EXISTS IN ai_disegno.py.
    /// A signature verified by reflection is not enough. HoleWizard5, called
    /// with the right signature and the right parameters, built a hole of 50,8 mm
    /// — two inches — with the right name in the tree and without giving any
    /// error. The defect was seen only by measuring the removed volume.
    ///
    /// Each of these seven has at least one doubt that reflection cannot
    /// resolve, and that if wrong produces a part different from the confirmed
    /// one WITHOUT giving an error:
    ///
    ///   chamfer_edges  swChamferType_e: is it really a chamfer or a fillet?
    ///   revolve         does the auto-selection resolve the profile AND the centerline?
    ///   shell           does «Outward» really mean toward the outside?
    ///   create_plane      the sign: absolute value + OptionFlip, or signed?
    ///   create_axis       is the axis then found again by name with SelectByID2?
    ///   linear_pattern  the Mark of the direction, and which way it goes
    ///   mirror          the Marks of the plane (1) and of the feature (4)
    ///
    /// Every check BUILDS and MEASURES: removed volume, bounding box, position
    /// of the cylinders. Positions are never assumed from the parameters asked —
    /// they are re-read from the geometry, because the point of the check is precisely
    /// to know whether the part ended up where it was believed.
    ///
    /// Those that pass become `provata=True` in ai_disegno.py, in the same
    /// commit in which the RAG records the measured numbers.
    /// </summary>
    internal static partial class Program
    {
        // THE LIST OF THESE CHECKS IS NO LONGER HERE.
        //
        // Up to 0.40.0 there was a `NuoveOperazioni()` that called them in a row.
        // Since entry F13 they are in the table of the groups, in `Program.cs`:
        // **in the same order**, with a name in front of each one, so that
        // `--gruppo guscio` launches just one. Two lists — one to launch them
        // all and one to launch one — would be two places to keep
        // aligned, and the day they diverge the bench launches fewer checks than
        // it shows.

        // ==================================================================
        // V3/V7. the shape stated in a sentence: NOW THEY ARE TWO FILES
        // ==================================================================
        //
        // `cases\sentence\clean_shaft_declares_profile.json` and
        // `cases\sentence\drilled_shaft_no_longer_declares_profile.json` (entry
        // F11). They became files when the expectations learned to
        // ask Â«contieneÂ» and Â«non_contieneÂ» on a sentence instead of
        // Â«ugualeÂ» on a number.
        //
        // THE RED IS ALL IN Â«non_contieneÂ». A sentence that declares the
        // profile of the three diameters on a part that no longer has that profile
        // fails nothing: it is the right description of ANOTHER
        // part, and
        // the model builds on it. It is caught only by demanding that a
        // word DISAPPEAR.


        // ==================================================================
        // R2. suppress_feature and delete_feature: NOW THEY ARE FILES
        // ==================================================================
        //
        // `cases\removal\*.json` (entry F11) - five cases, the same numbers:
        // three holes of 14 on a plate 200x100x20, the third turned off and the
        // volume that goes back EXACTLY to the one with two, turned on again and it goes back to
        // the one with three; the first deleted; and the three refusals - the invented
        // name, the suppression asked twice, the base extrusion
        // that would take its children away.
        //
        // THE FILE CLAMPS HARDER. The C# looked at the volume and the field
        // Â«soppressaÂ»; the cases also look at WHICH hole was turned off -
        // `centers_x_mm` goes from [-60, 0, 60] to [-60, 0] - and how many
        // features remain in the tree, which is the whole difference between
        // suppressing and deleting. They are launched with `--gruppo casi`.



        /// <summary>
        /// THE SHOT: a view of the part, saved to a file.
        ///
        /// WHAT IT IS FOR. The model in use on an internal GPU server (qwen3.6:35b) declares the
        /// «vision» capability: it can read images. So «showing the
        /// part to the AI» can be LITERAL, not just a list of numbers.
        ///
        /// Before building the whole chain — add-in that takes the shot, server that
        /// attaches it, capability guard — a question must be answered that no
        /// documentation can resolve: **does that model really understand a
        /// screenshot of a CAD?** Grey on grey, without dimensions, in
        /// perspective. If the answer is no, half an hour has been spent instead of
        /// two days.
        ///
        /// This method produces the material for the check: a part of which
        /// we know EVERYTHING, and its view. The comparison is made afterwards by whoever sends
        /// the image to the model and reads what it answers.
        ///
        /// `SaveBMP` is on IModelDoc2 and asks nothing of the add-in: one can
        /// take the shot without having yet written a line of operation.
        /// Verified by reflection:
        ///   IModelDoc2.SaveBMP(String FileNameIn, Int32 WidthIn, Int32 HeightIn)
        ///   IModelDoc2.ShowNamedView2(String VName, Int32 ViewId)
        ///   IModelDoc2.ViewZoomtofit2()
        /// </summary>
        private static void CaptureView()
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                CenteredPlate(doc, 200, 120, 20);
                var state0 = ReadState(doc);
                string front = FaceWithNormal(state0, "front");
                string error, holeName;
                RunWithName(doc, "hole_wizard", new Dictionary<string, object>
                {
                    ["face_uid"] = front,
                    ["variant"] = "threaded",
                    ["size"] = "M8",
                    ["through_all"] = true,
                    ["positions"] = new List<object>
                    {
                        new Dictionary<string, object> { ["x_mm"] = -70.0, ["y_mm"] = -40.0 },
                        new Dictionary<string, object> { ["x_mm"] = 70.0, ["y_mm"] = -40.0 },
                        new Dictionary<string, object> { ["x_mm"] = -70.0, ["y_mm"] = 40.0 },
                        new Dictionary<string, object> { ["x_mm"] = 70.0, ["y_mm"] = 40.0 },
                    },
                }, out error, out holeName, state0);

                // The selection must be removed: the faces highlighted in green
                // change the image and are not part of the part.
                doc.ClearSelection2(true);
                doc.ViewDisplayShaded();

                // WHERE THE SHOTS END UP: next to the bench's executable,
                // in `lavoro\viste`, which is in the .gitignore.
                //
                // It used to be an absolute path inside a temporary folder of a
                // session, user name included. Wrong twice: that
                // folder no longer exists, and the name of whoever works must not
                // leave this machine (`Checks\check-publishability.ps1`).
                string folder = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".",
                    "lavoro", "views");
                if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);

                // More views, because ONE ALONE IS NOT ENOUGH and it is worth
                // measuring: from an isometric one cannot tell whether a hole is
                // through or blind, and the back is not seen.
                // THE NAMES OF THE VIEWS ARE IN ENGLISH EVEN ON AN
                // ITALIAN INSTALLATION. With «*Isometrica» ShowNamedView2
                // does not complain and does nothing: the shot comes out with the view
                // that was there before. It is the same trap as the names of the planes
                // already documented in the RAG — and if the wrong image had
                // reached the model, the fault would have seemed its own.
                // NEITHER THE ITALIAN NAME NOR THE ENGLISH ONE: IT IS NOT THE NAME.
                // `ShowNamedView2` with «*Isometrica» and with «*Isometric» produced
                // the SAME IMAGE, that is it did nothing in
                // either case. So the numeric identifier of the standard view
                // is passed, which is the second parameter
                // and does not depend on any language:
                //   swFrontView=1 swTopView=5 swIsometricView=7 swTrimetricView=8
                var views = new[]
                {
                    Tuple.Create("isometric", (int)swStandardViews_e.swIsometricView),
                    Tuple.Create("trimetric", (int)swStandardViews_e.swTrimetricView),
                    Tuple.Create("front", (int)swStandardViews_e.swFrontView),
                    Tuple.Create("top", (int)swStandardViews_e.swTopView),
                };
                int done = 0;
                foreach (var view in views)
                {
                    try
                    {
                        doc.ShowNamedView2("", view.Item2);
                        doc.GraphicsRedraw2();
                        // THE ZOOM GOES AFTER THE REDRAW, and it must be redrawn
                        // again: on the first run the part came out cut off at the
                        // right edge because the fit worked on the
                        // old view.
                        doc.ViewZoomtofit2();
                        doc.GraphicsRedraw2();
                        string itemName = view.Item1;
                        string file = System.IO.Path.Combine(folder, "piastra_" + itemName + ".bmp");
                        if (doc.SaveBMP(file, 1024, 768) && System.IO.File.Exists(file))
                        {
                            done++;
                            Console.WriteLine("    " + itemName + ": " +
                                new System.IO.FileInfo(file).Length / 1024 + " KB");
                        }
                    }
                    catch (Exception ex) { Console.WriteLine("    " + view.Item1 + " non riuscita: " + ex.Message); }
                }
                Console.WriteLine();
                Console.WriteLine("=== scatto della vista ===");
                Console.WriteLine("    cartella: " + folder);
                Check("scatta la vista del pezzo", done > 0,
                      done + " viste su " + views.Length + " salvate in " + folder);
            }
            catch (Exception ex) { Check("scatta la vista del pezzo", false, Why(ex)); }
            finally { Close(doc); }
        }

        /// <summary>
        /// HOW MUCH THE MODEL SEES, on a part with features on it.
        ///
        /// IT IS NOT A CHECK, IT IS A CENSUS. The client said that the
        /// AI is «partially blind» and that it must be made to see well. Before
        /// deciding what to add to the state one must know what is there —
        /// measured on a real part, not on the bare plate.
        ///
        /// The part: plate 200x120x20, four M8 holes, a fillet. That is
        /// the minimum on which a designer would say «take that hole to M10».
        /// </summary>
        private static void HowMuchTheModelSees()
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                CenteredPlate(doc, 200, 120, 20);
                var state0 = ReadState(doc);
                string front = FaceWithNormal(state0, "front");
                string error, holeName;
                RunWithName(doc, "hole_wizard", new Dictionary<string, object>
                {
                    ["face_uid"] = front,
                    ["variant"] = "threaded",
                    ["size"] = "M8",
                    ["through_all"] = true,
                    ["positions"] = new List<object>
                    {
                        new Dictionary<string, object> { ["x_mm"] = -70.0, ["y_mm"] = -40.0 },
                        new Dictionary<string, object> { ["x_mm"] = 70.0, ["y_mm"] = -40.0 },
                        new Dictionary<string, object> { ["x_mm"] = -70.0, ["y_mm"] = 40.0 },
                        new Dictionary<string, object> { ["x_mm"] = 70.0, ["y_mm"] = 40.0 },
                    },
                }, out error, out holeName, state0);

                var docState = ReadState(doc);
                string json = Json(docState);

                Console.WriteLine();
                Console.WriteLine("=== quanto vede il modello (piastra + 4 fori M8) ===");
                Console.WriteLine("  lo stato pesa " + json.Length + " caratteri");
                foreach (var key in docState.Keys.OrderBy(k => k))
                {
                    var v = docState[key];
                    var itemList = v as System.Collections.IList;
                    Console.WriteLine(string.Format("  {0,-22} {1}",
                        key,
                        itemList != null ? itemList.Count + " voci" : Convert.ToString(v)));
                }

                // THE FEATURES: this is where the blindness shows. A designer who
                // says «take that hole to M10» is pointing at a feature, and
                // the model of that feature must know at least WHAT HOLE IT IS.
                Console.WriteLine();
                Console.WriteLine("  --- cosa dice lo stato di ogni lavorazione ---");
                var feature = docState["feature"] as System.Collections.IList;
                if (feature != null)
                    foreach (var f in feature)
                    {
                        var d = f as Dictionary<string, object>;
                        if (d == null) { Console.WriteLine("    " + f); continue; }
                        Console.WriteLine("    " + string.Join(", ",
                            d.Keys.Select(k => k + "=" + Convert.ToString(d[k]))));
                    }

                Console.WriteLine();
                Console.WriteLine("  --- una faccia, per esteso ---");
                var faces = docState["faces"] as System.Collections.IList;
                if (faces != null && faces.Count > 0)
                {
                    var d = faces[0] as Dictionary<string, object>;
                    if (d != null)
                        foreach (var k in d.Keys)
                            Console.WriteLine("    " + k + " = " + Describe(d[k]));
                }
                Check("censimento di cosa vede il modello", true,
                      json.Length + " caratteri, " + (feature == null ? 0 : feature.Count) +
                      " lavorazioni, " + (faces == null ? 0 : faces.Count) + " facce");
            }
            catch (Exception ex) { Check("censimento di cosa vede il modello", false, Why(ex)); }
            finally { Close(doc); }
        }

        private static string Describe(object v)
        {
            var itemList = v as System.Collections.IEnumerable;
            if (itemList != null && !(v is string))
                return "[" + string.Join(",", itemList.Cast<object>().Select(o => Convert.ToString(o))) + "]";
            return Convert.ToString(v);
        }

        /// <summary>
        /// What faces the test plate really has.
        ///
        /// IT IS NOT A CHECK, IT IS A QUESTION. On the first run inside SolidWorks
        /// fifteen cases all died on the same line — «nessuna faccia
        /// davanti» — and the cause could be in three different places: the plate
        /// built on a plane that is not the one I thought, the state that
        /// does not list that face, or `Facing` that calls it in another way.
        /// Printing the list removes the deduction.
        /// </summary>
        private static void PrintPlateFaces()
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                CenteredPlate(doc, 200, 120, 20);
                var docState = ReadState(doc);
                Console.WriteLine();
                Console.WriteLine("=== che facce ha la piastra di prova 200x120x20 ===");
                var faces = docState["faces"] as System.Collections.IList;
                Console.WriteLine("  corpi solidi: " + CountBodies(doc) +
                                  ", facce nello stato: " + (faces == null ? 0 : faces.Count));
                if (faces != null)
                    foreach (Dictionary<string, object> f in faces)
                    {
                        object v, n, area;
                        f.TryGetValue("facing", out v);
                        f.TryGetValue("normal", out n);
                        f.TryGetValue("area_mm2", out area);
                        Console.WriteLine(string.Format(
                            "  {0,-4} verso={1,-9} area={2,-10} normale={3}",
                            f["uid"], v ?? "(nessuno)", area,
                            n is System.Collections.IEnumerable && !(n is string)
                                ? "[" + string.Join(",", (n as System.Collections.IEnumerable)
                                    .Cast<object>().Select(o => Convert.ToString(o))) + "]"
                                : Convert.ToString(n)));
                    }
            }
            catch (Exception ex) { Console.WriteLine("  diagnostica fallita: " + ex.Message); }
            finally { Close(doc); }
        }

        // ==================================================================
        // 00. move_bodies — the center of mass says whether the distance is the right one
        // ==================================================================
        //
        // THE QUESTION THESE CHECKS ANSWER. `InsertMoveCopyBody2`
        // keeps the direction (TransformX/Y/Z) and the modulus (TransformValue) in
        // two distinct fields, and nowhere is it written which of the two
        // commands. The add-in passes a UNIT direction and the distance in
        // TransDist. If the right reading were another one — for example the
        // TransX/Y/Z as absolute displacement — the part would move by
        // a wrong distance WITH NO ERROR.
        //
        // The bench trusts neither SolidWorks nor the operation's internal
        // check: it re-measures the center of mass on its own.
        private static void TestMoveBodies()
        {
            Console.WriteLine();
            Console.WriteLine("=== move_bodies (add-in) ===");

            // Two equal plates, apart: 100x100x10 = 100.000 mm3 each.
            // Moving ONE of them by 50 mm in Z, the center of mass of the whole part
            // moves by half: 25 mm. It is an equality, not an estimate.
            Move("sposta una piastra di 50 in Z", 0, 0, 50, false, 0,
                   expectedBodies: 2, volumeExpected: 200000, centerMoveZ: 25);
            // NOTE for whoever passes by: on 15/9/2026 this 200000 was
            // set to 199000 on purpose, to see `bench.ps1` exit 1 and
            // name the case with expected and measured in the report. It is the
            // closing criterion of entry F2, and it must be redone this way if
            // somebody touches the report.
            //
            // And the same day, with a syntax error put here on purpose,
            // `bench.ps1` was seen to stop BEFORE launching and exit 2:
            // it is the defect that entry F2 named by name — a script that
            // exits 0 because the build failed and nobody looked.
            Move("sposta di 600 in Z", 0, 0, 600, false, 0,
                   expectedBodies: 2, volumeExpected: 200000, centerMoveZ: 300);
            Move("sposta all'indietro, -120 in Z", 0, 0, -120, false, 0,
                   expectedBodies: 2, volumeExpected: 200000, centerMoveZ: -60);

            // Copy: the originals stay, and one more appears.
            // Three bodies of 100.000, and the copy is at +600.
            // baricentroZ = (100000*0 + 100000*0 + 100000*600) / 300000 = 200.
            Move("copia una piastra a +600 in Z (il castello)", 0, 0, 600, true, 1,
                   expectedBodies: 3, volumeExpected: 300000, centerMoveZ: 200);
            // Two copies: +600 and +1200.
            // baricentroZ = (0 + 0 + 600 + 1200) / 4 = 450.
            Move("due copie, a +600 e +1200", 0, 0, 600, true, 2,
                   expectedBodies: 4, volumeExpected: 400000, centerMoveZ: 450);

            // Null displacement: the add-in must refuse it by itself, without
            // waiting for the server's validator.
            Move("spostamento nullo (deve fallire)", 0, 0, 0, false, 0,
                   expectedBodies: 0, volumeExpected: 0, centerMoveZ: 0,
                   mustFail: true);
            Move("copia con spostamento nullo (deve fallire)", 0, 0, 0, true, 1,
                   expectedBodies: 0, volumeExpected: 0, centerMoveZ: 0,
                   mustFail: true);
            // A body that is not there.
            Move("corpo inesistente (deve fallire)", 0, 0, 50, false, 0,
                   expectedBodies: 0, volumeExpected: 0, centerMoveZ: 0,
                   mustFail: true, fakeUid: "C99");
        }

        private static void Move(string itemName, double dx, double dy, double dz,
                                   bool copy, int copies,
                                   int expectedBodies, double volumeExpected,
                                   double centerMoveZ,
                                   bool mustFail = false, string fakeUid = null)
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                // Two separate bodies: the second does NOT merge with the first.
                PlateWhere(doc, 100, 100, 10, 0, 0, bottoms: true);
                PlateWhere(doc, 100, 100, 10, 200, 0, bottoms: false);
                if (CountBodies(doc) != 2)
                {
                    Check(itemName, false, "il pezzo di partenza ha " + CountBodies(doc) + " corpi invece di 2");
                    return;
                }

                var docState = ReadState(doc);
                var uid = BodiesUid(docState);
                if (uid.Count < 2) { Check(itemName, false, "lo stato elenca " + uid.Count + " corpi"); return; }

                double centerBefore = DocumentCenterZ(doc);
                var arguments = new Dictionary<string, object>
                {
                    // ONE body only is moved: it is what makes the center of mass
                    // a sensitive measurement. Moving them all, the center of mass would
                    // move by exactly d and would not tell a body
                    // forgotten behind.
                    ["bodies_uid"] = new List<string> { fakeUid ?? uid[1] },
                    ["dx_mm"] = dx,
                    ["dy_mm"] = dy,
                    ["dz_mm"] = dz,
                    ["copy"] = copy,
                };
                if (copy) arguments["copy_count"] = copies;

                string error;
                var measurements = Run(doc, "move_bodies", arguments, out error, docState);
                if (mustFail)
                {
                    Check(itemName, measurements == null && !string.IsNullOrEmpty(error),
                          measurements == null ? "rifiutato: " + Short(error) : "NON ha rifiutato");
                    return;
                }
                if (measurements == null) { Check(itemName, false, "fallita: " + Short(error)); return; }

                int bodies = CountBodies(doc);
                double volume = DocumentVolume(doc);
                double centerAfter = DocumentCenterZ(doc);
                double moved = centerAfter - centerBefore;

                bool ok = bodies == expectedBodies
                          && Math.Abs(volume - volumeExpected) < Math.Max(2, volumeExpected * 0.005)
                          && Math.Abs(moved - centerMoveZ) < 0.05;
                Check(itemName, ok, string.Format(
                    "{0} corpi (attesi {1}), volume {2:0.#} (atteso {3:0.#}), " +
                    "baricentro Z spostato di {4:0.###} mm (atteso {5:0.###})",
                    bodies, expectedBodies, volume, volumeExpected, moved, centerMoveZ));
            }
            catch (Exception ex) { Check(itemName, false, Why(ex)); }
            finally { Close(doc); }
        }

        /// <summary>The identifiers of the bodies, as the state lists them.</summary>
        private static List<string> BodiesUid(Dictionary<string, object> docState)
        {
            var outside = new List<string>();
            object rawItems;
            if (docState == null || !docState.TryGetValue("bodies", out rawItems)) return outside;
            var itemList = rawItems as System.Collections.IEnumerable;
            if (itemList == null) return outside;
            foreach (var c in itemList)
            {
                var d = c as Dictionary<string, object>;
                object u;
                if (d != null && d.TryGetValue("uid", out u) && u != null) outside.Add(u.ToString());
            }
            return outside;
        }

        /// <summary>
        /// The part's center of mass along Z, in mm. It is the measurement with which the
        /// bench checks the displacement on its own, without trusting
        /// what the operation declares to have done.
        /// </summary>
        private static double DocumentCenterZ(IModelDoc2 doc)
        {
            var partDoc = doc as IPartDoc;
            if (partDoc == null) return 0;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null || bodies.Length == 0) return 0;
            double sum = 0, volume = 0;
            foreach (var c in bodies)
            {
                var mp = (c as IBody2)?.GetMassProperties(1000.0) as double[];
                if (mp == null || mp.Length < 4) continue;
                double v = mp[3] * 1e9;
                if (v <= 0) continue;
                volume += v;
                sum += mp[2] * 1000.0 * v;
            }
            return volume > 0 ? sum / volume : 0;
        }

        /// <summary>
        /// A plate centered at (x, y). With `fondi` false it stays a SEPARATE
        /// body instead of merging with the one that is already there: it serves to
        /// have more bodies to move.
        /// </summary>
        private static void PlateWhere(IModelDoc2 doc, double L, double W, double H,
                                        double x, double y, bool bottoms)
        {
            var planes = ReferencePlanes(doc);
            doc.ClearSelection2(true);
            planes[0].Select2(false, 0);
            var sm = doc.SketchManager;
            sm.InsertSketch(true);
            sm.CreateCenterRectangle(x / 1000.0, y / 1000.0, 0,
                                     (x + L / 2) / 1000.0, (y + W / 2) / 1000.0, 0);
            sm.InsertSketch(true);
            doc.FeatureManager.FeatureExtrusion3(
                true, false, false,
                (int)swEndConditions_e.swEndCondMidPlane, 0,
                H / 1000.0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                bottoms, true, true, 0, 0, false);
            doc.ClearSelection2(true);
        }

        // ── THE SLOT AND THE CHAMFER ARE NOW FILES ────────────────────────
        //
        // `cases\slot\*.json` — fifteen cases: the same area 535,0332 mm²,
        // the same five side faces, the rotation seen on a narrow
        // plate, the three refusals with their twins.
        //
        // `cases\chamfer\*.json` — three cases: 5x5 at 45 degrees (1250 mm³, where a
        // fillet R5 would remove 537), the 3x6 bevel on two sides, and the
        // chamfer bigger than the edge.
        //
        // In both groups the numbers are **exact**: the percentage
        // tolerances that were here — 2% for the slot, 3% for the chamfer —
        // entry F3 forbids them. They are launched with `--gruppo casi`.

        // ==================================================================
        // 2. revolve NOW IS A GROUP OF FILES
        // ==================================================================
        //
        // `cases\revolve\*.json` (entry F11) - five cases: the cylinder, the
        // stepped shaft of the example, the half turn, the hollow bushing and the
        // profile straddling the axis with its twin.
        //
        // THE TOLERANCE IS THE DIFFERENCE. Here `atteso * 0.02` was accepted:
        // on the cylinder of 62.831 mm3 that is 1.256 mm3 of allowed slack, more
        // than a through M20 hole. In the files it is +-0,5 mm3, and the why of the
        // tolerance is written next to it (the computation has pi inside).

        // 3. shell — the direction of «Outward», measured on the bounding box
        // ==================================================================
        // ── THIS CHECK IS NOW A FILE ──────────────────────────────────────
        //
        // `cases/features/internal_shell.json` and `external_shell.json` (entry
        // F3). The same two numbers — 82.500 and 106.500 — plus the bounding box, the
        // faces removed, the declared direction and the material taken away, and
        // **readable by whoever does not write C#**.
        //
        // The C# below was removed, not left next to it. Two checks on the
        // same thing mean that one day one of the two will change and the
        // other will not. And above all: the old one accepted
        // `expectedVolume * 0.02` — on the 240.000 mm³ of the plate that is **4.800
        // mm³, more than a through M12 hole**. A check that accepts a hole of
        // slack does not prove the hole, and keeping it around as a safety net
        // would be keeping around the very defect that F3 closes.
        // ==================================================================
        // 4. create_plane NOW IS A GROUP OF FILES
        // ==================================================================
        //
        // `cases\plane\*.json` (entry F11) - three cases. The expectation is not on the
        // distance that the bridge copies from the request, but on WHERE THE PLANE
        // ENDED UP: `last_plane_mm`, three numbers read from the matrix of the
        // plane created. The sign is seen only this way. `--gruppo casi`.

        // 5. create_axis — and the real question: is it found again by name?
        // ==================================================================
        //
        // ── ALMOST ALL OF THIS FAMILY IS NOW TWO FILES ────────────────────
        //
        // `cases\axis\axis_from_hole_wall.json` and
        // `cases\axis\limit_axis_from_a_planar_face.json` (entry F11): the axis that
        // appears in the state, the volume that does NOT change — a reference
        // axis removes no material — and the planar face refused with
        // a reason that says what to do.
        //
        // ── WHAT STAYS HERE, AND WHY IT CANNOT BECOME A FILE ──────────────
        //
        // The real question is not «the axis exists», it is «the patterns
        // FIND IT AGAIN». They find it again with `SelectByID2(nome, "AXIS", ...)`: if
        // that line does not attach, `create_axis` succeeds, the state shows
        // the axis, and the pattern fails right afterwards — that is the operation
        // is useless and nothing says so.
        //
        // A declared case cannot prove it: `SelectByID2` is not
        // a catalog operation, it is a COM call. It stays in C#, and
        // the why stays written.
        private static void TestCreateAxis()
        {
            Console.WriteLine();
            Console.WriteLine("=== create_axis: l'asse si ritrova per nome? (add-in) ===");
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                CenteredPlate(doc, 120, 120, 15);
                var docState = ReadState(doc);
                string planarFace = FaceWithNormal(docState, "front");
                if (planarFace == null) { Check("create_axis: faccia di partenza", false, "assente"); return; }

                string error, holeName;
                var hole = RunWithName(doc, "hole_wizard", new Dictionary<string, object>
                {
                    ["face_uid"] = planarFace,
                    ["variant"] = "clearance",
                    ["size"] = "M10",
                    ["through_all"] = true,
                    ["positions"] = new List<object>
                    {
                        new Dictionary<string, object> { ["x_mm"] = 0.0, ["y_mm"] = 0.0 },
                    },
                }, out error, out holeName, docState);
                if (hole == null) { Check("create_axis: foro di prova", false, Short(error)); return; }

                docState = ReadState(doc);
                string cylindrical = FaceNotPlanar(docState);
                if (cylindrical == null)
                {
                    Check("create_axis: la parete del foro compare nello stato", false,
                          "nessuna faccia con «planar: false»: il modello non avrebbe " +
                          "niente da passare a create_axis");
                    return;
                }

                string axisName;
                var measurements = RunWithName(doc, "create_axis", new Dictionary<string, object>
                {
                    ["face_uid"] = cylindrical,
                }, out error, out axisName, docState);
                if (measurements == null) { Check("create_axis: l'asse per il gancio", false, Short(error)); return; }
                string axis = measurements.ContainsKey("axis") ? Convert.ToString(measurements["axis"]) : "";
                if (string.IsNullOrEmpty(axis))
                {
                    // IT IS NOT THE RED OF THIS CHECK: without the name there is
                    // nothing to attach to, and saying so is better than attaching
                    // the empty string and calling it a failure of the hook.
                    Check("create_axis: l'operazione dice come si chiama l'asse", false,
                          "le misure non hanno «axis»");
                    return;
                }

                // THE CHECK THAT COUNTS: «linear_pattern» and «circular_pattern»
                // find the axis again with SelectByID2(name, "AXIS", ...). If
                // that line does not attach, create_axis succeeds, the state shows
                // the axis, and the pattern fails right afterwards — that is
                // the operation is useless and nothing says so.
                bool attached = false;
                try
                {
                    doc.ClearSelection2(true);
                    attached = doc.Extension.SelectByID2(axis, "AXIS", 0, 0, 0, false, 1, null, 0);
                }
                catch { }
                Check("create_axis: l'asse si aggancia con SelectByID2(\"AXIS\")", attached,
                      attached ? "asse «" + axis + "»" : "la ripetizione non lo trovera' mai");
            }
            catch (Exception ex) { Check("create_axis", false, Why(ex)); }
            finally { Close(doc); }
        }
        // ==================================================================
        // 6. linear_pattern and 7. mirror NOW ARE TWO GROUPS OF FILES
        // ==================================================================
        //
        // `cases\patterns\*.json` - three holes at pitch 40 along an edge, the
        // 3x2 grid on two edges, and the direction taken from a CURVED
        // edge, which must be refused.
        //
        // `cases\mirror\*.json` - the hole at (60, 25) mirrored on the right
        // plane, which must appear at (-60, 25), and the invented plane.
        //
        // THE FILE CLAMPS HARDER THAN THE C# IT REPLACES. Here the mirror
        // was verified by looking for ONE copy far from the seed; the case writes the
        // two centers, both of them, and the y that does NOT flip. And the check
        // Â«nessuno spigolo curvo e' offerto al modelloÂ» that was here was
        // green regardless: it counted the edges with Â«drittoÂ»: false, and
        // that field in the state does not exist - it is called Â«curvoÂ». After a
        // through hole the curved edges are TWO, the model can name them, and
        // the refusal is a real case instead of a road believed
        // closed. They are launched with `--gruppo casi`.


        // ==================================================================
        // reflection: like Run, but it also says what the feature is called
        // ==================================================================
        private static Dictionary<string, object> RunWithName(
            IModelDoc2 doc, string operation, Dictionary<string, object> arguments,
            out string error, out string createdFeature, object docState = null)
        {
            error = null;
            createdFeature = null;
            var constructor = _tOperations.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
            var op = constructor.Invoke(new object[] { _sw, (Action<string>)(s => { }) });
            object objectState = (docState == null || docState is Dictionary<string, object>)
                ? ReadObjectState(doc) : docState;

            var run = _tOperations.GetMethod("Run",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var outcome = run.Invoke(op, new object[] { doc, operation, arguments, objectState, null });
            if (!(bool)_tOutcome.GetField("Succeeded").GetValue(outcome))
            {
                error = _tOutcome.GetField("Error").GetValue(outcome) as string;
                return null;
            }
            createdFeature = _tOutcome.GetField("CreatedFeature").GetValue(outcome) as string;
            try { doc.EditRebuild3(); } catch { }
            return _tOutcome.GetField("Measurements").GetValue(outcome) as Dictionary<string, object>;
        }

        // ==================================================================
        // measurements on the part — always from the geometry, never from the parameters
        // ==================================================================

        /// <summary>
        /// The solid volume in mm3. `GetMassProperties(densita)[3]` is the
        /// volume in m3: measured, not remembered.
        /// </summary>
        private static double DocumentVolume(IModelDoc2 doc)
        {
            double v = 0;
            // A DRAWING has no bodies. Since the session can keep
            // two documents open, the active document may well
            // be the drawing, and here one arrived with a null IPartDoc.
            var partDoc = doc as IPartDoc;
            if (partDoc == null) return 0;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null) return 0;
            foreach (var c in bodies)
            {
                var mp = (c as IBody2).GetMassProperties(1000.0) as double[];
                if (mp != null && mp.Length > 4) v += mp[3] * 1e9;
            }
            return v;
        }

        private static int CountBodies(IModelDoc2 doc)
        {
            var partDoc = doc as IPartDoc;
            if (partDoc == null) return 0;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            return bodies == null ? 0 : bodies.Length;
        }

        private static double[] DocumentBoundingBox(IModelDoc2 doc)
        {
            var b = DocumentBox(doc);
            if (b == null) return null;
            return new[] { b[3] - b[0], b[4] - b[1], b[5] - b[2] };
        }

        private static int CountPlanes(IModelDoc2 doc)
        {
            int n = 0;
            var f = (doc as IPartDoc).IFirstFeature();
            while (f != null)
            {
                try { if (f.GetTypeName2() == "RefPlane") n++; } catch { }
                try { f = f.IGetNextFeature(); } catch { break; }
            }
            return n;
        }

        /// <summary>
        /// The origin of the last reference plane of the tree, in mm.
        /// IMathTransform.ArrayData: 9 rotation numbers, then 3 of
        /// translation. It is the answer to the question «which way did it end up».
        /// </summary>
        private static double[] LastPlaneTranslation(IModelDoc2 doc)
        {
            IFeature last = null;
            var f = (doc as IPartDoc).IFirstFeature();
            while (f != null)
            {
                try { if (f.GetTypeName2() == "RefPlane") last = f; } catch { }
                try { f = f.IGetNextFeature(); } catch { break; }
            }
            if (last == null) return null;
            try
            {
                var plane = last.GetSpecificFeature2() as IRefPlane;
                if (plane == null) return null;
                var transformed = plane.Transform as IMathTransform;
                var v = transformed == null ? null : transformed.ArrayData as double[];
                if (v == null || v.Length < 12) return null;
                return new[] { v[9] * 1000, v[10] * 1000, v[11] * 1000 };
            }
            catch { return null; }
        }

        // ==================================================================
        // reading the state
        // ==================================================================
        private static string FaceWithNormal(Dictionary<string, object> docState, string facing)
        {
            var faces = docState["faces"] as System.Collections.IList;
            if (faces == null) return null;
            foreach (Dictionary<string, object> f in faces)
            {
                object v;
                if (f.TryGetValue("facing", out v) && Convert.ToString(v) == facing)
                    return Convert.ToString(f["uid"]);
            }
            return null;
        }

        private static string FaceNotPlanar(Dictionary<string, object> docState)
        {
            var faces = docState["faces"] as System.Collections.IList;
            if (faces == null) return null;
            foreach (Dictionary<string, object> f in faces)
            {
                object v;
                if (f.TryGetValue("planar", out v) && !Convert.ToBoolean(v))
                    return Convert.ToString(f["uid"]);
            }
            return null;
        }

        private static string LongestEdge(Dictionary<string, object> docState)
        {
            var edges = docState.ContainsKey("edges") ? docState["edges"] as System.Collections.IList : null;
            if (edges == null || edges.Count == 0) return null;
            // The state already sorts them by decreasing length.
            return Convert.ToString((edges[0] as Dictionary<string, object>)["uid"]);
        }

        // ==================================================================
        // the test plate
        // ==================================================================
        /// <summary>
        /// A plate centered on ALL THREE axes, thickness included.
        ///
        /// The `Plate` of the old checks extrudes from z=0 onward: it is fine
        /// for a hole on the big face, but on the side faces the origin
        /// of the sketch falls on the EDGE, not at the center, and a hole asked
        /// at (0,0) would come out half outside the material. With the end
        /// condition «mid plane» the part lies between -H/2 and +H/2 and the origin falls
        /// where one expects it.
        /// </summary>
        private static void CenteredPlate(IModelDoc2 doc, double L, double W, double H)
        {
            var planes = ReferencePlanes(doc);
            doc.ClearSelection2(true);
            planes[0].Select2(false, 0);          // front: the big face looks toward +Z
            var sm = doc.SketchManager;
            sm.InsertSketch(true);
            sm.CreateCenterRectangle(0, 0, 0, L / 2000.0, W / 2000.0, 0);
            sm.InsertSketch(true);
            doc.FeatureManager.FeatureExtrusion3(
                true, false, false,
                (int)swEndConditions_e.swEndCondMidPlane, 0,
                H / 1000.0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false);
            doc.ClearSelection2(true);
        }

    }
}
