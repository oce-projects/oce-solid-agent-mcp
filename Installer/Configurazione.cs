using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Oce.Solid.Agent.Installer
{
    /// <summary>Un client MCP che si puo' configurare da qui.</summary>
    internal sealed class Client
    {
        public string Nome;
        public string File;
        public bool Installato;
        /// <summary>Come si chiama il processo, per sapere se e' aperto adesso.</summary>
        public string Processo;
    }

    /// <summary>
    /// COLLEGARE CLAUDE A SOLIDWORKS.
    ///
    /// E' questo il lavoro vero dell'installatore. Copiare due file lo sa fare
    /// chiunque; il punto dove ci si perde e' l'ultimo: aprire un file JSON
    /// nascosto in %APPDATA%, capire dove va incollato il blocco, non rompere
    /// quello che c'era gia', e sapere che Claude va riavviato perche' legga.
    ///
    /// TRE REGOLE, TUTTE E TRE PAGATE DA QUALCUN ALTRO PRIMA DI NOI:
    ///
    /// 1. NON SI SOVRASCRIVE IL FILE. Chi usa Claude ha spesso altri server
    ///    MCP configurati. Un installatore che scrive il suo blocco e basta
    ///    glieli cancella tutti, e se ne accorgera' domani senza collegare la
    ///    cosa all'installazione di ieri. Si legge, si aggiunge la propria
    ///    voce, si riscrive tutto il resto com'era.
    ///
    /// 2. SI FA UNA COPIA PRIMA. Sempre, anche quando il file sembra semplice.
    ///    Costa un millisecondo e vale la differenza fra un errore e un danno.
    ///
    /// 3. SI DICE DI RIAVVIARE. Claude legge questo file all'avvio. Senza
    ///    riavvio non succede niente, e «non succede niente» e' il modo piu'
    ///    rapido di far concludere che l'installazione e' fallita.
    /// </summary>
    internal static class Configurazione
    {
        public const string NomeServer = "solidworks";

        private static JavaScriptSerializer Json()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 200;
            return s;
        }

        // ─────────────────────────────────────────────────────────────────
        // Quali client ci sono su questa macchina
        // ─────────────────────────────────────────────────────────────────

        public static List<Client> ClientTrovati()
        {
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var profilo = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var tutti = new List<Client>
            {
                new Client
                {
                    Nome = "Claude Desktop",
                    File = Path.Combine(appdata, @"Claude\claude_desktop_config.json"),
                    Processo = "Claude",
                },
                new Client
                {
                    Nome = "Claude Code",
                    File = Path.Combine(profilo, ".claude.json"),
                    Processo = null,
                },
            };

            foreach (var c in tutti)
            {
                try
                {
                    // «Installato» vuol dire che c'e' la CARTELLA del client:
                    // il file di configurazione puo' non esistere ancora, ed e'
                    // il caso normale di chi non ha mai aggiunto un server MCP.
                    var cartella = Path.GetDirectoryName(c.File);
                    c.Installato = Directory.Exists(cartella) || File.Exists(c.File);
                }
                catch { c.Installato = false; }
            }
            return tutti;
        }

        public static bool EAperto(Client client)
        {
            if (client == null || string.IsNullOrWhiteSpace(client.Processo)) return false;
            try { return Process.GetProcessesByName(client.Processo).Length > 0; }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────────────────
        // Scrivere
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Aggiunge (o aggiorna) la voce del ponte nella configurazione del
        /// client, lasciando intatto tutto il resto. Ritorna il percorso della
        /// copia di sicurezza, o null se il file non esisteva.
        /// </summary>
        public static string Collega(
            Client client, string eseguibile, string redist, Action<string> racconta)
        {
            if (client == null) throw new Exception("Nessun client scelto.");
            if (string.IsNullOrWhiteSpace(eseguibile) || !File.Exists(eseguibile))
                throw new Exception("Non trovo il ponte da collegare: " + eseguibile);

            var radice = Leggi(client.File);
            var server = Sotto(radice, "mcpServers");

            var voce = new Dictionary<string, object>
            {
                ["command"] = eseguibile,
            };

            // `env` solo se serve davvero: una variabile scritta «tanto per»
            // e' una cosa in piu' che puo' diventare sbagliata quando qualcuno
            // sposta SOLIDWORKS, e nessuno andra' a rileggerla.
            if (!string.IsNullOrWhiteSpace(redist) && !RedistStandard(redist))
            {
                voce["env"] = new Dictionary<string, object> { ["SW_REDIST"] = redist };
                racconta("aggiungo SW_REDIST: SOLIDWORKS non e' nel percorso solito");
            }

            bool cera = server.ContainsKey(NomeServer);
            server[NomeServer] = voce;

            var copia = Scrivi(client.File, radice, racconta);
            racconta((cera ? "aggiornata" : "aggiunta") + " la voce «" + NomeServer +
                     "» in " + client.Nome);
            return copia;
        }

        /// <summary>Toglie la voce del ponte, lasciando gli altri server.</summary>
        public static void Scollega(Client client, Action<string> racconta)
        {
            if (client == null || !File.Exists(client.File))
            {
                racconta("niente da scollegare");
                return;
            }
            var radice = Leggi(client.File);
            var server = Sotto(radice, "mcpServers");
            if (!server.Remove(NomeServer))
            {
                racconta("la voce non c'era");
                return;
            }
            Scrivi(client.File, radice, racconta);
            racconta("tolta la voce da " + client.Nome);
        }

        /// <summary>Cosa c'e' scritto adesso per questo server, se c'e'.</summary>
        public static string ComeEConfigurato(Client client)
        {
            try
            {
                if (client == null || !File.Exists(client.File)) return null;
                var server = Sotto(Leggi(client.File), "mcpServers");
                object voce;
                if (!server.TryGetValue(NomeServer, out voce)) return null;
                var d = voce as Dictionary<string, object>;
                if (d == null) return null;
                object c;
                return d.TryGetValue("command", out c) ? Convert.ToString(c) : null;
            }
            catch { return null; }
        }

        // ─────────────────────────────────────────────────────────────────
        // Leggere e scrivere il file
        // ─────────────────────────────────────────────────────────────────

        private static Dictionary<string, object> Leggi(string file)
        {
            try
            {
                if (!File.Exists(file)) return new Dictionary<string, object>();
                var testo = File.ReadAllText(file, Encoding.UTF8).Trim();
                if (testo.Length == 0) return new Dictionary<string, object>();
                var d = Json().DeserializeObject(testo) as Dictionary<string, object>;
                return d ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                // Un file rotto NON si sostituisce in silenzio con uno nuovo:
                // dentro potrebbero esserci dieci server configurati a mano.
                throw new Exception(
                    "Il file di configurazione «" + file + "» non e' JSON valido e non " +
                    "me la sento di riscriverlo: dentro potrebbe esserci altro lavoro. " +
                    "Aprilo e sistemalo, poi riprova. (" + ex.Message + ")");
            }
        }

        private static Dictionary<string, object> Sotto(
            Dictionary<string, object> radice, string chiave)
        {
            object v;
            if (radice.TryGetValue(chiave, out v))
            {
                var d = v as Dictionary<string, object>;
                if (d != null) return d;
            }
            var nuovo = new Dictionary<string, object>();
            radice[chiave] = nuovo;
            return nuovo;
        }

        private static string Scrivi(
            string file, Dictionary<string, object> radice, Action<string> racconta)
        {
            var cartella = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(cartella)) Directory.CreateDirectory(cartella);

            string copia = null;
            if (File.Exists(file))
            {
                // Il nome della copia porta la data: cosi' se ne accumulano
                // poche e si capisce quale e' quale.
                copia = file + ".prima-di-oce-solid-agent-" +
                        DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
                File.Copy(file, copia, true);
                racconta("copia di sicurezza: " + Path.GetFileName(copia));
            }

            var testo = Indenta(Json().Serialize(radice));

            var temporaneo = file + ".parziale";
            using (var w = new StreamWriter(temporaneo, false, new UTF8Encoding(false)))
                w.Write(testo);
            if (File.Exists(file)) File.Delete(file);
            File.Move(temporaneo, file);

            return copia;
        }

        /// <summary>
        /// Rimette a capo e indenta il JSON.
        ///
        /// `JavaScriptSerializer` scrive tutto su una riga sola. Tecnicamente
        /// e' JSON valido; praticamente vuol dire che chi apre il file dopo di
        /// noi trova una riga da duemila caratteri dove prima c'era qualcosa di
        /// leggibile, e non puo' piu' correggerlo a mano. Un installatore che
        /// rende illeggibile un file di configurazione dell'utente ha fatto un
        /// danno anche se il contenuto e' giusto.
        ///
        /// Si lavora sul testo e non sull'oggetto perche' serve solo spaziatura:
        /// l'unica cosa da cui stare attenti sono le stringhe, dove parentesi e
        /// virgole non sono struttura.
        /// </summary>
        private static string Indenta(string json)
        {
            var fuori = new StringBuilder(json.Length * 2);
            int livello = 0;
            bool inStringa = false, scappato = false;

            foreach (char c in json)
            {
                if (inStringa)
                {
                    fuori.Append(c);
                    if (scappato) scappato = false;
                    else if (c == '\\') scappato = true;
                    else if (c == '"') inStringa = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inStringa = true;
                        fuori.Append(c);
                        break;
                    case '{':
                    case '[':
                        fuori.Append(c);
                        livello++;
                        fuori.Append('\n').Append(new string(' ', livello * 2));
                        break;
                    case '}':
                    case ']':
                        livello--;
                        fuori.Append('\n').Append(new string(' ', Math.Max(0, livello) * 2));
                        fuori.Append(c);
                        break;
                    case ',':
                        fuori.Append(c).Append('\n').Append(new string(' ', livello * 2));
                        break;
                    case ':':
                        fuori.Append(": ");
                        break;
                    default:
                        fuori.Append(c);
                        break;
                }
            }
            return fuori.ToString().Replace("{\n" + new string(' ', 2) + "\n}", "{}") + "\n";
        }

        private static bool RedistStandard(string redist)
        {
            try
            {
                var standard = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"SOLIDWORKS Corp\SOLIDWORKS\api\redist");
                return Passi.PercorsiUguali(redist, standard);
            }
            catch { return false; }
        }

        /// <summary>Il blocco da incollare a mano, per chi usa un altro client.</summary>
        public static string BloccoDaIncollare(string eseguibile, string redist)
        {
            var voce = new Dictionary<string, object> { ["command"] = eseguibile };
            if (!string.IsNullOrWhiteSpace(redist) && !RedistStandard(redist))
                voce["env"] = new Dictionary<string, object> { ["SW_REDIST"] = redist };

            var radice = new Dictionary<string, object>
            {
                ["mcpServers"] = new Dictionary<string, object> { [NomeServer] = voce },
            };
            return Indenta(Json().Serialize(radice));
        }
    }
}
