// LA RINOMINA DEL PRODOTTO (voce I1).
//
// Da `SolidWorksMcp` a `Oce.Solid.Agent`, eseguibile `oce-solid-agent-mcp.exe`.
//
// PERCHE' NON BASTA RINOMINARE IL REPOSITORY. Il marchio intero di Dassault
// oggi non sta solo nell'URL: sta nel RootNamespace, nell'AssemblyName, nel
// nome dell'eseguibile che finisce nella configurazione di ogni client MCP,
// nel payload dell'installatore e nel manifest. Chi scarica il pacchetto si
// ritrova `SolidWorksMcp.exe` sul disco.
//
// PERCHE' UNO SCRIPT E NON VENTOTTO MODIFICHE A MANO. Perche' si rifa'
// identico se si sbaglia, perche' si legge cosa ha toccato, e perche' una
// sostituzione fatta a mano su ventotto file ne dimentica uno -- e quello che
// dimentica e' esattamente quello che nessuno riguardera' piu'.
//
// COSA NON SI TOCCA, e sono decisioni:
//
//   * `CHANGELOG.md`: not touched by this script, which removes a trademark.
//     The historical entries are translated too, in W3.1-bis.10 (decision D2
//     of 28 September 2026).
//   * `Riscontro.Disegno`: not touched by this script, which removes a
//     trademark. The project and its folder get an English name in
//     W3.1-bis.6 (decision D6 of 28 September 2026).
//   * i binari (`bin/`, `dist/`, `Installer/Payload/`): si rigenerano
//     compilando.
//
//   node Controlli/rinomina-prodotto.js            dice cosa farebbe
//   node Controlli/rinomina-prodotto.js --scrivi   lo fa

var fs = require('fs');
var path = require('path');

var RADICE = path.resolve(__dirname, '..');
var SCRIVI = process.argv.indexOf('--scrivi') >= 0;

// In quest'ordine: le forme piu' lunghe per prime, o una sostituzione corta
// mangia il pezzo di una lunga e il risultato non e' piu' riparabile.
var CAMBI = [
    // namespace e using
    ['SolidWorksMcp.Installer', 'Oce.Solid.Agent.Installer'],
    ['SolidWorksMcp.Strumenti', 'Oce.Solid.Agent.Strumenti'],
    ['SolidWorksMcp.Ponte', 'Oce.Solid.Agent.Ponte'],
    ['SolidWorksMcp.Mcp', 'Oce.Solid.Agent.Mcp'],
    // l'eseguibile, col nome del repository: e' quello che finisce nella
    // configurazione del client MCP e sul disco di chi installa
    ['SolidWorksMcp.exe', 'oce-solid-agent-mcp.exe'],
    ['SolidWorksMcp.csproj', 'Oce.Solid.Agent.csproj'],
    // il resto: nome del prodotto, RootNamespace, AssemblyName
    ['SolidWorksMcp', 'Oce.Solid.Agent'],

    // LE GRAFIE SEPARATE, che la prima passata non aveva preso ed erano le piu'
    // visibili di tutte:
    //
    //   Installer\Passi.cs   NomeProdotto, cioe' quello che legge chi installa
    //   NOTICE riga 1        la prima riga delle note legali
    //   Programma.cs         il nome che il server dichiara SUL PROTOCOLLO MCP,
    //                        cioe' quello che vede il client
    //   README.md riga 1     il titolo
    //
    // Cercare una sola grafia e credere di aver finito e' lo stesso errore del
    // percorso di scratchpad, che aveva quattro forme e se ne provavano due.
    ['SolidWorks MCP', 'OCE Solid Agent'],
    ['solidworks-mcp', 'oce-solid-agent'],
];

// ATTENZIONE: la parola `SolidWorks` DA SOLA non si tocca mai. E' il nome del
// programma con cui questo software parla, e nominarlo per dire con cosa si e'
// interoperabili e' uso lecito e necessario. Qui spariscono solo le forme in
// cui il marchio faceva da NOME DEL PRODOTTO.

