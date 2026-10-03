using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// DA QUALI FILE DIPENDE QUESTO DOCUMENTO, E QUALI MANCANO (voce Z-B3).
    ///
    /// Un assieme non e' un documento: e' un grafo di file sul disco. Il modello
    /// guardava un assieme e non sapeva che poggia su file che stanno altrove o
    /// non ci sono piu'.
    ///
    /// Misurato il 27/9/2026 (`prova0089 zb3`, assieme base + coperchio, poi il
    /// file del coperchio rinominato sul disco e l'assieme riaperto in silenzio):
    ///
    ///  * `IModelDocExtension.GetDependencies(true, true, false, true, false)` da'
    ///    un elenco PIATTO a coppie: nome, percorso, nome, percorso.
    ///  * **ListBrokenRefs puts broken references IN the list; it does not MARK
    ///    them** (finding N60). IModelDocExtension~GetDependencies.html says only
    ///    «ListBrokenRefs — True to get broken references, false to not»: the
    ///    parameter includes them, and nothing on the page promises a mark.
    ///    Measured: the missing file is listed with its path like the others,
    ///    so the only way to know it is missing is to look at the disk
    ///    (`File.Exists`). (This bullet used to call the signature one that
    ///    «promises and does not deliver»: the guide promises no mark.)
    ///  * Riaperto con un file mancante: `OpenDoc6` errori 2, il componente
    ///    risulta SOPPRESSO (`GetSuppression2` 0) e `GetModelDoc2` null.
    ///    `GetDocumentDependencies2` sul file chiuso da' lo stesso elenco.
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
    public partial class OperazioniSw
    {
        private Esito Dipendenze(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            object grezzo = null;
            try { grezzo = modello.Extension.GetDependencies(true, false, false, true, false); }   // L12-8: Searchflag false
            catch (Exception ex) { return Fallito("Non leggo le dipendenze del documento: " + ex.Message); }
            var voci = grezzo as object[];
            var file = new List<object>();
            var mancanti = new List<string>();
            var visti = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (voci != null)
                for (int i = 0; i + 1 < voci.Length; i += 2)
                {
                    string nome = Convert.ToString(voci[i]);
                    string percorso = Convert.ToString(voci[i + 1]);
                    if (string.IsNullOrWhiteSpace(percorso) || !visti.Add(percorso)) continue;
                    // THE DISK, NOT THE PARAMETER (N60): ListBrokenRefs includes
                    // broken references in the list without marking them (the
                    // guide promises no mark; measured), so the disk says
                    // whether the file is there.
                    bool esiste = false;
                    try { esiste = File.Exists(percorso); } catch { }
                    if (!esiste) mancanti.Add(percorso);
                    file.Add(new Dictionary<string, object>
                    {
                        ["name"] = nome,
                        ["path"] = percorso,
                        ["exists"] = esiste,
                    });
                }
            string mio = "";
            try { mio = modello.GetPathName() ?? ""; } catch { }
            var misure = new Dictionary<string, object>
            {
                ["document"] = mio.Length > 0 ? mio : null,
                ["file"] = file.ToArray(),
                ["how_many_files"] = file.Count,
                ["missing"] = mancanti.ToArray(),
                ["how_many_missing"] = mancanti.Count,
                ["how_known"] = "GetDependencies lists the files the document depends on, at every level, at the path " +
                                 "where each one was last saved (Searchflag false: the search rules can answer another " +
                                 "file with the same name, found in another folder, for a component that is not loaded; " +
                                 "measured). Whether each file is there, the disk says (File.Exists): a missing file is " +
                                 "listed with its path like the others.",
            };
            if (voci == null)
            {
                // L'API NON HA RISPOSTO: «non lo so», non «zero file» — e nemmeno
                // «zero mancanti» (revisione del 27/9/2026: how_many_missing diceva 0).
                misure["how_many_files"] = null;
                misure["missing"] = null;
                misure["how_many_missing"] = null;
                misure["warning"] = "GetDependencies non ha risposto: non so da quali file dipende il documento, " +
                                       "ne' se ne manca qualcuno. Non prenderlo per un documento senza dipendenze.";
            }
            if (mancanti.Count > 0)
                misure["warning"] = mancanti.Count + (mancanti.Count == 1 ? " file manca" : " file mancano") + " sul disco: " +
                    string.Join(", ", mancanti) + ". In un assieme quei componenti risultano soppressi: ritrova i file " +
                    "(o sostituiscili con «replace_component») prima di misurare o esportare.";
            return RiuscitoSenzaCreare(misure);
        }
    }
}
