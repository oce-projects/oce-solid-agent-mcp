using System.Reflection;
using System.Runtime.InteropServices;

// Riscontro.Disegno — le operazioni di disegno per SOLIDWORKS, senza il PDM.
//
// LA VERSIONE SI ALZA A OGNI MODIFICA, come per l'add-in e per il ponte, e per
// la stessa ragione: questa libreria serve a MISURARE, e una misura senza il
// numero di versione di cio' che l'ha prodotta non e' una misura, e' un
// aneddoto.
//
// Parte da 0.52.0 e non da 0.1.0: e' lo stesso codice dell'add-in OpenPDM alla
// 0.52.0, spostato. Ricominciare da capo la numerazione farebbe sembrare nuovo
// cio' che ha gia' un anno di misure alle spalle, e renderebbe impossibile dire
// «questa prova gira sulla stessa versione di allora».
[assembly: AssemblyTitle("Riscontro.Disegno")]
[assembly: AssemblyDescription(
    "Operazioni di disegno per SOLIDWORKS che riportano cos'e' venuto, non cos'e' stato chiesto.")]
[assembly: AssemblyProduct("Riscontro")]
[assembly: AssemblyCulture("")]

// Non e' visibile a COM: la usa il ponte, non SOLIDWORKS.
[assembly: ComVisible(false)]

// 0.94.1: **solo commenti.** La regola dice che ogni modifica alla libreria
// alza la versione, e non fa eccezioni per i commenti — perche' «sono solo
// commenti» e' esattamente quello che si dice un attimo prima che non lo
// fossero. Qui e' vero (due blocchi di documentazione in
// `OperazioniSostituisci.cs`, riscritti perche' dicevano una cosa che il banco
// ha poi smentito), e il numero si muove lo stesso.
// 0.95.0: `croci_di_centro` (operazione 64) e le croci nello stato della tavola.
// 0.96.0: l'esploso nello stato dell'assieme, col millimetro accanto al booleano.
// 0.97.0: `nuovo_foglio` e `attiva_foglio` (voce GT1), col diedro riletto.
// 0.98.0: `cartiglio` (voce GT3) e `verifica_interferenze` (GA10); e
//         `AncheSuAssiemi`, perche' l'esecutore rifiutava ogni assieme mentre
//         il catalogo ne prometteva quattro operazioni.
// 0.98.1: il rifiuto di `quote_dal_modello` dice quante quote sono MARCATE,
//         non solo quante ce ne sono: senza quel dato la diagnosi resta divisa
//         in due, e il 18/9 c'e' voluta una sonda apposta per dividerla.
// 0.98.2: i sei contatori di annotazione per vista (voce Z-E1), piu' i sei del
//         FOGLIO a parte — perche' `InsertNote` scrive li', e contando solo le
//         viste `note` sarebbe un campo che non puo' diventare vero.
// 0.98.3: `cartiglio` distingue «la proprieta' non c'e'» da «c'e' sul modello e
//         il cartiglio la mostra vuota» — due diagnosi opposte che prima
//         finivano nello stesso elenco, e mandavano a riparare la cosa
//         sbagliata.
// 0.98.4: `viste_standard` non sorteggia piu' il pezzo. Metteva in tavola le
//         viste del PRIMO documento aperto — un ordine che nessuno dichiara — e
//         con piu' di un pezzo aperto faceva la tavola di un altro pezzo senza
//         dare nessun errore. Adesso: uno solo, quello; due o piu', rifiuta.
// 0.98.5: `salva_documento` guarda PERCHE' non ha salvato invece di riportare
//         un codice: la cartella che non esiste e il file in sola lettura si
//         vedono da qui, e il codice di `SaveAs` non li dice.
// 0.99.0: `quota_in_tavola` (voce K10) — LE QUOTE IN TAVOLA SI CREANO.
//         `InsertModelAnnotations*` non ne porta nessuna fuori processo, ma
//         `AddDimension` sugli spigoli della vista si', e coi valori giusti.
//         Ogni quota viene RILETTA: risponde quanto misura, non «fatto».
// 0.100.0: `nota_in_tavola` (voce E2-bis) — una nota di testo sul foglio.
//          `InsertNote` risponde, `InsertNewNote3` e' `Void` e non fa niente.
//          Le note si contano SUL FOGLIO e non nelle viste: contarle nel posto
//          sbagliato avrebbe letto 0 con la nota al suo posto, e quel falso
//          negativo avrebbe chiuso per sbaglio quattro voci del TODO.
// 0.101.0: le quote non si lasciano piu' ammucchiate sulla vista. Il punto in
//          cui una quota finisce si calcola dallo spigolo: `GetCurveParams3`
//          da' i due estremi nel modello, `ModelToViewTransform` li porta sul
//          foglio, e SOLO ALLORA si somma `IView.Position` -- senza quella
//          somma le quote cadevano tutte a x=0. Orizzontali sopra, verticali
//          di lato (UNI ISO 129-1: linee di misura fuori dalle figure), in
//          ordine di lunghezza crescente allontanandosi dal pezzo, distacco
//          12 mm e passo 10 mm. Doppioni tolti per GEOMETRIA e non per
//          valore: due gradini da 10 mm sono due elementi, e vanno quotati
//          tutti e due.
[assembly: AssemblyVersion("0.101.0.0")]
[assembly: AssemblyFileVersion("0.101.0.0")]
// Era rimasta a 0.61.0 mentre l'assembly andava a 0.66.0: un numero che
// nessuno legge e che dice il falso a chi apra le proprieta' del file. Non
// serviva a niente ed era sbagliato, che e' la combinazione peggiore.
//
// ED E' RISUCCESSO: il 22/9/2026 diceva 0.94.0 con l'assembly a 0.99.0, cioe'
// cinque versioni indietro. La riga sopra racconta il difetto e non lo ha
// impedito — perche' un commento non e' un controllo. Finche' nessuna prova
// confronta i due numeri, questo terzo lo si allinea a mano e prima o poi
// invecchia di nuovo.
[assembly: AssemblyInformationalVersion("0.101.0")]