// Estensioni che si toccano. I binari no: si rigenerano compilando.
// Anche i file SENZA estensione: `NOTICE` e `LICENSE` non ne hanno una, e la
// prima riga del NOTICE portava il nome del prodotto col marchio dentro. Una
// lista di estensioni che dimentica i file che non ne hanno e' lo stesso buco
// gia' trovato nella guardia di pubblicabilita'.
var TESTO = ['.cs', '.csproj', '.js', '.ps1', '.md', '.json', '.manifest', '.config', '.txt', ''];

// File che NON si toccano, uno per uno e con la ragione accanto.
var INTOCCABILI = {
    'CHANGELOG.md': 'le voci storiche dicono cosa si chiamava allora',
    'ultimo-giro.txt': 'uscita del banco, si rigenera',
    'ultimo-ponte.txt': 'uscita del banco, si rigenera',
    'rinomina-prodotto.js': 'e\' questo script, e nomina i vietati di mestiere',
};

var SALTA_CARTELLE = ['.git', 'bin', 'obj', 'dist', 'node_modules', 'lavoro', 'Payload'];

var toccati = [];

function giro(cartella) {
    var voci;
    try { voci = fs.readdirSync(cartella, { withFileTypes: true }); } catch (e) { return; }
    for (var i = 0; i < voci.length; i++) {
        var v = voci[i];
        var pieno = path.join(cartella, v.name);
        if (v.isDirectory()) {
            if (SALTA_CARTELLE.indexOf(v.name) >= 0) continue;
            giro(pieno);
            continue;
        }
        if (!v.isFile()) continue;
        if (TESTO.indexOf(path.extname(v.name).toLowerCase()) < 0) continue;
        if (INTOCCABILI[v.name]) continue;

        var testo;
        try { testo = fs.readFileSync(pieno, 'utf8'); } catch (e) { continue; }

        // SI CERCA OGNI FORMA, NON LA PRIMA.
        //
        // Qui c'era `if (testo.indexOf('SolidWorksMcp') < 0) continue;`, e
        // saltava i file che avevano SOLO le grafie separate -- cioe' proprio
        // il NOTICE, il titolo del README e il nome del prodotto
        // nell'installatore. Un filtro d'ingresso piu' stretto dell'elenco
        // delle sostituzioni e' un modo di non fare meta' del lavoro senza
        // dirlo: il conteggio finale usciva zero e sembrava «niente da fare».
        var haQualcosa = false;
        for (var k = 0; k < CAMBI.length; k++)
            if (testo.indexOf(CAMBI[k][0]) >= 0) { haQualcosa = true; break; }
        if (!haQualcosa) continue;

        var nuovo = testo;
        var quante = 0;
        for (var c = 0; c < CAMBI.length; c++) {
            var pezzi = nuovo.split(CAMBI[c][0]);
            if (pezzi.length > 1) {
                quante += pezzi.length - 1;
                nuovo = pezzi.join(CAMBI[c][1]);
            }
        }
        if (nuovo === testo) continue;

        toccati.push({ file: path.relative(RADICE, pieno), quante: quante });
        if (SCRIVI) fs.writeFileSync(pieno, nuovo, 'utf8');
    }
}

giro(RADICE);

var totale = 0;
for (var i = 0; i < toccati.length; i++) {
    console.log('  ' + String(toccati[i].quante).padStart(3) + '  ' + toccati[i].file);
    totale += toccati[i].quante;
}
console.log('');
console.log(toccati.length + ' file, ' + totale + ' sostituzioni' +
    (SCRIVI ? ' SCRITTE' : ' (prova a vuoto: aggiungi --scrivi)'));

console.log('');
console.log('Non toccati apposta:');
Object.keys(INTOCCABILI).forEach(function (f) {
    console.log('  ' + f.padEnd(24) + INTOCCABILI[f]);
});
console.log('');
console.log('Il file SolidWorksMcp.csproj va rinominato a parte, con git mv.');
