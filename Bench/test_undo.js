// Tests the `undo` tool (entry R1) by talking to the bridge as an MCP client.
//
//   node test_undo.js
//
// WHY HERE AND NOT IN THE BENCH. `Oce.Solid.Agent.Bench` tests the LIBRARY by
// reflection; `undo` is not in the library — it is a tool of the bridge,
// and the part that counts (the memory of the last plan, the refusals) lives in
// `McpTools`. Testing it from the library would mean testing something else.
//
// ── WHAT IT ASSERTS, AND THE THREE REDS ──────────────────────────────────
//
// The green is one: a plan of two steps is withdrawn entirely and the part
// goes back to volume zero. The reds are three, and they are the reason why this file
// exists — an «undo» that works is not the hard part, an «undo» that
// REFUSES is:
//
//   R1a  with no plan executed                -> it must refuse
//   R1b  called twice in a row                -> the second must refuse,
//        because it would remove something that this bridge did not do
//   R1c  with another document in front       -> it must refuse and say so
//
// Without these three, «always press undo» would pass the check.
//
// Paths with the forward slash: Windows accepts them, and this way there is no
// level of escaping to get wrong.
const { spawn, spawnSync } = require("child_process");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const ESE = process.env.OCE_BRIDGE || path.join(ROOT, "bin", "oce-solid-agent-mcp.exe");
const NEW = path.join(ROOT, "Checks", "NewPart", "bin", "NewPart.exe");

const PLAN_TWO_STEPS = [
  {
    operation: "extrude_rectangle",
    arguments: {
      plane: "top", length_mm: 200, width_mm: 100,
      thickness_mm: 20, centered: true,
    },
  },
  {
    operation: "cylindrical_cut",
    arguments: {
      face_ref: { step: 1, kind: "top_face" },
      x_mm: 0, y_mm: 0, diameter_mm: 12, through_all: true,
    },
  },
];

const PLAN_ONE_STEP = [
  {
    operation: "extrude_rectangle",
    arguments: {
      plane: "top", length_mm: 80, width_mm: 80,
      thickness_mm: 10, centered: true,
    },
  },
];

// ──────────────────────────────────────────────────────────────────────────
// the MCP client, one request at a time

let nextId = 1;
const expectation = new Map();
let leftover = "";

const bridge = spawn(ESE, [], { stdio: ["pipe", "pipe", "pipe"] });
bridge.stderr.on("data", () => {});   // the service messages are not answers
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

// The text of an answer, plus `isError`. An operation that fails is not
// a protocol error: it is in the content, and that is where it must be looked at.
async function invoke(tool, args) {
  const m = await send("tools/call",
    { name: tool, arguments: args || {} });
  const r = m.result || {};
  const text = (r.content || []).map(c => c.text).filter(Boolean).join("\n");
  let data = null; try { data = JSON.parse(text); } catch (e) {}
  return { text: text, data: data, failed: !!r.isError };
}

// ──────────────────────────────────────────────────────────────────────────

let failed = 0;
function check(itemName, ok, detail) {
  if (!ok) failed++;
  console.log((ok ? "  OK   " : "  NO   ") + itemName.padEnd(56) + (detail || ""));
}

function shorter(s) {
  if (!s) return "(nothing)";
  const one = s.replace(/[\r\n]+/g, " ");
  return one.length > 100 ? one.slice(0, 100) + "..." : one;
}

function cleanPart() {
  const r = spawnSync(NEW, [], { encoding: "utf8" });
  return r.status === 0;
}

