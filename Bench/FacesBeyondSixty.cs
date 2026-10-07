using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
// ALIAS AND NOT `using static`, as in ClientPort.cs: the bench has an `Open` and a `Close` of its own.
using Mcp = Shared.McpClient;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// FACES BEYOND THE 60 LARGEST, THROUGH THE BRIDGE (entry W3.1-bis.12).
    ///
    /// The state listed the 60 largest faces; a face that was not among them had no
    /// identifier, so no plan and no model could name it. On the tube stand of task 07
    /// (177 faces) the walls of the third and fourth foot tie behind 40 larger faces of
    /// the frame, and the autonomous run could not even build the part tasks 11 and 15
    /// start from. The fix gives every face the identifier of its RANK by area and lets
    /// `inspect_part` list the faces a filter asks for (`faces`).
    ///
    /// This group walks the whole road, with a real document in front and the bridge
    /// over stdio, the way a model would. The part is the frame of the stand (1000 x 600
    /// of 40x40x3 tube, mitred: one call, about 240 faces), where the two faces of the
    /// mitre at a corner are among the smallest, so beyond the 60 largest by construction
    /// and not by luck of the order of the ties. Then
    ///
    ///   1. the ordinary reading lists 60 faces and says there are more (the premise:
    ///      without it everything below proves nothing);
    ///   2. the reading with a point finds the two faces of the mitre at the corner and
    ///      gives them their rank, above 60;
    ///   3. a plan cites one of them and the distance from it to the outer face of the
    ///      opposite side comes out as computed by hand (500 - (-460) = 960 mm);
    ///   4. the same identifier cited after an ORDINARY reading (no filter) works too:
    ///      the face is numbered even when it is not described;
    ///   5. an identifier beyond the last face is refused with the reason, a crooked
    ///      filter is refused before the document is touched.
    ///
    /// (A plate with 56 holes of the wizard was the first part of this group: the wizard
    /// takes seconds a hole on a tired session and the client waits 90 seconds for an
    /// answer, so it timed out. The declared case
    /// `a_face_beyond_the_largest_sixty_is_named_by_selector` walks a plate of that kind
    /// in-process, with a selector.) Both are red on a library older than 0.153.0, whose
    /// reading lists 60 faces and knows no rank beyond them.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>The two faces of the mitre at the corner (-500, -300) of the frame meet at a point inside the wall.</summary>
        private static readonly double[] MitrePoint = { -498, 0, -298 };
        private const double OppositeSideX = 500, MitreEndX = -460;

        private static string FrameStep(string profilesFolder)
        {
            string profile = (profilesFolder + "\\TUBO STRUTTURALE QUADRO.SLDLFP").Replace("\\", "\\\\");
            return "{\"steps\":[{\"operation\":\"rectangular_frame\",\"arguments\":{\"profile_path\":\"" + profile +
                   "\",\"configuration\":\"40x40x3\",\"length_mm\":1000,\"width_mm\":600,\"plane\":\"top\"," +
                   "\"corner_treatment\":\"miter\",\"centered\":true}}]}";
        }

        private static List<Dictionary<string, object>> FacesOf(Dictionary<string, object> state)
        {
            var outside = new List<Dictionary<string, object>>();
            var faces = Mcp.Inside(state, "faces") as System.Collections.IEnumerable;
            if (faces == null) return outside;
            foreach (var f in faces)
            {
                var d = f as Dictionary<string, object>;
                if (d != null) outside.Add(d);
            }
            return outside;
        }

        private static int Count(object o)
        {
            try { return Convert.ToInt32(o, CultureInfo.InvariantCulture); } catch { return -1; }
        }

        private static double Number(object o)
        {
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return double.NaN; }
        }

        private static bool Succeeded(string answer)
        {
            return answer.IndexOf("\"succeeded\":true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   answer.IndexOf("\"succeeded\": true", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string UidOf(Dictionary<string, object> face)
        {
            return Convert.ToString(Mcp.Inside(face, "uid"), CultureInfo.InvariantCulture);
        }

        private static int RankFromUid(string uid)
        {
            int rank;
            return uid != null && uid.Length > 1 && int.TryParse(uid.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out rank) ? rank : -1;
        }

        private static void TestFacesBeyondSixty()
        {
            const string Name = "faces beyond sixty";
            var executable = TheBridge();
            if (executable == null)
            {
                BenchError(Name, "cannot find bin\\oce-solid-agent-mcp.exe — build it with .\\build.ps1");
                return;
            }
            if (Expectations.Profiles == null)
            {
                BenchError(Name, "the weld profile library is not known: set OCE_WELDMENT_PROFILES to its folder");
                return;
            }
            IModelDoc2 doc = null;
            Mcp.Bridge p = null;
            try
            {
                doc = NewPart();
                p = Mcp.Open(executable);
                if (p == null) { BenchError(Name, "the bridge did not start"); return; }
                var hello = Mcp.Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"bench\",\"version\":\"1\"}}}");
                if (hello == null) { BenchError(Name, "the bridge did not answer initialize"); return; }
                Mcp.Write(p, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                ResponseText(Mcp.Ask(p, 2, Call(2, "inspect_part", "{}")));

                // ── THE PART ─────────────────────────────────────────────
                var built = ResponseText(Mcp.Ask(p, 3, Call(3, "build", FrameStep(Expectations.Profiles))));
                if (!Succeeded(built))
                {
                    BenchError(Name, "the frame was not built: " + (built.Length > 400 ? built.Substring(0, 400) : built));
                    return;
                }

                // ── 1. THE ORDINARY READING: 60 faces, and it says there are more ─
                var first = Mcp.Obj(ResponseText(Mcp.Ask(p, 4, Call(4, "inspect_part", "{}"))));
                var listed = FacesOf(first);
                int total = Count(Mcp.Inside(first, "faces_total"));
                Check("faces beyond sixty: the ordinary reading lists the 60 largest and says the part has more (the premise)",
                      listed.Count == 60 && total > 60 && Mcp.Inside(first, "truncated") is bool && (bool)Mcp.Inside(first, "truncated"),
                      listed.Count + " listed, faces_total " + total);
                if (total <= 60) return;
                var right = listed.FirstOrDefault(f =>
                {
                    var point = Mcp.Inside(f, "point_mm") as object[];
                    return Convert.ToString(Mcp.Inside(f, "facing")) == "right" && point != null && point.Length == 3 &&
                           Math.Abs(Number(point[0]) - OppositeSideX) < 0.5;
                });
                string rightUid = right == null ? null : UidOf(right);
                Check("faces beyond sixty: the outer face of the side at x = " + OppositeSideX + " is among the listed faces (its identifier is the end of the distance)",
                      rightUid != null, "no listed face looks right at x = " + OppositeSideX);
                if (rightUid == null) return;

                // ── 2. THE READING WITH A POINT ──────────────────────────
                var near = Mcp.Obj(ResponseText(Mcp.Ask(p, 5, Call(5, "inspect_part",
                    "{\"faces\":{\"near_mm\":[" + MitrePoint[0].ToString(CultureInfo.InvariantCulture) + "," + MitrePoint[1].ToString(CultureInfo.InvariantCulture) + "," + MitrePoint[2].ToString(CultureInfo.InvariantCulture) + "],\"within_mm\":0.5}}"))));
                var nearFaces = FacesOf(near);
                Check("faces beyond sixty: «near_mm» finds the two faces of the mitre at the corner, and nothing else",
                      nearFaces.Count == 2 && Count(Mcp.Inside(near, "faces_matched")) == 2,
                      nearFaces.Count + " listed, matched " + Mcp.Inside(near, "faces_matched"));
                string mitreUid = nearFaces.Count > 0 ? UidOf(nearFaces[0]) : null;
                Check("faces beyond sixty: they carry the identifier of their RANK among all the faces, beyond the 60 listed, and the state still says how many there are",
                      nearFaces.Count == 2 && nearFaces.All(f => RankFromUid(UidOf(f)) > 60 && RankFromUid(UidOf(f)) <= total) &&
                      Count(Mcp.Inside(near, "faces_total")) == total,
                      string.Join(", ", nearFaces.Select(UidOf)) + "; faces_total " + Mcp.Inside(near, "faces_total"));
                var echoed = Mcp.Inside(near, "faces_filter") as Dictionary<string, object>;
                Check("faces beyond sixty: the state echoes the filter that was applied",
                      echoed != null && echoed.ContainsKey("near_mm") && Number(Mcp.Inside(echoed, "within_mm")) == 0.5,
                      echoed == null ? "no faces_filter" : string.Join(", ", echoed.Keys));
                if (mitreUid == null) return;

                // ── 3. A PLAN CITES IT ───────────────────────────────────
                double expected = OppositeSideX - MitreEndX;
                var distance = ResponseText(Mcp.Ask(p, 6, Call(6, "build",
                    "{\"steps\":[{\"operation\":\"minimum_distance\",\"arguments\":{\"start_uid\":\"" + mitreUid + "\",\"end_uid\":\"" + rightUid + "\"}}]}")));
                Check("faces beyond sixty: a plan that cites the identifier of the filtered reading is accepted, and the distance from the mitre face to the opposite side is the one computed by hand (" +
                      expected.ToString(CultureInfo.InvariantCulture) + " mm)",
                      Succeeded(distance) && HasDistance(distance, expected), Short(distance));

                // ── 4. THE SAME IDENTIFIER AFTER AN ORDINARY READING ──────
                var plain = Mcp.Obj(ResponseText(Mcp.Ask(p, 7, Call(7, "inspect_part", "{}"))));
                int plainTotal = Count(Mcp.Inside(plain, "faces_total"));
                var again = ResponseText(Mcp.Ask(p, 8, Call(8, "build",
                    "{\"steps\":[{\"operation\":\"minimum_distance\",\"arguments\":{\"start_uid\":\"" + mitreUid + "\",\"end_uid\":\"" + rightUid + "\"}}]}")));
                Check("faces beyond sixty: after an ORDINARY reading (60 listed) the same identifier still names the same face: it is numbered even when it is not described",
                      plainTotal == total && FacesOf(plain).Count == 60 && Succeeded(again) && HasDistance(again, expected),
                      "faces_total " + plainTotal + "; " + Short(again));

                // ── 5. THE REFUSALS ──────────────────────────────────────
                var nonexistent = ResponseText(Mcp.Ask(p, 9, Call(9, "build",
                    "{\"steps\":[{\"operation\":\"minimum_distance\",\"arguments\":{\"start_uid\":\"F" + (total + 1) + "\",\"end_uid\":\"" + rightUid + "\"}}]}")));
                Check("faces beyond sixty: the identifier after the last face (F" + (total + 1) + ") is refused, and the reason is that it was not in the part",
                      !Succeeded(nonexistent) && nonexistent.IndexOf("not in the part", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(nonexistent));

                var crooked = ResponseText(Mcp.Ask(p, 10, Call(10, "inspect_part", "{\"faces\":{\"normal\":\"north\"}}")));
                Check("faces beyond sixty: a direction that is not one of the six is refused before the document is touched, and says which are",
                      crooked.IndexOf("must be one of", StringComparison.OrdinalIgnoreCase) >= 0 && crooked.IndexOf("front", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(crooked));
                var noBody = ResponseText(Mcp.Ask(p, 11, Call(11, "inspect_part", "{\"faces\":{\"body\":\"C99\"}}")));
                Check("faces beyond sixty: a body the part does not have is refused and says how many it has",
                      noBody.IndexOf("«C99»", StringComparison.OrdinalIgnoreCase) >= 0 && noBody.IndexOf("4 bodies", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(noBody));

                // ── 6. THE LIMIT SAYS WHAT IT LEFT OUT ───────────────────
                var cut = Mcp.Obj(ResponseText(Mcp.Ask(p, 12, Call(12, "inspect_part", "{\"faces\":{\"limit\":5}}"))));
                Check("faces beyond sixty: a limit of 5 lists the 5 largest and says how many matching faces it left out",
                      FacesOf(cut).Count == 5 && Count(Mcp.Inside(cut, "faces_matched")) == total && Count(Mcp.Inside(cut, "faces_not_listed")) == total - 5,
                      FacesOf(cut).Count + " listed, matched " + Mcp.Inside(cut, "faces_matched") + ", not listed " + Mcp.Inside(cut, "faces_not_listed"));
                var small = Mcp.Obj(ResponseText(Mcp.Ask(p, 13, Call(13, "inspect_part", "{\"faces\":{\"area_max_mm2\":700,\"limit\":500}}"))));
                var smallFaces = FacesOf(small);
                Check("faces beyond sixty: an area range lists only the faces inside it, every one beyond the 60 largest",
                      smallFaces.Count > 0 && smallFaces.All(f => RankFromUid(UidOf(f)) > 60 && Number(Mcp.Inside(f, "area_mm2")) <= 700.01) &&
                      Count(Mcp.Inside(small, "faces_matched")) == smallFaces.Count,
                      smallFaces.Count + " listed, matched " + Mcp.Inside(small, "faces_matched"));
            }
            catch (Exception ex) { BenchError(Name, Why(ex)); }
            finally
            {
                if (p != null) { Mcp.Close(p); p.Dispose(); }
                Close(doc);
            }
        }

        /// <summary>Whether the answer of minimum_distance says a distance_mm within a hundredth of the one computed by hand.</summary>
        private static bool HasDistance(string answer, double expectedMm)
        {
            const string Key = "\"distance_mm\":";
            int at = answer.IndexOf(Key, StringComparison.Ordinal);
            if (at < 0) return false;
            at += Key.Length;
            while (at < answer.Length && answer[at] == ' ') at++;
            int end = at;
            while (end < answer.Length && (char.IsDigit(answer[end]) || answer[end] == '.' || answer[end] == '-')) end++;
            double d;
            return double.TryParse(answer.Substring(at, end - at), NumberStyles.Float, CultureInfo.InvariantCulture, out d) &&
                   Math.Abs(d - expectedMm) < 0.05;
        }
    }
}
