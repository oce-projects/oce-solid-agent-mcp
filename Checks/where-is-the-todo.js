// Where the TODO is, and what to do when it is not there.
//
// THE TODO IS NO LONGER IN THE PUBLIC REPOSITORY. It is a work journal of
// seventeen thousand lines: it names the author, the colleagues, the workstations and the
// decisions still to be taken, and to whoever clones it teaches nothing. It lives in
// `note-interne/`, which the `.gitignore` excludes.
//
// FIVE PROBES EXIST ONLY TO CHECK IT, and three run in the pre-push
// hook. Removing the file without touching them would have made them die with
// `ENOENT` on every machine that is not this one - that is it would have broken the push
// for anyone who cloned, over a file that they do not have and must not have.
//
// So the probe does not die: IT SAYS THAT IT DID NOT CHECK, and exits 0. It is the
// same shape as `check-documents-without-file.ps1` when the add-in is not
// reachable: *a half not checked is written down, it is not declared
// passed.*
//
//     const { findTheTodo } = require('./where-is-the-todo.js');
//     const fileTodo = findTheTodo(radice, process.argv);
//     if (!fileTodo) process.exit(0);   // the message has already been printed
//
// THE ORDER OF SEARCH, and no step is for convenience:
//   1. a `.md` passed on the command line - it serves to make the probe trigger
//      on a BROKEN copy, which is the only way to believe it;
//   2. the variable TODO_MD, for whoever keeps it elsewhere;
//   3. `note-interne/TODO.md`, where it is on this machine;
//   4. `TODO.md` in the root, where it was before: an old clone keeps
//      working without anybody noticing.
const fs = require('fs');
const path = require('path');

function findTheTodo(root, argv) {
    const passed = (argv || []).find(a => typeof a === 'string' && a.endsWith('.md'));
    if (passed) return passed;

    const its = process.env.TODO_MD;
    if (its && fs.existsSync(its)) return its;

    const candidates = [
        path.join(root, 'note-interne', 'TODO.md'),
        path.join(root, 'TODO.md'),
    ];
    for (const c of candidates) if (fs.existsSync(c)) return c;

    console.log('Il TODO non c\'e\' in questo clone: e\' un documento interno e');
    console.log('non viene pubblicato. QUESTA SONDA NON HA CONTROLLATO NIENTE.');
    console.log('Se ne hai una copia, indicala con TODO_MD o passala come argomento.');
    return null;
}

module.exports = { findTheTodo };
