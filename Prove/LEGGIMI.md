# `Prove.Contratti` — le prove che girano dove SOLIDWORKS non c'e'

```
msbuild Prove\Prove.Contratti.csproj -p:Configuration=Release
Prove\bin\Prove.Contratti.exe
```

Esce **0** se non fallisce niente, **1** altrimenti, e scrive
`Prove\bin\prove.xml` in formato JUnit — quello che ogni CI sa leggere senza
che nessuno le insegni niente.

**128 controlli, qualche secondo, nessuna licenza.**

Quel numero non e' decorativo: **c'e' un controllo che lo rilegge da questo file
e lo confronta con quanti ne girano davvero**, ed e' l'ultimo del giro (si conta
anche da solo). Diceva `54` mentre ne giravano `113` — giusto quando fu scritto,
invecchiato da solo mentre la suite cresceva. Chi aggiunge un controllo cambia
anche questa riga, oppure il giro diventa rosso e gli dice di che numero si
tratta.

---

## Perche' un progetto a se', e non un modo del banco

Il csproj del banco risolve l'interop di Dassault con un `HintPath` dentro
`…\SOLIDWORKS\api\redist`: su una macchina senza SOLIDWORKS quel percorso non
esiste e **la compilazione fallisce**. Un flag non basta — il problema e' prima
del runtime.

Qui dentro **non c'e' nessun riferimento a `SolidWorks.Interop.*`**, e il primo
controllo del giro e' proprio quello: si guardano i riferimenti dell'assembly,
non il testo del csproj. Se qualcuno ne aggiunge uno «perche' serviva un tipo»,
su questa macchina non lo noterebbe nessuno — qui SOLIDWORKS c'e', e compila lo
stesso.

Quel che serve dalla libreria e dal ponte si prende per **riflessione** o
parlandoci **su stdio**, come farebbe un cliente qualunque.

## Cosa prova

| gruppo | |
|---|---|
| **i sorgenti stanno nei progetti** | ogni `.cs` di `Riscontro.Disegno`, `Ponte`, `Strumenti`, `Mcp`, `Banco`, `Prove` e `Installer` compare fra i `<Compile Include>` del suo `.csproj`. **Un file che non c'e' non viene compilato**, e se nessuno lo referenzia ancora la build resta VERDE e il codice non esiste: il difetto che non da' errore, applicato al progetto invece che al pezzo |
| **il valutatore delle attese** | numero giusto/sbagliato, tolleranze assolute, campo assente, elenchi, indici, selettori. E' lo **stesso file** che compila il banco (`..\Banco\Attese.cs`), non una copia |
| **il catalogo** | ogni operazione ha nome, famiglia, sommario, descrizione, schema; ogni famiglia usata e' dichiarata; **nessun percorso di questa macchina**; `Simili()` che su «asole» trova «asola» |
| **`Sessione.StessoDocumento`** | la guardia contro il blocco estruso nel pezzo di qualcun altro, che fino al 15/9/2026 **non aveva nemmeno una prova** |
| **il protocollo MCP** | `initialize` rimanda la versione ricevuta, una notifica non riceve risposta, `tools/list` da' **sei** strumenti e ognuno porta un `title` di primo livello, le annotazioni dicono la verita' (`misura` di sola lettura, `disegna` distruttivo e non idempotente), `misura` **rifiuta** `estrudi_rettangolo` nominando lo strumento con cui si fa, uno strumento sconosciuto da' `isError` **nel contenuto** e non un errore JSON-RPC **ed elenca tutti quelli che esistono**, JSON malformato da' `-32700` con id nullo, e **su stdout non c'e' niente che non sia JSON** |
| **il banco e' stato lanciato** | legge `Banco/resoconto/ultimo.json` e **rifiuta una consegna in cui nessuno ha lanciato il banco**: piu' di zero prove, nessuna rossa, non un giro parziale, la libreria di adesso, e **posteriore all'ultima modifica a `Riscontro.Disegno`** |

L'ultima riga e' la ragione per cui esiste la CI (voce F9): nessun runner ha una
licenza SOLIDWORKS e non l'avra', ma **rifiutare una consegna non provata** si
puo' fare da qualunque macchina. Per questo `ultimo.json` e' l'unico file di
`Banco/resoconto/` versionato.

> **Il valutatore delle attese DEVE stare qui.** Se e' rotto lui, tutta la suite
> con SOLIDWORKS diventa verde gratis.

## Due trappole, e come si evitano

**Il timeout non e' una comodita'.** Leggere da un sottoprocesso senza bloccarsi
vuole un timeout su ogni riga attesa: senza, una prova che fallisce **appende il
comando** invece di diventare rossa, e in una CI vuol dire un lavoro che non
finisce mai invece di un rosso che si legge.

**E il difetto silenzioso e' l'altro**: una prova di protocollo che «passa»
perche' ha letto **zero righe in attesa di zero righe**. Per questo «una notifica
non riceve risposta» non si misura con un'attesa a vuoto — si manda la notifica,
subito dopo una domanda vera, e si guarda che la **prima** riga che torna sia la
risposta a quella.

## Il BOM che ha rotto la prima riga

Appena scritto, questo gruppo ha trovato un difetto **suo**: `initialize` non
riceveva risposta, e il ponte rispondeva `-32700: Primitiva JSON non valida`.

`Process.StandardInput`, su .NET Framework, nasce con la codifica di
`Console.InputEncoding` e gli si mette subito `AutoFlush = true` — che **fa un
Flush**, e un Flush di uno `StreamWriter` UTF-8 scrive il preambolo: tre byte,
`EF BB BF`, davanti alla prima riga.

> Quello che arrivava al ponte non era JSON: erano tre byte e poi del JSON.

Il sintomo pero' diceva un'altra cosa — «initialize non risponde» — perche' la
riga d'errore non porta l'id della richiesta. Si e' visto solo **stampando le
righe arrivate** dentro il messaggio di fallimento, ed e' il motivo per cui il
dettaglio di una prova rossa qui non viene mai troncato.

E' la trappola gemella di quella che questo gruppo prova sul ponte: **su stdio,
il primo byte conta.**
