using System;
using System.Collections.Generic;
using ProvaSaldatura;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// IL VALUTATORE DELLE ATTESE, contro stati finti scritti a mano.
    ///
    /// **Se e' rotto lui, tutta la suite con SOLIDWORKS diventa verde gratis**:
    /// un confronto che risponde sempre «si'» non si vede da nessuna parte, se
    /// non da qui. E' il motivo per cui questo gruppo esiste, e per cui il
    /// valutatore e' stato spostato in un file che compila anche dove il CAD
    /// non c'e'.
    /// </summary>
    internal static class ProveAttese
    {
        internal static void Tutte()
        {
            Gruppo("il valutatore delle attese");

            var misure = new Dictionary<string, object>
            {
                ["tolti_mm3"] = 1234.5,
                ["verso"] = "interno",
                ["facce_tolte"] = 1,
            };
            var stato = new Dictionary<string, object>
            {
                ["ingombro_mm"] = new List<object> { 100.0, 60.0, 40.0 },
                ["materiale"] = null,
                ["facce"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "F1", ["area_mm2"] = 6000.0, ["verso"] = "davanti" },
                    new Dictionary<string, object> { ["uid"] = "F2", ["area_mm2"] = 2000.0, ["verso"] = "dietro" },
                },
            };

            string d;

            // ── IL NUMERO GIUSTO PASSA, QUELLO SBAGLIATO NO ───────────────
            Segna("numero giusto: verde",
                  Attese.Combacia(Attesa("misure.tolti_mm3", 1234.5), null, stato, misure, out d), d);
            Segna("numero sbagliato: rosso",
                  !Attese.Combacia(Attesa("misure.tolti_mm3", 1234.6), null, stato, misure, out d), d);

            // ── LA TOLLERANZA E' ASSOLUTA, E SERVE IL PERCHE' ─────────────
            var conTolleranza = Attesa("misure.tolti_mm3", 1234.0);
            conTolleranza["tolleranza"] = 1.0;
            conTolleranza["perche_questa_tolleranza"] = "e' una prova, e il numero e' scritto a mano";
            Segna("tolleranza rispettata: verde",
                  Attese.Combacia(conTolleranza, null, stato, misure, out d), d);

            conTolleranza["uguale"] = 1200.0;
            Segna("tolleranza superata: rosso",
                  !Attese.Combacia(conTolleranza, null, stato, misure, out d), d);

            var senzaPerche = Attesa("misure.tolti_mm3", 1234.0);
            senzaPerche["tolleranza"] = 1.0;
            Segna("tolleranza senza il perche': non si valuta",
                  Alza(() => { string x; Attese.Combacia(senzaPerche, null, stato, misure, out x); },
                       "perche_questa_tolleranza"));

            // ── IL CAMPO ASSENTE NON E' UNO ZERO (voce F4) ────────────────
            //
            // E' la trappola nella sua forma letterale: un dizionario che non
            // trova la chiave torna null, e null confrontato con null passa.
            Segna("campo assente: non si valuta, e dice quali c'erano",
                  Alza(() => { string x; Attese.Combacia(Attesa("misure.non_esiste", 0.0), null, stato, misure, out x); },
                       "campo assente: misure.non_esiste"));
            Segna("campo assente: elenca i campi che c'erano",
                  Alza(() => { string x; Attese.Combacia(Attesa("misure.non_esiste", 0.0), null, stato, misure, out x); },
                       "facce_tolte, tolti_mm3, verso"));

            // ── UN NULL DICHIARATO SI MISURA ──────────────────────────────
            Segna("«materiale: null» si misura, non e' assente",
                  Attese.Combacia(Attesa("stato.materiale", ""), null, stato, misure, out d), d);

            // ── GLI ELENCHI DI NUMERI ─────────────────────────────────────
            Segna("elenco di numeri giusto: verde",
                  Attese.Combacia(Attesa("stato.ingombro_mm", new List<object> { 100.0, 60.0, 40.0 }),
                                  null, stato, misure, out d), d);
            Segna("elenco di numeri con un valore diverso: rosso",
                  !Attese.Combacia(Attesa("stato.ingombro_mm", new List<object> { 100.0, 60.0, 41.0 }),
                                   null, stato, misure, out d), d);
            Segna("elenco di lunghezza diversa: rosso",
                  !Attese.Combacia(Attesa("stato.ingombro_mm", new List<object> { 100.0, 60.0 }),
                                   null, stato, misure, out d), d);

            // ── DENTRO UN ELENCO, PER INDICE ──────────────────────────────
            Segna("stato.facce[1].area_mm2 si legge",
                  Attese.Combacia(Attesa("stato.facce[1].area_mm2", 2000.0), null, stato, misure, out d), d);
            Segna("un indice oltre la fine: non si valuta",
                  Alza(() => { string x; Attese.Combacia(Attesa("stato.facce[9].area_mm2", 0.0), null, stato, misure, out x); },
                       "campo assente"));

            // ── LE MISURE DEL DOCUMENTO, SENZA DOCUMENTO ─────────────────
            //
            // Qui SOLIDWORKS non c'e': `volume_mm3` non si puo' misurare, e il
            // valutatore lo DICE invece di rispondere zero.
            Segna("«volume_mm3» senza documento: lo dice",
                  Alza(() => { string x; Attese.Combacia(Attesa("volume_mm3", 0.0), null, stato, misure, out x); },
                       "si misura sul documento"));

            // E con un documento finto — una funzione che torna numeri — si
            // misura eccome: e' il ponte che il banco vero gli passa.
            Func<string, object> finto = c => c == "volume_mm3" ? (object)240000.0 : null;
            Segna("«volume_mm3» con chi sa misurarlo: verde",
                  Attese.Combacia(Attesa("volume_mm3", 240000.0), finto, stato, misure, out d), d);

            // ── UN NOME CHE NON ESISTE ────────────────────────────────────
            Segna("un «cosa» sconosciuto: non si valuta",
                  Alza(() => { string x; Attese.Combacia(Attesa("pippo", 1.0), null, stato, misure, out x); },
                       "non so misurare"));

            // ── I SELETTORI: ZERO O PIU' D'UNA SI RIFIUTANO ───────────────
            Segna("selettore: una faccia sola si prende",
                  Attese.Seleziona("@faccia(verso=davanti)", stato) == "F1");
            Segna("selettore: nessuna faccia, si rifiuta",
                  Alza(() => Attese.Seleziona("@faccia(verso=alto)", stato), "non l'ho trovata"));

            var due = new Dictionary<string, object>
            {
                ["facce"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "F1", ["verso"] = "davanti" },
                    new Dictionary<string, object> { ["uid"] = "F5", ["verso"] = "davanti" },
                },
            };
            Segna("selettore: due facce uguali, si rifiuta invece di indovinare",
                  Alza(() => Attese.Seleziona("@faccia(verso=davanti)", due), "ne ho trovate 2"));
        }

        private static Dictionary<string, object> Attesa(string cosa, object uguale)
        {
            return new Dictionary<string, object> { ["cosa"] = cosa, ["uguale"] = uguale };
        }

        /// <summary>
        /// Alza, e il messaggio contiene quel pezzo? Un controllo che si
        /// accontentasse di «ha alzato» passerebbe anche per l'eccezione
        /// sbagliata — ed e' successo abbastanza volte in questo progetto da
        /// non volerlo piu'.
        /// </summary>
        private static bool Alza(Action f, string pezzo)
        {
            try { f(); return false; }
            catch (Exception ex) { return ex.Message.IndexOf(pezzo, StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        private static bool Alza(Func<string> f, string pezzo)
        {
            return Alza(() => { f(); }, pezzo);
        }
    }
}
