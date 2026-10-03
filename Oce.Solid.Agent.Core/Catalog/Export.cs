using System.Collections.Generic;
using System.Linq;

using static Riscontro.Disegno.Catalogo.Nodo;

namespace Riscontro.Disegno.Catalogo
{
    /// <summary>
    /// Da catalogo C# a `catalogo.json`.
    ///
    /// Il JSON e' un PRODOTTO, non una fonte: si rigenera, non si modifica. Chi
    /// vuole cambiare una descrizione tocca `Operazioni.cs` e riesporta.
    /// </summary>
    public static class Esporta
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
        public static Nodo Catalogo()
        {
            var provate = Operazioni.Tutte.Where(o => o.Provata).ToList();
            var usate = new HashSet<string>(provate.Select(o => o.Famiglia));

            var famiglie = new List<object>();
            foreach (var f in Operazioni.Famiglie)
            {
                if (!usate.Contains(f.Key)) continue;
                famiglie.Add(f.Key);
                famiglie.Add(f.Value);
            }

            var elenco = new List<object>();
            foreach (var o in provate)
            {
                elenco.Add(O(
                    "name", o.Nome,
                    "family", o.Famiglia,
                    "summary", o.Sommario,
                    "details", o.Descrizione,
                    "schema", o.Schema,
                    "precondition", o.Precondizione,
                    "document", o.DocumentoRichiesto,
                    "example", o.Esempio));
            }

            return O(
                "families", O(famiglie.ToArray()),
                "operations", A(elenco.ToArray()));
        }

        /// <summary>Il catalogo scritto, pronto da salvare.</summary>
        public static string Json()
        {
            return Nodo.Json(Catalogo());
        }

        /// <summary>
        /// Le famiglie dichiarate che NON hanno nemmeno un'operazione provata.
        /// Non e' un errore — un'operazione puo' essere in lavorazione — ma chi
        /// esporta deve saperlo, perche' il caso normale e' che sia una
        /// dimenticanza.
        /// </summary>
        public static List<string> FamiglieSenzaOperazioni()
        {
            var usate = new HashSet<string>(
                Operazioni.Tutte.Where(o => o.Provata).Select(o => o.Famiglia));
            return Operazioni.Famiglie
                .Select(f => f.Key)
                .Where(k => !usate.Contains(k))
                .ToList();
        }

        /// <summary>
        /// Le operazioni che il catalogo conosce ma che non sono provate.
        /// Restano fuori dal JSON, e si dicono: un elenco che si accorcia in
        /// silenzio non si nota.
        /// </summary>
        public static List<string> NonProvate()
        {
            return Operazioni.Tutte.Where(o => !o.Provata).Select(o => o.Nome).ToList();
        }
    }
}
