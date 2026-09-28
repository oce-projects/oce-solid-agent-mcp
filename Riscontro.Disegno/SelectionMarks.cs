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
    ///   - where the code does not do what the page says, the constant is the
    ///     value the code USES, marked DIVERGES with the audit finding: the
    ///     change is a probe (Z-C21.3) or a migration (Z-C21.7), not something
    ///     to slip in with a refactor;
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

        // ── IModelDoc2.CreatePlaneAtOffset3 (crea_piano, loft) ──────────────

        /// <summary>The reference plane an offset plane is measured from.</summary>
        /// Guide: IModelDoc2~CreatePlaneAtOffset3.html, which says nothing about marks.
        /// UNDOCUMENTED: the plane is selected with mark 1; measured on
        /// 10/9/2026 (one selected object, mark 1, type 4 = swSelDATUMPLANES).
        /// MEASURED: piano_superiore_piu_100
        /// DIVERGES: L8-1 — IFeatureManager~InsertRefPlane.html wants
        /// «0 = First reference entity»; the measure that ruled InsertRefPlane
        /// out was made with this mark 1 (probe of Z-C21.3).
        public const int OffsetPlaneReference = 1;

        // ── IModelDoc2.InsertAxis2 ──────────────────────────────────────────

        /// <summary>The cylindrical or conical face an axis is made from.</summary>
        /// Guide: IModelDoc2~InsertAxis2.html, which says nothing about marks.
        /// UNDOCUMENTED: selected with IEntity.SelectByMark(false, 1), a member
        /// that has no page in the extracted guide (hole C of the audit).
        /// MEASURED: asse_dalla_parete_del_foro
        public const int AxisFace = 1;
    }
}
