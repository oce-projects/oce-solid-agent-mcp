// =============================================================================
//  Quante operazioni «provate» hanno davvero qualcosa che le prova  (voce Z-C6)
// =============================================================================
//
//  `Provata = true` nel catalogo vuol dire due cose diverse che nel file si
//  leggono uguali:
//
//    - «qualcuno l'ha misurata una volta»   <- una firma nel changelog
//    - «qualcosa la controlla a ogni giro»  <- un caso che gira
//
//  Questo conta la seconda. Per ogni operazione a catalogo guarda se esiste
//  qualcosa che la esercita, e distingue TRE modi, perche' non valgono uguale:
//
//    caso dichiarato   un file in `Banco\casi\` con dentro delle attese.
//                      Gira a ogni giro, e se il comportamento cambia diventa
//                      rosso. E' l'unico che vale del tutto.
//
//    C# del banco      un gruppo scritto a mano. Gira a ogni giro e asserisce
//                      in C#: vale, ed e' scritto qui perche' non si legga
//                      come un buco.
//
//    piano a mano      un ARRAY in `casi\tavola\`, che lancia `corri_piani.js`.
//                      **Non gira in nessun giro e non asserisce niente**: e'
//                      un appunto di come si e' misurato, non una prova.
//
//  PERCHE' NON E' ANCORA UN CONTROLLO DI CONTRATTO. Misurato il 23/9/2026:
//  cinque operazioni a catalogo non sono esercitate da niente. Una guardia che
//  nasce con cinque rossi permanenti e' un elenco che qualcuno silenziera'; si
//  sistemano prima quelle, poi la guardia si accende su un campo pulito.
//
//      node Controlli\conta-provate.js
//      node Controlli\conta-provate.js --solo-orfane
//
//  Esce 0 sempre: e' una misura, non un giudizio. Il giudizio lo dara' la
//  guardia, quando ci sara'.
// =============================================================================
'use strict';

const fs = require('fs');
const path = require('path');

const RADICE = path.resolve(__dirname, '..');
const CATALOGO = path.join(RADICE, 'Strumenti', 'catalogo.json');
const CASI = path.join(RADICE, 'Banco', 'casi');
const BANCO = path.join(RADICE, 'Banco');

const soloOrfane = process.argv.indexOf('--solo-orfane') >= 0;

// ── LE OPERAZIONI CHE UN CLIENT VEDE ─────────────────────────────────────
//
// Si legge il catalogo esportato e non il C#: e' quello che arriva al client,
// ed e' quello di cui `Provata = true` e' la promessa. L'esportatore lascia
// gia' fuori le `Provata = false` e lo dice a voce mentre scrive il file.
let catalogo;
try { catalogo = JSON.parse(fs.readFileSync(CATALOGO, 'utf8')); }
catch (e) {
  console.log('non riesco a leggere ' + CATALOGO + ': ' + e.message);
  console.log('si produce con  .\\bin\\Riscontro.Catalogo.exe Strumenti\\catalogo.json');
  process.exit(0);
}
const operazioni = (catalogo.operazioni || []).map((o) => o.nome).sort();

// ── CHI LE ESERCITA ──────────────────────────────────────────────────────
const daCasi = new Map();     // operazione -> [casi]
const daRegressione = new Map();   // operazione -> [casi di regressione]
let testoPiani = '';
let quantiCasi = 0;