async function benchRun() {
  await send("initialize", {
    protocolVersion: "2024-11-05", capabilities: {},
    clientInfo: { name: "probe_undo", version: "1" },
  });
  warn("notifications/initialized");

  // The tool must BE THERE, and declare itself for what it does.
  const list = await send("tools/list", {});
  const tools = (list.result || {}).tools || [];
  const names = tools.map(function (t) { return t.name; });
  check("undo is among the tools", names.indexOf("undo") >= 0,
        names.join(", "));
  if (names.indexOf("undo") < 0) return;

  const its = tools.filter(function (t) { return t.name === "undo"; })[0] || {};
  const ann = its.annotations || {};
  check("undo declares itself destructive and not idempotent",
        ann.destructiveHint === true && ann.idempotentHint === false &&
        ann.readOnlyHint === false,
        JSON.stringify(ann));

  // ── RED 1: nothing to withdraw ─────────────────────────────────────────
  // This is the FIRST call of the conversation: the bridge has executed
  // no plan, so it does not know what it would remove.
  const empty = await invoke("undo");
  check("RED 1: with no plan, it refuses",
        empty.failed && /no plan to withdraw/i.test(empty.text),
        shorter(empty.text));

  if (!cleanPart()) {
    check("a clean part", false, "NewPart did not answer");
    return;
  }

  const seen = await invoke("inspect_part");
  check("the clean part can be read", !seen.failed && seen.data != null,
        seen.data ? "volume " + seen.data.volume_mm3 : shorter(seen.text));

  // ── THE GREEN: two steps, withdrawn together ───────────────────────────
  const done = await invoke("build", { steps: PLAN_TWO_STEPS });
  const volumeAfterPlan = done.data ? done.data.volume_after_mm3 : null;
  check("the two-step plan builds", !done.failed && volumeAfterPlan > 0,
        done.failed ? shorter(done.text) : "volume " + volumeAfterPlan);
  if (done.failed) return;

  const withdrawn = await invoke("undo");
  const d = withdrawn.data || {};
  check("undo takes back the whole plan",
        !withdrawn.failed && d.undone === true,
        withdrawn.failed ? shorter(withdrawn.text) : "undone");
  check("the volume goes back to zero",
        d.volume_after_mm3 != null && Math.abs(d.volume_after_mm3) < 0.001,
        "volume " + d.volume_after_mm3 + " (era " + d.volume_before_mm3 + ")");
  check("the features go back to zero",
        d.features_after === 0,
        d.features_before + " -> " + d.features_after);

  // THE NEW STATE COMES WITH THE ANSWER, and it is not an extra: after an
  // undo the identifiers are different, and whoever reads must be able to use them
  // without one more reading.
  //
  // `volume_mm3` IS NOT PROBED. The first writing of this line did, and
  // came out red on code that worked: after the undo the part
  // is EMPTY, and on an empty part the state writes no volume — it says so
  // two lines above, «the clean part reads: volume undefined».
  // An assertion on a field that legitimately is not there does not prove
  // the absence of the state: it proves that whoever wrote it had not read the state.
  check("the answer carries the current state",
        d.state_after != null &&
        typeof d.state_after.document_title === "string" &&
        ("solid_body" in d.state_after),
        d.state_after ? "title «" + d.state_after.document_title + "», solid_body " +
                       d.state_after.solid_body
                     : "missing");

  // ── RED 2: twice in a row ──────────────────────────────────────────────
  // The first undo consumed the memory. The second would remove
  // what was there BEFORE the plan, which this bridge did not do.
  const bis = await invoke("undo");
  check("RED 2: the second time it refuses",
        bis.failed && /no plan to withdraw/i.test(bis.text),
        shorter(bis.text));

  // ── RED 3: another document in front ───────────────────────────────────
  const other = await invoke("build", { steps: PLAN_ONE_STEP });
  check("a second plan builds", !other.failed,
        other.failed ? shorter(other.text)
                     : "volume " + (other.data || {}).volume_after_mm3);
  if (other.failed) return;

  if (!cleanPart()) {
    check("RED 3: with another document in front, it refuses", false,
          "NewPart did not answer");
    return;
  }
  const wrong = await invoke("undo");
  check("RED 3: with another document in front, it refuses",
        wrong.failed && /document changed/i.test(wrong.text),
        shorter(wrong.text));
}

benchRun()
  .then(function () {
    console.log("");
    console.log(failed === 0 ? "Everything adds up." : failed + " checks failed.");
    bridge.stdin.end();
    process.exitCode = failed === 0 ? 0 : 1;
  })
  .catch(function (e) {
    console.error("the run broke: " + (e && e.message));
    bridge.stdin.end();
    process.exitCode = 2;
  });
