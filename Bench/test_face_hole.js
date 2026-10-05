// E14 — `hole_face` takes a hole, and not a fillet.
//
//   node test_face_hole.js
//
// WHY HERE AND NOT IN THE BENCH. `face_ref: {passo, genere}` names a step
// of the SAME plan, and the bench executes one operation at a time: there the
// reference cannot even be formulated. The first writing of this check
// was in the bench and came out red for the wrong reason — «di quel passo
// non ho la geometria» — without ever reaching the code it was supposed to test.
//
// A red that fires for another reason proves nothing, and looks identical
// to one that works.
//
// ── THE DEFECT ────────────────────────────────────────────────────────────
//
// `References` looked for the «cylindrical» faces of the step: if there was just one it
// returned it, if more than one it triggered a refusal. But **a fillet leaves
// cylindrical faces like a hole**. A step that left exactly one, and
// that one was a fillet, returned it without saying anything.
//
// The criterion is geometric and measured (`Checks\HoleOrFillet`): the
// wall of a hole is CONCAVE, the surface of an external fillet is
// convex.
const { spawn, spawnSync } = require("child_process");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(ROOT, "bin", "oce-solid-agent-mcp.exe");
const NEW = path.join(ROOT, "Checks", "NewPart", "bin", "NewPart.exe");

const PLATE = {
  operation: "extrude_rectangle",
  arguments: {
    plane: "top", length_mm: 100, width_mm: 60,
    thickness_mm: 20, centered: true,
  },
};

// THE ROLE IS REACHABLE FROM ONE OPERATION ONLY, and it must be said: `shell`.
// Verified on the catalog — `hole_face` appears in the schema of `shell`
// and in no other. The first writing of this check used
// `cylindrical_cut`, which does not even accept that kind, and the refusal
// came from the schema validator instead of from the code to be tested: the
// second red that fired for the wrong reason, in the same hour.

// RED 1: step 2 is a FILLET, which leaves CONVEX cylindrical faces.
// Step 3 asks for «the wall of the hole» of that step: it does not exist.
const FILLET_IS_NOT_HOLE = [
  PLATE,
  {
    operation: "fillet_edges",
    arguments: {
      edges_ref: { step: 1, kind: "side_edges" },
      radius_mm: 5,
    },
  },
  {
    operation: "shell",
    arguments: {
      faces_ref: [{ step: 2, kind: "hole_face" }],
      thickness_mm: 4,
    },
  },
];

// GREEN: step 2 is a real hole, only one. Step 3 takes it.
const THE_HOLE_IS_PICKED = [
  PLATE,
  {
    operation: "cylindrical_cut",
    arguments: {
      face_ref: { step: 1, kind: "top_face" },
      x_mm: 0, y_mm: 0, diameter_mm: 30, through_all: true,
    },
  },
  {
    operation: "shell",
    arguments: {
      faces_ref: [{ step: 2, kind: "hole_face" }],
      thickness_mm: 4,
    },
  },
];

// RED 2: a step that leaves MORE hole walls. «The» wall does not exist, and
// choosing one would be guessing: the refusal is the right behavior, not
// a limit to be repaired.
//
// A SINGLE STEP THAT MAKES MORE HOLES IS NEEDED, and finding it cost three
// wrong attempts:
//
//   * `cylindrical_cut` makes one hole per step: no ambiguity;
//   * `linear_pattern` makes four, but wants a direction given by an axis or
//     by an edge — that is by a uid that inside this plan does not exist
//     yet, because the plate is created by step 1;
//   * `mirror` makes one new one, and the original belongs to the step before:
//     the mirror step has **only one** wall, and taking it is right.
//
// `hole_wizard` instead says so itself in the schema: «several equal holes go
// in one single call». Four holes, one step, four walls.
//
// The lesson that the three attempts cost: **a test case must not
// depend on geometry that the case itself is creating**, and «which operation
// leaves N faces» is read in the catalog instead of being deduced.
const MULTIPLE_WALLS = [
  PLATE,
  {
    operation: "hole_wizard",
    arguments: {
      face_ref: { step: 1, kind: "top_face" },
      size: "M10",
      through_all: true,
      positions: [
        { x_mm: -30, y_mm: -15 }, { x_mm: 30, y_mm: -15 },
        { x_mm: 30, y_mm: 15 }, { x_mm: -30, y_mm: 15 },
      ],
    },
  },
  {
    operation: "shell",
    arguments: {
      faces_ref: [{ step: 2, kind: "hole_face" }],
      thickness_mm: 4,
    },
  },
];

