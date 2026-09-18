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
[assembly: AssemblyVersion("0.98.1.0")]
[assembly: AssemblyFileVersion("0.98.1.0")]
// Era rimasta a 0.61.0 mentre l'assembly andava a 0.66.0: un numero che
// nessuno legge e che dice il falso a chi apra le proprieta' del file. Non
// serviva a niente ed era sbagliato, che e' la combinazione peggiore.
[assembly: AssemblyInformationalVersion("0.94.0")]
