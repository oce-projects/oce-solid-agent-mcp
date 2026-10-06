// =============================================================================
//  zero-or-unknown.js - are «zero» and «I could not read it» the same thing?
// =============================================================================
//
//  WHY IT EXISTS (entry Z-C8)
//
//  It has already been paid for once. `IComponent2.GetMates()` returns **`null`
//  when there are zero**, not an empty array, and the bridge answered «I could not
//  read it» on a component that had zero.
//
//  The same exchange also runs the other way round, and it is the most dangerous:
//  a function that READS from the CAD and answers `0` from its `catch`. Zero is not
//  «I do not know»: zero is **a statement**. «This part has zero bodies»,
//  «zero mm3 were removed from this hole». Whoever reads has no way
//  to notice that in reality the reading failed.
//
//  It is not theory. `SwOperations.cs` carries the two opposite rules two thousand
//  lines apart:
//
//      :2122  «A volume that could not be read does not become a zero:
//              a zero would mean "it removed nothing", which is
//              a statement.»
//      :4837  «The volume of all the solid bodies, in mm3. Zero if it
//              cannot be read.»
//
//  The first is the comment of whoever found the defect; the second is the
//  function that produces it. The call site defended itself, the root did not.
//
//  ── WHAT IT TAKES, AND WHAT IT LEAVES ALONE ─────────────────────────────────
//
//  It takes three forms, all syntactic:
//
//      catch { return 0; }          an error that becomes a number
//      x == null ? 0 : x.Length     a «not there» that becomes a count
//      int n = 0; try { ... }       a MUTE catch that leaves the count
//      catch { } return n;          where it was, that is at zero or halfway
//
//  The third was found by an agent on 25/9/2026 while classifying the first two:
//  `CountFaces`, `CountDimensions`, `CountViews` had the same defect with
//  another syntax, and the probe did not see them.
//
//  It leaves alone:
//
//   1. the INPUT conversions (`Convert.ToInt32`, `Convert.ToDouble`):
//      there one is reading a badly written JSON argument, not the CAD, and zero
//      is a fallback declared elsewhere;
//   2. the points that carry the marker `ZERO-E-ZERO:` with the reason
//      next to it. **The reason is mandatory**: a marker alone would be
//      a way of silencing the probe, and this repository has seen
//      enough of those.
//
//  ── SINCE 25/9/2026 IT IS A HOOK ────────────────────────────────────────────
//
//  Up to library 0.116.0 it always exited 0: repairing them meant changing
//  the signature of library functions and rerunning the bench, and it was work,
//  not a prohibition. With all of them repaired (entry Z-C8), now **it exits 1 if a new
//  one appears** without a marker: it is in the pre-push hook. A finished work list
//  that does not become a guard fills up again.
//
//  USAGE
//      node Checks/zero-or-unknown.js
//      node Checks/zero-or-unknown.js --all      also the declared ones
//      node Checks/zero-or-unknown.js --self-test    makes it fail on purpose
// =============================================================================
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const all = process.argv.includes('--all');

const FOLDERS = ['Oce.Solid.Agent.Core', 'Tools', 'Bridge', 'Shared', 'Mcp'];

// WHERE THE MARKER IS SEARCHED FOR, and the first draft was wrong.
//
// It counted fourteen lines above the point. The reason however sits in the
// comment of the METHOD, and the comment that triggered this error has
// twenty: the marker was there, written in the right place, and the probe did not
// see it.
//
// **It is the second time in two days.** The guard of entry Z-A1 was born
// with the exact same defect — it counted raw lines and accused a block
// separated by twenty-five lines of comment. *Counting lines punishes whoever
// explains, in a repository in which explaining is the rule.*
//
// Now we go back up to the start of the member's comment (`/// <summary>`)
// instead of counting. The ceiling of 80 lines is not the rule: it is only so as not to
// go back up the whole file if a `<summary>` is not there.
const CAP = 80;

function memberWindow(lines, i) {
  let da = i;
  for (let k = i; k >= 0 && i - k < CAP; k--) {
    da = k;
    if (/\/\/\/\s*<summary>/.test(lines[k])) break;
  }
  return lines.slice(da, i + 1).join('\n');
}

const MARKER = 'ZERO-E-ZERO:';

function sources(dir, inside) {
  const p = path.join(root, dir);
  if (!fs.existsSync(p)) return;
  for (const n of fs.readdirSync(p)) {
    const q = path.join(p, n);
    const s = fs.statSync(q);
    if (s.isDirectory()) { sources(path.join(dir, n), inside); continue; }
    if (n.endsWith('.cs')) inside.push(path.join(dir, n));
  }
}

