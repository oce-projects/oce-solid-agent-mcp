using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Oce.Solid.Agent.Mcp
{
    /// <summary>
    /// MCP su stdio: JSON-RPC 2.0, un messaggio per riga.
    ///
    /// PERCHE' SCRITTO A MANO E NON CON UNA LIBRERIA.
    /// Questo programma gira su .NET Framework 4.8, perche' deve caricare
    /// l'add-in vero di SolidWorks che e' compilato per quello. Le librerie MCP
    /// per .NET vogliono runtime moderni. La parte di protocollo che serve qui
    /// e' piccola — tre metodi e un formato di errore — e riscriverla costa
    /// meno che portarsi dietro un runtime in piu' sulla postazione di un
    /// progettista.
    ///
    /// UNA RIGA PER MESSAGGIO, E STDOUT E' SOLO DEL PROTOCOLLO.
    /// Qualunque cosa scritta su stdout che non sia un messaggio JSON rompe la
    /// conversazione senza dire perche': i messaggi di servizio vanno su
    /// stderr, sempre. E' la trappola numero uno di ogni server MCP su stdio.
    /// </summary>
    internal static class Protocollo
    {
        private static readonly JavaScriptSerializer Json = Creatore();

        private static JavaScriptSerializer Creatore()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 200;
            return s;
        }

        public static string InJson(object o) => Json.Serialize(o);

        public static Dictionary<string, object> DaJson(string testo)
        {
            return Json.DeserializeObject(testo) as Dictionary<string, object>;
        }

        /// <summary>Scrive un messaggio su stdout. Una riga, niente altro.</summary>
        public static void Manda(TextWriter uscita, object messaggio)
        {
            uscita.Write(InJson(messaggio));
            uscita.Write("\n");
            uscita.Flush();
        }

        public static void Nota(string testo)
        {
            Console.Error.WriteLine(testo);
            Console.Error.Flush();
        }

        public static Dictionary<string, object> Risposta(object id, object risultato)
        {
            return new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["result"] = risultato,
            };
        }

        /// <summary>
        /// Un errore di PROTOCOLLO: la richiesta era malformata o il metodo non
        /// esiste. NON si usa per un'operazione di disegno che fallisce — quello
        /// e' un risultato, e va nel contenuto con `isError`, altrimenti chi
        /// chiama non puo' distinguere «non ti ho capito» da «il pezzo non e'
        /// venuto».
        /// </summary>
        public static Dictionary<string, object> Errore(object id, int codice, string messaggio)
        {
            return new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new Dictionary<string, object>
                {
                    ["code"] = codice,
                    ["message"] = messaggio,
                },
            };
        }

        /// <summary>Il risultato di uno strumento: testo, con o senza errore.</summary>
        public static Dictionary<string, object> Contenuto(string testo, bool eUnErrore = false)
        {
            return new Dictionary<string, object>
            {
                ["content"] = new object[]
                {
                    new Dictionary<string, object> { ["type"] = "text", ["text"] = testo },
                },
                ["isError"] = eUnErrore,
            };
        }

        public const int ErroreParsing = -32700;
        public const int RichiestaNonValida = -32600;
        public const int MetodoSconosciuto = -32601;
        public const int ParametriNonValidi = -32602;
        public const int ErroreInterno = -32603;
    }
}
