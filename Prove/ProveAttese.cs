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
            //
            // L'ELENCO DEI NOMI NON STA PIU' NEL VALUTATORE, e questa e' la
            // prova che non ci torni. Ci stava, e si e' scollato: due misure
            // nuove aggiunte a chi misura e dimenticate qui uscivano come «non
            // so misurare «ultimo_piano_mm»: i nomi sono …» con accanto
            // l'elenco vecchio — un messaggio che diceva il falso su se stesso,
            // e che dava ERRORE DI BANCO a una prova che il ponte superava.
            //
            // Adesso il valutatore non giudica i nomi: li passa a chi il
            // documento ce l'ha, ed e' quello a dire che non li conosce. Si
            // verifica il passaggio (la funzione viene davvero chiamata) e che
            // la frase del rifiuto arrivi intera a chi legge.
            bool arrivato = false;
            Func<string, object> severo = c =>
            {
                arrivato = true;
                throw new Attese.NonHoPotutoMisurare(
                    "non so misurare «" + c + "»: i nomi sono volume_mm3");
            };
            Segna("un «cosa» sconosciuto: il rifiuto di chi misura arriva intero",
                  Alza(() => { string x; Attese.Combacia(Attesa("pippo", 1.0), severo, stato, misure, out x); },
                       "non so misurare «pippo»"));
            Segna("un «cosa» sconosciuto viene davvero chiesto a chi misura", arrivato);

            // ── DUE PROFILI DIVERSI NON SI SCRIVONO UGUALI ────────────────
            //
            // E' IL ROSSO DI UN DIFETTO CHE E' SCATTATO DAVVERO. Il confronto
            // fra un caso che deve fallire e la sua gemella usava
            // `Convert.ToString`, che su un `object[]` torna «System.Object[]»:
            // il rifiuto della rivoluzione e la sua gemella differiscono SOLO
            // nei contorni, e il banco li ha chiamati «una copia».
            //
            // Le due righe qui sotto sono le due facce della stessa cecita':
            // liste diverse devono scriversi diverse, liste uguali uguali —
            // perche' una funzione che torna sempre stringhe diverse passerebbe
            // la prima prova e romperebbe il confronto lo stesso.
            Func<double[], object> profilo = p =>
            {
                var punti = new List<object>();
                for (int i = 0; i + 1 < p.Length; i += 2)
                    punti.Add(new List<object> { p[i], p[i + 1] });
                return new List<object> { punti };
            };
            object cilindro = profilo(new double[] { 0, 0, 0, 20, 50, 20, 50, 0 });
            object aCavallo = profilo(new double[] { 0, -20, 0, 20, 50, 20, 50, -20 });
            Segna("due contorni diversi si scrivono diversi",
                  Attese.ComeSiScrive(cilindro) != Attese.ComeSiScrive(aCavallo),
                  Attese.ComeSiScrive(aCavallo));
            Segna("due contorni uguali si scrivono uguali",
                  Attese.ComeSiScrive(cilindro) ==
                  Attese.ComeSiScrive(profilo(new double[] { 0, 0, 0, 20, 50, 20, 50, 0 })));
            Segna("un elenco non si scrive «System.Object[]»",
                  Attese.ComeSiScrive(cilindro).IndexOf("System.Object", StringComparison.Ordinal) < 0,
                  Attese.ComeSiScrive(cilindro));
            Segna("un numero si scrive col punto, non con la virgola",
                  Attese.ComeSiScrive(1.5) == "1.5", Attese.ComeSiScrive(1.5));
            Segna("il niente ha un nome", Attese.ComeSiScrive(null) == "(niente)");

            // ── UN ELENCO CHE NUMERI NON NE HA ───────────────────────────
            //
            // `["C2"]` — i corpi su cui una booleana ha lavorato. Fino alla
            // 0.81.0 usciva «non e' un elenco di numeri: System.String[]»: un
            // messaggio vero e inutile, perche' il caso chiedeva una cosa
            // sensata e il valutatore non sapeva confrontarla.
            var conElenchi = new Dictionary<string, object>
            {
                ["utensili"] = new List<object> { "C2", "C3" },
                ["uno"] = new List<object> { "C2" },
                ["misti"] = new List<object> { "C2", 7.0 },
            };
            Func<string, object, Dictionary<string, object>> att = (c, u) =>
                new Dictionary<string, object> { ["cosa"] = c, ["uguale"] = u };

            Segna("elenco di testo: uguale passa",
                  Attese.Combacia(att("stato.utensili", new List<object> { "C2", "C3" }),
                                  null, conElenchi, misure, out d), d);
            Segna("elenco di testo ROSSO: un elemento diverso",
                  !Attese.Combacia(att("stato.utensili", new List<object> { "C2", "C9" }),
                                   null, conElenchi, misure, out d), d);
            Segna("elenco di testo ROSSO: l'ORDINE conta",
                  !Attese.Combacia(att("stato.utensili", new List<object> { "C3", "C2" }),
                                   null, conElenchi, misure, out d), d);
            Segna("elenco di testo ROSSO: uno in meno",
                  !Attese.Combacia(att("stato.utensili", new List<object> { "C2" }),
                                   null, conElenchi, misure, out d), d);
            Segna("elenco di testo: anche uno solo",
                  Attese.Combacia(att("stato.uno", new List<object> { "C2" }),
                                  null, conElenchi, misure, out d), d);
            Segna("elenco misto testo e numeri: si confronta come testo",
                  Attese.Combacia(att("stato.misti", new List<object> { "C2", 7.0 }),
                                  null, conElenchi, misure, out d), d);
            Segna("elenco di testo ROSSO: se la misura NON e' un elenco, lo dice",
                  !Attese.Combacia(att("stato.materiale", new List<object> { "C2" }),
                                   null, stato, misure, out d) &&
                  d.IndexOf("non e' un elenco", StringComparison.Ordinal) >= 0, d);

            // ── LA GEMELLA HA A CHE VEDERE COL RIFIUTO? (F5) ──────────────
            //
            // La regola sta qui e non accanto al banco perche' e' aritmetica
            // su due dizionari: i suoi rossi si provano in un millesimo di
            // secondo, e sono quattro casi che a mano non si guardano mai.
            Func<string, string, Dictionary<string, object>> arg2 = (a, b) =>
                new Dictionary<string, object> { ["faccia_uid"] = a, ["x_mm"] = b };
            string d2;

            Segna("gemella identica: rifiutata",
                  !Attese.SiSomigliano(arg2("F1", "10"), arg2("F1", "10"), out d2) &&
                  d2.IndexOf("copia", StringComparison.Ordinal) >= 0, d2);

            Segna("gemella che differisce per un argomento: accettata",
                  Attese.SiSomigliano(arg2("F1", "10"), arg2("F1", "40"), out d2), d2);

            Segna("gemella senza niente in comune, con due argomenti: rifiutata",
                  !Attese.SiSomigliano(arg2("F1", "10"), arg2("F5", "40"), out d2) &&
                  d2.IndexOf("non si parlano", StringComparison.Ordinal) >= 0, d2);

            // L'ECCEZIONE DELL'ARITA'. `crea_asse` prende `faccia_uid` e basta:
            // due casi che differiscono per quello non possono averne uno in
            // comune, e rifiutarli vieterebbe la coppia a meta' del catalogo.
            Func<string, Dictionary<string, object>> arg1 = a =>
                new Dictionary<string, object> { ["faccia_uid"] = a };
            Segna("gemella di un'operazione con UN argomento solo: accettata",
                  Attese.SiSomigliano(arg1("@faccia(piana=false)"),
                                      arg1("@faccia(verso=davanti)"), out d2), d2);
            Segna("...e lo dice, invece di far finta che sia una coppia normale",
                  d2.IndexOf("l'unico che questa operazione prende",
                             StringComparison.Ordinal) >= 0, d2);
            Segna("un argomento solo, ma uguale: resta una copia",
                  !Attese.SiSomigliano(arg1("F1"), arg1("F1"), out d2), d2);

            // I contorni sono liste: e' il caso che ha rotto il confronto.
            Segna("gemelle che differiscono solo in una lista: accettate",
                  Attese.SiSomigliano(
                      new Dictionary<string, object> { ["contorni"] = aCavallo, ["angolo_gradi"] = 360.0 },
                      new Dictionary<string, object> { ["contorni"] = cilindro, ["angolo_gradi"] = 360.0 },
                      out d2), d2);

            // ── UN'ATTESA SU UNA FRASE ───────────────────────────────────
            //
            // `non_contiene` e' la meta' che conta: una frase che dichiara «il
            // profilo Ø30 per 40» su un pezzo che quel profilo non ce l'ha
            // piu' e' il difetto che non da' errore.
            var conForma = new Dictionary<string, object>
            {
                ["forma"] = "una rivoluzione del profilo D30 per 40, D20 per 30",
            };
            Func<object, object, Dictionary<string, object>> frase = (c, n) =>
            {
                var a = new Dictionary<string, object> { ["cosa"] = "stato.forma" };
                if (c != null) a["contiene"] = c;
                if (n != null) a["non_contiene"] = n;
                return a;
            };

            Segna("frase: il pezzo che c'e' si trova",
                  Attese.Combacia(frase(new List<object> { "profilo", "D30 per 40" }, null),
                                  null, conForma, misure, out d), d);
            Segna("frase ROSSO: un pezzo che non c'e' fa fallire",
                  !Attese.Combacia(frase(new List<object> { "D99 per 1" }, null),
                                   null, conForma, misure, out d) &&
                  d.IndexOf("non dice", StringComparison.Ordinal) >= 0, d);
            Segna("frase ROSSO: «non_contiene» fallisce se la parola c'e' ancora",
                  !Attese.Combacia(frase(null, new List<object> { "profilo" }),
                                   null, conForma, misure, out d) &&
                  d.IndexOf("dice ancora", StringComparison.Ordinal) >= 0, d);
            Segna("frase: «non_contiene» passa quando la parola e' sparita",
                  Attese.Combacia(frase(null, new List<object> { "guscio" }),
                                  null, conForma, misure, out d), d);
            Segna("frase: si puo' chiedere l'una e l'altra insieme",
                  Attese.Combacia(frase(new List<object> { "rivoluzione" },
                                        new List<object> { "guscio" }),
                                  null, conForma, misure, out d), d);
            Segna("frase: una stringa sola vale come elenco di uno",
                  Attese.Combacia(frase("rivoluzione", null), null, conForma, misure, out d), d);

            // ── E UN ELENCO NON E' «System.String[]» ─────────────────────
            //
            // `misure.profili[0].misure` e' un elenco: chiedere che CONTENGA
            // una voce e' la cosa piu' naturale del mondo, e fino alla 0.91.0
            // falliva con «non dice "80X80X5": System.String[]» — un messaggio
            // vero e inutile. Per «uguale» gli elenchi di testo erano gia'
            // stati insegnati; per «contiene» no, e nessuno se n'era accorto
            // perche' nessun caso lo chiedeva.
            var conElenco = new Dictionary<string, object>
            {
                ["misure"] = new[] { "80X80X5", "100X100X6", "40X40X3" },
                ["vuoto"] = new string[0],
            };
            Func<string, object, object, Dictionary<string, object>> elenco = (campo, c, n) =>
            {
                var a = new Dictionary<string, object> { ["cosa"] = "misure." + campo };
                if (c != null) a["contiene"] = c;
                if (n != null) a["non_contiene"] = n;
                return a;
            };

            Segna("elenco: «contiene» trova una voce intera",
                  Attese.Combacia(elenco("misure", new List<object> { "80X80X5" }, null),
                                  null, stato, conElenco, out d), d);
            Segna("elenco: «contiene» trova anche un pezzo di voce",
                  Attese.Combacia(elenco("misure", new List<object> { "100X100" }, null),
                                  null, stato, conElenco, out d), d);
            Segna("elenco ROSSO: una voce che non c'e' fa fallire",
                  !Attese.Combacia(elenco("misure", new List<object> { "999X999X9" }, null),
                                   null, stato, conElenco, out d) &&
                  d.IndexOf("non dice", StringComparison.Ordinal) >= 0, d);
            // IL MESSAGGIO NON DEVE PIU' DIRE IL NOME DEL TIPO. E' la meta'
            // che conta: il rosso c'era anche prima, ma diceva una cosa da cui
            // non si poteva ripartire.
            Segna("elenco ROSSO: il messaggio mostra le voci, non «System.String[]»",
                  !Attese.Combacia(elenco("misure", new List<object> { "999X999X9" }, null),
                                   null, stato, conElenco, out d) &&
                  d.IndexOf("System.String[]", StringComparison.Ordinal) < 0 &&
                  d.IndexOf("80X80X5", StringComparison.Ordinal) >= 0, d);
            Segna("elenco: «non_contiene» passa se nessuna voce lo dice",
                  Attese.Combacia(elenco("misure", null, new List<object> { "999X999X9" }),
                                  null, stato, conElenco, out d), d);
            Segna("elenco ROSSO: «non_contiene» fallisce se una voce lo dice",
                  !Attese.Combacia(elenco("misure", null, new List<object> { "40X40X3" }),
                                   null, stato, conElenco, out d) &&
                  d.IndexOf("dice ancora", StringComparison.Ordinal) >= 0, d);
            // UN ELENCO VUOTO NON CONTIENE NIENTE, e non e' un campo assente:
            // e' un elenco che c'e' ed e' vuoto. Le due cose si distinguono.
            Segna("elenco ROSSO: un elenco vuoto non contiene niente",
                  !Attese.Combacia(elenco("vuoto", new List<object> { "qualunque" }, null),
                                   null, stato, conElenco, out d), d);
            // UN CAMPO ASSENTE NON E' UNA FRASE VUOTA, e non e' nemmeno un
            // rosso: e' un errore di banco, come per ogni altra misura. Il
            // caso chiede una cosa che nello stato non c'e', e va detto.
            Segna("frase ROSSO: un campo che non c'e' non si valuta",
                  Alza(() => { string x;
                               Attese.Combacia(frase(new List<object> { "x" }, null), null,
                                               new Dictionary<string, object>(), misure, out x); },
                       "campo assente"));
            // E se invece chi misura risponde «niente», quello si': e' un
            // rosso, perche' la frase c'era da fare e non c'e'.
            Segna("frase ROSSO: chi misura risponde niente, e la frase manca",
                  !Attese.Combacia(
                      new Dictionary<string, object>
                      { ["cosa"] = "forma_del_pezzo", ["contiene"] = new List<object> { "x" } },
                      c => null, stato, misure, out d) &&
                  d.IndexOf("non c'e'", StringComparison.Ordinal) >= 0, d);
            Segna("frase ROSSO: un elenco vuoto non chiede niente, e si rifiuta",
                  Alza(() => { string x;
                               Attese.Combacia(frase(new List<object>(), null), null, conForma, misure, out x); },
                       "non chiede niente"));
            Segna("frase ROSSO: «uguale» insieme a «contiene» si rifiuta",
                  Alza(() =>
                  {
                      var a = frase(new List<object> { "profilo" }, null);
                      a["uguale"] = "qualcosa";
                      string x; Attese.Combacia(a, null, conForma, misure, out x);
                  }, "decidi quale dei due"));

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
