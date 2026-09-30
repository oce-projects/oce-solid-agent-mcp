using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
// Stessa ragione di `CasiDichiarati.cs`: `Testo`, `Elenco`, `Risolvi` e `Breve`
// vivono in `Attese`, che non tocca SOLIDWORKS (voce F7). `using static` li
// lascia scrivere come se fossero qui.
using static ProvaSaldatura.Attese;

namespace ProvaSaldatura
{
    /// <summary>
    /// IL BANCO IMPARA A PREPARARE UN ASSIEME (voce GA0).
    ///
    /// ── PERCHE' QUESTA VOCE VIENE PRIMA DI TUTTA LA SEZIONE G-A ──────────
    ///
    /// Fino al 16 settembre 2026 `NuovaParte()` era l'UNICO modo in cui il
    /// banco preparava un documento, e usava `swDefaultTemplatePart`. Nessun
    /// `NuovoAssieme`, e `"parte": { "nuova": true }` era l'unica forma di
    /// preparazione che i 270 casi sapessero esprimere.
    ///
    /// Conseguenza: **nessuna operazione di assieme poteva avere un caso.** Si
    /// sarebbero potute scrivere tutte e nove le voci G-A, farle funzionare a
    /// mano, e non avere niente che se ne accorgesse quando si rompono — la
    /// condizione che questo progetto rifiuta per le parti, accettata per gli
    /// assiemi solo perche' era scomodo il contrario.
    ///
    /// *Un'operazione senza un caso non e' un'operazione: e' una dimostrazione.*
    ///
    /// ── PERCHE' UN ASSIEME NON E' UN DOCUMENTO VUOTO ─────────────────────
    ///
    /// `AddComponent5` prende un **percorso su disco**: non si puo' inserire un
    /// documento mai salvato. Quindi preparare un assieme e' una catena di sei
    /// passi, e ognuno puo' fallire in modo suo:
    ///
    ///   1. creare la parte componente (le stesse operazioni dei casi),
    ///   2. SALVARLA in `Banco\lavoro`,
    ///   3. chiuderla,
    ///   4. creare il documento di assieme,
    ///   5. riaprire i componenti e inserirli,
    ///   6. ricostruire.
    ///
    /// E poi ripulire, che e' il settimo: `ChiudiScarti` chiude i documenti, non
    /// cancella i file.
    ///
    /// ── LA REGOLA CHE NON SI ROMPE ───────────────────────────────────────
    ///
    /// Ogni passo che non riesce chiama `ErroreDiBanco`, **non** `Segna(false)`.
    /// Non e' una prova rossa: e' il banco che non e' riuscito a preparare il
    /// pezzo, e sono due cose diverse (voce F12). Una preparazione fallita
    /// dichiarata «prova fallita» farebbe credere che si sia misurato qualcosa.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>
        /// LA PROVA CHE QUESTA CAPACITA' FUNZIONI DAVVERO (voce GA0).
        ///
        /// Non c'e' ancora nessun caso `.json` di assieme, e il motivo e'
        /// onesto: **non esiste nessuna operazione di assieme da eseguire**, e
        /// `EseguiIPassi` vuole almeno un passo. Un caso finto, con dentro
        /// un'operazione di parte messa li' per riempire, proverebbe altro.
        ///
        /// Quindi la capacita' si prova come il banco prova se stesso (voci F1,
        /// F12, F13): si costruisce un assieme, si conta, e **si guarda la
        /// pulizia** — che e' la meta' che nessuno controlla mai.
        ///
        /// I tre controlli, e perche' sono tre:
        ///   1. **l'assieme nasce con due componenti.** Se ne avesse uno o tre,
        ///      ogni caso futuro girerebbe su un assieme diverso da quello
        ///      dichiarato, con misure credibili e sbagliate.
        ///   2. **i file dei componenti esistono, mentre serve.** `AddComponent5`
        ///      vuole un percorso: se il file non c'e', il componente entra
        ///      «vuoto» o non entra, e nessuno lo direbbe piu' avanti.
        ///   3. **i file NON esistono piu' dopo.** E' il controllo che nessuno
        ///      scriverebbe, ed e' quello che protegge dal difetto peggiore:
        ///      *un caso che riesce perche' un file del giro prima era rimasto
        ///      non e' un caso, e' un ricordo.* Senza questa riga, la pulizia
        ///      rotta si scoprirebbe mesi dopo, guardando una cartella piena.
        /// </summary>
        private static void ProvaLAssiemeDiProva()
        {
            Console.WriteLine();
            Console.WriteLine("=== il banco sa preparare un assieme (GA0) ===");

            var caso = new Dictionary<string, object>
            {
                ["nome"] = "ga0_assieme_di_due",
                ["assieme"] = new Dictionary<string, object>
                {
                    ["componenti"] = new List<object>
                    {
                        UnaPiastra("base", 100, 60, 10, null),
                        UnaPiastra("coperchio", 100, 60, 4, new object[] { 0.0, 0.0, 20.0 }),
                    },
                },
            };

            Preparazione p = null;
            var file = new List<string>();
            try
            {
                p = PreparaIlDocumento("ga0_assieme_di_due", caso);
                if (p == null || p.Documento == null)
                {
                    // `PreparaIlDocumento` ha gia' chiamato `ErroreDiBanco` e
                    // detto a quale dei sette passi si e' fermata: ripetere qui
                    // aggiungerebbe una riga che dice la stessa cosa peggio.
                    return;
                }
                file.AddRange(p.FileDaCancellare);

                var ass = p.Documento as IAssemblyDoc;
                int quanti = ass == null ? -1 : QuantiComponenti(ass);
                Segna("l'assieme nasce con due componenti", quanti == 2,
                      quanti + " componenti");

                int ciSono = 0;
                foreach (var f in file) if (File.Exists(f)) ciSono++;
                Segna("i file dei componenti ci sono, mentre servono",
                      ciSono == file.Count, ciSono + " su " + file.Count);
            }
            finally { ButtaViaLaPreparazione(p); }

            int rimasti = 0;
            foreach (var f in file) if (File.Exists(f)) rimasti++;
            Segna("dopo, in «lavoro» non resta niente", rimasti == 0,
                  rimasti == 0
                      ? file.Count + " file cancellati"
                      : rimasti + " file rimasti: il giro dopo li troverebbe gia' li'");
        }