// The two forms, and the why of each is in the header.
const SHAPES = [
  { itemName: 'catch that returns a number', re: /catch\s*\{\s*return\s+0(\.0)?\s*;/ },
  { itemName: 'a «not there» that becomes a count', re: /==\s*null\s*\?\s*0\s*:/ },
];

// The input conversions: an argument is being read, not the CAD.
const INPUT = /Convert\.To(Int32|Double|Single|Int64)\s*\(/;

// THE THIRD FORM: an empty `catch { }`, and shortly after `return x;` of a
// variable born at zero in the same member.
const MUTE_CATCH = /catch\s*\{\s*\}/;
function muteCounter(lines, i) {
  let variable = null;
  for (let k = i; k < Math.min(lines.length, i + 9); k++) {
    const m = /^\s*return\s+(\w+)\s*;/.exec(lines[k]);
    if (m) { variable = m[1]; break; }
  }
  if (!variable) return false;
  const born = new RegExp('\\b(int|long|double)\\s+' + variable + '\\s*=\\s*0(\\.0)?\\s*;');
  return born.test(memberWindow(lines, i));
}

function look(lines, fileName) {
  const takenOnes = [];
  for (let i = 0; i < lines.length; i++) {
    const r = lines[i];
    let shape = SHAPES.find(f => f.re.test(r));
    if (!shape && MUTE_CATCH.test(r) && muteCounter(lines, i))
      shape = { itemName: 'a mute catch that leaves the count where it was' };
    if (!shape) continue;
    if (INPUT.test(r)) continue;

    const window = memberWindow(lines, i);
    const declared = window.indexOf(MARKER) >= 0;
    takenOnes.push({
      location: fileName + ':' + (i + 1),
      line: r.trim().slice(0, 96),
      shape: shape.itemName,
      declared: declared,
    });
  }
  return takenOnes;
}

function benchRun(list, whatItIsCalled) {
  const takenOnes = [];
  for (const f of list) {
    const text = typeof f === 'string'
      ? fs.readFileSync(path.join(root, f), 'utf8')
      : f.text;
    const itemName = typeof f === 'string' ? f.replace(/\\/g, '/') : f.itemName;
    takenOnes.push(...look(text.split(/\r?\n/), itemName));
  }

  const bareOnes = takenOnes.filter(p => !p.declared);
  const called = takenOnes.filter(p => p.declared);

  console.log('');
  console.log(whatItIsCalled + ':');
  console.log('  points that answer zero on an error path: ' + takenOnes.length);
  console.log('  of which DECLARED (zero and zero are the same thing there): ' + called.length);
  if (all) for (const d of called) console.log('    ok  ' + d.location + '  ' + d.line);

  if (bareOnes.length === 0) {
    console.log('  No point mistakes «zero» for «I do not know» without saying so.');
    return 0;
  }
  console.log('  TO LOOK AT: ' + bareOnes.length);
  for (const n of bareOnes) console.log('    !  ' + n.location + '  ' + n.line);
  return bareOnes.length;
}

// ── the check, before everything else ────────────────────────────────────────
if (process.argv.includes('--self-test')) {
  console.log('=== TEST OF THE CHECK: the same point, bare and declared ===');
  const bare = [
    'private static double VolumeMm3(IModelDoc2 model)',
    '{',
    '    try { return ReadIt(model); }',
    '    catch { return 0; }',
    '}',
  ].join('\n');
  // THE DECLARED ONE IS FAR ON PURPOSE: twenty lines of comment between the
  // marker and the point. It is the case on which this probe went wrong the
  // first time, and it is the one that must stay green.
  const said = ['/// <summary>',
    '/// ZERO-E-ZERO: here zero and "I do not know" really coincide, because',
    '/// `GetMates` returns null when they are zero -- measured on 17/9/2026.'];
  for (let k = 0; k < 20; k++) said.push('/// ' + k + ' line of explanation.');
  said.push('/// </summary>',
    'private static int HowMany(IComponent2 c)',
    '{',
    '    try { return ReadIt(c); }',
    '    catch { return 0; }',
    '}');
  const saidText = said.join('\n');
  const mute = [
    'private static int CountFaces(IModelDoc2 m)',
    '{',
    '    int n = 0;',
    '    try { n = ReadIt(m); }',
    '    catch { }',
    '    return n;',
    '}',
  ].join('\n');
  const inputOnly = [
    'private static int Whole(object v)',
    '{',
    '    try { return Convert.ToInt32(v); } catch { return 0; }',
    '}',
  ].join('\n');

  const a = benchRun([{ itemName: 'FAKE-bare', text: bare }], 'the BARE point');
  const b = benchRun([{ itemName: 'FAKE-declared', text: saidText }], 'the same point DECLARED, twenty lines away');
  const c = benchRun([{ itemName: 'FAKE-input', text: inputOnly }], 'an INPUT conversion');
  const e = benchRun([{ itemName: 'FAKE-mute', text: mute }], 'the MUTE catch (the third form)');
  console.log('');
  if (a === 1 && b === 0 && c === 0 && e === 1) {
    console.log('THE CHECK catches the bare point, STAYS SILENT on the declared one,');
    console.log('and does not confuse an argument conversion with a reading. Proven.');
  } else {
    console.log('THE CHECK DOES NOT BEHAVE AS IT SAYS: bare=' + a +
                ' (expected 1), declared=' + b + ' (expected 0), input=' + c + ' (expected 0)' +
                ', mute=' + e + ' (expected 1)');
    process.exitCode = 1;
  }
  console.log('');
}

const files = [];
for (const c of FOLDERS) sources(c, files);
console.log('sources looked at: ' + files.length);
const discovered = benchRun(files, 'in the repository');
if (discovered > 0) process.exitCode = 1;

console.log('');
console.log('Zero is not «I do not know»: zero is a CLAIM — «this part has');
console.log('zero bodies», «zero mm3 were removed from this hole». Whoever reads has');
console.log('no way of noticing that the reading failed.');
console.log('');
console.log('It is repaired with -1 (or null) for «I do not know», or with the marker');
console.log('ZERO-E-ZERO: and the reason next to it when the zero is true. It exits 1 if');
console.log('one is left without: since 25/9/2026 it is in the pre-push hook (entry Z-C8).');
