using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Riscontro.Disegno;
// ATTENZIONE AL NOME: SolidWorks.Interop.sldworks ha un tipo `Environment`,
// che rende ambiguo `Environment.GetEnvironmentVariable`. Qui sotto quelle
// chiamate sono qualificate `System.Environment`: non e prolissita, senza
// non compila.
using SolidWorks.Interop.sldworks;

namespace SolidWorksMcp.Ponte
{
    /// <summary>
    /// L'aggancio a SolidWorks e alla libreria di disegno vera.
    ///
    /// NON SI RISCRIVE NIENTE. Le operazioni di disegno sono quelle di
    /// `Riscontro.Disegno`, chiamate direttamente: cosi' quello che si
    /// prova da qui e' ESATTAMENTE quello che il progettista avra' in mano.
    /// Una seconda implementazione «solo per le prove» misurerebbe se stessa —
    /// che e' il modo piu' elegante di non misurare niente.
    ///
    /// Fino all'add-in 0.46.0 la libreria caricata era `OpenPDM.SwAddin.dll`,
    /// cioe' il ponte tirava dentro tutto il PDM per disegnare un foro, e la
    /// riflessione era l'unico modo: quella DLL e' privata e questo
    /// repository non poteva referenziarla.
    ///
    /// Dalla 0.6.0 `Riscontro.Disegno` sta qui dentro e si referenzia come
    /// un progetto qualunque. **La riflessione sui tipi della libreria non
    /// c'e' piu'**: i nomi sono nomi, non stringhe, e un errore di battitura
    /// lo prende il compilatore invece del cliente.
    ///
    /// Resta il late binding verso SOLIDWORKS (`DocumentoAttivo`,
    /// `Identita`, `Chiama`), e non e' una svista: e' la strada misurata,
    /// e il commento sopra `DocumentoAttivo` racconta perche'.
    ///
    /// NON SI AVVIA SOLIDWORKS. Ci si aggancia a una sessione che c'e' gia'.
    /// Un ponte che avvia il programma decide per conto di chi lo usa quale
    /// versione, quale licenza e quale documento: se non e' aperto, si dice e
    /// ci si ferma.
    /// </summary>
    internal sealed class Sessione
    {
        private object _sw;                 // ISldWorks, tenuto come object

        /// <summary>
        /// L'assembly della libreria, tenuto SOLO per dirne la versione.
        /// I tipi non si cercano piu' qui dentro: si nominano.
        /// </summary>
        private Assembly _libreria;
        private bool _caricata;

        /// <summary>
        /// Dove sta la libreria di disegno. Si puo' cambiare con
        /// `RISCONTRO_DISEGNO`.
        ///
        /// **Non e' piu' `OpenPDM.SwAddin.dll`.** Le operazioni sono uscite
        /// dall'add-in e vivono in `Riscontro.Disegno.dll` (add-in 0.47.0): il
        /// ponte non ha piu' bisogno del PDM per disegnare.
        ///
        /// Da quando la libreria e' referenziata invece che caricata a mano,
        /// **questa proprieta' non decide piu' niente**: .NET carica la DLL da
        /// solo, accanto all'eseguibile. Serve a due cose, tutte e due di chi
        /// legge e non del programma — dire QUALE file si sta usando davvero, e
        /// avvisare se qualcuno ha impostato una variabile che punta altrove,
        /// invece di lasciargli credere che quella variabile conti ancora.
        /// La vecchia `OPENPDM_ADDIN` resta letta per lo stesso motivo.
        /// </summary>
        public string PercorsoLibreria { get; private set; }

        public bool Agganciata => _sw != null && _caricata;

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
                var suo = System.Environment.GetEnvironmentVariable("SW_REDIST");
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

