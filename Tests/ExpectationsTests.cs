using System;
using System.Collections.Generic;
using Oce.Solid.Agent.Bench;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE EVALUATOR OF THE EXPECTATIONS, against fake states written by hand.
    ///
    /// **If it is broken, the whole suite with SOLIDWORKS becomes green for free**:
    /// a comparison that always answers «yes» is not seen anywhere, except
    /// from here. It is the reason this group exists, and for which the
    /// evaluator was moved into a file that compiles even where the CAD
    /// is not there.
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

            // ── THE RIGHT NUMBER PASSES, THE WRONG ONE DOES NOT ───────────
            Check("numero giusto: verde",
                  Expectations.Matches(Expectation("measurements.removed_mm3", 1234.5), null, docState, measurements, out d), d);
            Check("numero sbagliato: rosso",
                  !Expectations.Matches(Expectation("measurements.removed_mm3", 1234.6), null, docState, measurements, out d), d);

            // ── THE TOLERANCE IS ABSOLUTE, AND THE WHY IS NEEDED ──────────
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

            // ── THE ABSENT FIELD IS NOT A ZERO (entry F4) ─────────────────
            //
            // It is the trap in its literal form: a dictionary that does not
            // find the key returns null, and null compared with null passes.
            Check("campo assente: non si valuta, e dice quali c'erano",
                  Raise(() => { string x; Expectations.Matches(Expectation("measurements.non_esiste", 0.0), null, docState, measurements, out x); },
                       "campo assente: measurements.non_esiste"));
            Check("campo assente: elenca i campi che c'erano",
                  Raise(() => { string x; Expectations.Matches(Expectation("measurements.non_esiste", 0.0), null, docState, measurements, out x); },
                       "faces_removed, facing, removed_mm3"));

            // ── A DECLARED NULL IS MEASURED ───────────────────────────────
            Check("«material: null» si misura, non e' assente",
                  Expectations.Matches(Expectation("state.material", ""), null, docState, measurements, out d), d);

            // ── THE LISTS OF NUMBERS ──────────────────────────────────────
            Check("elenco di numeri giusto: verde",
                  Expectations.Matches(Expectation("state.bounding_box_mm", new List<object> { 100.0, 60.0, 40.0 }),
                                  null, docState, measurements, out d), d);
            Check("elenco di numeri con un valore diverso: rosso",
                  !Expectations.Matches(Expectation("state.bounding_box_mm", new List<object> { 100.0, 60.0, 41.0 }),
                                   null, docState, measurements, out d), d);
            Check("elenco di lunghezza diversa: rosso",
                  !Expectations.Matches(Expectation("state.bounding_box_mm", new List<object> { 100.0, 60.0 }),
                                   null, docState, measurements, out d), d);

            // ── INSIDE A LIST, BY INDEX ───────────────────────────────────
            Check("stato.facce[1].area_mm2 si legge",
                  Expectations.Matches(Expectation("state.faces[1].area_mm2", 2000.0), null, docState, measurements, out d), d);
            Check("un indice oltre la fine: non si valuta",
                  Raise(() => { string x; Expectations.Matches(Expectation("state.faces[9].area_mm2", 0.0), null, docState, measurements, out x); },
                       "campo assente"));

            // ── THE MEASUREMENTS OF THE DOCUMENT, WITHOUT A DOCUMENT ──────
            //
            // Here SOLIDWORKS is not there: `volume_mm3` cannot be measured, and the
            // evaluator SAYS so instead of answering zero.
            Check("«volume_mm3» senza documento: lo dice",
                  Raise(() => { string x; Expectations.Matches(Expectation("volume_mm3", 0.0), null, docState, measurements, out x); },
                       "si misura sul documento"));

            // And with a fake document — a function that returns numbers — it
            // is measured all right: it is the bridge that the real bench passes to it.
            Func<string, object> fake = c => c == "volume_mm3" ? (object)240000.0 : null;
            Check("«volume_mm3» con chi sa misurarlo: verde",
                  Expectations.Matches(Expectation("volume_mm3", 240000.0), fake, docState, measurements, out d), d);

            // ── A NAME THAT DOES NOT EXIST ────────────────────────────────
            //
            // THE LIST OF NAMES IS NO LONGER IN THE EVALUATOR, and this is the
            // proof that it does not go back. It was there, and it came unstuck: two new measurements
            // added to whoever measures and forgotten here came out as «non
            // so misurare «last_plane_mm»: i nomi sono …» with the old
            // list next to it — a message that said what was false about itself,
            // and that gave ERRORE DI BANCO to a check that the bridge passed.
            //
            // Now the evaluator does not judge the names: it passes them to whoever has the
            // document, and it is that one who says it does not know them. The passing is
            // verified (the function is really called) and that
            // the sentence of the refusal reaches the reader whole.
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

            // ── TWO DIFFERENT PROFILES ARE NOT WRITTEN THE SAME ───────────
            //
            // IT IS THE RED OF A DEFECT THAT REALLY TRIGGERED. The comparison
            // between a case that must fail and its twin used
            // `Convert.ToString`, which on an `object[]` returns «System.Object[]»:
            // the refusal of the revolve and its twin differ ONLY
            // in the contours, and the bench called them «a copy».
            //
            // The two lines below are the two faces of the same blindness:
            // different lists must be written different, equal lists equal —
            // because a function that always returns different strings would pass
            // the first check and break the comparison all the same.
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

            // ── A LIST THAT HAS NO NUMBERS ────────────────────────────────
            //
            // `["C2"]` — the bodies a boolean worked on. Until
            // 0.81.0 it came out as «non e' un elenco di numeri: System.String[]»: a
            // true and useless message, because the case asked something
            // sensible and the evaluator did not know how to compare it.
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

            // ── HAS THE TWIN ANYTHING TO DO WITH THE REFUSAL? (F5) ────────
            //
            // The rule lives here and not next to the bench because it is arithmetic
            // on two dictionaries: its reds are tested in a thousandth of a
            // second, and they are four cases that nobody ever looks at by hand.
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

            // THE EXCEPTION OF THE ARITY. `create_axis` takes `face_uid` and nothing else:
            // two cases that differ in that cannot have one in
            // common, and refusing them would forbid the pair for half the catalogue.
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

            // THE DIFFERENCE DECLARED IN THE PART (28/9/2026, rib W1.2):
            // same arguments, different steps before. In both directions, because a
            // declaration that always passes is the fake argument from before
            // under another name.
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

            // The contours are lists: it is the case that broke the comparison.
            Check("gemelle che differiscono solo in una lista: accettate",
                  Expectations.Resemble(
                      new Dictionary<string, object> { ["contours"] = straddling, ["angle_deg"] = 360.0 },
                      new Dictionary<string, object> { ["contours"] = cylinder, ["angle_deg"] = 360.0 },
                      out d2), d2);

            // ── AN EXPECTATION ON A SENTENCE ──────────────────────────────
            //
            // `non_contiene` is the half that counts: a sentence that declares «the
            // Ø30 by 40 profile» on a part that no longer has that profile
            // is the defect that gives no error.
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

            // ── AND A LIST IS NOT «System.String[]» ───────────────────────
            //
            // `measurements.profiles[0].measurements` is a list: asking that it CONTAIN
            // an entry is the most natural thing in the world, and until 0.91.0
            // it failed with «non dice "80X80X5": System.String[]» — a true
            // and useless message. For «equals» the lists of text had already
            // been taught; for «contains» they had not, and nobody had noticed
            // because no case asked for it.
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
            // THE MESSAGE MUST NO LONGER SAY THE NAME OF THE TYPE. It is the half
            // that counts: the red was there before too, but it said something from which
            // one could not start again.
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
            // AN EMPTY LIST CONTAINS NOTHING, and it is not an absent field:
            // it is a list that is there and is empty. The two things are told apart.
            Check("elenco ROSSO: un elenco vuoto non contiene niente",
                  !Expectations.Matches(list("vuoto", new List<object> { "any" }, null),
                                   null, docState, withList, out d), d);
            // AN ABSENT FIELD IS NOT AN EMPTY SENTENCE, and it is not even a
            // red: it is a bench error, as for every other measurement. The
            // case asks for something that is not in the state, and it must be said.
            Check("frase ROSSO: un campo che non c'e' non si valuta",
                  Raise(() => { string x;
                               Expectations.Matches(sentence(new List<object> { "x" }, null), null,
                                               new Dictionary<string, object>(), measurements, out x); },
                       "campo assente"));
            // And if instead whoever measures answers «nothing», that yes: it is a
            // red, because the sentence was due and is not there.
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

            // ── THE SELECTORS: ZERO OR MORE THAN ONE ARE REFUSED ──────────
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

            // THE BODY BY NAME (25/9/2026): the numbers C1, C2... do not follow
            // the order of creation and change between one run and the next. The state
            // here is written backwards on purpose, and the name has the brackets.
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

            // THE EDGE BY WHERE IT IS (entry E18): x, y, z read point_mm. The
            // four edges 120 long of a plate are told apart only like this.
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
        /// Does it raise, and does the message contain that piece? A check that
        /// was satisfied with «it raised» would pass for the wrong
        /// exception too — and it has happened often enough in this project that
        /// we do not want it any more.
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
