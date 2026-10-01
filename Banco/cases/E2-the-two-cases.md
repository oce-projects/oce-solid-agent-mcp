# E2 — I due casi che oggi falliscono

Due piani scritti a mano, eseguibili **senza nessun modello linguistico**, con i
numeri attesi accanto a ogni passo. Servono a fissare il comportamento di oggi
**prima** di ripararlo: un rosso che non si e' visto non si sa se diventa verde.

Si lanciano cosi', su un documento vuoto:

```
Controlli\ChiudiScarti\bin\ChiudiScarti.exe --chiudi
Controlli\NuovaParte\bin\NuovaParte.exe
node Banco\corri_piani.js Banco\cases\bridge\e2_block.json
node Banco\corri_piani.js Banco\cases\bridge\e2_case_a.json
```

---

## Il blocco di partenza — `e2_block.json`

`estrudi_rettangolo` {frontale, 100 x 60 x 20, centrato}

| | |
|---|---|
| ingombro | `[100, 60, 20]` |
| volume | **120.000** mm³ |

Le sei facce, con la firma che lo stato ne da':

```
F1  6000  [0,0,20]   [0,0,1]
F2  6000  [0,0,0]    [0,0,-1]
F3  2000  [0,-30,0]  [0,-1,0]
F4  2000  [0,30,0]   [0,1,0]
F5  1200  [-50,0,0]  [-1,0,0]
F6  1200  [50,0,0]   [1,0,0]
```

---

## CASO A — il rifiuto · `e2_case_a.json`

Si cita **F6**, la faccia di destra, e poi si allunga il pezzo.

| passo | atteso |
|---|---|
| `modifica_quota D1@Schizzo1 100 → 160` | riuscita, volume **192.000** = 120.000 · 160/100 |
| `taglio_cilindrico` Ø10 su **F6** | **riuscita, e DICE di quanto si e' spostata** |

**Fino alla 0.25.2 era un RIFIUTO**, e la voce E2 l'aveva scritto cosi':

```
«F6» adesso indica un'altra parte del pezzo (spostata di 60 mm). Il pezzo e'
cambiato da quando hai confermato: non lavoro sulla faccia sbagliata.
```

**Dalla 0.26.0 (voce E6) il riferimento persistente ritrova la faccia vera**, e
il passo riesce spiegando cos'e' successo invece di fermarsi. Misurato il
15/9/2026, ponte 0.40.0 — volume **191.607,3**, tolti 392,7 mm³:

```
la faccia «F6» l'ha ritrovata SOLIDWORKS dal suo riferimento persistente: si
e' spostata di 60 mm, da [50, 0, 0] a [110, 0, 0]. Il pezzo e' cambiato da
quando l'hai guardato, ma la faccia e' quella giusta — se il piano contava
sulle misure di prima, ricontrolla il risultato. Per quel che vale: «F6»
stava al 100% del lato X e ci sta ancora — il lato e' passato da 100 a 160
mm. Puo' essere la stessa faccia cresciuta col pezzo, ma questo non lo decido
io: l'identita' si misura in millimetri.
```

L'ultima frase e' della voce E10, ed e' **solo una spiegazione**: la faccia
l'aveva gia' scelta il riferimento persistente, e il giudizio l'aveva gia' dato
`Corrobora`. La frazione dell'ingombro non entra in nessuna decisione — e c'e'
una prova che legge il sorgente per assicurarsene.

> **60 mm, non 30.** La voce del TODO si aspettava 30, supponendo che il pezzo
> crescesse **simmetrico** attorno al centro. Non lo fa: cresce da una parte
> sola, quindi la faccia di destra si sposta di tutti i 60 mm della crescita.
> E' una differenza che si vede solo misurando, e cambia il numero che si scrive
> nel caso di prova.

**Questo caso e' il comportamento GIUSTO**, ed e' qui per due ragioni: perche' la
difesa resti visibile quando E3 cambiera' il ritrovamento, e perche' il numero
nel messaggio non cambi per sbaglio.

---

## CASO B — il difetto silenzioso · `e2_pocket.json` poi `e2_case_b.json`

**E' il caso che conta.**

### La preparazione — `e2_pocket.json`

`taglio_rettangolare` sulla faccia in alto (F1), tasca 40 x 40 centrata in Y,
profondita' 8.

| | atteso |
|---|---|
| volume | **107.200** = 120.000 − 40·40·8 |
| faccia in alto residua | **4400** mm² a `[0,0,20]` |
| fondo della tasca | **1600** mm² a `[0,0,12]` |

> La tasca va centrata in Y. Con la larghezza spostata a `y_mm: 20` esce dal
> bordo — la faccia va da −30 a +30 — e viene tagliata a 40 x 30 = 1200 mm².
> Misurato: volume 110.400 invece di 107.200, e i numeri del caso non tornano
> piu' con quelli scritti qui.

### Il piano — `e2_case_b.json`

| passo | atteso |
|---|---|
| `modifica_quota D1@Estrusione-Estrusione1 20 → 12` | riuscita, volume **59.200** |
| `taglio_cilindrico` Ø10 prof. 4 su **F5** (il fondo della tasca) | «riuscita», **e il pezzo e' sbagliato** |

### Cosa succede, e perche' nessuno se ne accorge

Dopo la modifica il pezzo e' spesso 12: il fondo della tasca scende a **Z=4**, e
la faccia in alto scende a **Z=12** — cioe' **esattamente dove il piano si
aspetta il fondo**.

Rileggendo lo stato dopo la modifica:

```
F2  4400  [0,0,12]     <- la faccia in ALTO, dove il piano cerca il fondo
F3  1600  [0,0,4]      <- il fondo della tasca, dove il piano non guarda
```

La firma che il piano porta con se' dice: punto `[0,0,12]`, area **1600**. Il
ritrovamento trova una sola faccia che combacia sul **punto** — e quella ha area
**4400**, cioe' quasi il triplo.

```
[agente] la faccia «F5» si era spostata: ritrovata dalla firma
[agente] creata feature Taglio-Estrusione2
riuscita: true
```

**Il punto ha vinto sull'area.** E' la frase piu' corta che descrive il difetto.

### I due pezzi, a confronto

| | volume | il foro |
|---|---|---|
| **giusto** (foro sul fondo della tasca) | **58.885,8** | cilindro **125,66** mm² = π·10·4, a `[5,0,0]` |
| **sbagliato** (quello che esce oggi) | **59.042,9** | cilindro **62,83** mm² = π·10·2, a `[-5,0,8]` |

**157,1 mm³ di differenza, lo 0,27%.** Il foro sbagliato e' a cavallo del bordo
della tasca: meta' cade nel vuoto e meta' nel materiale, e viene fuori un
mezzo foro profondo 2 invece di un foro intero profondo 4.

Il pezzo giusto si ottiene rileggendo lo stato dopo la modifica e citando il
fondo con il suo identificativo NUOVO (`e2_case_b_correct.json`, che cita F3).

> Ricostruzione pulita, nessun errore, nessun avviso, «riuscita: true». Il
> difetto non e' «si rifiuta un lavoro legittimo»: e' **«si lavora sulla faccia
> sbagliata dicendo che e' andata bene»**.

---

## Cosa deve cambiare (voce E3)

Il ritrovamento a una firma sola non basta, e il caso B dice perche': **una
corroborazione**. Il punto combacia e l'area no — e una faccia che ha cambiato
area del 175% non e' la stessa faccia che si era guardata.

La strada non e' rifiutare di piu': e' **chiedere una seconda conferma prima di
dire «ritrovata»**, e quando le due non concordano, dirlo invece di scegliere.
