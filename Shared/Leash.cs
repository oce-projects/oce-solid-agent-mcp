using System;

namespace Shared
{
    /// <summary>
    /// QUANDO UNA CHIAMATA NON TORNA, ALMENO SI SA QUALE (voci K18 e Z-C4).
    ///
    /// ── COSA E' COSTATO NON AVERLO ───────────────────────────────────────
    ///
    /// Il 23/9/2026 tre giri di banco sono morti appesi. Il processo era vivo,
    /// SOLIDWORKS `Responding=False`, **zero CPU su tutti e due**, nessuna
    /// finestra modale: fermo dentro `NewDocument`, che stava leggendo il
    /// modello di documento da una condivisione di rete con una presa sopra.
    ///
    /// La sentinella (voce K17) non poteva aiutare: **conta le risposte**, e
    /// li' non tornava nessuna risposta da contare. Il giro e' rimasto appeso
    /// finche' qualcuno non ha guardato — e **nessuno guarda un giro che sta
    /// andando bene**.
    ///
    /// ── COSA FA, E SOPRATTUTTO COSA NON FA ───────────────────────────────
    ///
    /// **Non salva la chiamata.** Una chiamata COM piantata non si annulla: il
    /// thread che la fa resta li' per sempre, e non c'e' niente da fare al
    /// riguardo. Promettere un «timeout che riprova» sarebbe la solita
    /// guardia che non fa cio' che dice.
    ///
    /// Quello che fa e' trasformare **un'attesa infinita e muta** in **un
    /// referto**: quale operazione, da quanto, su quale documento. Poi chi lo
    /// usa decide — nel banco, si stampa e si muore, perche' con SOLIDWORKS
    /// piantato le prove che seguono non misurerebbero niente.
    ///
    /// ── PERCHE' UN BATTITO E NON UN THREAD DI LAVORO ─────────────────────
    ///
    /// La strada ovvia — eseguire la chiamata su un altro thread e aspettarla
    /// con una scadenza — **e' sbagliata qui**: l'interop di SOLIDWORKS e' a
    /// thread singolo (STA), e spostare le chiamate su un thread qualunque le
    /// fa passare per il marshalling COM, cioe' aggiunge un modo nuovo di
    /// piantarsi al posto di toglierne uno.
    ///
    /// Qui il thread che lavora non cambia mai: **annuncia** cosa sta per fare
    /// e **dice** quando ha finito. Un guardiano, da fuori, guarda l'orologio.
    /// Non tocca il lavoro: lo guarda.
    /// </summary>
    internal sealed class Guinzaglio
    {
        private readonly object _chiave = new object();
        private string _cosa;
        private DateTime _da;

        /// <summary>
        /// Da quanto una chiamata puo' durare prima che si dica che e' appesa.
        /// Non e' «quanto dovrebbe durare»: e' **quanto oltre ogni dubbio**.
        /// Un valore stretto trasforma una prova lenta in un falso allarme, e
        /// un falso allarme spegne il guardiano il giorno dopo.
        /// </summary>
        internal int ScadenzaMs { get; private set; }

        internal Guinzaglio(int scadenzaMs)
        {
            if (scadenzaMs < 1000)
                throw new ArgumentOutOfRangeException(
                    "scadenzaMs",
                    "sotto il secondo non e' un guinzaglio, e' un cronometro: "
                    + "qualunque prova vera lo farebbe scattare.");
            ScadenzaMs = scadenzaMs;
        }

        /// <summary>Sta per cominciare una chiamata, e si dice quale.</summary>
        internal void Comincia(string cosa, DateTime adesso)
        {
            lock (_chiave) { _cosa = cosa; _da = adesso; }
        }

        /// <summary>E' tornata. Da qui in poi non c'e' niente da sorvegliare —
        /// il guardiano deve tacere, altrimenti la prima pausa fra due prove
        /// diventerebbe un allarme.</summary>
        internal void Finita()
        {
            lock (_chiave) { _cosa = null; }
        }

        /// <summary>
        /// Cosa e' appeso in questo momento, oppure `null`. Prende l'ora da
        /// fuori: un orologio passato e' un orologio che si puo' far mentire
        /// in una prova, e **una guardia che non si puo' far scattare non si
        /// puo' credere**.
        /// </summary>
        internal string Appesa(DateTime adesso)
        {
            lock (_chiave)
            {
                if (_cosa == null) return null;
                var quanto = adesso - _da;
                if (quanto.TotalMilliseconds < ScadenzaMs) return null;
                return "LA CHIAMATA NON TORNA: «" + _cosa + "» e' partita "
                     + ((int)quanto.TotalSeconds) + " secondi fa e non ha "
                     + "ancora risposto (la soglia e' " + (ScadenzaMs / 1000)
                     + "). Non si puo' annullare una chiamata COM piantata: "
                     + "quello che si puo' fare e' dire QUALE era, e fermarsi.";
            }
        }

        /// <summary>Per il resoconto: da quanto dura quella in corso, in
        /// secondi, o -1 se non ce n'e' nessuna.</summary>
        internal int DaQuantiSecondi(DateTime adesso)
        {
            lock (_chiave)
            {
                if (_cosa == null) return -1;
                return (int)(adesso - _da).TotalSeconds;
            }
        }
    }
}
