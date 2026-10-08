// Sends a sequence of plans to the MCP bridge, as a client would.
//
//   node run_plans.js cases/bridge/c4r_primary.json cases/bridge/c4r_keyway.json
//   node run_plans.js --clean cases/bridge/*.json
//
// Paths with the forward slash: Windows accepts them, and this way there is no
// level of escaping to get wrong. The same line written with backslashes has
// already been eaten once by a heredoc.
//
// ── THE BENCH CLOSES WHAT IT OPENS (entry F1b) ────────────────────────────
//
// Every plan leaves open the part it created, and **the state enumerates them
// all at every reading**: 244 ms per document for `GetNext()` alone, plus 120
// ms each for title, path and type. After fifty cases the bench
// slows itself down by a minute and a half per reading.
//
// It is not about convenience: as long as the bench dirties itself, **the times it measures
// are not the product's times** — they are its own. Whoever reads a report of 27
// minutes thinks the bridge is slow.
//
// With `--clean`:
//   1. the documents that were there BEFORE are read, and named;
//   2. before every plan a clean part is opened;
//   3. at the end what the bench opened is closed, **sparing
//      those from before**: the discards are ours, not those of whoever was
//      working.
const { spawn, spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const ESE = process.env.OCE_BRIDGE || path.join(ROOT, "bin", "oce-solid-agent-mcp.exe");
const CLOSE = path.join(ROOT, "Checks", "CloseLeftovers", "bin", "CloseLeftovers.exe");
const NEW = path.join(ROOT, "Checks", "NewPart", "bin", "NewPart.exe");

const cliArgs = process.argv.slice(2);
const cleanFlag = cliArgs.includes("--clean");
const planes = cliArgs.filter(a => a !== "--clean");
if (!planes.length) { console.error("give me the plans"); process.exit(2); }

// ──────────────────────────────────────────────────────────────────────────

// WHERE THE BENCH WRITES ITS PARTS.
//
// The cases that save a file carried inside the path of the session
// scratchpad — user name included — and that was wrong twice: a
// repository destined to become public does not carry around the name of whoever
// works, and a case that points to a session folder cannot be
// re-run the week after.
//
// Now they write `{BENCH}/part/some_part.SLDPRT`, and here `{BENCH}` becomes
// `Bench/work`, which is in the .gitignore.
const WORK = path.join(__dirname, "work");

// THE NAMES SOLIDWORKS WRITES IN ITS OWN LANGUAGE. A plan that names a feature SOLIDWORKS
// wrote says {SW:feature.cut_extrude}1; here the placeholder becomes the word of the table
// (Oce.Solid.Agent.Core/Localization/solidworks-names.json) in the column SOLIDWORKS uses
// now, asked of Checks/SolidWorksLanguage.exe --columns (the same reading the library does).
const NAMES = path.join(ROOT, "Oce.Solid.Agent.Core", "Localization", "solidworks-names.json");
const LANGUAGE = path.join(ROOT, "Checks", "SolidWorksLanguage", "bin", "SolidWorksLanguage.exe");
let namesCache = null;
function namesInUse() {
  if (namesCache) return namesCache;
  const table = JSON.parse(fs.readFileSync(NAMES, "utf8").replace(/^\uFEFF/, ""));
  let columns = { feature: table.fallback_column, ui: table.fallback_column };
  const asked = spawnSync(LANGUAGE, ["--columns"], { encoding: "utf8" });
  const line = asked.status === 0 ? String(asked.stdout).trim() : "";
  const m = /^feature=(\S+) ui=(\S+)$/.exec(line);
  if (!m) throw new Error("cannot read the language of SOLIDWORKS from " + LANGUAGE + " --columns (build the checks: Checks/build-checks.ps1): «" + line + "»");
  columns = { feature: m[1], ui: m[2] };
  namesCache = { table, columns };
  return namesCache;
}
function expandNames(text) {
  if (text.indexOf("{SW:") < 0) return text;
  const { table, columns } = namesInUse();
  return text.replace(/\{SW:([A-Za-z0-9_.]+)\}/g, (all, key) => {
    const row = table.names[key];
    if (!row) throw new Error("the plan names {SW:" + key + "} and the table has no such key. It has: " + Object.keys(table.names).sort().join(", "));
    const word = row[columns[row.domain]] || row[table.fallback_column];
    if (!word) throw new Error("{SW:" + key + "} has no word in column " + columns[row.domain] + " nor in English");
    return word;
  });
}

function resolvePlaceholder(text) {
  return expandNames(text.split("{BENCH}").join(WORK.split("\\").join("/")));
}

// THE FOLDER WAS CREATED EMPTY, AND FIVE CASES COULD NOT RUN.
//
// The frame cases name `{BENCH}/profiles/FRAME_COPY.SLDLFP`. Before
// the repair with the placeholder (0.25.0) that path pointed to the
// session scratchpad, where the file really was because somebody had
// put it there by hand. The placeholder made the path portable and
// left the file without anybody to provide it: `prepareFolder` created
// `profiles/` and put nothing inside it.
//
// Measured on 14/9/2026: five cases answered «The profile ... does not exist on
// this workstation», which is a right message for a problem that was not there.
//
// WHY A COPY AND NOT THE ORIGINAL. Inserting a profile OPENS its
// `.SLDLFP` and leaves it dirty: the bench would work on the real
// standard library of the shop. It is copied, and one works on the copy.
// NO FALLBACK. Here there was the path of the library of a single
// workstation: on every other machine it did not exist, so it helped nobody, and
// meanwhile it told how that disk is laid out. Whoever does not have the variable
// finds out now with a sentence, instead of in ten minutes with an error.
const PROFILES = process.env.OCE_WELDMENT_PROFILES;
if (!PROFILES) {
  console.error("OCE_WELDMENT_PROFILES is not set: I do not know where the library");
  console.error("of standard profiles is, and there is no default path.");
  console.error('    setx OCE_WELDMENT_PROFILES "<the folder of the .SLDLFP files>"');
  process.exit(2);
}

// name in the library -> name of the copy that the cases name
const TO_COPY = {
  "TUBO STRUTTURALE QUADRO.SLDLFP": "FRAME_COPY.SLDLFP",
};

function prepareFolder() {
  for (const c of [WORK, path.join(WORK, "part"), path.join(WORK, "profiles")]) {
    if (!fs.existsSync(c)) fs.mkdirSync(c, { recursive: true });
  }

  for (const [origin, copy] of Object.entries(TO_COPY)) {
    const da = path.join(PROFILES, origin);
    const a = path.join(WORK, "profiles", copy);
    if (fs.existsSync(a)) continue;
    if (!fs.existsSync(da)) {
      // IT IS SAID, and one does not die: whoever clones the repository does not have that
      // library, and must be able to run all the other cases. But they must also
      // know WHY those five will fail, instead of reading a message
      // that blames the path.
      console.error(
        "  [bench] the profile «" + origin + "» is missing in " + PROFILES + "\n" +
        "          the frame cases will fail. Override it with OCE_WELDMENT_PROFILES.");
      continue;
    }
    fs.copyFileSync(da, a);
  }
}

function aRun(list, onThis) {
  return new Promise(resolve => {
    const requests = [
      { jsonrpc: "2.0", id: 1, method: "initialize",
        params: { protocolVersion: "2024-11-05", capabilities: {},
                  clientInfo: { name: "bench", version: "1" } } },
      { jsonrpc: "2.0", method: "notifications/initialized" },
      { jsonrpc: "2.0", id: 2, method: "tools/call",
        params: { name: "inspect_part", arguments: {} } },
    ];
    const itemName = {};
    list.forEach((f, i) => {
      requests.push({
        jsonrpc: "2.0", id: 10 + i, method: "tools/call",
        params: { name: "build",
                  arguments: {
                    steps: JSON.parse(resolvePlaceholder(fs.readFileSync(f, "utf8"))),
                  } },
      });
      itemName[10 + i] = f;
    });

    const p = spawn(ESE, [], { stdio: ["pipe", "pipe", "pipe"] });
    let leftover = "";
    let seen = 0;
    let initialState = null;
    p.stderr.on("data", d => process.stderr.write("  [sw] " + d));
    p.stdout.on("data", d => {
      leftover += d.toString();
      let i;
      while ((i = leftover.indexOf("\n")) >= 0) {
        const line = leftover.slice(0, i).trim();
        leftover = leftover.slice(i + 1);
        if (!line) continue;
        let m; try { m = JSON.parse(line); } catch (e) { continue; }
        if (m.id === 2) {
          const t = (m.result && m.result.content || []).map(c => c.text).join("\n");
          try { initialState = JSON.parse(t); } catch (e) { }
          console.log("inspect_part: ok");
          if (list.length === 0) { p.stdin.end(); clearTimeout(alarm); }
          continue;
        }
        if (itemName[m.id]) {
          const t = (m.result && m.result.content || []).map(c => c.text).join("\n");
          console.log("\n-- " + itemName[m.id] + " --");
          console.log(t);
          // The tap is closed AND the alarm clock cancelled: a setTimeout still
          // armed keeps node alive even with the bridge already gone, and the bench
          // seemed hung for two minutes at every plan.
          if (++seen === list.length) { p.stdin.end(); clearTimeout(alarm); }
        }
      }
    });
    const alarm = setTimeout(() => { try { p.kill(); } catch (e) { } }, 30 * 60 * 1000);
    p.on("exit", c => {
      console.log("\nbridge closed: " + c);
      resolve(initialState);
    });
    requests.forEach(r => p.stdin.write(JSON.stringify(r) + "\n"));
  });
}

function openTitles(docState) {
  if (!docState || !Array.isArray(docState.documents_open)) return [];
  return docState.documents_open.map(d => d && d.document_title).filter(Boolean);
}

function tool(filePath, args) {
  if (!fs.existsSync(filePath)) {
    console.error("missing " + filePath + ": run Checks\\build-checks.ps1");
    return null;
  }
  const r = spawnSync(filePath, args, { encoding: "utf8" });
  return r.stdout || "";
}

async function main() {
  prepareFolder();
  let toSpare = [];

  if (cleanFlag) {
    // Who was there before. An empty run, only to read the state.
    const docState = await aRun([], null);
    toSpare = openTitles(docState);
    console.log("documents already open, which we do not touch: " +
                (toSpare.length ? toSpare.join(", ") : "none"));
  }

  // THE TIME PER PLAN, not only the total.
  //
  // It is the number that says whether the bench is slowing ITSELF down: with the discard
  // parts on top it grows from the first case to the last, and a total does not
  // show it. Measured before `--clean`: from 236 to 271 seconds over
  // nineteen plans.
  const times = [];
  for (const f of planes) {
    if (cleanFlag) tool(NEW, []);
    const startedAt = process.hrtime.bigint();
    await aRun([f], null);
    const seconds = Number(process.hrtime.bigint() - startedAt) / 1e9;
    times.push({ plan: path.basename(f), seconds: seconds });
    console.log("  [tempo] " + path.basename(f) + ": " + seconds.toFixed(1) + " s");

    // IT IS CLOSED AFTER EVERY PLAN, NOT AT THE END.
    //
    // Closing only at the end is no use at all: the documents accumulate
    // DURING the run, and it is during it that the state enumerates them all. Measured
    // with the cleanup only at the end over nineteen plans: 20 s the first,
    // 124 the second, 210 the third, 244 the fourth — that is the same trouble as
    // before under a different name.
    if (cleanFlag) cleanUp(toSpare);
  }

  if (times.length > 1) {
    console.log("");
    console.log("=== The timings, in order of execution");
    times.forEach((t, i) =>
      console.log("  " + String(i + 1).padStart(3) + ". " +
                  t.plan.padEnd(28) + t.seconds.toFixed(1) + " s"));
    const first = times.slice(0, 3).reduce((a, t) => a + t.seconds, 0) / Math.min(3, times.length);
    const last = times.slice(-3).reduce((a, t) => a + t.seconds, 0) / Math.min(3, times.length);
    console.log("  first three: " + first.toFixed(1) + " s on average" +
                "   last three: " + last.toFixed(1) + " s");
    console.log(last > first * 1.3
      ? "  -> THE BENCH IS SLOWING ITSELF DOWN: the times are growing."
      : "  -> the times are not growing.");
  }

  if (cleanFlag) cleanUp(toSpare, true);
}

/// Closes the bench's discards, sparing whoever was there before.
function cleanUp(toSpare, tellIt) {
  const output = tool(CLOSE, toSpare.length
    ? ["--close", "--spare", toSpare.join(",")]
    : ["--close"]);
  if (!output || !tellIt) return;
  // Only the two lines that count are printed: how many it closed and how many
  // remain. The rest is the list, and in a report of fifty cases it
  // would be noise.
  output.split(/\r?\n/)
    .filter(r => /^closed:|^open documents now:/.test(r))
    .forEach(r => console.log("  " + r));
}

main().catch(e => { console.error(e); process.exit(1); });
