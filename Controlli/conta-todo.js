// Conta le voci del TODO, e dice i numeri che il TODO stesso dichiara.
//
//     node Controlli/conta-todo.js            i numeri
//     node Controlli/conta-todo.js --righe    e anche quali voci sono aperte
//     node Controlli/conta-todo.js --blocchi  e COSA le blocca, una per una
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

const file = process.argv.find((a) => a.endsWith(".md")) ||
  path.join(__dirname, "..", "TODO.md");
const mostraRighe = process.argv.includes("--righe");

const righe = fs.readFileSync(file, "utf8").split(/\r?\n/);

// IL CORPO SERVE QUANTO IL TITOLO. Le spie («firme:», «sonda:») stanno
// sotto l'intestazione, non dentro: una voce letta per titolo e' una voce
// letta a meta'.
const voci = [];
for (let i = 0; i < righe.length; i++) {
  const m = righe[i].match(/^#### \[([ x~])\] (.+)$/);
  if (!m) continue;
  const corpo = [];
  for (let j = i + 1; j < righe.length && !/^#### \[/.test(righe[j]); j++) {
    if (/^#{1,4} /.test(righe[j]) && !/^#### \[/.test(righe[j])) break;
    corpo.push(righe[j]);
  }
  // Il CODICE della voce (`Z-G7`, `W2.2`, `S3`, `GA11`) si tiene: senza,
  // «dipende da Y1» resta una frase e non una cosa che si puo' guardare.
  const c = m[2].match(/^([A-Z]{1,2}[0-9]+(?:[.-][0-9A-Za-z]+)*|[A-Z]-[A-Z][0-9]+(?:-[a-z]+)?)\b/);
  voci.push({
    stato: m[1], titolo: m[2], corpo: corpo.join(' '),
    codice: c ? c[1] : null,
  });
}

const piene = voci.filter((v) => v.stato === "x");
const meta = voci.filter((v) => v.stato === "~");
const aperte = voci.filter((v) => v.stato === " ");

// Una traccia tenuta per riferimento si riconosce dal titolo: il TODO le
// scrive cosi' per non perdere il perche' di una voce riscritta.
const tracce = aperte.filter((v) => /traccia originale|tenuta per riferimento/i.test(v.titolo));
const lavoroVero = aperte.length - tracce.length;

const totale = voci.length;
const soloPiene = (piene.length / totale) * 100;
const conMeta = ((piene.length + meta.length * 0.5) / totale) * 100;
const metaPerIntere = ((piene.length + meta.length) / totale) * 100;

console.log(`file: ${path.relative(process.cwd(), file)}`);
console.log(`voci con casella: ${totale}`);
console.log(`  [x] chiuse   ${piene.length}`);
console.log(`  [~] a meta'  ${meta.length}`);
console.log(`  [ ] aperte   ${aperte.length}`);
if (tracce.length) {
  console.log(`      di cui tracce tenute per riferimento: ${tracce.length}`);
  console.log(`      LAVORO VERO: ${lavoroVero} aperte su ${totale}`);
}
console.log();
console.log("completamento, e la forchetta e' il numero onesto:");
console.log(`  solo [x]                      ${soloPiene.toFixed(1)} %`);
console.log(`  [x] piene, [~] per meta'      ${conMeta.toFixed(1)} %`);
console.log(`  [~] contate per intere        ${metaPerIntere.toFixed(1)} %`);
console.log();
console.log("la riga da riscrivere nel TODO:");
console.log(
  `  Delle ${aperte.length} aperte, ${tracce.length === 1 ? "**una**" : `**${tracce.length}**`} ` +
    `e' una «traccia originale tenuta per riferimento»... ` +
    `Il lavoro vero e' quindi **${lavoroVero} aperte su ${totale}**.`
);

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
  const delProprietario = (v) =>
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
  const documento =
    /documento aperto|sul documento|\.sldprt|\.sldasm|assieme aperto|pezzo aperto|una tavola aperta|apre un|aprire un|oltre il tetto|guarda_il_pezzo/i;

  // QUINTA CORREZIONE, il 23/9/2026: una voce che si dichiara **BLOCCATA**
  // o «forse-mai» non e' lavoro fattibile adesso, ed e' la voce stessa a
  // dirlo. E11 («rollback come oracolo», bloccata e con la misura che non la
  // chiude) finiva in cima all'elenco delle cose da fare.
  //
  // Cinque correzioni in un giorno. Il numero e' utile, e **non e' un
  // verdetto**: ogni volta che si e' guardata una voce dell'elenco da
  // vicino, l'elenco si e' accorciato.
  const bloccata = (v) => /BLOCCATA|forse-mai|non stimabile/i.test(v.titolo);

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
  const traccia = (v) => /traccia originale|tenuta per riferimento/i.test(v.titolo);
  const ordine = { "x": 2, "~": 1, " ": 0 };
  const statoDi = new Map();
  for (const v of voci) {
    if (!v.codice || traccia(v)) continue;
    const gia = statoDi.get(v.codice);
    if (gia === undefined || ordine[v.stato] > ordine[gia]) statoDi.set(v.codice, v.stato);
  }

  const dipendeDaApertA = (v) => {
    const dove = (v.titolo + ' ' + v.corpo);
    const frasi = dove.match(/dipende(?:\s+interamente)?\s+da\s+[^.;)]*/gi) || [];
    for (const f of frasi) {
      for (const m of f.matchAll(/\b([A-Z]{1,2}[0-9]+(?:[.-][0-9A-Za-z]+)*|[A-Z]-[A-Z][0-9]+)\b/g)) {
        const s = statoDi.get(m[1]);
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
  const misuraSulPezzo =
    /volume (?:tolto|del|di|calcolato|vero)|ingombro (?:di|del|letto|dei)|massa (?:del|di)|area del contorno|corpi indipendenti/i;

  const vuoleSolidWorks = (v) =>
    misuraSulPezzo.test(v.corpo) ||
    /\*\*firme:\*\*|\*\*sonda:\*\* *s|sonda: *s\u00ec|giro di banco|caso di banco|banco verde/i.test(
      v.corpo) || api.test(v.corpo) || api.test(v.titolo) ||
    documento.test(v.corpo) || documento.test(v.titolo);

  const gruppi = { proprietario: [], solidworks: [], adesso: [], dipendenti: [] };
  for (const v of aperte) {
    if (/traccia originale|tenuta per riferimento/i.test(v.titolo)) continue;
    const nome = v.titolo.split("\u00b7")[0].trim().slice(0, 76);
    const aspetta = dipendeDaApertA(v);
    if (delProprietario(v)) { gruppi.proprietario.push(nome); continue; }
    if (aspetta) { gruppi.dipendenti.push(nome + "   (aspetta " + aspetta + ")"); continue; }
    if (bloccata(v) || vuoleSolidWorks(v)) { gruppi.solidworks.push(nome); continue; }
    gruppi.adesso.push(nome);
  }

  console.log();
  console.log("cosa blocca le voci aperte (spie, non sentenze):");
  console.log(`  decisione del proprietario   ${gruppi.proprietario.length}`);
  console.log(`  vuole SOLIDWORKS acceso      ${gruppi.solidworks.length}`);
  console.log(`  aspetta un'altra voce        ${gruppi.dipendenti.length}`);
  console.log(`  SI PUO' FARE ADESSO          ${gruppi.adesso.length}`);
  console.log();
  console.log("  -- si puo' fare adesso, senza SOLIDWORKS --");
  for (const v of gruppi.adesso) console.log("     " + v);
  console.log();
  console.log("  -- aspetta che un'ALTRA VOCE sia chiusa --");
  for (const v of gruppi.dipendenti) console.log("     " + v);
  console.log();
  console.log("  -- aspetta una decisione che non e' di chi programma --");
  for (const v of gruppi.proprietario) console.log("     " + v);
}

if (mostraRighe) {
  console.log();
  console.log("le aperte:");
  for (const v of aperte) {
    const segno = /traccia originale|tenuta per riferimento/i.test(v.titolo) ? "  (traccia)" : "";
    console.log("   " + v.titolo.split("·")[0].trim().slice(0, 78) + segno);
  }
}
