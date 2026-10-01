# Casi scritti prima della cosa che misurano

Questa cartella **il banco non la guarda**: scandisce `Banco/cases/` e basta
(`CasiDichiarati.cs:94`). Qui dentro stanno i casi che esercitano un
comportamento **che ancora non c'è**, scritti in anticipo.

## Perché esiste, e non è un cassetto

Un caso che deve fallire, messo in `cases/`, fa diventare rosso il banco per un
motivo noto — e un banco rosso per un motivo noto è **un banco che smette di
dire qualcosa**: chi lo guarda impara a ignorare quella riga, e il giorno in
cui diventa rossa per un altro motivo non se ne accorge nessuno.

È la stessa forma di `Banco/tasks/` (voce Z-G9): *l'ingresso nasce prima di
chi lo esegue, con la sua misura accanto.*

## La regola per uscire di qui

Un caso si sposta in `Banco/cases/` **nello stesso commit** in cui arriva il
comportamento che misura, e quel commit porta un giro di banco verde. Non
prima: un caso in `cases/` è una promessa che il giro mantiene.

## Cosa c'è adesso

**One case, from 28/9/2026 (Z-C21.10, C8-2):**
`a_dimension_hidden_by_hand_does_not_count.json`. It measures that a drawing
dimension hidden by hand (IAnnotation.Visible = swAnnotationHidden) does not
count as «already dimensioned» in `quota_in_tavola`. The library does it; the
bench cannot build the situation yet: no catalogue operation hides a dimension,
and the runner has no preamble field for it. It moves to `Banco/cases/drawing/`
in the commit that brings the runner field `hide_dimensions_first`
(`{"vista": "V1"}`: every display dimension of that view is selected with
IAnnotation.Select3 and hidden with IModelDoc2.HideDimension, then read back;
one that is not swAnnotationHidden is a bench error), with a bench run.

### Before, from 24/9/2026

**Niente, ed è la cosa giusta.** I quattro casi di Z-G3 sono stati spostati in
`Banco/cases/` il 24/9/2026, lo stesso giorno in cui i due `default` sono
arrivati — `document_properties/default_material.json` con la sua
`requested_material.json`, `bodies/default_combine.json` con la sua
`requested_combine.json`.

La cartella resta vuota con questo file dentro, perché la forma serve ancora:
la prossima volta che un caso nasce prima della cosa che misura, va qui.

### Una cosa che è successa mentre erano qui, e vale la pena ricordarla

I due casi di `combina_corpi` mettevano i corpi a **120 mm** di distanza, per
rendere inconfondibili i tre esiti (unione = somma, sottrazione = il solo
principale, intersezione = zero). Rileggendo la descrizione dell'operazione
prima di spostarli è saltato fuori che **`combina_corpi` dichiara di fallire**
quando dopo l'unione i corpi sono ancora due: *«di solito non si toccano
davvero, e fallisco invece di confermarti un pezzo diverso da quello che
credi»*.

Due corpi distanti non si fondono. L'unione sarebbe fallita, e **il caso rosso
avrebbe accusato il predefinito invece della geometria** — cioè avrebbe fatto
cercare il difetto nel posto sbagliato. Adesso si sovrappongono di 40 mm e i
tre numeri restano distinti: 80000, 30000, 20000.

*Un caso si rilegge contro la descrizione dell'operazione che esercita, non
solo contro lo schema: lo schema dice cosa accetta, la descrizione dice cosa
rifiuta.*
## La decisione di contratto che questi due casi fissano

Un predefinito **non è un campo in meno**: è una scelta fatta da qualcun altro.
La voce Z-G3 nasce proprio da lì — *«nessuno ci ha pensato» e «qualcuno ci ha
pensato e ha deciso» si leggono uguali nel JSON, e solo una delle due si può
migliorare.*

Vale identico per il verso opposto: se il ponte mette S235JR e non lo dice, il
modello **crede di averlo scelto lui**, e la prossima volta non lo chiederà
nemmeno. Poi arriva un pezzo che andava in inox e nessuno sa dove sia stata
presa la decisione.

Quindi i casi non si accontentano che l'operazione riesca. Pretendono che la
risposta **dica l'assunzione**: quale valore, e che è stato assunto, non
chiesto. Si pretende che ci siano **due parole** — il valore (`S235JR`,
`unisci`) e la parola `predefinito`. Il valore da solo direbbe soltanto cosa
c'è adesso; `predefinito` dice **chi l'ha scelto**, ed è l'unica differenza
fra un aiuto e una decisione presa di nascosto.

## Una cosa che questi casi propongono e non hanno il diritto di decidere

Il campo su cui guardano si chiama **`misure.nota_assunto`** — si chiamava
`misure.assunto` finché era una proposta, ed è stato allineato alla
convenzione `nota_*` che il repository usava già (`nota_identificativi`,
`nota_volume`) quando è stato implementato, il 24/9/2026. Chi implementa può chiamarlo
diversamente o metterlo altrove — in quel caso si cambia la riga `what` dei
quattro casi, che è un minuto.

Quello che **non** si cambia è la pretesa: la risposta deve distinguere «l'ho
scelto io» da «l'ha scelto il ponte per me». Il nome del campo è un dettaglio;
quella distinzione è la voce.