        /// <summary>
        /// Si aggancia, oppure spiega perche' no. Non solleva: un ponte che
        /// muore all'avvio non fa in tempo a dire niente a chi lo chiama.
        /// </summary>
        public string Aggancia()
        {
            TrovaLeLibrerieDiSolidWorks();
            PercorsoLibreria = System.Environment.GetEnvironmentVariable("RISCONTRO_DISEGNO");
            if (string.IsNullOrWhiteSpace(PercorsoLibreria))
                PercorsoLibreria = System.Environment.GetEnvironmentVariable("OPENPDM_ADDIN");
            if (string.IsNullOrWhiteSpace(PercorsoLibreria))
                PercorsoLibreria = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
                    "Riscontro.Disegno.dll");

            // NON SI RIFIUTA PIU' SE IL FILE NON C'E' DOVE CI SI ASPETTA.
            // Il tipo adesso e' compilato dentro: se .NET non trovasse la
            // DLL, il ponte non sarebbe nemmeno partito. Rifiutare qui
            // vorrebbe dire fermarsi per un percorso sbagliato mentre la
            // libreria giusta e' gia' in mano — un rifiuto di cio' che
            // funziona, che e' il difetto peggiore.
            if (!File.Exists(PercorsoLibreria))
                Mcp.Protocollo.Nota(
                    "[ponte] «" + PercorsoLibreria + "» non esiste; uso quella " +
                    "caricata insieme all'eseguibile.");

            try
            {
                // SI DICE QUALE FILE, ANCHE SE ADESSO IL TIPO E' COMPILATO.
                //
                // `OperazioniSw` arriva dal riferimento di progetto, quindi
                // .NET la carica da solo dalla cartella dell'eseguibile. Ma
                // chi ha impostato RISCONTRO_DISEGNO o OPENPDM_ADDIN si
                // aspetta che serva a qualcosa, e una variabile che non fa
                // niente e' peggio di una che non esiste: si tocca il tipo e
                // si guarda DA DOVE e' venuto davvero, poi lo si confronta
                // con quello che era stato chiesto.
                _libreria = typeof(OperazioniSw).Assembly;
                _caricata = true;

                var davvero = "";
                try { davvero = _libreria.Location ?? ""; } catch { }
                if (!string.IsNullOrEmpty(davvero) &&
                    !string.Equals(
                        Path.GetFullPath(davvero), Path.GetFullPath(PercorsoLibreria),
                        StringComparison.OrdinalIgnoreCase))
                {
                    Mcp.Protocollo.Nota(
                        "[ponte] avviso: avevi indicato «" + PercorsoLibreria +
                        "» ma sto usando «" + davvero + "». Le operazioni sono " +
                        "quelle del secondo.");
                    PercorsoLibreria = davvero;
                }
            }
            catch (Exception ex)
            {
                return "La libreria di disegno non si carica: " + ex.Message;
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
            try { return _libreria.GetName().Version.ToString(); }
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
                var stato = LeggiStato(doc);
                return stato?.Dati;
            }
            catch (Exception ex)
            {
                errore = "Non riesco a leggere il documento: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Lo stato del documento. Il secondo parametro e' l'applicazione:
        /// senza, lo stato non sa dire quali ALTRI documenti sono aperti, e
        /// per le tavole quello non e' un di piu' ma una precondizione.
        /// </summary>
        private StatoDocumento LeggiStato(object doc)
        {
            return StatoDocumento.Leggi(doc as IModelDoc2, _sw as ISldWorks);
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
                var op = new OperazioniSw(
                    _sw as ISldWorks,
                    riga => Mcp.Protocollo.Nota("  " + riga));

                var stato = LeggiStato(doc);
                var esito = op.Esegui(
                    doc as IModelDoc2, operazione, argomenti, stato, null);

                if (esito == null)
                {
                    errore = "L'operazione «" + operazione + "» non ha risposto niente: " +
                             "e' un difetto della libreria, segnalalo con il log.";
                    return null;
                }
                if (!esito.Riuscito)
                {
                    errore = esito.Errore;
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

                return esito.Misure;
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
