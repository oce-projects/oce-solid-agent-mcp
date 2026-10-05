using System;
using System.Collections.Generic;
using Oce.Solid.Agent.Bench;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
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
    internal static class ExpectationsTests
    {
        internal static void All()
        {
            Group("il valutatore delle attese");

            var measurements = new Dictionary<string, object>
            {
                ["removed_mm3"] = 1234.5,
                ["facing"] = "inner",
                ["faces_removed"] = 1,
            };
            var docState = new Dictionary<string, object>
            {
                ["bounding_box_mm"] = new List<object> { 100.0, 60.0, 40.0 },
                ["material"] = null,
                ["faces"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "F1", ["area_mm2"] = 6000.0, ["facing"] = "front" },
                    new Dictionary<string, object> { ["uid"] = "F2", ["area_mm2"] = 2000.0, ["facing"] = "back" },
                },
            };

            string d;

            // ── IL NUMERO GIUSTO PASSA, QUELLO SBAGLIATO NO ───────────────
            Check("numero giusto: verde",
                  Expectations.Matches(Expectation("measurements.removed_mm3", 1234.5), null, docState, measurements, out d), d);
            Check("numero sbagliato: rosso",
                  !Expectations.Matches(Expectation("measurements.removed_mm3", 1234.6), null, docState, measurements, out d), d);

            // ── LA TOLLERANZA E' ASSOLUTA, E SERVE IL PERCHE' ─────────────
            var withTolerance = Expectation("measurements.removed_mm3", 1234.0);
            withTolerance["tolerance"] = 1.0;
            withTolerance["tolerance_why"] = "e' una prova, e il numero e' scritto a mano";
            Check("tolleranza rispettata: verde",
                  Expectations.Matches(withTolerance, null, docState, measurements, out d), d);

            withTolerance["equal_to"] = 1200.0;
            Check("tolleranza superata: rosso",
                  !Expectations.Matches(withTolerance, null, docState, measurements, out d), d);

            var withoutWhy = Expectation("measurements.removed_mm3", 1234.0);
            withoutWhy["tolerance"] = 1.0;
            Check("tolleranza senza il perche': non si valuta",
                  Raise(() => { string x; Expectations.Matches(withoutWhy, null, docState, measurements, out x); },
                       "tolerance_why"));

            // ── IL CAMPO ASSENTE NON E' UNO ZERO (voce F4) ────────────────
            //
            // E' la trappola nella sua forma letterale: un dizionario che non
            // trova la chiave torna null, e null confrontato con null passa.
            Check("campo assente: non si valuta, e dice quali c'erano",
                  Raise(() => { string x; Expectations.Matches(Expectation("measurements.non_esiste", 0.0), null, docState, measurements, out x); },
                       "campo assente: measurements.non_esiste"));
            Check("campo assente: elenca i campi che c'erano",
                  Raise(() => { string x; Expectations.Matches(Expectation("measurements.non_esiste", 0.0), null, docState, measurements, out x); },
                       "faces_removed, facing, removed_mm3"));

            // ── UN NULL DICHIARATO SI MISURA ──────────────────────────────
            Check("«material: null» si misura, non e' assente",
                  Expectations.Matches(Expectation("state.material", ""), null, docState, measurements, out d), d);

            // ── GLI ELENCHI DI NUMERI ─────────────────────────────────────
            Check("elenco di numeri giusto: verde",
                  Expectations.Matches(Expectation("state.bounding_box_mm", new List<object> { 100.0, 60.0, 40.0 }),
                                  null, docState, measurements, out d), d);
            Check("elenco di numeri con un valore diverso: rosso",
                  !Expectations.Matches(Expectation("state.bounding_box_mm", new List<object> { 100.0, 60.0, 41.0 }),
                                   null, docState, measurements, out d), d);
            Check("elenco di lunghezza diversa: rosso",
                  !Expectations.Matches(Expectation("state.bounding_box_mm", new List<object> { 100.0, 60.0 }),
                                   null, docState, measurements, out d), d);

            // ── DENTRO UN ELENCO, PER INDICE ──────────────────────────────
            Check("stato.facce[1].area_mm2 si legge",
                  Expectations.Matches(Expectation("state.faces[1].area_mm2", 2000.0), null, docState, measurements, out d), d);
            Check("un indice oltre la fine: non si valuta",
                  Raise(() => { string x; Expectations.Matches(Expectation("state.faces[9].area_mm2", 0.0), null, docState, measurements, out x); },
                       "campo assente"));

            // ── LE MISURE DEL DOCUMENTO, SENZA DOCUMENTO ─────────────────
            //
            // Qui SOLIDWORKS non c'e': `volume_mm3` non si puo' misurare, e il
            // valutatore lo DICE invece di rispondere zero.
            Check("«volume_mm3» senza documento: lo dice",
                  Raise(() => { string x; Expectations.Matches(Expectation("volume_mm3", 0.0), null, docState, measurements, out x); },
                       "si misura sul documento"));

            // E con un documento finto — una funzione che torna numeri — si
            // misura eccome: e' il ponte che il banco vero gli passa.
            Func<string, object> fake = c => c == "volume_mm3" ? (object)240000.0 : null;
            Check("«volume_mm3» con chi sa misurarlo: verde",
                  Expectations.Matches(Expectation("volume_mm3", 240000.0), fake, docState, measurements, out d), d);

            // ── UN NOME CHE NON ESISTE ────────────────────────────────────
            //
            // L'ELENCO DEI NOMI NON STA PIU' NEL VALUTATORE, e questa e' la
            // prova che non ci torni. Ci stava, e si e' scollato: due misure
            // nuove aggiunte a chi misura e dimenticate qui uscivano come «non
            // so misurare «last_plane_mm»: i nomi sono …» con accanto
            // l'elenco vecchio — un messaggio che diceva il falso su se stesso,
            // e che dava ERRORE DI BANCO a una prova che il ponte superava.
            //
            // Adesso il valutatore non giudica i nomi: li passa a chi il
            // documento ce l'ha, ed e' quello a dire che non li conosce. Si
            // verifica il passaggio (la funzione viene davvero chiamata) e che
            // la frase del rifiuto arrivi intera a chi legge.
            bool arrived = false;
            Func<string, object> strict = c =>
            {
                arrived = true;
                throw new Expectations.CouldNotMeasure(
                    "non so misurare «" + c + "»: i nomi sono volume_mm3");
            };
            Check("un «cosa» sconosciuto: il rifiuto di chi misura arriva intero",
                  Raise(() => { string x; Expectations.Matches(Expectation("pippo", 1.0), strict, docState, measurements, out x); },
                       "non so misurare «pippo»"));
            Check("un «cosa» sconosciuto viene davvero chiesto a chi misura", arrived);

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
            Func<double[], object> profile = p =>
            {
                var points = new List<object>();
                for (int i = 0; i + 1 < p.Length; i += 2)
                    points.Add(new List<object> { p[i], p[i + 1] });
                return new List<object> { points };
            };
            object cylinder = profile(new double[] { 0, 0, 0, 20, 50, 20, 50, 0 });
            object straddling = profile(new double[] { 0, -20, 0, 20, 50, 20, 50, -20 });
            Check("due contorni diversi si scrivono diversi",
                  Expectations.HowItIsWritten(cylinder) != Expectations.HowItIsWritten(straddling),
                  Expectations.HowItIsWritten(straddling));
            Check("due contorni uguali si scrivono uguali",
                  Expectations.HowItIsWritten(cylinder) ==
                  Expectations.HowItIsWritten(profile(new double[] { 0, 0, 0, 20, 50, 20, 50, 0 })));
            Check("un elenco non si scrive «System.Object[]»",
                  Expectations.HowItIsWritten(cylinder).IndexOf("System.Object", StringComparison.Ordinal) < 0,
                  Expectations.HowItIsWritten(cylinder));
            Check("un numero si scrive col punto, non con la virgola",
                  Expectations.HowItIsWritten(1.5) == "1.5", Expectations.HowItIsWritten(1.5));
            Check("il niente ha un nome", Expectations.HowItIsWritten(null) == "(niente)");

            // ── UN ELENCO CHE NUMERI NON NE HA ───────────────────────────
            //
            // `["C2"]` — i corpi su cui una booleana ha lavorato. Fino alla
            // 0.81.0 usciva «non e' un elenco di numeri: System.String[]»: un
            // messaggio vero e inutile, perche' il caso chiedeva una cosa
            // sensata e il valutatore non sapeva confrontarla.
            var withLists = new Dictionary<string, object>
            {
                ["utensili"] = new List<object> { "C2", "C3" },
                ["one"] = new List<object> { "C2" },
                ["misti"] = new List<object> { "C2", 7.0 },
            };
            Func<string, object, Dictionary<string, object>> att = (c, u) =>
                new Dictionary<string, object> { ["what"] = c, ["equal_to"] = u };

            Check("elenco di testo: uguale passa",
                  Expectations.Matches(att("state.utensili", new List<object> { "C2", "C3" }),
                                  null, withLists, measurements, out d), d);
            Check("elenco di testo ROSSO: un elemento diverso",
                  !Expectations.Matches(att("state.utensili", new List<object> { "C2", "C9" }),
                                   null, withLists, measurements, out d), d);
            Check("elenco di testo ROSSO: l'ORDINE conta",
                  !Expectations.Matches(att("state.utensili", new List<object> { "C3", "C2" }),
                                   null, withLists, measurements, out d), d);
            Check("elenco di testo ROSSO: uno in meno",
                  !Expectations.Matches(att("state.utensili", new List<object> { "C2" }),
                                   null, withLists, measurements, out d), d);
            Check("elenco di testo: anche uno solo",
                  Expectations.Matches(att("state.one", new List<object> { "C2" }),
                                  null, withLists, measurements, out d), d);
            Check("elenco misto testo e numeri: si confronta come testo",
                  Expectations.Matches(att("state.misti", new List<object> { "C2", 7.0 }),
                                  null, withLists, measurements, out d), d);
            Check("elenco di testo ROSSO: se la misura NON e' un elenco, lo dice",
                  !Expectations.Matches(att("state.material", new List<object> { "C2" }),
                                   null, docState, measurements, out d) &&
                  d.IndexOf("non e' un elenco", StringComparison.Ordinal) >= 0, d);

            // ── LA GEMELLA HA A CHE VEDERE COL RIFIUTO? (F5) ──────────────
            //
            // La regola sta qui e non accanto al banco perche' e' aritmetica
            // su due dizionari: i suoi rossi si provano in un millesimo di
            // secondo, e sono quattro casi che a mano non si guardano mai.
            Func<string, string, Dictionary<string, object>> arg2 = (a, b) =>
                new Dictionary<string, object> { ["face_uid"] = a, ["x_mm"] = b };
            string d2;

            Check("gemella identica: rifiutata",
                  !Expectations.Resemble(arg2("F1", "10"), arg2("F1", "10"), out d2) &&
                  d2.IndexOf("copia", StringComparison.Ordinal) >= 0, d2);

            Check("gemella che differisce per un argomento: accettata",
                  Expectations.Resemble(arg2("F1", "10"), arg2("F1", "40"), out d2), d2);

            Check("gemella senza niente in comune, con due argomenti: rifiutata",
                  !Expectations.Resemble(arg2("F1", "10"), arg2("F5", "40"), out d2) &&
                  d2.IndexOf("non si parlano", StringComparison.Ordinal) >= 0, d2);

            // L'ECCEZIONE DELL'ARITA'. `create_axis` prende `face_uid` e basta:
            // due casi che differiscono per quello non possono averne uno in
            // comune, e rifiutarli vieterebbe la coppia a meta' del catalogo.
            Func<string, Dictionary<string, object>> arg1 = a =>
                new Dictionary<string, object> { ["face_uid"] = a };
            Check("gemella di un'operazione con UN argomento solo: accettata",
                  Expectations.Resemble(arg1("@face(planar=false)"),
                                      arg1("@face(normal=front)"), out d2), d2);
            Check("...e lo dice, invece di far finta che sia una coppia normale",
                  d2.IndexOf("l'unico che questa operazione prende",
                             StringComparison.Ordinal) >= 0, d2);
            Check("un argomento solo, ma uguale: resta una copia",
                  !Expectations.Resemble(arg1("F1"), arg1("F1"), out d2), d2);

            // LA DIFFERENZA DICHIARATA NEL PEZZO (28/9/2026, nervatura W1.2):
            // stessi argomenti, passi prima diversi. Nei due versi, perche' una
            // dichiarazione che passa sempre e' l'argomento finto di prima con
            // un altro nome.
            Func<string, double, object> partStep = (plane, thickness) => new Dictionary<string, object>
            {
                ["operation"] = "extrude_rectangle",
                ["arguments"] = new Dictionary<string, object> { ["plane"] = plane, ["thickness_mm"] = thickness },
            };
            var box = new List<object> { partStep("top", 20.0),
                new Dictionary<string, object> { ["operation"] = "shell" } };
            var plate = new List<object> { partStep("top", 2.0) };
            const string partSentence = "la piastra non ha pareti, la scatola si'";
            Check("stessi argomenti su un altro pezzo, dichiarato: accettata",
                  Expectations.ResembleInPart(arg2("F1", "10"), arg2("F1", "10"), plate, box, partSentence, out d2) &&
                  d2.IndexOf("pezzo diverso dal passo 1", StringComparison.Ordinal) >= 0, d2);
            Check("stessi argomenti e stesso pezzo, anche se dichiarato: resta una copia",
                  !Expectations.ResembleInPart(arg2("F1", "10"), arg2("F1", "10"), box, box, partSentence, out d2) &&
                  d2.IndexOf("copia", StringComparison.Ordinal) >= 0, d2);
            Check("argomenti diversi e la differenza dichiarata nel pezzo: la dichiarazione e' di troppo",
                  !Expectations.ResembleInPart(arg2("F1", "10"), arg2("F1", "40"), plate, box, partSentence, out d2) &&
                  d2.IndexOf("togli", StringComparison.Ordinal) >= 0, d2);
            Check("la differenza nel pezzo dichiarata senza dire quale: rifiutata",
                  !Expectations.ResembleInPart(arg2("F1", "10"), arg2("F1", "10"), plate, box, " ", out d2), d2);

            // I contorni sono liste: e' il caso che ha rotto il confronto.
            Check("gemelle che differiscono solo in una lista: accettate",
                  Expectations.Resemble(
                      new Dictionary<string, object> { ["contours"] = straddling, ["angle_deg"] = 360.0 },
                      new Dictionary<string, object> { ["contours"] = cylinder, ["angle_deg"] = 360.0 },
                      out d2), d2);

            // ── UN'ATTESA SU UNA FRASE ───────────────────────────────────
            //
            // `non_contiene` e' la meta' che conta: una frase che dichiara «il
            // profilo Ø30 per 40» su un pezzo che quel profilo non ce l'ha
            // piu' e' il difetto che non da' errore.
            var withShape = new Dictionary<string, object>
            {
                ["shape"] = "una rivoluzione del profilo D30 per 40, D20 per 30",
            };
            Func<object, object, Dictionary<string, object>> sentence = (c, n) =>
            {
                var a = new Dictionary<string, object> { ["what"] = "state.shape" };
                if (c != null) a["includes"] = c;
                if (n != null) a["does_not_include"] = n;
                return a;
            };

            Check("frase: il pezzo che c'e' si trova",
                  Expectations.Matches(sentence(new List<object> { "profilo", "D30 per 40" }, null),
                                  null, withShape, measurements, out d), d);
            Check("frase ROSSO: un pezzo che non c'e' fa fallire",
                  !Expectations.Matches(sentence(new List<object> { "D99 per 1" }, null),
                                   null, withShape, measurements, out d) &&
                  d.IndexOf("non dice", StringComparison.Ordinal) >= 0, d);
            Check("frase ROSSO: «non_contiene» fallisce se la parola c'e' ancora",
                  !Expectations.Matches(sentence(null, new List<object> { "profilo" }),
                                   null, withShape, measurements, out d) &&
                  d.IndexOf("dice ancora", StringComparison.Ordinal) >= 0, d);
            Check("frase: «non_contiene» passa quando la parola e' sparita",
                  Expectations.Matches(sentence(null, new List<object> { "guscio" }),
                                  null, withShape, measurements, out d), d);
            Check("frase: si puo' chiedere l'una e l'altra insieme",
                  Expectations.Matches(sentence(new List<object> { "rivoluzione" },
                                        new List<object> { "shell" }),
                                  null, withShape, measurements, out d), d);
            Check("frase: una stringa sola vale come elenco di uno",
                  Expectations.Matches(sentence("rivoluzione", null), null, withShape, measurements, out d), d);

            // ── E UN ELENCO NON E' «System.String[]» ─────────────────────
            //
            // `measurements.profiles[0].measurements` e' un elenco: chiedere che CONTENGA
            // una voce e' la cosa piu' naturale del mondo, e fino alla 0.91.0
            // falliva con «non dice "80X80X5": System.String[]» — un messaggio
            // vero e inutile. Per «uguale» gli elenchi di testo erano gia'
            // stati insegnati; per «contiene» no, e nessuno se n'era accorto
            // perche' nessun caso lo chiedeva.
            var withList = new Dictionary<string, object>
            {
                ["measurements"] = new[] { "80X80X5", "100X100X6", "40X40X3" },
                ["vuoto"] = new string[0],
            };
            Func<string, object, object, Dictionary<string, object>> list = (theField, c, n) =>
            {
                var a = new Dictionary<string, object> { ["what"] = "measurements." + theField };
                if (c != null) a["includes"] = c;
                if (n != null) a["does_not_include"] = n;
                return a;
            };

            Check("elenco: «contiene» trova una voce intera",
                  Expectations.Matches(list("measurements", new List<object> { "80X80X5" }, null),
                                  null, docState, withList, out d), d);
            Check("elenco: «contiene» trova anche un pezzo di voce",
                  Expectations.Matches(list("measurements", new List<object> { "100X100" }, null),
                                  null, docState, withList, out d), d);
            Check("elenco ROSSO: una voce che non c'e' fa fallire",
                  !Expectations.Matches(list("measurements", new List<object> { "999X999X9" }, null),
                                   null, docState, withList, out d) &&
                  d.IndexOf("non dice", StringComparison.Ordinal) >= 0, d);
            // IL MESSAGGIO NON DEVE PIU' DIRE IL NOME DEL TIPO. E' la meta'
            // che conta: il rosso c'era anche prima, ma diceva una cosa da cui
            // non si poteva ripartire.
            Check("elenco ROSSO: il messaggio mostra le voci, non «System.String[]»",
                  !Expectations.Matches(list("measurements", new List<object> { "999X999X9" }, null),
                                   null, docState, withList, out d) &&
                  d.IndexOf("System.String[]", StringComparison.Ordinal) < 0 &&
                  d.IndexOf("80X80X5", StringComparison.Ordinal) >= 0, d);
            Check("elenco: «non_contiene» passa se nessuna voce lo dice",
                  Expectations.Matches(list("measurements", null, new List<object> { "999X999X9" }),
                                  null, docState, withList, out d), d);
            Check("elenco ROSSO: «non_contiene» fallisce se una voce lo dice",
                  !Expectations.Matches(list("measurements", null, new List<object> { "40X40X3" }),
                                   null, docState, withList, out d) &&
                  d.IndexOf("dice ancora", StringComparison.Ordinal) >= 0, d);
            // UN ELENCO VUOTO NON CONTIENE NIENTE, e non e' un campo assente:
            // e' un elenco che c'e' ed e' vuoto. Le due cose si distinguono.
            Check("elenco ROSSO: un elenco vuoto non contiene niente",
                  !Expectations.Matches(list("vuoto", new List<object> { "any" }, null),
                                   null, docState, withList, out d), d);
            // UN CAMPO ASSENTE NON E' UNA FRASE VUOTA, e non e' nemmeno un
            // rosso: e' un errore di banco, come per ogni altra misura. Il
            // caso chiede una cosa che nello stato non c'e', e va detto.
            Check("frase ROSSO: un campo che non c'e' non si valuta",
                  Raise(() => { string x;
                               Expectations.Matches(sentence(new List<object> { "x" }, null), null,
                                               new Dictionary<string, object>(), measurements, out x); },
                       "campo assente"));
            // E se invece chi misura risponde «niente», quello si': e' un
            // rosso, perche' la frase c'era da fare e non c'e'.
            Check("frase ROSSO: chi misura risponde niente, e la frase manca",
                  !Expectations.Matches(
                      new Dictionary<string, object>
                      { ["what"] = "forma_del_pezzo", ["includes"] = new List<object> { "x" } },
                      c => null, docState, measurements, out d) &&
                  d.IndexOf("non c'e'", StringComparison.Ordinal) >= 0, d);
            Check("frase ROSSO: un elenco vuoto non chiede niente, e si rifiuta",
                  Raise(() => { string x;
                               Expectations.Matches(sentence(new List<object>(), null), null, withShape, measurements, out x); },
                       "non chiede niente"));
            Check("frase ROSSO: «uguale» insieme a «contiene» si rifiuta",
                  Raise(() =>
                  {
                      var a = sentence(new List<object> { "profilo" }, null);
                      a["equal_to"] = "qualcosa";
                      string x; Expectations.Matches(a, null, withShape, measurements, out x);
                  }, "decidi quale dei due"));

            // ── I SELETTORI: ZERO O PIU' D'UNA SI RIFIUTANO ───────────────
            Check("selettore: una faccia sola si prende",
                  Expectations.Select("@face(normal=front)", docState) == "F1");
            Check("selettore: nessuna faccia, si rifiuta",
                  Raise(() => Expectations.Select("@face(normal=up)", docState), "non l'ho trovata"));

            var due = new Dictionary<string, object>
            {
                ["faces"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "F1", ["facing"] = "front" },
                    new Dictionary<string, object> { ["uid"] = "F5", ["facing"] = "front" },
                },
            };
            Check("selettore: due facce uguali, si rifiuta invece di indovinare",
                  Raise(() => Expectations.Select("@face(normal=front)", due), "ne ho trovate 2"));

            // IL CORPO PER NOME (25/9/2026): i numeri C1, C2... non seguono
            // l'ordine di creazione e cambiano fra un giro e l'altro. Lo stato
            // qui e' scritto al contrario apposta, e il nome ha le parentesi.
            var bodies = new Dictionary<string, object>
            {
                ["bodies"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "C1", ["name"] = "TUBO 80X80X5(2)" },
                    new Dictionary<string, object> { ["uid"] = "C2", ["name"] = "TUBO 80X80X5(1)" },
                },
            };
            Check("selettore: @body prende per nome, parentesi comprese",
                  Expectations.Select("@body(name=TUBO 80X80X5(1))", bodies) == "C2");
            Check("selettore: @body con un nome che non c'e', si rifiuta",
                  Raise(() => Expectations.Select("@body(name=TUBO 80X80X5(3))", bodies), "non l'ho trovata"));

            // LO SPIGOLO PER DOVE STA (voce E18): x, y, z leggono point_mm. I
            // quattro spigoli lunghi 120 di una piastra si distinguono solo cosi'.
            Func<string, double, double, Dictionary<string, object>> edge = (uid, x, z) =>
                new Dictionary<string, object>
                {
                    ["uid"] = uid, ["along"] = "Y", ["length_mm"] = 120.0,
                    ["point_mm"] = new double[] { x, 0, z },
                };
            var four = new Dictionary<string, object>
            {
                ["edges"] = new List<object>
                {
                    edge("E1", -100, 0), edge("E2", 100, 0), edge("E3", -100, 20), edge("E4", 100, 20),
                },
            };
            Check("selettore: lo spigolo si prende per coordinate (x, z)",
                  Expectations.Select("@edge(axis=Y,x~=-100,z~=20)", four) == "E3");
            Check("selettore: con una coordinata sola i fianchi restano due, e si rifiuta",
                  Raise(() => Expectations.Select("@edge(axis=Y,x~=-100)", four), "ne ho trovate 2"));
        }

        private static Dictionary<string, object> Expectation(string what, object equal)
        {
            return new Dictionary<string, object> { ["what"] = what, ["equal_to"] = equal };
        }

        /// <summary>
        /// Alza, e il messaggio contiene quel pezzo? Un controllo che si
        /// accontentasse di «ha alzato» passerebbe anche per l'eccezione
        /// sbagliata — ed e' successo abbastanza volte in questo progetto da
        /// non volerlo piu'.
        /// </summary>
        private static bool Raise(Action f, string part)
        {
            try { f(); return false; }
            catch (Exception ex) { return ex.Message.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        private static bool Raise(Func<string> f, string part)
        {
            return Raise(() => { f(); }, part);
        }
    }
}
