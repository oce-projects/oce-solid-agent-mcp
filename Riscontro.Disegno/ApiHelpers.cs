using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// ONE PLACE FOR EACH API PATTERN THAT THE Z-C21 AUDIT FOUND REPEATED
    /// (Z-C21.6, "recurring patterns" S1, S2, S6, S10, S11, S12, S17).
    ///
    /// Sixteen patterns are worth more than seventy one-by-one fixes: when the
    /// way an API must be called changes (a probe, a migration, a guide page
    /// read again), it changes here and nowhere else. Every helper quotes the
    /// page of the SOLIDWORKS API help (sldworksapi.chm, 2021, extracted during
    /// Z-C21.1) that it is built on, so that whoever touches it reads the page
    /// first ("guide first" method).
    ///
    /// WHAT THIS FILE DOES NOT DO: migrate obsolete APIs. That is lot Z-C21.7,
    /// one API per commit behind its bank cases. Where a helper still calls an
    /// obsolete member, the comment says which one, why it is still the one in
    /// use, and what changes after the probe; the call is also in the frozen
    /// list of the static check (Prove/ObsoleteCallsContract.cs).
    ///
    /// Helpers throw what the API throws: the call sites already wrap SOLIDWORKS
    /// calls in try/catch with their own message, and a helper that swallowed
    /// exceptions would turn a failure into a plausible null.
    /// </summary>
    internal static partial class ApiHelpers
    {
        // =====================================================================
        //  S1 — READING A DIMENSION VALUE
        // =====================================================================

        /// <summary>
        /// The value of a dimension of a PART in system units (metres,
        /// radians), in the configuration that is active.
        ///
        /// Guide: IDimension~SystemValue.html, «Obsolete. Superseded by
        /// IDimension::GetSystemValue3, IDimension::IGetSystemValue3,
        /// IDimension::SetSystemValue3, and IDimension::ISetSystemValue3»;
        /// IDimension~GetSystemValue3.html, `object GetSystemValue3(int
        /// WhichConfigurations, object Config_names)`, «The Config_names
        /// argument is only used if whichConfigurations is set to
        /// swSpecifyConfiguration».
        ///
        /// MEASURED by the Z-C21.3 probe probe_dimension_value3 (SOLIDWORKS
        /// 29.5.1, 28/9/2026) on a linear and an angular dimension of a part:
        /// GetSystemValue3(swThisConfiguration, null) is a Double[1] equal to
        /// the obsolete SystemValue and GetSystemValue2("") in every stage (one
        /// configuration: 0.01 m and 5 deg; a second configuration B at 0.025 m
        /// and 8 deg, read with A active and then with B active). Until Z-C21.7
        /// this was SystemValue.
        ///
        /// PARTS ONLY. In a drawing the same call reads the configuration that
        /// is active in the MODEL, not the one the view shows (probe, drawing
        /// stage: 0.01 from A while the view shows B, where SystemValue and
        /// GetSystemValue2("") read 0.025): drawings read through
        /// <see cref="ReadDimensionInView"/>.
        /// </summary>
        internal static double ReadDimension(IDimension dimension)
        {
            return OneDimensionValue(
                dimension.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null),
                "swThisConfiguration");
        }

        /// <summary>
        /// The value of a dimension in system units in the configuration named
        /// <paramref name="configurationName"/> (not empty: the active
        /// configuration of a part is <see cref="ReadDimension"/>).
        ///
        /// Guide: IDimension~GetSystemValue2.html, «ConfigName Name of
        /// configuration Obsolete. See IDimension::GetSystemValue3»; the
        /// replacement with swSpecifyConfiguration and the name in an array,
        /// IDimension~GetSystemValue3.html (above). MEASURED by the same probe:
        /// GetSystemValue3(swSpecifyConfiguration, {name})[0] equals
        /// GetSystemValue2(name) for both configurations and both dimensions,
        /// with either one active; with null names it answers null. Until
        /// Z-C21.7 every call site passed "" to GetSystemValue2.
        /// </summary>
        internal static double ReadDimensionInConfiguration(IDimension dimension, string configurationName)
        {
            if (string.IsNullOrEmpty(configurationName))
                throw new ArgumentException("a configuration name is needed (the active configuration of a part is ReadDimension)");
            return OneDimensionValue(
                dimension.GetSystemValue3((int)swInConfigurationOpts_e.swSpecifyConfiguration, new[] { configurationName }),
                configurationName);
        }

        /// <summary>
        /// The one value of a GetSystemValue3 answer asked for one
        /// configuration. Anything but a single number throws: a wrong cast of a
        /// positional array gives a plausible wrong number, the worst kind of
        /// defect, and the call sites already catch.
        /// </summary>
        private static double OneDimensionValue(object raw, string asked)
        {
            var values = raw as double[];
            if (values == null && raw is object[] boxed && boxed.Length == 1 && boxed[0] is double one)
                values = new[] { one };
            if (values == null || values.Length != 1)
                throw new InvalidOperationException("IDimension.GetSystemValue3 (" + asked + ") did not answer one value");
            return values[0];
        }

        // =====================================================================
        //  S2 — MASS PROPERTIES
        // =====================================================================

        /// <summary>
        /// A NEW mass-property object of a PART, in system units, of the given
        /// bodies (none = every solid body, hidden ones included). Null if
        /// SOLIDWORKS does not give one, or if Recalculate fails.
        /// <paramref name="bodiesAttached"/> is false when the bodies read back
        /// from SelectedItems are not the ones given: the numbers would then be
        /// of other bodies than those asked for.
        ///
        /// One object per reading, as the callers do: the obsolete IMassProperty
        /// changed its answers depending on what was read before (measured, see
        /// OperazioniMisura); the probe below found IMassProperty2 free of that
        /// trap, and the callers keep one object per number anyway.
        ///
        /// Guide: IModelDocExtension~CreateMassProperty.html, «Obsolete.
        /// Superseded by IModelDocExtension::CreateMassProperty2».
        /// IModelDocExtension~CreateMassProperty2.html, Remarks, Part: «Before
        /// calling this method, call IModelDocExtension::SelectByID2 to
        /// pre-select the bodies. You can also specify more bodies after calling
        /// this method by setting IMassProperty2::SelectedItems» (so the
        /// selection is cleared first: a body left selected would be measured
        /// alone). IMassProperty2~IncludeHiddenBodiesOrComponents.html: «After
        /// setting this property to true, be sure to call
        /// IMassProperty2::Recalculate before using the IMassProperty2
        /// properties».
        ///
        /// MEASURED by the Z-C21.3 probe probe_mass_property2 (SOLIDWORKS 29.5.1,
        /// 28/9/2026) on a part of AISI 304 with three bodies, A 16000, B 4000
        /// and C 1000 mm3, C hidden. The obsolete call measured 21000 mm3 with no
        /// bodies (M0, the hidden one included) and each set given to AddBodies
        /// (M1 A 16000, M2 C 1000, M3 all 21000). CreateMassProperty2 with
        /// nothing selected, IncludeHidden true and Recalculate: 21000 (N1 = M0);
        /// SelectedItems Body2[]{A} WITHOUT Recalculate: 21000, the whole part
        /// (N4); with Recalculate: 16000 (N5 = M1); {C} with IncludeHidden true
        /// and Recalculate: 1000 (N7 = M2); {A, B, C}: 21000 (N8 = M3); density
        /// 8000 and mass consistent in every case, and SelectedItems read back
        /// as the bodies given. Hence: IncludeHidden true (the bridge counted the
        /// hidden bodies), the bodies as a TYPED Body2[] (rule of Z-C21.3), and
        /// Recalculate always.
        ///
        /// Parts only, and only where clearing the selection is harmless. The
        /// assembly and the document state keep <see cref="NewMassProperty"/>
        /// (see there why).
        /// </summary>
        internal static IMassProperty2 NewPartMassProperty(
            IModelDoc2 part, IEnumerable<object> bodies, out bool bodiesAttached)
        {
            bodiesAttached = false;
            part.ClearSelection2(true);
            var massProperty = part.Extension.CreateMassProperty2() as IMassProperty2;
            if (massProperty == null) return null;
            // Explicit, not a habit: without it values come in document units,
            // which on someone else's part can be inches and pounds.
            massProperty.UseSystemUnits = true;
            massProperty.IncludeHiddenBodiesOrComponents = true;
            var typed = new List<Body2>();
            if (bodies != null) foreach (var body in bodies) typed.Add((Body2)body);
            Body2[] list = typed.ToArray();
            if (list.Length > 0) massProperty.SelectedItems = list;
            if (!massProperty.Recalculate()) return null;
            if (list.Length == 0) bodiesAttached = true;
            else
            {
                var readBack = massProperty.SelectedItems as object[];
                bodiesAttached = readBack != null && readBack.Length == list.Length;
            }
            return massProperty;
        }

        /// <summary>
        /// A NEW mass-property object through the OBSOLETE call, in system units,
        /// with the given bodies attached (none = every body of the document).
        /// Null if SOLIDWORKS does not give one. <paramref name="bodiesAttached"/>
        /// is false when AddBodies refused: the numbers would then be of the
        /// whole document, not of the bodies asked for.
        ///
        /// Guide: IModelDocExtension~CreateMassProperty.html, «Obsolete.
        /// Superseded by IModelDocExtension::CreateMassProperty2»; its Remarks:
        /// "If IMassProperty::AddBodies ... Not called: mass properties'
        /// calculations include all available bodies in the document".
        ///
        /// STILL USED, FOR TWO CALLERS, after Z-C21.7 moved the other part
        /// readings to <see cref="NewPartMassProperty"/>:
        ///   - the ASSEMBLY (OperazioniMisuraAssieme): the Z-C21.3 probe
        ///     probe_mass_property2 measured CreateMassProperty2 on a part only;
        ///     on an assembly it works on COMPONENTS
        ///     (IModelDocExtension~CreateMassProperty2.html: «Before calling this
        ///     method, call IModelDocExtension::SelectByID2 to pre-select the
        ///     components») and IncludeHiddenBodiesOrComponents then decides
        ///     about hidden components: not measured, while the assembly mass was
        ///     measured with this call (the sum of the parts to the gram);
        ///   - the DOCUMENT STATE (StatoDocumento.Massa): CreateMassProperty2
        ///     measures what is SELECTED, so the probe cleared the selection
        ///     first, and the state must not: it reads the designer's selection
        ///     a few lines later (StatoDocumento.LaSelezione, the selection field
        ///     of the state), and clearing it would erase what the designer
        ///     points at.
        ///     Measuring with a selection left in place was not probed (does
        ///     SelectedItems replace a pre-selected body, or add to it?).
        /// </summary>
        internal static IMassProperty NewMassProperty(
            IModelDoc2 document, IEnumerable<object> bodies, out bool bodiesAttached)
        {
            bodiesAttached = false;
            var massProperty = document.Extension.CreateMassProperty() as IMassProperty;
            if (massProperty == null) return null;
            // Explicit, not a habit: without it values come in document units,
            // which on someone else's part can be inches and pounds.
            massProperty.UseSystemUnits = true;
            var list = bodies == null ? new object[0] : bodies.ToArray();
            bodiesAttached = list.Length == 0 || massProperty.AddBodies(list);
            return massProperty;
        }

        // =====================================================================
        //  S3 — IS A FEATURE SUPPRESSED (Z-C21.7)
        // =====================================================================

        /// <summary>
        /// Whether <paramref name="feature"/> is suppressed in the configuration
        /// that is active.
        ///
        /// Guide: IFeature~IsSuppressed.html, «Obsolete. Superseded by
        /// IFeature::IsSuppressed2»; IFeature~IsSuppressed2.html, «Return Value
        /// Array of Booleans indicating the suppression states for the feature
        /// in the specified configurations».
        ///
        /// MEASURED by the Z-C21.3 probe probe_is_suppressed2 (SOLIDWORKS
        /// 29.5.1, 28/9/2026) on a part with four configurations and a cut
        /// suppressed in one of them only: IsSuppressed2(swThisConfiguration,
        /// null) answers a Boolean[1] equal to the obsolete IsSuppressed() with
        /// either configuration active ({False} with the cut active, {True}
        /// with the configuration that suppresses it). With swAllConfiguration
        /// and null names it answers one Boolean per configuration, in the
        /// order of GetConfigurationNames; that form is not used here.
        ///
        /// Throws when the answer has another shape: a plausible false from a
        /// wrong cast is worse than the exception every call site already
        /// catches (the lesson of the wrong but plausible number, S1).
        /// </summary>
        internal static bool IsSuppressedHere(IFeature feature)
        {
            object raw = feature.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration, null);
            var states = raw as bool[];
            if (states == null && raw is object[] boxed && boxed.Length == 1 && boxed[0] is bool one)
                states = new[] { one };
            if (states == null || states.Length != 1)
                throw new InvalidOperationException(
                    "IFeature.IsSuppressed2(swThisConfiguration, null) did not answer one Boolean");
            return states[0];
        }

        // =====================================================================
        //  S11 — MODIFYING A FEATURE DEFINITION
        // =====================================================================

        /// <summary>What ModifyDefinition answered, and what the feature says afterwards.</summary>
        internal sealed class DefinitionChange
        {
            /// <summary>The boolean ModifyDefinition returned.</summary>
            public bool Applied;
            /// <summary>IFeature.GetErrorCode2 after the change (0 = none, or not known).</summary>
            public int ErrorCode;
            public bool IsWarning;
            /// <summary>
            /// False when GetErrorCode2 threw: ErrorCode is then 0 because
            /// nothing was read, not because the feature is sound.
            /// </summary>
            public bool ErrorCodeKnown;
            /// <summary>A real rebuild error, not a warning.</summary>
            public bool InError => ErrorCode != (int)swFeatureError_e.swFeatureErrorNone && !IsWarning;
            /// <summary>The phrase for the error, or null if there is none.</summary>
            public string ErrorPhrase => InError ? ErroriFeature.Frase(ErrorCode) : null;
        }

        /// <summary>
        /// ModifyDefinition followed by GetErrorCode2, always together.
        ///
        /// Guide: IFeature~ModifyDefinition.html, Remarks: «For some feature
        /// modifications, this method applies the new properties, returns true,
        /// but results in a new rebuild error on the feature. ... After calling
        /// this method, call IFeature::GetErrorCode2 to ascertain whether the
        /// feature has rebuild errors.» The boolean alone is not the verdict.
        /// Guide: IFeature~GetErrorCode2.html, «IsWarning ... should only be
        /// examined if the return value is non-zero».
        ///
        /// The caller still decides what to do with each answer (restore,
        /// refuse, report): this helper only makes sure the second question is
        /// always asked. An error code that cannot be read is left at zero and
        /// does not become an error, but <see cref="DefinitionChange.ErrorCodeKnown"/>
        /// says so: a caller that has no other check (volume, faces) must not
        /// read "not known" as "sound" (the project measured that GetErrorCode2
        /// alone can stay silent on a real defect, see CopieMancanti in
        /// OperazioniSw).
        /// </summary>
        internal static DefinitionChange ModifyAndVerify(
            IFeature feature, object definition, IModelDoc2 topDocument, object component = null)
        {
            var change = new DefinitionChange
            {
                Applied = feature.ModifyDefinition(definition, topDocument, component),
            };
            change.ErrorCodeKnown = ReadErrorCode(feature, out change.ErrorCode, out change.IsWarning);
            return change;
        }

        /// <summary>
        /// The phrase for a real rebuild error of the feature, or null if it is
        /// sound, if the code is only a warning, or if it cannot be read.
        /// Guide: IFeature~GetErrorCode2.html (see <see cref="ModifyAndVerify"/>).
        /// </summary>
        internal static string RebuildError(IFeature feature)
        {
            return RebuildError(feature, out _);
        }

        /// <summary>
        /// As <see cref="RebuildError(IFeature)"/>; <paramref name="known"/> is
        /// false when GetErrorCode2 threw, so that a null phrase can be told
        /// apart from "I could not ask".
        /// </summary>
        internal static string RebuildError(IFeature feature, out bool known)
        {
            known = ReadErrorCode(feature, out int code, out bool warning);
            return code != (int)swFeatureError_e.swFeatureErrorNone && !warning
                ? ErroriFeature.Frase(code) : null;
        }

        /// <summary>GetErrorCode2; false (and code 0) when it throws.</summary>
        private static bool ReadErrorCode(IFeature feature, out int code, out bool warning)
        {
            code = (int)swFeatureError_e.swFeatureErrorNone;
            warning = false;
            try { code = feature.GetErrorCode2(out warning); return true; }
            catch { code = (int)swFeatureError_e.swFeatureErrorNone; warning = false; return false; }
        }

        // =====================================================================
        //  S10 — CREATEDEFINITION -> CREATEFEATURE
        // =====================================================================

        /// <summary>Where a feature creation through a definition stopped.</summary>
        internal enum CreationStage { Selection, Definition, Fill, Creation, Done }

        /// <summary>The outcome of <see cref="CreateFeatureFromDefinition{TData}"/>.</summary>
        internal sealed class FeatureCreation
        {
            public CreationStage Stage;
            /// <summary>The new feature, when Stage is Done.</summary>
            public IFeature Feature;
            public int ErrorCode;
            public bool IsWarning;
            /// <summary>False when GetErrorCode2 threw (see <see cref="DefinitionChange.ErrorCodeKnown"/>).</summary>
            public bool ErrorCodeKnown;
            public bool InError => ErrorCode != (int)swFeatureError_e.swFeatureErrorNone && !IsWarning;
            public string ErrorPhrase => InError ? ErroriFeature.Frase(ErrorCode) : null;
        }

        /// <summary>
        /// The recipe that replaces the obsolete Insert*/Feature* methods:
        /// selection first, then CreateDefinition, fill the data object,
        /// CreateFeature, and GetErrorCode2 on what was born.
        ///
        /// Guide: IFeatureManager~CreateDefinition.html and
        /// IFeatureManager~CreateFeature.html; the obsolete pages point there
        /// (IFeatureManager~FeatureLinearPattern5.html, «Obsolete. See
        /// IFeatureManager::CreateFeature and the Remarks in
        /// ILinearPatternFeatureData»; IFeatureManager~FeatureFillet3.html,
        /// «use IFeatureManager::CreateDefinition, IFeatureManager::CreateFeature»).
        /// The selection goes BEFORE CreateDefinition: measured on the face
        /// fillet (RAG note 82zz-quinquies), which is why it is a parameter and
        /// not something the caller does "somewhere before".
        ///
        /// The migrations of Z-C21.7 (FeatureFillet3, LinearPattern5,
        /// CircularPattern5, Swept4, EdgeFlange2) go through here, one per
        /// commit. Users: the mirror of components and, since Z-C21.7 (L10-1),
        /// the edge flange (no selection: the edge goes in through AddEdges).
        /// </summary>
        internal static FeatureCreation CreateFeatureFromDefinition<TData>(
            IFeatureManager manager, int featureNameId, Func<TData, bool> fill,
            Func<bool> selectBeforeDefinition = null) where TData : class
        {
            var outcome = new FeatureCreation { Stage = CreationStage.Selection };
            if (selectBeforeDefinition != null && !selectBeforeDefinition()) return outcome;
            outcome.Stage = CreationStage.Definition;
            var data = manager.CreateDefinition(featureNameId) as TData;
            if (data == null) return outcome;
            outcome.Stage = CreationStage.Fill;
            if (fill != null && !fill(data)) return outcome;
            outcome.Stage = CreationStage.Creation;
            outcome.Feature = manager.CreateFeature(data) as IFeature;
            if (outcome.Feature == null) return outcome;
            outcome.Stage = CreationStage.Done;
            outcome.ErrorCodeKnown = ReadErrorCode(outcome.Feature, out outcome.ErrorCode, out outcome.IsWarning);
            return outcome;
        }

        // =====================================================================
        //  L8-1 / L2-4 — AN OFFSET REFERENCE PLANE (Z-C21.7)
        // =====================================================================

        /// <summary>
        /// Selects <paramref name="basePlane"/> as the FIRST reference of
        /// <see cref="InsertOffsetPlane"/>: by name, type "PLANE", mark
        /// <see cref="SelectionMarks.OffsetPlaneReference"/> (0), and through
        /// IFeature.Select2 with the same mark when the name does not select.
        /// Clears the selection first. False if nothing was selected.
        ///
        /// Guide: IFeatureManager~InsertRefPlane.html, Remarks: «Before calling
        /// this method, you must have selected the reference entities using
        /// these marks with IModelDocExtension::SelectByID2: 0 = First reference
        /// entity». The name is read from the feature, never typed: the names
        /// of the base planes change with the language of the installation.
        /// </summary>
        internal static bool SelectOffsetPlaneBase(IModelDoc2 document, IFeature basePlane)
        {
            document.ClearSelection2(true);
            string name = null;
            try { name = basePlane.Name; } catch { }
            bool selected = false;
            if (!string.IsNullOrEmpty(name))
            {
                try
                {
                    selected = document.Extension.SelectByID2(
                        name, "PLANE", 0, 0, 0, false, SelectionMarks.OffsetPlaneReference, null, 0);
                }
                catch { }
            }
            return selected || basePlane.Select2(false, SelectionMarks.OffsetPlaneReference);
        }

        /// <summary>
        /// A reference plane parallel to the plane selected with
        /// <see cref="SelectOffsetPlaneBase"/>, at <paramref name="distanceMm"/>
        /// (negative = the other side). Returns what InsertRefPlane returns (a
        /// RefPlane, which the probe also found to be an IFeature), or null. The
        /// caller still counts the planes of the tree: the tree decides.
        ///
        /// Guide: IModelDoc2~CreatePlaneAtOffset3.html, «Obsolete. Superseded by
        /// IFeatureManager::InsertRefPlane»; IFeatureManager~InsertRefPlane.html,
        /// «FirstConstraint First constraint as defined in
        /// swRefPlaneReferenceConstraints_e», where Distance = 8 and
        /// OptionFlip = 256 (swconst).
        ///
        /// MEASURED by the InsertRefPlane probe of Z-C21.3 (findings L8-1 and
        /// L2-4, SOLIDWORKS 29.5.1, 28/9/2026), the base plane selected with
        /// mark 0:
        ///   G1 (8, 0.100) on the top plane: one new plane, origin (0, 100, 0);
        ///   G4 (8|256, 0.100) on the top plane: (0, -100, 0);
        ///   G6 (8|256, 0.250) on the front plane: (0, 0, -250);
        ///   G5 (8, -0.100), a SIGNED distance: a plane at (0, 0, 0), wrong and
        ///      with no error. The side is the OptionFlip bit, never the sign,
        ///      so the distance passed is always the absolute value;
        ///   H1 mark 1 + (8, 0.100): no plane at all. That was the measure of
        ///      10/9/2026 that ruled InsertRefPlane out for eighteen days: the
        ///      mark was wrong, not the method;
        ///   L2 the loft between two circles 60 mm apart, second circle on a
        ///      plane made this way: 43980.736 mm3, the same as the loft on a
        ///      CreatePlaneAtOffset3 plane (L1, 43980.736 mm3).
        /// </summary>
        internal static object InsertOffsetPlane(IModelDoc2 document, double distanceMm)
        {
            int constraint = (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance;
            if (distanceMm < 0)
                constraint |= (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_OptionFlip;
            return document.FeatureManager.InsertRefPlane(constraint, Unita.M(Math.Abs(distanceMm)), 0, 0, 0, 0);
        }
    }
}
