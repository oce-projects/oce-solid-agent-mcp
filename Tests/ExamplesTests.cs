using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// UN ESEMPIO CHE IL CLIENT NON PUO' COPIARE E' PEGGIO DI NESSUN ESEMPIO
    /// (voce Z-G1).
    ///
    /// ── PERCHE' GLI ESEMPI ───────────────────────────────────────────────
    ///
    /// Uno schema dice quali campi sono leciti; **non dice quale combinazione
    /// costruisce un pezzo**. Sono due cose diverse, e la seconda si impara
    /// solo vedendola.
    ///
    /// ── PERCHE' NON SI INVENTANO ─────────────────────────────────────────
    ///
    /// Ogni esempio e' copiato da un passo che il banco ha **eseguito e
    /// misurato**. Un esempio scritto a mano e' una promessa; uno copiato da un
    /// caso verde e' un fatto. La differenza si vede il giorno in cui
    /// l'operazione cambia e l'esempio resta indietro: il caso diventa rosso,
    /// la promessa no.
    ///
    /// ── E LA TRAPPOLA CHE QUESTO GRUPPO GUARDA ───────────────────────────
    ///
    /// Il 23/9/2026, su 71 operazioni, **47 non avevano un esempio copiabile**:
    /// i loro casi usano selettori che vivono solo nel banco —
    /// `@face(normal=up)`, `@edge(axis=Z)` — o i segnaposto `{LAVORO}` e
    /// `{PROFILI}`, che il banco sostituisce e un client no.
    ///
    /// Copiarli lo stesso avrebbe prodotto ventiquattro esempi buoni e
    /// quarantasette **trappole**: un modello che copia `@face(normal=up)`
    /// riceve un rifiuto per un motivo che non capisce, e la prossima volta non
    /// si fida piu' del catalogo. Per questo la copertura e' 24/71 e non 71/71,
    /// e per questo questo gruppo la guarda a ogni giro.
    /// </summary>
    internal static class ExamplesTests
    {
        /// <summary>
        /// Quante operazioni hanno un esempio. Ventiquattro il 23/9/2026, e
        /// **puo' solo salire**: si alza scrivendo un caso di banco che usi
        /// argomenti da client invece che selettori del banco — cioe'
        /// migliorando la prova, non l'esempio. **25** il 27/9/2026: `fix_component`
        /// nasce con l'esempio copiato dal suo caso (`fixed_is_not_mated`).
        /// **26**: `global_variable`, dal suo caso (`wall_has_a_name`).
        /// **27**: `reorder_feature` (`order_changes_the_part`).
        /// **28**: `mirror_component` (`right_bracket_and_left_bracket`).
        /// **29**: `edit_extrusion` (`blind_becomes_through`).
        /// **30**: `rib` (`rib_in_the_corner_of_the_l`), voce W1.2. Le
        /// altre tre lavorazioni della guida (`fillet_face`,
        /// `fill_pattern`, `gusset`) nascono senza: i loro casi verdi
        /// nominano le facce coi selettori del banco.
        /// </summary>
        private const int Floor = 30;

        /// <summary>Cio' che un client non puo' copiare.</summary>
        private static readonly string[] BenchOnly = { "@", "{LAVORO}", "{PROFILI}", "{DATI}", "{SOLIDWORKS}" };

        internal static void All()
        {
            Group("gli esempi si copiano da un caso verde (Z-G1)");

            var file = ServedCatalog();
            if (!File.Exists(file))
            {
                Check("il catalogo servito c'e'", false, "manca " + file);
                return;
            }

            var s = new JavaScriptSerializer();
            var root = s.DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
            object raw;
            var entries = root != null && root.TryGetValue("operations", out raw)
                ? raw as object[] : null;
            if (entries == null)
            {
                Check("il catalogo ha le operazioni", false, "«operations» non e' un elenco");
                return;
            }

            int withExample = 0;
            var unreadable = new List<string>();
            var contaminated = new List<string>();
            var outsideSchema = new List<string>();

            foreach (var v in entries)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, e, sc;
                o.TryGetValue("name", out n);
                o.TryGetValue("example", out e);
                o.TryGetValue("schema", out sc);
                string itemName = Convert.ToString(n);
                string example = Convert.ToString(e);
                if (string.IsNullOrWhiteSpace(example)) continue;
                withExample++;

                // ── E' JSON? ─────────────────────────────────────────────
                Dictionary<string, object> arguments = null;
                try { arguments = s.DeserializeObject(example) as Dictionary<string, object>; }
                catch (Exception ex) { unreadable.Add(itemName + ": " + ex.Message); continue; }
                if (arguments == null) { unreadable.Add(itemName + ": non e' un oggetto"); continue; }

                // ── E' COPIABILE? ────────────────────────────────────────
                foreach (var bad in BenchOnly)
                    if (example.Contains(bad))
                        contaminated.Add(itemName + " contiene «" + bad + "»");

                // ── E I CAMPI ESISTONO NELLO SCHEMA? ─────────────────────
                //
                // Un esempio che nomina un campo che lo schema non ha e' un
                // esempio invecchiato: l'operazione e' cambiata e lui no. E'
                // il difetto piu' probabile di questa voce, perche' l'esempio
                // non viene eseguito da nessuno.
                var schema = sc as Dictionary<string, object>;
                object pg;
                var props = schema != null && schema.TryGetValue("properties", out pg)
                    ? pg as Dictionary<string, object> : null;
                if (props == null) continue;
                foreach (var theField in arguments.Keys)
                    if (!props.ContainsKey(theField))
                        outsideSchema.Add(itemName + "." + theField);
            }

            Check("gli esempi non sono diminuiti", withExample >= Floor,
                  withExample + " operazioni su " + entries.Length +
                  " hanno un esempio (pavimento " + Floor + ")");

            Check("e il pavimento e' quello vero", withExample == Floor,
                  withExample == Floor
                      ? "ventiquattro, come misurati"
                      : "sono " + withExample + ": alza il pavimento a questo numero");

            Check("ogni esempio e' JSON leggibile", unreadable.Count == 0,
                  unreadable.Count == 0 ? "tutti" : string.Join(" | ", unreadable));

            // QUESTO E' IL CONTROLLO CHE CONTA.
            Check("nessun esempio usa qualcosa che vive solo nel banco",
                  contaminated.Count == 0,
                  contaminated.Count == 0
                      ? "niente selettori @, niente segnaposto {LAVORO}/{PROFILI}/{DATI}/{SOLIDWORKS}"
                      : string.Join(" | ", contaminated));

            Check("e ogni campo dell'esempio esiste nello schema",
                  outsideSchema.Count == 0,
                  outsideSchema.Count == 0
                      ? "nessun esempio invecchiato"
                      : "campi che lo schema non ha: " + string.Join(", ", outsideSchema));
        }
    }
}
