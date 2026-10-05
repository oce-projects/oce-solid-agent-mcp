using System.Collections.Generic;
using System.Threading.Tasks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// Da dove arrivano i passi del piano.
    ///
    /// E' l'UNICO confine inventato in tutta l'estrazione: undici file su
    /// dodici non hanno bisogno di nessuna interfaccia, perche' parlano solo
    /// con SOLIDWORKS. `PlanExecutor` invece parlava con il server di
    /// OpenPDM, e senza questo confine si sarebbe portato dietro il PDM dentro
    /// la libreria che esiste per non dipenderne.
    ///
    /// Chi la implementa decide da dove vengono i passi: un server, un file
    /// JSON, un elenco in memoria per una prova. L'esecutore non lo sa e non
    /// deve saperlo.
    /// </summary>
    public interface IPlanSource
    {
        /// <summary>
        /// Il prossimo passo, dato lo stato del documento adesso.
        ///
        /// SI CHIEDE OGNI VOLTA invece di scaricare tutto il piano perche' il
        /// controllo va rifatto: fra il primo passo e l'ultimo possono passare
        /// minuti, e in mezzo il documento puo' essere cambiato.
        ///
        /// **Un guasto NON si racconta come «finito»: si solleva.** Vedi la
        /// nota su <see cref="NextStep"/>.
        /// </summary>
        Task<NextStep> NextAsync(Dictionary<string, object> docState);

        /// <summary>Com'e' andato il passo appena eseguito.</summary>
        Task OutcomeAsync(
            int order, bool succeeded, Dictionary<string, object> measurements, string error);
    }

    /// <summary>
    /// La risposta a «qual e' il prossimo passo»: o ce n'e' uno, o il piano e'
    /// finito.
    ///
    /// PERCHE' UN TIPO INVECE DI UN `null`.
    /// La forma ovvia sarebbe far tornare `null` a <see
    /// cref="IPlanSource.NextAsync"/> per dire «finito», e risparmiare
    /// questo tipo. Non si fa, ed e' una scelta contro un difetto preciso: un
    /// adattatore che inghiotte un'eccezione di rete e ritorna `null`
    /// racconterebbe **«piano completato»** per un piano interrotto a meta'.
    /// Nessun errore, nessun avviso, e un pezzo che a chi guarda sembra finito.
    ///
    /// Con <see cref="Done"/> esplicito, «non ho ricevuto niente» e «non c'e'
    /// piu' niente da fare» non hanno la stessa forma, e il primo non si puo'
    /// scrivere per distrazione al posto del secondo.
    ///
    /// Regola per chi implementa la fonte: **un guasto si solleva, non si
    /// traduce.** Se la rete cade, l'eccezione deve arrivare all'esecutore, che
    /// la scrive in <see cref="PlanExecutor.Result.Error"/> lasciando
    /// `Completed` a falso.
    /// </summary>
    public class NextStep
    {
        /// <summary>Vero quando il piano e' arrivato in fondo.</summary>
        public bool Done;

        /// <summary>Il passo da eseguire. Null se <see cref="Done"/>.</summary>
        public PlanStep Step;

        public static NextStep Fine()
        {
            return new NextStep { Done = true };
        }

        public static NextStep This(PlanStep planStep)
        {
            return new NextStep { Done = false, Step = planStep };
        }
    }

    /// <summary>Un passo del piano: cosa fare, con quali argomenti.</summary>
    public class PlanStep
    {
        /// <summary>Il numero d'ordine, contando da 1.</summary>
        public int Order;

        /// <summary>Il nome dell'operazione, come nel catalogo.</summary>
        public string Operation;

        /// <summary>Gli argomenti dell'operazione.</summary>
        public Dictionary<string, object> Arguments;

        /// <summary>Una riga in italiano su cosa fa il passo, per il log.</summary>
        public string Summary;
    }
}