function scorri(cartella) {
  let voci;
  try { voci = fs.readdirSync(cartella, { withFileTypes: true }); } catch (e) { return; }
  for (const v of voci) {
    const p = path.join(cartella, v.name);
    if (v.isDirectory()) { scorri(p); continue; }
    if (!v.name.endsWith('.json')) continue;
    let grezzo, j;
    try { grezzo = fs.readFileSync(p, 'utf8'); j = JSON.parse(grezzo); } catch (e) { continue; }

    // UN PIANO E' UN ARRAY, UN CASO E' UN OGGETTO. La differenza e'
    // strutturale, come gia' scritto in `CasiDichiarati.cs`: non si indovina
    // dal nome del file.
    if (Array.isArray(j)) { testoPiani += grezzo; continue; }

    quantiCasi++;
    const nome = j.nome || path.basename(v.name, '.json');
    // I CASI DI REGRESSIONE NON SONO CASI DI UN GIRO NORMALE (25/9/2026). Il
    // loro `costruisci` gira solo con `-Regressione`, e questo script li
    // contava come «caso dichiarato»: il riconteggio del 24/9 diceva 72 su 73,
    // e il controllo di contratto sui casi VERDI dell'ultimo giro ha trovato
    // tre operazioni (foro_guidato, estrudi_su_faccia, raccordo_spigoli) che in
    // un giro normale non toccava nessuno. Adesso stanno in un modo a parte.
    for (const passo of j.passi || []) {
      const op = passo && passo.operazione;
      if (!op) continue;
      if (!daCasi.has(op)) daCasi.set(op, []);
      if (daCasi.get(op).indexOf(nome) < 0) daCasi.get(op).push(nome);
    }
    for (const passo of j.costruisci || []) {
      const op = passo && passo.operazione;
      if (!op) continue;
      if (!daRegressione.has(op)) daRegressione.set(op, []);
      if (daRegressione.get(op).indexOf(nome) < 0) daRegressione.get(op).push(nome);
    }
  }
}
scorri(CASI);

let testoCs = '';
try {
  for (const f of fs.readdirSync(BANCO))
    if (f.endsWith('.cs')) testoCs += fs.readFileSync(path.join(BANCO, f), 'utf8');
} catch (e) { }

function comeEProvata(op) {
  const virgolette = '"' + op + '"';
  if (daCasi.has(op)) return { come: 'caso dichiarato', dettaglio: daCasi.get(op).slice(0, 3).join(', ') };
  if (daRegressione.has(op))
    return { come: 'solo regressione', dettaglio: 'gira solo con -Regressione: ' + daRegressione.get(op).slice(0, 3).join(', ') };
  if (testoCs.indexOf(virgolette) >= 0) return { come: 'C# del banco', dettaglio: '' };
  if (testoPiani.indexOf(virgolette) >= 0)
    return { come: 'solo piano a mano', dettaglio: 'non gira in nessun giro, non asserisce niente' };
  return { come: 'NIENTE', dettaglio: 'a catalogo, e nessun giro la tocca' };
}

const perModo = { 'caso dichiarato': [], 'solo regressione': [], 'C# del banco': [], 'solo piano a mano': [], 'NIENTE': [] };
for (const op of operazioni) perModo[comeEProvata(op).come].push(op);

if (!soloOrfane) {
  console.log('catalogo: ' + CATALOGO);
  console.log('operazioni a catalogo (Provata = true): ' + operazioni.length);
  console.log('casi dichiarati: ' + quantiCasi);
  console.log('');
  for (const modo of ['caso dichiarato', 'solo regressione', 'C# del banco', 'solo piano a mano', 'NIENTE']) {
    console.log(String(perModo[modo].length).padStart(4) + '  ' + modo);
  }
  console.log('');
  if (perModo['solo regressione'].length > 0) {
    console.log('SOLO IN REGRESSIONE — girano solo con -Regressione, non in un giro normale:');
    for (const o of perModo['solo regressione']) console.log('    ' + o);
    console.log('');
  }
  console.log('(La guardia vera e\' il controllo di contratto «ogni operazione a catalogo ha un');
  console.log(' caso VERDE nell\'ultimo giro», che legge i casi verdi da Banco\\resoconto\\ultimo.json.)');
  console.log('');
  if (perModo['solo piano a mano'].length > 0) {
    console.log('SOLO UN PIANO A MANO — un appunto di come si e\' misurato, non una prova:');
    for (const o of perModo['solo piano a mano']) console.log('    ' + o);
    console.log('');
  }
}

if (perModo['NIENTE'].length === 0) {
  console.log('Nessuna operazione orfana: ogni voce del catalogo ha qualcosa che la esercita.');
} else {
  console.log('ORFANE — a catalogo, e nessun giro le tocca:');
  for (const o of perModo['NIENTE']) console.log('    ' + o);
  console.log('');
  console.log('Per ognuna: o un caso dichiarato in Banco\\casi\\, oppure Provata = false,');
  console.log('che la toglie dal catalogo come gia\' succede a croci_di_centro.');
}
