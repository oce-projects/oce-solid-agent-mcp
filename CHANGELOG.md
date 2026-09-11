# SolidWorks MCP — versioni

**REGOLA, non consuetudine: ogni modifica al codice alza la versione in
`VERSION.txt` e aggiunge una voce qui.**

Perche' e' una regola: questo ponte serve a MISURARE, e una misura senza il
numero di versione di cio' che l'ha prodotta non e' una misura — e' un
aneddoto. Se fra un mese una prova dice una cosa diversa da oggi, la prima
domanda sara' «con quale versione?», e deve avere una risposta.

Criterio (semver ridotto MAJOR.MINOR.PATCH):

- **MINOR +1** quando il ponte guadagna strumenti o capacita' visibili;
- **PATCH +1** per correzioni;
- **MAJOR** resta 0 finche' il protocollo degli strumenti non e' stabile.

---

## 0.2.0 — 11 settembre 2026

**Il ponte sta in piedi e disegna.** Quattro strumenti, protocollo completo,
provato contro un SolidWorks vero con un pezzo aperto.

```
guarda_il_pezzo   elenco_operazioni   descrivi_operazione   disegna
```

### Quattro strumenti e non trentasei

Le operazioni di disegno sono trentasei, e la tentazione e' di esporne una
per strumento. Sarebbe sbagliato per due ragioni gia' misurate nel progetto:

1. **il costo.** Gli schemi interi sono decine di migliaia di caratteri in
   ogni richiesta. Il catalogo a due livelli del server esiste per questo, e
   qui vale uguale;
2. **l'ordine giusto.** Il difetto piu' caro non e' un'operazione sbagliata:
   e' un'operazione giusta fatta **alla cieca**. Quattro strumenti in fila —
   guarda, cerca, approfondisci, fai — rendono difficile saltare il primo.

`guarda_il_pezzo` non e' un accessorio: senza, gli identificativi che gli
altri vogliono (`F1`, `E4`, `Corpo1`) non esistono.

### Il catalogo non si riscrive: si esporta

`Strumenti/catalogo.json`, rigenerato dal server con un comando
(`Strumenti/aggiorna.md`). Contiene **solo le operazioni `provata=True`** —
36 alla data di oggi. Un ponte serve a misurare, e misurare con uno
strumento non misurato non e' misurare.

### Cosa e' costato piu' del previsto

- **`Assembly.LoadFrom` da un disco di rete** fallisce con `0x80131515`,
  «Operazione non supportata»: un messaggio che non nomina ne' la rete ne'
  la fiducia, e manda a cercare dipendenze mancanti che non mancano. Serve
  `<loadFromRemoteSources enabled="true"/>` in `App.config`.
- **`IActiveDoc2` non esiste per il COM.** Chiamando per riflessione si
  passa dall'IDispatch, e li' il nome e' `ActiveDoc`: `IActiveDoc2` e' come
  lo espone l'interoperabilita' tipizzata in C#. Chiedere quello sbagliato
  non da' un errore utile — il ponte rispondeva «non c'e' nessun documento
  aperto» mentre ce n'erano venti.
- **le librerie di interoperabilita' di Dassault non si copiano** nel
  repository: si cercano nella cartella `redist` dell'installazione, con un
  `AssemblyResolve`. Copiarle vorrebbe dire ridistribuire un binario di
  terzi e legarsi a una versione.

### Un errore che aiuta invece di constatare

Chi sbaglia il nome di un'operazione quasi sempre ne ha in mente uno vicino,
e lo sbaglia di una lettera. «asole» adesso risponde *«Forse cercavi:
asola»*. Il primo tentativo cercava la parola intera dentro il nome e su
«asole» non trovava «asola» — falliva proprio nel caso per cui era stato
scritto.

### Misurato

Contro SolidWorks 29.5.1 con add-in 0.40.0.0 e il contenitore stagno
aperto:

| | |
|---|---|
| `initialize` / `tools/list` | 4 strumenti |
| `guarda_il_pezzo` | stato completo del pezzo, con `documenti_aperti` |
| `elenco_operazioni` (famiglia `tavola`) | 7 operazioni |
| `descrivi_operazione` (`asola`) | schema intero |
| nome storto (`asole`) | «Forse cercavi: asola» |
| piano con operazione inesistente | rifiutato **prima** di toccare il pezzo |
| `disegna` → `applica_materiale` | «ABS generico», letto dallo stato dopo |

L'ambiguita' e' stata dichiarata invece di risolta: «ABS» corrisponde a due
materiali e il ponte si e' fermato chiedendo quale. E' il comportamento
dell'add-in, e passa dal ponte senza essere addolcito — che e' il punto di
caricare l'add-in vero invece di riscriverlo.
