using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oce.Solid.Agent.Mcp;
using Oce.Solid.Agent.Bridge;
using FaceFilter = Oce.Solid.Agent.Core.FaceFilter;
using DocumentState = Oce.Solid.Agent.Core.DocumentState;

namespace Oce.Solid.Agent.Tools
{
    /// <summary>
    /// The five tools the bridge exposes.
    ///
    /// WHY FIVE AND NOT THIRTY-EIGHT.
    /// There are thirty-eight drawing operations, and the temptation is to expose
    /// one per tool. That would be wrong for two reasons measured in the
    /// project:
    ///
    ///   1. **the cost.** The whole schemas of the thirty-eight operations are
    ///      tens of thousands of characters in every single request. The
    ///      server's two-level catalogue exists exactly for this.
    ///   2. **the right order.** The costliest defect of this project is not
    ///      a wrong operation: it is a right operation done BLINDLY.
    ///      The tools in a row — look, search, go deeper, do —
    ///      make it hard to skip the first.
    ///
    /// `inspect_part` is not an accessory: it is the first, and without it the
    /// identifiers the others want (`F1`, `E4`, `C1`) do not
    /// exist.
    ///
    /// **THE FIFTH IS `undo`, and it came later (entry R1).** It is not
    /// a drawing operation, and it sits outside the catalogue for a
    /// precise reason: operations are executed inside `build`, that is inside a
    /// plan, and undoing a plan from inside the plan itself is a knot. The
    /// reason the tools are not thirty-eight — not exposing the
    /// operations one by one — does not concern a gesture that is not an operation.
    ///
    /// **Removing a feature, on the other hand, YES**, and it is in the catalogue
    /// (`suppress_feature`, `delete_feature`, entry R2): those are
    /// operations on the part like all the others, written inside a plan and
    /// meaningful in the middle of a sequence. The difference from `undo` is not
    /// «removes» against «adds»: it is that `undo` undoes **a plan**, and a
    /// plan is not geometry.
    ///
    /// Up to 0.31.0 this bridge said **twenty-one times** «undo it with
    /// Ctrl+Z» to a recipient that has no keyboard.
    /// </summary>
    internal sealed class McpTools
    {
        private readonly Session _session;
        private readonly Catalog _catalog;

        /// <summary>
        /// What was asked for and was not there (entry Z-G7). It is not a log:
        /// it is the only place from which one can know which operation to
        /// build next, instead of choosing it as we chose the
        /// five without bench cases — by gut feeling.
        ///
        /// It is never null: if there is nowhere to write, it stays silent by itself. An
        /// `if (_registry != null)` scattered in four places is the way
        /// the fifth place forgets it.
        /// </summary>
        private readonly QuestionRegistry _registry;

        public McpTools(Session session, Catalog catalog)
            : this(session, catalog, new QuestionRegistry()) { }

        /// <summary>With a given registry: the checks use it, as they do not
        /// want to write next to the executable.</summary>
        public McpTools(Session session, Catalog catalog,
                        QuestionRegistry registry)
        {
            _session = session;
            _catalog = catalog;
            _registry = registry ?? new QuestionRegistry(null);
        }

