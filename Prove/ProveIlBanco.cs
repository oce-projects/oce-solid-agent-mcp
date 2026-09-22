using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// IL BANCO E' STATO LANCIATO SU QUESTA VERSIONE? (voce F9)
    ///
    /// ── PERCHE' UNA PROVA SENZA SOLIDWORKS PUO' DIRLO ────────────────────
    ///
    /// La CI non puo' pilotare SOLIDWORKS — nessun runner con licenza — ma
    /// puo' fare una cosa che vale quasi altrettanto: **rifiutare una consegna
    /// in cui nessuno ha lanciato il banco.** E' il pezzo di disciplina che si
    /// compra a due ore, invece del runner con licenza che non si compra
    /// affatto.
    ///
    /// L'unica prova che il banco sia girato e' `Banco/resoconto/ultimo.json`,
    /// che `banco.ps1` scrive a ogni giro ed e' l'unico file di `resoconto/`
    /// versionato.
    ///
    /// ── E IL DIFETTO SILENZIOSO DA TEMERE ───────────────────────────────
    ///
    /// Una CI che compila un progetto vuoto perche' il filtro dei csproj e'
    /// sbagliato, e resta verde per mesi. Per questo qui non si guarda solo che
    /// il file ci sia: si guarda che le prove siano state **piu' di zero**, che
    /// nessuna sia fallita, e che il giro non sia **piu' vecchio dell'ultima
    /// modifica alle operazioni**.
    /// </summary>
    internal static class ProveIlBanco
    {
        internal static void Tutte()
        {
            Gruppo("il banco e' stato lanciato su questa versione");

            var file = Path.Combine(Radice(), "Banco", "resoconto", "ultimo.json");
            if (!File.Exists(file))
            {
                Segna("il banco e' stato eseguito", false,
                      "il banco non e' stato eseguito su questa versione: manca " + file +
                      ". Si lancia con .\\Banco\\banco.ps1");
                return;
            }
            Segna("il banco e' stato eseguito", true, file);

            Dictionary<string, object> giro;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                giro = s.DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
            }
            catch (Exception ex) { Segna("il resoconto si legge", false, ex.Message); return; }
            if (giro == null) { Segna("il resoconto e' un oggetto", false, ""); return; }

            // ── PIU' DI ZERO PROVE ───────────────────────────────────────
            //
            // «0 prove, 0 fallite» e' verde e non vuol dire niente. E' la
            // forma che prende una suite che si e' svuotata.
            int passate = Intero(giro, "passate");
            Segna("il giro ha misurato qualcosa", passate > 0, passate + " prove passate");
            Segna("nessuna prova fallita", Intero(giro, "fallite") == 0,
                  Intero(giro, "fallite") + " fallite");
            Segna("nessun errore di banco", Intero(giro, "errori") == 0,
                  Intero(giro, "errori") + " errori di banco");
            Segna("il giro e' uscito 0", Intero(giro, "uscita") == 0,
                  "uscita " + Intero(giro, "uscita"));

            // ── NON UN GIRO PARZIALE ─────────────────────────────────────
            //
            // `--gruppo` e' comodo mentre si lavora e non vale come consegna:
            // il riepilogo lo dice, e qui si legge.
            string riepilogo = Convert.ToString(giro.ContainsKey("riepilogo") ? giro["riepilogo"] : "");
            Segna("non e' un giro parziale",
                  riepilogo.IndexOf("GIRO PARZIALE", StringComparison.Ordinal) < 0,
                  riepilogo);

            // ── LA VERSIONE DELLA LIBRERIA E' QUELLA DI ADESSO ───────────
            string dichiarata = VersioneDellAssembly();
            string girata = Convert.ToString(giro.ContainsKey("libreria") ? giro["libreria"] : "");
            Segna("il banco ha girato sulla libreria di adesso",
                  dichiarata != null && girata.StartsWith(dichiarata, StringComparison.Ordinal),
                  "AssemblyInfo dice " + (dichiarata ?? "?") + ", il giro dice " + girata);

            // ── E NON PRIMA DELL'ULTIMA MODIFICA ALLE OPERAZIONI ────────
            //
            // Il controllo che conta: una libreria toccata dopo l'ultimo giro
            // e' una libreria non provata, anche se il numero di versione
            // combacia perche' qualcuno si e' dimenticato di alzarlo.
            DateTime? quando = Data(Convert.ToString(giro.ContainsKey("quando") ? giro["quando"] : ""));
            DateTime? ultimoTocco = UltimaModificaAlleOperazioni();
            if (quando == null) Segna("il giro dice quando", false, "«quando» illeggibile");
            else if (ultimoTocco == null)
                Segna("so quando le operazioni sono state toccate", false,
                      "git non risponde: senza, questo controllo non esiste");
            else
                Segna("il giro e' posteriore all'ultima modifica alle operazioni",
                      quando.Value >= ultimoTocco.Value.AddMinutes(-1),
                      "giro " + quando.Value.ToString("s") +
                      ", ultima modifica " + ultimoTocco.Value.ToString("s"));
        }

        private static int Intero(Dictionary<string, object> d, string chiave)
        {
            object v;
            if (d == null || !d.TryGetValue(chiave, out v) || v == null) return -1;
            try { return Convert.ToInt32(v); } catch { return -1; }
        }

        private static DateTime? Data(string s)
        {
            DateTime d;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture,
                                     DateTimeStyles.AssumeLocal, out d) ? (DateTime?)d : null;
        }

        /// <summary>
        /// `AssemblyVersion` letta dal SORGENTE, non dalla DLL compilata: qui
        /// si vuole sapere cosa dichiara il repository, non cosa c'e' in `bin`
        /// — che potrebbe essere di ieri.
        /// </summary>
        private static string VersioneDellAssembly()
        {
            try
            {
                var f = Path.Combine(Radice(), "Riscontro.Disegno", "Properties", "AssemblyInfo.cs");
                if (!File.Exists(f)) return null;
                var m = Regex.Match(File.ReadAllText(f),
                                    @"AssemblyVersion\(""([0-9]+\.[0-9]+\.[0-9]+)");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Quando le operazioni sono state toccate l'ultima volta, secondo git.
        /// Null se git non c'e' o non risponde — e in quel caso il controllo
        /// lo DICE invece di passare.
        /// </summary>
        /// <summary>
        /// Quando la libreria e' stata toccata l'ultima volta SENZA che nello
        /// stesso commit entrasse anche un resoconto del banco.
        ///
        /// ── PRIMA QUESTO CONTROLLO NON POTEVA PASSARE MAI ─────────────────
        ///
        /// Chiedeva `git log -1 -- Riscontro.Disegno` e confrontava quella data
        /// con l'ora del giro. Ma il giro si lancia, scrive `ultimo.json`, e
        /// POI si committa tutto insieme — libreria e resoconto nello stesso
        /// commit. La data del commit e' quindi per costruzione posteriore
        /// all'ora del giro che contiene. Misurato su b35550d: giro 21:12:13,
        /// commit 21:13:52, novantanove secondi dopo contro una tolleranza di
        /// sessanta.
        ///
        /// Non mancava per poco: mancava sempre. *Un controllo che non puo'
        /// diventare verde e' peggio di nessun controllo — si legge come una
        /// difesa, fallisce a ogni giro, e chi lo guarda impara a non
        /// guardarlo.*
        ///
        /// ── LA DOMANDA VERA, E COME SI RISPONDE ───────────────────────────
        ///
        /// Non «quand'e' l'ultimo commit sulla libreria», ma **la libreria e'
        /// stata toccata dopo l'ultimo giro?**. Un commit che porta con se' un
        /// resoconto nuovo E' il giro: e' la consegna, e il resoconto che
        /// contiene la valida. Un commit che tocca la libreria e NON porta un
        /// resoconto nuovo e' invece esattamente il caso da prendere —
        /// qualcuno ha cambiato il codice e non ha rilanciato.
        ///
        /// Quindi si scorrono i commit sulla libreria e ci si ferma al primo
        /// che non tocca anche `Banco/resoconto/ultimo.json`.
        /// </summary>
        private static DateTime? UltimaModificaAlleOperazioni()
        {
            try
            {
                foreach (var riga in Git("log -30 --format=%H|%cI -- Riscontro.Disegno"))
                {
                    var pezzi = riga.Split('|');
                    if (pezzi.Length < 2) continue;
                    if (CommitPortaIlResoconto(pezzi[0])) continue;
                    return Data(pezzi[1]);
                }
                // Nessun commit sulla libreria senza resoconto: e' il caso di
                // un repository giovane, e non c'e' niente di non provato.
                return null;
            }
            catch { return null; }
        }

        /// <summary>Questo commit porta anche `Banco/resoconto/ultimo.json`?</summary>
        private static bool CommitPortaIlResoconto(string sha)
        {
            foreach (var r in Git("show --name-only --format= " + sha +
                                  " -- Banco/resoconto/ultimo.json"))
                if (!string.IsNullOrWhiteSpace(r)) return true;
            return false;
        }

        private static List<string> Git(string argomenti)
        {
            var fuori = new List<string>();
            var p = new Process
            {
                StartInfo = new ProcessStartInfo("git", argomenti)
                {
                    WorkingDirectory = Radice(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            p.Start();
            string riga;
            while ((riga = p.StandardOutput.ReadLine()) != null) fuori.Add(riga.Trim());
            p.WaitForExit(10000);
            return fuori;
        }
    }
}
