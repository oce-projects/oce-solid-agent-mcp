// Sostituisce il percorso di scratchpad di sessione col segnaposto {BANCO}
// dentro l'albero di lavoro corrente. Serve a `git filter-branch --tree-filter`,
// che lo esegue una volta per ogni commit riscritto.
//
// Perche' esiste: due commit non ancora spinti portavano il nome utente dentro
// un percorso `C:\Users\<nome>\AppData\Local\Temp\claude\<sessione>\scratchpad`.
// L'albero di lavoro era gia' stato riparato, la storia no, e
// `check-publishability.ps1` guardava solo l'albero: diceva «Pulito».
//
// Vale la stessa forma della riparazione dei casi: non si cancella il percorso,
// si mette il segnaposto che `run_plans.js` risolve in `Bench/lavoro/`.

var fs = require('fs');
var path = require('path');

// Il percorso di una cartella temporanea di sessione, nelle tre grafie in cui
// compare: separatore singolo (stringhe verbatim C#), doppio (JSON con escape)
// e barra dritta. Questo schema non nomina niente di aziendale, quindi sta
// qui.
var PATH = /C:[\\\/]+Users[\\\/]+[^\\\/"\s]+[\\\/]+AppData[\\\/]+Local[\\\/]+Temp[\\\/]+claude[\\\/]+[^\\\/"\s]+[\\\/]+[^\\\/"\s]+[\\\/]+scratchpad/gi;

// ── GLI SCHEMI DI QUESTO IMPIANTO STANNO FUORI DAL REPOSITORY ─────────────
//
// UNO STRUMENTO CHE TOGLIE UNA STRINGA DEVE NOMINARLA, ed e' un cortocircuito:
// il 23/9/2026 questa bonifica ha ripulito 140 commit da due cose interne — il
// nome di un server GPU aziendale e una cartella di rete — e a riscrittura
// finita la ricerca su tutta la storia ne trovava ancora una occorrenza per
// ciascuna, in un posto solo: **qui dentro**. La cura portava la malattia.
//
// Quindi gli schemi specifici di un impianto si mettono in
// `Checks/bonifica-schemi.local.js`, che NON e' versionato (.gitignore).
// Il meccanismo resta pubblico e utile a chiunque; i nomi di casa no.
//
// Il file locale esporta un elenco di coppie:
//     module.exports = [ { schema: /.../g, con: 'testo di ricambio' }, ... ];
// Se non c'e', si applica il solo PERCORSO e non e' un errore: un clone pulito
// non ha niente di aziendale da togliere.
var LOCALS = [];
try {
    LOCALS = require(path.join(__dirname, 'bonifica-schemi.local.js')) || [];
} catch (e) { /* non c'e': va bene cosi' */ }

var EXTENSIONS = ['.cs', '.json', '.md', '.ps1', '.js', '.csproj', '.txt', '.config', '.manifest'];
var SKIP = ['.git', 'bin', 'obj', 'node_modules'];

var touched = 0;

function benchRun(folder) {
    var entries;
    try { entries = fs.readdirSync(folder, { withFileTypes: true }); } catch (e) { return; }
    for (var i = 0; i < entries.length; i++) {
        var v = entries[i];
        var full = path.join(folder, v.name);
        if (v.isDirectory()) {
            if (SKIP.indexOf(v.name) >= 0) continue;
            benchRun(full);
            continue;
        }
        if (!v.isFile()) continue;
        if (EXTENSIONS.indexOf(path.extname(v.name).toLowerCase()) < 0) continue;

        var text;
        try { text = fs.readFileSync(full, 'utf8'); } catch (e) { continue; }

        // NESSUN FILTRO VELOCE. La prima stesura usciva subito se il file non
        // conteneva «AppData»: con l'aggiunta di schemi nuovi avrebbe saltato
        // proprio i file da riparare — un filtro di velocita' che diventa un
        // filtro di correttezza, in silenzio. Su qualche centinaio di file la
        // differenza non si misura; su un difetto che non da' errore si'.
        var newItem = text.replace(PATH, '{BANCO}');
        for (var k = 0; k < LOCALS.length; k++)
            newItem = newItem.replace(LOCALS[k].schema, LOCALS[k].con);
        if (newItem === text) continue;

        fs.writeFileSync(full, newItem, 'utf8');
        touched++;
    }
}

benchRun(process.cwd());

// Muto di proposito: `filter-branch` esegue questo comando per ogni commit e
// stampare qui renderebbe illeggibile l'uscita. Il conteggio si verifica dopo,
// sulla storia riscritta.
process.exit(0);