        public object Items()
        {
            return new object[]
            {
                Tool(
                    "inspect_part",
                    "LOOK at the document open in SolidWorks before touching it: " +
                    "bounding box, volume, bodies, features with their dimensions, faces " +
                    "and edges with their identifier. The identifiers (F1, E4, C1) " +
                    "that the other operations want are born HERE and are valid " +
                    "for this reading: after every change, read again.\n\n" +
                    "With «views» a PHOTOGRAPH of the part also comes back, which is the " +
                    "only way to know what SHAPE it has: the list of faces says how " +
                    "many there are and how big they are, not how it is made. " +
                    "It costs: each view weighs more than the whole written " +
                    "state, so it is asked for when the shape is in doubt — after " +
                    "a shell, after a chamfer, before saying the part is " +
                    "finished — not at every reading.\n\n" +
                    "The state lists the " + DocumentState.MaxFaces + " LARGEST faces and says " +
                    "«truncated» when the part has more. A small face — the wall of a " +
                    "tube, the seat of a gusset, one hole of a hundred — is then not " +
                    "listed and has no identifier you can read. With «faces» the state " +
                    "lists the faces you ask for instead: they keep the identifier " +
                    "they have among ALL the faces of the part.",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["faces"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["description"] =
                                    "WHICH FACES to list, instead of the " + DocumentState.MaxFaces +
                                    " largest. Every condition you give must hold. Each face keeps " +
                                    "its identifier among all the faces of the part (F1 is the " +
                                    "largest, F70 the seventieth by area), so «F70» is the same " +
                                    "face with or without the filter. The state then says " +
                                    "«faces_total» (all the faces of the part), «faces_matched» " +
                                    "(how many match) and «faces_not_listed» (the matching ones " +
                                    "beyond «limit»). LIMITS: only the faces are filtered — edges, " +
                                    "bodies and features are listed as always, and the «shape» sentence is not " +
                                    "written (it would describe only the faces listed). An identifier " +
                                    "holds for the reading that gave it: if an earlier step of " +
                                    "the same plan changes the part, a face that was not among " +
                                    "the " + DocumentState.MaxFaces + " largest is refused, not " +
                                    "searched for again — read the part again. At most " +
                                    FaceFilter.MaxLimit + " faces are listed in one reading, and " +
                                    "every face listed costs time and text: narrow the filter rather " +
                                    "than raise the limit. Faces of equal area are ranked in the order " +
                                    "of the bodies. A part with solid bodies only: on an assembly or a " +
                                    "drawing the call is refused.",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["normal"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["enum"] = FaceFilter.Facings,
                                        ["description"] =
                                            "which way the face looks (the words of «facing»: +X " +
                                            "right, +Y up, +Z front). Only the six axis-aligned " +
                                            "directions: a face that looks anywhere else matches none.",
                                    },
                                    ["area_min_mm2"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "number",
                                        ["description"] = "smallest area, in mm2 (included).",
                                    },
                                    ["area_max_mm2"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "number",
                                        ["description"] = "largest area, in mm2 (included).",
                                    },
                                    ["near_mm"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "array",
                                        ["items"] = new Dictionary<string, object> { ["type"] = "number" },
                                        ["description"] =
                                            "[x, y, z], a point of the model in mm: the faces whose " +
                                            "closest point to it is within «within_mm».",
                                    },
                                    ["within_mm"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "number",
                                        ["description"] =
                                            "how close «near_mm» must be, in mm. Default " +
                                            FaceFilter.DefaultWithinMm.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                            ". It is the distance to the FACE, not to the «point_mm» " +
                                            "the state writes, which is the point of the face closest " +
                                            "to the origin.",
                                    },
                                    ["body"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["description"] =
                                            "only the faces of one body: its identifier (C3) or its " +
                                            "exact name, as «bodies» lists them. The identifiers C1, " +
                                            "C2 ... can change from one reading to the next after a " +
                                            "change to the part: prefer the name.",
                                    },
                                    ["limit"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "integer",
                                        ["description"] =
                                            "how many faces to list at most, from 1 to " +
                                            FaceFilter.MaxLimit + ". Default " + DocumentState.MaxFaces +
                                            ". When more match, the largest are listed.",
                                    },
                                },
                            },
                            ["edges"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["description"] =
                                    "WHICH EDGES to list, instead of the " + DocumentState.MaxEdges + " longest (brief R17). " +
                                    "Every condition you give must hold. Each edge keeps its identifier among ALL " +
                                    "the edges of the part (E1 is the longest, E45 the forty-fifth), so a plan can cite " +
                                    "E45 even though the plain state lists E1..E" + DocumentState.MaxEdges + ". The state " +
                                    "then says «edges_total», «edges_matched» and «edges_not_listed». " +
                                    "Only the edges are filtered; «faces» filters the faces, and the two can be given together.",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["along"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["enum"] = new object[] { "x", "y", "z" },
                                        ["description"] = "a straight edge along that axis of the model (a diagonal matches none).",
                                    },
                                    ["curved"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "boolean",
                                        ["description"] = "true: only curved edges (arcs, circles); false: only straight ones.",
                                    },
                                    ["length_min_mm"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "number",
                                        ["description"] = "shortest length, in mm (included).",
                                    },
                                    ["length_max_mm"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "number",
                                        ["description"] = "longest length, in mm (included).",
                                    },
                                    ["near_mm"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "array",
                                        ["items"] = new Dictionary<string, object> { ["type"] = "number" },
                                        ["description"] = "[x, y, z] in mm: the edges whose closest point to it is within «within_mm».",
                                    },
                                    ["within_mm"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "number",
                                        ["description"] = "how close «near_mm» must be, in mm. Default " +
                                            Oce.Solid.Agent.Core.EdgeFilter.DefaultWithinMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".",
                                    },
                                    ["face"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["description"] = "a face identifier (F3) of the same reading: the edges that bound that face.",
                                    },
                                    ["body"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["description"] = "only the edges of one body: its identifier (C3) or its exact name.",
                                    },
                                    ["limit"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "integer",
                                        ["description"] = "how many edges to list at most, from 1 to " +
                                            Oce.Solid.Agent.Core.EdgeFilter.MaxLimit + ". Default " + DocumentState.MaxEdges + ".",
                                    },
                                },
                            },
                            ["view_edges"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["description"] =
                                    "ON A DRAWING: the edges of one view, with the numbers «edges» of drawing_dimension " +
                                    "takes — what each is (line, circle, curve), its mm in the model, how it sits on the " +
                                    "sheet, its component on an assembly — and the extent of the view.",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["view"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["description"] = "the view: V1, V2... as the state of the drawing writes it, or its name. Empty = the first.",
                                    },
                                    ["component"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["description"] = "only the edges of one component of an assembly (26P000011-1).",
                                    },
                                    ["limit"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "integer",
                                        ["description"] = "how many edges to list at most, from 1 to " +
                                            Oce.Solid.Agent.Core.SwOperations.MaxViewEdges + ". Default " + ViewEdgesDefaultLimit + ".",
                                    },
                                },
                            },
                            ["views"] = new Dictionary<string, object>
                            {
                                ["type"] = "array",
                                ["description"] =
                                    "which views to photograph, at most " +
                                    MaximumViews + ". If you do not know which, ask for " +
                                    "«isometric»: it is the only one that shows a " +
                                    "shape by itself. Available: " +
                                    string.Join(", ", Session.ViewNames) + ".",
                                ["items"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string",
                                    ["enum"] = Names(Session.ViewNames),
                                },
                            },
                            ["section"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["description"] =
                                    "A CUTAWAY: the part cut by a plane. It tells you more about " +
                                    "a shell than ten external views — from the outside a " +
                                    "hollowed box and a full one are the same block. " +
                                    "It cuts only the PICTURE, not the part, and it removes itself " +
                                    "as soon as the picture is taken.",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["plane"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["enum"] = Names(Session.PlaneNames),
                                        ["description"] = "which plane to cut on",
                                    },
                                    ["offset_mm"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "number",
                                        ["description"] =
                                            "how far to move the cutting plane, in mm. Default " +
                                            "0, that is, the plane itself — which passes through " +
                                            "the origin, not through the center of the part.",
                                    },
                                },
                                ["required"] = new[] { "plane" },
                            },
                        },
                        new string[0]),
                    readOnlyMode: true, destructive: false, idempotent: true),

                Tool(
                    "list_operations",
                    "The short list of what can be asked for, by family: a " +
                    "name and one line each. For the complete schema of one, use " +
                    "«describe_operation».",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["family"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "only one family: extrusions, features, weldments, " +
                                    "references, assemblies, physical_properties, drawing, " +
                                    "checks, exports, documents, identity, sheet_metal, dimensions",
                            },
                        },
                        new string[0]),
                    readOnlyMode: true, destructive: false, idempotent: true),

                Tool(
                    "describe_operation",
                    "The complete schema of one operation: what it wants, in " +
                    "which units, what must already be in the document.",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["name"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "the exact name, from «list_operations»",
                            },
                        },
                        new[] { "name" }),
                    readOnlyMode: true, destructive: false, idempotent: true),

                Tool(
                    "build",
                    "Runs a sequence of operations in SolidWorks and REPORTS " +
                    "WHAT CAME OUT: the measurements of each step and the state of the " +
                    "document afterwards.\n\n" +
                    "The plan is checked ENTIRELY before starting: if any " +
                    "step has a wrong name or arguments that do not follow " +
                    "the schema, nothing is run and the " +
                    "document stays as it was.\n\n" +
                    "If instead a step fails while it is being run, it stops " +
                    "there: the following steps almost always name what " +
                    "the failed step should have created. What had already been " +
                    "built stays, and «undo» removes it all in one " +
                    "go.\n\n" +
                    "With «dry_run: true» the plan is ONLY checked: nothing is " +
                    "touched, and the answer says what was looked at and what was not. " +
                    "It is for making mistakes beforehand, instead of " +
                    "in the middle of the part.\n\n" +
                    "A step can name the geometry created by an EARLIER step of " +
                    "the same plan (for example face_ref: " +
                    "{step: 1, kind: \"top_face\"}): it is the only way, " +
                    "because that face has no identifier yet.\n\n" +
                    "The plan runs on the document you looked at; if another window " +
                    "comes to the front while it runs, the plan's document is brought " +
                    "back (see «on_window_change»). " +
                    "THE ANSWER IS SHORT BY DEFAULT: the outcomes with their measures " +
                    "and warnings, «state_summary» (volume, box, mass, the features added " +
                    "and removed) and «timing_ms» for the whole plan. Of a step that " +
                    "succeeded it leaves out long explanations that are not warnings, " +
                    "and measures that came back equal to the argument of the same name " +
                    "(«note_outcomes» counts them). The identifiers of faces and edges are NOT in it: read " +
                    "them with «inspect_part» before a plan that cites them, or send " +
                    "«response»: «full» to get the whole «state_after». " +
                    "«volume_after_mm3» is the plan's document; «state_after» is the " +
                    "document in front, and when that is another one the answer says " +
                    "so in «document_in_front» and «note_document_in_front» (after " +
                    "a drawing step it is the new drawing, and the next plan works " +
                    "on it). «undo_entry_recorded» says whether the plan left its " +
                    "own undo entry, the one an undo takes away.",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["steps"] = new Dictionary<string, object>
                            {
                                ["type"] = "array",
                                ["description"] =
                                    "the operations to run, in order",
                                ["items"] = Object(
                                    new Dictionary<string, object>
                                    {
                                        ["operation"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "string",
                                            ["description"] = "the name of the operation",
                                        },
                                        ["arguments"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "object",
                                            ["description"] =
                                                "the arguments, as in «describe_operation»",
                                        },
                                        ["expect"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "object",
                                            ["description"] =
                                                "optional: what this step must measure, by the names of its " +
                                                "measurements, e.g. {\"removed_mm3\": 36} or {\"diameter_mm\": " +
                                                "{\"equal_to\": 0.992, \"tolerance\": 0.001}} («volume_mm3» is the " +
                                                "volume after the step; a plain number has a tolerance of 0.01). " +
                                                "One that does not hold, or that the step does not measure, makes " +
                                                "the step fail and the plan stop there, with the numbers side by side.",
                                        },
                                    },
                                    new[] { "operation" }),
                            },
                            ["document"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "Which document to draw on: the title or the " +
                                    "path, copied from «inspect_part». It is needed " +
                                    "ONLY if the document in front has changed " +
                                    "since you looked at it: normally it is omitted. " +
                                    "Naming it means «I know which part " +
                                    "I am touching», and a name that does not match " +
                                    "is refused.",
                            },
                            ["response"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "summary", "full" },
                                ["description"] =
                                    "«summary» (the default): outcomes, measures, warnings, " +
                                    "«state_summary» and the time of the whole plan. «full»: also " +
                                    "the explanations and the time of each step, and the " +
                                    "whole «state_after», with the identifiers of faces " +
                                    "and edges (tens of thousands of characters on a real part).",
                            },
                            ["on_window_change"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "reactivate", "stop" },
                                ["description"] =
                                    "What to do when another window comes to the front " +
                                    "while the plan runs (someone working in the same " +
                                    "SOLIDWORKS session). «reactivate» (the default): the " +
                                    "plan's document is brought back to the front, at most " +
                                    "3 times, and «reactivations» in the answer says when " +
                                    "and which window was there. «stop»: the plan stops " +
                                    "before the next step and touches nothing more.",
                            },
                            ["dry_run"] = new Dictionary<string, object>
                            {
                                ["type"] = "boolean",
                                ["description"] =
                                    "DRY RUN: checks the plan and touches NOTHING. " +
                                    "It says which operations do not exist, " +
                                    "which required arguments are missing, " +
                                    "which arguments would be IGNORED because " +
                                    "they are misspelled, and which references " +
                                    "point to a step that is not there. It also " +
                                    "says what it could NOT look at: whether " +
                                    "the «uid»s are still valid and whether the " +
                                    "document is the right one can only be known " +
                                    "by reading the document. It also works with " +
                                    "SOLIDWORKS closed.",
                            },
                        },
                        new[] { "steps" }),
                    // Running the same plan again does NOT put the part back
                    // as it was: it makes a second hole. Declaring it idempotent
                    // would invite a client to retry by itself after a
                    // network error.
                    readOnlyMode: false, destructive: true, idempotent: false),

                // MEASURING AND DRAWING WITHOUT THE PERMISSION TO TOUCH.
                //
                // It is not a convenience and it is the only way to tell the truth in the
                // protocol: MCP annotations travel on `tools/list`,
                // that is they sit on the TOOL and leave before any call
                // exists. As long as there was only `build`, the client read
                // «destructive» and asked for confirmation even for
                // `mass_properties`, which does not move a cubic millimetre — and whoever
                // confirms ten times in a row stops reading what they confirm.
                // It could not be repaired inside `build`: at the time
                // of the annotation it is not yet known which operation will arrive.
                // One more tool was needed, and this is it.
                Tool(
                    "measure",
                    "MEASURES the part without touching it. It takes " +
                    "the same operations as «build», but accepts ONLY those " +
                    "of the «checks» family — the ones that only read.\n\n" +
                    "If you only need to know how the part is made, use this one: " +
                    "your client knows it changes nothing and does not ask " +
                    "you for confirmation. One exception in the flags, not in the " +
                    "geometry: «interference_check» can leave a saved assembly marked " +
                    "unsaved (SOLIDWORKS does it when its detection closes), and its " +
                    "answer then says «marked_unsaved».\n\n" +
                    "An operation that touches the part is REFUSED here, and " +
                    "the answer says which tool does it. The plan does not " +
                    "start: it does not measure half the part and then stop.",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["steps"] = new Dictionary<string, object>
                            {
                                ["type"] = "array",
                                ["description"] =
                                    "the measurements to take, in order — only " +
                                    "operations of the «checks» family",
                                ["items"] = Object(
                                    new Dictionary<string, object>
                                    {
                                        ["operation"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "string",
                                            ["description"] =
                                                "the name of the check operation",
                                        },
                                        ["arguments"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "object",
                                            ["description"] =
                                                "the arguments, as in «describe_operation»",
                                        },
                                    },
                                    new[] { "operation" }),
                            },
                            ["document"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "Which document to measure: the title or " +
                                    "the path, copied from «inspect_part». " +
                                    "Normally omitted.",
                            },
                        },
                        new[] { "steps" }),
                    // Idempotent for real, and not out of courtesy: measuring twice
                    // gives the same answer twice because nothing
                    // changes in between. It is the difference from `build`,
                    // which on the second run makes a SECOND hole.
                    readOnlyMode: true, destructive: false, idempotent: true),

                Tool(
                    "undo",
                    "WITHDRAWS the last plan run by «build», the whole of it. " +
                    "It is what you would otherwise do with Ctrl+Z, which you " +
                    "do not have.\n\n" +
                    "It undoes ONLY if the part is still as «build» left it: " +
                    "if someone has worked by hand after the plan, I stop and " +
                    "say what changed — undoing then would remove someone " +
                    "else's work.\n\n" +
                    "Afterwards, the part returns to the volume it had before the plan, and the identifiers of " +
                    "the faces are NO LONGER the same: the answer carries " +
                    "the new state, and that is the one that counts.\n\n" +
                    "The last plan is the last one that built something or left its " +
                    "own undo entry (its answer said «undo_entry_recorded»: true), " +
                    "for example a reorder; a measure, or a plan that wrote nothing, " +
                    "does not replace it. The part must be in front: after a plan " +
                    "that ended on another window (a drawing it made stays in front, " +
                    "and its answer names it in «document_in_front»), bring the part " +
                    "back as «note_document_in_front» says, then call again. I compare the part with " +
                    "how the plan left it (volume, feature count, feature names and " +
                    "order) and I REFUSE BEFORE PRESSING («refused_before_pressing»: " +
                    "true) when it differs, or when something is known to be on top " +
                    "of the plan's undo entry: SOLIDWORKS cannot redo an undo entry " +
                    "recorded through the API, so a wrong press could not be taken " +
                    "back. After pressing I report it withdrawn only if the part " +
                    "reads as before the plan; otherwise I say what the press took " +
                    "away and whether it came back.",
                    Object(new Dictionary<string, object>(), new string[0]),
                    // It is NOT idempotent: calling it twice does not undo the same plan
                    // twice — the second would take away what
                    // was there BEFORE, that is work this bridge did not
                    // do. The memory is erased after the first, but
                    // declaring it idempotent would invite a client to
                    // retry by itself after a network error.
                    readOnlyMode: false, destructive: true, idempotent: false),

                Tool(
                    "document",
                    "The DOCUMENT you work on: creating one, opening one from " +
                    "disk, knowing which ones are open, bringing one to the front.\n\n" +
                    "Without open documents the other tools have nothing to " +
                    "look at: you start with «create» (an empty part from the " +
                    "default template) or with «open».\n\n" +
                    "Every time the document in front changes, what you had looked " +
                    "at is no longer valid: before drawing, «inspect_part». The " +
                    "bridge requires it.\n\n" +
                    "I always report a file opened READ-ONLY: it usually means a " +
                    "colleague has it open, and «build» does not write to it.\n\n" +
                    "«close» REFUSES a document with unsaved changes: I do not throw " +
                    "away anyone's work. Save it first, or close it by hand.\n\n" +
                    "Closing: a saved document is closed by its full path, and one never " +
                    "saved as the active document or by a title that no other open " +
                    "document answers to. It REFUSES when another open document answers " +
                    "to an unsaved document's title, and when hidden documents with " +
                    "unsaved changes that no other open document uses would go with it " +
                    "(CloseDoc closes non-active hidden documents without saving). The " +
                    "answer adds closed_by, still_loaded_hidden + still_loaded_reason " +
                    "(window closed, model kept because another open document references " +
                    "it), also_closed, also_closed_unsaved. Bringing forward: a saved " +
                    "document comes forward by file name with extension or path, never " +
                    "by title, and the active document is read back.",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["action"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "create", "open", "list", "activate", "close" },
                                ["description"] =
                                    "«create»: creates an empty document and brings it to the " +
                                    "front. «open»: opens a file from disk. «list»: the open " +
                                    "documents, and which one is in front. «activate»: brings an already " +
                                    "open one to the front. «close»: closes an open one, only if it " +
                                    "has no changes to save.",
                            },
                            ["variant"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "part", "assembly" },
                                ["description"] =
                                    "only with «create». Default «part». A drawing is " +
                                    "born from a part: «create_drawing», inside a plan.",
                            },
                            ["path"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "with «open»: the .sldprt, .sldasm or .slddrw file, " +
                                    "with the full path. With «activate» and «close»: the " +
                                    "path or, for a document never saved, the title " +
                                    "that «list» gave.",
                            },
                        },
                        new[] { "action" }),
                    // It is NOT read-only: «create», «open», «activate» and
                    // «close» change the document in front. It is NOT destructive:
                    // «close» refuses a document with unsaved changes,
                    // so it closes only what is already all on disk, and
                    // throws nothing away. It is NOT idempotent: «create» twice makes two
                    // documents. Still true for hidden documents (Z-C21.13 N11):
                    // a close that would take hidden documents with unsaved
                    // changes is refused.
                    readOnlyMode: false, destructive: false, idempotent: false),

                // THE WHOLE API, READ BY REFLECTION (entry U1.1).
                //
                // Two tools that never touch SOLIDWORKS: they read the interop
                // assembly loaded in this process and api_knowledge.json. They
                // work with SOLIDWORKS closed, and they are the first half of
                // Layer 1 (api_objects and api_call come with U1.2-U1.3).
                Tool(
                    "api_search",
                    "SEARCH the whole SOLIDWORKS API (the sldworks interop and the " +
                    "swconst enums) by a piece of a name. Answers with the REAL " +
                    "signatures, read from the interop on this machine: " +
                    "«SewRefSurface» finds IFeatureManager.InsertSewRefSurface(5 " +
                    "arguments) -> Feature and IModelDoc2.InsertSewRefSurface() -> Void.\n\n" +
                    "«Interface.piece» searches one interface («IEdge.Adjacent»). " +
                    "Enum names and enum values are searched too («swMateAlign»).\n\n" +
                    "The four old interfaces (IModelDoc, IBody, IFace, IComponent) are " +
                    "not exposed: a hit there names the new sister to use instead. " +
                    "Members of swmotionstudy are listed read only.\n\n" +
                    "Never touches SOLIDWORKS; works with it closed. For the full card " +
                    "of one member (what is known of it, enums, indices), use " +
                    "«api_signature».",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["query"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "a piece of the name, at least two characters; " +
                                    "«Interface.piece» to search one interface",
                            },
                            ["limit"] = new Dictionary<string, object>
                            {
                                ["type"] = "integer",
                                ["description"] =
                                    "how many lines to show, default 40, at most 200",
                            },
                        },
                        new[] { "query" }),
                    readOnlyMode: true, destructive: false, idempotent: true),

                Tool(
                    "api_signature",
                    "The CARD of one SOLIDWORKS API member, e.g. «IBody2.Check3» or " +
                    "«IAssemblyDoc.AddMate5»: every overload with its real types, " +
                    "property or method, the out (&) parameters by name, the typed " +
                    "I… variant next to it, the numbered family (X, X2, X3) with the " +
                    "latest marked «latest by number, not verified», and the flag " +
                    "when it returns Void (then a new feature is only found from the " +
                    "before/after snapshot).\n\n" +
                    "It also says what is KNOWN of the member here: its state " +
                    "(forbidden, confirm, gray, never_probed, probed_read, " +
                    "probed_modifies), which enum an Int32 parameter takes — ONLY " +
                    "where a hand-written map says so, otherwise «enum not " +
                    "tabulated» — the meaning of array indices ONLY when measured, " +
                    "otherwise «index not tabulated», the measured type of an Object " +
                    "parameter, and the source of each fact. Do not guess what it " +
                    "says is not tabulated.\n\n" +
                    "Also takes an interface («IEdge»: its member names) or an enum " +
                    "(«swFeatureNameID_e»: every value, names that share a value " +
                    "marked AMBIGUOUS). Never touches SOLIDWORKS.",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["name"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "«Interface.Member», an interface, an enum of " +
                                    "swconst, or a member name that exists on one " +
                                    "interface only",
                            },
                        },
                        new[] { "name" }),
                    readOnlyMode: true, destructive: false, idempotent: true),

                // AN STL FILE, MEASURED OR CUT (brief R15). On 24-25/10/2026
                // every number taken from the customer's STL files came from
                // three Python programs written for the occasion.
                Tool(
                    "mesh",
                    "Reads an STL file (binary or ASCII) and MEASURES it or CUTS it. Never touches " +
                    "SOLIDWORKS; works with it closed.\n\n" +
                    "«measure»: bounding box, size, area, whether the mesh is closed (every edge " +
                    "shared by two triangles) and, only when it is, the volume; with «axis» and " +
                    "«levels», the area of N sections evenly spaced along the axis.\n\n" +
                    "«section»: the plane square to «axis» at «at_mm» cuts the mesh into closed " +
                    "contours, simplified by «simplify_mm», sorted by area, each with its depth " +
                    "(outer edge, void, island). «profiles» are ready for «extrude_profile»: «plane» " +
                    "and «contours» in the sketch coordinates of that plane (front: x = X, y = Y; " +
                    "right: x = -Z, y = Y; top: x = X, y = -Z, as measured), outer edge first. " +
                    "«extrude_profile» draws on the plane through the origin, not at «at_mm».\n\n" +
                    "An STL has no units: its numbers are taken as mm, times «scale».",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["path"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "the full path of the .stl file",
                            },
                            ["action"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = new object[] { "measure", "section" },
                                ["description"] = "«measure» (default) or «section»",
                            },
                            ["axis"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = new object[] { "x", "y", "z" },
                                ["description"] = "the axis the section plane is square to (section, and levels of measure)",
                            },
                            ["at_mm"] = new Dictionary<string, object>
                            {
                                ["type"] = "number",
                                ["description"] = "where along «axis» to cut, in mm (section)",
                            },
                            ["simplify_mm"] = new Dictionary<string, object>
                            {
                                ["type"] = "number",
                                ["description"] = "how far a vertex left out may be from the contour kept, default 0.05 mm (section)",
                            },
                            ["levels"] = new Dictionary<string, object>
                            {
                                ["type"] = "integer",
                                ["description"] = "how many sections «measure» gives along «axis», 1 to 50",
                            },
                            ["scale"] = new Dictionary<string, object>
                            {
                                ["type"] = "number",
                                ["description"] = "mm per unit of the file, default 1 (25.4 for inches, 1000 for metres)",
                            },
                        },
                        new[] { "path" }),
                    readOnlyMode: true, destructive: false, idempotent: true),
            };
        }

        /// <summary>
        /// api_knowledge.json, embedded in the executable at build time.
        ///
        /// Embedded and not copied next to the executable: the installer
        /// carries one file per payload entry, and a knowledge file that can
        /// be missing would make api_signature answer "never_probed" for a
        /// member measured to crash SOLIDWORKS — the one wrong answer that
        /// looks like a right one. Read once.
        /// </summary>
        private static readonly Lazy<string> ApiKnowledgeJson = new Lazy<string>(() =>
        {
            try
            {
                var asm = typeof(McpTools).Assembly;
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith("api_knowledge.json", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stream = asm.GetManifestResourceStream(name))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                Protocol.Note("[bridge] api_knowledge.json not readable: " + ex.Message);
            }
            return null;
        });

        private static Dictionary<string, object> ApiAnswerContent(Func<Oce.Solid.Agent.Core.Api.ApiAnswer> ask, string tool)
        {
            try
            {
                if (ApiKnowledgeJson.Value == null)
                    return Protocol.Content(
                        tool + ": api_knowledge.json is missing from this build, and without it " +
                        "every member would look never probed — including the ones measured " +
                        "to crash SOLIDWORKS. Rebuild the bridge.", true);
                var answer = ask();
                return Protocol.Content(answer.Text, answer.IsError);
            }
            catch (Exception ex)
            {
                // The interop is missing (SOLIDWORKS not installed where the
                // bridge looks): say where to point it, not a stack trace.
                var inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                return Protocol.Content(
                    tool + " could not read the SOLIDWORKS interop: " + inner.Message +
                    " Set SW_INSTALL_PATH to the SOLIDWORKS installation folder.", true);
            }
        }

        private Dictionary<string, object> ApiSearch(Dictionary<string, object> arguments)
        {
            var query = TextOf(arguments, "query");
            int limit;
            int.TryParse(TextOf(arguments, "limit") ?? "", NumberStyles.Integer,
                         CultureInfo.InvariantCulture, out limit);
            return ApiAnswerContent(
                () => Oce.Solid.Agent.Core.Api.ApiCatalog.Search(query, limit, ApiKnowledgeJson.Value),
                "api_search");
        }

        private Dictionary<string, object> ApiSignature(Dictionary<string, object> arguments)
        {
            var name = TextOf(arguments, "name");
            return ApiAnswerContent(
                () => Oce.Solid.Agent.Core.Api.ApiCatalog.Signature(name, ApiKnowledgeJson.Value),
                "api_signature");
        }

        /// <summary>The most vertices a «mesh» section answers: more is refused with a bigger «simplify_mm».</summary>
        internal const int MeshMostPoints = 3000;

        /// <summary>
        /// The «mesh» tool (brief R15): an STL file measured, or cut into contours
        /// for «extrude_profile». It never touches SOLIDWORKS. See <see
        /// cref="Oce.Solid.Agent.Core.StlMesh"/>.
        /// </summary>
        private Dictionary<string, object> MeshTool(Dictionary<string, object> arguments)
        {
            var path = TextOf(arguments, "path");
            var action = TextOf(arguments, "action") ?? "measure";
            if (action != "measure" && action != "section")
                return Protocol.Content("«action» is «measure» or «section», not «" + action + "».", true);
            double scale = 1;
            var scaleText = TextOf(arguments, "scale");
            if (scaleText != null && !double.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out scale))
                return Protocol.Content("«scale» must be a number (25.4 for a file in inches, 1000 for metres).", true);
            var axisText = (TextOf(arguments, "axis") ?? "").ToLowerInvariant();
            int axis = axisText == "x" ? 0 : axisText == "y" ? 1 : axisText == "z" ? 2 : -1;
            if (axisText.Length > 0 && axis < 0)
                return Protocol.Content("«axis» is «x», «y» or «z», not «" + axisText + "».", true);

            string problem;
            var mesh = Oce.Solid.Agent.Core.StlMesh.Load(path, scale, out problem);
            if (mesh == null) return Protocol.Content(problem, true);
            var measured = mesh.Measure();
            var min = (double[])measured["bounding_box_min_mm"];
            var max = (double[])measured["bounding_box_max_mm"];

            if (action == "measure")
            {
                var answer = new Dictionary<string, object> { ["path"] = path, ["scale"] = scale };
                foreach (string key in measured.Keys) answer[key] = measured[key];
                int levels = 0;
                var levelsText = TextOf(arguments, "levels");
                if (levelsText != null && (!int.TryParse(levelsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out levels) || levels < 1 || levels > 50))
                    return Protocol.Content("«levels» is a whole number from 1 to 50: how many sections, evenly spaced along «axis».", true);
                if (levels > 0)
                {
                    if (axis < 0) return Protocol.Content("«levels» needs «axis» («x», «y» or «z»): the sections are square to it.", true);
                    var rows = new List<object>();
                    double from = min[axis], span = max[axis] - min[axis];
                    for (int i = 0; i < levels; i++)
                    {
                        double at = from + span * (i + 0.5) / levels;
                        var cut = mesh.Section(axis, at, 0);
                        rows.Add(new Dictionary<string, object>
                        {
                            ["at_mm"] = Math.Round(at, 3),
                            ["area_mm2"] = Math.Round(Oce.Solid.Agent.Core.StlMesh.SectionArea(cut), 3),
                            ["contours"] = cut.Loops.Count,
                            ["open_chains"] = cut.OpenChains,
                        });
                    }
                    answer["sections"] = rows;
                    answer["sections_axis"] = axisText;
                }
                answer["units"] = "The numbers of the file are taken as mm, times «scale».";
                return Protocol.ContentWithData(Protocol.InJson(answer), answer);
            }

            if (axis < 0) return Protocol.Content("A section needs «axis» («x», «y» or «z») and «at_mm»: the plane is square to the axis at that coordinate.", true);
            double atMm;
            var atText = TextOf(arguments, "at_mm");
            if (atText == null || !double.TryParse(atText, NumberStyles.Float, CultureInfo.InvariantCulture, out atMm))
                return Protocol.Content("«at_mm» is missing: give the coordinate along «" + axisText + "» where to cut, between " +
                                        min[axis].ToString(CultureInfo.InvariantCulture) + " and " + max[axis].ToString(CultureInfo.InvariantCulture) + " for this file.", true);
            double simplify = 0.05;
            var simplifyText = TextOf(arguments, "simplify_mm");
            if (simplifyText != null && (!double.TryParse(simplifyText, NumberStyles.Float, CultureInfo.InvariantCulture, out simplify) || simplify < 0))
                return Protocol.Content("«simplify_mm» is a number of zero or more: how far a vertex left out may be from the contour kept.", true);

            var section = mesh.Section(axis, atMm, simplify);
            var plane = Oce.Solid.Agent.Core.StlMesh.PlaneFor(axis);
            var result = new Dictionary<string, object>
            {
                ["path"] = path,
                ["axis"] = axisText,
                ["at_mm"] = atMm,
                ["plane"] = plane,
                ["mesh_from_mm"] = min[axis],
                ["mesh_to_mm"] = max[axis],
                ["closed_mesh"] = measured["closed"],
                ["simplify_mm"] = simplify,
                ["open_chains"] = section.OpenChains,
            };
            if (section.Loops.Count == 0)
            {
                result["contours_found"] = 0;
                result["verdict"] = atMm < min[axis] || atMm > max[axis]
                    ? "The plane at " + axisText + " = " + atMm.ToString(CultureInfo.InvariantCulture) + " does not cut the mesh: it spans " +
                      min[axis].ToString(CultureInfo.InvariantCulture) + " to " + max[axis].ToString(CultureInfo.InvariantCulture) + " along " + axisText + "."
                    : "No closed contour at " + axisText + " = " + atMm.ToString(CultureInfo.InvariantCulture) +
                      (section.OpenChains > 0 ? ": " + section.OpenChains + " chains did not close (the mesh has a hole there)." :
                       ": a plane exactly on a face of the mesh may not cut it; move it inside by a hair.");
                return Protocol.ContentWithData(Protocol.InJson(result), result);
            }
            int pointsAfter = 0;
            foreach (var loop in section.Loops) pointsAfter += loop.Points.Count;
            if (pointsAfter > MeshMostPoints)
                return Protocol.Content("The section has " + pointsAfter + " vertices after simplifying by " +
                                        simplify.ToString(CultureInfo.InvariantCulture) + " mm, and I answer at most " + MeshMostPoints +
                                        ": pass a larger «simplify_mm» (0.1 or 0.2 keeps a printed part within its tolerance).", true);
            var loops = new List<object>();
            foreach (var loop in section.Loops)
                loops.Add(new Dictionary<string, object>
                {
                    ["depth"] = loop.Depth,
                    ["kind"] = loop.Depth % 2 == 0 ? "outer edge" : "void",
                    ["area_mm2"] = Math.Round(Math.Abs(loop.SignedArea), 3),
                    ["perimeter_mm"] = Math.Round(loop.Perimeter, 3),
                    ["points"] = loop.Points.Count,
                });
            var profiles = new List<object>();
            foreach (var group in Oce.Solid.Agent.Core.StlMesh.Profiles(section))
            {
                var contours = new List<object>();
                double area = 0;
                foreach (var loop in group)
                {
                    var points = new List<object>();
                    foreach (var p in loop.Points) points.Add(Oce.Solid.Agent.Core.StlMesh.Round(p, 3));
                    contours.Add(points);
                    area += loop == group[0] ? Math.Abs(loop.SignedArea) : -Math.Abs(loop.SignedArea);
                }
                profiles.Add(new Dictionary<string, object> { ["plane"] = plane, ["contours"] = contours, ["area_mm2"] = Math.Round(area, 3) });
            }
            result["contours_found"] = section.Loops.Count;
            result["points_before"] = section.PointsBefore;
            result["points_after"] = pointsAfter;
            result["section_area_mm2"] = Math.Round(Oce.Solid.Agent.Core.StlMesh.SectionArea(section), 3);
            result["loops"] = loops;
            result["profiles"] = profiles;
            result["note"] = "Each profile is the «plane» and «contours» of one «extrude_profile» (the outer edge first, then its voids), " +
                             "in the sketch coordinates of that plane, rounded to 0.001 mm. «extrude_profile» draws on the plane through " +
                             "the origin: this section is at " + axisText + " = " + atMm.ToString(CultureInfo.InvariantCulture) + ".";
            return Protocol.ContentWithData(Protocol.InJson(result), result);
        }

        /// <summary>
        /// The «document» tool (entries Z-B1, Z-B2, Z-B5, Z-B6).
        ///
        /// All the work is in `Bridge/Documents.cs`; here is the part that
        /// only the bridge can do: when the document in front changes, what
        /// had been looked at no longer holds, and neither do the signatures. It is the
        /// rule of Z-A1 — the three fields that speak of the same part are
        /// thrown away together — applied at the point where the bridge itself changes
        /// document. Entry Z-B6 had written it before it existed.
        /// </summary>
        private Dictionary<string, object> Document(Dictionary<string, object> arguments)
        {
            if (!_session.Attached)
                return Protocol.Content("The bridge is not attached to SolidWorks.", true);

            string action = TextOf(arguments, "action");
            string error;
            Dictionary<string, object> card;
            switch (action)
            {
                case "list":
                {
                    var list = _session.Documents(out int hidden, out error);
                    if (list == null) return Protocol.Content(error, true);
                    var outside = new Dictionary<string, object>
                    {
                        ["documents"] = list,
                        ["how_many"] = list.Count,
                        ["loaded_inside_assembly"] = hidden,
                    };
                    if (list.Count == 0)
                        outside["note"] = "No document is open: start with «create» or «open».";
                    return Protocol.ContentWithData(Protocol.InJson(outside), outside);
                }
                case "create":
                    string typ = TextOf(arguments, "variant");
                    card = _session.CreateDocument(string.IsNullOrWhiteSpace(typ) ? "part" : typ, out error);
                    break;
                case "open":
                    card = _session.OpenDocument(TextOf(arguments, "path"), out error);
                    break;
                case "activate":
                    card = _session.ActivateDocument(TextOf(arguments, "path"), out error);
                    break;
                case "close":
                {
                    var closed = _session.CloseDocument(TextOf(arguments, "path"), out error);
                    if (closed == null) return Protocol.Content(error, true);
                    // The document looked at might be the very one that was closed:
                    // the look is thrown away as for every document change.
                    _seen = null;
                    _signatureSeen = null;
                    _seenSignatures = null;
                    closed["next_step"] = closed["in_front_now"] == null
                        ? "There is no open document any more: use «create» or «open» to continue."
                        : "In front now is «" + Convert.ToString(closed["in_front_now"]) + "». " +
                          "Before drawing, call «inspect_part».";
                    return Protocol.ContentWithData(Protocol.InJson(closed), closed);
                }
                default:
                    return Protocol.Content(
                        "«action» must be «create», «open», «list», «activate» or «close». I received «" +
                        (action ?? "") + "».", true);
            }
            if (card == null) return Protocol.Content(error, true);

            // THE DOCUMENT IN FRONT HAS CHANGED (or might have): what
            // had been looked at and its signatures are thrown away TOGETHER.
            _seen = null;
            _signatureSeen = null;
            _seenSignatures = null;
            _seenByDocument = null;

            // WHICH DOCUMENT IS KNOWN, HOW IT IS MADE IS NOT (brief R18). The card
            // says the document this call put in front; «build» takes it as the
            // document looked at, for a plan that cites no identifier. A plan
            // that cites one is still sent to «inspect_part».
            object cardInFront;
            bool putInFront = card.TryGetValue("in_front", out cardInFront) && cardInFront is bool && (bool)cardInFront;
            if (putInFront)
            {
                _seen = new Dictionary<string, object>
                {
                    ["document_title"] = card.TryGetValue("document_title", out var cardTitle) ? cardTitle : "",
                    ["path"] = card.TryGetValue("path", out var cardPath) ? cardPath ?? "" : "",
                };
                _seenByDocument = _seen;
            }

            card["next_step"] = putInFront
                ? "In front now is «" + Convert.ToString(card["document_title"]) + "». A plan that cites no " +
                  "identifier (a sketch on a plane, an extrusion, a property) can run now; before citing a face, an " +
                  "edge or a component, call «inspect_part»: identifiers are born there."
                : "In front now is «" + Convert.ToString(card["document_title"]) + "». " +
                  "Before drawing, call «inspect_part»: what you looked at " +
                  "before is no longer valid.";
            return Protocol.ContentWithData(Protocol.InJson(card), card);
        }

        private static string TextOf(Dictionary<string, object> arguments, string key)
        {
            if (arguments == null || !arguments.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)?.Trim();
        }

        /// <summary>
        /// The progress thread of the call in progress (entry W2.2).
        ///
        /// It is never null: switched off when the client did not send a token.
        /// This way the places that use it do not each have an `if`.
        /// </summary>
        private Mcp.Progress.Wire _wire = new Mcp.Progress.Wire(null);

        public Dictionary<string, object> Invoke(
            string itemName, Dictionary<string, object> arguments, object token = null)
        {
            // A NEW THREAD FOR EVERY CALL. The `progress` counter must
            // grow INSIDE a request; between different requests it starts again,
            // and keeping a single one would mean that the second call starts
            // from where the first one got to.
            _wire = new Mcp.Progress.Wire(token);

            return Dispatch(itemName, arguments);
        }

        private Dictionary<string, object> Dispatch(string itemName, Dictionary<string, object> arguments)
        {
            switch (itemName)
            {
                case "inspect_part": return InspectPart(arguments);
                case "list_operations": return ListOperations(arguments);
                case "describe_operation": return DescribeOperation(arguments);
                case "build": return Build(arguments);
                case "measure": return Measure(arguments);
                case "undo": return Undo();
                case "document": return Document(arguments);
                case "api_search": return ApiSearch(arguments);
                case "api_signature": return ApiSignature(arguments);
                case "mesh": return MeshTool(arguments);
                default:
                    // AND BEFORE ANSWERING, WE WRITE (entry Z-G7): this
                    // is the point that knew and kept quiet.
                    _registry.ToolThatDoesNotExist(itemName);

                    // WE SAY WHICH ONES EXIST, not just that this one does not.
                    //
                    // A model that got the name wrong has no way to
                    // correct itself by reading «I do not know x»: it can only
                    // try guessing again, and that is how one ends up
                    // calling the same wrong thing three times. The names
                    // are seven: they fit in one line, and cost less than one more
                    // run. It is the same rule as the refusals of the
                    // operations — the criterion, not the verdict.
                    //
                    // THE NUMBER OF OPERATIONS COMES FROM THE CATALOG (27/10/2026):
                    // this sentence said «sixty» with 104 operations in the
                    // catalog, seen by a client that asked a wrong name. A number
                    // written in a message ages like the one in the README.
                    return Protocol.Content(
                        "I do not know the tool «" + itemName + "». The tools are " +
                        "these: " + string.Join(", ", ToolNames) + ". " +
                        "The " + (_catalog.LoadingError == null ? _catalog.HowMany + " " : "") +
                        "drawing OPERATIONS are not tools: " +
                        "they are asked of «build» (or of «measure», if they only read " +
                        "and change nothing) and are listed with «list_operations».", true);
            }
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The last document that «inspect_part» described.
        ///
        /// It is not a convenience memory: it is the contract between the two tools.
        /// A plan is born by looking at ONE document, and must be executed on THAT one.
        /// </summary>
        private Dictionary<string, object> _seen;

        /// <summary>
        /// The identity «document» put in `_seen` when it brought a document to
        /// the front, without reading it (brief R18). While `_seen` is this very
        /// object, the document is known and its content is not: a plan that
        /// cites no identifier runs, one that cites one is sent to «inspect_part».
        /// Any reading puts another object in `_seen`, and the difference ends.
        /// </summary>
        private Dictionary<string, object> _seenByDocument;

        /// <summary>
        /// HOW THE PART WAS MADE when «inspect_part» looked at it
        /// (entry X2) — not WHICH part it was, which is `_seen`.
        ///
        /// ── TWO QUESTIONS THAT LOOK LIKE ONE ─────────────────────────
        ///
        /// «Is it the same document?» and «is it still the same part?» have
        /// different answers, and so far only one was looked at. Title and
        /// path match perfectly while someone, in the meantime, has
        /// pulled a dimension by hand in the SOLIDWORKS window: the
        /// document is the same, the part is not.
        ///
        /// It matters because **the face uids are positional**. A plan
        /// is born from a reading and its `F1`, `F2` speak of that moment;
        /// if the geometry has changed, «F1» points to another face and
        /// the operation succeeds in the wrong place — which is the defect already
        /// measured on 14 September with the two pockets, both
        /// «succeeded: true».
        ///
        /// ── WHAT GOES IN, AND WHY ONLY THIS ──────────────────────────
        ///
        /// Volume, number of features, write date of the file. Three
        /// things **already all in hand**: they cost no extra reading and
        /// ask SOLIDWORKS for nothing that has not already been asked.
        /// It is not a strong fingerprint and does not want to be — *a comparison that
        /// costs as much as the thing it protects is done by nobody.*
        ///
        /// **What it does NOT catch, said here instead of discovered:** a change
        /// that leaves the volume identical and does not touch the count of
        /// features — moving a hole without changing its diameter, say.
        /// That one passes, and to catch it a fingerprint of the
        /// geometry would be needed, which this is not.
        /// </summary>
        private Dictionary<string, object> _signatureSeen;

        /// <summary>
        /// How the entities were made when «inspect_part» described
        /// them: uid -> point, normal, area, length.
        ///
        /// THE UIDS ARE POSITIONAL. The add-in reassigns them at every reading
        /// by ordering the faces by area, so one step that changes
        /// the part is enough for «F1» to point to another face. A plan, however, is
        /// born from ONE reading: its uids speak of that moment.
        ///
        /// MEASURED on 14 September 2026, three steps in a single call to
        /// «build» on a 200x100x20 block:
        ///   1. extrude the block          -> F1 = top face, 20,000 mm2
        ///   2. Ø60 pocket 5 deep on F1    -> the top face drops to
        ///      17,172.57 mm2 and goes BEHIND the bottom one in the order
        ///   3. Ø10 pocket 5 deep on F1    -> «F1» is now the BOTTOM one
        /// All three steps answered «succeeded: true». The small
        /// pocket ended up on the wrong face, the depths were
        /// the ones asked for, and the volume added up: 385,470.1 mm3, exact. There
        /// was nothing, in that answer, to raise suspicion.
        ///
        /// The add-in already knows how to defend itself: it reads «_signatures» from the arguments and
        /// compares. But that key was written only by the OpenPDM
        /// server, and here nobody wrote it — so the comparison
        /// had no terms and always passed. This field is the term
        /// that was missing.
        /// </summary>
        private Dictionary<string, object> _seenSignatures;

        /// <summary>
        /// The last plan executed, the document it went on, and how the
        /// document was right after (entry W2.7).
        ///
        /// All three are needed TOGETHER: the plan alone does not tell a
        /// resend from a deliberate repetition on a different document, and the state
        /// alone says nothing about what is being asked for.
        /// </summary>
        private string _lastPlanDone;
        private Dictionary<string, object> _planDocument;
        private string _summaryAfterPlan;

        /// <summary>
        /// How many views at most in a single answer.
        ///
        /// Three are enough to define a shape — isometric plus two — and are
        /// already three times the cost of the written state. Nine (all those
        /// that exist) would be an order of magnitude more for one
        /// part, and nobody would look at them all.
        /// </summary>
        private const int MaximumViews = 3;

        /// <summary>Width and height of the frame.</summary>
        private const int ViewWidth = 1024;
        private const int ViewHeight = 768;

        private Dictionary<string, object> InspectPart(
            Dictionary<string, object> arguments)
        {
            // WHICH FACES (entry W3.1-bis.12). A filter is read BEFORE touching
            // the document, so that a crooked one costs nothing; the default
            // reading is the one that has always been.
            FaceFilter facesWanted = null;
            var facesRaw = Take(arguments, "faces");
            if (facesRaw != null)
            {
                string facesProblem;
                facesWanted = FaceFilter.Parse(facesRaw, out facesProblem);
                if (facesWanted == null) return Protocol.Content(facesProblem, true);
            }
            // WHICH EDGES (brief R17), read before touching the document too.
            Oce.Solid.Agent.Core.EdgeFilter edgesWanted = null;
            var edgesRaw = Take(arguments, "edges");
            if (edgesRaw != null)
            {
                string edgesProblem;
                edgesWanted = Oce.Solid.Agent.Core.EdgeFilter.Parse(edgesRaw, out edgesProblem);
                if (edgesWanted == null) return Protocol.Content(edgesProblem, true);
            }
            // THE EDGES OF A DRAWING VIEW (report 8cd843 N23), read before touching the document too.
            var viewEdgesRaw = Take(arguments, "view_edges");
            string viewEdgesView = null, viewEdgesComponent = null;
            int viewEdgesLimit = ViewEdgesDefaultLimit;
            if (viewEdgesRaw != null)
            {
                string viewEdgesProblem = ViewEdgesRequest(viewEdgesRaw, out viewEdgesView, out viewEdgesComponent, out viewEdgesLimit);
                if (viewEdgesProblem != null) return Protocol.Content(viewEdgesProblem, true);
            }
            var previousSeen = _seen;
            var previousLight = _signatureSeen;
            var previousSignatures = _seenSignatures;

            string error;
            var docState = edgesWanted != null
                ? _session.StateWithFilters(facesWanted, edgesWanted, out error)
                : facesWanted == null
                    ? _session.State(out error)
                    : _session.StateWithFaces(facesWanted, out error);
            if (docState == null) return Protocol.Content(error, true);
            object edgesFilterProblem;
            if (docState.TryGetValue("edges_filter_problem", out edgesFilterProblem))
                return Protocol.Content(Convert.ToString(edgesFilterProblem), true);
            // The same as for the faces: a filter that was not applied is said.
            if (edgesWanted != null && !docState.ContainsKey("edges_filter"))
            {
                object edgeKind, edgeSolid;
                docState.TryGetValue("kind", out edgeKind);
                bool edgePart = string.Equals(Convert.ToString(edgeKind, System.Globalization.CultureInfo.InvariantCulture),
                                              "part", StringComparison.Ordinal);
                if (edgePart && docState.TryGetValue("solid_body", out edgeSolid) && edgeSolid is bool && !(bool)edgeSolid)
                {
                    docState["edges"] = new object[0];
                    docState["edges_filter_note"] = "The part has no solid body yet: there is no edge to list.";
                }
                else
                    return Protocol.Content(
                        "«edges» was not applied: it filters the edges of the solid bodies of a part, and the document " +
                        "in front (kind «" + Convert.ToString(edgeKind, System.Globalization.CultureInfo.InvariantCulture) +
                        "») has none to rank. Ask again without «edges».", true);
            }
            object facesFilterProblem;
            if (docState.TryGetValue("faces_filter_problem", out facesFilterProblem))
                return Protocol.Content(Convert.ToString(facesFilterProblem), true);
            // A FILTER THAT WAS NOT APPLIED IS SAID, not answered with the ordinary state:
            // an assembly, a drawing or a part with no solid body has no ranking of faces,
            // and a state without «faces_filter» would read as «nothing matched».
            if (facesWanted != null && !docState.ContainsKey("faces_filter"))
            {
                object kind;
                docState.TryGetValue("kind", out kind);
                // A PART WITHOUT A BODY YET HAS NO FACES, and that is an answer,
                // not an error (brief R9): on 24/10/2026 the first reading of a
                // new part with «faces» failed and had to be asked again.
                object solid;
                bool part = string.Equals(Convert.ToString(kind, System.Globalization.CultureInfo.InvariantCulture),
                                          "part", StringComparison.Ordinal);
                if (part && docState.TryGetValue("solid_body", out solid) && solid is bool && !(bool)solid)
                {
                    docState["faces"] = new object[0];
                    docState["faces_filter_note"] =
                        "The part has no solid body yet: there is no face to list.";
                }
                else
                return Protocol.Content(
                    "«faces» was not applied: it filters the faces of the solid bodies of a part, and the document " +
                    "in front (kind «" + Convert.ToString(kind, System.Globalization.CultureInfo.InvariantCulture) +
                    "») has none to rank. Ask again without «faces».", true);
            }
            _seen = StateIdentity(docState);
            _signatureSeen = LightSignature(docState);
            _seenSignatures = StateSignatures(docState);

            // A READING WITH A FILTER MUST NOT UNDO THE LAST ONE. The signatures
            // are what protects a plan from naming a face that is no longer
            // the one it saw; a filtered reading lists other faces, and
            // replacing the whole map would leave F1..F60 of the reading before
            // without one. When the document is the same one and the part has
            // not changed (the same test that stops a plan on a changed part),
            // what the reading before knew about the faces this one does not
            // list is kept. On a changed part it is not: those signatures speak
            // of another part.
            if (facesWanted != null && previousSignatures != null && previousSeen != null &&
                Bridge.Session.SameDocument(previousSeen, _seen) &&
                WhatChanged(previousLight, _signatureSeen) == null)
            {
                if (_seenSignatures == null) _seenSignatures = new Dictionary<string, object>();
                foreach (var keptUid in previousSignatures.Keys)
                    if (!_seenSignatures.ContainsKey(keptUid)) _seenSignatures[keptUid] = previousSignatures[keptUid];
            }
            RemoveAnchors(docState);

            // THE EDGES OF A DRAWING VIEW (report 8cd843 N23, round 7): added
            // after the identity and the signatures were taken, so that a plan
            // compares the same reading with or without them.
            if (viewEdgesRaw != null)
            {
                string viewEdgesError;
                var listing = _session.ViewEdges(viewEdgesView, viewEdgesComponent, viewEdgesLimit, out viewEdgesError);
                if (listing == null) return Protocol.Content(viewEdgesError, true);
                docState["view_edges"] = listing;
            }

            var requestedItems = RequestedViews(arguments);
            var section = Take(arguments, "section") as Dictionary<string, object>;
            if (requestedItems.Count == 0 && section == null)
                // The text for whoever reads, the data for whoever has to use them (W2.4).
                return Protocol.ContentWithData(Protocol.InJson(docState), docState);

            // THE STATE COMES OUT ANYWAY, even if no photograph succeeds.
            // An `inspect_part` that fails because a shot could not
            // be taken would take from the reader also the list of faces —
            // that is the only thing they really needed. The reasons for the
            // missed photographs are said in the text, next to the state.
            var images = new List<KeyValuePair<string, byte[]>>();
            var missed = new List<string>();
            var succeeded = new List<string>();

            foreach (var which in requestedItems)
            {
                // THE SAME DOCUMENT AS THE STATE, BEFORE EVERY SHOT.
                //
                // `State` and `Snapshot` ask SOLIDWORKS which is the
                // document in front, each on its own: between the two
                // calls it is enough for the person working to bring another
                // window forward and the bridge answers with the photograph of one part
                // and the measures of another. No error, no warning,
                // an image that contradicts the list next to which it sits.
                //
                // It is exactly the defect that cost «Part4» on 11 September,
                // redone in its purest form: two readings of `ActiveDoc`
                // treated as if they were one.
                //
                // It is checked ON EVERY PASS and not just once: three views
                // are three redraws and a couple of seconds, and the window
                // may change during that time.
                var now = _session.Identity(_session.ActiveDocument());
                if (!Session.SameDocument(_seen, now))
                {
                    missed.Add(which + ": in front now is «" +
                                ReadableName(now) + "», not «" +
                                ReadableName(_seen) + "». I do not photograph " +
                                "a part different from the one I just " +
                                "measured.");
                    continue;
                }

                var png = _session.Snapshot(
                    which, ViewWidth, ViewHeight, out var why);
                if (png != null && png.Length > 0)
                {
                    images.Add(new KeyValuePair<string, byte[]>("image/png", png));
                    succeeded.Add(which);
                }
                else
                {
                    missed.Add(which + ": " + (why ?? "I do not know why"));
                }
            }

            // ── THE CUTAWAY, if they asked for it (entry V4) ────────────
            //
            // It comes AFTER the external views and before the text: in the order in which
            // a part is looked at, first from outside and then inside.
            if (section != null)
            {
                var nowSec = _session.Identity(_session.ActiveDocument());
                if (!Session.SameDocument(_seen, nowSec))
                {
                    missed.Add("cutaway: in front now is «" +
                                ReadableName(nowSec) + "», not «" +
                                ReadableName(_seen) + "».");
                }
                else
                {
                    string plane = Text(section, "plane");
                    double offset = 0;
                    object raw = Take(section, "offset_mm");
                    if (raw != null)
                        try { offset = Convert.ToDouble(raw,
                                  System.Globalization.CultureInfo.InvariantCulture); }
                        catch { }

                    // A SECTION IS LOOKED AT HEAD-ON, and it is not a preference.
                    //
                    // The first run photographed it in the current orientation,
                    // that is isometric. Measured: a model that was
                    // given that image read the wall «6 times
                    // the height» and the bottom «8», when the truth is **12.5**
                    // for both — wrong by 40-50%.
                    //
                    // It was not its fault: in isometric a thin thickness is
                    // foreshortened, and looks thicker than it is. Looked at
                    // perpendicular to the cutting plane, the section is true
                    // to scale in the two directions that matter.
                    //
                    // The view is derived from the plane, and is not asked for: whoever asks for
                    // a cutaway on the right plane wants to look at it from the right.
                    // Being able to choose it would be one more way to get it wrong.
                    var png = _session.Cutaway(
                        ViewFacing(plane), plane, offset,
                        ViewWidth, ViewHeight, out var why);
                    if (png != null && png.Length > 0)
                    {
                        images.Add(new KeyValuePair<string, byte[]>("image/png", png));
                        succeeded.Add("cutaway on plane " + plane +
                                     (Math.Abs(offset) > 1e-9
                                         ? " at " + offset.ToString("0.##",
                                             System.Globalization.CultureInfo.InvariantCulture) + " mm"
                                         : ""));
                    }
                    else missed.Add("cutaway: " + (why ?? "I do not know why"));
                }
            }

            var text = new StringBuilder();
            if (succeeded.Count > 0)
            {
                // THE CAPTION BEFORE THE IMAGES, AND IN ORDER.
                // The images arrive without a name: knowing that the first is
                // the isometric and the second the front one is the difference between
                // a view and a riddle.
                text.Append(succeeded.Count == 1
                    ? "Below is the " + succeeded[0] + " view of the part."
                    : "Below are " + succeeded.Count + " views of the part, in " +
                      "this order: " + string.Join(", ", succeeded) + ".");
                // ── A NUMBER READ FROM AN IMAGE IS NOT AUTHORITATIVE ───────
                //
                // The caption already said where the measures are. It did not
                // say the thing that matters: that reading them FROM THE PHOTOGRAPH
                // gets them wrong, and by how much. On this project it has happened twice,
                // measured both times:
                //
                //   - a crown of holes **90 read as 79** from the isometric
                //     alone, with full confidence;
                //   - a wall of **12.5 read as «6»** and a bottom of 12.5
                //     read as «8», wrong by 40-50%, because in
                //     isometric a thin thickness is foreshortened.
                //
                // Neither error notices itself: the number that
                // comes out is plausible, and is used for the next step.
                // **Saying "the measures are in the state" is not enough** — it must be
                // said that those of the image are WRONG, and how to get
                // the true one (entry Z-C10).
                text.Append(" The photographs show the SHAPE. **Numbers " +
                             "read from an image are not authoritative**: on " +
                             "these same views a diameter of 90 was " +
                             "read as 79, and a wall of 12.5 was read as 6 " +
                             "— in isometric the thicknesses are foreshortened and " +
                             "look bigger. Before using a measurement " +
                             "in a step, take it from the state below " +
                             "or ask for it with «measure_between», " +
                             "«minimum_distance» or «body_bounding_box». The state " +
                             "also holds the identifiers.\n\n");
            }
            if (missed.Count > 0)
                text.Append("Views that failed — " +
                             string.Join("; ", missed) + "\n\n");

            text.Append(Protocol.InJson(docState));

            return Protocol.ContentWithImages(text.ToString(), images);
        }

        /// <summary>How many edges of a drawing view «view_edges» lists when «limit» is not given.</summary>
        internal const int ViewEdgesDefaultLimit = 200;

        /// <summary>
        /// What «view_edges» asks for (report 8cd843 N23): the view alone
        /// («V1»), or {view, component, limit}. Null when it can be taken,
        /// otherwise the reason. Pure, so the contract suite reads it.
        /// </summary>
        internal static string ViewEdgesRequest(object raw, out string view, out string component, out int limit)
        {
            view = null;
            component = null;
            limit = ViewEdgesDefaultLimit;
            var word = raw as string;
            if (word != null)
            {
                view = word.Trim().Length == 0 ? null : word.Trim();
                return null;
            }
            var map = raw as IDictionary<string, object>;
            if (map == null)
                return "«view_edges» is written as the view alone («V1») or as an object with some of: view, component, limit.";
            foreach (var key in map.Keys)
                if (key != "view" && key != "component" && key != "limit")
                    return "«view_edges» has no «" + key + "». It takes: view, component, limit.";
            object v;
            if (map.TryGetValue("view", out v) && v != null)
            {
                if (!(v is string)) return "«view_edges.view» is written as the identifier of the view (V1) or its name.";
                view = ((string)v).Trim().Length == 0 ? null : ((string)v).Trim();
            }
            if (map.TryGetValue("component", out v) && v != null)
            {
                if (!(v is string) || ((string)v).Trim().Length == 0)
                    return "«view_edges.component» is written as the name of a component as the list gives it (26P000011-1).";
                component = ((string)v).Trim();
            }
            if (map.TryGetValue("limit", out v) && v != null)
            {
                double d;
                if (v is bool || v is string || !(v is IConvertible))
                    return "«view_edges.limit» is written as a whole number from 1 to " + Oce.Solid.Agent.Core.SwOperations.MaxViewEdges + ".";
                d = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
                if (d < 1 || d > Oce.Solid.Agent.Core.SwOperations.MaxViewEdges || Math.Abs(d - Math.Round(d)) > 1e-9)
                    return "«view_edges.limit» is written as a whole number from 1 to " + Oce.Solid.Agent.Core.SwOperations.MaxViewEdges + ".";
                limit = (int)Math.Round(d);
            }
            return null;
        }

        /// <summary>
        /// Which views the reader asked for: real names, without duplicates, at
        /// most <see cref="MaximumViews"/>.
        ///
        /// A WRONG NAME IS NOT DISCARDED IN SILENCE: it arrives at
        /// `PartView.Fire`, which answers with the list of the real ones.
        /// Discarding it here would mean answering «here is the part» to someone who
        /// asked for a view it never had — and they would not understand why
        /// it is missing.
        /// </summary>
        private static List<string> RequestedViews(Dictionary<string, object> arguments)
        {
            // `Strings` WAS ALREADY THERE, and does exactly the hard thing: a
            // string is an `IEnumerable` **of characters**, so
            // `views: "isometric"` — the most natural error in the world, and the
            // schema asks for a list — scanned as a sequence would give three views
            // called «i», «s», «o». Three incomprehensible refusals in
            // place of the photograph asked for.
            //
            // Writing a second copy of it here would have been the defect that
            // this project has seen three times today: the right rule already
            // written elsewhere, and a new line that does not follow it.
            var outside = new List<string>();
            foreach (var x in Strings(Take(arguments, "views")))
            {
                var itemName = x.Trim();
                if (itemName.Length == 0) continue;
                if (outside.Contains(itemName, StringComparer.OrdinalIgnoreCase)) continue;
                outside.Add(itemName);
                if (outside.Count >= MaximumViews) break;
            }
            return outside;
        }

        /// <summary>
        /// The view from which a cutaway is looked at: the one perpendicular to the
        /// cutting plane.
        ///
        /// Null for a plane that is not known — and then the shot is taken in the
        /// current orientation, which is better than nothing: the refusal for
        /// the wrong name is done by `CaptureInSection`, which knows the real planes,
        /// and doing it here too would mean writing the same list twice.
        /// </summary>
        private static string ViewFacing(string plane)
        {
            switch (plane)
            {
                case "front": return "front";
                case "top": return "top";
                case "right": return "right";
                default: return null;
            }
        }

        /// <summary>
        /// The names as an array, which is the form JSON Schema wants for
        /// `enum`: a serialized `IEnumerable&lt;string&gt;` becomes
        /// an object, not a list.
        /// </summary>
        private static string[] Names(IEnumerable<string> names)
        {
            return new List<string>(names).ToArray();
        }

        /// <summary>
        /// THE ANCHOR IS NOT SENT TO THE READER, and it is not to save space —
        /// even though it does save some.
        ///
        /// `point_mm` and `anchor` do two different jobs and have two different
        /// recipients:
        ///
        ///   * `point_mm` serves to **select**: it is a point guaranteed to be on the
        ///     face, and the reader uses it to say where to work;
        ///   * `anchor` serves to **recognise**: it is the area centroid, which
        ///     is not necessarily on the face (that of a horseshoe-shaped
        ///     face falls in the void) and cannot be used to
        ///     select anything.
        ///
        /// Sending both would mean offering two points to someone who must
        /// use one, and the wrong choice would give no error: it would give a hole
        /// outside the material.
        ///
        /// And space matters all the same: on a four-bar frame the anchor
        /// on sixty faces is **1,400 characters**, and the state went from
        /// 15,700 to 17,171 — over the 16 KB ceiling the bench watches.
        ///
        /// It is removed AFTER building the signatures, which carry it along.
        /// </summary>
        private static void RemoveAnchors(Dictionary<string, object> docState)
        {
            if (docState == null) return;
            var faces = (docState.TryGetValue("faces", out var g) ? g : null)
                        as System.Collections.IEnumerable;
            if (faces == null) return;
            foreach (var v in faces)
            {
                var entry = v as Dictionary<string, object>;
                if (entry == null) continue;
                entry.Remove("anchor");
                // THE PERSIST ID IS REMOVED FOR TWO REASONS, and the second is enough
                // on its own. It is not readable — it is an opaque blob, and the reader cannot
                // do anything with it. And it weighs: 375-423 bytes per face, that is
                // ~530 characters in base64, which on sixty faces is ~32 KB,
                // DOUBLE the whole ceiling of the state.
                entry.Remove("persist_id");
            }
        }

        /// <summary>
        /// What is enough to recognise the same entity between two readings.
        ///
        /// The same four fields the OpenPDM server uses, so the
        /// two paths say the same thing to the add-in. The one that really
        /// compares is the add-in, and it looks at point and normal: the area of a face
        /// changes legitimately — a hole is made in it — while the
        /// orientation does not.
        /// </summary>
        private static Dictionary<string, object> StateSignatures(
            Dictionary<string, object> docState)
        {
            if (docState == null) return null;
            var outside = new Dictionary<string, object>();

            // THE PART'S BOX, the same for all the entities.
            //
            // It sits in the signature of each one and not once at the bottom because
            // `_signatures` is a uid -> signature map, and whoever reads it looks up by uid:
            // a key that is not a uid would be a special case to remember
            // in three places. Six numbers per entity in an attachment that already carries
            // ~530 characters of persist ID per face go unnoticed.
            //
            // IT SERVES ONLY TO WRITE A SENTENCE (entry E10): where the face was
            // relative to the part, when the part has changed. The reason it
            // cannot become an identity criterion is set out at length in
            // `Oce.Solid.Agent.Core/Sentences.cs`.
            double[] partBoxValue = PartBox(docState);

            // THE BODIES ARE ENTITIES LIKE THE OTHERS: for a welded part they are THE
            // entities, and «cap the ends of C1» names bodies.
            foreach (var family in new[] { "faces", "edges", "feature", "axes", "bodies" })
            {
                // IT IS SCANNED AS IEnumerable, NOT CAST TO object[].
                // The state arrives from the add-in with its real types, and there the
                // families are List<Dictionary<string, object>>: an
                // «as object[]» returns null in silence, and on 14 September 2026
                // this check stayed switched off because of that. Going through
                // IEnumerable both forms are fine, even the one that
                // comes from a JSON.
                var entries = (docState.TryGetValue(family, out var g) ? g : null)
                           as System.Collections.IEnumerable;
                if (entries == null) continue;
                foreach (var v in entries)
                {
                    var entry = v as Dictionary<string, object>;
                    if (entry == null || !entry.TryGetValue("uid", out var uid) || uid == null)
                        continue;
                    var signature = new Dictionary<string, object>();
                    // `feature` and `feature_id` serve the
                    // corroboration of the finding (entry E3): without them, a
                    // face of another feature that ended up in the same place
                    // is indistinguishable from the one being looked for.
                    // `center_mm` and `bounding_box_mm` are the signature of the BODIES, and
                    // up to 0.24.0 they were not copied: for «C1» the signature
                    // came out empty, `SignatureMatches` had nothing to
                    // compare and always answered yes. The bodies were
                    // checked for EXISTENCE and not for IDENTITY.
                    foreach (var theField in new[]
                             {
                                 "point_mm", "normal", "area_mm2", "length_mm",
                                 "feature_id", "center_mm", "bounding_box_mm", "anchor",
                                 "feature_id",
                                 // THE DIRECTION OF AN AXIS (entry E4): without it, the
                                 // signature of the axis would have only a point, and an axis
                                 // turned around that point would pass.
                                 "direction",
                                 // THE PERSIST ID TRAVELS HERE AND ONLY HERE.
                                 // It is the CANDIDATE of the finding (entry E6):
                                 // ~530 characters in base64 per face, which
                                 // in the state would be ~32 KB on sixty
                                 // faces — double the ceiling. The signature does not go
                                 // to any model, it is an attachment internal to the
                                 // step, and there space is not a problem.
                                 "persist_id",
                             })
                        if (entry.TryGetValue(theField, out var x) && x != null)
                            signature[theField] = x;

                    // THE FEATURE'S NAME IS RESOLVED HERE.
                    //
                    // The state writes it only once, in a map at the bottom,
                    // because repeating it on sixty faces made the 16 KB ceiling
                    // overflow. The signature instead carries it in full: it does not
                    // go to any model — it is an attachment internal to the step — and
                    // it serves to write a refusal that names the expected
                    // feature instead of giving its number.
                    if (signature.TryGetValue("feature_id", out var featId) && featId != null)
                    {
                        var itemName = FeatureName(docState, featId);
                        if (itemName != null) signature["feature"] = itemName;
                    }
                    // AFTER the count above, never before: a signature that
                    // had ONLY the part's box would say nothing about that
                    // entity, and would end up in `_signatures` all the same, making
                    // people believe a signature is there.
                    if (signature.Count > 0 && partBoxValue != null)
                        signature["box_part_mm"] = partBoxValue;
                    if (signature.Count > 0) outside[Convert.ToString(uid)] = signature;
                }
            }
            return outside.Count > 0 ? outside : null;
        }

        /// <summary>Title and path taken from the state, without going through COM again.</summary>
        private static Dictionary<string, object> StateIdentity(
            Dictionary<string, object> docState)
        {
            if (docState == null) return null;
            return new Dictionary<string, object>
            {
                ["document_title"] = docState.TryGetValue("document_title", out var t) ? t : "",
                ["path"] = docState.TryGetValue("path", out var p) ? p : "",
            };
        }

        /// <summary>
        /// The light signature of a state: volume, how many features, when
        /// the file was written (entry X2). See <see cref="_signatureSeen"/>.
        ///
        /// The keys that are missing do NOT become zero. A part without volume —
        /// an empty document — has a signature without `volume_mm3`, and a
        /// comparison between «there was none» and «now there is» is a real difference
        /// that must be said; putting a zero in would turn it into «the volume
        /// went from 0 to 96000», which is another sentence and tells another
        /// story.
        /// </summary>
        private static Dictionary<string, object> LightSignature(
            Dictionary<string, object> docState)
        {
            if (docState == null) return null;
            var f = new Dictionary<string, object>(StringComparer.Ordinal);

            if (docState.TryGetValue("volume_mm3", out var v) && v != null)
                f["volume_mm3"] = v;

            if (docState.TryGetValue("feature", out var e) &&
                e is System.Collections.IList list)
                f["features"] = list.Count;

            // THE FILE'S DATE CATCHES WHAT THE VOLUME DOES NOT SEE: a save
            // made from outside. A part never saved has no file, and not having the
            // key is the right answer — not «date zero».
            var filePath = Convert.ToString(
                docState.TryGetValue("path", out var p) ? p : "");
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                try
                {
                    if (File.Exists(filePath))
                        f["file_written"] = File.GetLastWriteTimeUtc(filePath)
                            .ToString("o", CultureInfo.InvariantCulture);
                }
                catch { }
            }
            return f;
        }

        /// <summary>
        /// What changed between two signatures, in Italian, or `null` if
        /// nothing changed (entry X2).
        ///
        /// **IT STOPS WITH THE CRITERION, NOT WITH A VERDICT.** «The volume went
        /// from 96,000 to 94,500 since you looked at it» can be
        /// checked; «the state has changed» forces one to trust. The
        /// difference matters because a warning that cannot be verified is
        /// learned to be ignored, and that is the risk written in the entry.
        /// </summary>
        internal static string WhatChanged(
            Dictionary<string, object> before, Dictionary<string, object> now)
        {
            if (before == null || now == null) return null;

            var called = new List<string>();
            foreach (var key in new[] { "volume_mm3", "features", "file_written" })
            {
                bool existedBefore = before.TryGetValue(key, out var a);
                bool existsNow = now.TryGetValue(key, out var b);
                if (!existedBefore && !existsNow) continue;

                string textA = existedBefore ? Convert.ToString(a, CultureInfo.InvariantCulture) : null;
                string textB = existsNow ? Convert.ToString(b, CultureInfo.InvariantCulture) : null;
                if (string.Equals(textA, textB, StringComparison.Ordinal)) continue;

                if (key == "volume_mm3")
                    called.Add("the volume went from " + (textA ?? "(none)") +
                              " to " + (textB ?? "(none)") + " mm3");
                else if (key == "features")
                    called.Add("the features went from " + (textA ?? "(none)") +
                              " to " + (textB ?? "(none)"));
                else
                    called.Add("the file was rewritten (" + (textA ?? "(never)") +
                              " -> " + (textB ?? "(never)") + ")");
            }

            return called.Count == 0 ? null : string.Join("; ", called);
        }

        private static string ReadableName(Dictionary<string, object> identity)
        {
            if (identity == null) return "(none)";
            var p = Convert.ToString(identity.TryGetValue("path", out var x) ? x : "");
            if (!string.IsNullOrWhiteSpace(p)) return p;
            var t = Convert.ToString(identity.TryGetValue("document_title", out var y) ? y : "");
            return string.IsNullOrWhiteSpace(t) ? "(no name)" : t;
        }

        private Dictionary<string, object> ListOperations(
            Dictionary<string, object> arguments)
        {
            if (_catalog.LoadingError != null)
                return Protocol.Content(_catalog.LoadingError, true);
            var family = Text(arguments, "family");
            var list = _catalog.Items(family);
            return Protocol.ContentWithData(Protocol.InJson(list), list);
        }

        private Dictionary<string, object> DescribeOperation(
            Dictionary<string, object> arguments)
        {
            if (_catalog.LoadingError != null)
                return Protocol.Content(_catalog.LoadingError, true);

            var itemName = Text(arguments, "name");
            var descriptionText = _catalog.Description(itemName);
            if (descriptionText == null)
            {
                // AN ERROR THAT HELPS instead of one that merely states: whoever
                // got the name wrong almost always has a nearby one in mind.
                var similar = _catalog.Similar(itemName);
                _registry.OperationThatDoesNotExist(itemName, similar);
                return Protocol.Content(
                    "There is no operation called «" + itemName + "»." +
                    (similar.Count > 0
                        ? " Did you mean: " + string.Join(", ", similar) + "."
                        : " Ask «list_operations» to see which ones exist."),
                    true);
            }
            return Protocol.ContentWithData(Protocol.InJson(descriptionText), descriptionText);
        }

        /// <summary>
        /// The real run: it is executed, and WE LOOK AT WHAT CAME OUT.
        ///
        /// The result is not «succeeded». It is the measures of every step — the
        /// volume removed, the real diameter of the hole, how many walls it pierced —
        /// and the state of the document AFTER. It is the only form that lets the one
        /// who asked notice that the part is not what they wanted.
        /// </summary>
        /// <summary>
        /// The sentence added to every refusal of FORM.
        ///
        /// The plan is checked in full before starting, so an error at
        /// step 3 also stops the first two. The answer, however, lists only the
        /// step that got it wrong, and without this sentence the reader may believe
        /// that the previous ones were executed — and start looking in the
        /// document for a plate that is not there.
        /// </summary>
        private const string NothingTouched =
            " The plan was not run at all: the document is left " +
            "as it was. Fix this step and send the whole plan again.";

        /// <summary>
        /// `measure` — `build` without the permission to touch.
        ///
        /// THE GATE IS HERE AND NOT INSIDE `Build`, and the reason is that it must
        /// fire BEFORE the plan begins. A plan of three measures and
        /// an extrusion, stopped at the fourth step, would already have measured three
        /// times and then refused: whoever reads the answer sees three good measures and
        /// an error, and has no way to know that the part is still intact
        /// only by luck. Here ALL the steps are looked at, and if even one is not
        /// a verification nothing starts.
        ///
        /// WE SAY WITH WHICH TOOL IT IS DONE, not just that here it is not done. A
        /// refusal that leads nowhere forces the reader to
        /// guess, and guessing is the way one gets it wrong in silence.
        /// </summary>
        /// <summary>
        /// The fingerprint of a plan: the operations and their arguments, in order.
        ///
        /// IT IS SERIALIZED TO JSON and passed through a SHA-1 instead of comparing
        /// the structures: two dictionaries with the same keys in a different order
        /// are the same plan, and `InJson` writes them the same way. A
        /// comparison done by hand on the dictionaries would have had to decide what
        /// counts and what does not, and every decision of that kind is a place where
        /// two different plans become equal.
        /// </summary>
        private static string PlanFingerprint(object[] steps)
        {
            try
            {
                var text = Protocol.InJson(steps);
                using (var sha = System.Security.Cryptography.SHA1.Create())
                {
                    var b = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
                    var s = new System.Text.StringBuilder(b.Length * 2);
                    foreach (var x in b) s.Append(x.ToString("x2", CultureInfo.InvariantCulture));
                    return s.ToString();
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// The first identifier of the part a plan cites, or null (brief R18).
        ///
        /// TWO KINDS, read in every argument at any depth:
        ///  * any text under a key that ends in «_uid» or «_uids»: those name
        ///    what was already in the document (faces, edges, features);
        ///  * a text that has the shape of an identifier the state hands out:
        ///    one letter among C, E, F, M, S, V and a number («F3», «C2», «M1»),
        ///    under a key that names what identifiers name («component_a»,
        ///    «face_b», «mate», «plane»: see <see cref="IdentifierKeys"/>).
        ///    Under any other key it is a value: «S235» in «value_text» is a steel.
        /// The keys of a size or a thread («size», «thread_size») are skipped:
        /// «M6» there is a screw, not a mate. «*_ref» cites a step of the plan,
        /// not the part, and is not an identifier.
        /// </summary>
        internal static string FirstIdentifierCited(object[] steps)
        {
            if (steps == null) return null;
            foreach (var step in steps)
            {
                var s = step as Dictionary<string, object>;
                object arguments;
                if (s == null || !s.TryGetValue("arguments", out arguments)) continue;
                string found = IdentifierIn(arguments, null);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// The words of a key under which a text shaped like «F3» is an
        /// identifier. Under any other key it is a value: «S235» is a steel,
        /// «C45» another. Compared WORD BY WORD («component_a» has «component»):
        /// a substring would read «material» as «mate».
        /// </summary>
        private static readonly string[] IdentifierKeys =
        {
            "component", "components", "mate", "mates", "face", "faces", "edge", "edges", "body", "bodies",
            "sketch", "sketches", "feature", "features", "axis", "axes", "plane", "planes", "view", "views",
            "vertex", "vertices", "entity", "entities", "reference", "references", "target", "targets",
        };

        /// <summary>One of C, E, F, M, S, V and only digits after it: «F3», «C12».</summary>
        private static bool HasIdentifierShape(string t)
        {
            if (t.Length < 2 || "CEFMSV".IndexOf(t[0]) < 0) return false;
            for (int i = 1; i < t.Length; i++) if (t[i] < '0' || t[i] > '9') return false;
            return true;
        }

        private static string IdentifierIn(object value, string key)
        {
            string text = value as string;
            if (text != null)
            {
                string k = (key ?? "").ToLowerInvariant();
                if (k.Contains("size") || k.Contains("thread")) return null;
                string t = text.Trim();
                if (t.Length == 0) return null;
                if (k.EndsWith("_uid", StringComparison.Ordinal) || k.EndsWith("_uids", StringComparison.Ordinal)) return t;
                if (!HasIdentifierShape(t)) return null;
                foreach (string word in k.Split('_'))
                    if (Array.IndexOf(IdentifierKeys, word) >= 0) return t;
                return null;
            }
            var map = value as Dictionary<string, object>;
            if (map != null)
            {
                foreach (string name in map.Keys)
                {
                    if (name.EndsWith("_ref", StringComparison.Ordinal)) continue;
                    string found = IdentifierIn(map[name], name);
                    if (found != null) return found;
                }
                return null;
            }
            var list = value as System.Collections.IEnumerable;
            if (list != null)
            {
                foreach (var item in list)
                {
                    string found = IdentifierIn(item, key);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>
        /// How the document stands, in a few measures: volume, bodies, how many
        /// features.
        ///
        /// It is NOT a cryptographic signature of the whole state, and that is deliberate. The
        /// complete state changes even when the part does not change — a uid that
        /// is renumbered, a face that enters or leaves the ceiling — and a comparison
        /// on everything would say «different» every time, switching the check off without
        /// anyone noticing. **A check that never fires is a
        /// check that is switched off**, and this is the most common way of switching one off:
        /// making it too sensitive.
        ///
        /// The three measures taken here change when the PART changes, and do not
        /// change when only the way it was looked at changes.
        /// </summary>
        /// <summary>
        /// THE STATE IN A FEW LINES (brief R3): what a plan changed, without the
        /// lists of faces and edges. The fields are copied as the state names
        /// them; the features added and removed are the difference of the two
        /// lists of names, before and after.
        /// </summary>
        private static Dictionary<string, object> SummaryOfState(
            Dictionary<string, object> before, Dictionary<string, object> after)
        {
            var summary = new Dictionary<string, object>();
            foreach (var key in SummaryFields)
            {
                object v;
                if (after.TryGetValue(key, out v) && v != null) summary[key] = v;
            }
            object list;
            if (after.TryGetValue("faces", out list) && list is System.Collections.ICollection)
                summary["faces_listed"] = ((System.Collections.ICollection)list).Count;
            if (after.TryGetValue("bodies", out list) && list is System.Collections.ICollection)
                summary["bodies"] = ((System.Collections.ICollection)list).Count;
            var namesBefore = FeatureNamesInOrder(before) ?? new List<string>();
            var namesAfter = FeatureNamesInOrder(after) ?? new List<string>();
            var added = namesAfter.Where(n => !namesBefore.Contains(n)).ToList();
            var removed = namesBefore.Where(n => !namesAfter.Contains(n)).ToList();
            if (added.Count > 0) summary["features_added"] = added;
            if (removed.Count > 0) summary["features_removed"] = removed;
            return summary;
        }

        /// <summary>
        /// THE TIME OF THE WHOLE PLAN BY PHASE (brief R2): the sum of each step's
        /// «timing_ms», and the slowest step. Null when no step was timed, as in
        /// a plan refused before it ran.
        /// </summary>
        private static Dictionary<string, object> PlanTiming(List<object> outcomes)
        {
            var totals = new Dictionary<string, double>();
            int order = 0, slowestOrder = 0;
            double slowest = -1;
            string slowestOperation = null;
            foreach (var o in outcomes)
            {
                order++;
                var outcome = o as Dictionary<string, object>;
                object t;
                if (outcome == null || !outcome.TryGetValue("timing_ms", out t)) continue;
                var timing = t as Dictionary<string, object>;
                if (timing == null) continue;
                foreach (string phase in timing.Keys)
                {
                    double ms;
                    if (!TryNumber(timing[phase], out ms)) continue;
                    double sum;
                    totals.TryGetValue(phase, out sum);
                    totals[phase] = sum + ms;
                    if (phase == "total" && ms > slowest)
                    {
                        slowest = ms;
                        slowestOrder = order;
                        object op;
                        outcome.TryGetValue("operation", out op);
                        slowestOperation = Convert.ToString(op, CultureInfo.InvariantCulture);
                    }
                }
            }
            if (totals.Count == 0) return null;
            var plan = new Dictionary<string, object>();
            foreach (string phase in totals.Keys) plan[phase] = Math.Round(totals[phase]);
            plan["slowest_step"] = slowestOrder + " " + slowestOperation + " " + Math.Round(slowest) + " ms";
            return plan;
        }

        private static bool TryNumber(object v, out double d)
        {
            d = 0;
            if (v == null || v is string || v is bool) return false;
            try { d = Convert.ToDouble(v, CultureInfo.InvariantCulture); return true; }
            catch (FormatException) { return false; }
            catch (InvalidCastException) { return false; }
        }

        /// <summary>
        /// THE OUTCOMES OF A «summary» ANSWER (brief R3). On 26/10/2026 a plan of
        /// 21 steps answered 9,782 characters after the state had left: the
        /// outcomes were 95% of them. Of a step that succeeded it drops what the
        /// reader does not need to write the next plan:
        ///   - «error», which is null;
        ///   - «timing_ms», summed for the whole plan in the answer's
        ///     «timing_ms»;
        ///   - «volume_before_mm3» and «volume_after_mm3» when they are the
        ///     same: the step did not change the volume, and the absence says it;
        ///   - texts longer than <see cref="LongExplanation"/> characters that
        ///     explain rather than warn. A key that names a warning, a problem,
        ///     a verdict or an error always stays, whatever its length;
        ///   - a measure with the SAME NAME and the SAME VALUE as an argument the
        ///     caller sent in that step (26/10/2026: «value_text», «material»,
        ///     «name», «value_mm» came back as they went). Only the same name: a
        ///     measure that happens to equal another argument is a measure;
        ///   - «value_before_*» when it equals its «value_after_*».
        /// A step that failed keeps everything. Returns how many texts it left
        /// out, and in <paramref name="echoes"/> how many echoes, so the answer
        /// can say so.
        /// </summary>
        private static int CompactOutcomes(List<object> outcomes, object[] steps, out int echoes)
        {
            int leftOut = 0;
            echoes = 0;
            for (int i = 0; i < outcomes.Count; i++)
            {
                var outcome = outcomes[i] as Dictionary<string, object>;
                if (outcome == null) continue;
                object ok;
                if (!outcome.TryGetValue("succeeded", out ok) || !(ok is bool) || !(bool)ok) continue;
                object error;
                if (outcome.TryGetValue("error", out error) && error == null) outcome.Remove("error");
                outcome.Remove("timing_ms");
                object m;
                var measurements = outcome.TryGetValue("measurements", out m) ? m as Dictionary<string, object> : null;
                if (measurements == null) continue;
                object rawBefore, rawAfter;
                double before, after;
                if (measurements.TryGetValue("volume_before_mm3", out rawBefore) && TryNumber(rawBefore, out before) &&
                    measurements.TryGetValue("volume_after_mm3", out rawAfter) && TryNumber(rawAfter, out after) &&
                    Math.Abs(before - after) < VolumeUnchangedMm3)
                {
                    measurements.Remove("volume_before_mm3");
                    measurements.Remove("volume_after_mm3");
                }
                foreach (var key in measurements.Keys.ToList())
                {
                    var text = measurements[key] as string;
                    if (text == null || text.Length <= LongExplanation || Warns(key)) continue;
                    measurements.Remove(key);
                    leftOut++;
                }
                foreach (var key in measurements.Keys.ToList())
                {
                    if (!key.StartsWith("value_before_", StringComparison.Ordinal)) continue;
                    object afterValue;
                    if (measurements.TryGetValue("value_after_" + key.Substring("value_before_".Length), out afterValue) &&
                        SameValue(measurements[key], afterValue))
                        measurements.Remove(key);
                }
                // The step's arguments, only when the step is the one this outcome
                // speaks of: outcomes and steps go in the same order, but a name
                // that differs means the pairing cannot be trusted.
                var step = steps != null && i < steps.Length ? steps[i] as Dictionary<string, object> : null;
                object sentOperation, sentArguments;
                var sent = step != null && step.TryGetValue("operation", out sentOperation) &&
                           string.Equals(sentOperation as string, Text(outcome, "operation"), StringComparison.OrdinalIgnoreCase) &&
                           step.TryGetValue("arguments", out sentArguments)
                    ? sentArguments as Dictionary<string, object> : null;
                if (sent == null) continue;
                foreach (var key in measurements.Keys.ToList())
                {
                    object sentValue;
                    if (Warns(key) || !sent.TryGetValue(key, out sentValue) || !SameValue(measurements[key], sentValue)) continue;
                    measurements.Remove(key);
                    echoes++;
                }
            }
            return leftOut;
        }

        /// <summary>
        /// How many measures of the outcomes are warnings (a key that starts with
        /// «warning», holding a warning: see SaysAWarning). Counted after the
        /// compaction: they are the ones the reader sees. Pure, so the contract
        /// suite reads it.
        /// </summary>
        internal static int WarningsInOutcomes(List<object> outcomes)
        {
            int warnings = 0;
            foreach (var o in outcomes)
            {
                var outcome = o as Dictionary<string, object>;
                object m;
                var measurements = outcome != null && outcome.TryGetValue("measurements", out m) ? m as Dictionary<string, object> : null;
                if (measurements == null) continue;
                foreach (string key in measurements.Keys)
                    if (key.StartsWith("warning", StringComparison.OrdinalIgnoreCase) && SaysAWarning(measurements[key])) warnings++;
            }
            return warnings;
        }

        /// <summary>
        /// Whether the value of a «warning…» key is a warning: a text, or a list with
        /// something in it. «warnings» 0 of save_document (how many bits of the
        /// Warnings mask of the save are set) is a number, and it was counted: the
        /// note said «the 1 warning(s) of the measures stay» over measures with no
        /// warning (report 8cd843 N37, round 10). Pure, so the contract suite reads it.
        /// </summary>
        internal static bool SaysAWarning(object value)
        {
            string text = value as string;
            if (text != null) return !string.IsNullOrWhiteSpace(text);
            if (value is IEnumerable<object>)
                foreach (object item in (IEnumerable<object>)value) return true;
            return false;
        }

        /// <summary>
        /// The line that says what the compact answer left out, or null when nothing
        /// was. «None is a warning» alone read as «no warning» next to two warnings
        /// in the measures (report 8cd843 N36, round 9): the warnings the measures
        /// keep are counted and pointed at. Pure, so the contract suite reads it.
        /// </summary>
        internal static string NoteOutcomes(int explanationsLeftOut, int echoesLeftOut, int warningsKept)
        {
            if (explanationsLeftOut <= 0 && echoesLeftOut <= 0) return null;
            string kept = warningsKept > 0
                ? "; the " + warningsKept + " warning(s) of the measures stay, above"
                : "";
            return (explanationsLeftOut > 0
                       ? explanationsLeftOut + " explanation(s) of successful steps left out (none of them is a warning" +
                         kept + "). "
                       : (warningsKept > 0 ? "The " + warningsKept + " warning(s) of the measures stay, above. " : "")) +
                   (echoesLeftOut > 0
                       ? echoesLeftOut + " measure(s) left out because they came back equal to the argument " +
                         "of the same name you sent. "
                       : "") +
                   "Send «response»: «full» to have them, and the times of each step.";
        }

        /// <summary>Two scalar values that say the same: numbers within 1e-9, texts equal ignoring case and outer blanks.</summary>
        private static bool SameValue(object one, object other)
        {
            double a, b;
            if (TryNumber(one, out a) && TryNumber(other, out b)) return Math.Abs(a - b) < 1e-9;
            var s = one as string;
            var t = other as string;
            if (s != null && t != null) return string.Equals(s.Trim(), t.Trim(), StringComparison.OrdinalIgnoreCase);
            return false;
        }

        /// <summary>Longer than this, a text of a successful step is an explanation.</summary>
        private const int LongExplanation = 100;

        /// <summary>Two volumes closer than this are the same (they come rounded to 0.01).</summary>
        private const double VolumeUnchangedMm3 = 0.005;

        private static bool Warns(string key)
        {
            var k = key.ToLowerInvariant();
            return k.Contains("warn") || k.Contains("problem") || k.Contains("verdict") ||
                   k.Contains("error") || k.Contains("fail");
        }

        private static readonly string[] SummaryFields =
        {
            "path", "document_title", "kind", "solid_body", "volume_mm3", "mass_kg", "material",
            "bounding_box_mm", "bounding_box_from_mm", "shape", "feature_total", "faces_total",
            "edges_total", "features_in_error", "rebuild_needed", "equations_total",
            "configuration", "property", "configuration_properties",
        };

        /// <summary>
        /// The problem with the material of an `apply_material` step, or null
        /// (brief R4). Asked to the workstation's archives, the same that the
        /// step will search.
        /// </summary>
        private string MaterialProblemOf(string operation, Dictionary<string, object> arguments)
        {
            if (!string.Equals(operation, "apply_material", StringComparison.Ordinal)) return null;
            return _session.MaterialProblem(Text(arguments, "material"), Text(arguments, "library"));
        }

        private static string StateSummary(Dictionary<string, object> docState)
        {
            if (docState == null) return null;
            Func<string, string> theField = k =>
            {
                object v;
                return docState.TryGetValue(k, out v) && v != null
                    ? Convert.ToString(v, CultureInfo.InvariantCulture)
                    : "-";
            };
            return theField("volume_mm3") + "|" + theField("bodies") + "|" +
                   theField("how_many_features") + "|" + theField("document_title") +
                   AssemblyFingerprint(docState);
        }

        /// <summary>
        /// THE RESENT-PLAN GUARD WAS BLIND ON AN ASSEMBLY (scenario t9b,
        /// 27/10/2026): volume, bodies and features read the same on every
        /// assembly, so a plan inserted, withdrawn with «undo» and sent again was
        /// refused as «already run, and the document is still exactly as I left
        /// it». On an assembly the summary also carries its components (name,
        /// file, state, fixed, origin, axes) and mates (name, kind, suppressed,
        /// what they hold), as the undo snapshot reads them. Empty on a part.
        /// </summary>
        private static string AssemblyFingerprint(Dictionary<string, object> docState)
        {
            PartSnapshot s = PartSnapshot.Of(docState);
            if (s == null || !s.Assembly) return "";
            var words = new List<string>();
            words.Add(s.ComponentsRead ? "components " + s.ComponentTotal : "components unread");
            if (s.ComponentsRead)
                foreach (ComponentLook c in s.Components)
                    words.Add(c.Name + "/" + c.Path + "/" + c.State + "/" + c.Grounded + "/" +
                              (c.Origin == null ? "?" : NumbersText(c.Origin)) + "/" + (c.AxesText ?? "?"));
            words.Add(s.MatesRead ? "mates " + s.MateTotal : "mates unread");
            if (s.MatesRead)
                foreach (MateLook m in s.Mates) words.Add(m.Name + ": " + m.Look);
            return "|" + string.Join("|", words);
        }

        private Dictionary<string, object> Measure(Dictionary<string, object> arguments)
        {
            var steps = Take(arguments, "steps") as object[];
            if (steps == null || steps.Length == 0)
                return Protocol.Content(
                    "There is nothing to measure: «steps» must be a list " +
                    "of {operation, arguments}.", true);

            // A GATE THAT KNOWS NOTHING MUST CLOSE, NOT OPEN.
            //
            // The gate below decides by looking at the FAMILY, and the family
            // is known only to the catalogue. If the catalogue did not load,
            // `Family()` answers null for everything, every step passes the
            // check, and `measure` — which declared itself read-only in the
            // protocol, and for which the client has already decided NOT to ask for
            // confirmation — would execute an extrusion.
            //
            // It would be a defect that gives no error in its worst form:
            // the defence disappears exactly when something has already gone
            // wrong, and the symptom is that everything works.
            if (_catalog.LoadingError != null)
                return Protocol.Content(
                    "I cannot measure: the catalog did not load (" +
                    _catalog.LoadingError + "). Without the catalog I do not know " +
                    "which operations only read and which touch the part, and " +
                    "«measure» runs nothing it cannot vouch for. " +
                    "With «build» the same plan runs, but your client knows " +
                    "that one can modify the part.", true);

            var outOfPlace = new List<string>();
            for (int i = 0; i < steps.Length; i++)
            {
                var planStep = steps[i] as Dictionary<string, object>;
                string itemName = planStep == null ? null : Text(planStep, "operation");
                if (string.IsNullOrWhiteSpace(itemName)) continue;   // Build will say so

                string family = _catalog.Family(itemName);
                // An operation that the catalogue does not know is NOT a problem
                // for this gate: `Build` says it, with the suggestions of
                // `Similar`. Here we stay silent, otherwise the same mistake would give
                // two different messages depending on the tool.
                if (family == null) continue;
                if (!string.Equals(family, ReadOnlyFamily, StringComparison.OrdinalIgnoreCase))
                    outOfPlace.Add("step " + (i + 1) + ", «" + itemName +
                                   "» (family «" + family + "»)");
            }

            if (outOfPlace.Count > 0)
                return Protocol.Content(
                    "«measure» only reads, and " +
                    (outOfPlace.Count == 1 ? "this step touches" : "these steps touch") +
                    " the part: " + string.Join("; ", outOfPlace) + ". " +
                    "I ran nothing — not even the measures that came " +
                    "before, because half a plan is a result that cannot be " +
                    "read. To do these operations use «build», which your " +
                    "client knows to be destructive. To find out what " +
                    "«measure» can read: «list_operations» with family «" +
                    ReadOnlyFamily + "».", true);

            // WITHOUT the guard on repetition: measuring the same
            // thing twice is legitimate, and `measure` declares itself idempotent.
            return RunPlan(arguments, checkIfRepeated: false);
        }

        /// <summary>
        /// The only family that does not touch the part.
        ///
        /// «identity» is NOT here too, and it must be said because it is the direction in
        /// which being wrong costs most: `set_property`, `create_configuration` and
        /// `activate_configuration` WRITE into the document. Declaring them read-only
        /// would mean a client that does not ask for confirmation while
        /// someone changes the active configuration of a confirmed part.
        /// </summary>
        private const string ReadOnlyFamily = "checks";

        /// <summary>
        /// THE DRY RUN: getting it wrong without touching the part (entry Z-G6).
        ///
        /// ── WHY IT IS NEEDED ─────────────────────────────────────────
        ///
        /// `build` already checks the whole plan before starting, and it
        /// is the most valuable piece of the bridge: a wrong plan touches
        /// nothing. But that check **was not reachable without executing**:
        /// to know whether a plan holds one had to launch it, and a plan that
        /// holds halfway leaves half a part made.
        ///
        /// ── WHAT IT LOOKS AT, AND ABOVE ALL WHAT IT DOES NOT ─────────
        ///
        /// Without SOLIDWORKS a lot can be said: that the operation exists, that
        /// the mandatory arguments are there, that there are no
        /// **unknown** ones — a misspelled argument is ignored in
        /// silence, and it is the defect that gives no error — and that a
        /// reference to a previous step points to a step that exists.
        ///
        /// It cannot say whether a `uid` is still valid, whether the document is
        /// of the right type, whether the face is still there: for that the
        /// document must be read. **The answer says so in plain words**, because a
        /// dry run that stays silent about what it did not look at is worse than
        /// no dry run: it makes people believe the plan is fine.
        /// </summary>
        private Dictionary<string, object> TestThePlan(Dictionary<string, object> arguments)
        {
            if (_catalog.LoadingError != null)
                return Protocol.Content(_catalog.LoadingError, true);

            var steps = Take(arguments, "steps") as object[];
            if (steps == null || steps.Length == 0)
                return Protocol.Content(
                    "The plan is empty: «steps» must be a list of " +
                    "{operation, arguments}.", true);

            var trouble = new List<string>();

            // NO PROGRESS FROM THE DRY RUN (brief R13). It answers in
            // milliseconds, and on 24/10/2026 its notifications reached the
            // client AFTER the answer: the client no longer knew the token and
            // wrote «Connection error ... STDIO connection dropped» eight times
            // for a bridge that was working. A progress for something that is
            // already over tells nobody anything.

            for (int i = 0; i < steps.Length; i++)
            {
                var planStep = steps[i] as Dictionary<string, object>;
                string location = "step " + (i + 1);
                if (planStep == null) { trouble.Add(location + ": is not an object"); continue; }

                var itemName = Text(planStep, "operation");
                if (string.IsNullOrWhiteSpace(itemName))
                { trouble.Add(location + ": «operation» is missing"); continue; }

                if (!_catalog.Knows(itemName))
                {
                    var similar = _catalog.Similar(itemName);
                    _registry.OperationThatDoesNotExist(itemName, similar);
                    trouble.Add(location + ": «" + itemName + "» does not exist" +
                             (similar != null && similar.Count > 0
                                 ? ". Did you mean: " + string.Join(", ", similar)
                                 : ""));
                    continue;
                }

                // The form of «expect» (brief R16); its numbers only after a run.
                object rawExpect;
                planStep.TryGetValue("expect", out rawExpect);
                var expectProblem = Oce.Solid.Agent.Core.StepExpectations.FormProblem(rawExpect);
                if (expectProblem != null) trouble.Add(location + " («" + itemName + "»): " + expectProblem);

                var schema = _catalog.Schema(itemName);
                var data = Take(planStep, "arguments") as Dictionary<string, object>
                           ?? new Dictionary<string, object>();
                if (schema == null) continue;

                var property = Take(schema, "properties") as Dictionary<string, object>;
                var requested = Take(schema, "required") as object[];

                if (requested != null)
                    foreach (var r in requested)
                    {
                        var theField = Convert.ToString(r);
                        if (string.IsNullOrEmpty(theField)) continue;
                        // `x_ref` holds for `x_uid` and vice versa: the catalogue
                        // declares one of the two mandatory, and asking for
                        // both would be a wrong refusal.
                        var twin = theField.EndsWith("_uid", StringComparison.Ordinal)
                            ? theField.Substring(0, theField.Length - 4) + "_ref"
                            : theField.EndsWith("_ref", StringComparison.Ordinal)
                                ? theField.Substring(0, theField.Length - 4) + "_uid"
                                : null;
                        if (data.ContainsKey(theField)) continue;
                        if (twin != null && data.ContainsKey(twin)) continue;
                        trouble.Add(location + " («" + itemName + "»): «" + theField + "» is missing");
                    }

                if (property != null)
                    trouble.AddRange(UnknownArgumentsOfStep(location, itemName, data, property));

                // The material, when SOLIDWORKS is there to say (brief R4).
                var materialTrouble = MaterialProblemOf(itemName, data);
                if (materialTrouble != null)
                    trouble.Add(location + " («" + itemName + "»): " + materialTrouble);

                // The references to a previous step: `{step: N, kind: ...}`.
                foreach (var pair in data)
                {
                    if (!pair.Key.EndsWith("_ref", StringComparison.Ordinal)) continue;
                    var refObj = pair.Value as Dictionary<string, object>;
                    if (refObj == null) continue;
                    var n = Take(refObj, "step");
                    int which;
                    if (n == null || !int.TryParse(Convert.ToString(n), out which)) continue;
                    if (which >= 1 && which <= i) continue;
                    trouble.Add(location + " («" + itemName + "»): «" + pair.Key +
                             "» refers to step " + which + ", which " +
                             (which > i ? "has not been done yet" : "does not exist"));
                }
            }

            var text = new StringBuilder();
            text.Append(trouble.Count == 0
                ? "DRY RUN: the plan holds, as far as it can be " +
                  "known without looking at the document.\n\n"
                : "DRY RUN: " + trouble.Count + " things to fix.\n\n");
            foreach (var g in trouble) text.Append("  - " + g + "\n");
            if (trouble.Count > 0) text.Append("\n");

            // WHAT WAS NOT LOOKED AT. This part is ALWAYS there, even
            // when the plan holds -- especially when it holds.
            text.Append("Checked: the operation names, the required " +
                         "arguments, the unknown ones (which would be " +
                         "silently ignored) and the references to " +
                         "earlier steps.\n\n");
            text.Append("NOT checked, and for these the document has to be read: " +
                         "whether the «uid» values are still valid, whether the " +
                         "document is of the right type for these operations, " +
                         "whether the faces and edges named still exist, " +
                         "and whether the geometry you ask for is possible. " +
                         (_session.Attached
                             ? "SOLIDWORKS is attached: «build» without «dry_run» " +
                               "checks all of them before touching anything."
                             : "SOLIDWORKS is NOT attached now.") + "\n");

            return Protocol.Content(text.ToString(), trouble.Count > 0);
        }
        private Dictionary<string, object> Build(Dictionary<string, object> arguments)
        {
            // THE DRY RUN TOUCHES NOTHING, and does not even want
            // SOLIDWORKS to be attached: it exists precisely to get things wrong first.
            var requested = Take(arguments, "dry_run");
            bool test = false;
            if (requested != null) { try { test = Convert.ToBoolean(requested); } catch { } }
            // A RULE FOR THE WINDOWS THAT IS NOT ONE OF THE TWO IS REFUSED, not
            // read as the default (brief R1): «Stop» taken for «reactivate»
            // would do the opposite of what was asked.
            var windowRule = Text(arguments, "on_window_change");
            if (windowRule != null && windowRule != "reactivate" && windowRule != "stop")
                return Protocol.Content(
                    "«on_window_change» is «" + windowRule + "»: it is «reactivate» or «stop». " +
                    "Nothing was run.", true);
            var answerShape = Text(arguments, "response");
            if (answerShape != null && answerShape != "summary" && answerShape != "full")
                return Protocol.Content(
                    "«response» is «" + answerShape + "»: it is «summary» or «full». " +
                    "Nothing was run.", true);
            if (test) return TestThePlan(arguments);
            return RunPlan(arguments, checkIfRepeated: true);
        }

        /// <summary>
        /// The body of `build`, with or without the guard on repetition.
        ///
        /// ── WHY `measure` DOES NOT WANT IT, AND IT IS A BUG FOUND WHILE WRITING IT ──
        ///
        /// `measure` goes through here: they are the same operations, with a gate
        /// in front. But **measuring the same thing twice is legitimate** — indeed,
        /// it is the normal case: you look, you think, you look again. And since a
        /// measure does not change the document, the state after is identical to the state
        /// before: the guard on repetition would have seen an identical plan
        /// on an identical document and **would have refused the second measure.**
        ///
        /// It would not just have been inconvenient: `measure` declares itself
        /// `idempotentHint: true` in the annotations, and a client reads that
        /// line to decide whether it can retry by itself. Refusing the second
        /// attempt would have made that declaration **a lie in the
        /// protocol** — the same category of defect `measure` exists in order
        /// not to have.
        ///
        /// A parameter with a default value on `Build` would have been enough, but
        /// here the house rule is kept: new method, different name.
        /// </summary>
        /// <summary>
        /// THE OPERATIONS THAT CANNOT BE UNDONE (entry W2.5).
        ///
        /// **Only one, and the criterion matters more than the list**: what ends up
        /// here is what removes something that cannot be put back with
        /// an operation of the catalogue. `suppress_feature` is not here,
        /// because `reactivate` exists; `delete_feature` is, because a
        /// deleted feature does not come back.
        ///
        /// A long list would be worse: whoever confirms ten times in a row
        /// stops reading what they confirm, and it is the same reason
        /// `measure` exists separate from `build`.
        /// </summary>
        private static readonly string[] CannotBeUndone = { "delete_feature", "delete_mate" };

        /// <summary>
        /// The arguments of one step its operation does not declare, one line each,
        /// with the near names. AN UNKNOWN ARGUMENT IS NOT A DETAIL: it is ignored in
        /// silence, and the part comes out different from how it was asked for,
        /// without any error. The near name is said (brief R8: «value» for
        /// «value_text»), not taken: one name per argument. «_signatures» is the
        /// bridge's own, added to the steps after this check.
        /// </summary>
        internal static List<string> UnknownArgumentsOfStep(string location, string itemName,
                                                            Dictionary<string, object> data,
                                                            Dictionary<string, object> property)
        {
            var lines = new List<string>();
            if (data == null || property == null) return lines;
            foreach (var theField in data.Keys)
            {
                if (property.ContainsKey(theField) || theField == "_signatures") continue;
                var near = NearArguments(theField, property.Keys);
                lines.Add(location + " («" + itemName + "»): «" + theField +
                          "» is not an argument of this operation, " +
                          "and would be IGNORED" +
                          (near.Count > 0 ? ". Did you mean «" + string.Join("», «", near) + "»?" : ""));
            }
            return lines;
        }

        /// <summary>
        /// The unknown arguments of a whole plan, for a real run. Until bridge
        /// 0.150.1 only the dry run refused them (found in the replay of round 8,
        /// 27/10/2026: «components» sent to 0.150.1, which did not know it, put the
        /// overall height on every component, the very thing it asked to avoid).
        /// Measured before turning it on: 265 plan steps of session 8cd843 (args to
        /// args8), none with an argument its schema does not declare.
        /// </summary>
        private List<string> UnknownArgumentsOfPlan(object[] steps)
        {
            var lines = new List<string>();
            for (int i = 0; i < steps.Length; i++)
            {
                var planStep = steps[i] as Dictionary<string, object>;
                if (planStep == null) continue;
                var itemName = Text(planStep, "operation");
                if (string.IsNullOrWhiteSpace(itemName) || !_catalog.Knows(itemName)) continue;
                var schema = _catalog.Schema(itemName);
                if (schema == null) continue;
                lines.AddRange(UnknownArgumentsOfStep("step " + (i + 1), itemName,
                                                      Take(planStep, "arguments") as Dictionary<string, object>,
                                                      Take(schema, "properties") as Dictionary<string, object>));
            }
            return lines;
        }

        private Dictionary<string, object> RunPlan(
            Dictionary<string, object> arguments, bool checkIfRepeated)
        {
            var steps = Take(arguments, "steps") as object[];
            if (steps == null || steps.Length == 0)
                return Protocol.Content(
                    "The plan is empty: «steps» must be a list of " +
                    "{operation, arguments}.", true);

            // AN UNKNOWN ARGUMENT IS REFUSED BY A REAL RUN TOO, before anything is
            // asked or touched: the same lines as the dry run.
            var unknownArguments = UnknownArgumentsOfPlan(steps);
            if (unknownArguments.Count > 0)
            {
                string said = string.Join("; ", unknownArguments);
                return Protocol.Content(
                    "Nothing was run: " + said + (said.EndsWith("?", StringComparison.Ordinal) ? " " : ". ") +
                    "Correct the plan and send it again; «dry_run» checks a plan without touching the document.", true);
            }

            // ── WE ASK BEFORE TOUCHING ANYTHING (entry W2.5) ──
            //
            // Even before looking at whether SOLIDWORKS is attached, and it is not
            // a detail of order: reading the state of the document is already
            // a conversation with SOLIDWORKS, and a conversation with
            // SOLIDWORKS may not come back (entry Z-C4). The question concerns
            // the intention, and the intention can be asked at zero cost.
            //
            // Does `measure` not come through here with `checkIfRepeated` false? It does,
            // but it cannot execute destructive operations: the gate is
            // before. Asking for confirmation there would be asking permission to
            // do something that will be refused anyway.
            // THE OPERATIONS NOBODY WAS ASKED ABOUT (report 8cd843 N1, 27/10/2026):
            // with a client that does not declare «elicitation» the plan runs, as
            // before, and the answer now says so; the log line is written only
            // when the plan really reaches SOLIDWORKS (N6: it was written before
            // a plan then refused as a resend).
            string notAsked = null;
            if (checkIfRepeated)
            {
                var toConfirm = new List<string>();
                foreach (var x in steps)
                {
                    var planStep = x as Dictionary<string, object>;
                    if (planStep == null) continue;
                    var itemName = Text(planStep, "operation");
                    if (string.IsNullOrEmpty(itemName)) continue;
                    foreach (var bad in CannotBeUndone)
                        if (string.Equals(itemName, bad, StringComparison.OrdinalIgnoreCase) &&
                            !toConfirm.Contains(itemName))
                            toConfirm.Add(itemName);
                }

                if (toConfirm.Count > 0)
                {
                    var question =
                        "This plan contains " +
                        (toConfirm.Count == 1 ? "an operation that CANNOT be undone"
                                                 : "operations that CANNOT be undone") +
                        ": " + string.Join(", ", toConfirm) +
                        ". What it removes cannot be put back with any " +
                        "operation of the catalog. Do you want me to proceed?";

                    if (Oce.Solid.Agent.Mcp.Elicitation.ClientCanAsk)
                    {
                        if (!Oce.Solid.Agent.Mcp.Elicitation.Confirm(Console.Out, Console.In, question))
                        {
                            // WHAT THE CLIENT ANSWERED, quoted (brief R22).
                            var answered = Oce.Solid.Agent.Mcp.Elicitation.LastAnswer;
                            Protocol.Note("[bridge] confirmation not given; the client answered " + answered);
                            return Protocol.Content(
                                "Stopped: I asked the client for a confirmation and it did not " +
                                "say yes (it answered " + answered + "), so I touched nothing. " +
                                "If nobody was asked anything, the client does not show this " +
                                "question: ask the person yourself, in words, before trying again. " +
                                question, true);
                        }
                    }
                    else
                    {
                        // THE FALLBACK IS DECLARED (entry W2.5). Going on
                        // in silence would be the way a defence
                        // disappears without anyone noticing: in the log when
                        // the plan runs, and in the answer (report 8cd843 N1).
                        notAsked = string.Join(", ", toConfirm);
                    }
                }
            }

            if (!_session.Attached)
                return Protocol.Content("The bridge is not attached to SolidWorks.", true);

            // NOT LOOKED AT AND NOT NAMED: refused BEFORE reading the document
            // (report 8cd843 N17, 27/10/2026). The refusal needs only the name
            // of the window in front, and the whole reading of an assembly of 8
            // components took 10.7 s to arrive at it. With no document open the
            // reading below says so, as before.
            if (_seen == null && string.IsNullOrWhiteSpace(Text(arguments, "document")))
            {
                var frontNow = _session.Identity(_session.ActiveDocument());
                if (frontNow != null)
                    return Protocol.Content(NotLookedAtYet(frontNow), true);
            }

            // WITHOUT ANCHOR: this reading serves to know on WHICH document one is
            // working, how much volume there was and how many features were already in
            // error. No signatures are built from it (those are made on «after»,
            // further down) and it does not go back to the reader. The anchor costs one
            // GetTessTriangles per face, and here we paid for it only to throw it away.
            var before = _session.StateWithoutAnchor(out var readError);
            if (before == null)
                return Protocol.Content(readError, true);

            // WE DRAW ON THE DOCUMENT THAT WAS LOOKED AT, NOT ON THE ONE IN FRONT.
            //
            // The bridge works on the ACTIVE document, and the active document is
            // the one whose window is in front: it changes if someone clicks,
            // if another operation opens one, if SolidWorks brings one to the
            // foreground by itself. On 14 September 2026 it really happened —
            // a 314x274x52 block extruded inside someone else's part,
            // without an error, without a warning.
            //
            // Here we do not guess and do not switch window by ourselves: we
            // COMPARE with what «inspect_part» had described, and if it
            // does not match we stop. Whoever knows what they are doing can say
            // explicitly «yes, the one in front» with `any_document`.
            var now = StateIdentity(before);
            var named = Text(arguments, "document");

            // WE NAME THE DOCUMENT, WE DO NOT SWITCH THE CHECK OFF.
            //
            // The first version had a `any_document: true` to
            // override the refusal. It was a defect: an argument that
            // disables a defence is an invitation to disable it, and whoever
            // meets it has every incentive to retry with it
            // switched on — that is, the defence disappears precisely in the case where
            // it was needed.
            //
            // Naming the document asks for the same thing in a form that
            // cannot be given at random: to write its name one has to have looked
            // at it. A boolean can be set by anyone; a wrong name
            // is refused like everything else.
            bool namedIt =
                !string.IsNullOrWhiteSpace(named) &&
                Bridge.Session.SameDocument(
                    now,
                    new Dictionary<string, object>
                    {
                        [named.IndexOfAny(new[] { '\\', '/' }) >= 0
                            ? "path" : "document_title"] = named,
                    });

            if (!namedIt)
            {
                if (_seen == null)
                    return Protocol.Content(NotLookedAtYet(now), true);

                if (!Bridge.Session.SameDocument(_seen, now))
                    return Protocol.Content(
                        "I am stopping: the document changed. You had looked at «" +
                        ReadableName(_seen) + "», now in front is «" +
                        ReadableName(now) + "». Drawing here would mean " +
                        "writing into the wrong part, and the identifiers of your " +
                        "plan are not valid on this one.\n\n" +
                        "If it changed for a reason you know, read it again with " +
                        "«inspect_part». If you already know you want to work on exactly " +
                        "what is there now, call again passing «document»: \"" +
                        ReadableName(now) + "\".", true);
            }

            // ── PUT IN FRONT BY «document», NOT READ (brief R18) ──────────
            //
            // On 24/10/2026 a reading was asked before every plan: 19 calls of
            // «document» and «inspect_part» for 16 plans, also before plans that
            // cite nothing of the part (a sketch on «top», a property). The
            // document is known — «document» put it in front — and a plan that
            // cites no identifier needs nothing else. One that cites an
            // identifier still needs the reading: identifiers are born there and
            // hold for it.
            string identifierCited = FirstIdentifierCited(steps);
            bool seenWithoutReading = _seen != null && ReferenceEquals(_seen, _seenByDocument) &&
                                      Bridge.Session.SameDocument(_seen, now);
            if (seenWithoutReading && identifierCited != null)
                return Protocol.Content(
                    "Your plan cites «" + identifierCited + "», and I have not read «" + ReadableName(now) +
                    "» since «document» brought it to the front. Identifiers are born from «inspect_part» and hold for " +
                    "that reading: call «inspect_part» first and copy them from there. A plan that cites no " +
                    "identifier runs without it.", true);
            // ── THE SAME DOCUMENT, WITH SOMETHING ELSE INSIDE (entry X2) ──
            //
            // The check above asks «is it the same document?». This one
            // asks «is it still the same part?», and they are two different questions:
            // title and path match even while someone pulls a
            // dimension by hand in the SOLIDWORKS window.
            //
            // It is looked at ONLY if the document in front is the one looked at. If the
            // plan names another one, the old signature speaks of a different
            // part and comparing it would produce a true sentence about two things
            // that have nothing to do with each other.
            bool theCheckedOne =
                _seen != null && Bridge.Session.SameDocument(_seen, now);

            // A PLAN THAT CITES NO IDENTIFIER (brief R18) is not stopped by a
            // change: what the refusal protects is the meaning of «F1», and the
            // plan has none. It runs on the part as it is now, and the answer
            // says the part had changed.
            string partChangedNote = null;
            if (theCheckedOne)
            {
                var changed = WhatChanged(_signatureSeen, LightSignature(before));
                if (changed != null && identifierCited == null)
                    partChangedNote = "The part had changed since it was last read — " + changed + ". The plan cites no " +
                                      "identifier, so it ran on the part as it is now.";
                else if (changed != null)
                    return Protocol.Content(
                        "I am stopping: the part has changed since you looked at it — " +
                        changed + ".\n\n" +
                        "The document is right, the content is not. The face identifiers " +
                        "(«F1», «F2», ...) are POSITIONAL: they are born from a " +
                        "reading and hold for that reading. On a changed geometry «F1» " +
                        "may point to another face, and the operation would succeed in the " +
                        "wrong place without giving any error.\n\n" +
                        "Read again with «inspect_part» and redo the plan on the " +
                        "new identifiers.", true);
            }

            // THE SIGNATURES HOLD ONLY ON THE DOCUMENT THAT PRODUCED THEM.
            // If the plan names another document, the uids of the plan do not
            // speak of this one and attaching the old signatures would produce
            // refusals that have nothing to do with it. Better no comparison than
            // a comparison between two different parts.
            var signatures = theCheckedOne ? _seenSignatures : null;

            // ── THREE FIELDS THAT MUST SPEAK OF THE SAME PART (Z-A1) ──
            //
            // `_seen` says WHICH document, `_signatureSeen` and
            // `_seenSignatures` say HOW IT WAS. As long as this line moved
            // only the first, a plan that named another
            // document — `document: "..."`, the legitimate path — was enough for
            // `_seen` from then on to point to the new part and the
            // signatures to the old part.
            //
            // The trouble is not seen immediately: it is seen at the NEXT call, where
            // `theCheckedOne` comes back true and the comparison runs against the
            // signatures of another part. Either it refuses a good plan saying «the
            // part has changed», or — worse — lets through a plan on
            // uids that no longer hold.
            //
            // The right principle is written in the comment above, for the
            // local variable: **better no comparison than a comparison
            // between two different parts.** Here it is applied also to the fields that
            // remain: if the document changes, the old signatures are thrown away.
            // `inspect_part` will redo them, or the end of this plan.
            if (!theCheckedOne)
            {
                _seenSignatures = null;
                _signatureSeen = null;
            }
            _seen = now;
            // Still not read (brief R18): a plan refused further down must not
            // turn the document into one that was read.
            if (seenWithoutReading) _seenByDocument = _seen;

            // ── THE SAME PLAN, TWICE (entry W2.7) ──────────────────────
            //
            // A client that does not receive the answer — network, timeout, restart —
            // resends the same request. It is the normal behaviour of
            // any client, and it is documented in the annotations of this
            // bridge: `build` is **not** idempotent, running the same
            // plan again does not put the part back as it was, **it makes a second hole**.
            //
            // Until now nothing stopped it. Now it does, and the criterion is narrow
            // on purpose: it refuses **only** if the plan is identical to the previous
            // one **and** the document is still exactly as that plan
            // left it. The two conditions together describe a single case:
            // the resend.
            //
            // The first alone is not enough. Two identical plans on documents in different
            // states do different things and may both be wanted.
            // And the second is not enough: a document that has not changed is not
            // in itself a reason to refuse.
            string planFingerprint = PlanFingerprint(steps);
            if (checkIfRepeated &&
                _lastPlanDone != null &&
                _lastPlanDone == planFingerprint &&
                Bridge.Session.SameDocument(_planDocument, now) &&
                _summaryAfterPlan != null &&
                _summaryAfterPlan == StateSummary(before))
            {
                return Protocol.Content(
                    "I have already run this plan, and the document is still " +
                    "exactly as I left it. I will not redo it: «build» " +
                    "is not repeatable — running it again does not put the document back as it was, " +
                    "it would do the same work a SECOND time (a second hole, a second mate).\n\n" +
                    "If the earlier answer never reached you, call " +
                    "«inspect_part»: the current state is the result of " +
                    "that plan.\n\n" +
                    "If you really do want to redo it, change something in the plan " +
                    "or use «undo» and then run it again: that way it is a choice, " +
                    "not a replay.", true);
            }

            // THE PLAN IS CHECKED IN FULL BEFORE TOUCHING THE DOCUMENT.
            //
            // The name of the operation, the paths and the shape of the arguments can be seen
            // by reading, without opening anything. Before, they were checked one at a
            // time: a form error at step 3 stopped the plan with the first
            // two ALREADY BUILT, and half a part was left in the document for
            // something that could have been said before starting.
            //
            // Now either the plan is entirely writable, or nothing is touched.
            var toDo = new List<Oce.Solid.Agent.Core.PlanStep>();
            var outcomes = new List<object>();
            bool everythingFine = true;
            int order = 0;

            // THE PROGRESS OF A REAL PLAN IS TOLD IN TWO HALVES
            // (entry W2.2): first it is checked in full with the document intact,
            // then it is executed. The total keeps them together — the second half
            // is sent by `PlanInMemory`, which resumes the count from where this
            // run left it instead of recalculating it.
            //
            // Saying «step 3 of 5» during the check and then starting again from
            // «1 of 5» at execution would make the reader's bar go back,
            // and the specification demands that `progress` grow.
            _wire.Total = steps.Length * 2;

            // N18: the kind of the document the plan starts on.
            string startKind = Convert.ToString(before.TryGetValue("kind", out var kindAtStart) ? kindAtStart : null) ?? "";
            bool anotherDocumentAhead = false;

            foreach (var p in steps)
            {
                var planStep = p as Dictionary<string, object>;
                if (planStep == null) continue;
                order++;

                var itemName = Text(planStep, "operation");
                _wire.Step(order, "checking step " + order + " of " +
                                    steps.Length + ": " + itemName);
                var itsItems = Take(planStep, "arguments") as Dictionary<string, object>
                           ?? new Dictionary<string, object>();

                // WHAT THE STEP MUST MEASURE (brief R16): its form is checked
                // here, before anything runs; the numbers after the step.
                object rawExpect;
                planStep.TryGetValue("expect", out rawExpect);
                var expectProblem = Oce.Solid.Agent.Core.StepExpectations.FormProblem(rawExpect);
                if (expectProblem != null)
                {
                    outcomes.Add(new Dictionary<string, object>
                    {
                        ["operation"] = itemName,
                        ["succeeded"] = false,
                        ["error"] = "Step " + order + ": " + expectProblem + NothingTouched,
                    });
                    everythingFine = false;
                    break;
                }

                // THE NAME IS CHECKED BEFORE EXECUTING. The add-in, faced with
                // a name it does not know but with some «contours» inside, falls back
                // on the profile extrusion: handy for the server's recipes,
                // dangerous here, where a crooked name would become a crooked
                // part without anyone saying anything.
                if (!_catalog.Knows(itemName))
                {
                    var similar = _catalog.Similar(itemName);
                    _registry.OperationThatDoesNotExist(itemName, similar);
                    outcomes.Add(new Dictionary<string, object>
                    {
                        ["operation"] = itemName,
                        ["succeeded"] = false,
                        ["error"] =
                            "The operation «" + itemName + "» does not exist." +
                            (similar.Count > 0
                                ? " Did you mean: " + string.Join(", ", similar) + "."
                                : "") + NothingTouched,
                    });
                    everythingFine = false;
                    break;
                }

                // THE STEP GOES ON UNDER THE CATALOGUE'S NAME (Z-C21.10,
                // pattern S20). `Knows` finds the operation ignoring case,
                // and until 0.140.0 the step went on under the name as it was
                // written. The library dispatches Ordinal: «Extrude_Rectangle»
                // came back «Unknown operation» right after this check had
                // said it exists, and «Revolve», which carries «contours»,
                // fell into the profile extrusion the comment above warns
                // about, with no «thickness_mm». Same rule as the enum values
                // in Bridge/Shape.cs: what passes here goes on spelled as the
                // catalogue spells it.
                itemName = Text(_catalog.Find(itemName), "name") ?? itemName;

                // THE PATHS ARE CHECKED BEFORE EXECUTING.
                // They are the only thing this bridge writes outside the
                // open document, and they come from generated text.
                var crookedPath = Bridge.Paths.Check(itsItems);
                if (crookedPath != null)
                {
                    outcomes.Add(new Dictionary<string, object>
                    {
                        ["operation"] = itemName,
                        ["succeeded"] = false,
                        ["error"] = crookedPath + NothingTouched,
                    });
                    everythingFine = false;
                    break;
                }

                // DO THE ARGUMENTS RESPECT THE SCHEMA WE DECLARED
                // OURSELVES? The bridge offers thirty-six operations with
                // detailed schemas: not looking at them means that the
                // schema only serves to suggest, and that a forgotten «thickness_mm»
                // arrives downstream as zero — an extrusion of
                // null thickness, refused by SOLIDWORKS with a message of
                // its own, in the best case.
                //
                // ONLY THE FORM, NEVER THE MERIT: see Bridge/Shape.cs.
                //
                // AND THE SHAPE REWRITES `its` IN PLACE (Z-C21.10, S20): an
                // enum value accepted with a different case («Top»)
                // comes out spelled as the catalogue spells it («top»).
                // `its` is the very dictionary the step below hands to the
                // library, which compares Ordinal.
                var crookedShape = Bridge.Shape.Check(
                    itemName, _catalog.Schema(itemName), itsItems);
                if (crookedShape != null)
                {
                    outcomes.Add(new Dictionary<string, object>
                    {
                        ["operation"] = itemName,
                        ["succeeded"] = false,
                        ["error"] = crookedShape + NothingTouched,
                    });
                    everythingFine = false;
                    break;
                }

                // WHAT AN ASSEMBLY REFUSES, BEFORE THE FIRST STEP (report 8cd843
                // N18, 27/10/2026): [save_document, export_step with
                // reopen_and_compare] saved the copy, and only then step 2 was
                // refused. The kind is the one of the document the plan starts
                // on, so it holds until a step that brings another document
                // (create_drawing).
                if (startKind == "assembly" && !anotherDocumentAhead)
                {
                    object reopen;
                    var crookedKind = Oce.Solid.Agent.Core.SwOperations.AssemblyExchangeRefusal(
                        itemName, itsItems.TryGetValue("reopen_and_compare", out reopen) && reopen is bool r && r);
                    if (crookedKind != null)
                    {
                        outcomes.Add(new Dictionary<string, object>
                        {
                            ["operation"] = itemName,
                            ["succeeded"] = false,
                            ["error"] = "Step " + order + ": " + crookedKind + NothingTouched,
                        });
                        everythingFine = false;
                        break;
                    }
                }
                if (itemName == "create_drawing") anotherDocumentAhead = true;

                // THE MATERIAL IS FOUND BEFORE THE FIRST STEP, not at its own
                // (brief R4): see MaterialProblem.
                var crookedMaterial = MaterialProblemOf(itemName, itsItems);
                if (crookedMaterial != null)
                {
                    outcomes.Add(new Dictionary<string, object>
                    {
                        ["operation"] = itemName,
                        ["succeeded"] = false,
                        ["error"] = crookedMaterial + NothingTouched,
                    });
                    everythingFine = false;
                    break;
                }

                // DO THE IDENTIFIERS CITED REALLY EXIST?
                //
                // It is the other half of the anti-drift defence, and it catches a case
                // that the SIGNATURES cannot catch: a plan that creates the part
                // from scratch and then cites «F1» has no signature to compare,
                // because when the state was read that face did not
                // exist. No signature, no comparison, and the plan passes.
                //
                // Measured on 14/9/2026: `[extrude_rectangle, cylindrical_cut
                // su F1]` on an empty document succeeded, volume 117,738.1. The
                // hole ended up on the largest face — which in that case was
                // also the right one, but **nobody had chosen it**: «F1»
                // is positional and is renumbered by area at every reading.
                var crookedUid = UidsThatDoNotExist(
                    _catalog.Schema(itemName), itsItems, before);
                if (crookedUid != null)
                {
                    outcomes.Add(new Dictionary<string, object>
                    {
                        ["operation"] = itemName,
                        ["succeeded"] = false,
                        ["error"] = crookedUid + NothingTouched,
                    });
                    everythingFine = false;
                    break;
                }

                // THE SIGNATURES FROM WHEN THE PLAN WAS BORN, ATTACHED TO THE STEP.
                // What the caller has already put there is not overwritten:
                // a plan that carries its own signatures knows better than we do what
                // it is talking about.
                if (signatures != null && !itsItems.ContainsKey("_signatures"))
                    itsItems["_signatures"] = signatures;

                toDo.Add(new Oce.Solid.Agent.Core.PlanStep
                {
                    Order = order,
                    Operation = itemName,
                    Arguments = itsItems,
                    Summary = itemName,
                    Expect = rawExpect as Dictionary<string, object>,
                });
            }

            // THE RUN IS THE LIBRARY'S, NOT OUR OWN.
            // From here come the references between steps («the face on top of
            // what you did at step 1») and the single undo entry:
            // one Ctrl+Z undoes the whole plan.
            // What the executor said about its document and its undo entry
            // (Z-C21.14). Stays null when the plan never reached it: refused
            // above, before anything was written.
            // A PLAN REFUSED BEFORE RUNNING IS ANSWERED WITHOUT A READING
            // (brief R3). Nothing was touched, so the document is the one read in
            // `before`: on 24/10/2026 such a refusal carried 10,804 characters
            // of state and cost a second full reading (46 s on a part with a body).
            if (!everythingFine)
            {
                var refused = new Dictionary<string, object>
                {
                    ["document"] = ReadableName(now),
                    ["outcomes"] = outcomes,
                    ["volume_before_mm3"] = Volume(before),
                };
                return Protocol.ContentWithData(Protocol.InJson(refused), refused, true);
            }

            Oce.Solid.Agent.Core.PlanExecutor.Result planRun = null;
            bool executorThrew = false;
            object turnRefusedOn = null;
            if (everythingFine)
            {
                // THE EXECUTOR RUNS ON THE DOCUMENT CHECKED ABOVE (review F7).
                // Up to bridge 0.125.0 it took the active document by itself,
                // so a window brought to the front between the reading of
                // `before` and this call got the plan, and the memory mixed the
                // numbers of two documents. It now stops before writing when
                // the active document is not `now`.
                // ANOTHER WINDOW IN FRONT IS BROUGHT BACK unless the caller
                // said «stop» (brief R1).
                _session.ReactivateWindowInPlans =
                    !string.Equals(Text(arguments, "on_window_change"), "stop", StringComparison.Ordinal);
                if (notAsked != null)
                    Protocol.Note(
                        "[bridge] " + notAsked +
                        ": I could not ask the user for confirmation — " +
                        "this client does not declare «elicitation». I am going " +
                        "ahead as before.");
                var done = _session.ExecutePlan(toDo, out var runError, _wire,
                                                  IdentityForTheExecutor(now));
                planRun = _session.LastPlanResult;
                executorThrew = _session.LastPlanExecutorThrew;
                turnRefusedOn = _session.LastTurnRefusedDocument;
                if (done != null) foreach (var e in done) outcomes.Add(e);
                if (!string.IsNullOrEmpty(runError))
                {
                    everythingFine = false;
                    // A plan stopped BEFORE the first step leaves no
                    // outcome: without this, the answer would be an empty list
                    // and an implicit «succeeded» that nobody said.
                    if (done == null || done.Count == 0)
                        outcomes.Add(new Dictionary<string, object>
                        {
                            ["operation"] = toDo.Count > 0 ? toDo[0].Operation : null,
                            ["succeeded"] = false,
                            ["error"] = runError,
                        });
                }
            }

            var after = _session.State(out var stateError);

            // ── TWO READINGS WHEN THE PLAN ENDS ON ANOTHER WINDOW (Z-C21.14) ──
            //
            // `after` above is the document IN FRONT. Up to bridge 0.124.1 it
            // was also the only reading after the plan, and when the window
            // changed during the plan (a `create_drawing`, another session, the
            // person) the undo memory got the numbers of the other
            // document: the live probe M19 (1/10/2026, line BRIDGE-R1 (X2))
            // saw the next undo refuse a right undo («the part changed
            // after the plan»). The plan's own numbers are now read on the
            // plan's document, by the reference the executor kept
            // (`PlanDocument`, the expectedDocument it read once at the start).
            //
            // «state_after» KEEPS DESCRIBING THE DOCUMENT IN FRONT, on purpose:
            // the next plan works on the document in front, and after
            // [..., create_drawing] the next plan is `standard_views` on the
            // drawing, the most normal sequence there is. What describes the
            // plan's document is `volume_after_mm3` (paired with
            // `volume_before_mm3`, read on the same document before), the error
            // counts and the undo memory; and when the two documents
            // differ the answer says which one is in front.
            var frontIdentity = StateIdentity(after);
            var planStateAfter = after;
            var planIdentity = now;
            string planStateError = null;
            bool anotherInFront = false;
            // A plan the executor stopped before writing (its checks, or the
            // active document not the one checked here, review F7) worked on
            // nothing: its PlanDocument is only the window that was in front,
            // possibly a foreign one, and must not become the plan's document.
            bool wroteOnADocument = planRun != null && !planRun.StoppedBeforeWriting;
            object planDocumentHeld = wroteOnADocument ? planRun.PlanDocument : null;
            if (planDocumentHeld != null)
            {
                planIdentity = _session.Identity(planDocumentHeld) ?? now;
                if (!_session.IsInFront(planDocumentHeld))
                {
                    anotherInFront = true;
                    planStateAfter = _session.StateOfDocument(planDocumentHeld, out planStateError);
                }
            }
            else if (frontIdentity != null && !Bridge.Session.SameDocument(now, frontIdentity))
            {
                anotherInFront = true;
                // The executor held no document. When it was never reached, or
                // stopped before writing, nothing was written and the document
                // of this call is as it was read before. When it threw (review
                // F9), what it wrote is not known and the document is not held:
                // no reading after.
                planStateAfter = planRun == null || planRun.StoppedBeforeWriting ? before : null;
            }

            // ── IS THE WINDOW IN FRONT THE PLAN'S? (review F5) ──────────────
            //
            // It is when the plan ended on it: the plan document, or the new
            // document a step declared (the drawing of `create_drawing`). When a
            // window came to the front from outside during the plan (scenario
            // X2 of the probe M19: another session, the person), it is not, and
            // up to bridge 0.125.0 its identity and signatures were adopted
            // anyway: a retry of the refused step, written for the plan's part,
            // passed the «document changed» check and wrote into the other
            // document, the class of the 14 September 2026. Now nothing is
            // adopted from a window the plan did not end on: the identity and
            // the signatures are cleared, and the next plan asks for
            // «inspect_part» first.
            bool frontIsThePlans;
            object finalDocument = wroteOnADocument ? planRun.FinalDocument : null;
            if (finalDocument != null)
                frontIsThePlans = _session.IsInFront(finalDocument) ||
                                  (planDocumentHeld != null && !anotherInFront);
            else
                frontIsThePlans = frontIdentity != null &&
                                  Bridge.Session.SameDocument(planIdentity, frontIdentity);

            // «state_after» IS THE NEXT READING, AND MUST BE SIGNED AS SUCH.
            //
            // This answer carries with it the state of the document after the
            // steps, with the new identifiers: the reader will write the
            // next plan on THESE, not on those of «inspect_part».
            // If the signatures stayed those from before, the next plan —
            // written correctly — would be refused because it was compared with a
            // reading its author never saw. It would be a refusal
            // of what is correct, which is the worse defect of the two.
            // AND THE SIGNATURE FOLLOWS THE SAME RULE (entry X2). The part has
            // changed, but the bridge changed it doing what it was
            // asked to do: if the signature stayed that of
            // «inspect_part», the next `build` would stop
            // saying «someone touched the part by hand». It would be the
            // false positive that entry X2 listed among the risks, and it would be
            // annoying in just the right measure to teach people to ignore the warning.
            bool fullAnswer = string.Equals(Text(arguments, "response"), "full", StringComparison.Ordinal);
            if (after != null && frontIsThePlans)
            {
                // THE SIGNATURES ARE THOSE OF THE READING THE CALLER SAW (brief
                // R3). With «full» the caller sees `after` and writes the next plan
                // on its identifiers. With «summary» it did not see them: the
                // identifiers it may cite are those of its last «inspect_part»,
                // so those signatures stay, and a face that moved is re-anchored
                // or refused instead of being taken by its new number.
                if (fullAnswer) _seenSignatures = StateSignatures(after);
                _signatureSeen = LightSignature(after);

                // ── AND ALSO WHO IT IS, NOT JUST HOW IT IS MADE ─────────────
                //
                // `_seen` used to be brought back to the identity read BEFORE
                // executing, and a plan that contains `save_document` changes the
                // document's identity while it runs: before the save
                // it is «Part1597» without a path, after it is a path on disk.
                //
                // **The next `build` was refused**, with a correct
                // message and a wrong cause: «the document has changed,
                // you had looked at Part1597». It had changed, yes — the bridge
                // had changed it doing what it had been asked to do.
                //
                // Found on 23/9/2026 with a probe that prepared a drawing:
                // extrude -> cut -> save -> create_drawing, which is the most
                // ordinary sequence there is, and the fourth step did not
                // start. The bench could not see it: it calls the library,
                // not the bridge. It was seen by the group that goes through the client's
                // door (entry F14), the day after it was born.
                //
                // `undo` already did this three methods further down. Here it did not.
                _seen = StateIdentity(after);
            }
            else
            {
                // A window the plan did not end on, or no reading (review F5).
                _seen = null;
                _seenSignatures = null;
                _signatureSeen = null;
            }
            if (after != null) RemoveAnchors(after);

            // WHERE THE PLAN'S TIME WENT, in one line (brief R2), and the
            // outcomes without what only a reader of the whole answer needs
            // (brief R3). Taken before the compaction, which drops the times of
            // each step.
            var planTiming = PlanTiming(outcomes);
            int echoesLeftOut = 0;
            int explanationsLeftOut = fullAnswer ? 0 : CompactOutcomes(outcomes, steps, out echoesLeftOut);

            var result = new Dictionary<string, object>
            {
                // WE ALWAYS SAY WHAT WAS WORKED ON. Even when it is obvious:
                // it is the only way for the reader to notice when obvious
                // was not.
                ["document"] = ReadableName(now),
                ["outcomes"] = outcomes,
                ["note_state"] = after == null
                    ? stateError
                    : fullAnswer
                        ? null
                        : "The identifiers of faces and edges are not in this answer: read them with " +
                          "«inspect_part» before a plan that cites them, or send «response»: «full».",
                ["volume_before_mm3"] = Volume(before),
                // On the plan's document, like the line above (Z-C21.14).
                ["volume_after_mm3"] = Volume(planStateAfter),
            };

            // THE WHOLE STATE ONLY WHEN ASKED FOR (brief R3): on 24/10/2026 it was
            // 95% of the characters of every answer of `build`.
            if (fullAnswer) result["state_after"] = after;
            else if (after != null)
            {
                var summary = SummaryOfState(before, after);
                // The path is already in «document»: the same string twice.
                object path;
                if (summary.TryGetValue("path", out path) &&
                    string.Equals(Convert.ToString(path, CultureInfo.InvariantCulture),
                                  Convert.ToString(result["document"], CultureInfo.InvariantCulture),
                                  StringComparison.OrdinalIgnoreCase))
                    summary.Remove("path");
                result["state_summary"] = summary;
            }
            if (planTiming != null) result["timing_ms"] = planTiming;
            var outcomesNote = NoteOutcomes(explanationsLeftOut, echoesLeftOut, WarningsInOutcomes(outcomes));
            if (outcomesNote != null) result["note_outcomes"] = outcomesNote;

            // WHICH DOCUMENT IS IN FRONT, when it is not the plan's (Z-C21.14),
            // and whether the next plan may work on it (review F5).
            if (anotherInFront)
            {
                result["document_in_front"] = ReadableName(frontIdentity);
                result["note_document_in_front"] =
                    NoteDocumentInFront(ReadableName(planIdentity), ReadableName(frontIdentity), frontIsThePlans) +
                    (planStateAfter == null && planStateError != null ? " " + planStateError : "");
            }

            // WHETHER THE PLAN LEFT AN UNDO ENTRY (Z-C21.14): true, one entry
            // with the plan in it sits on top of that document's undo stack;
            // false, SOLIDWORKS answered false (measured for an empty
            // recording; whether that leaves an entry on the stack is
            // undecided, probe M19 phase H4); null, not known.
            if (planRun != null)
                result["undo_entry_recorded"] = planRun.UndoEntryOnPlanDocument;

            // NOBODY WAS ASKED (report 8cd843 N1): said to the agent, who can
            // still tell the person what went.
            if (notAsked != null && planRun != null)
                result["confirmation"] =
                    "Nobody was asked to confirm " + notAsked + ": this client does not declare «elicitation» " +
                    "(MCP), so I could not put the question to the person, and the plan ran as before. What it " +
                    "deletes cannot be put back with any operation of the catalog: tell the person what this plan " +
                    "deleted.";

            // The part had changed and the plan cited no identifier (brief R18).
            if (partChangedNote != null)
                result["warning_part_changed"] = partChangedNote;

            // WHEN ANOTHER WINDOW CAME TO THE FRONT and the plan's document was
            // brought back (brief R1): said, never hidden.
            if (planRun != null && planRun.Reactivations.Count > 0)
                result["reactivations"] = planRun.Reactivations;

            // THE COUNT OF ERRORS IS GIVEN AS A DELTA, NOT AS A NUMBER.
            //
            // «features_in_error: 1» after a plan seems to say «the plan
            // broke something». It may mean the opposite: the part was already
            // broken when it arrived, and the plan has nothing to do with it. The
            // absolute number lies about who did it, and it is the risk written in
            // entry D5.
            //
            // We say how many there were before only when something changes: on
            // a healthy part that stays healthy there is nothing to say, and repeating it
            // in every answer is space taken from what matters.
            int errorsBefore = FeaturesInError(before);
            // The same document as `before` (Z-C21.14), not the window in front.
            int errorsAfter = FeaturesInError(planStateAfter);
            if (errorsBefore > 0 || errorsAfter > 0)
            {
                result["features_in_error_before"] = errorsBefore;
                result["note_errors"] =
                    errorsAfter > errorsBefore
                        ? "The part has " + errorsAfter + " features in error, and before it " +
                          "had " + errorsBefore + ": this plan broke " +
                          (errorsAfter - errorsBefore) + (fullAnswer
                              ? ". Look at «state_after» to see which."
                              : ". Read «inspect_part» to see which.")
                        : errorsAfter < errorsBefore
                            ? "The features in error went from " + errorsBefore +
                              " to " + errorsAfter + ": this plan repaired " +
                              (errorsBefore - errorsAfter) + "."
                            : "The part already had " + errorsBefore + " features in error " +
                              "before this plan, and still has " + errorsAfter + ": " +
                              "this plan did not break them.";
            }

            // ── THE MEMORY FOR «undo» (entry R1) ──────────────────────
            //
            // We keep how the part was when this plan finished: it is the
            // only thing that will allow withdrawing it without risking
            // taking away somebody else's work.
            //
            // IT IS WRITTEN ALSO WHEN THE PLAN FAILED HALFWAY, and it is the case
            // that matters most: a plan stopped at step 3 leaves the first two
            // built, and those are exactly the ones the reader wants to withdraw.
            // The error message already tells them so («what had already
            // been built stays»).
            //
            // WHAT IS KEPT (Z-C21.14, review F1): the plan's document by its
            // COM reference (review F11: a save changes the name, not the
            // object), and a snapshot of it before and after the plan: volume,
            // feature count, and the feature names with their IDs in tree order.
            // The undo presses only if the part still reads as the snapshot
            // after, and says «withdrawn» only if it then reads as the snapshot
            // before.
            //
            // WHICH PLAN IS KEPT, AND WHEN (`MemoryAfterPlan`, pure, with the
            // rules written there). The short form:
            //  * a plan that built, or a plan whose own undo entry closed
            //    with something in it (FinishRecordingUndoObject answered true:
            //    a reorder, a rename, a property) becomes the memory: it is the
            //    last plan, and its entry is on top. Up to the first version of
            //    this lot such a plan only covered the plan before it, and
            //    the undo refused to withdraw the very plan the description
            //    promises to withdraw (review F1);
            //  * a plan that wrote nothing (it never reached the executor,
            //    stopped before opening its recording, or closed an empty one
            //    on the same document with nothing visibly changed) leaves the
            //    memory as it is (review F3: a plan, a measure and an undo, or
            //    a plan, a drawing with its views, the part brought back and
            //    an undo, were false refusals). The probe measured that an empty
            //    recording answers false; whether it leaves an entry on the
            //    stack is undecided, and the bench case
            //    `undo case c` decides it. If that entry were on top, a press
            //    would take away nothing visible, `undo` would see the part
            //    unchanged and say so, without a redo;
            //  * a measure that recorded a non-empty entry covers the memory
            //    (it is not a plan to withdraw, and its entry sits above);
            //    so does another bridge process refused the turn on the
            //    memory's document (review F4: another writer is on it);
            //  * anything else (an executor that threw, an answer not known, an
            //    entry on another document) drops the memory, and the reason is
            //    kept for the refusal, with what to do (review F12).
            //
            // Usually, in this project, **refusing what is correct is
            // the worse of the two ways of being wrong**. Not here, and it is worth
            // saying why: one refusal too many costs the reader a
            // re-reading and a new plan; one undo too many erases a person's
            // work, and no message puts it back. When the two
            // errors do not cost the same, the criterion is reversed.
            // On the plan's document (Z-C21.14): `before` was read on it before
            // the plan, `planStateAfter` after, whatever window is in front now.
            double? volBefore = NumberOf(before, "volume_mm3");
            double? volAfter = NumberOf(planStateAfter, "volume_mm3");
            int featBefore = HowManyFeatures(before);
            int featAfter = HowManyFeatures(planStateAfter);
            var snapshotBefore = PartSnapshot.Of(before);
            var snapshotAfter = PartSnapshot.Of(planStateAfter);

            // A volume that was not read does not count as «changed» (entry
            // Z-C8): here we err on the side of NOT remembering, for the reason
            // written above — one undo too many erases a person's
            // work. On an assembly volume and features read 0 on both sides
            // (brief R23): there it built when a component or a mate was seen
            // to change, never when one could not be read.
            bool hasBuilt =
                planStateAfter != null &&
                (featAfter != featBefore ||
                 (volBefore.HasValue && volAfter.HasValue &&
                  Math.Abs(volAfter.Value - volBefore.Value) > VolumeDeviationMm3) ||
                 AssemblyBuilt(snapshotBefore, snapshotAfter));
            bool visibleChange = SnapshotDiffers(snapshotBefore, snapshotAfter).Count > 0;

            var memoryBefore = _lastPlan;
            bool sameDocument = memoryBefore != null &&
                                SameDocumentAs(memoryBefore, planDocumentHeld, planIdentity);
            bool turnRefusedOnMemoryDocument = memoryBefore != null && turnRefusedOn != null &&
                                               SameDocumentAs(memoryBefore, turnRefusedOn,
                                                              _session.Identity(turnRefusedOn));
            string memoryReason;
            string memoryAction = MemoryAfterPlan(
                hasBuilt, visibleChange, planStateAfter != null,
                memoryBefore != null, memoryBefore != null && memoryBefore.CoveredBecause != null,
                planRun != null, planRun != null && planRun.StoppedBeforeWriting, executorThrew,
                turnRefusedOnMemoryDocument, planRun?.UndoEntryOnPlanDocument, sameDocument,
                !checkIfRepeated, EquationsChanged(snapshotBefore, snapshotAfter), ReadableName(planIdentity),
                out memoryReason);

            // A PLAN ON A DRAWING (report 8cd843 N24, round 7). Volume, features and
            // components do not exist on a drawing, and its plans record no undo
            // entry («undo_entry_recorded» false): «hasBuilt» stayed false after
            // three dimensions, the memory was kept as it was (none), and the undo
            // that followed said «I have not yet run any that built something» —
            // after four plans that had built — and offered delete_feature, which
            // does not take away a dimension. Now such a plan leaves no memory and
            // its reason, and the undo offers the remedy of a drawing.
            string onDrawing = checkIfRepeated
                ? DrawingPlanMemoryReason(
                      Convert.ToString(planStateAfter != null && planStateAfter.ContainsKey("kind") ? planStateAfter["kind"] : null,
                                       System.Globalization.CultureInfo.InvariantCulture),
                      SucceededOperations(outcomes), ReadableName(planIdentity))
                : null;
            // NOT EVEN WHEN SOLIDWORKS KEPT AN ENTRY (report 8cd843 N40, round 11): a
            // section view, then a detail view refused. The entry made the plan «new»,
            // the comparison saw nothing (a drawing has none of it), and the answer
            // said «refused and changed nothing I compare» beside «the 1 step(s)
            // before it are done and stay»; the undo said «as it was», and the view
            // stayed.
            if (onDrawing != null && !hasBuilt &&
                ((memoryBefore == null && memoryAction == MemoryKeep) || memoryAction == MemoryNew))
            {
                memoryAction = MemoryDrop;
                memoryReason = onDrawing;
            }
            if (memoryAction == MemoryNew) _noMemoryOnDrawing = false;
            else if (memoryAction == MemoryDrop) _noMemoryOnDrawing = onDrawing != null && memoryReason == onDrawing;

            switch (memoryAction)
            {
                case MemoryNew:
                    // A REFUSED PLAN THAT LEFT ONLY AN ENTRY (report 8cd843 N5).
                    bool refusedWithEntry = planRun != null && !string.IsNullOrEmpty(planRun.Error) &&
                                            !hasBuilt && !visibleChange;
                    var previous = refusedWithEntry && memoryBefore != null && sameDocument &&
                                   memoryBefore.CoveredBecause == null ? memoryBefore : null;
                    _lastPlan = new LastPlan
                    {
                        // The plan document as it is called NOW (Z-C21.14): a
                        // plan that saves its part changes its title and path.
                        Document = planIdentity,
                        DocumentRef = planDocumentHeld,
                        Before = snapshotBefore,
                        After = snapshotAfter,
                        Refused = refusedWithEntry,
                        Previous = previous,
                    };
                    _noMemoryBecause = null;
                    if (refusedWithEntry)
                        result["note_undo_entry"] =
                            "This plan was refused and changed nothing I compare, but SOLIDWORKS kept its undo entry " +
                            "on top of the undo stack: the next «undo» takes away that entry (nothing visible " +
                            "changes)" + (previous != null
                                ? ", and the one after it withdraws the plan before."
                                : "; I remember no plan before it on this document.");
                    break;
                case MemoryCover:
                    memoryBefore.CoveredBecause = memoryReason;
                    memoryBefore.CoveredByRecordedEntry = true;
                    break;
                case MemoryDrop:
                    _lastPlan = null;
                    _noMemoryBecause = memoryReason;
                    break;
                default:
                    // Kept: the name follows a save of the same document.
                    if (memoryBefore != null && sameDocument) memoryBefore.Document = planIdentity;
                    break;
            }

            // «undo» WILL REFUSE THIS PLAN, said by the plan and not first by
            // the undo (report 8cd843 N11, 27/10/2026): an extrusion with
            // «parameters» answered «undo_entry_recorded» true, and the undo
            // after it refused, rightly, for the equations.
            bool somethingToWithdraw = checkIfRepeated && (hasBuilt || visibleChange);
            bool drawingReason = onDrawing != null && memoryReason == onDrawing;
            if (memoryAction == MemoryDrop && (somethingToWithdraw || drawingReason))
            {
                result["undo_possible"] = false;
                result["note_undo"] =
                    "«undo» will refuse to withdraw this plan" +
                    (planRun != null && planRun.UndoEntryOnPlanDocument == true
                        ? ", although SOLIDWORKS recorded its undo entry («undo_entry_recorded» true)"
                        : "") +
                    ". " + memoryReason + (drawingReason ? " " + DrawingNoMemoryRemedy : "");
            }

            // A PLAN STOPPED HALFWAY SAYS WHAT STAYS (report 8cd843 N12): the
            // stop said «I touched nothing» of its own step, and the two cuts
            // before it were in the part.
            if (planRun != null && !string.IsNullOrEmpty(planRun.Error) && planRun.ExecutedSteps > 0 && steps != null)
            {
                string planName = ReadableName(planIdentity);
                result["steps_done"] = planRun.ExecutedSteps;
                result["steps_in_plan"] = steps.Length;
                result["note_stopped"] =
                    "The plan stopped at step " + (planRun.ExecutedSteps + 1) + " of " + steps.Length + ": the " +
                    planRun.ExecutedSteps + " step(s) before it are done and stay on «" + planName + "», and the " +
                    "«error» of the step that stopped says whether that step made anything. " +
                    (memoryAction == MemoryNew
                        ? anotherInFront && !frontIsThePlans
                            ? "«undo» withdraws the whole plan once «" + planName + "» is in front again («document» " +
                              "with «action»: «activate»)."
                            : "«undo» withdraws the whole plan."
                        : memoryAction == MemoryDrop
                            ? "«undo» cannot withdraw them: " + (result.ContainsKey("note_undo") ? "see «note_undo»." : memoryReason)
                            : "They changed nothing I compare, so «undo» has nothing of theirs to withdraw.");
            }

            // WE REMEMBER WHAT WAS DONE, and only if something was really done
            // (entry W2.7). A plan refused as a whole touched
            // nothing: remembering it would make the second attempt of whoever
            // has just corrected the error be refused — that is, the defence would fire against
            // the case it must let through.
            // And nothing is remembered of a MEASURE: it did not change the part,
            // and remembering it would make a later `build` be refused that by
            // chance had the same fingerprint.
            // The plan's document and its own summary (review F6): up to bridge
            // 0.125.0 the document was the plan's and the summary the window
            // in front's, so after [..., create_drawing] a resent plan on the part
            // was not recognised and built a second time.
            if (everythingFine && checkIfRepeated)
            {
                _lastPlanDone = planFingerprint;
                _planDocument = planIdentity;
                _summaryAfterPlan = StateSummary(planStateAfter);
            }

            // The data come out also when the plan went badly (W2.4): it is
            // precisely then that a program most needs to read them without
            // re-parsing a string — which step, which measure, which
            // refusal.
            return Protocol.ContentWithData(
                Protocol.InJson(result), result, !everythingFine);
        }

        /// <summary>
        /// The refusal of a plan when no document was looked at yet, with the
        /// name of the one in front (report 8cd843 N17: said before the reading).
        /// </summary>
        private static string NotLookedAtYet(Dictionary<string, object> front)
        {
            // «any document», not «any part» (report 8cd843 round 12, observation 2):
            // it came after the assembly looked at was closed, with a drawing in front.
            return "I have not looked at any document yet, or the one I looked at was closed, so I do not know which " +
                   "document you want me to draw on: in front now is «" +
                   ReadableName(front) + "». Call «inspect_part» first (or name it in «document») — " +
                   "it is needed anyway, because the face identifiers " +
                   "are born there.";
        }

        // ─────────────────────────────────────────────────────────────────
        // R1 — withdrawing what was done
        // ─────────────────────────────────────────────────────────────────
        /// <summary>
        /// How the part was when the last plan finished.
        ///
        /// It is the only thing that allows undoing without risking
        /// taking away somebody else's work: if the part is no longer like this,
        /// between the plan and now something happened, and that something was not
        /// done by this bridge.
        /// </summary>
        private sealed class LastPlan
        {
            /// <summary>The plan document's identity, for the messages (and the
            /// comparison when no reference is held).</summary>
            public Dictionary<string, object> Document;

            /// <summary>
            /// Z-C21.14, review F11: the plan document by its COM reference
            /// (`PlanExecutor.Result.PlanDocument`). A save changes the
            /// name of the same document; a document closed and opened again
            /// has the same name and an undo stack without the plan. Null when
            /// the executor held none.
            /// </summary>
            public object DocumentRef;

            /// <summary>Z-C21.14, review F1: the plan's document before the plan.</summary>
            public PartSnapshot Before;

            /// <summary>Z-C21.14, review F1: the plan's document after the plan.</summary>
            public PartSnapshot After;

            /// <summary>
            /// REPORT 8cd843 N5 (27/10/2026): this plan was refused, changed
            /// nothing that is compared, and still closed an undo entry with
            /// something in it (a place_component that moved the component and
            /// set it back). Its entry is on top, so it is the memory; the
            /// plan it covers is kept in <see cref="Previous"/>, and the undo
            /// that takes the empty entry away gives that plan back.
            /// </summary>
            public bool Refused;

            /// <summary>The memory before a refused plan (see <see cref="Refused"/>), or null.</summary>
            public LastPlan Previous;

            /// <summary>
            /// Z-C21.14: why this plan is KNOWN not to be on top of the undo
            /// stack any more, in English, for the refusal; null while nothing
            /// says so. A covered memory is never pressed.
            /// </summary>
            public string CoveredBecause;

            /// <summary>
            /// Z-C21.14, review F10: what covers it. True: an undo entry
            /// recorded through the API (a measure that recorded one, another
            /// bridge's plan), which SOLIDWORKS cannot redo. False: an action
            /// that was not recorded, put back by a redo, which a redo can put
            /// back again.
            /// </summary>
            public bool CoveredByRecordedEntry;
        }

        /// <summary>
        /// WHAT THE UNDO COMPARES OF A READING (Z-C21.14): the volume (null
        /// when the state could not read it, Z-C8), the feature count with the
        /// features beyond the list, the listed features as «name#id» in tree
        /// order, and how many features are beyond the list (review F16: those
        /// are compared only by count).
        ///
        /// ON AN ASSEMBLY (brief R23) the volume and the features say nothing:
        /// the state writes neither, so both read 0 before and after every plan
        /// (ZERO-E-ZERO, <see cref="NumberOf"/>), and up to bridge 0.141.2 an
        /// undo there would have pressed without seeing a component moved by
        /// hand after the plan. So an assembly is compared by what its plans
        /// change: the components (which ones, their file, configuration,
        /// state, fixed, visible, transparent, where the origin is and how they
        /// are turned) and the mates (which ones, their kind, suppressed or not,
        /// the components they hold), as the state lists them.
        /// </summary>
        private sealed class PartSnapshot
        {
            public double? Volume;
            public int Count;
            public List<string> Names;
            public int Unlisted;

            /// <summary>The reading is of an assembly (`kind` «assembly»).</summary>
            public bool Assembly;

            /// <summary>The state listed the components (`components`).</summary>
            public bool ComponentsRead;

            /// <summary>The state listed the mates (`mates_list`, `mates_total`).</summary>
            public bool MatesRead;

            /// <summary>Every component of the assembly, listed or not (`how_many_components`).</summary>
            public int ComponentTotal;

            /// <summary>Every mate of the assembly, listed or not (`mates_total`).</summary>
            public int MateTotal;

            /// <summary>The listed components, in the order of the state (by name).</summary>
            public List<ComponentLook> Components;

            /// <summary>The listed mates, in tree order.</summary>
            public List<MateLook> Mates;

            /// <summary>Both lists were read, so the assembly can be compared.</summary>
            public bool AssemblyRead { get { return ComponentsRead && MatesRead; } }

            /// <summary>
            /// The equations of the document as the state lists them («equations»,
            /// and «equations_total» when they are more), one text; empty when it has
            /// none. SOLIDWORKS records no equation in an undo entry
            /// (Checks\UndoGaps, 27/10/2026), so a plan that changes them is one an
            /// undo cannot withdraw, and the snapshot must see it.
            /// </summary>
            public string Equations = "";

            public static PartSnapshot Of(Dictionary<string, object> docState)
            {
                if (docState == null) return null;
                int unlisted = 0;
                if (docState.TryGetValue("feature_not_listed", out var u) && u != null)
                    try { unlisted = Convert.ToInt32(u, CultureInfo.InvariantCulture); } catch { }
                var snapshot = new PartSnapshot
                {
                    Volume = NumberOf(docState, "volume_mm3"),
                    Count = HowManyFeatures(docState),
                    Names = FeatureNamesInOrder(docState),
                    Unlisted = unlisted,
                    Equations = EquationsText(docState),
                };
                string kind = Convert.ToString(docState.TryGetValue("kind", out var k) ? k : null) ?? "";
                if (kind == "assembly") ReadAssembly(docState, snapshot);
                return snapshot;
            }

            /// <summary>The equations of a reading as one text (see <see cref="Equations"/>).</summary>
            private static string EquationsText(Dictionary<string, object> docState)
            {
                if (!docState.TryGetValue("equations", out var raw) || !(raw is System.Collections.IEnumerable listed) || raw is string)
                    return "";
                var texts = new List<string>();
                foreach (var v in listed)
                {
                    var entry = v as Dictionary<string, object>;
                    texts.Add(entry == null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : FieldText(entry, "equation"));
                }
                int total = WholeNumber(docState, "equations_total");
                return string.Join(" | ", texts) + (total > texts.Count ? " (" + total + " in all)" : "");
            }

            /// <summary>
            /// The assembly half of a snapshot, from the fields AssemblyState
            /// writes. A mate names its components by uid («C2»), positional and
            /// renumbered when a component comes or goes: here they become the
            /// component names of the same reading, so that a component inserted
            /// before them does not read as a changed mate.
            /// </summary>
            private static void ReadAssembly(Dictionary<string, object> docState, PartSnapshot s)
            {
                s.Assembly = true;
                s.Unlisted = WholeNumber(docState, "components_not_listed") + WholeNumber(docState, "mates_not_listed");
                var components = new List<ComponentLook>();
                var mateLooks = new List<MateLook>();
                s.Components = components;
                s.Mates = mateLooks;
                var nameOfUid = new Dictionary<string, string>(StringComparer.Ordinal);

                if (docState.TryGetValue("components", out var cs) && cs is System.Collections.IEnumerable listed &&
                    !(cs is string) && docState.ContainsKey("how_many_components"))
                {
                    s.ComponentsRead = true;
                    s.ComponentTotal = WholeNumber(docState, "how_many_components");
                    foreach (var v in listed)
                    {
                        var entry = v as Dictionary<string, object>;
                        if (entry == null) continue;
                        ComponentLook look = ComponentLook.Of(entry);
                        components.Add(look);
                        string uid = FieldText(entry, "uid");
                        if (uid.Length > 0) nameOfUid[uid] = look.Name;
                    }
                }

                if (docState.TryGetValue("mates_list", out var ms) && ms is System.Collections.IEnumerable mates &&
                    !(ms is string) && docState.ContainsKey("mates_total"))
                {
                    s.MatesRead = true;
                    s.MateTotal = WholeNumber(docState, "mates_total");
                    foreach (var v in mates)
                    {
                        var entry = v as Dictionary<string, object>;
                        if (entry == null) continue;
                        var held = new List<string>();
                        if (entry.TryGetValue("components", out var h) && h is System.Collections.IEnumerable items && !(h is string))
                            foreach (var c in items)
                            {
                                string uid = Convert.ToString(c, CultureInfo.InvariantCulture) ?? "";
                                held.Add(nameOfUid.TryGetValue(uid, out var name) ? name : uid);
                            }
                        object off = entry.TryGetValue("suppressed", out var o) ? o : null;
                        string state = off is bool b ? (b ? "suppressed" : "active") : "suppressed or not unknown";
                        mateLooks.Add(new MateLook
                        {
                            Name = FieldText(entry, "name"),
                            Look = (FieldText(entry, "kind").Length > 0 ? FieldText(entry, "kind") : "kind unknown") + ", " +
                                   state + ", holds «" + string.Join("», «", held) + "»",
                        });
                    }
                }
            }
        }

        /// <summary>
        /// One component as an assembly snapshot compares it (brief R23). The
        /// origin and the axes are null when the state could not read them, and
        /// an unreadable place counts as a difference (Z-C8); the other fields
        /// are compared as written, a missing one equal to a missing one.
        /// </summary>
        private sealed class ComponentLook
        {
            public string Name, Path, Configuration, State, Grounded, Visible, Transparent;
            public double[] Origin;
            public double[] Axes;
            public string AxesText;

            public static ComponentLook Of(Dictionary<string, object> entry)
            {
                var look = new ComponentLook
                {
                    Name = FieldText(entry, "name"),
                    Path = FieldText(entry, "path"),
                    Configuration = FieldText(entry, "configuration"),
                    State = FieldText(entry, "state"),
                    Grounded = FieldText(entry, "grounded"),
                    Visible = FieldText(entry, "visible"),
                    Transparent = FieldText(entry, "transparent"),
                    Origin = Numbers(entry.TryGetValue("origin_mm", out var o) ? o : null, 3),
                };
                // «axes» is «+X -Y -Z» when every axis lies on an axis of the
                // assembly, otherwise three rows of three (SwOperations.AxesValue).
                object axes = entry.TryGetValue("axes", out var a) ? a : null;
                if (axes is string words)
                {
                    look.AxesText = words;
                    look.Axes = AlignedAxesNumbers(words);
                }
                else if (axes is System.Collections.IEnumerable rows)
                {
                    var all = new List<double>();
                    foreach (var row in rows)
                    {
                        var three = Numbers(row, 3);
                        if (three == null) { all = null; break; }
                        all.AddRange(three);
                    }
                    if (all != null && all.Count == 9)
                    {
                        look.Axes = all.ToArray();
                        look.AxesText = NumbersText(look.Axes);
                    }
                }
                return look;
            }

            /// <summary>«+X -Y -Z» as the nine numbers of the three rows; null if it is not that form.</summary>
            private static double[] AlignedAxesNumbers(string words)
            {
                var parts = words.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3) return null;
                var all = new double[9];
                for (int i = 0; i < 3; i++)
                {
                    string p = parts[i];
                    if (p.Length != 2 || (p[0] != '+' && p[0] != '-')) return null;
                    int axis = "XYZ".IndexOf(p[1]);
                    if (axis < 0) return null;
                    all[i * 3 + axis] = p[0] == '+' ? 1.0 : -1.0;
                }
                return all;
            }
        }

        /// <summary>
        /// One mate as an assembly snapshot compares it (brief R23): its name,
        /// and its kind, whether it is suppressed and the components it holds
        /// (by name) in words.
        /// </summary>
        private sealed class MateLook
        {
            public string Name, Look;
        }

        /// <summary>A field of an entry as text, empty when it is missing or null.</summary>
        private static string FieldText(Dictionary<string, object> entry, string key)
        {
            if (entry == null || !entry.TryGetValue(key, out var v) || v == null) return "";
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }

        /// <summary>A whole number of the state, 0 when it is missing or unreadable.</summary>
        private static int WholeNumber(Dictionary<string, object> docState, string key)
        {
            if (docState == null || !docState.TryGetValue(key, out var v) || v == null) return 0;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        /// <summary>A list of exactly <paramref name="count"/> numbers; null otherwise.</summary>
        private static double[] Numbers(object value, int count)
        {
            if (value == null || value is string || !(value is System.Collections.IEnumerable items)) return null;
            var all = new List<double>();
            foreach (var x in items)
            {
                if (x == null) return null;
                try { all.Add(Convert.ToDouble(x, CultureInfo.InvariantCulture)); } catch { return null; }
            }
            return all.Count == count ? all.ToArray() : null;
        }

        private LastPlan _lastPlan;

        /// <summary>
        /// Z-C21.14: why there is no memory, when a later plan or an
        /// undo dropped it, in English, for the refusal; null otherwise.
        /// </summary>
        private string _noMemoryBecause;

        /// <summary>
        /// True when <see cref="_noMemoryBecause"/> is the reason of a plan on a
        /// drawing (report 8cd843 N24): the undo then offers
        /// <see cref="DrawingNoMemoryRemedy"/> instead of delete_feature.
        /// </summary>
        private bool _noMemoryOnDrawing;

        /// <summary>
        /// Why a plan on a drawing leaves no plan to withdraw, or null when it is
        /// not one (report 8cd843 N24): the document the plan ended on is a
        /// drawing and at least one step succeeded. Pure, so the contract suite
        /// reads it.
        /// </summary>
        internal static string DrawingPlanMemoryReason(string kind, IList<string> succeeded, string name)
        {
            if (!string.Equals(kind, "drawing", StringComparison.Ordinal) || succeeded == null || succeeded.Count == 0)
                return null;
            // N40 (report 8cd843, round 11): a plan that put a section view and then
            // stopped recorded an entry, and the undo pressed after it left the view.
            return "The last «build» worked on the drawing «" + (name ?? "") + "» (" +
                   string.Join(", ", succeeded.Distinct(StringComparer.Ordinal)) + "). On a drawing I keep no plan to " +
                   "withdraw: SOLIDWORKS records no undo entry for a dimension or a note («undo_entry_recorded» false), " +
                   "the undo pressed after a section view left the view on the sheet (measured), and what I compare " +
                   "before pressing undo (the volume, the features, the components) is not on a drawing.";
        }

        /// <summary>The operations of the steps that succeeded, in order.</summary>
        private static List<string> SucceededOperations(IEnumerable<object> outcomes)
        {
            var names = new List<string>();
            foreach (var o in outcomes ?? new object[0])
            {
                var step = o as IDictionary<string, object>;
                object ok, op;
                if (step == null || !step.TryGetValue("succeeded", out ok) || !(ok is bool) || !(bool)ok) continue;
                if (step.TryGetValue("operation", out op) && op != null)
                    names.Add(Convert.ToString(op, System.Globalization.CultureInfo.InvariantCulture));
            }
            return names;
        }

        /// <summary>What to do when the undo has nothing to withdraw after a plan on a drawing (report 8cd843 N24).</summary>
        internal const string DrawingNoMemoryRemedy =
            "To take away a dimension or a note, send a plan with «delete_annotation» and its name as the plan that " +
            "placed it gave it (a dimension: «RD1@Drawing View1@...»; a note: its «name»); the person at the SOLIDWORKS " +
            "screen can also select it and delete it by hand. A view (a section, a detail, a model view) has no " +
            "operation that removes it yet: only the person at the screen can delete it.";

        /// <summary>The four answers of <see cref="MemoryAfterPlan"/>.</summary>
        internal const string MemoryNew = "new", MemoryKeep = "keep", MemoryCover = "cover", MemoryDrop = "drop";

        /// <summary>
        /// WHAT A PLAN DOES TO THE UNDO MEMORY (Z-C21.14, reviewed).
        /// Pure, so that a contract can call it. Returns
        /// <see cref="MemoryNew"/> (this plan becomes the memory),
        /// <see cref="MemoryKeep"/> (the memory, or its absence and the reason
        /// for it, stays as it is), <see cref="MemoryCover"/> (the memory is
        /// kept and marked covered by a recorded entry, with
        /// <paramref name="reason"/>) or <see cref="MemoryDrop"/> (the memory
        /// goes, if there was one, and <paramref name="reason"/> says why).
        ///
        /// The rules, in this order:
        ///  0. another bridge process held the turn on the memory's document
        ///     (review F4): cover;
        ///  1. the plan never reached the executor, or stopped before opening
        ///     its recording: nothing was written, keep;
        ///  2. the executor threw (review F9): what it wrote and recorded is
        ///     not known, drop;
        ///  3. the plan's document could not be read after it: drop;
        ///  3b. the plan changed the equations of its document: drop, SOLIDWORKS
        ///     records no equation in an undo entry (Checks\UndoGaps), so one undo
        ///     would take away what came before them and leave them;
        ///  4. the plan built (volume or feature count changed; on an assembly a
        ///     component or a mate, brief R23): new, unless its own undo entry
        ///     closed empty (answer false): then drop, an undo would not withdraw
        ///     it (scenario t13);
        ///  5. a plan whose own undo entry closed with something in it
        ///     (FinishRecordingUndoObject answered true: a reorder, a rename, a
        ///     property): new, it is the last plan and its entry is on top
        ///     (review F1);
        ///  6. no memory: keep when the plan wrote nothing (answer false and
        ///     nothing visibly changed), otherwise drop with the reason (review
        ///     F12: the reason is rewritten after every plan that wrote);
        ///  7. a covered memory stays covered;
        ///  8. a plan on another document: drop;
        ///  9. a measure that recorded a non-empty entry: cover;
        /// 10. answer false and nothing visibly changed: keep (review F3; the
        ///     bench case `undo case c` decides that an empty recording does not
        ///     stand between the memory and the press);
        /// 11. anything else (answer unknown, or false with a visible change):
        ///     drop.
        /// </summary>
        internal static string MemoryAfterPlan(
            bool built, bool visibleChange, bool afterReadable,
            bool hasMemory, bool alreadyCovered,
            bool executorReached, bool stoppedBeforeWriting, bool executorThrew,
            bool turnRefusedOnMemoryDocument, bool? undoEntryOnPlanDocument, bool sameDocument,
            bool measure, bool equationsChanged, string planDocumentName, out string reason)
        {
            reason = null;
            string what = measure ? "The last «measure»" : "The last «build»";
            string on = " «" + (planDocumentName ?? "") + "»";

            if (turnRefusedOnMemoryDocument && hasMemory && !alreadyCovered)
            {
                reason = "Another bridge process was working on the document of the plan I remember when my last " +
                         "plan was refused its turn there: whatever it recorded is now on top of the undo stack, " +
                         "above my plan.";
                return MemoryCover;
            }
            if (!executorReached || stoppedBeforeWriting) return MemoryKeep;
            if (executorThrew)
            {
                reason = what + " stopped with an exception in the plan executor, so what it wrote on" + on +
                         " and what it left on top of its undo stack are not known.";
                return MemoryDrop;
            }
            if (!afterReadable)
            {
                reason = what + " ran, and then I could not read" + on + " again: what is on top of its undo stack " +
                         "is not known.";
                return MemoryDrop;
            }
            // EQUATIONS ARE IN NO UNDO ENTRY (scenario t14 and Checks\UndoGaps,
            // 27/10/2026): «parameters» wrote two variables, FinishRecordingUndoObject
            // answered false, and the undo pressed after it took away the
            // extrusion of the plan BEFORE it while the equations stayed. On the
            // probe the same with EditRebuild3, ForceRebuild3 or no rebuild. A plan
            // that changed them cannot be withdrawn by undo, whatever else it did.
            if (equationsChanged && !measure)
            {
                reason = what + " changed the equations of" + on + " (global variables or dimension links), and " +
                         "SOLIDWORKS records no equation in an undo entry (measured): one undo would leave them and take " +
                         "away what came before them. To put a variable back, send «parameters» with the value it had " +
                         "(the answer of the plan gives it in «equation_before»); a variable or a link the plan created " +
                         "stays, and no operation removes an equation yet: the person at the SOLIDWORKS screen can delete " +
                         "it (Tools > Equations).";
                return MemoryDrop;
            }
            // A PLAN THAT BUILT WHILE ITS UNDO ENTRY CLOSED EMPTY (scenario t13,
            // 27/10/2026: two revolves, FinishRecordingUndoObject false, and the
            // undo pressed took nothing away and blamed «something done after
            // the plan»). What it built is not in its entry, so one undo does not
            // withdraw it and could take away something older: no memory, and the
            // reason says what to do.
            if (built && !measure && undoEntryOnPlanDocument == false)
            {
                reason = what + " built on" + on + ", but SOLIDWORKS answered that its undo entry closed with nothing " +
                         "in it (FinishRecordingUndoObject false): one undo would not withdraw it, and could take away " +
                         "something older.";
                return MemoryDrop;
            }
            if (built) return MemoryNew;
            if (!measure && undoEntryOnPlanDocument == true) return MemoryNew;

            bool wroteNothing = undoEntryOnPlanDocument == false && !visibleChange;
            if (!hasMemory)
            {
                if (wroteNothing) return MemoryKeep;
                reason = undoEntryOnPlanDocument == true
                    ? what + " put its own undo entry on top of the undo stack of" + on +
                      " (FinishRecordingUndoObject answered true), and a measure is not a plan to withdraw."
                    : UnknownTop(what, on, undoEntryOnPlanDocument);
                return MemoryDrop;
            }
            if (alreadyCovered) return MemoryKeep;
            if (!sameDocument)
            {
                reason = what + " ran on another document," + on + ", so I no longer keep the memory of the plan " +
                         "before it: I do not press undo on a stack I cannot vouch for.";
                return MemoryDrop;
            }
            if (undoEntryOnPlanDocument == true)
            {
                reason = what + " put its own undo entry («MCP bridge»: FinishRecordingUndoObject answered true) on " +
                         "top of the undo stack of" + on + ", above the plan I remember.";
                return MemoryCover;
            }
            if (wroteNothing) return MemoryKeep;
            reason = UnknownTop(what, on, undoEntryOnPlanDocument);
            return MemoryDrop;
        }

        private static string UnknownTop(string what, string on, bool? undoEntry)
        {
            return undoEntry == false
                ? what + " changed something I can see on" + on + " (the feature names or their order, or on an " +
                  "assembly its components or mates) while " +
                  "SOLIDWORKS answered that its undo entry closed with nothing in it: I cannot tell what is on top " +
                  "of the undo stack."
                : what + " ran on" + on + ", and I do not know whether it left an undo entry on top of its undo stack.";
        }

        /// <summary>
        /// Is <paramref name="document"/> (identity <paramref name="identity"/>)
        /// the document of <paramref name="memory"/>? By the COM reference when
        /// both are held (review F11), by name otherwise.
        /// </summary>
        private static bool SameDocumentAs(LastPlan memory, object document, Dictionary<string, object> identity)
        {
            if (memory == null) return false;
            if (memory.DocumentRef != null && document != null)
                return Bridge.Session.SameComObject(memory.DocumentRef, document);
            return Bridge.Session.SameDocument(memory.Document, identity);
        }

        /// <summary>
        /// An identity in the form the plan executor compares (review F7): the
        /// path, or the title with its prefix when the document has never been
        /// saved; null when neither is known.
        /// </summary>
        internal static string IdentityForTheExecutor(Dictionary<string, object> identity)
        {
            if (identity == null) return null;
            string path = Convert.ToString(identity.TryGetValue("path", out var p) ? p : null) ?? "";
            if (path.Trim().Length > 0) return path;
            string title = Convert.ToString(identity.TryGetValue("document_title", out var t) ? t : null) ?? "";
            return title.Trim().Length > 0 ? "title:" + title : null;
        }

        /// <summary>
        /// The note of `build` when another document is in front (Z-C21.14,
        /// review F5). Pure, so that a contract can call it.
        /// </summary>
        internal static string NoteDocumentInFront(string planDocument, string front, bool frontIsThePlans)
        {
            const string bringBack = "(«document» with «action»: «activate»)";
            string head = "The plan worked on «" + planDocument + "», and in front now is «" + front + "»";
            if (frontIsThePlans)
                return head + ", the document the plan ended on. «state_after» describes «" + front + "», the document " +
                       "the next plan works on; «volume_after_mm3» is of «" + planDocument + "». To withdraw this " +
                       "plan with an undo, bring «" + planDocument + "» to the front first " + bringBack + ".";
            return head + ", a window the plan did not end on: it came to the front from outside while the plan ran. " +
                   "«state_after» describes «" + front + "» for information only: I keep neither its identity nor its " +
                   "signatures, so the next plan asks for «inspect_part» first. To go on with «" + planDocument +
                   "», bring it back " + bringBack + " and call «inspect_part»; to work on «" +
                   front + "», call «inspect_part» now.";
        }

        /// <summary>
        /// What to do when the undo has no plan to withdraw (review F12): the
        /// reader is a model, so the way that does not need a keyboard comes
        /// first.
        /// </summary>
        internal const string NoMemoryRemedy =
            "If something must go, take it away with a new plan («delete_feature» or «suppress_feature» " +
            "on the feature, by the name «inspect_part» gives it); the person at the SOLIDWORKS screen can also " +
            "undo by hand (Edit > Undo), seeing what each step takes away.";

        /// <summary>
        /// How far a volume may be off and still be considered the same.
        ///
        /// One thousandth of a mm³: SOLIDWORKS recalculates the volume at every rebuild and
        /// the last digit dances. Tighter than this it would refuse identical
        /// parts; wider it would let a real change through — a Ø1 hole
        /// 1 deep is 0.79 mm³, almost a thousand times this threshold.
        /// </summary>
        private const double VolumeDeviationMm3 = 0.001;

        /// <summary>
        /// WITHDRAWS THE LAST PLAN.
        ///
        /// WHY IT EXISTS. Up to 0.31.0 this bridge said **twenty-one
        /// times** to the model «undo it with Ctrl+Z». The recipient of those
        /// messages is a language model, which has no keyboard: it was a
        /// repair offered to someone who could not take it. And the
        /// thirty-six operations are all additive or parametric, so
        /// the tree could only grow: a model that noticed it had
        /// got it wrong had two paths, both wrong — build on top of it,
        /// or redo the part from scratch.
        ///
        /// THE MACHINE WAS ALREADY THERE. `PlanExecutor` opens and closes an undo
        /// entry around every plan (entry B8, measured: «one
        /// Ctrl+Z brings the document back to the value of volume_before_mm3»). It was
        /// triggered only by a human keyboard. It is the same shape as
        /// section V — the capability is there, and it does not reach the reader.
        ///
        /// ── THE REFUSAL COMES FIRST ──────────────────────────────────
        ///
        /// Between the plan and this call the designer may have worked by
        /// hand. Pressing undo then takes away THEIR work, and it is a
        /// damage that no message repairs. So TWO numbers that the answer of
        /// `build` already contained are compared — the volume and
        /// how many features there are — and if they do not match **we stop and
        /// say what changed**.
        ///
        /// Since Z-C21.14 (review F1) the feature names, their IDs and their
        /// order are compared too: a reorder or a rename done after the plan by
        /// another session, another bridge process or the person is seen before
        /// pressing. What changes none of these (a property, a configuration)
        /// still goes unseen.
        ///
        /// On an assembly (brief R23) the volume and the features are 0 on
        /// both sides, so the same three moments (before pressing, after
        /// pressing, after a redo) compare the components and the mates
        /// instead (<see cref="AssemblyDiffers"/>): a component moved, turned,
        /// added, taken away, fixed or suppressed by hand after the plan, or a
        /// mate changed, is seen before pressing; and without the two lists it
        /// stops, as it does without a volume.
        ///
        /// ── A WRONG PRESS CANNOT BE TAKEN BACK (Z-C21.14) ─────────────────
        ///
        /// Up to bridge 0.124.1 this summary said that the way out was to try,
        /// check, and undo the wrong try with a redo. Measured false on
        /// 1/10/2026 (probe `redo`; RAG note 82zz_duodecies, section «The live
        /// probe of M19»): SOLIDWORKS never redoes an undo entry recorded
        /// through the API, and every plan of this bridge is one; only an
        /// action that was not recorded comes back. And the answer said «I put it
        /// back right away» whenever EditRedo2 did not throw, without
        /// looking. So now:
        ///  * when the memory is covered (something is KNOWN to be on top of
        ///    the plan's entry), or the part reads differently from how the
        ///    plan left it, it refuses BEFORE pressing, says why and what to do;
        ///  * after pressing it says «withdrawn» only if the part reads as it
        ///    was before the plan (volume, feature count, feature names and
        ///    order); otherwise it compares the part with how it was before
        ///    pressing, tries one redo only when the press took away something
        ///    visible, and says «restored» only if the part then reads as before
        ///    pressing, otherwise what is still different. With a volume it
        ///    cannot read it does not redo at all (Z-C8).
        /// There is still no API to look at the undo stack before pressing.
        /// </summary>
        private Dictionary<string, object> Undo()
        {
            if (!_session.Attached)
                return Protocol.Content("The bridge is not attached to SolidWorks.", true);

            var memory = _lastPlan;
            if (memory == null)
                return Protocol.Content(
                    "I have no plan to withdraw: «undo» reverses the last " +
                    "«build» of this conversation, and " +
                    (_noMemoryBecause != null
                        ? "I keep no memory of the last one, for the reason below.\n\n"
                        : "here I have not yet run any that built something.\n\n") +
                    "I do not press undo blindly: I would remove something I did not " +
                    "do myself, and I could not even say what." +
                    (_noMemoryBecause != null ? "\n\n" + _noMemoryBecause : "") +
                    "\n\n" + (_noMemoryOnDrawing && _noMemoryBecause != null ? DrawingNoMemoryRemedy : NoMemoryRemedy), true);

            // ── SOMETHING IS KNOWN TO BE ON TOP: REFUSE BEFORE PRESSING ──
            // (Z-C21.14). No reading, no press: the memory says the remembered
            // plan is not on top of the undo stack, and a press would take away
            // what is.
            if (memory.CoveredBecause != null)
                return RefusalCovered(memory);

            // PROGRESS, AS «build» GIVES IT (report 8cd843 N9): an undo of six
            // cuts took 8.6 s with no notification at all.
            _wire.Total = 3;
            _wire.Step(1, "reading the document before the undo");
            var stateBefore = _session.StateWithoutAnchor(out var readError);
            if (stateBefore == null) return Protocol.Content(readError, true);

            var identityNow = StateIdentity(stateBefore);
            // THE SAME OPEN DOCUMENT, by its COM reference when it is held
            // (review F11): a save renames it, and a document closed and opened
            // again keeps its name and loses the undo stack with the plan in it.
            bool samePart = memory.DocumentRef != null
                ? _session.IsInFront(memory.DocumentRef)
                : Bridge.Session.SameDocument(memory.Document, identityNow);
            if (!samePart)
                return Protocol.Content(
                    "I am stopping: the document changed. I had run the plan " +
                    "on «" + ReadableName(memory.Document) + "», now in front " +
                    "is «" + ReadableName(identityNow) + "». Undoing here " +
                    "would remove the last thing done on a part that has nothing to do with it.\n\n" +
                    (Bridge.Session.SameDocument(memory.Document, identityNow)
                        ? "It has the same name, but it is not the same open document (it was closed and opened " +
                          "again, or it is another copy): its undo stack does not hold my plan.\n\n"
                        : "") +
                    "Bring the right part to the front and call me again.", true);

            var now = PartSnapshot.Of(stateBefore);
            var after = memory.After;
            if (now.Volume == null || after == null || after.Volume == null)
                return Protocol.Content(
                    "I am stopping: I cannot measure the volume of the part, and without it I cannot " +
                    "tell you whether someone else changed it after the plan. A step back " +
                    "blindly could undo the work of whoever is in front of the screen: " +
                    "if you still want to go back, do it by hand.", true);

            // ON AN ASSEMBLY THE VOLUME IS 0 ON BOTH SIDES (brief R23): what tells
            // whether someone moved a component after the plan are the lists of
            // components and mates, and without them we stop the same way.
            if (after.Assembly && !(now.AssemblyRead && after.AssemblyRead))
                return Protocol.Content(
                    "I am stopping: I cannot read the components and the mates of the assembly" +
                    (!after.AssemblyRead ? " as my plan left it" : " now") + ", and without them I cannot " +
                    "tell you whether someone else moved, added or took away something after the plan. A step back " +
                    "blindly could undo the work of whoever is in front of the screen: " +
                    "if you still want to go back, do it by hand.", true);

            if (Math.Abs(now.Volume.Value - after.Volume.Value) > VolumeDeviationMm3 ||
                now.Count != after.Count)
                return Protocol.Content(
                    "I am stopping: after the plan the part changed, and I did not " +
                    "change it.\n\n" +
                    Comparison("volume", after.Volume.Value, now.Volume.Value, "mm³") +
                    Comparison("features", after.Count, now.Count, "") +
                    "\nGoing back one step now would not undo my plan: " +
                    "it would undo the last thing done, which is someone else's. " +
                    "If you still want to go back, it is something that has to be done by " +
                    "whoever is in front of the screen, because only they know what is " +
                    "being thrown away.", true);

            // THE NAMES AND THE ORDER TOO (review F1, F14): a reorder or a
            // rename done after the plan leaves volume and count as they were,
            // and a press would take it away for good.
            var changedSince = SnapshotDiffers(after, now);
            if (changedSince.Count > 0)
                return RefusalChangedSinceThePlan(memory, changedSince, NamesComparedOnlyInPart(after, now));

            // ── UNDO IS PRESSED ─────────────────────────────────────────────────
            _wire.Step(2, "pressing undo");
            var undoError = _session.StepBack();
            if (undoError != null)
                return Protocol.Content(undoError, true);

            _wire.Step(3, "reading the document back");
            var stateAfter = _session.State(out var stateError);
            if (stateAfter == null)
                return Protocol.Content(
                    "I pressed undo and then could not read the " +
                    "document back: " + stateError + "\n\n" +
                    "I cannot tell you whether it worked. Look at the part before " +
                    "doing anything else.", true);

            var back = PartSnapshot.Of(stateAfter);
            var before = memory.Before;
            // IF THE VOLUME CANNOT BE READ, WE DO NOT REDO BLINDLY (entry Z-C8):
            // «it did not come back» would lead to a step forward that might
            // put back the very plan that was just taken away.
            if (back.Volume == null || before == null || before.Volume == null)
            {
                _lastPlan = null;
                _noMemoryBecause =
                    "The last «undo» pressed undo and then could not read the volume: what is on " +
                    "top of the undo stack now is not known.";
                return Protocol.Content(
                    "I pressed undo, but then I cannot measure the volume of the part: " +
                    "I cannot tell you whether it came back as it was. I did nothing else. Look at the " +
                    "part before going on.", true);
            }
            // THE SAME ON AN ASSEMBLY (brief R23): without the lists of components
            // and mates «it did not come back» cannot be told, and a redo would
            // be blind.
            if (before.Assembly && !(back.AssemblyRead && before.AssemblyRead))
            {
                _lastPlan = null;
                _noMemoryBecause =
                    "The last «undo» pressed undo and then could not read the components and the mates of the " +
                    "assembly: what is on top of the undo stack now is not known.";
                return Protocol.Content(
                    "I pressed undo, but then I cannot read the components and the mates of the assembly: " +
                    "I cannot tell you whether it came back as it was. I did nothing else. Look at the " +
                    "assembly before going on.", true);
            }
            // BACK AS IT WAS BEFORE THE PLAN: volume, count, names and order
            // (review F1), not only the two numbers.
            var notAsBeforeThePlan = SnapshotDiffers(before, back);
            bool namesPartial = NamesComparedOnlyInPart(before, back);

            if (notAsBeforeThePlan.Count > 0)
            {
                // IT TOOK AWAY SOMETHING ELSE (Z-C21.14). The part read as the
                // plan left it, so what was on top changed nothing that is
                // compared: a property, a configuration.
                //
                // Up to bridge 0.124.1 a redo followed here, blind, and the
                // answer said «I put it back right away» whenever EditRedo2
                // did not throw. Measured on 1/10/2026 (probe `redo`): an undo
                // entry recorded through the API is never redone, so that
                // answer lied for every plan of this bridge. Now the part is
                // compared with how it was before pressing (`before`): only when
                // the press took away something visible is ONE redo tried (it
                // can bring back an action that was not recorded, measured),
                // and then the part is read again and compared once more. The
                // answer says «restored» only from that comparison.
                var takenAway = WhatDiffers(stateBefore, stateAfter);
                bool redoTried = false;
                string redoError = null;
                var readAgain = stateAfter;
                if (takenAway.Count > 0)
                {
                    redoTried = true;
                    redoError = _session.StepForward();
                    readAgain = _session.State(out _);
                }
                var stillDifferent = readAgain == null
                    ? new List<string> { "the " + (before.Assembly ? "assembly" : "part") + " could not be read again" }
                    : WhatDiffers(stateBefore, readAgain);

                bool restored;
                string answerText;
                if (before.Assembly)
                    answerText = AnswerAfterWrongPressOnAssembly(
                        before.ComponentTotal, before.MateTotal, back.ComponentTotal, back.MateTotal, notAsBeforeThePlan,
                        takenAway, redoTried, redoError, stillDifferent, namesPartial, out restored);
                else
                    answerText = AnswerAfterWrongPress(
                        before.Volume.Value, before.Count, back.Volume.Value, back.Count, notAsBeforeThePlan,
                        takenAway, redoTried, redoError, stillDifferent, namesPartial, out restored);

                // THE MEMORY: restored, the foreign action is on top again and
                // KNOWN to be (covered by an action that was not recorded: the
                // next undo refuses before pressing); otherwise what is on
                // top is not known any more, and the memory goes.
                if (restored)
                {
                    memory.CoveredBecause =
                        "An action done after the plan, which changes neither the volume, nor the feature count, " +
                        "nor the feature names and order, is on top of the undo stack: the last undo took it " +
                        "away and the redo put it back.";
                    memory.CoveredByRecordedEntry = false;
                }
                else
                {
                    _lastPlan = null;
                    _noMemoryBecause =
                        "The last «undo» took away something that was not the plan, and it did not come " +
                        "back: what is on top of the undo stack now is not known.";
                }

                if (readAgain != null)
                {
                    _seen = StateIdentity(readAgain);
                    _seenSignatures = StateSignatures(readAgain);
                    _signatureSeen = LightSignature(readAgain);
                    RemoveAnchors(readAgain);
                }
                else
                {
                    _seen = null;
                    _seenSignatures = null;
                    _signatureSeen = null;
                }

                return Protocol.Content(Protocol.InJson(
                    new Dictionary<string, object>
                    {
                        ["undone"] = false,
                        ["document"] = ReadableName(identityNow),
                        ["reason"] = answerText,
                        ["not_as_before_the_plan"] = notAsBeforeThePlan,
                        ["what_the_undo_took_away"] = takenAway,
                        ["redo_tried"] = redoTried,
                        ["restored"] = restored,
                        ["still_different"] = stillDifferent,
                        ["state_after"] = readAgain,
                    }), true);
            }

            // SUCCEEDED: THE MEMORY IS ERASED.
            // A second «undo» must not undo what was there BEFORE the
            // plan: this bridge did not do that, and the undo stack
            // cannot tell the difference.
            // EXCEPT AFTER A REFUSED PLAN (report 8cd843 N5): what was taken
            // away was its empty entry, and the plan before it, which this
            // bridge did make, is on top again.
            _lastPlan = memory.Refused ? memory.Previous : null;
            _noMemoryBecause = _lastPlan != null ? null :
                "The last «undo» already withdrew the plan; one more step back would take away what " +
                "was there before it, which this bridge did not make.";

            // THE IDENTIFIERS ARE NO LONGER THE SAME, and the reader must
            // know it from the answer instead of finding out at the next plan:
            // the uids are positional and are renumbered by area at every
            // reading. This reading is signed, exactly as `build` does.
            _seen = StateIdentity(stateAfter);
            _seenSignatures = StateSignatures(stateAfter);
            _signatureSeen = LightSignature(stateAfter);
            RemoveAnchors(stateAfter);

            // A PLAN THAT CHANGED NOTHING THAT IS COMPARED (a property, a
            // sketch that is not a counted feature) cannot be seen to go by
            // reading: «withdrawn» is then what SOLIDWORKS did with the entry
            // on top, and the answer says it was not checked (Z-C21.14).
            bool checkedByReading = SnapshotDiffers(before, after).Count > 0;
            string documentWord = before.Assembly ? "assembly" : "part";
            var outside = new Dictionary<string, object>
            {
                ["undone"] = true,
                ["document"] = ReadableName(identityNow),
            };
            // On an assembly the volume and the features are 0 on both sides
            // (brief R23): the counts that say something are its own.
            if (before.Assembly)
            {
                outside["components_before"] = now.ComponentTotal;
                outside["components_after"] = back.ComponentTotal;
                outside["mates_before"] = now.MateTotal;
                outside["mates_after"] = back.MateTotal;
            }
            else
            {
                outside["volume_before_mm3"] = Math.Round(now.Volume.Value, 2);
                outside["volume_after_mm3"] = Math.Round(back.Volume.Value, 2);
                outside["features_before"] = now.Count;
                outside["features_after"] = back.Count;
            }
            outside["checked_by_reading"] = checkedByReading;
            // A REFUSED PLAN changed nothing, so nothing «went back» (report
            // 8cd843 N10: «withdrawn whole» next to «took away only its undo
            // entry», in the same answer).
            outside["note"] = memory.Refused
                ? "The plan was refused and had changed nothing: this undo took away its undo entry, and the " +
                  documentWord + " is as it was before that plan. The identifiers in «state_after» are the " +
                  "valid ones now: the earlier ones are no longer valid."
                : "The plan was withdrawn whole and the " + documentWord + " went " +
                  "back to how it was. The identifiers in «state_after» are " +
                  "the valid ones now: the earlier ones are no longer valid.";
            outside["state_after"] = stateAfter;
            if (memory.Refused)
                outside["note_refused_plan"] =
                    "The plan I withdrew had been refused and had changed nothing I compare: this undo took away " +
                    "only its undo entry. " + (memory.Previous != null
                        ? "The plan before it is on top of the undo stack again, and I remember it: «undo» once " +
                          "more withdraws it."
                        : "I remember no plan before it on this document.");
            if (!checkedByReading)
                outside["note_checked"] =
                    "This plan changed nothing I compare (" +
                    (before.Assembly ? AssemblyScope(false) : "volume, feature count, feature names and order") +
                    "), so reading the " + documentWord + " cannot show that it is gone: SOLIDWORKS took away the entry " +
                    "on top, and the last entry I know of on this document was this plan's (its «undo_entry_recorded» " +
                    "was true, and nothing I compare changed since).";
            else if (namesPartial)
                outside["note_checked"] = before.Assembly
                    ? "The assembly reads as before the plan by " + AssemblyScope(true) + "."
                    : "The part reads as before the plan by volume and feature count, and by " + NamesScope(true) + ".";
            return Protocol.Content(Protocol.InJson(outside));
        }

        /// <summary>
        /// THE REFUSAL BEFORE PRESSING (Z-C21.14): the remembered plan is known
        /// not to be on top of the undo stack. Nothing was read or touched.
        /// </summary>
        private Dictionary<string, object> RefusalCovered(LastPlan memory)
        {
            var outside = new Dictionary<string, object>
            {
                ["undone"] = false,
                ["refused_before_pressing"] = true,
                ["document"] = ReadableName(memory.Document),
                ["covered_by_recorded_entry"] = memory.CoveredByRecordedEntry,
                ["reason"] = RefusalCoveredText(memory.CoveredBecause, memory.CoveredByRecordedEntry),
            };
            return Protocol.ContentWithData(Protocol.InJson(outside), outside, true);
        }

        /// <summary>
        /// What to do instead of a press that would take away what is not the
        /// plan (Z-C21.14): the reader is a model, without a keyboard, so the
        /// way through a new plan comes first and the person at the screen
        /// second (review F1).
        /// </summary>
        internal const string WithdrawInsteadRemedy =
            "If the plan must go: take its features away with a new plan («delete_feature» or " +
            "«suppress_feature», by the names «inspect_part» gives), or ask the person at the SOLIDWORKS " +
            "screen, who can see what each step removes, to undo by hand (Edit > Undo).";

        /// <summary>
        /// The text of <see cref="RefusalCovered"/>, pure so that a contract can
        /// call it: why, what a press would do (which depends on what covers the
        /// plan, review F10), and what to do instead.
        /// </summary>
        internal static string RefusalCoveredText(string coveredBecause, bool recordedEntryOnTop)
        {
            string press = recordedEntryOnTop
                ? " One step back now would take that away, not my plan, and SOLIDWORKS cannot redo an undo entry " +
                  "recorded through the API (measured on 1/10/2026): it would be lost for good."
                : " One step back now would take that away again, not my plan. It is an action that was not recorded " +
                  "through the API, which a redo can put back (measured on 1/10/2026), but taking away what is not " +
                  "mine and putting it back does not withdraw my plan.";
            return "I am not pressing undo, and nothing was touched. " + coveredBecause + press + "\n\n" +
                   WithdrawInsteadRemedy;
        }

        /// <summary>
        /// THE REFUSAL BEFORE PRESSING when the part reads differently from how
        /// the plan left it in something other than volume and count: the
        /// feature names, their IDs or their order (Z-C21.14, review F1 and
        /// F14). Nothing was touched.
        /// </summary>
        private Dictionary<string, object> RefusalChangedSinceThePlan(
            LastPlan memory, List<string> changed, bool namesPartial)
        {
            var outside = new Dictionary<string, object>
            {
                ["undone"] = false,
                ["refused_before_pressing"] = true,
                ["document"] = ReadableName(memory.Document),
                ["what_changed_since_the_plan"] = changed,
                ["reason"] = memory.After != null && memory.After.Assembly
                    ? RefusalChangedSinceThePlanOnAssemblyText(changed, namesPartial)
                    : RefusalChangedSinceThePlanText(changed, namesPartial),
            };
            return Protocol.ContentWithData(Protocol.InJson(outside), outside, true);
        }

        /// <summary>The text of <see cref="RefusalChangedSinceThePlan"/>, pure.</summary>
        internal static string RefusalChangedSinceThePlanText(List<string> changed, bool namesPartial)
        {
            return ChangedSinceThePlanText("part", changed, namesPartial ? NamesScope(true) : null);
        }

        /// <summary>
        /// <see cref="RefusalChangedSinceThePlanText"/> on an assembly (brief
        /// R23): a component moved, turned, added or taken away, or a mate
        /// changed, since the plan. Pure.
        /// </summary>
        internal static string RefusalChangedSinceThePlanOnAssemblyText(List<string> changed, bool listedOnly)
        {
            return ChangedSinceThePlanText("assembly", changed, listedOnly ? AssemblyScope(true) : null);
        }

        private static string ChangedSinceThePlanText(string documentWord, List<string> changed, string partialScope)
        {
            return "I am not pressing undo, and nothing was touched. After my plan the " + documentWord +
                   " changed, and I did not change it: " + string.Join("; ", changed ?? new List<string>()) + "." +
                   (partialScope != null ? " (I compared " + partialScope + ".)" : "") +
                   " One step back now would not withdraw my plan: it would take away the last thing done, which is " +
                   "someone else's (another session, another bridge process or the person at the screen), and " +
                   "SOLIDWORKS cannot redo an undo entry recorded through the API (measured on 1/10/2026).\n\n" +
                   (documentWord == "assembly" ? WithdrawInsteadOnAssemblyRemedy : WithdrawInsteadRemedy);
        }

        /// <summary>
        /// <see cref="WithdrawInsteadRemedy"/> on an assembly (brief R23): the
        /// catalog has no operation that takes a component away, so what a plan
        /// inserted goes only by hand; mates and places do have one.
        /// </summary>
        internal const string WithdrawInsteadOnAssemblyRemedy =
            "If the plan must go: take its mates away with a new plan («delete_mate» or «suppress_mate», by the names " +
            "«inspect_part» gives in «mates_list») and put a component back with «place_component»; a component the plan " +
            "inserted cannot be taken away by a plan, so ask the person at the SOLIDWORKS screen, who can see what each " +
            "step removes, to undo by hand (Edit > Undo) or delete it.";

        /// <summary>
        /// WHAT IS VISIBLY DIFFERENT between two readings of the same part
        /// (Z-C21.14): the volume, the feature count, and the feature names and
        /// their order (with the ID SOLIDWORKS gives each feature, which a
        /// rename does not change). Empty when nothing visible differs; a volume
        /// that cannot be read on either side counts as a difference, because
        /// sameness cannot be seen (Z-C8). The names are those the state lists:
        /// beyond <c>DocumentState.MaxFeature</c> the older features are
        /// compared only by count (review F16, <see cref="NamesComparedOnlyInPart"/>).
        /// Pure, so that a contract can call it.
        /// </summary>
        internal static List<string> WhatDiffers(
            Dictionary<string, object> before, Dictionary<string, object> after)
        {
            return SnapshotDiffers(PartSnapshot.Of(before), PartSnapshot.Of(after));
        }

        /// <summary>The equations differ between two snapshots that were both read.</summary>
        private static bool EquationsChanged(PartSnapshot before, PartSnapshot after)
        {
            return before != null && after != null &&
                   !string.Equals(before.Equations ?? "", after.Equations ?? "", StringComparison.Ordinal);
        }

        /// <summary><see cref="WhatDiffers"/> on two snapshots.</summary>
        private static List<string> SnapshotDiffers(PartSnapshot before, PartSnapshot after)
        {
            var differences = new List<string>();
            if (before == null || after == null)
            {
                differences.Add("one of the two readings is missing");
                return differences;
            }

            if (before.Volume == null || after.Volume == null)
                differences.Add("the volume cannot be read" +
                                (before.Volume == null ? " before" : "") +
                                (after.Volume == null ? " after" : ""));
            else
            {
                double was = before.Volume.GetValueOrDefault(), now = after.Volume.GetValueOrDefault();
                if (Math.Abs(was - now) > VolumeDeviationMm3)
                    differences.Add("volume " + Number(was) + " mm³ before, " + Number(now) + " mm³ now");
            }

            if (before.Count != after.Count)
                differences.Add("features " + before.Count + " before, " + after.Count + " now");

            var namesBefore = before.Names ?? new List<string>();
            var namesAfter = after.Names ?? new List<string>();
            var gone = namesBefore.Where(n => !namesAfter.Contains(n)).ToList();
            var come = namesAfter.Where(n => !namesBefore.Contains(n)).ToList();
            if (gone.Count > 0) differences.Add("features no longer there: «" + string.Join("», «", gone) + "»");
            if (come.Count > 0) differences.Add("features that were not there: «" + string.Join("», «", come) + "»");
            if (gone.Count == 0 && come.Count == 0 && !namesBefore.SequenceEqual(namesAfter))
                differences.Add("the order of the features changed");
            if (EquationsChanged(before, after))
                differences.Add("the equations changed: «" + (before.Equations.Length > 0 ? before.Equations : "none") +
                                "» before, «" + (after.Equations.Length > 0 ? after.Equations : "none") + "» now");
            if (before.Assembly || after.Assembly)
                differences.AddRange(AssemblyDiffers(before, after, true));
            return differences;
        }

        /// <summary>
        /// How far an origin may move, in mm, and still be the same place
        /// (brief R23): half a step above the rounding of the state, which writes
        /// `origin_mm` to three decimals, so that a last digit rounded the other
        /// way does not read as a move and a move of two thousandths does.
        /// </summary>
        private const double OriginDeviationMm = 0.0015;

        /// <summary>
        /// How far a direction cosine of the axes may change and still be the
        /// same turn (brief R23): half a step above the rounding of the state
        /// (four decimals, SwOperations.AxesValue).
        /// </summary>
        private const double AxesDeviation = 0.00015;

        /// <summary>
        /// WHAT IS DIFFERENT between two readings of the same assembly (brief
        /// R23): components and mates that came or went, a total that changed,
        /// and for each component in both readings its file, configuration,
        /// state, fixed, visible, transparent, origin and axes; for each mate in
        /// both its kind, whether it is suppressed and what it holds. With
        /// <paramref name="unreadableCounts"/> a list or a place that could not
        /// be read is a difference, because sameness cannot be seen (Z-C8);
        /// without it only what was seen to change is given, the way
        /// `hasBuilt` wants it (a volume not read does not count as changed).
        /// </summary>
        private static List<string> AssemblyDiffers(PartSnapshot before, PartSnapshot after, bool unreadableCounts)
        {
            var differences = new List<string>();
            if (before == null || after == null) return differences;
            if (before.Assembly != after.Assembly)
            {
                if (unreadableCounts) differences.Add("one reading is of an assembly and the other is not");
                return differences;
            }

            if (!before.ComponentsRead || !after.ComponentsRead)
            {
                if (unreadableCounts)
                    differences.Add("the components cannot be read" + (!before.ComponentsRead ? " before" : "") +
                                    (!after.ComponentsRead ? " after" : ""));
            }
            else
            {
                if (before.ComponentTotal != after.ComponentTotal)
                    differences.Add("components " + before.ComponentTotal + " before, " + after.ComponentTotal + " now");
                var namesBefore = new List<string>();
                foreach (ComponentLook c in before.Components) namesBefore.Add(c.Name);
                var namesAfter = new List<string>();
                foreach (ComponentLook c in after.Components) namesAfter.Add(c.Name);
                var gone = namesBefore.Where(n => !namesAfter.Contains(n)).ToList();
                var come = namesAfter.Where(n => !namesBefore.Contains(n)).ToList();
                if (gone.Count > 0) differences.Add("components no longer there: «" + string.Join("», «", gone) + "»");
                if (come.Count > 0) differences.Add("components that were not there: «" + string.Join("», «", come) + "»");
                foreach (ComponentLook was in before.Components)
                {
                    ComponentLook now = null;
                    foreach (ComponentLook c in after.Components)
                        if (c.Name == was.Name) { now = c; break; }
                    if (now == null) continue;
                    string who = "«" + was.Name + "» ";
                    foreach (var line in new[]
                             {
                                 Changed(who + "file", was.Path, now.Path),
                                 Changed(who + "configuration", was.Configuration, now.Configuration),
                                 Changed(who + "state", was.State, now.State),
                                 Changed(who + "fixed", was.Grounded, now.Grounded),
                                 Changed(who + "visible", was.Visible, now.Visible),
                                 Changed(who + "transparent", was.Transparent, now.Transparent),
                             })
                        if (line != null) differences.Add(line);
                    if (was.Origin == null || now.Origin == null)
                    {
                        if (unreadableCounts)
                            differences.Add(who + "origin cannot be read" + (was.Origin == null ? " before" : "") +
                                            (now.Origin == null ? " after" : ""));
                    }
                    else if (!Near(was.Origin, now.Origin, OriginDeviationMm))
                        differences.Add(who + "moved: origin " + NumbersText(was.Origin) + " mm before, " +
                                        NumbersText(now.Origin) + " mm now");
                    if (was.Axes == null || now.Axes == null)
                    {
                        if (unreadableCounts)
                            differences.Add(who + "axes cannot be read" + (was.Axes == null ? " before" : "") +
                                            (now.Axes == null ? " after" : ""));
                    }
                    else if (!Near(was.Axes, now.Axes, AxesDeviation))
                        differences.Add(who + "turned: axes " + was.AxesText + " before, " + now.AxesText + " now");
                }
            }

            if (!before.MatesRead || !after.MatesRead)
            {
                if (unreadableCounts)
                    differences.Add("the mates cannot be read" + (!before.MatesRead ? " before" : "") +
                                    (!after.MatesRead ? " after" : ""));
            }
            else
            {
                if (before.MateTotal != after.MateTotal)
                    differences.Add("mates " + before.MateTotal + " before, " + after.MateTotal + " now");
                var namesBefore = new List<string>();
                foreach (MateLook m in before.Mates) namesBefore.Add(m.Name);
                var namesAfter = new List<string>();
                foreach (MateLook m in after.Mates) namesAfter.Add(m.Name);
                var gone = namesBefore.Where(n => !namesAfter.Contains(n)).ToList();
                var come = namesAfter.Where(n => !namesBefore.Contains(n)).ToList();
                if (gone.Count > 0) differences.Add("mates no longer there: «" + string.Join("», «", gone) + "»");
                if (come.Count > 0) differences.Add("mates that were not there: «" + string.Join("», «", come) + "»");
                foreach (MateLook was in before.Mates)
                    foreach (MateLook now in after.Mates)
                        if (now.Name == was.Name)
                        {
                            if (was.Look != now.Look)
                                differences.Add("mate «" + was.Name + "»: " + was.Look + " before, " + now.Look + " now");
                            break;
                        }
            }
            return differences;
        }

        /// <summary>
        /// The plan changed the assembly (brief R23): seen to change, not merely
        /// unreadable, the same rule as the volume in `hasBuilt` (Z-C8).
        /// </summary>
        private static bool AssemblyBuilt(PartSnapshot before, PartSnapshot after)
        {
            return before != null && after != null && before.Assembly && after.Assembly &&
                   AssemblyDiffers(before, after, false).Count > 0;
        }

        /// <summary>Numbers as «[0, 15, 0]», in the form of the answers.</summary>
        private static string NumbersText(double[] numbers)
        {
            var words = new List<string>();
            foreach (double n in numbers) words.Add(Number(n));
            return "[" + string.Join(", ", words) + "]";
        }

        /// <summary>The line of a field that changed, null when it did not.</summary>
        private static string Changed(string what, string was, string now)
        {
            if ((was ?? "") == (now ?? "")) return null;
            return what + ": «" + (was ?? "") + "» before, «" + (now ?? "") + "» now";
        }

        private static bool Near(double[] a, double[] b, double tolerance)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > tolerance) return false;
            return true;
        }

        /// <summary>
        /// True when either reading has features beyond the list of the state
        /// (`feature_not_listed`): the names and the order are then compared
        /// only on the listed ones (review F16).
        /// </summary>
        private static bool NamesComparedOnlyInPart(PartSnapshot a, PartSnapshot b)
        {
            return (a != null && a.Unlisted > 0) || (b != null && b.Unlisted > 0);
        }

        /// <summary>What «names and order» covers, for the answers (review F16).</summary>
        internal static string NamesScope(bool partial)
        {
            return partial
                ? "the names and order of the last " + Oce.Solid.Agent.Core.DocumentState.MaxFeature +
                  " features, the ones the state lists (the older ones only by count)"
                : "the feature names and order";
        }

        /// <summary>
        /// What an assembly snapshot covers, for the answers (brief R23): the
        /// state lists a bounded number of components and mates, and beyond
        /// the list they are compared only by count.
        /// </summary>
        internal static string AssemblyScope(bool partial)
        {
            const string what = "the components (which ones, their file, configuration, state, fixed, visible, " +
                                "transparent, origin and axes) and the mates (which ones, their kind, suppressed or not, " +
                                "what they hold)";
            return partial
                ? what + " the state lists (the ones beyond the list only by count)"
                : what;
        }

        /// <summary>
        /// The features of a reading as «name#id», in tree order (Z-C21.14).
        /// </summary>
        private static List<string> FeatureNamesInOrder(Dictionary<string, object> docState)
        {
            var names = new List<string>();
            foreach (var v in EntriesOf(docState, "feature"))
            {
                var entry = v as Dictionary<string, object>;
                if (entry == null) continue;
                string name = Convert.ToString(entry.TryGetValue("uid", out var u) ? u : null) ?? "";
                string id = Convert.ToString(entry.TryGetValue("feature_id", out var i) ? i : null,
                                             CultureInfo.InvariantCulture) ?? "";
                names.Add(id.Length > 0 ? name + "#" + id : name);
            }
            return names;
        }

        /// <summary>
        /// THE ANSWER AFTER A PRESS THAT DID NOT TAKE THE PLAN AWAY (Z-C21.14).
        /// Pure, so that a contract can call it: <paramref name="restored"/> is
        /// true only when a redo was tried, did not throw, the press had taken
        /// away something visible and the part now reads as before pressing.
        /// Never «put back» without that comparison.
        /// </summary>
        internal static string AnswerAfterWrongPress(
            double expectedVolume, int expectedFeatures, double volumeNow, int featuresNow,
            List<string> notAsBeforeThePlan, List<string> takenAway, bool redoTried, string redoError,
            List<string> stillDifferent, bool namesPartial, out bool restored)
        {
            return WrongPressText(
                "part",
                "the volume should be " + Number(expectedVolume) + " mm³ and the features " + expectedFeatures +
                "; they are " + Number(volumeNow) + " mm³ and " + featuresNow,
                "neither the volume, nor the feature count, nor " + NamesScope(namesPartial),
                "volume, feature count and " + NamesScope(namesPartial),
                notAsBeforeThePlan, takenAway, redoTried, redoError, stillDifferent, out restored);
        }

        /// <summary>
        /// <see cref="AnswerAfterWrongPress"/> on an assembly (brief R23): the
        /// counts are of components and mates, and what was compared is
        /// <see cref="AssemblyScope"/>. Pure, so that a contract can call it.
        /// </summary>
        internal static string AnswerAfterWrongPressOnAssembly(
            int expectedComponents, int expectedMates, int componentsNow, int matesNow,
            List<string> notAsBeforeThePlan, List<string> takenAway, bool redoTried, string redoError,
            List<string> stillDifferent, bool listedOnly, out bool restored)
        {
            return WrongPressText(
                "assembly",
                "the components should be " + expectedComponents + " and the mates " + expectedMates +
                "; they are " + componentsNow + " and " + matesNow,
                "none of " + AssemblyScope(listedOnly),
                AssemblyScope(listedOnly),
                notAsBeforeThePlan, takenAway, redoTried, redoError, stillDifferent, out restored);
        }

        /// <summary>The text of both answers after a wrong press, for a part or an assembly.</summary>
        private static string WrongPressText(
            string documentWord, string counts, string changedNeither, string compared,
            List<string> notAsBeforeThePlan, List<string> takenAway, bool redoTried, string redoError,
            List<string> stillDifferent, out bool restored)
        {
            notAsBeforeThePlan = notAsBeforeThePlan ?? new List<string>();
            takenAway = takenAway ?? new List<string>();
            stillDifferent = stillDifferent ?? new List<string> { "the " + documentWord + " could not be read again" };
            restored = redoTried && redoError == null && takenAway.Count > 0 && stillDifferent.Count == 0;
            string the = "the " + documentWord;

            string head =
                "I pressed undo and " + the + " did not go back to how it was before the plan (" + counts +
                (notAsBeforeThePlan.Count > 0 ? "; " + string.Join("; ", notAsBeforeThePlan) : "") +
                "). So the entry on top of the undo stack was not my plan but something done after it that changed " +
                changedNeither + ", which I could not see before pressing.";

            if (!redoTried && takenAway.Count > 0)
                return head + " It took away: " + string.Join("; ", takenAway) + ". I did not press redo, " +
                       "so it is NOT back. Look at " + the + " in SOLIDWORKS before going on.";
            if (!redoTried)
                return head + " Reading " + the + " again, the undo changed nothing I can see (" + compared +
                       " are as before pressing), so I did not press redo: I could not check " +
                       "what it would bring back. Look at " + the + " in SOLIDWORKS before going on; Edit > Redo puts " +
                       "back only an action that was not recorded through the API.";

            if (restored)
                return head + " It took away: " + string.Join("; ", takenAway) + ". I pressed redo and read " +
                       the + " again: " + compared + " are as before pressing, so it came " +
                       "back (only an action not recorded through the API can come back this way). The plan is " +
                       "still there.";

            return head + " It took away: " + string.Join("; ", takenAway) + ". I pressed redo" +
                   (redoError != null ? " and SOLIDWORKS refused: " + redoError : "") +
                   ", and " + the + " is NOT as it was before pressing: " + string.Join("; ", stillDifferent) +
                   ". SOLIDWORKS cannot redo an undo entry recorded through the API (measured on " +
                   "1/10/2026), so what the undo took away is lost. Look at " + the + " in SOLIDWORKS before " +
                   "going on, and make it again with «build» if you need it.";
        }

        /// <summary>A comparison line, only if the two numbers are different.</summary>
        private static string Comparison(string what, double expected, double found, string units)
        {
            if (Math.Abs(expected - found) <= VolumeDeviationMm3) return "";
            return "  " + what + ": was " + Number(expected) +
                   (units.Length > 0 ? " " + units : "") + " when I finished, " +
                   "now it is " + Number(found) +
                   (units.Length > 0 ? " " + units : "") + "\n";
        }

        private static string Number(double v)
        {
            return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A number from the state, or **null if the state says it does not know**
        /// (entry Z-C8): the key is there and is null, that is the reading failed.
        /// ZERO-E-ZERO: an ABSENT key is worth zero — the state does not write the
        /// volume of a part without solid bodies, nor of a drawing or of an
        /// assembly, and there zero is true. Until 25/9/2026 absent also meant
        /// «not read», and a correct undo could be redone.
        /// </summary>
        private static double? NumberOf(Dictionary<string, object> docState, string key)
        {
            if (docState == null) return null;
            object v;
            if (!docState.TryGetValue(key, out v)) return 0;
            if (v == null) return null;
            try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        /// <summary>
        /// How many features the part has, including those the ceiling
        /// left out.
        ///
        /// WITHOUT `feature_not_listed` THIS COUNT WOULD LIE on every
        /// part with more than eighty features: it would stop at eighty and
        /// say «nothing has changed» even after ten new holes. It is
        /// exactly the defect of entry E12, repeated on this side.
        /// </summary>
        private static int HowManyFeatures(Dictionary<string, object> docState)
        {
            if (docState == null) return 0;
            int howMany = 0;
            foreach (var v in EntriesOf(docState, "feature")) { if (v != null) howMany++; }
            object outside;
            if (docState.TryGetValue("feature_not_listed", out outside) && outside != null)
                try { howMany += Convert.ToInt32(outside); } catch { }
            return howMany;
        }

        // ─────────────────────────────────────────────────────────────────

        private static object Volume(Dictionary<string, object> docState)
        {
            if (docState == null) return null;
            return docState.TryGetValue("volume_mm3", out var v) ? v : null;
        }

        /// <summary>
        /// THE IDENTIFIERS THE STEP CITES THAT WERE NOT IN THE STATE.
        /// Null if all is well.
        ///
        /// Only the fields that the schema calls `*_uid` are looked at: they are those
        /// that name geometry **that was already there**. The geometry created by the
        /// plan itself is named with `*_ref`, which cites the STEP and not
        /// the identifier — and it is the only way, because at the time the
        /// plan is written that face does not exist and nobody can give it a
        /// name.
        ///
        /// WHY THE SIGNATURES WERE NOT ENOUGH. The signature compares an identifier
        /// with the geometry it described when the plan was born. If the face
        /// **did not exist at all**, there is no signature: nothing to
        /// compare, no refusal, and the plan passes. The signature protects
        /// against displacement; this protects against invention.
        ///
        /// THE LEGITIMATE IS NOT REFUSED. A plan that creates geometry and then
        /// works on it is normal, and passes: it uses `*_ref`. The message says so,
        /// because a refusal that does not point to the good way only teaches
        /// to retry at random.
        /// </summary>
        private static string UidsThatDoNotExist(
            Dictionary<string, object> schema,
            Dictionary<string, object> arguments,
            Dictionary<string, object> docState)
        {
            if (schema == null || arguments == null || docState == null) return null;
            object rawProp;
            if (!schema.TryGetValue("properties", out rawProp)) return null;
            var property = rawProp as Dictionary<string, object>;
            if (property == null) return null;

            var existing = StateUid(docState);

            foreach (var theField in property.Keys)
            {
                if (!theField.EndsWith("_uid", StringComparison.Ordinal)) continue;
                object val;
                if (!arguments.TryGetValue(theField, out val) || val == null) continue;

                foreach (var uid in Strings(val))
                {
                    if (existing.Contains(uid)) continue;
                    // A FACE THE STATE DID NOT LIST EXISTS ALL THE SAME: every face
                    // has the identifier of its rank by area, and the state says how
                    // many there are (`faces_total`). Whether it is the face the plan
                    // meant is the business of the signature and of the executor.
                    if (IsFaceBeyondTheList(uid, docState)) continue;
                    // And an edge beyond the thirty, by its rank (brief R17).
                    if (IsEdgeBeyondTheList(uid, docState)) continue;

                    // WE SAY WHAT IS THERE, not just what is missing. On an empty
                    // document the list is empty, and that is precisely the case in
                    // which the reader must understand that they must change path.
                    string whatIsThere = existing.Count == 0
                        ? "In the part I looked at there was no identifier at all: " +
                          "it was empty."
                        : "The identifiers that existed are: " + List(existing) + ".";

                    return
                        "«" + theField + "» cites «" + uid + "», which was not in the part I " +
                        "looked at. " + whatIsThere + "\n\n" +
                        "Identifiers are born from «inspect_part» and hold for " +
                        "THAT reading. If the geometry is created by an earlier step of " +
                        "this same plan, it has no identifier yet and must be " +
                        "named with the reference to the step — for example " +
                        "\"" + theField.Replace("_uid", "_ref") + "\": {\"step\": 1, " +
                        "\"kind\": \"top_face\"}. If instead it was already there, " +
                        "read the part again and copy the identifier from there.";
                }
            }
            return null;
        }

        /// <summary>
        /// All the names that that state really SHOWED: the uids of faces,
        /// edges, bodies, axes and features, plus the feature names that the
        /// state writes around by other ways.
        ///
        /// THE RULE: WE COLLECT WHAT THE STATE WROTE, NOT WHAT IT
        /// HAD IN MIND TO WRITE.
        ///
        /// This check blocks a plan BEFORE executing it when it cites a
        /// name that was not there. If it collects fewer names than the state
        /// showed, it refuses legitimate work — and refuses it in the worst way,
        /// because whoever wrote the plan read that name in the previous
        /// answer and is right to trust it.
        ///
        /// Up to 0.25.2 it looked only at the `uid` field of the five families.
        /// But the state publishes feature names in three other places too:
        ///
        ///   * `from_feature` on every face — and it is put there on purpose for
        ///     «take that hole to M10»;
        ///   * `created_by` on every body;
        ///   * the `features` map at the bottom.
        ///
        /// And the `feature` list instead is FILTERED by type and cut at
        /// `MaxFeature`. So a part with more than eighty features
        /// was enough, or a feature of a type that does not enter the list,
        /// for the state to show a name and the check to declare it
        /// nonexistent.
        ///
        /// IT WIDENS AND DOES NOT NARROW. The set is single and does not distinguish the
        /// families: `face_uid` also accepts a feature name. It is
        /// deliberate — this is a check of EXISTENCE, not of type, and whoever
        /// gets the family wrong is stopped later with a message that can say what
        /// type that entity is (`IDoNotWorkOn`). Narrowing here would mean
        /// risking a wrong refusal to gain a slightly better
        /// message.
        /// </summary>
        /// <summary>
        /// Whether an identifier names a face that exists but was not listed:
        /// «F90» on a part whose state says `faces_total` 177. The face is
        /// numbered, not described (W3.1-bis.12).
        /// </summary>
        private static bool IsFaceBeyondTheList(string uid, Dictionary<string, object> docState)
        {
            int rank = FaceFilter.RankOf(uid);
            if (rank < 1 || docState == null) return false;
            object total;
            if (!docState.TryGetValue("faces_total", out total) || total == null) return false;
            try { return rank <= Convert.ToInt32(total, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Whether an identifier names an edge that exists but was not listed:
        /// «E45» on a part whose state says `edges_total` 120 (brief R17).
        /// </summary>
        private static bool IsEdgeBeyondTheList(string uid, Dictionary<string, object> docState)
        {
            int rank = Oce.Solid.Agent.Core.EdgeFilter.RankOf(uid);
            if (rank < 1 || docState == null) return false;
            object total;
            if (!docState.TryGetValue("edges_total", out total) || total == null) return false;
            try { return rank <= Convert.ToInt32(total, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception) { return false; }
        }

        private static HashSet<string> StateUid(Dictionary<string, object> docState)
        {
            var outside = new HashSet<string>(StringComparer.Ordinal);

            // 1. The uids proper.
            foreach (var family in new[] { "faces", "edges", "bodies", "axes", "feature" })
                foreach (var entry in EntriesOf(docState, family))
                {
                    object uid;
                    if (entry.TryGetValue("uid", out uid) && uid != null)
                        outside.Add(Convert.ToString(uid));
                }

            // 2. The feature names written on the faces and on the bodies.
            foreach (var entry in EntriesOf(docState, "faces"))
            {
                object itemName;
                if (entry.TryGetValue("from_feature", out itemName) && itemName != null)
                    outside.Add(Convert.ToString(itemName));
            }
            foreach (var entry in EntriesOf(docState, "bodies"))
            {
                object itemName;
                if (entry.TryGetValue("created_by", out itemName) && itemName != null)
                    outside.Add(Convert.ToString(itemName));
            }

            // 3. The features map: the KEYS are the numbers, the VALUES
            //    are the names. Whoever writes a plan copies the name, not the number.
            object raw;
            if (docState.TryGetValue("features", out raw))
            {
                var map = raw as Dictionary<string, object>;
                if (map != null)
                    foreach (var v in map.Values)
                        if (v != null) outside.Add(Convert.ToString(v));
            }

            outside.Remove("");
            return outside;
        }

        /// <summary>
        /// The entries of a family of the state, as dictionaries.
        ///
        /// The families are `List&lt;Dictionary&lt;...&gt;&gt;`, not `object[]`: an
        /// `as object[]` here would return null in silence and the check would say
        /// «does not exist» about everything. It has already happened once, with the signatures.
        /// </summary>
        private static IEnumerable<Dictionary<string, object>> EntriesOf(
            Dictionary<string, object> docState, string family)
        {
            object raw;
            if (!docState.TryGetValue(family, out raw) || raw == null) yield break;
            var list = raw as System.Collections.IEnumerable;
            if (list == null) yield break;
            foreach (var v in list)
            {
                var entry = v as Dictionary<string, object>;
                if (entry != null) yield return entry;
            }
        }

        /// <summary>A value that may be a string or a list of strings.</summary>
        private static IEnumerable<string> Strings(object val)
        {
            if (val is string s) { if (s.Length > 0) yield return s; yield break; }
            if (val is System.Collections.IEnumerable list)
            {
                foreach (var v in list)
                {
                    var t = Convert.ToString(v);
                    if (!string.IsNullOrEmpty(t)) yield return t;
                }
            }
        }

        /// <summary>
        /// The list, but short. On a part with four hundred faces writing them
        /// all helps nobody: the first twelve are enough to show what
        /// shape the names have.
        /// </summary>
        private static string List(HashSet<string> uid)
        {
            var sortedItems = uid.ToList();
            sortedItems.Sort(StringComparer.Ordinal);
            if (sortedItems.Count <= 12) return string.Join(", ", sortedItems);
            return string.Join(", ", sortedItems.Take(12)) +
                   " and another " + (sortedItems.Count - 12);
        }

        /// <summary>
        /// The name of a feature, from the map the state writes at the
        /// bottom. Null if it is not there: better a signature without a name than a signature
        /// with an invented name.
        /// </summary>
        private static string FeatureName(Dictionary<string, object> docState, object id)
        {
            if (docState == null || id == null) return null;
            if (!docState.TryGetValue("features", out var g) || g == null) return null;
            var map = g as Dictionary<string, object>;
            if (map == null) return null;
            var key = Convert.ToString(id);
            return map.TryGetValue(key, out var itemName) && itemName != null
                ? Convert.ToString(itemName)
                : null;
        }

        /// <summary>
        /// The part's box — {minX,minY,minZ,maxX,maxY,maxZ} in mm —
        /// derived from the two fields the state publishes: the corner
        /// (`bounding_box_from_mm`) and the sides (`bounding_box_mm`).
        ///
        /// Null if either is missing. The corner is NOT invented by setting it to
        /// zero: a state that declared a box resting on the origin
        /// when the part is centred on it would make people write «it was halfway
        /// along the side» of a face that was at the extreme, and a wrong
        /// explanation is worse than no explanation.
        /// </summary>
        private static double[] PartBox(Dictionary<string, object> docState)
        {
            double[] da = ThreeNumbers(docState, "bounding_box_from_mm");
            double[] sides = ThreeNumbers(docState, "bounding_box_mm");
            if (da == null || sides == null) return null;
            return new[]
            {
                da[0], da[1], da[2],
                da[0] + sides[0], da[1] + sides[1], da[2] + sides[2],
            };
        }

        /// <summary>Three numbers from a field of the state, or null.</summary>
        private static double[] ThreeNumbers(Dictionary<string, object> docState, string key)
        {
            if (docState == null || !docState.TryGetValue(key, out var v) || v == null) return null;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return null;
            var outside = new List<double>();
            foreach (var x in list)
            {
                try
                {
                    outside.Add(Convert.ToDouble(
                        x, System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { return null; }
            }
            return outside.Count == 3 ? outside.ToArray() : null;
        }

        /// <summary>
        /// How many features in error that state had. Zero even when
        /// the field is not there — and it is right: the state writes it only when there
        /// are some.
        /// </summary>
        private static int FeaturesInError(Dictionary<string, object> docState)
        {
            if (docState == null) return 0;
            object v;
            if (!docState.TryGetValue("features_in_error", out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        /// <summary>
        /// The arguments of the operation whose name is near an unknown one
        /// (brief R8: «value» for «value_text»): one that begins with it and an
        /// underscore, one it begins with and an underscore, or one at most two
        /// letters away when both have four letters or more. Said in the refusal,
        /// never taken in its place.
        /// </summary>
        internal static List<string> NearArguments(string given, IEnumerable<string> names)
        {
            var near = new List<string>();
            if (string.IsNullOrEmpty(given) || names == null) return near;
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name) || name == given) continue;
                bool close = name.StartsWith(given + "_", StringComparison.Ordinal) ||
                             given.StartsWith(name + "_", StringComparison.Ordinal) ||
                             (given.Length >= 4 && name.Length >= 4 && LettersApart(given, name) <= 2);
                if (close) near.Add(name);
            }
            return near;
        }

        /// <summary>The edit distance of two names (insert, delete, change one letter).</summary>
        private static int LettersApart(string a, string b)
        {
            var row = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) row[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int diagonal = row[0];
                row[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int above = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                    diagonal = above;
                }
            }
            return row[b.Length];
        }

        private static object Take(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            return d.TryGetValue(key, out var v) ? v : null;
        }

        private static string Text(Dictionary<string, object> d, string key)
        {
            var v = Take(d, key);
            return v == null ? null : Convert.ToString(v);
        }


        private static Dictionary<string, object> Object(
            Dictionary<string, object> property, string[] mandatory)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = property,
                ["required"] = mandatory,
            };
        }

        /// <summary>
        /// A tool, with its ANNOTATIONS.
        ///
        /// WHY THE ANNOTATIONS ARE NOT A PROTOCOL DETAIL.
        /// The MCP client uses them to decide whether to stop and ask the person
        /// working for confirmation. Without them, `inspect_part` — which touches nothing —
        /// and `build` — which modifies the part in a way nobody undoes for
        /// them — reach the client identical, and the client treats as identical two
        /// things that are not.
        ///
        /// For a project whose thesis is «no geometry exists unless
        /// someone has confirmed it», declaring them is the minimum.
        ///
        /// `idempotent` deserves a line: `build` is NOT. Running the
        /// same plan again does not put the part back as it was — it makes a SECOND hole.
        /// Declaring it idempotent would invite a client to retry by itself
        /// after a network error, and the part would come out with double the
        /// features without anyone having asked.
        /// </summary>
        private static Dictionary<string, object> Tool(
            string itemName,
            string descriptionText,
            Dictionary<string, object> schema,
            bool readOnlyMode,
            bool destructive,
            bool idempotent)
        {
            return new Dictionary<string, object>
            {
                ["name"] = itemName,
                // THE TITLE IS IN TWO PLACES, AND IT IS NOT A REPETITION.
                //
                // In the recent revisions of the MCP spec `title` is a
                // TOP-LEVEL field of the tool; before, it was only inside
                // `annotations`, where it stays for compatibility. A new client
                // reads the one above, an old one the one below, and
                // writing only one means that half of the clients show the
                // technical name `inspect_part` to the person working.
                // Writing both costs two lines and no choice.
                ["title"] = HumanTitle(itemName),
                ["description"] = descriptionText,
                ["inputSchema"] = schema,
                ["annotations"] = new Dictionary<string, object>
                {
                    ["title"] = HumanTitle(itemName),
                    ["readOnlyHint"] = readOnlyMode,
                    ["destructiveHint"] = destructive,
                    ["idempotentHint"] = idempotent,
                    // The outside world here is SolidWorks: a program that we do not
                    // control, with documents that change under our feet.
                    ["openWorldHint"] = true,
                },
            };
        }

        /// <summary>
        /// The names of the tools, for the «I do not know it» message.
        ///
        /// Written by hand and not derived from `Items()`: building the real list
        /// to answer an error would mean rebuilding sixty JSON schemas
        /// to print six words. The price of having written them twice
        /// is that they can diverge — and indeed there is a contract check
        /// that compares this list with the one coming out of `tools/list`.
        /// </summary>
        internal static readonly string[] ToolNames =
        {
            "inspect_part", "list_operations", "describe_operation",
            "build", "measure", "undo", "document",
            "api_search", "api_signature", "mesh",
        };

        private static string HumanTitle(string itemName)
        {
            switch (itemName)
            {
                case "inspect_part": return "Inspect the open part";
                case "list_operations": return "List the operations";
                case "describe_operation": return "Describe an operation";
                case "build": return "Build in the part";
                case "measure": return "Measure the part, without touching it";
                case "undo": return "Withdraw the last plan";
                case "document": return "Open, create or pick the document";
                case "api_search": return "Search the SOLIDWORKS API";
                case "api_signature": return "Signature and knowledge of an API member";
                case "mesh": return "Measure or cut an STL file";
                default: return itemName;
            }
        }
    }
}
