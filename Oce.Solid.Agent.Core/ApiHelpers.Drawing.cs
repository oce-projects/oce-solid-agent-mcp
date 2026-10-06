using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// The drawing half of <see cref="ApiHelpers"/>: the views of every sheet
    /// and the model each one shows (finding N43, pattern "drawing state read
    /// from the current sheet only"), and the dimensions a drawing does not
    /// print (finding C8-2, pattern "dimensions listed without a visibility
    /// filter"). See ApiHelpers.cs for the rules every helper follows: each one
    /// quotes its page, and each one throws what the API throws.
    ///
    /// First user: DrawingDimensionOperations.KeysAlreadyDimensioned ("already dimensioned
    /// in this drawing"). The other walks named by the audit
    /// (DimensionTextOperations, PdfDimensionOperations, HoleTableOperations,
    /// DrawingState: C8-3 and N1, lot Z-C21.3) are meant to move here, one at a
    /// time behind their own bench case.
    /// </summary>
    internal static partial class ApiHelpers
    {
        // =====================================================================
        //  THE VIEWS OF EVERY SHEET (N43)
        // =====================================================================

        /// <summary>
        /// The drawing views of every sheet of the drawing, the sheets
        /// themselves excluded; null when the drawing does not hand them over.
        ///
        /// Guide: IDrawingDoc~GetFirstView.html, «Gets the first drawing view on
        /// the current sheet.» That is the walk (GetFirstView, then GetNextView)
        /// every call site used, so a sheet that is not in front was never read.
        /// IDrawingDoc~GetViews.html, Remarks: «The return value is an array of
        /// arrays with a length equal to the number of sheets in the drawing
        /// document. Each of those arrays is a list of views with the first view
        /// in the list being the sheet itself.» The same Remarks say «the order
        /// in which the sheets are returned is undetermined»: the list is for
        /// sets and counts, never for "the first view".
        ///
        /// NULL, NOT AN EMPTY LIST, when GetViews gives nothing usable or one of
        /// the sheets comes without its array: a drawing always has a sheet, so
        /// an empty answer is a reading that failed, and "every sheet" would be a
        /// false claim. The caller falls back to the current sheet and says so.
        /// (Not measured: what GetViews gives for a sheet the user chose not to
        /// load when opening the drawing.)
        /// </summary>
        internal static List<IView> ViewsOfEverySheet(IDrawingDoc drawing)
        {
            if (drawing == null) return null;
            var perSheet = drawing.GetViews() as object[];
            if (perSheet == null || perSheet.Length == 0) return null;
            var views = new List<IView>();
            foreach (var sheet in perSheet)
            {
                var list = sheet as object[];
                if (list == null || list.Length == 0) return null;
                for (int i = 1; i < list.Length; i++)   // [0] is the sheet itself
                    if (list[i] is IView view) views.Add(view);
            }
            return views;
        }

        /// <summary>
        /// The model and configuration a drawing view shows, as one key
        /// ("path|configuration", lower case), or null when the view does not
        /// say.
        ///
        /// Guide: IView~GetReferencedModelName.html, «Gets the name of the model
        /// that is referenced in the drawing view.»; IView~ReferencedConfiguration.html,
        /// «Gets or sets the configuration referenced by this drawing view.»
        /// IView~ReferencedDocument.html, Remarks: «Because section views do not
        /// have referenced documents, this method returns an empty string for
        /// this type of view. Instead, use IView::GetBaseView or
        /// IView::IGetBaseView to get the parent view of a section, and then get
        /// its referenced document from that view.» So an empty name is asked of
        /// the base view (IView~GetBaseView.html, «Gets the base (parent) view
        /// used to create this view.»), a few levels at most.
        /// </summary>
        internal static string ReferencedModelKey(IView view)
        {
            for (int level = 0; view != null && level < 8; level++)
            {
                string name = view.GetReferencedModelName();
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim().ToLowerInvariant() + "|" +
                           (view.ReferencedConfiguration ?? "").Trim().ToLowerInvariant();
                view = view.GetBaseView() as IView;
            }
            return null;
        }

        /// <summary>
        /// Two keys of <see cref="ReferencedModelKey"/> may belong to the same
        /// model and configuration: equal, or one of them unknown. An unknown
        /// view counts as the same model, because every view of the current
        /// sheet counted before this check existed; only two known and
        /// different keys are told apart.
        /// </summary>
        internal static bool MayShowTheSameModel(string keyA, string keyB)
        {
            if (string.IsNullOrEmpty(keyA) || string.IsNullOrEmpty(keyB)) return true;
            return string.Equals(keyA, keyB, StringComparison.Ordinal);
        }

        // =====================================================================
        //  THE DIMENSIONS A DRAWING DOES NOT PRINT (C8-2)
        // =====================================================================

        /// <summary>
        /// True when the display dimension was hidden by hand (its annotation is
        /// hidden or half-hidden); false when it is shown or its state is not
        /// known.
        ///
        /// Guide: IView~GetFirstDisplayDimension5.html, Remarks: «This method
        /// displays: dimensions on both the sheet and sheet format. suppressed
        /// dimensions.» and «Do not use IAnnotation::Visible property to modify
        /// this method's return value.» — the walk hands over what the sheet
        /// does not print as well, and the filter is the caller's (here it only
        /// READS Visible). IAnnotation~Visible.html, Remarks: «This property
        /// determines whether an annotation has been individually displayed or
        /// hidden if you selected and specifically hid the annotation».
        /// swAnnotationVisibilityState_e: «swAnnotationHidden 3 = Annotation is
        /// hidden», and a half-hidden annotation «is hidden when the interactive
        /// user finishes using Hide/Show Annotations».
        ///
        /// WHAT IT DOES NOT SEE, from the same Remarks: «This property cannot
        /// determine whether an annotation is hidden if it is on a layer that is
        /// hidden (all annotations of this type are hidden) or if the feature
        /// that owns it is suppressed.» Those two need a probe (TODO,
        /// Z-C21.10-outcome-2, C8-2) and are not guessed here: such a dimension
        /// still counts as printed, as every dimension did before.
        /// </summary>
        internal static bool IsHiddenByHand(IDisplayDimension dimension)
        {
            if (dimension == null) return false;
            var annotation = dimension.GetAnnotation() as IAnnotation;
            return annotation != null && IsHiddenVisibility(annotation.Visible);
        }

        /// <summary>
        /// The rule of <see cref="IsHiddenByHand"/> on the raw value of
        /// IAnnotation.Visible (swAnnotationVisibilityState_e), without
        /// SOLIDWORKS: hidden (3) and half-hidden (2) are not printed; visible
        /// (1) and unknown (0) are.
        /// </summary>
        internal static bool IsHiddenVisibility(int visibility)
        {
            return visibility == (int)swAnnotationVisibilityState_e.swAnnotationHidden
                || visibility == (int)swAnnotationVisibilityState_e.swAnnotationHalfHidden;
        }
    }
}
