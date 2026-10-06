using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// A «NO» WITHOUT A WAY FORWARD COSTS A TURN TO WHOEVER READS IT (entry Z-G4).
    ///
    /// ── THE FACT ──────────────────────────────────────────────────────────
    ///
    /// «Il profilo non ha la misura 40X40X3. Nel file quella misura si scrive
    /// 40x40x3» corrected the model **in one turn**. «Il profilo non ha
    /// quella misura» would have sent it off guessing.
    ///
    /// The difference is not courtesy: it is the number of runs needed to
    /// reach the part. A refusal that names the alternative is a
    /// correction; one that says only no is an obstacle.
    ///
    /// ── THE MEASUREMENT, AND WHY IT IS NOT THE ONE OF THE ENTRY ───────────
    ///
    /// Entry Z-G4 said «~162 refusal points, ~39 (24%) also say what to
    /// do». Counted on 23/9/2026 on all the `.cs` of the library: the points
    /// are **576** — `grep -c "Failed("` finds 577, minus the definition —
    /// and those that offer a way forward are **66**, that is **11%**.
    ///
    /// The number of the entry was an estimate on a subset; this is the
    /// whole count. *More points and a lower percentage: the entry underestimated
    /// the work, it did not overestimate it.*
    ///
    /// ── WHY A RATCHET AND NOT A CORRECTION ──────────────────────────────────
    ///
    /// Five hundred and ten messages to rewrite are the two days that the entry
    /// estimates, and they are not done in an afternoon. But **it can be prevented from
    /// getting worse**: the number below is a floor, and every message that
    /// learns to say what to do raises it. A new refusal written mute makes
    /// the percentage go down and the check turns red.
    ///
    /// ── AND THE DETECTOR IS GENEROUS ON PURPOSE ───────────────────────────
    ///
    /// The words that count as a «way forward» are a broad list — «usa»,
    /// «metti», «oppure», «si scrive»… Broad means that it **overestimates** the
    /// good messages: the real 11% is probably lower. A strict detector
    /// would have produced a more flattering number to improve and a less
    /// true one, and *one can cheat by adding the word «oppure»* — but cheating
    /// here would mean writing a worse message on purpose, and at that
    /// point the problem is not the check.
    /// </summary>
    internal static class RefusalsTests
    {
        /// <summary>
        /// How many refusals, out of the **537 that have a message of their own**, offer
        /// a way forward. **Eighty-three on 23/9/2026, that is 15%.** It can only
        /// go up.
        ///
        /// ── TWO NUMBERS ON THE SAME DAY, AND THE SECOND IS NOT PROGRESS ───────
        ///
        /// The first measurement said **66**. Then the list of remedies was
        /// looked at next to the real messages, and it counted as mute
        /// «So fare: filettato, passaggio, lamato, svasato» — which **names the
        /// alternatives**, that is it does exactly what the entry asks. With
        /// the forms the code really uses the count is 83.
        ///
        /// *Between 66 and 83 nobody worked: the detector improved.*
        /// Writing it here is the point — a floor that rises without anybody
        /// having written a better message teaches to read that number
        /// as a result, and the next time nobody checks.
        ///
        /// **84 since 24/9/2026, and this time it is real work.** By removing the
        /// fallback hard-wired into the profile library, `weldment_profiles` has
        /// gained a refusal that tells «I do not know where it is» from «it is not there»
        /// and says which variable to set. Before, the two cases gave the same
        /// message, with an empty path between quotes.
        ///
        /// **85 since the evening of 24/9/2026 (library 0.113.0).** `set_dimension` has a
        /// new refusal — the opening that leaves the material (entry E18 c) — and
        /// it says the way forward: «sposta prima l'apertura, poi cambia la quota».
        ///
        /// **87 with the new operations of the same version**: `base_flange`
        /// («allunga il tratto o stringi il raggio») and `flat_pattern_dxf` («salvalo
        /// prima»).
        ///
        /// **88 since 25/9/2026 (library 0.114.0)**: `weld_bead`
        /// rewritten on two faces, with the new refusals that say which face to
        /// indicate instead.
        ///
        /// **90 since 25/9/2026 (library 0.116.0)**: the anchor (entry E18), with the
        /// refusals that say how to anchor the missing axis and to which live
        /// edge to attach rather than to the border of a chamfer.
        ///
        /// **75 since 25/9/2026 (library 0.117.0), and it GOES DOWN without any
        /// message having got worse.** The new refusals of Z-C8 had raised the count
        /// to 92, and looking at them one by one neither of the two
        /// triggers was a way forward: «prima di scrivere» contains «scriv», and a
        /// refusal closed on its own line took the «metti» of the NEXT
        /// refusal, because the detector always read the following line.
        /// The detector was corrected (it stops at the end of the message), the real count is
        /// 75: fifteen «ways forward» were borrowed from the neighbour. Measured with
        /// a replica of the detector on HEAD and on the tree: 75 and 75.
        /// *It is the story of 66 and 83 the other way round: a more
        /// honest detector lowers the number, and it must be written for the same reason.*
        ///
        /// **76 since 26/9/2026 (library 0.121.0):** the new refusal of
        /// `set_dimension` on the chamfer or fillet that no longer fits says
        /// the way forward («cambia o togli prima lo smusso o il raccordo, poi la
        /// quota»).
        ///
        /// **77 since 26/9/2026 (library 0.122.0):** `edge_flange` refuses
        /// a flange with nothing straight, saying how far one can go down.
        /// **78, same library:** `hem` refuses an open one without clearance, saying
        /// that without clearance it is called «closed».
        /// **79 since library 0.123.0:** `sketched_bend` refuses 180 degrees
        /// naming «hem».
        /// **81 since library 0.124.0:** `hole_table` says on which
        /// view it goes (V2) and what to do about the double dimensioning; `drawing_dimension`
        /// names the hole table among the things already dimensioned.
        /// **89, same library:** the three annotations (surface finish, datum,
        /// geometric tolerance) say what to do for every refusal — the free
        /// letter, removing the reference, putting the datum first.
        /// **90, library 0.125.0:** `flat_pattern_view` on a part that is not
        /// sheet metal says there is no flat pattern to put on the drawing. **93:**
        /// `drawing_weld_symbol` says on which view to try when the joint is not
        /// visible, and removes by itself the symbol born wrong (or says to remove it).
        /// **94:** the bill of materials on the drawing without room says to hide columns or
        /// to remove x/y and let it choose the place. **98:** the letters of
        /// sections and details («Usa un'altra lettera»), the center marks (the fillets:
        /// «Chiedi fori»; a hole without a mark: the option of the document) and
        /// `dimension_tolerance` («Scegli un grado fra 5 e 11», the example of the
        /// bilateral). **100:** from the adversarial review — the angular dimension
        /// («Scegli una quota lineare») and the dimension not shown («mostra la quota»).
        /// **106:** GT9 — the BOM and the balloons say what to put first
        /// (the view of an assembly, the BOM before the balloons) and what to use
        /// for a weldment. **108:** Z-D1/Z-D6 — a document that is neither part
        /// nor assembly («Aprimi il pezzo o l'assieme»), the STL of an assembly («usa
        /// export_step»), the rereading of the assembly STEP («toglilo e richiedimelo»).
        /// **109:** Z-D5 — `fix_component` outside an assembly («Aprimi l'assieme»).
        /// **116:** Z-F1 — `global_variable` and `link_dimension`: the badly made name (with
        /// the examples), value or expression, the name already taken («usa parete»), the
        /// variable that does not exist («creala con global_variable»), the part to open.
        /// **119:** Z-F3 — `reorder_feature`: the impossible move («spostala
        /// rispetto a una lavorazione che viene dopo»), the missing names, the part.
        /// **122:** Z-F5 — `sketch_picture`: the PDF («convertilo in PNG»),
        /// the file that is not an image («PNG, JPG…»), the image to save again.
        /// **125:** GA11 — `mirror_component`: the mode that is missing (it explains the three), outside
        /// an assembly («c'e' specchia»), the file that is already there («cambia suffisso»).
        /// **128:** Z-F4 — `edit_extrusion`: the feature that is not an extrusion
        /// («c'e' set_dimension»), «through_all» on an extrusion («dimmi depth_mm»),
        /// through-all or depth.
        /// **145 since entry Z-C21.2 (28/9/2026, branch z-c21-2-difetti-silenziosi):**
        /// the silent defects that became refusals say the way forward — the read-only
        /// dimension («togli prima la sola lettura»), the driven one («cambia
        /// invece la quota che la guida»), edit_extrusion in two directions
        /// («cambiala a mano, oppure… set_dimension»), concentric and tangent
        /// (coincident, parallel… with the planar faces), the grid in
        /// suppress_instance («fai la ripetizione con una direzione sola»),
        /// the anchor on a curved face («togli ancora e dai x_mm e y_mm») and
        /// on the edge that cannot be looked at («ancoralo a un altro
        /// spigolo»), the hole outside the part. Nine more according to the
        /// detector: the number was given by the check, the list above is
        /// that of the new messages and not a one-to-one correspondence.
        /// **146, same entry, after the review:** minus two — the intermediate refusals of
        /// `hole_wizard` (before the positioning) are no longer there, and counted
        /// only because «RemoveCut» contains «togli» — and plus three: in
        /// `combine_bodies` uid and `_ref` given together («togli uno dei due»), and the
        /// step of `main_body_ref` that leaves several bodies («nominalo con
        /// «main_body_uid»»).
        /// **213 from the four features of the guide (28/9/2026, branch
        /// quattro-lavorazioni):** +67, as much as the check counted on the merged
        /// tree, and it matches the sum of the replicas of the four file writers, file by
        /// file. **+15, entry GP7 (`fillet_face`):** the same face twice and
        /// parallel faces point to «fillet_edges»; the fillet that is not born,
        /// that is born of another type, that breaks something or does not change the volume
        /// says to try a smaller radius or «fillet_edges». **+18, entry
        /// W1.1 (`fill_pattern`):** the direction that is missing («indica uno spigolo
        /// dritto… oppure… create_axis»), the non-planar face, the direction outside the
        /// face, the pitch and the copies per loop, and the NULL that names linear_pattern /
        /// circular_pattern. **+10, entry W1.2 (`rib`):** the end outside the
        /// plane, the line too short, the rib that is not born in either direction
        /// («extrude_on_face»), the plane to be named. **+24, entry GP20 (`gusset`):**
        /// the new refusals name the alternative — indicate the faces, use the
        /// triangular one, remove the position, try shorter sides,
        /// «weld_bead».
        /// **216 from the correction of the review (same branch):** +3. The face
        /// fillet that removes a volume different from the hand count names
        /// «fillet_edges»; the rib that, failing, leaves in the tree a
        /// feature that is not a «Rib» names «extrude_on_face»; the
        /// gusset born of a type different from «Gusset» names shorter sides and
        /// «weld_bead».
        /// **217, Z-C21.2-ter:** +1, and it is the detector, not a better message:
        /// the new refusal of «mirror_component» for a mirror born with the wrong
        /// orientation ends with the call that removes the mirror, and the name of
        /// that helper holds a remedy word, like the other refusals of that file
        /// that remove it.
        /// **230, Z-C21.8 and Z-C21.9 (30/9/2026, branch z-c21-8-9): +13, and most
        /// of it is the detector.** The refusals are being rewritten in English
        /// (W3.1-bis) and the list of remedy words below was Italian only: a
        /// refusal that named its remedy in Italian counted, the same refusal
        /// saying «remove it» did not. On this branch that took the count DOWN to 212
        /// with no message made worse. The English forms added below are the
        /// imperatives the new messages actually use, picked by reading them one
        /// by one; «check that» was tried and left out because it caught «I
        /// cannot check that», which is not a remedy. Measured with a replica of
        /// this detector: with the English words, HEAD (0.144.0) counts 223 and
        /// this branch 230. So +6 is the detector learning the English that
        /// earlier lots had already written, and +7 is this branch: the refusals
        /// that now say «ask again», «remove it», «try again», «give in
        /// «axis_uid»», «write one of the four», «undo the step».
        /// **232, Z-C21.4 and Z-C21.5 (branch z-c21-4-5): +2.** New refusals of
        /// this branch that say what to do, such as the configuration check of
        /// «insert_component» («insert it without a configuration name, or
        /// open the file again and retry»).
        /// **234, Z-C21.11 (branch z-c21-11): +2.** New refusals of lightweight
        /// components that say what to do (the mass: «Resolve them in SOLIDWORKS,
        /// or ask me again with ...»; the BOM's lightweight sub-assembly and the
        /// box's lightweight part without solid bodies: «Resolve it and ask
        /// again»), less the old mass refusal «nothing to weigh», whose remedy
        /// moved to the first of them.
        /// **236, W3.1-bis.2 (30/9/2026, branch w31bis-2): +2, and it is the
        /// detector.** The list is bilingual now: every Italian remedy word has
        /// the English forms that translate it, so that a refusal keeps counting
        /// when it is translated (W3.1-bis.8). The two refusals it adds are
        /// English and do name their remedy: «ask for the mate once more»
        /// (MateOperations.cs) and «Pass ... to import them all, or ...»
        /// (DrawingOperations.cs). On the Italian refusals nothing moved: 209
        /// with the Italian words alone and 209 with both lists, and on the tree
        /// of 25/9/2026 (library 0.117.0, still Italian) 75 and 75 of 578, the
        /// same percentage. A check below keeps it so.
        /// **238, merge of W3.1-bis.2 with Z-C14 (1/10/2026): +2.** Z-C14 added two
        /// refusals of `mate` (a face not found again after the test of the
        /// directions) that named no remedy: the share net of W3.1-bis.2 caught
        /// them on the merge (236 of 937 under 236 of 935), and both now end with
        /// «ask for the mate once more».
        /// **216, rename window 3 (W3.1-bis.6, 3/10/2026): the same messages.** A
        /// refusal is judged on its string literals only, not on the names of
        /// the code around them: the same count, 216 of 937, on the tree before
        /// the window (4c9e465) and after it. The 22 more of the old 238 were
        /// variables and methods whose Italian names held a remedy word.
        /// **217, W3.1-bis.8 pilot (5/10/2026): +1.** The refusals of the cluster of
        /// cases that assert the messages of Bridge/ are English now: the count of
        /// remedies went from 216 to 217 of 937 with the translation.
        /// **263, W3.1-bis.8 (6/10/2026): +46, the whole of the messages in English.**
        /// The 937 refusals with a message of their own are English now, and the
        /// English forms of the list count them: 263 of 937 (28.1%), against 216 of
        /// 937 (23.0%) on the Italian text of window 3 and the 236 of 935 (25.2%) of
        /// W3.1-bis.2. The rise is the DETECTOR meeting English sentences, not messages
        /// that were improved: a part of the +46 are generic forms («, or», «call it»,
        /// «put the») that an English refusal holds without its being a remedy, and the
        /// translators also restored remedies that a literal translation had hidden. It
        /// is said here so that nobody reads the floor as work done. The drop from 236
        /// to 216 in window 3 was never lost remedies: the old count judged the
        /// whole statement, and 22 variables and methods with an Italian name held a
        /// remedy word; when the names became English the words went, and the
        /// messages stayed what they were.
        /// </summary>
        private const int Floor = 263;

        /// <summary>
        /// THE SHARE OF REFUSALS THAT SAY WHAT TO DO may not go down: today's
        /// share, kept as the fraction it is (W3.1-bis.2, 30/9/2026: 236 of
        /// 935, that is 25.2%) and compared exactly, so that ONE new mute
        /// refusal is enough to turn it red (in thousandths it was not).
        ///
        /// The header of this file always said that a new refusal written mute
        /// lowers the percentage and turns the check red. The code checked only
        /// the absolute count, which a new mute refusal does not lower: the
        /// promise was a sentence, not a check. This floor is the check. It is
        /// raised by hand, like the one above, when the share goes up.
        /// </summary>
        private const int ShareFloorWithRemedy = 263;   // 6/10/2026, W3.1-bis.8: 263 of 937 (217 after the pilot, 216 before and after window 3)
        private const int ShareFloorWithText = 937;

        /// <summary>
        /// The openings with which this code says no.
        /// </summary>
        private static readonly string[] Aperture = { "Failed(", "Rifiuta(", "NonPosso(" };

        /// <summary>
        /// One remedy word in Italian, the English forms that translate it, and
        /// an English refusal that says the same thing (its own forms must
        /// catch it).
        /// </summary>
        private sealed class Remedy
        {
            internal readonly string Italian;
            internal readonly string[] English;
            internal readonly string Sample;

            internal Remedy(string italian, string[] english, string sample)
            {
                Italian = italian;
                English = english;
                Sample = sample;
            }
        }

        /// <summary>
        /// The words with which a message points to a way forward. A deliberately
        /// broad list: see the comment at the top.
        ///
        /// BILINGUAL SINCE W3.1-bis.2 (30/9/2026). The refusals are being
        /// translated message by message (W3.1-bis.8), and a refusal that loses
        /// its remedy word in translation drops out of the count although it
        /// still says what to do: the floor would go red for the wrong reason,
        /// or be lowered and stop protecting. So every Italian word has its
        /// English partners, picked as the translation of that word in the
        /// messages that use it. The Italian words match anywhere, as they
        /// always did (the count on the Italian messages must not move); the
        /// English ones only at the start of a word, because «use » is inside
        /// «because », and inside Italian words too. The Italian column goes
        /// away at the tag, with the rest of the transition.
        /// </summary>
        private static readonly Remedy[] Remedies =
        {
            new Remedy("usa ", new[] { "use «", "use the ", "use a ", "use an ", "use it", "use them", "use one " },
                       "Use the hole wizard for a threaded hole."),
            new Remedy("usa«", new[] { "use «" }, "Use «slot» instead."),
            new Remedy("metti ", new[] { "put the ", "put it", "put a ", "set it", "set the ", "set «" },
                       "Put the sketch on a plane first."),
            new Remedy("chiedi ", new[] { "ask for ", "ask me ", "ask again", "ask me again" },
                       "Read the assembly again and ask for the mate once more."),
            new Remedy("aggiungi ", new[] { "add a ", "add the ", "add it", "add «", "add one" },
                       "Add a sketch before the cut."),
            new Remedy("prova ", new[] { "try again", "try a ", "try the ", "try it", "try with ", "try «" },
                       "Try a smaller radius."),
            new Remedy("togli", new[] { "remove it", "remove the ", "remove «", "remove them", "remove one", "delete it", "delete the " },
                       "Remove «position» and the middle is used."),
            new Remedy("salvalo", new[] { "save it", "save the " }, "The part was never saved: save it first."),
            new Remedy("scriv", new[] { "write one of", "write it", "write the ", "write «", "is written", "are written" },
                       "Write the size as 40x40x3."),
            new Remedy("riattiva", new[] { "reactivate", "activate it", "turn it back on" },
                       "Reactivate the configuration and call me again."),
            new Remedy("si fa con", new[] { "is done with", "is made with", "can be done with" },
                       "A threaded hole is done with the hole wizard."),
            new Remedy("passa ", new[] { "pass «", "pass the ", "switch to " },
                       "Pass «export_anyway»: true if you know what you are doing."),
            new Remedy("indica ", new[] { "point to ", "point at ", "give «", "give in «", "name the " },
                       "Point to an edge of the part."),
            new Remedy("per farlo", new[] { "to do it", "to do that", "to do so" },
                       "To do that, the drawing must be open."),
            new Remedy("se vuoi", new[] { "if you want" }, "If you want a deeper cut, give the depth."),
            new Remedy("chiamala", new[] { "call it" }, "Call it again with the name of the view."),
            new Remedy("rifai", new[] { "redo ", "do it again", "run it again" }, "Redo the sketch on the other face."),
            new Remedy("apri ", new[] { "open the ", "open it", "open «", "open a " }, "Open the assembly first."),
            new Remedy("fai il check-out", new[] { "check it out", "check out " }, "Check it out of the vault first."),
            new Remedy("con «", new[] { "with «" }, "Round it with «fillet_edges»."),
            new Remedy("oppure", new[] { ", or " }, "Tell me the path, or save the part first."),

            // ADDED ON 23/9/2026, LOOKING AT THE REAL MESSAGES. The first
            // draft of the list was written at a desk and counted as mute
            // «So fare: filettato, passaggio, lamato, svasato», which **names
            // the alternatives** and is exactly what the entry asks for.
            //
            // The number that rises here rises because the DETECTOR
            // improved, not the messages: it must be said, otherwise the next
            // person who reads the floor believes that somebody worked.
            new Remedy("so fare", new[] { "i can do", "i can make", "i know how" },
                       "I can make: tapped, clearance, counterbore, countersink."),
            new Remedy("per esempio", new[] { "for example", "for instance", "e.g." }, "Give a size, for example 40x40x3."),
            new Remedy("dimmi", new[] { "tell me" }, "Tell me how deep the thread is."),
            new Remedy("dimmela", new[] { "tell me" }, "A blind hole wants a depth: tell me."),
            new Remedy("puoi ", new[] { "you can " }, "You can pass the flag that imports them all."),
            new Remedy("ti serve", new[] { "you need" }, "You need a saved part for this."),
            new Remedy("va scritt", new[] { "must be written", "has to be written", "is written" },
                       "The size must be written as 40x40x3."),
            new Remedy("si indica", new[] { "is given", "is indicated" }, "The depth is given in millimetres."),
        };

        /// <summary>
        /// ADDED ON 30/9/2026 (Z-C21.8 and Z-C21.9): English forms that the
        /// refusals rewritten in English actually use and that no Italian word
        /// of the list translates. Imperatives only, with the word that
        /// follows, so that «I removed the view» or «does not give access» do
        /// not count as a remedy.
        /// </summary>
        private static readonly string[] EnglishOnly =
        {
            "show the ", "undo the step", "leave «", "take them back",
        };

        private static readonly string[] ItalianForms = Remedies.Select(r => r.Italian).ToArray();

        private static readonly string[] EnglishForms =
            Remedies.SelectMany(r => r.English).Concat(EnglishOnly).Distinct(StringComparer.Ordinal).ToArray();

        /// <summary>A refusal found in the sources, with the text it can be judged on.</summary>
        private sealed class Refusal
        {
            internal string File;
            internal int Line;
            internal string Text;       // the statement, lower case
            internal string Prose;      // its string literals with a space in them
            internal bool Italian;      // an Italian remedy word is in it
            internal bool English;      // an English remedy form starts a word in it
        }

        /// <summary>What a set of sources says about its refusals.</summary>
        private sealed class Tally
        {
            internal int Points, WithText, Forwarded;
            internal readonly List<Refusal> Refusals = new List<Refusal>();
            internal int WithRemedy { get { return Refusals.Count(r => r.Italian || r.English); } }
            internal int PerMille { get { return WithText == 0 ? 0 : WithRemedy * 1000 / WithText; } }

            internal bool AtLeast(int floorWithRemedy, int floorWithText)
            {
                return ShareAtLeast(WithRemedy, WithText, floorWithRemedy, floorWithText);
            }
        }

        internal static void All()
        {
            Group("the refusals that also say what to do (Z-G4)");

            var folder = Path.Combine(Root(), "Oce.Solid.Agent.Core");
            if (!Directory.Exists(folder))
            {
                Check("the library folder is there", false, "missing " + folder);
                return;
            }

            var sources = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
                .Select(f => new KeyValuePair<string, string[]>(Path.GetFileName(f), File.ReadAllLines(f)));
            var tally = Count(sources, ItalianForms, EnglishForms);
            int total = tally.Points, withText = tally.WithText, forward = tally.Forwarded, withRoad = tally.WithRemedy;

            Check("the refusal points are counted", total > 0,
                  total + " points: " + withText + " with a message of their own, " +
                  forward + " that forward one written elsewhere");

            // THE FLOOR GOES UP AND DOES NOT GO DOWN.
            Check("the refusals with a way forward have not decreased",
                  withRoad >= Floor,
                  withRoad + " out of " + withText + " (" + (withText == 0 ? 0 : withRoad * 100 / withText) +
                  "%), floor " + Floor);

            Check("and the floor is the real one", withRoad == Floor,
                  withRoad == Floor
                      ? withRoad + ", as measured"
                      : "there are " + withRoad + ": raise the floor to this number");

            // WHERE THE MUTE ONES ARE, because a number without an address does not
            // make anybody start.
            var perFile = tally.Refusals.Where(r => !r.Italian && !r.English)
                .GroupBy(r => r.File).ToDictionary(g => g.Key, g => g.Count());
            var worse = perFile.OrderByDescending(x => x.Value).Take(5)
                .Select(x => x.Key + " (" + x.Value + ")");
            Check("it is known where to start", perFile.Count > 0 || withRoad == total,
                  perFile.Count == 0 ? "no mute refusal"
                                     : "the most mute: " + string.Join(", ", worse));

            Bilingual(tally);
        }

        /// <summary>
        /// The checks of W3.1-bis.2: the share floor, and the two halves of the
        /// word list watching each other through the translation.
        /// </summary>
        private static void Bilingual(Tally tally)
        {
            // THE SHARE, which a new mute refusal lowers.
            Check("the share of refusals that say what to do has not gone down",
                  tally.AtLeast(ShareFloorWithRemedy, ShareFloorWithText),
                  Share(tally.PerMille) + " (" + tally.WithRemedy + " of " + tally.WithText + "), floor " +
                  ShareFloorWithRemedy + " of " + ShareFloorWithText +
                  (tally.AtLeast(ShareFloorWithRemedy + 1, ShareFloorWithText) ? ": raise the floor to this share" : ""));

            // EVERY ITALIAN WORD HAS ITS ENGLISH, AND THE ENGLISH CATCHES WHAT IT
            // TRANSLATES. Remove an English form and its sample goes mute here.
            var holes = new List<string>();
            foreach (var r in Remedies)
            {
                if (r.English.Length == 0) { holes.Add("«" + r.Italian.Trim() + "» has no English partner"); continue; }
                if (!StartsAWord(r.Sample.ToLowerInvariant(), r.English))
                    holes.Add("the sample of «" + r.Italian.Trim() + "» («" + r.Sample + "») is caught by none of " +
                              string.Join(", ", r.English.Select(e => "«" + e + "»")));
            }
            Check("every Italian remedy word has English partners, and they catch an English refusal that says the same",
                  holes.Count == 0, holes.Count == 0 ? Remedies.Length + " words, " + EnglishForms.Length + " English forms"
                                                     : string.Join("; ", holes));

            // ON THE ITALIAN MESSAGES THE ENGLISH ADDS NOTHING: the bilingual list,
            // on a tree that is still Italian, gives the same count (TODO,
            // W3.1-bis.2). An English form that is also an Italian word
            // («indicate » is one, measured) would count a mute refusal as a good one.
            var words = ItalianFunctionWords();
            if (words == null)
            {
                Check("on the Italian refusals the English forms add nothing", false,
                      "Checks/language/lexicon.json has no function_words to tell Italian from English");
                return;
            }
            var italian = tally.Refusals.Where(r => LooksItalian(r.Prose, words)).ToList();
            var borrowed = italian.Where(r => r.English && !r.Italian).ToList();
            Check("on the Italian refusals the English forms add nothing (the same count as the Italian words alone)",
                  borrowed.Count == 0,
                  borrowed.Count == 0
                      ? italian.Count(r => r.Italian) + " of " + italian.Count + " Italian refusals, with or without the English forms"
                      : borrowed.Count + " Italian refusals counted only by an English form: " +
                        string.Join("; ", borrowed.Take(4).Select(r => r.File + ":" + r.Line + " «" + FirstEnglish(r.Text) + "»")));

            // AND THESE CHECKS, SEEN RED ON FAKE SOURCES.
            SeenRed(words);
        }

        private static void SeenRed(HashSet<string> words)
        {
            const char Q = '"';
            var wrong = new List<string>();

            // a new mute refusal lowers the share under the floor
            var two = new[]
            {
                "return Failed(" + Q + "The face is not planar. Use the top face instead." + Q + ");",
                "return Failed(" + Q + "The face is not planar." + Q + ");",
                "return Failed(error);",
            };
            var t2 = Count(new[] { new KeyValuePair<string, string[]>("Fake.cs", two) }, ItalianForms, EnglishForms);
            if (!(t2.Points == 3 && t2.Forwarded == 1 && t2.WithText == 2 && t2.WithRemedy == 1 && t2.PerMille == 500))
                wrong.Add("the fake source was counted " + t2.WithRemedy + "/" + t2.WithText + ", " + t2.Forwarded + " forwarded");
            var three = two.Concat(new[] { "return Failed(" + Q + "The edge is not straight." + Q + ");" }).ToArray();
            var t3 = Count(new[] { new KeyValuePair<string, string[]>("Fake.cs", three) }, ItalianForms, EnglishForms);
            if (!(t3.WithRemedy == t2.WithRemedy && t2.AtLeast(t2.WithRemedy, t2.WithText) &&
                  !t3.AtLeast(t2.WithRemedy, t2.WithText)))
                wrong.Add("a new mute refusal did not take the share under the floor (" + t2.PerMille + " -> " + t3.PerMille + " per mille)");
            // on today's numbers one mute refusal more is enough, one with a remedy more is not
            if (ShareAtLeast(236, 936, 236, 935) || !ShareAtLeast(237, 936, 236, 935) || !ShareAtLeast(236, 935, 236, 935))
                wrong.Add("the exact comparison of the share does not see a single refusal");

            // an English form that is a piece of Italian is caught on an Italian refusal
            var sentence = ItalianSentinel();
            if (sentence == null) wrong.Add("lexicon.json has no exception_sentinels to build an Italian refusal from");
            else
            {
                var piece = sentence.Split(' ').First(w => w.Length >= 5 && w.All(char.IsLetter)) + " ";
                var fake = new[] { "return Failed(" + Q + sentence + Q + ");" };
                var tf = Count(new[] { new KeyValuePair<string, string[]>("Fake.cs", fake) }, new string[0], new[] { piece });
                bool caught = tf.Refusals.Count == 1 && LooksItalian(tf.Refusals[0].Prose, words) &&
                              tf.Refusals[0].English && !tf.Refusals[0].Italian;
                if (!caught) wrong.Add("an English form «" + piece + "» inside an Italian refusal was not caught");
            }

            // an English form only counts at the start of a word
            if (StartsAWord("the parts that i could not close because ", new[] { "use " }))
                wrong.Add("«use » was taken inside «because »");

            Check("the counter, the share floor and the language guard turn red on fake sources",
                  wrong.Count == 0, wrong.Count == 0 ? "5 fakes ended as expected" : string.Join("; ", wrong));
        }

        /// <summary>
        /// The refusals of a set of sources. The message goes on over the next
        /// lines, but not past its own end.
        /// </summary>
        private static Tally Count(IEnumerable<KeyValuePair<string, string[]>> files, string[] italian, string[] english)
        {
            var tally = new Tally();
            foreach (var file in files)
            {
                var lines = file.Value;
                for (int i = 0; i < lines.Length; i++)
                {
                    var r = lines[i];
                    var bare = r.TrimStart();
                    if (bare.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!Aperture.Any(a => r.Contains(a))) continue;

                    // THE MESSAGE CONTINUES ON THE FOLLOWING LINES. Stopping at the
                    // first would mean judging almost all of them mute: here
                    // long messages are the norm, and they are precisely the ones that
                    // do have a way forward.
                    //
                    // BUT NOT BEYOND ITS END. Until 25/9/2026 the loop
                    // always read at least the following line, even when the
                    // message closed on its own: a short refusal
                    // took the «metti» of the next refusal, and the
                    // floor rose by one without anybody writing a
                    // better message.
                    var text = r;
                    if (!r.Contains(");"))
                    for (int j = i + 1; j < lines.Length && j < i + 12; j++)
                    {
                        if (lines[j].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                        text += " " + lines[j];
                        if (lines[j].Contains(");")) break;
                    }

                    tally.Points++;

                    // ── THREE KINDS, NOT TWO ─────────────────────────────
                    //
                    // `return Failed(error)` is not a mute refusal: it is a
                    // refusal that **forwards** a message written elsewhere.
                    // Judging it here would mean counting it twice — once
                    // where the text is and once where it passes — and inflating the
                    // problem. A point without even a quotation mark has no
                    // message to judge.
                    if (text.IndexOf('"') < 0) { tally.Forwarded++; continue; }

                    tally.WithText++;
                    // W3.1-bis.6: only what the message says, its string literals.
                    // The statement around them names variables and methods: until
                    // rename window 3 a variable or a method whose Italian name held
                    // a remedy word (reactivate, remove) counted as a remedy, and the count moved
                    // with the names of the code instead of with the messages.
                    var down = string.Join(" ", Regex.Matches(text, @"""((?:[^""\\]|\\.)*)""").Cast<Match>()
                                                  .Select(m => m.Groups[1].Value)).ToLowerInvariant();
                    tally.Refusals.Add(new Refusal
                    {
                        File = file.Key,
                        Line = i + 1,
                        Text = down,
                        Prose = string.Join(" ", Regex.Matches(text, @"""((?:[^""\\]|\\.)*)""").Cast<Match>()
                                                      .Select(m => m.Groups[1].Value).Where(s => s.IndexOf(' ') >= 0)),
                        Italian = italian.Any(x => down.Contains(x)),
                        English = StartsAWord(down, english),
                    });
                }
            }
            return tally;
        }

        /// <summary>True when one of the forms starts a word of the text (the text in lower case).</summary>
        private static bool StartsAWord(string text, IEnumerable<string> forms)
        {
            return forms.Any(f => FirstAt(text, f) >= 0);
        }

        // A form that begins with a letter must begin a word; one that begins
        // with punctuation («, or ») carries its own boundary.
        private static int FirstAt(string text, string form)
        {
            bool word = form.Length > 0 && char.IsLetter(form[0]);
            for (int i = text.IndexOf(form, StringComparison.Ordinal); i >= 0;
                 i = text.IndexOf(form, i + 1, StringComparison.Ordinal))
                if (!word || i == 0 || !char.IsLetter(text[i - 1])) return i;
            return -1;
        }

        private static string FirstEnglish(string text)
        {
            var f = EnglishForms.FirstOrDefault(e => FirstAt(text, e) >= 0);
            return f ?? "?";
        }

        /// <summary>withRemedy / withText is at least floorWithRemedy / floorWithText, compared exactly.</summary>
        private static bool ShareAtLeast(int withRemedy, int withText, int floorWithRemedy, int floorWithText)
        {
            return withText > 0 && (long)withRemedy * floorWithText >= (long)floorWithRemedy * withText;
        }

        private static string Share(int perMille)
        {
            return (perMille / 10) + "." + (perMille % 10) + "%";
        }

        // ── TELLING AN ITALIAN REFUSAL FROM AN ENGLISH ONE ───────────────
        //
        // The Italian function words come from the lexicon of the language
        // gate, so this file carries none, less the words the lexicon itself
        // calls weak because English writes them too. A hyphenated word stays
        // whole («non-planar» is English), and what stands between
        // guillemets, in backticks or in a snake_case or camelCase name is a
        // protocol name, not the language of the sentence. Measured on
        // 30/9/2026: 817 of the 935 refusals with a message read as Italian,
        // and the ones that read so by a single word were checked by eye.

        private static HashSet<string> ItalianFunctionWords()
        {
            var list = LexiconList("function_words");
            if (list == null || list.Count == 0) return null;
            var weak = new HashSet<string>(LexiconList("weak_words") ?? new List<string>(), StringComparer.Ordinal);
            return new HashSet<string>(list.Where(w => !weak.Contains(w)), StringComparer.Ordinal);
        }

        private static string ItalianSentinel()
        {
            var list = LexiconList("exception_sentinels");
            return list == null ? null : list.FirstOrDefault(s => s.IndexOf(' ') > 0);
        }

        private static List<string> LexiconList(string key)
        {
            try
            {
                var file = Path.Combine(Root(), "Checks", "language", "lexicon.json");
                var json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                    .DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
                object value;
                if (json == null || !json.TryGetValue(key, out value) || !(value is object[])) return null;
                return ((object[])value).Select(Convert.ToString).ToList();
            }
            catch (Exception) { return null; }
        }

        private static bool LooksItalian(string prose, HashSet<string> functionWords)
        {
            var s = Regex.Replace(prose ?? "", @"«[^»]*»|`[^`]*`", " ");
            s = Regex.Replace(s, @"\b[A-Za-z0-9]+(?:_[A-Za-z0-9]+)+\b|\b[a-z]+[A-Z][A-Za-z0-9]*\b", " ");
            if (Regex.IsMatch(s, "[\u00e0\u00e8\u00e9\u00ec\u00f2\u00f9\u00c0\u00c8\u00c9\u00cc\u00d2\u00d9]")) return true;
            if (Regex.IsMatch(s, @"[A-Za-z][aeiouAEIOU]'(?=\s|$|[.,;:!?)»])")) return true;
            foreach (Match m in Regex.Matches(s, @"[\p{L}'’]+(?:-[\p{L}'’]+)*"))
                if (functionWords.Contains(m.Value.ToLowerInvariant())) return true;
            return false;
        }
    }
}