        /// <summary>Un componente: una piastra, con la sua posizione.</summary>
        private static Dictionary<string, object> UnaPiastra(
            string nome, double lunghezza, double larghezza, double spessore, object[] posizione)
        {
            var c = new Dictionary<string, object>
            {
                ["nome"] = nome,
                ["passi"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["operazione"] = "estrudi_rettangolo",
                        ["argomenti"] = new Dictionary<string, object>
                        {
                            ["piano"] = "superiore",
                            ["lunghezza_mm"] = lunghezza,
                            ["larghezza_mm"] = larghezza,
                            ["spessore_mm"] = spessore,
                            ["centrato"] = true,
                        },
                    },
                },
            };
            if (posizione != null) c["posizione_mm"] = posizione;
            return c;
        }

        /// <summary>
        /// Il documento su cui gira un caso, piu' i file da cancellare dopo.
        ///
        /// E' una CLASSE e non una tupla perche' chi la riceve deve poterla
        /// passare a `ButtaViaLaPreparazione` senza ricordarsi l'ordine di due
        /// valori: dimenticarsi di cancellare i componenti lascia file che il
        /// giro successivo trova gia' li', e un caso che riesce perche' un file
        /// del giro prima era rimasto non e' un caso, e' un ricordo.
        /// </summary>
        internal sealed class Preparazione
        {
            public IModelDoc2 Documento;
            public readonly List<string> FileDaCancellare = new List<string>();

            /// <summary>
            /// Documenti da chiudere indicati per NOME. Va bene per i
            /// componenti di un assieme: sono salvati su disco prima di
            /// essere aperti, e il loro nome non cambia piu'.
            /// </summary>
            public readonly List<string> TitoliDaChiudere = new List<string>();

            /// <summary>
            /// Documenti da chiudere indicati per RIFERIMENTO (voce K12-bis).
            ///
            /// ── PERCHE' NON BASTA IL NOME ───────────────────────────────
            ///
            /// Una tavola nasce come `Disegno38 - Foglio1` e viene salvata
            /// dopo, diventando `la_tavola_si_quota - Foglio1`. Il banco
            /// segnava il titolo **nel momento in cui il documento
            /// compariva**, cioe' quello vecchio, e a fine caso
            /// `CloseDoc("Disegno38 - Foglio1")` non chiudeva niente **e non
            /// lo diceva**. La tavola restava aperta, e una tavola aperta
            /// tiene aperto il suo pezzo: due documenti per caso, quattordici
            /// a fine giro.
            ///
            /// **L'identita' di un documento non e' il suo titolo.** E' la
            /// stessa lezione del documento attivo: si tiene il riferimento,
            /// e il titolo si chiede nel momento in cui serve.
            /// </summary>
            public readonly List<IModelDoc2> DocumentiDaChiudere = new List<IModelDoc2>();
        }

        /// <summary>
        /// Prepara il documento che un caso chiede: una parte nuova, oppure un
        /// assieme con dentro i suoi componenti.
        ///
        /// Torna `null` quando la preparazione non e' riuscita — e in quel caso
        /// ha gia' detto perche' con `ErroreDiBanco`.
        /// </summary>
        internal static Preparazione PreparaIlDocumento(
            string nome, Dictionary<string, object> caso)
        {
            var fuori = new Preparazione();
            var assieme = caso != null && caso.ContainsKey("assieme")
                ? caso["assieme"] as Dictionary<string, object>
                : null;

            if (assieme == null)
            {
                fuori.Documento = NuovaParte();
                return fuori;
            }

            var componenti = Elenco(assieme, "componenti");
            if (componenti == null || componenti.Count == 0)
            {
                ErroreDiBanco(nome, "«assieme» non porta nessun componente: un " +
                                    "assieme vuoto non prova niente");
                return null;
            }

            // ── 1-3. i componenti, uno per uno, costruiti e salvati ───────
            //
            // `da_inserire: false` COSTRUISCE MA NON INSERISCE, ed e' il modo in
            // cui un caso prova `inserisci_componente`: il file c'e', e' aperto,
            // e l'inserimento lo fa il PASSO, non la preparazione. Senza questa
            // riga un caso non potrebbe mai provare l'operazione che inserisce —
            // troverebbe il componente gia' dentro, e misurerebbe il lavoro
            // della preparazione credendo di misurare l'operazione.
            //
            // Il percorso il caso lo nomina con `{LAVORO}\<caso>_<componente>.sldprt`:
            // il segnaposto esiste gia' e vale su qualunque macchina. *Un caso
            // che nomina un percorso di questa macchina non e' una prova, e' un
            // ricordo.*
            var percorsi = new List<string>();
            var posizioni = new List<double[]>();
            // The file of each component by its name in the case, for the
            // «configurations» of step 7 (Z-C21.4), which name components.
            var pathByComponentName = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < componenti.Count; i++)
            {
                var c = componenti[i] as Dictionary<string, object>;
                if (c == null)
                {
                    ErroreDiBanco(nome, "il componente " + (i + 1) + " non e' un oggetto");
                    return null;
                }
                string suoNome = Testo(c, "nome") ?? ("comp" + (i + 1));
                // A component with a list of components of its own is a
                // SUB-ASSEMBLY (Z-C21.5, L11-3/L11-5): see BuildSubassembly.
                var children = Elenco(c, "componenti");
                string percorso = Path.Combine(
                    Attese.Lavoro, NomeDiFile(nome) + "_" + NomeDiFile(suoNome) +
                                   (children != null ? ".sldasm" : ".sldprt"));
                pathByComponentName[suoNome] = percorso;

                if (children != null)
                {
                    if (!BuildSubassembly(nome + " · sub-assembly «" + suoNome + "»",
                                          NomeDiFile(nome) + "_" + NomeDiFile(suoNome), children, percorso, fuori))
                        return null;
                }
                else if (!CostruisciESalva(nome + " · componente «" + suoNome + "»", c, percorso))
                    return null;

                Register(fuori, percorso);

                if (!DaInserire(c)) continue;
                percorsi.Add(percorso);
                posizioni.Add(PosizioneMm(c));
            }

            // ── 4. il documento di assieme ───────────────────────────────
            IModelDoc2 doc;
            try { doc = NuovoAssieme(); }
            catch (Exception ex)
            {
                ErroreDiBanco(nome, "l'assieme nuovo su cui provare: " + Perche(ex));
                ButtaViaIFile(fuori);
                return null;
            }
            fuori.Documento = doc;

            var ass = doc as IAssemblyDoc;
            if (ass == null)
            {
                ErroreDiBanco(nome, "il documento nuovo non e' un IAssemblyDoc: " +
                                    "il modello di assieme predefinito non e' un assieme");
                return null;
            }

            // ── 5a. TUTTI i componenti si riaprono, anche quelli che non si
            //        inseriscono: `inserisci_componente` vuole il documento
            //        aperto, e chi lo chiama dal caso lo trova cosi'.
            foreach (var f in new List<string>(fuori.FileDaCancellare))
                if (!Apri(f, nome)) return null;

            // ── 5b. riaprire i componenti e inserirli ────────────────────
            //
            // SI RIAPRONO, e non e' una formalita': `AddComponent5` vuole un
            // documento GIA' APERTO, altrimenti risponde null senza dire
            // perche'. E' la stessa precondizione delle viste di tavola, dove
            // era gia' costata un giro.
            for (int i = 0; i < percorsi.Count; i++)
            {
                var p = posizioni[i];
                IComponent2 inserito = null;
                try
                {
                    inserito = ass.AddComponent5(
                        percorsi[i], 0, "", false, "",
                        p[0] / 1000.0, p[1] / 1000.0, p[2] / 1000.0) as IComponent2;
                }
                catch (Exception ex)
                {
                    ErroreDiBanco(nome, "AddComponent5 su «" +
                                        Path.GetFileName(percorsi[i]) + "»: " + Perche(ex));
                    return null;
                }
                if (inserito == null)
                {
                    ErroreDiBanco(nome, "AddComponent5 ha risposto null su «" +
                                        Path.GetFileName(percorsi[i]) + "»: il componente " +
                                        "non e' entrato, e nessuno lo direbbe piu' avanti");
                    return null;
                }
            }

            // ── 6. ricostruire, e CONTARE ────────────────────────────────
            //
            // Il conteggio non e' una cortesia: `AddComponent5` ha risposto un
            // IComponent2 per ognuno, e *il valore che torna da chi ha fatto la
            // cosa non e' la misura della cosa*. Se i componenti nell'assieme
            // non sono quanti se ne sono inseriti, il caso girerebbe su un
            // assieme diverso da quello dichiarato — e tutte le sue misure
            // sarebbero credibili e sbagliate.
            try { doc.EditRebuild3(); } catch { }
            int quanti = QuantiComponenti(ass);
            if (quanti != percorsi.Count)
            {
                ErroreDiBanco(nome, "ho inserito " + percorsi.Count + " componenti e " +
                                    "l'assieme ne ha " + quanti);
                return null;
            }

            // ── 7. the configurations of the assembly, when the case asks ──
            var configurations = Elenco(assieme, "configurations");
            if (configurations != null &&
                !PrepareConfigurations(nome, doc, ass, configurations, pathByComponentName))
                return null;

            return fuori;
        }

        /// <summary>
        /// THE CONFIGURATIONS A CASE ASKS OF ITS ASSEMBLY (Z-C21.4, findings
        /// L16-2 and L16-3): «configurations»: [{ "name": ..., "suppressed":
        /// [component names], "excluded_from_bom": [component names] }], made in
        /// this order after the components are in. No operation of the catalogue
        /// suppresses a component or excludes it from the BOM in one
        /// configuration, and without this a BOM read in the wrong configuration
        /// could not be written as a case.
        ///
        /// IConfigurationManager.AddConfiguration2 ACTIVATES the new
        /// configuration (measured on parts by crea_configurazione, read back
        /// here on the assembly), so each one is set while it is the active one:
        /// IComponent2.SetSuppression2 acts on the active configuration, and
        /// SetExcludeFromBOM2 is given swThisConfiguration. Every setting is READ
        /// BACK (IsSuppressed, GetExcludeFromBOM2) before the case runs. The last
        /// configuration made stays active; a step that needs another one active
        /// asks for it with «activate_before».
        /// </summary>
        private static bool PrepareConfigurations(
            string nome, IModelDoc2 doc, IAssemblyDoc ass, List<object> configurations,
            Dictionary<string, string> pathByComponentName)
        {
            var manager = doc.ConfigurationManager;
            if (manager == null)
            {
                ErroreDiBanco(nome, "«configurations»: the assembly gives no ConfigurationManager");
                return false;
            }
            for (int i = 0; i < configurations.Count; i++)
            {
                var one = configurations[i] as Dictionary<string, object>;
                string name = one == null ? null : Testo(one, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    ErroreDiBanco(nome, "«configurations» " + (i + 1) + " has no «name»");
                    return false;
                }
                IConfiguration made = null;
                try { made = manager.AddConfiguration2(name, "", "", 0, "", "", true) as IConfiguration; }
                catch (Exception ex)
                {
                    ErroreDiBanco(nome, "AddConfiguration2(«" + name + "»): " + Perche(ex));
                    return false;
                }
                string active = null;
                try
                {
                    IConfiguration activeNow = manager.ActiveConfiguration;
                    active = activeNow == null ? null : activeNow.Name;
                }
                catch { }
                if (made == null || !string.Equals(active, name, StringComparison.Ordinal))
                {
                    ErroreDiBanco(nome, "AddConfiguration2(«" + name + "») answered " +
                                        (made == null ? "null" : "«" + made.Name + "»") + " and the active " +
                                        "configuration is «" + (active ?? "?") + "»: the settings of this " +
                                        "configuration would land on another one");
                    return false;
                }

                foreach (var componentName in TextsOf(one, "suppressed"))
                {
                    var component = ComponentOfCase(nome, ass, componentName, pathByComponentName);
                    if (component == null) return false;
                    int answer = -1;
                    try { answer = component.SetSuppression2((int)swComponentSuppressionState_e.swComponentSuppressed); }
                    catch (Exception ex)
                    {
                        ErroreDiBanco(nome, "SetSuppression2 on «" + componentName + "»: " + Perche(ex));
                        return false;
                    }
                    bool suppressed = false;
                    try { suppressed = component.IsSuppressed(); } catch { }
                    if (!suppressed)
                    {
                        ErroreDiBanco(nome, "«" + componentName + "» is not suppressed in «" + name +
                                            "» after SetSuppression2 (answer " + answer + ")");
                        return false;
                    }
                }

                foreach (var componentName in TextsOf(one, "excluded_from_bom"))
                {
                    var component = ComponentOfCase(nome, ass, componentName, pathByComponentName);
                    if (component == null) return false;
                    int answer = -1;
                    object readBack = null;
                    try
                    {
                        answer = component.SetExcludeFromBOM2(
                            true, (int)swInConfigurationOpts_e.swThisConfiguration, null);
                        readBack = component.GetExcludeFromBOM2(
                            (int)swInConfigurationOpts_e.swThisConfiguration, null);
                    }
                    catch (Exception ex)
                    {
                        ErroreDiBanco(nome, "SetExcludeFromBOM2 on «" + componentName + "»: " + Perche(ex));
                        return false;
                    }
                    if (!OnlyTrue(readBack))
                    {
                        ErroreDiBanco(nome, "«" + componentName + "» is not excluded from the BOM of «" + name +
                                            "» after SetExcludeFromBOM2 (answer " + answer + ")");
                        return false;
                    }
                }
                Console.WriteLine("       | assembly configuration «" + name + "» made" +
                                  (TextsOf(one, "suppressed").Count + TextsOf(one, "excluded_from_bom").Count > 0
                                      ? ": suppressed [" + string.Join(", ", TextsOf(one, "suppressed")) +
                                        "], out of the BOM [" + string.Join(", ", TextsOf(one, "excluded_from_bom")) + "]"
                                      : ""));
            }
            try { doc.EditRebuild3(); } catch { }
            return true;
        }

        /// <summary>The texts of a list field of a case object; empty when the field is absent.</summary>
        private static List<string> TextsOf(Dictionary<string, object> one, string field)
        {
            var list = new List<string>();
            var raw = one == null ? null : Elenco(one, field);
            if (raw == null) return list;
            foreach (var x in raw) if (x != null) list.Add(Convert.ToString(x, CultureInfo.InvariantCulture));
            return list;
        }

        /// <summary>Whether a GetExcludeFromBOM2 answer is one Boolean, and true.</summary>
        private static bool OnlyTrue(object answer)
        {
            var flags = answer as bool[];
            if (flags != null) return flags.Length == 1 && flags[0];
            var boxed = answer as object[];
            return boxed != null && boxed.Length == 1 && boxed[0] is bool b && b;
        }

        /// <summary>
        /// The top-level component of the assembly made from the case component
        /// called <paramref name="componentName"/>, found by its FILE, never by
        /// its position; null (after ErroreDiBanco) when there is none.
        /// </summary>
        private static IComponent2 ComponentOfCase(
            string nome, IAssemblyDoc ass, string componentName, Dictionary<string, string> pathByComponentName)
        {
            string path;
            if (!pathByComponentName.TryGetValue(componentName, out path))
            {
                ErroreDiBanco(nome, "«configurations» names «" + componentName + "», which is not a component of the case");
                return null;
            }
            object[] all = null;
            try { all = ass.GetComponents(true) as object[]; } catch { }
            foreach (var o in all ?? new object[0])
            {
                var c = o as IComponent2;
                string own = null;
                try { own = c?.GetPathName(); } catch { }
                if (!string.IsNullOrWhiteSpace(own) &&
                    string.Equals(Path.GetFullPath(own), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            ErroreDiBanco(nome, "«configurations» names «" + componentName + "», and no component of the assembly " +
                                "uses its file (it has «da_inserire» false?)");
            return null;
        }

        /// <summary>
        /// Chiude e cancella quello che la preparazione ha lasciato in giro.
        ///
        /// SI CANCELLANO ANCHE I FILE, non solo i documenti. `ChiudiScarti`
        /// chiude finestre; questi sono `.sldprt` su disco, e restare li'
        /// vorrebbe dire che il giro dopo trova un file gia' fatto. **Un caso
        /// che riesce perche' un file del giro prima era rimasto non e' un
        /// caso, e' un ricordo** — e si accorgerebbe del difetto solo chi
        /// svuota la cartella a mano.
        /// </summary>
        internal static void ButtaViaLaPreparazione(Preparazione p)
        {
            if (p == null) return;

            // ── DUE GIRI, E L'ORDINE NON SI PRETENDE DI SAPERLO (voce GT0) ──
            //
            // Prima di GT0 la lista conteneva solo i componenti di un assieme,
            // e l'assieme era `Documento`: si chiudeva l'assieme e poi i pezzi,
            // cioe' dall'alto. Adesso la lista puo' contenere anche una TAVOLA,
            // e li' il sopra e il sotto sono rovesciati — la tavola sta sopra il
            // pezzo, che e' `Documento`.
            //
            // **Non si sceglie un ordine buono per tutti e due**, perche' non
            // c'e': si prova, e si riprova. `CloseDoc` su un documento che ha
            // gia' chiuso non fa niente, quindi il secondo giro costa un
            // tentativo a vuoto e raccoglie quello che il primo ha lasciato.
            // *Quando non si sa in che ordine vanno smontate due cose, si
            // smontano due volte — e' meno codice che dedurre l'ordine, e non
            // sbaglia quando la deduzione era sbagliata.*
            for (int giro = 0; giro < 2; giro++)
            {
                Chiudi(p.Documento);
                // Per riferimento PRIMA: e' la tavola, e finche' e' aperta
                // tiene aperto il pezzo. Vedi `DocumentiDaChiudere`.
                foreach (var d in p.DocumentiDaChiudere) Chiudi(d);
                foreach (var t in p.TitoliDaChiudere)
                {
                    try { _sw.CloseDoc(t); } catch { }
                }
            }

            // E SI GUARDA SE SONO ANDATI VIA DAVVERO (voce K12-bis).
            //
            // `CloseDoc` non torna niente: il difetto di K12-bis e' vissuto
            // sette giorni proprio perche' chiudere un documento che non
            // esiste con quel nome **non e' un errore**. Qui non si puo'
            // fare un rosso — lo smontaggio gira anche dentro un caso
            // fallito, e un secondo rosso coprirebbe il primo — ma si puo'
            // DIRE, e una riga che compare solo quando qualcosa non e'
            // andato e' il minimo per non riscoprirlo fra una settimana.
            int restano = 0;
            try
            {
                var d = (IModelDoc2)_sw.GetFirstDocument();
                while (d != null) { restano++; d = (IModelDoc2)d.GetNext(); }
            }
            catch { restano = -1; }
            if (restano > 0)
                Console.WriteLine("       | smontaggio: restano " + restano +
                                  " documenti aperti");

            ButtaViaIFile(p);
        }

        private static void ButtaViaIFile(Preparazione p)
        {
            foreach (var f in p.FileDaCancellare)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
            p.FileDaCancellare.Clear();
        }

        // =================================================================

        /// <summary>
        /// Costruisce una parte eseguendo i passi del componente e la salva.
        ///
        /// I passi sono le STESSE operazioni dei casi, eseguite con lo stesso
        /// `Esegui`: un componente non ha un linguaggio suo, altrimenti sarebbe
        /// una seconda implementazione da tenere allineata a mano.
        /// </summary>
        private static bool CostruisciESalva(
            string etichetta, Dictionary<string, object> componente, string percorso)
        {
            var passi = Elenco(componente, "passi");
            if (passi == null || passi.Count == 0)
            {
                ErroreDiBanco(etichetta, "manca «passi»: un componente senza " +
                                         "lavorazioni sarebbe un documento vuoto");
                return false;
            }

            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                var stato = LeggiStato(doc);
                for (int i = 0; i < passi.Count; i++)
                {
                    var passo = passi[i] as Dictionary<string, object>;
                    if (passo == null)
                    {
                        ErroreDiBanco(etichetta, "il passo " + (i + 1) + " non e' un oggetto");
                        return false;
                    }
                    string operazione = Testo(passo, "operazione");
                    if (operazione == null)
                    {
                        ErroreDiBanco(etichetta, "al passo " + (i + 1) + " manca «operazione»");
                        return false;
                    }
                    object grezzi;
                    passo.TryGetValue("argomenti", out grezzi);
                    var argomenti = Risolvi(grezzi, stato) as Dictionary<string, object>
                                    ?? new Dictionary<string, object>();

                    string errore;
                    Esegui(doc, operazione, argomenti, out errore, stato);
                    if (!string.IsNullOrEmpty(errore))
                    {
                        // UN COMPONENTE CHE NON SI COSTRUISCE NON E' UNA PROVA
                        // ROSSA. Il caso voleva misurare un assieme, e l'assieme
                        // non e' mai esistito: dirlo «fallito» farebbe cercare
                        // il difetto nell'operazione di assieme, che non e'
                        // nemmeno stata chiamata.
                        ErroreDiBanco(etichetta, "il passo " + (i + 1) + " («" + operazione +
                                                 "») e' stato rifiutato: " + Attese.Breve(errore));
                        return false;
                    }
                    stato = LeggiStato(doc);
                }

                int e, w;
                SaveTo(doc, percorso, out e, out w);
                // SI GUARDA IL FILE, NON IL CODICE DI RITORNO. `SaveAs` scrive
                // due interi che raccontano cosa pensa SOLIDWORKS di aver
                // fatto; l'unica prova che il file ci sia e' che ci sia.
                if (!File.Exists(percorso))
                {
                    ErroreDiBanco(etichetta, "salvato senza errori (e=" + e + ", w=" + w +
                                             ") ma «" + percorso + "» non c'e'");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                ErroreDiBanco(etichetta, Perche(ex));
                return false;
            }
            finally { Chiudi(doc); }
        }

        /// <summary>
        /// The one SaveAs of this file (IModelDocExtension.SaveAs, frozen for
        /// Z-C21.7 as L13-4/L13-8): a part component and a sub-assembly are
        /// saved the same way, and the caller looks at the FILE, not at the
        /// two numbers.
        /// </summary>
        private static void SaveTo(IModelDoc2 doc, string path, out int errors, out int warnings)
        {
            int e = 0, w = 0;
            doc.Extension.SaveAs(path, 0, 1, null, ref e, ref w);
            errors = e;
            warnings = w;
        }

        /// <summary>
        /// A file the preparation made: deleted at the end, and its document
        /// closed by title. The components and the parts of a sub-assembly
        /// (Z-C21.5) are registered here, in one place: the two lists are the
        /// whole cleanup, and a file missing from them is the file the next
        /// round finds already there.
        /// </summary>
        private static void Register(Preparazione p, string path)
        {
            p.FileDaCancellare.Add(path);
            p.TitoliDaChiudere.Add(Path.GetFileName(path));
        }

        /// <summary>
        /// A SUB-ASSEMBLY COMPONENT (Z-C21.5, findings L11-3 and L11-5): a
        /// component of the case with a list of components of its own instead
        /// of steps. Each child is a part built and saved like any component, as
        /// «&lt;stem&gt;_&lt;child&gt;.sldprt», inserted into a new assembly at its
        /// «posizione_mm», and that assembly is saved as
        /// <paramref name="path"/> («&lt;stem&gt;.sldasm»), which the caller then
        /// registers and inserts like a part. Without it no case could hold a
        /// component inside a sub-assembly, and fissa_componente and
        /// sostituisci_componente, which work on the top level only, could not
        /// be seen refusing one.
        ///
        /// The children are registered here, BEFORE the caller registers the
        /// sub-assembly: the files are reopened in that order, parts first. The
        /// sub-assembly is closed by its title at the end like a part: it is
        /// registered after it is saved, so the title no longer changes. If
        /// this fails before the save, the new assembly is closed here, since
        /// nothing else knows it. One level only: a sub-assembly inside a
        /// sub-assembly is refused, no case needs it yet.
        /// </summary>
        private static bool BuildSubassembly(
            string label, string stem, List<object> children, string path, Preparazione fuori)
        {
            if (children.Count == 0)
            {
                ErroreDiBanco(label, "the list of components is empty: a sub-assembly with nothing inside proves nothing");
                return false;
            }
            var childPaths = new List<string>();
            var childPositions = new List<double[]>();
            for (int k = 0; k < children.Count; k++)
            {
                var child = children[k] as Dictionary<string, object>;
                if (child == null)
                {
                    ErroreDiBanco(label, "component " + (k + 1) + " is not an object");
                    return false;
                }
                if (Elenco(child, "componenti") != null)
                {
                    ErroreDiBanco(label, "a sub-assembly inside a sub-assembly is not supported by the bench");
                    return false;
                }
                string childName = Testo(child, "nome") ?? ("comp" + (k + 1));
                string childPath = Path.Combine(Attese.Lavoro, stem + "_" + NomeDiFile(childName) + ".sldprt");
                if (!CostruisciESalva(label + " · component «" + childName + "»", child, childPath))
                    return false;
                Register(fuori, childPath);
                childPaths.Add(childPath);
                childPositions.Add(PosizioneMm(child));
            }

            IModelDoc2 sub;
            try { sub = NuovoAssieme(); }
            catch (Exception ex)
            {
                ErroreDiBanco(label, "the new sub-assembly: " + Perche(ex));
                return false;
            }
            string failure = FillAndSave(sub, childPaths, childPositions, path, label);
            if (failure == null) return true;
            ErroreDiBanco(label, failure);
            Chiudi(sub);
            return false;
        }

        /// <summary>
        /// The body of <see cref="BuildSubassembly"/> once the new assembly
        /// exists: the parts opened and inserted, counted, the assembly saved
        /// and its file looked at. Null when it all went, otherwise what did
        /// not (an error of the bench, not a red test).
        /// </summary>
        private static string FillAndSave(
            IModelDoc2 sub, List<string> childPaths, List<double[]> childPositions, string path, string label)
        {
            var subAssembly = sub as IAssemblyDoc;
            if (subAssembly == null) return "the new document is not an IAssemblyDoc";
            // AddComponent5 wants the document open, as in the main assembly.
            foreach (var p in childPaths)
                if (!Apri(p, label)) return "a part of the sub-assembly did not open again (the line above says which)";
            for (int k = 0; k < childPaths.Count; k++)
            {
                var at = childPositions[k];
                IComponent2 inserted = null;
                try
                {
                    inserted = subAssembly.AddComponent5(
                        childPaths[k], 0, "", false, "",
                        at[0] / 1000.0, at[1] / 1000.0, at[2] / 1000.0) as IComponent2;
                }
                catch (Exception ex)
                {
                    return "AddComponent5 on «" + Path.GetFileName(childPaths[k]) + "»: " + Perche(ex);
                }
                if (inserted == null)
                    return "AddComponent5 answered null on «" + Path.GetFileName(childPaths[k]) +
                           "»: the component did not go in";
            }
            try { sub.EditRebuild3(); } catch { }
            int count = QuantiComponenti(subAssembly);
            if (count != childPaths.Count)
                return "inserted " + childPaths.Count + " components and the sub-assembly has " + count;

            int e, w;
            SaveTo(sub, path, out e, out w);
            if (!File.Exists(path))
                return "saved without errors (e=" + e + ", w=" + w + ") but «" + path + "» is not there";
            return null;
        }

        private static IModelDoc2 NuovoAssieme()
        {
            // Voce K19: la copia in locale vale anche per l'assieme.
            string tpl = Program.ModelloInLocale(_sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly));
            // La sentinella (voce K17) vale anche qui: un assieme di prova
            // comincia da componenti che sono parti nuove, e se non nascono
            // piu' non e' il caso a essere sbagliato.
            if (Program.Sentinella.SeNEAndato)
                throw new Condiviso.PreparazioneFallita(Program.Sentinella.Referto());

            var nata = _sw.NewDocument(tpl, 0, 0, 0) as IModelDoc2;
            if (nata != null) Program.Sentinella.Riuscita();
            else if (Program.Sentinella.Fallita())
                throw new Condiviso.PreparazioneFallita(Program.Sentinella.Referto());

            return OppureDillo(nata,
                               "l'assieme nuovo su cui provare");
        }

        private static bool Apri(string percorso, string nome)
        {
            try
            {
                int e = 0, w = 0;
                // A sub-assembly (Z-C21.5) opens as an assembly, a component as a part.
                int type = percorso.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase)
                    ? (int)swDocumentTypes_e.swDocASSEMBLY
                    : (int)swDocumentTypes_e.swDocPART;
                var d = _sw.OpenDoc6(
                    percorso, type,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref e, ref w);
                if (d == null)
                {
                    ErroreDiBanco(nome, "non riesco a riaprire «" +
                                        Path.GetFileName(percorso) + "» (e=" + e + ", w=" + w +
                                        "): AddComponent5 vuole un documento gia' aperto");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                ErroreDiBanco(nome, "riaprendo «" + Path.GetFileName(percorso) + "»: " + Perche(ex));
                return false;
            }
        }

        /// <summary>Quanti componenti ci sono davvero nell'assieme.</summary>
        private static int QuantiComponenti(IAssemblyDoc ass)
        {
            try
            {
                // `true` = solo il primo livello. Un sottoassieme conta uno, che
                // e' quello che il caso ha inserito.
                var v = ass.GetComponents(true) as object[];
                return v == null ? 0 : v.Length;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Il componente va inserito dalla preparazione? Assente = si'.
        ///
        /// Il valore predefinito e' `true` e non `false` di proposito: il caso
        /// normale e' «voglio questo assieme, fammelo», e chiedere `da_inserire`
        /// a ogni componente sarebbe rumore. `false` si scrive solo quando si
        /// vuole provare l'inserimento, che e' il caso raro.
        /// </summary>
        private static bool DaInserire(Dictionary<string, object> componente)
        {
            object v;
            if (componente == null ||
                !componente.TryGetValue("da_inserire", out v) || v == null) return true;
            return Convert.ToString(v, CultureInfo.InvariantCulture)
                          .Equals("True", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Dove va il componente, in mm. Assente = nell'origine.
        ///
        /// **E' DOVE VA IL CENTRO DELL'INGOMBRO, non dove va l'origine.**
        /// `AddComponent5` posiziona il centro: un pezzo con l'origine su una
        /// faccia — cioe' qualunque cosa estrusa a una direzione sola — si
        /// ritrova l'origine spostata di meta' ingombro. Misurato il 17/9/2026,
        /// e previsto prima di misurarlo: spesso 4 chiesto a z=30 → origine a
        /// (0, -2, 30); spesso 10 chiesto a 0 → (0, -5, 0).
        ///
        /// Il campo si chiama `posizione_mm` e non `posizione`: e' la stessa
        /// convenzione del catalogo, dove l'unita' sta nel nome perche' un
        /// numero senza unita' e' un numero che qualcuno interpretera' in
        /// metri.
        /// </summary>
        private static double[] PosizioneMm(Dictionary<string, object> componente)
        {
            var v = new double[3];
            object grezzo;
            if (componente == null || !componente.TryGetValue("posizione_mm", out grezzo) ||
                grezzo == null)
                return v;
            var lista = grezzo as System.Collections.IEnumerable;
            if (lista == null) return v;
            int i = 0;
            foreach (var x in lista)
            {
                if (i >= 3) break;
                try { v[i++] = Convert.ToDouble(x, CultureInfo.InvariantCulture); }
                catch { i++; }
            }
            return v;
        }

        /// <summary>
        /// Un nome di caso reso adatto a stare in un nome di file.
        ///
        /// Non si usa il nome cosi' com'e': i casi si chiamano con parole
        /// separate da underscore e va bene, ma basta un carattere strano in un
        /// caso futuro perche' `SaveAs` fallisca con un messaggio che parla di
        /// permessi invece che del nome.
        /// </summary>
        private static string NomeDiFile(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "x";
            var b = new System.Text.StringBuilder();
            foreach (var c in s)
                b.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return b.ToString();
        }
    }
}
