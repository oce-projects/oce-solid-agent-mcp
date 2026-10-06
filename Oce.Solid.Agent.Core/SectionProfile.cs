using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// How big the section of a weldment profile is.
    ///
    /// THE DEFECT THIS FILE WAS BORN FROM
    /// On 9 September 2026 the designer asked for a 1000x800 frame in
    /// 80x80x5 tubing and measured 1080x880 on the finished part. It is not a
    /// calculation error: the path of a structural member is the CENTRE
    /// LINE, and SolidWorks centres the profile on that line. A path
    /// drawn on the edges of a 1000x800 rectangle makes the bars protrude
    /// by half a section per side, that is 40 mm, that is +80 per dimension. And the
    /// summary shown before executing said «1000x800».
    ///
    /// To pull the path back one must know how big the section is. And
    /// it cannot be read:
    ///   - the name of the configuration is NOT a source: in the same
    ///     library «80X80X3», «50x50x3» and «20x20x1,5» coexist, and for an
    ///     IPE 200 the name does not say 200x100 at all;
    ///   - the profile sketch has no readable bounding box: ISketch does not
    ///     expose any GetBox (verified by reflection on the interop
    ///     29.5.1.1: there are only GetSketchPoints, GetSketchPoints2,
    ///     GetSketchSegments, GetSketchPointsCount, IsBoundaryBoxSketch);
    ///   - Document Manager, which would read the .sldlfp without opening it, on
    ///     this workstation has no licence (CLASS_E_NOTLICENSED).
    ///
    /// WHAT IS DONE THEN: a TEST FRAME is built in a disposable
    /// document and it is measured how far it protrudes beyond its own path. It
    /// interprets nothing: it looks at the part.
    ///
    /// AND A FRAME IS BUILT, NOT A BAR. The first version probed one
    /// bar per direction and took its bounding box. It works for symmetric profiles and GETS IT WRONG for the
    /// others: measured on 9 September 2026, a 1200x600 frame in
    /// 80X50X5 rectangular tube came out 1170x600. The isolated bar laid along Y
    /// occupies 80 in X; the same bar INSIDE THE GROUP
    /// of the frame occupies 50. The orientation of the profile does not depend only on
    /// the direction of the segment: it depends on the group. So the
    /// real thing is measured, with the same options — plane, rotation, corner
    /// treatment, gap.
    ///
    /// The overhang does not depend on how big the frame is: a single measure
    /// holds for all dimensions.
    ///
    /// COST AND REMEDY: a test needs a new document, a sketch, a structural
    /// member and a closing — a few seconds, not zero. The
    /// result is kept in memory and on disk, per machine: the second time
    /// the same frame is asked for, nothing is measured any more.
    /// </summary>
    public static class SectionProfile
    {
        private static readonly object _lock = new object();
        private static Dictionary<string, double[]> _memory;

        /// <summary>
        /// How far a FRAME protrudes beyond its own path, on the two axes
        /// of the plane. It serves to pull the path back to get the outer
        /// dimensions that the designer asked for.
        ///
        /// WHY A TEST FRAME IS BUILT AND A BAR IS NOT MEASURED
        /// The first version probed one bar per direction and took
        /// its bounding box. It works for symmetric profiles and GETS IT WRONG for the
        /// others: measured on 9 September 2026, a 1200x600 frame in
        /// 80X50X5 rectangular tube came out 1170x600. The isolated bar laid along Y
        /// occupies 80 in X, but the same bar INSIDE THE GROUP of the frame
        /// occupies 50: the orientation of the profile does not depend only on the
        /// direction of the segment, it depends on the group.
        ///
        /// So it is not guessed: a test frame is built with the
        /// same options — same plane, same rotation, same corner
        /// treatment, same gap — and it is measured how far it protrudes. The
        /// overhang does not depend on how big the frame is, so a single
        /// measure holds for all dimensions.
        ///
        /// It costs a disposable document, and it is kept: the second time
        /// the same frame is asked for, nothing is measured any more.
        /// </summary>
        /// <returns>
        /// {overhangU, overhangV} in mm, or null if it could not be
        /// measured. In that case WHOEVER CALLS MUST FAIL, not guess:
        /// a frame with the wrong dimensions is the defect that this
        /// file exists to close.
        /// </returns>
        /// <remarks>
        /// THIS SIGNATURE DOES NOT CHANGE: the bench calls this method by
        /// reflection (Bench/Program.cs, Sections), and an overload would make
        /// `GetMethod("FrameOverhangs")` ambiguous. The work is in
        /// <see cref="FrameOverhang"/>, which also says where the overhang
        /// came from.
        /// </remarks>
        public static double[] FrameOverhangs(
            ISldWorks sw, string profile, string configuration,
            int axisU, int axisV, double rotationDeg,
            int cornerTreatment, double gapMm, Action<string> log)
        {
            string source;
            return FrameOverhang(sw, profile, configuration, axisU, axisV,
                                 rotationDeg, cornerTreatment, gapMm, log, out source);
        }

        /// <summary>The overhang was measured now, on a trial frame.</summary>
        public const string OverhangMeasured = "measured";

        /// <summary>The overhang was remembered: this session or the archive file.</summary>
        public const string OverhangFromArchive = "archive";

        /// <summary>
        /// <see cref="FrameOverhangs"/>, saying where the answer came from
        /// (<see cref="OverhangMeasured"/> or <see cref="OverhangFromArchive"/>;
        /// null when there is no answer).
        ///
        /// WHAT IS REMEMBERED, AND FOR HOW LONG (Z-C21.10 N36). Until library
        /// 0.140.0 two things went wrong with the memory:
        ///   - a FAILED probe was kept as null and returned as null for the
        ///     rest of the session: one passing hiccup (a network drive that
        ///     stutters, a template that does not open) blocked that profile
        ///     until SOLIDWORKS was closed;
        ///   - the key had no date and no size of the library file: a library
        ///     corrected afterwards kept its old overhang forever, in memory
        ///     and in the archive on disk.
        /// Now a failure is not remembered (the next call measures again), and
        /// the key carries the file's LastWriteTimeUtc and length: a library
        /// that changes is a new key. When the file cannot be read for its
        /// date, nothing is remembered and the probe runs every time — slower,
        /// never stale. A frame that comes out wrong on a remembered overhang
        /// drops it with <see cref="ForgetFrameOverhang"/>.
        /// </summary>
        public static double[] FrameOverhang(
            ISldWorks sw, string profile, string configuration,
            int axisU, int axisV, double rotationDegrees,
            int cornerTreatment, double gapMm, Action<string> log, out string source)
        {
            source = null;
            if (sw == null || string.IsNullOrWhiteSpace(profile)) return null;
            if (axisU < 0 || axisU > 2 || axisV < 0 || axisV > 2 || axisU == axisV) return null;
            log = log ?? (m => { });

            string key = FrameKeyOnDisk(profile, configuration, axisU, axisV,
                                        rotationDegrees, cornerTreatment, gapMm);
            return RememberOrMeasure(key,
                () => TestFrame(sw, profile, configuration, axisU, axisV,
                                    rotationDegrees, cornerTreatment, gapMm, log),
                log, out source);
        }

        /// <summary>
        /// The memory of <see cref="FrameOverhang"/>, without SOLIDWORKS: the
        /// remembered overhang for <paramref name="key"/>, or else what
        /// <paramref name="measure"/> returns — remembered only when it is not
        /// null (Z-C21.10 N36). A null key remembers nothing. It takes the
        /// measurement as a delegate so that a contract test can drive it
        /// with no CAD at all: a measurement that fails, then one that
        /// succeeds, must be asked twice.
        /// </summary>
        internal static double[] RememberOrMeasure(
            string key, Func<double[]> measure, Action<string> log, out string source)
        {
            source = null;
            log = log ?? (m => { });
            if (key != null)
            {
                lock (_lock)
                {
                    Load();
                    double[] known;
                    if (_memory.TryGetValue(key, out known) && known != null)
                    {
                        source = OverhangFromArchive;
                        log(string.Format(CultureInfo.InvariantCulture,
                            "[section] overhang {0:0.##}/{1:0.##} remembered, not measured now " +
                            "(archive: {2})", known[0], known[1], ReadArchivePath()));
                        return new[] { known[0], known[1] };
                    }
                }
            }

            double[] measured = null;
            try { measured = measure == null ? null : measure(); }
            catch (Exception ex) { log("[section] the trial frame failed: " + ex.Message); }

            // A FAILURE IS NOT REMEMBERED: the next call measures again.
            if (measured == null || measured.Length < 2) return null;
            source = OverhangMeasured;

            if (key != null)
            {
                lock (_lock)
                {
                    Load();
                    _memory[key] = new[] { measured[0], measured[1], 0.0 };
                    Save(log);
                }
            }
            return new[] { measured[0], measured[1] };
        }

        /// <summary>
        /// Drops a remembered overhang, from memory and from the archive file
        /// (Z-C21.10 N36): the frame built with it came out wrong, and keeping
        /// it would rebuild the same wrong frame at every attempt. The
        /// arguments are those of <see cref="FrameOverhang"/>.
        /// </summary>
        /// <returns>True if there was an entry and it is gone.</returns>
        public static bool ForgetFrameOverhang(
            string profile, string configuration, int axisU, int axisV,
            double rotationDegrees, int cornerTreatment, double gapMm, Action<string> log)
        {
            string key = FrameKeyOnDisk(profile, configuration, axisU, axisV,
                                        rotationDegrees, cornerTreatment, gapMm);
            return ForgetKey(key, log);
        }

        /// <summary>
        /// <see cref="ForgetFrameOverhang"/> by key, without the disk read of
        /// the library's date and size: what a contract test calls.
        /// </summary>
        internal static bool ForgetKey(string key, Action<string> log)
        {
            log = log ?? (m => { });
            if (key == null) return false;
            lock (_lock)
            {
                Load();
                if (!_memory.Remove(key)) return false;
                Save(log);
            }
            log("[section] overhang dropped from the archive, it will be measured again: " + key);
            return true;
        }

        /// <summary>
        /// Where the archive of measured overhangs lives, to name it in a
        /// message: a rejection that blames a remembered number must say
        /// where it is remembered.
        /// </summary>
        public static string ArchivePath
        {
            get { return ReadArchivePath(); }
        }

        /// <summary>
        /// The first field of every key of this version. An archive line that
        /// does not start with it was written before date and size were part
        /// of the key, can never be matched again, and is left out on load.
        /// </summary>
        private const string KeyPrefix = "frame-v2";

        /// <summary>
        /// The key of a frame overhang, from the library file's date and
        /// length as well as the frame options (Z-C21.10 N36). Pure: no disk,
        /// no SOLIDWORKS — a contract test can call it.
        /// </summary>
        internal static string FrameKey(
            string profile, long lastWriteUtcTicks, long length, string configuration,
            int axisU, int axisV, double rotationDegrees, int cornerTreatment, double gapMm)
        {
            var c = CultureInfo.InvariantCulture;
            return string.Join("|",
                KeyPrefix,
                (profile ?? "").ToUpperInvariant(),
                lastWriteUtcTicks.ToString(c),
                length.ToString(c),
                configuration ?? "",
                axisU.ToString(c),
                axisV.ToString(c),
                Math.Round(rotationDegrees, 3).ToString("0.###", c),
                cornerTreatment.ToString(c),
                Math.Round(gapMm, 3).ToString("0.###", c));
        }

        /// <summary>True for a key written by this version (see <see cref="KeyPrefix"/>).</summary>
        internal static bool IsCurrentKey(string key)
        {
            return key != null && key.StartsWith(KeyPrefix + "|", StringComparison.Ordinal);
        }

        /// <summary>
        /// <see cref="FrameKey"/> with the date and length read from the disk
        /// now; null when the file cannot be read, and then nothing is
        /// remembered.
        /// </summary>
        private static string FrameKeyOnDisk(
            string profile, string configuration, int axisU, int axisV,
            double rotationDegrees, int cornerTreatment, double gapMm)
        {
            try
            {
                var file = new FileInfo(profile);
                if (!file.Exists) return null;
                return FrameKey(profile, file.LastWriteTimeUtc.Ticks, file.Length, configuration,
                                axisU, axisV, rotationDegrees, cornerTreatment, gapMm);
            }
            catch { return null; }
        }

        /// <summary>The side of the test frame, in mm.</summary>
        private const double TEST_SIDE_U = 1000;
        private const double TEST_SIDE_V = 700;

        /// <summary>
        /// Builds a test frame and returns how far it protrudes beyond
        /// the path, on the two axes: {overhangU, overhangV} in mm.
        /// </summary>
        private static double[] TestFrame(
            ISldWorks sw, string profile, string configuration,
            int axisU, int axisV, double rotationDeg,
            int cornerTreatment, double gapMm, Action<string> log)
        {
            if (!File.Exists(profile))
            {
                log("[section] the profile does not exist: " + profile);
                return null;
            }

            // The document to go back to is kept by REFERENCE, not by title
            // (Z-C21.13, pattern S18): DocumentByPath.ActivateAndReadBack
            // brings it back by its path and reads back that it is in front.
            IModelDoc2 previousFront = null;
            try { previousFront = sw.ActiveDoc as IModelDoc2; }
            catch { }

            // THE TRIAL MEMBER OPENS THE PROFILE LIBRARY TOO (Z-C21.10 N31).
            // InsertStructuralWeldment5 below is a real insertion, and
            // SOLIDWORKS opens the library by itself and leaves it modified,
            // as WeldmentOperations measured for the real one. Closing the
            // trial document may or may not take the library with it: the
            // guide says «This method also closes any non-active hidden
            // documents» (ISldWorks~CloseDoc.html), and whether a library
            // opened this way is one of them is not measured. So the state
            // is read before, and the same rule as the real insertion is
            // applied after (SwOperations.CloseLibraryAgain): what we opened
            // or dirtied is closed without saving, then read back. It matters
            // beyond this probe: a library left modified here looks, to the
            // next frame, like one somebody was working on, and that frame
            // then leaves it alone.
            var libraryBefore = SwOperations.LibraryState(sw, profile);

            IModelDoc2 doc = null;
            try
            {
                string model = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                if (string.IsNullOrWhiteSpace(model) || !File.Exists(model))
                {
                    log("[section] there is no default part template.");
                    return null;
                }
                doc = sw.NewDocument(model, 0, 0, 0) as IModelDoc2;
                if (doc == null) { log("[section] trial document not created."); return null; }

                double u = TEST_SIDE_U / 2000.0, v = TEST_SIDE_V / 2000.0;
                var angles = new[]
                {
                    Point(axisU, -u, axisV, -v),
                    Point(axisU,  u, axisV, -v),
                    Point(axisU,  u, axisV,  v),
                    Point(axisU, -u, axisV,  v),
                };

                var sketches = doc.SketchManager;
                doc.ClearSelection2(true);
                sketches.Insert3DSketch(true);
                var created = new List<ISketchSegment>();
                for (int i = 0; i < 4; i++)
                {
                    var a = angles[i];
                    var b = angles[(i + 1) % 4];
                    var seg = sketches.CreateLine(a[0], a[1], a[2], b[0], b[1], b[2])
                        as ISketchSegment;
                    if (seg == null) { log("[section] trial segment not created."); return null; }
                    created.Add(seg);
                }
                sketches.Insert3DSketch(true);

                var theGroup = doc.FeatureManager.CreateStructuralMemberGroup()
                    as IStructuralMemberGroup;
                if (theGroup == null) { log("[section] trial group not created."); return null; }
                theGroup.Segments = created.Cast<SketchSegment>().ToArray();
                if (Math.Abs(rotationDeg) > 1e-9)
                    theGroup.Angle = rotationDeg * Math.PI / 180.0;
                theGroup.ApplyCornerTreatment = cornerTreatment != 0;
                if (cornerTreatment != 0) theGroup.CornerTreatmentType = cornerTreatment;
                if (gapMm > 0)
                {
                    theGroup.GapWithinGroup = gapMm / 1000.0;
                    theGroup.GapForOtherGroups = gapMm / 1000.0;
                }

                // Option 1, as the real member (WeldmentOperations, where the
                // measure is): 0 and 3 give no feature, and 1 and 2 gave the
                // same bodies on the round tube and on the square one in every
                // scene of the L9-6 probe (28/9/2026).
                var feature = doc.FeatureManager.InsertStructuralWeldment5(
                    Path: profile,
                    ConnectedSegmentsOption: 1,
                    AllowProtrusion: false,
                    Groups: new[] { (StructuralMemberGroup)theGroup },
                    ConfigurationName: configuration) as IFeature;
                if (feature == null)
                {
                    log("[section] the trial frame was not created: profile «" + profile +
                        "» size «" + configuration + "».");
                    return null;
                }
                try { doc.EditRebuild3(); } catch { }

                var bodies = (doc as IPartDoc).GetBodies2(
                    (int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null || bodies.Length == 0) return null;
                // Approximate ON PURPOSE: paired with the same approximation in
                // WeldmentOperations.BodiesBoundingBox, so that the two cancel out.
                var total = ApiHelpers.UnionOfBoxes(bodies, c => ApiHelpers.ApproximateBoundingBox(c as IBody2));
                if (total == null) return null;

                double boundingBoxU = (total[3 + axisU] - total[axisU]) * 1000.0;
                double boundingBoxV = (total[3 + axisV] - total[axisV]) * 1000.0;
                double protrudesU = boundingBoxU - TEST_SIDE_U;
                double protrudesV = boundingBoxV - TEST_SIDE_V;

                // A negative or absurd overhang means that something else
                // was measured: better not to know than to know it wrong.
                if (protrudesU < -0.01 || protrudesV < -0.01 ||
                    protrudesU > TEST_SIDE_U || protrudesV > TEST_SIDE_V)
                {
                    log(string.Format(CultureInfo.InvariantCulture,
                        "[section] overhangs out of scale on the trial frame: {0:0.##} / {1:0.##}",
                        protrudesU, protrudesV));
                    return null;
                }
                log(string.Format(CultureInfo.InvariantCulture,
                    "[section] trial frame {0}x{1}: bounding box {2:0.##}x{3:0.##}, " +
                    "overhangs {4:0.##}/{5:0.##}",
                    TEST_SIDE_U, TEST_SIDE_V, boundingBoxU, boundingBoxV, protrudesU, protrudesV));
                return new[] { protrudesU, protrudesV };
            }
            finally
            {
                // THE TRIAL DOCUMENT, BY REFERENCE (Z-C21.13 N11). It was never
                // saved, so it has no path: it is closed as the active document
                // it is, or by a title no other open document answers to, and
                // read back. Until library 0.143.0 it was CloseDoc(its title),
                // and a new part's title can be the stem of an open file.
                if (doc != null)
                {
                    var closing = DocumentByPath.CloseAndReadBack(sw, doc);
                    if (!closing.IsClosed || closing.AlsoClosed.Count > 0)
                        log("[section] trial frame: " + closing.Message);
                }
                string leftModified = SwOperations.CloseLibraryAgain(sw, profile, libraryBefore, log);
                if (leftModified != null) log("[section] " + leftModified);
                // BACK TO THE DOCUMENT THAT WAS IN FRONT, by its path, with
                // swDontRebuildActiveDoc: this was ActivateDoc3(title, false, 0),
                // and 0 is swUserDecision, a dialog the call waits on (N8).
                if (previousFront != null)
                {
                    int activationErrors;
                    string problem;
                    if (!DocumentByPath.ActivateAndReadBack(sw, previousFront, out activationErrors, out problem))
                        log("[section] " + problem);
                    else
                    {
                        string stale = DocumentByPath.PutBackNeedsRebuild(previousFront, activationErrors);
                        if (stale != null) log("[section] " + stale);
                    }
                }
            }
        }

        private static double[] Point(int axisU, double u, int axisV, double v)
        {
            var p = new double[3];
            p[axisU] = u;
            p[axisV] = v;
            return p;
        }
        // ─────────────────────────────────────────────────────────────────
        // the memory, in memory and on disk
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The name of the folder under %LOCALAPPDATA% where the archive
        /// of the measured sections lives.
        ///
        /// It used to be written fixed «OpenPDM», and it escaped audit A1 because
        /// that looked for paths and names of people, not the name of the product inside
        /// a string. In a library that exists so as not to presuppose OpenPDM,
        /// writing into the OpenPDM folder is wrong twice over: it dirties
        /// someone else's house, and it ties the library to a product that might not be
        /// installed.
        ///
        /// Whoever hosts the library can put back the name they want. The OpenPDM
        /// add-in does so, so the archive already measured is not abandoned:
        /// it is not precious data — it is rebuilt by measuring — but throwing it
        /// away without saying so would cost a library reopening per profile.
        /// </summary>
        public static string ArchiveFolder
        {
            get { return _archiveFolder; }
            set
            {
                _archiveFolder =
                    string.IsNullOrWhiteSpace(value) ? "Oce.Solid.Agent" : value.Trim();
            }
        }
        private static string _archiveFolder = "Oce.Solid.Agent";

        private static string ReadArchivePath()
        {
            string folder = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                _archiveFolder);
            return Path.Combine(folder, "profile-sections.txt");
        }

        /// <summary>
        /// The format is one row per measure: key TAB dx TAB dy TAB dz.
        /// No JSON: only this file reads it, and a broken row must
        /// lose one measure, not the archive.
        /// </summary>
        private static void Load()
        {
            if (_memory != null) return;
            _memory = new Dictionary<string, double[]>(StringComparer.Ordinal);
            try
            {
                string f = ReadArchivePath();
                if (!File.Exists(f)) return;
                foreach (var line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    var parts = line.Split('\t');
                    if (parts.Length != 4) continue;
                    // A key without the file's date and size (before
                    // Z-C21.10 N36) is never asked again: left out here, and
                    // the next save does not write it back.
                    if (!IsCurrentKey(parts[0])) continue;
                    double a, b, c;
                    if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out a)) continue;
                    if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out b)) continue;
                    if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out c)) continue;
                    _memory[parts[0]] = new[] { a, b, c };
                }
            }
            catch { /* an unreadable archive costs one measure, not a failure*/ }
        }

        private static void Save(Action<string> log)
        {
            try
            {
                string f = ReadArchivePath();
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                var lines = _memory
                    .Where(v => v.Value != null)
                    .Select(v => string.Join("\t",
                        v.Key,
                        v.Value[0].ToString("0.####", CultureInfo.InvariantCulture),
                        v.Value[1].ToString("0.####", CultureInfo.InvariantCulture),
                        v.Value[2].ToString("0.####", CultureInfo.InvariantCulture)));
                File.WriteAllLines(f, lines, Encoding.UTF8);
            }
            catch (Exception ex) { log("[section] I could not save the archive: " + ex.Message); }
        }
    }
}
