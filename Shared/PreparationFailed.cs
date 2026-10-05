using System;

namespace Shared
{
    /// <summary>
    /// NON HO POTUTO MISURARE. Non «ho misurato e il pezzo era sbagliato».
    ///
    /// ── PERCHE' SERVE UN TIPO, E NON BASTAVA IL MESSAGGIO (voce K17) ─────
    ///
    /// Il banco distingue gia' da tempo le due cose nel RESOCONTO: `NO` e' una
    /// prova rossa, `!!` e' un errore di banco, e hanno codici di uscita
    /// diversi perche' vogliono due reazioni diverse — la prima si guarda nel
    /// codice del prodotto, la seconda nel banco.
    ///
    /// Ma la preparazione di un caso dichiarato falliva dentro un
    /// `catch (Exception)` che segnava rosso e basta, e cosi' il 23/9/2026 il
    /// resoconto ha detto:
    ///
    ///     NO   detail_enlarges   SOLIDWORKS non ha prodotto la parte
    ///
    /// su un caso scritto bene, mentre il fatto era che SOLIDWORKS non apriva
    /// piu' documenti. Chi legge quella riga va a leggere il CASO — ed e' il
    /// posto sbagliato.
    ///
    /// Un tipo suo permette al `catch` di fare la differenza senza leggere il
    /// testo del messaggio. **Confrontare messaggi e' la stessa trappola una
    /// riga piu' in la'**: il giorno in cui qualcuno riscrive la frase, il
    /// `catch` torna a segnare rosso e nessuno se ne accorge.
    ///
    /// Resta un <see cref="InvalidOperationException"/> perche' chi lo cattura
    /// cosi' — e c'e' chi lo fa — continui a catturarlo.
    /// </summary>
    internal class PreparationFailed : InvalidOperationException
    {
        internal PreparationFailed(string message) : base(message) { }
    }
}
