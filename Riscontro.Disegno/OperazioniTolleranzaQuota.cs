using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LA TOLLERANZA SULLA QUOTA DEL MODELLO (voce Z-F2).
    ///
    /// Un foro Ø10 senza tolleranza non e' un'informazione di fabbricazione: il
    /// tornitore non sa se ci va un perno o un cavo. Messa sulla quota del
    /// MODELLO, la tolleranza arriva in tavola da sola con `quote_dal_modello`.
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
    public partial class OperazioniSw
    {
        // ISO 286-1, gradi IT5..IT11 in micrometri, per campi di dimensione
        // (mm, estremo superiore compreso): fino a 3, 6, 10, 18, 30, 50, 80, 120,
        // 180, 250, 315, 400, 500.
        private static readonly double[] CampiIso286 = { 3, 6, 10, 18, 30, 50, 80, 120, 180, 250, 315, 400, 500 };
        private static readonly Dictionary<int, int[]> GradiIt = new Dictionary<int, int[]>
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
        private static int ItMicron(int grado, double diametroMm)
        {
            int[] riga;
            if (!GradiIt.TryGetValue(grado, out riga) || diametroMm <= 0) return -1;
            for (int k = 0; k < CampiIso286.Length; k++)
                if (diametroMm <= CampiIso286[k] + 1e-9) return riga[k];
            return -1;
        }

        private Esito TolleranzaQuota(IModelDoc2 modello, Dictionary<string, object> a)
        {
            string nomeQuota = Str(a, "quota", "");
            if (string.IsNullOrWhiteSpace(nomeQuota))
                return Fallito("Non hai detto quale quota. I nomi stanno nello stato del documento, dentro ogni " +
                               "lavorazione (per esempio «D1@Schizzo2»).");
            string tipo = (Str(a, "tipo", "") ?? "").Trim().ToLowerInvariant();
            var quota = TrovaQuota(modello, nomeQuota, out string dove);
            if (quota == null)
                return Fallito("Non trovo la quota «" + nomeQuota + "» in questo documento." +
                               (dove == null ? "" : " Ci sono: " + dove + "."));
            // SOLO QUOTE LINEARI: su una quota angolare SystemValue e i limiti sono
            // in RADIANTI, e «0,5 mm» diventerebbe ±0,0005 rad riletto uguale
            // (revisione del 26/9/2026).
            int tipoQuota = -1;
            try { tipoQuota = quota.GetType(); } catch { }
            if (tipoQuota == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular)
                return Fallito("«" + nomeQuota + "» e' una quota ANGOLARE: le tolleranze che so mettere e riscontrare " +
                               "sono in millimetri. Scegli una quota lineare.");
            if (tipoQuota != (int)swDimensionParamType_e.swDimensionParamTypeDoubleLinear)
                return Fallito("Non riesco a dire se «" + nomeQuota + "» e' una quota lineare (tipo " + tipoQuota +
                               "): una tolleranza in mm su una quota di altro tipo sarebbe un numero sbagliato. Scegli " +
                               "una quota lineare.");
            double nominale = Unita.Mm(ApiHelpers.ReadDimension(quota));
            var tol = quota.Tolerance;
            if (tol == null) return Fallito("La quota «" + nomeQuota + "» non espone la tolleranza.");

            // Com'era, per rimetterla se il riscontro non torna.
            int tipoPrima = tol.Type;
            double minPrima = tol.GetMinValue(), maxPrima = tol.GetMaxValue();
            string foroPrima = SicuroFit(() => tol.GetHoleFitValue()), alberoPrima = SicuroFit(() => tol.GetShaftFitValue());
            const int tutte = (int)swSetValueInConfiguration_e.swSetValue_InAllConfigurations;

            string classe = null; int grado = -1; bool eForo = false;
            int decimali;
            switch (tipo)
            {
                case "simmetrica":
                {
                    double v;
                    if (!NumObbligatorio(a, "valore_mm", out v, out string m)) return Fallito(m);
                    if (v <= 0) return Fallito("«valore_mm» di una tolleranza simmetrica e' maggiore di zero (±" + v + ").");
                    tol.Type = (int)swTolType_e.swTolSYMMETRIC;
                    tol.SetValues2(-Unita.M(v), Unita.M(v), tutte, null);
                    decimali = Math.Max(Decimali(Num(a, "valore_mm", 0)), 1);
                    break;
                }
                case "bilaterale":
                {
                    double su, giu;
                    if (!NumObbligatorio(a, "superiore_mm", out su, out string m1)) return Fallito(m1);
                    if (!NumObbligatorio(a, "inferiore_mm", out giu, out string m2)) return Fallito(m2);
                    if (su <= giu)
                        return Fallito("Lo scostamento superiore (" + su + ") deve essere maggiore dell'inferiore (" + giu +
                                       "): con «superiore_mm» 0,1 e «inferiore_mm» -0,05 la quota sta fra -0,05 e +0,1.");
                    tol.Type = (int)swTolType_e.swTolBILAT;
                    tol.SetValues2(Unita.M(giu), Unita.M(su), tutte, null);
                    decimali = Math.Max(Math.Max(Decimali(su), Decimali(giu)), 1);
                    break;
                }
                case "accoppiamento":
                {
                    classe = (Str(a, "classe", "") ?? "").Trim();
                    var mc = Regex.Match(classe, @"^([A-Za-z]{1,2})(\d{1,2})$");
                    if (!mc.Success)
                        return Fallito("«classe» e' una classe ISO come «H7» (foro, maiuscola) o «g6» (albero, " +
                                       "minuscola), non «" + classe + "».");
                    eForo = char.IsUpper(mc.Groups[1].Value[0]);
                    grado = int.Parse(mc.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (!GradiIt.ContainsKey(grado))
                        return Fallito("Il grado IT" + grado + " non lo so riscontrare (so da IT5 a IT11): senza riscontro " +
                                       "non te lo metto. Scegli un grado fra 5 e 11.");
                    if (ItMicron(grado, nominale) < 0)
                        return Fallito("La quota misura " + nominale.ToString("0.###", CultureInfo.InvariantCulture) +
                                       " mm: la tabella ISO 286 che so riscontrare va fino a 500 mm.");
                    tol.Type = (int)swTolType_e.swTolFITWITHTOL;
                    if (eForo) tol.SetFitValues(classe, ""); else tol.SetFitValues("", classe);
                    decimali = 3;
                    break;
                }
                case "nessuna":
                    tol.Type = (int)swTolType_e.swTolNONE;
                    decimali = -1;
                    break;
                default:
                    return Fallito("«tipo» e' uno fra «simmetrica» (±valore_mm), «bilaterale» (superiore_mm / " +
                                   "inferiore_mm), «accoppiamento» (classe, es. H7 o g6) o «nessuna», non «" + tipo + "».");
            }
            try { modello.EditRebuild3(); } catch { }

            int tipoLetto = tol.Type;
            double minLetto = tol.GetMinValue() * 1000, maxLetto = tol.GetMaxValue() * 1000;
            string foroLetto = SicuroFit(() => tol.GetHoleFitValue()), alberoLetto = SicuroFit(() => tol.GetShaftFitValue());

            // ── IL RISCONTRO ────────────────────────────────────────────────
            string storto = null;
            if (tipo == "accoppiamento")
            {
                int it = ItMicron(grado, nominale);
                double ampiezza = Math.Round((maxLetto - minLetto) * 1000, 1);
                string letta = eForo ? foroLetto : alberoLetto;
                if (!string.Equals(letta, classe, StringComparison.Ordinal))
                    storto = "la classe riletta e' «" + letta + "» invece di «" + classe + "»";
                else if (Math.Abs(ampiezza - it) > 0.6)
                    storto = "l'ampiezza e' " + ampiezza.ToString("0.#", CultureInfo.InvariantCulture) + " µm e per IT" +
                             grado + " su " + nominale.ToString("0.###", CultureInfo.InvariantCulture) + " mm la ISO 286 dice " + it;
                else if (classe == "H" + grado && Math.Abs(minLetto) > 1e-6)
                    storto = "un foro H parte da zero, e lo scostamento inferiore letto e' " + minLetto.ToString("0.####", CultureInfo.InvariantCulture);
                else if (classe == "h" + grado && Math.Abs(maxLetto) > 1e-6)
                    storto = "un albero h arriva a zero, e lo scostamento superiore letto e' " + maxLetto.ToString("0.####", CultureInfo.InvariantCulture);
            }
            else if (tipo == "simmetrica")
            {
                double v = Num(a, "valore_mm", 0);
                if (tipoLetto != (int)swTolType_e.swTolSYMMETRIC || Math.Abs(maxLetto - v) > 1e-6 || Math.Abs(minLetto + v) > 1e-6)
                    storto = "riletta tipo " + tipoLetto + ", " + minLetto + " / " + maxLetto;
            }
            else if (tipo == "bilaterale")
            {
                double su = Num(a, "superiore_mm", 0), giu = Num(a, "inferiore_mm", 0);
                if (tipoLetto != (int)swTolType_e.swTolBILAT || Math.Abs(maxLetto - su) > 1e-6 || Math.Abs(minLetto - giu) > 1e-6)
                    storto = "riletta tipo " + tipoLetto + ", " + minLetto + " / " + maxLetto;
            }
            else if (tipoLetto != (int)swTolType_e.swTolNONE)
                storto = "il tipo riletto e' " + tipoLetto + " invece di nessuna";

            if (storto != null)
            {
                try
                {
                    tol.Type = tipoPrima;
                    tol.SetValues2(minPrima, maxPrima, tutte, null);
                    if (!string.IsNullOrEmpty(foroPrima) || !string.IsNullOrEmpty(alberoPrima)) tol.SetFitValues(foroPrima ?? "", alberoPrima ?? "");
                    modello.EditRebuild3();
                }
                catch { }
                return Fallito("La tolleranza su «" + nomeQuota + "» non torna: " + storto + ". L'ho rimessa com'era.");
            }

            // ── I DECIMALI DELLA TOLLERANZA, sulla quota come si vede ────────
            int decimaliLetti = -1, quoteToccate = 0;
            if (decimali >= 0)
                foreach (var dd in QuoteMostrate(modello, quota))
                {
                    try
                    {
                        dd.SetPrecision3((int)swDimensionPrecisionSettings_e.swDoNotChangePrecisionSetting,
                                         (int)swDimensionPrecisionSettings_e.swDoNotChangePrecisionSetting,
                                         decimali,
                                         (int)swDimensionPrecisionSettings_e.swDoNotChangePrecisionSetting);
                        decimaliLetti = dd.GetPrimaryTolPrecision2();
                        quoteToccate++;
                    }
                    catch { }
                }
            try { modello.EditRebuild3(); } catch { }

            var misure = new Dictionary<string, object>
            {
                ["quota"] = nomeQuota,
                ["nominale_mm"] = Math.Round(nominale, 4),
                ["tipo"] = tipo,
                ["tipo_riletto"] = tipoLetto,
                ["scostamento_inferiore_mm"] = Math.Round(minLetto, 4),
                ["scostamento_superiore_mm"] = Math.Round(maxLetto, 4),
                ["limite_inferiore_mm"] = Math.Round(nominale + minLetto, 4),
                ["limite_superiore_mm"] = Math.Round(nominale + maxLetto, 4),
                ["decimali_tolleranza"] = decimali >= 0 ? (object)decimaliLetti : null,
                ["quote_mostrate_toccate"] = quoteToccate,
            };
            if (tipo == "accoppiamento")
            {
                misure["classe"] = classe;
                misure["it_iso286_um"] = ItMicron(grado, nominale);
                misure["riscontro_iso286"] = true;
            }
            // DECIMALI NON IMPOSTATI = «+0 / 0» IN TAVOLA: e' un rifiuto, non un
            // avviso (revisione del 26/9/2026). Si rimette la tolleranza di prima.
            if (decimali >= 0 && (quoteToccate == 0 || decimaliLetti != decimali))
            {
                try
                {
                    tol.Type = tipoPrima;
                    tol.SetValues2(minPrima, maxPrima, tutte, null);
                    if (!string.IsNullOrEmpty(foroPrima) || !string.IsNullOrEmpty(alberoPrima)) tol.SetFitValues(foroPrima ?? "", alberoPrima ?? "");
                    modello.EditRebuild3();
                }
                catch { }
                return Fallito(quoteToccate == 0
                    ? "La quota «" + nomeQuota + "» non e' mostrata nel pezzo, e i decimali della tolleranza si impostano " +
                      "sulla quota mostrata: senza, in tavola i limiti uscirebbero «+0 / 0». L'ho rimessa com'era; " +
                      "mostra la quota (o usa quella della lavorazione) e riprova."
                    : "I decimali della tolleranza non si sono lasciati impostare (letti " + decimaliLetti + ", voluti " +
                      decimali + "): in tavola i limiti uscirebbero arrotondati, «+0 / 0». L'ho rimessa com'era.");
            }
            return SenzaFeature(misure);
        }

        private static string SicuroFit(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }

        private static int Decimali(double v)
        {
            var t = Math.Abs(v).ToString("0.######", CultureInfo.InvariantCulture);
            int p = t.IndexOf('.');
            return p < 0 ? 0 : t.Length - p - 1;
        }

        /// <summary>Le quote mostrate nel pezzo che appartengono a questa quota.</summary>
        private static List<IDisplayDimension> QuoteMostrate(IModelDoc2 modello, IDimension quota)
        {
            var fuori = new List<IDisplayDimension>();
            string nome = null;
            try { nome = quota.FullName; } catch { }
            if (string.IsNullOrEmpty(nome)) return fuori;
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    var dd = f.GetFirstDisplayDimension() as IDisplayDimension;
                    while (dd != null)
                    {
                        try
                        {
                            var d = dd.GetDimension2(0) as IDimension;
                            if (d != null && d.FullName == nome) fuori.Add(dd);
                        }
                        catch { }
                        dd = f.GetNextDisplayDimension(dd) as IDisplayDimension;
                    }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return fuori;
        }
    }
}
