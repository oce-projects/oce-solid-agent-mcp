// Conta le voci del TODO, e dice i numeri che il TODO stesso dichiara.
//
//     node Checks/count-todo.js            i numeri
//     node Checks/count-todo.js --righe    e anche quali voci sono aperte
//     node Checks/count-todo.js --blocchi  e COSA le blocca, una per una
//
// ── PERCHE' «COSA LE BLOCCA» E NON SOLO «QUANTE» ─────────────────────────
//
// Un numero solo — «95 aperte» — non dice cosa si puo' fare adesso, e le 95
// non sono la stessa cosa: alcune aspettano una decisione che non e' mia,
// altre vogliono SOLIDWORKS acceso e un giro di banco da un'ora, altre sono
// codice e documenti che si scrivono e si verificano in pochi minuti.
//
// Il 23/9/2026 questa differenza e' diventata concreta: SOLIDWORKS era
// inagibile per una presa rimasta su un file di rete, e senza questo elenco
// la risposta a «cosa faccio nel frattempo?» sarebbe stata leggere 15.000
// righe a occhio.
//
// ── E LA CLASSIFICAZIONE E' UN INDIZIO, NON UNA SENTENZA ─────────────────
//
// Si legge il CORPO della voce e si cercano i segni che il TODO usa gia':
// «**firme:**» vuol dire che c'e' un'API da misurare; «**sonda:** si'» che
// serve provarla su SOLIDWORKS; «proprietario» che la decisione non e' di
// chi programma. Sono spie, e una spia sbaglia: il conto e' un punto di
// partenza per guardare, non un verdetto. Per questo l'elenco stampa i
// TITOLI e non solo i numeri — cosi' chi legge puo' contestarlo.
//
// ── PERCHE' STA QUI E NON IN `scratchpad/` ────────────────────────────────
//
// Il TODO diceva «si ricontano con `node scratchpad/conta_todo.js`», e quello
// script NON ESISTE: `scratchpad/` non e' versionata, quindi sparisce a ogni
// sessione e non e' mai arrivata a nessun altro. Un'istruzione che indica un
// file assente e' peggio di nessuna istruzione: chi la legge prova, fallisce,
// e conclude che il conteggio non si puo' rifare — cosi' la riga scritta a
// mano resta com'e' e invecchia.
//
// E' lo stesso difetto che il TODO rimprovera ai numeri scritti a mano,
// applicato allo strumento che doveva impedirlo.
//
// ── DUE DENOMINATORI, E VANNO DETTI TUTTI E DUE ───────────────────────────
//
// Fra le voci con la casella vuota ce ne sono alcune che NON sono lavoro da
// fare: sono «tracce originali tenute per riferimento», cioe' la storia di una
// voce gia' chiusa. Contarle come aperte gonfia il lavoro residuo; non dirlo
// affatto lo sgonfia. Si dicono tutti e due i numeri.
const fs = require("fs");
const path = require("path");

const { findTheTodo } = require("./where-is-the-todo.js");
const file = findTheTodo(path.join(__dirname, ".."), process.argv);
if (!file) process.exit(0);
const showLines = process.argv.includes("--righe");

const lines = fs.readFileSync(file, "utf8").split(/\r?\n/);

