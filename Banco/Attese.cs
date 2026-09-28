using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ProvaSaldatura
{
    /// <summary>
    /// IL VALUTATORE DELLE ATTESE E I SELETTORI, SENZA SOLIDWORKS (voce F7).
    ///
    /// ── PERCHE' STA IN UNA CLASSE SUA ────────────────────────────────────
    ///
    /// Questo file lo compilano DUE progetti: il banco, che apre il CAD, e
    /// `Prove.Contratti`, che gira su una macchina qualunque in pochi secondi e
    /// **non referenzia l'interop di SOLIDWORKS**. Finche' il valutatore stava
    /// in una classe parziale che tocca `IModelDoc2`, il secondo progetto non
    /// poteva nemmeno compilare.
    ///
    /// E deve poter girare li': **se e' rotto il valutatore, tutta la suite col
    /// CAD diventa verde gratis.** E' l'ultimo posto che ci si puo' permettere
    /// di non provare.
    ///
    /// Le misure che vengono dal DOCUMENTO — volume, corpi, ingombro — arrivano
    /// da un `Func&lt;string, object&gt;`: chi ha il documento in mano lo passa, chi
    /// non ce l'ha passa null e prova tutto il resto.
    /// </summary>
    internal static class Attese
    {
        /// <summary>
        /// Quello che una prova non ha potuto misurare: un campo assente, un
        /// selettore che non risolve, un caso scritto male (voce F4).
        /// </summary>
        internal sealed class NonHoPotutoMisurare : Exception
        {
            public NonHoPotutoMisurare(string messaggio) : base(messaggio) { }
        }

        /// <summary>
        /// Dove sta la libreria dei profili saldati. Si dice con
        /// `PROFILI_SALDATURA`, come nel banco e come nella libreria.
        ///
        /// QUI C'ERA IL PERCORSO DI UNA POSTAZIONE SOLA, come ripiego. Non ha
        /// mai aiutato nessun altro - su ogni altra macchina quella cartella
        /// non esiste - e intanto raccontava com'e' fatto quel disco. Adesso
        /// vuoto e' vuoto, e chi chiama lo deve dire.
        /// </summary>
        internal static string Profili
        {
            get
            {
                var suo = Environment.GetEnvironmentVariable("PROFILI_SALDATURA");
                return string.IsNullOrWhiteSpace(suo) ? null : suo;
            }
        }

        /// <summary>
        /// DOVE UN CASO PUO' SCRIVERE. `Banco\lavoro\`, la stessa cartella in
        /// cui `ChiudiScarti` butta i documenti di scarto: e' ignorata da git e
        /// si puo' svuotare in qualunque momento.
        ///
        /// Serve da quando ci sono operazioni che producono FILE —
        /// `esporta_stl`, `esporta_pdf`, `salva_documento`. Un caso che scrive
        /// in una cartella sua la riempirebbe di roba che nessuno pulisce; un
        /// caso che scrive accanto al repository sporcherebbe `git status`.
        /// La cartella si crea se non c'e': un caso non deve fallire perche'
        /// qualcuno ha svuotato il cestino.
        /// </summary>
        internal static string Lavoro
        {
            get
            {
                var qui = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetEntryAssembly() != null
                        ? System.Reflection.Assembly.GetEntryAssembly().Location
                        : System.Reflection.Assembly.GetExecutingAssembly().Location);
                // L'eseguibile sta in `Banco\bin\`: la cartella di lavoro e'
                // la sorella di `bin`.
                var dove = Path.GetFullPath(Path.Combine(qui ?? ".", "..", "lavoro"));
                try { if (!Directory.Exists(dove)) Directory.CreateDirectory(dove); } catch { }
                return dove;
            }
        }

        /// <summary>
        /// LA CARTELLA DI INSTALLAZIONE DI SOLIDWORKS, per i file di serie che
        /// un caso deve poter nominare senza copiarli nel repository (voce
        /// Z-C21.2, revisione: il modello di distinta di taglio «cut
        /// list.sldwldtbt», che e' di Dassault e non si versiona). Si dice con
        /// `SW_INSTALL_PATH`, la stessa di build.ps1; senza, si cerca dove la mette l'installazione
        /// predefinita, riconoscendola da SLDWORKS.exe. Se non si trova, il caso
        /// non si puo' misurare — non e' rosso.
        /// </summary>
        internal static string CartellaSolidWorks
        {
            get
            {
                var sua = Environment.GetEnvironmentVariable("SW_INSTALL_PATH");
                if (!string.IsNullOrWhiteSpace(sua)) return sua;
                foreach (var pf in new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetEnvironmentVariable("ProgramW6432"),
                })
                {
                    if (string.IsNullOrWhiteSpace(pf)) continue;
                    var d = Path.Combine(pf, "SOLIDWORKS Corp", "SOLIDWORKS");
                    try { if (File.Exists(Path.Combine(d, "SLDWORKS.exe"))) return d; } catch { }
                }
                return null;
            }
        }

        /// <summary>
        /// I FILE CHE UN CASO LEGGE E NON SCRIVE: `Banco\dati\`, versionata (voce
        /// Z-F5, 27/9/2026: l'immagine da ricalcare). `{LAVORO}` si svuota a ogni
        /// giro e non e' nel repository, quindi un file d'ingresso non puo' starci.
        /// </summary>
        internal static string Dati
        {
            get
            {
                var qui = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetEntryAssembly() != null
                        ? System.Reflection.Assembly.GetEntryAssembly().Location
                        : System.Reflection.Assembly.GetExecutingAssembly().Location);
                return Path.GetFullPath(Path.Combine(qui ?? ".", "..", "dati"));
            }
        }

        /// <summary>Un testo lungo, accorciato per stare su una riga di esito.</summary>
        internal static string Breve(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(nessun motivo)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 110 ? s.Substring(0, 110) + "..." : s;
        }

        // ==================================================================
        // LE ATTESE
        // ==================================================================
        internal static bool Combacia(
            Dictionary<string, object> attesa, Func<string, object> dalDocumento,
            Dictionary<string, object> stato, Dictionary<string, object> misure,
            out string dettaglio)
        {
            dettaglio = "";
            string cosa = Testo(attesa, "cosa");
            if (cosa == null) throw new NonHoPotutoMisurare("un'attesa senza «cosa»");

            // ── QUANDO LA MISURA E' UNA FRASE ─────────────────────────────
            //
            // `stato.forma` non e' un numero: e' la descrizione che il pezzo
            // da' di se stesso — «una rivoluzione del profilo Ø30 per 40, Ø20
            // per 30, Ø34 per 20». Pretenderla UGUALE vorrebbe dire riscrivere
            // il caso a ogni virgola sistemata; quello che conta e' che i
            // pezzi che contano ci siano, e che quelli che NON devono esserci
            // non ci siano — la seconda meta' e' la piu' importante, perche'
            // e' come si prende una frase che dichiara un profilo che il pezzo
            // non ha piu'.
            bool contiene = attesa.ContainsKey("contiene");
            bool nonContiene = attesa.ContainsKey("non_contiene");
            if (contiene || nonContiene)
            {
                if (attesa.ContainsKey("uguale"))
                    throw new NonHoPotutoMisurare("«" + cosa + "» ha «uguale» insieme a " +
                                        "«contiene»/«non_contiene»: decidi quale dei due");
                return Frase(cosa, attesa, dalDocumento, stato, misure, out dettaglio);
            }

            if (!attesa.ContainsKey("uguale")) throw new NonHoPotutoMisurare("«" + cosa + "» senza «uguale»");

            double tolleranza = 0;
            if (attesa.ContainsKey("tolleranza") && attesa["tolleranza"] != null)
            {
                tolleranza = Convert.ToDouble(attesa["tolleranza"], CultureInfo.InvariantCulture);
                // IL PERCHE' E' OBBLIGATORIO, e non e' burocrazia: una
                // tolleranza senza motivo e' il posto in cui un giorno qualcuno
                // allarghera' il numero invece di guardare il pezzo.
                if (Math.Abs(tolleranza) > 1e-12 && Testo(attesa, "perche_questa_tolleranza") == null)
                    throw new NonHoPotutoMisurare("«" + cosa + "» ha tolleranza " +
                                        tolleranza.ToString(CultureInfo.InvariantCulture) +
                                        " e non dice «perche_questa_tolleranza»");
            }

            object misurato = Misura(cosa, dalDocumento, stato, misure);
            object atteso = attesa["uguale"];

            // Un elenco di numeri: l'ingombro, un punto, una terna.
            var attesoLista = atteso as System.Collections.IEnumerable;
            if (attesoLista != null && !(atteso is string))
            {
                var attesi = Numeri(attesoLista);

                // ── UN ELENCO CHE NUMERI NON NE HA ───────────────────────
                //
                // `["C2"]`, i corpi su cui una booleana ha lavorato. Fino alla
                // 0.81.0 finiva in «non e' un elenco di numeri:
                // System.String[]» — un messaggio vero e inutile: il caso
                // chiedeva una cosa sensata e il valutatore non sapeva
                // confrontarla. Un elenco di TESTO si confronta a uno a uno,
                // con la stessa scrittura che serve alle gemelle.
                if (attesi == null)
                {
                    var misuratoElenco = misurato as System.Collections.IEnumerable;
                    if (misuratoElenco == null || misurato is string)
                    {
                        dettaglio = cosa + " non e' un elenco: " + CosaEra(misurato);
                        return false;
                    }
                    var q = new List<string>();
                    foreach (var x in attesoLista) q.Add(ComeSiScrive(x));
                    var r = new List<string>();
                    foreach (var x in misuratoElenco) r.Add(ComeSiScrive(x));
                    dettaglio = cosa + " [" + string.Join(", ", r) + "] (atteso [" +
                                string.Join(", ", q) + "])";
                    if (q.Count != r.Count) return false;
                    for (int i = 0; i < q.Count; i++)
                        if (!string.Equals(q[i], r[i], StringComparison.Ordinal)) return false;
                    return true;
                }

                var misurati = Numeri(misurato as System.Collections.IEnumerable);
                if (misurati == null)
                {
                    dettaglio = cosa + " non e' un elenco di numeri: " + CosaEra(misurato);
                    return false;
                }
                dettaglio = cosa + " " + Insieme(misurati) + " (atteso " + Insieme(attesi) +
                            (tolleranza > 0 ? " ±" + tolleranza.ToString(CultureInfo.InvariantCulture) : "") + ")";
                if (attesi.Length != misurati.Length) return false;
                for (int i = 0; i < attesi.Length; i++)
                    if (Math.Abs(attesi[i] - misurati[i]) > tolleranza) return false;
                return true;
            }

            // Un numero.
            double numeroAtteso;
            if (atteso != null && !(atteso is string) && !(atteso is bool) &&
                double.TryParse(Convert.ToString(atteso, CultureInfo.InvariantCulture),
                                NumberStyles.Any, CultureInfo.InvariantCulture, out numeroAtteso))
            {
                double numeroMisurato;
                if (misurato == null ||
                    !double.TryParse(Convert.ToString(misurato, CultureInfo.InvariantCulture),
                                     NumberStyles.Any, CultureInfo.InvariantCulture, out numeroMisurato))
                {
                    dettaglio = cosa + " non e' un numero: " + CosaEra(misurato);
                    return false;
                }
                dettaglio = string.Format(CultureInfo.InvariantCulture,
                    "{0} {1:0.###} (atteso {2:0.###}{3})", cosa, numeroMisurato, numeroAtteso,
                    tolleranza > 0 ? " ±" + tolleranza.ToString(CultureInfo.InvariantCulture) : "");
                return Math.Abs(numeroMisurato - numeroAtteso) <= tolleranza;
            }

            // Tutto il resto si confronta come testo: «materiale», un booleano.
            string a = Convert.ToString(atteso, CultureInfo.InvariantCulture);
            string m = Convert.ToString(misurato, CultureInfo.InvariantCulture);
            dettaglio = cosa + " «" + m + "» (atteso «" + a + "»)";
            return string.Equals(a, m, StringComparison.Ordinal);
        }

        /// <summary>
        /// UN'ATTESA SU UNA FRASE: quali pezzi ci devono essere, e quali no.
        ///
        /// `non_contiene` non e' la comodita' simmetrica di `contiene`: e' il
        /// motivo per cui questo esiste. Una frase che dichiara «il profilo
        /// Ø30 per 40» su un pezzo che quel profilo non ce l'ha piu' e' il
        /// difetto che non da' errore, e si prende solo pretendendo che una
        /// parola SPARISCA.
        ///
        /// Il confronto e' esatto — maiuscole e accenti compresi — perche' una
        /// frase che il modello legge cambia di senso con una lettera.
        /// </summary>
        private static bool Frase(
            string cosa, Dictionary<string, object> attesa, Func<string, object> dalDocumento,
            Dictionary<string, object> stato, Dictionary<string, object> misure,
            out string dettaglio)
        {
            object misurato = Misura(cosa, dalDocumento, stato, misure);
            if (misurato == null)
            {
                dettaglio = cosa + " non c'e': una frase assente non e' una frase vuota";
                return false;
            }

            // ── UN ELENCO NON E' «System.String[]» ───────────────────────
            //
            // `misure.profili[0].misure` e' un elenco di misure: «80X80X5»,
            // «100X100X6». Chiedere che CONTENGA una voce e' la cosa piu'
            // naturale che ci sia, e fino alla 0.91.0 falliva con
            //
            //     non dice «80X80X5»: System.String[]
            //
            // — un messaggio vero e inutile, perche' `Convert.ToString` su un
            // array da' il nome del tipo. Per `uguale` gli elenchi di testo
            // erano gia' stati insegnati (voce 0.81.0); per `contiene` no, e il
            // difetto e' rimasto finche' un caso non l'ha ripreso.
            //
            // Su un elenco «contiene» vuol dire **una delle voci lo contiene**,
            // non «la scrittura dell'elenco lo contiene»: cosi' cercare
            // «80X80X5» trova la voce, e cercare «80X80» trova la stessa voce
            // per prefisso, che e' quello che chi scrive il caso si aspetta.
            var voci = Elenco(misurato);
            bool eUnElenco = voci != null;
            string frase = eUnElenco
                ? string.Join(", ", voci)
                : Convert.ToString(misurato, CultureInfo.InvariantCulture);

            var mancano = new List<string>();
            foreach (var p in Pezzi(attesa, "contiene"))
                if (!Dentro(voci, frase, p)) mancano.Add("«" + p + "»");

            var diTroppo = new List<string>();
            foreach (var p in Pezzi(attesa, "non_contiene"))
                if (Dentro(voci, frase, p)) diTroppo.Add("«" + p + "»");

            if (mancano.Count == 0 && diTroppo.Count == 0)
            {
                dettaglio = cosa + (eUnElenco ? " [" + Breve(frase) + "]" : ": " + Breve(frase));
                return true;
            }
            dettaglio = cosa +
                (mancano.Count > 0 ? " non dice " + string.Join(", ", mancano) : "") +
                (mancano.Count > 0 && diTroppo.Count > 0 ? ", e" : "") +
                (diTroppo.Count > 0 ? " dice ancora " + string.Join(", ", diTroppo) : "") +
                ": " + Breve(frase);
            return false;
        }

        /// <summary>
        /// Le voci di un elenco, gia' scritte. Null quando cio' che si e'
        /// misurato non e' un elenco — e una stringa NON e' un elenco, anche
        /// se in C# e' enumerabile carattere per carattere.
        /// </summary>
        private static List<string> Elenco(object misurato)
        {
            if (misurato == null || misurato is string) return null;
            var e = misurato as System.Collections.IEnumerable;
            if (e == null) return null;
            var fuori = new List<string>();
            foreach (var x in e) fuori.Add(ComeSiScrive(x));
            return fuori;
        }

        /// <summary>
        /// Il pezzo cercato c'e'? Su un elenco basta che lo contenga UNA voce;
        /// su una frase, la frase.
        /// </summary>
        /// <summary>
        /// SENZA GUARDARE LE MAIUSCOLE, e il perche' e' costato tre volte.
        ///
        /// ── I TRE INCIDENTI ──────────────────────────────────────────────
        ///
        ///   21/9  un'attesa cercava «vero», il testo diceva «VERO»
        ///   23/9  cercava «accavallano», il testo dice «si accavalla»
        ///   23/9  cercava «che serviva», il testo dice «CHE SERVIVA»
        ///
        /// Il secondo era una desinenza, e si e' riparato cercando la radice.
        /// Il primo e il terzo sono la stessa cosa, e non erano difetti dei
        /// casi: `contiene` guarda PROSA, scritta per una persona, e in questo
        /// repository l'enfasi si fa alzando la voce — cioe' cambiando le
        /// maiuscole. Un'attesa che si rompe quando una frase viene riscritta
        /// con piu' enfasi non sta misurando cio' che dice: sta misurando come
        /// e' scritta.
        ///
        /// *Tre incidenti dalla stessa causa non sono tre casi da riparare: e'
        /// un operatore da riparare.*
        ///
        /// ── E `non_contiene` DIVENTA PIU' SEVERO ─────────────────────────
        ///
        /// Cambiano insieme, ed e' il verso scomodo: un «non dice X» che
        /// passava solo perche' il testo scriveva «x» adesso diventa rosso. E'
        /// giusto che lo diventi — quel verde era un caso fortunato, non una
        /// misura — ed e' il motivo per cui questa modifica si e' fatta con un
        /// giro di banco intero addosso invece che a occhio.
        ///
        /// ── CHI VUOLE DAVVERO LE MAIUSCOLE ───────────────────────────────
        ///
        /// Non ha un operatore, e va detto invece di lasciarlo scoprire.
        /// Nessun caso oggi ne ha bisogno: si e' guardato prima di togliere il
        /// confronto esatto. Il giorno che servisse, si aggiunge
        /// `contiene_esatto` — un nome diverso, non un booleano in piu' su
        /// questo, perche' un interruttore che cambia il senso di un operatore
        /// e' un operatore travestito.
        /// </summary>
        private static bool Dentro(List<string> voci, string frase, string pezzo)
        {
            if (voci != null)
                return voci.Any(v => v.IndexOf(pezzo, StringComparison.OrdinalIgnoreCase) >= 0);
            return frase.IndexOf(pezzo, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>I pezzi di frase di un campo: una stringa sola o un elenco.</summary>
        private static List<string> Pezzi(Dictionary<string, object> attesa, string campo)
        {
            var fuori = new List<string>();
            object v;
            if (!attesa.TryGetValue(campo, out v) || v == null) return fuori;
            if (v is string) { fuori.Add((string)v); return fuori; }
            var elenco = v as System.Collections.IEnumerable;
            if (elenco == null)
            { fuori.Add(Convert.ToString(v, CultureInfo.InvariantCulture)); return fuori; }
            foreach (var x in elenco)
                fuori.Add(Convert.ToString(x, CultureInfo.InvariantCulture));
            if (fuori.Count == 0)
                throw new NonHoPotutoMisurare("«" + campo + "» e' un elenco vuoto: " +
                                    "non chiede niente, e passerebbe sempre");
            return fuori;
        }

        /// <summary>
        /// Da dove si legge un `cosa`. Tre sorgenti, e il prefisso lo dice:
        /// niente = quello che misura il banco per conto suo, `stato.` = lo
        /// stato dell'add-in, `misure.` = quello che l'operazione ha riportato.
        /// </summary>
        internal static object Misura(
            string cosa, Func<string, object> dalDocumento,
            Dictionary<string, object> stato, Dictionary<string, object> misure)
        {
            // LE TRE MISURE DEL DOCUMENTO ARRIVANO DA FUORI, e chi non ha un
            // documento in mano passa null: e' quello che rende questo file
            // compilabile dove SOLIDWORKS non c'e' (voce F7).
            // NON C'E' PIU' UN ELENCO DI NOMI QUI.
            //
            // Ce n'era uno, e si e' scollato: una misura nuova aggiunta a chi
            // misura, e dimenticata qui, usciva «non so misurare» con l'elenco
            // vecchio accanto — un messaggio che dice il falso su se stesso.
            // Tutto cio' che non ha un prefisso lo si chiede a chi il documento
            // ce l'ha, ed e' lui a dire se quel nome non lo conosce.
            if (!cosa.StartsWith("stato.", StringComparison.Ordinal) &&
                !cosa.StartsWith("misure.", StringComparison.Ordinal))
            {
                if (dalDocumento == null)
                    throw new NonHoPotutoMisurare(
                        "«" + cosa + "» si misura sul documento, e qui non ce n'e' uno");
                return dalDocumento(cosa);
            }
            if (cosa.StartsWith("stato.", StringComparison.Ordinal))
                return Dentro(stato, cosa.Substring(6), cosa);
            return Dentro(misure, cosa.Substring(7), cosa);
        }

        /// <summary>
        /// UN VALORE COME STRINGA, ANCHE QUANDO E' UN ELENCO.
        ///
        /// `Convert.ToString` su un `object[]` torna «System.Object[]»: DUE
        /// PROFILI DIVERSI SI SCRIVONO UGUALI. Il confronto fra un caso che
        /// deve fallire e la sua gemella si faceva cosi', e diventava cieco
        /// proprio sull'argomento che li distingue. La cecita' va in tutte e
        /// due le direzioni: qui ha dato un errore di banco onesto — «e'
        /// identica al rifiuto» su una coppia che differiva SOLO nei contorni —
        /// ma la stessa riga avrebbe detto «differisce per 1 argomento»
        /// guardando due liste che non aveva letto.
        ///
        /// Sta qui, e non accanto a chi la usa, perche' e' una funzione pura
        /// su un dizionario: `Prove.Contratti` la prova in un millesimo di
        /// secondo, senza CAD (voce F7).
        /// </summary>
        internal static string ComeSiScrive(object v)
        {
            if (v == null) return "(niente)";
            if (v is string) return (string)v;

            var dizionario = v as Dictionary<string, object>;
            if (dizionario != null)
            {
                var voci = new List<string>();
                foreach (var k in dizionario.Keys.OrderBy(x => x, StringComparer.Ordinal))
                    voci.Add(k + "=" + ComeSiScrive(dizionario[k]));
                return "{" + string.Join(", ", voci) + "}";
            }

            var elenco = v as System.Collections.IEnumerable;
            if (elenco != null)
            {
                var pezzi = new List<string>();
                foreach (var x in elenco) pezzi.Add(ComeSiScrive(x));
                return "[" + string.Join(", ", pezzi) + "]";
            }

            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// LA GEMELLA HA A CHE VEDERE COL RIFIUTO? (voce F5)
        ///
        /// La regola: **argomenti non identici, e almeno un argomento in comune
        /// con lo stesso valore.** La voce proponeva «differisce per UN solo
        /// argomento»; misurata sulle coppie canoniche ne rifiutava tre su
        /// quattro, e chi chiama dice quanti ne differiscono invece di
        /// decidere al posto di chi legge.
        ///
        /// ── L'ECCEZIONE DELL'ARGOMENTO UNICO, CHE E' ARITMETICA ───────────
        ///
        /// `crea_asse` prende `faccia_uid` e basta. Due casi che differiscono
        /// per quell'unico argomento NON POSSONO averne uno in comune: la
        /// regola li rifiuterebbe sempre, e non perche' siano scollegati ma
        /// perche' l'operazione ha un argomento solo. Rifiutarli vorrebbe dire
        /// vietare la coppia a meta' del catalogo. Quando le chiavi in gioco
        /// sono una sola, «niente in comune» non e' un indizio: e' l'arita'.
        /// </summary>
        internal static bool SiSomigliano(
            Dictionary<string, object> miei, Dictionary<string, object> suoi,
            out string perche)
        {
            miei = miei ?? new Dictionary<string, object>();
            suoi = suoi ?? new Dictionary<string, object>();

            var chiavi = new HashSet<string>(miei.Keys, StringComparer.Ordinal);
            foreach (var k in suoi.Keys) chiavi.Add(k);

            int diversi = 0, uguali = 0;
            var quali = new List<string>();
            foreach (var k in chiavi.OrderBy(x => x, StringComparer.Ordinal))
            {
                string a = miei.ContainsKey(k) ? ComeSiScrive(miei[k]) : null;
                string b = suoi.ContainsKey(k) ? ComeSiScrive(suoi[k]) : null;
                if (string.Equals(a, b, StringComparison.Ordinal)) uguali++;
                else { diversi++; quali.Add(k); }
            }

            if (diversi == 0)
            { perche = "e' identica al rifiuto: non e' una gemella, e' una copia"; return false; }

            if (uguali == 0 && chiavi.Count > 1)
            {
                perche = "non ha nemmeno un argomento in comune col rifiuto: " +
                         "sono due operazioni che non si parlano";
                return false;
            }

            perche = "stessa operazione, differisce per " + diversi +
                     (diversi == 1 ? " argomento (" : " argomenti (") +
                     string.Join(", ", quali) + ")";
            if (uguali == 0)
                perche += ", che e' l'unico che questa operazione prende";
            return true;
        }

        /// <summary>
        /// Un percorso dentro un dizionario: `facce[0].area_mm2`.
        /// Null quando il percorso non porta da nessuna parte — e chi chiama lo
        /// fa diventare un rosso, perche' un campo assente non e' uno zero.
        /// </summary>
        internal static object Dentro(object radice, string percorso, string nomeIntero)
        {
            object qui = radice;
            foreach (var pezzo in percorso.Split('.'))
            {
                string chiave = pezzo;
                int indice = -1;
                int quadra = pezzo.IndexOf('[');
                if (quadra >= 0 && pezzo.EndsWith("]", StringComparison.Ordinal))
                {
                    chiave = pezzo.Substring(0, quadra);
                    int.TryParse(pezzo.Substring(quadra + 1, pezzo.Length - quadra - 2), out indice);
                }

                var mappa = qui as Dictionary<string, object>;
                if (mappa == null)
                    throw new NonHoPotutoMisurare(
                        "campo assente: " + nomeIntero + "; «" + chiave +
                        "» andrebbe cercato dentro qualcosa che non e' un oggetto");

                // LA CHIAVE CHE NON C'E' E UN VALORE NULL NON SONO LA STESSA
                // COSA, ed e' tutta la voce F4. `materiale: null` vuol dire
                // «quel posto esiste ed e' vuoto»; una chiave che manca vuol
                // dire che il caso sta leggendo un campo che non esiste piu' —
                // e un `null` confrontato con un `null` passerebbe.
                if (!mappa.ContainsKey(chiave))
                    throw new NonHoPotutoMisurare(
                        "campo assente: " + nomeIntero + "; ci sono: " +
                        string.Join(", ", mappa.Keys.OrderBy(k => k, StringComparer.Ordinal)));
                qui = mappa[chiave];

                if (indice >= 0)
                {
                    var lista = qui as System.Collections.IList;
                    if (lista == null)
                        throw new NonHoPotutoMisurare(
                            "campo assente: " + nomeIntero + "; «" + chiave + "» non e' un elenco");
                    if (indice >= lista.Count)
                        throw new NonHoPotutoMisurare(
                            "campo assente: " + nomeIntero + "; «" + chiave + "» ha " +
                            lista.Count + " voci e il caso chiede la numero " + indice);
                    qui = lista[indice];
                }
            }
            return qui;
        }

        // ==================================================================
        // I SELETTORI
        // ==================================================================
        internal static object Risolvi(object grezzo, Dictionary<string, object> stato)
        {
            var mappa = grezzo as Dictionary<string, object>;
            if (mappa != null)
            {
                var fuori = new Dictionary<string, object>();
                foreach (var v in mappa) fuori[v.Key] = Risolvi(v.Value, stato);
                return fuori;
            }
            var lista = grezzo as System.Collections.IList;
            if (lista != null)
            {
                var fuori = new List<object>();
                foreach (var v in lista) fuori.Add(Risolvi(v, stato));
                return fuori;
            }
            var testo = grezzo as string;
            if (testo != null && testo.StartsWith("@", StringComparison.Ordinal))
                return Seleziona(testo, stato);

            // I SEGNAPOSTO: un caso che nomina un percorso di QUESTA macchina
            // non e' una prova, e' un ricordo. La libreria dei profili saldati
            // sta dove la mette ogni installazione, e il caso dice «{PROFILI}»
            // invece di sapere dov'e'.
            if (testo != null && testo.Contains("{PROFILI}"))
            {
                // `String.Replace(a, null)` solleva `ArgumentNullException`, e
                // da qui uscirebbe come un guasto del banco invece che come la
                // cosa che e': una precondizione che manca. Si dice.
                if (Profili == null)
                    throw new NonHoPotutoMisurare(
                        "Questo caso nomina {PROFILI}, ma PROFILI_SALDATURA non e' " +
                        "impostata e non c'e' un percorso predefinito. La libreria " +
                        "dei profili e' di chi lavora, non del repository: dille dov'e'.");
                return testo.Replace("{PROFILI}", Profili);
            }
            // `{LAVORO}`: dove un caso puo' scrivere un file senza sporcare
            // ne' il repository ne' il disco di chi lavora.
            if (testo != null && testo.Contains("{LAVORO}"))
                return testo.Replace("{LAVORO}", Lavoro);
            // `{DATI}`: i file d'ingresso versionati (Banco\dati\).
            if (testo != null && testo.Contains("{DATI}"))
                return testo.Replace("{DATI}", Dati);
            // `{SOLIDWORKS}`: i file DI SERIE dell'installazione (i modelli di
            // tabella), che non sono ne' del repository ne' di chi lavora.
            if (testo != null && testo.Contains("{SOLIDWORKS}"))
            {
                var sw = CartellaSolidWorks;
                if (sw == null)
                    throw new NonHoPotutoMisurare(
                        "Questo caso nomina {SOLIDWORKS}, ma non trovo la cartella di " +
                        "installazione di SOLIDWORKS: dilla con SW_INSTALL_PATH.");
                return testo.Replace("{SOLIDWORKS}", sw);
            }
            return grezzo;
        }

        internal static string Seleziona(string espressione, Dictionary<string, object> stato)
        {
            int apre = espressione.IndexOf('(');
            if (apre < 0 || !espressione.EndsWith(")", StringComparison.Ordinal))
                throw new NonHoPotutoMisurare("selettore storto: «" + espressione + "»");
            string tipo = espressione.Substring(1, apre - 1);
            var condizioni = Condizioni(espressione.Substring(apre + 1, espressione.Length - apre - 2));

            switch (tipo)
            {
                // `n=` VALE PER TUTTI, non solo per le feature: fra quattro
                // spigoli lunghi uguali non esiste nessun criterio, e chi
                // scrive il caso l'ordine puo' deciderlo — purche' lo DICA.
                // Senza `n=` l'ambiguita' resta un rifiuto.
                case "faccia":  return UnoSolo(espressione, "facce", stato, condizioni, "uid", Ennesimo(espressione, condizioni));
                case "spigolo": return UnoSolo(espressione, "spigoli", stato, condizioni, "uid", Ennesimo(espressione, condizioni));
                case "feature": return UnaFeature(espressione, stato, condizioni);
                case "quota":   return UnaQuota(espressione, stato, condizioni);
                // I CORPI SI CHIAMANO PER NOME, NON PER NUMERO. Misurato il
                // 25/9/2026 su tre membri strutturali creati nello stesso
                // ordine in due giri: una volta lo stato dice C1 = la barra
                // corta, l'altra C1 = la lunga. «C1» scritto in un caso vuol
                // dire un corpo diverso a seconda del giro; il nome («... (2)»)
                // segue invece l'ordine di creazione, ed e' quello che il caso
                // intende. Il nome ha le parentesi dentro, e il selettore le
                // regge: si apre alla PRIMA e si chiude all'ULTIMA.
                case "corpo":   return UnoSolo(espressione, "corpi", stato, condizioni, "uid", Ennesimo(espressione, condizioni));
                default:
                    throw new NonHoPotutoMisurare("selettore sconosciuto «@" + tipo + "»: ci sono " +
                                        "@faccia, @spigolo, @feature, @quota, @corpo");
            }
        }

        /// <summary>
        /// `verso=davanti, lungo~=100`: il `~` vuol dire «circa», e serve per le
        /// lunghezze, che non sono mai intere quando il pezzo e' stato ricostruito.
        /// </summary>
        internal sealed class Condizione
        {
            public string Campo;
            public string Valore;
            public bool Circa;
        }

        internal static List<Condizione> Condizioni(string dentro)
        {
            var fuori = new List<Condizione>();
            foreach (var pezzo in dentro.Split(','))
            {
                var t = pezzo.Trim();
                if (t.Length == 0) continue;
                bool circa = t.Contains("~=");
                int uguale = circa ? t.IndexOf("~=", StringComparison.Ordinal) : t.IndexOf('=');
                if (uguale < 0) throw new NonHoPotutoMisurare("condizione senza «=»: «" + t + "»");
                fuori.Add(new Condizione
                {
                    Campo = t.Substring(0, uguale).Trim(),
                    Valore = t.Substring(uguale + (circa ? 2 : 1)).Trim(),
                    Circa = circa,
                });
            }
            return fuori;
        }

        // I NOMI DEL CASO E I NOMI DELLO STATO NON SONO GLI STESSI, e la
        // traduzione sta qui e in un punto solo. `@spigolo(asse=Z)` si legge
        // bene; il campo dello stato si chiama `lungo`, che da solo vorrebbe
        // dire un'altra cosa.
        internal static string CampoDelloStato(string nomeNelCaso)
        {
            switch (nomeNelCaso)
            {
                case "asse":  return "lungo";
                case "lungo": return "lunghezza_mm";
                case "area":  return "area_mm2";
                default:      return nomeNelCaso;
            }
        }

        /// <summary>
        /// Il valore che una condizione del selettore confronta.
        ///
        /// `x`, `y`, `z` SONO LE COORDINATE DI `punto_mm` (25/9/2026, voce E18).
        /// Fra i quattro spigoli lunghi 120 di una piastra nessun campo dello
        /// stato distingueva il fianco sinistro davanti da quello dietro, e
        /// `n=` sceglie per ORDINE — lo stesso ordine che per i corpi si e'
        /// visto cambiare fra un giro e l'altro (voce Z-C16). Con le
        /// coordinate lo spigolo si nomina per DOVE sta:
        /// `@spigolo(asse=Y,x~=-100,z~=20)`.
        /// </summary>
        internal static bool ValoreDelCampo(Dictionary<string, object> voce, string campo, out object valore)
        {
            int indice = campo == "x" ? 0 : campo == "y" ? 1 : campo == "z" ? 2 : -1;
            if (indice >= 0)
            {
                valore = null;
                object punto;
                if (!voce.TryGetValue("punto_mm", out punto)) return false;
                var elenco = punto as System.Collections.IList;
                if (elenco == null || elenco.Count < 3) return false;
                valore = elenco[indice];
                return true;
            }
            return voce.TryGetValue(CampoDelloStato(campo), out valore);
        }

        internal static string UnoSolo(
            string espressione, string famiglia, Dictionary<string, object> stato,
            List<Condizione> condizioni, string campoDaTornare, int? ennesimo)
        {
            var voci = (stato != null && stato.ContainsKey(famiglia)
                ? stato[famiglia] : null) as System.Collections.IList;
            if (voci == null)
                throw new NonHoPotutoMisurare("lo stato non ha «" + famiglia + "»: " + espressione);

            var trovate = new List<Dictionary<string, object>>();
            foreach (var v in voci)
            {
                var voce = v as Dictionary<string, object>;
                if (voce == null) continue;
                bool va = true;
                foreach (var c in condizioni)
                {
                    if (c.Campo == "n") continue;
                    object valore;
                    if (!ValoreDelCampo(voce, c.Campo, out valore) || valore == null)
                    { va = false; break; }
                    if (c.Circa)
                    {
                        double a, b;
                        if (!double.TryParse(Convert.ToString(valore, CultureInfo.InvariantCulture),
                                             NumberStyles.Any, CultureInfo.InvariantCulture, out a) ||
                            !double.TryParse(c.Valore, NumberStyles.Any,
                                             CultureInfo.InvariantCulture, out b) ||
                            Math.Abs(a - b) > 0.5)
                        { va = false; break; }
                    }
                    else if (!string.Equals(
                        Convert.ToString(valore, CultureInfo.InvariantCulture), c.Valore,
                        StringComparison.OrdinalIgnoreCase))
                    { va = false; break; }
                }
                if (va) trovate.Add(voce);
            }

            if (ennesimo.HasValue)
            {
                if (ennesimo.Value < 1 || ennesimo.Value > trovate.Count)
                    throw new NonHoPotutoMisurare(espressione + ": ne ho trovate " + trovate.Count +
                                        ", e il caso chiede la numero " + ennesimo.Value);
                return Convert.ToString(trovate[ennesimo.Value - 1][campoDaTornare]);
            }

            // ZERO O PIU' D'UNA: SI RIFIUTA, non si sceglie la prima.
            // E' il difetto silenzioso che questa voce nomina: un caso che gira
            // su una faccia diversa da quella che il nome del file promette, e
            // che magari torna lo stesso volume.
            if (trovate.Count == 0)
                throw new NonHoPotutoMisurare(espressione + ": non l'ho trovata");
            if (trovate.Count > 1)
                throw new NonHoPotutoMisurare(espressione + ": ne ho trovate " + trovate.Count +
                                    ". Sceglierne una sarebbe indovinare: aggiungi una " +
                                    "condizione, oppure «n=» se l'ordine lo decidi tu");
            return Convert.ToString(trovate[0][campoDaTornare]);
        }

        internal static string UnaFeature(
            string espressione, Dictionary<string, object> stato, List<Condizione> condizioni)
        {
            return UnoSolo(espressione, "feature", stato, condizioni, "uid",
                           Ennesimo(espressione, condizioni));
        }

        /// <summary>Il `n=` di un selettore, se c'e'.</summary>
        internal static int? Ennesimo(string espressione, List<Condizione> condizioni)
        {
            var n = condizioni.FirstOrDefault(c => c.Campo == "n");
            if (n == null) return null;
            int k;
            if (!int.TryParse(n.Valore, out k))
                throw new NonHoPotutoMisurare(espressione + ": «n» non e' un numero");
            return k;
        }

        /// <summary>
        /// `@quota(feature=Schizzo1,nome=D1)` -> il nome ESTESO della quota, come
        /// lo scrive lo stato: `D1@Schizzo1@Parte286.Part`.
        ///
        /// E' il selettore che la voce chiamava «non un lusso»:
        /// `piani/cambia_diametro.json` scrive il nome col titolo del documento
        /// dentro, e quel `Parte76` cambia a ogni giro — **quel piano oggi non
        /// si puo' rieseguire.** Risolvendolo sullo stato, il caso nomina la
        /// quota e il titolo lo mette il banco.
        ///
        /// `angolare=true` (voce Z-C21.2, rilievo M3) tiene solo le quote che lo
        /// stato scrive in `gradi`, e allora «nome» si puo' omettere: i nomi che
        /// SOLIDWORKS da' alle quote di uno smusso (D1, D2) e l'ordine in cui le
        /// elenca non sono misurati, mentre «l'unica quota angolare del pezzo» e'
        /// un fatto che lo stato dice.
        /// </summary>
        internal static string UnaQuota(
            string espressione, Dictionary<string, object> stato, List<Condizione> condizioni)
        {
            string dentroA = condizioni.FirstOrDefault(c => c.Campo == "feature")?.Valore;
            string chiamata = condizioni.FirstOrDefault(c => c.Campo == "nome")?.Valore;
            string angolareTesto = condizioni.FirstOrDefault(c => c.Campo == "angolare")?.Valore;
            bool? angolare = null;
            if (angolareTesto != null)
            {
                bool a;
                if (!bool.TryParse(angolareTesto, out a))
                    throw new NonHoPotutoMisurare(espressione + ": «angolare» e' true o false");
                angolare = a;
            }
            if (chiamata == null && angolare == null)
                throw new NonHoPotutoMisurare(espressione + ": serve «nome» (oppure «angolare»)");

            var feature = (stato != null && stato.ContainsKey("feature")
                ? stato["feature"] : null) as System.Collections.IList;
            if (feature == null) throw new NonHoPotutoMisurare("lo stato non ha «feature»: " + espressione);

            var trovate = new List<string>();
            foreach (var f in feature)
            {
                var voce = f as Dictionary<string, object>;
                if (voce == null || !voce.ContainsKey("quote")) continue;
                var quote = voce["quote"] as System.Collections.IList;
                if (quote == null) continue;
                foreach (var q in quote)
                {
                    var quota = q as Dictionary<string, object>;
                    if (quota == null || !quota.ContainsKey("nome")) continue;
                    string esteso = Convert.ToString(quota["nome"]);
                    // «D1@Schizzo1@Parte286.Part» -> corto «D1», dentro «Schizzo1»
                    var pezzi = esteso.Split('@');
                    if (pezzi.Length < 2) continue;
                    if (chiamata != null &&
                        !string.Equals(pezzi[0], chiamata, StringComparison.OrdinalIgnoreCase)) continue;
                    if (angolare.HasValue && quota.ContainsKey("gradi") != angolare.Value) continue;
                    if (dentroA != null &&
                        !string.Equals(pezzi[1], dentroA, StringComparison.OrdinalIgnoreCase)) continue;
                    trovate.Add(esteso);
                }
            }

            if (trovate.Count == 0) throw new NonHoPotutoMisurare(espressione + ": non l'ho trovata");
            if (trovate.Count > 1)
                throw new NonHoPotutoMisurare(espressione + ": ne ho trovate " + trovate.Count +
                                    " (" + string.Join(", ", trovate) + ")");
            return trovate[0];
        }

        // ==================================================================
        // MINUTERIA
        // ==================================================================
        internal static string Testo(Dictionary<string, object> mappa, string chiave)
        {
            object v;
            if (mappa == null || !mappa.TryGetValue(chiave, out v) || v == null) return null;
            var s = Convert.ToString(v);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        internal static bool Vero(Dictionary<string, object> mappa, string chiave)
        {
            object v;
            if (mappa == null || !mappa.TryGetValue(chiave, out v) || v == null) return false;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }

        internal static List<object> Elenco(Dictionary<string, object> mappa, string chiave)
        {
            object v;
            if (mappa == null || !mappa.TryGetValue(chiave, out v) || v == null) return null;
            var lista = v as System.Collections.IList;
            if (lista == null) return null;
            var fuori = new List<object>();
            foreach (var x in lista) fuori.Add(x);
            return fuori;
        }

        internal static double[] Numeri(System.Collections.IEnumerable lista)
        {
            if (lista == null) return null;
            var fuori = new List<double>();
            foreach (var x in lista)
            {
                double d;
                if (!double.TryParse(Convert.ToString(x, CultureInfo.InvariantCulture),
                                     NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    return null;
                fuori.Add(d);
            }
            return fuori.ToArray();
        }

        internal static string Insieme(double[] n)
        {
            return "[" + string.Join(", ", n.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture))) + "]";
        }

        /// <summary>
        /// Che cos'e' quel valore, per un messaggio. Si chiama cosi' e non
        /// `Descrivi` perche' in `ProvaNuove.cs` un `Descrivi` c'e' gia': due
        /// metodi con lo stesso nome nella stessa classe parziale non
        /// compilano, ed e' la fine migliore possibile per un nome ripetuto.
        /// </summary>
        internal static string CosaEra(object x)
        {
            if (x == null) return "(non c'e')";
            return Breve(Convert.ToString(x, CultureInfo.InvariantCulture));
        }

        internal static int QuanteFeature(Dictionary<string, object> stato)
        {
            var f = (stato != null && stato.ContainsKey("feature")
                ? stato["feature"] : null) as System.Collections.IList;
            return f == null ? 0 : f.Count;
        }
    }
}
