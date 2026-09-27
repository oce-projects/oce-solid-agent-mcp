using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
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
    /// `@faccia(verso=alto)`, `@spigolo(asse=Z)` — o i segnaposto `{LAVORO}` e
    /// `{PROFILI}`, che il banco sostituisce e un client no.
    ///
    /// Copiarli lo stesso avrebbe prodotto ventiquattro esempi buoni e
    /// quarantasette **trappole**: un modello che copia `@faccia(verso=alto)`
    /// riceve un rifiuto per un motivo che non capisce, e la prossima volta non
    /// si fida piu' del catalogo. Per questo la copertura e' 24/71 e non 71/71,
    /// e per questo questo gruppo la guarda a ogni giro.
    /// </summary>
    internal static class ProveGliEsempi
    {
        /// <summary>
        /// Quante operazioni hanno un esempio. Ventiquattro il 23/9/2026, e
        /// **puo' solo salire**: si alza scrivendo un caso di banco che usi
        /// argomenti da client invece che selettori del banco — cioe'
        /// migliorando la prova, non l'esempio. **25** il 27/9/2026: `fissa_componente`
        /// nasce con l'esempio copiato dal suo caso (`fisso_non_e_accoppiato`).
        /// **26**: `variabile_globale`, dal suo caso (`la_parete_ha_un_nome`).
        /// </summary>
        private const int Pavimento = 26;

        /// <summary>Cio' che un client non puo' copiare.</summary>
        private static readonly string[] SoloDelBanco = { "@", "{LAVORO}", "{PROFILI}" };

        internal static void Tutte()
        {
            Gruppo("gli esempi si copiano da un caso verde (Z-G1)");

            var file = CatalogoServito();
            if (!File.Exists(file))
            {
                Segna("il catalogo servito c'e'", false, "manca " + file);
                return;
            }

            var s = new JavaScriptSerializer();
            var radice = s.DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
            object grezzo;
            var voci = radice != null && radice.TryGetValue("operazioni", out grezzo)
                ? grezzo as object[] : null;
            if (voci == null)
            {
                Segna("il catalogo ha le operazioni", false, "«operazioni» non e' un elenco");
                return;
            }

            int conEsempio = 0;
            var illeggibili = new List<string>();
            var contaminati = new List<string>();
            var fuoriSchema = new List<string>();

            foreach (var v in voci)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, e, sc;
                o.TryGetValue("nome", out n);
                o.TryGetValue("esempio", out e);
                o.TryGetValue("schema", out sc);
                string nome = Convert.ToString(n);
                string esempio = Convert.ToString(e);
                if (string.IsNullOrWhiteSpace(esempio)) continue;
                conEsempio++;

                // ── E' JSON? ─────────────────────────────────────────────
                Dictionary<string, object> argomenti = null;
                try { argomenti = s.DeserializeObject(esempio) as Dictionary<string, object>; }
                catch (Exception ex) { illeggibili.Add(nome + ": " + ex.Message); continue; }
                if (argomenti == null) { illeggibili.Add(nome + ": non e' un oggetto"); continue; }

                // ── E' COPIABILE? ────────────────────────────────────────
                foreach (var brutto in SoloDelBanco)
                    if (esempio.Contains(brutto))
                        contaminati.Add(nome + " contiene «" + brutto + "»");

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
                foreach (var campo in argomenti.Keys)
                    if (!props.ContainsKey(campo))
                        fuoriSchema.Add(nome + "." + campo);
            }

            Segna("gli esempi non sono diminuiti", conEsempio >= Pavimento,
                  conEsempio + " operazioni su " + voci.Length +
                  " hanno un esempio (pavimento " + Pavimento + ")");

            Segna("e il pavimento e' quello vero", conEsempio == Pavimento,
                  conEsempio == Pavimento
                      ? "ventiquattro, come misurati"
                      : "sono " + conEsempio + ": alza il pavimento a questo numero");

            Segna("ogni esempio e' JSON leggibile", illeggibili.Count == 0,
                  illeggibili.Count == 0 ? "tutti" : string.Join(" | ", illeggibili));

            // QUESTO E' IL CONTROLLO CHE CONTA.
            Segna("nessun esempio usa qualcosa che vive solo nel banco",
                  contaminati.Count == 0,
                  contaminati.Count == 0
                      ? "niente selettori @, niente segnaposto {LAVORO}/{PROFILI}"
                      : string.Join(" | ", contaminati));

            Segna("e ogni campo dell'esempio esiste nello schema",
                  fuoriSchema.Count == 0,
                  fuoriSchema.Count == 0
                      ? "nessun esempio invecchiato"
                      : "campi che lo schema non ha: " + string.Join(", ", fuoriSchema));
        }
    }
}
