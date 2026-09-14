using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SolidWorksMcp.Ponte
{
    /// <summary>
    /// L'aggancio a SolidWorks e alla libreria di disegno vera.
    ///
    /// NON SI RISCRIVE NIENTE. Le operazioni di disegno sono quelle di
    /// `Riscontro.Disegno.dll`, caricate per riflessione: cosi' quello che si
    /// prova da qui e' ESATTAMENTE quello che il progettista avra' in mano.
    /// Una seconda implementazione «solo per le prove» misurerebbe se stessa —
    /// che e' il modo piu' elegante di non misurare niente.
    ///
    /// Fino all'add-in 0.46.0 la libreria caricata era `OpenPDM.SwAddin.dll`,
    /// cioe' il ponte tirava dentro tutto il PDM per disegnare un foro. Dalla
    /// 0.47.0 le operazioni stanno per conto loro. **La riflessione pero' e'
    /// rimasta, e adesso non ha piu' ragione di esserci**: e' la voce B7 del
    /// TODO. Finche' c'e', i nomi dei tipi sono stringhe e un errore di
    /// battitura si scopre a runtime.
    ///
    /// NON SI AVVIA SOLIDWORKS. Ci si aggancia a una sessione che c'e' gia'.
    /// Un ponte che avvia il programma decide per conto di chi lo usa quale
    /// versione, quale licenza e quale documento: se non e' aperto, si dice e
    /// ci si ferma.
    /// </summary>
    internal sealed class Sessione
    {
        private object _sw;                 // ISldWorks, tenuto come object
        private Assembly _addin;
        private Type _tOperazioni;
        private Type _tStato;
        private Type _tEsito;

        /// <summary>
        /// Dove sta la libreria di disegno. Si puo' cambiare con
        /// `RISCONTRO_DISEGNO`.
        ///
        /// **Non e' piu' `OpenPDM.SwAddin.dll`.** Le operazioni sono uscite
        /// dall'add-in e vivono in `Riscontro.Disegno.dll` (add-in 0.47.0): il
        /// ponte non ha piu' bisogno del PDM per disegnare. La vecchia
        /// variabile `OPENPDM_ADDIN` resta accettata perche' chi ce l'ha
        /// impostata non deve scoprirlo da un rifiuto, ma se punta ancora
        /// all'add-in non troverebbe i tipi: in quel caso lo si dice per nome.
        /// </summary>
        public string PercorsoLibreria { get; private set; }

        public bool Agganciata => _sw != null && _tOperazioni != null;

        /// <summary>
        /// Si aggancia, oppure spiega perche' no. Non solleva: un ponte che
        /// muore all'avvio non fa in tempo a dire niente a chi lo chiama.
        /// </summary>
        /// <summary>
        /// Dove SolidWorks tiene le sue librerie di interoperabilita'.
        /// Si puo' cambiare con SW_REDIST.
        /// </summary>
        private static readonly string[] PostiDelRedist =
        {
            @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist",
            @"C:\Program Files\SolidWorks Corp\SolidWorks\api\redist",
        };

        /// <summary>
        /// LE LIBRERIE DI DASSAULT NON SI COPIANO NEL REPOSITORY.
        ///
        /// L'add-in e' compilato contro `SolidWorks.Interop.sldworks.dll`:
        /// appena si tocca un documento, .NET va a cercarla e la cerca
        /// accanto all'eseguibile. Metterla li' vorrebbe dire ridistribuire
        /// un binario di Dassault dentro un repository — e legarsi a una
        /// versione, che e' il modo piu' sicuro di rompersi al prossimo
        /// aggiornamento di SolidWorks.
        ///
        /// Si prende invece quella INSTALLATA su questa macchina. Cosi' la
        /// versione e' sempre quella giusta per definizione: e' la stessa
        /// che sta girando.
        /// </summary>
        private static void TrovaLeLibrerieDiSolidWorks()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (mittente, argomento) =>
            {
                var nome = new AssemblyName(argomento.Name).Name;
                if (!nome.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase))
                    return null;

                var posti = new List<string>();
                var suo = Environment.GetEnvironmentVariable("SW_REDIST");
                if (!string.IsNullOrWhiteSpace(suo)) posti.Add(suo);
                posti.AddRange(PostiDelRedist);

                foreach (var cartella in posti)
                {
                    var file = Path.Combine(cartella, nome + ".dll");
                    if (!File.Exists(file)) continue;
                    try { return Assembly.LoadFrom(file); }
                    catch (Exception ex)
                    {
                        Mcp.Protocollo.Nota("[ponte] " + file + ": " + ex.Message);
                    }
                }
                Mcp.Protocollo.Nota(
                    "[ponte] non trovo " + nome + ": indica la cartella redist " +
                    "di SolidWorks nella variabile SW_REDIST.");
                return null;
            };
        }

        public string Aggancia()
        {
            TrovaLeLibrerieDiSolidWorks();
            PercorsoLibreria = Environment.GetEnvironmentVariable("RISCONTRO_DISEGNO");
            if (string.IsNullOrWhiteSpace(PercorsoLibreria))
                PercorsoLibreria = Environment.GetEnvironmentVariable("OPENPDM_ADDIN");
            if (string.IsNullOrWhiteSpace(PercorsoLibreria))
                PercorsoLibreria = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
                    "Riscontro.Disegno.dll");

            if (!File.Exists(PercorsoLibreria))
                return "Non trovo la libreria di disegno in «" + PercorsoLibreria + "». " +
                       "Mettila accanto all'eseguibile oppure indica il percorso " +
                       "nella variabile d'ambiente RISCONTRO_DISEGNO.";

            try
            {
                // SI LEGGONO I BYTE INVECE DI LoadFrom, e non e' pignoleria:
                // se la libreria sta su un'unita' di rete `Assembly.LoadFrom`
                // fallisce con 0x80131515, un errore che non nomina la rete e
                // manda a cercare una dipendenza mancante che non esiste.
                _addin = Assembly.Load(File.ReadAllBytes(PercorsoLibreria));

                _tOperazioni = _addin.GetType("Riscontro.Disegno.OperazioniSw", false);
                _tStato = _addin.GetType("Riscontro.Disegno.StatoDocumento", false);
                _tEsito = _addin.GetType("Riscontro.Disegno.OperazioniSw+Esito", false);

                if (_tOperazioni == null || _tStato == null || _tEsito == null)
                {
                    // IL CASO DI CHI HA ANCORA IL VECCHIO PERCORSO IMPOSTATO.
                    // Dirgli «tipo non trovato» lo manderebbe a cercare un
                    // difetto che non c'e': il file e' quello sbagliato, non
                    // rotto.
                    bool vecchia = _addin.GetType("OpenPDM.SwAddin.Agente.OperazioniSw", false) != null;
                    return vecchia
                        ? "«" + PercorsoLibreria + "» e' il vecchio add-in OpenPDM. " +
                          "Dalla 0.47.0 le operazioni stanno in «Riscontro.Disegno.dll»: " +
                          "punta li' RISCONTRO_DISEGNO, oppure togli OPENPDM_ADDIN e " +
                          "mettila accanto all'eseguibile."
                        : "«" + PercorsoLibreria + "» si carica ma non contiene le " +
                          "operazioni di disegno: non e' Riscontro.Disegno.dll.";
                }
            }
            catch (Exception ex)
            {
                return "La libreria c'e' ma non si carica: " + ex.Message;
            }

            try
            {
                _sw = Marshal.GetActiveObject("SldWorks.Application");
            }
            catch (COMException)
            {
                return "SolidWorks non risulta aperto. Questo ponte si aggancia a " +
                       "una sessione che c'e' gia': aprilo, apri un pezzo, e riprova.";
            }
            catch (Exception ex)
            {
                return "Non riesco ad agganciarmi a SolidWorks: " + ex.Message;
            }

            return null;
        }

        public string VersioneSolidWorks()
        {
            try
            {
                var m = _sw.GetType().InvokeMember(
                    "RevisionNumber", BindingFlags.InvokeMethod, null, _sw, null);
                return Convert.ToString(m);
            }
            catch { return "?"; }
        }

        public string VersioneLibreria()
        {
            try { return _addin.GetName().Version.ToString(); }
            catch { return "?"; }
        }

        /// <summary>
        /// Il documento che SolidWorks ha davanti, o null.
        ///
        /// IL NOME GIUSTO NON E' QUELLO DEL C#. Chiamando per riflessione
        /// su un oggetto COM si passa dal suo IDispatch, e li' dentro non
        /// c'e' `IActiveDoc2` — quello e' il nome che l'interoperabilita'
        /// tipizzata espone in C#. Il nome che il COM conosce e'
        /// `ActiveDoc`. Chiedere quello sbagliato non da' errore utile:
        /// l'eccezione viene ingoiata e il ponte risponde «non c'e' nessun
        /// documento aperto» mentre ce ne sono venti — misurato l'11
        /// settembre 2026 alla prima prova del ponte.
        /// </summary>
        public object DocumentoAttivo()
        {
            foreach (var nome in new[] { "ActiveDoc", "IActiveDoc2" })
            {
                try
                {
                    var d = _sw.GetType().InvokeMember(
                        nome, BindingFlags.GetProperty, null, _sw, null);
                    if (d != null) return d;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// CHI E' il documento: titolo e percorso, per poterlo riconoscere dopo.
        ///
        /// PERCHE' SERVE, ED E' COSTATO UN PEZZO DI QUALCUN ALTRO.
        /// Il 14 settembre 2026 una sessione di prova ha estruso un blocco
        /// 314x274x52 dentro «Parte4», che non era il suo documento. Non per un
        /// difetto di geometria: fra la creazione del proprio pezzo e la
        /// chiamata, il documento ATTIVO era cambiato — una finestra portata
        /// davanti, e il ponte ha lavorato su quella. Nessun errore, nessun
        /// avviso, geometria scritta nel pezzo sbagliato.
        ///
        /// E' il difetto che questo progetto esiste per rendere impossibile,
        /// nella sua forma piu' pura, e stava nel ponte.
        /// </summary>
        public Dictionary<string, object> Identita(object doc)
        {
            if (doc == null) return null;
            string titolo = "", percorso = "";
            try { titolo = Convert.ToString(Chiama(doc, "GetTitle")); } catch { }
            try { percorso = Convert.ToString(Chiama(doc, "GetPathName")); } catch { }
            return new Dictionary<string, object>
            {
                ["titolo"] = titolo ?? "",
                ["percorso"] = percorso ?? "",
            };
        }

        private static object Chiama(object oggetto, string metodo)
        {
            return oggetto.GetType().InvokeMember(
                metodo, BindingFlags.InvokeMethod, null, oggetto, null);
        }

        /// <summary>
        /// Due identita' sono lo stesso documento?
        ///
        /// Il PERCORSO vince quando c'e': e' l'unica cosa che non cambia. Il
        /// titolo e' il ripiego per i documenti mai salvati, che percorso non
        /// ne hanno — e sono proprio quelli su cui si lavora in prova.
        /// </summary>
        public static bool StessoDocumento(
            Dictionary<string, object> a, Dictionary<string, object> b)
        {
            if (a == null || b == null) return false;
            string pa = Convert.ToString(a.ContainsKey("percorso") ? a["percorso"] : "");
            string pb = Convert.ToString(b.ContainsKey("percorso") ? b["percorso"] : "");
            if (!string.IsNullOrWhiteSpace(pa) || !string.IsNullOrWhiteSpace(pb))
                return string.Equals(
                    (pa ?? "").Trim().Replace('/', '\\'),
                    (pb ?? "").Trim().Replace('/', '\\'),
                    StringComparison.OrdinalIgnoreCase);

            string ta = Convert.ToString(a.ContainsKey("titolo") ? a["titolo"] : "");
            string tb = Convert.ToString(b.ContainsKey("titolo") ? b["titolo"] : "");
            return !string.IsNullOrWhiteSpace(ta) &&
                   string.Equals(ta, tb, StringComparison.Ordinal);
        }

        /// <summary>
        /// Lo stato del documento attivo: e' quello che il modello DEVE vedere
        /// prima di proporre qualunque cosa.
        /// </summary>
        public Dictionary<string, object> Stato(out string errore)
        {
            errore = null;
            var doc = DocumentoAttivo();
            if (doc == null)
            {
                errore = "Non c'e' nessun documento aperto in SolidWorks.";
                return null;
            }
            try
            {
                var stato = LeggiStatoOggetto(doc);
                var dati = _tStato.GetProperty(
                    "Dati", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return dati.GetValue(stato) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                errore = "Non riesco a leggere il documento: " + ex.Message;
                return null;
            }
        }

        private object LeggiStatoOggetto(object doc)
        {
            var leggi = _tStato.GetMethod(
                "Leggi", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            // La firma ha un secondo parametro facoltativo (l'applicazione):
            // senza, lo stato non sa dire quali ALTRI documenti sono aperti, e
            // per le tavole quello non e' un di piu' ma una precondizione.
            var parametri = leggi.GetParameters().Length == 1
                ? new[] { doc }
                : new[] { doc, _sw };
            return leggi.Invoke(null, parametri);
        }

        /// <summary>
        /// Esegue una operazione. Ritorna le misure, oppure null con il motivo.
        ///
        /// LO STATO SI RILEGGE PRIMA DI OGNI OPERAZIONE. Gli identificativi
        /// delle facce sono POSIZIONALI — si rinumerano per area a ogni
        /// lettura — quindi dopo un taglio «F13» non e' piu' la faccia di
        /// prima. Chi esegue se ne accorge dalla firma che il piano porta con
        /// se' e la ritrova; ma la firma va confrontata con lo stato di
        /// ADESSO, non con quello di dieci secondi fa.
        /// </summary>
        public Dictionary<string, object> Esegui(
            string operazione, Dictionary<string, object> argomenti, out string errore)
        {
            errore = null;
            var doc = DocumentoAttivo();
            if (doc == null)
            {
                errore = "Non c'e' nessun documento aperto in SolidWorks.";
                return null;
            }

            try
            {
                var costruttore = _tOperazioni.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
                var op = costruttore.Invoke(new object[]
                {
                    _sw,
                    (Action<string>)(riga => Mcp.Protocollo.Nota("  " + riga)),
                });

                var stato = LeggiStatoOggetto(doc);
                var esegui = _tOperazioni.GetMethod(
                    "Esegui", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                var esito = esegui.Invoke(op, new object[]
                {
                    doc, operazione, argomenti, stato, null,
                });

                bool riuscito = (bool)_tEsito.GetField("Riuscito").GetValue(esito);
                if (!riuscito)
                {
                    errore = _tEsito.GetField("Errore").GetValue(esito) as string;
                    return null;
                }

                // Si ricostruisce: cambiare i parametri non muove la geometria,
                // la muove la ricostruzione. Senza, il numero e' nuovo e il
                // pezzo e' vecchio.
                try
                {
                    doc.GetType().InvokeMember(
                        "EditRebuild3", BindingFlags.InvokeMethod, null, doc, null);
                }
                catch { }

                return _tEsito.GetField("Misure").GetValue(esito) as Dictionary<string, object>;
            }
            catch (TargetInvocationException ex)
            {
                errore = (ex.InnerException ?? ex).Message;
                return null;
            }
            catch (Exception ex)
            {
                errore = ex.Message;
                return null;
            }
        }
    }
}
