using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Programma;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// IL REGISTRO DELLE DOMANDE SENZA RISPOSTA (voce Z-G7)
    ///
    /// Il registro esiste per rispondere a una domanda che nessuno sa
    /// rispondere a memoria: **quale operazione costruire dopo.** Le cinque
    /// del catalogo senza casi di banco le abbiamo scelte a intuito, e questo
    /// file e' il modo di smettere.
    ///
    /// ── PERCHE' SI PUO' PROVARE SENZA SOLIDWORKS ────────────────────────
    ///
    /// Perche' non ne sa niente: scrive una riga di testo quando gli dicono
    /// che un nome non c'era. E' la parte del ponte che si puo' misurare per
    /// intero sulla macchina della CI.
    ///
    /// ── LE DUE REGOLE CHE QUI SI PRETENDONO, NON SI SPERANO ─────────────
    ///
    ///  1. **Mai su stdout**, dove passa il JSON-RPC. Si legge il SORGENTE e
    ///     si pretende che la parola `Console` non ci sia. Una regola che
    ///     riguarda un canale si controlla sul canale, non sulle intenzioni.
    ///  2. **Mai un'eccezione.** Si punta il registro su un percorso
    ///     impossibile e si pretende che la chiamata torni lo stesso — e che
    ///     il motivo del silenzio sia rimasto scritto, perche' un `catch`
    ///     vuoto e un `catch` che non e' mai stato eseguito si assomigliano
    ///     troppo.
    ///
    /// ── E IL PUNTO CHE SI DIMENTICA ─────────────────────────────────────
    ///
    /// I posti in cui il ponte sa che gli e' stato chiesto qualcosa che non
    /// c'e' sono quattro. Il difetto prevedibile non e' sbagliarne uno: e'
    /// aggiungere il quinto e non registrarlo. Quindi non si conta a quattro
    /// — si pretende che **ogni** punto che chiede `_catalogo.Simili(` scriva
    /// anche nel registro, cosi' il quinto nasce gia' sorvegliato.
    /// </summary>
    internal static class ProveIlRegistro
    {
        internal static void Tutte()
        {
            Gruppo("il registro delle domande senza risposta (Z-G7)");

            LaFormaDellaRiga();
            IlSilenzioEVoluto();
            OgniPuntoCheSaLoScrive();
            NonToccaStdout();
        }

        // ─────────────────────────────────────────────────────────────────
        //  1. LA FORMA DELLA RIGA
        // ─────────────────────────────────────────────────────────────────

        private static void LaFormaDellaRiga()
        {
            var ponte = PonteOppureNonQui("la riga del registro");
            if (ponte == null) return;

            MethodInfo riga;
            Type tipo;
            try
            {
                var asm = Carica.Ponte(ponte);
                tipo = Carica.Classe(asm, "RegistroDelleDomande");
                if (tipo == null)
                { Segna("la classe RegistroDelleDomande c'e'", false, ""); return; }
                riga = tipo.GetMethod("Riga",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (riga == null) { Segna("Riga() c'e'", false, ""); return; }
            }
            catch (Exception ex)
            { Segna("il ponte si carica", false, ex.ToString()); return; }

            var quando = new DateTime(2026, 9, 23, 21, 30, 0);
            Func<string, string, string[], string> fai = (t, n, s) =>
                (string)riga.Invoke(null, new object[] { quando, t, n, s });

            // ── una riga normale ──────────────────────────────────────────
            var normale = fai("operation", "ripiega_lamiera",
                              new[] { "piega_lamiera" });
            Segna("la riga porta quando, tipo e nome",
                  normale.Contains("\"timestamp\":\"2026-09-23T21:30:00\"") &&
                  normale.Contains("\"kind\":\"operation\"") &&
                  normale.Contains("\"name\":\"ripiega_lamiera\""),
                  normale);
            Segna("e i nomi vicini, che sono la meta' utile della misura",
                  normale.Contains("piega_lamiera") && normale.Contains("similar"),
                  "distingue «ha sbagliato a scrivere» da «chiede una cosa che non c'e'»");
            Segna("sta su UNA riga sola, che e' cio' che JSONL vuol dire",
                  !normale.Contains("\n") && !normale.Contains("\r"),
                  normale.Length + " caratteri, nessun a capo");

            // ── il nome che un modello puo' davvero inventarsi ────────────
            //
            // Non e' un caso di fantasia: il nome lo scrive un modello, e
            // basta una virgoletta per rendere illeggibile il file DA LI' IN
            // POI -- non la riga, il file. Per questo si serializza con lo
            // stesso `Protocollo.InJson` del resto del ponte invece di
            // incollare virgolette a mano.
            var cattivo = fai("operation", "taglia\"a\"meta" + "\n" + "riga2", null);
            Segna("un nome con le virgolette non spezza la riga",
                  !cattivo.Contains("\n") && !cattivo.Contains("\r"),
                  cattivo);
            Segna("e si rilegge come JSON, non come testo rotto",
                  SiRilegge(cattivo, "taglia\"a\"meta" + "\n" + "riga2"),
                  "il nome torna indietro identico, a capo compreso");

            // ── `similar` c'e' solo quando c'e' qualcosa ───────────────────
            var senza = fai("strumento", "guarda_la_tavola", null);
            Segna("senza nomi vicini il campo «similar» non compare",
                  !senza.Contains("similar"),
                  "un campo vuoto in ogni riga e' rumore che poi qualcuno conta");
            var vuoti = fai("strumento", "guarda_la_tavola", new string[0]);
            Segna("e nemmeno con un elenco vuoto",
                  !vuoti.Contains("similar"),
                  "zero e assente sono la stessa cosa, e si scrivono uguale");
        }

        /// <summary>
        /// La riga si rilegge come JSON e il nome torna indietro identico?
        ///
        /// Non si usa un parser scritto qui: si chiede a chi legge il JSON nel
        /// ponte, perche' la domanda vera e' *«il ponte si rilegge?»* e non
        /// *«assomiglia a JSON?»*.
        /// </summary>
        private static bool SiRilegge(string riga, string nomeAtteso)
        {
            try
            {
                var ponte = Path.Combine(Radice(), "bin", "oce-solid-agent-mcp.exe");
                var asm = Carica.Ponte(ponte);
                var prot = Carica.Classe(asm, "Protocollo");
                if (prot == null) return false;
                var daJson = prot.GetMethod("DaJson",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (daJson == null) return false;
                var d = daJson.Invoke(null, new object[] { riga })
                        as Dictionary<string, object>;
                object n;
                return d != null && d.TryGetValue("name", out n) &&
                       Convert.ToString(n) == nomeAtteso;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────────────────
        //  2. IL SILENZIO E' VOLUTO, E SI VEDE
        // ─────────────────────────────────────────────────────────────────

        private static void IlSilenzioEVoluto()
        {
            var ponte = PonteOppureNonQui("il registro che non puo' scrivere");
            if (ponte == null) return;

            Type tipo;
            try
            {
                var asm = Carica.Ponte(ponte);
                tipo = Carica.Classe(asm, "RegistroDelleDomande");
                if (tipo == null) return;   // gia' detto sopra
            }
            catch { return; }

            var costruttore = tipo.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(string) }, null);
            if (costruttore == null)
            { Segna("il registro si costruisce con un percorso", false, ""); return; }

            Func<object, string, string, object> annota = (reg, metodo, nome) =>
            {
                var m = ReflectionLog.Global.Method(tipo, metodo,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return m.Invoke(reg, metodo == "OperazioneCheNonCe"
                    ? new object[] { nome, null }
                    : new object[] { nome });
            };
            Func<object, string, object> leggi = (reg, prop) =>
                ReflectionLog.Global.Property(tipo, prop,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(reg);

            // ── SCRIVE DAVVERO ────────────────────────────────────────────
            //
            // Prima di provare che tace quando deve, si prova che parla
            // quando puo'. Un registro che tace sempre passerebbe ogni prova
            // sul silenzio.
            var file = Path.Combine(Path.GetTempPath(),
                                    "registro-prova-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                var buono = costruttore.Invoke(new object[] { file });
                annota(buono, "OperazioneCheNonCe", "fresa_a_candela");
                annota(buono, "StrumentoCheNonCe", "apri_la_tavola");
                var righe = File.Exists(file) ? File.ReadAllLines(file) : new string[0];
                Segna("scrive una riga per domanda",
                      righe.Length == 2, righe.Length + " righe");
                Segna("e le scrive in aggiunta, non sovrascrivendo",
                      righe.Length == 2 &&
                      righe[0].Contains("fresa_a_candela") &&
                      righe[1].Contains("apri_la_tavola"),
                      "la prima resta quando arriva la seconda");
                Segna("e dice di averle scritte",
                      Convert.ToInt32(leggi(buono, "Scritte")) == 2 &&
                      leggi(buono, "PercheHaTaciuto") == null,
                      "Scritte=2, nessun motivo di silenzio");
            }
            finally { try { File.Delete(file); } catch { } }

            // ── IL NOME VUOTO NON E' UNA DOMANDA ──────────────────────────
            var file2 = Path.Combine(Path.GetTempPath(),
                                     "registro-prova-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                var reg = costruttore.Invoke(new object[] { file2 });
                annota(reg, "OperazioneCheNonCe", "   ");
                Segna("un nome vuoto non finisce nel registro",
                      !File.Exists(file2) &&
                      Convert.ToInt32(leggi(reg, "Scritte")) == 0,
                      "e' un piano malfatto, non una cosa che qualcuno voleva");
            }
            finally { try { File.Delete(file2); } catch { } }

            // ── NON PUO' SCRIVERE: TACE, E NON SOLLEVA ────────────────────
            //
            // Il percorso impossibile e' un file VERO usato come cartella:
            // succede davvero, e non dipende dai permessi della macchina che
            // esegue la prova -- che su un runner sono diversi da qui.
            var ostacolo = Path.GetTempFileName();
            try
            {
                var impossibile = Path.Combine(ostacolo, "dentro-un-file.jsonl");
                var reg = costruttore.Invoke(new object[] { impossibile });
                bool haSollevato = false;
                try { annota(reg, "OperazioneCheNonCe", "fresa_a_candela"); }
                catch { haSollevato = true; }
                Segna("se non puo' scrivere NON solleva",
                      !haSollevato,
                      "un registro che fa fallire la chiamata che osserva e' " +
                      "peggio di nessun registro");
                Segna("e il motivo del silenzio resta scritto",
                      !haSollevato && leggi(reg, "PercheHaTaciuto") != null &&
                      Convert.ToInt32(leggi(reg, "Scritte")) == 0,
                      Convert.ToString(leggi(reg, "PercheHaTaciuto")));
            }
            finally { try { File.Delete(ostacolo); } catch { } }

            // ── NESSUN FILE SU CUI SCRIVERE ───────────────────────────────
            var senzaFile = costruttore.Invoke(new object[] { null });
            bool sollevato2 = false;
            try { annota(senzaFile, "StrumentoCheNonCe", "x_qualunque"); }
            catch { sollevato2 = true; }
            Segna("e senza nessun file su cui scrivere, idem",
                  !sollevato2 && leggi(senzaFile, "PercheHaTaciuto") != null,
                  "RISCONTRO_REGISTRO vuota vuol dire «non registrare», " +
                  "e non deve diventare «registra accanto all'eseguibile»");
        }

        // ─────────────────────────────────────────────────────────────────
        //  3. OGNI PUNTO CHE SA, SCRIVE
        // ─────────────────────────────────────────────────────────────────

        private static void OgniPuntoCheSaLoScrive()
        {
            var sorgente = Path.Combine(Radice(), "Tools", "McpTools.cs");
            if (!File.Exists(sorgente))
            { Segna("McpTools.cs si legge", false, sorgente); return; }

            var righe = File.ReadAllLines(sorgente);
            var scoperti = new List<int>();
            int sorvegliati = 0;

            for (int i = 0; i < righe.Length; i++)
            {
                if (righe[i].IndexOf("_catalogo.Simili(", StringComparison.Ordinal) < 0)
                    continue;
                // La scrittura sta subito accanto: tre righe bastano, e non
                // cosi' tante da prendere quella del punto successivo.
                var finestra = string.Join("\n",
                    righe.Skip(i).Take(4));
                if (finestra.IndexOf("_registro.OperazioneCheNonCe",
                                     StringComparison.Ordinal) >= 0) sorvegliati++;
                else scoperti.Add(i + 1);
            }

            Segna("ogni punto che cerca un nome vicino scrive anche nel registro",
                  scoperti.Count == 0,
                  scoperti.Count == 0
                      ? sorvegliati + " punti, tutti sorvegliati"
                      : "scoperti alle righe " + string.Join(", ", scoperti));
            Segna("e i punti sono piu' di uno, se no la regola sorveglia il vuoto",
                  sorvegliati >= 3,
                  sorvegliati + " punti che chiedono `_catalogo.Simili(`");

            var tutto = string.Join("\n", righe);
            Segna("e lo strumento che non esiste finisce nel registro",
                  tutto.IndexOf("_registro.StrumentoCheNonCe",
                                StringComparison.Ordinal) >= 0,
                  "e' il punto che sapeva e taceva: il `default` di Chiama");

            // ── LA REGOLA SI FA FALLIRE APPOSTA ───────────────────────────
            //
            // Su righe finte, perche' far fallire quella vera vorrebbe dire
            // togliere una chiamata dal ponte.
            var finteRotte = new[]
            {
                "                var simili = _catalogo.Simili(nome);",
                "                guai.Add(dove + \": non esiste\");",
            };
            var finteSane = new[]
            {
                "                var simili = _catalogo.Simili(nome);",
                "                _registro.OperazioneCheNonCe(nome, simili);",
                "                guai.Add(dove + \": non esiste\");",
            };
            Segna("la regola prende un punto che NON scrive",
                  Scoperti(finteRotte) == 1,
                  "una riga che cerca i nomi vicini e non registra niente");
            Segna("e lascia stare quello che scrive",
                  Scoperti(finteSane) == 0,
                  "provata in tutt'e due i versi, su righe finte");
        }

        private static int Scoperti(string[] righe)
        {
            int quanti = 0;
            for (int i = 0; i < righe.Length; i++)
            {
                if (righe[i].IndexOf("_catalogo.Simili(", StringComparison.Ordinal) < 0)
                    continue;
                var finestra = string.Join("\n", righe.Skip(i).Take(4));
                if (finestra.IndexOf("_registro.OperazioneCheNonCe",
                                     StringComparison.Ordinal) < 0) quanti++;
            }
            return quanti;
        }

        // ─────────────────────────────────────────────────────────────────
        //  4. MAI SU STDOUT
        // ─────────────────────────────────────────────────────────────────

        private static void NonToccaStdout()
        {
            var sorgente = Path.Combine(Radice(), "Tools", "QuestionRegistry.cs");
            if (!File.Exists(sorgente))
            { Segna("QuestionRegistry.cs si legge", false, sorgente); return; }

            // Si guarda il CODICE, non i commenti: la spiegazione della regola
            // nomina `Console` apposta, e un controllo che punisse la propria
            // documentazione si farebbe cancellare la documentazione.
            var codice = SenzaCommenti(File.ReadAllText(sorgente));
            var trovati = Regex.Matches(codice, @"\bConsole\s*\.").Count;

            Segna("il registro non nomina Console da nessuna parte",
                  trovati == 0,
                  trovati == 0
                      ? "li' passa il JSON-RPC: una riga di troppo e' il protocollo rotto"
                      : trovati + " occorrenze");
            Segna("e la regola guarda il codice, non i commenti che la spiegano",
                  File.ReadAllText(sorgente).IndexOf("Console", StringComparison.Ordinal) >= 0,
                  "il cappello nomina Console per dire di non usarlo: se questa " +
                  "riga diventasse rossa vorrebbe dire che la spiegazione e' sparita");
        }

        /// <summary>
        /// Via i commenti di riga e di blocco. Non e' un parser di C# e non
        /// deve esserlo: qui serve solo a non leggere la prosa che spiega la
        /// regola come se fosse una violazione della regola.
        /// </summary>
        private static string SenzaCommenti(string testo)
        {
            testo = Regex.Replace(testo, @"/\*.*?\*/", "", RegexOptions.Singleline);
            testo = Regex.Replace(testo, @"^\s*///.*$", "", RegexOptions.Multiline);
            testo = Regex.Replace(testo, @"^\s*//.*$", "", RegexOptions.Multiline);
            return testo;
        }
    }
}
