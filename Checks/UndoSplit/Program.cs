using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.UndoSplit
{
    /// <summary>
    /// DOES LOCKING THE SKETCH SPLIT THE UNDO ENTRY OF A PLAN? (brief R11,
    /// hypothesis H1.)
    ///
    /// On 25/10/2026 a plan of 11 steps on a part recorded its undo entry
    /// («undo_entry_recorded» true), and one undo took away seven features out
    /// of eight: the first extrusion stayed. The bridge records ONE entry around
    /// the whole plan (PlanExecutor, Start/FinishRecordingUndoObject). The
    /// hypothesis: the lock of the sketch after an extrusion
    /// (SwOperations.LockSketch: EditSketch, AddRelation FIXED, InsertSketch,
    /// EditRebuild3) closes or splits the recording.
    ///
    /// Three ways, each on a NEW part closed without saving, each the same two
    /// extrusions inside one recording, then ONE EditUndo2:
    ///   none   no lock at all;
    ///   after  the lock of the library: after the extrusion, by editing the
    ///          sketch again;
    ///   open   the FIXED relations added while the sketch is still open, with
    ///          the typed array (SketchSegment[]). The M5 finding of 28/9/2026
    ///          refused it with object[] (RPC_E_SERVERFAULT); the typed array was
    ///          measured only after the feature.
    /// If «none» goes back to zero extrusions and «after» does not, H1 holds. If
    /// «open» goes back to zero and its relations are there, the lock can move
    /// into the open sketch and the edit disappears (it is also two rebuilds
    /// fewer per profile, brief R2).
    ///
    ///     UndoSplit.exe              the three ways
    ///     UndoSplit.exe after        one of them
    /// </summary>
    internal static class Program
    {
        private static readonly double[][] Ell =
        {
            new[] { 10.0, 10 }, new[] { 60.0, 10 }, new[] { 60.0, 20 },
            new[] { 20.0, 20 }, new[] { 20.0, 50 }, new[] { 10.0, 50 },
        };

        private static readonly double[][] Square =
        {
            new[] { -50.0, -50 }, new[] { -30.0, -50 }, new[] { -30.0, -30 }, new[] { -50.0, -30 },
        };

        private static int Main(string[] arguments)
        {
            var ways = arguments.Length > 0 ? new[] { arguments[0] } : new[] { "none", "after", "open" };
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }
            var sw = (ISldWorks)raw;
            string template = Template(sw);
            if (template == null) { Console.Error.WriteLine("No part template (.prtdot) found."); return 2; }

            int worst = 0;
            foreach (var way in ways)
            {
                if (way != "none" && way != "after" && way != "open") { Console.Error.WriteLine("Unknown way: " + way); return 2; }
                var doc = (IModelDoc2)sw.NewDocument(template, 0, 0, 0);
                if (doc == null) { Console.Error.WriteLine("NewDocument returned null."); return 2; }
                string title = doc.GetTitle();
                Console.WriteLine("=== lock " + way + ", part " + title);
                try { worst = Math.Max(worst, Run(doc, way)); }
                catch (Exception ex) { Console.WriteLine("EXCEPTION " + ex.GetType().Name + ": " + ex.Message); worst = 1; }
                finally { try { sw.CloseDoc(title); } catch { } }
            }
            return worst;
        }

        private static int Run(IModelDoc2 doc, string way)
        {
            Console.WriteLine("before: " + Count(doc) + " extrusions, volume " + VolumeText(doc));
            doc.Extension.StartRecordingUndoObject();
            int fixedFirst = Extrude(doc, 0, Ell, 10, way);
            int fixedSecond = Extrude(doc, 1, Square, 5, way);
            bool finished = false;
            try { finished = doc.Extension.FinishRecordingUndoObject("UndoSplit probe"); }
            catch (Exception ex) { Console.WriteLine("FinishRecordingUndoObject threw: " + ex.Message); }
            Console.WriteLine("after the plan: " + Count(doc) + " extrusions, volume " + VolumeText(doc) +
                              ", FIXED relations " + fixedFirst + " + " + fixedSecond + ", FinishRecordingUndoObject " + finished);
            doc.EditUndo2(1);
            try { doc.EditRebuild3(); } catch { }
            int left = Count(doc);
            Console.WriteLine("after ONE undo: " + left + " extrusions, volume " + VolumeText(doc) +
                              (left == 0 ? "  -> the whole plan went" : "  -> " + left + " extrusion(s) stayed: the entry did not hold the whole plan"));
            return 0;
        }

        /// <summary>
        /// One profile on the plane of index <paramref name="planeIndex"/> (0
        /// front, 1 top), extruded; returns the FIXED relations read back on its
        /// sketch, or -1.
        /// </summary>
        private static int Extrude(IModelDoc2 doc, int planeIndex, double[][] vertices, double thicknessMm, string way)
        {
            if (!SelectPlane(doc, planeIndex)) { Console.WriteLine("  plane " + planeIndex + " not selected"); return -1; }
            var sm = doc.SketchManager;
            sm.InsertSketch(true);
            bool addToDb = sm.AddToDB, display = sm.DisplayWhenAdded;
            sm.AddToDB = true;
            sm.DisplayWhenAdded = false;
            for (int i = 0; i < vertices.Length; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Length];
                sm.CreateLine(a[0] / 1000, a[1] / 1000, 0, b[0] / 1000, b[1] / 1000, 0);
            }
            sm.AddToDB = addToDb;
            sm.DisplayWhenAdded = display;
            if (way == "open")
            {
                try { Lock(sm.ActiveSketch as ISketch); }
                catch (Exception ex) { Console.WriteLine("  FIXED in the open sketch threw: " + ex.Message); }
            }
            sm.InsertSketch(true);
            var sketchFeature = doc.FeatureByPositionReverse(0) as IFeature;
            doc.ClearSelection2(true);
            if (sketchFeature == null || !sketchFeature.Select2(false, 0)) { Console.WriteLine("  sketch not selected"); return -1; }
            var feature = doc.FeatureManager.FeatureExtrusion3(true, false, false,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                thicknessMm / 1000, 0, false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, (int)swStartConditions_e.swStartSketchPlane, 0, false) as IFeature;
            if (feature == null) { Console.WriteLine("  extrusion not created"); return -1; }
            if (way == "after")
            {
                doc.ClearSelection2(true);
                if (sketchFeature.Select2(false, 0))
                {
                    doc.EditSketch();
                    try { Lock(sketchFeature.GetSpecificFeature2() as ISketch); }
                    catch (Exception ex) { Console.WriteLine("  FIXED after the extrusion threw: " + ex.Message); }
                    if (doc.SketchManager.ActiveSketch != null) doc.SketchManager.InsertSketch(true);
                    doc.EditRebuild3();
                }
                doc.ClearSelection2(true);
            }
            return FixedCount(sketchFeature.GetSpecificFeature2() as ISketch);
        }

        private static void Lock(ISketch sketch)
        {
            var segments = sketch?.GetSketchSegments() as object[];
            if (segments == null || segments.Length == 0) { Console.WriteLine("  no segments to lock"); return; }
            var typed = new SketchSegment[segments.Length];
            for (int i = 0; i < segments.Length; i++) typed[i] = (SketchSegment)segments[i];
            sketch.RelationManager.AddRelation(typed, (int)swConstraintType_e.swConstraintType_FIXED);
        }

        private static int FixedCount(ISketch sketch)
        {
            try
            {
                var relations = sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[];
                if (relations == null) return 0;
                int n = 0;
                foreach (var r in relations)
                {
                    var relation = r as ISketchRelation;
                    if (relation != null && relation.GetRelationType() == (int)swConstraintType_e.swConstraintType_FIXED) n++;
                }
                return n;
            }
            catch { return -1; }
        }

        private static bool SelectPlane(IModelDoc2 doc, int index)
        {
            doc.ClearSelection2(true);
            int seen = 0;
            var f = doc.FirstFeature() as IFeature;
            while (f != null)
            {
                string type = "";
                try { type = f.GetTypeName2(); } catch { }
                if (type == "RefPlane")
                {
                    if (seen == index) return f.Select2(false, 0);
                    seen++;
                }
                f = f.GetNextFeature() as IFeature;
            }
            return false;
        }

        private static int Count(IModelDoc2 doc)
        {
            int n = 0;
            var f = doc.FirstFeature() as IFeature;
            while (f != null)
            {
                string type = "";
                try { type = f.GetTypeName2(); } catch { }
                if (type == "Extrusion") n++;
                f = f.GetNextFeature() as IFeature;
            }
            return n;
        }

        private static string VolumeText(IModelDoc2 doc)
        {
            try
            {
                var bodies = (doc as IPartDoc)?.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (bodies == null || bodies.Length == 0) return "no body";
                double total = 0;
                foreach (var c in bodies)
                {
                    var p = (c as IBody2)?.GetMassProperties(1) as double[];
                    if (p != null && p.Length > 3) total += p[3];
                }
                return (total * 1e9).ToString("0.###", CultureInfo.InvariantCulture) + " mm3";
            }
            catch { return "not read"; }
        }

        private static string Template(ISldWorks sw)
        {
            try
            {
                var p = sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            }
            catch { }
            var its = System.Environment.GetEnvironmentVariable("OCE_DOCUMENT_TEMPLATES");
            foreach (var folder in new[] { @"C:\ProgramData\SolidWorks", its })
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
                foreach (var f in Directory.GetFiles(folder, "*.prtdot", SearchOption.AllDirectories))
                {
                    string b = f.ToLowerInvariant();
                    if (b.Contains(@"\mbd\") || b.Contains("0001mm") || b.Contains("inch")) continue;
                    return f;
                }
            }
            return null;
        }
    }
}
