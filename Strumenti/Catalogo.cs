using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Oce.Solid.Agent.Strumenti
{
    /// <summary>
    /// Il catalogo delle operazioni, letto da `catalogo.json`.
    ///
    /// UNA SOLA FONTE DI VERITA', E UNA COPIA ONESTA.
    /// Il catalogo vero vive nel server, in `ai_disegno.py`: li' stanno le
    /// descrizioni scritte per essere lette da un modello, gli schemi, i
    /// limiti, e il campo `provata` che decide cosa esiste. Questo ponte e'
    /// scritto in C# e non puo' importare Python, quindi si porta dietro una
    /// copia — rigenerata con un comando (`Strumenti/aggiorna.md`), mai
    /// scritta a mano.
    ///
    /// NEL FILE CI SONO SOLO LE OPERAZIONI PROVATE. E' la regola del catalogo
    /// a due livelli: un'operazione che non e' stata costruita dentro
    /// SolidWorks e misurata non si offre a nessuno, nemmeno di qui.
    /// </summary>
    internal sealed class Catalogo
    {
        private readonly List<Dictionary<string, object>> _operazioni =
            new List<Dictionary<string, object>>();
        private Dictionary<string, object> _famiglie = new Dictionary<string, object>();

        public string Percorso { get; private set; }
        public int Quante => _operazioni.Count;
        public string ErroreDiCaricamento { get; private set; }

        public Catalogo()
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
            Percorso = Environment.GetEnvironmentVariable("RISCONTRO_CATALOGO");
            if (string.IsNullOrWhiteSpace(Percorso))
                Percorso = Environment.GetEnvironmentVariable("OPENPDM_CATALOGO");
            if (string.IsNullOrWhiteSpace(Percorso))
                Percorso = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
                    "catalogo.json");

            if (!File.Exists(Percorso))
            {
                ErroreDiCaricamento =
                    "Non trovo il catalogo in «" + Percorso + "». Senza, il ponte " +
                    "sa eseguire ma non sa dire cosa si puo' chiedere.";
                return;
            }

            try
            {
                var radice = Mcp.Protocollo.DaJson(File.ReadAllText(Percorso));
                _famiglie = Prendi(radice, "famiglie") as Dictionary<string, object>
                            ?? new Dictionary<string, object>();
                var elenco = Prendi(radice, "operazioni") as object[];
                if (elenco != null)
                    foreach (var o in elenco)
                        if (o is Dictionary<string, object> voce) _operazioni.Add(voce);
            }
            catch (Exception ex)
            {
                ErroreDiCaricamento = "Il catalogo non si legge: " + ex.Message;
            }
        }

        private static object Prendi(Dictionary<string, object> d, string chiave)
        {
            if (d == null) return null;
            return d.TryGetValue(chiave, out var v) ? v : null;
        }

        public bool Conosce(string nome) => Trova(nome) != null;

        /// <summary>
        /// Lo schema grezzo di un'operazione, per chi deve confrontarci gli
        /// argomenti. `Descrizione` lo impacchetta per il modello; qui
        /// serve nudo.
        /// </summary>
        public Dictionary<string, object> Schema(string nome)
        {
            var op = Trova(nome);
            return op == null ? null : Prendi(op, "schema") as Dictionary<string, object>;
        }

        public Dictionary<string, object> Trova(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return null;
            return _operazioni.FirstOrDefault(o =>
                string.Equals(Convert.ToString(Prendi(o, "nome")), nome,
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// La famiglia di un'operazione, o `null` se l'operazione non c'e'.
        ///
        /// Serve allo strumento `misura`, che deve poter dire di NO a
        /// un'operazione che tocca il pezzo — e per dirlo deve sapere di che
        /// famiglia e'. E' un METODO NUOVO e non un parametro in piu' su
        /// `Trova`: il banco e le prove chiamano questa classe per riflessione,
        /// e cambiare l'insieme delle firme rompe chi chiama anche quando il
        /// compilatore tace.
        /// </summary>
        public string Famiglia(string nome)
        {
            var op = Trova(nome);
            return op == null ? null : Convert.ToString(Prendi(op, "famiglia"));
        }

        public IEnumerable<string> Nomi =>
            _operazioni.Select(o => Convert.ToString(Prendi(o, "nome")));

        /// <summary>
        /// L'elenco corto: nome e una riga, raggruppato per famiglia.
        ///
        /// Non si mandano gli schemi interi: sono decine di migliaia di
        /// caratteri, e chi legge ne usa due o tre. Lo schema si chiede quando
        /// serve, con `descrivi_operazione` — e' il catalogo a due livelli,
        /// che qui vale per lo stesso motivo per cui vale nel server.
        /// </summary>
        public object Elenco(string famigliaVoluta)
        {
            var perFamiglia = new List<object>();
            var famiglie = _operazioni
                .Select(o => Convert.ToString(Prendi(o, "famiglia")))
                .Distinct()
                .OrderBy(f => f, StringComparer.Ordinal);

            foreach (var f in famiglie)
            {
                if (!string.IsNullOrWhiteSpace(famigliaVoluta) &&
                    !string.Equals(f, famigliaVoluta, StringComparison.OrdinalIgnoreCase))
                    continue;

                var voci = _operazioni
                    .Where(o => Convert.ToString(Prendi(o, "famiglia")) == f)
                    .Select(o => new Dictionary<string, object>
                    {
                        ["nome"] = Prendi(o, "nome"),
                        ["fa"] = Prendi(o, "sommario"),
                    })
                    .ToArray();

                perFamiglia.Add(new Dictionary<string, object>
                {
                    ["famiglia"] = f,
                    ["serve_a"] = Prendi(_famiglie, f),
                    ["operazioni"] = voci,
                });
            }

            return new Dictionary<string, object>
            {
                ["totale"] = _operazioni.Count,
                ["famiglie"] = perFamiglia,
                ["come_si_usa"] =
                    "Chiedi «descrivi_operazione» per avere lo schema completo " +
                    "di quella che ti serve, poi «disegna».",
            };
        }

        public object Descrizione(string nome)
        {
            var op = Trova(nome);
            if (op == null) return null;
            return new Dictionary<string, object>
            {
                ["nome"] = Prendi(op, "nome"),
                ["famiglia"] = Prendi(op, "famiglia"),
                ["serve_prima"] = Prendi(op, "precondizione"),
                ["descrizione"] = Prendi(op, "descrizione"),
                ["argomenti"] = Prendi(op, "schema"),
            };
        }

        /// <summary>
        /// I nomi che somigliano, per un errore che aiuta invece di
        /// constatare.
        ///
        /// SI CONFRONTANO LE RADICI, non le parole. Il primo tentativo
        /// cercava la parola intera dentro il nome, e su «asole» non
        /// trovava «asola» — cioe' falliva proprio nel caso per cui era
        /// stato scritto: chi sbaglia un nome quasi sempre ne ha in mente
        /// uno vicino, e lo sbaglia di una lettera.
        /// </summary>
        public List<string> Simili(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return new List<string>();
            var pezzi = nome.ToLowerInvariant().Split('_')
                .Where(p => p.Length >= 4)
                .Select(p => p.Substring(0, Math.Min(p.Length - 1, 6)))
                .ToList();
            if (pezzi.Count == 0) return new List<string>();
            return Nomi
                .Where(n => pezzi.Any(p => n.ToLowerInvariant().Contains(p)))
                .Take(5)
                .ToList();
        }
    }
}
