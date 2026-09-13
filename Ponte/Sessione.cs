using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SolidWorksMcp.Ponte
{
    /// <summary>
    /// L'aggancio a SolidWorks e all'add-in vero.
    ///
    /// NON SI RISCRIVE NIENTE. Le operazioni di disegno sono quelle di
    /// `OpenPDM.SwAddin.dll`, caricate per riflessione: cosi' quello che si
    /// prova da qui e' ESATTAMENTE quello che il progettista avra' in mano.
    /// Una seconda implementazione «solo per le prove» misurerebbe se stessa —
    /// che e' il modo piu' elegante di non misurare niente.
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

        /// <summary>Dove sta l'add-in. Si puo' cambiare con OPENPDM_ADDIN.</summary>
        public string PercorsoAddin { get; private set; }

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
            PercorsoAddin = Environment.GetEnvironmentVariable("OPENPDM_ADDIN");
            if (string.IsNullOrWhiteSpace(PercorsoAddin))
                PercorsoAddin = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
                    "OpenPDM.SwAddin.dll");

            if (!File.Exists(PercorsoAddin))
                return "Non trovo l'add-in in «" + PercorsoAddin + "». " +
                       "Mettilo accanto all'eseguibile oppure indica il percorso " +
                       "nella variabile d'ambiente OPENPDM_ADDIN.";

            try
            {
                _addin = Assembly.LoadFrom(PercorsoAddin);
                _tOperazioni = _addin.GetType("OpenPDM.SwAddin.Agente.OperazioniSw", true);
                _tStato = _addin.GetType("OpenPDM.SwAddin.Agente.StatoDocumento", true);
                _tEsito = _addin.GetType("OpenPDM.SwAddin.Agente.OperazioniSw+Esito", true);
            }
            catch (Exception ex)
            {
                return "L'add-in c'e' ma non si carica: " + ex.Message;
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

        public string VersioneAddin()
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
