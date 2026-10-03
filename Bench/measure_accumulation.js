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

const RADICE = path.resolve(__dirname, "..");
const NODE = process.execPath;
const CORRI = path.join(__dirname, "run_plans.js");
const NUOVA = path.join(RADICE, "Checks", "NewPart", "bin", "NewPart.exe");
const CHIUDI = path.join(RADICE, "Checks", "CloseLeftovers", "bin", "CloseLeftovers.exe");

const quante = parseInt(process.argv[2] || "12", 10);
const piano = process.argv[3] || path.join(__dirname, "cases", "bridge", "c4s_round.json");

function secondi(cosa) {
  const t0 = process.hrtime.bigint();
  cosa();
  return Number(process.hrtime.bigint() - t0) / 1e9;
}

function giro(chiudiOgniVolta) {
  spawnSync(CHIUDI, ["--chiudi"], { encoding: "utf8" });
  const tempi = [];
  for (let i = 0; i < quante; i++) {
    spawnSync(NUOVA, [], { encoding: "utf8" });
    tempi.push(secondi(() =>
      spawnSync(NODE, [CORRI, piano], { encoding: "utf8" })));
    if (chiudiOgniVolta) spawnSync(CHIUDI, ["--chiudi"], { encoding: "utf8" });
  }
  const restano = spawnSync(CHIUDI, [], { encoding: "utf8" }).stdout || "";
  return { tempi, restano: (restano.split(/\r?\n/)[0] || "").trim() };
}

function racconta(titolo, r) {
  console.log("");
  console.log("=== " + titolo);
  console.log("  " + r.tempi.map(t => t.toFixed(1)).join("  "));
  const primo = r.tempi[0];
  const ultimo = r.tempi[r.tempi.length - 1];
  console.log("  primo " + primo.toFixed(1) + " s, ultimo " + ultimo.toFixed(1) +
              " s  (x" + (ultimo / primo).toFixed(1) + ")");
  console.log("  alla fine: " + r.restano);
  return { primo, ultimo };
}

console.log("Lo stesso piano " + quante + " volte: " + path.basename(piano));

const senza = racconta("parte nuova ogni volta, MAI chiusa", giro(false));
const con = racconta("parte nuova ogni volta, CHIUSA dopo ogni piano", giro(true));

console.log("");
console.log(senza.ultimo > senza.first * 1.5 && con.ultimo < con.first * 1.5
  ? "Il banco rallentava se stesso, e chiudendo non lo fa piu'."
  : "Nessuna differenza leggibile fra i due modi su questa macchina.");
