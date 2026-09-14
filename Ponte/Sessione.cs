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
        /// </summary>
        public string UnPassoIndietro()
        {
            var doc = DocumentoAttivo() as IModelDoc2;
            if (doc == null) return "Non c'e' nessun documento aperto in SolidWorks.";
            try { doc.EditUndo2(1); return null; }
            catch (Exception ex) { return "SOLIDWORKS non ha annullato: " + ex.Message; }
        }

        /// <summary>
        /// UN PASSO AVANTI: rimette quello che `UnPassoIndietro` ha tolto.
        ///
        /// Serve a una cosa sola, e non e' una comodita': se l'annullamento ha
        /// disfatto qualcosa di DIVERSO da quello che doveva — perche' dopo il
        /// piano qualcuno ha fatto un gesto che e' finito in cima alla pila —
        /// questo lo rimette a posto. Non c'e' nessuna API per guardare la pila
        /// degli annullamenti prima di premere: l'unica strada onesta e'
        /// provare, controllare, e disfare la prova se era quella sbagliata.
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
            errore = null;
            var doc = DocumentoAttivo();
            if (doc == null)
            {
                errore = "Non c'e' nessun documento aperto in SolidWorks.";
                return null;
            }

            var fonte = new PianoInMemoria(passi);
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
            };

            try
            {
                var esito = esecutore
                    .EseguiAsync(null)
                    .GetAwaiter()
                    .GetResult();

                // L'ERRORE SI RIPORTA ANCHE QUANDO GLI ESITI LO DICONO GIA'.
                // Un piano fermato PRIMA del primo passo — documento sbagliato,
                // controlli preliminari — non lascia nessun esito, e senza
                // questo il ponte risponderebbe «nessun passo, tutto bene».
                if (!esito.Completato && !string.IsNullOrEmpty(esito.Errore))
                    errore = esito.Errore;
                return fonte.Esiti;
            }
            catch (TargetInvocationException ex)
            {
                errore = (ex.InnerException ?? ex).Message;
                return fonte.Esiti;
            }
            catch (Exception ex)
            {
                errore = ex.Message;
                return fonte.Esiti;
            }
        }
    }
}
