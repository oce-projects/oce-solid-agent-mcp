// Conta le voci del TODO, e dice i numeri che il TODO stesso dichiara.
//
//     node Controlli/conta-todo.js            i numeri
//     node Controlli/conta-todo.js --righe    e anche quali voci sono aperte
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

const voci = [];
for (const r of righe) {
  const m = r.match(/^#### \[([ x~])\] (.+)$/);
  if (!m) continue;
  voci.push({ stato: m[1], titolo: m[2] });
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

if (mostraRighe) {
  console.log();
  console.log("le aperte:");
  for (const v of aperte) {
    const segno = /traccia originale|tenuta per riferimento/i.test(v.titolo) ? "  (traccia)" : "";
    console.log("   " + v.titolo.split("·")[0].trim().slice(0, 78) + segno);
  }
}
