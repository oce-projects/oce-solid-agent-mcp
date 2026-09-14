namespace Riscontro.Disegno
{
    /// <summary>
    /// LE FRASI CHE RICORRONO, scritte una volta sola.
    ///
    /// PERCHE' ESISTE QUESTO FILE. Fino alla 0.31.0 il modo di tornare indietro
    /// era scritto **ventuno volte** con parole quasi uguali, e diceva sempre la
    /// stessa cosa: «annullalo con Ctrl+Z». Poi e' arrivato lo strumento
    /// `annulla` (voce R1) e quelle ventuno frasi sono diventate ventuno posti
    /// da correggere — che e' esattamente il conto che una frase ripetuta
    /// presenta il giorno in cui cambia.
    ///
    /// Qui ce n'e' una sola, e i tre file che la usano dicono la stessa cosa
    /// per costruzione.
    /// </summary>
    internal static class Frasi
    {
        /// <summary>
        /// Come si ritira quello che e' appena stato fatto.
        ///
        /// ── PERCHE' DICE DUE COSE E NON UNA ───────────────────────────────
        ///
        /// Questa libreria ha **due lettori diversi**, e una frase vera per uno
        /// solo dei due e' una frase falsa:
        ///
        ///   * il **modello**, che arriva dal ponte MCP. Non ha una tastiera:
        ///     «premi Ctrl+Z» e' una riparazione che non puo' prendere. Per lui
        ///     esiste lo strumento `annulla`.
        ///   * la **persona**, che usa le stesse operazioni dall'add-in di
        ///     OpenPDM o che legge questa riga in un log. Per lei `annulla` non
        ///     esiste: il gesto e' `Ctrl+Z`.
        ///
        /// Nominare solo lo strumento avrebbe mandato il progettista a cercare
        /// un comando che nel suo menu non c'e'. Nominare solo il tasto e' il
        /// difetto che la voce R3 chiude. Si dicono tutti e due, nell'ordine in
        /// cui servono a chi legge.
        ///
        /// Comincia con lo spazio: si attacca in coda a un messaggio.
        /// </summary>
        public const string CosiSiRitira =
            "«annulla» se stai parlando col ponte, Ctrl+Z se sei davanti allo schermo";
    }
}
