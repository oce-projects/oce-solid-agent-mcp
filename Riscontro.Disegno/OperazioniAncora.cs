using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// L'ANCORA: un foro o una tasca su una faccia che seguono il bordo quando
    /// il pezzo cambia (voce E18, pezzi a e b).
    ///
    /// ── IL DIFETTO ────────────────────────────────────────────────────────
    ///
    /// `x_mm`/`y_mm` sono coordinate ASSOLUTE del modello. Quattro fori «a 20
    /// mm dagli angoli» di una piastra che si allarga restano dov'erano, e una
    /// tasca «al centro» resta a x = 0 quando la piastra cresce da un lato solo
    /// (cresce da UNA parte anche se lo schizzo e' centrato: voce E2).
    /// Ricostruzione pulita, nessun errore, pezzo sbagliato: tre dei quattro
    /// silenzi di `--regressione` (K5).
    ///
    /// ── LA FORMA: UN'ANCORA PER ASSE, E TUTTI E DUE GLI ASSI ─────────────
    ///
    ///   "ancora": { "x": {"spigolo_uid": "E7", "distanza_mm": 20},
    ///               "y": {"centrata_fra": ["E3", "E5"]} }
    ///
    /// Per ogni asse una di due cose: una QUOTA dal centro del profilo a uno
    /// spigolo, oppure il centro A META' fra due spigoli opposti. Tutti e due
    /// gli assi, sempre: un asse senza ancora lascerebbe il punto libero, e il
    /// risolutore di SOLIDWORKS lo sposterebbe dove gli pare alla prima
    /// modifica (rischio 8 del critico avversariale, 25/9/2026).
    ///
    /// ── PERCHE' «CENTRATA» E' PER ASSE, E NON PER PUNTO ──────────────────
    ///
    /// Misurato il 25/9/2026, piastra 200 x 120 allungata e poi alzata:
    ///
    ///   mezzeria fra i PUNTI MEDI dei due fianchi -> il foro a y = -20 salta
    ///                                               a y = 0 GIA' ALLA CREAZIONE,
    ///                                               e ignora la quota in y
    ///   mezzeria con gli estremi sulla RETTA dei  -> (0,-20) -> (30,-20) ->
    ///   fianchi, perpendicolare, centro a meta'      (30,-20): centrato in x,
    ///   + quota di 40 dal fondo per la y             a 40 dal fondo in y; esatto,
    ///                                               zero errori, schizzo
    ///                                               completamente definito
    ///
    /// La prima centrava in DUE dimensioni e dipendeva dalla lunghezza dei
    /// fianchi (uno smusso su un angolo l'avrebbe spostata); la seconda centra
    /// in una sola, e l'altra la decide l'altro asse.
    ///
    /// ── LE REGOLE CHE LA MISURA E IL CRITICO HANNO IMPOSTO ───────────────
    ///
    ///   1. si disegna con `AddToDB` a vero, ma SOLO attorno a ogni creazione:
    ///      le relazioni che SOLIDWORKS indovina sovravincolano lo schizzo alla
    ///      prima modifica (codice 51), e un `AddToDB` lasciato acceso toglierebbe
    ///      le inferenze a chi disegna dopo;
    ///   2. il centro della tasca si COSTRUISCE (diagonale + punto a meta'):
    ///      quello di `CreateCenterRectangle` non e' fra i punti dello schizzo;
    ///   3. lo schizzo, prima di uscire, dev'essere COMPLETAMENTE DEFINITO
    ///      (`GetConstrainedStatus` = 3): uno schizzo sottovincolato va dove
    ///      vuole il risolutore, uno sovravincolato si rompe alla modifica;
    ///   4. dopo il taglio, una PROVA DI MOVIMENTO: ogni quota d'ancora +0,1 mm,
    ///      ricostruzione, nessuna lavorazione nuova in errore, valore rimesso.
    ///      Il conteggio degli errori subito dopo il taglio non basta: la
    ///      seconda stesura della sonda si e' rotta solo ALLA MODIFICA;
    ///   5. se qualcosa non regge dopo il taglio, il taglio si toglie.
    ///
    /// ── `x_mm`/`y_mm` RESTANO OBBLIGATORI ─────────────────────────────────
    ///
    /// Dicono DOVE il punto sta adesso; l'ancora dice COSA lo tiene li'. Devono
    /// dire lo stesso punto, e se non lo dicono si rifiuta: una quota da 20 su
    /// un punto che sta a 25 sposterebbe il profilo di 5 mm senza errore.
    /// </summary>
    public partial class OperazioniSw
    {
        private sealed class SpigoloDiAncora
        {
            public string Uid;
            public IEdge Spigolo;
            public double[] InizioMm, FineMm;   // nel sistema del MODELLO
        }

        private sealed class AncoraAsse
        {
            public string Nome;                 // «x» o «y»
            public int Indice;                  // 0 = x, 1 = y (lungo assi[Indice])
            public SpigoloDiAncora Spigolo;     // quota da uno spigolo
            public double DistanzaMm;
            public SpigoloDiAncora Fra1, Fra2;  // oppure centrata fra due
            public bool Centrata { get { return Fra1 != null; } }
            public IDimension QuotaNata;        // per la prova di movimento
        }

        private sealed class Ancoraggio
        {
            public AncoraAsse X, Y;
            public bool Vuoto { get { return X == null && Y == null; } }
            public IEnumerable<AncoraAsse> Assi() { if (X != null) yield return X; if (Y != null) yield return Y; }
        }

        /// <summary>
        /// Legge `ancora` e la confronta con la faccia, con `x_mm`/`y_mm` e con
        /// l'ingombro del profilo (`mezzoU`, `mezzoV`: meta' della larghezza del
        /// profilo lungo x e lungo y — il raggio per un foro). Torna un
        /// ancoraggio VUOTO se l'argomento non c'e' (il vecchio modo, coordinate
        /// assolute, resta com'e'); null con il motivo se l'ancora e' sbagliata.
        /// </summary>
        private Ancoraggio LeggiAncora(
            Dictionary<string, object> a, StatoDocumento stato, IFace2 faccia,
            double[] assi, double offsetM, double x, double y, double mezzoU, double mezzoV,
            out string motivo)
        {
            motivo = null;
            object grezza;
            if (a == null || !a.TryGetValue("ancora", out grezza) || grezza == null) return new Ancoraggio();

            var d = grezza as Dictionary<string, object>;
            if (d == null)
            {
                motivo = "«ancora» dev'essere un oggetto con «x» e «y»: per ognuno " +
                         "{\"spigolo_uid\": \"E7\", \"distanza_mm\": 20} oppure {\"centrata_fra\": [\"E3\", \"E5\"]}.";
                return null;
            }
            foreach (var chiave in d.Keys)
                if (chiave != "x" && chiave != "y")
                {
                    motivo = "«ancora» non conosce «" + chiave + "»: le chiavi sono «x» e «y», una per asse.";
                    return null;
                }
            if (!d.ContainsKey("x") || !d.ContainsKey("y"))
            {
                motivo = "L'ancora vuole TUTTI E DUE gli assi, «x» e «y». Un asse senza ancora resterebbe " +
                         "libero, e alla prima modifica del pezzo il profilo andrebbe dove lo mette il " +
                         "risolutore, senza errore. Se su un asse vuoi la posizione di adesso, ancoralo allo " +
                         "spigolo da cui la misureresti.";
                return null;
            }

            double[] normale = faccia?.Normal as double[];
            var anc = new Ancoraggio();
            foreach (var nome in new[] { "x", "y" })
            {
                int k = nome == "x" ? 0 : 1;
                double qui = k == 0 ? x : y;
                double mezzo = k == 0 ? mezzoU : mezzoV;
                var voce = d[nome] as Dictionary<string, object>;
                if (voce == null)
                {
                    motivo = "«ancora." + nome + "» dev'essere {\"spigolo_uid\": ..., \"distanza_mm\": ...} " +
                             "oppure {\"centrata_fra\": [..., ...]}.";
                    return null;
                }
                var asse = new AncoraAsse { Nome = nome, Indice = k };

                if (voce.ContainsKey("centrata_fra"))
                {
                    var uid = new List<string>();
                    var elenco = voce["centrata_fra"] as IEnumerable;
                    if (elenco != null && !(voce["centrata_fra"] is string))
                        foreach (var o in elenco) uid.Add(Convert.ToString(o, CultureInfo.InvariantCulture));
                    if (uid.Count != 2 || uid[0] == uid[1])
                    {
                        motivo = "«ancora." + nome + ".centrata_fra» vuole DUE spigoli diversi, opposti, presi dallo stato.";
                        return null;
                    }
                    asse.Fra1 = SpigoloPerAncora(a, stato, uid[0], offsetM, assi, normale, out motivo);
                    if (asse.Fra1 == null) return null;
                    asse.Fra2 = SpigoloPerAncora(a, stato, uid[1], offsetM, assi, normale, out motivo);
                    if (asse.Fra2 == null) return null;
                    // PER CENTRARE LUNGO x i due spigoli corrono lungo y.
                    if (AsseDelloSpigolo(asse.Fra1, assi) != 1 - k || AsseDelloSpigolo(asse.Fra2, assi) != 1 - k)
                    {
                        motivo = "Per centrare lungo «" + nome + "» i due spigoli devono correre lungo «" +
                                 (k == 0 ? "y" : "x") + "» della faccia: «" + uid[0] + "» e «" + uid[1] + "» non lo fanno.";
                        return null;
                    }
                    double c1 = Coord(asse.Fra1.InizioMm, assi, k), c2 = Coord(asse.Fra2.InizioMm, assi, k);
                    if ((c1 - qui) * (c2 - qui) >= 0)
                    {
                        motivo = "Il punto non sta FRA «" + uid[0] + "» e «" + uid[1] + "» lungo «" + nome +
                                 "»: sono dalla stessa parte, e fra loro non c'e' niente in mezzo a cui centrare.";
                        return null;
                    }
                    double centro = (c1 + c2) / 2;
                    if (Math.Abs(centro - qui) > TolleranzaPuntoMm)
                    {
                        motivo = string.Format(CultureInfo.InvariantCulture,
                            "Il punto che hai dato ha {0} = {1:0.##}, ma a meta' fra «{2}» e «{3}» c'e' {0} = {4:0.##}. " +
                            "x_mm/y_mm dicono dove il punto sta ADESSO, l'ancora cosa lo tiene li': devono dire lo " +
                            "stesso punto. Rifiuto invece di spostarlo in silenzio.",
                            nome, qui, uid[0], uid[1], centro);
                        return null;
                    }
                    if (Math.Abs(centro - c1) < mezzo)
                    {
                        motivo = string.Format(CultureInfo.InvariantCulture,
                            "Fra «{0}» e «{1}» ci sono {2:0.##} mm lungo «{3}», e il profilo ne e' largo {4:0.##}: " +
                            "non ci sta.", uid[0], uid[1], Math.Abs(c2 - c1), nome, 2 * mezzo);
                        return null;
                    }
                }
                else
                {
                    string uid = voce.ContainsKey("spigolo_uid")
                        ? Convert.ToString(voce["spigolo_uid"], CultureInfo.InvariantCulture) : "";
                    double distanza;
                    if (uid.Length == 0 || !voce.ContainsKey("distanza_mm") ||
                        !double.TryParse(Convert.ToString(voce["distanza_mm"], CultureInfo.InvariantCulture),
                                         NumberStyles.Any, CultureInfo.InvariantCulture, out distanza))
                    {
                        motivo = "«ancora." + nome + "» vuole {\"spigolo_uid\": \"E7\", \"distanza_mm\": 20} " +
                                 "oppure {\"centrata_fra\": [\"E3\", \"E5\"]}.";
                        return null;
                    }
                    // LA DISTANZA E' DAL CENTRO del foro o della tasca, e non
                    // puo' essere meno di meta' della sua larghezza: il profilo
                    // scavalcherebbe lo spigolo (rischio 5 del critico). Uguale
                    // e' ammesso: e' una battuta.
                    if (distanza + 1e-9 < mezzo)
                    {
                        motivo = string.Format(CultureInfo.InvariantCulture,
                            "«distanza_mm» e' dal CENTRO del profilo allo spigolo, e {0:0.##} e' meno di meta' della " +
                            "sua larghezza lungo «{1}» ({2:0.##}): il profilo scavalcherebbe lo spigolo «{3}».",
                            distanza, nome, mezzo, uid);
                        return null;
                    }
                    var sp = SpigoloPerAncora(a, stato, uid, offsetM, assi, normale, out motivo);
                    if (sp == null) return null;
                    if (AsseDelloSpigolo(sp, assi) != 1 - k)
                    {
                        motivo = "Per ancorare «" + nome + "» serve uno spigolo che corre lungo «" + (k == 0 ? "y" : "x") +
                                 "» della faccia (la distanza si misura lungo «" + nome + "»): «" + uid + "» non lo fa.";
                        return null;
                    }
                    double vera = Math.Abs(qui - Coord(sp.InizioMm, assi, k));
                    if (Math.Abs(vera - distanza) > TolleranzaPuntoMm)
                    {
                        motivo = string.Format(CultureInfo.InvariantCulture,
                            "Il punto che hai dato sta a {0:0.##} mm da «{1}», non a {2:0.##}. x_mm/y_mm dicono dove il " +
                            "punto sta ADESSO, l'ancora cosa lo tiene li': devono dire lo stesso punto. Rifiuto invece " +
                            "di spostare il profilo di {3:0.##} mm in silenzio.",
                            vera, uid, distanza, Math.Abs(vera - distanza));
                        return null;
                    }
                    asse.Spigolo = sp;
                    asse.DistanzaMm = distanza;
                }
                if (k == 0) anc.X = asse; else anc.Y = asse;
            }
            return anc;
        }

        /// <summary>
        /// Lo spigolo per uid: con la firma, dritto, nel piano della faccia, e
        /// non sul bordo di uno smusso o di un raccordo.
        /// </summary>
        private SpigoloDiAncora SpigoloPerAncora(
            Dictionary<string, object> a, StatoDocumento stato, string uid,
            double offsetM, double[] assi, double[] normaleFaccia, out string motivo)
        {
            motivo = null;
            StatoDocumento.FirmaEntita firma;
            if (stato == null || !stato.Firme.TryGetValue(uid, out firma) || firma.Tipo != "EDGE")
            {
                motivo = "Lo spigolo «" + uid + "» non c'e' in questo documento: gli spigoli dell'ancora si " +
                         "prendono dallo stato (E1, E2...).";
                return null;
            }
            // LA FIRMA E' OBBLIGATORIA QUANDO IL PIANO NE PORTA (rischio 2 del
            // critico): `FirmaCombacia` senza firma risponde si', e fra spigoli
            // uguali l'ordine dello stato non e' garantito — un foro a meta'
            // altezza passerebbe la coerenza anche legato al lato sbagliato. Un
            // piano che porta `_firme` ma non quella di questo spigolo e' un piano
            // che non sa quale spigolo intendeva.
            if (a != null && a.ContainsKey("_firme") && FirmaAttesa(a, uid) == null)
            {
                motivo = "Il piano porta le firme delle entita' ma non quella dello spigolo «" + uid + "»: " +
                         "senza, non so se «" + uid + "» e' ancora lo spigolo che avevi visto.";
                return null;
            }
            if (!FirmaCombacia(a, uid, firma, out motivo)) return null;
            var spigolo = firma.Entita as IEdge;
            ICurve curva = null;
            ICurveParamData par = null;
            try { curva = spigolo?.GetCurve() as ICurve; par = spigolo?.GetCurveParams3() as ICurveParamData; } catch { }
            if (curva == null || par == null || !curva.IsLine())
            {
                motivo = "Lo spigolo «" + uid + "» non e' dritto: l'ancora si prende da uno spigolo dritto.";
                return null;
            }
            var p0 = par.StartPoint as double[];
            var p1 = par.EndPoint as double[];
            if (p0 == null || p1 == null || p0.Length < 3 || p1.Length < 3)
            {
                motivo = "Non riesco a leggere gli estremi dello spigolo «" + uid + "».";
                return null;
            }
            int n = (int)assi[2];
            if (Math.Abs(p0[n] - offsetM) > 1e-6 || Math.Abs(p1[n] - offsetM) > 1e-6)
            {
                motivo = string.Format(CultureInfo.InvariantCulture,
                    "Lo spigolo «{0}» non sta nel piano della faccia su cui lavori (e' a {1:0.##} mm da quel piano): " +
                    "l'ancora si prende da uno spigolo della faccia stessa.",
                    uid, Math.Max(Math.Abs(p0[n] - offsetM), Math.Abs(p1[n] - offsetM)) * 1000.0);
                return null;
            }
            // IL BORDO DI UNO SMUSSO O DI UN RACCORDO (rischio 9 del critico):
            // lo spigolo fra la faccia e uno smusso sta nel piano, e' dritto, e'
            // parallelo — e se lo smusso passa da 2 a 5 il foro si sposta di 3
            // senza che nessuno lo sappia. L'altra faccia dello spigolo dev'essere
            // piana e perpendicolare a quella su cui si lavora.
            try
            {
                var facce = spigolo.GetTwoAdjacentFaces2() as object[];
                if (facce != null && normaleFaccia != null && normaleFaccia.Length >= 3)
                    foreach (var o in facce)
                    {
                        var f = o as IFace2;
                        if (f == null) continue;
                        var nf = f.Normal as double[];
                        double coseno = nf == null || nf.Length < 3 ? 1
                            : Math.Abs(nf[0] * normaleFaccia[0] + nf[1] * normaleFaccia[1] + nf[2] * normaleFaccia[2]);
                        if (nf != null && coseno > 0.999) continue;          // la faccia stessa, o complanare
                        if (nf == null || coseno > 0.01)
                        {
                            motivo = "Lo spigolo «" + uid + "» sta sul bordo di uno smusso o di un raccordo (la sua " +
                                     "altra faccia non e' perpendicolare a questa): se lo smusso cambia, il profilo si " +
                                     "sposta con lui. Ancoralo a uno spigolo vivo del pezzo.";
                            return null;
                        }
                    }
            }
            catch { }
            return new SpigoloDiAncora
            {
                Uid = uid,
                Spigolo = spigolo,
                InizioMm = new[] { p0[0] * 1000.0, p0[1] * 1000.0, p0[2] * 1000.0 },
                FineMm = new[] { p1[0] * 1000.0, p1[1] * 1000.0, p1[2] * 1000.0 },
            };
        }

        /// <summary>Lungo quale asse della faccia corre lo spigolo: 0 = x, 1 = y, -1 = obliquo.</summary>
        private static int AsseDelloSpigolo(SpigoloDiAncora s, double[] assi)
        {
            double du = Math.Abs(Coord(s.FineMm, assi, 0) - Coord(s.InizioMm, assi, 0));
            double dv = Math.Abs(Coord(s.FineMm, assi, 1) - Coord(s.InizioMm, assi, 1));
            if (du > 0.01 && dv < 0.001) return 0;
            if (dv > 0.01 && du < 0.001) return 1;
            return -1;
        }

        private static double Coord(double[] puntoMm, double[] assi, int quale)
        {
            return puntoMm[(int)assi[quale]];
        }

        /// <summary>
        /// Mette nello schizzo APERTO cio' che tiene il punto dove l'ancora dice,
        /// asse per asse, rilegge tutto, e pretende lo schizzo COMPLETAMENTE
        /// DEFINITO. Torna false con il motivo se qualcosa non torna.
        /// </summary>
        private bool ApplicaAncora(
            IModelDoc2 modello, Ancoraggio anc, ISketchPoint punto,
            double[] assi, double offsetM, double x, double y,
            Dictionary<string, object> misure, out string motivo)
        {
            motivo = null;
            if (anc == null || anc.Vuoto) return true;
            if (punto == null)
            {
                motivo = "Non ho il punto di riferimento del profilo a cui attaccare l'ancora.";
                return false;
            }
            var detto = new Dictionary<string, object>();
            var schizzi = modello.SketchManager;

            foreach (var asse in anc.Assi())
            {
                int k = asse.Indice;
                string errore;
                if (asse.Centrata)
                {
                    // LA MEZZERIA: lungo l'asse k, all'altezza del punto
                    // sull'altro asse, con gli estremi sulla RETTA dei due
                    // spigoli (coincidenza), perpendicolare a loro, e il punto a
                    // meta'. Misurato il 25/9/2026: centra lungo k e lascia
                    // l'altro asse all'altra ancora.
                    double altro = k == 0 ? y : x;
                    double c1 = Coord(asse.Fra1.InizioMm, assi, k), c2 = Coord(asse.Fra2.InizioMm, assi, k);
                    var m1 = PuntoNelloSchizzo(modello, assi, offsetM, k == 0 ? c1 : altro, k == 0 ? altro : c1, out errore);
                    var m2 = m1 == null ? null
                        : PuntoNelloSchizzo(modello, assi, offsetM, k == 0 ? c2 : altro, k == 0 ? altro : c2, out errore);
                    if (m1 == null || m2 == null) { motivo = errore; return false; }

                    int relPrima = RelazioniDelloSchizzo(schizzi);
                    ISketchSegment mezzeria;
                    bool addPrima = schizzi.AddToDB;
                    try
                    {
                        schizzi.AddToDB = true;
                        mezzeria = schizzi.CreateCenterLine(m1[0], m1[1], m1[2], m2[0], m2[1], m2[2]);
                    }
                    finally { schizzi.AddToDB = addPrima; }
                    var linea = mezzeria as ISketchLine;
                    if (linea == null) { motivo = "La mezzeria fra «" + asse.Fra1.Uid + "» e «" + asse.Fra2.Uid + "» non e' nata."; return false; }

                    bool r1 = Relazione(modello, (ISketchPoint)linea.GetStartPoint2(), asse.Fra1.Spigolo, "sgCOINCIDENT");
                    bool r2 = Relazione(modello, (ISketchPoint)linea.GetEndPoint2(), asse.Fra2.Spigolo, "sgCOINCIDENT");
                    bool r3 = Relazione(modello, mezzeria, asse.Fra1.Spigolo, "sgPERPENDICULAR");
                    bool r4 = Relazione(modello, punto, mezzeria, "sgATMIDDLE");
                    int relDopo = RelazioniDelloSchizzo(schizzi);
                    // QUATTRO RELAZIONI, CONTATE: `SketchAddConstraints` e' Void,
                    // e una relazione che non nasce non lo dice nessuno.
                    if (!r1 || !r2 || !r3 || !r4 || (relPrima >= 0 && relDopo - relPrima < 4))
                    {
                        motivo = "Non sono riuscito a centrare lungo «" + asse.Nome + "» fra «" + asse.Fra1.Uid + "» e «" +
                                 asse.Fra2.Uid + "» (relazioni nate: " +
                                 (relPrima < 0 ? "?" : (relDopo - relPrima).ToString(CultureInfo.InvariantCulture)) + " su 4).";
                        return false;
                    }
                    detto[asse.Nome] = new Dictionary<string, object>
                    {
                        ["centrata_fra"] = new[] { asse.Fra1.Uid, asse.Fra2.Uid },
                    };
                }
                else
                {
                    double suo = Coord(asse.Spigolo.InizioMm, assi, k);
                    // Il numero a meta' strada fra punto e spigolo, un po' di lato:
                    // solo DOVE si scrive, non cosa misura.
                    double tu = k == 0 ? (x + suo) / 2 : x + 6;
                    double tv = k == 0 ? y + 6 : (y + suo) / 2;
                    var testo = PuntoNelloSchizzo(modello, assi, offsetM, tu, tv, out errore);
                    if (testo == null) { motivo = errore; return false; }
                    // LA DIREZIONE SI LEGGE NELLO SCHIZZO, non nel modello: lo
                    // spigolo corre lungo l'altro asse della faccia, e nello
                    // schizzo quell'asse puo' essere la X o la Y (sulla faccia
                    // di destra x_mm e' la Y del modello). Si portano i due
                    // estremi nello schizzo e si guarda dove corre.
                    double altro1 = Coord(asse.Spigolo.InizioMm, assi, 1 - k), altro2 = Coord(asse.Spigolo.FineMm, assi, 1 - k);
                    var e1 = PuntoNelloSchizzo(modello, assi, offsetM, k == 0 ? suo : altro1, k == 0 ? altro1 : suo, out errore);
                    var e2 = e1 == null ? null
                        : PuntoNelloSchizzo(modello, assi, offsetM, k == 0 ? suo : altro2, k == 0 ? altro2 : suo, out errore);
                    if (e2 == null) { motivo = errore; return false; }
                    bool spigoloVerticale = Math.Abs(e2[1] - e1[1]) > Math.Abs(e2[0] - e1[0]);
                    IDimension nata;
                    double riletta = QuotaPuntoSpigolo(modello, punto, asse.Spigolo.Spigolo, testo, spigoloVerticale, out nata);
                    if (riletta < 0)
                    {
                        motivo = "La quota fra il punto e lo spigolo «" + asse.Spigolo.Uid + "» non e' nata.";
                        return false;
                    }
                    if (Math.Abs(riletta - asse.DistanzaMm) > TolleranzaPuntoMm)
                    {
                        motivo = string.Format(CultureInfo.InvariantCulture,
                            "La quota dallo spigolo «{0}» e' nata ma vale {1:0.###} mm invece di {2:0.###}: misura " +
                            "un'altra cosa, e non la tengo.", asse.Spigolo.Uid, riletta, asse.DistanzaMm);
                        return false;
                    }
                    asse.QuotaNata = nata;
                    detto[asse.Nome] = new Dictionary<string, object>
                    {
                        ["spigolo"] = asse.Spigolo.Uid,
                        ["distanza_mm"] = asse.DistanzaMm,
                        ["quota_riletta_mm"] = Math.Round(riletta, 3),
                    };
                }
            }

            // COMPLETAMENTE DEFINITO, O NIENTE. Sottovincolato, il profilo va
            // dove vuole il risolutore; sovravincolato, si rompe alla modifica.
            int stato = -1;
            try { stato = schizzi.ActiveSketch.GetConstrainedStatus(); } catch { }
            if (stato != (int)swConstrainedStatus_e.swFullyConstrained)
            {
                motivo = "Con l'ancora lo schizzo dovrebbe essere completamente definito, e non lo e' (stato " +
                         stato.ToString(CultureInfo.InvariantCulture) + ": " + NomeDelloStato(stato) + "). Non taglio: " +
                         "alla prima modifica del pezzo il profilo andrebbe dove lo mette il risolutore.";
                return false;
            }
            // E NESSUNA RELAZIONE MALATA (rischio 1 del critico): penzolante,
            // sovravincolante o rotta. Lo stato «definito» non le esclude tutte.
            string malate = RelazioniMalate(schizzi);
            if (malate != null)
            {
                motivo = "Lo schizzo dell'ancora ha relazioni " + malate + ": non taglio, si romperebbe alla prima modifica.";
                return false;
            }
            detto["schizzo"] = "completamente definito";
            // LA TAVOLA (rischio 15): un asse centrato lo tiene una relazione,
            // non una quota, e `quote_dal_modello` non ha niente da riportare.
            if ((anc.X != null && anc.X.Centrata) || (anc.Y != null && anc.Y.Centrata))
                detto["nota_tavola"] = "Lungo l'asse centrato la posizione la tiene una relazione, non una quota: " +
                                       "quote_dal_modello non la riporta, quota_in_tavola la misura dai bordi.";
            misure["ancora"] = detto;
            return true;
        }

        private static string NomeDelloStato(int stato)
        {
            switch (stato)
            {
                case (int)swConstrainedStatus_e.swUnderConstrained: return "sottovincolato";
                case (int)swConstrainedStatus_e.swOverConstrained: return "sovravincolato";
                case (int)swConstrainedStatus_e.swNoSolution: return "senza soluzione";
                case (int)swConstrainedStatus_e.swInvalidSolution: return "soluzione non valida";
                default: return "non so";
            }
        }

        /// <summary>
        /// LA PROVA DI MOVIMENTO (rischio 1 del critico): ogni quota d'ancora +0,1
        /// mm, ricostruzione, e la lavorazione nuova e il suo schizzo non devono
        /// andare in errore; poi il valore si rimette e si rilegge. Torna null se
        /// regge, il motivo se no.
        /// </summary>
        private string ProvaDiMovimento(IModelDoc2 modello, Ancoraggio anc, IFeature taglio)
        {
            if (anc == null || anc.Vuoto) return null;
            foreach (var asse in anc.Assi())
            {
                var q = asse.QuotaNata;
                if (q == null) continue;
                double era;
                try { era = q.GetSystemValue2(""); } catch { continue; }
                string rotta = null;
                try
                {
                    q.SetSystemValue3(era + 0.0001, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
                    modello.EditRebuild3();
                    rotta = LavorazioneInErrore(taglio);
                }
                catch (Exception ex) { rotta = ex.Message; }
                finally
                {
                    try
                    {
                        q.SetSystemValue3(era, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
                        modello.EditRebuild3();
                    }
                    catch { }
                }
                if (rotta != null)
                    return "Spostando la quota d'ancora «" + asse.Nome + "» di un decimo, il taglio va in errore (" + rotta +
                           "): lo schizzo non regge una modifica, e un'ancora che non regge una modifica non serve.";
                double tornata = -1;
                try { tornata = q.GetSystemValue2(""); } catch { }
                if (Math.Abs(tornata - era) > 1e-9)
                    return "Dopo la prova di movimento la quota d'ancora «" + asse.Nome + "» non e' tornata al suo valore.";
            }
            return null;
        }

        /// <summary>La lavorazione o il suo schizzo in errore, detto a parole; null se sani.</summary>
        private static string LavorazioneInErrore(IFeature taglio)
        {
            if (taglio == null) return null;
            try
            {
                bool avviso;
                int codice = taglio.GetErrorCode2(out avviso);
                if (codice != 0 && !avviso) return "codice " + codice.ToString(CultureInfo.InvariantCulture);
                var schizzo = taglio.GetFirstSubFeature() as IFeature;
                if (schizzo != null)
                {
                    int c2 = schizzo.GetErrorCode2(out avviso);
                    if (c2 != 0 && !avviso) return "schizzo, codice " + c2.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Toglie un taglio appena fatto e il suo schizzo, quando l'ancora non
        /// regge (rischio 11 del critico): una lavorazione che non regge una
        /// modifica non si lascia nel pezzo con «ritirala tu». Torna la coda
        /// del messaggio: tolto e pezzo com'era, oppure cosa e' rimasto.
        ///
        /// «Ha detto di averlo tolto» non basta, come in `EliminaLavorazione`:
        /// si ricontano le lavorazioni e si rimisura il volume.
        /// </summary>
        private string TogliIlTaglio(IModelDoc2 modello, IFeature taglio, double? volumePrima)
        {
            bool tolto = false;
            try
            {
                modello.ClearSelection2(true);
                if (taglio.Select2(false, 0))
                    tolto = modello.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch { }
            finally { try { modello.ClearSelection2(true); } catch { } }
            if (!tolto) return RestaNelPezzo;
            try { modello.EditRebuild3(); } catch { }
            int lavorazioni = ContaFeature(modello);
            if (lavorazioni < 0 || _featureAllInizioDelPasso < 0)
                return " Il taglio l'ho tolto, con il suo schizzo, ma non ho potuto ricontare " +
                       "l'albero per controllare che il pezzo sia com'era: guardalo.";
            if (lavorazioni > _featureAllInizioDelPasso) return RestaNelPezzo;
            double? volumeDopo = VolumeMm3(modello);
            if (volumePrima == null || volumeDopo == null)
                return " Il taglio l'ho tolto, con il suo schizzo, ma non ho potuto rimisurare il " +
                       "volume per controllare che il pezzo sia com'era: guardalo.";
            if (Math.Abs(volumeDopo.Value - volumePrima.Value) > 0.01) return RestaNelPezzo;
            return " Il taglio l'ho tolto, con il suo schizzo: il pezzo e' com'era prima di questo passo.";
        }

        /// <summary>
        /// Una quota fra un punto dello schizzo e uno spigolo del MODELLO, col
        /// testo in `testo` (coordinate di schizzo). Torna il valore riletto in
        /// mm, o -1. La finestrella del valore si spegne e si rimette com'era.
        ///
        /// ORIZZONTALE O VERTICALE, e non `Extension.AddDimension`. Misurato il
        /// 25/9/2026 sulle facce davanti, dietro e destra: con un punto dello
        /// schizzo e uno spigolo del modello selezionati, `Extension.
        /// AddDimension` torna null in tutte e quattro le direzioni — la
        /// chiamata che la voce GP6 raccomanda per i segmenti qui non crea
        /// niente. `AddHorizontalDimension2` e `AddVerticalDimension2` danno
        /// 20 e 30 esatti; `AddDimension2` anche, ma e' vietata per contratto
        /// (niente direzione). Si sceglie dalla direzione dello spigolo NELLO
        /// SCHIZZO: spigolo verticale, quota orizzontale.
        /// </summary>
        private double QuotaPuntoSpigolo(
            IModelDoc2 modello, ISketchPoint punto, IEdge spigolo, double[] testo, bool spigoloVerticale,
            out IDimension nata)
        {
            nata = null;
            bool chiedeva = true;
            try
            {
                chiedeva = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            }
            catch { }
            try
            {
                modello.ClearSelection2(true);
                if (!punto.Select4(false, null)) return -1;
                if (!((IEntity)spigolo).Select4(true, null)) return -1;
                var quota = (spigoloVerticale
                    ? modello.AddHorizontalDimension2(testo[0], testo[1], testo[2])
                    : modello.AddVerticalDimension2(testo[0], testo[1], testo[2])) as IDisplayDimension;
                nata = quota?.GetDimension2(0);
                if (nata == null) return -1;
                try { quota.MarkedForDrawing = true; } catch { }
                return nata.GetSystemValue2("") * 1000.0;
            }
            catch { return -1; }
            finally
            {
                try { modello.ClearSelection2(true); } catch { }
                try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, chiedeva); } catch { }
            }
        }

        /// <summary>
        /// LO STADIO DELL'ASOLA, DEFINITO (voce E18). `DisegnaLoStadio` lo
        /// disegna con quattro entita' sotto `AddToDB` e basta: niente relazioni,
        /// niente quote. Qui si aggiunge cio' che lo rende un'asola anche dopo
        /// una modifica, e — se c'e' — l'ancora. Torna quante quote ha messo;
        /// `errore` non nullo vuol dire che lo schizzo non e' come serve (con
        /// l'ancora e' un rifiuto, senza e' solo una quota in meno).
        ///
        /// MISURATO il 25/9/2026 (`prova0089 stadio`, `stadio2`), piastra 200 x
        /// 120, asola 60 x 20 a 0 e a 30 gradi:
        ///
        ///   gli estremi disegnati con AddToDB   gia' fusi: 6 punti (4 raccordi e
        ///                                       2 centri), le coincidenze non
        ///                                       servono
        ///   4 tangenze + raggi uguali           +5 relazioni
        ///   quota sull'arco                     il RAGGIO (10)
        ///   quota fra i due centri              null, con tutte e due le chiamate:
        ///                                       serve una mezzeria di costruzione
        ///                                       legata ai centri, e si quota lei (40)
        ///   orientamento a 0 gradi              orizzontale sulla mezzeria
        ///   orientamento a 30 gradi             Extension.AddDimension null,
        ///                                       AddDimension2 30 (vietata, GP6),
        ///                                       AddSpecificDimension ANGOLARE 30
        ///   raggio 15 e centri a 60 dopo        volume atteso al centesimo, zero
        ///                                       errori: resta un'asola
        ///
        /// Senza ancora l'asola obliqua resta libera di ruotare (nessuno spigolo
        /// da cui misurare l'angolo): si dice nelle misure. Con l'ancora l'angolo
        /// si quota contro il suo primo spigolo.
        /// </summary>
        private int DefinisciLoStadio(
            IModelDoc2 modello, ISketchSegment[] stadio, double[] assi, double offsetM,
            double x, double y, double angoloGradi, double corsa, double raggio,
            Ancoraggio ancora, Dictionary<string, object> misure, out string errore)
        {
            errore = null;
            if (stadio == null || stadio.Length < 4 || stadio.Any(s => s == null))
            {
                errore = "Lo stadio non ha i suoi quattro pezzi: non posso definirlo.";
                return 0;
            }
            ISketchSegment l1 = stadio[0], l2 = stadio[1], a1 = stadio[2], a2 = stadio[3];
            var arcoB = a1 as ISketchArc;   // a1 e' l'estremita' dalla parte +corsa
            var arcoA = a2 as ISketchArc;
            if (arcoA == null || arcoB == null)
            {
                errore = "Le estremita' dell'asola non sono archi: non posso definirla.";
                return 0;
            }
            var schizzi = modello.SketchManager;

            // TANGENZE E RAGGI UGUALI: quattro linee-arco, e i due archi uguali.
            int relPrima = RelazioniDelloSchizzo(schizzi);
            foreach (var coppia in new[] { new[] { l1, a1 }, new[] { l1, a2 }, new[] { l2, a1 }, new[] { l2, a2 } })
                Relazione(modello, coppia[0], coppia[1], "sgTANGENT");
            Relazione(modello, a1, a2, "sgSAMELENGTH");

            // LA MEZZERIA FRA I CENTRI, di costruzione e legata a tutti e due.
            double rad = angoloGradi * Math.PI / 180.0, cos = Math.Cos(rad), sin = Math.Sin(rad);
            string e1;
            var cA = PuntoNelloSchizzo(modello, assi, offsetM, x - corsa * cos, y - corsa * sin, out e1);
            var cB = cA == null ? null : PuntoNelloSchizzo(modello, assi, offsetM, x + corsa * cos, y + corsa * sin, out e1);
            if (cB == null) { errore = e1; return 0; }
            ISketchSegment mezzeria;
            bool addPrima = schizzi.AddToDB;
            try
            {
                schizzi.AddToDB = true;
                mezzeria = schizzi.CreateCenterLine(cA[0], cA[1], cA[2], cB[0], cB[1], cB[2]);
            }
            finally { schizzi.AddToDB = addPrima; }
            var linea = mezzeria as ISketchLine;
            if (linea == null) { errore = "La mezzeria fra i centri dell'asola non e' nata."; return 0; }
            // Disegnata sotto AddToDB esattamente sui centri, la mezzeria vi si
            // FONDE (misurato: le coincidenze non aggiungono relazioni). Si
            // chiedono lo stesso, per il caso in cui un arrotondamento le
            // lasciasse a parte; il conto che vale e' quello delle altre cinque.
            Relazione(modello, (ISketchPoint)linea.GetStartPoint2(), (ISketchPoint)arcoA.GetCenterPoint2(), "sgCOINCIDENT");
            Relazione(modello, (ISketchPoint)linea.GetEndPoint2(), (ISketchPoint)arcoB.GetCenterPoint2(), "sgCOINCIDENT");
            int relDopo = RelazioniDelloSchizzo(schizzi);
            if (relPrima >= 0 && relDopo - relPrima < 5)
                errore = "Le relazioni dello stadio non sono nate tutte (" +
                         (relDopo - relPrima).ToString(CultureInfo.InvariantCulture) + " su 5: quattro tangenze e i raggi uguali).";

            // LE QUOTE: il raggio e la mezzeria (la lunghezza fra i centri).
            string e2;
            var testoR = PuntoNelloSchizzo(modello, assi, offsetM,
                x + (corsa + raggio + 6) * cos, y + (corsa + raggio + 6) * sin + 6, out e2);
            var testoL = PuntoNelloSchizzo(modello, assi, offsetM,
                x - (raggio + 8) * sin, y + (raggio + 8) * cos, out e2);
            var daQuotare = new List<Quota>();
            if (testoR != null) daQuotare.Add(Quota.Su(a1, testoR));
            if (testoL != null) daQuotare.Add(Quota.Su(mezzeria, testoL));
            int quote = QuotaSegmenti(modello, daQuotare);
            if (quote < 2 && errore == null) errore = "Le quote dell'asola non sono nate tutte (" + quote + " su 2).";

            // L'ORIENTAMENTO. Mezzeria orizzontale o verticale NELLO SCHIZZO:
            // una relazione. Obliqua: un angolo contro il primo spigolo
            // dell'ancora, se c'e'; altrimenti resta libera, e lo si dice.
            bool orizzontale = Math.Abs(cB[1] - cA[1]) < 1e-9, verticale = Math.Abs(cB[0] - cA[0]) < 1e-9;
            if (orizzontale || verticale)
                RelazioneSola(modello, mezzeria, orizzontale ? "sgHORIZONTAL2D" : "sgVERTICAL2D");
            else if (!ancora.Vuoto)
            {
                var asse = ancora.X ?? ancora.Y;
                var riferimento = asse.Centrata ? asse.Fra1 : asse.Spigolo;
                string e3;
                var testoA = PuntoNelloSchizzo(modello, assi, offsetM,
                    x + (corsa + raggio + 14) * cos, y + (corsa + raggio + 14) * sin, out e3);
                if (testoA == null || !QuotaAngolo(modello, mezzeria, riferimento.Spigolo, testoA))
                {
                    if (errore == null) errore = "L'angolo dell'asola contro lo spigolo «" + riferimento.Uid + "» non e' nato.";
                }
                else quote++;
            }
            else
                misure["orientamento"] = "libero: un'asola obliqua senza ancora non ha uno spigolo da cui " +
                                         "misurare l'angolo, e dopo una modifica puo' ruotare.";

            // L'ANCORA, sul punto a meta' della mezzeria.
            if (!ancora.Vuoto)
            {
                if (errore != null) return quote;
                string e4;
                var centro = PuntoNelloSchizzo(modello, assi, offsetM, x, y, out e4);
                if (centro == null) { errore = e4; return quote; }
                ISketchPoint punto;
                try
                {
                    schizzi.AddToDB = true;
                    punto = schizzi.CreatePoint(centro[0], centro[1], centro[2]);
                }
                finally { schizzi.AddToDB = addPrima; }
                if (punto == null || !Relazione(modello, punto, mezzeria, "sgATMIDDLE"))
                {
                    errore = "Non sono riuscito a mettere il punto al centro dell'asola.";
                    return quote;
                }
                string e5;
                if (!ApplicaAncora(modello, ancora, punto, assi, offsetM, x, y, misure, out e5))
                    errore = e5;
            }
            return quote;
        }

        /// <summary>
        /// Una quota ANGOLARE fra una linea dello schizzo e uno spigolo del
        /// modello. `AddSpecificDimension` col tipo esplicito: `Extension.
        /// AddDimension` torna null su questa coppia, e `AddDimension2` e'
        /// vietata (GP6). Misurato il 25/9/2026: 30 gradi su 30.
        /// </summary>
        private bool QuotaAngolo(IModelDoc2 modello, ISketchSegment linea, IEdge spigolo, double[] testo)
        {
            bool chiedeva = true;
            try
            {
                chiedeva = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            }
            catch { }
            try
            {
                modello.ClearSelection2(true);
                if (!linea.Select4(false, null) || !((IEntity)spigolo).Select4(true, null)) return false;
                int errore = 0;
                var quota = modello.Extension.AddSpecificDimension(
                    testo[0], testo[1], testo[2], (int)swDimensionType_e.swAngularDimension, ref errore) as IDisplayDimension;
                if (quota == null) return false;
                try { quota.MarkedForDrawing = true; } catch { }
                return true;
            }
            catch { return false; }
            finally
            {
                try { modello.ClearSelection2(true); } catch { }
                try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, chiedeva); } catch { }
            }
        }

        /// <summary>
        /// Una relazione fra due cose dello schizzo o del modello. Si passano
        /// come `object` perche' si selezionano ognuna col SUO `Select4`:
        /// misurato il 25/9/2026, un punto o un segmento di schizzo NON sono
        /// `IEntity` — «as IEntity» torna null in silenzio, e la prima stesura
        /// della mezzeria faceva nascere due relazioni su quattro.
        /// </summary>
        private static bool Relazione(IModelDoc2 modello, object uno, object due, string quale)
        {
            try
            {
                modello.ClearSelection2(true);
                if (!Seleziona(uno, false) || !Seleziona(due, true)) return false;
                modello.SketchAddConstraints(quale);
                return true;
            }
            catch { return false; }
            finally { try { modello.ClearSelection2(true); } catch { } }
        }

        /// <summary>Una relazione su una cosa sola (orizzontale, verticale).</summary>
        private static bool RelazioneSola(IModelDoc2 modello, object cosa, string quale)
        {
            try
            {
                modello.ClearSelection2(true);
                if (!Seleziona(cosa, false)) return false;
                modello.SketchAddConstraints(quale);
                return true;
            }
            catch { return false; }
            finally { try { modello.ClearSelection2(true); } catch { } }
        }

        private static bool Seleziona(object cosa, bool aggiungi)
        {
            var punto = cosa as ISketchPoint;
            if (punto != null) return punto.Select4(aggiungi, null);
            var segmento = cosa as ISketchSegment;
            if (segmento != null) return segmento.Select4(aggiungi, null);
            var entita = cosa as IEntity;
            if (entita != null) return entita.Select4(aggiungi, null);
            return false;
        }

        /// <summary>Le relazioni penzolanti, sovravincolanti o rotte dello schizzo aperto, a parole; null se nessuna.</summary>
        private static string RelazioniMalate(ISketchManager schizzi)
        {
            try
            {
                var gestore = schizzi.ActiveSketch?.RelationManager as ISketchRelationManager;
                if (gestore == null) return null;
                var dette = new List<string>();
                int n = gestore.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling);
                if (n > 0) dette.Add(n + " penzolant" + (n == 1 ? "e" : "i"));
                n = gestore.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining);
                if (n > 0) dette.Add(n + " sovravincolant" + (n == 1 ? "e" : "i"));
                n = gestore.GetRelationsCount((int)swSketchRelationFilterType_e.swBroken);
                if (n > 0) dette.Add(n + " rott" + (n == 1 ? "a" : "e"));
                return dette.Count == 0 ? null : string.Join(", ", dette);
            }
            catch { return null; }
        }

        private static int RelazioniDelloSchizzo(ISketchManager schizzi)
        {
            try
            {
                var gestore = schizzi.ActiveSketch?.RelationManager as ISketchRelationManager;
                return gestore == null ? -1 : gestore.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);
            }
            catch { return -1; }
        }

        /// <summary>
        /// Il punto al centro della tasca, costruito: una diagonale di
        /// costruzione legata a due angoli opposti, e un punto a meta'. Misurato
        /// il 25/9/2026: cosi' la tasca segue la mezzeria; col centro di
        /// `CreateCenterRectangle` no. Null se qualcosa non e' nato.
        /// </summary>
        private static ISketchPoint CentroDelRettangolo(IModelDoc2 modello, object[] lati, double[] p1, double[] p2)
        {
            if (lati == null) return null;
            ISketchPoint a1 = null, a2 = null;
            foreach (var o in lati)
            {
                var l = o as ISketchLine;
                if (l == null) continue;
                foreach (var p in new[] { (ISketchPoint)l.GetStartPoint2(), (ISketchPoint)l.GetEndPoint2() })
                {
                    if (p == null) continue;
                    if (Math.Abs(p.X - p1[0]) < 1e-9 && Math.Abs(p.Y - p1[1]) < 1e-9) a1 = p;
                    if (Math.Abs(p.X - p2[0]) < 1e-9 && Math.Abs(p.Y - p2[1]) < 1e-9) a2 = p;
                }
            }
            if (a1 == null || a2 == null) return null;
            var schizzi = modello.SketchManager;
            ISketchSegment diagonale;
            ISketchPoint centro;
            bool addPrima = schizzi.AddToDB;
            try
            {
                schizzi.AddToDB = true;
                diagonale = schizzi.CreateLine(p1[0], p1[1], p1[2], p2[0], p2[1], p2[2]);
            }
            finally { schizzi.AddToDB = addPrima; }
            var dl = diagonale as ISketchLine;
            if (dl == null) return null;
            try { diagonale.ConstructionGeometry = true; } catch { return null; }
            if (!Relazione(modello, (ISketchPoint)dl.GetStartPoint2(), a1, "sgCOINCIDENT")) return null;
            if (!Relazione(modello, (ISketchPoint)dl.GetEndPoint2(), a2, "sgCOINCIDENT")) return null;
            try
            {
                schizzi.AddToDB = true;
                centro = schizzi.CreatePoint((p1[0] + p2[0]) / 2, (p1[1] + p2[1]) / 2, (p1[2] + p2[2]) / 2);
            }
            finally { schizzi.AddToDB = addPrima; }
            if (centro == null || !Relazione(modello, centro, diagonale, "sgATMIDDLE")) return null;
            return centro;
        }
    }
}
