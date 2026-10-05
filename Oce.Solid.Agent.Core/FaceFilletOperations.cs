using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// IL RACCORDO DI FACCIA (voce GP7, la meta' che mancava).
    ///
    /// Un raccordo che non si attacca a uno SPIGOLO ma a DUE FACCE: la palla di
    /// raggio r rotola appoggiata a tutte e due, e la superficie che lascia e' il
    /// raccordo. Sullo spigolo di un cubo da' lo stesso pezzo di
    /// «fillet_edges»; la differenza e' che non serve uno spigolo da
    /// nominare — bastano le due facce, anche quando lo spigolo fra loro non e'
    /// uno solo o cambia a ogni lavorazione.
    ///
    /// ── LA GUIDA PRIMA (metodo della voce, 28/9/2026) ────────────────────
    ///
    /// Pagine della guida API (sldworksapi, estratta in locale), per nome:
    ///
    ///  * `IFeatureManager~FeatureFillet3.html`, Remarks: «As of SOLIDWORKS 2020,
    ///    this method is obsolete for creating: constant radius fillets, offset
    ///    face chamfers, face fillets, face-face chamfers, full round fillets».
    ///    La via vecchia, quella di `fillet_edges` e `variable_fillet`, qui
    ///    non si usa: e' la prima delle due cause dei quattro NULL storici.
    ///  * `ISimpleFilletFeatureData2.html`, Remarks, ricetta: `CreateDefinition`
    ///    con `swFmFillet`; `Initialize` con `swSimpleFilletType_e.swFaceFillet`;
    ///    riga «Face fillet/face-face chamfer»: «Call IModelDocExtension::SelectByID2
    ///    with: Mark = 2 to select Face Set 1. Mark = 4 to select Face Set 2»; per
    ///    il simmetrico `DefaultRadius`; poi `IFeatureManager::CreateFeature`.
    ///    L'esempio VBA della stessa pagina seleziona «adjacent faces using Marks
    ///    2 and 4»: due facce adiacenti di un cubo, come qui.
    ///  * `ISimpleFilletFeatureData2~Initialize.html`, Remarks: dopo `Initialize`
    ///    si stringe il tipo con `ConicTypeForCrossSectionProfile`.
    ///  * `ISimpleFilletFeatureData2~SetFaces.html`: `WhichFaceList` e' «Face as
    ///    defined in swSimpleFilletWhichFaces_e». E' la SECONDA causa dei NULL
    ///    storici: le marche di selezione erano state prese da quell'enum
    ///    (Set1 = 1, Set2 = 2), che e' il parametro di `SetFaces` e non una marca.
    ///    L'enum col nome giusto e' quello sbagliato, di nuovo (vedi GP8).
    ///
    /// ── MISURATO PRIMA DI SCRIVERE (sonda Checks\FaceFillet, 28/9/2026) ──
    ///
    /// Cubo 40x40x40, faccia +Z (marca 2) e faccia +X (marca 4), r = 5. Un
    /// raccordo r su uno spigolo di 40 fra due facce a 90 gradi toglie
    /// 40·5²·(1 − π/4) = 214,60 mm³:
    ///
    ///   FeatureFillet3 + Ftyp=Face + marche 1/2 (il vecchio)     NULL, tolti 0
    ///   ricetta della guida (marche 2/4, profilo circolare)      «Raccordo1» (Fillet), tolti 214,6
    ///   ricetta della guida con le marche 1/2                    NULL, tolti 0
    ///   ricetta senza ConicTypeForCrossSectionProfile            «Raccordo1» (Fillet), tolti 214,6
    ///   SetFaces con Face2[] tipizzato, senza selezione          «Raccordo1» (Fillet), tolti 214,6
    ///   SelectByID2 per coordinate, marche 2/4                   «Raccordo1» (Fillet), tolti 214,6
    ///
    /// Si usa la seconda riga, parola per parola: selezione con marca 2 e 4 su
    /// facce vere (IEntity.Select4), profilo dichiarato come la guida chiede.
    /// Corpi 1 → 1 in tutte.
    ///
    /// ── COSA SI MISURA, E QUANDO CI SI RIFIUTA ──────────────────────────
    ///
    /// Prima: le due facce devono essere due, e se sono tutte e due piane e
    /// PARALLELE nessuna palla le tocca tutte e due dal lato dell'aria (salvo il
    /// caso di due pareti che si guardano a distanza 2r esatta) — si dice senza
    /// chiamare niente. Dopo: la feature nuova e il suo tipo (`Fillet`), il
    /// volume prima e dopo (un raccordo che non cambia il volume non ha fatto
    /// niente), i corpi e le lavorazioni in errore (le guardie di
    /// GuardOperations.cs). Se qualcosa non torna il raccordo si TOGLIE
    /// (DeleteSelection2, mai la chiamata con la finestra di conferma) e si
    /// ricontrolla che il pezzo sia com'era.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>Due normali con |coseno| oltre questo sono parallele.</summary>
        private const double FaceFilletAlmostParallel = 0.9999;

        private Outcome FaceFillet(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("Il raccordo di faccia si fa su un pezzo: apri il pezzo e chiedimelo li'.");

            // DUE FACCE, E «face_ref» NE NOMINA UNA SOLA. `ResolveFace`
            // guarda «face_ref» prima dell'uid: con due facce da risolvere lo
            // stesso riferimento vincerebbe tutte e due le volte, e il raccordo
            // si chiederebbe fra una faccia e se stessa.
            if (HasReference(a, "face_ref"))
                return Failed(
                    "Qui servono due facce diverse, e «face_ref» ne nomina una sola: usa " +
                    "«face_1_uid» e «face_2_uid» con gli identificativi dello stato. Se una " +
                    "delle due la crea un passo di questo piano, rileggi lo stato dopo quel passo.");

            double radius;
            string error;
            if (!RequiredNum(a, "radius_mm", out radius, out error)) return Failed(error);
            if (radius <= 0)
                return Failed(
                    "«radius_mm» deve essere maggiore di zero: hai chiesto " +
                    radius.ToString("0.##", CultureInfo.InvariantCulture) + ". Per esempio 5.");

            string uid1 = (Str(a, "face_1_uid", "") ?? "").Trim();
            string uid2 = (Str(a, "face_2_uid", "") ?? "").Trim();
            if (uid1.Length == 0 || uid2.Length == 0)
                return Failed(
                    "Dimmi le due facce: «face_1_uid» e «face_2_uid», con gli identificativi " +
                    "dello stato del documento (per esempio «F1» e «F3»). Il raccordo si appoggia " +
                    "a tutte e due.");

            // LA STESSA FACCIA DUE VOLTE. Si dice prima di chiamare: e' la
            // confusione di chi voleva arrotondare i bordi di UNA faccia, e per
            // quello c'e' un'altra operazione.
            if (string.Equals(uid1, uid2, StringComparison.OrdinalIgnoreCase))
                return Failed(
                    "«" + uid1 + "» e' in tutti e due gli insiemi: un raccordo di faccia unisce DUE " +
                    "facce diverse, appoggiandosi a tutte e due. Per arrotondare i bordi di una " +
                    "faccia sola usa «fillet_edges» sui suoi spigoli.");

            string reason;
            var face1 = ResolveFace(docState, a, uid1, out reason);
            if (face1 == null) return Failed("Faccia «" + uid1 + "»: " + reason);
            var face2 = ResolveFace(docState, a, uid2, out reason);
            if (face2 == null) return Failed("Faccia «" + uid2 + "»: " + reason);

            // DUE UID, UNA FACCIA. Dopo un ritrovamento per firma due nomi
            // diversi possono essere finiti sulla stessa faccia: lo si chiede a
            // SOLIDWORKS (ISldWorks~IsSame.html, «Gets whether the two specified
            // objects are the same object»), come in OperazioniCorpi.
            bool same = ReferenceEquals(face1, face2);
            if (!same)
            {
                try { same = _sw.IsSame(face1, face2) == (int)swObjectEquality.swObjectSame; }
                catch { }
            }
            if (same)
                return Failed(
                    "«" + uid1 + "» e «" + uid2 + "» adesso sono la stessa faccia: una delle due si e' " +
                    "spostata ed e' stata ritrovata sull'altra. Rileggi lo stato e indica di nuovo " +
                    "le due facce.");

            // ── DUE FACCE PIANE PARALLELE ────────────────────────────────
            //
            // Nessuna chiamata a SOLIDWORKS, solo geometria: la palla del
            // raccordo deve toccare tutte e due le facce dal lato dell'aria.
            //  * stessa normale (complanari, o un gradino): impossibile;
            //  * normali opposte e le facce si danno le spalle (davanti e dietro
            //    di un blocco): impossibile;
            //  * normali opposte e si guardano (le pareti di una cava) a distanza
            //    d: solo con r = d/2. Con r = d/2 si lascia provare a SOLIDWORKS,
            //    e le guardie dopo dicono se e' nato.
            // La sonda ha tolto dal giro la variante «facce parallele»
            // (Checks\FaceFillet, REVISIONE 28/9/2026) proprio perche' un
            // NULL li' non avrebbe misurato l'API ma la geometria.
            string parallel = ParallelPlanarFaces(face1, face2, uid1, uid2, radius);
            if (parallel != null) return Failed(parallel);

            // ── IL CONTO A MANO, LETTO PRIMA DI TOCCARE IL PEZZO ─────────
            //
            // Dove e' un conto chiuso: due facce piane a 90 gradi che si
            // toccano su uno spigolo lungo L. La sezione tolta (o aggiunta, in
            // un angolo interno) e' r²(1 − π/4), e per lunghezza L. Altrove
            // (facce curve, angoli diversi) non si scrive un numero inventato:
            // null, e detto.
            //
            // PRIMA e non dopo: dopo il raccordo le due facce NON si toccano
            // piu' (fra loro c'e' la faccia del raccordo), e lo spigolo comune
            // letto li' sarebbe zero — il conto non ci sarebbe mai stato.
            double edge = -1;
            try { edge = LengthThatLiesOn(face1, face2); } catch { }
            double? expected = null;
            var n1 = FaceNormal(face1);
            var n2 = FaceNormal(face2);
            if (edge > 0 && n1 != null && n2 != null && n1.Length >= 3 && n2.Length >= 3 &&
                Math.Abs(n1[0] * n2[0] + n1[1] * n2[1] + n1[2] * n2[2]) < 1e-6)
                expected = Math.Round(edge * radius * radius * (1 - Math.PI / 4), 2);

            // ── LA FOTOGRAFIA PRIMA (GuardOperations.cs) ───────────────
            var photo = SnapshotPart(model);
            if (photo.Volume == null) return Failed(VolumeNotReadBefore("fare il raccordo di faccia"));
            int featureBefore = CountFeature(model);
            if (featureBefore < 0 || photo.Bodies < 0)
                return Failed(VolumeNotReadBefore("fare il raccordo di faccia")
                    .Replace("il volume del pezzo", "l'albero e i corpi del pezzo"));

            // ── LA RICETTA DELLA GUIDA, NELL'ORDINE MISURATO ─────────────
            IFeature feature;
            try
            {
                model.ClearSelection2(true);
                var data = model.FeatureManager.CreateDefinition(
                    (int)swFeatureNameID_e.swFmFillet) as SimpleFilletFeatureData2;
                if (data == null)
                    return Failed(
                        "SOLIDWORKS non mi ha dato la definizione di un raccordo (CreateDefinition). " +
                        "Non ho toccato niente: prova «fillet_edges» sullo spigolo fra le due facce.");
                if (!data.Initialize((int)swSimpleFilletType_e.swFaceFillet))
                    return Failed(
                        "SOLIDWORKS non accetta un raccordo di faccia su questa definizione " +
                        "(Initialize ha risposto false). Non ho toccato niente: prova «fillet_edges» " +
                        "sullo spigolo fra le due facce.");

                var handler = model.SelectionManager as ISelectionMgr;
                if (handler == null)
                    return Failed("Non riesco a usare il gestore di selezione. Non ho toccato niente.");
                // INSIEME 1 MARCA 2, INSIEME 2 MARCA 4 (vedi le costanti).
                if (!Select(handler, face1, SelectionMarks.FaceFilletFaceSet1))
                    return Failed("Non sono riuscito a selezionare la faccia «" + uid1 + "». Non ho toccato " +
                                   "niente: rileggi lo stato e indica di nuovo le due facce.");
                if (!Select(handler, face2, SelectionMarks.FaceFilletFaceSet2))
                    return Failed("Non sono riuscito a selezionare la faccia «" + uid2 + "». Non ho toccato " +
                                   "niente: rileggi lo stato e indica di nuovo le due facce.");

                // Il profilo circolare: la guida lo chiede per «completare il tipo»
                // (misurato: senza, nasce lo stesso raccordo; si mette perche' la
                // ricetta lo dice e non costa niente).
                data.ConicTypeForCrossSectionProfile =
                    (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
                data.DefaultRadius = Units.M(radius);

                feature = model.FeatureManager.CreateFeature(data) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("Il raccordo di faccia non e' riuscito: " + ex.Message +
                               ". Prova «fillet_edges» sullo spigolo fra le due facce.");
            }
            finally { try { model.ClearSelection2(true); } catch { } }

            if (feature == null)
            {
                int now = CountFeature(model);
                string queue = now > featureBefore
                    ? " Eppure l'albero e' cresciuto (" + featureBefore + " -> " + now + "): guardalo e " +
                      "ritira quello che c'e' in piu' — " + Sentences.HowToWithdrawIt + "."
                    : " Non ho toccato niente.";
                return Failed(
                    "Il raccordo di faccia non e' stato creato, e SOLIDWORKS non dice perche'. Di " +
                    "solito e' il raggio: la palla deve appoggiarsi a tutte e due le facce senza " +
                    "uscirne. Prova un raggio piu' piccolo; se le due facce si toccano su uno " +
                    "spigolo, lo stesso raccordo si fa con «fillet_edges» su quello spigolo." + queue);
            }

            // ── E ADESSO SI MISURA IL PEZZO ──────────────────────────────
            //
            // ForceRebuild3 e non la ricostruzione semplice: e' quella di
            // `edit_extrusion`, la cui risposta `WhatBroke` sa gia'
            // leggere su un pezzo sano (caso blind_becomes_through).
            bool rebuilt = false;
            try { rebuilt = model.ForceRebuild3(false); } catch { }

            string itemName = "";
            string typ = "";
            try { itemName = feature.Name; } catch { }
            try { typ = feature.GetTypeName2(); } catch { }

            // LA FEATURE NUOVA PER TIPO: la sonda l'ha vista nascere «Fillet».
            if (!string.Equals(typ, "Fillet", StringComparison.Ordinal))
                return Failed(
                    "E' nata una lavorazione «" + itemName + "» di tipo «" + typ + "», non un raccordo " +
                    "(«Fillet»): non e' quello che ho chiesto." +
                    RemoveFaceFillet(model, feature, photo, featureBefore) +
                    " Prova «fillet_edges» sullo spigolo fra le due facce.");

            string broken = WhatBroke(model, photo, rebuilt, false);
            if (broken != null)
                return Failed(
                    "Il raccordo di faccia «" + itemName + "» e' nato, ma " + broken + "." +
                    RemoveFaceFillet(model, feature, photo, featureBefore) +
                    " Prova un raggio piu' piccolo, oppure «fillet_edges» sullo spigolo fra le due facce.");

            double? volumeAfter = VolumeMm3(model);
            double? cambio = volumeAfter - photo.Volume;

            // UN RACCORDO CHE NON CAMBIA IL VOLUME NON HA FATTO NIENTE. Toglie
            // materiale su uno spigolo esterno, ne aggiunge in un angolo interno:
            // zero vuol dire nessuno dei due. Si dice solo di un volume riletto
            // (voce Z-C8).
            if (cambio.HasValue && Math.Abs(cambio.Value) < 0.01)
                return Failed(
                    "Il raccordo di faccia «" + itemName + "» e' nato ma il volume e' rimasto " +
                    photo.Volume.Value.ToString("0.##", CultureInfo.InvariantCulture) + " mm3: non ha " +
                    "arrotondato niente." + RemoveFaceFillet(model, feature, photo, featureBefore) +
                    " Prova «fillet_edges» sullo spigolo fra le due facce.");

            // IL CONTO A MANO, CONFRONTATO. Dove c'e' (vedi sopra), un raccordo
            // che toglie un'altra quantita' non e' quello chiesto: parziale, o
            // appoggiato altrove. Lo scarto ammesso e' il mezzo decimo, o un
            // decimillesimo del conto se e' piu' grande: il numero misurato
            // dalla sonda (214,6) e il conto (214,602) stanno nel centesimo.
            if (expected.HasValue && cambio.HasValue &&
                Math.Abs(Math.Abs(cambio.Value) - expected.Value) > Math.Max(0.05, expected.Value * 1e-4))
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "Il raccordo di faccia «{0}» e' nato, ma ha cambiato il volume di {1:0.##} mm3 e il " +
                    "conto a mano ne vuole {2:0.##} (spigolo comune {3:0.##} mm x r² x (1 − π/4)): non " +
                    "si e' appoggiato come chiesto sulle due facce.{4} Per lo stesso raccordo sullo " +
                    "spigolo fra le due facce usa «fillet_edges».",
                    itemName, Math.Abs(cambio.Value), expected.Value, edge,
                    RemoveFaceFillet(model, feature, photo, featureBefore)));

            // IL RAGGIO CHE LA FEATURE DICHIARA (ISimpleFilletFeatureData2~
            // DefaultRadius.html: «Default radius of the symmetric fillet»),
            // riletto con IFeature::GetDefinition — non l'argomento ripetuto.
            // Non decide niente: decide il volume. Null se non si rilegge.
            object declaredRadius = null;
            try
            {
                var read = feature.GetDefinition() as ISimpleFilletFeatureData2;
                if (read != null) declaredRadius = Math.Round(Units.Mm(read.DefaultRadius), 3);
            }
            catch { }

            int bodiesAfter = CountSolidBodies(model);
            int inErrorAfter = FeaturesInError(model);

            var measurements = new Dictionary<string, object>
            {
                // I CAMPI NUOVI SONO IN INGLESE (decisione della lingua, W3.1).
                // Niente argomenti ripetuti come fossero misure: il raggio e'
                // quello che la feature DICHIARA, il resto e' letto dal pezzo.
                ["declared_radius_mm"] = declaredRadius,
                ["feature_type"] = typ,
                ["volume_before_mm3"] = NumberOrNull(photo.Volume, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                ["volume_change_mm3"] = NumberOrNull(cambio, 2),
                ["material_removed"] = cambio.HasValue ? (object)(cambio.Value < 0) : null,
                ["bodies"] = NumberOrNull(bodiesAfter),
                ["features_in_error"] = NumberOrNull(inErrorAfter),
                ["shared_edge_mm"] = edge >= 0 ? (object)Math.Round(edge, 2) : null,
                ["expected_change_abs_mm3"] = NumberOrNull(expected, 2),
                ["how_expected_is_counted"] =
                    "shared edge length x r^2 x (1 - pi/4): exact for two flat faces at 90 degrees " +
                    "that touch along an edge (material removed on an outside edge, added in an " +
                    "inside corner), read BEFORE the fillet (afterwards the faces no longer touch) " +
                    "and compared with the volume change. Null when the two faces are not like that: " +
                    "no number is made up.",
            };
            if (!cambio.HasValue) measurements["volume_not_read_again"] = NoteVolumeNotReread;

            _log("[agente] raccordo di faccia r=" + radius.ToString("0.##", CultureInfo.InvariantCulture) +
                 " fra " + uid1 + " e " + uid2 + ": «" + itemName + "», volume " + ReadValue(photo.Volume) +
                 " -> " + ReadValue(volumeAfter));
            return Succeeded(feature, measurements);
        }

        /// <summary>
        /// La frase di rifiuto quando le due facce sono piane e parallele e il
        /// raccordo e' impossibile per geometria; null se si puo' provare (facce
        /// non piane, non parallele, o due pareti che si guardano a distanza 2r).
        /// </summary>
        private static string ParallelPlanarFaces(IFace2 f1, IFace2 f2, string uid1, string uid2, double radiusMm)
        {
            var n1 = FaceNormal(f1);
            var n2 = FaceNormal(f2);
            if (n1 == null || n2 == null || n1.Length < 3 || n2.Length < 3) return null;
            double cosine = n1[0] * n2[0] + n1[1] * n2[1] + n1[2] * n2[2];
            if (Math.Abs(cosine) < FaceFilletAlmostParallel) return null;

            if (cosine > 0)
                return "«" + uid1 + "» e «" + uid2 + "» sono piane, parallele e guardano dalla stessa " +
                       "parte (complanari, o separate da un gradino): nessuna palla le tocca tutte e due " +
                       "dal lato dell'aria, e il raccordo non puo' nascere. Se c'e' un gradino fra loro, " +
                       "usa «fillet_edges» sugli spigoli del gradino.";

            // Normali opposte: si guardano o si danno le spalle? Il punto del
            // piano della seconda, visto dalla normale della prima.
            var p1 = PointOnFacePlane(f1);
            var p2 = PointOnFacePlane(f2);
            if (p1 == null || p2 == null) return null;   // non si legge: decide SOLIDWORKS, e le guardie dopo
            double distanceMm = Units.Mm(
                (p2[0] - p1[0]) * n1[0] + (p2[1] - p1[1]) * n1[1] + (p2[2] - p1[2]) * n1[2]);
            if (distanceMm <= 0)
                return "«" + uid1 + "» e «" + uid2 + "» sono piane, parallele e si danno le spalle " +
                       "(come il davanti e il dietro di un blocco): nessuna palla le tocca tutte e due " +
                       "dal lato dell'aria, e il raccordo non puo' nascere. Per arrotondare gli spigoli " +
                       "di una delle due usa «fillet_edges».";
            if (Math.Abs(distanceMm - 2 * radiusMm) > 0.01)
                return string.Format(CultureInfo.InvariantCulture,
                    "«{0}» e «{1}» sono piane, parallele e si guardano a {2:0.##} mm: un raccordo di " +
                    "raggio costante le tocca tutte e due solo se il raggio e' la meta' della distanza, " +
                    "{3:0.##} mm, e hai chiesto {4:0.##}. Se vuoi arrotondare il fondo fra le due, " +
                    "prova con «radius_mm»: {3:0.##}.",
                    uid1, uid2, distanceMm, distanceMm / 2, radiusMm);
            return null;
        }

        /// <summary>
        /// Un punto del piano della faccia, in metri, da `ISurface.PlaneParams`
        /// (i sei numeri: normale e punto, come in `FaceAxes`); null se la
        /// faccia non e' piana o non si legge.
        /// </summary>
        private static double[] PointOnFacePlane(IFace2 face)
        {
            try
            {
                var surface = face?.IGetSurface();
                if (surface == null || !surface.IsPlane()) return null;
                var plane = surface.PlaneParams as double[];
                if (plane == null || plane.Length < 6) return null;
                return new[] { plane[3], plane[4], plane[5] };
            }
            catch { return null; }
        }

        /// <summary>
        /// Toglie il raccordo appena nato quando una guardia lo boccia, e dice
        /// com'e' andata: la coda del messaggio di rifiuto. Stessa strada di
        /// `RemoveCut` (AnchorOperations.cs): DeleteSelection2 con
        /// swDelete_Absorbed, che non chiede conferma a nessuno. «Ha detto di
        /// averlo tolto» non basta: si ricontano le lavorazioni e si rimisura il
        /// volume.
        /// </summary>
        private string RemoveFaceFillet(
            IModelDoc2 model, IFeature fillet, PartPhoto before, int featureBefore)
        {
            bool removed = false;
            try
            {
                model.ClearSelection2(true);
                if (fillet.Select2(false, 0))
                    removed = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch { }
            finally { try { model.ClearSelection2(true); } catch { } }
            if (!removed)
                return " E non sono riuscito a toglierlo: e' ancora nel pezzo, ritiralo — " + Sentences.HowToWithdrawIt + ".";
            try { model.ForceRebuild3(false); } catch { }
            int features = CountFeature(model);
            if (features < 0)
                return " L'ho tolto, ma non ho potuto ricontare l'albero per controllare che il pezzo sia " +
                       "com'era: guardalo.";
            if (features > featureBefore)
                return " Ho provato a toglierlo, ma l'albero ha ancora " + (features - featureBefore) +
                       " lavorazioni in piu': ritirale — " + Sentences.HowToWithdrawIt + ".";
            double? now = VolumeMm3(model);
            if (now == null || before.Volume == null)
                return " L'ho tolto, ma non ho potuto rimisurare il volume per controllare che il pezzo sia " +
                       "com'era: guardalo.";
            if (Math.Abs(now.Value - before.Volume.Value) > 0.01)
                return " L'ho tolto, ma il volume adesso e' " +
                       now.Value.ToString("0.##", CultureInfo.InvariantCulture) + " mm3 invece di " +
                       before.Volume.Value.ToString("0.##", CultureInfo.InvariantCulture) +
                       ": guarda il pezzo prima di andare avanti.";
            return " Non l'ho lasciato: l'ho tolto e il pezzo e' com'era prima di questo passo.";
        }
    }
}
