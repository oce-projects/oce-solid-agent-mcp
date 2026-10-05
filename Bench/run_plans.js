// Manda una sequenza di piani al ponte MCP, come farebbe un client.
//
//   node run_plans.js cases/bridge/c4r_primary.json cases/bridge/c4r_keyway.json
//   node run_plans.js --pulisci cases/bridge/*.json
//
// Percorsi con la barra avanti: Windows li accetta, e cosi' non c'e' nessun
// livello di escape da sbagliare. La stessa riga scritta con i backslash e'
// gia' stata mangiata una volta da un heredoc.
//
// ── IL BANCO CHIUDE QUELLO CHE APRE (voce F1b) ────────────────────────────
//
// Ogni piano lascia aperto il pezzo che ha creato, e **lo stato li enumera
// tutti a ogni lettura**: 244 ms per documento solo per `GetNext()`, piu' 120
// ms a testa per titolo, percorso e tipo. Dopo cinquanta casi il banco
// rallenta se stesso di un minuto e mezzo a lettura.
//
// Non e' comodita': finche' il banco si sporca da solo, **i tempi che misura
// non sono i tempi del prodotto** — sono i suoi. Chi legge un resoconto da 27
// minuti pensa che il ponte sia lento.
//
// Con `--pulisci`:
//   1. si leggono i documenti che c'erano PRIMA, e si nominano;
//   2. prima di ogni piano si apre una parte pulita;
//   3. alla fine si chiude quello che ha aperto il banco, **risparmiando
//      quelli di prima**: gli scarti sono i nostri, non di chi stava
//      lavorando.
const { spawn, spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

const ROOT = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(ROOT, "bin", "oce-solid-agent-mcp.exe");
const CLOSE = path.join(ROOT, "Checks", "CloseLeftovers", "bin", "CloseLeftovers.exe");
const NEW = path.join(ROOT, "Checks", "NewPart", "bin", "NewPart.exe");

const cliArgs = process.argv.slice(2);
const cleanFlag = cliArgs.includes("--pulisci");
const planes = cliArgs.filter(a => a !== "--pulisci");
if (!planes.length) { console.error("dammi i piani"); process.exit(2); }

// ──────────────────────────────────────────────────────────────────────────

// DOVE IL BANCO SCRIVE I SUOI PEZZI.
//
// I casi che salvano un file portavano dentro il percorso della scratchpad di
// sessione — nome utente compreso — e quello era sbagliato due volte: un
// repository destinato a diventare pubblico non porta in giro il nome di chi
// lavora, e un caso che punta a una cartella di sessione non si puo'
// rilanciare la settimana dopo.
//
// Adesso scrivono `{BANCO}/pezzo/tizio.SLDPRT`, e qui `{BANCO}` diventa
// `Bench/lavoro`, che sta nel .gitignore.
const WORK = path.join(__dirname, "lavoro");

function resolvePlaceholder(text) {
  return text.split("{BANCO}").join(WORK.split("\\").join("/"));
}

// LA CARTELLA SI CREAVA VUOTA, E CINQUE CASI NON POTEVANO GIRARE.
//
// I casi del telaio nominano `{BANCO}/profili/QUADRO_COPIA.SLDLFP`. Prima
// della riparazione col segnaposto (0.25.0) quel percorso puntava alla
// scratchpad di sessione, dove il file c'era davvero perche' qualcuno ce
// l'aveva messo a mano. Il segnaposto ha reso il percorso portabile e ha
// lasciato il file senza nessuno che lo provveda: `prepareFolder` creava
// `profili/` e non ci metteva dentro niente.
//
// Misurato il 14/9/2026: cinque casi rispondevano «Il profilo ... non esiste su
// questa postazione», che e' un messaggio giusto per un problema che non c'era.
//
// PERCHE' UNA COPIA E NON L'ORIGINALE. Inserire un profilo APRE il suo
// `.SLDLFP` e lo lascia sporco: il banco lavorerebbe sulla libreria normata
// vera dell'officina. Si copia, e si lavora sulla copia.
// NESSUN RIPIEGO. Qui c'era il percorso della libreria di una postazione
// sola: su ogni altra macchina non esisteva, quindi non aiutava nessuno, e
// intanto raccontava com'e' fatto quel disco. Chi non ha la variabile lo
// scopre adesso con una frase, invece che fra dieci minuti con un errore.
const PROFILES = process.env.PROFILI_SALDATURA;
if (!PROFILES) {
  console.error("PROFILI_SALDATURA non e' impostata: non so dove sia la libreria");
  console.error("dei profili normati, e non c'e' un percorso predefinito.");
  console.error('    setx PROFILI_SALDATURA "<la cartella dei .SLDLFP>"');
  process.exit(2);
}

// nome nella libreria -> nome della copia che i casi nominano
const TO_COPY = {
  "TUBO STRUTTURALE QUADRO.SLDLFP": "QUADRO_COPIA.SLDLFP",
};

function prepareFolder() {
  for (const c of [WORK, path.join(WORK, "pezzo"), path.join(WORK, "profili")]) {
    if (!fs.existsSync(c)) fs.mkdirSync(c, { recursive: true });
  }

  for (const [origin, copy] of Object.entries(TO_COPY)) {
    const da = path.join(PROFILES, origin);
    const a = path.join(WORK, "profili", copy);
    if (fs.existsSync(a)) continue;
    if (!fs.existsSync(da)) {
      // SI DICE, e non si muore: chi clona il repository non ha quella
      // libreria, e deve poter far girare tutti gli altri casi. Ma deve anche
      // sapere PERCHE' quei cinque falliranno, invece di leggere un messaggio
      // che incolpa il percorso.
      console.error(
        "  [banco] manca il profilo «" + origin + "» in " + PROFILES + "\n" +
        "          i casi del telaio falliranno. Scavalca con PROFILI_SALDATURA.");
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
                  clientInfo: { name: "banco", version: "1" } } },
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
          // Chiuso il rubinetto E annullata la sveglia: un setTimeout ancora
          // armato tiene vivo node anche a ponte gia' uscito, e il banco
          // sembrava appeso per due minuti a ogni piano.
          if (++seen === list.length) { p.stdin.end(); clearTimeout(alarm); }
        }
      }
    });
    const alarm = setTimeout(() => { try { p.kill(); } catch (e) { } }, 30 * 60 * 1000);
    p.on("exit", c => {
      console.log("\nponte chiuso: " + c);
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
    console.error("manca " + filePath + ": lancia Checks\\build-checks.ps1");
    return null;
  }
  const r = spawnSync(filePath, args, { encoding: "utf8" });
  return r.stdout || "";
}

