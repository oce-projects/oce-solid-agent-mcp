using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
// ALIAS AND NOT `using static`, as in ClientPort.cs: the bench has an `Open` and a `Close` of its own.
using Mcp = Shared.McpClient;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// THE PART-SIDE DEFECTS OF THE DRAWING TEST OF 12/10/2026, THROUGH THE BRIDGE (0.131.0).
    ///
    /// The owner asked the bridge to draw a bracket with a drawing and a PDF, and six things went
    /// wrong on the part side. Each has its case here, red on the bridge of 0.130.2 and green on this
    /// one, and each READS THE DOCUMENT BY COM by the reference it holds, never the answer alone: the
    /// answer of the bridge is what is being tested.
    ///
    ///   1. `undo` of the whole plan: not here. The main line has its own undo (one recorded entry per
    ///      plan, compared before and after), and the two cases of 0.131.0 (`undo`, `cuts`) tested the
    ///      undo of that release, which was not ported (bridge 0.171.0);
    ///   2. `set_property` with a drawing in front writes the property where the title block reads it
    ///      (the part of the views for a `$PRPSHEET` field) and says so;
    ///   3. `slot` angle_deg is as the face is SEEN: 0 horizontal, 90 vertical, on a face seen in the
    ///      front view and on one seen in the right view (where the first axis of the face is the
    ///      vertical one);
    ///   4. `save_document` refuses a path with no separator after the drive letter, and the document is
    ///      left as it was (it was left renamed);
    ///   5. the first base flange of a new document;
    ///   6. `apply_material` with a name that matches several steels lists them, all of them up to the
    ///      cap, and says how many are left out.
    ///
    /// The group needs the bridge in bin\ (build.ps1), or the one in the environment variable
    /// OCE_BRIDGE: pointing it at the bridge of the release before is how the cases are seen RED.
    /// </summary>
    internal static partial class Program
    {
        private static int _pf131Id = 1000;

        /// <summary>How many candidates the refusal of an ambiguous material lists (SolidWorksMaterials.CandidatesListed).</summary>
        private const int Pf131Cap = 20;

        /// <summary>A tools/call to the bridge, the text of the answer; waits ten minutes (a plan can take two).</summary>
        private static string Pf131(Mcp.Bridge p, string tool, string arguments)
        {
            int id = ++_pf131Id;
            return ResponseText(Mcp.AskWithin(p, id, Call(id, tool, arguments), 600000));
        }

        private static string Pf131Escape(string path) { return path.Replace("\\", "\\\\"); }

        private static void TestPartFixes131()
        {
            const string Name = "part fixes 131";
            var executable = TheBridge();
            if (executable == null)
            {
                BenchError(Name, "cannot find the bridge: build it with .\\build.ps1, or set OCE_BRIDGE");
                return;
            }
            Mcp.Bridge p = null;
            try
            {
                p = Mcp.Open(executable);
                if (p == null) { BenchError(Name, "the bridge did not start"); return; }
                var hello = Mcp.Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"bench\",\"version\":\"1\"}}}");
                if (hello == null) { BenchError(Name, "the bridge did not answer initialize"); return; }
                Mcp.Write(p, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");

                // OCE_PF131_ONLY=property,slot,save,flange,material runs only those: the whole group
                // is half an hour of SOLIDWORKS, and a shared session is held for forty minutes at most.
                string only = System.Environment.GetEnvironmentVariable("OCE_PF131_ONLY");
                Func<string, bool> wanted = name => string.IsNullOrWhiteSpace(only) || (";" + only.Replace(",", ";") + ";").Contains(";" + name + ";");
                if (wanted("property")) Pf131PropertyWithADrawingInFront(p);
                if (wanted("slot")) Pf131SlotAngle(p);
                if (wanted("save")) Pf131SaveWithAMalformedPath(p);
                if (wanted("flange")) Pf131FirstFlangeAfterFailedSteps(p);
                if (wanted("material")) Pf131MaterialCandidates(p);
            }
            catch (Exception ex)
            {
                BenchError(Name, Why(ex));
            }
            finally
            {
                if (p != null) p.Dispose();   // kills the bridge process
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // The L bracket of the drawing test, and the plan that went wrong on it (case 5)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>The L bracket of the drawing test: sheet metal 8 thick, bend radius 8, 60 deep, legs 120 and 70.</summary>
        private const string Bracket131 =
            "{\"steps\":[{\"operation\":\"base_flange\",\"arguments\":{\"plane\":\"front\"," +
            "\"points\":[[120,0],[0,0],[0,70]],\"thickness_mm\":8,\"radius_mm\":8,\"depth_mm\":60}}]}";

        /// <summary>
        /// The plan of the drawing test, the one whose undo went wrong: a material, two guided holes in the
        /// long leg and a slot in the inner face of the short one, with the identifiers of <paramref name="state"/>.
        ///
        /// THE IDENTIFIERS ARE FOUND BY THE GEOMETRY, NOT WRITTEN (bridge 0.171.1). The case of 0.131.0 wrote F2,
        /// F3, E3, E4, E5, E10, E14 and E16, the ranks the bracket had on that bridge; the bracket has two faces of
        /// 6240 mm2 (the top and the bottom of the long leg) and two of 3240 mm2 (the two sides of the short one), and
        /// the ranks of equal areas are not the same on the main line, so on 16/11/2026 the plan did not build and the
        /// case ended as a bench error. The faces and edges are the ones the declared case
        /// `l_bracket_drawing_end_to_end` names with its selectors, found here in the answer of `inspect_part`.
        /// Null, with <paramref name="why"/>, when one of them is not found exactly once.
        /// </summary>
        private static string Pf131ThreeSteps(string state, out string why)
        {
            string top = Pf131FaceUid(state, "up", 6240, out why);
            string tip = top == null ? null : Pf131EdgeUid(state, "Z", 120, 8, double.NaN, out why);
            string near = tip == null ? null : Pf131EdgeUid(state, "X", double.NaN, 8, 0, out why);
            string far = near == null ? null : Pf131EdgeUid(state, "X", double.NaN, 8, 60, out why);
            string slot = far == null ? null : Pf131SlotOnly(state, 90, out why);
            if (slot == null) return null;
            return "{\"steps\":[" +
                "{\"operation\":\"apply_material\",\"arguments\":{\"material\":\"S235JR\"}}," +
                "{\"operation\":\"hole_wizard\",\"arguments\":{\"face_uid\":\"" + top + "\",\"variant\":\"clearance\",\"size\":\"M10\"," +
                "\"through_all\":true,\"positions\":[" +
                "{\"x_mm\":90,\"y_mm\":30,\"anchor\":{\"x\":{\"edge_uid\":\"" + tip + "\",\"distance_mm\":30},\"y\":{\"centered_between\":[\"" + near + "\",\"" + far + "\"]}}}," +
                "{\"x_mm\":30,\"y_mm\":30,\"anchor\":{\"x\":{\"edge_uid\":\"" + tip + "\",\"distance_mm\":90},\"y\":{\"centered_between\":[\"" + near + "\",\"" + far + "\"]}}}]}}," +
                slot + "]}";
        }

        /// <summary>
        /// The slot of the drawing test on the inner face of the short leg (x = 8, looking right, 3240 mm2), as one
        /// step: 35 from the top edge of that face (y = 70), centred between its two edges along Y (z = 0 and z = 60).
        /// Null, with <paramref name="why"/>, when a face or an edge is not found exactly once.
        ///
        /// The face is the LARGEST planar face looking right, not the one of 3240 mm2: the tip of the long leg looks right
        /// too (480 mm2), and a slot alone that succeeds takes 304.03 mm2 off the face, so the next attempt finds no face
        /// of 3240 (bench of 16/11/2026, 22:21: «0 faces looking right with 3240 mm2», attempt 3).
        /// </summary>
        private static string Pf131SlotOnly(string state, int angle, out string why)
        {
            why = null;
            string face = Pf131FaceUid(state, "right");
            if (face == null) why = "no planar face looking right in the state";
            string top = face == null ? null : Pf131EdgeUid(state, "Z", 8, 70, double.NaN, out why);
            string front = top == null ? null : Pf131EdgeUid(state, "Y", 8, double.NaN, 0, out why);
            string back = front == null ? null : Pf131EdgeUid(state, "Y", 8, double.NaN, 60, out why);
            if (back == null) return null;
            return "{\"operation\":\"slot\",\"arguments\":{\"face_uid\":\"" + face + "\",\"x_mm\":35,\"y_mm\":30,\"length_mm\":30,\"width_mm\":11," +
                "\"angle_deg\":" + angle.ToString(CultureInfo.InvariantCulture) + ",\"through_all\":true,\"anchor\":{\"x\":{\"edge_uid\":\"" + top + "\",\"distance_mm\":35}," +
                "\"y\":{\"centered_between\":[\"" + front + "\",\"" + back + "\"]}}}}";
        }

        /// <summary>The uid of the one planar face of the state that looks that way and has that area (within 1 mm2), or null with the reason.</summary>
        private static string Pf131FaceUid(string stateText, string facing, double areaMm2, out string why)
        {
            why = null;
            var faces = Mcp.Inside(Mcp.Obj(stateText), "faces") as object[];
            var found = new List<string>();
            foreach (var f in faces ?? new object[0])
            {
                var d = f as Dictionary<string, object>;
                if (d == null || Convert.ToString(Mcp.Inside(d, "facing")) != facing) continue;
                if (Math.Abs(Number(Mcp.Inside(d, "area_mm2")) - areaMm2) <= 1.0) found.AddRange(new[] { UidOf(d) });
            }
            if (found.Count == 1) return found[0];
            why = found.Count + " faces looking " + facing + " with " + areaMm2.ToString(CultureInfo.InvariantCulture) + " mm2 in the state (" +
                  string.Join(", ", found) + "): exactly one was expected";
            return null;
        }

        /// <summary>
        /// The uid of the one straight edge of the state along <paramref name="along"/> whose middle point is at the
        /// given coordinates (within 0.5 mm; NaN for a coordinate that does not matter), or null with the reason.
        /// </summary>
        private static string Pf131EdgeUid(string stateText, string along, double x, double y, double z, out string why)
        {
            why = null;
            var edges = Mcp.Inside(Mcp.Obj(stateText), "edges") as object[];
            var found = new List<string>();
            double[] wanted = { x, y, z };
            foreach (var e in edges ?? new object[0])
            {
                var d = e as Dictionary<string, object>;
                if (d == null || Convert.ToString(Mcp.Inside(d, "along")) != along) continue;
                var point = Mcp.Inside(d, "point_mm") as object[];
                if (point == null || point.Length < 3) continue;
                bool here = true;
                for (int i = 0; i < 3; i++)
                    if (!double.IsNaN(wanted[i]) && !(Math.Abs(Number(point[i]) - wanted[i]) <= 0.5)) here = false;
                if (here) found.AddRange(new[] { UidOf(d) });
            }
            if (found.Count == 1) return found[0];
            string at = "";
            foreach (double v in wanted) at += (at.Length == 0 ? "" : ", ") + (double.IsNaN(v) ? "any" : v.ToString(CultureInfo.InvariantCulture));
            why = found.Count + " edges along " + along + " at (" + at + ") in the state (" + string.Join(", ", found) + "): exactly one was expected";
            return null;
        }

        /// <summary>The error of the first step that failed in an answer of `build`, for a bench error that says why; the start of the answer when there is none.</summary>
        private static string Pf131FirstError(string answer)
        {
            int failed = answer.IndexOf("\"succeeded\":false", StringComparison.OrdinalIgnoreCase);
            int error = failed < 0 ? -1 : answer.IndexOf("\"error\":", failed, StringComparison.Ordinal);
            if (error < 0) return Short(answer);
            string text = answer.Substring(error);
            return text.Length > 600 ? text.Substring(0, 600) + "..." : text;
        }

        private static int Pf131Built(string answer)
        {
            int n = 0, at = 0;
            while ((at = answer.IndexOf("\"succeeded\":true", at, StringComparison.OrdinalIgnoreCase)) >= 0) { n++; at += 10; }
            return n;
        }

        // ─────────────────────────────────────────────────────────────────
        // 2. set_property with a drawing in front
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// A saved part with a drawing of it in front (the sheet and its three views), `set_property` of a name
        /// the title block links with `$PRPSHEET`: the property must land on the PART, which is where the title
        /// block reads it, and the answer must say so. A name that no field reads is written on the drawing, as
        /// asked, and the answer must say that nothing shows it.
        /// </summary>
        private static void Pf131PropertyWithADrawingInFront(Mcp.Bridge p)
        {
            IModelDoc2 part = null;
            string partPath = Path.Combine(Expectations.Work, "pf131_prop.SLDPRT");
            string drawingPath = Path.Combine(Expectations.Work, "pf131_prop.SLDDRW");
            string drawingTitle = null;
            try
            {
                part = NewPart();
                Pf131(p, "inspect_part", "{}");
                var made = Pf131(p, "build",
                    "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"top\",\"length_mm\":60,\"width_mm\":40," +
                    "\"thickness_mm\":10,\"centered\":true}},{\"operation\":\"save_document\",\"arguments\":{\"path\":\"" + Pf131Escape(partPath) + "\"}}]}");
                if (Pf131Built(made) != 2) { BenchError("property with a drawing", "the part was not built and saved: " + Short(made)); return; }
                var drawn = Pf131(p, "build",
                    "{\"steps\":[{\"operation\":\"create_drawing\",\"arguments\":{\"paper_size\":\"A3\"}}," +
                    "{\"operation\":\"standard_views\",\"arguments\":{\"model\":\"" + Pf131Escape(partPath) + "\"}}]}");
                if (Pf131Built(drawn) != 2) { BenchError("property with a drawing", "the drawing was not made: " + drawn.Substring(0, Math.Min(900, drawn.Length))); return; }
                var all = _sw.GetDocuments() as object[] ?? new object[0];
                IModelDoc2 drawing = null;
                foreach (var o in all)
                {
                    // the drawing of THIS part: other documents may be open on a shared SOLIDWORKS
                    var d = o as IModelDoc2;
                    if (d != null && d.GetType() == (int)swDocumentTypes_e.swDocDRAWING &&
                        d.GetTitle().StartsWith(Path.GetFileNameWithoutExtension(partPath), StringComparison.OrdinalIgnoreCase))
                    {
                        drawing = d;
                        drawingTitle = d.GetTitle();
                    }
                }
                Pf131(p, "inspect_part", "{}");
                var wanted = Pf131(p, "measure", "{\"steps\":[{\"operation\":\"title_block\",\"arguments\":{}}]}");
                if (wanted.IndexOf("PRPSHEET", StringComparison.OrdinalIgnoreCase) < 0 &&
                    wanted.IndexOf("from the part", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    BenchError("property with a drawing", "the title block of this template has no $PRPSHEET field, so the case has nothing to measure: " + Short(wanted));
                    return;
                }

                // THE NAME COMES FROM THE TITLE BLOCK ITSELF: the first property it reads from the model of the views.
                string field = Pf131FirstModelProperty(wanted);
                if (field == null) { BenchError("property with a drawing", "no property read with $PRPSHEET among: " + Short(wanted)); return; }
                const string value = "PF131 BRACKET";
                var set = Pf131(p, "build",
                    "{\"steps\":[{\"operation\":\"set_property\",\"arguments\":{\"name\":\"" + field + "\",\"value_text\":\"" + value + "\"}}]}");
                string onPart = Pf131Property(part, field);
                string onDrawing = Pf131Property(drawing, field);
                Check("property with a drawing: the answer is a success",
                      Succeeded(set), Short(set));
                Check("property with a drawing: «" + field + "» is on the PART of the views, where the title block reads it (read by COM)",
                      onPart == value, "on the part «" + onPart + "», on the drawing «" + onDrawing + "»");
                Check("property with a drawing: and the answer says where it went and why",
                      // Only the sentence of the route (PropertyOperations.PropertyRouteForDrawing): «the part» alone is in
                      // almost any answer, and would have passed on 0.130.2 too (review of 0.131.0).
                      set.IndexOf("from the MODEL of its views", StringComparison.Ordinal) >= 0,
                      Short(set));

                var stray = Pf131(p, "build",
                    "{\"steps\":[{\"operation\":\"set_property\",\"arguments\":{\"name\":\"PF131NoField\",\"value_text\":\"x\"}}]}");
                Check("property with a drawing: a name no field of the title block reads is written on the drawing and the answer says that nothing shows it",
                      Succeeded(stray) && Pf131Property(drawing, "PF131NoField") == "x" && stray.IndexOf("NO FIELD OF THE TITLE BLOCK", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(stray));
            }
            finally
            {
                try { if (!string.IsNullOrEmpty(drawingTitle)) CloseTitle(drawingTitle); } catch { }
                Close(part);
                try { CloseTitle(Path.GetFileName(partPath)); } catch { }
                RemoveQuietly(partPath);
                RemoveQuietly(drawingPath);
            }
        }

        /// <summary>
        /// The first property the title block reads from the MODEL of the views (a field linked with $PRPSHEET), as the
        /// answer of `title_block` lists it («name  (from the part, not from the drawing)»), or null.
        /// </summary>
        private static string Pf131FirstModelProperty(string titleBlockAnswer)
        {
            int at = titleBlockAnswer.IndexOf("properties_wanted_by_title_block", StringComparison.Ordinal);
            if (at < 0) return null;
            int open = titleBlockAnswer.IndexOf('[', at), close = open < 0 ? -1 : titleBlockAnswer.IndexOf(']', open);
            if (close < 0) return null;
            foreach (var entry in titleBlockAnswer.Substring(open + 1, close - open - 1).Split(','))
            {
                string text = entry.Trim().Trim('"');
                int mark = text.IndexOf("  (from the part", StringComparison.Ordinal);
                if (mark > 0 && !text.StartsWith("SW-", StringComparison.Ordinal)) return text.Substring(0, mark);
            }
            return null;
        }

        /// <summary>The document-level custom property, as the text stored in it; the empty string when it is not there.</summary>
        private static string Pf131Property(IModelDoc2 doc, string itemName)
        {
            if (doc == null) return "(no document)";
            try
            {
                string text, resolved; bool wasResolved, linked;
                var manager = doc.Extension.get_CustomPropertyManager("");
                int result = manager.Get6(itemName, false, out text, out resolved, out wasResolved, out linked);
                return result == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent ? "" : (text ?? "");
            }
            catch (Exception ex) { return "(" + ex.Message + ")"; }
        }

        // ─────────────────────────────────────────────────────────────────
        // 3. slot angle_deg is as the face is seen
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// A block 200 x 120 x 60 (centered on the origin in X and Y, Z from 0 to 60) with a blind slot 51 x 11 x 5
        /// in a face, and the long axis of the slot read from the geometry by COM: the two cylindrical faces of the
        /// ends are the only cylinders of the part, and the vector between their axes IS the long axis, in model
        /// coordinates, whatever the face and the sketch said.
        /// </summary>
        private static void Pf131SlotAngle(Mcp.Bridge p)
        {
            // (facing, angle asked, expected long axis, how the face is seen)
            var cases = new[]
            {
                Tuple.Create("front", 0, 'X', "front view: horizontal is X"),
                Tuple.Create("front", 90, 'Y', "front view: vertical is Y"),
                Tuple.Create("right", 0, 'Z', "right view: horizontal is Z"),
                Tuple.Create("right", 90, 'Y', "right view: vertical is Y"),
                Tuple.Create("up", 0, 'X', "top view: horizontal is X"),
                Tuple.Create("up", 90, 'Z', "top view: vertical is Z"),
            };
            foreach (var c in cases)
            {
                IModelDoc2 part = null;
                try
                {
                    part = NewPart();
                    Pf131(p, "inspect_part", "{}");
                    var block = Pf131(p, "build",
                        "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"front\"," +
                        "\"length_mm\":200,\"width_mm\":120,\"thickness_mm\":60,\"centered\":true}}]}");
                    if (!Succeeded(block)) { BenchError("slot angle", "the block was not built: " + Short(block)); continue; }
                    var look = Pf131(p, "inspect_part", "{}");
                    string uid = Pf131FaceUid(look, c.Item1);
                    if (uid == null) { BenchError("slot angle", "no planar face looks " + c.Item1); continue; }

                    // x_mm, y_mm are model coordinates on the face (the first and second axis that remain);
                    // the second axis: 10 off the centre on the front face (Y, +-60), 30 on the two others (Z, 0 to 60: the middle of the thickness).
                    var slot = Pf131(p, "build",
                        "{\"steps\":[{\"operation\":\"slot\",\"arguments\":{\"face_uid\":\"" + uid + "\"," +
                        "\"x_mm\":0,\"y_mm\":" + (c.Item1 == "front" ? "10" : "30") + ",\"length_mm\":51,\"width_mm\":11,\"angle_deg\":" + c.Item2 +
                        ",\"through_all\":false,\"depth_mm\":5}}]}");
                    if (!Succeeded(slot)) { BenchError("slot angle", "the slot was not cut: " + Short(slot)); continue; }

                    string ends;
                    var span = Pf131SlotSpan(part, out ends);
                    char longest = span == null ? '?' : "XYZ"[Array.IndexOf(span, span.Max())];
                    bool singleAxis = span != null && span.Count(v => v > 1.0) == 1;
                    Check("slot angle " + c.Item2 + " on the face looking " + c.Item1 + ": the long axis of the slot runs along " + c.Item3 +
                          " in the model (" + c.Item4 + ")",
                          singleAxis && longest == c.Item3 && Math.Abs(span.Max() - 40.0) < 0.1,
                          "centres of the end arcs differ by X " + (span == null ? "?" : span[0].ToString("0.##", CultureInfo.InvariantCulture)) +
                          ", Y " + (span == null ? "?" : span[1].ToString("0.##", CultureInfo.InvariantCulture)) +
                          ", Z " + (span == null ? "?" : span[2].ToString("0.##", CultureInfo.InvariantCulture)) + " (" + ends + ")");
                }
                finally { Close(part); }
            }
        }

        /// <summary>The uid of the largest planar face of the state that looks that way ("front", "right", "up"...), or null.</summary>
        private static string Pf131FaceUid(string stateText, string facing)
        {
            var state = Mcp.Obj(stateText);
            var faces = Mcp.Inside(state, "faces") as object[];
            if (faces == null) return null;
            string best = null; double area = -1;
            foreach (var f in faces)
            {
                var d = f as Dictionary<string, object>;
                if (d == null) continue;
                if (!(Mcp.Inside(d, "planar") is bool) || !(bool)Mcp.Inside(d, "planar")) continue;
                if (Convert.ToString(Mcp.Inside(d, "facing")) != facing) continue;
                double a = Number(Mcp.Inside(d, "area_mm2"));
                if (a > area) { area = a; best = UidOf(d); }
            }
            return best;
        }

        /// <summary>
        /// |dX|, |dY|, |dZ| between the axes of the two cylindrical faces of the part, in mm, or null when the part
        /// does not have exactly two cylinders. <paramref name="ends"/> prints them for the red.
        /// </summary>
        private static double[] Pf131SlotSpan(IModelDoc2 part, out string ends)
        {
            ends = "";
            var origins = new List<double[]>();
            var partDoc = part as IPartDoc;
            var bodies = partDoc == null ? null : partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            if (bodies == null) { ends = "no body"; return null; }
            foreach (var bo in bodies)
            {
                var body = bo as IBody2;
                var faces = body == null ? null : body.GetFaces() as object[];
                if (faces == null) continue;
                foreach (var fo in faces)
                {
                    var face = fo as IFace2;
                    var surface = face == null ? null : face.IGetSurface();
                    if (surface == null || !surface.IsCylinder()) continue;
                    var cp = surface.CylinderParams as double[];
                    if (cp == null || cp.Length < 7) continue;
                    origins.Add(new[] { cp[0] * 1000, cp[1] * 1000, cp[2] * 1000 });
                }
            }
            if (origins.Count != 2) { ends = origins.Count + " cylindrical faces"; return null; }
            ends = string.Format(CultureInfo.InvariantCulture, "({0:0.##},{1:0.##},{2:0.##}) and ({3:0.##},{4:0.##},{5:0.##})",
                origins[0][0], origins[0][1], origins[0][2], origins[1][0], origins[1][1], origins[1][2]);
            return new[]
            {
                Math.Abs(origins[0][0] - origins[1][0]),
                Math.Abs(origins[0][1] - origins[1][1]),
                Math.Abs(origins[0][2] - origins[1][2]),
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // 4. save_document with a malformed path
        // ─────────────────────────────────────────────────────────────────

        private static void Pf131SaveWithAMalformedPath(Mcp.Bridge p)
        {
            IModelDoc2 part = null;
            string renamed = null;
            string good = Path.Combine(Expectations.Work, "pf131_save_ok.SLDPRT");
            string bad = Path.Combine(Expectations.Work, "pf131_save.SLDPRT").Replace("\\", "");   // «C:Users...pf131_save.SLDPRT»
            try
            {
                part = NewPart();
                Pf131(p, "inspect_part", "{}");
                var block = Pf131(p, "build",
                    "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"front\"," +
                    "\"length_mm\":50,\"width_mm\":30,\"thickness_mm\":10,\"centered\":true}}]}");
                if (!Succeeded(block)) { BenchError("save path", "the block was not built: " + Short(block)); return; }
                string titleBefore = part.GetTitle();

                var answer = Pf131(p, "build",
                    "{\"steps\":[{\"operation\":\"save_document\",\"arguments\":{\"path\":\"" + Pf131Escape(bad) + "\"}}]}");
                string titleAfter = part.GetTitle();
                renamed = titleAfter != titleBefore ? titleAfter : null;
                Check("save path: a path with no separator after the drive letter («" + Short(bad) + "») is refused",
                      !Succeeded(answer) && answer.IndexOf("not an absolute path", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(answer));
                Check("save path: and the document is left as it was (same title, no path)",
                      titleAfter == titleBefore && string.IsNullOrEmpty(part.GetPathName()),
                      "title «" + titleBefore + "» -> «" + titleAfter + "», path «" + part.GetPathName() + "»");

                var wrongKind = Pf131(p, "build",
                    "{\"steps\":[{\"operation\":\"save_document\",\"arguments\":{\"path\":\"" + Pf131Escape(Path.ChangeExtension(good, ".SLDDRW")) + "\"}}]}");
                Check("save path: a part saved to a file name that ends in .SLDDRW is refused before SOLIDWORKS is asked",
                      !Succeeded(wrongKind) && wrongKind.IndexOf(".SLDPRT", StringComparison.OrdinalIgnoreCase) >= 0 && part.GetTitle() == titleBefore,
                      Short(wrongKind));

                // THE CONTROL: the check must not refuse a good path, or it refuses everything.
                var fine = Pf131(p, "build",
                    "{\"steps\":[{\"operation\":\"save_document\",\"arguments\":{\"path\":\"" + Pf131Escape(good) + "\"}}]}");
                Check("save path: the same part saved to a good absolute path is saved, and the file is on the disk",
                      Succeeded(fine) && File.Exists(good), Short(fine));
            }
            finally
            {
                try { if (renamed != null) CloseTitle(renamed); } catch { }
                Close(part);
                try { CloseTitle(Path.GetFileName(good)); } catch { }
                RemoveQuietly(good);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 5. the first base flange of a new part
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// THE CAUSE OF A BASE FLANGE THAT FAILED «WITHOUT A REASON», SHOWN. The sequence of the drawing test: a
        /// bracket, a plan with a slot, ONE undo press (SOLIDWORKS's own, from outside the bridge: it leaves the slot's
        /// sketch in edit, which is what the undo of 0.130.2 did), and then the slot plan three times, which fails each
        /// time with «No sketch open». That leaves the first part in a state in which SOLIDWORKS refuses the first
        /// base flange of a NEW part, in 5 seconds, every time, until the first part is rebuilt or closed (measured,
        /// Checks/Part131, modes evidence-*). The bridge now rebuilds the document a plan stopped on, so the second part
        /// gets its flange. The three failing plans are the premise: if they do not fail, the case is a bench error.
        /// </summary>
        private static void Pf131FirstFlangeAfterFailedSteps(Mcp.Bridge p)
        {
            IModelDoc2 first = null, second = null;
            try
            {
                first = NewPart();
                Pf131(p, "inspect_part", "{}");
                var bracket = Pf131(p, "build", Bracket131);
                if (!Succeeded(bracket)) { BenchError("first flange", "the bracket was not built: " + Short(bracket)); return; }
                string why;
                var threeSteps = Pf131ThreeSteps(Pf131(p, "inspect_part", "{}"), out why);
                if (threeSteps == null) { BenchError("first flange", "the plan with the slot cannot be written: " + why); return; }
                var plan = Pf131(p, "build", threeSteps);
                if (Pf131Built(plan) != 3) { BenchError("first flange", "the plan with the slot did not build: " + Pf131FirstError(plan)); return; }
                first.EditUndo2(1);   // the slot goes and its sketch is left in edit
                int failed = 0;
                for (int k = 1; k <= 3; k++)
                {
                    // The slot alone, as the drawing test sent it after the undo, with the identifiers of this reading.
                    var slotOnly = Pf131SlotOnly(Pf131(p, "inspect_part", "{}"), 0, out why);
                    if (slotOnly == null) { BenchError("first flange", "the slot alone cannot be written (attempt " + k + "): " + why); return; }
                    if (!Succeeded(Pf131(p, "build", "{\"steps\":[" + slotOnly + "]}"))) failed++;
                }
                // On the bridge of 0.130.2 all three fail; on this one the first failure rebuilds the part and a later
                // attempt may work. At least one failure is the premise: steps that stopped on this part.
                if (failed < 1)
                {
                    BenchError("first flange", "the premise did not hold: no slot plan failed after one undo press");
                    return;
                }

                second = NewPart();
                Pf131(p, "inspect_part", "{}");
                var flange = Pf131(p, "build", Bracket131);
                double volume = TestVolume(second);
                Check("first flange: after steps that failed on another part, the first base flange of a NEW part is made (it failed with «does not say why»)",
                      Succeeded(flange) && Math.Abs(volume - 84887.9) < 1, Short(flange) + " — volume " + volume.ToString("0.#", CultureInfo.InvariantCulture) + " mm3");
            }
            finally { Close(second); Close(first); }
        }

        // ─────────────────────────────────────────────────────────────────
        // 6. apply_material: the refusal lists the candidates
        // ─────────────────────────────────────────────────────────────────

        private static void Pf131MaterialCandidates(Mcp.Bridge p)
        {
            IModelDoc2 part = null;
            try
            {
                part = NewPart();
                Pf131(p, "inspect_part", "{}");
                var block = Pf131(p, "build",
                    "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"front\"," +
                    "\"length_mm\":50,\"width_mm\":30,\"thickness_mm\":10,\"centered\":true}}]}");
                if (!Succeeded(block)) { BenchError("material candidates", "the block was not built: " + Short(block)); return; }

                // «S235» is the owner's own case: six steels, all named. «AISI» matches more than the 8 the old refusal listed.
                foreach (var asked in new[] { "S235", "AISI" })
                    Pf131CandidatesOf(p, part, asked);
            }
            finally { Close(part); }
        }

        /// <summary>
        /// The refusal for an ambiguous material name must list the candidates so the model can choose: the
        /// number it announces («matches N different materials») must be the number of names it lists, up to the
        /// cap of the library, and the material must NOT have been applied.
        /// </summary>
        private static void Pf131CandidatesOf(Mcp.Bridge p, IModelDoc2 part, string asked)
        {
            var answer = Pf131(p, "build",
                "{\"steps\":[{\"operation\":\"apply_material\",\"arguments\":{\"material\":\"" + asked + "\"}}]}");
            int announced = -1;
            int at = answer.IndexOf("matches ", StringComparison.Ordinal);
            if (at >= 0)
            {
                string digits = new string(answer.Substring(at + 8).TakeWhile(char.IsDigit).ToArray());
                if (digits.Length > 0) announced = int.Parse(digits, CultureInfo.InvariantCulture);
            }
            // THE CANDIDATES ARE COUNTED BY THEIR LIBRARY (bridge 0.171.1). Each one is written «name» (library «...»)
            // in the segment that follows «not the same material:» up to «Tell me which one». This case was written on
            // 12/10/2026 against the text of that day («not the same steel:», a name without its library) and counted
            // every «: the main line says «material» since 0.132.0 (a plastic is not a steel), and the library, which
            // came with 0.131.0 too, puts two « in every candidate. The bench of 16/11/2026 read «0 listed» for six names.
            const string Library = "(library «";
            int from = answer.IndexOf("not the same material:", StringComparison.Ordinal);
            int to = answer.IndexOf("Tell me which one", StringComparison.Ordinal);
            string segment = from >= 0 && to > from ? answer.Substring(from, to - from) : "";
            int listed = 0;
            for (int found = segment.IndexOf(Library, StringComparison.Ordinal); found >= 0;
                 found = segment.IndexOf(Library, found + Library.Length, StringComparison.Ordinal))
                listed++;
            int cap = Pf131Cap;
            int expectedListed = announced < 0 ? -1 : Math.Min(announced, cap);
            Check("material candidates: «" + asked + "» is refused as ambiguous, and every candidate up to " + cap +
                  " is named (" + announced + " announced, " + listed + " listed)",
                  !Succeeded(answer) && announced > 1 && listed == expectedListed &&
                  (announced <= cap || answer.IndexOf("more", StringComparison.OrdinalIgnoreCase) >= 0),
                  Short(answer));
            string applied = "";
            try { string db; applied = ((IPartDoc)part).GetMaterialPropertyName2("", out db) ?? ""; } catch { }
            Check("material candidates: and nothing was applied to the part", applied.Length == 0, "material «" + applied + "»");
        }
    }
}
