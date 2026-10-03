using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// THE RAW CODES OF SOLIDWORKS, DECODED TO THEIR ENUM NAMES (Z-C21.6,
    /// pattern S13).
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
    /// The names come from the interop enums themselves, not from a table typed
    /// here: a table of numbers is what drifts. The decoder gives the ENUM NAME
    /// and nothing else (swSetValue_InvalidValue, swFileLockError): the name
    /// already says what happened, it is what the guide and a search use, and
    /// it reads the same inside an Italian message today and an English one
    /// after W3.1-bis. A sentence in either language would have put English
    /// words inside the Italian messages of the operations, or new Italian
    /// words past the language gate (review of Z-C21.6).
    ///
    /// Reading the returns that are still discarded is lot Z-C21.8, which
    /// depends on this decoder.
    /// </summary>
    internal static class ErrorCodeDecoder
    {
        /// <summary>
        /// The swSetValueReturnStatus_e name of a SetSystemValue3 return, or null
        /// when it is swSetValue_Successful.
        /// </summary>
        internal static string SetValueStatus(int code)
        {
            if (code == (int)swSetValueReturnStatus_e.swSetValue_Successful) return null;
            return Single<swSetValueReturnStatus_e>(code);
        }

        /// <summary>The swFileSaveError_e names in the Errors mask of a save, or null when it is 0.</summary>
        internal static string FileSaveErrors(int mask) => Mask<swFileSaveError_e>(mask);

        /// <summary>The swFileSaveWarning_e names in the Warnings mask of a save, or null when it is 0.</summary>
        internal static string FileSaveWarnings(int mask) => Mask<swFileSaveWarning_e>(mask);

        /// <summary>The enum name, or "EnumType(code)" for a value the enum does not define.</summary>
        private static string Single<TEnum>(int code) where TEnum : struct
        {
            if (!Enum.IsDefined(typeof(TEnum), code))
                return typeof(TEnum).Name + "(" + code.ToString(CultureInfo.InvariantCulture) + ")";
            return Enum.ToObject(typeof(TEnum), code).ToString();
        }

        /// <summary>
        /// The names of the bits set in a mask, joined as a flags expression
        /// ("swReadOnlySaveError | swFileLockError"); bits the enum does not
        /// define are kept as "EnumType(bits)".
        /// </summary>
        private static string Mask<TEnum>(int mask) where TEnum : struct
        {
            if (mask == 0) return null;
            var parts = new List<string>();
            int known = 0;
            foreach (TEnum value in Enum.GetValues(typeof(TEnum)))
            {
                int bit = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (bit == 0 || (mask & bit) != bit) continue;
                known |= bit;
                parts.Add(value.ToString());
            }
            int rest = mask & ~known;
            if (rest != 0)
                parts.Add(typeof(TEnum).Name + "(" + rest.ToString(CultureInfo.InvariantCulture) + ")");
            return string.Join(" | ", parts);
        }
    }
}
