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
