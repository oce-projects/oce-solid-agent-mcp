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

namespace Oce.Solid.Agent.Ponte
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
    // `partial`: il ciclo di vita del documento (nuovo, apri, elenca, attiva)
    // sta in Ponte/Documenti.cs, voci Z-B1, Z-B2, Z-B5, Z-B6.
    internal sealed partial class Sessione
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
        /// Si puo' cambiare con SW_INSTALL_PATH (vedi <see cref="RedistChiesto"/>).
        /// </summary>
        private static readonly string[] PostiDelRedist =
        {
            @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist",
            @"C:\Program Files\SolidWorks Corp\SolidWorks\api\redist",
        };

        /// <summary>
        /// LA CARTELLA REDIST CHIESTA DA CHI HA INSTALLATO, O `null` (voce H10).
        ///
        /// ── PERCHE' DUE VARIABILI, E PERCHE' UNA SOLA E' QUELLA GIUSTA ───
        ///
        /// C'erano **tre modi** di dire dove sta SOLIDWORKS, con **due
        /// significati**: la radice dell'installazione (`SW_INSTALL_PATH`, che
        /// i `.csproj` leggono gia') e la sottocartella `api\redist`
        /// (`SW_REDIST`). Nessuno dei due era sbagliato, ma *due nomi per la
        /// stessa cosa sono due cose che un giorno divergono*, e qui
        /// divergevano gia': chi impostava quella per compilare doveva
        /// impostare **anche** l'altra per far girare il ponte, senza che
        /// niente glielo dicesse.
        ///
        /// **Vince la radice.** Da `...\SOLIDWORKS` si ricava `api\redist`; al
        /// contrario no — da una sottocartella si risalirebbe per convenzione,
        /// e una convenzione indovinata e' il posto dove nascono i percorsi
        /// plausibili e sbagliati.
        ///
        /// `SW_REDIST` **resta letto**, e resta secondo: chi ce l'ha gia' nel
        /// proprio profilo non deve accorgersi di niente. *Una variabile che
        /// smette di funzionare senza dirlo e' peggio di una che non c'e' mai
        /// stata.*
        /// </summary>
        internal static string RedistChiesto()
        {
            var radice = System.Environment.GetEnvironmentVariable("SW_INSTALL_PATH");
            if (!string.IsNullOrWhiteSpace(radice))
                return Path.Combine(radice.Trim(), "api", "redist");
            var suo = System.Environment.GetEnvironmentVariable("SW_REDIST");
            return string.IsNullOrWhiteSpace(suo) ? null : suo.Trim();
        }

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
                var suo = RedistChiesto();
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
                    "[ponte] non trovo " + nome + ": indica dove e' installato " +
                    "SolidWorks nella variabile SW_INSTALL_PATH (la radice, " +
                    "quella che finisce con «\\SOLIDWORKS»).");
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

            // OCE_NO_ATTACH: START WITHOUT TOUCHING SOLIDWORKS (entry H19, L1).
            //
            // A SOLIDWORKS session can be busy with work that must not be
            // disturbed: a batch job, or the bench of this repository, which
            // drives the same session for a quarter of an hour. The installer
            // runs this bridge twice after copying it (the self-test and, with
            // --verify, an MCP handshake), and both used to attach to whatever
            // session was running and read its active document. With this
            // variable set the bridge starts, answers the protocol and lists
            // its tools, and says why it is not attached; the tools that need
            // SOLIDWORKS refuse with that reason. It is read here and nowhere
            // else, so there is one place that decides.
            if (NoAttachRequested())
                return "Not attaching to SOLIDWORKS: OCE_NO_ATTACH is set. Unset it " +
                       "(or set it to 0) and restart the bridge to attach.";

            try
            {
                _sw = Marshal.GetActiveObject("SldWorks.Application");
            }
            catch (COMException ex)
            {
                return MessaggioDiAggancio(ex.ErrorCode);
            }
            catch (Exception ex)
            {
                return "Non riesco ad agganciarmi a SolidWorks: " + ex.Message;
            }

            RicordaIlProcesso();
            return null;
        }

        /// <summary>
        /// True when OCE_NO_ATTACH asks the bridge not to attach: any value
        /// except empty, "0" and "false". A variable that is set to "0" and
        /// still switches something off would be the surprise this avoids.
        /// </summary>
        internal static bool NoAttachRequested()
        {
            var v = System.Environment.GetEnvironmentVariable("OCE_NO_ATTACH");
            if (string.IsNullOrWhiteSpace(v)) return false;
            v = v.Trim();
            return v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Cosa dire quando `GetActiveObject("SldWorks.Application")` fallisce
        /// (voce H16). Sono DUE mancanze diverse con due azioni diverse, e fino
        /// alla 0.87.0 avevano un messaggio solo:
        ///
        ///   MK_E_UNAVAILABLE    0x800401E3   installato, non aperto  -> aprilo
        ///   CO_E_CLASSSTRING    0x800401F3   il ProgID non c'e'      -> non e' installato
        ///   REGDB_E_CLASSNOTREG 0x80040154   classe non registrata   -> idem
        ///
        /// «non risulta aperto» resta PAROLA PER PAROLA: l'installatore la cerca
        /// nell'uscita di `--prova` per dire «installato, manca solo SOLIDWORKS
        /// aperto». Il messaggio di «non installato» NON deve contenerla, se no
        /// un'installazione su una macchina senza SOLIDWORKS uscirebbe 0.
        /// </summary>
        internal static string MessaggioDiAggancio(int hresult)
        {
            switch (unchecked((uint)hresult))
            {
                case 0x800401E3:
                    return "SolidWorks non risulta aperto. Questo ponte si aggancia a " +
                           "una sessione che c'e' gia': aprilo, apri un pezzo, e riprova.";
                case 0x800401F3:
                case 0x80040154:
                    return "SolidWorks non e' installato su questa macchina, o non e' " +
                           "registrato: Windows non conosce «SldWorks.Application». " +
                           "Aprirlo non basta — va installato, o riparato dall'Installation Manager.";
                default:
                    return "Non riesco ad agganciarmi a SolidWorks (codice 0x" +
                           unchecked((uint)hresult).ToString("X8") + ").";
            }
        }

        /// <summary>
        /// Il PID del processo `SLDWORKS.exe` a cui ci si e' agganciati.
        /// `0` quando non si e' riusciti a saperlo.
        /// </summary>
        private int _pid;

        /// <summary>
        /// Si chiede a SOLIDWORKS qual e' il suo processo, e si tiene il numero.
        ///
        /// PERCHE' NON `Process.GetProcessesByName("SLDWORKS")`. Su questa
        /// macchina di sessioni SOLIDWORKS ce n'e' piu' di una — e' scritto fra
        /// i vincoli del progetto e ci si e' gia' sbagliati una volta: cercare
        /// per nome trova *un* processo, non *il* processo a cui questo ponte
        /// parla, e un controllo che guarda il processo sbagliato risponde
        /// «vivo» mentre il proprio e' morto. E' il difetto che non da' errore,
        /// in un controllo scritto per prenderlo.
        ///
        /// SI FA A RIFLESSIONE e si accetta di non riuscirci: `_sw` e' tenuto
        /// come `object` apposta, e se `GetProcessID` non c'e' su questa
        /// versione dell'interfaccia il ponte deve funzionare **come prima**,
        /// non rifiutare. Un controllo nuovo che diventa una nuova causa di
        /// rifiuto e' un peggioramento, non una difesa.
        /// </summary>
        private void RicordaIlProcesso()
        {
            _pid = 0;
            // Un nuovo aggancio cancella il ricordo del vecchio: e' proprio la
            // cosa che si fa dopo aver riavviato SOLIDWORKS.
            _motivoDellaPerdita = null;
            if (_sw == null) return;
            try
            {
                var v = Chiama(_sw, "GetProcessID");
                if (v != null) _pid = Convert.ToInt32(v);
            }
            catch
            {
                // Volutamente muto: si dice una volta sola, e piu' sotto, che
                // il controllo non e' disponibile. Gridarlo a ogni aggancio
                // insegnerebbe a ignorarlo.
            }
            if (_pid <= 0)
                Mcp.Protocollo.Nota(
                    "[ponte] non sono riuscito a sapere il processo di SolidWorks: " +
                    "se si chiude a meta' di un piano, me ne accorgero' dall'errore " +
                    "della chiamata e non prima.");
        }

        /// <summary>
        /// SOLIDWORKS e' ancora li'? Torna `null` se si', il motivo se no.
        ///
        /// Va chiamata PRIMA di un piano e FRA un passo e l'altro. Senza, un
        /// SOLIDWORKS che si chiude a meta' si manifesta come un riferimento
        /// nullo tre chiamate dopo, in un punto che non ha niente a che vedere
        /// con la causa — e chi legge l'errore cerca il difetto dove non e'.
        ///
        /// **Quando il PID non si e' potuto sapere, questa risponde `null`**,
        /// cioe' «per quanto ne so, vivo». Non e' una svista: il controllo che
        /// non c'e' non deve diventare un rifiuto. Che non ci sia e' gia' stato
        /// detto all'aggancio.
        /// </summary>
        public string SolidWorksECheSeNeEAndato()
        {
            // UNA VOLTA CHE SE N'E' ANDATO, RESTA ANDATO.
            // Il motivo si tiene e si ripete: dalla seconda chiamata in poi non
            // si va a ricontrollare un processo che non c'e', e soprattutto non
            // si degrada il messaggio.
            if (_motivoDellaPerdita != null) return _motivoDellaPerdita;
            if (_pid <= 0) return null;
            try
            {
                var p = System.Diagnostics.Process.GetProcessById(_pid);
                if (p == null || p.HasExited) return Sparito();
                return null;
            }
            catch (ArgumentException)
            {
                // Process.GetProcessById alza ArgumentException quando il
                // processo non esiste piu'. E' il caso che stiamo cercando.
                return Sparito();
            }
            catch
            {
                // Qualunque altro guaio (permessi, WMI che non risponde) NON e'
                // una prova che SOLIDWORKS sia morto: si tace.
                return null;
            }
        }

        private string _motivoDellaPerdita;

        /// <summary>
        /// NON si azzera `_sw` qui, ed e' una scelta.
        ///
        /// Azzerarlo sembrava igiene — l'oggetto COM e' morto comunque — ma
        /// rende `Agganciata` falsa, e allora `disegna` risponde «Il ponte non
        /// e' agganciato a SolidWorks»: vero, generico, e **peggiore** del
        /// messaggio che questa funzione ha appena costruito. Si sarebbe
        /// sostituita la causa con un sintomo, un giro dopo averla trovata.
        /// </summary>
        private string Sparito()
        {
            _motivoDellaPerdita =
                "SolidWorks si e' chiuso: il processo a cui ero agganciato " +
                "(PID " + _pid + ") non c'e' piu'. Quello che era gia' stato " +
                "costruito e' rimasto nel documento e NON e' stato salvato. " +
                "Riapri SolidWorks, riapri il pezzo, e rilancia il ponte.";
            return _motivoDellaPerdita;
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
        /// <summary>
        /// Lo stato del documento, ancora compresa: e' la lettura da cui si
        /// costruiscono le FIRME dei passi.
        /// </summary>
        public Dictionary<string, object> Stato(out string errore)
        {
            return Stato(true, out errore);
        }

        /// <summary>
        /// Lo stato del documento SENZA l'ancora.
        ///
        /// L'ancora costa: un `GetTessTriangles` e un baricentro pesato per
        /// area su OGNI faccia. Su un telaio saldato sono decine di chiamate
        /// COM per un dato che serve a una cosa sola — corroborare il
        /// ritrovamento di una faccia — e quindi solo a chi costruisce le
        /// firme.
        ///
        /// Fino alla 0.25.2 il flag era cablato a `true` e la si pagava anche
        /// per la lettura «prima» di `disegna`, che serve solo a sapere su
        /// quale documento si lavora, quanto volume c'era e quante lavorazioni
        /// erano in errore: tre cose che l'ancora non riguarda. Quello stato
        /// non torna nemmeno a chi legge.
        ///
        /// Il nome e' diverso invece di essere un parametro in piu' sullo
        /// stesso metodo: e' la lezione di `LeggiConAncora` — cambiare
        /// l'insieme delle firme di un metodo rompe chi lo chiama per
        /// riflessione, anche quando il compilatore non dice niente.
        /// </summary>
        public Dictionary<string, object> StatoSenzaAncora(out string errore)
        {
            return Stato(false, out errore);
        }

        /// <summary>
        /// THE STATE OF A GIVEN DOCUMENT, not of the window in front (Z-C21.14).
        ///
        /// After a plan the document in front may be another one: a
        /// `crea_disegno` puts its drawing in front, another session or the
        /// person may bring a window forward. The live probe M19 (1/10/2026,
        /// line BRIDGE-R1 (X2)) measured what reading the front window did to
        /// «annulla»: the plan's part was compared with the numbers of the
        /// other document and a right undo was refused. This reads the plan's
        /// own document, by the reference the executor kept. Without the
        /// anchor: the caller wants the volume, the feature count and the
        /// identity, not signatures.
        /// </summary>
        public Dictionary<string, object> StateOfDocument(object document, out string errore)
        {
            errore = null;
            var model = document as IModelDoc2;
            if (model == null)
            {
                errore = "The document of the plan is not held any more.";
                return null;
            }
            try
            {
                var stato = LeggiStato(model, false);
                return stato?.Dati;
            }
            catch (Exception ex)
            {
                errore = "I cannot read the document of the plan: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Is <paramref name="document"/> the document in front? By the COM
        /// object (its IUnknown), not by name: a plan that saves its part
        /// changes the title and the path of the same document (Z-C21.14).
        /// </summary>
        public bool IsInFront(object document)
        {
            return SameComObject(document, DocumentoAttivo());
        }

        /// <summary>
        /// Are <paramref name="a"/> and <paramref name="b"/> the same COM
        /// object? By IUnknown, like the executor's StessoOggetto: the name of
        /// a document changes when it is saved, the object does not, and a
        /// document closed and opened again is another object with an empty
        /// undo stack (Z-C21.14). False when either is null.
        /// </summary>
        public static bool SameComObject(object a, object b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            IntPtr pa = IntPtr.Zero, pb = IntPtr.Zero;
            try
            {
                pa = Marshal.GetIUnknownForObject(a);
                pb = Marshal.GetIUnknownForObject(b);
                return pa == pb;
            }
            catch { return false; }
            finally
            {
                if (pa != IntPtr.Zero) Marshal.Release(pa);
                if (pb != IntPtr.Zero) Marshal.Release(pb);
            }
        }

        /// <summary>
        /// What the executor reported about the last plan (Z-C21.14): its
        /// document, the document it ended on and what FinishRecordingUndoObject
        /// answered for the plan's undo entry. Null when the last call to
        /// <see cref="EseguiPiano(List{PassoDelPiano}, out string, Mcp.Avanzamento.Filo, string)"/>
        /// stopped before an executor was made (SOLIDWORKS gone, no document,
        /// another bridge's turn): then nothing was written and no undo entry
        /// was recorded. When the executor threw, a result with nothing filled
        /// and <see cref="LastPlanExecutorThrew"/> true: what it wrote and
        /// recorded is not known.
        /// </summary>
        public EsecutorePiano.Risultato LastPlanResult { get; private set; }

        /// <summary>
        /// True when the executor of the last plan threw instead of returning
        /// its result (Z-C21.14, review F9). Its result is then empty: no plan
        /// document, no answer of FinishRecordingUndoObject.
        /// </summary>
        public bool LastPlanExecutorThrew { get; private set; }

        /// <summary>
        /// The document whose turn was refused to the last plan because another
        /// bridge process held it (Z-C21.14, review F4); null otherwise. At
        /// that moment another writer is known to be working on that document,
        /// and whatever it records lands on top of its undo stack.
        /// </summary>
        public object LastTurnRefusedDocument { get; private set; }

        private Dictionary<string, object> Stato(bool conAncora, out string errore)
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
                var stato = LeggiStato(doc, conAncora);
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
        private StatoDocumento LeggiStato(object doc, bool conAncora)
        {
            return StatoDocumento.LeggiConAncora(doc as IModelDoc2, _sw as ISldWorks, conAncora);
        }

        /// <summary>
        /// UNA FOTOGRAFIA del documento attivo, in PNG. Null col motivo.
        ///
        /// Sta qui e non in `Attrezzi` per la stessa ragione di `Stato`: e'
        /// `Sessione` che sa qual e' il documento davanti, e trovarlo e' la
        /// meta' difficile — il commento sopra `DocumentoAttivo` racconta
        /// quanto e' costata.
        ///
        /// Il nome e' nuovo e non un parametro in piu' su qualcosa che c'era:
        /// vedi `StatoSenzaAncora`, stessa regola, stessa pagina.
        /// </summary>
        public byte[] Fotografia(string quale, int larghezza, int altezza, out string errore)
        {
            errore = null;
            var doc = DocumentoAttivo();
            if (doc == null)
            {
                errore = "Non c'e' nessun documento aperto in SolidWorks.";
                return null;
            }

            var modello = doc as IModelDoc2;
            if (modello == null)
            {
                errore = "Il documento davanti non si lascia fotografare.";
                return null;
            }

            return VistaDelPezzo.Scatta(modello, quale, larghezza, altezza, out errore);
        }

        /// <summary>I nomi delle viste che si possono chiedere.</summary>
        public static IEnumerable<string> NomiDelleViste
        {
            get { return VistaDelPezzo.NomiDelleViste; }
        }

        /// <summary>I piani su cui si puo' sezionare.</summary>
        public static IEnumerable<string> NomiDeiPiani
        {
            get { return VistaDelPezzo.NomiDeiPiani; }
        }

        /// <summary>
        /// UNO SPACCATO del documento attivo, in PNG (voce V4). Null col
        /// motivo.
        ///
        /// Nome nuovo accanto a `Fotografia`, non parametri in piu' su quella:
        /// stessa regola di `StatoSenzaAncora`, e qui vale doppio perche'
        /// `Fotografia` e' gia' chiamata da `Attrezzi`.
        /// </summary>
        public byte[] Spaccato(
            string quale, string piano, double offsetMm,
            int larghezza, int altezza, out string errore)
        {
            errore = null;
            var doc = DocumentoAttivo();
            if (doc == null)
            {
                errore = "Non c'e' nessun documento aperto in SolidWorks.";
                return null;
            }

            var modello = doc as IModelDoc2;
            if (modello == null)
            {
                errore = "Il documento davanti non si lascia fotografare.";
                return null;
            }

            return VistaDelPezzo.ScattaInSezione(
                modello, quale, piano, offsetMm, larghezza, altezza, out errore);
        }

        /// <summary>
        /// UN PASSO INDIETRO sul documento davanti. Torna il motivo se non si
        /// e' potuto, null se e' andata.
        ///
        /// PERCHE' `EditUndo2` E NON `RunCommand`. Il comando di menu dipende
        /// dalla lingua dell'installazione e da cosa ha il fuoco; `EditUndo2`
        /// e' l'API, e fa la stessa cosa che fa `Ctrl+Z` su
        /// `Controlli\Annulla`, dove e' gia' provata.
        ///
        /// UN PASSO SOLO, E NON E' UN DETTAGLIO. `EsecutorePiano` apre e chiude
        /// UNA voce di annullamento intorno all'intero piano
        /// (`StartRecordingUndoObject` / `FinishRecordingUndoObject`): un passo
        /// indietro disfa il piano intero. E' il criterio di chiusura della
        /// voce B8, gia' misurato. Chiederne due vorrebbe dire disfare anche
        /// qualcosa che questo ponte non ha fatto.
        ///
        /// `EditUndo2` torna `void`: non dice se ha annullato. Chi chiama deve
        /// GUARDARE il documento dopo — che e' esattamente cio' che fa
        /// `Attrezzi.Annulla`, e l'unica ragione per cui questa operazione e'
        /// difendibile.
        ///
        /// AND A WRONG PRESS CANNOT BE TAKEN BACK when what it removed was an
        /// undo entry recorded through the API (see `UnPassoAvanti`, probe
        /// `redo` of 1/10/2026). That is why `Attrezzi.Annulla` refuses before
        /// pressing whenever it knows a foreign entry is on top (Z-C21.14).
        /// </summary>
        public string UnPassoIndietro()
        {
            var doc = DocumentoAttivo() as IModelDoc2;
            if (doc == null) return "Non c'e' nessun documento aperto in SolidWorks.";
            try { doc.EditUndo2(1); return null; }
            catch (Exception ex) { return "SOLIDWORKS non ha annullato: " + ex.Message; }
        }

        /// <summary>
        /// ONE STEP FORWARD: EditRedo2(1) on the document in front. Returns the
        /// reason when SOLIDWORKS threw, null when the call returned; EditRedo2
        /// is void, so null says only that it did not throw, never that
        /// anything came back.
        ///
        /// WHAT IT CAN AND CANNOT PUT BACK (measured, probe `redo`, 1/10/2026;
        /// RAG note 82zz_duodecies, section «La sonda viva di M19»): an
        /// ordinary API action that was not recorded (an extrusion made without
        /// Start/FinishRecordingUndoObject) is redone at once. An undo entry
        /// RECORDED through the API is never redone: not with EditRedo2(1) or
        /// (2), not with RunCommand(swCommands_Redo), not after EditRebuild3 or
        /// ForceRebuild3, with the obsolete Finish and with Finish2, first item
        /// or not, the plan executor's own entry included. Every plan of this
        /// bridge is such an entry. So this is not a way to take back a wrong
        /// undo: `Attrezzi.Annulla` calls it only after a press that took away
        /// something visible, and re-reads the part to say whether it came
        /// back, never assuming it.
        /// </summary>
        public string UnPassoAvanti()
        {
            var doc = DocumentoAttivo() as IModelDoc2;
            if (doc == null) return "Non c'e' nessun documento aperto in SolidWorks.";
            try { doc.EditRedo2(1); return null; }
            catch (Exception ex) { return "SOLIDWORKS non ha rifatto: " + ex.Message; }
        }

        /// <summary>
        /// IL PIANO INTERO, ESEGUITO DALLA LIBRERIA E NON DAL PONTE.
        ///
        /// Fino alla 0.14.0 il ponte aveva un giro suo: un
        /// <see cref="OperazioniSw"/> nuovo a ogni passo e `passiFatti` a
        /// `null`. Funzionava, e costava tre cose misurabili:
        ///
        ///   * **niente riferimenti fra passi.** Un piano di due passi in cui il
        ///     secondo diceva «la faccia in cima a quello che hai fatto al passo
        ///     1» falliva con «di quel passo non ho la geometria». Misurato il
        ///     14/9/2026: passo 1 riuscito, volume 120.000, passo 2 rifiutato;
        ///   * **niente voce di annullamento unica.** `Ctrl+Z` disfaceva un
        ///     passo solo;
        ///   * **una seconda implementazione dello stesso ciclo**, piu' povera
        ///     di quella che l'add-in usa tutti i giorni. Due implementazioni
        ///     sono due comportamenti che divergono, e le prove si fanno su una
        ///     sola delle due.
        ///
        /// Adesso il ciclo e' <see cref="EsecutorePiano"/>, lo stesso
        /// dell'add-in, e il ponte gli passa i passi attraverso
        /// <see cref="PianoInMemoria"/>.
        ///
        /// SUL FILO E SULL'ASINCRONO. Il ponte e' sincrono: legge una riga da
        /// standard input, risponde, ricomincia. Non c'e' nessun filo di
        /// SOLIDWORKS separato su cui accodare, quindi `sulThreadSw` esegue
        /// sul posto e `GetAwaiter().GetResult()` non puo' incastrarsi — tutte
        /// le attese si completano prima di essere attese.
        /// </summary>
        public List<Dictionary<string, object>> EseguiPiano(
            List<PassoDelPiano> passi, out string errore)
        {
            return EseguiPiano(passi, out errore, null, null);
        }

        public List<Dictionary<string, object>> EseguiPiano(
            List<PassoDelPiano> passi, out string errore,
            Mcp.Avanzamento.Filo filo)
        {
            return EseguiPiano(passi, out errore, filo, null);
        }

        /// <summary>
        /// The plan, on the document the caller has already checked (Z-C21.14,
        /// review F7). <paramref name="expectedDocument"/> is that document's
        /// identity in the executor's form (its path, or «titolo:» and its
        /// title when it has never been saved): the executor reads the active
        /// document once and stops before writing anything when it is not this
        /// one, instead of running on whatever window came to the front between
        /// the caller's reading and now. Null: the executor takes the active
        /// document, as before.
        /// </summary>
        public List<Dictionary<string, object>> EseguiPiano(
            List<PassoDelPiano> passi, out string errore,
            Mcp.Avanzamento.Filo filo, string expectedDocument)
        {
            errore = null;
            // Z-C21.14: what a plan before this one reported must not be read
            // as this plan's.
            LastPlanResult = null;
            LastPlanExecutorThrew = false;
            LastTurnRefusedDocument = null;

            // PRIMA DI TUTTO: c'e' ancora qualcuno dall'altra parte?
            //
            // Senza questa riga, un SOLIDWORKS chiuso dopo l'aggancio si
            // manifesta come `DocumentoAttivo()` che torna null, e il ponte
            // risponde «non c'e' nessun documento aperto» — che e' vero e manda
            // a cercare la cosa sbagliata: si va ad aprire un pezzo dentro un
            // SOLIDWORKS che non esiste piu'.
            errore = SolidWorksECheSeNeEAndato();
            if (errore != null) return null;

            var doc = DocumentoAttivo();
            if (doc == null)
            {
                errore = "Non c'e' nessun documento aperto in SolidWorks.";
                return null;
            }

            // ── UN PONTE ALLA VOLTA SU UN DOCUMENTO (voce W2.9) ─────────
            //
            // Il precedente e' gia' costato: due sessioni sullo stesso
            // SOLIDWORKS, e una prova finita dentro il pezzo dell'altra — un
            // blocco 314x274x52 estruso nel documento di qualcun altro, senza
            // un errore e senza un avviso. Li' si e' riparato tenendo il
            // riferimento `IModelDoc2` invece dell'attivo, e basta **dentro un
            // processo**. Fra due PROCESSI-ponte quel riferimento non dice
            // niente: sono due oggetti COM diversi che guardano lo stesso file.
            //
            // Qui il turno se lo prendono a vicenda con un mutex di sistema.
            using (var turno = TurnoSulDocumento.Prendi(doc, out string diChiE))
            {
                if (turno == null)
                {
                    errore =
                        "Un altro ponte sta gia' lavorando su questo documento (" +
                        diChiE + "). Non eseguo: due piani sullo stesso pezzo, " +
                        "uno sopra l'altro, danno un pezzo che non e' nessuno dei " +
                        "due e nessuno dei due se ne accorge. Aspetta che finisca, " +
                        "oppure lavora su un altro documento.";
                    // Another writer is on this document now (review F4).
                    LastTurnRefusedDocument = doc;
                    return null;
                }
                return EseguiIlPiano(passi, out errore, filo, expectedDocument);
            }
        }

        /// <summary>
        /// Il piano vero e proprio, una volta che il turno sul documento e'
        /// nostro.
        ///
        /// NON prende `doc`: `EsecutorePiano` lavora attraverso `_sw` e trova il
        /// documento da se'. Passarglielo sarebbe un parametro che dice il falso
        /// su cosa serve a questo metodo — e il primo che lo legge penserebbe
        /// che il documento arrivi di qui.
        ///
        /// It takes <paramref name="expectedDocument"/>, which is not the
        /// document but the identity the executor checks the active one
        /// against (Z-C21.14, review F7): the executor still finds the document
        /// itself, and refuses before writing when it is another one.
        /// </summary>
        private List<Dictionary<string, object>> EseguiIlPiano(
            List<PassoDelPiano> passi, out string errore,
            Mcp.Avanzamento.Filo filo, string expectedDocument)
        {
            errore = null;
            var fonte = new PianoInMemoria(passi, filo);
            var esecutore = new EsecutorePiano(
                _sw as ISldWorks,
                fonte,
                riga => Mcp.Protocollo.Nota("  " + riga),
                f => System.Threading.Tasks.Task.FromResult(f()))
            {
                // Il nome che l'utente legge nel menu «Annulla» di SOLIDWORKS.
                NomeAnnullamento = "Ponte MCP",

                // QUI SI', E SOLO QUI. Il ponte lavora apposta su parti appena
                // aperte e mai salvate: senza questo, il suo caso normale
                // sarebbe rifiutato con «salvalo su disco prima». Nell'add-in
                // resta falso, e c'e' un controllo che lo sorveglia.
                DocumentoSenzaFile = true,

                // E anche qui, e solo qui. Il catalogo del ponte porta cinque
                // operazioni di tavola: rifiutare le tavole renderebbe inutili
                // proprio quelle. L'assistente dell'add-in disegna pezzi, e li'
                // resta falso.
                AncheSuTavole = true,

                // E il terzo, per la stessa ragione esatta. Il catalogo del
                // ponte porta `inserisci_componente`, `accoppia`,
                // `sostituisci_componente` e `verifica_interferenze`: fino al
                // 18/9/2026 le ELENCAVA e l'esecutore le avrebbe rifiutate
                // prima del primo passo, senza che niente lo dicesse.
                // Il banco non poteva accorgersene: chiama la libreria, non il
                // ponte. Adesso c'e' un controllo che confronta le famiglie del
                // catalogo coi tipi ammessi — `Prove\Programma.cs` →
                // `OgniFamigliaDelCatalogoHaUnDocumentoAmmesso`.
                AncheSuAssiemi = true,
            };

            try
            {
                // Until the executor returns, what it recorded is unknown
                // (Z-C21.14): a throw below leaves this result, not null, and
                // LastPlanExecutorThrew says so.
                LastPlanResult = new EsecutorePiano.Risultato();
                LastPlanExecutorThrew = true;
                var esito = esecutore
                    .EseguiAsync(string.IsNullOrEmpty(expectedDocument) ? null : expectedDocument)
                    .GetAwaiter()
                    .GetResult();
                LastPlanResult = esito;
                LastPlanExecutorThrew = false;

                // L'ERRORE SI RIPORTA ANCHE QUANDO GLI ESITI LO DICONO GIA'.
                // Un piano fermato PRIMA del primo passo — documento sbagliato,
                // controlli preliminari — non lascia nessun esito, e senza
                // questo il ponte risponderebbe «nessun passo, tutto bene».
                if (!esito.Completato && !string.IsNullOrEmpty(esito.Errore))
                    errore = NominaLaCausaVera(esito.Errore, fonte.Esiti);
                return fonte.Esiti;
            }
            catch (TargetInvocationException ex)
            {
                errore = NominaLaCausaVera((ex.InnerException ?? ex).Message, fonte.Esiti);
                return fonte.Esiti;
            }
            catch (Exception ex)
            {
                errore = NominaLaCausaVera(ex.Message, fonte.Esiti);
                return fonte.Esiti;
            }
        }

        /// <summary>
        /// Se il piano si e' fermato E nel frattempo SOLIDWORKS e' sparito, la
        /// causa e' quella — non il messaggio della chiamata che ha trovato il
        /// vuoto.
        ///
        /// PERCHE' SI GUARDA DOPO E NON SOLO PRIMA. Il controllo all'inizio
        /// prende il caso «era gia' chiuso»; questo prende quello che conta di
        /// piu': **chiuso a meta' piano**. Li' l'errore che arriva su e' quello
        /// della prima chiamata che ha trovato il riferimento morto — un
        /// «Riferimento a un oggetto non impostato», o un HRESULT — e chi lo
        /// legge va a cercare un difetto nell'operazione che stava girando.
        ///
        /// Il messaggio originale NON si butta: si dice tutt'e due le cose,
        /// perche' se un giorno le due non combaciassero — SOLIDWORKS vivo e
        /// un errore vero — buttare l'originale avrebbe nascosto la cosa da
        /// riparare. E si dice **quanti passi erano gia' andati**, che e' cio'
        /// che serve per sapere com'e' rimasto il documento.
        /// </summary>
        private string NominaLaCausaVera(
            string originale, List<Dictionary<string, object>> esiti)
        {
            var sparito = SolidWorksECheSeNeEAndato();
            if (sparito == null) return originale;

            // ZERO-E-ZERO: `esiti` e' la nostra lista, non una lettura dal CAD.
            int fatti = esiti == null ? 0 : esiti.Count;
            return sparito +
                   " Si e' fermato dopo " + fatti +
                   (fatti == 1 ? " passo" : " passi") +
                   ". Quello che SolidWorks aveva risposto per ultimo: «" +
                   originale + "».";
        }
    }
}
