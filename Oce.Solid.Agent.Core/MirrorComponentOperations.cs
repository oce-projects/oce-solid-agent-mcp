using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// MIRRORING A COMPONENT (entry GA11): the right and the left bracket, the
    /// most common case of metalwork.
    ///
    /// Measured on 27/9/2026 (`probe0089 ga11`, base 120x80x10 + bracket
    /// 20x40x10 at x = +40, assembly right plane) with the modern way,
    /// `FeatureManager.CreateDefinition(swFmMirrorComponent)` →
    /// `IMirrorComponentFeatureData` → `CreateFeature` («MirrorComponent1»,
    /// `MirrorCompFeat`), TYPED arrays (`Component2[]`):
    ///
    ///  * INSTANCE (`ComponentsToInstanceAlignToComponentOrigin`): one more
    ///    component of the SAME file, at x = -40, orientation reversed.
    ///  * LINKED OPPOSITE (`OppositeHandComponents`, `BreakLinksToOriginalPart`
    ///    false): a NEW FILE («…_spec»), whose tree is a single
    ///    feature, `MirrorStock`: change the right one and the left one changes.
    ///  * UNLINKED OPPOSITE (the same with `BreakLinksToOriginalPart` true): new
    ///    file with the features COPIED (sketch, extrusion, move/copy
    ///    body): correct the right one and the left one stays as it was, without saying so.
    ///
    /// The choice is always asked for (no direction by default) and afterwards it is VERIFIED:
    /// the centre of the new part against the mirror of the centre of the original,
    /// and — for the opposites — the tree of the new file (`MirrorStock` is there = it is
    /// linked).
    ///
    /// THE ORIENTATION OF AN INSTANCE (finding N26, measured on 28/9/2026 by
    /// the Z-C21.3 probe of the mirrored instance's axes: bracket 20 x 40 x 10
    /// with a corner boss, inserted at x = +40, mirrored on the assembly Right
    /// plane). An instance of the same file can come out in FOUR rigid
    /// orientations, one per swMirrorComponentOrientation2_e value, and the
    /// four have the SAME box
    /// centre: the centre check passed on all of them (0 mm), so it cannot
    /// tell them apart. Measured axes X' / Y' / Z' of Transform2:
    ///   0 MirroredX_MirroredY                     (-1,0,0) (0,1,0) (0,0,-1)
    ///   1 MirroredAndFlippedX_MirroredY           (1,0,0) (0,1,0) (0,0,1)
    ///   2 MirroredX_MirroredAndFlippedY           (-1,0,0) (0,-1,0) (0,0,1)
    ///   3 MirroredAndFlippedX_MirroredAndFlippedY (1,0,0) (0,-1,0) (0,0,-1)
    /// With no orientation array SOLIDWORKS gave 0, the default the guide
    /// documents (IMirrorComponentFeatureData~ComponentOrientationsAlignToComponentOrigin.html,
    /// Remarks: «missing orientations default to
    /// swMirrorComponentOrientation2_e.swOrientation_MirroredX_MirroredY»);
    /// an int[] gave the same axes as the typed enum array. So the orientation
    /// is now always SET (argument «orientation», default 0) and the axes of
    /// the new instance are CHECKED against it: X' = ±(X mirrored in the plane),
    /// Y' = ±(Y mirrored in the plane), Z' = X' x Y' (see MirrorAxesProblem).
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>The centre of the true box of a component, in the assembly, in MILLIMETRES; null if it cannot be read.</summary>
        private static double[] ComponentCenter(IComponent2 c)
        {
            try
            {
                object info;
                var bodies = c.GetBodies3((int)swBodyType_e.swSolidBody, out info) as object[];
                var tr = c.Transform2 as MathTransform;
                if (bodies == null || bodies.Length == 0 || tr == null) return null;
                double[] everything = null;
                foreach (var ob in bodies)
                {
                    var copy = (ob as IBody2)?.Copy() as IBody2;
                    if (copy == null || !copy.ApplyTransform(tr)) return null;
                    var sc = BodyBox(copy);
                    if (sc == null) return null;
                    if (everything == null) everything = (double[])sc.Clone();
                    else { for (int k = 0; k < 3; k++) everything[k] = Math.Min(everything[k], sc[k]); for (int k = 3; k < 6; k++) everything[k] = Math.Max(everything[k], sc[k]); }
                }
                return new[] { (everything[0] + everything[3]) / 2, (everything[1] + everything[4]) / 2, (everything[2] + everything[5]) / 2 };
            }
            catch { return null; }
        }

        /// <summary>
        /// N26: the «orientation» argument, as the catalogue spells it, and the
        /// swMirrorComponentOrientation2_e value it stands for (swconst page
        /// swMirrorComponentOrientation2_e.html: MirroredX_MirroredY 0,
        /// MirroredAndFlippedX_MirroredY 1, MirroredX_MirroredAndFlippedY 2,
        /// MirroredAndFlippedX_MirroredAndFlippedY 3). The first one is the
        /// default, as it is for SOLIDWORKS.
        /// </summary>
        internal static readonly string[] MirrorOrientationNames =
        {
            "mirrored_x_mirrored_y",
            "mirrored_and_flipped_x_mirrored_y",
            "mirrored_x_mirrored_and_flipped_y",
            "mirrored_and_flipped_x_mirrored_and_flipped_y",
        };

        private static readonly swMirrorComponentOrientation2_e[] MirrorOrientationValues =
        {
            swMirrorComponentOrientation2_e.swOrientation_MirroredX_MirroredY,
            swMirrorComponentOrientation2_e.swOrientation_MirroredAndFlippedX_MirroredY,
            swMirrorComponentOrientation2_e.swOrientation_MirroredX_MirroredAndFlippedY,
            swMirrorComponentOrientation2_e.swOrientation_MirroredAndFlippedX_MirroredAndFlippedY,
        };

        /// <summary>
        /// N26: the X, Y and Z axes of a component in the assembly, as unit
        /// vectors: Transform2 applied to (1,0,0), (0,1,0), (0,0,1) with
        /// IMathVector.MultiplyTransform (a vector takes the rotation, not the
        /// translation), as the probe read them. Null if any is unreadable.
        /// </summary>
        private double[][] ComponentAxes(IComponent2 component)
        {
            try
            {
                var math = _sw.GetMathUtility() as IMathUtility;
                var transform = component.Transform2 as MathTransform;
                if (math == null || transform == null) return null;
                var axes = new double[3][];
                for (int k = 0; k < 3; k++)
                {
                    var unit = new double[3];
                    unit[k] = 1;
                    var moved = ((IMathVector)math.CreateVector(unit)).MultiplyTransform(transform) as IMathVector;
                    var data = moved == null ? null : moved.ArrayData as double[];
                    if (data == null || data.Length < 3) return null;
                    axes[k] = new[] { data[0], data[1], data[2] };
                }
                return axes;
            }
            catch { return null; }
        }

        /// <summary>
        /// N26: the axes a mirrored instance must have, from the original's
        /// axes, the assembly axis the mirror plane flips (0 = x for the Right
        /// plane, 1 = y for Top, 2 = z for Front) and the orientation index
        /// (see <see cref="MirrorOrientationNames"/>): X' = ±(X mirrored),
        /// Y' = ±(Y mirrored), minus where the orientation says «flipped», and
        /// Z' = X' x Y' so that the result is rigid. Null if an input is not
        /// three vectors of three numbers.
        ///
        /// The probe measured this rule on the Right plane with the original
        /// unrotated (all four orientations, table in the class comment). The
        /// names speak of the COMPONENT's X and Y, which do not depend on the
        /// plane, so the same rule is applied to the other two planes.
        /// </summary>
        internal static double[][] ExpectedMirrorAxes(double[][] before, int planeNormalIndex, int orientation)
        {
            if (before == null || before.Length < 3 || planeNormalIndex < 0 || planeNormalIndex > 2 ||
                orientation < 0 || orientation > 3) return null;
            for (int k = 0; k < 3; k++) if (before[k] == null || before[k].Length < 3) return null;
            double sx = (orientation & 1) != 0 ? -1 : 1;   // 1 and 3: X mirrored AND flipped
            double sy = (orientation & 2) != 0 ? -1 : 1;   // 2 and 3: Y mirrored AND flipped
            var x = new[] { before[0][0], before[0][1], before[0][2] };
            var y = new[] { before[1][0], before[1][1], before[1][2] };
            x[planeNormalIndex] = -x[planeNormalIndex];
            y[planeNormalIndex] = -y[planeNormalIndex];
            for (int k = 0; k < 3; k++) { x[k] *= sx; y[k] *= sy; }
            var z = new[] { x[1] * y[2] - x[2] * y[1], x[2] * y[0] - x[0] * y[2], x[0] * y[1] - x[1] * y[0] };
            return new[] { x, y, z };
        }

        /// <summary>
        /// N26: null when the new instance's axes are the ones the orientation
        /// asks for (1e-6 on each component: the probe read exact 0 and ±1), else
        /// the sentence that says which axis is off and how.
        /// </summary>
        internal static string MirrorAxesProblem(double[][] before, double[][] after, int planeNormalIndex, int orientation)
        {
            var expected = ExpectedMirrorAxes(before, planeNormalIndex, orientation);
            if (expected == null) return "the axes of the original component could not be read";
            if (after == null || after.Length < 3) return "the axes of the new component could not be read";
            var names = new[] { "X", "Y", "Z" };
            var wrong = new List<string>();
            for (int k = 0; k < 3; k++)
            {
                if (after[k] == null || after[k].Length < 3) return "the " + names[k] + " axis of the new component could not be read";
                double off = 0;
                for (int j = 0; j < 3; j++) off = Math.Max(off, Math.Abs(after[k][j] - expected[k][j]));
                if (off > 1e-6) wrong.Add(names[k] + "' is " + AxisText(after[k]) + " instead of " + AxisText(expected[k]));
            }
            return wrong.Count == 0 ? null : string.Join(", ", wrong);
        }

        private static string AxisText(double[] v)
        {
            var parts = new string[3];
            for (int k = 0; k < 3; k++)
            {
                double r = Math.Round(v[k], 4);
                if (r == 0) r = 0;   // no «-0»
                parts[k] = r.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            }
            return "(" + string.Join(", ", parts) + ")";
        }

        private static double[] RoundedAxis(double[] v)
        {
            var r = new double[3];
            for (int k = 0; k < 3; k++) { r[k] = Math.Round(v[k], 6); if (r[k] == 0) r[k] = 0; }
            return r;
        }

        private Outcome MirrorComponent(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var assemblyDoc = model as IAssemblyDoc;
            if (assemblyDoc == null)
                return Failed("I mirror a component of an assembly: this is not one. To mirror a feature in a " +
                               "part there is «mirror».");
            string mode = (Str(a, "mode", "") ?? "").Trim().ToLowerInvariant();
            if (mode != "instance" && mode != "opposite_linked" && mode != "opposite_unlinked")
                return Failed("Tell me «mode», it is the choice that matters: «instance» (the same file, mirrored: for a symmetric part), " +
                               "«opposite_linked» (a new file that follows the original: you change the right one and the left one changes) " +
                               "or «opposite_unlinked» " +
                               "(a new independent file: you correct the right one and the left one stays as it was).");
            string plane = (Str(a, "plane", "") ?? "").Trim().ToLowerInvariant();
            int which = plane == "front" ? 1 : plane == "top" ? 2 : plane == "right" ? 3 : 0;
            if (which == 0)
                return Failed("«plane» is a plane of the assembly: «front», «top» or «right», not «" + plane + "».");
            object[] rawItems = null;
            try { rawItems = assemblyDoc.GetComponents(false) as object[]; } catch { }
            var components = DocumentState.ComponentsInOrder(rawItems);
            string uid = Str(a, "component", null);
            var c = PerUid(components, uid);
            if (c == null) return Failed(NotFound(uid, "component", components));
            string suffix = (Str(a, "suffix", "_spec") ?? "_spec").Trim();
            if (suffix.Length == 0 || suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return Failed("«suffix» goes in the name of the new file: letters and digits, for example «_sx».");
            // N26: the orientation of a mirrored instance, always set and then checked.
            // An opposite-hand file is a new, mirrored part: SOLIDWORKS has no
            // orientation to choose for it (the orientations go with
            // ComponentsToInstanceAlignToComponentOrigin), so asking for one
            // there is refused rather than ignored.
            string orientationAsked = (Str(a, "orientation", "") ?? "").Trim().ToLowerInvariant();
            int orientation = orientationAsked.Length == 0 ? 0 : Array.IndexOf(MirrorOrientationNames, orientationAsked);
            if (orientationAsked.Length > 0 && mode != "instance")
                return Failed("«orientation» applies only to a mirrored instance of the same file: an opposite-hand " +
                               "file is a new mirrored part and has no orientation to choose. Leave «orientation» out. " +
                               "Nothing was changed.");
            if (orientation < 0)
                return Failed("«orientation» is one of «" + string.Join("», «", MirrorOrientationNames) + "» (the first is " +
                               "the default), not «" + orientationAsked + "». Nothing was changed.");

            // THE ASSEMBLY PLANE BY ITS GEOMETRY, NOT AS THE N-TH RefPlane OF THE
            // TREE (Z-C21.5, finding C3-6 extended, scheme S19): the plane with the
            // canonical normal through the origin, References.BasePlane. Counting
            // the RefPlanes («the third in order») mirrored on another plane as
            // soon as a plane sat before the default ones. Not N26, the
            // orientation of the instance, fixed in Z-C21.2-ter.
            IFeature planeF = References.BasePlane(model, plane, out string planeProblem);
            if (planeF == null)
                return Failed("I cannot find the assembly's «" + plane + "» plane: " + planeProblem +
                               " Nothing was changed.");
            var centerBefore = ComponentCenter(c);
            if (centerBefore == null)
                return Failed("I cannot read where «" + NameOf(c) + "» is in the assembly: without it, I could not verify " +
                               "the mirror. I touched nothing.");
            double[][] axesBefore = mode == "instance" ? ComponentAxes(c) : null;
            if (mode == "instance" && axesBefore == null)
                return Failed("I cannot read the axes of «" + NameOf(c) + "» in the assembly (Transform2): without them I " +
                               "could not check the orientation of the mirrored instance. Nothing was changed.");
            string originalFile = "";
            try { originalFile = c.GetPathName() ?? ""; } catch { }
            var namesBefore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in components) namesBefore.Add(v.Key);

            IFeature newOne = null;
            ApiHelpers.FeatureCreation creation = null;
            try
            {
                // S10: CreateDefinition -> fill -> CreateFeature, and the error code
                // of what was born, through ApiHelpers.CreateFeatureFromDefinition.
                creation = ApiHelpers.CreateFeatureFromDefinition<IMirrorComponentFeatureData>(
                    model.FeatureManager, (int)swFeatureNameID_e.swFmMirrorComponent, data =>
                {
                    data.MirrorPlane = planeF;
                    var whichOnes = new Component2[] { (Component2)c };   // TYPED array (object[] makes phantom features)
                    if (mode == "instance")
                    {
                        data.ComponentsToInstanceAlignToComponentOrigin = whichOnes;
                        // N26: one orientation per component, typed as in
                        // Mirror_Components_II_Example_CSharp.htm; the page of
                        // ComponentsToInstanceAlignToComponentOrigin says «Use
                        // IMirrorComponentFeatureData::ComponentOrientationsAlignToComponentOrigin
                        // to specify the orientation of each component in this array».
                        data.ComponentOrientationsAlignToComponentOrigin =
                            new swMirrorComponentOrientation2_e[] { MirrorOrientationValues[orientation] };
                    }
                    else
                    {
                        data.OppositeHandComponents = whichOnes;
                        data.NameModifierType = (int)swMirrorComponentNameModifier_e.swMirrorComponentName_Suffix;
                        data.NameModifier = suffix;
                        data.PlaceFilesInOneFolder = true;
                        data.MirrorComponentsFolderLocation = Path.GetDirectoryName(originalFile) ?? "";
                        data.BreakLinksToOriginalPart = mode == "opposite_unlinked";
                        data.CreateDerivedConfigurations = false;
                    }
                    data.MirrorType = (int)swMirrorComponentMirrorType_e.swMirrorType_CenterOfBoundingBox;
                    return true;
                });
                if (creation.Stage == ApiHelpers.CreationStage.Definition)
                    return Failed("SOLIDWORKS did not give the definition of the component mirror.");
                newOne = creation.Feature;
            }
            catch (Exception ex) { return Failed("The mirror did not succeed: " + ex.Message); }
            try { RebuildKeepingUndo(model); } catch { }
            if (newOne == null)
                return Failed("SOLIDWORKS did not create the mirror (CreateFeature answered null). If the file «" +
                               Path.GetFileNameWithoutExtension(originalFile) + suffix + "» already exists, change «suffix».");

            // THE ERROR CODE OF THE MIRROR THAT WAS BORN (finding L11-7).
            // IFeatureManager~CreateFeature.html: «Use IFeature::GetErrorCode2
            // to get any errors». ApiHelpers.CreateFeatureFromDefinition reads it
            // right after CreateFeature, but until library 0.144.0 this caller
            // kept only the feature, so a mirror born in error went on to the
            // component checks and could pass them. It is read again after the
            // rebuild above, because a rebuild can put a feature in error (the
            // same rule as ModifyAndVerify). A warning is not an error
            // (IFeature~GetErrorCode2.html, «IsWarning»). When CreateFeature
            // answers null there is no feature to ask, and the message above
            // says what else to check.
            // If the second reading cannot be made, the first one decides.
            string mirrorError = ApiHelpers.RebuildError(newOne, out bool mirrorErrorKnown);
            if (mirrorError == null && !mirrorErrorKnown && creation != null) mirrorError = creation.ErrorPhrase;
            if (mirrorError != null)
                return Failed("The component mirror «" + SafeName(newOne) + "» was born in error: " + mirrorError +
                               "." + RemoveMirror(model, newOne, null));

            // THE NEW COMPONENT: the one that was not there before.
            object[] after = null;
            try { after = assemblyDoc.GetComponents(false) as object[]; } catch { }
            IComponent2 newItem = null;
            foreach (var v in DocumentState.ComponentsInOrder(after))
                if (!namesBefore.Contains(v.Key)) { newItem = v.Value; break; }
            if (newItem == null)
                return Failed("The mirror «" + SafeName(newOne) + "» is there but I do not see a new component in the assembly." +
                               RemoveMirror(model, newOne, null));
            string newFile = "";
            try { newFile = newItem.GetPathName() ?? ""; } catch { }
            bool sameFile = string.Equals(newFile, originalFile, StringComparison.OrdinalIgnoreCase);
            if (mode == "instance" && !sameFile)
                return Failed("I asked for an instance of the same file and SOLIDWORKS put «" + Path.GetFileName(newFile) + "»." +
                               RemoveMirror(model, newOne, newFile));
            if (mode != "instance" && sameFile)
                return Failed("I asked for an opposite file and the new component still uses «" + Path.GetFileName(originalFile) + "»." +
                               RemoveMirror(model, newOne, null));
            // THE LINK, READ FROM THE TREE OF THE NEW FILE: MirrorStock = linked.
            bool? linked = null;
            if (mode != "instance")
            {
                try
                {
                    var dn = newItem.GetModelDoc2() as IModelDoc2;
                    var ff = dn?.FirstFeature() as IFeature;
                    bool found = false;
                    while (ff != null) { if (ff.GetTypeName2() == "MirrorStock") { found = true; break; } ff = ff.GetNextFeature() as IFeature; }
                    if (dn != null) linked = found;
                }
                catch { }
                if (linked == null)
                    return Failed("The new file «" + Path.GetFileName(newFile) + "» is there, but I cannot read its tree: I cannot " +
                                   "tell you whether it follows the original. Open it and look whether the first feature of its tree is a MirrorStock (a mirrored part)." +
                                   RemoveMirror(model, newOne, newFile));
                if (linked.Value != (mode == "opposite_linked"))
                    return Failed("I asked for a file " + (mode == "opposite_linked" ? "linked" : "unlinked") + " to the original and «" +
                                   Path.GetFileName(newFile) + "» is " + (linked.Value ? "linked" : "unlinked") + "." +
                                   RemoveMirror(model, newOne, newFile));
            }
            // THE PLACE: the centre of the new one and the mirror of the centre of the original.
            var newCenter = ComponentCenter(newItem);
            var expected = (double[])centerBefore.Clone();
            expected[which == 1 ? 2 : which == 2 ? 1 : 0] *= -1;   // front XY -> z, top XZ -> y, right YZ -> x
            double deviation = newCenter == null ? double.NaN :
                Math.Sqrt(Math.Pow(newCenter[0] - expected[0], 2) + Math.Pow(newCenter[1] - expected[1], 2) + Math.Pow(newCenter[2] - expected[2], 2));   // BodyBox already gives mm
            if (double.IsNaN(deviation) || deviation > 0.01)
                return Failed("«" + NameOf(newItem) + "» was created, but its center " + (newCenter == null ? "cannot be read" :
                               "is " + deviation.ToString("0.###") + " mm from the mirror of the original") + ": it is not the mirror you wanted." +
                               RemoveMirror(model, newOne, mode == "instance" ? null : newFile));
            // N26: THE AXES, which the centre cannot see (all four orientations
            // measured with the centre 0 mm off). Read from Transform2 of the new
            // instance and compared with the orientation that was set.
            double[][] axesAfter = null;
            if (mode == "instance")
            {
                axesAfter = ComponentAxes(newItem);
                string axesProblem = MirrorAxesProblem(axesBefore, axesAfter, which == 1 ? 2 : which == 2 ? 1 : 0, orientation);
                if (axesProblem != null)
                    return Failed("«" + NameOf(newItem) + "» was born in the right place but not with the orientation " +
                                   "«" + MirrorOrientationNames[orientation] + "»: " + axesProblem + "." +
                                   RemoveMirror(model, newOne, null));
            }
            var measurements = new Dictionary<string, object>
            {
                ["mirror"] = SafeName(newOne),
                ["mode"] = mode,
                ["plane"] = plane,
                ["new_component"] = NameOf(newItem),
                ["file"] = newFile,
                ["new_file"] = !sameFile,
                ["linked_to_original"] = linked,
                ["original_center_mm"] = new[] { Round(centerBefore[0]), Round(centerBefore[1]), Round(centerBefore[2]) },
                ["new_center_mm"] = new[] { Round(newCenter[0]), Round(newCenter[1]), Round(newCenter[2]) },
                // N26: the orientation set, and the axes read back that match it
                // (null for an opposite-hand file, which has no orientation to set).
                ["orientation"] = mode == "instance" ? MirrorOrientationNames[orientation] : null,
                ["new_axes"] = axesAfter == null ? null : new Dictionary<string, object>
                {
                    ["x"] = RoundedAxis(axesAfter[0]),
                    ["y"] = RoundedAxis(axesAfter[1]),
                    ["z"] = RoundedAxis(axesAfter[2]),
                },
                ["verdict"] = mode == "instance"
                    ? "An instance of the same file, mirrored: it works for a symmetric part."
                    : mode == "opposite_linked"
                        ? "A new file that FOLLOWS the original: you change the right one and the left one changes too."
                        : "A new INDEPENDENT file: you correct the right one and the left one stays as it was.",
            };
            _log("[agent] mirrored " + NameOf(c) + " (" + mode + ") -> " + NameOf(newItem));
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// Removes the mirror just born when the verification refuses it (review of
        /// 27/9/2026): before, a component that nobody had accepted stayed in the assembly. The new
        /// FILE, if there is one, I do not delete — a file on disk I do not
        /// remove on anybody's behalf — but I say so, because with the same suffix
        /// the mirror cannot be reborn. The tail of the refusal sentence.
        /// </summary>
        /// <summary>
        /// Removes the mirror this step made and returns the tail of the
        /// refusal: «removed» only after the tree is read again and the mirror
        /// is no longer in it (Z-C21.8, the rule of M42; until the review of
        /// this lot it said «the assembly is as it was» on the answer of
        /// DeleteSelection2 alone, and L11-7 added a caller).
        /// </summary>
        private static string RemoveMirror(IModelDoc2 model, IFeature f, string newFile)
        {
            string name = SafeName(f);
            bool deleted = false;
            try
            {
                model.ClearSelection2(true);
                if (f != null && f.Select2(false, 0))
                    deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
                RebuildKeepingUndo(model);
            }
            catch { }
            bool? stillInTree = deleted ? FeatureStillInTree(model, name) : (bool?)null;
            return MirrorRemovalNote(name, deleted, stillInTree,
                                     string.IsNullOrEmpty(newFile) ? null : Path.GetFileName(newFile));
        }

        /// <summary>
        /// Whether a feature of that name is still in the tree: null when the
        /// name is unknown or the tree cannot be walked to the end.
        /// </summary>
        private static bool? FeatureStillInTree(IModelDoc2 model, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (string.Equals(f.Name, name, StringComparison.Ordinal)) return true;
                    f = f.GetNextFeature() as IFeature;
                }
                return false;
            }
            catch { return null; }
        }

        /// <summary>
        /// The words of <see cref="RemoveMirror"/>: «removed» only when the
        /// deletion answered true AND the tree read again no longer holds the
        /// mirror. <paramref name="stillInTree"/> null is «not read».
        /// <paramref name="leftFile"/> is the file of the new component left on
        /// disk, or null. No interop type in the signature: Oce.Solid.Agent.ContractTests
        /// calls it by reflection.
        /// </summary>
        internal static string MirrorRemovalNote(string name, bool deleted, bool? stillInTree, string leftFile)
        {
            string mirror = "the mirror «" + (name ?? "?") + "»";
            string file = string.IsNullOrEmpty(leftFile) ? "" :
                " The file «" + leftFile + "» stayed on the disk: delete it, or use another «suffix».";
            if (!deleted)
                return " DeleteSelection2 did not remove " + mirror + ": it is still in the assembly, remove it yourself." + file;
            if (stillInTree == false)
                return " I removed " + mirror + ": reading the tree again, it is gone." + file;
            if (stillInTree == true)
                return " DeleteSelection2 answered true, but " + mirror + " is still in the tree: remove it yourself." + file;
            return " DeleteSelection2 answered true, but I could not read the tree again to check that " + mirror +
                   " is gone: look at the assembly before going on." + file;
        }

        private static string SafeName(IFeature f)
        {
            try { return f?.Name; } catch { return null; }
        }
    }
}
