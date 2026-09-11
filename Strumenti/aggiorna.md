# Rigenerare `catalogo.json`

Il catalogo vero vive nel server OpenPDM, in `<un file del server>`: li'
stanno le descrizioni scritte per essere lette da un modello, gli schemi, i
limiti, e il campo `provata` che decide **cosa esiste**.

Questo ponte e' scritto in C# e non puo' importare Python, quindi si porta
dietro una copia. Una copia che si rigenera con un comando e' onesta; una
copiata a mano no — e diverge al primo cambiamento, in silenzio.

## Il comando

Serve accesso al server (dove gira il container `docker-server`):

```bash
# 1. impacchetta i sorgenti del server
cd S:/PDM/OpenPDM/server
tar -czf /tmp/server_src.tgz --exclude=__pycache__ app tests pyproject.toml
scp /tmp/server_src.tgz openpdm-server:/tmp/

# 2. esporta il catalogo da un container USA E GETTA
#    (mai dentro docker-server-1: quello e' la produzione)
scp esporta_catalogo.py openpdm-server:/tmp/
ssh openpdm-server '
  rm -rf /tmp/esporta && mkdir -p /tmp/esporta
  tar -xzf /tmp/server_src.tgz -C /tmp/esporta
  docker run --rm -v /tmp/esporta:/sorgente:ro -v /tmp:/fuori docker-server bash -c "
    cp -r /sorgente/app /app/
    cd /app && python /<un file del server> > /fuori/catalogo.json
  "'

# 3. riportalo qui
scp openpdm-server:/tmp/catalogo.json S:/PDM/SolidWorksMcp/Strumenti/catalogo.json
```

`esporta_catalogo.py` sta nel repository del server, accanto ai test.

## Cosa finisce nel file

**Solo le operazioni `provata=True`.** E' la regola del catalogo a due livelli:
un'operazione che non e' stata costruita dentro SolidWorks e misurata non si
offre a nessuno — nemmeno da qui, e specialmente da qui, perche' un ponte
serve a misurare e misurare con uno strumento non misurato non e' misurare.

Per ogni operazione: nome, famiglia, sommario, descrizione, schema degli
argomenti, precondizione.

## Come accorgersi che e' vecchio

Il ponte scrive all'avvio, su stderr:

```
[ponte] catalogo: 36 operazioni provate
```

Se quel numero non corrisponde a quello del server, il file e' indietro.
