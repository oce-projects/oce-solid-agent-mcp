using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// UN LIMITE MISURATO CHE LA DESCRIZIONE NON DICE COSTA UN TURNO A VUOTO
    /// (voci Z-G5 e Z-C11).
    ///
    /// ── LE DUE META' DELLA STESSA COSA ───────────────────────────────────
    ///
    /// Quando un'operazione rifiuta, qualcuno ha gia' misurato quel limite e
    /// ci ha scritto un caso di banco. Ma **il modello non legge i casi di
    /// banco**: legge il catalogo. Se la descrizione tace, l'unico modo che ha
    /// di scoprire il limite e' sbatterci contro — e un rifiuto costa un giro,
    /// mentre un limite dichiarato costa zero.
    ///
    /// *Dichiarare un limite prima di provare vale piu' di un rifiuto.*
    ///
    /// E l'altra meta' (Z-C11): **ogni rifiuto nasce con la sua gemella
    /// verde**. Il rischio speculare — rifiutare un pezzo legittimo — non lo
    /// rivede nessuno, perche' un rifiuto sbagliato *sembra* prudenza. La
    /// gemella e' cio' che distingue una guardia da un'operazione che dice
    /// sempre di no, ed e' capitato davvero: la prima versione di
    /// `sezione_in_tavola` rifiutava tutto, e il difetto e' uscito al primo
    /// uso vero.
    ///
    /// ── PERCHE' QUI E NON NEL BANCO ──────────────────────────────────────
    ///
    /// Il banco le gemelle le controlla gia', **ma dopo un'ora di giro e con
    /// SOLIDWORKS acceso**. Un caso di rifiuto scritto senza gemella e' un
    /// errore di scrittura, non di misura: si vede leggendo il JSON, in un
    /// decimo di secondo, e chi lo scrive deve saperlo prima di lanciare il
    /// banco, non dopo.
    /// </summary>
    internal static class ProveILimiti
    {
        /// <summary>
        /// Quante operazioni hanno un rifiuto provato dal banco e **non lo
        /// dicono** nella loro descrizione. **Zero, dal 23/9/2026.**
        ///
        /// Erano dodici quella mattina — asola, attiva_configurazione,
        /// attiva_foglio, crea_configurazione, quota_in_tavola, ripeti_lineare,
        /// rivoluzione, smusso_spigoli, sopprimi_lavorazione, specchia,
        /// taglio_cilindrico, taglio_rettangolare — e ognuna aveva un limite
        /// gia' misurato, con tanto di caso di banco che lo pretende, che il
        /// modello poteva scoprire **solo sbattendoci contro**.
        ///
        /// Le frasi nuove non sono inventate: sono il messaggio che il caso
        /// pretende, girato al futuro. *Un limite dichiarato costa zero turni;
        /// lo stesso limite scoperto da un rifiuto ne costa uno.*
        ///
        /// E' un TETTO, e da zero non puo' che salire — cioe' non puo'. Una
        /// guardia nuova senza la frase che la dichiara fa diventare rosso
        /// questo controllo **il giorno in cui la si scrive**, che e' l'unico
        /// giorno in cui qualcuno la sta guardando.
        /// </summary>
        private const int Mute = 0;

        /// <summary>
        /// Le parole con cui una descrizione dichiara un limite. Sono quelle
        /// che il catalogo usa davvero, non quelle che «dovrebbe» usare: un
        /// elenco inventato a tavolino direbbe che tutte tacciono.
        /// </summary>
        private static readonly string[] Spie =
        {
            "rifiut", "non posso", "limite", "non guarda", "non sa",
            "non copre", "fallisc", "sbord", "piu' grande",
        };

        internal static void Tutte()
        {
            Gruppo("i limiti misurati e le descrizioni che li dichiarano (Z-G5, Z-C11)");

            var cartella = Path.Combine(Radice(), "Banco", "casi");
            if (!Directory.Exists(cartella))
            {
                Segna("la cartella dei casi c'e'", false, "manca " + cartella);
                return;
            }

            var serializzatore = new JavaScriptSerializer();
            int dichiarati = 0, piani = 0;
            var illeggibili = new List<string>();
            var senzaGemella = new List<string>();
            var gemelleAssenti = new List<string>();
            var senzaMessaggio = new List<string>();
            var operazioniRifiutate = new Dictionary<string, int>(StringComparer.Ordinal);

            var file = Directory.GetFiles(cartella, "*.json", SearchOption.AllDirectories);
            var nomi = new HashSet<string>(
                file.Select(f => Path.GetFileNameWithoutExtension(f)), StringComparer.Ordinal);

            foreach (var f in file)
            {
                string nome = Path.GetFileNameWithoutExtension(f);
                object radice;
                try { radice = serializzatore.DeserializeObject(File.ReadAllText(f)); }
                catch (Exception ex) { illeggibili.Add(nome + ": " + ex.Message); continue; }

                var caso = radice as Dictionary<string, object>;
                if (caso == null) { piani++; continue; }
                dichiarati++;

                object grezzo;
                if (!caso.TryGetValue("passi", out grezzo)) continue;
                var passi = grezzo as object[];
                if (passi == null) continue;

                foreach (var p in passi)
                {
                    var passo = p as Dictionary<string, object>;
                    if (passo == null) continue;
                    object dv;
                    if (!passo.TryGetValue("deve_fallire", out dv)) continue;
                    var rifiuto = dv as Dictionary<string, object>;
                    if (rifiuto == null) continue;

                    object op;
                    string operazione = passo.TryGetValue("operazione", out op)
                        ? Convert.ToString(op) : "(senza nome)";
                    int quanti;
                    operazioniRifiutate.TryGetValue(operazione, out quanti);
                    operazioniRifiutate[operazione] = quanti + 1;

                    // ── LA GEMELLA (Z-C11) ───────────────────────────────
                    object g;
                    string gemella = rifiuto.TryGetValue("gemella", out g)
                        ? Convert.ToString(g) : null;
                    if (string.IsNullOrEmpty(gemella)) senzaGemella.Add(nome + " / " + operazione);
                    else if (!nomi.Contains(gemella))
                        gemelleAssenti.Add(nome + " -> «" + gemella + "» non esiste");

                    // ── E IL MESSAGGIO DEV'ESSERE PRETESO ────────────────
                    //
                    // «deve fallire» senza dire con quale messaggio e' quasi
                    // niente: qualunque errore lo soddisfa, compreso quello
                    // sbagliato. Il banco lo pretende gia'; qui si vede subito.
                    object m;
                    var messaggi = rifiuto.TryGetValue("il_messaggio_contiene", out m)
                        ? m as object[] : null;
                    if (messaggi == null || messaggi.Length == 0)
                        senzaMessaggio.Add(nome + " / " + operazione);
                }
            }

            Segna("tutti i file di caso si leggono", illeggibili.Count == 0,
                  illeggibili.Count == 0
                      ? file.Length + " file (" + dichiarati + " casi, " + piani + " piani)"
                      : string.Join(" | ", illeggibili));

            Segna("ogni rifiuto dichiara la sua gemella", senzaGemella.Count == 0,
                  senzaGemella.Count == 0
                      ? operazioniRifiutate.Values.Sum() + " rifiuti, tutti con gemella"
                      : string.Join(" | ", senzaGemella));

            Segna("e la gemella esiste davvero", gemelleAssenti.Count == 0,
                  gemelleAssenti.Count == 0
                      ? "nessun rimando a un caso che non c'e'"
                      : string.Join(" | ", gemelleAssenti));

            Segna("ogni rifiuto pretende un messaggio", senzaMessaggio.Count == 0,
                  senzaMessaggio.Count == 0
                      ? "nessun «deve_fallire» che accetti qualunque errore"
                      : string.Join(" | ", senzaMessaggio));

            // ── E LE DESCRIZIONI? (Z-G5) ────────────────────────────────
            var catalogo = Path.Combine(Radice(), "bin", "catalogo.json");
            if (!File.Exists(catalogo))
            {
                Segna("il catalogo servito c'e'", false, "manca " + catalogo);
                return;
            }

            var radiceCat = serializzatore.DeserializeObject(File.ReadAllText(catalogo))
                            as Dictionary<string, object>;
            object vociGrezze;
            var voci = radiceCat != null && radiceCat.TryGetValue("operazioni", out vociGrezze)
                ? vociGrezze as object[] : null;
            if (voci == null)
            {
                Segna("il catalogo ha le operazioni", false, "«operazioni» non e' un elenco");
                return;
            }

            var testoDi = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var v in voci)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, d, s;
                o.TryGetValue("nome", out n);
                o.TryGetValue("descrizione", out d);
                o.TryGetValue("sommario", out s);
                if (n == null) continue;
                testoDi[Convert.ToString(n)] =
                    (Convert.ToString(d) + " " + Convert.ToString(s)).ToLowerInvariant();
            }

            var mute = new List<string>();
            var fuoriCatalogo = new List<string>();
            foreach (var op in operazioniRifiutate.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                string testo;
                if (!testoDi.TryGetValue(op, out testo)) { fuoriCatalogo.Add(op); continue; }
                bool parla = false;
                foreach (var spia in Spie) if (testo.Contains(spia)) { parla = true; break; }
                if (!parla) mute.Add(op + " (" + operazioniRifiutate[op] + ")");
            }

            Segna("ogni operazione col rifiuto provato sta a catalogo",
                  fuoriCatalogo.Count == 0,
                  fuoriCatalogo.Count == 0
                      ? operazioniRifiutate.Count + " operazioni con almeno un rifiuto provato"
                      : "fuori catalogo: " + string.Join(", ", fuoriCatalogo));

            // IL TETTO CALA E NON SALE. Una guardia nuova senza la frase che la
            // dichiara fa diventare rosso questo controllo, ed e' il momento in
            // cui serve: dopo, nessuno ci torna piu'.
            Segna("le descrizioni mute non sono aumentate", mute.Count <= Mute,
                  mute.Count + " mute su " + operazioniRifiutate.Count +
                  " (tetto " + Mute + "): " + string.Join(", ", mute));

            Segna("e il tetto e' quello vero", mute.Count == Mute,
                  mute.Count == Mute
                      ? "zero: ogni operazione col rifiuto provato lo dichiara"
                      : "sono " + mute.Count + ": abbassa il tetto a questo numero");
        }
    }
}
