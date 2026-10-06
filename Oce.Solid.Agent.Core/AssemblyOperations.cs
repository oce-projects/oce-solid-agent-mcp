using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE FIRST ASSEMBLY OPERATION (entry GA3).
    ///
    /// ── THE DEFECT THIS OPERATION MUST NOT HAVE ──────────────────────────
    ///
    /// The census of the assembly APIs (note `80q`) already says it, and it is the
    /// sentence to keep in front of you while writing this file:
    ///
    ///     «AddComponent5 places without mating: four free bodies that
    ///      look like an assembly».
    ///
    /// Inserting almost always succeeds. The part appears, the tree fills up, the
    /// rebuild is clean, and **nothing is constrained**. An assembly of four
    /// free components opens, rotates, exports, and is assembled wrong.
    /// So this operation, besides inserting, **says how many degrees of
    /// freedom remain**, and says it out loud in its answer: it is not
    /// a detail, it is the only measure that tells an assembly from four parts
    /// in the same room.
    ///
    /// ── TWO PRECONDITIONS THAT CANNOT BE GUESSED ─────────────────────────
    ///
    ///  1. **The document must be an assembly.** Calling this on a
    ///     part is not a user error to punish with an exception: it is a
    ///     thing that is said, with the type that was found.
    ///  2. **The component must already be OPEN.** `AddComponent5` takes a
    ///     path, but wants the document open in SOLIDWORKS: with a closed
    ///     file it answers `null` **without saying why**. It is the same
    ///     precondition as the drawing views, where it had already cost a run.
    ///     Here it is checked FIRST, and the refusal lists what is open:
    ///     a refusal that does not say the alternative forces you to guess.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome InsertComponent(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var assemblyDoc = model as IAssemblyDoc;
            if (assemblyDoc == null)
                return Failed(
                    "This is not an assembly, it is a document of type «" +
                    DocumentTypeName(TypeOf(model)) + "». Components are " +
                    "inserted only in an assembly: open one, or create it, and " +
                    "then call me again.");

            string filePath = Str(a, "path", null);
            if (string.IsNullOrWhiteSpace(filePath))
                return Failed(
                    "You did not tell me which component to insert: «path» is the " +
                    "full path of the file, and it must be one of the documents " +
                    "already open. You find them in «documents_open» of the state.");

            if (!File.Exists(filePath))
                return Failed(
                    "The file «" + filePath + "» does not exist. " + WhatIsOpen());

            // ── IS IT OPEN? ──────────────────────────────────────────────
            //
            // The check is here and not afterwards, because afterwards there would be
            // nothing to read: `AddComponent5` answers null and that is all, and a
            // null does not say whether the file is missing, whether it is closed, or whether the path is
            // written another way.
            if (!IsOpen(filePath))
                return Failed(
                    "«" + Path.GetFileName(filePath) + "» is not open in SOLIDWORKS, and " +
                    "the insertion wants a document that is already open: if I call it on " +
                    "a closed file it answers «not done» without telling me why. " +
                    WhatIsOpen());

            double x = Num(a, "x_mm", 0), y = Num(a, "y_mm", 0), z = Num(a, "z_mm", 0);
            string configuration = Str(a, "configuration", "");
            bool configurationAsked = !string.IsNullOrWhiteSpace(configuration);

            // ── THE CONFIGURATION ASKED FOR IS CHECKED BEFORE INSERTING ──────
            //
            // Z-C21.4, finding L11-1. Until library 0.145.0 «configuration» went
            // to AddComponent5 with UseConfigForPartReferences false, so it was
            // never used (see the call below), and a name the file does not have
            // was not refused either. The names are read from the open document
            // itself (the insertion needs it open anyway) and compared whole:
            // the two configurations of the company template share their first
            // word and differ in the part in angle brackets, which belongs to
            // the name (see TrueConfigurationName). A refusal here has
            // inserted nothing.
            if (configurationAsked)
            {
                var source = OpenDocumentAtPath(filePath);
                var sourceConfigurations = source == null ? new List<string>() : ConfigurationNames(source);
                if (sourceConfigurations.Count == 0)
                    return Failed(
                        "I could not read the configurations of «" + Path.GetFileName(filePath) + "», so I " +
                        "cannot check «" + configuration + "» before inserting it. Nothing was inserted: " +
                        "insert it without a configuration name, or open the file again and retry.");
                if (!sourceConfigurations.Contains(configuration, StringComparer.Ordinal))
                    return Failed(
                        "«" + Path.GetFileName(filePath) + "» has no configuration «" + configuration +
                        "». It has: «" + string.Join("», «", sourceConfigurations) + "»" +
                        ". The name is compared whole, the part in angle brackets included. Nothing was inserted.");
            }

            int before = HowManyComponentsInAssembly(assemblyDoc);

            IComponent2 born;
            try
            {
                // THE NAMED CONFIGURATION IS ASKED FOR WITH ITS SWITCH (Z-C21.4,
                // L11-1). IAssemblyDoc~AddComponent5.html: «UseConfigForPartReferences
                // If true, the configuration specified in ExistingConfigName is
                // used» and «ExistingConfigName Name of the configuration of the
                // loaded component; valid only if UseConfigForPartReferences =
                // true». Up to library 0.145.0 the switch was false and the name
                // was ignored: the component came in with ConfigOption 0,
                // swAddComponentConfigOptions_CurrentSelectedConfig, which
                // swAddComponentConfigOptions_e.html describes as «Add a part or
                // assembly in its last saved configuration». Without a name that
                // is still what happens. With a name, what the component really
                // references is read back below and a different one is refused.
                born = assemblyDoc.AddComponent5(
                    filePath,
                    (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                    "", configurationAsked, configurationAsked ? configuration : "",
                    x / 1000.0, y / 1000.0, z / 1000.0) as IComponent2;
            }
            catch (Exception ex)
            {
                return Failed("I could not insert the component: " + ex.Message);
            }

            // THE NAME IS TAKEN BEFORE THE REBUILD, and it is needed to find
            // the component AFTER. A rebuild can invalidate the COM pointers
            // to the components: going on querying the handle that
            // `AddComponent5` returned would mean measuring on an
            // old reference, and reading credible and wrong numbers.
            //
            // It is the house rule applied one level further: *the value
            // that comes back from whoever did the thing is not the measure of the thing* —
            // and not even the OBJECT that comes back is the object to measure on.
            string itemName = null;
            try { itemName = born == null ? null : born.Name2; } catch { }

            try { model.EditRebuild3(); } catch { }

            // Re-reading: it is searched by name among the components that the assembly
            // declares NOW. If it is not found, the old handle is kept
            // instead of giving up the measurement — saying so is better than keeping quiet,
            // but a suspect measurement is better than none only as long as
            // it is known to be suspect, and indeed the bench case watches it.
            var real = RereadComponent(assemblyDoc, itemName) ?? born;

            // ── THE MEASUREMENT IS ON THE ASSEMBLY, NOT ON THE ANSWER ────
            //
            // `AddComponent5` answered an `IComponent2`, and *the value that
            // comes back from whoever did the thing is not the measure of the thing*: we
            // count how many components there are now.
            int after = HowManyComponentsInAssembly(assemblyDoc);
            if (after != before + 1)
                return Failed(
                    "I asked to insert «" + Path.GetFileName(filePath) + "» and " +
                    (born == null ? "got no answer" : "was told yes") +
                    ", but the assembly had " + before + " components and now it has " + after +
                    ". I did not consider it inserted.");

            // THE CONFIGURATION THE COMPONENT REALLY REFERENCES (Z-C21.4, L11-1),
            // read on the component found again after the rebuild. When one was
            // asked for, a different one (or one that cannot be read) is not the
            // component that was asked for: it is taken out again, and «taken
            // out» is said only after the components are counted again.
            string referenced = null;
            try { referenced = real == null ? null : real.ReferencedConfiguration; } catch { }
            if (configurationAsked && !string.Equals(referenced, configuration, StringComparison.Ordinal))
                return Failed(
                    "I asked SOLIDWORKS for «" + Path.GetFileName(filePath) + "» in configuration «" +
                    configuration + "», and the new component references " +
                    (string.IsNullOrWhiteSpace(referenced) ? "a configuration I could not read" : "«" + referenced + "»") +
                    ". It is not the component you asked for." +
                    TakeOutInsertedComponent(model, assemblyDoc, real, before));

            var measurements = new Dictionary<string, object>
            {
                ["components_before"] = before,
                ["components_after"] = after,
            };

            if (!string.IsNullOrWhiteSpace(itemName)) measurements["component"] = itemName;

            if (!string.IsNullOrWhiteSpace(referenced)) measurements["configuration"] = referenced;

            // ── IS IT CONSTRAINED? THE MEASUREMENTS THAT REALLY WORK ─────
            //
            // The degrees of freedom CANNOT be had from this API in this
            // context: measured on 16/9/2026, `GetRemainingDOFs` answers
            // `Unavailable` on a just-built assembly, selecting the
            // component, after saving it to disk, and even after a true
            // coincident mate (`AddMate5` with `NoError`).
            // Four conditions, always the same.
            //
            // What is REALLY measured is two things, and they are enough to tell the
            // truth: whether the component is **fixed**, and **how many mates**
            // it has. `IsFixed()` works — it tells the first component
            // (nailed down) from the others — and `GetMates()` can be counted.
            //
            // A component that is NOT fixed with ZERO mates is just resting, and
            // this is known for certain. HOW MUCH it is free is not known, and
            // indeed it is not said.
            var deg = DegreesOfFreedom(real);
            measurements["degrees_of_freedom_state"] = deg.State;
            if (deg.Available) measurements["degrees_of_freedom_remaining"] = deg.HowMany;

            bool grounded = false;
            try { grounded = real != null && real.IsFixed(); } catch { }
            measurements["grounded"] = grounded;

            int mates = CountMates(real);
            if (mates >= 0) measurements["mates"] = mates;

            bool resting = !grounded && mates == 0;
            measurements["resting"] = resting;

            // WHERE THE ORIGIN ENDED UP, which is NOT the point you asked for.
            //
            // `AddComponent5(x, y, z)` places the **centre of the bounding box**.
            // A part with the origin on a face — the norm, if it was
            // extruded in one direction only — finds its origin shifted by
            // half the thickness. Measured on 17/9/2026 and predicted beforehand: thickness 4
            // asked at z=30 → origin at (0, -2, 30); thickness 10 asked at 0 →
            // (0, -5, 0).
            //
            // It is reported because it is what the model will read back in the state,
            // and seeing two different numbers without an explanation is worse than
            // seeing only one.
            var origin = ComponentOriginMm(real);
            if (origin != null) measurements["origin_mm"] = origin;

            // THE VERDICT IS A SENTENCE, NOT A NUMBER, and travels with the measurements.
            //
            // «degrees_of_freedom_remaining: 6» alone does not say what to do. And whoever
            // reads this answer may never have read the description of the
            // operation: the warning goes here, where it arrives anyway. It is
            // the same choice as `check_body_geometry`, which next to
            // `printable` writes `verdict`.
            string verdictValue =
                "Inserted «" + (itemName ?? Path.GetFileName(filePath)) + "» at " +
                AssemblyMm(x) + ", " + AssemblyMm(y) + ", " + AssemblyMm(z) + " mm. " +
                (grounded
                    ? "It is FIXED: it does not move because it is nailed down, not because it " +
                      "is mated. They are two different things."
                    : resting
                        ? "WARNING: it is not fixed and it has no mate — it is just " +
                          "laid down there. It moves if someone drags it, and the assembly " +
                          "looks assembled without being so."
                        : mates > 0
                            ? "It has " + mates +
                              (mates == 1 ? " mate" : " mates") +
                              ". How free it still is I cannot tell you: SOLIDWORKS does " +
                              "not give the remaining degrees of freedom from here."
                            : "I could not say whether it is constrained: I could " +
                              "not read either the mates or the state.");
            measurements["verdict"] = verdictValue;
            _log("[agent] " + verdictValue);

            return SucceededWithoutCreating(measurements);
        }

        // =================================================================

        /// <summary>
        /// The residual degrees of freedom of a component, or `-1` if they could not
        /// be read.
        ///
        /// `IComponent2.GetRemainingDOFs` has **twelve output parameters** —
        /// two points and four directions with their statuses — and the number
        /// that is needed is the **return value**, not something to deduce from
        /// those. The twelve serve to say *which* freedom remains, and that is a
        /// separate entry (GA4): here we count and that is all.
        ///
        /// On a just-inserted and unmated component **6** is expected:
        /// three translations and three rotations. A **zero** on a component
        /// that nobody mated does not mean «well constrained»: it means
        /// FIXED, which is another thing — that is why the state also writes
        /// `grounded` separately (entry GA1).
        /// </summary>
        /// <remarks>
        /// `internal` and not `private`: `StatoAssieme` reads it too (entry GA5),
        /// and two copies of the same reading are two things to keep aligned by
        /// hand.
        /// </remarks>
        /// <remarks>
        /// `internal`: `StatoAssieme` uses it too, for the same reason
        /// why `DegreesOfFreedom` is internal — one reading, one place
        /// to repair.
        /// </remarks>
        internal static int MatesOf(IComponent2 component)
        {
            return CountMates(component);
        }

        internal static DegreesOfFreedomRead DegreesOfFreedom(IComponent2 component)
        {
            var outside = new DegreesOfFreedomRead { State = "not read", HowMany = -1 };
            if (component == null) return outside;
            try
            {
                int s1, s2, s3, s4, s5, s6;
                MathPoint p1, p2;
                MathVector d1, d2, t1, t2;
                int outcome = component.GetRemainingDOFs(
                    out s1, out p1, out s2, out d1,
                    out s3, out p2, out s4, out d2,
                    out s5, out t1, out s6, out t2);

                outside.State = DofStateName(outcome);

                // THE COUNT IS DERIVED FROM THE STATES, and ONLY if the outcome says
                // that the information is there. The six `status` values are of
                // `swDofStatus_e`: `Unused` means «this slot
                // describes nothing», not «zero degrees».
                if (outcome == (int)swRemainingDofs_e.swRemainingDofs_Restricted ||
                    outcome == (int)swRemainingDofs_e.swRemainingDofs_Unrestricted)
                {
                    outside.HowMany = HowManyFree(s1, s2, s3, s4, s5, s6);
                }
            }
            catch { outside.State = "not read"; }
            return outside;
        }

        /// <summary>
        /// What `GetRemainingDOFs` answered, and whether a number came out of it.
        ///
        /// ── WHY IT IS NO LONGER AN `int` ─────────────────────────────────
        ///
        /// Up to 0.58.0 this method returned the value of
        /// `GetRemainingDOFs` directly and it was published as
        /// `degrees_of_freedom_remaining`. **It was wrong: that value is not a
        /// count, it is a status** (`swRemainingDofs_e`). Measured on
        /// 16/9/2026: on a just-built assembly it answers **2**, which means
        /// `Unavailable` — and it answers it **identical on a fixed component
        /// and on a free one**.
        ///
        /// The bridge printed «has still 2 degrees of freedom out of 6». Invented
        /// number, invented unit, and equal for a nailed-down part and one
        /// that was dancing. *A case in which two different values give the same
        /// result tabulates nothing* — and this published it as a measurement.
        /// </summary>
        internal sealed class DegreesOfFreedomRead
        {
            /// <summary>The name of the status: «non disponibile», «libero», …</summary>
            public string State;

            /// <summary>How many degrees, or `-1` if it could not be known.</summary>
            public int HowMany;

            public bool Available { get { return HowMany >= 0; } }
        }

        private static int HowManyFree(params int[] states)
        {
            int n = 0;
            foreach (var s in states)
                if (s == (int)swDofStatus_e.swDofStatus_Free ||
                    s == (int)swDofStatus_e.swDofStatus_FreeNormal ||
                    s == (int)swDofStatus_e.swDofStatus_Instantaneous ||
                    s == (int)swDofStatus_e.swDofStatus_InstantaneousNormal) n++;
            return n;
        }

        /// <summary>
        /// `swRemainingDofs_e`, with the name. The values are
        /// Restricted=0, Unrestricted=1, **Unavailable=2**, Failed=3,
        /// RootComponent=4 — and 2 is the one that is always met.
        /// </summary>
        private static string DofStateName(int outcome)
        {
            switch (outcome)
            {
                case (int)swRemainingDofs_e.swRemainingDofs_Restricted: return "vincolato";
                case (int)swRemainingDofs_e.swRemainingDofs_Unrestricted: return "libero";
                case (int)swRemainingDofs_e.swRemainingDofs_Unavailable: return "unavailable";
                case (int)swRemainingDofs_e.swRemainingDofs_Failed: return "failed";
                case (int)swRemainingDofs_e.swRemainingDofs_RootComponent: return "root component";
                default: return "unknown (" + outcome.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        /// <summary>
        /// Finds a component by name among those the assembly declares
        /// now, or `null` if it is not there.
        ///
        /// `Name2` is unique inside an assembly — it is the name with which SOLIDWORKS
        /// itself tells the instances apart — so searching for it this way is not
        /// ambiguous, not even with two copies of the same file («base-1» and
        /// «base-2»).
        /// </summary>
        private static IComponent2 RereadComponent(IAssemblyDoc assemblyDoc, string itemName)
        {
            if (assemblyDoc == null || string.IsNullOrWhiteSpace(itemName)) return null;
            try
            {
                var v = assemblyDoc.GetComponents(false) as object[];
                if (v == null) return null;
                foreach (var o in v)
                {
                    var c = o as IComponent2;
                    if (c == null) continue;
                    string its = null;
                    try { its = c.Name2; } catch { }
                    if (string.Equals(its, itemName, StringComparison.Ordinal)) return c;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// How many mates the component has, or `-1` if it could not be
        /// known.
        ///
        /// ── `null` MEANS ZERO, BUT ONLY IF THE COMPONENT IS ALIVE ───────
        ///
        /// `GetMates()` answers **null when the mates are zero**,
        /// not an empty array — measured on 16/9/2026 on a just-inserted
        /// component. It is the convention of half the SOLIDWORKS API, and it is also
        /// the way «there are none» and «I could not read» become
        /// the same answer.
        ///
        /// Here the two things are separated, and the discriminator is whether the component
        /// **still answers**: if `Name2` can be read, the handle is alive and a
        /// `null` from `GetMates` really means zero. If not even the name
        /// can be read, nothing is known and `-1` is said.
        ///
        /// The difference is not academic: `accoppiamenti = 0` leads to
        /// «resting», which is a **warning**; `-1` leads to «I do not know»,
        /// which is another thing. Confusing them would mean either keeping quiet about a danger
        /// or shouting about one that is not there.
        ///
        /// ZERO-E-ZERO: the `m == null ? 0` below is a TRUE zero, not a
        /// fallback, and the reason is the six lines above — at that point the
        /// handle has already answered, so `null` means zero. It is the only
        /// point of the repository that already respected the rule of Z-C8, and it is
        /// the model for the other twenty: **`-1` for «I do not know», `0` only
        /// when the zero is a statement that can be made.**
        /// (`Checks/zero-or-unknown.js` reads this marker.)
        /// </summary>
        private static int CountMates(IComponent2 component)
        {
            if (component == null) return -1;
            try
            {
                var its = component.Name2;      // if it throws, the handle is dead
                if (its == null) return -1;
            }
            catch { return -1; }

            try
            {
                var m = component.GetMates() as object[];
                return m == null ? 0 : m.Length;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Where the origin of the component is, in mm, or `null`.
        ///
        /// Same reading as `StatoAssieme`, and for the same reason it is written
        /// only once: `GetTotalTransform(false)`, indices 9-10-11 of
        /// `ArrayData`. The `false` excludes the explode transformation — with
        /// `true` the position **in the exploded view** would be read.
        /// </summary>
        private static object[] ComponentOriginMm(IComponent2 c)
        {
            if (c == null) return null;
            try
            {
                var t = c.GetTotalTransform(false) as IMathTransform;
                if (t == null) return null;
                var d = t.ArrayData as double[];
                if (d == null || d.Length < 12) return null;
                return new object[]
                {
                    Math.Round(Units.Mm(d[9]), 3),
                    Math.Round(Units.Mm(d[10]), 3),
                    Math.Round(Units.Mm(d[11]), 3),
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// How many problems the document declares, or `-1` if unknown.
        ///
        /// ── WHY IT IS NEEDED, AND WHY THE COUNT OF THE CONSTRAINTS IS NOT ENOUGH ──
        ///
        /// After a component replacement the mates **stay** (measured).
        /// Staying does not mean working: a constraint that points to
        /// a face of the OLD part survives as an entry in the tree and goes into
        /// error. The count still sees it — and would say «two before, two
        /// now», that is, all fine.
        ///
        /// **Two broken constraints are worse than zero constraints**: zero shows,
        /// broken ones look like they constrain. `GetWhatsWrongCount` is the question that
        /// separates the two things, and it is the same one the designer asks
        /// looking at the red tree.
        /// </summary>
        internal static int HowManyProblems(IModelDoc2 model)
        {
            try
            {
                var e = model == null ? null : model.Extension;
                return e == null ? -1 : e.GetWhatsWrongCount();
            }
            catch { return -1; }
        }

        private static int HowManyComponentsInAssembly(IAssemblyDoc assemblyDoc)
        {
            try
            {
                var v = assemblyDoc.GetComponents(true) as object[];
                return v == null ? -1 : v.Length;
            }
            catch { return -1; }
        }

        private bool IsOpen(string filePath)
        {
            return OpenDocumentAtPath(filePath) != null;
        }

        /// <summary>
        /// The open document whose path is <paramref name="path"/>, or null.
        /// The same walk EAperto always made, by full path, so that the
        /// configuration check of L11-1 reads the very document the insertion
        /// will use.
        /// </summary>
        private IModelDoc2 OpenDocumentAtPath(string filePath)
        {
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                int guard = 0;
                while (d != null && guard++ < 500)
                {
                    string its = "";
                    try { its = d.GetPathName() ?? ""; } catch { }
                    if (!string.IsNullOrWhiteSpace(its) &&
                        string.Equals(Path.GetFullPath(its), Path.GetFullPath(filePath),
                                      StringComparison.OrdinalIgnoreCase))
                        return d;
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Takes out the component this step inserted when it references another
        /// configuration than the one asked for (Z-C21.4, L11-1), and returns the
        /// tail of the refusal. «Taken out» only when the top-level components,
        /// counted again, are as many as before the insertion. DeleteSelection2
        /// and not EditDelete, which can open a confirmation window (the rule of
        /// RemoveMirror and RemoveCutTable); the component is selected
        /// with its own Select4 (SelectComponent says why).
        /// </summary>
        private static string TakeOutInsertedComponent(
            IModelDoc2 model, IAssemblyDoc assemblyDoc, IComponent2 component, int countBefore)
        {
            bool deleted = false;
            try
            {
                model.ClearSelection2(true);
                if (SelectComponent(component))
                    deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
                model.EditRebuild3();
            }
            catch { }
            int countNow = HowManyComponentsInAssembly(assemblyDoc);
            if (!deleted)
                return " DeleteSelection2 did not take it out: it is still in the assembly, remove it yourself.";
            if (countBefore >= 0 && countNow == countBefore)
                return " I took it out: the assembly has " + countNow + " components again, as before.";
            if (countBefore < 0 || countNow < 0)
                return " DeleteSelection2 answered true, but I could not count the components again to check " +
                       "that it is gone: look at the assembly before going on.";
            return " DeleteSelection2 answered true, but the assembly has " + countNow + " components and had " +
                   countBefore + " before: look at it before going on.";
        }

        /// <summary>
        /// The list of what is open, for the refusal.
        ///
        /// A REFUSAL THAT DOES NOT SAY THE ALTERNATIVE FORCES YOU TO GUESS, and
        /// guessing is the way one goes wrong silently. Here it costs one
        /// line: the open files are said, which are the only paths that
        /// the insertion accepts.
        /// </summary>
        private string WhatIsOpen()
        {
            var names = new List<string>();
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                int guard = 0;
                while (d != null && guard++ < 500)
                {
                    string its = "";
                    try { its = d.GetPathName() ?? ""; } catch { }
                    if (!string.IsNullOrWhiteSpace(its)) names.Add(its);
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }

            if (names.Count == 0)
                return "No document saved on disk is open now: a component " +
                       "that was never saved cannot be inserted, because the " +
                       "insertion looks for it by path.";
            return "These are open now: " + string.Join("; ", names) + ".";
        }

        private static int TypeOf(IModelDoc2 model)
        {
            try { return model == null ? -1 : model.GetType(); } catch { return -1; }
        }

        /// <summary>
        /// Millimetres for a message.
        ///
        /// NAME DIFFERENT FROM ANY OTHER HELPER OF THIS PARTIAL CLASS.
        /// `SwOperations` is spread over some fifteen files, and a private
        /// method with the same name in two files is a compilation error
        /// that talks about «more definitions», not about the right place. `AssemblyMm`
        /// is ugly and does not collide: it is the same repair as `TripletMm` and
        /// `DistanceBetweenPoints`.
        /// </summary>
        private static string AssemblyMm(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
