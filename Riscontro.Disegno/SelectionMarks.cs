namespace Riscontro.Disegno
{
    /// <summary>
    /// THE SELECTION MARKS, ONE TABLE FOR EVERY API THAT READS THEM (Z-C21.6,
    /// pattern S9).
    ///
    /// A feature method that works on preselected entities does not read the
    /// selection: it reads the selection WITH THE RIGHT MARK. With the wrong
    /// mark the selection is there, the feature does not see it, and nothing
    /// says so (InsertMirrorFeature2 until 0.35.0 had the pattern's 1 and 4 and
    /// the mirror was simply not created). The marks lived as bare numbers at
    /// the call sites, each one checked, or not, against its own page.
    ///
    /// THE RULES OF THIS TABLE, checked by Prove.Contratti (SelectionMarksContract):
    ///   - every constant cites the guide page it comes from ("Guide:") and
    ///     quotes, between « », the words of the page that give that number;
    ///   - a mark the guide does not document is allowed only as UNDOCUMENTED,
    ///     with the bank case that measured it ("MEASURED:");
    ///   - where the code does not do what the page says, the constant is
    ///     marked DIVERGES with the audit finding: the change is a probe
    ///     (Z-C21.3) or a migration (Z-C21.7), not something to slip in with a
    ///     refactor. If the code uses a different non-zero mark, the constant is
    ///     the value the code USES; if the code selects with NO mark (the
    ///     `Select4(true, null)` of pattern S9), the constant is the value of
    ///     the page, "Not used by the code yet", so that the mark to try is
    ///     written next to its quote;
    ///   - outside this file no SOLIDWORKS selection call carries a non-zero
    ///     literal mark.
    ///
    /// Mark 0 is "no mark" for most methods, and stays a literal 0 at the call
    /// sites.
    /// </summary>
    internal static class SelectionMarks
    {
        // ── IFeatureManager.InsertMoveCopyBody2 ─────────────────────────────

        /// <summary>The bodies to move or copy.</summary>
        /// Guide: IFeatureManager~InsertMoveCopyBody2.html, «Selection Mark Body or surface 1».
        public const int MoveCopyBodyBody = 1;

        // ── IFeatureManager.InsertMirrorFeature2 ────────────────────────────

        /// <summary>The features to mirror.</summary>
        /// Guide: IFeatureManager~InsertMirrorFeature2.html, «Features to be mirrored 1».
        public const int MirrorFeatureFeatures = 1;

        /// <summary>The mirror plane or planar face.</summary>
        /// Guide: IFeatureManager~InsertMirrorFeature2.html, «Plane or planar face 2».
        public const int MirrorFeaturePlane = 2;

        // ── IFeatureManager.FeatureLinearPattern5 ───────────────────────────

        /// <summary>Direction 1 of a linear pattern.</summary>
        /// Guide: IFeatureManager~FeatureLinearPattern5.html, «1 to mark Direction 1».
        /// DIVERGES: L7-5 — the table pairs IFeature::Select2 only with marks 4,
        /// 2097152 and 8388608; the axis branch selects an axis FEATURE with
        /// Select2 and this mark. No bank case uses an axis here (all use an
        /// edge); measure before relying on it.
        public const int LinearPatternDirection1 = 1;

        /// <summary>Direction 2 of a linear pattern (a grid).</summary>
        /// Guide: IFeatureManager~FeatureLinearPattern5.html, «2 to mark Direction 2».
        /// DIVERGES: L7-5, as for Direction 1.
        public const int LinearPatternDirection2 = 2;

        /// <summary>The features to pattern.</summary>
        /// Guide: IFeatureManager~FeatureLinearPattern5.html, «IFeature::Select2 with Mark= 4 to mark the features to pattern».
        public const int LinearPatternFeatures = 4;

        // ── IFeatureManager.FeatureCircularPattern5 ─────────────────────────

        /// <summary>The axis of a circular pattern.</summary>
        /// Guide: IFeatureManager~FeatureCircularPattern5.html, «Directly selecting a feature or axis using IFeature::Select2 1 to mark the direction axis».
        public const int CircularPatternAxis = 1;

        /// <summary>The features to pattern.</summary>
        /// Guide: IFeatureManager~FeatureCircularPattern5.html, «4 to mark the features to pattern».
        public const int CircularPatternFeatures = 4;

        // ── IFeatureManager.InsertProtrusionSwept4 (and the new sweep) ──────

        /// <summary>The sketch profile of a sweep.</summary>
        /// Guide: IFeatureManager~InsertProtrusionSwept4.html, «Specify these marks: 1 = Sketch profile».
        public const int SweepProfile = 1;

        /// <summary>The path of a sweep.</summary>
        /// Guide: IFeatureManager~InsertProtrusionSwept4.html, «4 = Sweep path».
        public const int SweepPath = 4;

        // ── IFeatureManager.InsertProtrusionBlend2 (loft) ───────────────────

        /// <summary>The profiles of a loft, in the order of the loft.</summary>
        /// Guide: IFeatureManager~InsertProtrusionBlend2.html, «Set the mark for: 1 = profile selections».
        public const int LoftProfile = 1;

        // ── IView.InsertHoleTable2 ──────────────────────────────────────────

        /// <summary>The datum origin vertex of a hole table.</summary>
        /// Guide: IView~InsertHoleTable2.html, «Selection Mark Datum origin vertex 1».
        public const int HoleTableDatumOrigin = 1;

        /// <summary>The hole edges and faces of a hole table.</summary>
        /// Guide: IView~InsertHoleTable2.html, «Hole edges and faces 2».
        public const int HoleTableHoles = 2;

        // ── IModelDoc2.InsertFeatureShell ───────────────────────────────────

        /// <summary>The faces a shell removes.</summary>
        /// Guide: IModelDoc2~InsertFeatureShell.html, «select faces to remove ... with Marks of 1».
        /// DIVERGES: L4-4 — the shell selects with no mark (Select4(true, null),
        /// OperazioniSw.cs), measured working with one face
        /// (e15_guscio_da_riferimento). Not used by the code yet: moving the
        /// shell to this mark is Z-C21.7, behind the shell cases.
        public const int ShellFacesToRemove = 1;

        // ── IFeatureManager.FeatureRevolve2 ─────────────────────────────────

        /// <summary>The axis of revolution.</summary>
        /// Guide: IFeatureManager~FeatureRevolve2.html, «the axis of revolution, using Mark = 4 or 16».
        /// DIVERGES: L2-1 — the code selects only the sketch and relies on
        /// UseAutoSelect; five revolve cases pass that way. Not used yet.
        public const int RevolveAxis = 4;

        // ── IFeatureManager.InsertRefPlane (crea_piano, loft, rib plane) ────

        /// <summary>The reference plane an offset plane is measured from.</summary>
        /// Guide: IFeatureManager~InsertRefPlane.html, Remarks, «0 = First reference entity».
        /// Here 0 is a real mark, not "no mark": it has a name so that the
        /// three call sites (ApiHelpers.SelectOffsetPlaneBase) cannot drift.
        /// Measured by the InsertRefPlane probe of Z-C21.3 (28/9/2026): with
        /// mark 0 InsertRefPlane makes the plane where it
        /// belongs (G1 top +100 at (0, 100, 0), G4 top -100 at (0, -100, 0),
        /// G6 front -250 at (0, 0, -250)); with mark 1 (H1, the value this
        /// constant had until Z-C21.7, taken over from CreatePlaneAtOffset3)
        /// it makes no plane at all, which is what ruled it out on 10/9/2026.
        public const int OffsetPlaneReference = 0;

        // ── IFeatureManager.InsertMultiFaceDraft (sformo) ───────────────────

        /// <summary>The neutral plane or face of a draft.</summary>
        /// Guide: IFeatureManager~InsertMultiFaceDraft.html, «then these items have a selection mark of 1».
        /// The marks are given inside the description of the FlipDir parameter,
        /// not in the Remarks (which only say to select with SelectByID2).
        /// Passed through OperazioniSformo.Seleziona; swapped with the faces'
        /// mark the feature is still created and drafts the wrong faces
        /// (sformo_quattro_pareti).
        public const int DraftNeutralFace = 1;

        /// <summary>The faces to draft.</summary>
        /// Guide: IFeatureManager~InsertMultiFaceDraft.html, «the faces to draft have a selection mark of 2».
        public const int DraftFaces = 2;

        // ── IFeatureManager.InsertGussetFeature3 (fazzoletto) ───────────────

        /// <summary>The two faces that carry a weldment gusset.</summary>
        /// Guide: IFeatureManager~InsertGussetFeature3.html, «select two faces that are the supporting legs of this gusset with a Mark of 1».
        /// Used by fazzoletto (OperazioniSaldatura, 0.140.0). Until then the two
        /// faces were selected with no mark and the gusset was never created
        /// (GP20, M27): the probe of 28/9/2026 showed that the cause was the
        /// degenerate profile, and that with a valid profile the recipe of
        /// Insert_Gusset_Feature_Example (this mark) makes it, +9000 mm³
        /// (il_fazzoletto_sta_nell_angolo).
        public const int GussetSupportFaces = 1;

        // ── IFeatureManager.FeatureFillet3, variable radius ─────────────────

        /// <summary>The edges of a variable-radius fillet.</summary>
        /// Guide: IFeatureManager~FeatureFillet3.html, «Variable radius fillets Call IModelDocExtension::SelectByID2 with Mark = 1 to select the edges to fillet».
        /// DIVERGES: S9/M37 — raccordo_variabile (OperazioniCorpi) selects the
        /// edge and its two vertices with no mark, measured working
        /// (raccordo_che_cambia_raggio).
        /// Moving to this mark belongs with the CreateDefinition migration of
        /// FeatureFillet3 (Z-C21.7). Not used by the code yet.
        public const int VariableFilletEdges = 1;

        // ── IFeatureManager.InsertCosmeticWeldBead2 (cordone) ───────────────

        /// <summary>Each weld-from face or edge of a cosmetic weld bead (weld geometry mode).</summary>
        /// Guide: IFeatureManager~InsertCosmeticWeldBead2.html, «4 for each weld-from entity in WeldFromFaceOrEdgeSet».
        /// DIVERGES: L9-3 — cordone_saldatura calls the OBSOLETE
        /// InsertCosmeticWeldBead with both faces selected with no mark, which is
        /// what that method's own example does (Insert_Cosmetic_Weld_Bead_Example:
        /// SelectByID2(..., false, 0, ...) then (..., true, 0, ...)). The marks 4
        /// and 8 belong to InsertCosmeticWeldBead2, which returned null in every
        /// combination tried on 25/9/2026, never with these marks: that is the
        /// remeasure before Z-C21.7 migrates it. Not used by the code yet.
        public const int WeldBeadFrom = 4;

        /// <summary>Each weld-to face or edge of a cosmetic weld bead (weld geometry mode).</summary>
        /// Guide: IFeatureManager~InsertCosmeticWeldBead2.html, «8 for each weld-to entity in WeldToFaceOrEdgeSet».
        /// DIVERGES: L9-3, as <see cref="WeldBeadFrom"/>.
        public const int WeldBeadTo = 8;

        // ── ISimpleFilletFeatureData2, face fillet (raccordo_faccia) ────────

        /// <summary>The first face set of a face fillet.</summary>
        /// Guide: ISimpleFilletFeatureData2.html, Remarks, «Face fillet/face-face chamfer Call IModelDocExtension::SelectByID2 with: - Mark = 2 to select Face Set 1».
        /// Not the values of swSimpleFilletWhichFaces_e (1 and 2), which belong
        /// to SetFaces: as marks they give a null feature (measured 28/9/2026).
        public const int FaceFilletFaceSet1 = 2;

        /// <summary>The second face set of a face fillet.</summary>
        /// Guide: ISimpleFilletFeatureData2.html, Remarks, «Mark = 4 to select Face Set 2».
        public const int FaceFilletFaceSet2 = 4;

        // ── IFillPatternFeatureData (ripeti_riempimento) ────────────────────

        /// <summary>The direction reference (an edge or an axis) of a fill pattern.</summary>
        /// Guide: IFillPatternFeatureData.html, Remarks, «Direction reference (e.g., edge or axis) Pattern direction (if not selected, SOLIDWORKS chooses a direction reference appropriate for the specified layout) 1».
        /// The page calls it optional; measured on 28/9/2026 it is not: without
        /// it CreateFeature returns null (lim_riempimento_senza_direzione).
        public const int FillPatternDirection = 1;

        /// <summary>The features a fill pattern repeats.</summary>
        /// Guide: IFillPatternFeatureData.html, Remarks, «Features Pattern objects 4».
        public const int FillPatternFeatures = 4;

        /// <summary>The boundary (face, sketch or coplanar faces) a fill pattern fills.</summary>
        /// Guide: IFillPatternFeatureData.html, Remarks, «Sketch, face, planar curve on a face, or coplanar faces Fill boundary 16384».
        public const int FillPatternBoundary = 16384;

        // ── IModelDoc2.InsertAxis2 ──────────────────────────────────────────

        /// <summary>The cylindrical or conical face an axis is made from.</summary>
        /// Guide: IModelDoc2~InsertAxis2.html, which says nothing about marks.
        /// UNDOCUMENTED: selected with IEntity.SelectByMark(false, 1), a member
        /// that has no page in the extracted guide (hole C of the audit).
        /// MEASURED: asse_dalla_parete_del_foro
        public const int AxisFace = 1;
    }
}
