namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LE FRASI CHE RICORRONO, scritte una volta sola.
    ///
    /// PERCHE' ESISTE QUESTO FILE. Fino alla 0.31.0 il modo di tornare indietro
    /// era scritto **ventuno volte** con parole quasi uguali, e diceva sempre la
    /// stessa cosa: «annullalo con Ctrl+Z». Poi e' arrivato lo strumento
    /// `undo` (voce R1) e quelle ventuno frasi sono diventate ventuno posti
    /// da correggere — che e' esattamente il conto che una frase ripetuta
    /// presenta il giorno in cui cambia.
    ///
    /// Qui ce n'e' una sola, e i tre file che la usano dicono la stessa cosa
    /// per costruzione.
    /// </summary>
    internal static class Sentences
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
        ///     esiste lo strumento `undo`.
        ///   * la **persona**, che usa le stesse operazioni dall'add-in di
        ///     OpenPDM o che legge questa riga in un log. Per lei `undo` non
        ///     esiste: il gesto e' `Ctrl+Z`.
        ///
        /// Nominare solo lo strumento avrebbe mandato il progettista a cercare
        /// un comando che nel suo menu non c'e'. Nominare solo il tasto e' il
        /// difetto che la voce R3 chiude. Si dicono tutti e due, nell'ordine in
        /// cui servono a chi legge.
        ///
        /// Comincia con lo spazio: si attacca in coda a un messaggio.
        /// </summary>
        public const string HowToWithdrawIt =
            "«undo» se stai parlando col ponte, Ctrl+Z se sei davanti allo schermo";

        /// <summary>
        /// DOVE STAVA E DOVE STA, in frazione dell'ingombro — **e solo a
        /// parole** (voce E10).
        ///
        /// ── PERCHE' TORNA UNA FRASE E NON UN NUMERO ───────────────────────
        ///
        /// La frazione dell'ingombro — `u = (x − minX) / (maxX − minX)` — e' la
        /// spiegazione che manca ai messaggi di questa libreria: quando un
        /// pezzo cresce, «la faccia si e' spostata di 60 mm» e' vero e non dice
        /// niente, mentre «stava al 100% del lato e ci sta ancora» dice
        /// **che cosa e' successo**.
        ///
        /// E' anche, come CRITERIO DI IDENTITA', l'unica delle strade proposte
        /// che trasforma un «non lo so» in un «si'» sbagliato:
        ///
        ///   * un foro a 20 mm dal bordo, su una piastra che passa da 100 a
        ///     300, **resta a 20 mm dal bordo**: e' cio' che significa in
        ///     officina. La frazione dice che e' andato a 60 mm, e siccome li'
        ///     una faccia la trova, **CONFERMA invece di rifiutare**;
        ///   * l'ingombro e' instabile: basta un rialzo in un angolo per
        ///     cambiare il denominatore di **tutte** le facce insieme.
        ///
        /// Per questo il tipo di ritorno e' `string` e non `double`, e non e'
        /// un dettaglio: una frase non entra in un `if`. Chi volesse promuovere
        /// questo numero a criterio — fra due mesi, «tanto il conto c'e' gia'»
        /// — dovrebbe rileggersi la frase per estrarne i numeri, e lo si
        /// vedrebbe. Una ricerca di «frazione» dentro `SignatureMatches` o dentro
        /// il ciclo di ritrovamento deve tornare **vuota**: e' il vero criterio
        /// di chiusura della voce E10.
        ///
        /// Null quando non c'e' niente da dire: dati mancanti, scatola
        /// degenere, oppure la faccia non si e' spostata.
        /// </summary>
        /// <param name="uid">Come si chiama l'entita' nel messaggio.</param>
        /// <param name="before">Il punto che il piano portava con se', in mm.</param>
        /// <param name="now">Il punto misurato adesso, in mm.</param>
        /// <param name="boxBefore">{minX,minY,minZ,maxX,maxY,maxZ} di allora.</param>
        /// <param name="boxNow">{minX,minY,minZ,maxX,maxY,maxZ} di adesso.</param>
        public static string WhereItWasAndWhereItIs(
            string uid, double[] before, double[] now,
            double[] boxBefore, double[] boxNow)
        {
            if (before == null || now == null ||
                boxBefore == null || boxNow == null) return null;
            if (before.Length < 3 || now.Length < 3 ||
                boxBefore.Length < 6 || boxNow.Length < 6) return null;

            // L'ASSE CHE SI E' MOSSO, uno solo. Dire tutti e tre vorrebbe dire
            // scrivere due righe su due direzioni in cui non e' successo
            // niente: un messaggio piu' lungo che si legge peggio.
            int axis = -1;
            double howMuch = 0;
            for (int i = 0; i < 3; i++)
            {
                double d = System.Math.Abs(now[i] - before[i]);
                if (d > howMuch) { howMuch = d; axis = i; }
            }
            if (axis < 0 || howMuch <= 0.05) return null;

            double sideBefore = boxBefore[axis + 3] - boxBefore[axis];
            double sideNow = boxNow[axis + 3] - boxNow[axis];
            // Sotto il decimo di millimetro la divisione non significa piu'
            // niente: un lato di 0,01 mm fa uscire percentuali da migliaia.
            if (sideBefore < 0.1 || sideNow < 0.1) return null;

            double uBefore = (before[axis] - boxBefore[axis]) / sideBefore;
            double uNow = (now[axis] - boxNow[axis]) / sideNow;

            string itemName = axis == 0 ? "X" : (axis == 1 ? "Y" : "Z");
            var c = System.Globalization.CultureInfo.InvariantCulture;

            // UN PUNTO PERCENTUALE. Piu' stretto sarebbe un confronto fra
            // arrotondamenti; piu' largo direbbe «ci sta ancora» di una faccia
            // che si e' spostata davvero rispetto al pezzo.
            if (System.Math.Abs(uBefore - uNow) <= 0.01)
                return string.Format(c,
                    " Per quel che vale: «{0}» stava al {1:0.#}% del lato {2} e ci sta " +
                    "ancora — il lato e' passato da {3:0.##} a {4:0.##} mm. Puo' essere la " +
                    "stessa faccia cresciuta col pezzo, ma questo non lo decido io: " +
                    "l'identita' si misura in millimetri.",
                    uid, uBefore * 100.0, itemName, sideBefore, sideNow);

            return string.Format(c,
                " Per quel che vale: «{0}» stava al {1:0.#}% del lato {2} e adesso e' al " +
                "{3:0.#}% — il lato e' passato da {4:0.##} a {5:0.##} mm.",
                uid, uBefore * 100.0, itemName, uNow * 100.0, sideBefore, sideNow);
        }
    }
}
