using System.Collections.Generic;

namespace Riscontro.Disegno.Catalogo
{
    /// <summary>Una voce del catalogo: cosa si puo' chiedere, e come si scrive.</summary>
    public sealed class OperazioneDisegno
    {
        /// <summary>Il nome esatto, quello che va in `disegna`.</summary>
        public string Nome;

        /// <summary>A quale famiglia appartiene.</summary>
        public string Famiglia;

        /// <summary>Una riga sola: e' quello che il modello vede per primo.</summary>
        public string Sommario;

        /// <summary>
        /// Il testo lungo. **E' l'interfaccia vera verso il modello**, molto
        /// piu' dello schema: e' qui che si dice quando NON usare
        /// un'operazione, in che unita' sono i numeri, e cosa deve esserci gia'
        /// nel documento. Si tocca con la stessa cura del codice.
        /// </summary>
        public string Descrizione;

        /// <summary>Lo schema degli argomenti.</summary>
        public Nodo Schema;

        /// <summary>Cosa deve esserci nel documento perche' abbia senso. Puo' mancare.</summary>
        public string Precondizione;

        /// <summary>
        /// SU CHE DOCUMENTO GIRA: «parte», «assieme», «tavola» — o piu'
        /// d'uno separati da una barra, «qualunque» se va su tutti,
        /// «nessuno» se non tocca il documento attivo (voce Z-D0-ter).
        ///
        /// ── PERCHE' NON BASTA LA FAMIGLIA ────────────────────────────
        ///
        /// La famiglia e' un'approssimazione, e `verifiche` lo dimostra da
        /// sola: `cartiglio` vuole una TAVOLA, `verifica_interferenze` un
        /// ASSIEME, e le altre sei una PARTE. Tre documenti diversi sotto
        /// la stessa etichetta.
        ///
        /// Finche' non era scritto, quel fatto viveva nella testa di chi ha
        /// scritto l'operazione e in una frase di prosa che nessun controllo
        /// legge. **Un'operazione che dichiara su che documento gira e'
        /// un'operazione che il modello non prova sul documento sbagliato**,
        /// e provarlo costa un turno.
        /// </summary>
        public string DocumentoRichiesto;

        /// <summary>
        /// UNA CHIAMATA VERA, in JSON, presa da un caso di banco VERDE
        /// (voce Z-G1).
        ///
        /// ── PERCHE' LO SCHEMA NON BASTA ──────────────────────────────
        ///
        /// Uno schema dice quali campi sono leciti; **non dice quale
        /// combinazione costruisce un pezzo**. Sono due cose diverse, e la
        /// seconda si impara solo vedendola.
        ///
        /// ── E NON E' INVENTATO ───────────────────────────────────────
        ///
        /// Ogni esempio e' copiato da un passo che il banco ha eseguito e
        /// misurato. Un esempio scritto a mano e' una promessa: questo e'
        /// un fatto. E' anche il motivo per cui molte operazioni non ce
        /// l'hanno — i loro casi usano selettori che vivono solo nel banco
        /// (`@faccia(verso=alto)`), e un esempio che il client non puo'
        /// copiare e' peggio di nessun esempio.
        ///
        /// ── IL RISCHIO, DICHIARATO ───────────────────────────────────
        ///
        /// **Un esempio diventa uno stampo.** Se qui c'e' 200x120x15,
        /// quel pezzo comparira' ovunque. I numeri di un esempio sono
        /// ARBITRARI: contano i campi e la loro forma, non i valori.
        /// </summary>
        public string Esempio;

        /// <summary>
        /// E' stata costruita e MISURATA dentro SOLIDWORKS?
        ///
        /// NON FINISCE NEL JSON, ED E' IL PUNTO. Finche' e' falsa l'operazione
        /// non si esporta, quindi il modello non la vede e non puo' chiederla.
        /// «Provata» qui vuol dire una cosa precisa: qualcuno ha eseguito
        /// l'operazione su un pezzo vero e ha guardato cos'e' venuto — non che
        /// il codice compila.
        ///
        /// Sta accanto al codice che esegue, e non in un file lontano, perche'
        /// una promessa scritta altrove si scorda di essere mantenuta.
        /// </summary>
        public bool Provata;
    }
}
