// Counts the entries of the TODO, and tells the numbers that the TODO itself declares.
//
//     node Checks/count-todo.js            the numbers
//     node Checks/count-todo.js --righe    and also which entries are open
//     node Checks/count-todo.js --blocchi  and WHAT blocks them, one by one
//
// ── WHY «WHAT BLOCKS THEM» AND NOT ONLY «HOW MANY» ───────────────────────
//
// A single number — «95 open» — does not say what can be done now, and the 95
// are not the same thing: some wait for a decision that is not mine,
// others want SOLIDWORKS running and a bench run of an hour, others are
// code and documents that are written and verified in a few minutes.
//
// On 23/9/2026 this difference became concrete: SOLIDWORKS was
// unusable because of a lock left on a network file, and without this list
// the answer to «what do I do in the meantime?» would have been reading 15,000
// lines by eye.
//
// ── AND THE CLASSIFICATION IS A HINT, NOT A SENTENCE ─────────────────────
//
// The BODY of the entry is read and we look for the signs that the TODO already uses:
// «**firme:**» means there is an API to measure; «**sonda:** si'» that it
// must be tried on SOLIDWORKS; «proprietario» that the decision is not
// up to whoever programs. They are warning lights, and a warning light errs: the count is a starting
// point for looking, not a verdict. That is why the list prints the
// TITLES and not only the numbers — so that whoever reads can dispute it.
//
// ── WHY IT LIVES HERE AND NOT IN `scratchpad/` ────────────────────────────
//
// The TODO said «they are recounted with `node scratchpad/conta_todo.js`», and that
// script DOES NOT EXIST: `scratchpad/` is not versioned, so it disappears at every
// session and never reached anybody else. An instruction that points to an
// absent file is worse than no instruction: whoever reads it tries, fails,
// and concludes that the count cannot be redone — so the line written by
// hand stays as it is and ages.
//
// It is the same defect that the TODO reproaches to hand-written numbers,
// applied to the tool that was supposed to prevent it.
//
// ── TWO DENOMINATORS, AND BOTH MUST BE SAID ───────────────────────────────
//
// Among the entries with an empty box there are some that are NOT work to
// be done: they are «tracce originali tenute per riferimento», that is the history of an
// entry already closed. Counting them as open inflates the residual work; not saying it
// at all deflates it. Both numbers are said.
const fs = require("fs");
const path = require("path");

const { findTheTodo } = require("./where-is-the-todo.js");
const file = findTheTodo(path.join(__dirname, ".."), process.argv);
if (!file) process.exit(0);
const showLines = process.argv.includes("--righe");

const lines = fs.readFileSync(file, "utf8").split(/\r?\n/);

