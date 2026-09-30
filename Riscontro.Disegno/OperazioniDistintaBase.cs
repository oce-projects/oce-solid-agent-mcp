using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LA DISTINTA BASE E LA BOLLATURA IN TAVOLA (voce GT9).
    ///
    /// «Bollatura» e' il nome d'officina dei palloncini coi numeri di voce
    /// (deciso il 26/9/2026): l'operazione si chiama cosi'.
    ///
    /// `IView.InsertBomTable4(UseAnchorPoint, X, Y, AnchorType, BomType,
    /// Configuration, TableTemplate, Hidden, IndentedNumberingType,
    /// DetailedCutList)` sta sulla VISTA; `IDrawingDoc.AutoBalloon5` prende un
    /// `AutoBalloonOptions` costruito con `CreateAutoBalloonOptions` (la forma con
    /// l'oggetto: un errore di posizione fra nove argomenti non da' eccezione, da'
    /// un disegno diverso).
    ///
    /// ── IL CONFRONTO ────────────────────────────────────────────────────
    ///
    /// La distinta dice quali componenti e quanti. L'assieme lo sa per conto suo
    /// (`IAssemblyDoc.GetComponents(true)`, il primo livello, raggruppato per file
    /// e configurazione): **due letture dello stesso fatto**, e se non combaciano
    /// lo si dice. I palloncini si contano DOPO, sulla vista.
    ///
    /// Tipi di distinta (swBomType_e parte da 1, a differenza di altri enum):
    /// «primo_livello» (TopLevelOnly = 2, predefinito), «solo_parti» (1),
    /// «indentata» (3). Si scelgono per nome: un intero passato da fuori e' il
    /// modo perfetto di mettere il tipo sbagliato senza errore.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito DistintaBaseInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito("Il documento attivo non e' una tavola: la distinta base sta su una vista di un assieme.");
            var nomi = NomiDelleViste(disegno);
            if (nomi.Count == 0) return Fallito("La tavola non ha viste: metti prima le viste dell'assieme con «viste_standard».");
            string motivo;
            var vista = RisolviVista(disegno, nomi, Str(a, "vista", null), out motivo);
            if (motivo != null) return Fallito(motivo);
            if (vista == null) return Fallito("Non riesco a prendere la vista.");

            var assieme = vista.ReferencedDocument as IAssemblyDoc;
            if (assieme == null)
                return Fallito("Dietro la vista «" + (vista.GetName2() ?? "") + "» non c'e' un assieme: la distinta base e' " +
                               "l'elenco dei componenti di un assieme. Per un saldato usa «distinta_taglio_in_tavola».");

            string tipoChiesto = (Str(a, "tipo", "primo_livello") ?? "primo_livello").Trim().ToLowerInvariant();
            int tipoBom;
            switch (tipoChiesto)
            {
                case "primo_livello": tipoBom = (int)swBomType_e.swBomType_TopLevelOnly; break;
                case "solo_parti": tipoBom = (int)swBomType_e.swBomType_PartsOnly; break;
                case "indentata": tipoBom = (int)swBomType_e.swBomType_Indented; break;
                default:
                    return Fallito("«tipo» e' uno fra «primo_livello» (predefinito), «solo_parti», «indentata», non «" + tipoChiesto + "».");
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
            string configurazione = "";
            try { configurazione = vista.ReferencedConfiguration ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(configurazione))
                return Fallito(
                    "I could not read which configuration of the assembly the view «" + (vista.GetName2() ?? "") +
                    "» shows. The table is made for that configuration, and without its name the assembly could " +
                    "be read in another one. No table was inserted.");
            string activeAfterReading, readProblem;
            var attese = ComponentsInViewConfiguration(assieme, tipoBom, configurazione,
                                                      out activeAfterReading, out readProblem);
            if (readProblem != null) return Fallito(readProblem);
            if (attese == null) return Fallito("Non riesco a leggere i componenti dell'assieme: senza, non posso confrontare la distinta.");
            if (attese.Count == 0)
                return Fallito(
                    "In the configuration «" + configurazione + "» of the view the assembly has no component for the " +
                    "BOM (every one is suppressed or excluded from the BOM): the table would be empty.");

            string modelloTabella = Str(a, "template", null);
            if (string.IsNullOrWhiteSpace(modelloTabella))
                modelloTabella = PrimoModelloDiTabella((int)swUserPreferenceStringValue_e.swFileLocationsBOMTemplates, ".sldbomtbt");

            int tabellePrima = TabelleDellaTavolaTutte(disegno);
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
                bom = vista.InsertBomTable4(false, 0.02, 0.28, (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft,
                                            tipoBom, configurazione, modelloTabella ?? "", false,
                                            (int)swNumberingType_e.swNumberingType_Detailed, false) as IBomTableAnnotation;
            }
            catch (Exception ex) { return Fallito("La distinta base non e' nata: " + ex.Message); }
            var tabella = bom as ITableAnnotation;
            if (bom == null || tabella == null)
                return Fallito("SOLIDWORKS non ha creato la distinta base e non ha detto perche' (modello di tabella «" +
                               (modelloTabella ?? "") + "»).");

            // ── LE RIGHE, LETTE ─────────────────────────────────────────────
            int righe = 0, colonne = 0;
            try { righe = tabella.RowCount; colonne = tabella.ColumnCount; } catch { }
            int colQuantita = -1, colNumero = -1;
            var intestazioni = new List<string>();
            for (int c = 0; c < colonne; c++)
            {
                intestazioni.Add(Cella(tabella, 0, c));
                int t = -1;
                try { t = tabella.GetColumnType2(c, false); } catch { }
                if (t == (int)swTableColumnTypes_e.swBomTableColumnType_Quantity && colQuantita < 0) colQuantita = c;
                if (t == (int)swTableColumnTypes_e.swBomTableColumnType_ItemNumber && colNumero < 0) colNumero = c;
            }
            var righeLette = new List<object>();
            int quantitaTot = 0; bool quantitaLetta = colQuantita >= 0;
            for (int r = 1; r < righe; r++)
            {
                var celle = new List<string>();
                for (int c = 0; c < colonne; c++) celle.Add(Cella(tabella, r, c));
                righeLette.Add(celle.ToArray());
                if (colQuantita >= 0)
                {
                    int q;
                    if (int.TryParse(Cella(tabella, r, colQuantita).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out q)) quantitaTot += q;
                    else quantitaLetta = false;
                }
            }
            int voci = righe - 1;
            int istanze = attese.Values.Sum();
            var misure = new Dictionary<string, object>
            {
                ["vista"] = vista.GetName2(),
                ["tipo"] = tipoChiesto,
                ["righe"] = righe,
                ["voci"] = voci,
                ["intestazioni"] = intestazioni.ToArray(),
                ["righe_lette"] = righeLette.ToArray(),
                ["quantita_in_tabella"] = quantitaLetta ? (object)quantitaTot : null,
                ["componenti_distinti_nell_assieme"] = attese.Count,
                ["istanze_nell_assieme"] = istanze,
                ["tabelle_prima"] = NumeroOppureNull(tabellePrima),
                // L16-2: the configuration the table and the expected items were
                // read in (the view's), and the active configuration of the
                // assembly read again after it was put back.
                ["configurazione"] = configurazione,
                ["assembly_active_configuration"] = activeAfterReading,
            };

            string storto = null;
            if (voci != attese.Count) storto = "la distinta ha " + voci + " voci e l'assieme " + attese.Count + " componenti distinti";
            else if (!quantitaLetta) storto = "la colonna delle quantita' non si legge";
            else if (quantitaTot != istanze) storto = "la distinta somma " + quantitaTot + " pezzi e l'assieme ne ha " + istanze;
            if (storto != null)
            {
                bool tolta = TogliLaTabellaDiTaglio(modello, tabella);
                return Fallito("La distinta base non combacia con l'assieme: " + storto + ". " +
                               (tolta ? "L'ho tolta." : "Toglila: " + Frasi.CosiSiRitira + "."));
            }
            misure["combaciano"] = true;

            // ── IL POSTO ────────────────────────────────────────────────────
            double w = 0, h = 0;
            try
            {
                for (int c = 0; c < tabella.ColumnCount; c++) w += tabella.GetColumnWidth(c);
                for (int r = 0; r < tabella.RowCount; r++) h += tabella.GetRowHeight(r);
            }
            catch { }
            string nomeTabella = null;
            try { nomeTabella = (tabella.GetAnnotation() as IAnnotation)?.GetName(); } catch { }
            var foglio = LeggiIlFoglio(disegno, null, nomeTabella);
            string dove = null;
            var basso = w > 0 && h > 0 ? PostoLibero(foglio, w, h, out dove) : null;
            if (basso == null)
            {
                bool tolta = TogliLaTabellaDiTaglio(modello, tabella);
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "Sul foglio non c'e' un posto libero per la distinta base ({0:0} x {1:0} mm) fuori dalle viste e dal cartiglio. " +
                    "Usa un foglio piu' grande. {2}", w * 1000, h * 1000, tolta ? "L'ho tolta." : ""));
            }
            try { (tabella.GetAnnotation() as IAnnotation)?.SetPosition2(basso[0], basso[1] + h, 0); } catch { }
            misure["ingombro_tabella_mm"] = new object[] { Math.Round(w * 1000, 1), Math.Round(h * 1000, 1) };
            misure["dove_sta_la_tabella"] = dove;
            misure["dentro_il_foglio"] = true;
            return RiuscitoSenzaCreare(misure);
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
        /// tables SOLIDWORKS wrote. The indented table was not measured and
        /// keeps the old count (every part, all levels).
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
        private static Dictionary<string, int> ComponentiPerVoce(IAssemblyDoc assieme, int bomType, string topConfiguration)
        {
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
                foreach (var o in (assieme.GetComponents(true) as object[]) ?? new object[0])
                {
                    var top = o as IComponent2;
                    if (top == null) continue;
                    comps.Add(top);
                    parents.Add(-1);
                }
                for (int i = 0; i < comps.Count && i < 100000; i++)
                {
                    IComponent2 c = comps[i];
                    bool soppresso = false, escluso = false;
                    try { soppresso = c.IsSuppressed(); } catch { }
                    string configuration = c.ReferencedConfiguration ?? "";
                    referencedConfigurations.Add(configuration);
                    // A suppressed component is out of the BOM whatever its flag
                    // says; the flag is read only on the others. An exclusion
                    // that cannot be read is not taken as «included»: it throws,
                    // and the whole reading answers null (the caller refuses).
                    if (!soppresso)
                        escluso = ExcludedFromBomIn(c, parents[i] < 0 ? topConfiguration : referencedConfigurations[parents[i]]);
                    IModelDoc2 doc = null;
                    try { doc = c.GetModelDoc2() as IModelDoc2; } catch { }   // null when suppressed or lightweight: a part, as before
                    bool assembly = doc is IAssemblyDoc;
                    int display = -1;
                    if (assembly)
                    {
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
                    skipped.Add(soppresso || escluso);
                    if (topLevelOnly && (soppresso || escluso || !assembly ||
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
            catch { return null; }
        }

        /// <summary>
        /// THE EXPECTED BOM ITEMS, READ IN THE CONFIGURATION THE TABLE SHOWS
        /// (Z-C21.4, finding L16-2). When the assembly's active configuration is
        /// already the view's, it is read as it is. Otherwise the view's
        /// configuration is activated with ShowConfiguration2, read, and the one
        /// that was active is activated again; both moves are judged by READING
        /// the active configuration back, never by the answer of
        /// ShowConfiguration2 (it answers false when the configuration is already
        /// the active one: the bench case of attiva_configurazione, 16/9/2026).
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
            IAssemblyDoc assieme, int bomType, string viewConfiguration, out string activeAfter, out string problem)
        {
            problem = null;
            var doc = assieme as IModelDoc2;
            string activeBefore = ActiveConfigurationName(doc);
            activeAfter = activeBefore;
            if (string.IsNullOrEmpty(activeBefore))
            {
                problem = "I could not read which configuration of the assembly is active, so I cannot tell " +
                          "whether it must be activated to read it in «" + viewConfiguration + "», the " +
                          "configuration of the view. No table was inserted.";
                return null;
            }
            if (string.Equals(activeBefore, viewConfiguration, StringComparison.Ordinal))
                return ComponentiPerVoce(assieme, bomType, viewConfiguration);

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
                    items = ComponentiPerVoce(assieme, bomType, viewConfiguration);
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
            if (!string.IsNullOrEmpty(ownerConfiguration))
                answer = OneBoolean(component.GetExcludeFromBOM2(
                    (int)swInConfigurationOpts_e.swSpecifyConfiguration, new[] { ownerConfiguration }));
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

        private static int TabelleDellaTavolaTutte(IDrawingDoc disegno)
        {
            try
            {
                int n = 0;
                var v = disegno.GetFirstView() as IView;
                while (v != null) { n += ((v.GetTableAnnotations() as object[]) ?? new object[0]).Length; v = v.GetNextView() as IView; }
                return n;
            }
            catch { return -1; }
        }

        // ──────────────────────────────────────────────────────────────────
        // bollatura (i palloncini coi numeri di voce)
        // ──────────────────────────────────────────────────────────────────
        private Esito Bollatura(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null) return Fallito("Il documento attivo non e' una tavola: la bollatura sta su una vista di un assieme.");
            var nomi = NomiDelleViste(disegno);
            if (nomi.Count == 0) return Fallito("La tavola non ha viste: metti prima le viste dell'assieme.");
            string motivo;
            var vista = RisolviVista(disegno, nomi, Str(a, "vista", null), out motivo);
            if (motivo != null) return Fallito(motivo);
            if (vista == null) return Fallito("Non riesco a prendere la vista.");
            var assieme = vista.ReferencedDocument as IAssemblyDoc;
            if (assieme == null)
                return Fallito("Dietro la vista non c'e' un assieme: la bollatura numera i componenti di un assieme.");
            if (TabelleDellaTavolaTutte(disegno) <= 0 || !HaDistintaBase(disegno))
                return Fallito("In tavola non c'e' una distinta base: la bollatura porta il numero di voce della distinta. " +
                               "Metti prima «distinta_base_in_tavola».");

            string disposizione = (Str(a, "disposizione", "quadrato") ?? "quadrato").Trim().ToLowerInvariant();
            int layout;
            switch (disposizione)
            {
                case "quadrato": layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Square; break;
                case "cerchio": layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Circle; break;
                case "sopra": layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Top; break;
                case "sotto": layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Bottom; break;
                default: return Fallito("«disposizione» e' una fra «quadrato», «cerchio», «sopra», «sotto», non «" + disposizione + "».");
            }

            int prima = PalloncinidellaVista(vista);
            object nati = null;
            try
            {
                modello.ClearSelection2(true);
                // THE VIEW MUST BE SELECTED, AND THE ANSWER SAYS SO (finding
                // M39). IDrawingDoc~AutoBalloon5.html, Remarks: «automatically
                // inserts BOM balloons for bill of materials items in selected
                // drawing views. If a drawing sheet is selected, BOM balloons are
                // automatically inserted for all of the drawing views on that
                // drawing sheet». Until library 0.144.0 the answer of
                // SelectByID2 was dropped: with the view not selected,
                // AutoBalloon5 worked on nothing, or on whatever else was
                // selected, and only the count below noticed.
                bool viewSelected = modello.Extension.SelectByID2(
                    vista.GetName2(), "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                if (!viewSelected)
                {
                    modello.ClearSelection2(true);
                    return Fallito(
                        "I could not select the view «" + vista.GetName2() + "» (SelectByID2 answered " +
                        "false), and AutoBalloon5 balloons the SELECTED views only: nothing was changed. " +
                        "Name the view in «vista» as the drawing state lists it, and ask again.");
                }
                var opt = disegno.CreateAutoBalloonOptions() as AutoBalloonOptions;
                opt.Layout = layout;
                opt.IgnoreMultiple = true;
                opt.Style = (int)swBalloonStyle_e.swBS_Circular;
                opt.Size = (int)swBalloonFit_e.swBF_2Chars;
                opt.UpperTextContent = (int)swBalloonTextContent_e.swBalloonTextItemNumber;
                nati = disegno.AutoBalloon5(opt);
            }
            catch (Exception ex) { modello.ClearSelection2(true); return Fallito("La bollatura non e' nata: " + ex.Message); }
            modello.ClearSelection2(true);
            int dopo = PalloncinidellaVista(vista);
            var visibili = ComponentiVisibili(vista);
            // WHAT AutoBalloon5 RETURNED, COUNTED (M39). IDrawingDoc~AutoBalloon5.html,
            // Return Value: «Array of INotes». Until library 0.144.0 the array
            // was kept and never looked at. -1 when no array came back: that is
            // not «zero balloons», it is an answer that says nothing.
            int returnedNotes = BalloonNotesReturned(nati);
            var misure = new Dictionary<string, object>
            {
                ["vista"] = vista.GetName2(),
                ["palloncini_prima"] = NumeroOppureNull(prima),
                ["palloncini_dopo"] = NumeroOppureNull(dopo),
                ["palloncini_messi"] = prima >= 0 && dopo >= 0 ? (object)(dopo - prima) : null,
                ["balloons_returned"] = NumeroOppureNull(returnedNotes),
                ["componenti_visibili"] = NumeroOppureNull(visibili),
                ["numeri"] = NumeriDeiPalloncini(vista).ToArray(),
            };
            string countNote = BalloonCountNote(returnedNotes, prima, dopo);
            if (countNote != null) misure["balloons_count_note"] = countNote;
            if (prima < 0 || dopo < 0)
                return Fallito("Non riesco a contare i palloncini della vista: non so se la bollatura e' nata. Guarda la tavola.");
            if (dopo <= prima)
            {
                // NIENTE DI NUOVO NON E' SEMPRE UN ERRORE: se ogni voce della
                // distinta ha gia' il suo palloncino in un'altra vista, SOLIDWORKS
                // giustamente non ne mette (visto sullo Starlink, 26/9/2026).
                var gia = NumeriDiVoceDellaDistinta(disegno);
                var giaBollati = NumeriBollatiInTavola(disegno);
                if (gia != null && gia.Count > 0 && gia.All(giaBollati.Contains))
                {
                    misure["voci_della_distinta"] = gia.Count;
                    misure["voci_senza_bollatura"] = new string[0];
                    misure["quante_voci_senza_bollatura"] = 0;
                    misure["giudizio"] = "Ogni voce della distinta ha gia' il suo palloncino in tavola: niente da aggiungere in questa vista.";
                    return RiuscitoSenzaCreare(misure);
                }
                return Fallito("SOLIDWORKS non ha messo nessun palloncino (erano " + prima + ", sono " + dopo + ").");
            }

            // OGNI VOCE DELLA DISTINTA HA IL SUO PALLONCINO? (visto sull'assieme
            // Starlink, 26/9/2026: il disco coperto nell'isometrica restava senza,
            // e la risposta diceva «fatto»). Si confrontano i numeri di voce della
            // distinta con i palloncini di TUTTA la tavola, e si dice quali mancano:
            // si bolla un'altra vista, dove quel componente si vede.
            var voci = NumeriDiVoceDellaDistinta(disegno);
            if (voci != null)
            {
                var bollati = NumeriBollatiInTavola(disegno);
                var senza = voci.Where(v => !bollati.Contains(v)).ToList();
                misure["voci_della_distinta"] = voci.Count;
                misure["voci_senza_bollatura"] = senza.ToArray();
                misure["quante_voci_senza_bollatura"] = senza.Count;
                if (senza.Count > 0)
                    misure["attenzione"] = (senza.Count == 1
                        ? "La voce " + senza[0] + " della distinta non ha palloncino in tavola: in questa vista quel componente " +
                          "non si vede. Fai la bollatura anche su un'altra vista, dove si vede."
                        : "Le voci " + string.Join(", ", senza) + " della distinta non hanno palloncino in tavola: in questa " +
                          "vista quei componenti non si vedono. Fai la bollatura anche su un'altra vista, dove si vedono.");
            }
            else misure["quante_voci_senza_bollatura"] = null;   // la colonna dei numeri di voce non si legge
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>I numeri di voce che hanno un palloncino in una qualunque vista della tavola.</summary>
        private static HashSet<string> NumeriBollatiInTavola(IDrawingDoc disegno)
        {
            var bollati = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var w = disegno.GetFirstView() as IView;
                while (w != null) { foreach (var n in NumeriDeiPalloncini(w)) bollati.Add(n.Trim()); w = w.GetNextView() as IView; }
            }
            catch { }
            return bollati;
        }

        /// <summary>I numeri di voce della prima distinta base della tavola; null se non si leggono.</summary>
        private static List<string> NumeriDiVoceDellaDistinta(IDrawingDoc disegno)
        {
            try
            {
                var v = disegno.GetFirstView() as IView;
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
                        var fuori = new List<string>();
                        for (int r = 1; r < t.RowCount; r++)
                        {
                            string n = Cella(t, r, col).Trim();
                            if (n.Length > 0) fuori.Add(n);
                        }
                        return fuori;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return null;
        }

        private static bool HaDistintaBase(IDrawingDoc disegno)
        {
            try
            {
                var v = disegno.GetFirstView() as IView;
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

        /// <summary>I palloncini della vista: le note che sono palloncini di distinta; -1 se non si legge.</summary>
        private static int PalloncinidellaVista(IView vista)
        {
            try
            {
                int n = 0;
                var nota = vista.GetFirstNote() as INote;
                while (nota != null)
                {
                    if (nota.IsBomBalloon()) n++;
                    nota = nota.GetNext() as INote;
                }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>
        /// How many notes AutoBalloon5 returned (finding M39): the non-null
        /// items of its array, or -1 when it returned no array. No interop type
        /// in the signature, so that Prove.Contratti can call it by reflection.
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

        private static List<string> NumeriDeiPalloncini(IView vista)
        {
            var fuori = new List<string>();
            try
            {
                var nota = vista.GetFirstNote() as INote;
                while (nota != null)
                {
                    if (nota.IsBomBalloon()) fuori.Add(nota.GetText() ?? "");
                    nota = nota.GetNext() as INote;
                }
            }
            catch { }
            return fuori;
        }

        private static int ComponentiVisibili(IView vista)
        {
            try { return ((vista.GetVisibleComponents() as object[]) ?? new object[0]).Length; }
            catch { return -1; }
        }
    }
}
