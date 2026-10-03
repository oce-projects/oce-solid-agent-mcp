using System;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// L'unico posto del programma dove si converte fra millimetri e metri.
    ///
    /// PERCHE' UN FILE INTERO PER TRE DIVISIONI
    /// L'API di SOLIDWORKS vuole METRI e RADIANTI, sempre, comunque siano
    /// impostate le unita' del documento. Passarle millimetri non produce
    /// nessun errore: non solleva eccezioni, non impedisce il rebuild, e
    /// costruisce un pezzo MILLE VOLTE piu' grande. Se ne accorge solo chi
    /// guarda le quote — cioe' magari nessuno, finche' il pezzo non arriva in
    /// officina.
    ///
    /// E' la classe di errore piu' pericolosa di tutto questo lavoro, e l'unica
    /// difesa che regge nel tempo e' che la conversione stia in un posto solo e
    /// che quel posto si veda. Sul protocollo, in tabella e davanti all'utente
    /// si parla SEMPRE in millimetri e gradi; qui dentro, e solo qui, si passa
    /// a cio' che vuole SOLIDWORKS.
    ///
    /// REGOLA PER CHI RIVEDE IL CODICE
    /// Una divisione per 1000, una moltiplicazione per 0.001 o un Math.PI/180
    /// fuori da questo file sono un difetto, anche se il risultato sembra
    /// giusto: vuol dire che e' nata una seconda strada, e la seconda strada
    /// prima o poi diverge.
    /// </summary>
    public static class Unita
    {
        public const double MmPerMetro = 1000.0;

        /// <summary>Millimetri -> metri, per andare verso SOLIDWORKS.</summary>
        public static double M(double mm) => mm / MmPerMetro;

        /// <summary>Gradi -> radianti, per andare verso SOLIDWORKS.</summary>
        public static double Rad(double gradi) => gradi * Math.PI / 180.0;

        /// <summary>Metri -> millimetri, per riportare al server cio' che si e' misurato.</summary>
        public static double Mm(double metri) => metri * MmPerMetro;

        /// <summary>
        /// Metri quadri -> millimetri quadri, per le aree lette da SOLIDWORKS.
        /// Sta qui e non accanto alla lettura perche' la regola vale anche per
        /// le aree: una conversione sparsa e' il primo passo verso due strade.
        /// </summary>
        public static double Mm2(double m2) => m2 * MmPerMetro * MmPerMetro;

        /// <summary>Radianti -> gradi, per riportare al server.</summary>
        public static double Gradi(double radianti) => radianti * 180.0 / Math.PI;
    }
}
