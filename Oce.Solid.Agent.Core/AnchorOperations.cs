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
        /// Legge `anchor` e la confronta con la faccia, con `x_mm`/`y_mm` e con
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
            if (a == null || !a.TryGetValue("anchor", out grezza) || grezza == null) return new Ancoraggio();

            var d = grezza as Dictionary<string, object>;
            if (d == null)
            {
                motivo = "«anchor» dev'essere un oggetto con «x» e «y»: per ognuno " +
                         "{\"edge_uid\": \"E7\", \"distance_mm\": 20} oppure {\"centered_between\": [\"E3\", \"E5\"]}.";
                return null;
            }
            foreach (var chiave in d.Keys)
                if (chiave != "x" && chiave != "y")
                {
                    motivo = "«anchor» non conosce «" + chiave + "»: le chiavi sono «x» e «y», una per asse.";
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

            // LA NORMALE SOLO DI UNA FACCIA PIANA (voce Z-C21.2, rilievo N24).
            // IFace2~Normal.html, Remarks: «If the face is not a planar face, then
            // this property returns 0,0,0». Con (0,0,0) il controllo del bordo
            // di smusso qui sotto dava coseno 0 per OGNI faccia adiacente, e
            // lasciava passare tutto. NormaleDellaFaccia chiede IsPlane() prima,
            // come Riferimenti.Normale; senza una faccia piana l'ancora si rifiuta.
            double[] normale = NormaleDellaFaccia(faccia);
            if (normale == null || normale.Length < 3)
            {
                motivo = "La faccia su cui lavori non e' piana (o non riesco a leggerne la normale): " +
                         "l'ancora misura le distanze dagli spigoli nel piano della faccia, e su una " +
                         "faccia curva quel piano non c'e'. Togli «anchor» e dai la posizione con " +
                         "x_mm e y_mm.";
                return null;
            }
            var anc = new Ancoraggio();
            foreach (var nome in new[] { "x", "y" })
            {
                int k = nome == "x" ? 0 : 1;
                double qui = k == 0 ? x : y;
                double mezzo = k == 0 ? mezzoU : mezzoV;
                var voce = d[nome] as Dictionary<string, object>;
                if (voce == null)
                {
                    motivo = "«ancora." + nome + "» dev'essere {\"edge_uid\": ..., \"distance_mm\": ...} " +
                             "oppure {\"centered_between\": [..., ...]}.";
                    return null;
                }
                var asse = new AncoraAsse { Nome = nome, Indice = k };

                if (voce.ContainsKey("centered_between"))
                {
                    var uid = new List<string>();
                    var elenco = voce["centered_between"] as IEnumerable;
                    if (elenco != null && !(voce["centered_between"] is string))
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
                    string uid = voce.ContainsKey("edge_uid")
                        ? Convert.ToString(voce["edge_uid"], CultureInfo.InvariantCulture) : "";
                    double distanza;
                    if (uid.Length == 0 || !voce.ContainsKey("distance_mm") ||
                        !double.TryParse(Convert.ToString(voce["distance_mm"], CultureInfo.InvariantCulture),
                                         NumberStyles.Any, CultureInfo.InvariantCulture, out distanza))
                    {
                        motivo = "«ancora." + nome + "» vuole {\"edge_uid\": \"E7\", \"distance_mm\": 20} " +
                                 "oppure {\"centered_between\": [\"E3\", \"E5\"]}.";
                        return null;
                    }
                    // LA DISTANZA E' DAL CENTRO del foro o della tasca, e non
                    // puo' essere meno di meta' della sua larghezza: il profilo
                    // scavalcherebbe lo spigolo (rischio 5 del critico). Uguale
                    // e' ammesso: e' una battuta.
                    if (distanza + 1e-9 < mezzo)
                    {
                        motivo = string.Format(CultureInfo.InvariantCulture,
                            "«distance_mm» e' dal CENTRO del profilo allo spigolo, e {0:0.##} e' meno di meta' della " +
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
            // LA FIRMA E' OBBLIGATORIA QUANDO IL PIANO NE PORTA (rischio 2 del
            // critico): `FirmaCombacia` senza firma risponde si', e fra spigoli
            // uguali l'ordine dello stato non e' garantito — un foro a meta'
            // altezza passerebbe la coerenza anche legato al lato sbagliato. Un
            // piano che porta `_signatures` ma non quella di questo spigolo e' un piano
            // che non sa quale spigolo intendeva.
            if (a != null && a.ContainsKey("_signatures") && FirmaAttesa(a, uid) == null)
            {
                motivo = "Il piano porta le firme delle entita' ma non quella dello spigolo «" + uid + "»: " +
                         "senza, non so se «" + uid + "» e' ancora lo spigolo che avevi visto.";
                return null;
            }
            // Se l'uid e' cambiato fra la lettura e adesso, lo spigolo si
            // ritrova per firma (voce E19): stessa lunghezza, stesso punto.
            var firma = FirmaDelloSpigolo(
                a, stato, uid,
                "Lo spigolo «" + uid + "» non c'e' in questo documento: gli spigoli dell'ancora si " +
                "prendono dallo stato (E1, E2...).", out motivo);
            if (firma == null) return null;
            var spigolo = firma.Entita as IEdge;
            ICurve curva = null;
            ICurveParamData par = null;
            try { par = ApiHelpers.EdgeParameters(spigolo, out curva); } catch { }
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
            //
            // «PIANA» NON SI CONTROLLAVA (voce Z-C21.2, rilievo C2-1).
            // IFace2~Normal.html: «Gets the unit normal vector for any planar
            // face» e, nelle Remarks, «If the face is not a planar face, then
            // this property returns 0,0,0». Il raccordo e' un cilindro: Normal
            // dava (0,0,0), il coseno veniva 0 come per una parete perpendicolare
            // vera, e lo spigolo tangente del raccordo passava per spigolo vivo.
            // Il controllo funzionava solo per lo smusso (faccia piana obliqua).
            // Adesso IsPlane() viene prima di Normal, come in
            // Riferimenti.Normale e NormaleDellaFaccia; una faccia non piana si
            // rifiuta con lo stesso messaggio.
            //
            // E SE NON RIESCE A GUARDARE, NON ACCETTA (rilievo N6). Prima un
            // `catch { }` e un `if` che saltava con le facce o la normale
            // mancanti lasciavano passare lo spigolo senza averlo guardato: un
            // controllo che non guarda e dice si' e' peggio di nessun controllo
            // (memoria «controllo mai visto scattare»).
            string nonGuardato = null;
            try
            {
                var facce = spigolo.GetTwoAdjacentFaces2() as object[];
                if (facce == null || facce.Length == 0)
                    nonGuardato = "SOLIDWORKS non mi da' le facce dello spigolo";
                else if (normaleFaccia == null || normaleFaccia.Length < 3)
                    nonGuardato = "non ho la normale della faccia su cui lavori";
                else
                    foreach (var o in facce)
                    {
                        var f = o as IFace2;
                        if (f == null) continue;   // uno spigolo di bordo ha una faccia sola e un NULL (GetTwoAdjacentFaces2.html)
                        bool piana = false;
                        ISurface superficie = null;
                        try { superficie = f.GetSurface() as ISurface; piana = superficie?.IsPlane() == true; } catch { }
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
                        if (piana) nf = ApiHelpers.NormalIfPlanar(f);
                        else if (superficie != null)
                        {
                            try
                            {
                                var v = superficie.EvaluateAtPoint(
                                    (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2, (p0[2] + p1[2]) / 2) as double[];
                                if (v != null && v.Length >= 3) nf = new[] { v[0], v[1], v[2] };
                            }
                            catch { nf = null; }
                        }
                        double lunghezza = nf == null || nf.Length < 3 ? 0
                            : Math.Sqrt(nf[0] * nf[0] + nf[1] * nf[1] + nf[2] * nf[2]);
                        if (lunghezza < 1e-9)
                        {
                            nonGuardato = "non riesco a leggere la normale di una faccia dello spigolo" +
                                          (piana ? "" : " (non e' piana)");
                            break;
                        }
                        double coseno = Math.Abs(nf[0] * normaleFaccia[0] + nf[1] * normaleFaccia[1] +
                                                 nf[2] * normaleFaccia[2]) / lunghezza;
                        if (piana && coseno > 0.999) continue;          // la faccia stessa, o complanare
                        if (!piana && coseno > 0.999)
                        {
                            motivo = "Lo spigolo «" + uid + "» sta sul bordo di un raccordo (la sua altra faccia e' " +
                                     "curva e tangente a questa): se il raccordo cambia, lo spigolo si sposta e il " +
                                     "profilo con lui. Ancoralo a uno spigolo vivo del pezzo.";
                            return null;
                        }
                        if (coseno > 0.01)
                        {
                            motivo = "Lo spigolo «" + uid + "» sta sul bordo di uno smusso o di un raccordo (la sua " +
                                     "altra faccia " + (piana ? "" : "e' curva e ") + "non e' perpendicolare a questa): " +
                                     "se lo smusso o il raccordo cambia, il profilo si sposta con lui. Ancoralo " +
                                     "a uno spigolo vivo del pezzo.";
                            return null;
                        }
                    }
            }
            catch (Exception ex) { nonGuardato = ex.Message; }
            if (nonGuardato != null)
            {
                motivo = "Non sono riuscito a guardare l'altra faccia dello spigolo «" + uid + "» (" + nonGuardato +
                         "): non so se sta sul bordo di uno smusso o di un raccordo, e un'ancora presa li' " +
                         "si sposterebbe con lui senza errore. Ancoralo a un altro spigolo vivo del pezzo.";
                return null;
            }
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
            Dictionary<string, object> misure, out string motivo, bool pretendiDefinito = true)
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

                    // THE ENDS ARE CHECKED (Z-C21.8, finding N49): ISketchLine~
                    // GetStartPoint2.html, «Start sketch point or NULL if operation
                    // fails». A null end made the relation fail and the refusal
                    // count relations, which pointed at the wrong thing.
                    var centreLineStart = linea.GetStartPoint2() as ISketchPoint;
                    var centreLineEnd = linea.GetEndPoint2() as ISketchPoint;
                    if (centreLineStart == null || centreLineEnd == null)
                    {
                        motivo = "The centre line between «" + asse.Fra1.Uid + "» and «" + asse.Fra2.Uid + "» gives no " +
                                 (centreLineStart == null ? "start" : "end") + " point (GetStartPoint2 / GetEndPoint2 " +
                                 "answered null): I cannot centre along «" + asse.Nome + "».";
                        return false;
                    }
                    bool r1 = Relazione(modello, centreLineStart, asse.Fra1.Spigolo, "sgCOINCIDENT");
                    bool r2 = Relazione(modello, centreLineEnd, asse.Fra2.Spigolo, "sgCOINCIDENT");
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
                        ["centered_between"] = new[] { asse.Fra1.Uid, asse.Fra2.Uid },
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
                        ["edge"] = asse.Spigolo.Uid,
                        ["distance_mm"] = asse.DistanzaMm,
                        ["dimension_reread_mm"] = Math.Round(riletta, 3),
                    };
                }
            }

            // COMPLETAMENTE DEFINITO, O NIENTE — ma con piu' punti nello stesso
            // schizzo (i fori di `hole_wizard`) lo si guarda dopo l'ultimo.
            if (pretendiDefinito)
            {
                if (!SchizzoDefinitoESano(schizzi, out motivo)) return false;
                detto["sketch"] = "fully defined";
            }
            // LA TAVOLA (rischio 15): un asse centrato lo tiene una relazione,
            // non una quota, e `model_dimensions` non ha niente da riportare.
            if ((anc.X != null && anc.X.Centrata) || (anc.Y != null && anc.Y.Centrata))
                detto["note_drawing"] = "Lungo l'asse centrato la posizione la tiene una relazione, non una quota: " +
                                       "model_dimensions non la riporta, drawing_dimension la misura dai bordi.";
            misure["anchor"] = detto;
            return true;
        }

        /// <summary>
        /// Lo schizzo aperto e' completamente definito e senza relazioni malate?
        /// Sottovincolato, il profilo va dove vuole il risolutore; sovravincolato,
        /// si rompe alla modifica; una relazione penzolante, sovravincolante o
        /// rotta (rischio 1 del critico) non la esclude lo stato «definito».
        /// </summary>
        private static bool SchizzoDefinitoESano(ISketchManager schizzi, out string motivo)
        {
            motivo = null;
            int stato = -1;
            try { stato = schizzi.ActiveSketch.GetConstrainedStatus(); } catch { stato = -1; }
            if (stato != (int)swConstrainedStatus_e.swFullyConstrained)
            {
                motivo = ConstrainedStatusRefusal(stato);
                return false;
            }
            string malate, notRead;
            if (!RelazioniMalate(schizzi, out malate, out notRead))
            {
                motivo = "I cannot read the relations of the anchor sketch (" + notRead + "): fully defined is " +
                         "not enough, a dangling, over-defining or broken relation would break it at the first " +
                         "change, and I do not cut without having looked.";
                return false;
            }
            if (malate != null)
            {
                motivo = "Lo schizzo dell'ancora ha relazioni " + malate + ": non taglio, si romperebbe alla prima modifica.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// L'ANCORA DI `hole_wizard` (voce E18, l'ultimo pezzo). Lo schizzo dei
        /// punti lo fa HoleWizard, e `PosizionaIFori` ci mette i punti: qui ci si
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
        private bool AncoraIFori(
            IModelDoc2 modello, IFeature foro, double[] assi, double offsetM,
            List<double[]> posizioni, List<Ancoraggio> ancore,
            Dictionary<string, object> misure, out string motivo)
        {
            motivo = null;
            var featSchizzo = SchizzoDiPosizionamento(foro);
            var schizzo = featSchizzo?.GetSpecificFeature2() as ISketch;
            if (schizzo == null) { motivo = "Non trovo lo schizzo che tiene la posizione dei fori."; return false; }
            var gestore = modello.SketchManager;
            var dette = new List<object>();
            bool inModifica = false;
            try
            {
                modello.ClearSelection2(true);
                if (!featSchizzo.Select2(false, 0)) { motivo = "Non riesco a selezionare lo schizzo dei fori."; return false; }
                modello.EditSketch();
                // EDIT MODE IS READ BACK, NOT ASSUMED (Z-C21.8, finding N56).
                // IModelDoc2~EditSketch.html returns nothing: «If the selection is
                // a feature, its underlying sketch is placed in edit mode». The
                // flag was set without looking, and a sketch that did not open
                // went on to ActiveSketch.ModelToSketchTransform and a .NET null
                // reference. Now the open sketch must be there, and must be the
                // positioning sketch when its name can be read.
                var openSketch = gestore.ActiveSketch;
                string openName = null, expectedName = null;
                try { openName = (openSketch as IFeature)?.Name; } catch { openName = null; }
                try { expectedName = featSchizzo.Name; } catch { expectedName = null; }
                bool anotherSketch = openName != null && expectedName != null &&
                                     !string.Equals(openName, expectedName, StringComparison.Ordinal);
                if (openSketch == null || anotherSketch)
                {
                    inModifica = openSketch != null;   // another sketch is open: the finally closes it
                    motivo = openSketch == null
                        ? "The positioning sketch of the holes did not open for editing (EditSketch returns nothing, " +
                          "and no sketch is active after it): I cannot anchor the holes."
                        : "Editing the positioning sketch «" + expectedName + "» opened «" + openName + "» instead: I " +
                          "cannot anchor the holes.";
                    return false;
                }
                inModifica = true;
                var matematica = _sw.GetMathUtility() as IMathUtility;
                var trasformazione = gestore.ActiveSketch.ModelToSketchTransform;
                var punti = (gestore.ActiveSketch.GetSketchPoints2() as object[]) ?? new object[0];
                for (int i = 0; i < posizioni.Count; i++)
                {
                    var atteso = NelloSchizzo(matematica, trasformazione, assi, offsetM, posizioni[i]);
                    var candidati = new List<ISketchPoint>();
                    foreach (var o in punti)
                    {
                        var p = o as ISketchPoint;
                        if (p != null && atteso != null && Math.Abs(p.X - atteso[0]) < 1e-7 && Math.Abs(p.Y - atteso[1]) < 1e-7)
                            candidati.Add(p);
                    }
                    if (candidati.Count == 0)
                    {
                        motivo = "Non ritrovo nello schizzo il punto del foro " + (i + 1) + ".";
                        return false;
                    }
                    // N23: MORE THAN ONE SKETCH POINT CAN ANSWER TO THE SAME
                    // SEARCH WINDOW (1e-7 m above — loose on purpose, since
                    // NelloSchizzo's own transform carries some numerical
                    // slack). Up to library 0.146.0 the loop just kept the LAST
                    // one it found: a guess. SameHolePoint proves the candidates
                    // are the same point by the sketch point's own ID, not by
                    // how close they are: two points at the same coordinates are
                    // still two points (two coincident holes in a Hole Wizard
                    // sketch). When it cannot prove it, the call is refused and
                    // names every candidate, in millimetres, with its ID.
                    var candidateXY = new List<double[]>();
                    var candidateIds = new List<long[]>();
                    foreach (ISketchPoint c in candidati)
                    {
                        candidateXY.Add(new[] { c.X, c.Y });
                        candidateIds.Add(SketchPointId(c));
                    }
                    string ambiguity;
                    if (!SameHolePoint(candidateXY.ToArray(), candidateIds.ToArray(), out ambiguity))
                    {
                        motivo = "Hole " + (i + 1) + ": " + ambiguity;
                        return false;
                    }
                    var suo = candidati[0];
                    var suaMisura = new Dictionary<string, object>();
                    if (!ApplicaAncora(modello, ancore[i], suo, assi, offsetM, posizioni[i][0], posizioni[i][1],
                                       suaMisura, out motivo, pretendiDefinito: false))
                    {
                        motivo = "Foro " + (i + 1) + ": " + motivo;
                        return false;
                    }
                    dette.Add(suaMisura.ContainsKey("anchor") ? suaMisura["anchor"] : null);
                }
                if (!SchizzoDefinitoESano(gestore, out motivo)) return false;
                misure["anchors"] = dette.ToArray();
                misure["hole_sketch"] = "fully defined";
                return true;
            }
            catch (Exception ex) { motivo = "L'ancora dei fori non e' riuscita: " + ex.Message; return false; }
            finally
            {
                // THE SELECTION IS CLEARED BEFORE THE SKETCH IS CLOSED (Z-C21.8,
                // finding N56): InsertSketch(true) toggles the sketch, and with no
                // sketch open it starts a new one on the selected plane or face.
                // The anchoring leaves points and edges selected behind it; with
                // the selection empty, the toggle can only close.
                try { modello.ClearSelection2(true); } catch { }
                if (inModifica) { try { gestore.InsertSketch(true); } catch { } }
                try { modello.ClearSelection2(true); } catch { }
            }
        }

        /// <summary>
        /// N23: are the sketch points AncoraIFori found within its search
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
        /// check skipped centred axes (`!asse.Centrata`), and LeggiAncora runs
        /// with mezzo = 0 for hole_wizard, so its rule «the profile fits
        /// between the two edges» (the «non ci sta» refusal in LeggiAncora)
        /// never fired: an M8 centred between two edges 6 mm apart was
        /// confirmed, and the hole cut through both of them. The same rule is
        /// applied here, half the gap against the radius, with the same `Coord`
        /// reading of the two edges; equal is allowed, as for a distance.
        /// <paramref name="raggioMm"/> is the radius of the widest cut of the
        /// hole (counterbore or countersink included, see ForoGuidato), and
        /// <paramref name="widestCut"/> names it in the message.
        /// </summary>
        private static string ScavalcaColRaggio(List<Ancoraggio> ancore, double[] assi, double raggioMm, string widestCut)
        {
            for (int i = 0; i < ancore.Count; i++)
                foreach (var asse in ancore[i].Assi())
                {
                    if (asse.Centrata)
                    {
                        double gap = Math.Abs(Coord(asse.Fra2.InizioMm, assi, asse.Indice) -
                                              Coord(asse.Fra1.InizioMm, assi, asse.Indice));
                        if (gap / 2 + 1e-9 < raggioMm)
                            return string.Format(CultureInfo.InvariantCulture,
                                "Hole {0}: between «{1}» and «{2}» there are {3:0.##} mm along «{4}», and the {5} is " +
                                "{6:0.##} mm wide: it does not fit, and it would cut through both edges.",
                                i + 1, asse.Fra1.Uid, asse.Fra2.Uid, gap, asse.Nome, widestCut, 2 * raggioMm);
                    }
                    else if (asse.DistanzaMm + 1e-9 < raggioMm)
                        return string.Format(CultureInfo.InvariantCulture,
                            "Hole {0}: «distance_mm» {1:0.##} from edge «{2}» is less than the radius of the {3} " +
                            "({4:0.##} mm): the {3} would cut past the edge.",
                            i + 1, asse.DistanzaMm, asse.Spigolo.Uid, widestCut, raggioMm);
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
        private string ProvaDiMovimento(IModelDoc2 modello, Ancoraggio anc, IFeature taglio)
        {
            if (anc == null || anc.Vuoto) return null;
            foreach (var asse in anc.Assi())
            {
                // A CENTRED AXIS HAS NO DIMENSION TO MOVE (Z-C21.2, finding
                // N51): the sgATMIDDLE relation on the centre line holds it, and
                // skipping it is the design. A DISTANCE axis without its
                // dimension, or whose dimension cannot be read, is a different
                // thing: both went through a `continue`, and the test «passed»
                // having moved nothing, the pattern that M12 closed below for
                // the return of SetSystemValue3. Now they stop the test.
                if (asse.Centrata) continue;
                var q = asse.QuotaNata;
                if (q == null)
                    return MovementTestNotVerifiable(asse, "the anchor dimension was not kept when it was created");
                double era;
                try { era = ApiHelpers.ReadDimensionInConfiguration(q, ""); }
                catch (Exception ex)
                {
                    return MovementTestNotVerifiable(asse, "I cannot read the anchor dimension: " + ex.Message);
                }
                string rotta = null;
                string ferma = null;
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
                    int esitoMossa = q.SetSystemValue3(era + 0.0001, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
                    if (esitoMossa != (int)swSetValueReturnStatus_e.swSetValue_Successful)
                        ferma = "SOLIDWORKS ha risposto " + esitoMossa.ToString(CultureInfo.InvariantCulture) +
                                " a SetSystemValue3 (" + ErrorCodeDecoder.SetValueStatus(esitoMossa) + ")";
                    else
                    {
                        modello.EditRebuild3();
                        double mossa = era;
                        try { mossa = ApiHelpers.ReadDimensionInConfiguration(q, ""); } catch { }
                        if (Math.Abs(mossa - (era + 0.0001)) > 1e-9)
                            ferma = "rilette, la quota non si e' spostata";
                        else
                            rotta = LavorazioneInErrore(taglio, out notVerified);
                    }
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
                if (ferma != null)
                    return "Non sono riuscito a spostare la quota d'ancora «" + asse.Nome + "» di un decimo (" + ferma +
                           "): la prova di movimento non ha provato niente, e un'ancora che non so far muovere " +
                           "non te la confermo.";
                if (notVerified != null)
                    return MovementTestNotVerifiable(asse, "the anchor dimension moved, but " + notVerified);
                if (rotta != null)
                    return "Spostando la quota d'ancora «" + asse.Nome + "» di un decimo, il taglio va in errore (" + rotta +
                           "): lo schizzo non regge una modifica, e un'ancora che non regge una modifica non serve.";
                double tornata = -1;
                try { tornata = ApiHelpers.ReadDimensionInConfiguration(q, ""); } catch { }
                if (Math.Abs(tornata - era) > 1e-9)
                    return "Dopo la prova di movimento la quota d'ancora «" + asse.Nome + "» non e' tornata al suo valore.";
            }
            return null;
        }

        /// <summary>
        /// The movement test that could not test anything, in words: an anchor
        /// that cannot be tested is not confirmed (Z-C21.2, finding N51).
        /// </summary>
        private static string MovementTestNotVerifiable(AncoraAsse asse, string why)
        {
            return "The movement test of the anchor on «" + asse.Nome + "» is not verifiable (" + why +
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
        private static string LavorazioneInErrore(IFeature taglio, out string notVerified)
        {
            notVerified = null;
            if (taglio == null)
            {
                notVerified = "there is no feature to watch";
                return null;
            }
            try
            {
                // S11: ApiHelpers.RebuildError (GetErrorCode2, warnings excluded),
                // which also says the code in words (ErroriFeature), as the summary promises.
                bool known;
                var cutError = ApiHelpers.RebuildError(taglio, out known);
                if (!known)
                {
                    notVerified = "GetErrorCode2 did not answer on the feature";
                    return null;
                }
                if (cutError != null) return cutError;
                var schizzo = taglio.GetFirstSubFeature() as IFeature;
                if (schizzo == null) return null;
                var sketchError = ApiHelpers.RebuildError(schizzo, out known);
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
                return " L'ho tolto, con il suo schizzo, ma non ho potuto ricontare " +
                       "l'albero per controllare che il pezzo sia com'era: guardalo.";
            if (lavorazioni > _featureAllInizioDelPasso)
                return RemovedButTreeLonger(lavorazioni - _featureAllInizioDelPasso);
            double? volumeDopo = VolumeMm3(modello);
            if (volumePrima == null || volumeDopo == null)
                return " L'ho tolto, con il suo schizzo, ma non ho potuto rimisurare il " +
                       "volume per controllare che il pezzo sia com'era: guardalo.";
            if (Math.Abs(volumeDopo.GetValueOrDefault() - volumePrima.GetValueOrDefault()) > 0.01)
                return RemovedButVolumeChanged(volumeDopo.GetValueOrDefault(), volumePrima.GetValueOrDefault());
            return " L'ho tolto, con il suo schizzo: il pezzo e' com'era prima di questo passo.";
        }

        /// <summary>
        /// The tail of a refusal when DeleteSelection2 said it removed what the
        /// step had made, and the tree read back is still LONGER than before the
        /// step (Z-C21.9, finding C9-6). Something of this step is still there,
        /// but not necessarily the feature that was removed: until library
        /// 0.144.0 the three removal helpers (TogliIlTaglio, TogliIlRiempimento,
        /// TogliIlFazzoletto) answered «it is still in the part: undo it» about
        /// a feature that was gone. The same words as the face fillet
        /// (TogliIlRaccordoDiFaccia): the features in excess, counted.
        /// </summary>
        internal static string RemovedButTreeLonger(int extraFeatures)
        {
            return " I tried to remove it, but the tree still has " +
                   extraFeatures.ToString(CultureInfo.InvariantCulture) +
                   (extraFeatures == 1 ? " feature" : " features") + " more than before this step: undo " +
                   (extraFeatures == 1 ? "it" : "them") + " (" + Frasi.CosiSiRitira + ").";
        }

        /// <summary>
        /// The tail of a refusal when DeleteSelection2 removed what the step had
        /// made and the tree is back as it was, but the volume read back is not
        /// the one before the step (Z-C21.9, finding C9-6). The removed feature
        /// is gone, so the sentence says so and gives the new volume, as the
        /// face fillet does (TogliIlRaccordoDiFaccia), instead of «it is still in
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
                return ApiHelpers.ReadDimensionInConfiguration(nata, "") * 1000.0;
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
            // THE FIVE ARE COUNTED HERE, BEFORE THE CENTRE LINE (Z-C21.2,
            // finding N54). IModelDoc2~SketchAddConstraints.html gives it no
            // return value (C# «void SketchAddConstraints»), so the relation
            // count is the only proof that a tangency was born. It was taken
            // after the two coincidences below, which are requested exactly for
            // the case in which the centre line does NOT fuse with the centres:
            // then they add two relations, and two missing tangencies passed as
            // «5 of 5».
            int relStadio = RelazioniDelloSchizzo(schizzi);
            if (relPrima < 0 || relStadio < 0)
                misure["slot_relations"] = "not counted: SOLIDWORKS did not give the relation count of the " +
                                           "sketch, so I could not check that the four tangencies and the " +
                                           "equal radii were born";
            else if (relStadio - relPrima < 5)
            {
                errore = "Not all the relations of the slot were born (" +
                         (relStadio - relPrima).ToString(CultureInfo.InvariantCulture) +
                         " of 5: four tangencies and the equal radii).";
                // Without an anchor the caller does not stop on `error` (a slot
                // that is only less constrained is still cut): the response says it.
                misure["slot_relations"] = (relStadio - relPrima).ToString(CultureInfo.InvariantCulture) +
                                           " of 5 born (four tangencies and the equal radii): after a change the " +
                                           "slot may lose its shape";
            }

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
            // The points go in with `as` (Z-C21.8, finding N49): a null point
            // makes `Relazione` answer false without an exception, and these two
            // relations are optional by design, so their answer is not needed.
            Relazione(modello, linea.GetStartPoint2() as ISketchPoint, arcoA.GetCenterPoint2() as ISketchPoint, "sgCOINCIDENT");
            Relazione(modello, linea.GetEndPoint2() as ISketchPoint, arcoB.GetCenterPoint2() as ISketchPoint, "sgCOINCIDENT");

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
                // The angle the dimension must read: between the slot line and
                // the reference edge, both on the face (Z-C21.2, finding N55).
                // It is not `angoloGradi` itself: against an edge that runs
                // along y (the edge of an «x» anchor) a slot at 30 degrees reads
                // 60, or its supplement.
                double edgeDeg = Math.Atan2(
                    Coord(riferimento.FineMm, assi, 1) - Coord(riferimento.InizioMm, assi, 1),
                    Coord(riferimento.FineMm, assi, 0) - Coord(riferimento.InizioMm, assi, 0)) * 180.0 / Math.PI;
                string angleNotKept = null;
                if (testoA == null ||
                    !QuotaAngolo(modello, mezzeria, riferimento.Spigolo, testoA,
                                 AngleBetweenLinesDeg(angoloGradi, edgeDeg), out angleNotKept))
                {
                    if (errore == null)
                        errore = "The angle of the slot against edge «" + riferimento.Uid + "» " +
                                 (angleNotKept ?? "was not born.");
                }
                else quote++;
            }
            else
                misure["orientation"] = "libero: un'asola obliqua senza ancora non ha uno spigolo da cui " +
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
        /// Il centro del contorno di `extrude_on_face` a cui si attacca l'ancora
        /// (voce E18), e il mezzo ingombro per asse. Vale per UN contorno solo:
        /// un cerchio, o una spezzata di quattro vertici allineata agli assi
        /// della faccia (un rettangolo). Per tutto il resto un rifiuto che dice
        /// perche': quale sia «il centro» di un poligono qualunque lo decide chi
        /// disegna, e indovinarlo vorrebbe dire ancorare un punto che non ha
        /// scelto nessuno.
        /// </summary>
        private static bool CentroDelContornoSolo(
            List<Contorno> contorni, out double cx, out double cy, out double mezzoU, out double mezzoV,
            out string motivo)
        {
            cx = cy = mezzoU = mezzoV = 0;
            motivo = null;
            const string Regola =
                "L'ancora di un rialzo vale per UN contorno solo, tondo o rettangolare allineato " +
                "agli assi della faccia: il punto che tiene e' il suo centro. ";
            int quanti = contorni == null ? -1 : contorni.Count;
            if (quanti != 1)
            {
                motivo = Regola + (quanti < 0 ? "Qui non ci sono contorni." : "Qui i contorni sono " + quanti +
                         ": fai un rialzo per contorno, e ancora ciascuno.");
                return false;
            }
            var c = contorni[0];
            if (c.Cerchio)
            {
                cx = c.Centro[0]; cy = c.Centro[1];
                mezzoU = mezzoV = c.Diametro / 2.0;
                return true;
            }
            var punti = new List<double[]>();
            if (c.Tratti.Count == 1 && !c.Tratti[0].Spline)
                foreach (var p in c.Tratti[0].Punti)
                    if (!punti.Any(q => Math.Abs(q[0] - p[0]) < 1e-9 && Math.Abs(q[1] - p[1]) < 1e-9))
                        punti.Add(p);
            bool rettangolo = punti.Count == 4;
            for (int i = 0; rettangolo && i < 4; i++)
            {
                var p = punti[i]; var q = punti[(i + 1) % 4];
                bool orizzontale = Math.Abs(p[1] - q[1]) < 1e-9, verticale = Math.Abs(p[0] - q[0]) < 1e-9;
                if (orizzontale == verticale) rettangolo = false;
            }
            if (!rettangolo)
            {
                motivo = Regola + "Questo contorno non e' ne' un cerchio ne' un rettangolo allineato.";
                return false;
            }
            double minX = punti.Min(p => p[0]), maxX = punti.Max(p => p[0]);
            double minY = punti.Min(p => p[1]), maxY = punti.Max(p => p[1]);
            cx = (minX + maxX) / 2; cy = (minY + maxY) / 2;
            mezzoU = (maxX - minX) / 2; mezzoV = (maxY - minY) / 2;
            return true;
        }

        /// <summary>
        /// Definisce il contorno del rialzo e ci attacca l'ancora, a schizzo
        /// aperto (voce E18). Cerchio: il diametro, e l'ancora sul centro.
        /// Rettangolo di quattro linee (gli angoli, disegnati sotto AddToDB, sono
        /// gia' fusi — misurato sull'asola): orizzontali e verticali, due quote,
        /// e l'ancora sul centro costruito con la diagonale, come la tasca.
        /// </summary>
        private bool AncoraIlContorno(
            IModelDoc2 modello, Contorno contorno, List<ISketchSegment> creati, double[] assi, double offsetM,
            double cx, double cy, Ancoraggio ancora, Dictionary<string, object> misure, out string errore)
        {
            errore = null;
            ISketchPoint punto;
            if (contorno.Cerchio)
            {
                var cerchio = creati.Count == 1 ? creati[0] as ISketchArc : null;
                if (cerchio == null) { errore = "Il cerchio del rialzo non e' nato come ci si aspetta."; return false; }
                var testo = PuntoNelloSchizzo(modello, assi, offsetM,
                    cx + contorno.Diametro * 0.7 + 5, cy + contorno.Diametro * 0.7 + 5, out errore);
                if (testo == null || QuotaSegmenti(modello, new List<Quota> { Quota.Su(creati[0], testo) }) < 1)
                {
                    errore = errore ?? "Il diametro del rialzo non si e' lasciato quotare.";
                    return false;
                }
                punto = cerchio.GetCenterPoint2() as ISketchPoint;
            }
            else
            {
                if (creati.Count != 4 || creati.Any(s => !(s is ISketchLine)))
                {
                    errore = "Il rettangolo del rialzo non ha i suoi quattro lati.";
                    return false;
                }
                // THE ENDS ARE CHECKED (Z-C21.8, finding N49): ISketchLine~
                // GetStartPoint2.html, «Start sketch point or NULL if operation
                // fails». A null end reached .X and gave the bare .NET message;
                // the twin in CentroDelRettangolo already skipped nulls.
                const string noEnd =
                    "A side of the rectangle of the boss gives no end point (GetStartPoint2 / GetEndPoint2 answered " +
                    "null): I cannot anchor the rectangle.";
                var angoli = new List<double[]>();
                foreach (var s in creati)
                {
                    var l = (ISketchLine)s;
                    var a0 = l.GetStartPoint2() as ISketchPoint;
                    var a1 = l.GetEndPoint2() as ISketchPoint;
                    if (a0 == null || a1 == null) { errore = noEnd; return false; }
                    bool orizzontale = Math.Abs(a0.Y - a1.Y) < 1e-9;
                    RelazioneSola(modello, s, orizzontale ? "sgHORIZONTAL2D" : "sgVERTICAL2D");
                    angoli.Add(new[] { a0.X, a0.Y, a0.Z });
                }
                // Le due quote: un lato per verso, col testo fuori dal rettangolo.
                var p0 = ((ISketchLine)creati[0]).GetStartPoint2() as ISketchPoint;
                var p1 = ((ISketchLine)creati[0]).GetEndPoint2() as ISketchPoint;
                var q1 = ((ISketchLine)creati[1]).GetEndPoint2() as ISketchPoint;
                if (p0 == null || p1 == null || q1 == null) { errore = noEnd; return false; }
                var daQuotare = new List<Quota>
                {
                    Quota.Su(creati[0], new[] { (p0.X + p1.X) / 2 + (p1.X - q1.X) * 0.2, (p0.Y + p1.Y) / 2 + (p1.Y - q1.Y) * 0.2, 0.0 }),
                    Quota.Su(creati[1], new[] { (p1.X + q1.X) / 2 + (p1.X - p0.X) * 0.2, (p1.Y + q1.Y) / 2 + (p1.Y - p0.Y) * 0.2, 0.0 }),
                };
                if (QuotaSegmenti(modello, daQuotare) < 2)
                {
                    errore = "Le quote del rettangolo del rialzo non sono nate tutte.";
                    return false;
                }
                punto = CentroDelRettangolo(modello, creati.Cast<object>().ToArray(), angoli[0], angoli[2]);
                if (punto == null)
                {
                    errore = "Non sono riuscito a costruire il centro del rettangolo a cui attaccare l'ancora.";
                    return false;
                }
            }
            return ApplicaAncora(modello, ancora, punto, assi, offsetM, cx, cy, misure, out errore);
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
        /// where its text sits, like QuotaPuntoSpigolo refuses a dimension that
        /// measures something else. False with <paramref name="whyNot"/> null
        /// means «not born» (the caller's message); not null says what is wrong.
        /// </summary>
        private bool QuotaAngolo(IModelDoc2 modello, ISketchSegment linea, IEdge spigolo, double[] testo,
                                 double betweenDeg, out string whyNot)
        {
            whyNot = null;
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
                if (errore != (int)swAddSpecificDimension_e.swAddSpecificDimension_Success)
                {
                    whyNot = "was born, but AddSpecificDimension answered " + (swAddSpecificDimension_e)errore +
                             ": I do not keep a dimension of the wrong type.";
                    return false;
                }
                double readDeg;
                try { readDeg = Unita.Gradi(ApiHelpers.ReadDimensionInConfiguration(quota.GetDimension2(0), "")); }
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

        /// <summary>
        /// The dangling, over-defining and broken relations of the open sketch,
        /// in words in <paramref name="malate"/> (null if there are none).
        ///
        /// «NONE» AND «NOT READ» ARE TWO ANSWERS (Z-C21.2, finding N52). The
        /// function returned null both for a sketch without sick relations and
        /// when the RelationManager did not come or GetRelationsCount threw,
        /// and SchizzoDefinitoESano read null as healthy: the check said yes
        /// without having looked. ISketchRelationManager~GetRelationsCount.html:
        /// «Return Value Number of sketch relations»; a count that does not come,
        /// or comes negative, is not a count. Now the function returns false,
        /// with the reason in <paramref name="notRead"/>, and the caller refuses.
        /// </summary>
        private static bool RelazioniMalate(ISketchManager schizzi, out string malate, out string notRead)
        {
            malate = null;
            notRead = null;
            try
            {
                var gestore = schizzi.ActiveSketch?.RelationManager as ISketchRelationManager;
                if (gestore == null)
                {
                    notRead = "SOLIDWORKS does not give the relation manager of the open sketch";
                    return false;
                }
                var dette = new List<string>();
                int n = gestore.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling);
                if (n > 0) dette.Add(n + " penzolant" + (n == 1 ? "e" : "i"));
                int dangling = n;
                n = gestore.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining);
                if (n > 0) dette.Add(n + " sovravincolant" + (n == 1 ? "e" : "i"));
                int overDefining = n;
                n = gestore.GetRelationsCount((int)swSketchRelationFilterType_e.swBroken);
                if (n > 0) dette.Add(n + " rott" + (n == 1 ? "a" : "e"));
                if (dangling < 0 || overDefining < 0 || n < 0)
                {
                    notRead = string.Format(CultureInfo.InvariantCulture,
                        "GetRelationsCount answered {0} dangling, {1} over-defining, {2} broken", dangling, overDefining, n);
                    return false;
                }
                malate = dette.Count == 0 ? null : string.Join(", ", dette);
                return true;
            }
            catch (Exception ex)
            {
                notRead = "counting them failed: " + ex.Message;
                return false;
            }
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
                foreach (var p in new[] { l.GetStartPoint2() as ISketchPoint, l.GetEndPoint2() as ISketchPoint })
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
            // `as` and a null check (Z-C21.8, finding N49); null is «nothing
            // born», which the caller already refuses in words.
            var diagonalStart = dl.GetStartPoint2() as ISketchPoint;
            var diagonalEnd = dl.GetEndPoint2() as ISketchPoint;
            if (diagonalStart == null || diagonalEnd == null) return null;
            if (!Relazione(modello, diagonalStart, a1, "sgCOINCIDENT")) return null;
            if (!Relazione(modello, diagonalEnd, a2, "sgCOINCIDENT")) return null;
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
