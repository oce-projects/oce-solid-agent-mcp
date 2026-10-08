using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oce.Solid.Agent.Mcp;
using Oce.Solid.Agent.Bridge;

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
                    "finished — not at every reading.",
                    Object(
                        new Dictionary<string, object>
                        {
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
                                    "checks, exports, identity, sheet_metal, dimensions",
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
                    "came to the front in between, it stops before writing. " +
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
                    "you for confirmation.\n\n" +
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
            // had been looked at and its signatures are thrown away TOGETHER. «build» at
            // this point answers «I have not looked at any part yet», which
            // is the truth.
            _seen = null;
            _signatureSeen = null;
            _seenSignatures = null;

            card["next_step"] =
                "In front now is «" + Convert.ToString(card["document_title"]) + "». " +
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
                    return Protocol.Content(
                        "I do not know the tool «" + itemName + "». The tools are " +
                        "these: " + string.Join(", ", ToolNames) + ". " +
                        "The sixty drawing OPERATIONS are not tools: " +
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
            var docState = _session.State(out var error);
            if (docState == null) return Protocol.Content(error, true);
            _seen = StateIdentity(docState);
            _signatureSeen = LightSignature(docState);
            _seenSignatures = StateSignatures(docState);
            RemoveAnchors(docState);

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
                                         ? " a " + offset.ToString("0.##",
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
                   theField("how_many_features") + "|" + theField("document_title");
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

            // THE PROGRESS OF THE DRY RUN (entry W2.2). Nothing is executed
            // here, so the total is the number of steps and that is all:
            // there is no second half to wait for.
            _wire.Total = steps.Length;

            for (int i = 0; i < steps.Length; i++)
            {
                var planStep = steps[i] as Dictionary<string, object>;
                string location = "step " + (i + 1);
                _wire.Step(i + 1, "checking " + location + " of " + steps.Length);
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
                    foreach (var theField in data.Keys)
                    {
                        if (property.ContainsKey(theField)) continue;
                        // AN UNKNOWN ARGUMENT IS NOT A DETAIL: it is
                        // ignored in silence, and the part comes out different from
                        // how it was asked for, without any error.
                        trouble.Add(location + " («" + itemName + "»): «" + theField +
                                 "» is not an argument of this operation, " +
                                 "and would be IGNORED");
                    }

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
        private static readonly string[] CannotBeUndone = { "delete_feature" };

        private Dictionary<string, object> RunPlan(
            Dictionary<string, object> arguments, bool checkIfRepeated)
        {
            var steps = Take(arguments, "steps") as object[];
            if (steps == null || steps.Length == 0)
                return Protocol.Content(
                    "The plan is empty: «steps» must be a list of " +
                    "{operation, arguments}.", true);

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
                            return Protocol.Content(
                                "Stopped: the confirmation did not arrive, and I " +
                                "touched nothing. " + question, true);
                    }
                    else
                    {
                        // THE FALLBACK IS DECLARED (entry W2.5). Going on
                        // in silence would be the way a defence
                        // disappears without anyone noticing.
                        Protocol.Note(
                            "[bridge] " + string.Join(", ", toConfirm) +
                            ": I could not ask the user for confirmation — " +
                            "this client does not declare «elicitation». I am going " +
                            "ahead as before.");
                    }
                }
            }

            if (!_session.Attached)
                return Protocol.Content("The bridge is not attached to SolidWorks.", true);

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
                    return Protocol.Content(
                        "I have not looked at any part yet, so I do not know which " +
                        "document you want me to draw on: in front now is «" +
                        ReadableName(now) + "». Call «inspect_part» first — " +
                        "it is needed anyway, because the face identifiers " +
                        "are born there.", true);

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

            if (theCheckedOne)
            {
                var changed = WhatChanged(_signatureSeen, LightSignature(before));
                if (changed != null)
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
                    "is not repeatable — running it again does not put the part back as it was, " +
                    "it would make a SECOND hole.\n\n" +
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
                // AND THE FORMA REWRITES `its` IN PLACE (Z-C21.10, S20): an
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
                });
            }

            // THE RUN IS THE LIBRARY'S, NOT OUR OWN.
            // From here come the references between steps («the face on top of
            // what you did at step 1») and the single undo entry:
            // one Ctrl+Z undoes the whole plan.
            // What the executor said about its document and its undo entry
            // (Z-C21.14). Stays null when the plan never reached it: refused
            // above, before anything was written.
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
            if (after != null && frontIsThePlans)
            {
                _seenSignatures = StateSignatures(after);
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

            var result = new Dictionary<string, object>
            {
                // WE ALWAYS SAY WHAT WAS WORKED ON. Even when it is obvious:
                // it is the only way for the reader to notice when obvious
                // was not.
                ["document"] = ReadableName(now),
                ["outcomes"] = outcomes,
                ["state_after"] = after,
                ["note_state"] = after == null ? stateError : null,
                ["volume_before_mm3"] = Volume(before),
                // On the plan's document, like the line above (Z-C21.14).
                ["volume_after_mm3"] = Volume(planStateAfter),
            };

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
                          (errorsAfter - errorsBefore) + ". Look at «state_after» to see which."
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
            // work.
            bool hasBuilt =
                planStateAfter != null &&
                (featAfter != featBefore ||
                 (volBefore.HasValue && volAfter.HasValue &&
                  Math.Abs(volAfter.Value - volBefore.Value) > VolumeDeviationMm3));
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
                !checkIfRepeated, ReadableName(planIdentity), out memoryReason);

            switch (memoryAction)
            {
                case MemoryNew:
                    _lastPlan = new LastPlan
                    {
                        // The plan document as it is called NOW (Z-C21.14): a
                        // plan that saves its part changes its title and path.
                        Document = planIdentity,
                        DocumentRef = planDocumentHeld,
                        Before = snapshotBefore,
                        After = snapshotAfter,
                    };
                    _noMemoryBecause = null;
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
        /// </summary>
        private sealed class PartSnapshot
        {
            public double? Volume;
            public int Count;
            public List<string> Names;
            public int Unlisted;

            public static PartSnapshot Of(Dictionary<string, object> docState)
            {
                if (docState == null) return null;
                int unlisted = 0;
                if (docState.TryGetValue("feature_not_listed", out var u) && u != null)
                    try { unlisted = Convert.ToInt32(u, CultureInfo.InvariantCulture); } catch { }
                return new PartSnapshot
                {
                    Volume = NumberOf(docState, "volume_mm3"),
                    Count = HowManyFeatures(docState),
                    Names = FeatureNamesInOrder(docState),
                    Unlisted = unlisted,
                };
            }
        }

        private LastPlan _lastPlan;

        /// <summary>
        /// Z-C21.14: why there is no memory, when a later plan or an
        /// undo dropped it, in English, for the refusal; null otherwise.
        /// </summary>
        private string _noMemoryBecause;

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
        ///  4. the plan built (volume or feature count changed): new;
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
            bool measure, string planDocumentName, out string reason)
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
                ? what + " changed something I can see on" + on + " (the feature names or their order) while " +
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
                    "«build» of this conversation, and here I have not yet " +
                    "run any that built something.\n\n" +
                    "I do not press undo blindly: I would remove something I did not " +
                    "do myself, and I could not even say what." +
                    (_noMemoryBecause != null ? "\n\n" + _noMemoryBecause : "") +
                    "\n\n" + NoMemoryRemedy, true);

            // ── SOMETHING IS KNOWN TO BE ON TOP: REFUSE BEFORE PRESSING ──
            // (Z-C21.14). No reading, no press: the memory says the remembered
            // plan is not on top of the undo stack, and a press would take away
            // what is.
            if (memory.CoveredBecause != null)
                return RefusalCovered(memory);

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

            // ── SI PREME ─────────────────────────────────────────────────
            var undoError = _session.StepBack();
            if (undoError != null)
                return Protocol.Content(undoError, true);

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
                    ? new List<string> { "the part could not be read again" }
                    : WhatDiffers(stateBefore, readAgain);

                bool restored;
                var answerText = AnswerAfterWrongPress(
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
            _lastPlan = null;
            _noMemoryBecause =
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
            var outside = new Dictionary<string, object>
            {
                ["undone"] = true,
                ["document"] = ReadableName(identityNow),
                ["volume_before_mm3"] = Math.Round(now.Volume.Value, 2),
                ["volume_after_mm3"] = Math.Round(back.Volume.Value, 2),
                ["features_before"] = now.Count,
                ["features_after"] = back.Count,
                ["checked_by_reading"] = checkedByReading,
                ["note"] =
                    "The plan was withdrawn whole and the part went " +
                    "back to how it was. The identifiers in «state_after» are " +
                    "the valid ones now: the earlier ones are no longer valid.",
                ["state_after"] = stateAfter,
            };
            if (!checkedByReading)
                outside["note_checked"] =
                    "This plan changed nothing I compare (volume, feature count, feature names and order), so reading " +
                    "the part cannot show that it is gone: SOLIDWORKS took away the entry on top, and the last entry I " +
                    "know of on this document was this plan's (its «undo_entry_recorded» was true, and nothing I " +
                    "compare changed since).";
            else if (namesPartial)
                outside["note_checked"] =
                    "The part reads as before the plan by volume and feature count, and by " + NamesScope(true) + ".";
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
                ["reason"] = RefusalChangedSinceThePlanText(changed, namesPartial),
            };
            return Protocol.ContentWithData(Protocol.InJson(outside), outside, true);
        }

        /// <summary>The text of <see cref="RefusalChangedSinceThePlan"/>, pure.</summary>
        internal static string RefusalChangedSinceThePlanText(List<string> changed, bool namesPartial)
        {
            return "I am not pressing undo, and nothing was touched. After my plan the part changed, and I did not " +
                   "change it: " + string.Join("; ", changed ?? new List<string>()) + "." +
                   (namesPartial ? " (I compared " + NamesScope(true) + ".)" : "") +
                   " One step back now would not withdraw my plan: it would take away the last thing done, which is " +
                   "someone else's (another session, another bridge process or the person at the screen), and " +
                   "SOLIDWORKS cannot redo an undo entry recorded through the API (measured on 1/10/2026).\n\n" +
                   WithdrawInsteadRemedy;
        }

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
            return differences;
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
            notAsBeforeThePlan = notAsBeforeThePlan ?? new List<string>();
            takenAway = takenAway ?? new List<string>();
            stillDifferent = stillDifferent ?? new List<string> { "the part could not be read again" };
            restored = redoTried && redoError == null && takenAway.Count > 0 && stillDifferent.Count == 0;
            string scope = NamesScope(namesPartial);

            string head =
                "I pressed undo and the part did not go back to how it was before the plan (the volume should be " +
                Number(expectedVolume) + " mm³ and the features " + expectedFeatures + "; they are " +
                Number(volumeNow) + " mm³ and " + featuresNow +
                (notAsBeforeThePlan.Count > 0 ? "; " + string.Join("; ", notAsBeforeThePlan) : "") +
                "). So the entry on top of the undo stack was not my plan but something done after it that changed " +
                "neither the volume, nor the feature count, nor " + scope + ", which I could not see before pressing.";

            if (!redoTried && takenAway.Count > 0)
                return head + " It took away: " + string.Join("; ", takenAway) + ". I did not press redo, " +
                       "so it is NOT back. Look at the part in SOLIDWORKS before going on.";
            if (!redoTried)
                return head + " Reading the part again, the undo changed nothing I can see (volume, feature " +
                       "count and " + scope + " are as before pressing), so I did not press redo: I could not check " +
                       "what it would bring back. Look at the part in SOLIDWORKS before going on; Edit > Redo puts " +
                       "back only an action that was not recorded through the API.";

            if (restored)
                return head + " It took away: " + string.Join("; ", takenAway) + ". I pressed redo and read " +
                       "the part again: volume, feature count and " + scope + " are as before pressing, so it came " +
                       "back (only an action not recorded through the API can come back this way). The plan is " +
                       "still there.";

            return head + " It took away: " + string.Join("; ", takenAway) + ". I pressed redo" +
                   (redoError != null ? " and SOLIDWORKS refused: " + redoError : "") +
                   ", and the part is NOT as it was before pressing: " + string.Join("; ", stillDifferent) +
                   ". SOLIDWORKS cannot redo an undo entry recorded through the API (measured on " +
                   "1/10/2026), so what the undo took away is lost. Look at the part in SOLIDWORKS before " +
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
            "api_search", "api_signature",
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
                default: return itemName;
            }
        }
    }
}
