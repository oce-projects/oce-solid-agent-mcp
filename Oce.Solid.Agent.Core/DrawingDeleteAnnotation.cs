using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// TAKES AWAY A DIMENSION OR A NOTE OF A DRAWING (report 8cd843 N24, round
    /// 7, 27/10/2026).
    ///
    /// Three dimensions put on a copy of the clip drawing could not be taken away:
    /// «undo» had no plan to withdraw (a plan on a drawing records no undo entry
    /// it can take back), and its remedy, delete_feature or suppress_feature,
    /// does not touch an annotation. They stayed on the drawing. The plan that
    /// places a dimension already gives its name («RD1@Drawing View1@...»), and
    /// the one that places a note its text and now its name: this takes one away
    /// by that name, or a note by its exact text.
    ///
    /// ONLY THE CURRENT SHEET: an annotation of another sheet cannot be selected
    /// without bringing that sheet in front, and doing it here would change what
    /// the person sees. The refusal says to activate it.
    ///
    /// GONE MEANS NOT FOUND ANY MORE: after DeleteSelection2 (not EditDelete,
    /// which is forbidden here: it can open a confirmation window) the sheet is
    /// walked again, and «removed» is said only when no annotation answers to the
    /// name or the text, and the count went down by one.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// Does the full name of a dimension answer to the name asked? The full
        /// name itself, or what comes before an «@» of it: «RD1» and
        /// «RD1@Drawing View1» both answer for «RD1@Drawing View1@Part.Drawing».
        /// Pure, so the contract suite reads it.
        /// </summary>
        internal static bool DimensionNameMatches(string fullName, string asked)
        {
            if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(asked)) return false;
            if (string.Equals(fullName, asked, StringComparison.Ordinal)) return true;
            return fullName.StartsWith(asked + "@", StringComparison.Ordinal);
        }

        private sealed class AnnotationFound
        {
            public string Kind;      // "dimension" or "note"
            public string Name;
            public string Text;
            public string View;
            public IAnnotation Annotation;
        }

        /// <summary>
        /// The annotations of the current sheet (its views and the sheet itself)
        /// that answer to <paramref name="name"/> or, for a note, to its exact
        /// <paramref name="text"/>. Null when the walk broke off.
        /// </summary>
        private static List<AnnotationFound> FindAnnotations(IDrawingDoc drawing, string name, string text)
        {
            var found = new List<AnnotationFound>();
            try
            {
                for (var v = drawing.GetFirstView() as IView; v != null; v = v.GetNextView() as IView)
                {
                    string viewName = "";
                    try { viewName = v.GetName2() ?? ""; } catch { }
                    if (name != null)
                        for (IDisplayDimension d = v.GetFirstDisplayDimension5(); d != null; d = d.GetNext5())
                        {
                            string full = null;
                            try { full = (d.GetDimension2(0) as IDimension)?.FullName; } catch { }
                            if (DimensionNameMatches(full, name))
                                found.Add(new AnnotationFound
                                {
                                    Kind = "dimension", Name = full, View = viewName,
                                    Annotation = d.GetAnnotation() as IAnnotation,
                                });
                        }
                    for (var n = v.GetFirstNote() as INote; n != null; n = n.GetNext() as INote)
                    {
                        string noteName = null, noteText = null;
                        try { noteName = n.GetName(); } catch { }
                        try { noteText = n.GetText(); } catch { }
                        bool byName = name != null && string.Equals(noteName, name, StringComparison.Ordinal);
                        bool byText = text != null && noteText != null &&
                                      string.Equals(noteText.Trim(), text.Trim(), StringComparison.Ordinal);
                        if (byName || byText)
                            found.Add(new AnnotationFound
                            {
                                Kind = "note", Name = noteName, Text = noteText, View = viewName,
                                Annotation = n.GetAnnotation() as IAnnotation,
                            });
                    }
                }
            }
            catch { return null; }
            return found;
        }

        private Outcome DeleteAnnotation(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "The active document is not a drawing: «delete_annotation» takes away a dimension or a note of a " +
                    "drawing. Open the drawing first; on a part use «delete_feature».");

            string name = Str(a, "name", null), text = Str(a, "text", null);
            if (string.IsNullOrWhiteSpace(name)) name = null; else name = name.Trim();
            if (string.IsNullOrWhiteSpace(text)) text = null;
            if ((name == null) == (text == null))
                return Failed(
                    "Write «name» or «text», one of the two: «name» is the name of a dimension as drawing_dimension " +
                    "gives it («RD1@Drawing View1@...», or «RD1» alone when it is the only one) or of a note as «note» " +
                    "gives it; «text» is the exact text of a note. I touched nothing.");

            string sheetName = "";
            try { sheetName = (drawing.GetCurrentSheet() as ISheet)?.GetName() ?? ""; } catch { }

            var found = FindAnnotations(drawing, name, text);
            if (found == null)
                return Failed(
                    "I cannot walk the annotations of the sheet «" + sheetName + "»: I cannot say which one answers. " +
                    "I touched nothing: try again once the drawing answers.");
            string asked = name != null ? "the name «" + name + "»" : "the text «" + text + "»";
            if (found.Count == 0)
                return Failed(
                    "No dimension or note of the sheet «" + sheetName + "» answers to " + asked + ". Use the name the plan " +
                    "that placed it gave («dimensions[].name» of drawing_dimension, «name» of note), or the exact text " +
                    "of the note; if it is on another sheet, activate it with «activate_sheet» first. I touched nothing.");
            if (found.Count > 1)
            {
                var which = new List<string>();
                foreach (AnnotationFound f in found) which.Add(f.Kind + " «" + (f.Name ?? f.Text) + "» (" + f.View + ")");
                return Failed(
                    found.Count + " annotations answer to " + asked + ": " + string.Join(", ", which) + ". Write the full " +
                    "name of the one to take away. I touched nothing.");
            }

            AnnotationFound target = found[0];
            IAnnotation annotation = target.Annotation;
            bool isDimension = target.Kind == "dimension";
            int before = isDimension ? CountDimensions(drawing) : NotesOfTheSheet(drawing);
            bool deleted = false;
            try
            {
                model.ClearSelection2(true);
                if (annotation != null && annotation.Select3(false, null))
                    deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch (Exception ex)
            {
                return Failed("Taking away the " + target.Kind + " «" + (target.Name ?? target.Text) + "» went into error: " +
                              ex.Message + ". Look at the drawing, and read it with «inspect_part» before going on.");
            }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
            }

            int after = isDimension ? CountDimensions(drawing) : NotesOfTheSheet(drawing);
            var still = FindAnnotations(drawing, name, text);
            bool gone = still != null && still.Count == 0;
            if (!gone)
                return Failed(
                    "I asked to take away the " + target.Kind + " «" + (target.Name ?? target.Text) + "»" +
                    (deleted ? " and SOLIDWORKS answered yes" : " and SOLIDWORKS answered no") + ", but " +
                    (still == null ? "I cannot walk the sheet again to see it" : "it is still on the sheet") +
                    ". Select it on the sheet and delete it by hand, or try again.");

            var measurements = new Dictionary<string, object>
            {
                ["kind"] = target.Kind,
                ["name"] = target.Name,
                ["view"] = target.View,
                ["sheet"] = sheetName,
                ["removed"] = true,
                [isDimension ? "dimensions_before" : "notes_before"] = before < 0 ? null : (object)before,
                [isDimension ? "dimensions_after" : "notes_after"] = after < 0 ? null : (object)after,
                ["what_it_does_not_check"] =
                    "Whether the drawing still has the dimensions it needs: I take away the one you name, not the ones " +
                    "that should replace it.",
            };
            if (target.Text != null) measurements["text"] = target.Text;
            if (before >= 0 && after >= 0 && after != before - 1)
                measurements["warning_count"] =
                    "The annotation no longer answers, but the count went from " + before + " to " + after +
                    " instead of down by one: look at the sheet.";
            _log("[agent] " + target.Kind + " «" + (target.Name ?? target.Text) + "» taken away from the sheet «" + sheetName + "»");
            return WithoutFeature(measurements);
        }

        /// <summary>The notes of the current sheet and of its views together, or -1.</summary>
        private int NotesOfTheSheet(IDrawingDoc drawing)
        {
            int onSheet = CountSheetNotes(drawing), inViews = CountViewNotes(drawing);
            return onSheet < 0 || inViews < 0 ? -1 : onSheet + inViews;
        }
    }
}
