// Manda una sequenza di piani al ponte MCP, come farebbe un client.
//
//   node corri_piani.js casi/ponte/c4r_primario.json casi/ponte/c4r_cava.json
//   node corri_piani.js --pulisci casi/ponte/*.json
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

const RADICE = path.resolve(__dirname, "..");
const ESE = process.env.PONTE || path.join(RADICE, "bin", "oce-solid-agent-mcp.exe");
const CHIUDI = path.join(RADICE, "Controlli", "ChiudiScarti", "bin", "ChiudiScarti.exe");
const NUOVA = path.join(RADICE, "Controlli", "NuovaParte", "bin", "NuovaParte.exe");

const argomenti = process.argv.slice(2);
const pulisci = argomenti.includes("--pulisci");
const piani = argomenti.filter(a => a !== "--pulisci");
if (!piani.length) { console.error("dammi i piani"); process.exit(2); }

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
// `Banco/lavoro`, che sta nel .gitignore.
const LAVORO = path.join(__dirname, "lavoro");

function risolviSegnaposto(testo) {
  return testo.split("{BANCO}").join(LAVORO.split("\\").join("/"));
}

// LA CARTELLA SI CREAVA VUOTA, E CINQUE CASI NON POTEVANO GIRARE.
//
// I casi del telaio nominano `{BANCO}/profili/QUADRO_COPIA.SLDLFP`. Prima
// della riparazione col segnaposto (0.25.0) quel percorso puntava alla
// scratchpad di sessione, dove il file c'era davvero perche' qualcuno ce
// l'aveva messo a mano. Il segnaposto ha reso il percorso portabile e ha
// lasciato il file senza nessuno che lo provveda: `preparaLaCartella` creava
// `profili/` e non ci metteva dentro niente.
//
// Misurato il 14/9/2026: cinque casi rispondevano «Il profilo ... non esiste su
// questa postazione», che e' un messaggio giusto per un problema che non c'era.
//
// PERCHE' UNA COPIA E NON L'ORIGINALE. Inserire un profilo APRE il suo
// `.SLDLFP` e lo lascia sporco: il banco lavorerebbe sulla libreria normata
// vera dell'officina. Si copia, e si lavora sulla copia.
const PROFILI = process.env.PROFILI_SALDATURA ||
  "<impostazioni SOLIDWORKS>\\ELEMENTI SALDATURA\\NORMATI";

// nome nella libreria -> nome della copia che i casi nominano
const DA_COPIARE = {
  "TUBO STRUTTURALE QUADRO.SLDLFP": "QUADRO_COPIA.SLDLFP",
};

function preparaLaCartella() {
  for (const c of [LAVORO, path.join(LAVORO, "pezzo"), path.join(LAVORO, "profili")]) {
    if (!fs.existsSync(c)) fs.mkdirSync(c, { recursive: true });
  }

  for (const [origine, copia] of Object.entries(DA_COPIARE)) {
    const da = path.join(PROFILI, origine);
    const a = path.join(LAVORO, "profili", copia);
    if (fs.existsSync(a)) continue;
    if (!fs.existsSync(da)) {
      // SI DICE, e non si muore: chi clona il repository non ha quella
      // libreria, e deve poter far girare tutti gli altri casi. Ma deve anche
      // sapere PERCHE' quei cinque falliranno, invece di leggere un messaggio
      // che incolpa il percorso.
      console.error(
        "  [banco] manca il profilo «" + origine + "» in " + PROFILI + "\n" +
        "          i casi del telaio falliranno. Scavalca con PROFILI_SALDATURA.");
      continue;
    }
    fs.copyFileSync(da, a);
  }
}

function unGiro(elenco, suQuesto) {
  return new Promise(risolvi => {
    const richieste = [
      { jsonrpc: "2.0", id: 1, method: "initialize",
        params: { protocolVersion: "2024-11-05", capabilities: {},
                  clientInfo: { name: "banco", version: "1" } } },
      { jsonrpc: "2.0", method: "notifications/initialized" },
      { jsonrpc: "2.0", id: 2, method: "tools/call",
        params: { name: "guarda_il_pezzo", arguments: {} } },
    ];
    const nome = {};
    elenco.forEach((f, i) => {
      richieste.push({
        jsonrpc: "2.0", id: 10 + i, method: "tools/call",
        params: { name: "disegna",
                  arguments: {
                    passi: JSON.parse(risolviSegnaposto(fs.readFileSync(f, "utf8"))),
                  } },
      });
      nome[10 + i] = f;
    });

    const p = spawn(ESE, [], { stdio: ["pipe", "pipe", "pipe"] });
    let avanzo = "";
    let visti = 0;
    let statoIniziale = null;
    p.stderr.on("data", d => process.stderr.write("  [sw] " + d));
    p.stdout.on("data", d => {
      avanzo += d.toString();
      let i;
      while ((i = avanzo.indexOf("\n")) >= 0) {
        const riga = avanzo.slice(0, i).trim();
        avanzo = avanzo.slice(i + 1);
        if (!riga) continue;
        let m; try { m = JSON.parse(riga); } catch (e) { continue; }
        if (m.id === 2) {
          const t = (m.result && m.result.content || []).map(c => c.text).join("\n");
          try { statoIniziale = JSON.parse(t); } catch (e) { }
          console.log("guarda_il_pezzo: ok");
          if (elenco.length === 0) { p.stdin.end(); clearTimeout(sveglia); }
          continue;
        }
        if (nome[m.id]) {
          const t = (m.result && m.result.content || []).map(c => c.text).join("\n");
          console.log("\n-- " + nome[m.id] + " --");
          console.log(t);
          // Chiuso il rubinetto E annullata la sveglia: un setTimeout ancora
          // armato tiene vivo node anche a ponte gia' uscito, e il banco
          // sembrava appeso per due minuti a ogni piano.
          if (++visti === elenco.length) { p.stdin.end(); clearTimeout(sveglia); }
        }
      }
    });
    const sveglia = setTimeout(() => { try { p.kill(); } catch (e) { } }, 30 * 60 * 1000);
    p.on("exit", c => {
      console.log("\nponte chiuso: " + c);
      risolvi(statoIniziale);
    });
    richieste.forEach(r => p.stdin.write(JSON.stringify(r) + "\n"));
  });
}

