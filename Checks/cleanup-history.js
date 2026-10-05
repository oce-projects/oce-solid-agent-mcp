// Replaces the session scratchpad path with the placeholder {BANCO}
// inside the current working tree. It serves `git filter-branch --tree-filter`,
// which runs it once for every rewritten commit.
//
// Why it exists: two commits not yet pushed carried the user name inside
// a path `C:\Users\<nome>\AppData\Local\Temp\claude\<sessione>\scratchpad`.
// The working tree had already been repaired, the history had not, and
// `check-publishability.ps1` only looked at the tree: it said «Pulito».
//
// The same shape as the repair of the cases applies: the path is not deleted,
// the placeholder is put in that `run_plans.js` resolves to `Bench/lavoro/`.

var fs = require('fs');
var path = require('path');

// The path of a temporary session folder, in the three spellings in which it
// appears: single separator (C# verbatim strings), double (JSON with escapes)
// and forward slash. This pattern names nothing belonging to the company, so it stays
// here.
var PATH = /C:[\\\/]+Users[\\\/]+[^\\\/"\s]+[\\\/]+AppData[\\\/]+Local[\\\/]+Temp[\\\/]+claude[\\\/]+[^\\\/"\s]+[\\\/]+[^\\\/"\s]+[\\\/]+scratchpad/gi;

// ── THE PATTERNS OF THIS PLANT LIVE OUTSIDE THE REPOSITORY ────────────────
//
// A TOOL THAT REMOVES A STRING MUST NAME IT, and it is a short circuit:
// on 23/9/2026 this cleanup cleaned 140 commits of two internal things — the
// name of a company GPU server and a network folder — and when the rewrite
// was finished the search over the whole history still found one occurrence of
// each, in one place only: **in here**. The cure carried the disease.
//
// So the patterns specific to a plant are put in
// `Checks/bonifica-schemi.local.js`, which is NOT versioned (.gitignore).
// The mechanism stays public and useful to anyone; the house names do not.
//
// The local file exports a list of pairs:
//     module.exports = [ { schema: /.../g, con: 'replacement text' }, ... ];
// If it is not there, only the PATH is applied and it is not an error: a clean clone
// has nothing belonging to the company to remove.
var LOCALS = [];
try {
    LOCALS = require(path.join(__dirname, 'bonifica-schemi.local.js')) || [];
} catch (e) { /* not there: that is fine*/ }

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

        // NO QUICK FILTER. The first draft exited at once if the file did not
        // contain «AppData»: with the addition of new patterns it would have skipped
        // the very files to repair — a speed filter that becomes a
        // correctness filter, silently. Over a few hundred files the
        // difference cannot be measured; on a defect that raises no error it can.
        var newItem = text.replace(PATH, '{BANCO}');
        for (var k = 0; k < LOCALS.length; k++)
            newItem = newItem.replace(LOCALS[k].schema, LOCALS[k].con);
        if (newItem === text) continue;

        fs.writeFileSync(full, newItem, 'utf8');
        touched++;
    }
}

benchRun(process.cwd());

// Mute on purpose: `filter-branch` runs this command for every commit and
// printing here would make the output unreadable. The count is verified afterwards,
// on the rewritten history.
process.exit(0);
