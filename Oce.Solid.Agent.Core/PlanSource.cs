using System.Collections.Generic;
using System.Threading.Tasks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// Da dove arrivano i passi del piano.
    ///
    /// E' l'UNICO confine inventato in tutta l'estrazione: undici file su
    /// dodici non hanno bisogno di nessuna interfaccia, perche' parlano solo
    /// con SOLIDWORKS. `EsecutorePiano` invece parlava con il server di
    /// OpenPDM, e senza questo confine si sarebbe portato dietro il PDM dentro
    /// la libreria che esiste per non dipenderne.
    ///
    /// Chi la implementa decide da dove vengono i passi: un server, un file
    /// JSON, un elenco in memoria per una prova. L'esecutore non lo sa e non
    /// deve saperlo.
    /// </summary>
    public interface IFontePiano
    {
        /// <summary>
        /// Il prossimo passo, dato lo stato del documento adesso.
        ///
        /// SI CHIEDE OGNI VOLTA invece di scaricare tutto il piano perche' il
        /// controllo va rifatto: fra il primo passo e l'ultimo possono passare
        /// minuti, e in mezzo il documento puo' essere cambiato.
        ///
        /// **Un guasto NON si racconta come «finito»: si solleva.** Vedi la
        /// nota su <see cref="ProssimoPasso"/>.
        /// </summary>
        Task<ProssimoPasso> ProssimoAsync(Dictionary<string, object> stato);

        /// <summary>Com'e' andato il passo appena eseguito.</summary>
        Task EsitoAsync(
            int ordine, bool riuscito, Dictionary<string, object> misure, string errore);
    }

    /// <summary>
    /// La risposta a «qual e' il prossimo passo»: o ce n'e' uno, o il piano e'
    /// finito.
    ///
    /// PERCHE' UN TIPO INVECE DI UN `null`.
    /// La forma ovvia sarebbe far tornare `null` a <see
    /// cref="IFontePiano.ProssimoAsync"/> per dire «finito», e risparmiare
    /// questo tipo. Non si fa, ed e' una scelta contro un difetto preciso: un
    /// adattatore che inghiotte un'eccezione di rete e ritorna `null`
    /// racconterebbe **«piano completato»** per un piano interrotto a meta'.
    /// Nessun errore, nessun avviso, e un pezzo che a chi guarda sembra finito.
    ///
    /// Con <see cref="Finito"/> esplicito, «non ho ricevuto niente» e «non c'e'
    /// piu' niente da fare» non hanno la stessa forma, e il primo non si puo'
    /// scrivere per distrazione al posto del secondo.
    ///
    /// Regola per chi implementa la fonte: **un guasto si solleva, non si
    /// traduce.** Se la rete cade, l'eccezione deve arrivare all'esecutore, che
    /// la scrive in <see cref="EsecutorePiano.Risultato.Errore"/> lasciando
    /// `Completato` a falso.
    /// </summary>
    public class ProssimoPasso
    {
        /// <summary>Vero quando il piano e' arrivato in fondo.</summary>
        public bool Finito;

        /// <summary>Il passo da eseguire. Null se <see cref="Finito"/>.</summary>
        public PassoDelPiano Passo;

        public static ProssimoPasso Fine()
        {
            return new ProssimoPasso { Finito = true };
        }

        public static ProssimoPasso Questo(PassoDelPiano passo)
        {
            return new ProssimoPasso { Finito = false, Passo = passo };
        }
    }

    /// <summary>Un passo del piano: cosa fare, con quali argomenti.</summary>
    public class PassoDelPiano
    {
        /// <summary>Il numero d'ordine, contando da 1.</summary>
        public int Ordine;

        /// <summary>Il nome dell'operazione, come nel catalogo.</summary>
        public string Operazione;

        /// <summary>Gli argomenti dell'operazione.</summary>
        public Dictionary<string, object> Argomenti;

        /// <summary>Una riga in italiano su cosa fa il passo, per il log.</summary>
        public string Riassunto;
    }
}
