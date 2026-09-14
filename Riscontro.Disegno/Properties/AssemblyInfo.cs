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

[assembly: AssemblyVersion("0.63.0.0")]
[assembly: AssemblyFileVersion("0.63.0.0")]
[assembly: AssemblyInformationalVersion("0.61.0")]
