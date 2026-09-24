using System;
using System.IO;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// QUELLO CHE ACCOMPAGNA UNA FOTOGRAFIA (voce Z-C10).
    ///
    /// ── DUE ERRORI MISURATI, NESSUNO DEI DUE SI ACCORGE DI SE' ───────────
    ///
    /// - una corona di fori **Ø90 letta Ø79** dalla sola isometrica, con
    ///   confidenza piena;
    /// - una parete di **12,5 letta «6»** e un fondo di 12,5 letto «8»,
    ///   sbagliati del 40-50%, perche' in isometrica uno spessore sottile e'
    ///   scorciato e sembra piu' grosso.
    ///
    /// Il numero che esce e' **plausibile**, e finisce nel passo dopo. Non c'e'
    /// niente, nel numero stesso, che dica «vengo da una fotografia».
    ///
    /// ── E QUESTO CONTROLLO GUARDA SOLO IL TESTO ──────────────────────────
    ///
    /// Va detto, perche' e' meta' della voce e non l'altra: il ponte **non puo'
    /// sapere** da dove un modello ha preso un numero. La voce chiedeva che un
    /// numero letto da un'immagine «chieda il riscontro geometrico prima di
    /// essere usato in un passo», e quel pezzo non e' implementabile — non c'e'
    /// niente da intercettare.
    ///
    /// Cio' che si puo' fare e' **dirlo dove la fotografia arriva**, con i
    /// numeri veri accanto, e nominare le operazioni che danno la misura
    /// autorevole. Un avvertimento generico — «le misure sono nello stato» —
    /// c'era gia' e non bastava: non diceva che quelle dell'immagine sono
    /// *sbagliate*.
    /// </summary>
    internal static class ProveLeImmagini
    {
        internal static void Tutte()
        {
            Gruppo("cosa accompagna una fotografia (Z-C10)");

            var file = Path.Combine(Radice(), "Strumenti", "Attrezzi.cs");
            if (!File.Exists(file))
            {
                Segna("il sorgente del ponte c'e'", false, "manca " + file);
                return;
            }
            var testo = File.ReadAllText(file);

            Segna("la didascalia dice che i numeri dell'immagine non sono autorevoli",
                  testo.Contains("non sono autorevoli"),
                  "Strumenti/Attrezzi.cs, accanto alle viste");

            // I NUMERI, NON L'AGGETTIVO. «Puo' essere impreciso» non fa cambiare
            // comportamento a nessuno; «90 letto 79» si'.
            Segna("e porta gli errori misurati, non un aggettivo",
                  testo.Contains("90") && testo.Contains("79") &&
                  testo.Contains("12,5") && testo.Contains("letta 6"),
                  "Ø90 letto 79; parete 12,5 letta 6");

            // E LA STRADA PER LA MISURA VERA. Un avvertimento senza alternativa
            // lascia il modello con lo stesso problema e in piu' il dubbio.
            Segna("e nomina come si prende la misura vera",
                  testo.Contains("misura_fra_entita") &&
                  testo.Contains("distanza_minima") &&
                  testo.Contains("ingombro_corpo"),
                  "tre operazioni nominate, piu' lo stato");

            // ── E IL MOTIVO, che e' cio' che rende l'avviso ricordabile ──
            Segna("e dice PERCHE' l'isometrica sbaglia",
                  testo.Contains("scorciati"),
                  "in isometrica gli spessori sono scorciati e sembrano piu' grossi");
        }
    }
}
