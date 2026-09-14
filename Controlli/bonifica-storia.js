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
        if (testo.indexOf('AppData') < 0) continue;

        var nuovo = testo.replace(PERCORSO, '{BANCO}');
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
