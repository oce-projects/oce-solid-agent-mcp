using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// Photograph of what is in the open document right now.
    ///
    /// WHAT IT IS FOR
    /// The language model runs on the server and SolidWorks does not. If it has to propose
    /// "a hole on that face", somebody has to tell it which faces exist.
    /// This is that description: small, numeric, with nothing that can
    /// be mistaken for a command.
    ///
    /// THE IDENTIFIERS ARE OURS AND LAST A SHORT TIME
    /// F1, F2, E7 are not SOLIDWORKS names: they are labels that we assign
    /// by enumerating in a deterministic order. Together with each one we keep a
    /// SIGNATURE (area or length, and a point on the entity): before executing
    /// an operation it is enumerated again and the signature is checked again. If it does not match —
    /// because the user in the meantime added a feature — the operation is
    /// refused instead of working on the wrong face.
    ///
    /// No persistent SOLIDWORKS reference (GetPersistReference and
    /// the like): it is not needed for a plan that lasts minutes, and it would be one more
    /// API to verify for a problem that we do not have.
    ///
    /// THE CEILINGS EXIST AND ARE DECLARED
    /// A bent sheet-metal part has hundreds of faces. Sending them all to a
    /// language model costs tokens and does not help: the largest are sent,
    /// and `truncated: true` is written instead of making it believed that they are all.
    ///
    /// There are four, and each says it has fired:
    ///
    ///   MaxFaces, MaxEdges  -> `truncated: true`
    ///   MaxBodies              -> `bodies_not_listed` + `bodies_total`
    ///   MaxFeature            -> `feature_not_listed` + `feature_total`
    ///                            + `truncated: true`
    ///
    /// The last one was mute up to 0.25.2: the features beyond
    /// the eightieth ended up in `other_feature`, together with those of an
    /// ignored type, and `truncated` stayed false.
    ///
    /// And up to 0.30.0 it kept the OLDEST, which was the worst of the two
    /// ways of truncating: the tree is scanned in creation order, so the
    /// feature that the plan has just made — the only one that whoever reads is
    /// really looking for — was always among the discarded. Now the
    /// last ones are kept (entry E12), and the detail is paid for only on those.
    ///
    /// A CEILING THAT DOES NOT SAY IT HAS FIRED IS NOT A CEILING: it is a
    /// wrong answer that presents itself as complete.
    /// </summary>
    public partial class DocumentState
    {
        public const int MaxFaces = 60;

        /// <summary>
        /// THIRTY, NOT SIXTY, AND THE NUMBER COMES FROM A MEASUREMENT (entry K13).
        ///
        /// ── THE 16 KB CEILING WAS ALREADY EXCEEDED ───────────────────
        ///
        /// The bench check «state: under 16 KB on a frame of four
        /// bars» passed at **15,922** characters against 16,000: seventy-eight of
        /// margin. But the bench runs with **one document open only**, because
        /// it refuses to start otherwise (entry K12). With two documents —
        /// the normal situation of whoever works — the same frame measures
        /// **16,089**. *The ceiling did not have a thin margin: it was already
        /// exceeded, and the check did not see it because the bench cleans its
        /// own desk.*
        ///
        /// ── WHERE THE CHARACTERS ARE, measured on 23/9/2026 ─────────
        ///
        ///     8,718  faces      60 entries   (145 each)
        ///     5,572  edges      60 entries   ( 93 each)
        ///     1,799  everything else
        ///
        /// Eighty-nine per cent are two lists. The entries are already
        /// dry — an edge is `{"uid":"E1","length_mm":1000,
        /// "point_mm":[...],"along":"X","between":["F36","F1"],"body":"C3"}` —
        /// so shortening the fields moves nothing: **the only thing that can be
        /// changed is HOW MANY**. It is the question that the comment of the check
        /// already asked: *«do we really need sixty edges?»*.
        ///
        /// ── WHY THE EDGES AND NOT THE FACES ─────────────────────────
        ///
        /// The faces are the attachment surface of the operations: almost
        /// all take a `face_uid`. The edges are used by few —
        /// fillets, chamfers, dimensions in the drawing — and on a four-bar
        /// weldment they are noise: three hundred and more edges of which sixty are sent
        /// are a sample that cannot be counted on anyway.
        ///
        /// Thirty bring the state of the same frame to ~13,300: **17 per
        /// cent of margin**, which is the threshold that entry K13 had set itself.
        ///
        /// And the ceiling goes on declaring itself: `edges_total` says how many
        /// there were and `truncated` becomes true. *A ceiling that does not say it has
        /// fired is not a ceiling.*
        /// </summary>
        public const int MaxEdges = 30;
        public const int MaxFeature = 80;
        public const int MaxBodies = 24;

        /// <summary>
        /// The feature types that the model can really cite or work
        /// on. They are the names of IFeature.GetTypeName2(), which are NOT
        /// localized (the name in the tree is, the type is not).
        /// </summary>
        private static readonly HashSet<string> FeaturesThatCount =
            new HashSet<string>(StringComparer.Ordinal)
        {
            // «ICE» IS AN EXTRUSION, AND THIS CANNOT BE TOLD FROM THE NAME.
            //
            // An extrusion made on a FACE — a boss, a lip, a
            // turret — is not called «Extrusion»: `GetTypeName2()` returns
            // «ICE». Measured on 10 September 2026, just after `extrude_on_face`
            // was born: the model made a sealing lip
            // and in the state of the next step that lip WAS NOT THERE. It had
            // ended up among the «other features», that is, in a number.
            //
            // It was noticed by the field `other_feature_kinds`, added
            // half an hour earlier precisely for this: a count hides, a
            // list does not.
            "Extrusion", "ICE", "Cut", "HoleWzd", "Fillet", "Chamfer",
            "CircularPattern", "LPattern", "MirrorPattern", "Revolution",
            "Sweep", "Loft", "Shell", "Draft", "Rib", "Dome",
            "WeldMemberFeat", "WeldmentTrimExtend", "WeldmentBead",
            "EndCapFeature", "GussetFeature", "CutListFolder",
            "SheetMetal", "SketchBend", "FlatPattern",
        };

        /// <summary>
        /// The service folders that are not even counted as «other».
        /// They are in every document and say nothing about this part.
        /// </summary>
        private static readonly HashSet<string> ServiceFolders =
            new HashSet<string>(StringComparer.Ordinal)
        {
            "HistoryFolder", "SensorFolder", "DetailCabinet", "SolidBodyFolder",
            "SurfaceBodyFolder", "CommentFolder", "FavoriteFolder",
            "MaterialFolder", "LightsFolder", "OriginProfileFeature",
            "EnvFolder", "DocsFolder", "SketchFolder", "BlockFolder",
            // Added on 10 September 2026 after SEEING them: they were among the
            // types that the state did not know how to read, and they inflated the
            // count of the «other» ones, making it believed that there was
            // something more in the part to look at.
            //
            // «CommentsFolder» in the PLURAL: the line above says
            // «CommentFolder» in the singular, and it never matched
            // anything. A list of names written from memory goes wrong like this: not
            // with an absent name, with an almost right name.
            "CommentsFolder", "EqnFolder", "InkMarkupFolder", "SelectionSetFolder",
        };

        /// <summary>uid -> signature, to find the same entity again at the right moment.</summary>
        public Dictionary<string, EntitySignature> Signatures { get; } =
            new Dictionary<string, EntitySignature>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// EVERY face of the part, largest first, with the body it belongs to
        /// (an index into the bodies) and its area in m2: the ranking whose
        /// positions are the face identifiers (the face at position 0 is F1).
        /// The state lists some of them; the others are not described, but they
        /// are here, and <see cref="TryGetSignature"/> reads one when a plan
        /// cites it. Null on a document that is not a part.
        /// </summary>
        private List<Tuple<IFace2, int, double>> _ranked;

        /// <summary>
        /// EVERY edge of the part, longest first, with the index of its body:
        /// the ranking whose positions are the edge identifiers (E1 is the
        /// longest), whatever the state lists (brief R17). Null on a document
        /// that is not a part.
        /// </summary>
        private List<Tuple<IEdge, int>> _rankedEdges;

        /// <summary>From which feature each body is born, as the state read it (indexed like the bodies).</summary>
        private string[] _bodyFeatureOfFace;

        /// <summary>The signatures built on demand, for the faces the state did not list.</summary>
        private readonly Dictionary<string, EntitySignature> _lazySignatures =
            new Dictionary<string, EntitySignature>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// THE SIGNATURE OF AN ENTITY BY ITS IDENTIFIER — including the faces
        /// the state did not list (entry W3.1-bis.12).
        ///
        /// `Signatures` holds what the state described: the largest faces (or
        /// the ones a filter asked for), the edges, the bodies, the features.
        /// Every other face of the part has an identifier all the same, its rank
        /// by area, and this method builds its signature the first time it is
        /// asked for: a part of 8,000 faces pays for the faces a plan cites, not
        /// for all of them at every reading.
        ///
        /// It answers for a face only by its rank in THIS reading. A plan that
        /// carries the signature it was born with is still checked against it by
        /// whoever resolves the face (the guard is the same as for a listed
        /// face): rank 90 that is not the face the plan saw is refused there,
        /// not here.
        /// </summary>
        public bool TryGetSignature(string uid, out EntitySignature signature)
        {
            signature = null;
            if (string.IsNullOrEmpty(uid)) return false;
            if (Signatures.TryGetValue(uid, out signature)) return true;
            if (_lazySignatures.TryGetValue(uid, out signature)) return true;
            // AN EDGE BEYOND THE THIRTY LISTED (brief R17): its rank among all
            // the edges of this reading, as a filtered reading named it.
            int edgeRank = EdgeFilter.RankOf(uid);
            if (edgeRank >= 1)
            {
                if (_rankedEdges == null || edgeRank > _rankedEdges.Count) return false;
                var edgeBuilt = EdgeSignature(_rankedEdges[edgeRank - 1].Item1);
                if (edgeBuilt == null) return false;
                _lazySignatures[uid] = edgeBuilt;
                signature = edgeBuilt;
                return true;
            }
            int rank = FaceFilter.RankOf(uid);
            if (rank < 1 || _ranked == null || rank > _ranked.Count) return false;
            var built = FaceSignature(_ranked[rank - 1].Item1);
            if (built == null) return false;
            built.BoxPartMm = BoxPartMm;
            _lazySignatures[uid] = built;
            signature = built;
            return true;
        }

        public Dictionary<string, object> Data { get; private set; } =
            new Dictionary<string, object>();

        /// <summary>
        /// The box of the whole part, in mm: {minX, minY, minZ, maxX, maxY,
        /// maxZ}. Null when the document has no bodies.
        ///
        /// It is a NEW PROPERTY and not one more parameter somewhere:
        /// the bench calls this class by reflection, and changing the set
        /// of signatures of a method breaks whoever calls it even when the
        /// compiler is silent.
        /// </summary>
        public double[] BoxPartMm { get; private set; }

        /// <summary>
        /// The document from which this state was read.
        ///
        /// It serves to resolve the persistent references (entry E6):
        /// `GetObjectByPersistReference3` is on `IModelDocExtension`, and whoever
        /// finds a face again has in hand the state, not the document.
        ///
        /// It is a PROPERTY and not one more parameter on `Read`: the bench
        /// calls this class by reflection, and changing the set of
        /// signatures of a method breaks whoever calls it even when the compiler
        /// is silent. A new property is seen by nobody. It is the lesson of
        /// `ReadWithAnchor`, applied before bumping into it again.
        ///
        /// It holds for THIS session: it is not serialized and not carried around.
        /// </summary>
        public IModelDoc2 Model { get; private set; }

        /// <summary>
        /// A state that holds the document and nothing read from it: for a step
        /// whose operation never looks at the state (brief R2, see
        /// <see cref="SwOperations.UsesState"/>). No identifier, no signature:
        /// an operation that needed them would find none and say so.
        /// </summary>
        public static DocumentState WithoutReading(IModelDoc2 model)
        {
            return new DocumentState { Model = model };
        }

        /// <summary>
        /// A feature waiting to know whether it goes into the list.
        ///
        /// It carries only what the walk over the tree has already read — the type and the
        /// name, which are read anyway to understand what it is — plus the
        /// COM reference. The detail, which costs, is paid for later and only on the
        /// survivors.
        /// </summary>
        private sealed class Candidate
        {
            public IFeature F;
            public string Type;
            public string Name;
        }

        public class EntitySignature
        {
            public string Type;          // "FACE" | "EDGE"
            public double Measure;        // area in mm2, or length in mm
            public double[] PointMm;     // a point on the entity, in mm
            public double[] Normal;     // only for planar faces
            public object Entity;        // IFace2 / IEdge, valid in this session

            /// <summary>
            /// FROM WHICH FEATURE THIS FACE COMES, by identifier and
            /// by name. `-1` when it could not be read.
            ///
            /// WHY THE IDENTIFIER AND NOT THE NAME. The name is localized
            /// («Boss-Extrude1» in English, another word in another language) and can be renamed by hand:
            /// comparing it would mean that the same face stops being
            /// the same because somebody changed the language of
            /// SOLIDWORKS. `IFeature.GetID()` does not change.
            ///
            /// The name is kept all the same, but only FOR THE MESSAGES: whoever reads a
            /// refusal wants to know «it comes from Cut-Extrude1», not «it comes
            /// from feature 42».
            ///
            /// `IFace2.GetFeature()` gives the GENERATING feature, not the last one that
            /// touched the face: measured on 14/9/2026 (entry 0.5), a fillet
            /// shrinks three faces and all three keep saying
            /// «extrusion».
            /// </summary>
            public int FeatureId = -1;
            public string Feature;

            /// <summary>
            /// THE BOX OF THE PART when this signature was measured, in
            /// mm: {minX, minY, minZ, maxX, maxY, maxZ}. Null if it could not
            /// be read.
            ///
            /// IT IS NOT A CRITERION AND CANNOT BECOME ONE. It serves for one single
            /// thing: writing in a message where the face was with respect to the
            /// part, when the part has changed. The reason why it cannot
            /// enter the identity is written in full in
            /// <see cref="Sentences.WhereItWasAndWhereItIs"/>, which is the only place in
            /// the whole library where these numbers are divided among themselves — and
            /// it returns a SENTENCE, not a number.
            /// </summary>
            public double[] BoxPartMm;
        }

        /// <summary>
        /// WHAT IS SELECTED, with the identifiers of the state.
        ///
        /// THE TWO AWKWARD CASES ARE DECIDED HERE, not discovered afterwards:
        ///
        ///   * **nothing selected** — an empty list is returned, and the caller
        ///     does not write the field. It is not an error: most of the
        ///     time nothing is selected;
        ///   * **several things together** — all are returned, in the order in which
        ///     SOLIDWORKS gives them. Choosing one would be guessing, and
        ///     refusing would be useless: whoever selected four faces
        ///     probably meant to select four.
        ///
        /// And a third case, which the criterion of the entry did not name: a selected
        /// entity that in the state **has no identifier** — because it is
        /// beyond the ceiling of sixty faces, or because it is a vertex, which the
        /// state does not describe at all. There it is said **what it is** without giving it
        /// a name: «a face I did not describe» is information, «there is
        /// nothing» would be a lie.
        ///
        /// The type is read from the OBJECT and not from the enum of the selection: an
        /// `as IFace2` cannot go wrong, a misread enum number can.
        /// </summary>
        private static List<Dictionary<string, object>> TheSelection(
            IModelDoc2 model, Dictionary<string, EntitySignature> signatures)
        {
            var outside = new List<Dictionary<string, object>>();
            try
            {
                var selection = model?.SelectionManager as ISelectionMgr;
                if (selection == null) return outside;
                int howMany = selection.GetSelectedObjectCount2(-1);
                for (int i = 1; i <= howMany && i <= 20; i++)
                {
                    object obj = null;
                    try { obj = selection.GetSelectedObject6(i, -1); } catch { }
                    if (obj == null) continue;

                    string what =
                        obj is IFace2 ? "face" :
                        obj is IEdge ? "edge" :
                        obj is IBody2 ? "body" :
                        obj is IVertex ? "vertex" :
                        obj is IFeature ? "feature" : null;

                    var entry = new Dictionary<string, object>();
                    if (what != null) entry["kind"] = what;

                    // The name, if the state gave it one.
                    foreach (var f in signatures)
                    {
                        if (f.Value.Entity == null) continue;
                        if (!ReferenceEquals(f.Value.Entity, obj) &&
                            !Equals(f.Value.Entity, obj)) continue;
                        entry["uid"] = f.Key;
                        break;
                    }

                    if (!entry.ContainsKey("uid"))
                    {
                        // A feature is called by its name, which in the
                        // state IS the uid.
                        var featureItem = obj as IFeature;
                        if (featureItem != null)
                        {
                            try { entry["uid"] = featureItem.Name; } catch { }
                        }
                    }
                    if (!entry.ContainsKey("uid"))
                        entry["note"] =
                            "selected, but it is not among those I described: " +
                            "I cannot name it.";

                    if (entry.Count > 0) outside.Add(entry);
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// THE ANCHOR OF A FACE: the area centroid, in mm.
        ///
        /// WHY `point_mm` IS NOT ENOUGH. That point does a different job and does it
        /// well: it is the point GUARANTEED on the face from which one selects, and
        /// for this it is taken with `GetClosestPointOn(0,0,0)` instead of the
        /// centre of the box — the centre of a face with a hole falls into the void.
        ///
        /// As a FINGERPRINT however it is bad, because for almost all the
        /// faces that do not look at the origin it is a point of the **frontier**: it
        /// moves when the BORDER moves, not the face.
        ///
        /// Measured on 14/9/2026: plate 100x60x20, top face with anchor
        /// `[0,20,0]`. A notch of 15x15 in a corner — **3.75%** of the area
        /// — moves that point to `[15,20,0]`, that is by **15 mm**. The area
        /// centroid, in the same case, moves by about 1.9 mm: proportional to
        /// how much the face really changed, instead of to where the
        /// border happened to be.
        ///
        /// AND THIS ALONE REMOVES NO REFUSAL. The tolerance is 0.05 mm,
        /// and 1.9 mm exceeds it all the same. It serves to make possible a
        /// tolerance PROPORTIONAL to the change, which is entry E8: here the
        /// number is prepared, there it will be decided what to do with it.
        ///
        /// THE TESSELLATION IS USED, and not without consequences: `GetTessTriangles`
        /// depends on the display quality of the document. Within a
        /// session it is deterministic — measured, ten identical readings — but
        /// between two machines with different settings it might not be, and for
        /// this the anchor **goes alongside** `point_mm` instead of replacing it.
        /// </summary>
        /// <summary>
        /// The persistent reference that SOLIDWORKS gives to an entity, in
        /// base64. Null if it cannot be obtained.
        ///
        /// IT IS AN OPAQUE BLOB, and must be treated as such: it is not interpreted, not
        /// compared in pieces, not written to disk and not carried from one
        /// document to another. It holds inside THIS document and for this
        /// plan session, which lasts minutes.
        ///
        /// `GetPersistReference3` answers a `byte[]`: measured on 14/9/2026,
        /// 375-423 bytes per face on a part of eight faces. It is not an integer
        /// and not a name — it is a structure, and the only honest way to
        /// ship it in a JSON is base64.
        ///
        /// WHY THEY ARE USED NOW. `DocumentState` declared that it did not use them:
        /// «it is not needed for a plan that lasts minutes, and it would be one more API to
        /// verify for a problem that we do not have». It was right for the
        /// problem of that time. The problem is there now: a dimension upstream moves
        /// a face, the anchor on the point is lost, and the plan is refused
        /// although it is right. Measured in 0.1: the persist ID found again
        /// **8 faces out of 8** in all five successful edits, including the
        /// three in which the anchor on the point is lost.
        /// </summary>
        private static string PersistIdOf(IModelDoc2 model, object entity)
        {
            if (model == null || entity == null) return null;
            try
            {
                var ext = model.Extension;
                if (ext == null) return null;
                var blob = ext.GetPersistReference3(entity) as byte[];
                if (blob == null || blob.Length == 0) return null;
                return Convert.ToBase64String(blob);
            }
            catch { return null; }
        }

        private static double[] FaceAnchor(IFace2 face)
        {
            try
            {
                // THE TESSELLATION ARRIVES AS `float`, NOT `double`.
                // `IGetTessTriangles` in the interop is declared `Single`, and
                // an `as double[]` on an array of floats returns **null
                // silently**: the anchor came out empty on every face and there was
                // no error anywhere. It is the same family
                // as the `as object[]` on a List<>, which already cost a check
                // turned off for half a day.
                var raw = face?.GetTessTriangles(true);
                double[] t = raw as double[];
                if (t == null && raw is float[] f)
                {
                    t = new double[f.Length];
                    for (int k = 0; k < f.Length; k++) t[k] = f[k];
                }
                if (t == null || t.Length < 9) return null;

                // Centroid weighted on the AREA of the triangles, not mean of the
                // vertices: a face tessellated dense on one side and wide
                // on the other would pull the mean to where the triangles are more
                // numerous, which means nothing.
                double sx = 0, sy = 0, sz = 0, weight = 0;
                for (int i = 0; i + 8 < t.Length; i += 9)
                {
                    double ax = t[i], ay = t[i + 1], az = t[i + 2];
                    double bx = t[i + 3], by = t[i + 4], bz = t[i + 5];
                    double cx = t[i + 6], cy = t[i + 7], cz = t[i + 8];

                    double ux = bx - ax, uy = by - ay, uz = bz - az;
                    double vx = cx - ax, vy = cy - ay, vz = cz - az;
                    double nx = uy * vz - uz * vy;
                    double ny = uz * vx - ux * vz;
                    double nz = ux * vy - uy * vx;
                    double area = Math.Sqrt(nx * nx + ny * ny + nz * nz) / 2.0;
                    if (area <= 0) continue;

                    sx += area * (ax + bx + cx) / 3.0;
                    sy += area * (ay + by + cy) / 3.0;
                    sz += area * (az + bz + cz) / 3.0;
                    weight += area;
                }
                if (weight <= 0) return null;
                return new[]
                {
                    Math.Round(Units.Mm(sx / weight), 3),
                    Math.Round(Units.Mm(sy / weight), 3),
                    Math.Round(Units.Mm(sz / weight), 3),
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// The feature that generated a face: identifier and name.
        ///
        /// It does not raise and does not complain: a face without a readable
        /// feature comes out with `-1` and `null`, and whoever uses it must know how to live
        /// without. Better no data than invented data — a comparison between
        /// two `-1` would say «same feature» of any two faces.
        /// </summary>
        private static void FeatureOfFace(
            IFace2 face, out int id, out string itemName)
        {
            id = -1;
            itemName = null;
            try
            {
                var f = face?.GetFeature() as IFeature;
                if (f == null) return;
                itemName = f.Name;
                id = f.GetID();
            }
            catch { id = -1; }
        }

        /// <summary>
        /// Reads the document. It must be called ON THE SOLIDWORKS THREAD.
        /// </summary>
        /// <param name="sw">
        /// The application, if the caller has it. It serves for one single and
        /// important thing: saying WHICH DOCUMENTS ARE OPEN. From an `IModelDoc2`
        /// one cannot go back up to the application — searched by reflection on 11
        /// September 2026: neither `IModelDoc2` nor `IModelDocExtension` have
        /// access to the application — so either the caller passes it or it
        /// is not known. It stayed optional so as not to break whoever called before.
        /// </param>
        /// <param name="withAnchor">
        /// Write the anchor of every face inside the state.
        ///
        /// OFF BY DEFAULT, and not out of generic prudence. The anchor is the
        /// area centroid and serves to RECOGNISE a face between two readings;
        /// it does not serve whoever reads the state to decide what to do, and offering it
        /// next to `point_mm` would mean giving two points when only one must be used
        /// — and the wrong choice would not give an error, it would give a hole
        /// outside the material.
        ///
        /// And it costs: on a four-bar frame it is **1,400 characters**, which
        /// take the state from 15.7 KB to 17.1 — beyond the 16 KB ceiling that the
        /// bench watches.
        ///
        /// Whoever builds the SIGNATURES of the steps turns it on, and turns it off before
        /// sending the state to anybody.
        /// </param>
        /// <remarks>
        /// THIS SIGNATURE IS NOT TO BE TOUCHED, AND IT IS NOT GENERIC PRUDENCE.
        ///
        /// `Read` is called also by the bench, **by reflection**, and there two
        /// things that are harmless for a compiler are not:
        ///
        ///   * adding a parameter with a default value → `Invoke` with
        ///     two arguments on a method that wants three answers «Parameter count
        ///     mismatch». Measured on
        ///     14/9/2026: the bench went from 88 checks and 0 failed to **70 and 58**;
        ///   * adding an OVERLOAD → `GetMethod("Read")` finds two and
        ///     answers «Ambiguous match found». Measured half an hour
        ///     later, repairing the first error with the second.
        ///
        /// **Any change to the set of signatures of a method breaks
        /// whoever calls it by reflection**, even one that the compiler
        /// accepts without saying anything. What is needed in addition goes in a
        /// method with a different NAME — see <see cref="ReadWithAnchor"/>.
        /// </remarks>
        public static DocumentState Read(IModelDoc2 model, ISldWorks sw = null)
        {
            return ReadWithAnchor(model, sw, false);
        }

        /// <summary>
        /// Like <see cref="Read"/>, saying whether the anchor of every
        /// face is wanted too.
        ///
        /// A different name and not an overload: see the note on `Read`.
        /// </summary>
        public static DocumentState ReadWithAnchor(
            IModelDoc2 model, ISldWorks sw, bool withAnchor)
        {
            return ReadListing(model, sw, withAnchor, null, null);
        }

        /// <summary>
        /// Like <see cref="ReadWithAnchor"/>, listing the faces a filter asks
        /// for instead of the largest ones (see <see cref="FaceFilter"/>).
        /// With a null filter it is exactly <see cref="ReadWithAnchor"/>.
        ///
        /// A different name and not an overload, as for the other readers (see
        /// the note on <see cref="Read"/>): the bench calls this class by
        /// reflection.
        ///
        /// WHATEVER THE FILTER, EVERY FACE HAS AN IDENTIFIER: the rank of the
        /// face among all the faces of the part, largest first. The faces not
        /// listed are not described, but a plan that cites one by its number
        /// finds it (<see cref="TryGetSignature"/>), and `faces_total` says
        /// how many there are.
        /// </summary>
        public static DocumentState ReadFaces(
            IModelDoc2 model, ISldWorks sw, bool withAnchor, FaceFilter filter)
        {
            return ReadListing(model, sw, withAnchor, filter, null);
        }

        /// <summary>
        /// Like <see cref="ReadFaces"/>, with a filter on the edges too (brief
        /// R17; see <see cref="EdgeFilter"/>): the state lists the edges that
        /// match instead of the thirty longest, each with its rank among ALL
        /// the edges of the part. Either filter may be null. A new name, for the
        /// same reason as <see cref="ReadFaces"/>: the bench calls this class by
        /// reflection.
        /// </summary>
        public static DocumentState ReadFiltered(
            IModelDoc2 model, ISldWorks sw, bool withAnchor, FaceFilter faces, EdgeFilter edges)
        {
            return ReadListing(model, sw, withAnchor, faces, edges);
        }

        private static DocumentState ReadListing(
            IModelDoc2 model, ISldWorks sw, bool withAnchor, FaceFilter filter, EdgeFilter edgeFilter)
        {
            var docState = new DocumentState();
            if (model == null) return docState;
            docState.Model = model;
            var lap = PhaseClock.Laps("state");

            int docType = 0;
            try { docType = model.GetType(); } catch { }

            var data = new Dictionary<string, object>
            {
                ["path"] = model.GetPathName() ?? "",
                ["document_title"] = model.GetTitle() ?? "",
                ["kind"] = SwOperations.DocumentTypeName(docType),
            };

            // THE LANGUAGE SOLIDWORKS WRITES NAMES IN, so that whoever reads the names of
            // the tree knows which column of solidworks-names.json they come from, and
            // so that an unknown language (no column) says it fell back to English.
            if (sw != null)
            {
                try { data["solidworks_language"] = SolidWorksLanguage.Read(sw).ToState(); } catch { }
            }

            // WHO ELSE IS OPEN.
            //
            // Up to 0.39.0 the state described ONE document and took it for
            // granted that it was the only one. It is no longer: a drawing lives
            // next to its model, an assembly next to its parts, and
            // `CreateDrawViewFromModelView3` like `AddComponent5` want
            // the PATH OF AN OPEN DOCUMENT. Without this list the
            // model has no way to name it, and a view attached to a
            // closed document comes out empty without giving an error.
            var others = OpenDocuments(sw, model);
            if (others != null && others.Count > 0) data["documents_open"] = others;

            if (docType == (int)swDocumentTypes_e.swDocDRAWING)
            {
                DescribeDrawing(model as IDrawingDoc, data, sw);
                docState.Data = data;
                return docState;
            }

            // THE ASSEMBLY IS DESCRIBED, NOT REFUSED (entry GA1).
            //
            // Up to 0.91.0 there was a single line here — «assembly document:
            // the assistant does not modify it yet» — and it returned. It was true and
            // made the model blind: the state is the only thing it sees, and from
            // that line it could not know how many components there were, what they
            // were called, which were suppressed. It could not even
            // refuse knowingly.
            //
            // Now it is described (`AssemblyState.cs`), and what is NOT yet known how to
            // do is said in there, in full. The branch comes BEFORE the cast to
            // `IPartDoc` because an assembly is not a part and would fall into the
            // `part == null` below.
            if (docType == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                DescribeAssembly(model as IAssemblyDoc, data);
                docState.Data = data;
                return docState;
            }

            var partDoc = model as IPartDoc;
            if (partDoc == null)
            {
                // It is neither a part, nor a drawing, nor an assembly: here nothing
                // is guessed. Saying «of an assembly» as the previous version did
                // would be a wrong diagnosis on a document that
                // nobody looked at.
                data["solid_body"] = false;
                data["note"] =
                    "document of type «" + SwOperations.DocumentTypeName(docType) +
                    "»: I cannot read it.";
                docState.Data = data;
                return docState;
            }

            lap.Next("header");
            // ── features of the tree, and axes ──────────────────────────────
            //
            // INCLUSION FILTER, NOT EXCLUSION.
            //
            // Up to 0.29.1 three entries were excluded here and everything else
            // came in: HistoryFolder, SensorFolder, DetailCabinet,
            // SolidBodyFolder, CommentFolder, FavoriteFolder,
            // OriginProfileFeature. They are service folders that can be neither
            // cited nor worked on, and they took ~600 characters and
            // SEVEN places of the ceiling in every message, driving out real features.
            // Now what is known how to read is declared; the rest is counted and
            // that is all, so the model knows there is more without paying for it.
            var feature = new List<Dictionary<string, object>>();
            var axes = new List<Dictionary<string, object>>();
            int nPlanes = 0;
            int otherFeatures = 0;
            // TWO COUNTERS, NOT ONE. `otherFeatures` counts the features of a
            // TYPE that does not go into the list — invisible by choice, and the types
            // are said in `other_feature_kinds`. `unlistedFeatures` counts
            // those that would have gone in and do not fit because of the ceiling: they are
            // two different things, and summing them hid the second.
            int unlistedFeatures = 0;
            // The features that COULD go into the list, all collected
            // and chosen afterwards. See the comment on `candidate.Add` further below.
            var candidate = new List<Candidate>();
            // WHICH, not only HOW MANY.
            //
            // «other_feature: 7» says there is more and does not say what,
            // and that number is a place where things disappear. Measured
            // on 10 September 2026: just after `extrude_on_face` was born, the
            // feature it had just created did NOT appear in the
            // state — it had ended up among the seven «other» ones, because its type
            // is not what I thought. The model had just done
            // something and no longer saw it.
            //
            // The names of the types are short and few: listing them costs nothing and
            // makes a blind spot visible instead of hiding it behind a
            // number.
            var unknownTypes = new SortedSet<string>(StringComparer.Ordinal);
            // The names of the features cited by the faces, once only.
            var featureNames = new SortedDictionary<int, string>();
            int featuresInError = 0;
            IFeature f = partDoc.IFirstFeature();
            while (f != null)
            {
                string typ = "";
                string itemName = "";
                try { typ = f.GetTypeName2(); itemName = f.Name; } catch { }

                if (typ == "RefPlane") nPlanes++;
                // ONLY THE REAL AXES.
                //
                // In here there was also "OriginProfileFeature", that is, the origin
                // of the part — which is always there. The result: the precondition
                // «an axis exists» came out true in ANY document, the
                // model saw itself offered the circular pattern, and
                // at execution SelectByID2(name, "AXIS", ...) failed because
                // the origin is not an axis. A tool always offered and
                // almost never usable: the model tries it, the plan falls at the
                // first step, and it looks like its fault.
                else if (typ == "RefAxis")
                {
                    // «uid» and «name» were THE SAME STRING, written twice.
                    // On eighty features with names of twenty characters that was
                    // over two kilobytes of message that said
                    // nothing — and the room they take is exactly what
                    // the parameters below need. «uid» stays, which is the name
                    // with which the whole rest of the system calls the entities.
                    var axisEntry = new Dictionary<string, object> { ["uid"] = itemName, ["kind"] = typ };

                    // THE SIGNATURE OF THE AXIS (entry E4, the part that was missing). Up
                    // to 0.113.0 the state of an axis was {uid, type} and that was all:
                    // nothing to sign, and `axis_uid` in the patterns was
                    // checked for EXISTENCE and not for IDENTITY. An axis
                    // redone elsewhere with the name of before passed. Now: a point
                    // on the line, the direction, the identifier of the feature.
                    try
                    {
                        var par = (f.GetSpecificFeature2() as IRefAxis)?.GetRefAxisParams() as double[];
                        if (par != null && par.Length >= 6)
                        {
                            double dx = par[3] - par[0], dy = par[4] - par[1], dz = par[5] - par[2];
                            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (len > 1e-12)
                            {
                                var point = new[] { Math.Round(Units.Mm(par[0]), 3), Math.Round(Units.Mm(par[1]), 3), Math.Round(Units.Mm(par[2]), 3) };
                                var direction = new[] { Math.Round(dx / len, 5), Math.Round(dy / len, 5), Math.Round(dz / len, 5) };
                                axisEntry["point_mm"] = point;
                                axisEntry["direction"] = direction;
                                docState.Signatures[itemName] = new EntitySignature
                                {
                                    Type = "AXIS", PointMm = point, Normal = direction, Entity = f,
                                    Feature = itemName,
                                };
                            }
                        }
                    }
                    catch { }
                    try { axisEntry["feature_id"] = f.GetID(); } catch { }
                    axes.Add(axisEntry);
                }
                else if (FeaturesThatCount.Contains(typ))
                {
                    // ALL ARE COLLECTED, AND THE CHOICE IS MADE AFTERWARDS (entry E12).
                    //
                    // Up to 0.30.0 there was `if (feature.Count <
                    // MaxFeature)` here, that is, the first eighty
                    // ENCOUNTERED were kept. And the tree is scanned with `GetNextFeature`,
                    // in CREATION order: the first eighty are the OLDEST,
                    // and the feature that the plan has just created
                    // is among those discarded. The model did not find it, and
                    // could conclude that the operation had done
                    // nothing — and redo it.
                    //
                    // THE COST DOES NOT CHANGE, and it is the reason why it can be
                    // done. `GetTypeName2()` and `Name` are already read for EVERY
                    // feature of the tree, before this check, to
                    // decide what type it is: the light pass already existed and
                    // was already paid for. Here only a reference to
                    // a list is added. The expensive part — `GetID`, `IsSuppressed`,
                    // `GetErrorCode2`, the parameters, the dimensions — stays on eighty
                    // exact features, only that now they are the LAST ones.
                    candidate.Add(new Candidate { F = f, Type = typ, Name = itemName });
                }
                else if (!ServiceFolders.Contains(typ))
                {
                    otherFeatures++;
                    if (!string.IsNullOrEmpty(typ)) unknownTypes.Add(typ);
                }
                try { f = (IFeature)f.GetNextFeature(); } catch { f = null; }
            }

            // ── WHICH EIGHTY (entry E12) ──────────────────────────────────
            //
            // The tree is scanned in CREATION order, so the first ones are
            // the oldest. Keeping the first eighty means throwing away
            // precisely the feature that the plan has just created — the one that
            // whoever reads is looking for. The LAST ones are kept.
            //
            // The detail is paid for HERE, and only on these: it is the same expense as
            // before, on different features.
            //
            // THE CEILING IS DECLARED, like the other three. An incomplete list
            // that is declared is a different thing from an incomplete list passed off
            // as whole — and that is what this state did up to
            // 0.25.2, with `truncated: false` on a truncated list.
            if (candidate.Count > MaxFeature)
            {
                unlistedFeatures = candidate.Count - MaxFeature;
                candidate.RemoveRange(0, unlistedFeatures);
            }
            foreach (var c in candidate)
            {
                var entryF = FeatureDetail(c.F, c.Type, c.Name, ref featuresInError);
                if (entryF != null) feature.Add(entryF);
            }

            lap.Next("features");
            // ── bodies, faces, edges ──────────────────────────────────────
            //
            // THE BODIES ARE THE NOVELTY OF 0.30.0, AND THEY ANSWER A QUESTION
            // THAT BEFORE HAD NO ANSWER.
            //
            // Up to 0.29.1 of what was in the document one single thing was said:
            // `solid_body`, yes or no. To whoever has to work on a
            // frame already drawn — «cap the ends of those bars»,
            // «make the holes on the upright» — a boolean is of no use, and the
            // client asked for it by name: «are there missing tools to
            // know what has already been drawn?».
            //
            // BVisibleOnly: false, and the false is intended. Before it was true: a
            // body hidden to look inside the part vanished from the state
            // without anything saying so, and the features on it
            // became impossible without a readable reason.
            var faces = new List<Dictionary<string, object>>();
            var edges = new List<Dictionary<string, object>>();
            var describedBodies = new List<Dictionary<string, object>>();
            bool hasBody = false;
            bool truncated = false;

            // THE BOX OF THE PART, not only how big it is: WHERE IT IS.
            // It is needed outside the bodies block, so it is declared here. See
            // the comment on `bounding_box_from_mm` further below.
            double[] partBox = null;

            object bodiesObj = null;
            try { bodiesObj = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false); } catch { }
            var bodies = (bodiesObj as object[] ?? new object[0])
                .Select(o => o as IBody2).Where(b => b != null).ToList();

            // The material: EXPLICIT null when there is none.
            //
            // Omitting the field and writing it null are not the same thing. A
            // field that is missing the model does not see; a `material: null` tells it
            // that that place exists and is empty — and it is the only way
            // for it to notice that it must fill it. Up to 0.29.1 the
            // materials folder was in fact excluded on purpose from the tree.
            string material = null;
            try
            {
                string archive;
                // "" IS THE ACTIVE CONFIGURATION (finding L17-1, NOT REAL). The
                // Z-C21.3 probe of L17-1 and M10 (28/9/2026) read "" against each
                // configuration name, activating each in turn: "" followed the
                // active configuration four times out of four. So the material field is
                // the material of the active configuration only.
                var read = partDoc.GetMaterialPropertyName2("", out archive);
                if (!string.IsNullOrWhiteSpace(read)) material = read;
            }
            catch { }
            data["material"] = material;

            // THE ACTIVE CONFIGURATION, AND THE OTHERS.
            //
            // A part with several configurations is the same file that describes
            // several parts: the left bracket and the right one, with and without a hole. The
            // faces, the bodies, the volume and the mass written above are
            // those of the ACTIVE configuration, and of no other.
            //
            // Without this field the state says «volume 84200 mm3» and whoever reads
            // has no way of knowing OF WHICH variant: it is exactly the
            // defect that gives no error. It is always written, even when the
            // configuration is only one, because a field that appears only
            // sometimes teaches not to look for it.
            //
            // The name often has something in angle brackets
            // («Default<As Machined>»), and THAT IS THE NAME: it is not a label
            // that the name carries around. A part born from the template of
            // this company has two real configurations, «Default<As Machined>»
            // and «Default<As Welded>» — the weldment pair. It is reported as it is,
            // because it is what `activate_configuration` wants back.
            try
            {
                var configHandler = model.ConfigurationManager as IConfigurationManager;
                if (configHandler != null) data["configuration"] = configHandler.ActiveConfiguration.Name;
            }
            catch { }
            try
            {
                var configNames = model.GetConfigurationNames() as string[];
                if (configNames != null && configNames.Length > 0) data["configurations"] = configNames;
            }
            catch { }
            // HOW MANY IT DECLARES, which is not necessarily how many the
            // rows of «configurations» are: the names are given by `GetConfigurationNames`,
            // the count is given by `GetConfigurationCount`, and they are two different
            // calls. Both are written, because the day they do not
            // match that is the fact to know -- not a detail to
            // hide by choosing which of the two to show.
            try { data["how_many_configurations"] = model.GetConfigurationCount(); }
            catch { }

            // THE CUSTOM PROPERTIES, IN THE TWO SEPARATE DRAWERS.
            //
            // They are what ends up in the title block and in the PDM card.
            // They are in two fields and not in one because they are in two different
            // places: those of the document and those of the active configuration
            // are not inherited and do not see each other. A single field,
            // «properties», would be a merge that does not exist in the file,
            // and whoever read it back would have no way of knowing where to look.
            lap.Next("bodies_box");
            var document = ReadProperty(model, "");
            if (document.Count > 0) data["property"] = document;
            try
            {
                object activate;
                if (data.TryGetValue("configuration", out activate))
                {
                    var config = ReadProperty(model, Convert.ToString(activate) ?? "");
                    if (config.Count > 0) data["configuration_properties"] = config;
                }
            }
            catch { }

            lap.Next("properties");
            if (bodies.Count > 0)
            {
                hasBody = true;

                // ── the bodies, one by one ────────────────────────────────
                for (int i = 0; i < bodies.Count && i < MaxBodies; i++)
                {
                    var body = bodies[i];
                    var entry = new Dictionary<string, object> { ["uid"] = "C" + (i + 1) };
                    try { entry["name"] = body.Name; } catch { }
                    var box = BoundingBox(body);
                    if (box != null)
                    {
                        entry["bounding_box_mm"] = new[]
                        {
                            Math.Round(box[3] - box[0], 2),
                            Math.Round(box[4] - box[1], 2),
                            Math.Round(box[5] - box[2], 2),
                        };
                        entry["center_mm"] = new[]
                        {
                            Math.Round((box[0] + box[3]) / 2, 2),
                            Math.Round((box[1] + box[4]) / 2, 2),
                            Math.Round((box[2] + box[5]) / 2, 2),
                        };
                    }
                    // The bridge between a body and the feature that made it:
                    // it is what allows saying «the bars» instead of «C3».
                    //
                    // AND HERE IT IS NOT REMOVED, EVEN IF IT LOOKS REDUNDANT.
                    //
                    // SolidWorks names the bodies of a weldment with the name
                    // of the feature plus a suffix:
                    //   name     = TUBO STRUTTURALE QUADRO 80X80X5(1)[4]
                    //   created_by = TUBO STRUTTURALE QUADRO 80X80X5(1)
                    // On 10 September 2026 this resemblance made me
                    // remove the field when the name begins with the
                    // feature. Wrong, for two reasons that the bench
                    // told at once:
                    //
                    // 1. the saving was 192 characters on a frame, not 840:
                    //    the bars are FOUR, not twenty-four (I had used
                    //    the `MaxBodies` ceiling in place of the real number);
                    // 2. and above all: `name` and `created_by` share the
                    //    string but are NOT the same thing. One is what the body
                    //    is called, the other is where it comes from. That today
                    //    one can be derived from the other is a coincidence
                    //    of how SolidWorks names welded bodies, not a
                    //    property to lean on — and deriving it would
                    //    anyway be a DEDUCTION, which is exactly what
                    //    we are removing from the model's way.
                    //
                    // The rule «write only what adds» stays good
                    // for identical fields (`uid`/`name`) and for those that
                    // repeat another field (`from_feature` on a weldment).
                    // It does not hold for two fields that say different things.
                    string feat = BodyFeature(body);
                    if (feat != null) entry["created_by"] = feat;
                    describedBodies.Add(entry);

                    // And the body must be able to be FOUND AGAIN. Without this signature,
                    // «C1» in the state would be a label that the model
                    // reads and nobody knows how to bring back to an IBody2 at the time of
                    // executing: it is the bridge that is missing for «cap the ends
                    // of this frame» in a new message.
                    //
                    // The signature of a body is its centre plus the bounding box:
                    // they are the two numbers that change if somebody modifies the
                    // part between the proposal and the execution.
                    docState.Signatures["C" + (i + 1)] = new EntitySignature
                    {
                        Type = "BODY",
                        // ZERO-E-ZERO: whoever reads `Measure` always looks at `> 0`, and
                        // nobody reads it for the bodies; zero here is «not measured».
                        Measure = box == null ? 0 : Math.Round(
                            (box[3] - box[0]) * (box[4] - box[1]) *
                            (box[5] - box[2]), 2),
                        PointMm = entry.ContainsKey("center_mm")
                            ? entry["center_mm"] as double[]
                            : null,
                        Entity = body,
                    };
                }
                if (bodies.Count > MaxBodies) data["bodies_not_listed"] = bodies.Count - MaxBodies;
                data["bodies_total"] = bodies.Count;

                // ── the bounding box of the whole part ────────────────────
                double[] everything = null;
                foreach (var body in bodies)
                {
                    var b = BoundingBox(body);
                    if (b == null) continue;
                    if (everything == null) { everything = (double[])b.Clone(); continue; }
                    for (int k = 0; k < 3; k++) everything[k] = Math.Min(everything[k], b[k]);
                    for (int k = 3; k < 6; k++) everything[k] = Math.Max(everything[k], b[k]);
                }
                if (everything != null)
                {
                    partBox = everything;
                    data["bounding_box_mm"] = new[]
                    {
                        Math.Round(everything[3] - everything[0], 2),
                        Math.Round(everything[4] - everything[1], 2),
                        Math.Round(everything[5] - everything[2], 2),
                    };

                    // ── WHERE THE BOX IS, and not only how big it is ──────
                    //
                    // `bounding_box_mm` says «140 x 16 x 140» and does not say where that
                    // volume is: a plan that wants to put a hole «20 mm
                    // from the edge» must guess whether the part is centred
                    // on the origin or resting on it. They are three numbers, some
                    // thirty characters on the 16,000 ceiling.
                    //
                    // It is also the only input that makes possible the
                    // EXPLANATION of entry E10 — saying where a face was
                    // with respect to the part — which without the corner cannot
                    // even be written: 50 on a side 100 long means half
                    // if the box starts from zero and all of it if it starts from -50.
                    data["bounding_box_from_mm"] = new[]
                    {
                        Math.Round(everything[0], 2),
                        Math.Round(everything[1], 2),
                        Math.Round(everything[2], 2),
                    };
                }

                lap.Next("bodies_each");
                // ── the mass, ONLY if there is a material ─────────────────
                //
                // Without a material SolidWorks uses a default density anyway
                // and returns a number. A plausible and false weight is the worst
                // defect there is here: whoever reads «12.4
                // kg» has no way of knowing that it is invented. So it is not
                // sent at all.
                if (material != null)
                {
                    double? kg = Mass(model, bodies);
                    if (kg.HasValue) data["mass_kg"] = Math.Round(kg.Value, 3);
                }

                // THE VOLUME: the only measure that moves when the part changes
                // INSIDE without changing outside.
                //
                // The state had the bounding box and the mass. The bounding box does not
                // notice an enlarged hole, a removed fillet, a
                // suppressed rib: the part occupies the same space. The
                // mass is there only if the material is assigned, and in the workshop
                // half of the parts do not have it until they go to the bill of materials.
                // The volume is always there and always changes — and it is the number with
                // which the model can understand whether an edit did
                // anything. The add-in already computed it for its own
                // checks and told nobody.
                //
                // A BODY THAT DOES NOT ANSWER MAKES «I DO NOT KNOW», NOT A LOWER
                // VOLUME (entry Z-C8). Before it was skipped, and the key vanished even
                // when the volume had not been read: absent meant zero
                // OR «not read», and `undo` could redo a step after a
                // correct undo. Now: key absent = no solid
                // volume; key with null = it could not be read.
                double volume = 0;
                bool unreadableVolume = false;
                foreach (var c in bodies)
                {
                    try
                    {
                        var mp = c.GetMassProperties(1000.0) as double[];
                        if (mp == null || mp.Length <= 4) { unreadableVolume = true; break; }
                        if (mp[3] > 0) volume += mp[3] * 1e9;
                    }
                    catch { unreadableVolume = true; break; }
                }
                if (unreadableVolume) data["volume_mm3"] = null;
                else if (volume > 0) data["volume_mm3"] = Math.Round(volume, 1);

                lap.Next("mass");
                // ── faces and edges, with the ceiling on the TOTAL ────────
                //
                // The ceiling was INSIDE the loop over the bodies: on a four-bar
                // frame 240 faces came out instead of 60, and `truncated`
                // stayed false because no single body exceeded the ceiling.
                // The payload grew with the bars: ~11 KB for four, over
                // 50 KB for twenty. Now everything is collected, sorted once
                // and cut once.
                var allFaces = new List<Tuple<IFace2, int, double>>();
                var allEdges = new List<Tuple<IEdge, int>>();
                // From which feature EACH BODY is born. It serves to avoid repeating
                // on every face a thing that the field «body» already says: see
                // the comment on `from_feature` further below.
                var bodyFeature = new string[bodies.Count];
                for (int i = 0; i < bodies.Count; i++) bodyFeature[i] = BodyFeature(bodies[i]);
                for (int i = 0; i < bodies.Count; i++)
                {
                    var facesObj = bodies[i].GetFaces() as object[];
                    if (facesObj != null)
                        foreach (var o in facesObj)
                        {
                            var x = o as IFace2;
                            if (x != null) allFaces.Add(Tuple.Create(x, i, AreaOrZero(x)));
                        }
                    var edgesObj = bodies[i].GetEdges() as object[];
                    if (edgesObj != null)
                        foreach (var o in edgesObj)
                        {
                            var x = o as IEdge;
                            if (x != null) allEdges.Add(Tuple.Create(x, i));
                        }
                }

                // The largest first: they are the ones a designer
                // works on, and they are the ones a short description must have.
                // ZERO-E-ZERO: it is a sort key, not a statement —
                // a face that does not give its area ends up at the bottom, and that is all.
                var sorted = allFaces.OrderByDescending(t => t.Item3).ToList();
                docState._ranked = sorted;
                docState._bodyFeatureOfFace = bodyFeature;

                // WHICH FACES ARE LISTED. By default the largest MaxFaces; with a
                // filter, the ones that match it (FaceFilter), with the SAME
                // identifiers: the identifier of a face is its place in `sorted`,
                // whatever is listed.
                var listedRanks = new List<int>();
                if (filter == null)
                {
                    for (int r = 0; r < sorted.Count && r < MaxFaces; r++) listedRanks.Add(r);
                    if (sorted.Count > MaxFaces)
                    {
                        truncated = true;
                        data["faces_total"] = sorted.Count;
                    }
                }
                else
                {
                    var bodyNames = new List<string>();
                    foreach (IBody2 named in bodies)
                    {
                        string n = null;
                        try { n = named.Name; } catch { }
                        bodyNames.Add(n ?? "");
                    }
                    int wantedBody = filter.BodyIndex(bodyNames);
                    if (wantedBody == -1)
                        data["faces_filter_problem"] =
                            "«faces.body» names «" + filter.Body + "», and this part has " + bodies.Count +
                            " bod" + (bodies.Count == 1 ? "y" : "ies") + ": C1 to C" + bodies.Count +
                            (bodies.Count > 0 ? ", or one of the names in «bodies»" : "") + ".";
                    int matched = 0;
                    var normalsRead = new Dictionary<int, string>();
                    listedRanks = wantedBody == -1
                        ? new List<int>()
                        : filter.Choose(
                            sorted.Count,
                            r => sorted[r].Item2,
                            r => sorted[r].Item3 > 0 ? Units.Mm2(sorted[r].Item3) : (double?)null,
                            r =>
                            {
                                string facing;
                                if (!normalsRead.TryGetValue(r, out facing))
                                    normalsRead[r] = facing = Facing(FaceNormalVector(sorted[r].Item1));
                                return facing;
                            },
                            r => DistanceToFaceMm(sorted[r].Item1, filter.NearMm),
                            wantedBody, out matched);
                    data["faces_total"] = sorted.Count;
                    data["faces_matched"] = matched;
                    data["faces_filter"] = filter.Describe();
                    if (matched > listedRanks.Count)
                    {
                        truncated = true;
                        data["faces_not_listed"] = matched - listedRanks.Count;
                    }
                }
                // Face -> uid, to be able to say on which faces an
                // edge rests. It is filled here because the faces are numbered
                // before the edges, and it is needed there.
                var faceUid = new Dictionary<IFace2, string>();
                foreach (var rankIndex in listedRanks)
                {
                    var t = sorted[rankIndex];
                    var uid = "F" + (rankIndex + 1);
                    // The area that cannot be read is null in the state, not 0 mm²
                    // (entry Z-C8). ZERO-E-ZERO: in the signature it stays 0, because whoever
                    // reads `Measure` always looks at `> 0` and takes it for «not
                    // measured».
                    double? readArea = null;
                    try { readArea = Units.Mm2(t.Item1.GetArea()); } catch { }
                    double areaMm2 = readArea ?? 0;
                    var point = PointOnFace(t.Item1);
                    var normal = FaceNormalVector(t.Item1);
                    var entry = new Dictionary<string, object>
                    {
                        ["uid"] = uid,
                        ["area_mm2"] = readArea.HasValue ? (object)Math.Round(readArea.Value, 2) : null,
                        ["point_mm"] = point,
                        ["normal"] = normal,
                        ["planar"] = normal != null,
                    };
                    // A HOLE MUST BE SEEN AS A HOLE, NOT AS «A NON-PLANAR
                    // FACE OF AREA 1256».
                    //
                    // Up to 10 September 2026 of a cylindrical face the
                    // state said only `planar: false`. At the request
                    // «take that hole from M8 to M10» the model had no
                    // way of knowing which face was an M8: the diameter
                    // was in the geometry and we did not tell it.
                    //
                    // The radius is read from `CylinderParams`, which returns a
                    // vector of seven numbers: point (0-2), axis (3-5), radius
                    // in metres (6). It is the same recipe already in production
                    // to recognise the holes in `HoleOperations`.
                    double radiusMm;
                    double[] cylinderAxis;
                    // `= null` is not pedantry: `CylinderOf` assigns it with
                    // an `out`, but the call is to the right of a `&&` and on a
                    // planar face it is not executed. Without the initialisation
                    // the compiler refuses the use further down — and it is a
                    // right refusal: that variable there would not have a
                    // value.
                    double[] cylinderCenter = null;
                    if (normal == null &&
                        CylinderOf(t.Item1, out radiusMm, out cylinderAxis, out cylinderCenter))
                    {
                        entry["radius_mm"] = Math.Round(radiusMm, 3);
                        entry["diameter_mm"] = Math.Round(radiusMm * 2, 3);
                        entry["axis"] = cylinderAxis;
                    }
                    // FROM WHICH FEATURE IT COMES — BUT ONLY IF IT ADDS SOMETHING.
                    //
                    // The face→feature link is what makes face F17 stop being
                    // anonymous and become «the wall of the
                    // M8 hole».
                    //
                    // WRITTEN ON EVERY FACE HOWEVER IT COSTS AND LIES. Measured on
                    // 10 September 2026: on a four-bar frame the state
                    // went from 11,703 to 15,849 characters, and all the faces of
                    // a bar said «Structural Member1[1]» — that is
                    // exactly what the field «body» already said.
                    // Full cost, zero information.
                    //
                    // The rule is therefore: it is written when the face comes from
                    // a feature DIFFERENT from the one that made the body.
                    // It is the case in which the field says something new — a
                    // hole, a pocket, a fillet on a part — and it is
                    // exactly the case that is needed for «take that hole to
                    // M10».
                    string fromFeature = FaceFeature(t.Item1);
                    if (fromFeature != null
                        && (t.Item2 >= bodyFeature.Length
                            || !string.Equals(fromFeature, bodyFeature[t.Item2], StringComparison.Ordinal)))
                    {
                        entry["from_feature"] = fromFeature;

                        // ── WHERE THE HOLE IS, and it is not `point_mm` ────
                        //
                        // `point_mm` is on the SURFACE — it serves to
                        // SELECT — and is one radius away from the axis in a
                        // direction that nobody declares. Whoever uses it to
                        // measure a centre distance is wrong by one radius for every
                        // hole and does not notice: the number has the right
                        // shape.
                        //
                        // Measured on 15/9/2026 (entry E16) on a flange with
                        // six holes on Ø90: two independent models
                        // answered **Ø79**, both with confidence 5 out of 5.
                        //
                        // `center_mm` and not `axis_mm`: `axis` above is already
                        // the DIRECTION, and two almost homonymous fields that mean
                        // different things are the surest way to get them
                        // swapped.
                        //
                        // ── AND IT IS WRITTEN ONLY IN HERE, not on every
                        //    cylindrical face. THE CEILING SAID SO.
                        //
                        // The first write put it on ALL of them, and the bench
                        // stopped the commit: the state of a four-bar
                        // frame went from 15,633 to **16,163**
                        // characters, beyond the ceiling of 16,000, with **one single
                        // document open** — so real growth, not
                        // leftovers lying around.
                        //
                        // The cylindrical faces of a tubular frame are the
                        // corner fillets of the profiles: dozens, and among their
                        // axes nobody measures a centre distance. The ones that
                        // count are the faces that come from a feature
                        // DIFFERENT from the one that made the body — that is, a
                        // hole, a pocket, a countersink: the things that are
                        // positioned. It is the same rule that decides
                        // `from_feature`, applied where needed, and it is the
                        // reason why this line is inside that block
                        // instead of next to the radius.
                        //
                        // **What this does NOT cover, and it must be said:** a cylindrical
                        // face that IS the body — the inside of a tube —
                        // does not get the centre. If one day it is needed to
                        // measure something on it, it is a new entry, not a line
                        // to add here on the sly.
                        if (cylinderCenter != null) entry["center_mm"] = cylinderCenter;
                    }
                    // WHICH WAY IT FACES, SAID IN WORDS.
                    //
                    // The normal was already there, but it is a triple of numbers: to
                    // know which face is «the top one» one has to understand
                    // that in SolidWorks up is +Y, look for the normal
                    // [0,1,0] and then choose among those the largest. On 9
                    // September 2026 the model did not get there: at the
                    // request «on the top face of the plate make me two
                    // M8 holes» it computed the coordinates well, chose the
                    // operation well, and stopped writing «I need
                    // the identifier of the top face».
                    //
                    // Sixteen characters per face remove a deduction from
                    // the way. The direction is that of the SolidWorks system:
                    // +X right, +Y up, +Z front.
                    string facing = Facing(normal);
                    if (facing != null) entry["facing"] = facing;
                    // Which body it belongs to. On a weldment without this
                    // field forty indistinguishable faces arrive.
                    if (bodies.Count > 1 && t.Item2 < MaxBodies) entry["body"] = "C" + (t.Item2 + 1);

                    // FROM WHICH FEATURE IT COMES. It serves two different things: whoever
                    // reads the state, to understand what that face is; and finding it
                    // again, which without it cannot tell «the same
                    // face moved» from «another face that ended up in the same
                    // place».
                    // THE NAME IS WRITTEN ONLY ONCE, AT THE END.
                    //
                    // Writing it on every face costs: on a welded frame the
                    // names of the features are things like
                    // «TUBO STRUTTURALE QUADRO 80X80X5(1)», and sixty faces that
                    // come from four features repeated them sixty times.
                    // Measured: the state of a four-bar frame went
                    // from under 16 KB to **18,629 characters**, and there is
                    // a bench check that watches that ceiling.
                    //
                    // With the map at the end the long names are four, and on
                    // every face the number stays.
                    int featureId;
                    string featureItem;
                    FeatureOfFace(t.Item1, out featureId, out featureItem);
                    if (featureItem != null)
                    {
                        entry["feature_id"] = featureId;
                        featureNames[featureId] = featureItem;
                    }

                    // THE TWO ANCHORS, both behind the same switch and
                    // both meant for the SIGNATURE and not for whoever reads.
                    //
                    //   `anchor`     the area centroid: a conjecture of ours
                    //                about the geometry, which moves when the
                    //                face really changes;
                    //   `persist_id` the answer that SOLIDWORKS gives to the same
                    //                question, and the only one that is not a conjecture
                    //                of ours.
                    //
                    // The persist ID is 375-423 bytes per face, that is ~530
                    // characters in base64: on sixty faces it is ~32 KB, the
                    // DOUBLE of the whole ceiling of the state. It cannot reach
                    // whoever reads, and indeed the bridge removes it right after having
                    // built the signatures — the same path as the anchor,
                    // multiplied by twenty.
                    if (withAnchor)
                    {
                        var anchor = FaceAnchor(t.Item1);
                        if (anchor != null) entry["anchor"] = anchor;

                        var persistId = PersistIdOf(model, t.Item1);
                        if (persistId != null) entry["persist_id"] = persistId;
                    }

                    faces.Add(entry);
                    faceUid[t.Item1] = uid;

                    docState.Signatures[uid] = new EntitySignature
                    {
                        Type = "FACE",
                        Measure = Math.Round(areaMm2, 2),
                        PointMm = point,
                        Normal = normal,
                        Entity = t.Item1,
                        FeatureId = featureId,
                        Feature = featureItem,
                    };
                }

                lap.Next("faces");
                var sortedItems = allEdges
                    .OrderByDescending(t => LengthMm(t.Item1))
                    .ToList();
                docState._rankedEdges = sortedItems;

                // WHICH EDGES ARE LISTED (brief R17). By default the longest
                // MaxEdges; with a filter, the ones that match it, with the SAME
                // identifiers: the identifier of an edge is its place in
                // `sortedItems`, whatever is listed.
                var listedEdges = new List<int>();
                if (edgeFilter == null)
                {
                    for (int r = 0; r < sortedItems.Count && r < MaxEdges; r++) listedEdges.Add(r);
                    if (sortedItems.Count > MaxEdges)
                    {
                        truncated = true;
                        data["edges_total"] = sortedItems.Count;
                    }
                }
                else
                {
                    var edgeBodyNames = new List<string>();
                    foreach (IBody2 named in bodies)
                    {
                        string n = null;
                        try { n = named.Name; } catch { }
                        edgeBodyNames.Add(n ?? "");
                    }
                    int wantedEdgeBody = edgeFilter.BodyIndex(edgeBodyNames);
                    if (wantedEdgeBody == -1)
                        data["edges_filter_problem"] =
                            "«edges.body» names «" + edgeFilter.Body + "», and this part has " + bodies.Count +
                            " bod" + (bodies.Count == 1 ? "y" : "ies") + ": C1 to C" + bodies.Count +
                            (bodies.Count > 0 ? ", or one of the names in «bodies»" : "") + ".";
                    IFace2 boundedFace = null;
                    if (edgeFilter.Face != null)
                    {
                        int faceRank = FaceFilter.RankOf(edgeFilter.Face);
                        if (faceRank > sorted.Count)
                            data["edges_filter_problem"] =
                                "«edges.face» names " + edgeFilter.Face + ", and this part has " + sorted.Count +
                                " faces: read the part again and use one of them.";
                        else boundedFace = sorted[faceRank - 1].Item1;
                    }
                    int matchedEdges = 0;
                    var curveRead = new Dictionary<int, string>();
                    var straightRead = new Dictionary<int, bool>();
                    Action<int> readCurve = r =>
                    {
                        if (straightRead.ContainsKey(r)) return;
                        string along;
                        double radius;
                        straightRead[r] = EdgeDirection(sortedItems[r].Item1, out along, out radius);
                        curveRead[r] = along;
                    };
                    listedEdges = data.ContainsKey("edges_filter_problem")
                        ? new List<int>()
                        : edgeFilter.Choose(
                            sortedItems.Count,
                            r => sortedItems[r].Item2,
                            r => { double len = LengthMm(sortedItems[r].Item1); return len >= 0 ? len : (double?)null; },
                            r => { readCurve(r); return !straightRead[r]; },
                            r => { readCurve(r); return curveRead[r]; },
                            r => EdgeBoundsFace(sortedItems[r].Item1, boundedFace),
                            r => DistanceToEdgeMm(sortedItems[r].Item1, edgeFilter.NearMm),
                            wantedEdgeBody, out matchedEdges);
                    data["edges_total"] = sortedItems.Count;
                    data["edges_matched"] = matchedEdges;
                    data["edges_filter"] = edgeFilter.Describe();
                    if (matchedEdges > listedEdges.Count)
                    {
                        truncated = true;
                        data["edges_not_listed"] = matchedEdges - listedEdges.Count;
                    }
                }
                foreach (var edgeRankIndex in listedEdges)
                {
                    var t = sortedItems[edgeRankIndex];
                    var uid = "E" + (edgeRankIndex + 1);
                    double len = LengthMm(t.Item1);
                    var point = PointOnEdge(t.Item1);
                    var entry = new Dictionary<string, object>
                    {
                        ["uid"] = uid,
                        // null when it could not be read: -1 is the «I do not
                        // know» of whoever uses it in here, not a length.
                        ["length_mm"] = len >= 0 ? (object)Math.Round(len, 2) : null,
                        ["point_mm"] = point,
                    };
                    // AN EDGE WITHOUT A ROLE CANNOT BE NAMED.
                    //
                    // The faces say which way they look — «up»,
                    // «front» — and the model knows how to choose them. The edges
                    // said only how long they are. Measured on 10
                    // September 2026 on a 300x200x15 plate: twenty
                    // edges, all the same to read, and at the request
                    // «fillet the four corners» the model did not even
                    // try to name them — it cited a nonexistent step.
                    //
                    // «along: Y» costs fourteen characters and says what
                    // a designer would say: the corners of a plate
                    // are the edges in the direction of the thickness.
                    string axisLength;
                    double edgeRadius;
                    if (EdgeDirection(t.Item1, out axisLength, out edgeRadius))
                    {
                        if (axisLength != null) entry["along"] = axisLength;
                    }
                    else
                    {
                        // Not straight: it is said, because as a DIRECTION it is
                        // no good and the repetition would refuse it.
                        entry["curved"] = true;
                        if (edgeRadius > 0) entry["radius_mm"] = Math.Round(edgeRadius, 2);
                    }
                    // WHICH FACES IT RESTS ON: this is how one says «the
                    // edge of the top face» without counting millimetres.
                    var fra = EdgeFaces(t.Item1, faceUid);
                    if (fra != null) entry["between"] = fra;
                    if (bodies.Count > 1 && t.Item2 < MaxBodies) entry["body"] = "C" + (t.Item2 + 1);
                    edges.Add(entry);
                    docState.Signatures[uid] = new EntitySignature
                    {
                        Type = "EDGE",
                        Measure = Math.Round(len, 2),
                        PointMm = point,
                        Entity = t.Item1,
                    };
                }
            }

            lap.Next("edges");
            data["solid_body"] = hasBody;
            data["planes_available"] = nPlanes;

            // IS THE TREE RED? IT IS SAID AT THE TOP, NOT ONLY INSIDE.
            //
            // The features in error are already marked one by one, but whoever
            // reads the state scans the list of the features only if there is a reason
            // to. A number at the top is the reason.
            //
            // **`NeedsRebuild2` is NOT enough, and it is measured**: on the part with the
            // impossible fillet it answered **0** — no rebuild
            // needed — while `GetWhatsWrongCount` said 1 and the fillet
            // had code 12. The two questions are different: «needs rebuilding» and
            // «is broken» are not the same thing, and trusting the first to
            // answer the second would have given an always-green guard.
            if (featuresInError > 0)
                data["features_in_error"] = featuresInError;
            try
            {
                if (model.Extension.NeedsRebuild2 != 0)
                    data["rebuild_needed"] = true;
            }
            catch { }

            data["feature"] = feature;
            if (otherFeatures > 0) data["other_feature"] = otherFeatures;
            // The ceiling on the features, declared like those on faces,
            // edges and bodies. `truncated` holds for the whole state.
            if (unlistedFeatures > 0)
            {
                data["feature_not_listed"] = unlistedFeatures;
                data["feature_total"] = feature.Count + unlistedFeatures;
                truncated = true;
            }
            // The types are ALWAYS sent when there are any: it is what
            // allows noticing that a feature is invisible.
            if (unknownTypes.Count > 0) data["other_feature_kinds"] = unknownTypes.ToArray();
            data["axes"] = axes;
            if (describedBodies.Count > 0) data["bodies"] = describedBodies;
            data["faces"] = faces;
            data["edges"] = edges;
            // WHAT IS SELECTED NOW, said with the same names.
            //
            // It is the other half of the signature: instead of describing in words which
            // face is meant, the designer POINTS at it with a finger and the state
            // says what it is called. The ambiguity is removed before
            // the operation, not discovered afterwards.
            //
            // It is not a new tool, and this is intended: the bridge has four
            // and the reason is written in `McpTools`. This is one more line
            // in an answer that is already read, and it is written **only when something
            // is selected** — on a document without a selection it
            // costs nothing.
            // The dictionary of the names, only if somebody uses it.
            if (featureNames.Count > 0)
            {
                var map = new Dictionary<string, object>();
                foreach (var v in featureNames)
                    map[v.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] = v.Value;
                data["features"] = map;
            }

            var selected = TheSelection(model, docState.Signatures);
            if (selected != null && selected.Count > 0) data["selection"] = selected;

            data["truncated"] = truncated;
            data["protocol_units"] = "mm";

            // THE EQUATIONS AND THE GLOBAL VARIABLES (entry Z-F1), if there are any: whoever
            // reads the state must know that a dimension is driven before trying
            // to change it by hand.
            lap.Next("errors_selection");
            int totalEquations;
            var equations = SwOperations.DocumentEquations(model, out totalEquations);
            if (equations != null)
            {
                data["equations"] = equations;
                if (totalEquations > equations.Length)
                {
                    data["equations_total"] = totalEquations;
                    data["truncated"] = true;
                }
            }

            // ── THE SHAPE IN ONE SENTENCE (entry V3) ─────────────────────
            //
            // It goes LAST because it reads `data`: the bounding box, the volume, the
            // features and the faces must all be there already. Putting it first
            // would mean describing half a part without anybody
            // noticing — the sentence would come out all the same, only shorter.
            //
            // It costs **less than three hundred characters** on a ceiling of 16,000, and
            // says things that today everybody derives on their own: how
            // full the bounding box is, the wall thickness, the centres of the holes and the
            // diameter of the circle on which they stand. The latter is the calculation that
            // two models got wrong with full confidence (entry E16).
            //
            // Null when there is not enough to say one: the key does not
            // appear, instead of appearing empty. An empty description reads
            // as «there is nothing to say about this part», which is a
            // different thing from «I could not say it».
            //
            // NOT ON A READING WITH A FILTER (W3.1-bis.12): the sentence is told
            // from the faces the state lists, and a filtered reading lists some
            // faces of the part, not the largest ones — «one hole of Ø3» of a plate
            // with sixty-five would be a true sentence about what was listed and a
            // false one about the part. The key is absent, which reads «not said».
            if (filter == null)
            {
                try
                {
                    string shape = ShapeToWords.Describe(data);
                    if (!string.IsNullOrEmpty(shape)) data["shape"] = shape;
                }
                catch { }
            }

            // ── THE BOX OF THE PART IN EVERY SIGNATURE ───────────────────
            //
            // A single pass, at the end, because the box is known after having
            // read the bodies while the signatures of the faces are filled in afterwards. It is
            // the SAME object in all of them: it is a property of the part, not
            // of the entity, and copying it would serve no purpose.
            docState.BoxPartMm = partBox;
            if (partBox != null)
                foreach (var signed in docState.Signatures.Values)
                    if (signed != null) signed.BoxPartMm = partBox;

            lap.Next("equations_shape");
            docState.Data = data;
            return docState;
        }

        /// <summary>
        /// The custom properties of a drawer: `""` for the document,
        /// the name of a configuration for that configuration.
        ///
        /// THEY ARE READ RESOLVED. A property can be a formula
        /// («"SW-Mass@part.SLDPRT"») and then it has two values: the one written and
        /// the one that comes out of it. Whoever looks at the card sees the second, and this
        /// is a description of what is seen.
        /// </summary>
        private static Dictionary<string, object> ReadProperty(
            IModelDoc2 model, string configuration)
        {
            var outside = new Dictionary<string, object>(StringComparer.Ordinal);
            try
            {
                var propertyManager = model.Extension.get_CustomPropertyManager(configuration)
                               as ICustomPropertyManager;
                if (propertyManager == null) return outside;
                var names = propertyManager.GetNames() as string[];
                if (names == null) return outside;
                foreach (var itemName in names)
                {
                    if (string.IsNullOrEmpty(itemName)) continue;
                    try
                    {
                        string val, resolved;
                        bool wasResolved, connected;
                        // THE RESULT CODE IS READ (Z-C21.8, finding L17-8).
                        // ICustomPropertyManager~Get6.html: «Result code as
                        // defined in swCustomInfoGetResult_e». The names come
                        // from GetNames, so NotPresent should not happen (the
                        // audit said so, L9-7); if it does, the name is left out
                        // instead of being written as an empty property.
                        int outcome = propertyManager.Get6(itemName, false, out val, out resolved,
                                                  out wasResolved, out connected);
                        if (outcome == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent) continue;
                        outside[itemName] = resolved ?? val ?? "";
                    }
                    catch { }
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// The box of a body, in mm: {minX, minY, minZ, maxX, maxY, maxZ}.
        ///
        /// GetExtremePoint six times, not GetBodyBox. GetBodyBox is
        /// declaredly APPROXIMATE for non-planar surfaces — it can
        /// be larger than the part — and a state that declares a bounding box
        /// larger than the real one makes everything around it be
        /// sized wrong. GetExtremePoint returns the true point of the body
        /// farthest in a direction.
        /// </summary>
        private static double[] BoundingBox(IBody2 body)
        {
            try
            {
                var box = ApiHelpers.TrueBoundingBox(body);
                if (box == null) return ApproximateBox(body);
                var outside = new double[6];
                for (int i = 0; i < 6; i++) outside[i] = Units.Mm(box[i]);
                return outside;
            }
            catch { return ApproximateBox(body); }
        }

        private static double[] ApproximateBox(IBody2 body)
        {
            try
            {
                // Only the fallback when GetExtremePoint does not answer (Overall).
                var b = ApiHelpers.ApproximateBoundingBox(body);
                if (b == null || b.Length < 6) return null;
                var outside = new double[6];
                for (int i = 0; i < 6; i++) outside[i] = b[i] * 1000.0;
                return outside;
            }
            catch { return null; }
        }

        /// <summary>
        /// Which way a planar face looks, in the SolidWorks system:
        /// +X right, +Y up, +Z front. null if the face is not planar or is
        /// tilted with respect to the axes — and in that case a word is not
        /// invented: a face «almost up» described as «up» would make
        /// the wrong one be chosen.
        /// </summary>
        private static string Facing(double[] normal)
        {
            if (normal == null || normal.Length < 3) return null;
            const double almostOne = 0.99;
            if (normal[0] > almostOne) return "right";
            if (normal[0] < -almostOne) return "left";
            if (normal[1] > almostOne) return "up";
            if (normal[1] < -almostOne) return "down";
            if (normal[2] > almostOne) return "front";
            if (normal[2] < -almostOne) return "back";
            return null;
        }

        /// <summary>The name of the feature that generated the body, or null.</summary>
        /// <summary>
        /// THE PARAMETERS OF A FEATURE: what makes it «an M8» instead of
        /// «a feature named Foro1».
        ///
        /// WHY IT EXISTS. Up to 10 September 2026 the state sent for every
        /// feature three fields: uid, name, type. A through M12 and a blind M6
        /// reached the model as two identical rows except for the name that
        /// the user had written in the tree. At the request «take that hole
        /// from M8 to M10» the model could not know WHICH hole was M8: the
        /// datum was in the part and we did not tell it.
        ///
        /// FEW FIELDS, NOT ALL. `IWizardHoleFeatureData2` exposes
        /// fifty-eight properties. In the line that a person reads before
        /// confirming four fit. The cost here is not SolidWorks time:
        /// it is the room that each field takes from another field in the
        /// message. So a choice is made, and the choice is «what is needed to
        /// recognise this feature among the others».
        ///
        /// ONLY WHAT IS THERE IS WRITTEN. A field at zero is not a datum: it is
        /// a hole that takes up room and that the model might read as a
        /// true measure.
        ///
        /// Signatures verified by reflection on the interop 29.5.1.1:
        ///   IFeature.GetDefinition() -> Object
        ///   IWizardHoleFeatureData2: Standard, FastenerType, FastenerSize
        ///       (String); Type, EndCondition (Int32); HoleDepth, Depth,
        ///       ThreadDiameter, and the four diameters of the recipe below
        ///   IExtrudeFeatureData2.GetDepth(Boolean), .GetEndCondition(Boolean),
        ///       .BothDirections, .Merge
        ///   ISimpleFilletFeatureData2: DefaultRadius, IsMultipleRadius,
        ///       GetEdgeCount()
        ///   IChamferFeatureData2: Type, GetEdgeChamferDistance(Int32),
        ///       EdgeChamferAngle, GetVertexChamferDistance(Int32), GetEdgeCount()
        ///   IFeature.GetFaceCount()
        /// </summary>
        private static void FeatureParameters(
            IFeature f, string typ, Dictionary<string, object> entry)
        {
            object definition;
            try { definition = f.GetDefinition(); }
            catch { return; }
            if (definition == null) return;

            try
            {
                if (typ == "HoleWzd")
                {
                    var d = definition as IWizardHoleFeatureData2;
                    if (d == null) return;
                    // THE STANDARD AND THE SIZE ARE STRINGS, and they are what a
                    // designer says out loud: «a through M8 ISO».
                    Put(entry, "standard", Text(() => d.Standard));
                    Put(entry, "hole_type", Text(() => d.FastenerType));
                    Put(entry, "size", Text(() => d.FastenerSize));
                    // THE WORKED DIAMETER, with the four-family recipe
                    // already in production in `HoleOperations`: which of the four
                    // fields is filled depends on the type of hole, and
                    // reading only one would give zero without saying so.
                    double diam = Math.Max(
                        Math.Max(d.ThruTapDrillDiameter, d.TapDrillDiameter),
                        Math.Max(d.ThruHoleDiameter, d.HoleDiameter)) * 1000.0;
                    if (diam > 0) entry["diameter_mm"] = Math.Round(diam, 2);
                    if (d.ThreadDiameter > 0)
                        entry["thread_mm"] = Math.Round(d.ThreadDiameter * 1000.0, 2);
                    double prof = Math.Max(d.HoleDepth, d.Depth) * 1000.0;
                    if (prof > 0) entry["depth_mm"] = Math.Round(prof, 2);
                }
                else if (typ == "Extrusion" || typ == "Cut")
                {
                    var d = definition as IExtrudeFeatureData2;
                    if (d == null) return;
                    double prof = d.GetDepth(true) * 1000.0;
                    if (prof > 0) entry["depth_mm"] = Math.Round(prof, 2);
                    if (d.BothDirections)
                    {
                        entry["two_directions"] = true;
                        double prof2 = d.GetDepth(false) * 1000.0;
                        if (prof2 > 0) entry["depth2_mm"] = Math.Round(prof2, 2);
                    }
                    // «Through all» and «12 deep» are two different parts, and
                    // the depth alone does not tell them apart: on a through hole
                    // a number stays written that commands nothing.
                    entry["end_condition"] = EndCondition(d.GetEndCondition(true));
                }
                else if (typ == "Fillet")
                {
                    var d = definition as ISimpleFilletFeatureData2;
                    if (d == null) return;
                    if (d.DefaultRadius > 0)
                        entry["radius_mm"] = Math.Round(d.DefaultRadius * 1000.0, 2);
                    if (d.IsMultipleRadius) entry["radius_variable"] = true;
                    // NOT FilletItemsCount (C7-2): «This method returns -1 if
                    // this simple fillet feature is not multiple radius»
                    // (ISimpleFilletFeatureData2~FilletItemsCount.html,
                    // Remarks), and the bridge only makes uniform fillets, so
                    // the edge count it guarded never came out (-1 on every
                    // fillet of the Z-C21.3 edge-count probe, 28/9/2026).
                    EdgeAndFaceCounts(f, () => d.GetEdgeCount(), entry);
                }
                else if (typ == "Chamfer")
                {
                    // IChamferFeatureData2, not the obsolete IChamferFeatureData:
                    // the type that decides how to read the sizes is
                    // IChamferFeatureData2::Type (IChamferFeatureData~Type.html:
                    // «Obsolete. Superseded by IChamferFeatureData2::Type»).
                    var d = definition as IChamferFeatureData2;
                    if (d == null) return;
                    ChamferSizes(d.Type, side => d.GetEdgeChamferDistance(side),
                                 () => d.EdgeChamferAngle,
                                 side => d.GetVertexChamferDistance(side), entry);
                    EdgeAndFaceCounts(f, () => d.GetEdgeCount(), entry);
                }
            }
            catch { }
        }

        /// <summary>
        /// HOW MANY EDGES WERE SELECTED, AND HOW MANY FACES CAME OUT (C7-2,
        /// N14, Z-C21.3).
        ///
        /// GetEdgeCount on the definition counts the SELECTED edges, not the
        /// worked ones. Measured on 28/9/2026 (Z-C21.3, edge-count probe): a block
        /// 40 x 30 x 20 high with its four vertical edges rounded r 5, then a
        /// fillet r 2 with the bridge's options on ONE top edge. The fillet
        /// propagates along the tangent loop, removes 110.399 mm3 (110.399 by
        /// hand) and owns 8 faces; GetEdgeCount says 1, with or without
        /// AccessSelections. The same for a chamfer 2 x 45 on one edge of the
        /// same loop: 254.454 mm3, 8 faces, GetEdgeCount 1. With nothing to
        /// propagate to, one edge and one face (F1, C1); four vertical edges,
        /// 4 and 4 (F4).
        ///
        /// So the count goes out under the name of what it is,
        /// «selected_edges», and next to it the feature's own faces, which is
        /// what tells a reader that one selected edge became a whole loop:
        ///   ISimpleFilletFeatureData2~GetEdgeCount.html: «Gets the number of
        ///     edges on which to create a simple radius fillet»;
        ///   IChamferFeatureData2~GetEdgeCount.html: «Gets the number of edges
        ///     that are chamfered» — both measured as the selection;
        ///   IFeature~GetFaceCount.html: «Gets the number of faces in this
        ///     feature».
        /// Only what is there is written: a vertex chamfer has no edges, and a
        /// suppressed feature has no faces.
        /// </summary>
        private static void EdgeAndFaceCounts(
            IFeature f, Func<int> selectedEdges, Dictionary<string, object> entry)
        {
            try
            {
                int n = selectedEdges();
                if (n > 0) entry["selected_edges"] = n;
            }
            catch { }
            try
            {
                int n = f.GetFaceCount();
                if (n > 0) entry["faces"] = n;
            }
            catch { }
        }

        /// <summary>
        /// THE SIZES OF A CHAMFER, READ BY ITS TYPE (N29, Z-C21.3).
        ///
        /// Up to library 0.141.0 every chamfer was read as an edge chamfer:
        /// side 0, side 1 when different, the angle when not zero. A VERTEX
        /// chamfer has none of them — its three sizes are distances along the
        /// three edges that meet at the corner — so it went into the state
        /// with no size at all. Measured on 28/9/2026 (Z-C21.3, chamfer probe,
        /// V1 and V2, InsertFeatureChamfer with swChamferVertex on a corner of
        /// a block): Type 3, edge distances 0 and 0, angle 0, vertex distances
        /// 2/3/4 and 3/3/3, the ones asked; the triangle cut from the corner
        /// measured 2/3/4 and 3/3/3, and the removed volume was the
        /// tetrahedron's, 4 and 4.5 mm3. The same probe read the edge chamfers
        /// back by type as they were made: angle-distance 5 at 30 degrees as
        /// Type 1 with side 0 = 5 and angle 30; distance-distance 3 x 6 as
        /// Type 2 with sides 3 and 6 and angle 0.
        ///
        /// Two distances, not one, on a distance-distance chamfer: a 3 x 6 and
        /// a 3 x 3 remove different material, and with one side they would
        /// read the same.
        ///
        /// IChamferFeatureData2~Type.html, Remarks: «An angle-distance chamfer
        /// requires an angle and a distance; a distance-distance chamfer
        /// requires two distances, one for each side of the chamfered edge»;
        /// «A vertex chamfer requires three distances along three adjacent
        /// edges» (their order is the edge order of IVertex::EnumEdgesOriented,
        /// not X, Y, Z). ~EdgeChamferAngle.html: «relevant only for an
        /// angle-distance type of chamfer»; ~GetVertexChamferDistance.html:
        /// side 0, 1 or 2; ~GetEdgeChamferDistance.html: side 0 or 1, and 0
        /// only for angle-distance. swChamferType_e: swChamferAngleDistance 1,
        /// swChamferDistanceDistance 2, swChamferVertex 3,
        /// swChamferEqualDistance 16. A type none of these three branches
        /// knows (equal distance, or a combination) keeps the reading of
        /// before: side 0, side 1 when different, the angle when not zero.
        ///
        /// No interop in the signature: the reads come in as functions, so the
        /// rule runs without SOLIDWORKS (Prove, «the state reads a chamfer by
        /// its type»), and it can be checked there that a vertex chamfer never
        /// asks for an edge distance.
        /// </summary>
        internal static void ChamferSizes(
            int type, Func<int, double> edgeDistance, Func<double> angle,
            Func<int, double> vertexDistance, Dictionary<string, object> entry)
        {
            const int angleDistance = 1, distanceDistance = 2, vertex = 3;
            if (type == vertex)
            {
                var sides = new double[3];
                for (int i = 0; i < 3; i++) sides[i] = Math.Round(vertexDistance(i) * 1000.0, 2);
                if (sides.Any(s => s > 0)) entry["vertex_distances_mm"] = sides;
                return;
            }
            double first = edgeDistance(0) * 1000.0;
            if (first > 0) entry["leg1_mm"] = Math.Round(first, 2);
            if (type != angleDistance)
            {
                double second = edgeDistance(1) * 1000.0;
                if (second > 0 && Math.Abs(second - first) > 0.001) entry["leg2_mm"] = Math.Round(second, 2);
            }
            if (type != distanceDistance)
            {
                double radians = angle();
                if (radians > 0) entry["angle_deg"] = Math.Round(Units.Degrees(radians), 1);
            }
        }

        private static void Put(Dictionary<string, object> entry, string key, string val)
        {
            if (!string.IsNullOrWhiteSpace(val)) entry[key] = val.Trim();
        }

        private static string Text(Func<string> read)
        {
            try { return read(); }
            catch { return null; }
        }

        /// <summary>
        /// The end condition, in words. The numbers of `swEndConditions_e`
        /// mean nothing to whoever reads the plan, and they are exactly the
        /// thing that tells «through all» from «12 deep».
        /// </summary>
        private static string EndCondition(int code)
        {
            switch (code)
            {
                case (int)swEndConditions_e.swEndCondBlind: return "given depth";
                case (int)swEndConditions_e.swEndCondThroughAll: return "through_all";
                case (int)swEndConditions_e.swEndCondThroughAllBoth: return "through in both directions";
                case (int)swEndConditions_e.swEndCondMidPlane: return "symmetric on the plane";
                case (int)swEndConditions_e.swEndCondUpToVertex: return "up to a vertex";
                case (int)swEndConditions_e.swEndCondUpToSurface: return "up to a surface";
                case (int)swEndConditions_e.swEndCondOffsetFromSurface: return "offset from a surface";
                case (int)swEndConditions_e.swEndCondUpToBody: return "up to a body";
                case (int)swEndConditions_e.swEndCondUpToNext: return "up to the next";
                default: return "condition " + code;
            }
        }

        /// <summary>
        /// Whether the face is cylindrical: the radius in mm and the direction of the axis.
        ///
        /// `CylinderParams` returns seven numbers — point (0-2), axis (3-5),
        /// radius in METRES (6). The recipe is copied from `HoleOperations`, which
        /// already uses it in production to recognise the holes: it is reused instead of
        /// rewritten, because two copies diverge at the first edit.
        /// </summary>
        /// <summary>
        /// A cylinder: radius, direction of the axis, and A POINT ON THE AXIS.
        ///
        /// ── THE POINT ON THE AXIS ARRIVED LATE, AND COST DEAR ─────────────
        ///
        /// `CylinderParams` returns seven numbers: **point (0-2)**, axis (3-5),
        /// radius in metres (6). Up to 0.66.0 this method read
        /// `par[3..6]` and threw away `par[0..2]` — that is, the only thing that says
        /// WHERE the hole is.
        ///
        /// **What was left to whoever reads.** Of a cylindrical face the state
        /// said `point_mm`, `radius_mm`, `diameter_mm` and `axis`. But
        /// `point_mm` comes from `PointOnFace`, and it is a point **on the
        /// surface**, guaranteed one radius from the axis in any
        /// direction: it serves to SELECT, not to say where the hole is. And
        /// `axis` is a direction, not a position. The centre could not be
        /// derived: for a cylindrical face `normal` is null, so there is not even
        /// the direction in which to correct.
        ///
        /// **The measurement, of 15/9/2026 (entry V2).** Two models, each on its
        /// own and without seeing the answers of the other, read the state
        /// of a flange with six holes on a circle Ø90 and
        /// answered **Ø79**, both with confidence 5 out of 5. Ø79 is Ø90
        /// minus the diameter of one hole: they had measured between the near EDGES
        /// instead of between the axes, because it is the only thing the state gave.
        /// On the same plate the centre distance 160 came out 149.5.
        ///
        /// Neither of the two reasoned wrongly. They used a number
        /// correct for the thing it was not for: **the defect that gives no
        /// error**, produced by the state instead of by the geometry. A
        /// flange with the circle wrong by 11 mm cannot be bolted.
        ///
        /// ── THE EXTRA PARAMETER CAN BE ADDED HERE ────────────────────────
        ///
        /// The rule of the project is that changing the set of signatures of a
        /// method breaks whoever calls it by reflection even when the
        /// compiler is silent. It holds for what the bench invokes by name —
        /// `Read`, `ReadWithAnchor`, `SwOperations.Run`. This one is
        /// `private static` inside the file that uses it, with a single call
        /// site: outside of here nobody can invoke it.
        /// </summary>
        private static bool CylinderOf(
            IFace2 face, out double radiusMm, out double[] axis, out double[] center)
        {
            radiusMm = 0;
            axis = null;
            center = null;
            try
            {
                var surface = face.IGetSurface();
                if (surface == null || !surface.IsCylinder()) return false;
                var par = surface.CylinderParams as double[];
                if (par == null || par.Length < 7) return false;
                radiusMm = Units.Mm(par[6]);
                axis = new[]
                {
                    Math.Round(par[3], 4),
                    Math.Round(par[4], 4),
                    Math.Round(par[5], 4),
                };
                center = new[]
                {
                    Math.Round(Units.Mm(par[0]), 3),
                    Math.Round(Units.Mm(par[1]), 3),
                    Math.Round(Units.Mm(par[2]), 3),
                };
                return radiusMm > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Is an edge straight? And if so, along which axis of the model?
        ///
        /// Signatures verified by reflection on the interop 29.5.1.1:
        ///   IEdge.IGetCurve() -> Curve
        ///   IEdge.GetTwoAdjacentFaces2() -> Object
        ///   ICurve.IsLine() / IsCircle() -> Boolean
        ///   ICurve.LineParams -> Object    (point 0-2, direction 3-5)
        ///   ICurve.CircleParams -> Object  (centre 0-2, axis 3-5, radius 6)
        ///
        /// Returns false if the edge is NOT straight; in that case `radius` is
        /// the radius of the circle, when it is a circle.
        /// </summary>
        private static bool EdgeDirection(IEdge edge, out string along, out double radius)
        {
            along = null;
            radius = 0;
            try
            {
                var curva = edge.IGetCurve();
                if (curva == null) return false;
                if (!curva.IsLine())
                {
                    if (curva.IsCircle())
                    {
                        var c = curva.CircleParams as double[];
                        if (c != null && c.Length >= 7) radius = Units.Mm(c[6]);
                    }
                    return false;
                }
                var par = curva.LineParams as double[];
                if (par == null || par.Length < 6) return true;
                // «along an axis» is said only if it really fits: a
                // diagonal has no axis, and calling it «along X» would be
                // a convenient lie — of the kind that gives no error.
                double[] d = { Math.Abs(par[3]), Math.Abs(par[4]), Math.Abs(par[5]) };
                const double almostOne = 0.99;
                if (d[0] > almostOne) along = "X";
                else if (d[1] > almostOne) along = "Y";
                else if (d[2] > almostOne) along = "Z";
                return true;
            }
            catch { return true; }
        }

        /// <summary>
        /// The signature of an edge the state did not list (brief R17): the same
        /// fields the listed ones get. Null when its length cannot be read.
        /// </summary>
        private static EntitySignature EdgeSignature(IEdge edge)
        {
            double len = LengthMm(edge);
            if (len < 0) return null;
            return new EntitySignature
            {
                Type = "EDGE",
                Measure = Math.Round(len, 2),
                PointMm = PointOnEdge(edge),
                Entity = edge,
            };
        }

        /// <summary>
        /// Whether an edge bounds that face: one of its two adjacent faces is the
        /// same COM object (by IUnknown, not by reference: two reads of the same
        /// face may come back as two wrappers). False when unread.
        /// </summary>
        private static bool EdgeBoundsFace(IEdge edge, IFace2 face)
        {
            if (edge == null || face == null) return false;
            try
            {
                var two = edge.GetTwoAdjacentFaces2() as object[];
                if (two == null) return false;
                foreach (object o in two)
                    if (SameComObject(o, face)) return true;
                return false;
            }
            catch { return false; }
        }

        private static bool SameComObject(object a, object b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            IntPtr pa = IntPtr.Zero, pb = IntPtr.Zero;
            try
            {
                pa = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(a);
                pb = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(b);
                return pa == pb;
            }
            catch { return false; }
            finally
            {
                if (pa != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(pa);
                if (pb != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(pb);
            }
        }

        /// <summary>
        /// How far a point of the model (mm) is from an edge (mm), by the closest
        /// point of the EDGE. IEdge~GetClosestPointOn.html: «[ PointX, PointY,
        /// PointZ, UParameter, NotUsed ]», the point in metres. Null when unread.
        /// </summary>
        private static double? DistanceToEdgeMm(IEdge edge, double[] pointMm)
        {
            if (edge == null || pointMm == null) return null;
            try
            {
                var p = edge.GetClosestPointOn(Units.M(pointMm[0]), Units.M(pointMm[1]), Units.M(pointMm[2])) as double[];
                if (p == null || p.Length < 3) return null;
                double dx = Units.Mm(p[0]) - pointMm[0];
                double dy = Units.Mm(p[1]) - pointMm[1];
                double dz = Units.Mm(p[2]) - pointMm[2];
                return Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            catch { return null; }
        }

        /// <summary>The two faces that meet on this edge, by uid.</summary>
        private static string[] EdgeFaces(
            IEdge edge, Dictionary<IFace2, string> faceUid)
        {
            try
            {
                var due = edge.GetTwoAdjacentFaces2() as object[];
                if (due == null) return null;
                var names = new List<string>();
                foreach (var o in due)
                {
                    var f = o as IFace2;
                    string u;
                    if (f != null && faceUid.TryGetValue(f, out u)) names.Add(u);
                }
                // If one of the two is outside the ceiling on faces, better
                // nothing than a maimed pair that looks complete.
                return names.Count == 2 ? names.ToArray() : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// The dimensions of a feature: name and value in mm.
        ///
        /// Signatures verified by reflection on the interop 29.5.1.1:
        ///   IFeature.GetFirstDisplayDimension() -> Object
        ///   IFeature.GetNextDisplayDimension(Object DispIn) -> Object
        ///   IDisplayDimension.GetDimension2(Int32 Index) -> Dimension
        ///   IDimension.Name / .FullName -> String
        ///   IDimension.SystemValue -> Double   (in METRES)
        ///
        /// FOUR AT MOST. A complicated feature has many, and
        /// listing them all would fill the message with numbers among which the
        /// model would have to fish. The first four are the ones that
        /// count — depth, diameter, radius — and if there are more
        /// it is said how many, so the gap is not invisible.
        ///
        /// THE NAME IS WHAT IS NEEDED, NOT THE INDEX. An index changes if
        /// somebody touches the tree; «D1@Sketch1» does not, and it is what
        /// is written to change it.
        /// </summary>
        private static void FeatureDimensions(IFeature f, Dictionary<string, object> entry)
        {
            try
            {
                var quote = new List<Dictionary<string, object>>();
                int other = 0;
                object display = f.GetFirstDisplayDimension();
                while (display != null)
                {
                    var dd = display as IDisplayDimension;
                    var d = dd?.GetDimension2(0);
                    if (d != null)
                    {
                        if (quote.Count < 4)
                        {
                            string itemName = null;
                            try { itemName = d.FullName; } catch { }
                            if (string.IsNullOrEmpty(itemName)) { try { itemName = d.Name; } catch { } }
                            if (!string.IsNullOrEmpty(itemName))
                            {
                                quote.Add(Dimension(d, itemName));
                            }
                        }
                        else other++;
                    }
                    object next = null;
                    try { next = f.GetNextDisplayDimension(display); } catch { }
                    display = next;
                }
                if (quote.Count > 0) entry["dimensions"] = quote;
                if (other > 0) entry["other_dimensions"] = other;
            }
            catch { }
        }

        /// <summary>
        /// A dimension, in the unit that belongs to it.
        ///
        /// ── AN ANGLE IS NOT MEASURED IN MILLIMETRES ───────────────────────
        ///
        /// `SystemValue` returns everything in system units, and for an angle
        /// those are **radians**. Up to 0.69.0 this method
        /// multiplied by a thousand anything — the metres→mm conversion — and
        /// it came out as `mm`. On a revolution of 360 degrees:
        ///
        ///     "name": "D1@Revolve1", "mm": 6283.185
        ///
        /// which is **2π × 1000**. The state declared a dimension of six metres and
        /// twenty-eight on a part ninety millimetres long, and nobody
        /// noticed because the field is called `mm` and the number is a number.
        ///
        /// **Found on the way, measuring something else** (entry V3): a
        /// model that had been given only the description of the stepped shaft
        /// tree did not manage to redraw it, and it was the only one of the five.
        /// Looking at why, the only dimension that part offered was this one.
        ///
        /// ── THE NAME OF THE FIELD CHANGES WITH THE UNIT ──────────────────
        ///
        /// An angle comes out as `"deg": 360`, not as `"mm": 360`. Keeping the
        /// name `mm` and changing only the number would have been worse than before:
        /// a right value under a false label cannot even be
        /// suspected, while 6283 at least jumped to the eye.
        ///
        /// The others — linear, radii, diameters, chamfers — stay in `mm`, which
        /// is the true unit. When the type cannot be read `mm` is kept:
        /// it is the behaviour of before, and it is right for the
        /// vast majority of the dimensions.
        /// </summary>
        private static Dictionary<string, object> Dimension(IDimension d, string itemName)
        {
            // ── AND THE RIGHT ENUM IS NOT THE ONE THAT IT SEEMS ──────────
            //
            // `IDimension.GetType()` is a COM method that returns an `int`, and has
            // nothing to do with `Object.GetType()` (verified by
            // reflection on the interop 29.5.1: it returns `System.Int32` and is
            // declared on `IDimension`).
            //
            // But it **does not return `swDimensionType_e`**, which is the enum with the more
            // similar name and where «angular» is 3. The first write used
            // that one, and the repair did not fire: the dimension kept
            // coming out 6283.185 mm.
            //
            // Measured by putting the number in the state and looking at it:
            //
            //     revolution at 360 degrees  ->  1
            //     thickness, length, Ø       ->  0
            //
            // which is `swDimensionParamType_e`: `DoubleLinear = 0`,
            // `DoubleAngular = 1`, `Integer = 2`, `Unknown = -1`.
            //
            // Two almost homonymous enums for two different things, and the compiler cannot
            // say anything because both are `int`. The only way of
            // knowing was to ask the part.
            const int Angular = 1;   // swDimensionParamType_e.swDimensionParamTypeDoubleAngular
            int typ = 0;
            try { typ = d.GetType(); } catch { }

            // A VALUE THAT CANNOT BE READ IS NULL, NOT ZERO (Z-C21.13 follow-up,
            // S1 in parts). ApiHelpers.ReadDimension moved to GetSystemValue3,
            // which throws on an answer that is not one number (shapes the probe
            // did not see: a driven dimension, a suppressed feature). Until
            // library 0.145.0 the angle stayed at the 0 it was initialised to,
            // «0 degrees», a plausible wrong number, and a linear dimension that
            // threw dropped the dimensions of the whole feature, because the only
            // catch was the one around the walk in FeatureDimensions. Now the
            // row stays, with its name and a null value: not read, not zero.
            object readValue = null;
            if (typ == Angular)
            {
                try { readValue = Math.Round(ApiHelpers.ReadDimension(d) * 180.0 / Math.PI, 3); } catch { }
                return new Dictionary<string, object>
                {
                    ["name"] = itemName,
                    ["deg"] = readValue,
                };
            }

            try { readValue = Math.Round(Units.Mm(ApiHelpers.ReadDimension(d)), 3); } catch { }
            return new Dictionary<string, object>
            {
                ["name"] = itemName,
                ["mm"] = readValue,
            };
        }

        /// <summary>The name of the feature that generated this face.</summary>
        private static string FaceFeature(IFace2 face)
        {
            try { return (face.GetFeature() as IFeature)?.Name; }
            catch { return null; }
        }

        private static string BodyFeature(IBody2 body)
        {
            try
            {
                var face = body.GetFirstFace() as IFace2;
                var feature = face?.GetFeature() as IFeature;
                return feature?.Name;
            }
            catch { return null; }
        }

        /// <summary>
        /// The mass of the part in kg, or null if it cannot be said.
        ///
        /// UseSystemUnits is written EXPLICITLY (ApiHelpers.NewMassProperty) as
        /// a statement of the units, not as a fix: IMassProperty~UseSystemUnits.html,
        /// «The default value is True. Thus, system units (meters, radians, and
        /// kilograms) are used» (Z-C21.9, finding N74; until library 0.144.0
        /// this comment said the value would otherwise come in document units).
        /// </summary>
        private static double? Mass(IModelDoc2 model, List<IBody2> bodies)
        {
            try
            {
                // S2 (ApiHelpers.NewMassProperty). As before, a refused
                // AddBodies is not checked here.
                var property = ApiHelpers.NewMassProperty(model, bodies?.Cast<object>(), out _);
                if (property == null) return null;
                double kg = property.Mass;
                return kg > 0 ? (double?)kg : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Ends of the edge, in metres. Null if they cannot be read.
        ///
        /// GetCurveParams3 does NOT return a double[]: it returns
        /// ICurveParamData, with StartPoint and EndPoint that are objects to
        /// convert to double[3]. Verified by reflection on the interop
        /// 29.5.1.1 (SOLIDWORKS 2021), not remembered: the wrong signature here
        /// would have given an InvalidCastException at the first edge read.
        /// </summary>
        private static bool Ends(IEdge edge, out double[] start, out double[] fine)
        {
            start = null;
            fine = null;
            try
            {
                var data = ApiHelpers.EdgeParameters(edge);
                if (data == null) return false;
                start = data.StartPoint as double[];
                fine = data.EndPoint as double[];
                return start != null && fine != null && start.Length >= 3 && fine.Length >= 3;
            }
            catch { return false; }
        }

        /// <summary>
        /// Approximate length of an edge, in mm. Zero if it cannot be read.
        ///
        /// `internal` and not `private` because `SelectEdges` uses it too
        /// when the edges arrive by reference to a step and not by
        /// uid: there is no signature there to read the measurement from, and without this
        /// the check «the radius fits on the edge» was skipped.
        /// Copying it would have been the way to make them diverge.
        /// </summary>
        internal static double LengthMm(IEdge edge)
        {
            if (edge == null) return -1;
            // THE TRUE LENGTH, NOT THE CHORD (entry E19, 25/9/2026).
            //
            // Up to library 0.119.0 here there was the distance between the ends,
            // «approximate but stable, never shown as a dimension». It was shown
            // indeed: it ends up in `length_mm` in the state, and for a
            // CLOSED edge — the border of a hole — the ends coincide and the chord
            // is ZERO. Measured with the little bench: a plate with two O12 holes
            // published four edges «0 long», and whoever reads the state saw
            // a circumference of 37.7 described as an edge that is not there.
            // For an arc the chord is less than the arc. And the ranking of the uids,
            // which is by length, always put the holes at the bottom: a hole never
            // renumbered anything, and the finding of the edges by signature
            // could not even be tested.
            //
            // `ICurve.GetLength3` between the two parameters of the edge: it is already
            // used and measured in `WeldmentOperations` for the perimeter. If it does not
            // answer we go back to the chord, which for a segment is the same
            // thing; if not even the ends can be read, -1: «I do not know», not
            // «zero long» (entry Z-C8).
            try
            {
                var parameters = ApiHelpers.EdgeParameters(edge, out ICurve curva);
                if (curva != null && parameters != null)
                {
                    double real = curva.GetLength3(parameters.UMinValue, parameters.UMaxValue);
                    if (real > 0) return Units.Mm(real);
                }
            }
            catch { }
            double[] a, b;
            if (!Ends(edge, out a, out b)) return -1;
            double dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2];
            return Units.Mm(Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }

        private static double[] PointOnEdge(IEdge edge)
        {
            double[] a, b;
            if (!Ends(edge, out a, out b)) return null;
            return new[]
            {
                Math.Round(Units.Mm((a[0] + b[0]) / 2), 3),
                Math.Round(Units.Mm((a[1] + b[1]) / 2), 3),
                Math.Round(Units.Mm((a[2] + b[2]) / 2), 3),
            };
        }

        /// <summary>
        /// The signature of a face MEASURED NOW, without reading the whole
        /// document again.
        ///
        /// It serves for the finding by persist ID (entry E6): when SOLIDWORKS
        /// proposes a face, the judge needs the signature of THAT
        /// face as it is at this moment — not the one that the plan carries
        /// with it, which is precisely what is being verified.
        ///
        /// No uid: a uid is a position in a ranking, and here there is
        /// no ranking. Whoever uses it already has the uid it was looking for.
        /// </summary>
        internal static EntitySignature FaceSignature(IFace2 face)
        {
            if (face == null) return null;
            try
            {
                // ZERO-E-ZERO: 0 in `Measure` means «not measured» for all
                // those who read it (they look at `> 0`).
                double areaMm2 = 0;
                try { areaMm2 = Units.Mm2(face.GetArea()); } catch { }

                int featureId;
                string featureItem;
                FeatureOfFace(face, out featureId, out featureItem);

                return new EntitySignature
                {
                    Type = "FACE",
                    Measure = Math.Round(areaMm2, 2),
                    PointMm = PointOnFace(face),
                    Normal = FaceNormalVector(face),
                    Entity = face,
                    FeatureId = featureId,
                    Feature = featureItem,
                };
            }
            catch { return null; }
        }


        /// <summary>
        /// The DETAIL of a feature: everything that costs COM calls.
        ///
        /// It is in a method of its own because it is called AFTER deciding which
        /// features survive the ceiling, and not while scanning the tree
        /// (entry E12). The pass over the tree reads only type and name — which were
        /// already read anyway — and sets a reference aside; the detail
        /// is paid for only on the eighty that remain.
        /// </summary>
        /// <param name="inError">
        /// It is incremented for every feature that carries an error. It is a
        /// counter of the caller, and is passed by reference because the
        /// count ends up in `features_in_error` on the whole state:
        /// keeping it in here would mean losing it.
        /// </param>
        private static Dictionary<string, object> FeatureDetail(
            IFeature f, string typ, string itemName, ref int inError)
        {
            var entryF = new Dictionary<string, object>
            { ["uid"] = itemName, ["kind"] = typ };

            // THE UID OF A FEATURE IS ITS NAME, and a name
            // can be renamed. Whoever renames «Cut1» to «Hollow hole» and
            // then calls another feature «Cut1» has just
            // transferred the identifier from one thing to another,
            // and nobody notices.
            //
            // `GetID()` cannot be renamed. It does not replace the uid —
            // the name serves whoever reads — but it allows
            // checking that the name still points to that one.
            try { entryF["feature_id"] = f.GetID(); } catch { }

            // A SUPPRESSED FEATURE AND A LIVE ONE WERE
            // INDISTINGUISHABLE. The model proposed to fillet
            // the edges of a hole that is not in the part.
            // It is written only when true: it is the rare case, and
            // writing it always would cost room to say «all
            // normal» eighty times.
            // Read in the active configuration with IsSuppressed2 since Z-C21.7
            // (S3, M44; ApiHelpers.IsSuppressedHere has the probe numbers).
            try { if (ApiHelpers.IsSuppressedHere(f)) entryF["suppressed"] = true; } catch { }

            // A FEATURE IN ERROR AND A SOUND ONE WERE
            // INDISTINGUISHABLE, and it is worse than the suppressed one.
            //
            // Measured on 14/9/2026: a 100x60x20 plate with an 8
            // fillet on the edges at the top, then the width
            // brought to 10. Two fillets of 8 on 10 mm do not
            // fit, and SOLIDWORKS says so —
            // `GetErrorCode2` = 12, `GetWhatsWrongCount` = 1 — but
            // the state described «Fillet1, radius 8» as any
            // feature. The volume was 3,000, that is
            // **exactly** 100x10x3: the fillet had
            // done nothing, and whoever read saw a sound part.
            //
            // It is written only when there is one, as for `suppressed`:
            // it is the rare case.
            try
            {
                bool warningOnly;
                int code = f.GetErrorCode2(out warningOnly);
                if (code != 0)
                {
                    entryF["error"] = FeatureErrors.Sentence(code);
                    entryF["error_code"] = code;
                    if (warningOnly) entryF["warning_only"] = true;
                    inError++;
                }
            }
            catch { }

            // THE PARAMETERS: what makes a hole «an M8» instead
            // of «a feature named Foro1».
            FeatureParameters(f, typ, entryF);

            // THE DIMENSIONS: without these nothing is modified.
            //
            // «take that hole from M8 to M10» and «take the
            // thickness to 3» are the most frequent request in the
            // workshop, and until now they were impossible for a
            // simple reason: the model did not know that the
            // dimensions existed. It saw a name and some read-only
            // parameters.
            //
            // A dimension has a NAME that can be cited
            // («D1@Sketch1») and a value. Putting them here is what
            // makes `set_dimension` possible.
            FeatureDimensions(f, entryF);
            return entryF;
        }

        /// <summary>
        /// The area of a face in m2, or 0 when it cannot be read. It is a sort
        /// key and nothing else — a face that does not give its area ends up at
        /// the bottom of the ranking — and every place that WRITES an area
        /// reads it again and turns an unreadable one into null.
        /// </summary>
        private static double AreaOrZero(IFace2 face)
        {
            // ZERO-E-ZERO: a sort key, not a statement. An unreadable area sorts
            // last; the state writes null for it (readArea in the listing).
            try { return face.GetArea(); } catch { return 0.0; }
        }

        /// <summary>
        /// How far a point of the model (mm) is from a face (mm): the distance
        /// to the closest point of the FACE, which is not the `point_mm` the
        /// state writes. IFace2~GetClosestPointOn.html: «Array of five doubles
        /// representing the X,Y,Z point on the face followed by the U, V
        /// parameter on the face that is closest to the input point»; the
        /// remarks: it «returns only one point, regardless of how many points
        /// achieve the minimum distance» — the distance is the same. Null when
        /// it cannot be read.
        /// </summary>
        private static double? DistanceToFaceMm(IFace2 face, double[] pointMm)
        {
            if (face == null || pointMm == null || pointMm.Length < 3) return null;
            try
            {
                var p = face.GetClosestPointOn(Units.M(pointMm[0]), Units.M(pointMm[1]), Units.M(pointMm[2])) as double[];
                if (p == null || p.Length < 3) return null;
                double dx = Units.Mm(p[0]) - pointMm[0];
                double dy = Units.Mm(p[1]) - pointMm[1];
                double dz = Units.Mm(p[2]) - pointMm[2];
                return Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            catch { return null; }
        }

        private static double[] PointOnFace(IFace2 face)
        {
            try
            {
                // A GUARANTEED point on the face, not the centre of the box:
                // the centre of a face with a hole in the middle can fall into the
                // void, and SelectByID2 on a point that is not on the face
                // answers False silently.
                var p = face.GetClosestPointOn(0, 0, 0) as double[];
                if (p == null) return null;
                return new[]
                {
                    Math.Round(Units.Mm(p[0]), 3),
                    Math.Round(Units.Mm(p[1]), 3),
                    Math.Round(Units.Mm(p[2]), 3),
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// The normal of a planar face — the TRUE one, not that of the
        /// surface below.
        ///
        /// THE DIFFERENCE IS NOT A DETAIL, AND IT COST A DAY.
        /// `PlaneParams` describes the INFINITE PLANE on which the face lies, and the
        /// two opposite faces of a plate lie on parallel planes with the
        /// same orientation: the surface does not know which way the
        /// face looks. On 10 September 2026, at the first run inside SolidWorks, the
        /// 200x120x20 test plate came out like this:
        ///
        ///     F1  verso=dietro  area=24000  normale=[0,0,-1]
        ///     F2  verso=dietro  area=24000  normale=[0,0,-1]
        ///
        /// Two opposite faces, the same normal, and no «front» face in the
        /// whole document. Fifteen checks died there. And it was not a defect of
        /// the bench: it is what the MODEL read. To whoever asks «a hole on the
        /// front face» the state answered that a front face is not there, and the
        /// two large faces were indistinguishable from each
        /// other — same area, same direction, same signature.
        ///
        /// `IFace2.FaceInSurfaceSense()` is the missing piece: it says whether the
        /// face is oriented against its surface. Verified
        /// by reflection on the interop 29.5.1.1.
        /// </summary>
        private static double[] FaceNormalVector(IFace2 face)
        {
            try
            {
                var surface = face.IGetSurface();
                if (surface == null || !surface.IsPlane()) return null;
                var plane = surface.PlaneParams as double[];
                if (plane == null || plane.Length < 3) return null;
                double sign = face.FaceInSurfaceSense() ? -1.0 : 1.0;
                return new[]
                {
                    Math.Round(plane[0] * sign, 4),
                    Math.Round(plane[1] * sign, 4),
                    Math.Round(plane[2] * sign, 4),
                };
            }
            catch { return null; }
        }
    }
}
