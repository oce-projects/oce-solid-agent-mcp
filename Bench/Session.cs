using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// THE JOINT THAT HAD NEVER BEEN TESTED.
    ///
    /// This project has two benches, and both measure half a thing:
    ///   - `banco0300.py` tests the PLAN — what the model proposes, given a
    ///     hand-written state of the document. It draws nothing.
    ///   - `Oce.Solid.Agent.Bench` tests the DRAWING — whether an operation builds the
    ///     right part, given the arguments. It asks nothing of the model.
    ///
    /// In between is the real run: the model looks at the part, proposes, it is
    /// executed, the part CHANGES, and the model looks again. That is where
    /// «make me a plate» followed by «now give it four holes» lives, and that is where
    /// nobody ever measured anything.
    ///
    /// HOW IT WORKS. This mode keeps ONE document open and waits for the plans
    /// in files. Whoever plans (on the other side, on the server) writes
    /// `step_1.json`; here it is executed, the state is re-read and
    /// `state_1.json` is written; the other side reads that, asks the next thing of the
    /// model, and writes `step_2.json`.
    ///
    /// The document stays OPEN between one step and the next. It is the only way of
    /// measuring what really counts: whether the model, looking at what it has
    /// just done, manages to work on top of it.
    /// </summary>
    internal static partial class Program
    {
        private static int Session(string folder, string toOpen = null)
        {
            Directory.CreateDirectory(folder);
            Console.WriteLine("=== session: " + folder + " ===");

            IModelDoc2 doc = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(toOpen))
                {
                    doc = OpenDocument(toOpen);
                    if (doc == null)
                    {
                        Console.WriteLine("cannot open " + toOpen);
                        return 1;
                    }
                    Console.WriteLine("  resumed: " + toOpen);
                }
                else
                {
                    // The null check is no longer there: `NewPart()` does not
                    // return null, it raises an exception that SAYS what was missing, and
                    // the `catch` below prints it (entry F12). An
                    // unreachable check is worse than no check: it reads
                    // like a defense and defends against nothing.
                    doc = NewPart();
                }

                // THE SESSION'S DOCUMENT MUST BE PUT IN FRONT.
                //
                // «Open» and «active» are not the same thing, and since
                // the operations work on the ACTIVE document the difference
                // is paid for: a drawing left open by a previous
                // session stayed in front, and the first step worked on
                // that. The initial state, which instead looked at the
                // right document, said the opposite — two descriptions
                // of the same instant that did not match.
                Front(doc);

                // The starting state. Whoever plans starts from here,
                // exactly as the real add-in would.
                WriteState(folder, 0, Active() ?? doc, new List<object>());
                Console.WriteLine("  state_0 written (empty document)");

                for (int k = 1; k <= 40; k++)
                {
                    string request = Path.Combine(folder, "step_" + k + ".json");
                    string fine = Path.Combine(folder, "fine.json");

                    // The plan is awaited. Half an hour: a model run with
                    // reasoning on has already taken nine on its own, and whoever
                    // is on the other side often also has to diagnose
                    // before sending the next step. A session that expires
                    // while one looks at the defect forces starting the
                    // part over from scratch.
                    var expiry = DateTime.UtcNow.AddMinutes(30);
                    while (!File.Exists(request) && !File.Exists(fine))
                    {
                        if (DateTime.UtcNow > expiry)
                        {
                            Console.WriteLine("  no step_" + k + " within half an hour: closing");
                            return 0;
                        }
                        Thread.Sleep(500);
                    }
                    if (File.Exists(fine)) { Console.WriteLine("  end of request"); return 0; }

                    // The file may still be being written when we see it.
                    Thread.Sleep(300);
                    var plane = ReadPlane(request);
                    Console.WriteLine("  step " + k + ": " + plane.Count + " operations");

                    // THE SIGNATURES ARE ATTACHED BY THE BENCH, AS THE SERVER DOES.
                    //
                    // The uids are POSITIONAL: faces are renumbered at every
                    // reading, in order of area. One cut is enough for
                    // «F13» no longer to be the face that it was a moment before.
                    // Measured on 10 September 2026 on the fixing slots
                    // of the enclosure: four equal cuts on four equal
                    // tabs, the first right and the second on the wrong
                    // face.
                    //
                    // The first attempt was to freeze the state for the whole
                    // step. It was not enough: the pointer to the face expires
                    // anyway as soon as the model changes, and the second slot
                    // failed all the same.
                    //
                    // The real defense is the one that runs in production: the
                    // plan carries with it the SIGNATURE of every entity it names
                    // — a point on the face and its normal — and whoever executes
                    // finds the face again there even if it changed number. Here the
                    // plans are written by hand and the signature is not there: it is put by
                    // the bench, now, reading it from the state at the start of the step.
                    // Without it, the bench would test a road that in production
                    // nobody travels.
                    var stepState = ReadState(Active() ?? doc);

                    var outcomes = new List<object>();
                    foreach (var planStep in plane)
                    {
                        string itemName = Convert.ToString(planStep.Item1);
                        var arguments = planStep.Item2;
                        AttachSignatures(arguments, stepState);
                        string error;
                        Dictionary<string, object> measurements = null;
                        try
                        {
                            // THE ACTIVE DOCUMENT CAN CHANGE UNDER ONE'S FEET.
                            //
                            // As long as only parts were made, the document
                            // was one and stayed that one. `create_drawing` opens
                            // a second one and puts it in front: from then
                            // on working on the first would mean putting
                            // the views inside the part.
                            var onWhich = Active() ?? doc;
                            measurements = Run(onWhich, itemName, arguments, out error);
                        }
                        catch (Exception ex) { error = ex.Message; }

                        bool ok = measurements != null;
                        Console.WriteLine("     " + (ok ? "OK  " : "NO  ") + itemName +
                                          (ok ? "" : "  -> " + Short(error)));
                        outcomes.Add(new Dictionary<string, object>
                        {
                            ["operation"] = itemName,
                            ["succeeded"] = ok,
                            ["error"] = ok ? null : error,
                            ["measurements"] = measurements,
                        });
                        // One does NOT go on after a failure: a plan is a
                        // sequence, and the next step almost always names what
                        // the failed step should have created.
                        if (!ok) break;
                    }

                    WriteState(folder, k, Active() ?? doc, outcomes);
                    Console.WriteLine("  state_" + k + " written");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("session interrupted: " + ex.Message);
                return 1;
            }
            finally
            {
                // The part is NOT closed: whoever is looking wants to see it.
                Console.WriteLine("=== the document stays open ===");
            }
        }

        /// <summary>
        /// Puts in `_signatures` the description of the entities that the plan names.
        ///
        /// It is what the server does for every real plan before sending it:
        /// without it, whoever executes has no way of noticing that a face has
        /// moved, nor of finding it again.
        /// </summary>
        private static void AttachSignatures(
            Dictionary<string, object> arguments, Dictionary<string, object> docState)
        {
            if (arguments == null || docState == null) return;
            var signatures = new Dictionary<string, object>();

            // THE BODIES LIKE THE FACES (entry Z-C21.2, finding L6-1). The bridge has sent
            // the signatures of the bodies for some time (`McpTools.StateSignatures`, «center_mm»
            // and «bounding_box_mm»); the bench did not, and with «uid_dal_passo» a body
            // named before a cut arrived WITHOUT a signature: `SignatureMatches`
            // had nothing to compare and said yes. Entry E4 — the body
            // renumbered by a cut — could not be seen from the bench.
            var location = new[]
            {
                Tuple.Create("faces", new[] { "face_uid", "faces_uid" }),
                Tuple.Create("edges", new[] { "edges_uid", "edge_uid" }),
                Tuple.Create("bodies", new[]
                {
                    "bodies_uid", "body_uid", "main_body_uid", "tool_bodies_uid",
                    "bodies_to_trim_uid", "trim_boundary_uid",
                }),
            };
            foreach (var pair in location)
            {
                object raw;
                if (!docState.TryGetValue(pair.Item1, out raw)) continue;
                var list = raw as System.Collections.IEnumerable;
                if (list == null) continue;

                // THE EDGES OF THE ANCHOR (entry E18) are one level further down,
                // in `anchor.x` and `anchor.y`: the bridge sends the signatures of EVERYTHING in the
                // state, and the library refuses an anchor whose edge has no
                // signature in a plan that carries some. Without this, the bench would see
                // a refusal that in production is not there.
                var sources = new List<object>();
                foreach (var theField in pair.Item2)
                {
                    object val;
                    if (arguments.TryGetValue(theField, out val) && val != null) sources.Add(val);
                }
                if (pair.Item1 == "edges") sources.AddRange(AnchorEdges(arguments));

                foreach (var val in sources)
                {
                    var wanted = new List<string>();
                    if (val is string) wanted.Add((string)val);
                    else
                    {
                        var itemList = val as System.Collections.IEnumerable;
                        if (itemList == null) continue;
                        foreach (var v in itemList) wanted.Add(Convert.ToString(v));
                    }

                    foreach (var uid in wanted)
                    {
                        foreach (var o in list)
                        {
                            var entry = o as Dictionary<string, object>;
                            if (entry == null) continue;
                            object u;
                            if (!entry.TryGetValue("uid", out u)) continue;
                            if (!string.Equals(Convert.ToString(u), uid,
                                    StringComparison.OrdinalIgnoreCase)) continue;
                            var signature = new Dictionary<string, object>();
                            foreach (var signatureField in
                                     new[] { "point_mm", "normal", "area_mm2", "length_mm",
                                             "center_mm", "bounding_box_mm" })
                            {
                                object x;
                                if (entry.TryGetValue(signatureField, out x) && x != null)
                                    signature[signatureField] = x;
                            }
                            // THE PART'S BOX, as the bridge does: without it, the
                            // bench would read refusal messages shorter
                            // than the real ones, and it is exactly the difference
                            // that a bench must not have.
                            if (signature.Count > 0)
                            {
                                var box = PartBox(docState);
                                if (box != null) signature["box_part_mm"] = box;
                            }
                            if (signature.Count > 0) signatures[uid] = signature;
                            break;
                        }
                    }
                }
            }

            if (signatures.Count > 0) arguments["_signatures"] = signatures;
        }

        /// <summary>The uids of the edges named by `anchor` (edge_uid and centered_between, per axis).</summary>
        private static List<object> AnchorEdges(Dictionary<string, object> arguments)
        {
            var outside = new List<object>();
            object raw;
            var anchor = arguments.TryGetValue("anchor", out raw) ? raw as Dictionary<string, object> : null;
            if (anchor == null) return outside;
            foreach (var axis in anchor.Values)
            {
                var entry = axis as Dictionary<string, object>;
                if (entry == null) continue;
                object v;
                if (entry.TryGetValue("edge_uid", out v) && v != null) outside.Add(v);
                if (entry.TryGetValue("centered_between", out v) && v != null) outside.Add(v);
            }
            return outside;
        }

        /// <summary>
        /// {minX,minY,minZ,maxX,maxY,maxZ} in mm from the two fields of the state:
        /// the corner and the sides. Null if one of the two is missing — the corner is not
        /// invented (see the bridge, `McpTools.PartBox`).
        /// </summary>
        private static double[] PartBox(Dictionary<string, object> docState)
        {
            double[] da = ThreeNumbers(docState, "bounding_box_from_mm");
            double[] sides = ThreeNumbers(docState, "bounding_box_mm");
            if (da == null || sides == null) return null;
            return new[]
            {
                da[0], da[1], da[2],
                da[0] + sides[0], da[1] + sides[1], da[2] + sides[2],
            };
        }

        private static double[] ThreeNumbers(Dictionary<string, object> docState, string key)
        {
            object v;
            if (docState == null || !docState.TryGetValue(key, out v) || v == null) return null;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return null;
            var outside = new List<double>();
            foreach (var x in list)
            {
                try
                {
                    outside.Add(Convert.ToDouble(
                        x, System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { return null; }
            }
            return outside.Count == 3 ? outside.ToArray() : null;
        }

        /// <summary>Opens an already saved document and puts it in front.</summary>
        private static IModelDoc2 OpenDocument(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                int typ = filePath.EndsWith(".SLDDRW", StringComparison.OrdinalIgnoreCase)
                    ? (int)swDocumentTypes_e.swDocDRAWING
                    : filePath.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase)
                        ? (int)swDocumentTypes_e.swDocASSEMBLY
                        : (int)swDocumentTypes_e.swDocPART;
                int errors = 0, warnings = 0;
                var d = _sw.OpenDoc6(
                    filePath, typ,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "", ref errors, ref warnings) as IModelDoc2;
                return d;
            }
            catch { return null; }
        }

        /// <summary>Puts a document in front of all the others.</summary>
        private static void Front(IModelDoc2 doc)
        {
            try
            {
                var docTitle = doc?.GetTitle();
                if (string.IsNullOrWhiteSpace(docTitle)) return;
                int errors = 0;
                _sw.ActivateDoc3(docTitle, true,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors);
            }
            catch { }
        }

        /// <summary>The document that SolidWorks has in front now.</summary>
        private static IModelDoc2 Active()
        {
            try { return _sw.IActiveDoc2; } catch { return null; }
        }

        /// <summary>The plan: a list of {operation, arguments}.</summary>
        private static List<Tuple<string, Dictionary<string, object>>> ReadPlane(string file)
        {
            var outside = new List<Tuple<string, Dictionary<string, object>>>();
            var text = File.ReadAllText(file);
            var s = new System.Web.Script.Serialization.JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            var root = s.DeserializeObject(text);
            var list = root as object[];
            if (list == null)
            {
                var d = root as Dictionary<string, object>;
                object p;
                if (d != null && d.TryGetValue("steps", out p)) list = p as object[];
            }
            if (list == null) return outside;
            foreach (var entry in list)
            {
                var d = entry as Dictionary<string, object>;
                if (d == null) continue;
                object itemName, arg;
                d.TryGetValue("operation", out itemName);
                d.TryGetValue("arguments", out arg);
                outside.Add(Tuple.Create(
                    Convert.ToString(itemName),
                    (arg as Dictionary<string, object>) ?? new Dictionary<string, object>()));
            }
            return outside;
        }

        private static void WriteState(string folder, int k, IModelDoc2 doc, List<object> outcomes)
        {
            var docState = ReadState(doc);
            var outside = new Dictionary<string, object>
            {
                ["step"] = k,
                ["state"] = docState,
                ["outcomes"] = outcomes,
                ["bodies"] = CountBodies(doc),
                ["volume_mm3"] = Math.Round(DocumentVolume(doc), 1),
            };
            // It is written to a temporary file and then renamed: whoever reads
            // on the other side must never see half a JSON.
            string final = Path.Combine(folder, "state_" + k + ".json");
            string temporary = final + ".partial";
            File.WriteAllText(temporary, Json(outside));
            if (File.Exists(final)) File.Delete(final);
            File.Move(temporary, final);
        }
    }
}