// ──────────────────────────────────────────────────────────────────────────

let nextId = 1;
const expectation = new Map();
let leftover = "";

const bridge = spawn(ESE, [], { stdio: ["pipe", "pipe", "pipe"] });
bridge.stderr.on("data", () => {});
bridge.stdout.on("data", d => {
  leftover += d.toString();
  let i;
  while ((i = leftover.indexOf("\n")) >= 0) {
    const line = leftover.slice(0, i).trim();
    leftover = leftover.slice(i + 1);
    if (!line) continue;
    let m; try { m = JSON.parse(line); } catch (e) { continue; }
    const chi = expectation.get(m.id);
    if (chi) { expectation.delete(m.id); chi(m); }
  }
});

function send(method, parameters) {
  const id = nextId++;
  return new Promise(resolve => {
    expectation.set(id, resolve);
    bridge.stdin.write(JSON.stringify(
      { jsonrpc: "2.0", id: id, method: method, params: parameters }) + "\n");
  });
}

function warn(method) {
  bridge.stdin.write(JSON.stringify({ jsonrpc: "2.0", method: method }) + "\n");
}

async function invoke(tool, args) {
  const m = await send("tools/call", { name: tool, arguments: args || {} });
  const r = m.result || {};
  const text = (r.content || []).filter(c => c.type === "text")
                                 .map(c => c.text).join("\n");
  let data = null;
  const i = text.indexOf("{");
  if (i >= 0) { try { data = JSON.parse(text.slice(i)); } catch (e) {} }
  return { testo: text, dati: data, errore: !!r.isError };
}

let failed = 0;
function check(itemName, ok, detail) {
  if (!ok) failed++;
  console.log((ok ? "  OK   " : "  NO   ") + itemName.padEnd(52) + (detail || ""));
}

function shorter(s) {
  if (!s) return "(niente)";
  const one = s.replace(/[\r\n]+/g, " ");
  return one.length > 150 ? one.slice(0, 150) + "..." : one;
}

// THE STEP'S ERROR, not the whole text of the answer: inside there is also the
// state of the document, and looking for a word in it would give false positives.
function stepsError(data) {
  if (!data || !Array.isArray(data.outcomes)) return "";
  return data.outcomes.filter(e => e && e.errore).map(e => e.errore).join(" | ");
}

async function plane(itemName, steps) {
  const r = spawnSync(NEW, [], { encoding: "utf8" });
  if (r.status !== 0) { check(itemName, false, "parte nuova non creata"); return null; }
  await invoke("inspect_part");
  return await invoke("build", { steps: steps });
}

(async function () {
  await send("initialize", {
    protocolVersion: "2024-11-05", capabilities: {},
    clientInfo: { name: "prova_faccia_foro", version: "1" },
  });
  warn("notifications/initialized");

  console.log("");
  console.log("=== hole_face: un foro si', un raccordo no (E14) ===");
  console.log("");

  // ── RED 1 ──────────────────────────────────────────────────────────────
  const a = await plane("ROSSO 1", FILLET_IS_NOT_HOLE);
  if (a) {
    const why = stepsError(a.dati);
    check("ROSSO: «la parete del foro» di un RACCORDO e' rifiutata",
          a.errore && /convess/i.test(why),
          a.errore ? shorter(why) : "NON ha rifiutato: ha preso la faccia del raccordo");
  }

  // ── GREEN ──────────────────────────────────────────────────────────────
  const b = await plane("VERDE", THE_HOLE_IS_PICKED);
  if (b) {
    check("un foro vero, uno solo, si prende",
          !b.errore,
          b.errore ? shorter(stepsError(b.dati)) :
                     "volume " + (b.dati || {}).volume_after_mm3);
  }

  // ── RED 2 ──────────────────────────────────────────────────────────────
  const c = await plane("ROSSO 2", MULTIPLE_WALLS);
  if (c) {
    const why = stepsError(c.dati);
    check("ROSSO: piu' pareti di foro, «la» parete non esiste",
          c.errore && /pareti di foro/i.test(why),
          c.errore ? shorter(why) : "NON ha rifiutato: ne ha scelta una a caso");
  }

  console.log("");
  console.log(failed === 0 ? "Tutto torna." : failed + " prove fallite.");
  bridge.stdin.end();
  process.exitCode = failed === 0 ? 0 : 1;
})();