function titoliAperti(stato) {
  if (!stato || !Array.isArray(stato.documenti_aperti)) return [];
  return stato.documenti_aperti.map(d => d && d.titolo).filter(Boolean);
}

function attrezzo(percorso, args) {
  if (!fs.existsSync(percorso)) {
    console.error("manca " + percorso + ": lancia Controlli\\costruisci-controlli.ps1");
    return null;
  }
  const r = spawnSync(percorso, args, { encoding: "utf8" });
  return r.stdout || "";
}

async function principale() {
  preparaLaCartella();
  let daRisparmiare = [];

  if (pulisci) {
    // Chi c'era prima. Un giro a vuoto, solo per leggere lo stato.
    const stato = await unGiro([], null);
    daRisparmiare = titoliAperti(stato);
    console.log("documenti gia' aperti, che non tocchiamo: " +
                (daRisparmiare.length ? daRisparmiare.join(", ") : "nessuno"));
  }

  // IL TEMPO PER PIANO, non solo il totale.
  //
  // E' il numero che dice se il banco sta rallentando SE STESSO: con i pezzi
  // di scarto addosso cresce dal primo caso all'ultimo, e un totale non lo
  // fa vedere. Misurato prima di `--pulisci`: da 236 a 271 secondi su
  // diciannove piani.
  const tempi = [];
  for (const f of piani) {
    if (pulisci) attrezzo(NUOVA, []);
    const partito = process.hrtime.bigint();
    await unGiro([f], null);
    const secondi = Number(process.hrtime.bigint() - partito) / 1e9;
    tempi.push({ piano: path.basename(f), secondi });
    console.log("  [tempo] " + path.basename(f) + ": " + secondi.toFixed(1) + " s");

    // SI CHIUDE DOPO OGNI PIANO, NON ALLA FINE.
    //
    // Chiudere solo in fondo non serve a niente: i documenti si accumulano
    // DURANTE il giro, ed e' durante che lo stato li enumera tutti. Misurato
    // con la pulizia solo finale su diciannove piani: 20 s il primo,
    // 124 il secondo, 210 il terzo, 244 il quarto — cioe' lo stesso guaio di
    // prima con un nome diverso.
    if (pulisci) ripulisci(daRisparmiare);
  }

  if (tempi.length > 1) {
    console.log("");
    console.log("=== I tempi, in ordine di esecuzione");
    tempi.forEach((t, i) =>
      console.log("  " + String(i + 1).padStart(3) + ". " +
                  t.piano.padEnd(28) + t.secondi.toFixed(1) + " s"));
    const primi = tempi.slice(0, 3).reduce((a, t) => a + t.secondi, 0) / Math.min(3, tempi.length);
    const ultimi = tempi.slice(-3).reduce((a, t) => a + t.secondi, 0) / Math.min(3, tempi.length);
    console.log("  primi tre: " + primi.toFixed(1) + " s di media" +
                "   ultimi tre: " + ultimi.toFixed(1) + " s");
    console.log(ultimi > primi * 1.3
      ? "  -> IL BANCO RALLENTA SE STESSO: i tempi crescono."
      : "  -> i tempi non crescono.");
  }

  if (pulisci) ripulisci(daRisparmiare, true);
}

/// Chiude gli scarti del banco, risparmiando chi c'era prima.
function ripulisci(daRisparmiare, raccontalo) {
  const uscita = attrezzo(CHIUDI, daRisparmiare.length
    ? ["--chiudi", "--risparmia", daRisparmiare.join(",")]
    : ["--chiudi"]);
  if (!uscita || !raccontalo) return;
  // Si stampano solo le due righe che contano: quante ne ha chiuse e quante
  // ne restano. Il resto e' l'elenco, e in un resoconto di cinquanta casi
  // sarebbe rumore.
  uscita.split(/\r?\n/)
    .filter(r => /^chiusi:|^documenti aperti adesso:/.test(r))
    .forEach(r => console.log("  " + r));
}

principale().catch(e => { console.error(e); process.exit(1); });
