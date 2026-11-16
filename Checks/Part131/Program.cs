using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Mcp = Shared.McpClient;

namespace Checks.Part131
{
    /// <summary>
    /// SIX DEFECTS OF THE DRAWING TEST OF 12/10/2026 (the part side), MEASURED BEFORE THEY ARE FIXED.
    ///
    /// House method: the probe measures the real behaviour BEFORE the code changes, replicating
    /// the ORDER and the POINT of the reads the bridge does, and it prints numbers, never a verdict.
    /// Every subcommand drives the bridge as a client would (Shared\McpClient.cs, the same transport
    /// as the bench) and then reads the documents by COM, by the reference it found, never as the
    /// document in front.
    ///
    ///   Part131.exe BRIDGE.exe undo        defect 1: the anatomy of the undo stack after a plan of three
    ///                                      steps (material, hole wizard, slot): one EditUndo2 at a time,
    ///                                      with the feature list, the volume, the material and whether a
    ///                                      sketch is in edit after every press
    ///   Part131.exe BRIDGE.exe undo-tool   defect 1: what the bridge tool `undo` answers on the same plan,
    ///                                      and what the document looks like afterwards
    ///   Part131.exe BRIDGE.exe property    defect 2: set_property with a drawing active; what the title block
    ///                                      links ask for ($PRP / $PRPSHEET / $PRPVIEW), where the property
    ///                                      lands, what the title block shows
    ///   Part131.exe BRIDGE.exe slot        defect 3: the two ends of a slot, in MODEL coordinates, for angle 0
    ///                                      and 90 on a face with the normal along Z, Y and X
    ///   Part131.exe BRIDGE.exe save        defect 4: save_document with a malformed path (drive letter, colon,
    ///                                      no separator): what the document is called before and after, and
    ///                                      what appears on disk
    ///   Part131.exe BRIDGE.exe flange N    defect 5: N times a new part and a base flange, with the state of the
    ///                                      previous part (clean, with an open sketch, after an undo)
    ///   Part131.exe BRIDGE.exe material    defect 6: what apply_material answers to «S235»
    ///
    /// The folder the probe writes in is the one of the environment variable OCE_PROBE_FOLDER, or a
    /// folder next to the executable. SOLIDWORKS may be shared: the probe closes what it opened.
    ///
    /// ── GUIDE PAGES READ (sldworksapi.chm, extracted 28/9/2026) ──────────────────
    ///   IModelDocExtension~StartRecordingUndoObject.html, ~FinishRecordingUndoObject.html,
    ///       ~FinishRecordingUndoObject2.html: «Only SOLIDWORKS operations that support Undo will be
    ///       undone»; a hidden object that is the first of the list is discarded
    ///   IModelDoc2~EditUndo2.html («Number of actions to undo»), ~EditRedo2.html, ~ClearUndoList.html
    ///   ISketchManager~ActiveSketch.html, ISketchManager~InsertSketch.html
    ///   ISheet~CustomPropertyView.html («the drawing view to use to set the custom information for this
    ///       drawing sheet»), IView~ReferencedDocument.html, INote~PropertyLinkedText.html
    ///   ICustomPropertyManager~Add3.html, ~Get6.html
    ///   IFace2~GetSurface, ISurface~CylinderParams.html (origin, axis, radius of a cylindrical face)
    /// </summary>
    internal static class Program
    {
        private static ISldWorks _sw;
        private static Mcp.Bridge _bridge;
        private static int _id = 100;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string _work;

        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: Part131.exe BRIDGE.exe undo|undo-tool|property|slot|save|flange [N]|material");
                return 2;
            }
            try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }

            _work = System.Environment.GetEnvironmentVariable("OCE_PROBE_FOLDER");
            if (string.IsNullOrWhiteSpace(_work))
                _work = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "work");
            Directory.CreateDirectory(_work);

            _bridge = Mcp.Open(args[0]);
            if (_bridge == null) { Console.Error.WriteLine("the bridge did not start"); return 2; }
            try
            {
                if (Mcp.Ask(_bridge, 1,
                        "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                        "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                        "\"clientInfo\":{\"name\":\"probe\",\"version\":\"1\"}}}") == null)
                { Console.Error.WriteLine("initialize: no answer"); return 2; }

                switch (args[1])
                {
                    case "undo": return UndoStack(false);
                    case "undo-tool": return UndoStack(true);
                    case "undo-delete": return UndoDelete();
                    case "undo-cuts": return UndoCuts(1);
                    case "undo-bulk": return UndoCuts(args.Length > 2 ? int.Parse(args[2], Inv) : 8);
                    case "property": return Property();
                    case "slot": return Slot();
                    case "save": return Save();
                    case "flange": return Flange(args.Length > 2 ? int.Parse(args[2], Inv) : 4, args.Length > 3 ? args[3] : "clean");
                    case "material": return Material();
                    default:
                        Console.Error.WriteLine("unknown subcommand " + args[1]);
                        return 2;
                }
            }
            finally
            {
                try { Mcp.Close(_bridge); } catch { }
                try { _bridge.Dispose(); } catch { }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // The client side
        // ─────────────────────────────────────────────────────────────────

        /// <summary>A tools/call, the text of the answer. Never throws.</summary>
        private static string Tool(string name, string arguments)
        {
            int id = ++_id;
            var line = Mcp.AskWithin(_bridge, id,
                "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":" +
                "{\"name\":\"" + name + "\",\"arguments\":" + arguments + "}}", 600000);
            if (line == null) return "(no answer)";
            var o = Mcp.Obj(line);
            var result = Mcp.Inside(o, "result") as Dictionary<string, object>;
            var content = Mcp.Inside(result, "content") as object[];
            if (content == null || content.Length == 0) return line;
            var t = Convert.ToString(Mcp.Inside(content[0], "text"));
            return string.IsNullOrEmpty(t) ? line : t;
        }

        private static void Say(string text) { Console.WriteLine(text); }

        /// <summary>The first match of a regular expression in a text, or the empty string.</summary>
        private static string Pick(string text, string pattern)
        {
            var m = Regex.Match(text ?? "", pattern);
            return m.Success ? (m.Groups.Count > 1 ? m.Groups[1].Value : m.Value) : "";
        }

        private static string NewPartTitle()
        {
            var a = Tool("document", "{\"action\":\"create\"}");
            return Pick(a, "\"document_title\":\"([^\"]+)\"");
        }

        /// <summary>The open document with that title, found by name among all the open ones.</summary>
        private static IModelDoc2 Find(string title)
        {
            var all = _sw.GetDocuments() as object[];
            if (all == null) return null;
            foreach (var o in all)
            {
                var d = o as IModelDoc2;
                if (d == null) continue;
                try { if (string.Equals(d.GetTitle(), title, StringComparison.OrdinalIgnoreCase)) return d; }
                catch { }
            }
            return null;
        }

        private static void CloseQuietly(string title)
        {
            if (string.IsNullOrEmpty(title)) return;
            try { _sw.CloseDoc(title); } catch { }
        }

        // ─────────────────────────────────────────────────────────────────
        // Readings by COM
        // ─────────────────────────────────────────────────────────────────

        private static List<string> Features(IModelDoc2 doc)
        {
            var found = new List<string>();
            var f = doc.FirstFeature() as IFeature;
            for (int i = 0; f != null && i < 2000; i++)
            {
                string name = "", type = "";
                try { name = f.Name ?? ""; } catch { }
                try { type = f.GetTypeName2() ?? ""; } catch { }
                found.Add(name + ":" + type);
                f = f.GetNextFeature() as IFeature;
            }
            return found;
        }

        private static double Volume(IModelDoc2 doc)
        {
            try
            {
                doc.ClearSelection2(true);
                var m = doc.Extension.CreateMassProperty2() as IMassProperty2;
                if (m == null) return 0;
                m.UseSystemUnits = true;
                m.IncludeHiddenBodiesOrComponents = true;
                return m.Recalculate() ? m.Volume * 1e9 : 0;
            }
            catch { return -1; }
        }

        private static string MaterialOf(IModelDoc2 doc)
        {
            try { var part = doc as IPartDoc; string db; return part == null ? "" : (part.GetMaterialPropertyName2("", out db) ?? ""); }
            catch { return "?"; }
        }

        private static string SketchInEdit(IModelDoc2 doc)
        {
            try
            {
                var s = doc.SketchManager.ActiveSketch as ISketch;
                if (s == null) return "no";
                var feature = s as IFeature;
                return "YES";
            }
            catch (Exception ex) { return "? " + ex.Message; }
        }

        private static string Describe(IModelDoc2 doc)
        {
            var f = Features(doc);
            return "features=" + f.Count + " volume=" + Volume(doc).ToString("0.0", Inv) +
                   " material=«" + MaterialOf(doc) + "» sketch_in_edit=" + SketchInEdit(doc) +
                   "\n      tree: " + string.Join(" | ", f.Where(x => !x.EndsWith(":RefPlane") && !x.EndsWith(":OriginProfileFeature")));
        }

        // ─────────────────────────────────────────────────────────────────
        // 1. undo
        // ─────────────────────────────────────────────────────────────────

        private const string Bracket =
            "{\"steps\":[{\"operation\":\"base_flange\",\"arguments\":{\"plane\":\"front\"," +
            "\"points\":[[120,0],[0,0],[0,70]],\"thickness_mm\":8,\"radius_mm\":8,\"depth_mm\":60}}]}";

        private const string SlotOnly =
            "{\"steps\":[{\"operation\":\"slot\",\"arguments\":{\"face_uid\":\"F3\",\"x_mm\":35,\"y_mm\":30,\"length_mm\":30,\"width_mm\":11," +
            "\"angle_deg\":0,\"through_all\":true,\"anchor\":{\"x\":{\"edge_uid\":\"E5\",\"distance_mm\":35}," +
            "\"y\":{\"centered_between\":[\"E14\",\"E16\"]}}}}]}";

        private static string Outcomes(string answer)
        {
            return string.Join(",", Regex.Matches(answer, "\"succeeded\":(true|false)").Cast<Match>().Select(m => m.Groups[1].Value));
        }

        private const string BracketWithMaterial =
            "{\"steps\":[{\"operation\":\"base_flange\",\"arguments\":{\"plane\":\"front\"," +
            "\"points\":[[120,0],[0,0],[0,70]],\"thickness_mm\":8,\"radius_mm\":8,\"depth_mm\":60}}," +
            "{\"operation\":\"apply_material\",\"arguments\":{\"material\":\"S235JR\"}}]}";

        /// <summary>The plan of the evidence: material, two guided holes, a slot. Faces and edges by uid.</summary>
        private const string ThreeSteps =
            "{\"steps\":[" +
            "{\"operation\":\"apply_material\",\"arguments\":{\"material\":\"S235JR\"}}," +
            "{\"operation\":\"hole_wizard\",\"arguments\":{\"face_uid\":\"F2\",\"variant\":\"clearance\",\"size\":\"M10\"," +
            "\"through_all\":true,\"positions\":[" +
            "{\"x_mm\":90,\"y_mm\":30,\"anchor\":{\"x\":{\"edge_uid\":\"E10\",\"distance_mm\":30},\"y\":{\"centered_between\":[\"E3\",\"E4\"]}}}," +
            "{\"x_mm\":30,\"y_mm\":30,\"anchor\":{\"x\":{\"edge_uid\":\"E10\",\"distance_mm\":90},\"y\":{\"centered_between\":[\"E3\",\"E4\"]}}}]}}," +
            "{\"operation\":\"slot\",\"arguments\":{\"face_uid\":\"F3\",\"x_mm\":35,\"y_mm\":30,\"length_mm\":30,\"width_mm\":11," +
            "\"angle_deg\":90,\"through_all\":true,\"anchor\":{\"x\":{\"edge_uid\":\"E5\",\"distance_mm\":35}," +
            "\"y\":{\"centered_between\":[\"E14\",\"E16\"]}}}}]}";

        private static int UndoStack(bool throughTheTool)
        {
            string title = NewPartTitle();
            Say("part: " + title);
            var doc = Find(title);
            if (doc == null) { Say("cannot find the part by COM"); return 1; }
            try
            {
                Tool("inspect_part", "{}");
                var p0 = Tool("build", Bracket);
                Say("plan 0 (base flange): succeeded=" + Pick(p0, "\"succeeded\":(true|false)") +
                    " undo_entry_recorded=" + Pick(p0, "\"undo_entry_recorded\":(true|false|null)"));
                Tool("inspect_part", "{}");
                var d0 = Describe(doc);
                Say("BEFORE the plan: " + d0);
                int featuresBefore = Features(doc).Count;

                var p1 = Tool("build", ThreeSteps);
                Say("plan 1 (material, hole wizard, slot): outcomes " +
                    string.Join(",", Regex.Matches(p1, "\"succeeded\":(true|false)").Cast<Match>().Select(m => m.Groups[1].Value)) +
                    " undo_entry_recorded=" + Pick(p1, "\"undo_entry_recorded\":(true|false|null)"));
                if (!p1.Contains("\"succeeded\":true")) Say("   plan 1 text: " + p1.Substring(0, Math.Min(1500, p1.Length)));
                var d1 = Describe(doc);
                Say("AFTER the plan:  " + d1);

                if (throughTheTool)
                {
                    var u = Tool("undo", "{}");
                    Say("undo tool answered: " + u.Substring(0, Math.Min(2500, u.Length)));
                    Say("AFTER the tool: " + Describe(doc));
                    return 0;
                }

                for (int press = 1; press <= 8; press++)
                {
                    doc.EditUndo2(1);
                    Say("after press " + press + ": " + Describe(doc));
                    if (Features(doc).Count <= featuresBefore) { Say("   (back to the feature count of before the plan)"); break; }
                }
                return 0;
            }
            finally { CloseQuietly(title); }
        }

        /// <summary>
        /// The rest of the withdrawal, measured: after the presses that change something, what is left of the
        /// plan (the hole wizard feature, the cut list folder, the material) and whether it can be taken away
        /// by deleting it and by clearing the material, and what the part looks like after that.
        /// </summary>
        private static int UndoDelete()
        {
            string title = NewPartTitle();
            var doc = Find(title);
            if (doc == null) { Say("cannot find the part by COM"); return 1; }
            try
            {
                Tool("inspect_part", "{}");
                Tool("build", Bracket);
                Tool("inspect_part", "{}");
                var beforeNames = Features(doc).Select(x => x.Split(':')[0]).ToList();
                Say("BEFORE: " + Describe(doc));
                var p1 = Tool("build", ThreeSteps);
                Say("plan 1 outcomes " + string.Join(",", Regex.Matches(p1, "\"succeeded\":(true|false)").Cast<Match>().Select(m => m.Groups[1].Value)));
                Say("AFTER:  " + Describe(doc));

                string last = Describe(doc);
                for (int press = 1; press <= 6; press++)
                {
                    doc.EditUndo2(1);
                    string now = Describe(doc);
                    Say("press " + press + ": " + now.Split('\n')[0]);
                    if (now == last) { Say("   (nothing changed: stop)"); break; }
                    last = now;
                }
                if (doc.SketchManager.ActiveSketch != null) { doc.ClearSelection2(true); doc.SketchManager.InsertSketch(true); Say("closed the sketch in edit: " + SketchInEdit(doc)); }
                Say("AFTER THE PRESSES: " + Describe(doc));

                // what is left of the plan
                var left = Features(doc).Select(x => x.Split(':')[0]).Where(n => !beforeNames.Contains(n)).ToList();
                Say("left of the plan (names not there before): " + string.Join(" | ", left));
                for (int i = left.Count - 1; i >= 0; i--)
                {
                    var f = doc.FirstFeature() as IFeature;
                    IFeature target = null;
                    for (int k = 0; f != null && k < 2000; k++, f = f.GetNextFeature() as IFeature)
                        if (f.Name == left[i]) { target = f; break; }
                    if (target == null) { Say("   «" + left[i] + "» is already gone"); continue; }
                    int children = target.IGetChildCount();
                    doc.ClearSelection2(true);
                    bool selected = target.Select2(false, 0);
                    bool deleted = selected && doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                    doc.ClearSelection2(true);
                    doc.EditRebuild3();
                    Say("   delete «" + left[i] + "» (" + target.GetTypeName2() + ", children " + children + "): selected=" + selected + " deleted=" + deleted);
                }
                Say("AFTER THE DELETIONS: " + Describe(doc));

                var part = doc as IPartDoc;
                foreach (var cfg in doc.GetConfigurationNames() as string[])
                {
                    string db;
                    Say("material of «" + cfg + "» before clearing: «" + part.GetMaterialPropertyName2(cfg, out db) + "» (db «" + db + "»)");
                    part.SetMaterialPropertyName2(cfg, "", "");
                    Say("   after SetMaterialPropertyName2(cfg,\"\",\"\"): «" + part.GetMaterialPropertyName2(cfg, out db) + "» (db «" + db + "»)");
                }
                Say("FINAL: " + Describe(doc) + "  names equal to before: " +
                    Features(doc).Select(x => x.Split(':')[0]).SequenceEqual(beforeNames));

                var again = Tool("build", ThreeSteps);
                Say("the same plan again: outcomes " + string.Join(",", Regex.Matches(again, "\"succeeded\":(true|false)").Cast<Match>().Select(m => m.Groups[1].Value)));
                return 0;
            }
            finally { CloseQuietly(title); }
        }

        /// <summary>
        /// How the undo stack looks after a plan made only of sketch-based cuts (two round cuts and a slot):
        /// does one press take a step, and how many presses does the plan need.
        /// </summary>
        private static int UndoCuts(int bulk)
        {
            string title = NewPartTitle();
            var doc = Find(title);
            if (doc == null) { Say("cannot find the part by COM"); return 1; }
            try
            {
                Tool("inspect_part", "{}");
                var p0 = Tool("build", "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"top\"," +
                    "\"length_mm\":120,\"width_mm\":80,\"thickness_mm\":10,\"centered\":true}}]}");
                Say("plate: " + Pick(p0, "\"succeeded\":(true|false)"));
                var look = Tool("inspect_part", "{}");
                Say("BEFORE: " + Describe(doc));
                var plan = Tool("build", "{\"steps\":[" +
                    "{\"operation\":\"cylindrical_cut\",\"arguments\":{\"face_uid\":\"" + FaceUid(look, "up") + "\",\"x_mm\":-30,\"y_mm\":0,\"diameter_mm\":10,\"through_all\":true}}," +
                    "{\"operation\":\"cylindrical_cut\",\"arguments\":{\"face_uid\":\"" + FaceUid(look, "up") + "\",\"x_mm\":30,\"y_mm\":0,\"diameter_mm\":10,\"through_all\":true}}," +
                    "{\"operation\":\"slot\",\"arguments\":{\"face_uid\":\"" + FaceUid(look, "up") + "\",\"x_mm\":0,\"y_mm\":20,\"length_mm\":30,\"width_mm\":8,\"angle_deg\":0,\"through_all\":true}}]}");
                Say("plan outcomes " + string.Join(",", Regex.Matches(plan, "\"succeeded\":(true|false)").Cast<Match>().Select(m => m.Groups[1].Value)) +
                    " undo_entry_recorded=" + Pick(plan, "\"undo_entry_recorded\":(true|false|null)"));
                if (!plan.Contains("\"succeeded\":true")) Say(plan.Substring(0, Math.Min(1200, plan.Length)));
                string last = Describe(doc);
                Say("AFTER: " + last);
                for (int press = 1; press <= 10; press++)
                {
                    doc.EditUndo2(bulk);
                    string now = Describe(doc);
                    Say("press " + press + " (EditUndo2(" + bulk + ")): " + now.Replace("\n      tree:", " | tree:").Substring(0, Math.Min(260, now.Length)) + "   cuts in tree: " +
                        string.Join(",", Features(doc).Where(f => f.EndsWith(":ICE") || f.EndsWith(":Cut") || f.EndsWith(":HoleCut")).Select(f => f.Split(':')[0])));
                    if (now == last) { Say("   (nothing changed: stop)"); break; }
                    last = now;
                }
                return 0;
            }
            finally { CloseQuietly(title); }
        }

        // ─────────────────────────────────────────────────────────────────
        // 2. set_property with a drawing active
        // ─────────────────────────────────────────────────────────────────

        private static int Property()
        {
            string title = NewPartTitle();
            var doc = Find(title);
            if (doc == null) { Say("cannot find the part by COM"); return 1; }
            string partPath = Path.Combine(_work, "prop131.SLDPRT");
            string drawPath = Path.Combine(_work, "prop131.SLDDRW");
            string drawTitle = null;
            try
            {
                Tool("inspect_part", "{}");
                var b = Tool("build", "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"top\"," +
                    "\"length_mm\":60,\"width_mm\":40,\"thickness_mm\":10,\"centered\":true}}," +
                    "{\"operation\":\"save_document\",\"arguments\":{\"path\":\"" + partPath.Replace("\\", "\\\\") + "\"}}]}");
                Say("part build: " + Pick(b, "\"succeeded\":(true|false)"));
                var c = Tool("build", "{\"steps\":[{\"operation\":\"create_drawing\",\"arguments\":{\"paper_size\":\"A3\"}}," +
                    "{\"operation\":\"standard_views\",\"arguments\":{}}]}");
                Say("drawing build: " + Regex.Replace(Pick(c, "\"outcomes\":\\[(.{0,300})"), "\\s+", " "));
                Tool("inspect_part", "{}");
                var tb = Tool("measure", "{\"steps\":[{\"operation\":\"title_block\",\"arguments\":{}}]}");
                Say("title_block BEFORE: wanted=" + Pick(tb, "\"properties_wanted_by_title_block\":\\[([^\\]]*)\\]") +
                    " empty=" + Pick(tb, "\"requested_but_empty\":\\[([^\\]]*)\\]") +
                    " model_checked=" + Pick(tb, "\"model_checked_for_properties\":\"([^\"]*)\""));
                Say("   the links of the notes: " + string.Join(" || ",
                    Regex.Matches(tb, "\"asks\":\"((?:[^\"\\\\]|\\\\.)*)\"").Cast<Match>().Select(m => m.Groups[1].Value)));

                // the drawing is in front now; find it by COM and read both drawers
                var all = _sw.GetDocuments() as object[] ?? new object[0];
                IModelDoc2 drawing = null;
                foreach (var o in all)
                {
                    var d = o as IModelDoc2;
                    if (d != null && d.GetType() == (int)swDocumentTypes_e.swDocDRAWING) { drawing = d; drawTitle = d.GetTitle(); }
                }
                Say("drawing: " + drawTitle);

                var named = Regex.Match(tb, "\"([^\"\\\\]+?)  \\(from the part");
                if (named.Success) Field = named.Groups[1].Value;
                Say("property used: " + Field);
                var w = Tool("build", "{\"steps\":[{\"operation\":\"set_property\",\"arguments\":{\"name\":\"" + Field + "\",\"value_text\":\"BRACKET 131\"}}]}");
                Say("set_property answered: " + Regex.Replace(w.Substring(0, Math.Min(900, w.Length)), "\\s+", " "));
                Say("   property on the DRAWING: «" + Prop(drawing) + "»   on the PART: «" + Prop(doc) + "»");
                var tb2 = Tool("measure", "{\"steps\":[{\"operation\":\"title_block\",\"arguments\":{}}]}");
                Say("title_block AFTER: shown=" + Pick(tb2, "\"requested_and_shown\":\\[([^\\]]*)\\]") +
                    " empty=" + Pick(tb2, "\"requested_but_empty\":\\[([^\\]]*)\\]"));
                return 0;
            }
            finally
            {
                CloseQuietly(drawTitle);
                CloseQuietly(title);
                foreach (var f in new[] { partPath, drawPath }) try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
        }

        /// <summary>The property of the title block read from the model of the views, found at run time.</summary>
        private static string Field = "Description";

        private static string Prop(IModelDoc2 d)
        {
            if (d == null) return "(none)";
            try
            {
                var pm = d.Extension.get_CustomPropertyManager("");
                string value, resolved; bool link;
                pm.Get6(Field, false, out value, out resolved, out link, out link);
                return value ?? "";
            }
            catch (Exception ex) { return "? " + ex.Message; }
        }

        // ─────────────────────────────────────────────────────────────────
        // 3. slot angle
        // ─────────────────────────────────────────────────────────────────

        private static int Slot()
        {
            foreach (var facing in new[] { "front", "up", "right" })
                foreach (var angle in new[] { 0, 90 })
                {
                    string title = NewPartTitle();
                    var doc = Find(title);
                    if (doc == null) { Say("cannot find the part"); return 1; }
                    try
                    {
                        Tool("inspect_part", "{}");
                        // a block 200 x 120 x 60, centered on the origin in X and Y, Z from 0 to 60
                        var b = Tool("build", "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"front\"," +
                            "\"length_mm\":200,\"width_mm\":120,\"thickness_mm\":60,\"centered\":true}}]}");
                        var s = Tool("build", "{\"steps\":[{\"operation\":\"slot\",\"arguments\":{\"face_uid\":\"@face(normal=" + facing + ")\"," +
                            "\"x_mm\":0,\"y_mm\":" + (facing == "front" ? "10" : "30") + ",\"length_mm\":51,\"width_mm\":11,\"angle_deg\":" + angle + ",\"through_all\":false,\"depth_mm\":5}}]}");
                        string ok = Pick(s, "\"succeeded\":(true|false)");
                        if (ok != "true")
                        {
                            // the uid form @face is a bench selector, not a bridge one: look the face up
                            var look = Tool("inspect_part", "{}");
                            string uid = FaceUid(look, facing);
                            s = Tool("build", "{\"steps\":[{\"operation\":\"slot\",\"arguments\":{\"face_uid\":\"" + uid + "\"," +
                                "\"x_mm\":0,\"y_mm\":" + (facing == "front" ? "10" : "30") + ",\"length_mm\":51,\"width_mm\":11,\"angle_deg\":" + angle + ",\"through_all\":false,\"depth_mm\":5}}]}");
                            ok = Pick(s, "\"succeeded\":(true|false)");
                        }
                        Say("face facing=" + facing + " angle=" + angle + ": succeeded=" + ok + "  ends of the slot (model mm): " + SlotEnds(doc));
                        if (ok != "true") Say("   " + s.Substring(0, Math.Min(500, s.Length)));
                    }
                    finally { CloseQuietly(title); }
                }
            return 0;
        }

        private static string FaceUid(string stateText, string facing)
        {
            // faces are listed as {"uid":"F3",...,"facing":"right",...}; the largest planar one with that facing
            var best = ""; double area = -1;
            foreach (Match m in Regex.Matches(stateText, "\\{\"uid\":\"(F\\d+)\",\"area_mm2\":([0-9.]+),[^{}]*\"planar\":true[^{}]*\"facing\":\"" + facing + "\""))
            {
                double a = double.Parse(m.Groups[2].Value, Inv);
                if (a > area) { area = a; best = m.Groups[1].Value; }
            }
            return best;
        }

        /// <summary>The two cylindrical faces of the slot: the origins of their axes, in model mm, and the vector between them.</summary>
        private static string SlotEnds(IModelDoc2 doc)
        {
            var origins = new List<double[]>();
            var part = doc as IPartDoc;
            var bodies = part == null ? null : part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            if (bodies == null) return "(no body)";
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
                    origins.Add(new[] { cp[0] * 1000, cp[1] * 1000, cp[2] * 1000, cp[3], cp[4], cp[5], cp[6] * 1000 });
                }
            }
            if (origins.Count < 2) return origins.Count + " cylindrical faces";
            var a = origins[0]; var b = origins[1];
            double dx = Math.Abs(a[0] - b[0]), dy = Math.Abs(a[1] - b[1]), dz = Math.Abs(a[2] - b[2]);
            return string.Format(Inv, "centres ({0:0.##},{1:0.##},{2:0.##}) and ({3:0.##},{4:0.##},{5:0.##}) r={6:0.##}; |dX|={7:0.##} |dY|={8:0.##} |dZ|={9:0.##}",
                a[0], a[1], a[2], b[0], b[1], b[2], a[6], dx, dy, dz);
        }

        // ─────────────────────────────────────────────────────────────────
        // 4. save_document with a malformed path
        // ─────────────────────────────────────────────────────────────────

        private static int Save()
        {
            string title = NewPartTitle();
            var doc = Find(title);
            if (doc == null) { Say("cannot find the part"); return 1; }
            string name = null;
            try
            {
                Tool("inspect_part", "{}");
                Tool("build", "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"top\"," +
                    "\"length_mm\":60,\"width_mm\":40,\"thickness_mm\":10,\"centered\":true}}]}");
                Say("before: title=«" + doc.GetTitle() + "» path=«" + doc.GetPathName() + "»");
                string good = Path.Combine(_work, "save131.SLDPRT");
                // a drive-relative path: what a path looks like when its backslashes were eaten
                string bad = good.Replace("\\", "");
                var r = Tool("build", "{\"steps\":[{\"operation\":\"save_document\",\"arguments\":{\"path\":\"" + bad.Replace("\\", "\\\\") + "\"}}]}");
                Say("save_document(" + bad + ") answered: " + Regex.Replace(r.Substring(0, Math.Min(700, r.Length)), "\\s+", " "));
                name = doc.GetTitle();
                Say("after:  title=«" + name + "» path=«" + doc.GetPathName() + "»");
                Say("files in the working folder: " + string.Join(", ", Directory.GetFiles(_work).Select(Path.GetFileName)));
                Say("files in the folder of the bridge: " + string.Join(", ",
                    Directory.GetFiles(Path.GetDirectoryName(bad.Length > 2 ? Path.GetFullPath(bad) : _work) ?? _work).Select(Path.GetFileName).Take(10)));
                return 0;
            }
            finally { CloseQuietly(name); CloseQuietly(title); }
        }

        // ─────────────────────────────────────────────────────────────────
        // 5. the first base flange of a new document
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Every user preference toggle SOLIDWORKS answers, by enum name (ISldWorks~GetUserPreferenceToggle.html).</summary>
        private static Dictionary<string, bool> Toggles()
        {
            var all = new Dictionary<string, bool>();
            foreach (swUserPreferenceToggle_e t in Enum.GetValues(typeof(swUserPreferenceToggle_e)))
            {
                try { all[t.ToString()] = _sw.GetUserPreferenceToggle((int)t); } catch { }
            }
            return all;
        }

        private static int Flange(int times, string mode)
        {
            // mode: clean   a new part after the previous one, which is left as it is
            //       sketch  the previous part is left with a sketch in edit (what a slot left after an undo)
            //       wait    clean, with three seconds between the creation and the first call
            string previous = null;
            var opened = new List<string>();
            var togglesAtStart = mode == "evidence-prefs" ? Toggles() : null;
            try
            {
                for (int i = 1; i <= times; i++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string title = NewPartTitle();
                    opened.Add(title);
                    if (mode == "wait") System.Threading.Thread.Sleep(3000);
                    long created = sw.ElapsedMilliseconds;
                    Tool("inspect_part", "{}");
                    long inspected = sw.ElapsedMilliseconds;
                    var r = Tool("build", mode == "undo" || mode.StartsWith("evidence") ? BracketWithMaterial : Bracket);
                    long done = sw.ElapsedMilliseconds;
                    string ok = Pick(r, "\"succeeded\":(true|false)");
                    var doc = Find(title);
                    Say("part " + i + " «" + title + "» (previous: " + (previous ?? "none") + ", mode " + mode + "): base_flange succeeded=" + ok +
                        " create=" + created + "ms inspect=" + (inspected - created) + "ms build=" + (done - inspected) + "ms" +
                        (ok != "true" ? "\n      " + Regex.Replace(r.Substring(0, Math.Min(900, r.Length)), "\\s+", " ") : "") +
                        (doc != null ? "\n      " + Describe(doc) : ""));
                    if (mode == "undo" && doc != null)
                    {
                        // what the drawing test did: a plan with a slot, one undo, and the next part
                        Tool("inspect_part", "{}");
                        var more = Tool("build", ThreeSteps);
                        Say("   plan with the slot: " + string.Join(",", Regex.Matches(more, "\"succeeded\":(true|false)").Cast<Match>().Select(m => m.Groups[1].Value)));
                        doc.EditUndo2(1);
                        Say("   after one undo press: sketch in edit " + SketchInEdit(doc));
                    }
                    if (mode.StartsWith("evidence") && doc != null)
                    {
                        // the whole sequence of the drawing test: the plan, one undo, then three slots that fail
                        Tool("inspect_part", "{}");
                        var more = Tool("build", ThreeSteps);
                        Say("   plan with the slot: " + Outcomes(more));
                        var u = Tool("undo", "{}");
                        Say("   undo answered undone=" + Pick(u, "\"undone\":(true|false)") + " sketch in edit " + SketchInEdit(doc));
                        for (int k = 1; k <= 3; k++)
                        {
                            Tool("inspect_part", "{}");
                            var again = Tool("build", SlotOnly);
                            Say("   slot again " + k + ": " + Outcomes(again) + " sketch in edit " + SketchInEdit(doc) +
                                "  " + Pick(again, "\"error\":\"([^\"]{0,100})"));
                        }
                    }
                    if (mode.StartsWith("evidence-") && doc != null)
                    {
                        // a cleanup of the part that was left in the state of the drawing test, to find which one matters
                        switch (mode)
                        {
                            case "evidence-prefs":
                                {
                                    var now = Toggles();
                                    foreach (var kv in now)
                                        if (togglesAtStart.ContainsKey(kv.Key) && togglesAtStart[kv.Key] != kv.Value)
                                            Say("   user preference toggle " + kv.Key + ": " + togglesAtStart[kv.Key] + " -> " + kv.Value);
                                    Say("   (" + now.Count + " toggles compared)");
                                    break;
                                }
                            case "evidence-rebuild":
                                {
                                    bool again = doc.ForceRebuild3(false);
                                    Say("   ForceRebuild3 on the old part: " + again + ", rebuild errors: " + doc.GetFeatureCount());
                                    break;
                                }
                            case "evidence-editrebuild":
                                {
                                    bool again = doc.EditRebuild3();
                                    Say("   EditRebuild3 on the old part: " + again + ", needs rebuild now: " + doc.Extension.NeedsRebuild2);
                                    break;
                                }
                            case "evidence-needs":
                                {
                                    foreach (var o in (_sw.GetDocuments() as object[]) ?? new object[0])
                                    {
                                        var d = o as IModelDoc2;
                                        if (d != null) Say("   NeedsRebuild2 of «" + d.GetTitle() + "»: " + d.Extension.NeedsRebuild2);
                                    }
                                    break;
                                }
                            case "evidence-clearsel":
                                doc.ClearSelection2(true);
                                Say("   selection cleared on the old part");
                                break;
                            case "evidence-hide":
                                doc.Visible = false;
                                Say("   old part hidden");
                                break;
                            case "evidence-reactivate":
                                {
                                    int err = 0;
                                    _sw.ActivateDoc2(title, false, ref err);
                                    Say("   old part activated again (error " + err + ")");
                                    break;
                                }
                            case "evidence-finish":
                                Say("   FinishRecordingUndoObject now on the old part: " + doc.Extension.FinishRecordingUndoObject("probe"));
                                break;
                            case "evidence-clear":
                                doc.ClearUndoList();
                                Say("   ClearUndoList on the old part");
                                break;
                            case "evidence-exit":
                                for (int k = 0; k < 3 && doc.SketchManager.ActiveSketch != null; k++) { doc.ClearSelection2(true); doc.SketchManager.InsertSketch(true); }
                                Say("   left the sketch edit of the old part: " + SketchInEdit(doc));
                                break;
                            case "evidence-close":
                                _sw.CloseDoc(title);
                                opened.Remove(title);
                                Say("   closed the old part");
                                break;
                        }
                    }
                    if (mode == "sketch" && doc != null)
                    {
                        // leave a sketch in edit in this part, the way an undo of a slot did
                        doc.ClearSelection2(true);
                        var f = doc.FirstFeature() as IFeature;
                        for (int k = 0; f != null && k < 200; k++, f = f.GetNextFeature() as IFeature)
                            if (f.GetTypeName2() == "RefPlane") { f.Select2(false, 0); break; }
                        doc.SketchManager.InsertSketch(true);
                        Say("   left a sketch in edit: " + SketchInEdit(doc));
                    }
                    if (mode == "evidence-prefs" && i >= 2 && ok != "true" && previous != null)
                    {
                        // part 2 failed: look at the session, close the old part, and try the SAME call again on part 2
                        Say("   CommandInProgress=" + _sw.CommandInProgress + "  active document: " + (_sw.IActiveDoc2 == null ? "none" : _sw.IActiveDoc2.GetTitle()));
                        var oldDoc = Find(previous);
                        if (oldDoc != null)
                        {
                            Say("   old part: sketch in edit " + SketchInEdit(oldDoc) + ", save flag " + oldDoc.GetSaveFlag() + ", add-to-db " + oldDoc.SketchManager.AddToDB +
                                ", new part add-to-db " + (doc == null ? "?" : doc.SketchManager.AddToDB.ToString()));
                            _sw.CloseDoc(previous);
                            opened.Remove(previous);
                            Say("   closed the old part");
                            Tool("inspect_part", "{}");
                            var retry = Tool("build", BracketWithMaterial);
                            Say("   same plan again on the new part, old part closed: " + Outcomes(retry));
                        }
                    }
                    previous = title;
                }
                return 0;
            }
            finally { foreach (var t in opened) CloseQuietly(t); }
        }

        // 6. apply_material «S235»
        // ─────────────────────────────────────────────────────────────────

        private static int Material()
        {
            string title = NewPartTitle();
            var doc = Find(title);
            if (doc == null) { Say("cannot find the part"); return 1; }
            try
            {
                Tool("inspect_part", "{}");
                Tool("build", "{\"steps\":[{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"top\"," +
                    "\"length_mm\":60,\"width_mm\":40,\"thickness_mm\":10,\"centered\":true}}]}");
                foreach (var m in new[] { "S235", "steel", "304" })
                {
                    var r = Tool("build", "{\"steps\":[{\"operation\":\"apply_material\",\"arguments\":{\"material\":\"" + m + "\"}}]}");
                    Say("apply_material «" + m + "»: succeeded=" + Pick(r, "\"succeeded\":(true|false)") + " error: " +
                        Pick(r, "\"error\":\"((?:[^\"\\\\]|\\\\.)*)\""));
                }
                return 0;
            }
            finally { CloseQuietly(title); }
        }
    }
}
