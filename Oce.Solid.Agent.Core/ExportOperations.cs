using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// GETTING THE PART OUT OF SOLIDWORKS. (entry GP2)
    ///
    /// ── WHY THE STL IS THE MOST DANGEROUS CASE OF THE CATALOGUE ──────────
    ///
    /// An STL file contains almost nothing: a list of triangles. It does not say
    /// what units they are in, it does not say whether the solid is closed, it does not say which
    /// part it comes from. Everything that can go wrong here **opens all the
    /// same, slices all the same and prints all the same**, and the defect is
    /// seen when the part is in hand.
    ///
    /// The three things that give an error nowhere:
    ///
    /// 1. **THE UNITS.** The STL does not carry them: the slicer assumes
    ///    millimetres. If SOLIDWORKS exports in inches, the part comes out 25.4
    ///    times smaller and nothing says so. `swExportStlUnits` is a
    ///    GLOBAL PREFERENCE of the machine: leaving it as it is means the
    ///    result depends on who used SOLIDWORKS before.
    /// 2. **THE UNCLOSED BODY.** A surface or a solid with a gap
    ///    exports an STL that opens and is printed with a hole. It is the reason
    ///    why entry GP1 had to be done before this one: **here it refuses
    ///    instead of writing**, and the check runs BEFORE, not after.
    /// 3. **MORE THAN ONE BODY.** An STL of two bodies is two separate shells:
    ///    the slicer prints both of them, and the printer does not know which of the two
    ///    was the part.
    ///
    /// ── HOW THE QUALITY IS SET, SINCE THERE IS NO INTERFACE ─────────────
    ///
    /// `IExportStlData` **does not exist in this interop**: searched by
    /// reflection among all 1737 interfaces, no type with «Stl» in the
    /// name. The export is done with `SaveAs3` and the `.stl` extension, and every
    /// option is a **global user preference**:
    ///
    ///   swSTLQuality (int)            swSTLQuality_Coarse=1, _Fine=2, _Custom=3
    ///   swSTLBinaryFormat (toggle)    binary instead of ASCII
    ///   swExportStlUnits (int)        swLengthUnit_e: swMM = 0
    ///   swSTLShowInfoOnSave (toggle)  ** a window that blocks the COM call **
    ///   swSTLDontTranslateToPositive  if false, the coordinates get translated
    ///   swSTLComponentsIntoOneFile    only for assemblies
    ///
    /// THEY ARE READ, SET, AND PUT BACK AS THEY WERE. They are settings
    /// of the machine of whoever designs: an operation that changes them and leaves them
    /// changed modifies somebody else's CAD without saying so.
    ///
    /// `swSTLShowInfoOnSave` at `true` would open a modal, and **a COM call
    /// that opens a modal does not return**: it is the same trap as
    /// `AddDimension`, already paid for in this project.
    ///
    /// ── AND THEN THE FILE IS LOOKED AT, NOT THE ANSWER ──────────────────
    ///
    /// A binary STL has a header of 80 bytes, then **4 bytes with the number
    /// of triangles**, then 50 bytes per triangle. So:
    ///
    ///     expected size = 84 + 50 * triangles
    ///
    /// and the number of triangles is compared with the one the tessellated
    /// geometry declares. It is the only way to know that the file written is
    /// the part and not an empty header: a `SaveAs3` that returns `true` says
    /// only that SOLIDWORKS did not complain.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome ExportStl(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null)
                return Failed(
                    "This document is not a part: the STL is exported from a part. To send an assembly " +
                    "use «export_step», which carries it whole.");

            // ── 1. THE CHECK COMES FIRST, and it can stop everything ──────
            //
            // It is the line for which entry GP1 had to be done before this one. The
            // measurement of the file — it exists, it weighs something — does NOT catch the body
            // with the gap: the integrity check is needed BEFORE, not the
            // verification of the file AFTER.
            var check = CheckBodyGeometry(model, new Dictionary<string, object>(), docState);
            if (!check.Succeeded) return check;

            bool printable = check.Measurements != null &&
                              check.Measurements.ContainsKey("printable") &&
                              Convert.ToBoolean(check.Measurements["printable"]);
            string verdictValue = check.Measurements != null && check.Measurements.ContainsKey("verdict")
                ? Convert.ToString(check.Measurements["verdict"]) : "";

            bool force = Bool(a, "export_anyway", false);
            if (!printable && !force)
                return Failed(
                    "I do not export: " + verdictValue + "\n\n" +
                    "An STL carries nothing with it that says the part was like this: " +
                    "it opens, gets sliced and is printed all the same. Fix the part, " +
                    "or pass «export_anyway»: true if you know what you are doing.");

            // ── 2. DOVE ──────────────────────────────────────────────────
            string location = Str(a, "path", null);
            if (string.IsNullOrWhiteSpace(location))
            {
                string its = "";
                try { its = model.GetPathName() ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(its))
                    return Failed(
                        "The part has never been saved and you did not tell me where to put " +
                        "the STL. Tell me «path», or save the part first.");
                location = Path.ChangeExtension(its, ".stl");
            }
            if (!".stl".Equals(Path.GetExtension(location), StringComparison.OrdinalIgnoreCase))
                return Failed(
                    "The path «" + location + "» does not end in .stl. The extension is NOT " +
                    "a detail: it is the only thing that tells SaveAs3 which format to write.");

            string folder;
            try { folder = Path.GetDirectoryName(Path.GetFullPath(location)); }
            catch (Exception ex) { return Failed("A path I cannot read: " + ex.Message); }
            if (!Directory.Exists(folder))
                return Failed("The folder «" + folder + "» does not exist: I do not create it.");

            bool binary = Bool(a, "binary", true);
            string requestedQuality = (Str(a, "quality", "fine") ?? "fine").Trim().ToLowerInvariant();
            int quality;
            if (requestedQuality == "fine") quality = (int)swSTLQuality_e.swSTLQuality_Fine;
            else if (requestedQuality == "coarse") quality = (int)swSTLQuality_e.swSTLQuality_Coarse;
            else return Failed(
                "Quality «" + requestedQuality + "» unknown: there are «fine» and «coarse».");

            // ── 3. THE PREFERENCES: read, set, put back ──────────────────
            var before = ReadStlPreferences();
            List<string> preferencesNotRestored = null;
            string placed = null;
            int errors = 0, warnings = 0;
            bool ok;
            try
            {
                SetStlPreferences(quality, binary);

                if (File.Exists(location))
                {
                    try { File.Delete(location); }
                    catch (Exception ex)
                    {
                        return Failed(
                            "There is already a file there and I cannot replace it (" + ex.Message +
                            "): it is probably open in another program.");
                    }
                }

                // THE SELECTION IS CLEARED FIRST, ALWAYS (entry Z-C21.2, finding L13-1).
                //
                // IModelDocExtension~SaveAs3.html, Remarks: «Exports the entire
                // model, unless faces or bodies are selected, in which case, it
                // exports only those. Call IModelDoc2::ClearSelection2 before
                // calling this method to clear the selection list and export the
                // entire model.»
                //
                // A face left selected — from a step before, or by hand by
                // whoever is sitting in front of SOLIDWORKS — made an STL come out with that
                // face alone, and the checks below could not notice it:
                // they compare the file with itself (84 + 50 per triangle), not
                // with the part. `triangles_from_geometry` stays a number reported
                // alongside and does not become a constraint: the screen tessellation and
                // the export one do not have the same tolerance, and a
                // threshold on the order of magnitude is not fixed without having measured it
                // on curved parts. The case
                // `stl_does_not_export_the_selected_face` catches the defect.
                model.ClearSelection2(true);
                try
                {
                    ok = model.Extension.SaveAs3(
                        location,
                        (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                        (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                        null, null, ref errors, ref warnings);
                }
                catch (Exception ex) { return Failed("STL not written: " + ex.Message); }
                placed = location;
            }
            finally
            {
                // ALWAYS, even if the export went badly: the
                // preferences belong to the machine of whoever designs, and leaving them
                // changed means changing their CAD without telling them. Only
                // the ones that were read go back (N17).
                preferencesNotRestored = RestorePreferences(before);
            }

            if (!ok || !File.Exists(placed))
                return Failed(
                    "The STL was not written (code " + errors + SaveErrorNames(errors) + "). " +
                    "The file is not there: " + placed + SaveWarningsTail(warnings));

            long howMuchItWeighs = new FileInfo(placed).Length;

            // ── 4. THE FILE IS LOOKED AT, NOT THE ANSWER ─────────────────
            int trianglesInFile = StlTriangles(placed, binary, howMuchItWeighs, out string howIKnow);
            int partTriangles = GeometryTriangles(partDoc);

            var measurements = new Dictionary<string, object>
            {
                ["path"] = placed,
                ["byte"] = howMuchItWeighs,
                ["file_format"] = binary ? "binary" : "ASCII",
                ["quality"] = requestedQuality,
                // THE UNITS ARE ALWAYS STATED. The file does not carry them, and whoever opens
                // the STL has no way of knowing them: if this line is not
                // there, the only possible answer is «dunno, try it».
                ["units"] = "mm",
                ["triangles"] = trianglesInFile >= 0 ? (object)trianglesInFile : null,
                ["how_triangles_are_known"] = howIKnow,
                ["triangles_from_geometry"] = partTriangles >= 0
                    ? (object)partTriangles : null,
                ["check"] = verdictValue,
                // L13-7: the Warnings of SaveAs3, decoded, also on a success.
                ["save_warnings"] = SaveWarningsForAnswer(warnings),
            };
            string stlWarningAdvice = SaveWarningsAdvice(warnings);
            if (stlWarningAdvice != null) measurements["save_warnings_note"] = stlWarningAdvice;
            string stlPreferencesNote = PreferencesNotRestoredNote(preferencesNotRestored);
            if (stlPreferencesNote != null) measurements["preferences_not_restored"] = stlPreferencesNote;
            if (!printable)
                measurements["exported_anyway"] =
                    "You asked for it with «export_anyway»: the file is there and the part " +
                    "was not right. " + verdictValue;

            // A FILE THAT DOES NOT ADD UP IS NOT A FILE. On a binary STL the
            // size is exactly 84 + 50 per triangle: if it does not add up, the
            // file is there and is not the one needed, and it is better to know it now
            // than from the slicer.
            if (binary && trianglesInFile > 0)
            {
                long expectation = 84L + 50L * trianglesInFile;
                if (expectation != howMuchItWeighs)
                    return Failed(
                        "The STL says " + trianglesInFile + " triangles, which make " + expectation +
                        " bytes, and the file weighs " + howMuchItWeighs + ". The file is there and it " +
                        "does not add up: look at it before sending it to the printer. (" + placed + ")");
            }
            if (trianglesInFile == 0)
                return Failed(
                    "The STL was written but it has no triangle: it is an empty file " +
                    "with the header. (" + placed + ")");

            _log("[agent] STL: " + trianglesInFile + " triangles, " + howMuchItWeighs +
                 " bytes, mm, " + placed);
            return WithoutFeature(measurements);
        }

        // =================================================================
        // THE STL PREFERENCES, read and put back
        // =================================================================

        /// <summary>
        /// The user preferences an export changes, as they were READ before it,
        /// and nothing else (finding N17).
        ///
        /// Until library 0.144.0 the snapshot had one field per preference with
        /// its default value, 0 or false, and a read that threw left that
        /// default in place: the restore then wrote 0 into swSTLQuality, which is
        /// out of its range (swSTLQuality_e has Coarse, Fine and Custom, from 1),
        /// or false into a toggle the user had on. A preference is now written
        /// back only if its value was read. The answers of the writes are read
        /// too: ISldWorks~SetUserPreferenceIntegerValue.html, «True if
        /// UserPreferenceValue is set, false if not»; SetUserPreferenceToggle
        /// returns nothing (C# «void»), so a toggle is read again with the
        /// getter. The preferences that did not go back are returned by name,
        /// for the answer.
        ///
        /// No interop type in the members: the getters and setters come in as
        /// delegates, so Oce.Solid.Agent.ContractTests can exercise it by reflection.
        /// </summary>
        internal sealed class UserPreferenceSnapshot
        {
            // Preference -> the value read before the export.
            private readonly Dictionary<int, int> _integers = new Dictionary<int, int>();
            private readonly Dictionary<int, bool> _toggles = new Dictionary<int, bool>();
            private readonly Dictionary<int, string> _integerNames = new Dictionary<int, string>();
            private readonly Dictionary<int, string> _toggleNames = new Dictionary<int, string>();

            /// <summary>Reads one integer preference; a read that throws leaves it out of the restore.</summary>
            internal void ReadInteger(int preference, string name, Func<int, int> get)
            {
                try
                {
                    int value = get(preference);
                    _integers[preference] = value;
                    _integerNames[preference] = name;
                }
                catch { }
            }

            /// <summary>Reads one toggle; a read that throws leaves it out of the restore.</summary>
            internal void ReadToggle(int preference, string name, Func<int, bool> get)
            {
                try
                {
                    bool value = get(preference);
                    _toggles[preference] = value;
                    _toggleNames[preference] = name;
                }
                catch { }
            }

            /// <summary>How many preferences were read, and so will be written back.</summary>
            internal int ReadCount => _integers.Count + _toggles.Count;

            /// <summary>
            /// Writes back every preference that was read, and only those.
            /// Returns the names of the ones that did not go back: the integer
            /// setter answered false or threw, or the toggle does not read back
            /// as it was.
            /// </summary>
            internal List<string> Restore(
                Func<int, int, bool> setInteger, Action<int, bool> setToggle, Func<int, bool> getToggle)
            {
                var notRestored = new List<string>();
                foreach (int preference in _integers.Keys)
                {
                    int before = _integers[preference];
                    bool set = false;
                    try { set = setInteger(preference, before); } catch { }
                    if (!set) notRestored.Add(_integerNames[preference]);
                }
                foreach (int preference in _toggles.Keys)
                {
                    bool before = _toggles[preference];
                    bool back = false;
                    try { setToggle(preference, before); back = getToggle(preference) == before; } catch { }
                    if (!back) notRestored.Add(_toggleNames[preference]);
                }
                return notRestored;
            }
        }

        /// <summary>The five STL preferences that SetStlPreferences writes, read first (N17).</summary>
        private UserPreferenceSnapshot ReadStlPreferences()
        {
            var p = new UserPreferenceSnapshot();
            foreach (var i in new[] { swUserPreferenceIntegerValue_e.swSTLQuality, swUserPreferenceIntegerValue_e.swExportStlUnits })
                p.ReadInteger((int)i, i.ToString(), _sw.GetUserPreferenceIntegerValue);
            foreach (var t in new[]
            {
                swUserPreferenceToggle_e.swSTLBinaryFormat,
                swUserPreferenceToggle_e.swSTLShowInfoOnSave,
                swUserPreferenceToggle_e.swSTLDontTranslateToPositive,
            })
                p.ReadToggle((int)t, t.ToString(), _sw.GetUserPreferenceToggle);
            return p;
        }

        /// <summary>
        /// Writes back the preferences of a snapshot through ISldWorks (N17),
        /// and returns the names of those that did not go back.
        /// </summary>
        private List<string> RestorePreferences(UserPreferenceSnapshot snapshot)
        {
            if (snapshot == null) return new List<string>();
            return snapshot.Restore(
                _sw.SetUserPreferenceIntegerValue, _sw.SetUserPreferenceToggle, _sw.GetUserPreferenceToggle);
        }

        /// <summary>
        /// The note for the answer when some preferences of the user did not go
        /// back after the export (N17), or null when all of them did.
        /// </summary>
        internal static string PreferencesNotRestoredNote(List<string> names)
        {
            if (names == null || names.Count == 0) return null;
            return "I could not put back " + string.Join(", ", names) + " as they were before this export: " +
                   "check them in System Options > Export, they are the user's settings.";
        }

        private void SetStlPreferences(int quality, bool binary)
        {
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, quality); } catch { }
            // THE MILLIMETRES, EXPLICIT. It is the line that makes the operation worth it: without it,
            // the result depends on who used SOLIDWORKS before.
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits, (int)swLengthUnit_e.swMM); } catch { }
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, binary); } catch { }
            // A WINDOW WOULD BLOCK THE COM CALL, and it would never return.
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, false); } catch { }
            // THE COORDINATES STAY WHERE THEY ARE. Translating the part into the positive
            // means that the origin of the STL is not the origin of the model, and
            // whoever compares two exports no longer finds anything.
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, true); } catch { }
        }

        /// <summary>
        /// The swFileSaveError_e names of the Errors mask of SaveAs3, as ": ..." to append after the
        /// raw code, or "" when the mask is 0 (S13, ErrorCodeDecoder). The raw
        /// code stays in the message: whoever already searches for it still
        /// finds it.
        /// </summary>
        private static string SaveErrorNames(int errors)
        {
            var names = ErrorCodeDecoder.FileSaveErrors(errors);
            return names == null ? "" : ": " + names;
        }

        /// <summary>
        /// The Warnings mask of a save, for the answer (finding L13-7): the
        /// swFileSaveWarning_e names through ErrorCodeDecoder, or «none» with
        /// its reason when the mask is 0, a zero that was read. Until library
        /// 0.144.0 the warnings of SaveAs3 were never looked at, and the two
        /// save calls of the drawing gave the raw number or nothing.
        /// swFileSaveWarning_e.html: «These warnings do not cause the File,
        /// Save operation to fail. Bitmask.» So they go in the answer of a
        /// success, and in the message of a failure, never in a refusal of
        /// their own.
        /// </summary>
        internal static string SaveWarningsForAnswer(int mask)
        {
            return ErrorCodeDecoder.FileSaveWarnings(mask) ?? "none (the Warnings mask of the save is 0)";
        }

        /// <summary>
        /// The tail of a failure message with the warnings of the save, or ""
        /// when the mask is 0 (L13-7).
        /// </summary>
        private static string SaveWarningsTail(int mask)
        {
            var names = ErrorCodeDecoder.FileSaveWarnings(mask);
            return names == null ? "" : " Warnings of the save: " + names + ".";
        }

        /// <summary>
        /// What a warning of the save means for the file just written, or null
        /// when none of the warnings says the file may be out of date (L13-7).
        /// The bits are read by their enum names, never by number:
        /// swFileSaveWarning_e.html gives swFileSaveWarning_RebuildError 1,
        /// swFileSaveWarning_NeedsRebuild 2 and swFileSaveWarning_ViewsNeedUpdate
        /// 4 (the Z-C21.12 audit corrected a note that gave NeedsRebuild as 1).
        /// </summary>
        internal static string SaveWarningsAdvice(int mask)
        {
            var parts = new List<string>();
            if ((mask & (int)swFileSaveWarning_e.swFileSaveWarning_RebuildError) != 0)
                parts.Add("the model had rebuild errors");
            if ((mask & (int)swFileSaveWarning_e.swFileSaveWarning_NeedsRebuild) != 0)
                parts.Add("the model needed a rebuild");
            if ((mask & (int)swFileSaveWarning_e.swFileSaveWarning_ViewsNeedUpdate) != 0)
                parts.Add("some drawing views needed an update");
            if (parts.Count == 0) return null;
            return "SOLIDWORKS warned, while writing the file, that " + string.Join(" and ", parts) +
                   ": the file may not show the model as it is now. Rebuild it (Ctrl+Q) and write the " +
                   "file again before sending it.";
        }

        // =================================================================
        // THE TRIANGLES: those written in the file and those of the geometry
        // =================================================================

        /// <summary>
        /// How many triangles the FILE says. On a binary it sits in the four bytes
        /// after the header of 80; on an ASCII one the «facet» lines are counted.
        /// </summary>
        private static int StlTriangles(string filePath, bool binary, long howMuchItWeighs,
                                             out string howIKnow)
        {
            howIKnow = "";
            try
            {
                if (binary)
                {
                    if (howMuchItWeighs < 84) { howIKnow = "the file is shorter than the header"; return -1; }
                    using (var f = File.OpenRead(filePath))
                    {
                        var head = new byte[84];
                        if (f.Read(head, 0, 84) != 84)
                        { howIKnow = "I could not read the header"; return -1; }
                        howIKnow = "from the four bytes after the header of the binary file";
                        return BitConverter.ToInt32(head, 80);
                    }
                }
                int howMany = 0;
                foreach (var line in File.ReadLines(filePath))
                    if (line.TrimStart().StartsWith("facet ", StringComparison.OrdinalIgnoreCase))
                        howMany++;
                howIKnow = "counting the «facet» records of the ASCII file";
                return howMany;
            }
            catch (Exception ex)
            {
                howIKnow = "I could not read the file: " + ex.Message;
                return -1;
            }
        }

        /// <summary>
        /// How many triangles the tessellation of the part has, face by face.
        ///
        /// IT IS NOT THE SAME NUMBER as the file's, and it must not be: the
        /// screen tessellation and the export one have different tolerances.
        /// It serves as an ORDER OF MAGNITUDE — a file with ten
        /// triangles on a part that has a thousand is a wrong file — and for
        /// that reason it is reported alongside, not compared.
        /// </summary>
        private static int GeometryTriangles(IPartDoc partDoc)
        {
            try
            {
                int howMany = 0;
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null) return -1;
                foreach (var c in bodies)
                {
                    var faces = (c as IBody2).GetFaces() as object[];
                    if (faces == null) continue;
                    foreach (var f in faces)
                    {
                        // A face that does not say its triangles makes the whole count
                        // «I do not know», not a lower count (entry Z-C8).
                        try { howMany += (f as IFace2).GetTessTriangleCount(); } catch { return -1; }
                    }
                }
                return howMany;
            }
            catch { return -1; }
        }

        // =================================================================
        // STEP and IGES: the formats with which a part is sent to someone else
        // =================================================================
        //
        // ── WHY THEY DO NOT REFUSE LIKE THE STL ──────────────────────────
        //
        // `export_stl` refuses if the part is not a single closed solid
        // body. Here it does NOT, and it is a deliberate difference: a STEP with ten
        // bodies is **normal** — a weldment is sent to the supplier like that — and
        // so is one with open surfaces, because STEP and IGES carry them.
        // Refusing them would be applying the rules of one format to
        // another.
        //
        // The check runs all the same and **its verdict travels with the
        // answer**: whoever reads knows what the part they sent was like, and
        // is not prevented from sending it.
        //
        // ── NO EXPORT INTERFACE, ONLY PREFERENCES ────────────────────────
        //
        // Searched by reflection: of STEP and IGES only the IMPORT interfaces
        // exist (`IImportStepData`, `IImportIgesData`). To write
        // there is `SaveAs3` with `ExportData` at null, and everything else is
        // global user preferences:
        //
        //   swStepAP (int)                 203, 214, with no enum (not 242: below)
        //   swIGESRepresentation (int)     swIGES_BREP = 3 (solids),
        //                                  swIGES_TRMSRF = 0 (surfaces)
        //   swIGESExportSolidAndSurface    on: «Specifies whether to export data
        //                                  as solid or surface entities»
        //   swIGESExportAsWireframe        off: «Specifies whether to convert
        //                                  solid body to a 3D wireframe
        //                                  representation in the IGES file»
        //   swIGESExportFreeCurves         off: «Specifies whether to include 3D
        //                                  curve features in the exported file»
        //   swIGESExportSketchEntities     off: «Specifies whether to export
        //                                  sketch entities»
        //
        // (estratto_swconst/FileSaveAsIGESOptions.htm, «System Options > Export
        // > IGES 5.3».) THE THREE «off» ARE Z-C21.10, finding N59: they were
        // left as the user's profile had them. With «IGES wireframe (3D curves)»
        // ticked the page says the solid body is converted to curves (whether
        // next to the solid or in its place is not measured); with sketches or
        // 3D curves ticked, the file carries stray curves the recipient takes
        // for geometry. All four are read before, set, and put back in
        // `finally`, like the representation. After writing, the file's own
        // directory says what is in it (`IgesEntityCensus`): a file with no
        // solid and no surface is refused.
        //
        // `swStepAP` IS THE AWKWARD ONE: its values have no enum in `swconst`,
        // so the number is a literal, and a literal is something one believes.
        // That is why, after writing, the file's own FILE_SCHEMA is read back:
        // it says which protocol really came out. AP203 declares
        // «CONFIG_CONTROL_DESIGN», AP214 «AUTOMOTIVE_DESIGN», AP242 its long
        // name. It is the only way to know that the 214 asked is the 214 got.
        //
        // ── AP242 IS NOT A VALUE OF swStepAP (Z-C21.13, finding C5-7) ────────
        //
        // The page lists two values only (estratto_swconst/
        // FileSaveAsSTEPOptions.htm, «203 = STEP AP203 format», «214 = STEP
        // AP214 format»), and until library 0.143.0 this code wrote 242 anyway.
        // The probe probe_step_ap242 (29/9/2026, SOLIDWORKS 2021 SP5.1): 242 is
        // accepted, read back as 242, SaveAs3 answers true with no error and no
        // warning, and the file is AP203 (FILE_SCHEMA «CONFIG_CONTROL_DESIGN»);
        // the junk value 999 does the same. The FILE_SCHEMA check refused that
        // file, but for the wrong reason: «AP242 asked, AP203 out» sends one
        // looking at the protocol preference, when AP242 has another door.
        //
        // The door is IModelDocExtension.PublishSTEP242File (page
        // IModelDocExtension~PublishSTEP242File.html: «Exports the SOLIDWORKS
        // MBD 3D part or assembly to a STEP 242 file», Path «use .STP for the
        // file name extension», Return «Status as defined in
        // swStep242Error_e»), SOLIDWORKS MBD, since 2017. The same probe: with a
        // .step path it answers 1 (swPublishStep242_InvalidPath), with a .stp
        // path 4 (swPublishStep242_MBDLicenseNotAvailable): the bench machine
        // has no MBD licence. So AP242 never touches swStepAP, wants a .stp
        // path, and on 4 is refused saying that AP214 and AP203 remain. On 0
        // the file goes through the same FILE_SCHEMA check as the other two;
        // that branch is not measured here, for want of the licence.

        private Outcome ExportExchange(
            IModelDoc2 model, Dictionary<string, object> a, string operation,
            DocumentState docState)
        {
            var partDoc = model as IPartDoc;
            var assemblyDoc = model as IAssemblyDoc;   // entry Z-D6: the assembly is sent too
            // Z-C21.11: how many components were lightweight before the export,
            // read right before the export (the probe saw SaveAs3 resolve them).
            int lightweightBeforeExport = -1;
            if (partDoc == null && assemblyDoc == null)
                return Failed(
                    "This document is neither a part nor an assembly: STEP and IGES are exported from a part " +
                    "or from an assembly. Open the one to send for me.");
            if (assemblyDoc != null && Bool(a, "reopen_and_compare", false))
                return Failed(
                    "On an assembly I do not do «reopen_and_compare»: reading back the STEP of an assembly opens " +
                    "the window «New Document» and the call does not return (measured). The check I do is the count " +
                    "of the solids in the file against those of the parts: remove it and ask me again.");

            bool iges = operation == "export_iges";
            // THE ASSEMBLY IGES IS NOT (adversarial review of 27/9/2026): it does not have the
            // check of the solids that the STEP has, and it depends on a global preference
            // (swIGESComponentsIntoOneFile) that is not set — with that one off
            // SOLIDWORKS writes one file per component, and the one in «path» may
            // not contain the whole assembly. Until it is measured, it refuses.
            if (iges && assemblyDoc != null)
                return Failed(
                    "I do not write the IGES of an assembly: I cannot verify that the whole assembly is inside " +
                    "(with certain settings SOLIDWORKS writes one file per component). To send the assembly use " +
                    "«export_step», which carries it whole and I check it; for a single part, open it and ask me for the IGES there.");

            // THE PROTOCOL BEFORE THE PATH (Z-C21.13, C5-7): AP242 decides which
            // extension the path may have.
            int protocol = 0;
            string requestedProtocol = null;
            bool ap242 = false;
            int representation = 0;
            string requestedRepresentation = null;
            if (iges)
            {
                requestedRepresentation = (Str(a, "representation", "solids") ?? "solids")
                    .Trim().ToLowerInvariant();
                if (requestedRepresentation == "solids")
                    representation = (int)swIGESRepresentation_e.swIGES_BREP;
                else if (requestedRepresentation == "surfaces")
                    representation = (int)swIGESRepresentation_e.swIGES_TRMSRF;
                else return Failed(
                    "Representation «" + requestedRepresentation + "» unknown: there are " +
                    "«solids» (BREP, the one needed by whoever has to machine the part) " +
                    "and «surfaces».");
            }
            else
            {
                requestedProtocol = (Str(a, "protocol", "AP214") ?? "AP214")
                    .Trim().ToUpperInvariant();
                if (requestedProtocol == "AP203") protocol = 203;
                else if (requestedProtocol == "AP214") protocol = 214;
                // AP242 HAS NO swStepAP VALUE: written there, it gives an AP203
                // file (the comment above this method). PublishSTEP242File
                // writes it, further down.
                else if (requestedProtocol == "AP242") ap242 = true;
                else return Failed(
                    "Protocol «" + requestedProtocol + "» unknown: there are AP203, " +
                    "AP214 (the default) and AP242.");
            }

            string[] goodExtensions = iges
                ? new[] { ".iges", ".igs" }
                : ap242 ? new[] { ".stp" } : new[] { ".step", ".stp" };

            // THE CHECK RUNS ALL THE SAME, AND STOPS NOTHING. The verdict
            // travels with the answer: whoever sends a part to a supplier has the
            // right to know that it had two bodies, not to be stopped.
            string verdictValue = "";
            if (partDoc != null)
            {
                var check = CheckBodyGeometry(model, new Dictionary<string, object>(), docState);
                if (check.Succeeded && check.Measurements != null && check.Measurements.ContainsKey("verdict"))
                    verdictValue = Convert.ToString(check.Measurements["verdict"]);
            }
            else verdictValue = "assembly: the check of the bodies is done on the parts, one by one";

            string location = Str(a, "path", null);
            if (string.IsNullOrWhiteSpace(location))
            {
                string its = "";
                try { its = model.GetPathName() ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(its))
                    return Failed(
                        "The part has never been saved and you did not tell me where to put " +
                        "the file. Tell me «path», or save the part first.");
                location = Path.ChangeExtension(its, goodExtensions[0]);
            }

            // AP242 WANTS .stp, and is refused before the call otherwise (C5-7).
            if (ap242)
            {
                string pathProblem = Step242PathProblem(location);
                if (pathProblem != null) return Failed(pathProblem);
            }

            string ext = (Path.GetExtension(location) ?? "").ToLowerInvariant();
            if (Array.IndexOf(goodExtensions, ext) < 0)
                return Failed(
                    "The path «" + location + "» ends in «" + ext + "», and for «" +
                    operation + "» you need " + string.Join(" or ", goodExtensions) +
                    ". The extension is NOT a detail: it is the only thing that tells " +
                    "SaveAs3 which format to write.");

            string folder;
            try { folder = Path.GetDirectoryName(Path.GetFullPath(location)); }
            catch (Exception ex) { return Failed("A path I cannot read: " + ex.Message); }
            if (!Directory.Exists(folder))
                return Failed("The folder «" + folder + "» does not exist: I do not create it.");

            var before = ReadExchangePreferences();
            List<string> preferencesNotRestored = null;
            int errors = 0, warnings = 0;
            bool ok;
            List<string> igesTogglesNotOff = null;
            try
            {
                if (iges) igesTogglesNotOff = SetIgesPreferences(representation);
                else if (!ap242) SetStepPreferences(protocol);

                // Before either door (review of Z-C21.11): PublishSTEP242File
                // writes an assembly too, and whatever it resolves is said the same way.
                if (assemblyDoc != null) lightweightBeforeExport = LightweightCount(assemblyDoc);

                if (ap242)
                {
                    // C5-7: its own method, its own handling of a file already at
                    // the path (see PublishStep242), and swStepAP left alone.
                    string refusal = PublishStep242(model, location);
                    if (refusal != null) return Failed(refusal);
                    ok = true;
                }
                else
                {
                    if (File.Exists(location))
                    {
                        try { File.Delete(location); }
                        catch (Exception ex)
                        {
                            return Failed(
                                "There is already a file there and I cannot replace it (" + ex.Message +
                                "): it is probably open in another program.");
                        }
                    }

                    // AS FOR THE STL (finding L13-1). IModelDocExtension~SaveAs3.html,
                    // Remarks: «Exports the entire model, unless faces or bodies are
                    // selected, in which case, it exports only those». For the STEP of
                    // a part there is no count of the bodies that would notice it,
                    // and the assembly one would take a file with fewer solids.
                    model.ClearSelection2(true);
                    try
                    {
                        ok = model.Extension.SaveAs3(
                            location,
                            (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                            null, null, ref errors, ref warnings);
                    }
                    catch (Exception ex) { return Failed("File not written: " + ex.Message); }
                }
            }
            finally { preferencesNotRestored = RestorePreferences(before); }   // only what was read (N17)

            if (!ok || !File.Exists(location))
                return Failed("The file was not written (code " + errors + SaveErrorNames(errors) + "): " + location +
                               SaveWarningsTail(warnings));

            long howMuchItWeighs = new FileInfo(location).Length;
            var measurements = new Dictionary<string, object>
            {
                ["path"] = location,
                ["byte"] = howMuchItWeighs,
                ["check"] = verdictValue,
            };
            // L13-7: the Warnings of SaveAs3, decoded, also on a success. The
            // AP242 door (PublishSTEP242File) has no Warnings mask to read, so
            // the field is null there, not «none».
            if (ap242)
                measurements["save_warnings"] = null;
            else
            {
                measurements["save_warnings"] = SaveWarningsForAnswer(warnings);
                string exchangeWarningAdvice = SaveWarningsAdvice(warnings);
                if (exchangeWarningAdvice != null) measurements["save_warnings_note"] = exchangeWarningAdvice;
            }
            string exchangePreferencesNote = PreferencesNotRestoredNote(preferencesNotRestored);
            if (exchangePreferencesNote != null) measurements["preferences_not_restored"] = exchangePreferencesNote;

            if (iges)
            {
                measurements["file_format"] = "IGES";
                measurements["representation"] = requestedRepresentation;
                string why;
                int record = RecordIges(location, out why);
                measurements["parameter_record"] = record >= 0 ? (object)record : null;
                measurements["how_known"] = why;
                if (record == 0)
                    return Failed(
                        "The IGES was written but it has no parameter record: it is a " +
                        "file with the headers and nothing inside. (" + location + ")");

                // WHAT IS IN THE FILE, NOT ONLY HOW MUCH (N59). The record count
                // does not tell a solid from its wireframe: both have records.
                // The directory section does, one type number per entity.
                int entities, solids, surfaces;
                string census;
                bool counted = IgesEntityCensus(location, out entities, out solids, out surfaces, out census);
                measurements["entities"] = counted ? (object)entities : null;
                measurements["solid_entities"] = counted ? (object)solids : null;
                measurements["surface_entities"] = counted ? (object)surfaces : null;
                measurements["entity_census"] = census;
                // THE THREE TOGGLES, AS READ BACK after turning them off (N59,
                // review): an empty list is the proof they were off while the
                // file was written; a name in it is an option that may have put
                // curves in the file next to the solid, which the census above
                // cannot tell from the solid's own edges.
                measurements["iges_options_not_off"] = igesTogglesNotOff != null
                    ? (object)igesTogglesNotOff.ToArray() : null;
                if (igesTogglesNotOff != null && igesTogglesNotOff.Count > 0)
                    measurements["iges_options_warning"] =
                        "I could not turn off " + string.Join(", ", igesTogglesNotOff) + " for this export " +
                        "(read back on, or unreadable): the file may carry sketch entities or 3D curves next " +
                        "to the solid, and whoever opens it may take them for geometry. Turn them off in " +
                        "System Options > Export > IGES 5.3 and export again.";
                if (counted && entities > 0 && solids == 0 && surfaces == 0)
                    return Failed(
                        "The IGES was written, but none of its " + entities + " entities is a solid " +
                        "(type 186) or a surface: it is a wireframe of the part, curves only, and " +
                        "whoever opens it gets edges instead of a part. (" + location + ")");
            }
            else
            {
                measurements["file_format"] = "STEP";
                measurements["protocol_requested"] = requestedProtocol;
                // WHICH DOOR WROTE IT (C5-7): the two do not share their settings.
                measurements["written_with"] = ap242
                    ? "IModelDocExtension.PublishSTEP242File (SOLIDWORKS MBD)"
                    : "SaveAs3 with swStepAP = " + protocol;
                int entity;
                string why;
                string schema = StepSchema(location, out entity, out why);
                string exited = FromSchema(schema);
                measurements["file_schema"] = schema;
                measurements["protocol_returned"] = exited;
                measurements["entity"] = entity >= 0 ? (object)entity : null;
                measurements["how_known"] = why;

                if (schema == null)
                    return Failed(
                        "The file has no «ISO-10303-21» at its start or no FILE_SCHEMA: " +
                        "it is not a STEP file, whatever the extension says. (" + location + ")");
                if (entity == 0)
                    return Failed(
                        "The STEP was written but its DATA section is empty: it is a " +
                        "file with the headers and nothing inside. (" + location + ")");
                if (exited != null && exited != requestedProtocol)
                    return Failed(
                        "You asked for " + requestedProtocol + " and " + exited +
                        " came out (FILE_SCHEMA «" + schema + "»). The file is there and it is " +
                        "not the protocol that is needed: some suppliers accept only 203. (" +
                        location + ")");
                // THE WHOLE ASSEMBLY IN THE FILE (entry Z-D6): one MANIFOLD_SOLID_BREP per
                // body, against the bodies of the unsuppressed parts. Rereading it is not
                // possible (it opens a modal): the solids in the text are counted.
                if (assemblyDoc != null)
                {
                    var hiddenLeftOut = new List<string>();
                    int expectedValues = ExpectedSolidsInAssembly(assemblyDoc, hiddenLeftOut);
                    int inFile = SolidsInStep(location);
                    measurements["solids_expected"] = expectedValues >= 0 ? (object)expectedValues : null;
                    measurements["solids_in_file"] = inFile >= 0 ? (object)inFile : null;
                    // Z-C21.11: hidden components are not written (measured), and the
                    // export resolved lightweight components (measured): both are said.
                    if (hiddenLeftOut.Count > 0)
                        measurements["hidden_components_not_exported"] = hiddenLeftOut.ToArray();
                    int lightweightAfterExport = LightweightCount(assemblyDoc);
                    if (lightweightBeforeExport > 0 && lightweightAfterExport >= 0 && lightweightAfterExport < lightweightBeforeExport)
                        measurements["lightweight_resolved_by_the_export"] =
                            (lightweightBeforeExport - lightweightAfterExport) + " component(s) were lightweight before the " +
                            "export and SOLIDWORKS resolved them to write the file; they stay resolved.";
                    if (expectedValues >= 0 && inFile >= 0 && inFile < expectedValues)
                        return Failed(
                            "The STEP of the assembly has " + inFile + " solids and the parts that are not suppressed have " +
                            expectedValues + ": something did not make it into the file. (" + location + ")");
                }
            }

            // ── THE REAL TEST, IF ASKED FOR ──────────────────────────────
            //
            // Reopening the file and comparing the volume is the only measurement that
            // says the GEOMETRY survived the translation: the
            // size of a file does not say it, nor does the number of
            // entities. It is not the default because it costs a document
            // opening: on a large part it is minutes.
            if (Bool(a, "reopen_and_compare", false))
            {
                // THE COMPARISON NEEDS TWO VOLUMES READ (entry Z-C8): with the zero
                // of a failed read on this side, «the part has 0: the
                // translation lost something» was a false sentence.
                double? myRead = VolumeMm3(model);
                string comE;
                double its = VolumeOnReopen(location, out comE);
                measurements["volume_mm3"] = NumberOrNull(myRead, 2);
                measurements["volume_reread_mm3"] = its >= 0 ? (object)Math.Round(its, 2) : null;
                measurements["how_the_reread_went"] = comE;
                if (its < 0)
                    return Failed(
                        "The file is written but I could not read it back to " +
                        "compare it: " + comE + " (" + location + ")");
                if (myRead == null)
                    return Failed(
                        "The file is written and read back, but I cannot measure the volume of the " +
                        "starting part: without it, the comparison cannot be made. (" + location + ")");
                double myOwn = myRead.Value;

                // ONE THOUSANDTH, not «about». A translation that loses a
                // thousandth of volume has lost a face, and on a part that
                // goes to the workshop it is not a rounding.
                double deviation = myOwn > 0 ? Math.Abs(its - myOwn) / myOwn : Math.Abs(its - myOwn);
                measurements["deviation_relative"] = Math.Round(deviation, 6);
                if (deviation > 0.001)
                    return Failed(
                        "The reopened file has volume " + its.ToString("0.##") + " mm3 and " +
                        "the part has " + myOwn.ToString("0.##") + ": a deviation of " +
                        (deviation * 100).ToString("0.###") + "%. The translation lost " +
                        "something. (" + location + ")");
            }

            _log("[agent] " + (iges ? "IGES" : "STEP") + ": " + howMuchItWeighs + " bytes, " + location);
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// The IGES toggles the export turns off (N59), one list for read, set
        /// and restore. FileSaveAsIGESOptions.htm: «IGES wireframe (3D curves)»,
        /// «Export 3D Curve features», «Export sketch entities».
        /// </summary>
        private static readonly swUserPreferenceToggle_e[] IgesTogglesTurnedOff =
        {
            swUserPreferenceToggle_e.swIGESExportAsWireframe,
            swUserPreferenceToggle_e.swIGESExportFreeCurves,
            swUserPreferenceToggle_e.swIGESExportSketchEntities,
        };

        /// <summary>
        /// The STEP and IGES preferences the export may write, read first (N17).
        /// The N59 IGES toggles were already restored only when read; the
        /// protocol, the representation and the solid-and-surface toggle were
        /// written back as 0 or false after a failed read until library 0.144.0.
        /// </summary>
        private UserPreferenceSnapshot ReadExchangePreferences()
        {
            var p = new UserPreferenceSnapshot();
            foreach (var i in new[] { swUserPreferenceIntegerValue_e.swStepAP, swUserPreferenceIntegerValue_e.swIGESRepresentation })
                p.ReadInteger((int)i, i.ToString(), _sw.GetUserPreferenceIntegerValue);
            p.ReadToggle((int)swUserPreferenceToggle_e.swIGESExportSolidAndSurface,
                swUserPreferenceToggle_e.swIGESExportSolidAndSurface.ToString(), _sw.GetUserPreferenceToggle);
            foreach (var t in IgesTogglesTurnedOff)
                p.ReadToggle((int)t, t.ToString(), _sw.GetUserPreferenceToggle);
            return p;
        }

        private void SetStepPreferences(int protocol)
        {
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, protocol); } catch { }
        }

        // =================================================================
        // STEP AP242: PublishSTEP242File (Z-C21.13, finding C5-7)
        // =================================================================

        /// <summary>
        /// Writes STEP AP242 at <paramref name="path"/> (a .stp path, checked
        /// before by <see cref="Step242PathProblem"/>) through
        /// IModelDocExtension.PublishSTEP242File. Null when the new file is
        /// there, otherwise the refusal.
        ///
        /// A FILE ALREADY AT THE PATH IS MOVED ASIDE, NOT DELETED. SaveAs3's
        /// branch deletes it first; here a refusal is the expected answer on
        /// every machine without the MBD licence, the bench machine included,
        /// and deleting first would destroy the file the user had there each
        /// time (on 0.143.0 it was replaced by an AP203 file, and then the
        /// export was refused). The previous file is renamed next to itself, put
        /// back when nothing new came out, and deleted once the new one is
        /// written.
        /// </summary>
        private string PublishStep242(IModelDoc2 document, string path)
        {
            string aside = null;
            if (File.Exists(path))
            {
                aside = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".previous";
                try { File.Move(path, aside); }
                catch (Exception ex)
                {
                    return "There is already a file at «" + path + "» and I cannot move it aside (" + ex.Message +
                           "): it is probably open in another program. Nothing was written.";
                }
            }

            string refusal;
            try { refusal = WriteStep242(document, path); }
            catch (Exception ex) { refusal = "STEP AP242 was not written: " + ex.Message; }

            if (refusal == null)
            {
                if (aside != null)
                {
                    // A FileInfo and not File.Delete: the obsolete-call contract
                    // cannot type the receiver «File», and counts it undecided.
                    try { new FileInfo(aside).Delete(); }
                    catch (Exception ex) { _log("[agent] STEP AP242: the previous file stays at " + aside + " (" + ex.Message + ")"); }
                }
                return null;
            }

            // NOTHING NEW CAME OUT. Whatever the refused call left at the path is
            // not a checked AP242, and the file that was there goes back.
            if (aside == null)
            {
                if (File.Exists(path))
                    refusal += " The refused call left a file at «" + path + "»: it is not a checked AP242, do not send it.";
                return refusal;
            }
            try
            {
                if (File.Exists(path)) new FileInfo(path).Delete();
                File.Move(aside, path);
                return refusal + " The file that was already at «" + path + "» is back in its place.";
            }
            catch (Exception ex)
            {
                return refusal + " The file that was already at «" + path + "» is now «" + aside +
                       "»: I could not put it back (" + ex.Message + ").";
            }
        }

        /// <summary>
        /// The call itself, with OUR document in front of SOLIDWORKS while it
        /// runs. The page of PublishSTEP242File does not say whether it works on
        /// a document that is not the active one, the probe brought the part to
        /// the front before calling it, and more than one session can work on
        /// the same SOLIDWORKS: so our document goes to the front first, as for
        /// the flat pattern (ExportToDWG2, SheetMetalOperations.cs), and the one
        /// that was in front goes back there.
        ///
        /// The selection is cleared as for SaveAs3 (finding L13-1, whose page
        /// says a selection limits what is exported). This page says nothing
        /// about a selection; a face left selected must not get the chance to
        /// decide what the file holds.
        /// </summary>
        private string WriteStep242(IModelDoc2 document, string path)
        {
            IModelDoc2 previousFront;
            string activationProblem = BringDocumentToFront(document, out previousFront);
            // The one that was in front goes back there, even when bringing ours
            // failed: a failed attempt may have brought another document
            // forward. By REFERENCE, through DocumentByPath (Z-C21.13, pattern
            // S18): it was the title, which can be another document's too.
            Action putBackTheFront = () =>
            {
                if (previousFront == null) return;
                int activationErrors;
                string problem;
                if (!DocumentByPath.ActivateAndReadBack(_sw, previousFront, out activationErrors, out problem))
                    _log("[agent] STEP AP242: " + problem);
                else
                {
                    string stale = DocumentByPath.PutBackNeedsRebuild(previousFront, activationErrors);
                    if (stale != null) _log("[agent] STEP AP242: " + stale);
                }
            };
            if (activationProblem != null)
            {
                putBackTheFront();
                return "STEP AP242 was not written: " + activationProblem + ".";
            }
            int code;
            try
            {
                document.ClearSelection2(true);
                code = document.Extension.PublishSTEP242File(path);
            }
            catch (Exception ex)
            {
                return "STEP AP242 was not written: PublishSTEP242File raised an exception: " + ex.Message;
            }
            finally { putBackTheFront(); }
            string refusal = Step242Refusal(code, path);
            if (refusal != null) return refusal;
            if (!File.Exists(path))
                return "PublishSTEP242File answered " + code + " (swPublishStep242_Success) and there is no file at «" +
                       path + "».";
            return null;
        }

        /// <summary>
        /// Brings <paramref name="document"/> to the front of SOLIDWORKS. Null
        /// when the active document is ours afterwards, otherwise what went
        /// wrong; <paramref name="previousFront"/> is the document that was in
        /// front, when it was another one, so that the caller can bring it back.
        ///
        /// The same call as BringToFront (SheetMetalOperations.cs) and the bridge's
        /// BringToFront: DocumentByPath.ActivateAndReadBack (Z-C21.13, pattern
        /// S18), which gives ActivateDoc3 the file name with extension and then
        /// the full path of a saved part, and the title of a part never saved only when
        /// no other open document answers to it (ISldWorks~ActivateDoc3.html
        /// warns that a title without extension can name a part and an assembly
        /// at once), with swDontRebuildActiveDoc, and reads back the COM
        /// identity of the active document, which needs neither a path nor a
        /// title: a STEP is also written from a part never saved.
        /// </summary>
        private string BringDocumentToFront(IModelDoc2 document, out IModelDoc2 previousFront)
        {
            previousFront = null;
            IModelDoc2 front = null;
            try { front = _sw.ActiveDoc as IModelDoc2; } catch { }
            if (SameComObject(front, document)) return null;
            previousFront = front;
            int activationErrors;
            string problem;
            if (!DocumentByPath.ActivateAndReadBack(_sw, document, out activationErrors, out problem))
                return problem;
            // C4-3: brought here with swDontRebuildActiveDoc, so a pending rebuild
            // is done now, before the file is written from it.
            string rebuilt = DocumentByPath.RebuildIfActivationAsked(document, activationErrors);
            if (rebuilt != null) _log("[agent] " + rebuilt);
            return null;
        }

        /// <summary>
        /// The same COM object? ReferenceEquals first, since .NET almost always
        /// reuses one wrapper for one COM object; when it does not, the IUnknown
        /// pointers, which are the real identity (as PlanExecutor.SameObject).
        /// Both Release calls are needed: GetIUnknownForObject adds a reference,
        /// and a count that does not go back is a document SOLIDWORKS cannot close.
        /// </summary>
        private static bool SameComObject(object a, object b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            IntPtr pa = IntPtr.Zero, pb = IntPtr.Zero;
            try
            {
                pa = Marshal.GetIUnknownForObject(a);
                pb = Marshal.GetIUnknownForObject(b);
                return pa == pb;
            }
            catch { return false; }
            finally
            {
                if (pa != IntPtr.Zero) Marshal.Release(pa);
                if (pb != IntPtr.Zero) Marshal.Release(pb);
            }
        }

        /// <summary>
        /// Why <paramref name="path"/> cannot take STEP AP242, or null when it
        /// can. IModelDocExtension~PublishSTEP242File.html, Path: «Full qualified
        /// path to which to export the SOLIDWORKS MBD 3D part or assembly; use
        /// .STP for the file name extension»; the probe probe_step_ap242 measured
        /// a .step path answering 1 (swPublishStep242_InvalidPath). Refused here,
        /// before the call, with the reason. No interop in the signature: the
        /// contract group of Z-C21.13 calls it without SOLIDWORKS.
        /// </summary>
        private static string Step242PathProblem(string path)
        {
            string extension;
            try { extension = (Path.GetExtension(path ?? "") ?? "").ToLowerInvariant(); }
            catch (Exception ex) { return "I cannot read the path «" + path + "»: " + ex.Message; }
            if (extension == ".stp") return null;
            if (extension == ".step")
                return "STEP AP242 is written by PublishSTEP242File, and its page asks for a .stp path " +
                       "(«use .STP for the file name extension»): with a .step path it answers " +
                       "swPublishStep242_InvalidPath (measured on 29/9/2026). Ask again with the same path " +
                       "ending in .stp instead of «" + path + "».";
            return "The path «" + path + "» ends in «" + extension + "», and STEP AP242 is written by " +
                   "PublishSTEP242File, which wants a path ending in .stp.";
        }

        /// <summary>
        /// The refusal for an answer of PublishSTEP242File, or null for success.
        /// swStep242Error_e (its page in estratto_swconst): «Status codes for
        /// exporting SOLIDWORKS MBD 3D PDF files to STEP 242»,
        /// swPublishStep242_Success 0, swPublishStep242_InvalidPath 1,
        /// swPublishStep242_UnknownError 3, swPublishStep242_MBDLicenseNotAvailable
        /// 4. A value the enum does not list is not taken for a success. No
        /// interop in the signature: the contract group of Z-C21.13 calls it
        /// without SOLIDWORKS.
        /// </summary>
        private static string Step242Refusal(int code, string path)
        {
            switch (code)
            {
                case (int)swStep242Error_e.swPublishStep242_Success:
                    return null;
                case (int)swStep242Error_e.swPublishStep242_MBDLicenseNotAvailable:
                    return "STEP AP242 is not available on this machine: PublishSTEP242File answered " + code +
                           " (swPublishStep242_MBDLicenseNotAvailable). In SOLIDWORKS 2021 AP242 is written only " +
                           "by SOLIDWORKS MBD («Publish STEP 242 File»), and it needs the SOLIDWORKS MBD licence, " +
                           "which is not available on this machine. AP214 (the default, which also carries the colours) " +
                           "and AP203 (the oldest and the most widely read) are available: ask for one of those.";
                case (int)swStep242Error_e.swPublishStep242_InvalidPath:
                    return "PublishSTEP242File refused the path «" + path + "»: it answered " + code +
                           " (swPublishStep242_InvalidPath). Its page asks for a full path ending in .stp.";
                case (int)swStep242Error_e.swPublishStep242_UnknownError:
                    return "PublishSTEP242File failed without saying why: it answered " + code +
                           " (swPublishStep242_UnknownError), and there is no AP242 file to check.";
                default:
                    return "PublishSTEP242File answered " + code + ", a value swStep242Error_e does not list " +
                           "(0 success, 1 invalid path, 3 unknown error, 4 no MBD licence): I do not take it " +
                           "for a success.";
            }
        }

        /// <returns>
        /// The IGES toggles of <see cref="IgesTogglesTurnedOff"/> that are NOT
        /// off after setting them (Z-C21.10 N59, review): still on, or
        /// unreadable. Empty when all three read back off.
        ///
        /// ISldWorks~SetUserPreferenceToggle.html gives no return value to read
        /// (C# «void SetUserPreferenceToggle(System.int UserPreferenceValue,
        /// System.bool OnFlag)»), so each toggle is read again with
        /// ISldWorks~GetUserPreferenceToggle.html («True if UserPreferenceToggle
        /// is toggled on; false if toggled off»). The census of the file sees a
        /// solid replaced by curves, but not stray sketch entities or 3D curves
        /// written NEXT TO the solid: for those, this read-back is the only
        /// witness, and the answer carries it.
        /// </returns>
        private List<string> SetIgesPreferences(int representation)
        {
            try { _sw.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swIGESRepresentation, representation); } catch { }
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swIGESExportSolidAndSurface, true); } catch { }
            // N59: the solid (or the trimmed surfaces), and nothing next to it.
            foreach (var t in IgesTogglesTurnedOff)
                try { _sw.SetUserPreferenceToggle((int)t, false); } catch { }

            var notOff = new List<string>();
            foreach (var t in IgesTogglesTurnedOff)
            {
                bool on = true;   // unreadable is not off
                try { on = _sw.GetUserPreferenceToggle((int)t); } catch { }
                if (on) notOff.Add(t.ToString());
            }
            return notOff;
        }

        /// <summary>
        /// The `FILE_SCHEMA` of the STEP, and how many entities the DATA section has.
        /// Null if the file does not start with «ISO-10303-21».
        /// </summary>
        private static string StepSchema(string filePath, out int entity, out string why)
        {
            entity = -1;
            why = "";
            try
            {
                string schema = null;
                int howMany = 0;
                bool goodStart = false, firstLine = true, insideTheData = false;
                foreach (var line in File.ReadLines(filePath))
                {
                    if (firstLine)
                    {
                        firstLine = false;
                        goodStart = line.TrimStart().StartsWith("ISO-10303-21", StringComparison.Ordinal);
                        if (!goodStart) break;
                    }
                    if (schema == null)
                    {
                        int i = line.IndexOf("FILE_SCHEMA", StringComparison.Ordinal);
                        if (i >= 0)
                        {
                            int opens = line.IndexOf('\'', i);
                            int closes = opens < 0 ? -1 : line.IndexOf('\'', opens + 1);
                            if (opens >= 0 && closes > opens)
                                schema = line.Substring(opens + 1, closes - opens - 1);
                        }
                    }
                    if (line.StartsWith("DATA;", StringComparison.Ordinal)) { insideTheData = true; continue; }
                    if (line.StartsWith("ENDSEC;", StringComparison.Ordinal)) { insideTheData = false; continue; }
                    if (insideTheData && line.TrimStart().StartsWith("#", StringComparison.Ordinal)) howMany++;
                }
                if (!goodStart)
                {
                    why = "the file has no «ISO-10303-21» at its start";
                    return null;
                }
                entity = howMany;
                why = "read from the file: the FILE_SCHEMA and the «#» records of the DATA section";
                return schema;
            }
            catch (Exception ex)
            {
                why = "I could not read the file: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// From `FILE_SCHEMA` to the protocol name. The three names are those
        /// of the STANDARD, not of SOLIDWORKS: AP203 declares itself
        /// «CONFIG_CONTROL_DESIGN», AP214 «AUTOMOTIVE_DESIGN», AP242 with its long
        /// name.
        /// </summary>
        private static string FromSchema(string schema)
        {
            if (string.IsNullOrEmpty(schema)) return null;
            string s = schema.ToUpperInvariant();
            if (s.Contains("AP242")) return "AP242";
            if (s.Contains("AUTOMOTIVE_DESIGN")) return "AP214";
            if (s.Contains("CONFIG_CONTROL_DESIGN")) return "AP203";
            return null;
        }

        /// <summary>
        /// How many PARAMETER records the IGES has. An IGES is made of lines of
        /// eighty characters, and column 73 says which section they belong to:
        /// S header, G global, D directory, P parameters, T terminator. A file
        /// without even one «P» record has the headers and nothing inside.
        /// </summary>
        private static int RecordIges(string filePath, out string why)
        {
            why = "";
            try
            {
                int howMany = 0;
                foreach (var line in File.ReadLines(filePath))
                    if (line.Length >= 73 && (line[72] == 'P' || line[72] == 'p')) howMany++;
                why = "counting the records with «P» in column 73 of the IGES file";
                return howMany;
            }
            catch (Exception ex)
            {
                why = "I could not read the file: " + ex.Message;
                return -1;
            }
        }

        /// <summary>IGES 5.3 type 186, Manifold Solid B-Rep Object: the solid of a BREP file (one for the one body of `iges_solids`).</summary>
        private const int IgesManifoldSolidType = 186;

        /// <summary>
        /// IGES 5.3 surface entity types: plane 108, parametric spline surface
        /// 114, ruled 118, surface of revolution 120, tabulated cylinder 122,
        /// rational B-spline surface 128, offset 140, bounded 143, trimmed 144,
        /// and the analytic surfaces 190-198 (plane, cylinder, cone, sphere,
        /// torus). Faces (510) and shells (514) are topology, not surfaces, and
        /// are left out: a face always points at one of these.
        /// </summary>
        private static readonly HashSet<int> IgesSurfaceTypes =
            new HashSet<int> { 108, 114, 118, 120, 122, 128, 140, 143, 144, 190, 192, 194, 196, 198 };

        /// <summary>
        /// The entities of the IGES by kind (N59), read from its DIRECTORY
        /// ENTRY section: the lines with «D» in column 73, two per entity, the
        /// first of the two carrying the entity type number in columns 1-8.
        ///
        /// Checked by hand on 28/9/2026 against the file `iges_solids` writes
        /// (block 100 x 100 x 20, BREP, SOLIDWORKS 2021): 106 «D» lines, 53
        /// entities, one of type 186, six of type 190 (plane surface), six 510
        /// faces, one 514 shell, twelve 110 lines. So: solids 1, surfaces 6.
        /// A wireframe file has lines and points and neither of the two.
        /// </summary>
        private static bool IgesEntityCensus(
            string path, out int entities, out int solids, out int surfaces, out string how)
        {
            entities = 0; solids = 0; surfaces = 0;
            try
            {
                int directoryLines = 0;
                foreach (var line in File.ReadLines(path))
                {
                    if (line.Length < 73 || (line[72] != 'D' && line[72] != 'd')) continue;
                    directoryLines++;
                    if (directoryLines % 2 == 0) continue;   // the second line of the entry
                    int type;
                    if (!int.TryParse(line.Substring(0, 8).Trim(), System.Globalization.NumberStyles.Integer,
                                      System.Globalization.CultureInfo.InvariantCulture, out type))
                    {
                        how = "directory line " + directoryLines + " has no entity type in columns 1-8";
                        return false;
                    }
                    entities++;
                    if (type == IgesManifoldSolidType) solids++;
                    else if (IgesSurfaceTypes.Contains(type)) surfaces++;
                }
                if (directoryLines % 2 != 0)
                {
                    how = "the directory section has " + directoryLines + " lines, and an entry takes two";
                    return false;
                }
                how = "from the directory section: the lines with «D» in column 73, the entity type " +
                      "in columns 1-8 of the first line of each entry";
                return true;
            }
            catch (Exception ex)
            {
                how = "could not read the file: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// The volume of the file just written, reopened in SOLIDWORKS and
        /// closed again. It is the only measurement that says the GEOMETRY
        /// survived the translation: the size of a file does not say it,
        /// nor does the number of entities.
        ///
        /// THE DOCUMENT IS ALWAYS CLOSED, and at the end the one from before is
        /// reactivated: opening a file and leaving it there means the operation
        /// after works on a document that is not the one the model believes in.
        ///
        /// ── `LoadFile4` AND NOT `OpenDoc6`, AND THE ERROR SAYS SOMETHING ELSE ──
        ///
        /// `OpenDoc6` on a STEP or an IGES **opens nothing** and answers
        /// `swFileRequiresRepairError = 2097152` — «the file requires a
        /// repair». The file has nothing to repair: we just wrote it
        /// ourselves, and `LoadFile4` opens it without a warning. Measured on
        /// 15/9/2026 on a cube of 200,000 mm³: `OpenDoc6` with `swDocPART`
        /// 2097152, with `swDocIMPORTED_PART` 1024 («invalid file type»),
        /// `LoadFile4(percorso, "", null)` **open, one body, 200000 mm³**.
        ///
        /// It is a message that sends one looking for the fault on the wrong
        /// side: whoever reads it goes to look at the file, and the file is
        /// fine. For the neutral formats the door is `LoadFile4`.
        /// </summary>
        private double VolumeOnReopen(string filePath, out string comE)
        {
            comE = "";
            IModelDoc2 reopened = null;
            // The document to go back to, by REFERENCE (Z-C21.13, pattern S18).
            IModelDoc2 previousFront = null;
            try { previousFront = _sw.IActiveDoc2; }
            catch { }
            bool volumeRead = false;
            try
            {
                int errors = 0;
                reopened = _sw.LoadFile4(filePath, "", null, ref errors) as IModelDoc2;
                if (reopened == null)
                {
                    comE = "LoadFile4 opened nothing (code " + errors + ")";
                    return -1;
                }
                double? v = VolumeMm3(reopened);
                if (v == null)
                {
                    comE = "reopened, but the volume of the translated geometry could not be read";
                    return -1;
                }
                comE = "reopened as a part and closed again; volume read from the translated geometry";
                volumeRead = true;
                return v.Value;
            }
            catch (Exception ex)
            {
                comE = "exception while reopening: " + ex.Message;
                return -1;
            }
            finally
            {
                // BY REFERENCE, AND READ BACK (Z-C21.13, findings M4 and N11).
                // The imported document was never saved, and its title is the
                // stem of the file: the stem of the part itself when the export
                // sits next to it. CloseDoc(that title) could close the ORIGINAL,
                // without saving. When LoadFile4 leaves it in front,
                // DocumentByPath closes it with the empty name, which names the
                // active document; when it does not, its title is used only if
                // no other open document answers to it. The answer says
                // «closed» only if the read back finds it gone.
                if (reopened != null)
                {
                    var closing = DocumentByPath.CloseAndReadBack(_sw, reopened);
                    if (!closing.IsClosed)
                    {
                        string notClosed = "the reopened document is NOT closed: " + closing.Message +
                                           "; close it by hand, without saving";
                        comE = volumeRead
                            ? "reopened as a part, volume read from the translated geometry, and " + notClosed
                            : comE + "; " + notClosed;
                        _log("[agent] " + notClosed);
                    }
                    else if (closing.AlsoClosed.Count > 0)
                        _log("[agent] closing the reopened file: " + closing.Message);
                }
                // Back to the document that was in front, by its path, with
                // swDontRebuildActiveDoc: this was ActivateDoc3(title, false, 0),
                // and 0 is swUserDecision, a dialog the call waits on (L13-3).
                if (previousFront != null)
                {
                    int activationErrors;
                    string problem;
                    if (!DocumentByPath.ActivateAndReadBack(_sw, previousFront, out activationErrors, out problem))
                        _log("[agent] after reopening the file: " + problem);
                    else
                    {
                        string stale = DocumentByPath.PutBackNeedsRebuild(previousFront, activationErrors);
                        if (stale != null) _log("[agent] after reopening the file: " + stale);
                    }
                }
            }
        }
    }
}
