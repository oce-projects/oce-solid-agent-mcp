using System;
using System.IO;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
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
    internal static class ImagesTests
    {
        internal static void All()
        {
            Group("cosa accompagna una fotografia (Z-C10)");

            var file = Path.Combine(Root(), "Tools", "McpTools.cs");
            if (!File.Exists(file))
            {
                Check("il sorgente del ponte c'e'", false, "manca " + file);
                return;
            }
            var text = File.ReadAllText(file);

            Check("la didascalia dice che i numeri dell'immagine non sono autorevoli",
                  text.Contains("non sono autorevoli"),
                  "Tools/McpTools.cs, accanto alle viste");

            // I NUMERI, NON L'AGGETTIVO. «Puo' essere impreciso» non fa cambiare
            // comportamento a nessuno; «90 letto 79» si'.
            Check("e porta gli errori misurati, non un aggettivo",
                  text.Contains("90") && text.Contains("79") &&
                  text.Contains("12,5") && text.Contains("letta 6"),
                  "Ø90 letto 79; parete 12,5 letta 6");

            // E LA STRADA PER LA MISURA VERA. Un avvertimento senza alternativa
            // lascia il modello con lo stesso problema e in piu' il dubbio.
            Check("e nomina come si prende la misura vera",
                  text.Contains("measure_between") &&
                  text.Contains("minimum_distance") &&
                  text.Contains("body_bounding_box"),
                  "tre operazioni nominate, piu' lo stato");

            // ── E IL MOTIVO, che e' cio' che rende l'avviso ricordabile ──
            Check("e dice PERCHE' l'isometrica sbaglia",
                  text.Contains("scorciati"),
                  "in isometrica gli spessori sono scorciati e sembrano piu' grossi");
        }
    }
}
