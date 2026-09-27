using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;

namespace Riscontro.Disegno
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
    ///  * **Il parametro che si chiama «elenca i riferimenti rotti» NON segna
    ///    niente**: il file che non c'e' compare col suo percorso, come gli
    ///    altri. L'unico modo di sapere che manca e' guardare il disco
    ///    (`File.Exists`). Una firma che promette e non mantiene.
    ///  * Riaperto con un file mancante: `OpenDoc6` errori 2, il componente
    ///    risulta SOPPRESSO (`GetSuppression2` 0) e `GetModelDoc2` null.
    ///    `GetDocumentDependencies2` sul file chiuso da' lo stesso elenco.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito Dipendenze(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            object grezzo = null;
            try { grezzo = modello.Extension.GetDependencies(true, true, false, true, false); }
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
                    // IL DISCO, NON IL PARAMETRO: «ListBrokenRefs» non segna i
                    // mancanti (misurato), quindi si guarda se il file c'e'.
                    bool esiste = false;
                    try { esiste = File.Exists(percorso); } catch { }
                    if (!esiste) mancanti.Add(percorso);
                    file.Add(new Dictionary<string, object>
                    {
                        ["nome"] = nome,
                        ["percorso"] = percorso,
                        ["esiste"] = esiste,
                    });
                }
            string mio = "";
            try { mio = modello.GetPathName() ?? ""; } catch { }
            var misure = new Dictionary<string, object>
            {
                ["documento"] = mio.Length > 0 ? mio : null,
                ["file"] = file.ToArray(),
                ["quanti_file"] = file.Count,
                ["mancanti"] = mancanti.ToArray(),
                ["quanti_mancano"] = mancanti.Count,
                ["come_lo_so"] = "GetDependencies elenca i file da cui il documento dipende, a tutti i livelli; se " +
                                 "ciascuno c'e' lo dice il disco (File.Exists), perche' il parametro dell'API che " +
                                 "promette di segnare i riferimenti rotti non li segna (misurato).",
            };
            if (voci == null) misure["quanti_file"] = null;   // l'API non ha risposto: «non lo so», non «zero file»
            if (mancanti.Count > 0)
                misure["attenzione"] = mancanti.Count + (mancanti.Count == 1 ? " file manca" : " file mancano") + " sul disco: " +
                    string.Join(", ", mancanti) + ". In un assieme quei componenti risultano soppressi: ritrova i file " +
                    "(o sostituiscili con «sostituisci_componente») prima di misurare o esportare.";
            return RiuscitoSenzaCreare(misure);
        }
    }
}