// THE BODY MATTERS AS MUCH AS THE TITLE. The warning lights («firme:», «sonda:») sit
// under the heading, not inside it: an entry read by its title is an entry
// read halfway.
const entries = [];
for (let i = 0; i < lines.length; i++) {
  const m = lines[i].match(/^#### \[([ x~])\] (.+)$/);
  if (!m) continue;
  const body = [];
  for (let j = i + 1; j < lines.length && !/^#### \[/.test(lines[j]); j++) {
    if (/^#{1,4} /.test(lines[j]) && !/^#### \[/.test(lines[j])) break;
    body.push(lines[j]);
  }
  // The CODE of the entry (`Z-G7`, `W2.2`, `S3`, `GA11`) is kept: without it,
  // «dipende da Y1» stays a sentence and not a thing that can be looked at.
  const c = m[2].match(/^([A-Z]{1,2}[0-9]+(?:[.-][0-9A-Za-z]+)*|[A-Z]-[A-Z][0-9]+(?:-[a-z]+)?)\b/);
  entries.push({
    stato: m[1], titolo: m[2], corpo: body.join(' '),
    codice: c ? c[1] : null,
  });
}

const full = entries.filter((v) => v.stato === "x");
const meta = entries.filter((v) => v.stato === "~");
const open = entries.filter((v) => v.stato === " ");

// A TRACE HAS NO BOX, AND THIS SCRIPT NOW DEMANDS IT.
//
// Here the traces were searched only among the `[ ]`, and ONE was found. The
// re-reading of 24/9/2026 counted 54: 49 sat at `[~]`, where this
// filter did not look, and counted as «a meta'» work. The count said 87
// entries halfway and the real ones were 36; the total said 312 and the real entries were
// 258. *The number that was supposed to prevent inflating the work inflated it, and it
// did so with the right rule applied to a fifth of the cases.*
//
// The rule of the TODO (at the top of the file) is: a trace loses its box and
// becomes `(com'era)`. So a trace WITH the box is not to be counted
// separately: it is a defect of the file, and the script exits 1. The same
// recognition as Tests/Program.cs, because two rules for the same
// thing diverge.
const TRACE = /traccia originale|progetto originale|tenut[ao] per|per riferimento|com'era scritta/i;
const traces = entries.filter((v) => TRACE.test(v.titolo) && !/FATTO il/.test(v.titolo));
const realWork = open.length - traces.filter((v) => v.stato === " ").length;

// AND A BOX OUTSIDE THE `####` IS SEEN BY NOBODY. W3.1 — 30-50 hours,
// prerequisite of the publication — sat at `###` and was invisible to this
// count, to the check on duplicate names and to the grep that the head of the file indicates.
const offLevel = [];
for (let i = 0; i < lines.length; i++) {
  if (/^(#{1,3}|#{5,6}) \[[ x~]\] /.test(lines[i])) offLevel.push(i + 1);
}

const total = entries.length;
const fullOnly = (full.length / total) * 100;
const conMeta = ((full.length + meta.length * 0.5) / total) * 100;
const halfForWhole = ((full.length + meta.length) / total) * 100;

console.log(`file: ${path.relative(process.cwd(), file)}`);
console.log(`entries with a box: ${total}`);
console.log(`  [x] closed   ${full.length}`);
console.log(`  [~] half done  ${meta.length}`);
console.log(`  [ ] open     ${open.length}`);
let defects = 0;
if (traces.length) {
  defects++;
  console.log();
  console.log(`  XX  ${traces.length} traces still have the box, and count as work:`);
  for (const v of traces) console.log(`        [${v.stato}] ${v.titolo.slice(0, 70)}`);
  console.log(`      The rule at the head of the TODO: a trace becomes «(com'era)».`);
  console.log(`      Net of those: ${realWork} really open.`);
}
if (offLevel.length) {
  defects++;
  console.log();
  console.log(`  XX  boxes outside level ####, invisible to every count: lines ${offLevel.join(", ")}`);
}
console.log();
console.log("completion, and the range is the honest number:");
console.log(`  solo [x]                      ${fullOnly.toFixed(1)} %`);
console.log(`  [x] full, [~] counted half    ${conMeta.toFixed(1)} %`);
console.log(`  [~] counted whole             ${halfForWhole.toFixed(1)} %`);
console.log();
console.log("the line to rewrite in §1 of the TODO:");
console.log(
  `  **${total} entries**: ${full.length} closed, ${meta.length} half done, ${open.length} open ` +
    `(${fullOnly.toFixed(1)}% / ${conMeta.toFixed(1)}% / ${halfForWhole.toFixed(1)}%).`
);
if (defects) process.exitCode = 1;

if (process.argv.includes("--blocchi")) {
  // The warning lights, in order of strength: a decision of the owner beats
  // everything (it is not our work), then SOLIDWORKS, then the rest.
  // FOURTH CORRECTION, on 23/9/2026: an entry marked **decisione** in its
  // title is not work to be written, it is a choice to be made -- and if it concerns
  // a file of whoever works here, it is not taken by whoever programs.
  //
  // J4 («decide what to do with the dirty document»: undo, keep or
  // redo a part on which there is recent work) ended up among the "doable
  // now". It is not: the choice belongs to the owner of the part.
  //
  // **This warning light was corrected four times in one day**, and it remains
  // a warning light. The number it prints is a starting point for looking, not
  // a verdict -- that is why it also prints the TITLES, so whoever reads can
  // dispute it instead of believing it.
  const owner = (v) =>
    /proprietario|decisa dal|force-push|deploy|tag v0|licenza commerciale/i.test(
      v.titolo + " " + v.corpo) ||
    /\*\*decisione\*\*/i.test(v.titolo);
  // THE STRONGEST WARNING LIGHT IS THE NAME OF AN API. The first version searched for
  // «**firme:**» and «sonda: si'», and left GP10 among the doable ones — which
  // talks about `InsertRib` for ten lines — because that entry is written
  // as a quotation, without the fields. An entry that names a SOLIDWORKS
  // API wants SOLIDWORKS: there is no way to measure it elsewhere.
  const api = /\bI[A-Z]\w+\.\w+\(|\bsw[A-Z]\w+_e\b|\b(?:Insert|Create|Get|Set|Add)[A-Z]\w+\(/;

  // THIRD CORRECTION, on 23/9/2026: a job that is measured ON A DOCUMENT
  // wants SOLIDWORKS even if it names no API.
  //
  // The first two drafts sent among the "doable now" Z-B7 (choosing
  // a screw from a workshop folder and opening it), Z-A11 ("runs on the
  // open document") and Y3 (opening an unknown .sldprt and telling about it).
  // None of the three can be done with the CAD off.
  //
  // **A criterion that promises work that cannot be done is worse than
  // no criterion**: the first time one tries, the second time it is
  // no longer read.
  const document =
    /documento aperto|sul documento|\.sldprt|\.sldasm|assieme aperto|pezzo aperto|una tavola aperta|apre un|aprire un|oltre il tetto|guarda_il_pezzo/i;

  // FIFTH CORRECTION, on 23/9/2026: an entry that declares itself **BLOCCATA**
  // or «forse-mai» is not work that can be done now, and it is the entry itself
  // that says so. E11 («rollback as oracle», blocked and with the measurement that does not
  // close it) ended up at the top of the list of things to do.
  //
  // Five corrections in one day. The number is useful, and **it is not a
  // verdict**: every time an entry of the list was looked at
  // closely, the list got shorter.
  const locked = (v) => /BLOCCATA|forse-mai|non stimabile/i.test(v.titolo);

  // SIXTH CORRECTION, on 24/9/2026, and this is not a regex: it is a
  // check.
  //
  // The list «si puo' fare adesso» sent me to **Y2** («dipende da
  // Y1») and to **Z-B7** («ore: dipende interamente da B1 e B3»). Both
  // declare their own dependency **in plain words**, and both depend
  // on entries that are still open. Neither of the two could be started.
  //
  // Here nothing is guessed: we read the code that the entry wrote
  // by itself and look at how that entry stands. If the cited entry does not
  // exist, **it does not block**: it would be a sentence on a typo.
  // THE TRACES DO NOT COUNT, and I fell for it the same day in which I
  // wrote the rule.
  //
  // The TODO keeps, next to a closed entry, its «traccia originale
  // tenuta per riferimento»: **same code**, and at `~` forever because
  // it is a memory, not work. There are dozens. Building the map without
  // skipping them, the last one written won — that is the trace — and `B1`
  // turned out open although the real B1 has been closed since 14/9/2026.
  //
  // Z-B7 thus ended up among the «blocked», and the thing it was waiting for was
  // already done for ten days. *A warning light that accuses a thing done sends
  // to look in the wrong place, which is worse than keeping quiet.*
  //
  // And if the same code stayed ambiguous, the entry further down wins: between
  // two lines that talk about the same thing, the closed one is the last
  // word.
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

  // SEVENTH. **W2.6** says «Finita quando: volume tolto pari all'area del
  // contorno per la profondita'; e due corpi indipendenti dopo, con
  // l'ingombro di tutti e due letto». It names no API and names
  // no document — but a volume and a bounding box **are read from a real
  // part**, and there is no other place to read them from.
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
  console.log("what blocks the open entries (warning lights, not verdicts):");
  console.log(`  owner's decision             ${groups.proprietario.length}`);
  console.log(`  wants SOLIDWORKS running     ${groups.solidworks.length}`);
  console.log(`  waits for another entry      ${groups.dipendenti.length}`);
  console.log(`  CAN BE DONE NOW              ${groups.adesso.length}`);
  console.log();
  console.log("  -- can be done now, without SOLIDWORKS --");
  for (const v of groups.adesso) console.log("     " + v);
  console.log();
  console.log("  -- waits for ANOTHER ENTRY to be closed --");
  for (const v of groups.dipendenti) console.log("     " + v);
  console.log();
  console.log("  -- waits for a decision that is not the programmer's --");
  for (const v of groups.proprietario) console.log("     " + v);
}

if (showLines) {
  console.log();
  console.log("the open ones:");
  for (const v of open) {
    const sign = /traccia originale|tenuta per riferimento/i.test(v.titolo) ? "  (traccia)" : "";
    console.log("   " + v.titolo.split("·")[0].trim().slice(0, 78) + sign);
  }
}
