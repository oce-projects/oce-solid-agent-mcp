// Dove sta il TODO, e cosa fare quando non c'e'.
//
// IL TODO NON E' PIU' NEL REPOSITORY PUBBLICO. E' un diario di lavoro da
// diciassettemila righe: nomina l'autore, i colleghi, le postazioni e le
// decisioni ancora da prendere, e a chi clona non insegna niente. Vive in
// `note-interne/`, che il `.gitignore` esclude.
//
// CINQUE SONDE ESISTONO SOLO PER CONTROLLARLO, e tre girano nel gancio di
// pre-push. Togliere il file senza toccarle le avrebbe fatte morire con
// `ENOENT` su ogni macchina che non e' questa - cioe' avrebbe rotto il push a
// chiunque clonasse, per un file che non ha e non deve avere.
//
// Quindi la sonda non muore: DICE CHE NON HA CONTROLLATO, ed esce 0. E' la
// stessa forma di `check-documents-without-file.ps1` quando l'add-in non e'
// raggiungibile: *una meta' non controllata si scrive, non si dichiara
// passata.*
//
//     const { findTheTodo } = require('./where-is-the-todo.js');
//     const fileTodo = findTheTodo(radice, process.argv);
//     if (!fileTodo) process.exit(0);   // il messaggio l'ha gia' stampato
//
// L'ORDINE DI RICERCA, e nessun passo e' di comodo:
//   1. un `.md` passato sulla riga di comando - serve a far scattare la sonda
//      su una copia ROTTA, che e' l'unico modo di crederle;
//   2. la variabile TODO_MD, per chi lo tiene altrove;
//   3. `note-interne/TODO.md`, dove sta su questa macchina;
//   4. `TODO.md` in radice, dov'era prima: un clone vecchio continua a
//      funzionare senza che nessuno se ne accorga.
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
