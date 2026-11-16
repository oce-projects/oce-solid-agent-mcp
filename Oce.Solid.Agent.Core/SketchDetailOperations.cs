using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// WHAT A SKETCH IS MADE OF (report 8cd843 N63, round 20, 28/10/2026).
    ///
    /// The owner corrected four parts by hand and saved them, and the round could not see what he
    /// changed: the state gives a sketch as one «ProfileFeature» among the features, not its
    /// entities, its relations, its dimensions, nor whether it is fully defined. To learn how
    /// he draws, a plan must read them. This operation only reads: it changes nothing.
    ///
    /// The sketches are the features of type «ProfileFeature» (and «3DProfileFeature») among
    /// IFeatureManager.GetFeatures(true), which gives the sub-features too: a sketch absorbed
    /// by its extrusion sits under it in the tree. For each one: the status
    /// (ISketch.GetConstrainedStatus, ConstrainedStatusName), the segments by kind
    /// (ISketch.GetSketchSegments, ISketchSegment.GetType, swSketchSegments_e) and how many are
    /// construction lines, the relations by kind (ISketchRelationManager.GetRelations(swAll),
    /// swConstraintType_e; the guide says some may be null, they are skipped), and the
    /// dimensions with their value in the unit that belongs to them (DocumentState.Dimension).
    /// </summary>
    public partial class SwOperations
    {
        private Outcome SketchDetails(IModelDoc2 model, Dictionary<string, object> a)
        {
            string only = Str(a, "sketch", null);
            if (only != null && only.Trim().Length == 0) only = null;

            object[] all;
            try { all = model.FeatureManager.GetFeatures(true) as object[]; }
            catch (Exception ex) { return Failed("I cannot read the features of the document: " + ex.Message); }

            var names = new List<string>();
            var sketches = new List<object>();
            foreach (var o in all ?? new object[0])
            {
                IFeature f = o as IFeature;
                if (f == null) continue;
                string type = null, name = null;
                try { type = f.GetTypeName2(); name = f.Name; } catch { }
                if ((type != "ProfileFeature" && type != "3DProfileFeature") || string.IsNullOrEmpty(name)) continue;
                // A name once: whether GetFeatures(true) can give a sub-feature twice was not measured.
                if (names.Contains(name)) continue;
                names.Add(name);
                if (only != null && !string.Equals(only.Trim(), name, StringComparison.OrdinalIgnoreCase)) continue;
                sketches.Add(DescribeSketch(f, name, type == "3DProfileFeature"));
            }

            if (only != null && sketches.Count == 0)
                return Failed("There is no sketch «" + only + "» in this document. The sketches are: " +
                              (names.Count == 0 ? "none" : string.Join(", ", names)) +
                              ". Use one of them, or leave «sketch» out to read them all.");

            var measurements = new Dictionary<string, object>
            {
                ["sketches_total"] = names.Count,
                ["sketch_names"] = names.ToArray(),
                ["sketches"] = sketches.ToArray(),
                ["what_it_does_not_check"] =
                    "WHY THE SKETCH IS MADE THAT WAY. I give what it is made of and whether SOLIDWORKS calls it " +
                    "fully defined; which relation holds which entity, and whether the design intent is the " +
                    "right one, I do not say.",
            };
            _log("[agent] sketch_details: " + names.Count + " sketches, " + sketches.Count + " read");
            return SucceededWithoutCreating(measurements);
        }

        private static Dictionary<string, object> DescribeSketch(IFeature f, string name, bool threeD)
        {
            var d = new Dictionary<string, object> { ["name"] = name };
            if (threeD) d["three_d"] = true;
            ISketch s = null;
            try { s = f.GetSpecificFeature2() as ISketch; } catch { }
            if (s == null)
            {
                d["note"] = "it does not answer as a sketch: nothing more could be read";
                return d;
            }

            try { d["status"] = ConstrainedStatusName(s.GetConstrainedStatus()); } catch { d["status"] = null; }

            var segmentKinds = new Dictionary<string, object>();
            int segments = 0, construction = 0;
            bool segmentsRead = false;
            try
            {
                var list = s.GetSketchSegments() as object[];
                segmentsRead = true;
                foreach (var o in list ?? new object[0])
                {
                    ISketchSegment segment = o as ISketchSegment;
                    if (segment == null) continue;
                    segments++;
                    string kind = SegmentKindName(segment.GetType());
                    segmentKinds[kind] = segmentKinds.ContainsKey(kind) ? (int)segmentKinds[kind] + 1 : 1;
                    bool isConstruction = false;
                    try { isConstruction = segment.ConstructionGeometry; } catch { }
                    if (isConstruction) construction++;
                }
            }
            catch { }
            d["segments"] = segmentsRead ? (object)segments : null;
            d["segments_by_kind"] = segmentKinds;
            d["construction_segments"] = construction;

            var relationKinds = new Dictionary<string, object>();
            int relations = -1;
            try
            {
                ISketchRelationManager manager = s.RelationManager;
                var list = manager == null ? null : manager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[];
                if (manager != null) relations = 0;
                foreach (var o in list ?? new object[0])
                {
                    ISketchRelation relation = o as ISketchRelation;
                    if (relation == null) continue;
                    relations++;
                    string kind = RelationKindName(relation.GetRelationType());
                    relationKinds[kind] = relationKinds.ContainsKey(kind) ? (int)relationKinds[kind] + 1 : 1;
                }
            }
            catch { relations = -1; }
            d["relations"] = relations >= 0 ? (object)relations : null;
            d["relations_by_kind"] = relationKinds;

            var dimensions = new List<object>();
            try
            {
                object display = f.GetFirstDisplayDimension();
                int guard = 0;
                while (display != null && guard++ < 1000)
                {
                    IDisplayDimension dd = display as IDisplayDimension;
                    IDimension dimension = dd == null ? null : dd.GetDimension2(0);
                    if (dimension != null)
                    {
                        string dimensionName = null;
                        try { dimensionName = dimension.FullName; } catch { }
                        if (string.IsNullOrEmpty(dimensionName)) { try { dimensionName = dimension.Name; } catch { } }
                        dimensions.Add(DocumentState.Dimension(dimension, dimensionName ?? "?"));
                    }
                    object next = null;
                    try { next = f.GetNextDisplayDimension(display); } catch { }
                    display = next;
                }
            }
            catch { }
            d["dimensions"] = dimensions.ToArray();
            return d;
        }

        /// <summary>
        /// The name of a swSketchSegments_e value (N63): line, arc, ellipse, spline, text,
        /// parabola. Pure, so the contract suite reads it.
        /// </summary>
        internal static string SegmentKindName(int kind)
        {
            switch (kind)
            {
                case (int)swSketchSegments_e.swSketchLINE: return "line";
                case (int)swSketchSegments_e.swSketchARC: return "arc";
                case (int)swSketchSegments_e.swSketchELLIPSE: return "ellipse";
                case (int)swSketchSegments_e.swSketchSPLINE: return "spline";
                case (int)swSketchSegments_e.swSketchTEXT: return "text";
                case (int)swSketchSegments_e.swSketchPARABOLA: return "parabola";
                default: return "kind " + kind;
            }
        }

        /// <summary>
        /// The name of a swConstraintType_e value without its prefix, in lower case
        /// («coincident», «horizontal», «fixed») (N63). Pure, so the contract suite reads it.
        /// </summary>
        internal static string RelationKindName(int kind)
        {
            string name = Enum.GetName(typeof(swConstraintType_e), kind);
            if (string.IsNullOrEmpty(name)) return "kind " + kind;
            const string prefix = "swConstraintType_";
            if (name.StartsWith(prefix, StringComparison.Ordinal)) name = name.Substring(prefix.Length);
            return name.ToLowerInvariant();
        }
    }
}
