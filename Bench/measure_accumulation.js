// QUANTO COSTA UN DOCUMENTO DI SCARTO LASCIATO APERTO.
//
// La voce F1b dice che il banco rallenta se stesso: ogni piano lascia aperto
// il pezzo che ha creato, e lo stato li enumera tutti a ogni lettura. Qui si
// misura, invece di ripeterlo.
//
// LO STESSO PIANO, N VOLTE, IN DUE MODI. E' l'unico confronto che isola la
// variabile giusta: piani diversi hanno costi diversi — una ruota da 42 denti
// ci mette dieci volte un tondo — e mettendo in fila piani diversi si legge
// l'ordine in cui sono scritti, non l'accumulo. Con lo stesso piano ripetuto,
// l'unica cosa che cambia fra la prima volta e l'ultima e' quanti documenti
// ci sono addosso.
//
//   node measure_accumulation.js [quante] [piano]
//
// Predefiniti: 12 giri su cases/bridge/c4s_round.json.
const { spawnSync } = require("child_process");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const NODE = process.execPath;
const RUN = path.join(__dirname, "run_plans.js");
const NEW = path.join(ROOT, "Checks", "NewPart", "bin", "NewPart.exe");
const CLOSE = path.join(ROOT, "Checks", "CloseLeftovers", "bin", "CloseLeftovers.exe");

const howMany = parseInt(process.argv[2] || "12", 10);
const plane = process.argv[3] || path.join(__dirname, "cases", "bridge", "c4s_round.json");

function seconds(what) {
  const t0 = process.hrtime.bigint();
  what();
  return Number(process.hrtime.bigint() - t0) / 1e9;
}

function benchRun(closeEachTime) {
  spawnSync(CLOSE, ["--chiudi"], { encoding: "utf8" });
  const times = [];
  for (let i = 0; i < howMany; i++) {
    spawnSync(NEW, [], { encoding: "utf8" });
    times.push(seconds(() =>
      spawnSync(NODE, [RUN, plane], { encoding: "utf8" })));
    if (closeEachTime) spawnSync(CLOSE, ["--chiudi"], { encoding: "utf8" });
  }
  const remain = spawnSync(CLOSE, [], { encoding: "utf8" }).stdout || "";
  return { tempi: times, restano: (remain.split(/\r?\n/)[0] || "").trim() };
}

function tell(docTitle, r) {
  console.log("");
  console.log("=== " + docTitle);
  console.log("  " + r.tempi.map(t => t.toFixed(1)).join("  "));
  const first = r.tempi[0];
  const last = r.tempi[r.tempi.length - 1];
  console.log("  primo " + first.toFixed(1) + " s, ultimo " + last.toFixed(1) +
              " s  (x" + (last / first).toFixed(1) + ")");
  console.log("  alla fine: " + r.restano);
  return { primo: first, ultimo: last };
}

console.log("Lo stesso piano " + howMany + " volte: " + path.basename(plane));

const without = tell("parte nuova ogni volta, MAI chiusa", benchRun(false));
const con = tell("parte nuova ogni volta, CHIUSA dopo ogni piano", benchRun(true));

console.log("");
console.log(without.ultimo > without.first * 1.5 && con.ultimo < con.first * 1.5
  ? "Il banco rallentava se stesso, e chiudendo non lo fa piu'."
  : "Nessuna differenza leggibile fra i due modi su questa macchina.");
