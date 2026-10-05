using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Oce.Solid.Agent.Tools
{
    /// <summary>
    /// Il catalogo delle operazioni, letto da `catalog.json`.
    ///
    /// UNA SOLA FONTE DI VERITA', E UNA COPIA ONESTA.
    /// Il catalogo vero vive nel server, in `ai_disegno.py`: li' stanno le
    /// descrizioni scritte per essere lette da un modello, gli schemi, i
    /// limiti, e il campo `provata` che decide cosa esiste. Questo ponte e'
    /// scritto in C# e non puo' importare Python, quindi si porta dietro una
    /// copia — rigenerata con un comando (`Tools/update.md`), mai
    /// scritta a mano.
    ///
    /// NEL FILE CI SONO SOLO LE OPERAZIONI PROVATE. E' la regola del catalogo
    /// a due livelli: un'operazione che non e' stata costruita dentro
    /// SolidWorks e misurata non si offre a nessuno, nemmeno di qui.
    /// </summary>
    internal sealed class Catalog
    {
        private readonly List<Dictionary<string, object>> _operations =
            new List<Dictionary<string, object>>();
        private Dictionary<string, object> _families = new Dictionary<string, object>();

        public string FilePath { get; private set; }
        public int HowMany => _operations.Count;
        public string LoadingError { get; private set; }

        public Catalog()
        {
            // ── DUE NOMI, E UNO SOLO E' QUELLO DI QUESTO PRODOTTO ───────
            //
            // `RISCONTRO_CATALOGO` e' il nome nuovo. `OPENPDM_CATALOGO` era il
            // vecchio, e **resta letto**: chi l'ha impostata su una macchina
            // installata non deve accorgersi di niente, e una variabile che
            // smette di funzionare senza dirlo e' il difetto che non da'
            // errore — il catalogo non si carica, il ponte esegue lo stesso, e
            // nessuno sa piu' dire cosa si puo' chiedere.
            //
            // L'ordine conta: se sono impostate tutte e due vince la nuova.
            // Chi ne imposta due diverse ha gia' un guaio, e almeno sa quale
            // delle due comanda.
            FilePath = Environment.GetEnvironmentVariable("RISCONTRO_CATALOGO");
            if (string.IsNullOrWhiteSpace(FilePath))
                FilePath = Environment.GetEnvironmentVariable("OPENPDM_CATALOGO");
            if (string.IsNullOrWhiteSpace(FilePath))
                FilePath = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
                    "catalog.json");

            if (!File.Exists(FilePath))
            {
                LoadingError =
                    "Non trovo il catalogo in «" + FilePath + "». Senza, il ponte " +
                    "sa eseguire ma non sa dire cosa si puo' chiedere.";
                return;
            }

            try
            {
                var root = Mcp.Protocol.FromJson(File.ReadAllText(FilePath));
                _families = Take(root, "families") as Dictionary<string, object>
                            ?? new Dictionary<string, object>();
                var list = Take(root, "operations") as object[];
                if (list != null)
                    foreach (var o in list)
                        if (o is Dictionary<string, object> entry) _operations.Add(entry);
            }
            catch (Exception ex)
            {
                LoadingError = "Il catalogo non si legge: " + ex.Message;
            }
        }

        private static object Take(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            return d.TryGetValue(key, out var v) ? v : null;
        }

        public bool Knows(string itemName) => Find(itemName) != null;

        /// <summary>
        /// Lo schema grezzo di un'operazione, per chi deve confrontarci gli
        /// argomenti. `Description` lo impacchetta per il modello; qui
        /// serve nudo.
        /// </summary>
        public Dictionary<string, object> Schema(string itemName)
        {
            var op = Find(itemName);
            return op == null ? null : Take(op, "schema") as Dictionary<string, object>;
        }

        public Dictionary<string, object> Find(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return null;
            return _operations.FirstOrDefault(o =>
                string.Equals(Convert.ToString(Take(o, "name")), itemName,
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// La famiglia di un'operazione, o `null` se l'operazione non c'e'.
        ///
        /// Serve allo strumento `measure`, che deve poter dire di NO a
        /// un'operazione che tocca il pezzo — e per dirlo deve sapere di che
        /// famiglia e'. E' un METODO NUOVO e non un parametro in piu' su
        /// `Find`: il banco e le prove chiamano questa classe per riflessione,
        /// e cambiare l'insieme delle firme rompe chi chiama anche quando il
        /// compilatore tace.
        /// </summary>
        public string Family(string itemName)
        {
            var op = Find(itemName);
            return op == null ? null : Convert.ToString(Take(op, "family"));
        }

        public IEnumerable<string> Names =>
            _operations.Select(o => Convert.ToString(Take(o, "name")));

        /// <summary>
        /// L'elenco corto: nome e una riga, raggruppato per famiglia.
        ///
        /// Non si mandano gli schemi interi: sono decine di migliaia di
        /// caratteri, e chi legge ne usa due o tre. Lo schema si chiede quando
        /// serve, con `describe_operation` — e' il catalogo a due livelli,
        /// che qui vale per lo stesso motivo per cui vale nel server.
        /// </summary>
        public object Items(string wantedFamily)
        {
            var perFamily = new List<object>();
            var families = _operations
                .Select(o => Convert.ToString(Take(o, "family")))
                .Distinct()
                .OrderBy(f => f, StringComparer.Ordinal);

            foreach (var f in families)
            {
                if (!string.IsNullOrWhiteSpace(wantedFamily) &&
                    !string.Equals(f, wantedFamily, StringComparison.OrdinalIgnoreCase))
                    continue;

                var entries = _operations
                    .Where(o => Convert.ToString(Take(o, "family")) == f)
                    .Select(o => new Dictionary<string, object>
                    {
                        ["name"] = Take(o, "name"),
                        ["does"] = Take(o, "summary"),
                    })
                    .ToArray();

                perFamily.Add(new Dictionary<string, object>
                {
                    ["family"] = f,
                    ["purpose"] = Take(_families, f),
                    ["operations"] = entries,
                });
            }

            return new Dictionary<string, object>
            {
                ["total"] = _operations.Count,
                ["families"] = perFamily,
                ["how_to_use_it"] =
                    "Chiedi «describe_operation» per avere lo schema completo " +
                    "di quella che ti serve, poi «build».",
            };
        }

        public object Description(string itemName)
        {
            var op = Find(itemName);
            if (op == null) return null;
            return new Dictionary<string, object>
            {
                ["name"] = Take(op, "name"),
                ["family"] = Take(op, "family"),
                ["precondition"] = Take(op, "precondition"),
                ["details"] = Take(op, "details"),
                ["arguments"] = Take(op, "schema"),
            };
        }

        /// <summary>
        /// I nomi che somigliano, per un errore che aiuta invece di
        /// constatare.
        ///
        /// SI CONFRONTANO LE RADICI, non le parole. Il primo tentativo
        /// cercava la parola intera dentro il nome, e su «asole» non
        /// trovava «slot» — cioe' falliva proprio nel caso per cui era
        /// stato scritto: chi sbaglia un nome quasi sempre ne ha in mente
        /// uno vicino, e lo sbaglia di una lettera.
        /// </summary>
        public List<string> Similar(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return new List<string>();
            var parts = itemName.ToLowerInvariant().Split('_')
                .Where(p => p.Length >= 4)
                .Select(p => p.Substring(0, Math.Min(p.Length - 1, 6)))
                .ToList();
            if (parts.Count == 0) return new List<string>();
            return Names
                .Where(n => parts.Any(p => n.ToLowerInvariant().Contains(p)))
                .Take(5)
                .ToList();
        }
    }
}
