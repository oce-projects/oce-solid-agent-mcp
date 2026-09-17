using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LA PRIMA OPERAZIONE DI ASSIEME (voce GA3).
    ///
    /// ── IL DIFETTO CHE QUESTA OPERAZIONE DEVE NON AVERE ──────────────────
    ///
    /// Il censimento delle API di assieme (nota `80q`) lo dice gia', ed e' la
    /// frase da tenere davanti scrivendo questo file:
    ///
    ///     «AddComponent5 piazza senza accoppiare: quattro corpi liberi che
    ///      sembrano un assieme».
    ///
    /// Inserire riesce quasi sempre. Il pezzo appare, l'albero si riempie, la
    /// ricostruzione e' pulita, e **niente e' vincolato**. Un assieme di quattro
    /// componenti liberi si apre, si ruota, si esporta, e si monta sbagliato.
    /// Percio' questa operazione, oltre a inserire, **dice quanti gradi di
    /// liberta' restano**, e lo dice a voce alta nella sua risposta: non e' un
    /// dettaglio, e' l'unica misura che distingue un assieme da quattro pezzi
    /// nella stessa stanza.
    ///
    /// ── DUE PRECONDIZIONI CHE NON SI POSSONO INDOVINARE ──────────────────
    ///
    ///  1. **Il documento deve essere un assieme.** Chiamare questa su una
    ///     parte non e' un errore dell'utente da punire con un'eccezione: e' una
    ///     cosa che si dice, col tipo che si e' trovato davanti.
    ///  2. **Il componente dev'essere gia' APERTO.** `AddComponent5` prende un
    ///     percorso, ma vuole il documento aperto in SOLIDWORKS: con un file
    ///     chiuso risponde `null` **senza dire perche'**. E' la stessa
    ///     precondizione delle viste di tavola, dove era gia' costata un giro.
    ///     Qui si controlla PRIMA, e il rifiuto elenca cosa c'e' di aperto:
    ///     un rifiuto che non dice l'alternativa costringe a indovinare.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito InserisciComponente(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var assieme = modello as IAssemblyDoc;
            if (assieme == null)
                return Fallito(
                    "Questo non e' un assieme, e' un documento di tipo «" +
                    NomeTipoDocumento(TipoDi(modello)) + "». I componenti si " +
                    "inseriscono solo in un assieme: aprine uno, oppure crealo, " +
                    "e poi richiamami.");

            string percorso = Str(a, "percorso", null);
            if (string.IsNullOrWhiteSpace(percorso))
                return Fallito(
                    "Non mi hai detto quale componente inserire: «percorso» e' il " +
                    "percorso completo del file, e dev'essere uno dei documenti " +
                    "gia' aperti. Li trovi in «documenti_aperti» dello stato.");

            if (!File.Exists(percorso))
                return Fallito(
                    "Il file «" + percorso + "» non esiste. " + CosaEAperto());

            // ── E' APERTO? ───────────────────────────────────────────────
            //
            // Il controllo sta qui e non dopo, perche' dopo non ci sarebbe
            // niente da leggere: `AddComponent5` risponde null e basta, e un
            // null non dice se il file manca, se e' chiuso, o se il percorso e'
            // scritto in un altro modo.
            if (!EAperto(percorso))
                return Fallito(
                    "«" + Path.GetFileName(percorso) + "» non e' aperto in SOLIDWORKS, e " +
                    "l'inserimento vuole un documento gia' aperto: se lo chiamo su un " +
                    "file chiuso mi risponde «non fatto» senza dirmi perche'. " +
                    CosaEAperto());

            double x = Num(a, "x_mm", 0), y = Num(a, "y_mm", 0), z = Num(a, "z_mm", 0);
            string configurazione = Str(a, "configurazione", "");

            int prima = QuantiComponentiInAssieme(assieme);

            IComponent2 nato;
            try
            {
                // ConfigOption 0 = usa la configurazione nominata (o l'attiva se
                // la stringa e' vuota). Non si passa un nome inventato: una
                // configurazione che non esiste **non da' errore**, prende
                // l'attiva. E' la stessa trappola gia' pagata con i profili di
                // saldatura e con `attiva_configurazione`.
                nato = assieme.AddComponent5(
                    percorso, 0, "", false, configurazione ?? "",
                    x / 1000.0, y / 1000.0, z / 1000.0) as IComponent2;
            }
            catch (Exception ex)
            {
                return Fallito("Non sono riuscito a inserire il componente: " + ex.Message);
            }

            // IL NOME SI PRENDE PRIMA DELLA RICOSTRUZIONE, e serve per ritrovare
            // il componente DOPO. Una ricostruzione puo' invalidare i puntatori
            // COM ai componenti: continuare a interrogare la maniglia che
            // `AddComponent5` ha restituito vorrebbe dire misurare su un
            // riferimento vecchio, e leggere numeri credibili e sbagliati.
            //
            // E' la regola di casa applicata un livello piu' in la': *il valore
            // che torna da chi ha fatto la cosa non e' la misura della cosa* —
            // e nemmeno l'OGGETTO che torna e' l'oggetto su cui misurare.
            string nome = null;
            try { nome = nato == null ? null : nato.Name2; } catch { }

            try { modello.EditRebuild3(); } catch { }

            // Rilettura: si cerca per nome fra i componenti che l'assieme
            // dichiara ADESSO. Se non si ritrova, si tiene la maniglia vecchia
            // invece di rinunciare alla misura — dirlo e' meglio che tacere,
            // ma una misura sospetta e' meglio di nessuna misura solo finche'
            // si sa che e' sospetta, e infatti il caso di banco la sorveglia.
            var vero = RileggiIlComponente(assieme, nome) ?? nato;

            // ── LA MISURA STA SULL'ASSIEME, NON SULLA RISPOSTA ───────────
            //
            // `AddComponent5` ha risposto un `IComponent2`, e *il valore che
            // torna da chi ha fatto la cosa non e' la misura della cosa*: si
            // conta quanti componenti ci sono adesso.
            int dopo = QuantiComponentiInAssieme(assieme);
            if (dopo != prima + 1)
                return Fallito(
                    "Ho chiesto di inserire «" + Path.GetFileName(percorso) + "» e " +
                    (nato == null ? "non mi e' stato risposto niente" : "mi e' stato risposto di si'") +
                    ", ma l'assieme aveva " + prima + " componenti e adesso ne ha " + dopo +
                    ". Non l'ho considerato inserito.");

            var misure = new Dictionary<string, object>
            {
                ["componenti_prima"] = prima,
                ["componenti_dopo"] = dopo,
            };

            if (!string.IsNullOrWhiteSpace(nome)) misure["componente"] = nome;

            try
            {
                var cfg = vero == null ? null : vero.ReferencedConfiguration;
                if (!string.IsNullOrWhiteSpace(cfg)) misure["configurazione"] = cfg;
            }
            catch { }

            // ── E' VINCOLATO? LE MISURE CHE FUNZIONANO DAVVERO ──────────
            //
            // I gradi di liberta' NON si possono avere da questa API in questo
            // contesto: misurato il 16/9/2026, `GetRemainingDOFs` risponde
            // `Unavailable` su un assieme appena costruito, selezionando il
            // componente, dopo averlo salvato su disco, e perfino dopo un
            // accoppiamento coincidente vero (`AddMate5` con `NoError`).
            // Quattro condizioni, sempre lo stesso.
            //
            // Quello che si misura DAVVERO sono due cose, e bastano a dire la
            // verita': se il componente e' **fisso**, e **quanti accoppiamenti**
            // ha. `IsFixed()` funziona — distingue il primo componente
            // (inchiodato) dagli altri — e `GetMates()` si conta.
            //
            // Un componente NON fisso con ZERO accoppiamenti e' appoggiato, e
            // questo lo si sa per certo. Non si sa DI QUANTO sia libero, e
            // infatti non lo si dice.
            var gradi = GradiDiLiberta(vero);
            misure["stato_gradi_di_liberta"] = gradi.Stato;
            if (gradi.Disponibile) misure["gradi_di_liberta_residui"] = gradi.Quanti;

            bool fisso = false;
            try { fisso = vero != null && vero.IsFixed(); } catch { }
            misure["fisso"] = fisso;

            int accoppiamenti = ContaGliAccoppiamenti(vero);
            if (accoppiamenti >= 0) misure["accoppiamenti"] = accoppiamenti;

            bool appoggiato = !fisso && accoppiamenti == 0;
            misure["appoggiato"] = appoggiato;

            // DOVE E' FINITA L'ORIGINE, che NON e' il punto che hai chiesto.
            //
            // `AddComponent5(x, y, z)` posiziona il **centro dell'ingombro**.
            // Un pezzo con l'origine su una faccia — la norma, se e' stato
            // estruso a una direzione sola — si ritrova l'origine spostata di
            // meta' spessore. Misurato il 17/9/2026 e previsto prima: spesso 4
            // chiesto a z=30 → origine a (0, -2, 30); spesso 10 chiesto a 0 →
            // (0, -5, 0).
            //
            // Si riporta perche' e' cio' che il modello rileggera' nello stato,
            // e vedere due numeri diversi senza spiegazione e' peggio che
            // vederne uno solo.
            var origine = OrigineDelComponenteMm(vero);
            if (origine != null) misure["origine_mm"] = origine;

            // IL GIUDIZIO E' UNA FRASE, NON UN NUMERO, e viaggia con le misure.
            //
            // «gradi_di_liberta_residui: 6» da solo non dice cosa fare. E chi
            // legge questa risposta potrebbe non aver mai letto la descrizione
            // dell'operazione: l'avvertimento va qui, dove arriva comunque. E'
            // la stessa scelta di `verifica_geometria_corpo`, che accanto a
            // `stampabile` scrive `giudizio`.
            string giudizio =
                "Inserito «" + (nome ?? Path.GetFileName(percorso)) + "» a " +
                MmDiAssieme(x) + ", " + MmDiAssieme(y) + ", " + MmDiAssieme(z) + " mm. " +
                (fisso
                    ? "E' FISSO: non si muove perche' e' inchiodato, non perche' sia " +
                      "accoppiato. Sono due cose diverse."
                    : appoggiato
                        ? "ATTENZIONE: non e' fisso e non ha nessun accoppiamento — e' " +
                          "appoggiato li'. Si sposta se qualcuno lo trascina, e l'assieme " +
                          "sembra montato senza esserlo."
                        : accoppiamenti > 0
                            ? "Ha " + accoppiamenti +
                              (accoppiamenti == 1 ? " accoppiamento" : " accoppiamenti") +
                              ". Quanto sia ancora libero non te lo so dire: SOLIDWORKS " +
                              "non fornisce i gradi di liberta' residui da qui."
                            : "Non sono riuscito a dire se sia vincolato: non ho potuto " +
                              "leggere ne' gli accoppiamenti ne' lo stato.");
            misure["giudizio"] = giudizio;
            _log("[agente] " + giudizio);

            return RiuscitoSenzaCreare(misure);
        }

        // =================================================================

        /// <summary>
        /// I gradi di liberta' residui di un componente, o `-1` se non si sono
        /// potuti leggere.
        ///
        /// `IComponent2.GetRemainingDOFs` ha **dodici parametri in uscita** —
        /// due punti e quattro direzioni con i rispettivi stati — e il numero
        /// che serve e' il **valore di ritorno**, non qualcosa da dedurre da
        /// quelli. I dodici servono per dire *quale* liberta' resta, ed e' una
        /// voce a se' (GA4): qui si conta e basta.
        ///
        /// Su un componente appena inserito e non accoppiato ci si aspetta
        /// **6**: tre traslazioni e tre rotazioni. Uno **zero** su un componente
        /// che nessuno ha accoppiato non vuol dire «vincolato bene»: vuol dire
        /// FISSO, che e' un'altra cosa — per questo lo stato scrive anche
        /// `fisso` a parte (voce GA1).
        /// </summary>
        /// <remarks>
        /// `internal` e non `private`: la legge anche `StatoAssieme` (voce GA5),
        /// e due copie della stessa lettura sono due cose da tenere allineate a
        /// mano.
        /// </remarks>
        /// <remarks>
        /// `internal`: la usa anche `StatoAssieme`, per la stessa ragione per
        /// cui e' internal `GradiDiLiberta` — una sola lettura, un solo posto
        /// da riparare.
        /// </remarks>
        internal static int AccoppiamentiDi(IComponent2 componente)
        {
            return ContaGliAccoppiamenti(componente);
        }

        internal static GradiDiLibertaLetti GradiDiLiberta(IComponent2 componente)
        {
            var fuori = new GradiDiLibertaLetti { Stato = "non letto", Quanti = -1 };
            if (componente == null) return fuori;
            try
            {
                int s1, s2, s3, s4, s5, s6;
                MathPoint p1, p2;
                MathVector d1, d2, t1, t2;
                int esito = componente.GetRemainingDOFs(
                    out s1, out p1, out s2, out d1,
                    out s3, out p2, out s4, out d2,
                    out s5, out t1, out s6, out t2);

                fuori.Stato = NomeDelloStatoDof(esito);

                // IL CONTEGGIO SI RICAVA DAGLI STATI, e SOLO se l'esito dice
                // che l'informazione c'e'. I sei `status` sono di
                // `swDofStatus_e`: `Unused` vuol dire «questo posto non
                // descrive niente», non «zero gradi».
                if (esito == (int)swRemainingDofs_e.swRemainingDofs_Restricted ||
                    esito == (int)swRemainingDofs_e.swRemainingDofs_Unrestricted)
                {
                    fuori.Quanti = QuantiLiberi(s1, s2, s3, s4, s5, s6);
                }
            }
            catch { fuori.Stato = "non letto"; }
            return fuori;
        }

        /// <summary>
        /// Cosa ha risposto `GetRemainingDOFs`, e se ne e' uscito un numero.
        ///
        /// ── PERCHE' NON E' PIU' UN `int` ─────────────────────────────────
        ///
        /// Fino alla 0.58.0 questo metodo tornava direttamente il valore di
        /// `GetRemainingDOFs` e lo si pubblicava come
        /// `gradi_di_liberta_residui`. **Era sbagliato: quel valore non e' un
        /// conteggio, e' uno stato** (`swRemainingDofs_e`). Misurato il
        /// 16/9/2026: su un assieme appena costruito risponde **2**, che vuol
        /// dire `Unavailable` — e lo risponde **identico su un componente fisso
        /// e su uno libero**.
        ///
        /// Il ponte stampava «ha ancora 2 gradi di liberta' su 6». Numero
        /// inventato, unita' inventata, e uguale per un pezzo inchiodato e uno
        /// che ballava. *Un caso in cui due valori diversi danno lo stesso
        /// risultato non tabella niente* — e questo lo pubblicava come misura.
        /// </summary>
        internal sealed class GradiDiLibertaLetti
        {
            /// <summary>Il nome dello stato: «non disponibile», «libero», …</summary>
            public string Stato;

            /// <summary>Quanti gradi, oppure `-1` se non si e' potuto sapere.</summary>
            public int Quanti;

            public bool Disponibile { get { return Quanti >= 0; } }
        }

        private static int QuantiLiberi(params int[] stati)
        {
            int n = 0;
            foreach (var s in stati)
                if (s == (int)swDofStatus_e.swDofStatus_Free ||
                    s == (int)swDofStatus_e.swDofStatus_FreeNormal ||
                    s == (int)swDofStatus_e.swDofStatus_Instantaneous ||
                    s == (int)swDofStatus_e.swDofStatus_InstantaneousNormal) n++;
            return n;
        }

        /// <summary>
        /// `swRemainingDofs_e`, col nome. I valori sono
        /// Restricted=0, Unrestricted=1, **Unavailable=2**, Failed=3,
        /// RootComponent=4 — e il 2 e' quello che si incontra sempre.
        /// </summary>
        private static string NomeDelloStatoDof(int esito)
        {
            switch (esito)
            {
                case (int)swRemainingDofs_e.swRemainingDofs_Restricted: return "vincolato";
                case (int)swRemainingDofs_e.swRemainingDofs_Unrestricted: return "libero";
                case (int)swRemainingDofs_e.swRemainingDofs_Unavailable: return "non disponibile";
                case (int)swRemainingDofs_e.swRemainingDofs_Failed: return "non riuscito";
                case (int)swRemainingDofs_e.swRemainingDofs_RootComponent: return "componente radice";
                default: return "sconosciuto (" + esito.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        /// <summary>
        /// Ritrova un componente per nome fra quelli che l'assieme dichiara
        /// adesso, o `null` se non c'e'.
        ///
        /// `Name2` e' unico dentro un assieme — e' il nome con cui SOLIDWORKS
        /// stesso distingue le istanze — quindi cercarlo cosi' non e'
        /// ambiguo, nemmeno con due copie dello stesso file («base-1» e
        /// «base-2»).
        /// </summary>
        private static IComponent2 RileggiIlComponente(IAssemblyDoc assieme, string nome)
        {
            if (assieme == null || string.IsNullOrWhiteSpace(nome)) return null;
            try
            {
                var v = assieme.GetComponents(false) as object[];
                if (v == null) return null;
                foreach (var o in v)
                {
                    var c = o as IComponent2;
                    if (c == null) continue;
                    string suo = null;
                    try { suo = c.Name2; } catch { }
                    if (string.Equals(suo, nome, StringComparison.Ordinal)) return c;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Quanti accoppiamenti ha il componente, o `-1` se non si e' potuto
        /// sapere.
        ///
        /// ── `null` VUOL DIRE ZERO, MA SOLO SE IL COMPONENTE E' VIVO ─────
        ///
        /// `GetMates()` risponde **null quando gli accoppiamenti sono zero**,
        /// non un array vuoto — misurato il 16/9/2026 su un componente appena
        /// inserito. E' la convenzione di mezza API di SOLIDWORKS, ed e' anche
        /// il modo in cui «non ce ne sono» e «non ho potuto leggere» diventano
        /// la stessa risposta.
        ///
        /// Qui le due cose si separano, e il discrimine e' se il componente
        /// **risponde ancora**: se `Name2` si legge, la maniglia e' viva e un
        /// `null` da `GetMates` significa davvero zero. Se non si legge
        /// nemmeno il nome, non si sa niente e si dice `-1`.
        ///
        /// La differenza non e' accademica: `accoppiamenti = 0` porta a
        /// «appoggiato», che e' un **avvertimento**; `-1` porta a «non lo so»,
        /// che e' un'altra cosa. Confonderle vorrebbe dire o tacere un pericolo
        /// o gridarne uno che non c'e'.
        /// </summary>
        private static int ContaGliAccoppiamenti(IComponent2 componente)
        {
            if (componente == null) return -1;
            try
            {
                var suo = componente.Name2;      // se lancia, la maniglia e' morta
                if (suo == null) return -1;
            }
            catch { return -1; }

            try
            {
                var m = componente.GetMates() as object[];
                return m == null ? 0 : m.Length;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Dove sta l'origine del componente, in mm, o `null`.
        ///
        /// Stessa lettura di `StatoAssieme`, e per la stessa ragione sta scritta
        /// una volta sola: `GetTotalTransform(false)`, indici 9-10-11 di
        /// `ArrayData`. Il `false` esclude la trasformazione di esploso — con
        /// `true` si leggerebbe la posizione **nella vista esplosa**.
        /// </summary>
        private static object[] OrigineDelComponenteMm(IComponent2 c)
        {
            if (c == null) return null;
            try
            {
                var t = c.GetTotalTransform(false) as IMathTransform;
                if (t == null) return null;
                var d = t.ArrayData as double[];
                if (d == null || d.Length < 12) return null;
                return new object[]
                {
                    Math.Round(Unita.Mm(d[9]), 3),
                    Math.Round(Unita.Mm(d[10]), 3),
                    Math.Round(Unita.Mm(d[11]), 3),
                };
            }
            catch { return null; }
        }

        private static int QuantiComponentiInAssieme(IAssemblyDoc assieme)
        {
            try
            {
                var v = assieme.GetComponents(true) as object[];
                return v == null ? -1 : v.Length;
            }
            catch { return -1; }
        }

        private bool EAperto(string percorso)
        {
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                int guardia = 0;
                while (d != null && guardia++ < 500)
                {
                    string suo = "";
                    try { suo = d.GetPathName() ?? ""; } catch { }
                    if (!string.IsNullOrWhiteSpace(suo) &&
                        string.Equals(Path.GetFullPath(suo), Path.GetFullPath(percorso),
                                      StringComparison.OrdinalIgnoreCase))
                        return true;
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// L'elenco di cosa e' aperto, per il rifiuto.
        ///
        /// UN RIFIUTO CHE NON DICE L'ALTERNATIVA COSTRINGE A INDOVINARE, e
        /// indovinare e' il modo in cui si sbaglia in silenzio. Qui costa una
        /// riga: si dicono i file aperti, che sono gli unici percorsi che
        /// l'inserimento accetta.
        /// </summary>
        private string CosaEAperto()
        {
            var nomi = new List<string>();
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                int guardia = 0;
                while (d != null && guardia++ < 500)
                {
                    string suo = "";
                    try { suo = d.GetPathName() ?? ""; } catch { }
                    if (!string.IsNullOrWhiteSpace(suo)) nomi.Add(suo);
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }

            if (nomi.Count == 0)
                return "Adesso non e' aperto nessun documento salvato su disco: " +
                       "un componente mai salvato non si puo' inserire, perche' " +
                       "l'inserimento lo cerca per percorso.";
            return "Adesso sono aperti: " + string.Join("; ", nomi) + ".";
        }

        private static int TipoDi(IModelDoc2 modello)
        {
            try { return modello == null ? -1 : modello.GetType(); } catch { return -1; }
        }

        /// <summary>
        /// Millimetri per un messaggio.
        ///
        /// NOME DIVERSO DA QUALUNQUE ALTRO HELPER DI QUESTA CLASSE PARZIALE.
        /// `OperazioniSw` e' spalmata su una quindicina di file, e un metodo
        /// privato con lo stesso nome in due file e' un errore di compilazione
        /// che parla di «piu' definizioni», non del posto giusto. `MmDiAssieme`
        /// e' brutto e non collide: e' la stessa riparazione di `TernaMm` e
        /// `DistanzaFraPunti`.
        /// </summary>
        private static string MmDiAssieme(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
