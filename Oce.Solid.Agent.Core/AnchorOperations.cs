using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
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
    ///   "anchor": { "x": {"edge_uid": "E7", "distance_mm": 20},
    ///               "y": {"centered_between": ["E3", "E5"]} }
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
    public partial class SwOperations
    {
        private sealed class AnchorEdge
        {
            public string Uid;
            public IEdge Edge;
            public double[] StartMm, FineMm;   // nel sistema del MODELLO
        }

        private sealed class AnchorAxis
        {
            public string Name;                 // «x» o «y»
            public int Index;                  // 0 = x, 1 = y (lungo assi[Indice])
            public AnchorEdge Edge;     // quota da uno spigolo
            public double DistanceMm;
            public AnchorEdge Fra1, Fra2;  // oppure centrata fra due
            public bool Centered { get { return Fra1 != null; } }
            public IDimension CreatedDimension;        // per la prova di movimento
        }

        private sealed class Anchoring
        {
            public AnchorAxis X, Y;
            public bool Empty { get { return X == null && Y == null; } }
            public IEnumerable<AnchorAxis> Axes() { if (X != null) yield return X; if (Y != null) yield return Y; }
        }

        /// <summary>
        /// Legge `anchor` e la confronta con la faccia, con `x_mm`/`y_mm` e con
        /// l'ingombro del profilo (`halfU`, `halfV`: meta' della larghezza del
        /// profilo lungo x e lungo y — il raggio per un foro). Torna un
        /// ancoraggio VUOTO se l'argomento non c'e' (il vecchio modo, coordinate
        /// assolute, resta com'e'); null con il motivo se l'ancora e' sbagliata.
        /// </summary>
        private Anchoring ReadAnchor(
            Dictionary<string, object> a, DocumentState docState, IFace2 face,
            double[] axes, double offsetM, double x, double y, double halfU, double halfV,
            out string reason)
        {
            reason = null;
            object raw;
            if (a == null || !a.TryGetValue("anchor", out raw) || raw == null) return new Anchoring();

            var d = raw as Dictionary<string, object>;
            if (d == null)
            {
                reason = "«anchor» dev'essere un oggetto con «x» e «y»: per ognuno " +
                         "{\"edge_uid\": \"E7\", \"distance_mm\": 20} oppure {\"centered_between\": [\"E3\", \"E5\"]}.";
                return null;
            }
            foreach (var key in d.Keys)
                if (key != "x" && key != "y")
                {
                    reason = "«anchor» non conosce «" + key + "»: le chiavi sono «x» e «y», una per asse.";
                    return null;
                }
            if (!d.ContainsKey("x") || !d.ContainsKey("y"))
            {
                reason = "L'ancora vuole TUTTI E DUE gli assi, «x» e «y». Un asse senza ancora resterebbe " +
                         "libero, e alla prima modifica del pezzo il profilo andrebbe dove lo mette il " +
                         "risolutore, senza errore. Se su un asse vuoi la posizione di adesso, ancoralo allo " +
                         "spigolo da cui la misureresti.";
                return null;
            }

            // LA NORMALE SOLO DI UNA FACCIA PIANA (voce Z-C21.2, rilievo N24).
            // IFace2~Normal.html, Remarks: «If the face is not a planar face, then
            // this property returns 0,0,0». Con (0,0,0) il controllo del bordo
            // di smusso qui sotto dava coseno 0 per OGNI faccia adiacente, e
            // lasciava passare tutto. FaceNormal chiede IsPlane() prima,
            // come Riferimenti.Normale; senza una faccia piana l'ancora si rifiuta.
            double[] normal = FaceNormal(face);
            if (normal == null || normal.Length < 3)
            {
                reason = "La faccia su cui lavori non e' piana (o non riesco a leggerne la normale): " +
                         "l'ancora misura le distanze dagli spigoli nel piano della faccia, e su una " +
                         "faccia curva quel piano non c'e'. Togli «anchor» e dai la posizione con " +
                         "x_mm e y_mm.";
                return null;
            }
            var anc = new Anchoring();
            foreach (var itemName in new[] { "x", "y" })
            {
                int k = itemName == "x" ? 0 : 1;
                double here = k == 0 ? x : y;
                double half = k == 0 ? halfU : halfV;
                var entry = d[itemName] as Dictionary<string, object>;
                if (entry == null)
                {
                    reason = "«ancora." + itemName + "» dev'essere {\"edge_uid\": ..., \"distance_mm\": ...} " +
                             "oppure {\"centered_between\": [..., ...]}.";
                    return null;
                }
                var axis = new AnchorAxis { Name = itemName, Index = k };

                if (entry.ContainsKey("centered_between"))
                {
                    var uid = new List<string>();
                    var list = entry["centered_between"] as IEnumerable;
                    if (list != null && !(entry["centered_between"] is string))
                        foreach (var o in list) uid.Add(Convert.ToString(o, CultureInfo.InvariantCulture));
                    if (uid.Count != 2 || uid[0] == uid[1])
                    {
                        reason = "«ancora." + itemName + ".centrata_fra» vuole DUE spigoli diversi, opposti, presi dallo stato.";
                        return null;
                    }
                    axis.Fra1 = EdgeForAnchor(a, docState, uid[0], offsetM, axes, normal, out reason);
                    if (axis.Fra1 == null) return null;
                    axis.Fra2 = EdgeForAnchor(a, docState, uid[1], offsetM, axes, normal, out reason);
                    if (axis.Fra2 == null) return null;
                    // PER CENTRARE LUNGO x i due spigoli corrono lungo y.
                    if (EdgeAxis(axis.Fra1, axes) != 1 - k || EdgeAxis(axis.Fra2, axes) != 1 - k)
                    {
                        reason = "Per centrare lungo «" + itemName + "» i due spigoli devono correre lungo «" +
                                 (k == 0 ? "y" : "x") + "» della faccia: «" + uid[0] + "» e «" + uid[1] + "» non lo fanno.";
                        return null;
                    }
                    double c1 = Coord(axis.Fra1.StartMm, axes, k), c2 = Coord(axis.Fra2.StartMm, axes, k);
                    if ((c1 - here) * (c2 - here) >= 0)
                    {
                        reason = "Il punto non sta FRA «" + uid[0] + "» e «" + uid[1] + "» lungo «" + itemName +
                                 "»: sono dalla stessa parte, e fra loro non c'e' niente in mezzo a cui centrare.";
                        return null;
                    }
                    double center = (c1 + c2) / 2;
                    if (Math.Abs(center - here) > PointToleranceMm)
                    {
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "Il punto che hai dato ha {0} = {1:0.##}, ma a meta' fra «{2}» e «{3}» c'e' {0} = {4:0.##}. " +
                            "x_mm/y_mm dicono dove il punto sta ADESSO, l'ancora cosa lo tiene li': devono dire lo " +
                            "stesso punto. Rifiuto invece di spostarlo in silenzio.",
                            itemName, here, uid[0], uid[1], center);
                        return null;
                    }
                    if (Math.Abs(center - c1) < half)
                    {
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "Fra «{0}» e «{1}» ci sono {2:0.##} mm lungo «{3}», e il profilo ne e' largo {4:0.##}: " +
                            "non ci sta.", uid[0], uid[1], Math.Abs(c2 - c1), itemName, 2 * half);
                        return null;
                    }
                }
                else
                {
                    string uid = entry.ContainsKey("edge_uid")
                        ? Convert.ToString(entry["edge_uid"], CultureInfo.InvariantCulture) : "";
                    double distance;
                    if (uid.Length == 0 || !entry.ContainsKey("distance_mm") ||
                        !double.TryParse(Convert.ToString(entry["distance_mm"], CultureInfo.InvariantCulture),
                                         NumberStyles.Any, CultureInfo.InvariantCulture, out distance))
                    {
                        reason = "«ancora." + itemName + "» vuole {\"edge_uid\": \"E7\", \"distance_mm\": 20} " +
                                 "oppure {\"centered_between\": [\"E3\", \"E5\"]}.";
                        return null;
                    }
                    // LA DISTANZA E' DAL CENTRO del foro o della tasca, e non
                    // puo' essere meno di meta' della sua larghezza: il profilo
                    // scavalcherebbe lo spigolo (rischio 5 del critico). Uguale
                    // e' ammesso: e' una battuta.
                    if (distance + 1e-9 < half)
                    {
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "«distance_mm» e' dal CENTRO del profilo allo spigolo, e {0:0.##} e' meno di meta' della " +
                            "sua larghezza lungo «{1}» ({2:0.##}): il profilo scavalcherebbe lo spigolo «{3}».",
                            distance, itemName, half, uid);
                        return null;
                    }
                    var sp = EdgeForAnchor(a, docState, uid, offsetM, axes, normal, out reason);
                    if (sp == null) return null;
                    if (EdgeAxis(sp, axes) != 1 - k)
                    {
                        reason = "Per ancorare «" + itemName + "» serve uno spigolo che corre lungo «" + (k == 0 ? "y" : "x") +
                                 "» della faccia (la distanza si misura lungo «" + itemName + "»): «" + uid + "» non lo fa.";
                        return null;
                    }
                    double real = Math.Abs(here - Coord(sp.StartMm, axes, k));
                    if (Math.Abs(real - distance) > PointToleranceMm)
                    {
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "Il punto che hai dato sta a {0:0.##} mm da «{1}», non a {2:0.##}. x_mm/y_mm dicono dove il " +
                            "punto sta ADESSO, l'ancora cosa lo tiene li': devono dire lo stesso punto. Rifiuto invece " +
                            "di spostare il profilo di {3:0.##} mm in silenzio.",
                            real, uid, distance, Math.Abs(real - distance));
                        return null;
                    }
                    axis.Edge = sp;
                    axis.DistanceMm = distance;
                }
                if (k == 0) anc.X = axis; else anc.Y = axis;
            }
            return anc;
        }

        /// <summary>
        /// Lo spigolo per uid: con la firma, dritto, nel piano della faccia, e
        /// non sul bordo di uno smusso o di un raccordo.
        /// </summary>
        private AnchorEdge EdgeForAnchor(
            Dictionary<string, object> a, DocumentState docState, string uid,
            double offsetM, double[] axes, double[] faceNormal, out string reason)
        {
            reason = null;
            // LA FIRMA E' OBBLIGATORIA QUANDO IL PIANO NE PORTA (rischio 2 del
            // critico): `SignatureMatches` senza firma risponde si', e fra spigoli
            // uguali l'ordine dello stato non e' garantito — un foro a meta'
            // altezza passerebbe la coerenza anche legato al lato sbagliato. Un
            // piano che porta `_signatures` ma non quella di questo spigolo e' un piano
            // che non sa quale spigolo intendeva.
            if (a != null && a.ContainsKey("_signatures") && ExpectedSignature(a, uid) == null)
            {
                reason = "Il piano porta le firme delle entita' ma non quella dello spigolo «" + uid + "»: " +
                         "senza, non so se «" + uid + "» e' ancora lo spigolo che avevi visto.";
                return null;
            }
            // Se l'uid e' cambiato fra la lettura e adesso, lo spigolo si
            // ritrova per firma (voce E19): stessa lunghezza, stesso punto.
            var signature = EdgeSignature(
                a, docState, uid,
                "Lo spigolo «" + uid + "» non c'e' in questo documento: gli spigoli dell'ancora si " +
                "prendono dallo stato (E1, E2...).", out reason);
            if (signature == null) return null;
            var edge = signature.Entity as IEdge;
            ICurve curva = null;
            ICurveParamData par = null;
            try { par = ApiHelpers.EdgeParameters(edge, out curva); } catch { }
            if (curva == null || par == null || !curva.IsLine())
            {
                reason = "Lo spigolo «" + uid + "» non e' dritto: l'ancora si prende da uno spigolo dritto.";
                return null;
            }
            var p0 = par.StartPoint as double[];
            var p1 = par.EndPoint as double[];
            if (p0 == null || p1 == null || p0.Length < 3 || p1.Length < 3)
            {
                reason = "Non riesco a leggere gli estremi dello spigolo «" + uid + "».";
                return null;
            }
            int n = (int)axes[2];
            if (Math.Abs(p0[n] - offsetM) > 1e-6 || Math.Abs(p1[n] - offsetM) > 1e-6)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
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
            //
            // «PIANA» NON SI CONTROLLAVA (voce Z-C21.2, rilievo C2-1).
            // IFace2~Normal.html: «Gets the unit normal vector for any planar
            // face» e, nelle Remarks, «If the face is not a planar face, then
            // this property returns 0,0,0». Il raccordo e' un cilindro: Normal
            // dava (0,0,0), il coseno veniva 0 come per una parete perpendicolare
            // vera, e lo spigolo tangente del raccordo passava per spigolo vivo.
            // Il controllo funzionava solo per lo smusso (faccia piana obliqua).
            // Adesso IsPlane() viene prima di Normal, come in
            // Riferimenti.Normale e FaceNormal; una faccia non piana si
            // rifiuta con lo stesso messaggio.
            //
            // E SE NON RIESCE A GUARDARE, NON ACCETTA (rilievo N6). Prima un
            // `catch { }` e un `if` che saltava con le facce o la normale
            // mancanti lasciavano passare lo spigolo senza averlo guardato: un
            // controllo che non guarda e dice si' e' peggio di nessun controllo
            // (memoria «controllo mai visto scattare»).
            string notChecked = null;
            try
            {
                var faces = edge.GetTwoAdjacentFaces2() as object[];
                if (faces == null || faces.Length == 0)
                    notChecked = "SOLIDWORKS non mi da' le facce dello spigolo";
                else if (faceNormal == null || faceNormal.Length < 3)
                    notChecked = "non ho la normale della faccia su cui lavori";
                else
                    foreach (var o in faces)
                    {
                        var f = o as IFace2;
                        if (f == null) continue;   // uno spigolo di bordo ha una faccia sola e un NULL (GetTwoAdjacentFaces2.html)
                        bool planar = false;
                        ISurface surface = null;
                        try { surface = f.GetSurface() as ISurface; planar = surface?.IsPlane() == true; } catch { }
                        // LA NORMALE: di una faccia piana da Normal; di una curva
                        // nel punto medio dello spigolo, da EvaluateAtPoint
                        // (revisione di Z-C21.2, difetto 8). ISurface~EvaluateAtPoint.html:
                        // «This method calculates the normal, the principal
                        // directions, and the principal curvatures, of the surface
                        // at the specified point», e il ritorno comincia con
                        // «surfNorm[i, j, k]». Il verso non conta: si usa il valore
                        // assoluto del coseno. La prima stesura rifiutava OGNI faccia
                        // non piana, anche un cilindro che incontra la faccia a 90
                        // gradi lungo una retta (un mezzo tondo appoggiato): uno
                        // spigolo vivo, e il messaggio «bordo di un raccordo» li'
                        // era falso. Il raccordo si riconosce perche' e' TANGENTE:
                        // la sua normale sullo spigolo e' quella della faccia.
                        double[] nf = null;
                        if (planar) nf = ApiHelpers.NormalIfPlanar(f);
                        else if (surface != null)
                        {
                            try
                            {
                                var v = surface.EvaluateAtPoint(
                                    (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2, (p0[2] + p1[2]) / 2) as double[];
                                if (v != null && v.Length >= 3) nf = new[] { v[0], v[1], v[2] };
                            }
                            catch { nf = null; }
                        }
                        double length = nf == null || nf.Length < 3 ? 0
                            : Math.Sqrt(nf[0] * nf[0] + nf[1] * nf[1] + nf[2] * nf[2]);
                        if (length < 1e-9)
                        {
                            notChecked = "non riesco a leggere la normale di una faccia dello spigolo" +
                                          (planar ? "" : " (non e' piana)");
                            break;
                        }
                        double cosine = Math.Abs(nf[0] * faceNormal[0] + nf[1] * faceNormal[1] +
                                                 nf[2] * faceNormal[2]) / length;
                        if (planar && cosine > 0.999) continue;          // la faccia stessa, o complanare
                        if (!planar && cosine > 0.999)
                        {
                            reason = "Lo spigolo «" + uid + "» sta sul bordo di un raccordo (la sua altra faccia e' " +
                                     "curva e tangente a questa): se il raccordo cambia, lo spigolo si sposta e il " +
                                     "profilo con lui. Ancoralo a uno spigolo vivo del pezzo.";
                            return null;
                        }
                        if (cosine > 0.01)
                        {
                            reason = "Lo spigolo «" + uid + "» sta sul bordo di uno smusso o di un raccordo (la sua " +
                                     "altra faccia " + (planar ? "" : "e' curva e ") + "non e' perpendicolare a questa): " +
                                     "se lo smusso o il raccordo cambia, il profilo si sposta con lui. Ancoralo " +
                                     "a uno spigolo vivo del pezzo.";
                            return null;
                        }
                    }
            }
            catch (Exception ex) { notChecked = ex.Message; }
            if (notChecked != null)
            {
                reason = "Non sono riuscito a guardare l'altra faccia dello spigolo «" + uid + "» (" + notChecked +
                         "): non so se sta sul bordo di uno smusso o di un raccordo, e un'ancora presa li' " +
                         "si sposterebbe con lui senza errore. Ancoralo a un altro spigolo vivo del pezzo.";
                return null;
            }
            return new AnchorEdge
            {
                Uid = uid,
                Edge = edge,
                StartMm = new[] { p0[0] * 1000.0, p0[1] * 1000.0, p0[2] * 1000.0 },
                FineMm = new[] { p1[0] * 1000.0, p1[1] * 1000.0, p1[2] * 1000.0 },
            };
        }

        /// <summary>Lungo quale asse della faccia corre lo spigolo: 0 = x, 1 = y, -1 = obliquo.</summary>
        private static int EdgeAxis(AnchorEdge s, double[] axes)
        {
            double du = Math.Abs(Coord(s.FineMm, axes, 0) - Coord(s.StartMm, axes, 0));
            double dv = Math.Abs(Coord(s.FineMm, axes, 1) - Coord(s.StartMm, axes, 1));
            if (du > 0.01 && dv < 0.001) return 0;
            if (dv > 0.01 && du < 0.001) return 1;
            return -1;
        }

        private static double Coord(double[] pointMm, double[] axes, int which)
        {
            return pointMm[(int)axes[which]];
        }

        /// <summary>
        /// Mette nello schizzo APERTO cio' che tiene il punto dove l'ancora dice,
        /// asse per asse, rilegge tutto, e pretende lo schizzo COMPLETAMENTE
        /// DEFINITO. Torna false con il motivo se qualcosa non torna.
        /// </summary>
        private bool ApplyAnchor(
            IModelDoc2 model, Anchoring anc, ISketchPoint point,
            double[] axes, double offsetM, double x, double y,
            Dictionary<string, object> measurements, out string reason, bool expectDefined = true)
        {
            reason = null;
            if (anc == null || anc.Empty) return true;
            if (point == null)
            {
                reason = "Non ho il punto di riferimento del profilo a cui attaccare l'ancora.";
                return false;
            }
            var said = new Dictionary<string, object>();
            var sketches = model.SketchManager;

            foreach (var axis in anc.Axes())
            {
                int k = axis.Index;
                string error;
                if (axis.Centered)
                {
                    // LA MEZZERIA: lungo l'asse k, all'altezza del punto
                    // sull'altro asse, con gli estremi sulla RETTA dei due
                    // spigoli (coincidenza), perpendicolare a loro, e il punto a
                    // meta'. Misurato il 25/9/2026: centra lungo k e lascia
                    // l'altro asse all'altra ancora.
                    double other = k == 0 ? y : x;
                    double c1 = Coord(axis.Fra1.StartMm, axes, k), c2 = Coord(axis.Fra2.StartMm, axes, k);
                    var m1 = PointInSketch(model, axes, offsetM, k == 0 ? c1 : other, k == 0 ? other : c1, out error);
                    var m2 = m1 == null ? null
                        : PointInSketch(model, axes, offsetM, k == 0 ? c2 : other, k == 0 ? other : c2, out error);
                    if (m1 == null || m2 == null) { reason = error; return false; }

                    int relBefore = SketchRelations(sketches);
                    ISketchSegment centerline;
                    bool addBefore = sketches.AddToDB;
                    try
                    {
                        sketches.AddToDB = true;
                        centerline = sketches.CreateCenterLine(m1[0], m1[1], m1[2], m2[0], m2[1], m2[2]);
                    }
                    finally { sketches.AddToDB = addBefore; }
                    var line = centerline as ISketchLine;
                    if (line == null) { reason = "La mezzeria fra «" + axis.Fra1.Uid + "» e «" + axis.Fra2.Uid + "» non e' nata."; return false; }

                    // THE ENDS ARE CHECKED (Z-C21.8, finding N49): ISketchLine~
                    // GetStartPoint2.html, «Start sketch point or NULL if operation
                    // fails». A null end made the relation fail and the refusal
                    // count relations, which pointed at the wrong thing.
                    var centreLineStart = line.GetStartPoint2() as ISketchPoint;
                    var centreLineEnd = line.GetEndPoint2() as ISketchPoint;
                    if (centreLineStart == null || centreLineEnd == null)
                    {
                        reason = "The centre line between «" + axis.Fra1.Uid + "» and «" + axis.Fra2.Uid + "» gives no " +
                                 (centreLineStart == null ? "start" : "end") + " point (GetStartPoint2 / GetEndPoint2 " +
                                 "answered null): I cannot centre along «" + axis.Name + "».";
                        return false;
                    }
                    bool r1 = Relation(model, centreLineStart, axis.Fra1.Edge, "sgCOINCIDENT");
                    bool r2 = Relation(model, centreLineEnd, axis.Fra2.Edge, "sgCOINCIDENT");
                    bool r3 = Relation(model, centerline, axis.Fra1.Edge, "sgPERPENDICULAR");
                    bool r4 = Relation(model, point, centerline, "sgATMIDDLE");
                    int relAfter = SketchRelations(sketches);
                    // QUATTRO RELAZIONI, CONTATE: `SketchAddConstraints` e' Void,
                    // e una relazione che non nasce non lo dice nessuno.
                    if (!r1 || !r2 || !r3 || !r4 || (relBefore >= 0 && relAfter - relBefore < 4))
                    {
                        reason = "Non sono riuscito a centrare lungo «" + axis.Name + "» fra «" + axis.Fra1.Uid + "» e «" +
                                 axis.Fra2.Uid + "» (relazioni nate: " +
                                 (relBefore < 0 ? "?" : (relAfter - relBefore).ToString(CultureInfo.InvariantCulture)) + " su 4).";
                        return false;
                    }
                    said[axis.Name] = new Dictionary<string, object>
                    {
                        ["centered_between"] = new[] { axis.Fra1.Uid, axis.Fra2.Uid },
                    };
                }
                else
                {
                    double its = Coord(axis.Edge.StartMm, axes, k);
                    // Il numero a meta' strada fra punto e spigolo, un po' di lato:
                    // solo DOVE si scrive, non cosa misura.
                    double tu = k == 0 ? (x + its) / 2 : x + 6;
                    double tv = k == 0 ? y + 6 : (y + its) / 2;
                    var text = PointInSketch(model, axes, offsetM, tu, tv, out error);
                    if (text == null) { reason = error; return false; }
                    // LA DIREZIONE SI LEGGE NELLO SCHIZZO, non nel modello: lo
                    // spigolo corre lungo l'altro asse della faccia, e nello
                    // schizzo quell'asse puo' essere la X o la Y (sulla faccia
                    // di destra x_mm e' la Y del modello). Si portano i due
                    // estremi nello schizzo e si guarda dove corre.
                    double other1 = Coord(axis.Edge.StartMm, axes, 1 - k), other2 = Coord(axis.Edge.FineMm, axes, 1 - k);
                    var e1 = PointInSketch(model, axes, offsetM, k == 0 ? its : other1, k == 0 ? other1 : its, out error);
                    var e2 = e1 == null ? null
                        : PointInSketch(model, axes, offsetM, k == 0 ? its : other2, k == 0 ? other2 : its, out error);
                    if (e2 == null) { reason = error; return false; }
                    bool verticalEdge = Math.Abs(e2[1] - e1[1]) > Math.Abs(e2[0] - e1[0]);
                    IDimension born;
                    double reread = DimensionPointEdge(model, point, axis.Edge.Edge, text, verticalEdge, out born);
                    if (reread < 0)
                    {
                        reason = "La quota fra il punto e lo spigolo «" + axis.Edge.Uid + "» non e' nata.";
                        return false;
                    }
                    if (Math.Abs(reread - axis.DistanceMm) > PointToleranceMm)
                    {
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "La quota dallo spigolo «{0}» e' nata ma vale {1:0.###} mm invece di {2:0.###}: misura " +
                            "un'altra cosa, e non la tengo.", axis.Edge.Uid, reread, axis.DistanceMm);
                        return false;
                    }
                    axis.CreatedDimension = born;
                    said[axis.Name] = new Dictionary<string, object>
                    {
                        ["edge"] = axis.Edge.Uid,
                        ["distance_mm"] = axis.DistanceMm,
                        ["dimension_reread_mm"] = Math.Round(reread, 3),
                    };
                }
            }

            // COMPLETAMENTE DEFINITO, O NIENTE — ma con piu' punti nello stesso
            // schizzo (i fori di `hole_wizard`) lo si guarda dopo l'ultimo.
            if (expectDefined)
            {
                if (!SketchDefinedAndHealthy(sketches, out reason)) return false;
                said["sketch"] = "fully defined";
            }
            // LA TAVOLA (rischio 15): un asse centrato lo tiene una relazione,
            // non una quota, e `model_dimensions` non ha niente da riportare.
            if ((anc.X != null && anc.X.Centered) || (anc.Y != null && anc.Y.Centered))
                said["note_drawing"] = "Lungo l'asse centrato la posizione la tiene una relazione, non una quota: " +
                                       "model_dimensions non la riporta, drawing_dimension la misura dai bordi.";
            measurements["anchor"] = said;
            return true;
        }

        /// <summary>
        /// Lo schizzo aperto e' completamente definito e senza relazioni malate?
        /// Sottovincolato, il profilo va dove vuole il risolutore; sovravincolato,
        /// si rompe alla modifica; una relazione penzolante, sovravincolante o
        /// rotta (rischio 1 del critico) non la esclude lo stato «definito».
        /// </summary>
        private static bool SketchDefinedAndHealthy(ISketchManager sketches, out string reason)
        {
            reason = null;
            int docState = -1;
            try { docState = sketches.ActiveSketch.GetConstrainedStatus(); } catch { docState = -1; }
            if (docState != (int)swConstrainedStatus_e.swFullyConstrained)
            {
                reason = ConstrainedStatusRefusal(docState);
                return false;
            }
            string unhealthy, notRead;
            if (!UnhealthyRelations(sketches, out unhealthy, out notRead))
            {
                reason = "I cannot read the relations of the anchor sketch (" + notRead + "): fully defined is " +
                         "not enough, a dangling, over-defining or broken relation would break it at the first " +
                         "change, and I do not cut without having looked.";
                return false;
            }
            if (unhealthy != null)
            {
                reason = "Lo schizzo dell'ancora ha relazioni " + unhealthy + ": non taglio, si romperebbe alla prima modifica.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// L'ANCORA DI `hole_wizard` (voce E18, l'ultimo pezzo). Lo schizzo dei
        /// punti lo fa HoleWizard, e `PositionHoles` ci mette i punti: qui ci si
        /// rientra, e ogni punto riceve la sua ancora. Misurato il 25/9/2026
        /// (`prova0089 guidato`, il caso r01): lo schizzo di posizionamento e'
        /// il sotto-schizzo con i punti e senza segmenti, si apre con
        /// `EditSketch`, otto quote orizzontali/verticali verso gli spigoli lo
        /// rendono completamente definito, e dopo la modifica i fori stanno a
        /// y -40 -40 100 100 e x -80 -80 140 140: esattamente l'attesa di r01.
        ///
        /// Lo stato di definizione si guarda DOPO l'ultimo punto: un punto non
        /// ancora ancorato lascia lo schizzo sottovincolato per costruzione.
        /// </summary>
        private bool AnchorHoles(
            IModelDoc2 model, IFeature hole, double[] axes, double offsetM,
            List<double[]> positions, List<Anchoring> anchors,
            Dictionary<string, object> measurements, out string reason)
        {
            reason = null;
            var featSketch = PositioningSketch(hole);
            var sketch = featSketch?.GetSpecificFeature2() as ISketch;
            if (sketch == null) { reason = "Non trovo lo schizzo che tiene la posizione dei fori."; return false; }
            var handler = model.SketchManager;
            var called = new List<object>();
            bool beingEdited = false;
            try
            {
                model.ClearSelection2(true);
                if (!featSketch.Select2(false, 0)) { reason = "Non riesco a selezionare lo schizzo dei fori."; return false; }
                model.EditSketch();
                // EDIT MODE IS READ BACK, NOT ASSUMED (Z-C21.8, finding N56).
                // IModelDoc2~EditSketch.html returns nothing: «If the selection is
                // a feature, its underlying sketch is placed in edit mode». The
                // flag was set without looking, and a sketch that did not open
                // went on to ActiveSketch.ModelToSketchTransform and a .NET null
                // reference. Now the open sketch must be there, and must be the
                // positioning sketch when its name can be read.
                var openSketch = handler.ActiveSketch;
                string openName = null, expectedName = null;
                try { openName = (openSketch as IFeature)?.Name; } catch { openName = null; }
                try { expectedName = featSketch.Name; } catch { expectedName = null; }
                bool anotherSketch = openName != null && expectedName != null &&
                                     !string.Equals(openName, expectedName, StringComparison.Ordinal);
                if (openSketch == null || anotherSketch)
                {
                    beingEdited = openSketch != null;   // another sketch is open: the finally closes it
                    reason = openSketch == null
                        ? "The positioning sketch of the holes did not open for editing (EditSketch returns nothing, " +
                          "and no sketch is active after it): I cannot anchor the holes."
                        : "Editing the positioning sketch «" + expectedName + "» opened «" + openName + "» instead: I " +
                          "cannot anchor the holes.";
                    return false;
                }
                beingEdited = true;
                var math = _sw.GetMathUtility() as IMathUtility;
                var transformation = handler.ActiveSketch.ModelToSketchTransform;
                var points = (handler.ActiveSketch.GetSketchPoints2() as object[]) ?? new object[0];
                for (int i = 0; i < positions.Count; i++)
                {
                    var expected = InSketch(math, transformation, axes, offsetM, positions[i]);
                    var candidates = new List<ISketchPoint>();
                    foreach (var o in points)
                    {
                        var p = o as ISketchPoint;
                        if (p != null && expected != null && Math.Abs(p.X - expected[0]) < 1e-7 && Math.Abs(p.Y - expected[1]) < 1e-7)
                            candidates.Add(p);
                    }
                    if (candidates.Count == 0)
                    {
                        reason = "Non ritrovo nello schizzo il punto del foro " + (i + 1) + ".";
                        return false;
                    }
                    // N23: MORE THAN ONE SKETCH POINT CAN ANSWER TO THE SAME
                    // SEARCH WINDOW (1e-7 m above — loose on purpose, since
                    // InSketch's own transform carries some numerical
                    // slack). Up to library 0.146.0 the loop just kept the LAST
                    // one it found: a guess. SameHolePoint proves the candidates
                    // are the same point by the sketch point's own ID, not by
                    // how close they are: two points at the same coordinates are
                    // still two points (two coincident holes in a Hole Wizard
                    // sketch). When it cannot prove it, the call is refused and
                    // names every candidate, in millimetres, with its ID.
                    var candidateXY = new List<double[]>();
                    var candidateIds = new List<long[]>();
                    foreach (ISketchPoint c in candidates)
                    {
                        candidateXY.Add(new[] { c.X, c.Y });
                        candidateIds.Add(SketchPointId(c));
                    }
                    string ambiguity;
                    if (!SameHolePoint(candidateXY.ToArray(), candidateIds.ToArray(), out ambiguity))
                    {
                        reason = "Hole " + (i + 1) + ": " + ambiguity;
                        return false;
                    }
                    var its = candidates[0];
                    var itsMeasure = new Dictionary<string, object>();
                    if (!ApplyAnchor(model, anchors[i], its, axes, offsetM, positions[i][0], positions[i][1],
                                       itsMeasure, out reason, expectDefined: false))
                    {
                        reason = "Foro " + (i + 1) + ": " + reason;
                        return false;
                    }
                    called.Add(itsMeasure.ContainsKey("anchor") ? itsMeasure["anchor"] : null);
                }
                if (!SketchDefinedAndHealthy(handler, out reason)) return false;
                measurements["anchors"] = called.ToArray();
                measurements["hole_sketch"] = "fully defined";
                return true;
            }
            catch (Exception ex) { reason = "L'ancora dei fori non e' riuscita: " + ex.Message; return false; }
            finally
            {
                // THE SELECTION IS CLEARED BEFORE THE SKETCH IS CLOSED (Z-C21.8,
                // finding N56): InsertSketch(true) toggles the sketch, and with no
                // sketch open it starts a new one on the selected plane or face.
                // The anchoring leaves points and edges selected behind it; with
                // the selection empty, the toggle can only close.
                try { model.ClearSelection2(true); } catch { }
                if (beingEdited) { try { handler.InsertSketch(true); } catch { } }
                try { model.ClearSelection2(true); } catch { }
            }
        }

        /// <summary>
        /// N23: are the sketch points AnchorHoles found within its search
        /// window the SAME point, or an ambiguity to refuse instead of guess?
        ///
        /// SAME POINT MEANS SAME ID, NOT SAME PLACE. ISketchPoint~GetID.html,
        /// Remarks: «Each point within a specific sketch has a unique ID», an
        /// ordered pair whose «combination ... is always unique within a
        /// specific sketch». All candidates come from one GetSketchPoints2 of
        /// one sketch, so equal IDs are one point met more than once, and
        /// different IDs are different points however close they sit: two
        /// points at the very same coordinates are two holes on top of each
        /// other in a Hole Wizard sketch, and anchoring either one would be the
        /// guess this file's own rule refuses. A candidate
        /// whose ID could not be read (null) proves nothing either.
        ///
        /// Pure (numbers only, no interop), so Oce.Solid.Agent.ContractTests can drive the
        /// duplicate case a bench case cannot build on purpose.
        /// <paramref name="candidatesXY"/> holds one {x, y} pair per candidate,
        /// in metres, only for the message; <paramref name="candidateIds"/> the
        /// matching {i1, i2} IDs (<see cref="SketchPointId"/>). Empty or null
        /// answers false with no ambiguity text (the caller's own "no
        /// candidate" refusal covers that case).
        /// </summary>
        internal static bool SameHolePoint(double[][] candidatesXY, long[][] candidateIds, out string ambiguity)
        {
            ambiguity = null;
            if (candidatesXY == null || candidatesXY.Length == 0) return false;
            if (candidatesXY.Length == 1) return true;

            bool unreadId = candidateIds == null || candidateIds.Length != candidatesXY.Length;
            for (int c = 0; !unreadId && c < candidateIds.Length; c++)
                if (candidateIds[c] == null || candidateIds[c].Length < 2) unreadId = true;
            bool sameId = !unreadId;
            for (int c = 1; sameId && c < candidateIds.Length; c++)
                if (candidateIds[c][0] != candidateIds[0][0] || candidateIds[c][1] != candidateIds[0][1])
                    sameId = false;
            if (sameId) return true;

            // A plain loop, not a LINQ projection: the obsolete-call scanner of
            // Oce.Solid.Agent.ContractTests reads a bare ".Select(" on an untyped receiver as
            // the old selection call.
            var found = new List<string>();
            for (int c = 0; c < candidatesXY.Length; c++)
            {
                double[] q = candidatesXY[c];
                long[] id = candidateIds != null && c < candidateIds.Length ? candidateIds[c] : null;
                found.Add(string.Format(CultureInfo.InvariantCulture, "({0:0.######}, {1:0.######}) ID {2}",
                                        q[0] * 1000.0, q[1] * 1000.0,
                                        id != null && id.Length >= 2 ? id[0] + "," + id[1] : "unread"));
            }
            ambiguity = candidatesXY.Length + " sketch points answer to the same position, and " +
                        (unreadId
                            ? "the ID of at least one could not be read, so nothing proves they are the same point"
                            : "their IDs differ, so they are not the same point") +
                        ": I will not guess which one is the hole's. Points found (mm): " +
                        string.Join("; ", found) + ".";
            return false;
        }

        /// <summary>
        /// The ID of a sketch point, {i1, i2} (ISketchPoint~GetID.html, Return
        /// Value: «Array with two longs or two integers ... identifying this
        /// sketch point ID»), or null when it cannot be read: null is «not
        /// known», and <see cref="SameHolePoint"/> does not prove anything with
        /// it.
        /// </summary>
        private static long[] SketchPointId(ISketchPoint point)
        {
            try
            {
                var pair = point == null ? null : point.GetID() as Array;
                if (pair == null || pair.Length < 2) return null;
                return new[]
                {
                    Convert.ToInt64(pair.GetValue(0), CultureInfo.InvariantCulture),
                    Convert.ToInt64(pair.GetValue(1), CultureInfo.InvariantCulture),
                };
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// The anchor of a guided hole is read before the diameter is known (the
        /// ISO table decides it, later): here, once the hole exists, no anchor
        /// may overrun an edge with the real radius.
        ///
        /// CENTRED AXES TOO, AND THE WIDEST CUT (Z-C21.2, finding N50). This
        /// check skipped centred axes (`!asse.Centrata`), and ReadAnchor runs
        /// with mezzo = 0 for hole_wizard, so its rule «the profile fits
        /// between the two edges» (the «non ci sta» refusal in ReadAnchor)
        /// never fired: an M8 centred between two edges 6 mm apart was
        /// confirmed, and the hole cut through both of them. The same rule is
        /// applied here, half the gap against the radius, with the same `Coord`
        /// reading of the two edges; equal is allowed, as for a distance.
        /// <paramref name="radiusMm"/> is the radius of the widest cut of the
        /// hole (counterbore or countersink included, see HoleWizard), and
        /// <paramref name="widestCut"/> names it in the message.
        /// </summary>
        private static string BypassWithRadius(List<Anchoring> anchors, double[] axes, double radiusMm, string widestCut)
        {
            for (int i = 0; i < anchors.Count; i++)
                foreach (var axis in anchors[i].Axes())
                {
                    if (axis.Centered)
                    {
                        double gap = Math.Abs(Coord(axis.Fra2.StartMm, axes, axis.Index) -
                                              Coord(axis.Fra1.StartMm, axes, axis.Index));
                        if (gap / 2 + 1e-9 < radiusMm)
                            return string.Format(CultureInfo.InvariantCulture,
                                "Hole {0}: between «{1}» and «{2}» there are {3:0.##} mm along «{4}», and the {5} is " +
                                "{6:0.##} mm wide: it does not fit, and it would cut through both edges.",
                                i + 1, axis.Fra1.Uid, axis.Fra2.Uid, gap, axis.Name, widestCut, 2 * radiusMm);
                    }
                    else if (axis.DistanceMm + 1e-9 < radiusMm)
                        return string.Format(CultureInfo.InvariantCulture,
                            "Hole {0}: «distance_mm» {1:0.##} from edge «{2}» is less than the radius of the {3} " +
                            "({4:0.##} mm): the {3} would cut past the edge.",
                            i + 1, axis.DistanceMm, axis.Edge.Uid, widestCut, radiusMm);
                }
            return null;
        }

        /// <summary>
        /// The refusal for an anchor sketch that is not fully defined, by its
        /// status (Z-C21.8, finding N53). swConstrainedStatus_e.html has seven
        /// values; the old name covered four (under, over, no solution, invalid
        /// solution) and had only an «I do not know» for swUnknownConstraint 1
        /// and swAutosolveOff 7. With automatic solving off the refusal said the
        /// profile «would go where the solver puts it», which is not the
        /// trouble: the solver is not running. -1 is a status that could not be
        /// read. Kept apart from the document so that Oce.Solid.Agent.ContractTests can call it
        /// without SOLIDWORKS.
        /// </summary>
        internal static string ConstrainedStatusRefusal(int status)
        {
            string code = status.ToString(CultureInfo.InvariantCulture);
            switch (status)
            {
                case (int)swConstrainedStatus_e.swAutosolveOff:
                    return "The anchor sketch cannot tell whether it is fully defined: automatic solving of sketches " +
                           "is off (status " + code + ", swAutosolveOff), so SOLIDWORKS is not solving it. I do not cut. " +
                           "Turn automatic solving of sketches back on, or cut without an anchor.";
                case (int)swConstrainedStatus_e.swUnknownConstraint:
                    return "SOLIDWORKS does not know whether the anchor sketch is fully defined (status " + code +
                           ", swUnknownConstraint). I do not cut: a sketch that is not known to be fully defined may " +
                           "move at the first change of the part.";
                case -1:
                    return "I could not read whether the anchor sketch is fully defined (GetConstrainedStatus gave no " +
                           "answer). I do not cut: a sketch that is not known to be fully defined may move at the first " +
                           "change of the part.";
                default:
                    return "With the anchor the sketch should be fully defined, and it is not (status " + code + ": " +
                           ConstrainedStatusName(status) + "). I do not cut: at the first change of the part the profile " +
                           "would go where the solver puts it.";
            }
        }

        /// <summary>The name of a swConstrainedStatus_e value, all seven (finding N53).</summary>
        internal static string ConstrainedStatusName(int status)
        {
            switch (status)
            {
                case (int)swConstrainedStatus_e.swUnknownConstraint: return "unknown";
                case (int)swConstrainedStatus_e.swUnderConstrained: return "under defined";
                case (int)swConstrainedStatus_e.swFullyConstrained: return "fully defined";
                case (int)swConstrainedStatus_e.swOverConstrained: return "over defined";
                case (int)swConstrainedStatus_e.swNoSolution: return "no solution";
                case (int)swConstrainedStatus_e.swInvalidSolution: return "invalid solution";
                case (int)swConstrainedStatus_e.swAutosolveOff: return "automatic solving off";
                default: return "not a swConstrainedStatus_e value";
            }
        }

        /// <summary>
        /// LA PROVA DI MOVIMENTO (rischio 1 del critico): ogni quota d'ancora +0,1
        /// mm, ricostruzione, e la lavorazione nuova e il suo schizzo non devono
        /// andare in errore; poi il valore si rimette e si rilegge. Torna null se
        /// regge, il motivo se no.
        /// </summary>
        private string MovementTest(IModelDoc2 model, Anchoring anc, IFeature cut)
        {
            if (anc == null || anc.Empty) return null;
            foreach (var axis in anc.Axes())
            {
                // A CENTRED AXIS HAS NO DIMENSION TO MOVE (Z-C21.2, finding
                // N51): the sgATMIDDLE relation on the centre line holds it, and
                // skipping it is the design. A DISTANCE axis without its
                // dimension, or whose dimension cannot be read, is a different
                // thing: both went through a `continue`, and the test «passed»
                // having moved nothing, the pattern that M12 closed below for
                // the return of SetSystemValue3. Now they stop the test.
                if (axis.Centered) continue;
                var q = axis.CreatedDimension;
                if (q == null)
                    return MovementTestNotVerifiable(axis, "the anchor dimension was not kept when it was created");
                double era;
                try { era = ApiHelpers.ReadDimensionInConfiguration(q, ""); }
                catch (Exception ex)
                {
                    return MovementTestNotVerifiable(axis, "I cannot read the anchor dimension: " + ex.Message);
                }
                string broken = null;
                string stop = null;
                string notVerified = null;
                try
                {
                    // IL RITORNO SI LEGGE (voce Z-C21.2, rilievo M12).
                    // IDimension~SetSystemValue3.html: «Return Value Success
                    // indicator value as defined in swSetValueReturnStatus_e»
                    // (Successful = 0; Failure = 1, InvalidValue = 2,
                    // DrivenDimension = 3, ModelNotLoaded = 4, FrozenFeatureOwner
                    // = 5, inventario swconst). Si scartava: se la quota non si
                    // muoveva, la ricostruzione non trovava errori perche' non era
                    // cambiato niente, e la prova «passava» senza aver provato.
                    // Si rilegge anche il valore: mosso vuol dire mosso.
                    int moveOutcome = q.SetSystemValue3(era + 0.0001, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
                    if (moveOutcome != (int)swSetValueReturnStatus_e.swSetValue_Successful)
                        stop = "SOLIDWORKS ha risposto " + moveOutcome.ToString(CultureInfo.InvariantCulture) +
                                " a SetSystemValue3 (" + ErrorCodeDecoder.SetValueStatus(moveOutcome) + ")";
                    else
                    {
                        model.EditRebuild3();
                        double moved = era;
                        try { moved = ApiHelpers.ReadDimensionInConfiguration(q, ""); } catch { }
                        if (Math.Abs(moved - (era + 0.0001)) > 1e-9)
                            stop = "rilette, la quota non si e' spostata";
                        else
                            broken = FeatureInError(cut, out notVerified);
                    }
                }
                catch (Exception ex) { broken = ex.Message; }
                finally
                {
                    try
                    {
                        q.SetSystemValue3(era, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
                        model.EditRebuild3();
                    }
                    catch { }
                }
                if (stop != null)
                    return "Non sono riuscito a spostare la quota d'ancora «" + axis.Name + "» di un decimo (" + stop +
                           "): la prova di movimento non ha provato niente, e un'ancora che non so far muovere " +
                           "non te la confermo.";
                if (notVerified != null)
                    return MovementTestNotVerifiable(axis, "the anchor dimension moved, but " + notVerified);
                if (broken != null)
                    return "Spostando la quota d'ancora «" + axis.Name + "» di un decimo, il taglio va in errore (" + broken +
                           "): lo schizzo non regge una modifica, e un'ancora che non regge una modifica non serve.";
                double returned = -1;
                try { returned = ApiHelpers.ReadDimensionInConfiguration(q, ""); } catch { }
                if (Math.Abs(returned - era) > 1e-9)
                    return "Dopo la prova di movimento la quota d'ancora «" + axis.Name + "» non e' tornata al suo valore.";
            }
            return null;
        }

        /// <summary>
        /// The movement test that could not test anything, in words: an anchor
        /// that cannot be tested is not confirmed (Z-C21.2, finding N51).
        /// </summary>
        private static string MovementTestNotVerifiable(AnchorAxis axis, string why)
        {
            return "The movement test of the anchor on «" + axis.Name + "» is not verifiable (" + why +
                   "): it would pass without having tested anything, and I do not confirm an anchor I cannot test.";
        }

        /// <summary>
        /// The feature or its sketch in error, in words; null if both are
        /// healthy. <paramref name="notVerified"/> is not null when the error
        /// codes could not be read (Z-C21.2, finding N51): before, a `catch { }`
        /// answered null, that is «healthy», and the movement test passed
        /// without having looked. IFeature~GetErrorCode2.html, «Return Value
        /// Feature error code as defined in swFeatureError_e», is read through
        /// ApiHelpers.RebuildError(feature, out known), which says when the call
        /// threw. A feature without a sketch is not a gap:
        /// IFeature~GetFirstSubFeature.html, «If no sub-features exist for this
        /// feature, then this method returns NULL».
        /// </summary>
        private static string FeatureInError(IFeature cut, out string notVerified)
        {
            notVerified = null;
            if (cut == null)
            {
                notVerified = "there is no feature to watch";
                return null;
            }
            try
            {
                // S11: ApiHelpers.RebuildError (GetErrorCode2, warnings excluded),
                // which also says the code in words (FeatureErrors), as the summary promises.
                bool known;
                var cutError = ApiHelpers.RebuildError(cut, out known);
                if (!known)
                {
                    notVerified = "GetErrorCode2 did not answer on the feature";
                    return null;
                }
                if (cutError != null) return cutError;
                var sketch = cut.GetFirstSubFeature() as IFeature;
                if (sketch == null) return null;
                var sketchError = ApiHelpers.RebuildError(sketch, out known);
                if (!known)
                {
                    notVerified = "GetErrorCode2 did not answer on its sketch";
                    return null;
                }
                if (sketchError != null) return "schizzo: " + sketchError;
            }
            catch (Exception ex)
            {
                notVerified = "reading its rebuild errors failed: " + ex.Message;
                return null;
            }
            return null;
        }

        /// <summary>
        /// Toglie un taglio appena fatto e il suo schizzo, quando l'ancora non
        /// regge (rischio 11 del critico): una lavorazione che non regge una
        /// modifica non si lascia nel pezzo con «ritirala tu». Torna la coda
        /// del messaggio: tolto e pezzo com'era, oppure cosa e' rimasto.
        ///
        /// «Ha detto di averlo tolto» non basta, come in `DeleteFeature`:
        /// si ricontano le lavorazioni e si rimisura il volume.
        /// </summary>
        private string RemoveCut(IModelDoc2 model, IFeature cut, double? volumeBefore)
        {
            bool removed = false;
            try
            {
                model.ClearSelection2(true);
                if (cut.Select2(false, 0))
                    removed = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch { }
            finally { try { model.ClearSelection2(true); } catch { } }
            if (!removed) return RemainsInPart;
            try { model.EditRebuild3(); } catch { }
            int features = CountFeature(model);
            if (features < 0 || _featuresAtStepStart < 0)
                return " L'ho tolto, con il suo schizzo, ma non ho potuto ricontare " +
                       "l'albero per controllare che il pezzo sia com'era: guardalo.";
            if (features > _featuresAtStepStart)
                return RemovedButTreeLonger(features - _featuresAtStepStart);
            double? volumeAfter = VolumeMm3(model);
            if (volumeBefore == null || volumeAfter == null)
                return " L'ho tolto, con il suo schizzo, ma non ho potuto rimisurare il " +
                       "volume per controllare che il pezzo sia com'era: guardalo.";
            if (Math.Abs(volumeAfter.GetValueOrDefault() - volumeBefore.GetValueOrDefault()) > 0.01)
                return RemovedButVolumeChanged(volumeAfter.GetValueOrDefault(), volumeBefore.GetValueOrDefault());
            return " L'ho tolto, con il suo schizzo: il pezzo e' com'era prima di questo passo.";
        }

        /// <summary>
        /// The tail of a refusal when DeleteSelection2 said it removed what the
        /// step had made, and the tree read back is still LONGER than before the
        /// step (Z-C21.9, finding C9-6). Something of this step is still there,
        /// but not necessarily the feature that was removed: until library
        /// 0.144.0 the three removal helpers (RemoveCut, RemoveFill,
        /// RemoveGusset) answered «it is still in the part: undo it» about
        /// a feature that was gone. The same words as the face fillet
        /// (RemoveFaceFillet): the features in excess, counted.
        /// </summary>
        internal static string RemovedButTreeLonger(int extraFeatures)
        {
            return " I tried to remove it, but the tree still has " +
                   extraFeatures.ToString(CultureInfo.InvariantCulture) +
                   (extraFeatures == 1 ? " feature" : " features") + " more than before this step: undo " +
                   (extraFeatures == 1 ? "it" : "them") + " (" + Sentences.HowToWithdrawIt + ").";
        }

        /// <summary>
        /// The tail of a refusal when DeleteSelection2 removed what the step had
        /// made and the tree is back as it was, but the volume read back is not
        /// the one before the step (Z-C21.9, finding C9-6). The removed feature
        /// is gone, so the sentence says so and gives the new volume, as the
        /// face fillet does (RemoveFaceFillet), instead of «it is still in
        /// the part: undo it».
        /// </summary>
        internal static string RemovedButVolumeChanged(double volumeNowMm3, double volumeBeforeMm3)
        {
            return " I removed it, but the volume is now " +
                   volumeNowMm3.ToString("0.##", CultureInfo.InvariantCulture) + " mm3 instead of " +
                   volumeBeforeMm3.ToString("0.##", CultureInfo.InvariantCulture) +
                   ": look at the part before going on.";
        }

        /// <summary>
        /// Una quota fra un punto dello schizzo e uno spigolo del MODELLO, col
        /// testo in `text` (coordinate di schizzo). Torna il valore riletto in
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
        private double DimensionPointEdge(
            IModelDoc2 model, ISketchPoint point, IEdge edge, double[] text, bool verticalEdge,
            out IDimension born)
        {
            born = null;
            bool asked = true;
            try
            {
                asked = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            }
            catch { }
            try
            {
                model.ClearSelection2(true);
                if (!point.Select4(false, null)) return -1;
                if (!((IEntity)edge).Select4(true, null)) return -1;
                var dimension = (verticalEdge
                    ? model.AddHorizontalDimension2(text[0], text[1], text[2])
                    : model.AddVerticalDimension2(text[0], text[1], text[2])) as IDisplayDimension;
                born = dimension?.GetDimension2(0);
                if (born == null) return -1;
                try { dimension.MarkedForDrawing = true; } catch { }
                return ApiHelpers.ReadDimensionInConfiguration(born, "") * 1000.0;
            }
            catch { return -1; }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
                try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, asked); } catch { }
            }
        }

        /// <summary>
        /// LO STADIO DELL'ASOLA, DEFINITO (voce E18). `DrawStadium` lo
        /// disegna con quattro entita' sotto `AddToDB` e basta: niente relazioni,
        /// niente quote. Qui si aggiunge cio' che lo rende un'asola anche dopo
        /// una modifica, e — se c'e' — l'ancora. Torna quante quote ha messo;
        /// `error` non nullo vuol dire che lo schizzo non e' come serve (con
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
        private int DefineStadium(
            IModelDoc2 model, ISketchSegment[] stadium, double[] axes, double offsetM,
            double x, double y, double angleDeg, double travel, double radius,
            Anchoring anchor, Dictionary<string, object> measurements, out string error)
        {
            error = null;
            if (stadium == null || stadium.Length < 4 || stadium.Any(s => s == null))
            {
                error = "Lo stadio non ha i suoi quattro pezzi: non posso definirlo.";
                return 0;
            }
            ISketchSegment l1 = stadium[0], l2 = stadium[1], a1 = stadium[2], a2 = stadium[3];
            var arcB = a1 as ISketchArc;   // a1 e' l'estremita' dalla parte +corsa
            var arcA = a2 as ISketchArc;
            if (arcA == null || arcB == null)
            {
                error = "Le estremita' dell'asola non sono archi: non posso definirla.";
                return 0;
            }
            var sketches = model.SketchManager;

            // TANGENZE E RAGGI UGUALI: quattro linee-arco, e i due archi uguali.
            int relBefore = SketchRelations(sketches);
            foreach (var pair in new[] { new[] { l1, a1 }, new[] { l1, a2 }, new[] { l2, a1 }, new[] { l2, a2 } })
                Relation(model, pair[0], pair[1], "sgTANGENT");
            Relation(model, a1, a2, "sgSAMELENGTH");
            // THE FIVE ARE COUNTED HERE, BEFORE THE CENTRE LINE (Z-C21.2,
            // finding N54). IModelDoc2~SketchAddConstraints.html gives it no
            // return value (C# «void SketchAddConstraints»), so the relation
            // count is the only proof that a tangency was born. It was taken
            // after the two coincidences below, which are requested exactly for
            // the case in which the centre line does NOT fuse with the centres:
            // then they add two relations, and two missing tangencies passed as
            // «5 of 5».
            int stadiumRelations = SketchRelations(sketches);
            if (relBefore < 0 || stadiumRelations < 0)
                measurements["slot_relations"] = "not counted: SOLIDWORKS did not give the relation count of the " +
                                           "sketch, so I could not check that the four tangencies and the " +
                                           "equal radii were born";
            else if (stadiumRelations - relBefore < 5)
            {
                error = "Not all the relations of the slot were born (" +
                         (stadiumRelations - relBefore).ToString(CultureInfo.InvariantCulture) +
                         " of 5: four tangencies and the equal radii).";
                // Without an anchor the caller does not stop on `error` (a slot
                // that is only less constrained is still cut): the response says it.
                measurements["slot_relations"] = (stadiumRelations - relBefore).ToString(CultureInfo.InvariantCulture) +
                                           " of 5 born (four tangencies and the equal radii): after a change the " +
                                           "slot may lose its shape";
            }

            // LA MEZZERIA FRA I CENTRI, di costruzione e legata a tutti e due.
            double rad = angleDeg * Math.PI / 180.0, cos = Math.Cos(rad), sin = Math.Sin(rad);
            string e1;
            var cA = PointInSketch(model, axes, offsetM, x - travel * cos, y - travel * sin, out e1);
            var cB = cA == null ? null : PointInSketch(model, axes, offsetM, x + travel * cos, y + travel * sin, out e1);
            if (cB == null) { error = e1; return 0; }
            ISketchSegment centerline;
            bool addBefore = sketches.AddToDB;
            try
            {
                sketches.AddToDB = true;
                centerline = sketches.CreateCenterLine(cA[0], cA[1], cA[2], cB[0], cB[1], cB[2]);
            }
            finally { sketches.AddToDB = addBefore; }
            var line = centerline as ISketchLine;
            if (line == null) { error = "La mezzeria fra i centri dell'asola non e' nata."; return 0; }
            // Disegnata sotto AddToDB esattamente sui centri, la mezzeria vi si
            // FONDE (misurato: le coincidenze non aggiungono relazioni). Si
            // chiedono lo stesso, per il caso in cui un arrotondamento le
            // lasciasse a parte; il conto che vale e' quello delle altre cinque.
            // The points go in with `as` (Z-C21.8, finding N49): a null point
            // makes `Relation` answer false without an exception, and these two
            // relations are optional by design, so their answer is not needed.
            Relation(model, line.GetStartPoint2() as ISketchPoint, arcA.GetCenterPoint2() as ISketchPoint, "sgCOINCIDENT");
            Relation(model, line.GetEndPoint2() as ISketchPoint, arcB.GetCenterPoint2() as ISketchPoint, "sgCOINCIDENT");

            // LE QUOTE: il raggio e la mezzeria (la lunghezza fra i centri).
            string e2;
            var textR = PointInSketch(model, axes, offsetM,
                x + (travel + radius + 6) * cos, y + (travel + radius + 6) * sin + 6, out e2);
            var textL = PointInSketch(model, axes, offsetM,
                x - (radius + 8) * sin, y + (radius + 8) * cos, out e2);
            var toDimension = new List<Dimension>();
            if (textR != null) toDimension.Add(Dimension.Su(a1, textR));
            if (textL != null) toDimension.Add(Dimension.Su(centerline, textL));
            int quote = DimensionSegments(model, toDimension);
            if (quote < 2 && error == null) error = "Le quote dell'asola non sono nate tutte (" + quote + " su 2).";

            // L'ORIENTAMENTO. Mezzeria orizzontale o verticale NELLO SCHIZZO:
            // una relazione. Obliqua: un angolo contro il primo spigolo
            // dell'ancora, se c'e'; altrimenti resta libera, e lo si dice.
            bool horizontal = Math.Abs(cB[1] - cA[1]) < 1e-9, vertical = Math.Abs(cB[0] - cA[0]) < 1e-9;
            if (horizontal || vertical)
                SingleRelation(model, centerline, horizontal ? "sgHORIZONTAL2D" : "sgVERTICAL2D");
            else if (!anchor.Empty)
            {
                var axis = anchor.X ?? anchor.Y;
                var reference = axis.Centered ? axis.Fra1 : axis.Edge;
                string e3;
                var textA = PointInSketch(model, axes, offsetM,
                    x + (travel + radius + 14) * cos, y + (travel + radius + 14) * sin, out e3);
                // The angle the dimension must read: between the slot line and
                // the reference edge, both on the face (Z-C21.2, finding N55).
                // It is not `angleDeg` itself: against an edge that runs
                // along y (the edge of an «x» anchor) a slot at 30 degrees reads
                // 60, or its supplement.
                double edgeDeg = Math.Atan2(
                    Coord(reference.FineMm, axes, 1) - Coord(reference.StartMm, axes, 1),
                    Coord(reference.FineMm, axes, 0) - Coord(reference.StartMm, axes, 0)) * 180.0 / Math.PI;
                string angleNotKept = null;
                if (textA == null ||
                    !AngleDimension(model, centerline, reference.Edge, textA,
                                 AngleBetweenLinesDeg(angleDeg, edgeDeg), out angleNotKept))
                {
                    if (error == null)
                        error = "The angle of the slot against edge «" + reference.Uid + "» " +
                                 (angleNotKept ?? "was not born.");
                }
                else quote++;
            }
            else
                measurements["orientation"] = "libero: un'asola obliqua senza ancora non ha uno spigolo da cui " +
                                         "misurare l'angolo, e dopo una modifica puo' ruotare.";

            // L'ANCORA, sul punto a meta' della mezzeria.
            if (!anchor.Empty)
            {
                if (error != null) return quote;
                string e4;
                var center = PointInSketch(model, axes, offsetM, x, y, out e4);
                if (center == null) { error = e4; return quote; }
                ISketchPoint point;
                try
                {
                    sketches.AddToDB = true;
                    point = sketches.CreatePoint(center[0], center[1], center[2]);
                }
                finally { sketches.AddToDB = addBefore; }
                if (point == null || !Relation(model, point, centerline, "sgATMIDDLE"))
                {
                    error = "Non sono riuscito a mettere il punto al centro dell'asola.";
                    return quote;
                }
                string e5;
                if (!ApplyAnchor(model, anchor, point, axes, offsetM, x, y, measurements, out e5))
                    error = e5;
            }
            return quote;
        }

        /// <summary>
        /// Il centro del contorno di `extrude_on_face` a cui si attacca l'ancora
        /// (voce E18), e il mezzo ingombro per asse. Vale per UN contorno solo:
        /// un cerchio, o una spezzata di quattro vertici allineata agli assi
        /// della faccia (un rettangolo). Per tutto il resto un rifiuto che dice
        /// perche': quale sia «il centro» di un poligono qualunque lo decide chi
        /// disegna, e indovinarlo vorrebbe dire ancorare un punto che non ha
        /// scelto nessuno.
        /// </summary>
        private static bool ContourCenterOnly(
            List<Contour> contours, out double cx, out double cy, out double halfU, out double halfV,
            out string reason)
        {
            cx = cy = halfU = halfV = 0;
            reason = null;
            const string Rule =
                "L'ancora di un rialzo vale per UN contorno solo, tondo o rettangolare allineato " +
                "agli assi della faccia: il punto che tiene e' il suo centro. ";
            int howMany = contours == null ? -1 : contours.Count;
            if (howMany != 1)
            {
                reason = Rule + (howMany < 0 ? "Qui non ci sono contorni." : "Qui i contorni sono " + howMany +
                         ": fai un rialzo per contorno, e ancora ciascuno.");
                return false;
            }
            var c = contours[0];
            if (c.Circle)
            {
                cx = c.Center[0]; cy = c.Center[1];
                halfU = halfV = c.Diameter / 2.0;
                return true;
            }
            var points = new List<double[]>();
            if (c.Strokes.Count == 1 && !c.Strokes[0].Spline)
                foreach (var p in c.Strokes[0].Points)
                    if (!points.Any(q => Math.Abs(q[0] - p[0]) < 1e-9 && Math.Abs(q[1] - p[1]) < 1e-9))
                        points.Add(p);
            bool rectangle = points.Count == 4;
            for (int i = 0; rectangle && i < 4; i++)
            {
                var p = points[i]; var q = points[(i + 1) % 4];
                bool horizontal = Math.Abs(p[1] - q[1]) < 1e-9, vertical = Math.Abs(p[0] - q[0]) < 1e-9;
                if (horizontal == vertical) rectangle = false;
            }
            if (!rectangle)
            {
                reason = Rule + "Questo contorno non e' ne' un cerchio ne' un rettangolo allineato.";
                return false;
            }
            double minX = points.Min(p => p[0]), maxX = points.Max(p => p[0]);
            double minY = points.Min(p => p[1]), maxY = points.Max(p => p[1]);
            cx = (minX + maxX) / 2; cy = (minY + maxY) / 2;
            halfU = (maxX - minX) / 2; halfV = (maxY - minY) / 2;
            return true;
        }

        /// <summary>
        /// Definisce il contorno del rialzo e ci attacca l'ancora, a schizzo
        /// aperto (voce E18). Cerchio: il diametro, e l'ancora sul centro.
        /// Rettangolo di quattro linee (gli angoli, disegnati sotto AddToDB, sono
        /// gia' fusi — misurato sull'asola): orizzontali e verticali, due quote,
        /// e l'ancora sul centro costruito con la diagonale, come la tasca.
        /// </summary>
        private bool AnchorContour(
            IModelDoc2 model, Contour contour, List<ISketchSegment> created, double[] axes, double offsetM,
            double cx, double cy, Anchoring anchor, Dictionary<string, object> measurements, out string error)
        {
            error = null;
            ISketchPoint point;
            if (contour.Circle)
            {
                var circle = created.Count == 1 ? created[0] as ISketchArc : null;
                if (circle == null) { error = "Il cerchio del rialzo non e' nato come ci si aspetta."; return false; }
                var text = PointInSketch(model, axes, offsetM,
                    cx + contour.Diameter * 0.7 + 5, cy + contour.Diameter * 0.7 + 5, out error);
                if (text == null || DimensionSegments(model, new List<Dimension> { Dimension.Su(created[0], text) }) < 1)
                {
                    error = error ?? "Il diametro del rialzo non si e' lasciato quotare.";
                    return false;
                }
                point = circle.GetCenterPoint2() as ISketchPoint;
            }
            else
            {
                if (created.Count != 4 || created.Any(s => !(s is ISketchLine)))
                {
                    error = "Il rettangolo del rialzo non ha i suoi quattro lati.";
                    return false;
                }
                // THE ENDS ARE CHECKED (Z-C21.8, finding N49): ISketchLine~
                // GetStartPoint2.html, «Start sketch point or NULL if operation
                // fails». A null end reached .X and gave the bare .NET message;
                // the twin in RectangleCenter already skipped nulls.
                const string noEnd =
                    "A side of the rectangle of the boss gives no end point (GetStartPoint2 / GetEndPoint2 answered " +
                    "null): I cannot anchor the rectangle.";
                var angles = new List<double[]>();
                foreach (var s in created)
                {
                    var l = (ISketchLine)s;
                    var a0 = l.GetStartPoint2() as ISketchPoint;
                    var a1 = l.GetEndPoint2() as ISketchPoint;
                    if (a0 == null || a1 == null) { error = noEnd; return false; }
                    bool horizontal = Math.Abs(a0.Y - a1.Y) < 1e-9;
                    SingleRelation(model, s, horizontal ? "sgHORIZONTAL2D" : "sgVERTICAL2D");
                    angles.Add(new[] { a0.X, a0.Y, a0.Z });
                }
                // Le due quote: un lato per verso, col testo fuori dal rettangolo.
                var p0 = ((ISketchLine)created[0]).GetStartPoint2() as ISketchPoint;
                var p1 = ((ISketchLine)created[0]).GetEndPoint2() as ISketchPoint;
                var q1 = ((ISketchLine)created[1]).GetEndPoint2() as ISketchPoint;
                if (p0 == null || p1 == null || q1 == null) { error = noEnd; return false; }
                var toDimension = new List<Dimension>
                {
                    Dimension.Su(created[0], new[] { (p0.X + p1.X) / 2 + (p1.X - q1.X) * 0.2, (p0.Y + p1.Y) / 2 + (p1.Y - q1.Y) * 0.2, 0.0 }),
                    Dimension.Su(created[1], new[] { (p1.X + q1.X) / 2 + (p1.X - p0.X) * 0.2, (p1.Y + q1.Y) / 2 + (p1.Y - p0.Y) * 0.2, 0.0 }),
                };
                if (DimensionSegments(model, toDimension) < 2)
                {
                    error = "Le quote del rettangolo del rialzo non sono nate tutte.";
                    return false;
                }
                point = RectangleCenter(model, created.Cast<object>().ToArray(), angles[0], angles[2]);
                if (point == null)
                {
                    error = "Non sono riuscito a costruire il centro del rettangolo a cui attaccare l'ancora.";
                    return false;
                }
            }
            return ApplyAnchor(model, anchor, point, axes, offsetM, cx, cy, measurements, out error);
        }

        /// <summary>
        /// Una quota ANGOLARE fra una linea dello schizzo e uno spigolo del
        /// modello. `AddSpecificDimension` col tipo esplicito: `Extension.
        /// AddDimension` torna null su questa coppia, e `AddDimension2` e'
        /// vietata (GP6). Misurato il 25/9/2026: 30 gradi su 30.
        ///
        /// THE STATUS AND THE VALUE ARE READ (Z-C21.2, finding N55).
        /// IModelDocExtension~AddSpecificDimension.html: «Error Result status as
        /// defined in swAddSpecificDimension_e», and swAddSpecificDimension_e
        /// has swAddSpecificDimension_Success 0 and
        /// swAddSpecificDimension_DimTypeMismatch 1. Only `quota == null` was
        /// checked, and the caller counted a dimension of unknown type and
        /// value. Now the status must be Success and the value, read back,
        /// must be <paramref name="betweenDeg"/> or its supplement: an angular
        /// dimension between two lines shows one of the two, depending on
        /// where its text sits, like DimensionPointEdge refuses a dimension that
        /// measures something else. False with <paramref name="whyNot"/> null
        /// means «not born» (the caller's message); not null says what is wrong.
        /// </summary>
        private bool AngleDimension(IModelDoc2 model, ISketchSegment line, IEdge edge, double[] text,
                                 double betweenDeg, out string whyNot)
        {
            whyNot = null;
            bool asked = true;
            try
            {
                asked = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            }
            catch { }
            try
            {
                model.ClearSelection2(true);
                if (!line.Select4(false, null) || !((IEntity)edge).Select4(true, null)) return false;
                int error = 0;
                var dimension = model.Extension.AddSpecificDimension(
                    text[0], text[1], text[2], (int)swDimensionType_e.swAngularDimension, ref error) as IDisplayDimension;
                if (dimension == null) return false;
                if (error != (int)swAddSpecificDimension_e.swAddSpecificDimension_Success)
                {
                    whyNot = "was born, but AddSpecificDimension answered " + (swAddSpecificDimension_e)error +
                             ": I do not keep a dimension of the wrong type.";
                    return false;
                }
                double readDeg;
                try { readDeg = Units.Degrees(ApiHelpers.ReadDimensionInConfiguration(dimension.GetDimension2(0), "")); }
                catch (Exception ex)
                {
                    whyNot = "was born, but I cannot read its value back (" + ex.Message + ").";
                    return false;
                }
                const double AngleToleranceDeg = 0.01;
                if (Math.Abs(readDeg - betweenDeg) > AngleToleranceDeg &&
                    Math.Abs(readDeg - (180.0 - betweenDeg)) > AngleToleranceDeg)
                {
                    whyNot = string.Format(CultureInfo.InvariantCulture,
                        "was born but reads {0:0.###} degrees instead of {1:0.###} (or {2:0.###}): it measures " +
                        "something else, and I do not keep it.", readDeg, betweenDeg, 180.0 - betweenDeg);
                    return false;
                }
                try { dimension.MarkedForDrawing = true; } catch { }
                return true;
            }
            catch { return false; }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
                try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, asked); } catch { }
            }
        }

        /// <summary>
        /// The angle between two LINES given by their directions in degrees,
        /// from 0 to 90: a line has no verse, so 30 and 210 are the same line.
        /// </summary>
        private static double AngleBetweenLinesDeg(double aDeg, double bDeg)
        {
            double d = Math.Abs(aDeg - bDeg) % 180.0;
            return Math.Min(d, 180.0 - d);
        }

        /// <summary>
        /// Una relazione fra due cose dello schizzo o del modello. Si passano
        /// come `object` perche' si selezionano ognuna col SUO `Select4`:
        /// misurato il 25/9/2026, un punto o un segmento di schizzo NON sono
        /// `IEntity` — «as IEntity» torna null in silenzio, e la prima stesura
        /// della mezzeria faceva nascere due relazioni su quattro.
        /// </summary>
        private static bool Relation(IModelDoc2 model, object one, object due, string which)
        {
            try
            {
                model.ClearSelection2(true);
                if (!Select(one, false) || !Select(due, true)) return false;
                model.SketchAddConstraints(which);
                return true;
            }
            catch { return false; }
            finally { try { model.ClearSelection2(true); } catch { } }
        }

        /// <summary>Una relazione su una cosa sola (orizzontale, verticale).</summary>
        private static bool SingleRelation(IModelDoc2 model, object what, string which)
        {
            try
            {
                model.ClearSelection2(true);
                if (!Select(what, false)) return false;
                model.SketchAddConstraints(which);
                return true;
            }
            catch { return false; }
            finally { try { model.ClearSelection2(true); } catch { } }
        }

        private static bool Select(object what, bool append)
        {
            var point = what as ISketchPoint;
            if (point != null) return point.Select4(append, null);
            var segment = what as ISketchSegment;
            if (segment != null) return segment.Select4(append, null);
            var entity = what as IEntity;
            if (entity != null) return entity.Select4(append, null);
            return false;
        }

        /// <summary>
        /// The dangling, over-defining and broken relations of the open sketch,
        /// in words in <paramref name="unhealthy"/> (null if there are none).
        ///
        /// «NONE» AND «NOT READ» ARE TWO ANSWERS (Z-C21.2, finding N52). The
        /// function returned null both for a sketch without sick relations and
        /// when the RelationManager did not come or GetRelationsCount threw,
        /// and SketchDefinedAndHealthy read null as healthy: the check said yes
        /// without having looked. ISketchRelationManager~GetRelationsCount.html:
        /// «Return Value Number of sketch relations»; a count that does not come,
        /// or comes negative, is not a count. Now the function returns false,
        /// with the reason in <paramref name="notRead"/>, and the caller refuses.
        /// </summary>
        private static bool UnhealthyRelations(ISketchManager sketches, out string unhealthy, out string notRead)
        {
            unhealthy = null;
            notRead = null;
            try
            {
                var handler = sketches.ActiveSketch?.RelationManager as ISketchRelationManager;
                if (handler == null)
                {
                    notRead = "SOLIDWORKS does not give the relation manager of the open sketch";
                    return false;
                }
                var called = new List<string>();
                int n = handler.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling);
                if (n > 0) called.Add(n + " penzolant" + (n == 1 ? "e" : "i"));
                int dangling = n;
                n = handler.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining);
                if (n > 0) called.Add(n + " sovravincolant" + (n == 1 ? "e" : "i"));
                int overDefining = n;
                n = handler.GetRelationsCount((int)swSketchRelationFilterType_e.swBroken);
                if (n > 0) called.Add(n + " rott" + (n == 1 ? "a" : "e"));
                if (dangling < 0 || overDefining < 0 || n < 0)
                {
                    notRead = string.Format(CultureInfo.InvariantCulture,
                        "GetRelationsCount answered {0} dangling, {1} over-defining, {2} broken", dangling, overDefining, n);
                    return false;
                }
                unhealthy = called.Count == 0 ? null : string.Join(", ", called);
                return true;
            }
            catch (Exception ex)
            {
                notRead = "counting them failed: " + ex.Message;
                return false;
            }
        }

        private static int SketchRelations(ISketchManager sketches)
        {
            try
            {
                var handler = sketches.ActiveSketch?.RelationManager as ISketchRelationManager;
                return handler == null ? -1 : handler.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);
            }
            catch { return -1; }
        }

        /// <summary>
        /// Il punto al centro della tasca, costruito: una diagonale di
        /// costruzione legata a due angoli opposti, e un punto a meta'. Misurato
        /// il 25/9/2026: cosi' la tasca segue la mezzeria; col centro di
        /// `CreateCenterRectangle` no. Null se qualcosa non e' nato.
        /// </summary>
        private static ISketchPoint RectangleCenter(IModelDoc2 model, object[] sides, double[] p1, double[] p2)
        {
            if (sides == null) return null;
            ISketchPoint a1 = null, a2 = null;
            foreach (var o in sides)
            {
                var l = o as ISketchLine;
                if (l == null) continue;
                foreach (var p in new[] { l.GetStartPoint2() as ISketchPoint, l.GetEndPoint2() as ISketchPoint })
                {
                    if (p == null) continue;
                    if (Math.Abs(p.X - p1[0]) < 1e-9 && Math.Abs(p.Y - p1[1]) < 1e-9) a1 = p;
                    if (Math.Abs(p.X - p2[0]) < 1e-9 && Math.Abs(p.Y - p2[1]) < 1e-9) a2 = p;
                }
            }
            if (a1 == null || a2 == null) return null;
            var sketches = model.SketchManager;
            ISketchSegment diagonal;
            ISketchPoint center;
            bool addBefore = sketches.AddToDB;
            try
            {
                sketches.AddToDB = true;
                diagonal = sketches.CreateLine(p1[0], p1[1], p1[2], p2[0], p2[1], p2[2]);
            }
            finally { sketches.AddToDB = addBefore; }
            var dl = diagonal as ISketchLine;
            if (dl == null) return null;
            try { diagonal.ConstructionGeometry = true; } catch { return null; }
            // `as` and a null check (Z-C21.8, finding N49); null is «nothing
            // born», which the caller already refuses in words.
            var diagonalStart = dl.GetStartPoint2() as ISketchPoint;
            var diagonalEnd = dl.GetEndPoint2() as ISketchPoint;
            if (diagonalStart == null || diagonalEnd == null) return null;
            if (!Relation(model, diagonalStart, a1, "sgCOINCIDENT")) return null;
            if (!Relation(model, diagonalEnd, a2, "sgCOINCIDENT")) return null;
            try
            {
                sketches.AddToDB = true;
                center = sketches.CreatePoint((p1[0] + p2[0]) / 2, (p1[1] + p2[1]) / 2, (p1[2] + p2[2]) / 2);
            }
            finally { sketches.AddToDB = addBefore; }
            if (center == null || !Relation(model, center, diagonal, "sgATMIDDLE")) return null;
            return center;
        }
    }
}