// IL CORPO SERVE QUANTO IL TITOLO. Le spie («firme:», «sonda:») stanno
// sotto l'intestazione, non dentro: una voce letta per titolo e' una voce
// letta a meta'.
const entries = [];
for (let i = 0; i < lines.length; i++) {
  const m = lines[i].match(/^#### \[([ x~])\] (.+)$/);
  if (!m) continue;
  const body = [];
  for (let j = i + 1; j < lines.length && !/^#### \[/.test(lines[j]); j++) {
    if (/^#{1,4} /.test(lines[j]) && !/^#### \[/.test(lines[j])) break;
    body.push(lines[j]);
  }
  // Il CODICE della voce (`Z-G7`, `W2.2`, `S3`, `GA11`) si tiene: senza,
  // «dipende da Y1» resta una frase e non una cosa che si puo' guardare.
  const c = m[2].match(/^([A-Z]{1,2}[0-9]+(?:[.-][0-9A-Za-z]+)*|[A-Z]-[A-Z][0-9]+(?:-[a-z]+)?)\b/);
  entries.push({
    stato: m[1], titolo: m[2], corpo: body.join(' '),
    codice: c ? c[1] : null,
  });
}

const full = entries.filter((v) => v.stato === "x");
const meta = entries.filter((v) => v.stato === "~");
const open = entries.filter((v) => v.stato === " ");

// UNA TRACCIA NON HA LA CASELLA, E QUESTO SCRIPT ADESSO LO PRETENDE.
//
// Qui si cercavano le tracce solo fra le `[ ]`, e se ne trovava UNA. La
// rilettura del 24/9/2026 ne ha contate 54: 49 stavano a `[~]`, dove questo
// filtro non guardava, e contavano come lavoro «a meta'». Il conto diceva 87
// voci a meta' e le vere erano 36; il totale diceva 312 e le voci vere erano
// 258. *Il numero che doveva impedire di gonfiare il lavoro lo gonfiava, e lo
// faceva con la regola giusta applicata a un quinto dei casi.*
//
// La regola del TODO (in testa al file) e': una traccia perde la casella e
// diventa `(com'era)`. Quindi una traccia CON la casella non e' da contare
// a parte: e' un difetto del file, e lo script esce 1. Lo stesso
// riconoscimento di Tests/Program.cs, perche' due regole per la stessa
// cosa divergono.
const TRACE = /traccia originale|progetto originale|tenut[ao] per|per riferimento|com'era scritta/i;
const traces = entries.filter((v) => TRACE.test(v.titolo) && !/FATTO il/.test(v.titolo));
const realWork = open.length - traces.filter((v) => v.stato === " ").length;

// E UNA CASELLA FUORI DAI `####` NON LA VEDE NESSUNO. W3.1 — 30-50 ore,
// prerequisito della pubblicazione — stava a `###` ed era invisibile a questo
// conto, al controllo sui nomi doppi e al grep che la testa del file indica.
const offLevel = [];
for (let i = 0; i < lines.length; i++) {
  if (/^(#{1,3}|#{5,6}) \[[ x~]\] /.test(lines[i])) offLevel.push(i + 1);
}

const total = entries.length;
const fullOnly = (full.length / total) * 100;
const conMeta = ((full.length + meta.length * 0.5) / total) * 100;
const halfForWhole = ((full.length + meta.length) / total) * 100;

console.log(`file: ${path.relative(process.cwd(), file)}`);
console.log(`voci con casella: ${total}`);
console.log(`  [x] chiuse   ${full.length}`);
console.log(`  [~] a meta'  ${meta.length}`);
console.log(`  [ ] aperte   ${open.length}`);
let defects = 0;
if (traces.length) {
  defects++;
  console.log();
  console.log(`  XX  ${traces.length} tracce hanno ancora la casella, e contano come lavoro:`);
  for (const v of traces) console.log(`        [${v.stato}] ${v.titolo.slice(0, 70)}`);
  console.log(`      La regola in testa al TODO: una traccia diventa «(com'era)».`);
  console.log(`      Al netto: ${realWork} aperte vere.`);
}
if (offLevel.length) {
  defects++;
  console.log();
  console.log(`  XX  caselle fuori dal livello ####, invisibili a ogni conto: righe ${offLevel.join(", ")}`);
}
console.log();
console.log("completamento, e la forchetta e' il numero onesto:");
console.log(`  solo [x]                      ${fullOnly.toFixed(1)} %`);
console.log(`  [x] piene, [~] per meta'      ${conMeta.toFixed(1)} %`);
console.log(`  [~] contate per intere        ${halfForWhole.toFixed(1)} %`);
console.log();
console.log("la riga da riscrivere nel §1 del TODO:");
console.log(
  `  **${total} voci**: ${full.length} chiuse, ${meta.length} a meta', ${open.length} aperte ` +
    `(${fullOnly.toFixed(1)}% / ${conMeta.toFixed(1)}% / ${halfForWhole.toFixed(1)}%).`
);
if (defects) process.exitCode = 1;

if (process.argv.includes("--blocchi")) {
  // Le spie, in ordine di forza: una decisione del proprietario batte
  // tutto (non e' lavoro nostro), poi SOLIDWORKS, poi il resto.
  // QUARTA CORREZIONE, il 23/9/2026: una voce marcata **decisione** nel suo
  // titolo non e' lavoro da scrivere, e' una scelta da fare -- e se riguarda
  // un file di chi lavora qui, non la prende chi programma.
  //
  // J4 («decidere cosa fare del documento sporco»: annullare, tenere o
  // rifare un pezzo su cui c'e' lavoro recente) finiva fra le "fattibili
  // adesso". Non lo e': la scelta e' del proprietario del pezzo.
  //
  // **Questa spia e' stata corretta quattro volte in un giorno**, e resta
  // una spia. Il numero che stampa e' un punto di partenza per guardare, non
  // un verdetto -- per questo stampa anche i TITOLI, cosi' chi legge puo'
  // contestarlo invece di crederlo.
  const owner = (v) =>
    /proprietario|decisa dal|force-push|deploy|tag v0|licenza commerciale/i.test(
      v.titolo + " " + v.corpo) ||
    /\*\*decisione\*\*/i.test(v.titolo);
  // LA SPIA PIU' FORTE E' IL NOME DI UN'API. La prima versione cercava
  // «**firme:**» e «sonda: si'», e lasciava fra le fattibili GP10 — che
  // parla di `InsertRib` per dieci righe — perche' quella voce e' scritta
  // come una citazione, senza i campi. Una voce che nomina un'API di
  // SOLIDWORKS vuole SOLIDWORKS: non c'e' modo di misurarla altrove.
  const api = /\bI[A-Z]\w+\.\w+\(|\bsw[A-Z]\w+_e\b|\b(?:Insert|Create|Get|Set|Add)[A-Z]\w+\(/;

  // TERZA CORREZIONE, il 23/9/2026: un lavoro che si misura SU UN DOCUMENTO
  // vuole SOLIDWORKS anche se non nomina nessuna API.
  //
  // Le prime due stesure mandavano fra le "fattibili adesso" Z-B7 (scegliere
  // una vite da una cartella dell'officina e aprirla), Z-A11 ("gira sul
  // documento aperto") e Y3 (aprire un .sldprt sconosciuto e raccontarlo).
  // Nessuna delle tre si puo' fare col CAD spento.
  //
  // **Un criterio che promette lavoro che non si puo' fare e' peggio di
  // nessun criterio**: la prima volta ci si prova, la seconda non lo si
  // legge piu'.
  const document =
    /documento aperto|sul documento|\.sldprt|\.sldasm|assieme aperto|pezzo aperto|una tavola aperta|apre un|aprire un|oltre il tetto|guarda_il_pezzo/i;

  // QUINTA CORREZIONE, il 23/9/2026: una voce che si dichiara **BLOCCATA**
  // o «forse-mai» non e' lavoro fattibile adesso, ed e' la voce stessa a
  // dirlo. E11 («rollback come oracolo», bloccata e con la misura che non la
  // chiude) finiva in cima all'elenco delle cose da fare.
  //
  // Cinque correzioni in un giorno. Il numero e' utile, e **non e' un
  // verdetto**: ogni volta che si e' guardata una voce dell'elenco da
  // vicino, l'elenco si e' accorciato.
  const locked = (v) => /BLOCCATA|forse-mai|non stimabile/i.test(v.titolo);

  // SESTA CORREZIONE, il 24/9/2026, e questa non e' una regex: e' un
  // controllo.
  //
  // L'elenco «si puo' fare adesso» mi ha mandato su **Y2** («dipende da
  // Y1») e su **Z-B7** («ore: dipende interamente da B1 e B3»). Tutt'e due
  // dichiarano la propria dipendenza **in chiaro**, e tutt'e due dipendono
  // da voci ancora aperte. Nessuna delle due si poteva cominciare.
  //
  // Qui non si indovina niente: si legge il codice che la voce ha scritto
  // da sola e si guarda com'e' messa quella voce. Se la voce citata non
  // esiste, **non si blocca**: sarebbe una sentenza su un refuso.
  // LE TRACCE NON CONTANO, e ci sono cascato lo stesso giorno in cui ho
  // scritto la regola.
  //
  // Il TODO tiene, accanto a una voce chiusa, la sua «traccia originale
  // tenuta per riferimento»: **stesso codice**, e a `~` per sempre perche'
  // e' un ricordo, non lavoro. Sono decine. Costruendo la mappa senza
  // saltarle, l'ultima scritta vinceva — cioe' la traccia — e `B1`
  // risultava aperta benche' la B1 vera sia chiusa dal 14/9/2026.
  //
  // Z-B7 e' finita cosi' fra le «bloccate», e la cosa che aspettava era
  // gia' fatta da dieci giorni. *Una spia che accusa una cosa fatta manda
  // a cercare nel posto sbagliato, che e' peggio che tacere.*
  //
  // E se lo stesso codice restasse ambiguo, vince la voce piu' avanti: fra
  // due righe che parlano della stessa cosa, quella chiusa e' l'ultima
  // parola.
  const trace = (v) => /traccia originale|tenuta per riferimento/i.test(v.titolo);
  const order = { "x": 2, "~": 1, " ": 0 };
  const stateOf = new Map();
  for (const v of entries) {
    if (!v.codice || trace(v)) continue;
    const already = stateOf.get(v.codice);
    if (already === undefined || order[v.stato] > order[already]) stateOf.set(v.codice, v.stato);
  }

  const dependsOnOpenOne = (v) => {
    const location = (v.titolo + ' ' + v.corpo);
    const sentences = location.match(/dipende(?:\s+interamente)?\s+da\s+[^.;)]*/gi) || [];
    for (const f of sentences) {
      for (const m of f.matchAll(/\b([A-Z]{1,2}[0-9]+(?:[.-][0-9A-Za-z]+)*|[A-Z]-[A-Z][0-9]+)\b/g)) {
        const s = stateOf.get(m[1]);
        if (s === ' ' || s === '~') return m[1];
      }
    }
    return null;
  };

  // SETTIMA. **W2.6** dice «Finita quando: volume tolto pari all'area del
  // contorno per la profondita'; e due corpi indipendenti dopo, con
  // l'ingombro di tutti e due letto». Non nomina nessuna API e non nomina
  // nessun documento — ma un volume e un ingombro **si leggono da un pezzo
  // vero**, e non c'e' nessun altro posto da cui leggerli.
  const measureOnPart =
    /volume (?:tolto|del|di|calcolato|vero)|ingombro (?:di|del|letto|dei)|massa (?:del|di)|area del contorno|corpi indipendenti/i;

  const wantsSolidWorks = (v) =>
    measureOnPart.test(v.corpo) ||
    /\*\*firme:\*\*|\*\*sonda:\*\* *s|sonda: *s\u00ec|giro di banco|caso di banco|banco verde/i.test(
      v.corpo) || api.test(v.corpo) || api.test(v.titolo) ||
    document.test(v.corpo) || document.test(v.titolo);

  const groups = { proprietario: [], solidworks: [], adesso: [], dipendenti: [] };
  for (const v of open) {
    if (/traccia originale|tenuta per riferimento/i.test(v.titolo)) continue;
    const itemName = v.titolo.split("\u00b7")[0].trim().slice(0, 76);
    const wait = dependsOnOpenOne(v);
    if (owner(v)) { groups.proprietario.push(itemName); continue; }
    if (wait) { groups.dipendenti.push(itemName + "   (aspetta " + wait + ")"); continue; }
    if (locked(v) || wantsSolidWorks(v)) { groups.solidworks.push(itemName); continue; }
    groups.adesso.push(itemName);
  }

  console.log();
  console.log("cosa blocca le voci aperte (spie, non sentenze):");
  console.log(`  decisione del proprietario   ${groups.proprietario.length}`);
  console.log(`  vuole SOLIDWORKS acceso      ${groups.solidworks.length}`);
  console.log(`  aspetta un'altra voce        ${groups.dipendenti.length}`);
  console.log(`  SI PUO' FARE ADESSO          ${groups.adesso.length}`);
  console.log();
  console.log("  -- si puo' fare adesso, senza SOLIDWORKS --");
  for (const v of groups.adesso) console.log("     " + v);
  console.log();
  console.log("  -- aspetta che un'ALTRA VOCE sia chiusa --");
  for (const v of groups.dipendenti) console.log("     " + v);
  console.log();
  console.log("  -- aspetta una decisione che non e' di chi programma --");
  for (const v of groups.proprietario) console.log("     " + v);
}

if (showLines) {
  console.log();
  console.log("le aperte:");
  for (const v of open) {
    const sign = /traccia originale|tenuta per riferimento/i.test(v.titolo) ? "  (traccia)" : "";
    console.log("   " + v.titolo.split("·")[0].trim().slice(0, 78) + sign);
  }
}
