using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// WHICH FILES THIS DOCUMENT DEPENDS ON, AND WHICH ARE MISSING (entry Z-B3).
    ///
    /// An assembly is not a document: it is a graph of files on disk. The model
    /// looked at an assembly and did not know that it rests on files that are elsewhere or
    /// are no longer there.
    ///
    /// Measured on 27/9/2026 (`prova0089 zb3`, assembly base + lid, then the
    /// lid file renamed on disk and the assembly reopened silently):
    ///
    ///  * `IModelDocExtension.GetDependencies(true, true, false, true, false)` gives
    ///    a FLAT list in pairs: name, path, name, path.
    ///  * **ListBrokenRefs puts broken references IN the list; it does not MARK
    ///    them** (finding N60). IModelDocExtension~GetDependencies.html says only
    ///    «ListBrokenRefs — True to get broken references, false to not»: the
    ///    parameter includes them, and nothing on the page promises a mark.
    ///    Measured: the missing file is listed with its path like the others,
    ///    so the only way to know it is missing is to look at the disk
    ///    (`File.Exists`). (This bullet used to call the signature one that
    ///    «promises and does not deliver»: the guide promises no mark.)
    ///  * Reopened with a missing file: `OpenDoc6` errors 2, the component
    ///    turns out SUPPRESSED (`GetSuppression2` 0) and `GetModelDoc2` null.
    ///    `GetDocumentDependencies2` on the closed file gives the same list.
    ///
    /// WHERE THE FILE WAS SAVED, NOT WHERE A SEARCH FINDS ONE (finding L12-8,
    /// measured on 28/9/2026 by the Z-C21.3 probe of the same-name file:
    /// the file of a component renamed away, and a DIFFERENT part with the same
    /// file name in another folder). IModelDocExtension~GetDependencies.html:
    /// «Searchflag — True to use the search rules to find dependencies, false to
    /// look where the documents were last saved», and in the Remarks: «Setting
    /// Searchflag to true causes this method to apply the current search
    /// criteria rules to locate the particular reference and may result in a
    /// different return value». Measured, with the component not loaded
    /// (suppressed, OpenDoc6 swFileNotFoundError): with the current SOLIDWORKS
    /// directory on the other folder, Searchflag=true (the call used up to
    /// library 0.141.0) answered the same-name file of the other folder, which
    /// exists, so the list of missing files came out EMPTY on a broken
    /// assembly; Searchflag=false answered the saved path, which does not
    /// exist. And once a true call had
    /// found that file, a later false call answered it too: the search changes
    /// the reference kept in memory. So the search is never run here:
    /// Searchflag=false, the saved paths, and the disk says which are missing.
    /// (When SOLIDWORKS itself LOADS a same-name file, found next to the
    /// assembly, both flags answer that file: the component is resolved, and no
    /// path tells it apart.)
    /// </summary>
    public partial class SwOperations
    {
        private Outcome Dependencies(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            object raw = null;
            try { raw = model.Extension.GetDependencies(true, false, false, true, false); }   // L12-8: Searchflag false
            catch (Exception ex) { return Failed("I cannot read the dependencies of the document: " + ex.Message); }
            var entries = raw as object[];
            var file = new List<object>();
            var missingItems = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entries != null)
                for (int i = 0; i + 1 < entries.Length; i += 2)
                {
                    string itemName = Convert.ToString(entries[i]);
                    string filePath = Convert.ToString(entries[i + 1]);
                    if (string.IsNullOrWhiteSpace(filePath) || !seen.Add(filePath)) continue;
                    // THE DISK, NOT THE PARAMETER (N60): ListBrokenRefs includes
                    // broken references in the list without marking them (the
                    // guide promises no mark; measured), so the disk says
                    // whether the file is there.
                    bool exists = false;
                    try { exists = File.Exists(filePath); } catch { }
                    if (!exists) missingItems.Add(filePath);
                    file.Add(new Dictionary<string, object>
                    {
                        ["name"] = itemName,
                        ["path"] = filePath,
                        ["exists"] = exists,
                    });
                }
            string myOwn = "";
            try { myOwn = model.GetPathName() ?? ""; } catch { }
            var measurements = new Dictionary<string, object>
            {
                ["document"] = myOwn.Length > 0 ? myOwn : null,
                ["file"] = file.ToArray(),
                ["how_many_files"] = file.Count,
                ["missing"] = missingItems.ToArray(),
                ["how_many_missing"] = missingItems.Count,
                ["how_known"] = "GetDependencies lists the files the document depends on, at every level, at the path " +
                                 "where each one was last saved (Searchflag false: the search rules can answer another " +
                                 "file with the same name, found in another folder, for a component that is not loaded; " +
                                 "measured). Whether each file is there, the disk says (File.Exists): a missing file is " +
                                 "listed with its path like the others.",
            };
            if (entries == null)
            {
                // THE API DID NOT ANSWER: «I do not know», not «zero files» — and not even
                // «zero missing» (review of 27/9/2026: how_many_missing said 0).
                measurements["how_many_files"] = null;
                measurements["missing"] = null;
                measurements["how_many_missing"] = null;
                measurements["warning"] = "GetDependencies did not answer: I do not know which files the document depends on, " +
                                       "nor whether any is missing. Do not take it for a document without dependencies.";
            }
            if (missingItems.Count > 0)
                measurements["warning"] = missingItems.Count + (missingItems.Count == 1 ? " file is missing" : " files are missing") + " on the disk: " +
                    string.Join(", ", missingItems) + ". In an assembly those components show up as suppressed: find the files " +
                    "again (or replace them with «replace_component») before measuring or exporting.";
            return SucceededWithoutCreating(measurements);
        }
    }
}
