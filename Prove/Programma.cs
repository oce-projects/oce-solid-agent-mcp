using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Prove.Contratti
{
    /// <summary>
    /// LE PROVE CHE GIRANO SU UNA MACCHINA QUALUNQUE (voce F7).
    ///
    /// ── PERCHE' ESISTONO ──────────────────────────────────────────────────
    ///
    /// Sono le uniche prove che girano **senza licenza, senza CAD, in
    /// secondi**. Il banco vero apre SOLIDWORKS e dura un quarto d'ora: va
    /// benissimo la sera prima di committare, e non serve a niente in una CI
    /// o sulla macchina di chi apre questo repository per la prima volta.
    ///
    /// E il valutatore delle attese **deve** stare qui: se e' rotto lui, tutta
    /// la suite con SOLIDWORKS diventa verde gratis.
    ///
    /// ── COSA PROVA ────────────────────────────────────────────────────────
    ///
    ///   * il **catalogo**: campi, famiglie, nessun percorso di questa macchina
    ///     dentro le descrizioni, e `Simili()` che su «asole» trova «asola»;
    ///   * `Sessione.StessoDocumento()` — la guardia contro il blocco estruso
    ///     nel pezzo di qualcun altro, che fino a oggi non aveva una prova;
    ///   * il **protocollo MCP**, parlando col ponte su stdin/stdout come
    ///     farebbe un cliente;
    ///   * il **valutatore delle attese**, contro stati finti scritti a mano.
    ///
    /// ── E SCRIVE `prove.xml` ─────────────────────────────────────────────
    ///
    /// In formato JUnit, che e' quello che ogni CI sa leggere senza che
    /// nessuno le insegni niente.
    /// </summary>
    internal static class Programma
    {
        internal static int Falliti;
        internal static readonly List<Esito> Esiti = new List<Esito>();

        internal sealed class Esito
        {
            public string Gruppo;
            public string Nome;
            public bool Ok;
            public string Dettaglio;
        }

        private static string _gruppo = "generale";

        internal static void Gruppo(string nome)
        {
            _gruppo = nome;
            Console.WriteLine();
            Console.WriteLine("=== " + nome + " ===");
        }

        internal static void Segna(string nome, bool ok, string dettaglio = "")
        {
            if (!ok) Falliti++;
            Esiti.Add(new Esito { Gruppo = _gruppo, Nome = nome, Ok = ok, Dettaglio = dettaglio });
            // UN VERDE SI ACCORCIA, UN ROSSO NO. Il dettaglio di una prova
            // rossa e' l'unica cosa che si legge davvero, e troncarlo a
            // centodieci caratteri vuol dire rilanciare per vedere il resto.
            Console.WriteLine((ok ? "  OK   " : "  NO   ") + nome.PadRight(58) +
                              (ok ? Breve(dettaglio) : dettaglio));
        }

        internal static string Breve(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 110 ? s.Substring(0, 110) + "..." : s;
        }

        /// <summary>La radice del repository, due livelli sopra `Prove\bin\`.</summary>
        internal static string Radice()
        {
            var bin = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            return Directory.GetParent(Directory.GetParent(bin).FullName).FullName;
        }

        private static int Main(string[] argomenti)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("Prove.Contratti — senza SOLIDWORKS, su questa macchina qualunque");
            Console.WriteLine("radice: " + Radice());

            NessunInterop();
            ProveAttese.Tutte();
            ProveCatalogo.Tutte();
            ProveSessione.Tutte();
            ProveProtocollo.Tutte();

            Console.WriteLine();
            Console.WriteLine(new string('=', 76));
            var riepilogo = Esiti.Count + " controlli, " + Falliti + " falliti";
            Console.WriteLine(riepilogo);

            // IL MINIMO E' DICHIARATO E SI CONTROLLA. Una suite che si svuota
            // — un file di prove tolto, un gruppo che esce prima — resterebbe
            // verde con tre controlli, e «0 falliti» suonerebbe uguale.
            const int MinimoDichiarato = 25;
            if (Esiti.Count < MinimoDichiarato)
            {
                Console.WriteLine("TROPPO POCHI: ne erano dichiarati almeno " + MinimoDichiarato +
                                  ". Una suite che si svuota resta verde, ed e' il modo piu' " +
                                  "silenzioso di smettere di provare.");
                Falliti++;
            }

            ScriviJUnit();
            return Falliti == 0 ? 0 : 1;
        }

        /// <summary>
        /// QUESTO PROGETTO NON REFERENZIA L'INTEROP, E LO CONTROLLA DA SOLO.
        ///
        /// E' la ragione per cui esiste: girare dove SOLIDWORKS non c'e'. Se
        /// qualcuno aggiunge un riferimento al csproj — perche' «serviva un
        /// tipo» — la promessa cade, e su questa macchina non lo noterebbe
        /// nessuno: qui SOLIDWORKS c'e', e compila lo stesso.
        ///
        /// Si guardano i riferimenti dell'assembly, non il testo del csproj: il
        /// csproj si puo' leggere in dieci modi, i riferimenti veri sono uno.
        /// </summary>
        private static void NessunInterop()
        {
            Gruppo("questo progetto gira dove SOLIDWORKS non c'e'");
            var mio = typeof(Programma).Assembly;
            var dassault = mio.GetReferencedAssemblies()
                .Where(a => a.Name.StartsWith("SolidWorks", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Name)
                .ToList();
            Segna("nessun riferimento a SolidWorks.Interop",
                  dassault.Count == 0,
                  dassault.Count == 0
                      ? mio.GetReferencedAssemblies().Length + " riferimenti, nessuno di Dassault"
                      : "trovati: " + string.Join(", ", dassault));
        }

        /// <summary>
        /// `prove.xml` in formato JUnit: e' quello che ogni CI sa leggere senza
        /// che nessuno le insegni niente.
        /// </summary>
        private static void ScriviJUnit()
        {
            try
            {
                var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prove.xml");
                var x = new StringBuilder();
                x.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                x.AppendLine("<testsuites name=\"Prove.Contratti\" tests=\"" + Esiti.Count +
                             "\" failures=\"" + Falliti + "\">");
                foreach (var g in Esiti.GroupBy(e => e.Gruppo))
                {
                    x.AppendLine("  <testsuite name=\"" + Xml(g.Key) + "\" tests=\"" + g.Count() +
                                 "\" failures=\"" + g.Count(e => !e.Ok) + "\">");
                    foreach (var e in g)
                    {
                        x.Append("    <testcase classname=\"" + Xml(g.Key) +
                                 "\" name=\"" + Xml(e.Nome) + "\"");
                        if (e.Ok) x.AppendLine(" />");
                        else
                        {
                            x.AppendLine(">");
                            x.AppendLine("      <failure message=\"" + Xml(e.Dettaglio) + "\" />");
                            x.AppendLine("    </testcase>");
                        }
                    }
                    x.AppendLine("  </testsuite>");
                }
                x.AppendLine("</testsuites>");
                File.WriteAllText(file, x.ToString(), new UTF8Encoding(false));
                Console.WriteLine("prove.xml: " + file);
            }
            catch (Exception ex)
            {
                Console.WriteLine("non ho potuto scrivere prove.xml: " + ex.Message);
            }
        }

        private static string Xml(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;")
                            .Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
