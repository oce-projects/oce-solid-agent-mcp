using System.Collections.Generic;
using System.Linq;

using static Oce.Solid.Agent.Core.Catalog.Node;

namespace Oce.Solid.Agent.Core.Catalog
{
    /// <summary>
    /// Da catalogo C# a `catalog.json`.
    ///
    /// Il JSON e' un PRODOTTO, non una fonte: si rigenera, non si modifica. Chi
    /// vuole cambiare una descrizione tocca `Operations.cs` e riesporta.
    /// </summary>
    public static class Export
    {
        /// <summary>
        /// Il catalogo come lo legge il ponte.
        ///
        /// **Solo le operazioni provate.** Un'operazione che compila ma non e'
        /// mai stata eseguita su un pezzo vero non esiste, per il modello: se
        /// entrasse in catalogo, il modello la chiederebbe e si prenderebbe un
        /// errore per una cosa che gli era stata promessa.
        ///
        /// **Solo le famiglie che hanno almeno un'operazione.** Una famiglia
        /// dichiarata e vuota arriva al modello come una porta che non apre su
        /// niente — ed e' un difetto gia' successo una volta, nell'esportatore
        /// Python di prima.
        /// </summary>
        public static Node Catalog()
        {
            var tested = Operations.All.Where(o => o.Tested).ToList();
            var used = new HashSet<string>(tested.Select(o => o.Family));

            var families = new List<object>();
            foreach (var f in Operations.Families)
            {
                if (!used.Contains(f.Key)) continue;
                families.Add(f.Key);
                families.Add(f.Value);
            }

            var list = new List<object>();
            foreach (var o in tested)
            {
                list.Add(O(
                    "name", o.Name,
                    "family", o.Family,
                    "summary", o.Summary,
                    "details", o.Description,
                    "schema", o.Schema,
                    "precondition", o.Precondition,
                    "document", o.RequestedDocument,
                    "example", o.Example));
            }

            return O(
                "families", O(families.ToArray()),
                "operations", A(list.ToArray()));
        }

        /// <summary>Il catalogo scritto, pronto da salvare.</summary>
        public static string Json()
        {
            return Node.Json(Catalog());
        }

        /// <summary>
        /// Le famiglie dichiarate che NON hanno nemmeno un'operazione provata.
        /// Non e' un errore — un'operazione puo' essere in lavorazione — ma chi
        /// esporta deve saperlo, perche' il caso normale e' che sia una
        /// dimenticanza.
        /// </summary>
        public static List<string> FamiliesWithoutOperations()
        {
            var used = new HashSet<string>(
                Operations.All.Where(o => o.Tested).Select(o => o.Family));
            return Operations.Families
                .Select(f => f.Key)
                .Where(k => !used.Contains(k))
                .ToList();
        }

        /// <summary>
        /// Le operazioni che il catalogo conosce ma che non sono provate.
        /// Restano fuori dal JSON, e si dicono: un elenco che si accorcia in
        /// silenzio non si nota.
        /// </summary>
        public static List<string> NotTested()
        {
            return Operations.All.Where(o => !o.Tested).Select(o => o.Name).ToList();
        }
    }
}
