using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LA TOLLERANZA SULLA QUOTA DEL MODELLO (voce Z-F2).
    ///
    /// Un foro Ø10 senza tolleranza non e' un'informazione di fabbricazione: il
    /// tornitore non sa se ci va un perno o un cavo. Messa sulla quota del
    /// MODELLO, la tolleranza arriva in tavola da sola con `model_dimensions`.
    ///
    /// ── MISURATO PRIMA DI SCRIVERLA, 26/9/2026 (`prova0089 tolq`) ─────────
    ///
    /// `IDimension.Tolerance` (`IDimensionTolerance`): `Type` (swTolType_e),
    /// `SetValues2(min, max, config, nomi)`, `SetFitValues(foro, albero)`.
    ///
    ///   simmetrica ±0,05          -> tipo 4, min -0,05, max +0,05
    ///   H7 su Ø10 (tipo 8 e 7)    -> min 0, max +0,015: SOLIDWORKS calcola da
    ///                                se' gli scostamenti ISO 286
    ///
    /// **E IL DIFETTO VISTO NEL PNG:** in tavola la quota usciva «10 H7 +0 / 0».
    /// I limiti si stampano coi decimali della TOLLERANZA, che di serie seguono
    /// quelli della quota — zero. «+0/0» in officina vuol dire «esatta», che e'
    /// peggio di nessuna tolleranza. Qui si impostano i decimali della
    /// tolleranza sulla quota (`IDisplayDimension.SetPrecision3`, il terzo
    /// argomento) e si rileggono.
    ///
    /// ── IL CONTROLLO INDIPENDENTE ───────────────────────────────────────
    ///
    /// Per un accoppiamento il numero lo calcola SOLIDWORKS, e rileggerlo da
    /// SOLIDWORKS e' la stessa lettura due volte. Il riscontro vero e' la tabella
    /// ISO 286-1: l'ampiezza (max - min) deve essere il grado IT della classe per
    /// quel diametro, e il segno quello della lettera (H parte da zero in su, h da
    /// zero in giu'). Se non torna, si rimette com'era e si rifiuta.
    /// </summary>
    public partial class SwOperations
    {
        // ISO 286-1, gradi IT5..IT11 in micrometri, per campi di dimensione
        // (mm, estremo superiore compreso): fino a 3, 6, 10, 18, 30, 50, 80, 120,
        // 180, 250, 315, 400, 500.
        private static readonly double[] Iso286Fields = { 3, 6, 10, 18, 30, 50, 80, 120, 180, 250, 315, 400, 500 };
        private static readonly Dictionary<int, int[]> ItGrades = new Dictionary<int, int[]>
        {
            [5] = new[] { 4, 5, 6, 8, 9, 11, 13, 15, 18, 20, 23, 25, 27 },
            [6] = new[] { 6, 8, 9, 11, 13, 16, 19, 22, 25, 29, 32, 36, 40 },
            [7] = new[] { 10, 12, 15, 18, 21, 25, 30, 35, 40, 46, 52, 57, 63 },
            [8] = new[] { 14, 18, 22, 27, 33, 39, 46, 54, 63, 72, 81, 89, 97 },
            [9] = new[] { 25, 30, 36, 43, 52, 62, 74, 87, 100, 115, 130, 140, 155 },
            [10] = new[] { 40, 48, 58, 70, 84, 100, 120, 140, 160, 185, 210, 230, 250 },
            [11] = new[] { 60, 75, 90, 110, 130, 160, 190, 220, 250, 290, 320, 360, 400 },
        };

        /// <summary>Il grado IT in micrometri per quel diametro, o -1 se fuori tabella.</summary>
        private static int ItMicron(int degree, double diameterMm)
        {
            int[] line;
            if (!ItGrades.TryGetValue(degree, out line) || diameterMm <= 0) return -1;
            for (int k = 0; k < Iso286Fields.Length; k++)
                if (diameterMm <= Iso286Fields[k] + 1e-9) return line[k];
            return -1;
        }

        private Outcome DimensionTolerance(IModelDoc2 model, Dictionary<string, object> a)
        {
            string dimensionName = Str(a, "dimension", "");
            if (string.IsNullOrWhiteSpace(dimensionName))
                return Failed("Non hai detto quale quota. I nomi stanno nello stato del documento, dentro ogni " +
                               "lavorazione (per esempio «D1@Schizzo2»).");
            string typ = (Str(a, "variant", "") ?? "").Trim().ToLowerInvariant();
            var dimension = FindDimension(model, dimensionName, out string location);
            if (dimension == null)
                return Failed("Non trovo la quota «" + dimensionName + "» in questo documento." +
                               (location == null ? "" : " Ci sono: " + location + "."));
            // SOLO QUOTE LINEARI: su una quota angolare SystemValue e i limiti sono
            // in RADIANTI, e «0,5 mm» diventerebbe ±0,0005 rad riletto uguale
            // (revisione del 26/9/2026).
            int dimensionType = -1;
            try { dimensionType = dimension.GetType(); } catch { }
            if (dimensionType == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular)
                return Failed("«" + dimensionName + "» e' una quota ANGOLARE: le tolleranze che so mettere e riscontrare " +
                               "sono in millimetri. Scegli una quota lineare.");
            if (dimensionType != (int)swDimensionParamType_e.swDimensionParamTypeDoubleLinear)
                return Failed("Non riesco a dire se «" + dimensionName + "» e' una quota lineare (tipo " + dimensionType +
                               "): una tolleranza in mm su una quota di altro tipo sarebbe un numero sbagliato. Scegli " +
                               "una quota lineare.");
            double nominal = Units.Mm(ApiHelpers.ReadDimension(dimension));
            var tol = dimension.Tolerance;
            if (tol == null) return Failed("La quota «" + dimensionName + "» non espone la tolleranza.");

            // Com'era, per rimetterla se il riscontro non torna.
            int typeBefore = tol.Type;
            double minBefore, maxBefore;
            ReadToleranceLimits(tol, out minBefore, out maxBefore);
            string holeBefore = SafeFit(() => tol.GetHoleFitValue()), treeBefore = SafeFit(() => tol.GetShaftFitValue());
            const int all = (int)swSetValueInConfiguration_e.swSetValue_InAllConfigurations;

            string fitClass = null; int degree = -1; bool isHole = false;
            int decimals;
            switch (typ)
            {
                case "symmetric":
                {
                    double v;
                    if (!RequiredNum(a, "value_mm", out v, out string m)) return Failed(m);
                    if (v <= 0) return Failed("«value_mm» di una tolleranza simmetrica e' maggiore di zero (±" + v + ").");
                    tol.Type = (int)swTolType_e.swTolSYMMETRIC;
                    tol.SetValues2(-Units.M(v), Units.M(v), all, null);
                    decimals = Math.Max(Decimals(Num(a, "value_mm", 0)), 1);
                    break;
                }
                case "bilateral":
                {
                    double su, down;
                    if (!RequiredNum(a, "upper_mm", out su, out string m1)) return Failed(m1);
                    if (!RequiredNum(a, "lower_mm", out down, out string m2)) return Failed(m2);
                    if (su <= down)
                        return Failed("Lo scostamento superiore (" + su + ") deve essere maggiore dell'inferiore (" + down +
                                       "): con «upper_mm» 0,1 e «lower_mm» -0,05 la quota sta fra -0,05 e +0,1.");
                    tol.Type = (int)swTolType_e.swTolBILAT;
                    tol.SetValues2(Units.M(down), Units.M(su), all, null);
                    decimals = Math.Max(Math.Max(Decimals(su), Decimals(down)), 1);
                    break;
                }
                case "fit":
                {
                    fitClass = (Str(a, "fit_class", "") ?? "").Trim();
                    var mc = Regex.Match(fitClass, @"^([A-Za-z]{1,2})(\d{1,2})$");
                    if (!mc.Success)
                        return Failed("«fit_class» e' una classe ISO come «H7» (foro, maiuscola) o «g6» (albero, " +
                                       "minuscola), non «" + fitClass + "».");
                    isHole = char.IsUpper(mc.Groups[1].Value[0]);
                    degree = int.Parse(mc.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (!ItGrades.ContainsKey(degree))
                        return Failed("Il grado IT" + degree + " non lo so riscontrare (so da IT5 a IT11): senza riscontro " +
                                       "non te lo metto. Scegli un grado fra 5 e 11.");
                    if (ItMicron(degree, nominal) < 0)
                        return Failed("La quota misura " + nominal.ToString("0.###", CultureInfo.InvariantCulture) +
                                       " mm: la tabella ISO 286 che so riscontrare va fino a 500 mm.");
                    tol.Type = (int)swTolType_e.swTolFITWITHTOL;
                    if (isHole) tol.SetFitValues(fitClass, ""); else tol.SetFitValues("", fitClass);
                    decimals = 3;
                    break;
                }
                case "none":
                    tol.Type = (int)swTolType_e.swTolNONE;
                    decimals = -1;
                    break;
                default:
                    return Failed("«variant» e' uno fra «symmetric» (±value_mm), «bilateral» (upper_mm / " +
                                   "lower_mm), «fit» (classe, es. H7 o g6) o «none», non «" + typ + "».");
            }
            try { model.EditRebuild3(); } catch { }

            int readType = tol.Type;
            double minRead, maxRead;
            ReadToleranceLimits(tol, out minRead, out maxRead);
            minRead *= 1000;
            maxRead *= 1000;
            string readHole = SafeFit(() => tol.GetHoleFitValue()), treeRead = SafeFit(() => tol.GetShaftFitValue());

            // ── IL RISCONTRO ────────────────────────────────────────────────
            string crooked = null;
            if (typ == "fit")
            {
                int it = ItMicron(degree, nominal);
                double toleranceBand = Math.Round((maxRead - minRead) * 1000, 1);
                string read = isHole ? readHole : treeRead;
                if (!string.Equals(read, fitClass, StringComparison.Ordinal))
                    crooked = "the class read back is «" + read + "» instead of «" + fitClass + "»";
                else if (Math.Abs(toleranceBand - it) > 0.6)
                    crooked = "the width is " + toleranceBand.ToString("0.#", CultureInfo.InvariantCulture) + " µm, and for IT" +
                             degree + " on " + nominal.ToString("0.###", CultureInfo.InvariantCulture) + " mm ISO 286 gives " + it;
                else if (fitClass == "H" + degree && Math.Abs(minRead) > 1e-6)
                    crooked = "an H hole starts from zero, and the lower deviation read back is " + minRead.ToString("0.####", CultureInfo.InvariantCulture);
                else if (fitClass == "h" + degree && Math.Abs(maxRead) > 1e-6)
                    crooked = "an h shaft ends at zero, and the upper deviation read back is " + maxRead.ToString("0.####", CultureInfo.InvariantCulture);
            }
            else if (typ == "symmetric")
            {
                double v = Num(a, "value_mm", 0);
                if (readType != (int)swTolType_e.swTolSYMMETRIC || Math.Abs(maxRead - v) > 1e-6 || Math.Abs(minRead + v) > 1e-6)
                    crooked = "read back as type " + readType + ", " + minRead + " / " + maxRead;
            }
            else if (typ == "bilateral")
            {
                double su = Num(a, "upper_mm", 0), down = Num(a, "lower_mm", 0);
                if (readType != (int)swTolType_e.swTolBILAT || Math.Abs(maxRead - su) > 1e-6 || Math.Abs(minRead - down) > 1e-6)
                    crooked = "read back as type " + readType + ", " + minRead + " / " + maxRead;
            }
            else if (readType != (int)swTolType_e.swTolNONE)
                crooked = "the type read back is " + readType + " instead of none";

            if (crooked != null)
                return Failed("The tolerance on «" + dimensionName + "» does not check out: " + crooked + "." +
                               RestoreTolerance(model, tol, typeBefore, minBefore, maxBefore, holeBefore, treeBefore, null));

            // ── I DECIMALI DELLA TOLLERANZA, sulla quota come si vede ────────
            //
            // EACH DISPLAYED DIMENSION IS SET AND READ BACK (Z-C21.8, finding
            // L5-7). IDisplayDimension~SetPrecision3.html: «A value of 0
            // indicates success. A value greater than 0 indicates that a
            // problem occurred»; -1 is «Method failed; no precision values were
            // set». The answer was dropped, and only the reading of the LAST
            // dimension of the loop survived: a first dimension left at «+0 / 0»
            // passed when the last one took the decimals. Now every one answers
            // for itself (ToleranceDecimalsFault), and the setting each one had
            // is kept, to be put back with the tolerance on a refusal.
            const int keepPrecision = (int)swDimensionPrecisionSettings_e.swDoNotChangePrecisionSetting;
            var precisionStatuses = new List<int>();
            var precisionRereads = new List<int>();
            var precisionBefore = new List<PrecisionBefore>();
            if (decimals >= 0)
                foreach (var dd in ShownDimensions(model, dimension))
                {
                    int before = int.MinValue, status = int.MinValue, reread = int.MinValue;
                    try { before = dd.GetPrimaryTolPrecision2(); } catch { before = int.MinValue; }
                    try { status = dd.SetPrecision3(keepPrecision, keepPrecision, decimals, keepPrecision); }
                    catch { status = int.MinValue; }
                    if (before != int.MinValue) precisionBefore.Add(new PrecisionBefore(dd, before));
                    try { reread = dd.GetPrimaryTolPrecision2(); } catch { reread = int.MinValue; }
                    precisionStatuses.Add(status);
                    precisionRereads.Add(reread);
                }
            try { model.EditRebuild3(); } catch { }
            int touchedDimensions = precisionStatuses.Count;
            int readDecimals = precisionRereads.Count > 0 ? precisionRereads[0] : -1;
            string decimalsFault = decimals >= 0
                ? ToleranceDecimalsFault(decimals, precisionStatuses.ToArray(), precisionRereads.ToArray())
                : null;

            var measurements = new Dictionary<string, object>
            {
                ["dimension"] = dimensionName,
                ["nominal_mm"] = Math.Round(nominal, 4),
                ["kind"] = typ,
                ["kind_reread"] = readType,
                ["lower_deviation_mm"] = Math.Round(minRead, 4),
                ["upper_deviation_mm"] = Math.Round(maxRead, 4),
                ["lower_limit_mm"] = Math.Round(nominal + minRead, 4),
                ["upper_limit_mm"] = Math.Round(nominal + maxRead, 4),
                ["tolerance_decimals"] = decimals >= 0 ? (object)readDecimals : null,
                ["dimensions_shown_touched"] = touchedDimensions,
            };
            if (typ == "fit")
            {
                measurements["class_name"] = fitClass;
                measurements["it_iso286_um"] = ItMicron(degree, nominal);
                measurements["match_iso286"] = true;
            }
            // DECIMALI NON IMPOSTATI = «+0 / 0» IN TAVOLA: e' un rifiuto, non un
            // avviso (revisione del 26/9/2026). Si rimette la tolleranza di prima.
            if (decimalsFault != null)
            {
                string restored = RestoreTolerance(model, tol, typeBefore, minBefore, maxBefore, holeBefore, treeBefore, precisionBefore);
                return Failed(touchedDimensions == 0
                    ? "The dimension «" + dimensionName + "» is not displayed in the part, and the tolerance decimals are set " +
                      "on the displayed dimension: without them the limits would come out «+0 / 0» in the drawing." + restored +
                      " Show the dimension (or use the one of the feature) and try again."
                    : "The tolerance decimals did not take (" + decimalsFault + ", wanted " + decimals +
                      "): the limits would come out rounded in the drawing, «+0 / 0»." + restored);
            }
            string statusNote = ToleranceDecimalsStatusNote(precisionStatuses.ToArray());
            if (statusNote != null) measurements["precision_status"] = statusNote;
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// Which displayed dimensions did not take the tolerance decimals, in
        /// words, or null when every one did (Z-C21.8, finding L5-7).
        /// <paramref name="statuses"/> are the answers of SetPrecision3
        /// (int.MinValue: it threw) and <paramref name="rereads"/> the
        /// GetPrimaryTolPrecision2 read right after (int.MinValue: not read),
        /// one pair per displayed dimension. No displayed dimension at all is not
        /// «every one did»: it is a fault of its own. Kept apart from the
        /// document so that Oce.Solid.Agent.ContractTests can call it without SOLIDWORKS.
        ///
        /// WHICH ANSWERS ARE A FAULT. IDisplayDimension~SetPrecision3.html: -1
        /// «Method failed; no precision values were set», 3 «Primary tolerance
        /// precision value was invalid» (the value this operation sets), and a
        /// reading that is not the value asked. 1, 2 and 4 name the primary,
        /// alternate and alternate tolerance values, which this operation passes
        /// as swDoNotChangePrecisionSetting: with the tolerance decimals read
        /// back as asked they are not a refusal (the status of that constant is
        /// not measured: swDimensionPrecisionSettings_e.html says «Used by
        /// IDisplayDimension::SetPrecision2 only», while the SetPrecision3 page
        /// lists it for all four values), and the answer reports them
        /// (<see cref="ToleranceDecimalsStatusNote"/>).
        /// </summary>
        internal static string ToleranceDecimalsFault(int wanted, int[] statuses, int[] rereads)
        {
            if (statuses == null || rereads == null || statuses.Length != rereads.Length)
                return "the answers of SetPrecision3 and the readings do not pair up";
            if (statuses.Length == 0) return "no displayed dimension to set";
            var faults = new List<string>();
            for (int i = 0; i < statuses.Length; i++)
            {
                bool statusAboutOtherValues = IsPrecisionStatusAboutOtherValues(statuses[i]);
                if ((statuses[i] == 0 || statusAboutOtherValues) && rereads[i] == wanted) continue;
                faults.Add("displayed dimension " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": SetPrecision3 " +
                           (statuses[i] == int.MinValue ? "threw" : "answered " + statuses[i].ToString(CultureInfo.InvariantCulture)) +
                           ", tolerance decimals read back " +
                           (rereads[i] == int.MinValue ? "not read" : rereads[i].ToString(CultureInfo.InvariantCulture)));
            }
            return faults.Count == 0 ? null : string.Join("; ", faults);
        }

        /// <summary>
        /// The note for the answers of SetPrecision3 that are not 0 and not a
        /// fault (1, 2, 4: see <see cref="ToleranceDecimalsFault"/>), or null
        /// when every answer was 0. The answer is read and said, not dropped.
        /// </summary>
        internal static string ToleranceDecimalsStatusNote(int[] statuses)
        {
            if (statuses == null) return null;
            var odd = new List<string>();
            foreach (int s in statuses)
            {
                string text = s.ToString(CultureInfo.InvariantCulture);
                if (IsPrecisionStatusAboutOtherValues(s) && !odd.Contains(text)) odd.Add(text);
            }
            if (odd.Count == 0) return null;
            return "SetPrecision3 answered " + string.Join(", ", odd) + " (by its page 1, 2 and 4 name the primary, " +
                   "alternate and alternate tolerance values, which this operation leaves as they are): the " +
                   "tolerance decimals read back as asked.";
        }

        /// <summary>
        /// Puts the tolerance back as it was read before the change, and the
        /// tolerance decimals of the displayed dimensions that were touched,
        /// then READS THEM BACK (Z-C21.8, finding M42): the tail of the refusal
        /// says what the reread found. Until library 0.144.0 the refusal said it
        /// was put back as it was right after writing, without reading anything
        /// back, and the decimals already set on the displayed dimensions stayed.
        /// </summary>
        private static string RestoreTolerance(
            IModelDoc2 model, IDimensionTolerance tol, int typeBefore, double minBefore, double maxBefore,
            string holeBefore, string shaftBefore, List<PrecisionBefore> precisionBefore)
        {
            const int allConfigurations = (int)swSetValueInConfiguration_e.swSetValue_InAllConfigurations;
            const int keepPrecision = (int)swDimensionPrecisionSettings_e.swDoNotChangePrecisionSetting;
            try
            {
                tol.Type = typeBefore;
                tol.SetValues2(minBefore, maxBefore, allConfigurations, null);
                if (!string.IsNullOrEmpty(holeBefore) || !string.IsNullOrEmpty(shaftBefore))
                    tol.SetFitValues(holeBefore ?? "", shaftBefore ?? "");
            }
            catch { }
            int precisionNotBack = 0;
            if (precisionBefore != null)
                foreach (var p in precisionBefore)
                {
                    try
                    {
                        // -1 and 3 are faults, 1, 2 and 4 are about the values
                        // left alone (ToleranceDecimalsFault); the reading decides.
                        int status = p.Dimension.SetPrecision3(keepPrecision, keepPrecision, p.Decimals, keepPrecision);
                        if (status < 0 || status == PrecisionPrimaryTolInvalid ||
                            p.Dimension.GetPrimaryTolPrecision2() != p.Decimals) precisionNotBack++;
                    }
                    catch { precisionNotBack++; }
                }
            try { model.EditRebuild3(); } catch { }

            int typeNow = int.MinValue;
            double minNow = double.NaN, maxNow = double.NaN;
            try { typeNow = tol.Type; ReadToleranceLimits(tol, out minNow, out maxNow); }
            catch { typeNow = int.MinValue; }
            if (typeNow == int.MinValue || double.IsNaN(minNow) || double.IsNaN(maxNow))
                return " I tried to put the tolerance back, but I could not read it back: look at the dimension before going on.";
            string holeNow = SafeFit(() => tol.GetHoleFitValue()), shaftNow = SafeFit(() => tol.GetShaftFitValue());
            var off = new List<string>();
            if (typeNow != typeBefore)
                off.Add("type " + typeNow.ToString(CultureInfo.InvariantCulture) + " instead of " + typeBefore.ToString(CultureInfo.InvariantCulture));
            if (Math.Abs(minNow - minBefore) > 1e-9 || Math.Abs(maxNow - maxBefore) > 1e-9)
                off.Add("deviations " + (minNow * 1000).ToString("0.####", CultureInfo.InvariantCulture) + " / " +
                        (maxNow * 1000).ToString("0.####", CultureInfo.InvariantCulture) + " mm instead of " +
                        (minBefore * 1000).ToString("0.####", CultureInfo.InvariantCulture) + " / " +
                        (maxBefore * 1000).ToString("0.####", CultureInfo.InvariantCulture));
            if (!string.Equals(holeNow, holeBefore ?? "", StringComparison.Ordinal) ||
                !string.Equals(shaftNow, shaftBefore ?? "", StringComparison.Ordinal))
                off.Add("fit «" + holeNow + "» / «" + shaftNow + "» instead of «" + (holeBefore ?? "") + "» / «" + (shaftBefore ?? "") + "»");
            if (precisionNotBack > 0)
                off.Add(precisionNotBack.ToString(CultureInfo.InvariantCulture) +
                        " displayed dimension(s) did not take their old tolerance decimals back");
            if (off.Count == 0) return " I put it back as it was, and read it back.";
            return " I tried to put it back, but reading it back: " + string.Join("; ", off) +
                   ". Look at the dimension, and undo the step (" + Sentences.HowToWithdrawIt + ").";
        }

        // THE ANSWERS OF IDisplayDimension.SetPrecision3, by name. swconst has no
        // enum for them; IDisplayDimension~SetPrecision3.html, Remarks, lists
        // 0 success, -1 method failed, and 1, 2, 3, 4 for the primary, dual,
        // primary tolerance and dual tolerance value that was invalid.
        private const int PrecisionPrimaryInvalid = 1;
        private const int PrecisionDualInvalid = 2;
        private const int PrecisionPrimaryTolInvalid = 3;
        private const int PrecisionDualTolInvalid = 4;

        /// <summary>
        /// True for the answers of SetPrecision3 about the values this operation
        /// passes as swDoNotChangePrecisionSetting (see ToleranceDecimalsFault).
        /// </summary>
        private static bool IsPrecisionStatusAboutOtherValues(int status)
        {
            return status == PrecisionPrimaryInvalid || status == PrecisionDualInvalid ||
                   status == PrecisionDualTolInvalid;
        }

        /// <summary>A displayed dimension and the tolerance decimals it had before this step.</summary>
        private sealed class PrecisionBefore
        {
            internal PrecisionBefore(IDisplayDimension dimension, int decimals)
            {
                Dimension = dimension;
                Decimals = decimals;
            }

            internal IDisplayDimension Dimension { get; private set; }
            internal int Decimals { get; private set; }
        }

        /// <summary>
        /// THE ONE PLACE THAT READS THE TOLERANCE LIMITS, in metres. It uses the
        /// obsolete GetMinValue/GetMaxValue (frozen for L5-3, to be moved to
        /// GetMinValue2/GetMaxValue2 with a measure): the value read before the
        /// change, the value read after it and the value read after putting it
        /// back must come from the same call, or the comparison between them
        /// would compare two APIs. Z-C21.8 (M42) added the third reading; it goes
        /// through here instead of adding a third call site.
        /// </summary>
        private static void ReadToleranceLimits(IDimensionTolerance tol, out double min, out double max)
        {
            min = tol.GetMinValue();
            max = tol.GetMaxValue();
        }

        private static string SafeFit(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }

        private static int Decimals(double v)
        {
            var t = Math.Abs(v).ToString("0.######", CultureInfo.InvariantCulture);
            int p = t.IndexOf('.');
            return p < 0 ? 0 : t.Length - p - 1;
        }

        /// <summary>Le quote mostrate nel pezzo che appartengono a questa quota.</summary>
        private static List<IDisplayDimension> ShownDimensions(IModelDoc2 model, IDimension dimension)
        {
            var outside = new List<IDisplayDimension>();
            string itemName = null;
            try { itemName = dimension.FullName; } catch { }
            if (string.IsNullOrEmpty(itemName)) return outside;
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    var dd = f.GetFirstDisplayDimension() as IDisplayDimension;
                    while (dd != null)
                    {
                        try
                        {
                            var d = dd.GetDimension2(0) as IDimension;
                            if (d != null && d.FullName == itemName) outside.Add(dd);
                        }
                        catch { }
                        dd = f.GetNextDisplayDimension(dd) as IDisplayDimension;
                    }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return outside;
        }
    }
}
