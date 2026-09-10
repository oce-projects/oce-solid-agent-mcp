# SolidWorks MCP

Un ponte MCP per **disegnare in SOLIDWORKS**: espone come strumenti le
operazioni di disegno gia' misurate del progetto OpenPDM, cosi' che un modello
qualunque — locale o remoto — possa costruire un pezzo e, soprattutto,
**guardare cos'e' venuto**.

## Perche' esiste

OpenPDM ha un assistente che disegna: un modello locale propone un piano, il
server lo valida, l'add-in lo esegue dentro SolidWorks, e un progettista
conferma prima che il pezzo esista. Quella catena e' il prodotto e resta il
prodotto.

Il problema e' stato sempre lo stesso: **misurare**. Ogni difetto vero di
quell'assistente e' venuto fuori disegnando davvero, e ogni giro di prova
costava un balletto di file fra due macchine. Con questo ponte un giro e' una
chiamata.

E c'e' una seconda ragione, meno ovvia e piu' importante. Il modello che gira
in officina e' piccolo. Finche' l'unico modo di provare il sistema e' passare
da lui, non si distingue **«mancano gli strumenti»** da **«il modello non ce la
fa»**. Con questo ponte si puo' far disegnare lo stesso pezzo a un modello
grande e a uno piccolo, sulla stessa strada e con gli stessi controlli: la
differenza che resta e' la risposta.

## Cosa NON e'

Non e' un modo per far disegnare a un modello in rete i pezzi dell'azienda. Il
prodotto resta l'assistente locale, offline, con il pannello di conferma. Qui
si misura e si sviluppa.

## Come funziona

Un'applicazione da riga di comando che parla **MCP su stdio** e carica per
riflessione `OpenPDM.SwAddin.dll`, l'add-in vero. Non riscrive le operazioni:
usa quelle, cosi' cio' che si prova di qui e' esattamente cio' che il
progettista avra' in mano.

```
modello  <--MCP/stdio-->  SolidWorksMcp.exe  --riflessione-->  OpenPDM.SwAddin.dll
                                                                      |
                                                                  SolidWorks
```

## Stato

Vedi [CHANGELOG.md](CHANGELOG.md). La versione sta in `VERSION.txt` e sale a
ogni modifica: e' una regola, non una consuetudine.

## Vincoli che non si negoziano

- **Serve SolidWorks aperto.** Il ponte si aggancia a una sessione che c'e',
  non ne avvia una.
- **Niente scritture fuori dal documento aperto.** Il ponte non salva nel
  vault, non tocca librerie, non installa niente.
- **Nessuna operazione non misurata.** Se un'operazione non e' stata costruita
  dentro SolidWorks e verificata con un numero, non si espone. E' la regola
  che regge tutto OpenPDM e vale anche qui.