async function main() {
  prepareFolder();
  let toSpare = [];

  if (cleanFlag) {
    // Chi c'era prima. Un giro a vuoto, solo per leggere lo stato.
    const docState = await aRun([], null);
    toSpare = openTitles(docState);
    console.log("documenti gia' aperti, che non tocchiamo: " +
                (toSpare.length ? toSpare.join(", ") : "none"));
  }

  // IL TEMPO PER PIANO, non solo il totale.
  //
  // E' il numero che dice se il banco sta rallentando SE STESSO: con i pezzi
  // di scarto addosso cresce dal primo caso all'ultimo, e un totale non lo
  // fa vedere. Misurato prima di `--pulisci`: da 236 a 271 secondi su
  // diciannove piani.
  const times = [];
  for (const f of planes) {
    if (cleanFlag) tool(NEW, []);
    const startedAt = process.hrtime.bigint();
    await aRun([f], null);
    const seconds = Number(process.hrtime.bigint() - startedAt) / 1e9;
    times.push({ piano: path.basename(f), secondi: seconds });
    console.log("  [tempo] " + path.basename(f) + ": " + seconds.toFixed(1) + " s");

    // SI CHIUDE DOPO OGNI PIANO, NON ALLA FINE.
    //
    // Chiudere solo in fondo non serve a niente: i documenti si accumulano
    // DURANTE il giro, ed e' durante che lo stato li enumera tutti. Misurato
    // con la pulizia solo finale su diciannove piani: 20 s il primo,
    // 124 il secondo, 210 il terzo, 244 il quarto — cioe' lo stesso guaio di
    // prima con un nome diverso.
    if (cleanFlag) cleanUp(toSpare);
  }

  if (times.length > 1) {
    console.log("");
    console.log("=== I tempi, in ordine di esecuzione");
    times.forEach((t, i) =>
      console.log("  " + String(i + 1).padStart(3) + ". " +
                  t.piano.padEnd(28) + t.secondi.toFixed(1) + " s"));
    const first = times.slice(0, 3).reduce((a, t) => a + t.secondi, 0) / Math.min(3, times.length);
    const last = times.slice(-3).reduce((a, t) => a + t.secondi, 0) / Math.min(3, times.length);
    console.log("  primi tre: " + first.toFixed(1) + " s di media" +
                "   ultimi tre: " + last.toFixed(1) + " s");
    console.log(last > first * 1.3
      ? "  -> IL BANCO RALLENTA SE STESSO: i tempi crescono."
      : "  -> i tempi non crescono.");
  }

  if (cleanFlag) cleanUp(toSpare, true);
}

/// Chiude gli scarti del banco, risparmiando chi c'era prima.
function cleanUp(toSpare, tellIt) {
  const output = tool(CLOSE, toSpare.length
    ? ["--chiudi", "--risparmia", toSpare.join(",")]
    : ["--chiudi"]);
  if (!output || !tellIt) return;
  // Si stampano solo le due righe che contano: quante ne ha chiuse e quante
  // ne restano. Il resto e' l'elenco, e in un resoconto di cinquanta casi
  // sarebbe rumore.
  output.split(/\r?\n/)
    .filter(r => /^chiusi:|^documenti aperti adesso:/.test(r))
    .forEach(r => console.log("  " + r));
}

main().catch(e => { console.error(e); process.exit(1); });
