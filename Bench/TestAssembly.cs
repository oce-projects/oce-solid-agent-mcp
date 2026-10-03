using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
// Stessa ragione di `DeclaredCases.cs`: `Testo`, `Elenco`, `Risolvi` e `Breve`
// vivono in `Attese`, che non tocca SOLIDWORKS (voce F7). `using static` li
// lascia scrivere come se fossero qui.
using static Oce.Solid.Agent.Bench.Attese;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// IL BANCO IMPARA A PREPARARE UN ASSIEME (voce GA0).
    ///
    /// ── PERCHE' QUESTA VOCE VIENE PRIMA DI TUTTA LA SEZIONE G-A ──────────
    ///
    /// Fino al 16 settembre 2026 `NuovaParte()` era l'UNICO modo in cui il
    /// banco preparava un documento, e usava `swDefaultTemplatePart`. Nessun
    /// `NuovoAssieme`, e `"start": { "new_part": true }` era l'unica forma di
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
    ///   2. SALVARLA in `Bench\lavoro`,
    ///   3. chiuderla,
    ///   4. creare il documento di assieme,
    ///   5. riaprire i componenti e inserirli,
    ///   6. ricostruire.
    ///
    /// E poi ripulire, che e' il settimo: `CloseLeftovers` chiude i documenti, non
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
                ["name"] = "ga0_assieme_di_due",
                ["assembly"] = new Dictionary<string, object>
                {
                    ["components"] = new List<object>
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
                ["name"] = nome,
                ["steps"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["operation"] = "extrude_rectangle",
                        ["arguments"] = new Dictionary<string, object>
                        {
                            ["plane"] = "top",
                            ["length_mm"] = lunghezza,
                            ["width_mm"] = larghezza,
                            ["thickness_mm"] = spessore,
                            ["centered"] = true,
                        },
                    },
                },
            };
            if (posizione != null) c["position_mm"] = posizione;
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
            /// dopo, diventando `drawing_dimensions_itself - Foglio1`. Il banco
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
            var assieme = caso != null && caso.ContainsKey("assembly")
                ? caso["assembly"] as Dictionary<string, object>
                : null;

            if (assieme == null)
            {
                fuori.Documento = NuovaParte();
                return fuori;
            }

            var componenti = Elenco(assieme, "components");
            if (componenti == null || componenti.Count == 0)
            {
                ErroreDiBanco(nome, "«assembly» non porta nessun componente: un " +
                                    "assieme vuoto non prova niente");
                return null;
            }

            // ── 0. the keys of Z-C21.11, read before anything is built ────
            //
            // «envelope» and «hidden» on a component, «reopen_lightweight» on
            // the assembly: the three states in which a component answers the
            // component readers differently (lightweight, envelope, hidden),
            // measured by the probe zc2111/probe_alleggeriti (30/9/2026) and the
            // Z-C21.3 probe of envelopes and BOM children (28/9/2026). No operation of the catalogue
            // makes a component an envelope, hides it or loads it lightweight,
            // so without them none of the three could be written as a case.
            // They are read here, before the first file is written: a key
            // written wrong is a bench error that leaves nothing behind. A key
            // that is not true or false is refused, never read as false: a case
            // that asked for an envelope and ran on a normal part would measure
            // another assembly and look right.
            bool reopenLightweight;
            if (!FlagOf(nome, assieme, "reopen_lightweight", out reopenLightweight)) return null;
            var envelopeNames = new List<string>();
            var hiddenNames = new List<string>();
            for (int i = 0; i < componenti.Count; i++)
            {
                var one = componenti[i] as Dictionary<string, object>;
                if (one == null) continue;   // said by the loop below, with its number
                string itsName = Testo(one, "name") ?? ("comp" + (i + 1));
                bool envelope, hidden;
                if (!FlagOf(nome, one, "envelope", out envelope) || !FlagOf(nome, one, "hidden", out hidden))
                    return null;
                bool isSubassembly = Elenco(one, "components") != null;
                if (envelope && isSubassembly)
                {
                    ErroreDiBanco(nome, "«envelope» on the sub-assembly «" + itsName + "»: an envelope was " +
                                        "measured on a part only (probe of 28/9/2026)");
                    return null;
                }
                if ((envelope || hidden) && !DaInserire(one))
                {
                    ErroreDiBanco(nome, "«" + itsName + "» asks «envelope» or «hidden» with «to_insert» false: " +
                                        "the preparation does not insert it, so it cannot make it either");
                    return null;
                }
                if (reopenLightweight && !DaInserire(one))
                {
                    ErroreDiBanco(nome, "«reopen_lightweight» closes every document of the components, and «" +
                                        itsName + "» («to_insert» false) is there to be inserted by a step, " +
                                        "which needs its document open");
                    return null;
                }
                if (envelope) envelopeNames.Add(itsName);
                if (hidden) hiddenNames.Add(itsName);
            }

            // ── 1-3. i componenti, uno per uno, costruiti e salvati ───────
            //
            // `da_inserire: false` COSTRUISCE MA NON INSERISCE, ed e' il modo in
            // cui un caso prova `insert_component`: il file c'e', e' aperto,
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
                string suoNome = Testo(c, "name") ?? ("comp" + (i + 1));
                // A component with a list of components of its own is a
                // SUB-ASSEMBLY (Z-C21.5, L11-3/L11-5): see BuildSubassembly.
                var children = Elenco(c, "components");
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
            //        inseriscono: `insert_component` vuole il documento
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

            // ── 6b. envelopes and hidden components, when the case asks ──
            //
            // After the count and before «configurations»: each is set in the
            // configuration active now, and read back (Z-C21.11). A failure here
            // closes and deletes what the preparation made (see Abandon).
            if (!MakeEnvelopes(nome, doc, ass, envelopeNames, pathByComponentName) ||
                !HideComponents(nome, ass, hiddenNames, pathByComponentName))
            {
                ButtaViaLaPreparazione(fuori);
                return null;
            }

            // ── 7. the configurations of the assembly, when the case asks ──
            var configurations = Elenco(assieme, "configurations");
            if (configurations != null &&
                !PrepareConfigurations(nome, doc, ass, configurations, pathByComponentName))
                return null;

            // ── 8. saved, closed and reopened lightweight, when the case asks ──
            //
            // Last, so that the file holds everything above (envelopes, hidden
            // components, configurations: the last one made stays the active
            // one, and the assembly reopens on it). From here the case runs on
            // the REOPENED document: the one built above is closed.
            if (reopenLightweight)
            {
                IModelDoc2 reopened = ReopenLightweight(nome, doc, percorsi.Count, fuori);
                if (reopened == null) return null;
                fuori.Documento = reopened;
            }

            return fuori;
        }

        /// <summary>
        /// A TRUE/FALSE KEY OF A CASE OBJECT (Z-C21.11): absent is false, a JSON
        /// Boolean is itself, anything else is a bench error (never read as
        /// false: the case would run on another assembly and look right).
        /// </summary>
        private static bool FlagOf(string nome, Dictionary<string, object> map, string key, out bool value)
        {
            value = false;
            object raw;
            if (map == null || !map.TryGetValue(key, out raw) || raw == null) return true;
            if (raw is bool flag) { value = flag; return true; }
            ErroreDiBanco(nome, "«" + key + "» is «" + Convert.ToString(raw, CultureInfo.InvariantCulture) +
                                "», not true or false");
            return false;
        }

        /// <summary>
        /// THE COMPONENTS A CASE ASKS AS ENVELOPES (Z-C21.11, finding N27):
        /// «envelope»: true on a component (a part; see the checks at step 0).
        ///
        /// As the Z-C21.3 probe of envelopes and BOM children (28/9/2026, T_env,
        /// the component «F»): the component is selected with IComponent2.Select4,
        /// then IAssemblyDoc.CompConfigProperties6(swComponentFullyResolved,
        /// swComponentRigidSolving, Visibility true, UseNamedConfiguration true,
        /// the configuration it references, ExcludeFromBOM false, IsEnvelope
        /// true, swAlwaysInclude) acts on the selection; the probe read
        /// IsEnvelope true after it, and CreateMassProperty of the assembly left
        /// it out (16000 mm3 of 26000). Here the component is found again after a
        /// rebuild, by its file, and IsEnvelope is READ BACK: an envelope that
        /// did not take is a bench error, never a case run on a normal part.
        /// </summary>
        private static bool MakeEnvelopes(string nome, IModelDoc2 doc, IAssemblyDoc ass, List<string> names,
                                          Dictionary<string, string> pathByComponentName)
        {
            foreach (var componentName in names)
            {
                IComponent2 component = ComponentOfCase(nome, ass, componentName, pathByComponentName, "«envelope»");
                if (component == null) return false;
                string configuration = null;
                try { configuration = component.ReferencedConfiguration; } catch { }
                if (string.IsNullOrEmpty(configuration))
                {
                    ErroreDiBanco(nome, "«envelope»: I could not read which configuration «" + componentName +
                                        "» references (ReferencedConfiguration), and CompConfigProperties6 is given it");
                    return false;
                }
                bool selected = false;
                try
                {
                    doc.ClearSelection2(true);
                    selected = component.Select4(false, null, false);
                }
                catch (Exception ex)
                {
                    ErroreDiBanco(nome, "«envelope»: selecting «" + componentName + "»: " + Perche(ex));
                    return false;
                }
                if (!selected)
                {
                    ErroreDiBanco(nome, "«envelope»: Select4 did not select «" + componentName + "», and " +
                                        "CompConfigProperties6 acts on the selected component");
                    return false;
                }
                bool answer = false;
                try
                {
                    answer = ass.CompConfigProperties6(
                        (int)swComponentSuppressionState_e.swComponentFullyResolved,
                        (int)swComponentSolvingOption_e.swComponentRigidSolving,
                        true, true, configuration, false, true,
                        (int)swASMSLDPRTCompPref_e.swAlwaysInclude);
                }
                catch (Exception ex)
                {
                    try { doc.ClearSelection2(true); } catch { }
                    ErroreDiBanco(nome, "CompConfigProperties6 on «" + componentName + "»: " + Perche(ex));
                    return false;
                }
                try { doc.ClearSelection2(true); } catch { }
                try { doc.EditRebuild3(); } catch { }
                IComponent2 again = ComponentOfCase(nome, ass, componentName, pathByComponentName, "«envelope»");
                if (again == null) return false;
                bool isEnvelope = false;
                try { isEnvelope = again.IsEnvelope(); } catch { }
                if (!isEnvelope)
                {
                    ErroreDiBanco(nome, "«" + componentName + "» is not an envelope after CompConfigProperties6 " +
                                        "(it answered " + answer + ", and IsEnvelope reads false or cannot be read)");
                    return false;
                }
                Console.WriteLine("       | component «" + componentName + "» made an envelope (IsEnvelope read back)");
            }
            return true;
        }

        /// <summary>
        /// THE COMPONENTS A CASE ASKS HIDDEN (Z-C21.11): «hidden»: true on a
        /// component. As the probe zc2111/probe_alleggeriti (30/9/2026, T_hid):
        /// IComponent2.Visible = swComponentHidden (0); the probe read Visible 0
        /// and IsHidden(false) true, CreateMassProperty of the assembly weighed
        /// it and the STEP did not write it. Here IsHidden(false) is READ BACK
        /// on the component found again by its file (false, so that a
        /// lightweight component is not taken for hidden: IComponent2~IsHidden.html).
        /// </summary>
        private static bool HideComponents(string nome, IAssemblyDoc ass, List<string> names,
                                           Dictionary<string, string> pathByComponentName)
        {
            foreach (var componentName in names)
            {
                IComponent2 component = ComponentOfCase(nome, ass, componentName, pathByComponentName, "«hidden»");
                if (component == null) return false;
                try { component.Visible = (int)swComponentVisibilityState_e.swComponentHidden; }
                catch (Exception ex)
                {
                    ErroreDiBanco(nome, "«hidden»: IComponent2.Visible on «" + componentName + "»: " + Perche(ex));
                    return false;
                }
                IComponent2 again = ComponentOfCase(nome, ass, componentName, pathByComponentName, "«hidden»");
                if (again == null) return false;
                bool hiddenNow = false;
                try { hiddenNow = again.IsHidden(false); } catch { }
                if (!hiddenNow)
                {
                    ErroreDiBanco(nome, "«" + componentName + "» is not hidden after IComponent2.Visible = " +
                                        "swComponentHidden (IsHidden(false) reads false or cannot be read)");
                    return false;
                }
                Console.WriteLine("       | component «" + componentName + "» hidden (IsHidden(false) read back)");
            }
            return true;
        }

        /// <summary>
        /// THE ASSEMBLY REOPENED LIGHTWEIGHT (Z-C21.11, finding N5):
        /// «reopen_lightweight»: true next to the list of components.
        ///
        /// Measured by the probe zc2111/probe_alleggeriti (30/9/2026): an
        /// assembly reopened with OpenDoc6 options Silent | OverrideDefaultLoadLightweight
        /// | LoadLightweight (1 | 64 | 128 = 193), after EVERY document of it was
        /// closed, came back with all its components lightweight
        /// (GetLightWeightComponentCount 4 of 4); a part still loaded as a
        /// document came back resolved. So:
        ///   1. the assembly is saved as «&lt;case&gt;.sldasm» under Bench\lavoro
        ///      and registered like the parts (deleted at the end, closed by
        ///      title);
        ///   2. it is closed by reference, then every loaded document whose FILE
        ///      is one of the case's is closed, twice (a part stays loaded while a
        ///      sub-assembly that holds it is open, as in ButtaViaLaPreparazione);
        ///   3. nothing of the case may still be loaded, looked at by path: a
        ///      leftover would come back resolved and the case would measure a
        ///      resolved assembly believing it lightweight;
        ///   4. it is reopened; a warning «already open» is a bench error for
        ///      the same reason, and so are a count of lightweight components
        ///      that is not above zero and a count of top-level components that
        ///      is not the one built.
        /// The documents of the components stay closed: a step that needs one
        /// open (insert_component) cannot follow, which step 0 refuses. Any
        /// failure closes and deletes what the preparation made.
        /// </summary>
        private static IModelDoc2 ReopenLightweight(string nome, IModelDoc2 doc, int topLevelCount, Preparazione fuori)
        {
            string path = Path.Combine(Attese.Lavoro, NomeDiFile(nome) + ".sldasm");
            int e = 0, w = 0;
            try { SaveTo(doc, path, out e, out w); }
            catch (Exception ex)
            {
                ErroreDiBanco(nome, "«reopen_lightweight»: saving the assembly: " + Perche(ex));
                return Abandon(fuori);
            }
            if (!File.Exists(path))
            {
                ErroreDiBanco(nome, "«reopen_lightweight»: saved without errors (e=" + e + ", w=" + w + ") but «" +
                                    path + "» is not there");
                return Abandon(fuori);
            }
            Register(fuori, path);

            Chiudi(doc);
            for (int round = 0; round < 2; round++)
                foreach (IModelDoc2 loaded in LoadedDocumentsOf(fuori.FileDaCancellare) ?? new List<IModelDoc2>())
                    Chiudi(loaded);
            // As the probe, before looking: the COM wrappers of the closed
            // documents are let go, so that nothing of this process keeps one.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            List<IModelDoc2> still = LoadedDocumentsOf(fuori.FileDaCancellare);
            if (still == null || still.Count > 0)
            {
                ErroreDiBanco(nome, "«reopen_lightweight»: " + (still == null
                    ? "I could not read which documents are loaded (GetFirstDocument, GetNext, GetPathName)"
                    : "still loaded after closing: " + string.Join(", ", FileNamesOf(still))) +
                    ". Reopened now, those would come back resolved.");
                return Abandon(fuori);
            }

            const int options = (int)swOpenDocOptions_e.swOpenDocOptions_Silent |
                                (int)swOpenDocOptions_e.swOpenDocOptions_OverrideDefaultLoadLightweight |
                                (int)swOpenDocOptions_e.swOpenDocOptions_LoadLightweight;   // 193, as the probe
            int errors = 0, warnings = 0;
            IModelDoc2 reopened;
            try
            {
                reopened = _sw.OpenDoc6(path, (int)swDocumentTypes_e.swDocASSEMBLY, options, "",
                                        ref errors, ref warnings) as IModelDoc2;
            }
            catch (Exception ex)
            {
                ErroreDiBanco(nome, "«reopen_lightweight»: OpenDoc6: " + Perche(ex));
                return Abandon(fuori);
            }
            if (reopened == null)
            {
                ErroreDiBanco(nome, "«reopen_lightweight»: OpenDoc6 answered null (e=" + errors + ", w=" + warnings + ")");
                return Abandon(fuori);
            }
            // From here the cleanup closes the reopened document.
            fuori.Documento = reopened;
            if ((warnings & (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen) != 0)
            {
                ErroreDiBanco(nome, "«reopen_lightweight»: OpenDoc6 warned that the assembly was already open " +
                                    "(w=" + warnings + "): it was not reopened, so it is not lightweight");
                return Abandon(fuori);
            }
            IAssemblyDoc reopenedAssembly = reopened as IAssemblyDoc;
            if (reopenedAssembly == null)
            {
                ErroreDiBanco(nome, "«reopen_lightweight»: the reopened document is not an IAssemblyDoc");
                return Abandon(fuori);
            }
            int lightweight = -1;
            try { lightweight = reopenedAssembly.GetLightWeightComponentCount(); } catch { }
            if (lightweight <= 0)
            {
                ErroreDiBanco(nome, "«reopen_lightweight»: after OpenDoc6 (options " + options + ") " +
                                    "GetLightWeightComponentCount " + (lightweight < 0 ? "could not be read" : "answered 0") +
                                    ": the case would run on a resolved assembly");
                return Abandon(fuori);
            }
            int count = QuantiComponenti(reopenedAssembly);
            if (count != topLevelCount)
            {
                ErroreDiBanco(nome, "«reopen_lightweight»: the assembly was built with " + topLevelCount +
                                    " top-level components and reopens with " + count);
                return Abandon(fuori);
            }
            Console.WriteLine("       | assembly saved as «" + Path.GetFileName(path) + "», closed with its components " +
                              "and reopened lightweight: " + lightweight + " lightweight component(s)");
            return reopened;
        }

        /// <summary>
        /// The loaded documents (ISldWorks.GetFirstDocument, then IModelDoc2.GetNext)
        /// whose file is one of <paramref name="paths"/>, compared as full paths.
        /// Null when the list cannot be read, or a document does not say its path:
        /// «I could not look» is not «nothing is loaded».
        /// </summary>
        private static List<IModelDoc2> LoadedDocumentsOf(List<string> paths)
        {
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paths)
            {
                try { wanted.Add(Path.GetFullPath(p)); }
                catch (ArgumentException) { }
            }
            var found = new List<IModelDoc2>();
            IModelDoc2 d;
            try { d = _sw.GetFirstDocument() as IModelDoc2; }
            catch (Exception) { return null; }
            for (int guard = 0; d != null; guard++)
            {
                if (guard > 10000) return null;
                string own;
                try { own = d.GetPathName(); }
                catch (Exception) { return null; }
                // A document never saved has no path, and none of the case's is
                // one: every file of the case is on disk before it is opened.
                if (!string.IsNullOrWhiteSpace(own))
                {
                    try { if (wanted.Contains(Path.GetFullPath(own))) found.Add(d); }
                    catch (ArgumentException) { return null; }
                }
                IModelDoc2 next;
                try { next = d.GetNext() as IModelDoc2; }
                catch (Exception) { return null; }
                d = next;
            }
            return found;
        }

        /// <summary>The file names of some documents, for a message.</summary>
        private static List<string> FileNamesOf(List<IModelDoc2> docs)
        {
            var names = new List<string>();
            foreach (IModelDoc2 one in docs)
            {
                string own = "?";
                try { own = Path.GetFileName(one.GetPathName()); } catch { }
                names.Add(own);
            }
            return names;
        }

        /// <summary>
        /// A preparation that stopped half-way (Z-C21.11): what it made is closed
        /// and deleted here, since the caller gets null and has nothing to hand
        /// to ButtaViaLaPreparazione. A document left open would be found by the
        /// next case, and a file left in Bench\lavoro would be found by the next
        /// round.
        /// </summary>
        private static IModelDoc2 Abandon(Preparazione p)
        {
            ButtaViaLaPreparazione(p);
            return null;
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
        /// configuration (measured on parts by create_configuration, read back
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
                    var component = ComponentOfCase(nome, ass, componentName, pathByComponentName, "«configurations»");
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
                    var component = ComponentOfCase(nome, ass, componentName, pathByComponentName, "«configurations»");
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
        /// <paramref name="askedBy"/> is the key of the case that names it
        /// («configurations», «envelope», «hidden»), for the message.
        /// </summary>
        private static IComponent2 ComponentOfCase(
            string nome, IAssemblyDoc ass, string componentName, Dictionary<string, string> pathByComponentName,
            string askedBy)
        {
            string path;
            if (!pathByComponentName.TryGetValue(componentName, out path))
            {
                ErroreDiBanco(nome, askedBy + " names «" + componentName + "», which is not a component of the case");
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
            ErroreDiBanco(nome, askedBy + " names «" + componentName + "», and no component of the assembly " +
                                "uses its file (it has «to_insert» false?)");
            return null;
        }

        /// <summary>
        /// Chiude e cancella quello che la preparazione ha lasciato in giro.
        ///
        /// SI CANCELLANO ANCHE I FILE, non solo i documenti. `CloseLeftovers`
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
            var passi = Elenco(componente, "steps");
            if (passi == null || passi.Count == 0)
            {
                ErroreDiBanco(etichetta, "manca «steps»: un componente senza " +
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
                    string operazione = Testo(passo, "operation");
                    if (operazione == null)
                    {
                        ErroreDiBanco(etichetta, "al passo " + (i + 1) + " manca «operation»");
                        return false;
                    }
                    object grezzi;
                    passo.TryGetValue("arguments", out grezzi);
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
        /// «position_mm», and that assembly is saved as
        /// <paramref name="path"/> («&lt;stem&gt;.sldasm»), which the caller then
        /// registers and inserts like a part. Without it no case could hold a
        /// component inside a sub-assembly, and fix_component and
        /// replace_component, which work on the top level only, could not
        /// be seen refusing one.
        ///
        /// The children are registered here, BEFORE the caller registers the
        /// sub-assembly: the files are reopened in that order, parts first. The
        /// sub-assembly is closed by its title at the end like a part: it is
        /// registered after it is saved, so the title no longer changes. If
        /// this fails before the save, the new assembly is closed here, since
        /// nothing else knows it. One level only: a sub-assembly inside a
        /// sub-assembly is refused, no case needs it yet.
        /// Two children with the same name are two instances of ONE part
        /// (Z-C21.11): it is built from the first, and the second brings only
        /// its «position_mm».
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
            // The child names already built here (Z-C21.11, finding M23).
            var builtHere = new HashSet<string>(StringComparer.Ordinal);
            for (int k = 0; k < children.Count; k++)
            {
                var child = children[k] as Dictionary<string, object>;
                if (child == null)
                {
                    ErroreDiBanco(label, "component " + (k + 1) + " is not an object");
                    return false;
                }
                if (Elenco(child, "components") != null)
                {
                    ErroreDiBanco(label, "a sub-assembly inside a sub-assembly is not supported by the bench");
                    return false;
                }
                // Z-C21.11: «envelope» and «hidden» act on top-level components
                // only (they are found by file among GetComponents(true)); inside
                // a sub-assembly they would be read by nobody, and the case
                // would run on another assembly.
                if (child.ContainsKey("envelope") || child.ContainsKey("hidden"))
                {
                    ErroreDiBanco(label, "«envelope» and «hidden» act on top-level components only, and component " +
                                         (k + 1) + " is inside a sub-assembly");
                    return false;
                }
                string childName = Testo(child, "name") ?? ("comp" + (k + 1));
                string childPath = Path.Combine(Attese.Lavoro, stem + "_" + NomeDiFile(childName) + ".sldprt");
                // A NAME ALREADY BUILT HERE IS ONE MORE INSTANCE OF THE SAME PART
                // (Z-C21.11, finding M23): the indented BOM of «A + 3 x S(2 B) + B»
                // needs two instances of ONE file inside S, and the file name comes
                // from the child name. The part is built and registered once, from
                // the first child with that name; a later one brings only its
                // «position_mm», and steps of its own are refused rather than
                // ignored.
                if (builtHere.Contains(childName))
                {
                    if (Elenco(child, "steps") != null)
                    {
                        ErroreDiBanco(label, "component «" + childName + "» repeats a name already built in this " +
                                             "sub-assembly and brings steps of its own: a repeated name is one more " +
                                             "instance of the same part, built once from the first");
                        return false;
                    }
                }
                else
                {
                    if (!CostruisciESalva(label + " · component «" + childName + "»", child, childPath))
                        return false;
                    Register(fuori, childPath);
                    builtHere.Add(childName);
                }
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
                throw new Shared.PreparazioneFallita(Program.Sentinella.Referto());

            var nata = _sw.NewDocument(tpl, 0, 0, 0) as IModelDoc2;
            if (nata != null) Program.Sentinella.Riuscita();
            else if (Program.Sentinella.Fallita())
                throw new Shared.PreparazioneFallita(Program.Sentinella.Referto());

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
        /// normale e' «voglio questo assieme, fammelo», e chiedere `to_insert`
        /// a ogni componente sarebbe rumore. `false` si scrive solo quando si
        /// vuole provare l'inserimento, che e' il caso raro.
        /// </summary>
        private static bool DaInserire(Dictionary<string, object> componente)
        {
            object v;
            if (componente == null ||
                !componente.TryGetValue("to_insert", out v) || v == null) return true;
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
        /// Il campo si chiama `position_mm` e non `position`: e' la stessa
        /// convenzione del catalogo, dove l'unita' sta nel nome perche' un
        /// numero senza unita' e' un numero che qualcuno interpretera' in
        /// metri.
        /// </summary>
        private static double[] PosizioneMm(Dictionary<string, object> componente)
        {
            var v = new double[3];
            object grezzo;
            if (componente == null || !componente.TryGetValue("position_mm", out grezzo) ||
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
