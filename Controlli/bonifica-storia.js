// Sostituisce il percorso di scratchpad di sessione col segnaposto {BANCO}
// dentro l'albero di lavoro corrente. Serve a `git filter-branch --tree-filter`,
// che lo esegue una volta per ogni commit riscritto.
//
// Perche' esiste: due commit non ancora spinti portavano il nome utente dentro
// un percorso `C:\Users\<nome>\AppData\Local\Temp\claude\<sessione>\scratchpad`.
// L'albero di lavoro era gia' stato riparato, la storia no, e
// `controlla-pubblicabilita.ps1` guardava solo l'albero: diceva «Pulito».
//
// Vale la stessa forma della riparazione dei casi: non si cancella il percorso,
// si mette il segnaposto che `corri_piani.js` risolve in `Banco/lavoro/`.

var fs = require('fs');
var path = require('path');

// {BANCO}
// nelle tre grafie in cui compare: separatore singolo (stringhe verbatim C#),
// doppio (JSON con escape) e barra dritta.
var PERCORSO = /C:[\\\/]+Users[\\\/]+[^\\\/"\s]+[\\\/]+AppData[\\\/]+Local[\\\/]+Temp[\\\/]+claude[\\\/]+[^\\\/"\s]+[\\\/]+[^\\\/"\s]+[\\\/]+scratchpad/gi;

// ── AGGIUNTE IL 23/9/2026: due cose interne che erano GIA' SU origin/main ──
//
// Trovate dall'audit di pubblicabilita' e verificate una per una. Finche' il
// repository e' privato si riscrive e si force-pusha; un minuto dopo essere
// diventato pubblico quei commit sono clonabili da chiunque e riscrivere non
// serve piu' a niente.
//
// 1. IL NOME DI UN SERVER GPU AZIENDALE, dentro un commento che spiega perche'
//    una prova e' scritta cosi'. Il commento serve; il nome della macchina no.
//    Si sostituisce la FRASE, non la parola: «<server>» da sola e' un nome comune
//    e cancellarlo ovunque rovinerebbe testi che non c'entrano.
var SERVER_GPU = /\bsu un server GPU interno \(([^)]*)\)/g;

// 2. UNA CARTELLA DI RETE INTERNA, con dentro il nome del grafo RAG. Compare
//    in due righe del TODO: una nota di flusso di lavoro e una citazione
//    bibliografica. Il segnaposto tiene in piedi la frase — si capisce ancora
//    che c'e' un archivio di note — senza dire dove.
//    Il separatore finale NON si mangia: lasciandolo, «...\archivio\reference\»
//    diventa «<archivio di note interno>\reference\» e la riga resta leggibile
//    come un percorso. Mangiandolo diventava «...internoreference\», che si
//    legge come un refuso e manda a cercare un errore di battitura.
var CARTELLA_RAG = /O:[\\\/]+CARTELLA_INTERNA[\\\/]+archivio/gi;

var ESTENSIONI = ['.cs', '.json', '.md', '.ps1', '.js', '.csproj', '.txt', '.config', '.manifest'];
var SALTA = ['.git', 'bin', 'obj', 'node_modules'];

var toccati = 0;

function giro(cartella) {
    var voci;
    try { voci = fs.readdirSync(cartella, { withFileTypes: true }); } catch (e) { return; }
    for (var i = 0; i < voci.length; i++) {
        var v = voci[i];
        var pieno = path.join(cartella, v.name);
        if (v.isDirectory()) {
            if (SALTA.indexOf(v.name) >= 0) continue;
            giro(pieno);
            continue;
        }
        if (!v.isFile()) continue;
        if (ESTENSIONI.indexOf(path.extname(v.name).toLowerCase()) < 0) continue;

        var testo;
        try { testo = fs.readFileSync(pieno, 'utf8'); } catch (e) { continue; }

        // IL FILTRO VELOCE DEVE NOMINARE TUTTE E TRE LE COSE. La prima
        // stesura usciva subito se il file non conteneva «AppData», e con
        // l'aggiunta dei due schemi nuovi avrebbe saltato proprio i file da
        // riparare — un filtro di velocita' che diventa un filtro di
        // correttezza, in silenzio.
        if (testo.indexOf('AppData') < 0 &&
            testo.indexOf('<server>') < 0 &&
            testo.indexOf('CARTELLA_INTERNA') < 0) continue;

        var nuovo = testo
            .replace(PERCORSO, '{BANCO}')
            .replace(SERVER_GPU, 'su un server GPU interno ($1)')
            .replace(CARTELLA_RAG, '<archivio di note interno>');
        if (nuovo === testo) continue;

        fs.writeFileSync(pieno, nuovo, 'utf8');
        toccati++;
    }
}

giro(process.cwd());

// Muto di proposito: `filter-branch` esegue questo comando per ogni commit e
// stampare qui renderebbe illeggibile l'uscita. Il conteggio si verifica dopo,
// sulla storia riscritta.
process.exit(0);
