using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE BILL OF MATERIALS AND THE BALLOONING IN THE DRAWING (entry GT9).
    ///
    /// The operation is named for the balloons with the item numbers, in the workshop's English
    /// (decided on 26/9/2026): the operation is named after it.
    ///
    /// `IView.InsertBomTable4(UseAnchorPoint, X, Y, AnchorType, BomType,
    /// Configuration, TableTemplate, Hidden, IndentedNumberingType,
    /// DetailedCutList)` lives on the VIEW; `IDrawingDoc.AutoBalloon5` takes an
    /// `AutoBalloonOptions` built with `CreateAutoBalloonOptions` (the form with
    /// the object: a position error among nine arguments gives no exception, it gives
    /// a different drawing).
    ///
    /// ── THE COMPARISON ──────────────────────────────────────────────────
    ///
    /// The bill of materials says which components and how many. The assembly knows it on its own
    /// (`IAssemblyDoc.GetComponents(true)`, the first level, grouped by file
    /// and configuration): **two readings of the same fact**, and if they do not match
    /// it is said. The balloons are counted AFTER, on the view.
    ///
    /// Types of bill of materials (swBomType_e starts from 1, unlike other enums):
    /// «top_level» (TopLevelOnly = 2, default), «parts_only» (1),
    /// «indented» (3). They are chosen by name: an integer passed from outside is the
    /// perfect way to put the wrong type without an error.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome DrawingBom(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed("The active document is not a drawing: the bill of materials goes on a view of an assembly.");
            var names = ViewNames(drawing);
            if (names.Count == 0) return Failed("The drawing has no views: first put the views of the assembly with «standard_views».");
            string reason;
            var view = ResolveView(drawing, names, Str(a, "view", null), out reason);
            if (reason != null) return Failed(reason);
            if (view == null) return Failed("I cannot get the view.");

            var assemblyDoc = view.ReferencedDocument as IAssemblyDoc;
            if (assemblyDoc == null)
                return Failed("Behind the view «" + (view.GetName2() ?? "") + "» there is no assembly: the bill of materials " +
                               "is the list of the components of an assembly. For a weldment use «drawing_cut_list».");

            string requestedType = (Str(a, "variant", "top_level") ?? "top_level").Trim().ToLowerInvariant();
            int bomType;
            switch (requestedType)
            {
                case "top_level": bomType = (int)swBomType_e.swBomType_TopLevelOnly; break;
                case "parts_only": bomType = (int)swBomType_e.swBomType_PartsOnly; break;
                case "indented": bomType = (int)swBomType_e.swBomType_Indented; break;
                default:
                    return Failed("«variant» is one of «top_level» (the default), «parts_only», «indented», not «" + requestedType + "».");
            }

            // ── THE ASSEMBLY, READ FIRST, IN THE CONFIGURATION OF THE VIEW ───
            //
            // Z-C21.4, finding L16-2. The table is inserted for the view's own
            // configuration (IView.ReferencedConfiguration, passed to
            // InsertBomTable4 below), while IAssemblyDoc~GetComponents.html
            // «Gets all of the components in the active configuration of this
            // assembly». Up to library 0.145.0 the expected items came from the
            // ACTIVE configuration, so a table on another configuration was
            // compared with another assembly: a false refusal when the two
            // suppress different components, a false «they match» when the
            // differences cancel out. ComponentsInViewConfiguration reads them
            // in the view's configuration and puts the active one back.
            string configuration = "";
            try { configuration = view.ReferencedConfiguration ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(configuration))
                return Failed(
                    "I could not read which configuration of the assembly the view «" + (view.GetName2() ?? "") +
                    "» shows. The table is made for that configuration, and without its name the assembly could " +
                    "be read in another one. No table was inserted.");
            string activeAfterReading, readProblem;
            var expectations = ComponentsInViewConfiguration(assemblyDoc, bomType, configuration,
                                                      out activeAfterReading, out readProblem);
            if (readProblem != null) return Failed(readProblem);
            if (expectations == null) return Failed("I cannot read the components of the assembly: without them, I cannot check the bill of materials.");
            if (expectations.Count == 0)
                return Failed(
                    "In the configuration «" + configuration + "» of the view the assembly has no component for the " +
                    "BOM (every one is suppressed or excluded from the BOM): the table would be empty.");

            string tableModel = Str(a, "template", null);
            if (string.IsNullOrWhiteSpace(tableModel))
                tableModel = FirstTableModel((int)swUserPreferenceStringValue_e.swFileLocationsBOMTemplates, ".sldbomtbt");

            int tablesBefore = AllDrawingTables(drawing);
            IBomTableAnnotation bom = null;
            try
            {
                // THE CONFIGURATION IS NAMED FOR EVERY TYPE, THE TOP-LEVEL ONE
                // INCLUDED, AND THAT IS STILL OPEN (Z-C21.4, finding L16-4).
                // IView~InsertBomTable4.html: «If BomType is
                // swBomType_TopLevelOnly, then do not specify Configuration.
                // Instead, use IBomFeature::GetConfigurations ... and
                // IBomFeature::SetConfigurations», and «If the BOM table is a
                // parts-only or indented-style BOM and the Configuration
                // specified is invalid, then the BOM is not created». Measured so
                // far only with the active configuration named (the Z-C21.3 probe
                // of envelopes and BOM children, 28/9/2026: ten tables, top-level and
                // parts-only, on «Default», the active configuration: every one
                // was created, none came back null). A
                // top-level table on a configuration that is NOT the active one
                // is step 5 of the bench case the_bom_follows_the_view_configuration:
                // if it is made for another configuration, the comparison below
                // refuses it, and this branch moves to the empty name plus
                // SetConfigurations.
                bom = view.InsertBomTable4(false, 0.02, 0.28, (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft,
                                            bomType, configuration, tableModel ?? "", false,
                                            (int)swNumberingType_e.swNumberingType_Detailed, false) as IBomTableAnnotation;
            }
            catch (Exception ex) { return Failed("The bill of materials was not created: " + ex.Message); }
            var table = bom as ITableAnnotation;
            if (bom == null || table == null)
                return Failed("SOLIDWORKS did not create the bill of materials and did not say why (table template «" +
                               (tableModel ?? "") + "»).");

            // ── THE ROWS, READ ──────────────────────────────────────────────
            int lines = 0, columns = 0;
            try { lines = table.RowCount; columns = table.ColumnCount; } catch { }
            int withQuantity = -1, withNumber = -1;
            var headers = new List<string>();
            for (int c = 0; c < columns; c++)
            {
                headers.Add(Cell(table, 0, c));
                int t = -1;
                try { t = table.GetColumnType2(c, false); } catch { }
                if (t == (int)swTableColumnTypes_e.swBomTableColumnType_Quantity && withQuantity < 0) withQuantity = c;
                if (t == (int)swTableColumnTypes_e.swBomTableColumnType_ItemNumber && withNumber < 0) withNumber = c;
            }
            var readLines = new List<object>();
            int totalQuantity = 0; bool quantityRead = withQuantity >= 0;
            for (int r = 1; r < lines; r++)
            {
                var cells = new List<string>();
                for (int c = 0; c < columns; c++) cells.Add(Cell(table, r, c));
                readLines.Add(cells.ToArray());
                if (withQuantity >= 0)
                {
                    int q;
                    if (int.TryParse(Cell(table, r, withQuantity).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out q)) totalQuantity += q;
                    else quantityRead = false;
                }
            }
            int entries = lines - 1;
            int instances = expectations.Values.Sum();
            var measurements = new Dictionary<string, object>
            {
                ["view"] = view.GetName2(),
                ["kind"] = requestedType,
                ["rows"] = lines,
                ["entries"] = entries,
                ["headers"] = headers.ToArray(),
                ["rows_read"] = readLines.ToArray(),
                ["quantity_in_table"] = quantityRead ? (object)totalQuantity : null,
                ["components_distinct_in_assembly"] = expectations.Count,
                ["instances_in_assembly"] = instances,
                // M23: for an indented table the items are the ROWS expected (the
                // tree, see IndentedLevel) and the instances their quantity sum.
                ["counted_as"] = bomType == (int)swBomType_e.swBomType_Indented
                    ? "indented: expected rows of the tree and the sum of their quantities (a child row counts one instance of its parent)"
                    : "items of the table and the sum of their quantities",
                ["tables_before"] = NumberOrNull(tablesBefore),
                // L16-2: the configuration the table and the expected items were
                // read in (the view's), and the active configuration of the
                // assembly read again after it was put back.
                ["configuration"] = configuration,
                ["assembly_active_configuration"] = activeAfterReading,
            };

            string crooked = null;
            if (entries != expectations.Count) crooked = "the bill of materials has " + entries + " entries and the assembly has " + expectations.Count + " distinct components";
            else if (!quantityRead) crooked = "the quantity column cannot be read";
            else if (totalQuantity != instances) crooked = "the bill of materials adds up to " + totalQuantity + " parts and the assembly has " + instances;
            if (crooked != null)
            {
                bool removed = RemoveCutTable(model, table);
                return Failed("The bill of materials does not match the assembly: " + crooked + ". " +
                               (removed ? "I removed it." : "Remove it: " + Sentences.HowToWithdrawIt + "."));
            }
            measurements["match"] = true;

            // ── A COLUMN EMPTY ON EVERY ROW IS SAID (report 8cd843 N20) ─────
            //
            // 27/10/2026: the description column read "" on 7 rows of 7 and nothing in the
            // answer said it; the table was delivered with a blank column. The
            // count matching says nothing of what the cells hold.
            string emptyColumns = EmptyColumnsWarning(headers, readLines);
            if (emptyColumns != null) measurements["warning_empty_columns"] = emptyColumns;

            // ── THE PLACE ───────────────────────────────────────────────────
            double w = 0, h = 0;
            try
            {
                for (int c = 0; c < table.ColumnCount; c++) w += table.GetColumnWidth(c);
                for (int r = 0; r < table.RowCount; r++) h += table.GetRowHeight(r);
            }
            catch { }
            string tableName = null;
            try { tableName = (table.GetAnnotation() as IAnnotation)?.GetName(); } catch { }
            var sheet = ReadSheet(drawing, null, tableName);
            string location = null;
            var down = w > 0 && h > 0 ? FreePlace(sheet, w, h, out location) : null;
            if (down == null)
            {
                bool removed = RemoveCutTable(model, table);
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "On the sheet there is no free place for the bill of materials ({0:0} x {1:0} mm) outside the views and the title " +
                    "block. Use a bigger sheet, or another sheet of the drawing («activate_sheet», then this step again). {2}",
                    w * 1000, h * 1000, removed ? "I removed it." : ""));
            }
            try { (table.GetAnnotation() as IAnnotation)?.SetPosition2(down[0], down[1] + h, 0); } catch { }
            measurements["bounding_box_table_mm"] = new object[] { Math.Round(w * 1000, 1), Math.Round(h * 1000, 1) };
            measurements["table_location"] = location;
            measurements["inside_sheet"] = true;
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// The columns of a table that are empty on every row, in one sentence,
        /// or null (report 8cd843 N20). `rows` holds one string[] per row, as
        /// the bill of materials reads them. No interop type:
        /// Oce.Solid.Agent.ContractTests calls it by reflection.
        /// </summary>
        internal static string EmptyColumnsWarning(List<string> headers, List<object> rows)
        {
            if (headers == null || rows == null || rows.Count == 0) return null;
            var empty = new List<string>();
            for (int c = 0; c < headers.Count; c++)
            {
                bool allEmpty = true;
                foreach (var o in rows)
                {
                    var row = o as string[];
                    if (row != null && c < row.Length && !string.IsNullOrWhiteSpace(row[c])) { allEmpty = false; break; }
                }
                string name = headers[c] == null ? "" : headers[c].Trim();
                if (allEmpty) empty.Add(name.Length > 0 ? "«" + name + "»" : "column " + (c + 1));
            }
            if (empty.Count == 0) return null;
            return (empty.Count == 1 ? "The column " + empty[0] + " is" : "The columns " + string.Join(", ", empty) + " are") +
                   " empty on all " + rows.Count + " rows: the property " + (empty.Count == 1 ? "it reads" : "they read") +
                   " is missing or empty in every part. Fill it in the parts, or use a table template that reads the " +
                   "property they have.";
        }

        /// <summary>
        /// The components per BOM item: key = file + configuration, value = how
        /// many instances. Null if the assembly cannot be read.
        ///
        /// SUB-ASSEMBLIES THAT PROMOTE OR HIDE THEIR CHILDREN (finding N27,
        /// measured on 28/9/2026 by the Z-C21.3 probe of envelopes and BOM children:
        /// part A plus a sub-assembly of two B, whose configuration has
        /// IConfiguration.ChildComponentDisplayInBOM set to Show, Hide or Promote;
        /// IConfiguration~ChildComponentDisplayInBOM.html: «Gets or sets the child
        /// component display option of a configuration in a Bill of Materials
        /// (BOM)», swChildComponentInBOMOption_e Hide 1, Show 2, Promote 3). The
        /// tables SOLIDWORKS wrote, and the old count of this method:
        ///   Promote, top level only: rows A (1) and B (2), 3 pieces; the old
        ///     count saw A and the sub-assembly, 2 instances -> false refusal;
        ///   Hide, parts only: rows A (1) and the SUB-ASSEMBLY itself (1), 2
        ///     pieces; the old count saw A and the two B, 3 -> false refusal;
        ///   Show (both types), Promote parts only, Hide top level only: as the
        ///     old count. Envelopes read ExcludeFromBOM true and were already out.
        /// So: in a top-level table a promoting sub-assembly is replaced by its
        /// children (and theirs, if they promote too: swChildComponentInBOMOption_e.html,
        /// Promote: «all of its child components are promoted one level»); in a
        /// parts-only table a hiding sub-assembly is one item and its children
        /// are not. The same page describes Show as «The subassembly appears as a
        /// single item in the BOM» and Hide as «Child components might be listed
        /// individually»: in the parts-only tables it was the other way round
        /// (Hide gave the single item, Show the two B), and the code follows the
        /// tables SOLIDWORKS wrote. The indented table follows the tree measured
        /// for M23 (see IndentedLevel).
        /// The tree is read with IComponent2.GetChildren (IComponent2~GetChildren.html:
        /// «it returns only the immediate (one level) children», so it is walked
        /// level by level); a top-level table reads below the first level only
        /// under a promoting sub-assembly.
        ///
        /// IN WHICH CONFIGURATION (Z-C21.4, L16-2 and L16-3): GetComponents,
        /// IsSuppressed and ReferencedConfiguration answer for the ACTIVE
        /// configuration, so the caller activates the view's configuration first
        /// (<see cref="ComponentsInViewConfiguration"/>). The exclusion from the
        /// BOM is asked by name: <paramref name="topConfiguration"/> for a
        /// top-level component, and for a child the configuration its parent
        /// references, which is the configuration of the sub-assembly that holds
        /// it (<see cref="ExcludedFromBomIn"/>).
        /// </summary>
        private static Dictionary<string, int> ComponentsForEntry(IAssemblyDoc assemblyDoc, int bomType, string topConfiguration,
                                                                 out string readProblem)
        {
            readProblem = null;
            try
            {
                bool topLevelOnly = bomType == (int)swBomType_e.swBomType_TopLevelOnly;
                var comps = new List<IComponent2>();
                var parents = new List<int>();
                var keys = new List<string>();
                var isAssembly = new List<bool>();
                var childDisplay = new List<int>();
                var skipped = new List<bool>();
                // The configuration each component references, one per index,
                // so that a child can name the configuration of its parent.
                var referencedConfigurations = new List<string>();
                foreach (var o in (assemblyDoc.GetComponents(true) as object[]) ?? new object[0])
                {
                    var top = o as IComponent2;
                    if (top == null) continue;
                    comps.Add(top);
                    parents.Add(-1);
                }
                for (int i = 0; i < comps.Count && i < 100000; i++)
                {
                    IComponent2 c = comps[i];
                    bool suppressed = false, excluded = false;
                    // SUPPRESSED BY GetSuppression2, NOT BY IsSuppressed (Z-C21.11,
                    // probe zc2111/probe_alleggeriti, 30/9/2026): IsSuppressed answers
                    // TRUE on a LIGHTWEIGHT component, as the programming guide says
                    // (Overview/Work_with_Lightweight_Components.htm), so a lightweight
                    // sub-assembly and all its children were left out of the BOM.
                    var item = AssemblyComponents.Describe(c);
                    if (item.Load == ComponentLoad.Unknown)
                    {
                        readProblem = "I could not read whether «" + item.Name + "» is suppressed, lightweight or " +
                                      "resolved (GetSuppression2): the expected BOM cannot be counted.";
                        return null;
                    }
                    suppressed = item.Load == ComponentLoad.Suppressed;
                    string configuration = c.ReferencedConfiguration ?? "";
                    referencedConfigurations.Add(configuration);
                    // A suppressed component is out of the BOM whatever its flag
                    // says; the flag is read only on the others. An exclusion
                    // that cannot be read is not taken as «included»: it throws,
                    // and the whole reading answers null (the caller refuses).
                    if (!suppressed)
                        excluded = ExcludedFromBomIn(c, parents[i] < 0 ? topConfiguration : referencedConfigurations[parents[i]]);
                    // A SUB-ASSEMBLY BY ITS FILE EXTENSION (finding M23): GetModelDoc2 is
                    // null on a lightweight component (measured), which counted a
                    // lightweight sub-assembly as a part. Its BOM option lives in its
                    // document: a lightweight sub-assembly that is not suppressed cannot
                    // say it, and the reading stops instead of taking «Show». (Measured:
                    // reopening the drawing resolved the lightweight sub-assemblies it
                    // shows, so this is the rare case.)
                    IModelDoc2 doc = item.Document;
                    bool assembly = item.IsSubAssembly;
                    int display = -1;
                    if (assembly && !suppressed)
                    {
                        if (doc == null)
                        {
                            readProblem = "The sub-assembly «" + item.Name + "» is lightweight: how it shows its children " +
                                          "in a BOM (show, hide, promote) is in its document, which is not loaded. Resolve it " +
                                          "and ask again.";
                            return null;
                        }
                        try
                        {
                            var cfg = doc.GetConfigurationByName(configuration) as IConfiguration;
                            if (cfg != null) display = cfg.ChildComponentDisplayInBOM;
                        }
                        catch { }
                    }
                    keys.Add((c.GetPathName() ?? "") + "|" + configuration);
                    isAssembly.Add(assembly);
                    childDisplay.Add(display);
                    skipped.Add(suppressed || excluded);
                    if (topLevelOnly && (suppressed || excluded || !assembly ||
                                         display != (int)swChildComponentInBOMOption_e.swChildComponent_Promote)) continue;
                    object[] children = null;
                    try { children = c.GetChildren() as object[]; } catch { }
                    foreach (var o in children ?? new object[0])
                    {
                        var child = o as IComponent2;
                        if (child == null) continue;
                        comps.Add(child);
                        parents.Add(i);
                    }
                }
                return ExpectedBomItems(keys.ToArray(), parents.ToArray(), isAssembly.ToArray(),
                                        childDisplay.ToArray(), skipped.ToArray(), bomType);
            }
            catch (Exception ex)
            {
                readProblem = "Reading the components for the BOM failed: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// THE EXPECTED BOM ITEMS, READ IN THE CONFIGURATION THE TABLE SHOWS
        /// (Z-C21.4, finding L16-2). When the assembly's active configuration is
        /// already the view's, it is read as it is. Otherwise the view's
        /// configuration is activated with ShowConfiguration2, read, and the one
        /// that was active is activated again; both moves are judged by READING
        /// the active configuration back, never by the answer of
        /// ShowConfiguration2 (it answers false when the configuration is already
        /// the active one: the bench case of activate_configuration, 16/9/2026).
        ///
        /// Activating and not walking IConfiguration.GetRootComponent3 of the
        /// view's configuration: IConfiguration~GetRootComponent3.html says «An
        /// IComponent2 object is based on the currently active configuration; one
        /// assembly configuration might suppress the component, while another
        /// might display it», and the 2021 guide shows no getter for the
        /// configuration a component references in a configuration that is not
        /// the active one (IComponent2.ReferencedConfiguration takes no
        /// configuration name). What the user sees
        /// moves for the time of the reading only, and comes back before the
        /// table is inserted.
        ///
        /// <paramref name="problem"/> is the refusal when the assembly could not
        /// be read in that configuration or could not be put back (then
        /// <paramref name="activeAfter"/> says where it stayed); null otherwise.
        /// </summary>
        private static Dictionary<string, int> ComponentsInViewConfiguration(
            IAssemblyDoc assemblyDoc, int bomType, string viewConfiguration, out string activeAfter, out string problem)
        {
            problem = null;
            var doc = assemblyDoc as IModelDoc2;
            string activeBefore = ActiveConfigurationName(doc);
            activeAfter = activeBefore;
            if (string.IsNullOrEmpty(activeBefore))
            {
                problem = "I could not read which configuration of the assembly is active, so I cannot tell " +
                          "whether it must be activated to read it in «" + viewConfiguration + "», the " +
                          "configuration of the view. No table was inserted.";
                return null;
            }
            string readProblem;
            if (string.Equals(activeBefore, viewConfiguration, StringComparison.Ordinal))
            {
                var asIs = ComponentsForEntry(assemblyDoc, bomType, viewConfiguration, out readProblem);
                if (asIs == null && readProblem != null) problem = readProblem + " No table was inserted.";
                return asIs;
            }

            Dictionary<string, int> items = null;
            string activated = null;
            // The answers of ShowConfiguration2 are kept for the messages only:
            // the decision is the active configuration read back.
            string activateAnswer = "not called", restoreAnswer = "not needed";
            try
            {
                try { activateAnswer = doc.ShowConfiguration2(viewConfiguration) ? "true" : "false"; }
                catch (Exception ex) { activateAnswer = "an exception: " + ex.Message; }
                activated = ActiveConfigurationName(doc);
                if (string.Equals(activated, viewConfiguration, StringComparison.Ordinal))
                {
                    items = ComponentsForEntry(assemblyDoc, bomType, viewConfiguration, out readProblem);
                    if (items == null && readProblem != null) problem = readProblem + " No table was inserted.";
                }
            }
            finally
            {
                if (!string.Equals(ActiveConfigurationName(doc), activeBefore, StringComparison.Ordinal))
                {
                    try { restoreAnswer = doc.ShowConfiguration2(activeBefore) ? "true" : "false"; }
                    catch (Exception ex) { restoreAnswer = "an exception: " + ex.Message; }
                }
                activeAfter = ActiveConfigurationName(doc);
            }
            if (!string.Equals(activeAfter, activeBefore, StringComparison.Ordinal))
            {
                problem = "To read the assembly in «" + viewConfiguration + "», the configuration of the view, I " +
                          "activated it, and I could not put the assembly back: it was on «" + activeBefore +
                          "» and reading it again it is on " +
                          (string.IsNullOrEmpty(activeAfter) ? "a configuration I cannot read" : "«" + activeAfter + "»") +
                          " (ShowConfiguration2 answered " + restoreAnswer + "). Activate «" + activeBefore +
                          "» again in the assembly. No table was inserted.";
                return null;
            }
            if (!string.Equals(activated, viewConfiguration, StringComparison.Ordinal))
            {
                problem = "The view shows the configuration «" + viewConfiguration + "» of the assembly, and I could " +
                          "not activate it to read its components (ShowConfiguration2 answered " + activateAnswer +
                          ", and reading it back the active one was " +
                          (string.IsNullOrEmpty(activated) ? "unreadable" : "«" + activated + "»") + "). Comparing " +
                          "the table with «" + activeBefore + "» would compare two different assemblies. No table " +
                          "was inserted; the assembly is on «" + activeAfter + "», as before.";
                return null;
            }
            return items;
        }

        /// <summary>The name of the active configuration of a document, or null when it cannot be read.</summary>
        private static string ActiveConfigurationName(IModelDoc2 doc)
        {
            try
            {
                var manager = doc == null ? null : doc.ConfigurationManager;
                IConfiguration active = manager == null ? null : manager.ActiveConfiguration;
                return active == null ? null : active.Name;
            }
            catch { return null; }
        }

        /// <summary>
        /// Whether <paramref name="component"/> is excluded from the BOM in
        /// <paramref name="ownerConfiguration"/>, a configuration of the assembly
        /// that holds it (Z-C21.4, finding L16-3).
        ///
        /// OBSOLETE-MIGRATED: IComponent2~ExcludeFromBOM.html, «Obsolete.
        /// Superseded by IComponent2::GetExcludeFromBOM2 and
        /// IComponent2::SetExcludeFromBOM2»; the old property takes no
        /// configuration. IComponent2~GetExcludeFromBOM2.html: «Config_names
        /// Array of the names of configurations in whose BOMs this component is
        /// excluded or included», «Return Value Array of values indicating
        /// whether this component is: excluded (true) from - or - included (false)
        /// in the BOMs of the specified configurations». The name is passed
        /// (swSpecifyConfiguration, pattern S8: the name is always passed). When
        /// that form does not answer one Boolean, swThisConfiguration with no
        /// names is asked, the form the Z-C21.3 probe probe_is_suppressed2
        /// measured equal to the obsolete getter for the sibling
        /// IFeature.IsSuppressed2. Neither form was measured on GetExcludeFromBOM2
        /// itself: the bench case the_bom_follows_the_view_configuration reads it
        /// on a component excluded in a configuration that is not the active one.
        /// Throws when neither form answers one Boolean: an exclusion that cannot
        /// be read must not become «included».
        /// </summary>
        private static bool ExcludedFromBomIn(IComponent2 component, string ownerConfiguration)
        {
            bool? answer = null;
            // The named form is the one asked first, and a throw from it falls
            // back like an answer that is not one Boolean: neither form was
            // measured on this getter, and a COM error on the named one must not
            // make every BOM with a child refuse while the other form answers.
            if (!string.IsNullOrEmpty(ownerConfiguration))
            {
                try
                {
                    answer = OneBoolean(component.GetExcludeFromBOM2(
                        (int)swInConfigurationOpts_e.swSpecifyConfiguration, new[] { ownerConfiguration }));
                }
                catch (System.Runtime.InteropServices.COMException) { answer = null; }
            }
            if (!answer.HasValue)
                answer = OneBoolean(component.GetExcludeFromBOM2(
                    (int)swInConfigurationOpts_e.swThisConfiguration, null));
            if (!answer.HasValue)
                throw new InvalidOperationException(
                    "IComponent2.GetExcludeFromBOM2 did not answer one Boolean for «" + ownerConfiguration + "»");
            return (bool)answer;
        }

        /// <summary>The single Boolean of an answer that is an array of one, or null.</summary>
        private static bool? OneBoolean(object raw)
        {
            var flags = raw as bool[];
            if (flags != null) return flags.Length == 1 ? flags[0] : (bool?)null;
            var boxed = raw as object[];
            if (boxed != null && boxed.Length == 1 && boxed[0] is bool one) return one;
            return null;
        }

        /// <summary>
        /// N27, the rule without SOLIDWORKS (the contract group in Prove calls it
        /// with the probe's assemblies): one entry per component of the tree, its
        /// key, the index of its parent (-1 at the top), whether it is an
        /// assembly, its swChildComponentInBOMOption_e (-1 if not read) and
        /// whether it is out of the BOM (suppressed or excluded). Returns the BOM
        /// items a table of that swBomType_e must have, or null if the arrays do
        /// not line up.
        /// </summary>
        internal static Dictionary<string, int> ExpectedBomItems(
            string[] keys, int[] parents, bool[] isAssembly, int[] childDisplay, bool[] skipped, int bomType)
        {
            if (keys == null || parents == null || isAssembly == null || childDisplay == null || skipped == null) return null;
            int n = keys.Length;
            if (parents.Length != n || isAssembly.Length != n || childDisplay.Length != n || skipped.Length != n) return null;
            const int hide = (int)swChildComponentInBOMOption_e.swChildComponent_Hide;
            const int promote = (int)swChildComponentInBOMOption_e.swChildComponent_Promote;
            var items = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (bomType == (int)swBomType_e.swBomType_TopLevelOnly)
            {
                // A promoting sub-assembly gives way to its children, which then
                // stand as top-level items (and may promote in their turn).
                var pending = new List<int>();
                for (int i = 0; i < n; i++) if (parents[i] < 0) pending.Add(i);
                for (int p = 0; p < pending.Count && p <= n; p++)
                {
                    int i = pending[p];
                    if (skipped[i]) continue;
                    if (isAssembly[i] && childDisplay[i] == promote)
                    {
                        for (int j = 0; j < n; j++) if (parents[j] == i) pending.Add(j);
                        continue;
                    }
                    CountBomItem(items, keys[i]);
                }
                return items;
            }
            if (bomType == (int)swBomType_e.swBomType_Indented)
            {
                var top = new List<int>();
                for (int i = 0; i < n; i++) if (parents[i] < 0) top.Add(i);
                IndentedLevel(top, "", keys, parents, isAssembly, childDisplay, skipped, items, 0);
                return items;
            }
            bool partsOnly = bomType == (int)swBomType_e.swBomType_PartsOnly;
            for (int i = 0; i < n; i++)
            {
                if (skipped[i]) continue;
                if (partsOnly && HiddenByParent(i, parents, isAssembly, childDisplay, hide)) continue;
                if (isAssembly[i])
                {
                    // Parts only: a sub-assembly is no item, unless it hides its
                    // children, and then it is one (measured: the table lists it).
                    if (partsOnly && childDisplay[i] == hide) CountBomItem(items, keys[i]);
                    continue;
                }
                CountBomItem(items, keys[i]);
            }
            return items;
        }

        /// <summary>
        /// THE INDENTED TABLE (finding M23, measured on 30/9/2026 by the Z-C21.11
        /// probe of the indented BOM on the tables SOLIDWORKS wrote, Detailed
        /// numbering). Up to library 0.146.0 an indented table was compared as the
        /// flat list of parts, so every indented table of an assembly with a
        /// sub-assembly was refused (A + 3 S_show + B: 4 rows against 2 «items»). The
        /// rows SOLIDWORKS writes: siblings grouped by file and configuration, one row
        /// per group with the group's count; under a sub-assembly row the children of
        /// ONE instance, one level deeper (A + 3 S_show(2 B) + B: A 1, S_show 3,
        /// S_show > B 2, B 1 — the child row says 2, not 6); a Hide sub-assembly is a
        /// row with no children below; a Promote sub-assembly has no row and its
        /// children stand at its level; two levels (A + 2 N, N = S_show + B): A 1, N 2,
        /// N > S_show 1, N > S_show > B 2, N > B 1. Each row is an item here, keyed by
        /// the keys above it and its own (« > » between them), valued by its
        /// quantity: the caller's checks (rows = items, quantity sum = values) then
        /// read the indented table as they read the others.
        /// </summary>
        private static void IndentedLevel(List<int> siblings, string above, string[] keys, int[] parents, bool[] isAssembly,
                                          int[] childDisplay, bool[] skipped, Dictionary<string, int> items, int depth)
        {
            if (depth > keys.Length) return;
            const int hide = (int)swChildComponentInBOMOption_e.swChildComponent_Hide;
            var flat = FlattenPromoted(siblings, parents, isAssembly, childDisplay, skipped, 0);
            // One group per key: its count and its first member (the children
            // of ONE instance stand under the row).
            var order = new List<string>();
            var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var firstOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (int i in flat)
            {
                string k = keys[i] ?? "";
                if (!count.ContainsKey(k)) { count[k] = 0; firstOf[k] = i; order.Add(k); }
                count[k]++;
            }
            foreach (var k in order)
            {
                string row = above.Length == 0 ? k : above + " > " + k;
                items[row] = count[k];
                int first = firstOf[k];
                if (!isAssembly[first] || childDisplay[first] == hide) continue;
                var children = new List<int>();
                for (int j = 0; j < keys.Length; j++) if (parents[j] == first) children.Add(j);
                IndentedLevel(children, row, keys, parents, isAssembly, childDisplay, skipped, items, depth + 1);
            }
        }

        /// <summary>M23: the components of a level with the suppressed and excluded left out, and a Promote sub-assembly replaced by its children (recursively).</summary>
        private static List<int> FlattenPromoted(List<int> list, int[] parents, bool[] isAssembly, int[] childDisplay, bool[] skipped,
                                                 int depth)
        {
            var into = new List<int>();
            if (depth > parents.Length) return into;
            const int promote = (int)swChildComponentInBOMOption_e.swChildComponent_Promote;
            foreach (int i in list)
            {
                if (skipped[i]) continue;
                if (isAssembly[i] && childDisplay[i] == promote)
                {
                    var children = new List<int>();
                    for (int j = 0; j < parents.Length; j++) if (parents[j] == i) children.Add(j);
                    into.AddRange(FlattenPromoted(children, parents, isAssembly, childDisplay, skipped, depth + 1));
                    continue;
                }
                into.Add(i);
            }
            return into;
        }

        /// <summary>N27: whether a sub-assembly above this component hides its children in the BOM.</summary>
        private static bool HiddenByParent(int i, int[] parents, bool[] isAssembly, int[] childDisplay, int hide)
        {
            int p = parents[i];
            for (int guard = 0; p >= 0 && p < parents.Length && guard <= parents.Length; guard++)
            {
                if (isAssembly[p] && childDisplay[p] == hide) return true;
                p = parents[p];
            }
            return false;
        }

        private static void CountBomItem(Dictionary<string, int> items, string key)
        {
            string k = key ?? "";
            items[k] = items.TryGetValue(k, out int n) ? n + 1 : 1;
        }

        private static int AllDrawingTables(IDrawingDoc drawing)
        {
            try
            {
                int n = 0;
                var v = drawing.GetFirstView() as IView;
                while (v != null) { n += ((v.GetTableAnnotations() as object[]) ?? new object[0]).Length; v = v.GetNextView() as IView; }
                return n;
            }
            catch { return -1; }
        }

        // ──────────────────────────────────────────────────────────────────
        // ballooning (the balloons with the item numbers)
        // ──────────────────────────────────────────────────────────────────
        private Outcome Balloons(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null) return Failed("The active document is not a drawing: the balloons go on a view of an assembly.");
            var names = ViewNames(drawing);
            if (names.Count == 0) return Failed("The drawing has no views: first put the views of the assembly.");
            string reason;
            var view = ResolveView(drawing, names, Str(a, "view", null), out reason);
            if (reason != null) return Failed(reason);
            if (view == null) return Failed("I cannot get the view.");
            var assemblyDoc = view.ReferencedDocument as IAssemblyDoc;
            if (assemblyDoc == null)
                return Failed("Behind the view there is no assembly: the balloons number the components of an assembly.");
            if (AllDrawingTables(drawing) <= 0 || !HasBom(drawing))
                return Failed("There is no bill of materials in the drawing: a balloon carries the entry number of the bill of materials. " +
                               "Put the bill of materials in first, with «drawing_bom».");

            string arrangement = (Str(a, "layout", "square") ?? "square").Trim().ToLowerInvariant();
            int layout;
            switch (arrangement)
            {
                case "square": layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Square; break;
                case "circle": layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Circle; break;
                case "above": layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Top; break;
                case "below": layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Bottom; break;
                default: return Failed("«layout» is one of «square», «circle», «above», «below», not «" + arrangement + "».");
            }

            int before = ViewBalloons(view);
            // The rows of the bill of materials before the balloons (N50): AutoBalloon5
            // renumbers them, and the answer says so with the rows as they are after.
            var rowsBefore = BomRowsOfDrawing(drawing);
            object born = null;
            try
            {
                model.ClearSelection2(true);
                // THE VIEW MUST BE SELECTED, AND THE ANSWER SAYS SO (finding
                // M39). IDrawingDoc~AutoBalloon5.html, Remarks: «automatically
                // inserts BOM balloons for bill of materials items in selected
                // drawing views. If a drawing sheet is selected, BOM balloons are
                // automatically inserted for all of the drawing views on that
                // drawing sheet». Until library 0.144.0 the answer of
                // SelectByID2 was dropped: with the view not selected,
                // AutoBalloon5 worked on nothing, or on whatever else was
                // selected, and only the count below noticed.
                bool viewSelected = model.Extension.SelectByID2(
                    view.GetName2(), "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                if (!viewSelected)
                {
                    model.ClearSelection2(true);
                    return Failed(
                        "I could not select the view «" + view.GetName2() + "» (SelectByID2 answered " +
                        "false), and AutoBalloon5 balloons the SELECTED views only: nothing was changed. " +
                        "Name the view in «view» as the drawing state lists it, and ask again.");
                }
                var opt = drawing.CreateAutoBalloonOptions() as AutoBalloonOptions;
                opt.Layout = layout;
                opt.IgnoreMultiple = true;
                opt.Style = (int)swBalloonStyle_e.swBS_Circular;
                opt.Size = (int)swBalloonFit_e.swBF_2Chars;
                opt.UpperTextContent = (int)swBalloonTextContent_e.swBalloonTextItemNumber;
                // THE BALLOONS NUMBER THE ITEMS IN THE ORDER THEY GO AROUND THE VIEW, AND
                // THAT RENUMBERS THE BILL OF MATERIALS (report 8cd843 N50, round 13): the
                // printed table was not the «rows_read» of «drawing_bom». Measured on a copy
                // of the drawing of round 13 (27/10/2026): ItemOrder comes as 4,
                // swBalloonItemNumbers_OrderSequentially (IAutoBalloonOptions~ItemOrder.html:
                // «Item ordering as defined in swBalloonItemNumbersOrder_e»); the table read
                // 26P000007 to 26P000013 as items 1 to 7 before the balloons and 26P000010,
                // 11, 9, 8, 7, 13, 12 after, the order printed in round 13; with
                // swBalloonItemNumbers_DoNotChangeItemNumbers it kept 7 to 13. The order is
                // now set, not left to the default, and kept as every drawing had it (which
                // order a drawing wants is not decided here); the answer gives the rows again.
                opt.ItemOrder = (int)swBalloonItemNumbersOrder_e.swBalloonItemNumbers_OrderSequentially;
                born = drawing.AutoBalloon5(opt);
            }
            catch (Exception ex) { model.ClearSelection2(true); return Failed("The balloons were not created: " + ex.Message); }
            model.ClearSelection2(true);
            int after = ViewBalloons(view);
            var visible = VisibleComponents(view);
            // WHAT AutoBalloon5 RETURNED, COUNTED (M39). IDrawingDoc~AutoBalloon5.html,
            // Return Value: «Array of INotes». Until library 0.144.0 the array
            // was kept and never looked at. -1 when no array came back: that is
            // not «zero balloons», it is an answer that says nothing.
            int returnedNotes = BalloonNotesReturned(born);
            var measurements = new Dictionary<string, object>
            {
                ["view"] = view.GetName2(),
                ["balloons_before"] = NumberOrNull(before),
                ["balloons_after"] = NumberOrNull(after),
                ["balloons_placed"] = before >= 0 && after >= 0 ? (object)(after - before) : null,
                ["balloons_returned"] = NumberOrNull(returnedNotes),
                ["components_visible"] = NumberOrNull(visible),
                ["numbers"] = BalloonNumbers(view).ToArray(),
            };
            // THE BILL OF MATERIALS AFTER THE BALLOONS (N50): renumbered or not, and its
            // rows as they are now, the ones the print shows.
            var rowsAfter = BomRowsOfDrawing(drawing);
            if (rowsAfter != null)
            {
                bool renumbered = rowsBefore != null && !SameRows(rowsBefore, rowsAfter);
                measurements["bom_renumbered"] = renumbered;
                measurements["bom_rows_after"] = rowsAfter.Cast<object>().ToArray();
                if (renumbered)
                    measurements["bom_note"] =
                        "The balloons numbered the items in the order they go around the view, and that renumbered the " +
                        "bill of materials: the «rows_read» of «drawing_bom» no longer hold. Use «bom_rows_after», the " +
                        "rows as the table prints them now.";
            }
            string countNote = BalloonCountNote(returnedNotes, before, after);
            if (countNote != null) measurements["balloons_count_note"] = countNote;
            if (before < 0 || after < 0)
                return Failed("I cannot count the balloons of the view: I do not know whether the balloons were created. Look at the drawing.");
            if (after <= before)
            {
                // NOTHING NEW IS NOT ALWAYS AN ERROR: if every entry of the
                // bill of materials already has its balloon in another view, SOLIDWORKS
                // rightly puts none (seen on the Starlink, 26/9/2026).
                var already = BomItemNumbers(drawing);
                var alreadyBallooned = BalloonedNumbersInDrawing(drawing);
                if (already != null && already.Count > 0 && already.All(alreadyBallooned.Contains))
                {
                    measurements["bom_entries"] = already.Count;
                    measurements["entries_without_balloon"] = new string[0];
                    measurements["how_many_entries_without_balloon"] = 0;
                    measurements["verdict"] = "Every entry of the bill of materials already has its balloon in the drawing: nothing to add in this view.";
                    return SucceededWithoutCreating(measurements);
                }
                return Failed("SOLIDWORKS did not put any balloon (there were " + before + ", there are " + after + ").");
            }

            // DOES EVERY ENTRY OF THE BILL OF MATERIALS HAVE ITS BALLOON? (seen on the
            // Starlink assembly, 26/9/2026: the disc covered in the isometric stayed without,
            // and the answer said «done»). The item numbers of the
            // bill of materials are compared with the balloons of the WHOLE drawing, and it is said which are missing:
            // another view is ballooned, where that component can be seen.
            var entries = BomItemNumbers(drawing);
            if (entries != null)
            {
                var ballooned = BalloonedNumbersInDrawing(drawing);
                var without = entries.Where(v => !ballooned.Contains(v)).ToList();
                measurements["bom_entries"] = entries.Count;
                measurements["entries_without_balloon"] = without.ToArray();
                measurements["how_many_entries_without_balloon"] = without.Count;
                if (without.Count > 0)
                    measurements["warning"] = (without.Count == 1
                        ? "The entry " + without[0] + " of the bill of materials has no balloon in the drawing: in this view that component " +
                          "is NOT visible. Put the balloons on another view too, where it is visible."
                        : "The entries " + string.Join(", ", without) + " of the bill of materials have no balloon in the drawing: in " +
                          "this view those components are not visible. Put the balloons on another view too, where they are visible.");
            }
            else measurements["how_many_entries_without_balloon"] = null;   // the column of the item numbers cannot be read
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>The item numbers that have a balloon in any view of the drawing.</summary>
        private static HashSet<string> BalloonedNumbersInDrawing(IDrawingDoc drawing)
        {
            var ballooned = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var w = drawing.GetFirstView() as IView;
                while (w != null) { foreach (var n in BalloonNumbers(w)) ballooned.Add(n.Trim()); w = w.GetNextView() as IView; }
            }
            catch { }
            return ballooned;
        }

        /// <summary>
        /// The rows of the first bill of materials of the drawing, every cell, as
        /// «drawing_bom» reads them in «rows_read»; null when there is none or it cannot
        /// be read (N50).
        /// </summary>
        private static List<string[]> BomRowsOfDrawing(IDrawingDoc drawing)
        {
            try
            {
                for (var v = drawing.GetFirstView() as IView; v != null; v = v.GetNextView() as IView)
                    foreach (var o in (v.GetTableAnnotations() as object[]) ?? new object[0])
                    {
                        if (!(o is IBomTableAnnotation)) continue;
                        var t = o as ITableAnnotation;
                        var rows = new List<string[]>();
                        for (int r = 1; r < t.RowCount; r++)
                        {
                            var cells = new string[t.ColumnCount];
                            for (int c = 0; c < cells.Length; c++) cells[c] = Cell(t, r, c);
                            rows.Add(cells);
                        }
                        return rows;
                    }
            }
            catch { }
            return null;
        }

        /// <summary>Whether two readings of a table hold the same rows in the same order (N50). Pure, so the contract suite reads it.</summary>
        internal static bool SameRows(List<string[]> a, List<string[]> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int r = 0; r < a.Count; r++)
            {
                if (a[r] == null || b[r] == null || a[r].Length != b[r].Length) return false;
                for (int c = 0; c < a[r].Length; c++)
                    if (!string.Equals(a[r][c], b[r][c], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>The item numbers of the first bill of materials of the drawing; null if they cannot be read.</summary>
        private static List<string> BomItemNumbers(IDrawingDoc drawing)
        {
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    foreach (var o in (v.GetTableAnnotations() as object[]) ?? new object[0])
                    {
                        if (!(o is IBomTableAnnotation)) continue;
                        var t = o as ITableAnnotation;
                        int col = -1;
                        for (int c = 0; c < t.ColumnCount && col < 0; c++)
                            try { if (t.GetColumnType2(c, false) == (int)swTableColumnTypes_e.swBomTableColumnType_ItemNumber) col = c; } catch { }
                        if (col < 0) return null;
                        var outside = new List<string>();
                        for (int r = 1; r < t.RowCount; r++)
                        {
                            string n = Cell(t, r, col).Trim();
                            if (n.Length > 0) outside.Add(n);
                        }
                        return outside;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return null;
        }

        private static bool HasBom(IDrawingDoc drawing)
        {
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    foreach (var o in (v.GetTableAnnotations() as object[]) ?? new object[0])
                        if (o is IBomTableAnnotation) return true;
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return false;
        }

        /// <summary>The balloons of the view: the notes that are balloons of the bill of materials; -1 if it cannot be read.</summary>
        private static int ViewBalloons(IView view)
        {
            try
            {
                int n = 0;
                var note = view.GetFirstNote() as INote;
                while (note != null)
                {
                    if (note.IsBomBalloon()) n++;
                    note = note.GetNext() as INote;
                }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>
        /// How many notes AutoBalloon5 returned (finding M39): the non-null
        /// items of its array, or -1 when it returned no array. No interop type
        /// in the signature, so that Oce.Solid.Agent.ContractTests can call it by reflection.
        /// </summary>
        internal static int BalloonNotesReturned(object returned)
        {
            var notes = returned as object[];
            if (notes == null) return -1;
            int n = 0;
            foreach (var o in notes) if (o != null) n++;
            return n;
        }

        /// <summary>
        /// The note for the answer when the array AutoBalloon5 returned and the
        /// balloons counted in the view before and after do not tell the same
        /// story (M39); null when they agree or when the view could not be
        /// counted (that case has its own refusal). -1 is «not known».
        /// </summary>
        internal static string BalloonCountNote(int returned, int before, int after)
        {
            if (before < 0 || after < 0) return null;
            if (returned < 0)
                return "AutoBalloon5 returned no array of notes: the balloons placed were counted in the view " +
                       "instead (" + (after - before) + ").";
            if (returned != after - before)
                return "AutoBalloon5 returned " + returned + (returned == 1 ? " note" : " notes") + ", and the " +
                       "view has " + (after - before) + " balloons more than before: look at the view before " +
                       "trusting either number.";
            return null;
        }

        private static List<string> BalloonNumbers(IView view)
        {
            var outside = new List<string>();
            try
            {
                var note = view.GetFirstNote() as INote;
                while (note != null)
                {
                    if (note.IsBomBalloon()) outside.Add(note.GetText() ?? "");
                    note = note.GetNext() as INote;
                }
            }
            catch { }
            return outside;
        }

        private static int VisibleComponents(IView view)
        {
            try { return ((view.GetVisibleComponents() as object[]) ?? new object[0]).Length; }
            catch { return -1; }
        }
    }
}
