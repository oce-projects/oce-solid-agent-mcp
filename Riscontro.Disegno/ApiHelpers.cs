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
        /// The value of a dimension in system units (metres, radians), in the
        /// configuration that is active.
        ///
        /// Guide: IDimension~SystemValue.html, «Obsolete. Superseded by
        /// IDimension::GetSystemValue3». The replacement is NOT used yet:
        /// IDimension~GetSystemValue3.html declares `object GetSystemValue3(int
        /// WhichConfigurations, object Config_names)`, and the shape of that
        /// object (an array with one value per configuration?) has not been
        /// measured (probe of Z-C21.3). A wrong cast there gives a plausible
        /// wrong number, the worst kind of defect (memory "numero sbagliato e
        /// plausibile"). After the probe this becomes
        /// `((double[])d.GetSystemValue3(swThisConfiguration, null))[0]`,
        /// compared once against SystemValue on the same part.
        /// </summary>
        internal static double ReadDimension(IDimension dimension)
        {
            return dimension.SystemValue;
        }

        /// <summary>
        /// The value of a dimension in system units in a named configuration;
        /// every call site today passes "" (the call it made before Z-C21.6).
        ///
        /// Guide: IDimension~GetSystemValue2.html, «ConfigName Name of
        /// configuration Obsolete. See IDimension::GetSystemValue3». Kept
        /// separate from <see cref="ReadDimension"/> on purpose: the two
        /// obsolete calls are not proven to answer the same thing (the page does
        /// not say which configuration "" means, and in a drawing the model's
        /// active configuration can differ from the one the view shows), and
        /// merging them without the bank would change eleven readings at once.
        /// Both collapse into GetSystemValue3 after the Z-C21.3 probe.
        /// </summary>
        internal static double ReadDimensionInConfiguration(IDimension dimension, string configurationName)
        {
            return dimension.GetSystemValue2(configurationName ?? "");
        }

        // =====================================================================
        //  S2 — MASS PROPERTIES
        // =====================================================================

        /// <summary>
        /// A NEW mass-property object, in system units, with the given bodies
        /// attached (none = every body of the document). Null if SOLIDWORKS does
        /// not give one. <paramref name="bodiesAttached"/> is false when
        /// AddBodies refused: the numbers would then be of the whole document,
        /// not of the bodies asked for.
        ///
        /// One object per reading, as the callers do: IMassProperty changes its
        /// answers depending on what was read before (measured, see
        /// OperazioniMisura).
        ///
        /// Guide: IModelDocExtension~CreateMassProperty.html, «Obsolete.
        /// Superseded by IModelDocExtension::CreateMassProperty2»; its Remarks:
        /// "If IMassProperty::AddBodies ... Not called: mass properties'
        /// calculations include all available bodies in the document".
        /// Still the obsolete one: IModelDocExtension~CreateMassProperty2.html
        /// works by SELECTION ("call IModelDocExtension::SelectByID2 to pre-select
        /// the bodies ... IMassProperty2::SelectedItems"), not by AddBodies, and
        /// that has not been measured (probe of Z-C21.3; the project has met
        /// selection traps many times). After the probe only this method
        /// changes.
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
        /// commit. Today the only user is the mirror of components.
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
