using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// IL RACCORDO DI FACCIA (voce GP7, la meta' che mancava).
    ///
    /// Un raccordo che non si attacca a uno SPIGOLO ma a DUE FACCE: la palla di
    /// raggio r rotola appoggiata a tutte e due, e la superficie che lascia e' il
    /// raccordo. Sullo spigolo di un cubo da' lo stesso pezzo di
    /// «raccordo_spigoli»; la differenza e' che non serve uno spigolo da
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
    ///    La via vecchia, quella di `raccordo_spigoli` e `raccordo_variabile`, qui
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
    /// ── MISURATO PRIMA DI SCRIVERE (sonda Controlli\RaccordoDiFaccia, 28/9/2026) ──
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
    /// OperazioniGuardie.cs). Se qualcosa non torna il raccordo si TOGLIE
    /// (DeleteSelection2, mai la chiamata con la finestra di conferma) e si
    /// ricontrolla che il pezzo sia com'era.
    /// </summary>
    public partial class OperazioniSw
    {
        /// <summary>
        /// Le due marche di selezione della ricetta, da `ISimpleFilletFeatureData2.html`,
        /// Remarks, riga «Face fillet/face-face chamfer»: «Mark = 2 to select Face
        /// Set 1. Mark = 4 to select Face Set 2». Non sono i valori di
        /// `swSimpleFilletWhichFaces_e` (1 e 2): quelli sono per `SetFaces`, e come
        /// marche danno NULL (misurato).
        /// </summary>
        private const int MarcaFacceInsieme1 = 2;
        private const int MarcaFacceInsieme2 = 4;

        /// <summary>Due normali con |coseno| oltre questo sono parallele.</summary>
        private const double RaccordoFacciaQuasiParallele = 0.9999;

        private Esito RaccordoFaccia(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            if (!(modello is IPartDoc))
                return Fallito("Il raccordo di faccia si fa su un pezzo: apri il pezzo e chiedimelo li'.");

            // DUE FACCE, E «faccia_rif» NE NOMINA UNA SOLA. `RisolviFaccia`
            // guarda «faccia_rif» prima dell'uid: con due facce da risolvere lo
            // stesso riferimento vincerebbe tutte e due le volte, e il raccordo
            // si chiederebbe fra una faccia e se stessa.
            if (HaRiferimento(a, "faccia_rif"))
                return Fallito(
                    "Qui servono due facce diverse, e «faccia_rif» ne nomina una sola: usa " +
                    "«faccia_1_uid» e «faccia_2_uid» con gli identificativi dello stato. Se una " +
                    "delle due la crea un passo di questo piano, rileggi lo stato dopo quel passo.");

            double raggio;
            string errore;
            if (!NumObbligatorio(a, "raggio_mm", out raggio, out errore)) return Fallito(errore);
            if (raggio <= 0)
                return Fallito(
                    "«raggio_mm» deve essere maggiore di zero: hai chiesto " +
                    raggio.ToString("0.##", CultureInfo.InvariantCulture) + ". Per esempio 5.");

            string uid1 = (Str(a, "faccia_1_uid", "") ?? "").Trim();
            string uid2 = (Str(a, "faccia_2_uid", "") ?? "").Trim();
            if (uid1.Length == 0 || uid2.Length == 0)
                return Fallito(
                    "Dimmi le due facce: «faccia_1_uid» e «faccia_2_uid», con gli identificativi " +
                    "dello stato del documento (per esempio «F1» e «F3»). Il raccordo si appoggia " +
                    "a tutte e due.");

            // LA STESSA FACCIA DUE VOLTE. Si dice prima di chiamare: e' la
            // confusione di chi voleva arrotondare i bordi di UNA faccia, e per
            // quello c'e' un'altra operazione.
            if (string.Equals(uid1, uid2, StringComparison.OrdinalIgnoreCase))
                return Fallito(
                    "«" + uid1 + "» e' in tutti e due gli insiemi: un raccordo di faccia unisce DUE " +
                    "facce diverse, appoggiandosi a tutte e due. Per arrotondare i bordi di una " +
                    "faccia sola usa «raccordo_spigoli» sui suoi spigoli.");

            string motivo;
            var faccia1 = RisolviFaccia(stato, a, uid1, out motivo);
            if (faccia1 == null) return Fallito("Faccia «" + uid1 + "»: " + motivo);
            var faccia2 = RisolviFaccia(stato, a, uid2, out motivo);
            if (faccia2 == null) return Fallito("Faccia «" + uid2 + "»: " + motivo);

            // DUE UID, UNA FACCIA. Dopo un ritrovamento per firma due nomi
            // diversi possono essere finiti sulla stessa faccia: lo si chiede a
            // SOLIDWORKS (ISldWorks~IsSame.html, «Gets whether the two specified
            // objects are the same object»), come in OperazioniCorpi.
            bool stessa = ReferenceEquals(faccia1, faccia2);
            if (!stessa)
            {
                try { stessa = _sw.IsSame(faccia1, faccia2) == (int)swObjectEquality.swObjectSame; }
                catch { }
            }
            if (stessa)
                return Fallito(
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
            // (Controlli\RaccordoDiFaccia, REVISIONE 28/9/2026) proprio perche' un
            // NULL li' non avrebbe misurato l'API ma la geometria.
            string parallele = FaccePianeParallele(faccia1, faccia2, uid1, uid2, raggio);
            if (parallele != null) return Fallito(parallele);

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
            double spigolo = -1;
            try { spigolo = LunghezzaCheStaSu(faccia1, faccia2); } catch { }
            double? atteso = null;
            var n1 = NormaleDellaFaccia(faccia1);
            var n2 = NormaleDellaFaccia(faccia2);
            if (spigolo > 0 && n1 != null && n2 != null && n1.Length >= 3 && n2.Length >= 3 &&
                Math.Abs(n1[0] * n2[0] + n1[1] * n2[1] + n1[2] * n2[2]) < 1e-6)
                atteso = Math.Round(spigolo * raggio * raggio * (1 - Math.PI / 4), 2);

            // ── LA FOTOGRAFIA PRIMA (OperazioniGuardie.cs) ───────────────
            var foto = FotografaIlPezzo(modello);
            if (foto.Volume == null) return Fallito(VolumeNonLettoPrima("fare il raccordo di faccia"));
            int featurePrima = ContaFeature(modello);
            if (featurePrima < 0 || foto.Corpi < 0)
                return Fallito(VolumeNonLettoPrima("fare il raccordo di faccia")
                    .Replace("il volume del pezzo", "l'albero e i corpi del pezzo"));

            // ── LA RICETTA DELLA GUIDA, NELL'ORDINE MISURATO ─────────────
            IFeature feature;
            try
            {
                modello.ClearSelection2(true);
                var dati = modello.FeatureManager.CreateDefinition(
                    (int)swFeatureNameID_e.swFmFillet) as SimpleFilletFeatureData2;
                if (dati == null)
                    return Fallito(
                        "SOLIDWORKS non mi ha dato la definizione di un raccordo (CreateDefinition). " +
                        "Non ho toccato niente: prova «raccordo_spigoli» sullo spigolo fra le due facce.");
                if (!dati.Initialize((int)swSimpleFilletType_e.swFaceFillet))
                    return Fallito(
                        "SOLIDWORKS non accetta un raccordo di faccia su questa definizione " +
                        "(Initialize ha risposto false). Non ho toccato niente: prova «raccordo_spigoli» " +
                        "sullo spigolo fra le due facce.");

                var gestore = modello.SelectionManager as ISelectionMgr;
                if (gestore == null)
                    return Fallito("Non riesco a usare il gestore di selezione. Non ho toccato niente.");
                // INSIEME 1 MARCA 2, INSIEME 2 MARCA 4 (vedi le costanti).
                if (!Seleziona(gestore, faccia1, MarcaFacceInsieme1))
                    return Fallito("Non sono riuscito a selezionare la faccia «" + uid1 + "». Non ho toccato " +
                                   "niente: rileggi lo stato e indica di nuovo le due facce.");
                if (!Seleziona(gestore, faccia2, MarcaFacceInsieme2))
                    return Fallito("Non sono riuscito a selezionare la faccia «" + uid2 + "». Non ho toccato " +
                                   "niente: rileggi lo stato e indica di nuovo le due facce.");

                // Il profilo circolare: la guida lo chiede per «completare il tipo»
                // (misurato: senza, nasce lo stesso raccordo; si mette perche' la
                // ricetta lo dice e non costa niente).
                dati.ConicTypeForCrossSectionProfile =
                    (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
                dati.DefaultRadius = Unita.M(raggio);

                feature = modello.FeatureManager.CreateFeature(dati) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("Il raccordo di faccia non e' riuscito: " + ex.Message +
                               ". Prova «raccordo_spigoli» sullo spigolo fra le due facce.");
            }
            finally { try { modello.ClearSelection2(true); } catch { } }

            if (feature == null)
            {
                int ora = ContaFeature(modello);
                string coda = ora > featurePrima
                    ? " Eppure l'albero e' cresciuto (" + featurePrima + " -> " + ora + "): guardalo e " +
                      "ritira quello che c'e' in piu' — " + Frasi.CosiSiRitira + "."
                    : " Non ho toccato niente.";
                return Fallito(
                    "Il raccordo di faccia non e' stato creato, e SOLIDWORKS non dice perche'. Di " +
                    "solito e' il raggio: la palla deve appoggiarsi a tutte e due le facce senza " +
                    "uscirne. Prova un raggio piu' piccolo; se le due facce si toccano su uno " +
                    "spigolo, lo stesso raccordo si fa con «raccordo_spigoli» su quello spigolo." + coda);
            }

            // ── E ADESSO SI MISURA IL PEZZO ──────────────────────────────
            //
            // ForceRebuild3 e non la ricostruzione semplice: e' quella di
            // `cambia_estrusione`, la cui risposta `CosaSiERotto` sa gia'
            // leggere su un pezzo sano (caso il_cieco_diventa_passante).
            bool ricostruito = false;
            try { ricostruito = modello.ForceRebuild3(false); } catch { }

            string nome = "";
            string tipo = "";
            try { nome = feature.Name; } catch { }
            try { tipo = feature.GetTypeName2(); } catch { }

            // LA FEATURE NUOVA PER TIPO: la sonda l'ha vista nascere «Fillet».
            if (!string.Equals(tipo, "Fillet", StringComparison.Ordinal))
                return Fallito(
                    "E' nata una lavorazione «" + nome + "» di tipo «" + tipo + "», non un raccordo " +
                    "(«Fillet»): non e' quello che ho chiesto." +
                    TogliIlRaccordoDiFaccia(modello, feature, foto, featurePrima) +
                    " Prova «raccordo_spigoli» sullo spigolo fra le due facce.");

            string rotto = CosaSiERotto(modello, foto, ricostruito, false);
            if (rotto != null)
                return Fallito(
                    "Il raccordo di faccia «" + nome + "» e' nato, ma " + rotto + "." +
                    TogliIlRaccordoDiFaccia(modello, feature, foto, featurePrima) +
                    " Prova un raggio piu' piccolo, oppure «raccordo_spigoli» sullo spigolo fra le due facce.");

            double? volumeDopo = VolumeMm3(modello);
            double? cambio = volumeDopo - foto.Volume;

            // UN RACCORDO CHE NON CAMBIA IL VOLUME NON HA FATTO NIENTE. Toglie
            // materiale su uno spigolo esterno, ne aggiunge in un angolo interno:
            // zero vuol dire nessuno dei due. Si dice solo di un volume riletto
            // (voce Z-C8).
            if (cambio.HasValue && Math.Abs(cambio.Value) < 0.01)
                return Fallito(
                    "Il raccordo di faccia «" + nome + "» e' nato ma il volume e' rimasto " +
                    foto.Volume.Value.ToString("0.##", CultureInfo.InvariantCulture) + " mm3: non ha " +
                    "arrotondato niente." + TogliIlRaccordoDiFaccia(modello, feature, foto, featurePrima) +
                    " Prova «raccordo_spigoli» sullo spigolo fra le due facce.");

            // IL CONTO A MANO, CONFRONTATO. Dove c'e' (vedi sopra), un raccordo
            // che toglie un'altra quantita' non e' quello chiesto: parziale, o
            // appoggiato altrove. Lo scarto ammesso e' il mezzo decimo, o un
            // decimillesimo del conto se e' piu' grande: il numero misurato
            // dalla sonda (214,6) e il conto (214,602) stanno nel centesimo.
            if (atteso.HasValue && cambio.HasValue &&
                Math.Abs(Math.Abs(cambio.Value) - atteso.Value) > Math.Max(0.05, atteso.Value * 1e-4))
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "Il raccordo di faccia «{0}» e' nato, ma ha cambiato il volume di {1:0.##} mm3 e il " +
                    "conto a mano ne vuole {2:0.##} (spigolo comune {3:0.##} mm x r² x (1 − π/4)): non " +
                    "si e' appoggiato come chiesto sulle due facce.{4} Per lo stesso raccordo sullo " +
                    "spigolo fra le due facce usa «raccordo_spigoli».",
                    nome, Math.Abs(cambio.Value), atteso.Value, spigolo,
                    TogliIlRaccordoDiFaccia(modello, feature, foto, featurePrima)));

            // IL RAGGIO CHE LA FEATURE DICHIARA (ISimpleFilletFeatureData2~
            // DefaultRadius.html: «Default radius of the symmetric fillet»),
            // riletto con IFeature::GetDefinition — non l'argomento ripetuto.
            // Non decide niente: decide il volume. Null se non si rilegge.
            object raggioDichiarato = null;
            try
            {
                var letti = feature.GetDefinition() as ISimpleFilletFeatureData2;
                if (letti != null) raggioDichiarato = Math.Round(Unita.Mm(letti.DefaultRadius), 3);
            }
            catch { }

            int corpiDopo = ContaCorpiSolidi(modello);
            int inErroreDopo = LavorazioniInErrore(modello);

            var misure = new Dictionary<string, object>
            {
                // I CAMPI NUOVI SONO IN INGLESE (decisione della lingua, W3.1).
                // Niente argomenti ripetuti come fossero misure: il raggio e'
                // quello che la feature DICHIARA, il resto e' letto dal pezzo.
                ["declared_radius_mm"] = raggioDichiarato,
                ["feature_type"] = tipo,
                ["volume_before_mm3"] = NumeroOppureNull(foto.Volume, 2),
                ["volume_after_mm3"] = NumeroOppureNull(volumeDopo, 2),
                ["volume_change_mm3"] = NumeroOppureNull(cambio, 2),
                ["material_removed"] = cambio.HasValue ? (object)(cambio.Value < 0) : null,
                ["bodies"] = NumeroOppureNull(corpiDopo),
                ["features_in_error"] = NumeroOppureNull(inErroreDopo),
                ["shared_edge_mm"] = spigolo >= 0 ? (object)Math.Round(spigolo, 2) : null,
                ["expected_change_abs_mm3"] = NumeroOppureNull(atteso, 2),
                ["how_expected_is_counted"] =
                    "shared edge length x r^2 x (1 - pi/4): exact for two flat faces at 90 degrees " +
                    "that touch along an edge (material removed on an outside edge, added in an " +
                    "inside corner), read BEFORE the fillet (afterwards the faces no longer touch) " +
                    "and compared with the volume change. Null when the two faces are not like that: " +
                    "no number is made up.",
            };
            if (!cambio.HasValue) misure["volume_not_read_again"] = NotaVolumeNonRiletto;

            _log("[agente] raccordo di faccia r=" + raggio.ToString("0.##", CultureInfo.InvariantCulture) +
                 " fra " + uid1 + " e " + uid2 + ": «" + nome + "», volume " + Letto(foto.Volume) +
                 " -> " + Letto(volumeDopo));
            return Riuscito(feature, misure);
        }

        /// <summary>
        /// La frase di rifiuto quando le due facce sono piane e parallele e il
        /// raccordo e' impossibile per geometria; null se si puo' provare (facce
        /// non piane, non parallele, o due pareti che si guardano a distanza 2r).
        /// </summary>
        private static string FaccePianeParallele(IFace2 f1, IFace2 f2, string uid1, string uid2, double raggioMm)
        {
            var n1 = NormaleDellaFaccia(f1);
            var n2 = NormaleDellaFaccia(f2);
            if (n1 == null || n2 == null || n1.Length < 3 || n2.Length < 3) return null;
            double coseno = n1[0] * n2[0] + n1[1] * n2[1] + n1[2] * n2[2];
            if (Math.Abs(coseno) < RaccordoFacciaQuasiParallele) return null;

            if (coseno > 0)
                return "«" + uid1 + "» e «" + uid2 + "» sono piane, parallele e guardano dalla stessa " +
                       "parte (complanari, o separate da un gradino): nessuna palla le tocca tutte e due " +
                       "dal lato dell'aria, e il raccordo non puo' nascere. Se c'e' un gradino fra loro, " +
                       "usa «raccordo_spigoli» sugli spigoli del gradino.";

            // Normali opposte: si guardano o si danno le spalle? Il punto del
            // piano della seconda, visto dalla normale della prima.
            var p1 = PuntoSulPianoDellaFaccia(f1);
            var p2 = PuntoSulPianoDellaFaccia(f2);
            if (p1 == null || p2 == null) return null;   // non si legge: decide SOLIDWORKS, e le guardie dopo
            double distanzaMm = Unita.Mm(
                (p2[0] - p1[0]) * n1[0] + (p2[1] - p1[1]) * n1[1] + (p2[2] - p1[2]) * n1[2]);
            if (distanzaMm <= 0)
                return "«" + uid1 + "» e «" + uid2 + "» sono piane, parallele e si danno le spalle " +
                       "(come il davanti e il dietro di un blocco): nessuna palla le tocca tutte e due " +
                       "dal lato dell'aria, e il raccordo non puo' nascere. Per arrotondare gli spigoli " +
                       "di una delle due usa «raccordo_spigoli».";
            if (Math.Abs(distanzaMm - 2 * raggioMm) > 0.01)
                return string.Format(CultureInfo.InvariantCulture,
                    "«{0}» e «{1}» sono piane, parallele e si guardano a {2:0.##} mm: un raccordo di " +
                    "raggio costante le tocca tutte e due solo se il raggio e' la meta' della distanza, " +
                    "{3:0.##} mm, e hai chiesto {4:0.##}. Se vuoi arrotondare il fondo fra le due, " +
                    "prova con «raggio_mm»: {3:0.##}.",
                    uid1, uid2, distanzaMm, distanzaMm / 2, raggioMm);
            return null;
        }

        /// <summary>
        /// Un punto del piano della faccia, in metri, da `ISurface.PlaneParams`
        /// (i sei numeri: normale e punto, come in `AssiDellaFaccia`); null se la
        /// faccia non e' piana o non si legge.
        /// </summary>
        private static double[] PuntoSulPianoDellaFaccia(IFace2 faccia)
        {
            try
            {
                var superficie = faccia?.IGetSurface();
                if (superficie == null || !superficie.IsPlane()) return null;
                var piano = superficie.PlaneParams as double[];
                if (piano == null || piano.Length < 6) return null;
                return new[] { piano[3], piano[4], piano[5] };
            }
            catch { return null; }
        }

        /// <summary>
        /// Toglie il raccordo appena nato quando una guardia lo boccia, e dice
        /// com'e' andata: la coda del messaggio di rifiuto. Stessa strada di
        /// `TogliIlTaglio` (OperazioniAncora.cs): DeleteSelection2 con
        /// swDelete_Absorbed, che non chiede conferma a nessuno. «Ha detto di
        /// averlo tolto» non basta: si ricontano le lavorazioni e si rimisura il
        /// volume.
        /// </summary>
        private string TogliIlRaccordoDiFaccia(
            IModelDoc2 modello, IFeature raccordo, FotoDelPezzo prima, int featurePrima)
        {
            bool tolto = false;
            try
            {
                modello.ClearSelection2(true);
                if (raccordo.Select2(false, 0))
                    tolto = modello.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch { }
            finally { try { modello.ClearSelection2(true); } catch { } }
            if (!tolto)
                return " E non sono riuscito a toglierlo: e' ancora nel pezzo, ritiralo — " + Frasi.CosiSiRitira + ".";
            try { modello.ForceRebuild3(false); } catch { }
            int lavorazioni = ContaFeature(modello);
            if (lavorazioni < 0)
                return " L'ho tolto, ma non ho potuto ricontare l'albero per controllare che il pezzo sia " +
                       "com'era: guardalo.";
            if (lavorazioni > featurePrima)
                return " Ho provato a toglierlo, ma l'albero ha ancora " + (lavorazioni - featurePrima) +
                       " lavorazioni in piu': ritirale — " + Frasi.CosiSiRitira + ".";
            double? adesso = VolumeMm3(modello);
            if (adesso == null || prima.Volume == null)
                return " L'ho tolto, ma non ho potuto rimisurare il volume per controllare che il pezzo sia " +
                       "com'era: guardalo.";
            if (Math.Abs(adesso.Value - prima.Volume.Value) > 0.01)
                return " L'ho tolto, ma il volume adesso e' " +
                       adesso.Value.ToString("0.##", CultureInfo.InvariantCulture) + " mm3 invece di " +
                       prima.Volume.Value.ToString("0.##", CultureInfo.InvariantCulture) +
                       ": guarda il pezzo prima di andare avanti.";
            return " Non l'ho lasciato: l'ho tolto e il pezzo e' com'era prima di questo passo.";
        }
    }
}
