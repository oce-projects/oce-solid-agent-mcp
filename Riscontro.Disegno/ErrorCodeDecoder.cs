using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// THE RAW CODES OF SOLIDWORKS, SAID IN WORDS (Z-C21.6, pattern S13).
    ///
    /// A number in a message is not information, it is homework: "SOLIDWORKS
    /// answered 3" sends whoever reads it to look up an enum. The audit found
    /// the same three enums printed raw or discarded in several places:
    ///
    ///   - swSetValueReturnStatus_e, returned by IDimension.SetSystemValue3
    ///     (guide: IDimension~SetSystemValue3.html, «Return Value Success
    ///     indicator value as defined in swSetValueReturnStatus_e»);
    ///   - swFileSaveError_e and swFileSaveWarning_e, the Errors and Warnings of
    ///     IModelDocExtension.SaveAs3 (guide: IModelDocExtension~SaveAs3.html,
    ///     «Errors that caused the save to fail as defined in swFileSaveError_e»,
    ///     «Warnings or extra information generated during the save operation as
    ///     defined in swFileSaveWarning_e»). These two are BIT MASKS: one call
    ///     can report several of them.
    ///
    /// The names and values come from the interop enums themselves, not from a
    /// table typed here: a table of numbers is what drifts. The words below only
    /// say what the enum name says; a code nobody has described stays its enum
    /// name and number, never an invented explanation (same rule as
    /// ErroriFeature).
    ///
    /// Reading the returns that are still discarded is lot Z-C21.8, which
    /// depends on this decoder.
    /// </summary>
    internal static class ErrorCodeDecoder
    {
        private static readonly Dictionary<swSetValueReturnStatus_e, string> SetValueWords =
            new Dictionary<swSetValueReturnStatus_e, string>
            {
                [swSetValueReturnStatus_e.swSetValue_Failure] = "the value was not set",
                [swSetValueReturnStatus_e.swSetValue_InvalidValue] = "the value is not valid for this dimension",
                [swSetValueReturnStatus_e.swSetValue_DrivenDimension] = "the dimension is driven, it cannot be set",
                [swSetValueReturnStatus_e.swSetValue_ModelNotLoaded] = "the model of the dimension is not loaded",
                [swSetValueReturnStatus_e.swSetValue_FrozenFeatureOwner] = "the feature that owns the dimension is frozen",
            };

        private static readonly Dictionary<swFileSaveError_e, string> SaveErrorWords =
            new Dictionary<swFileSaveError_e, string>
            {
                [swFileSaveError_e.swGenericSaveError] = "generic save error",
                [swFileSaveError_e.swReadOnlySaveError] = "the file is read-only",
                [swFileSaveError_e.swFileNameEmpty] = "the file name is empty",
                [swFileSaveError_e.swFileNameContainsAtSign] = "the file name contains '@'",
                [swFileSaveError_e.swFileLockError] = "the file is locked",
                [swFileSaveError_e.swFileSaveFormatNotAvailable] = "this format is not available",
                [swFileSaveError_e.swFileSaveWithRebuildError] = "the document has rebuild errors",
                [swFileSaveError_e.swFileSaveAsDoNotOverwrite] = "the file exists and was not overwritten",
                [swFileSaveError_e.swFileSaveAsInvalidFileExtension] = "the file extension is not valid",
                [swFileSaveError_e.swFileSaveAsNoSelection] = "nothing is selected to save",
                [swFileSaveError_e.swFileSaveAsBadEDrawingsVersion] = "bad eDrawings version",
                [swFileSaveError_e.swFileSaveAsNameExceedsMaxPathLength] = "the path is too long",
                [swFileSaveError_e.swFileSaveAsNotSupported] = "save as is not supported for this document",
                [swFileSaveError_e.swFileSaveRequiresSavingReferences] = "the referenced documents must be saved first",
            };

        private static readonly Dictionary<swFileSaveWarning_e, string> SaveWarningWords =
            new Dictionary<swFileSaveWarning_e, string>
            {
                [swFileSaveWarning_e.swFileSaveWarning_RebuildError] = "the document has rebuild errors",
                [swFileSaveWarning_e.swFileSaveWarning_NeedsRebuild] = "the document needs a rebuild",
                [swFileSaveWarning_e.swFileSaveWarning_ViewsNeedUpdate] = "drawing views need an update",
                [swFileSaveWarning_e.swFileSaveWarning_MissingOLEObjects] = "OLE objects are missing",
                [swFileSaveWarning_e.swFileSaveWarning_OpenedViewOnly] = "the document is open in view-only mode",
                [swFileSaveWarning_e.swFileSaveWarning_XmlInvalid] = "the XML is not valid",
            };

        /// <summary>
        /// The words for a SetSystemValue3 return, or null when it is
        /// swSetValue_Successful.
        /// </summary>
        internal static string SetValueStatus(int code)
        {
            if (code == (int)swSetValueReturnStatus_e.swSetValue_Successful) return null;
            return Single(code, SetValueWords);
        }

        /// <summary>The words for the Errors mask of a save, or null when it is 0.</summary>
        internal static string FileSaveErrors(int mask) => Mask(mask, SaveErrorWords);

        /// <summary>The words for the Warnings mask of a save, or null when it is 0.</summary>
        internal static string FileSaveWarnings(int mask) => Mask(mask, SaveWarningWords);

        private static string Single<TEnum>(int code, Dictionary<TEnum, string> words) where TEnum : struct
        {
            var number = code.ToString(CultureInfo.InvariantCulture);
            if (!Enum.IsDefined(typeof(TEnum), code))
                return "code " + number + " (not a value of " + typeof(TEnum).Name + ")";
            var value = (TEnum)Enum.ToObject(typeof(TEnum), code);
            string text;
            return (words.TryGetValue(value, out text) ? text + " " : "") + "(" + value + " = " + number + ")";
        }

        private static string Mask<TEnum>(int mask, Dictionary<TEnum, string> words) where TEnum : struct
        {
            if (mask == 0) return null;
            var parts = new List<string>();
            int known = 0;
            foreach (TEnum value in Enum.GetValues(typeof(TEnum)))
            {
                int bit = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (bit == 0 || (mask & bit) != bit) continue;
                known |= bit;
                string text;
                parts.Add((words.TryGetValue(value, out text) ? text + " " : "") +
                          "(" + value + " = " + bit.ToString(CultureInfo.InvariantCulture) + ")");
            }
            int rest = mask & ~known;
            if (rest != 0)
                parts.Add("bits " + rest.ToString(CultureInfo.InvariantCulture) + " not in " + typeof(TEnum).Name);
            return string.Join("; ", parts);
        }
    }
}
