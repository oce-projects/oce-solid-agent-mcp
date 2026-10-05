using System;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  FaceFilletProbe — GP7, la parte «fillet_face» (face fillet)
// =============================================================================
//
//  CONTESTO (letto da note-interne/TODO.md §GP7, CHANGELOG.md 0.47.0, e dalla
//  sonda storica RaccordoProva.cs::AFaccia, mai entrata nel repo): quattro tentativi, tutti su un cubo 40x40x40,
//  faccia superiore + una faccia laterale (facce ADIACENTI, condividono uno
//  spigolo), tutti con
//
//      FeatureManager.FeatureFillet3(
//          Options: Propagate | UniformRadius | entrambe | nessuna (4 giri),
//          Ftyp:    swFeatureFilletType_e.swFeatureFilletType_Face,
//          ... marche di selezione = swSimpleFilletWhichFaces_e.swFaceFilletSet1/Set2 ...)
//
//  e tutti e quattro tornano NULL. La voce (TODO GP7) registra l'ipotesi che
//  serva una coppia di facce NON adiacenti (l'uso "vero" del face fillet: un
//  blend fra facce separate da un gradino).
//
//  QUELLO CHE LA GUIDA DICE, E CHE NESSUNO DEI 4 TENTATIVI HA TOCCATO:
//
//  1) `IFeatureManager::FeatureFillet3` — pagina di riferimento, sezione
//     Remarks: dichiara il metodo "obsolete" (la sua stessa parola, dalla
//     versione 2020 in poi) per una lista di casi che include ESPLICITAMENTE
//     i face fillet, e rimanda per crearli a
//     CreateDefinition/CreateFeature/ISimpleFilletFeatureData2. SOLIDWORKS
//     installato: 2021 SP5.1 — il metodo usato in tutti e 4 i tentativi e'
//     ESATTAMENTE quello che la guida dice essere obsoleto per il tipo
//     Face. E' verosimile che per questo torni sempre NULL qualunque
//     combinazione di `Options`, mentre lo stesso metodo funziona per il
//     raccordo SEMPLICE (Ftyp=Simple, gia' in produzione: vedi
//     SwOperations.cs::RunFillet) — la stessa pagina elenca anche
//     quello fra gli obsoleti, ma evidentemente tollerato per compatibilita'
//     all'indietro sui casi comuni, mentre Face non lo e' in nessuna
//     combinazione provata.
//
//  2) `ISimpleFilletFeatureData2` — pagina di riferimento, Remarks, riga
//     "Face fillet/face-face chamfer": chiede SelectByID2 con Mark=2 per il
//     primo insieme di facce e Mark=4 per il secondo. L'esempio VBA
//     incorporato nella stessa pagina (precondizione: un cubo di esempio
//     SOLIDWORKS 2020, postcondizione un face fillet asimmetrico fra le
//     facce selezionate) lo conferma:
//         swFilletData.Initialize swFaceFillet
//         ' marche 2 e 4, la seconda selezione in Append
//     Le marche USATE nei 4 tentativi erano invece 1 e 2 — i valori
//     dell'enum `swSimpleFilletWhichFaces_e` (letto per riflessione su
//     SolidWorks.Interop.swconst.dll: swFaceFilletSet1=1, swFaceFilletSet2=2).
//     Quell'enum e' il parametro WhichFaceList di ISetFaces/GetFaces — un
//     posto DIVERSO dalla Mark di selezione — e i suoi valori non
//     coincidono con le marche che la guida chiede (2 e 4). E' un tranello
//     nuovo, della stessa famiglia di "l'enum col nome giusto e' quello
//     sbagliato" gia' pagato su GP8: un nome che sembra il posto giusto.
//
//  3) L'esempio VBA della stessa pagina usa due facce ADIACENTI di un cubo
//     (la voce lo dice esplicitamente nel proprio commento) — esattamente
//     la geometria dei 4 tentativi falliti. Questo rende debole l'ipotesi
//     della voce GP7 ("servono facce non adiacenti"): la guida stessa
//     mostra un face fillet fra facce adiacenti che condividono uno
//     spigolo.
//
//  IPOTESI, IN ORDINE DI PROBABILITA':
//   H1 — API sbagliata: FeatureFillet3 e' obsoleto per Ftyp=Face da SW2020.
//        Prova: FeatureFillet3.html, Remarks (citato sopra).
//   H2 — Marche sbagliate: 1/2 invece di 2/4, per aver riusato l'enum
//        swSimpleFilletWhichFaces_e (pensato per ISetFaces) come se fossero
//        le Mark di selezione. Prova: ISimpleFilletFeatureData2.html,
//        Remarks, riga "Face fillet/face-face chamfer", e l'esempio VBA
//        incorporato nella stessa pagina.
//   H3 — Anche con API e marche corrette, manca la specifica del profilo
//        (ConicTypeForCrossSectionProfile): la guida la elenca come passo
//        esplicito ("Complete the fillet/chamfer type specification...")
//        prima di poter creare la feature.
//   H4 (demota quella della voce) — facce non adiacenti: la guida la
//        smentisce (vedi punto 3), ma si prova comunque un caso a gradino
//        per chiudere la domanda con un numero, non con una supposizione.
//
//  LA RICETTA DELLA GUIDA (da ISimpleFilletFeatureData2.html + CreateDefinition.html):
//   1. swFeatDataObj = FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet)
//      — swFmFillet e' descritto li' come "constant radius, face, full round
//      fillet/chamfer": stesso Type per tutti e tre, e' Initialize() dopo che
//      sceglie quale.
//   2. swFilletData = (SimpleFilletFeatureData2)swFeatDataObj
//   3. swFilletData.Initialize((int)swSimpleFilletType_e.swFaceFillet)   // = 2
//   4. Selezionare le facce PRIMA di richiamare CreateFeature:
//        SelectData Mark=2 -> Face Set 1
//        SelectData Mark=4 -> Face Set 2   (Append=true sulla seconda)
//   5. (facoltativo ma la guida lo chiede per completare il tipo)
//      swFilletData.ConicTypeForCrossSectionProfile =
//          (int)swFeatureFilletProfileType_e.swFeatureFilletCircular
//   6. swFilletData.DefaultRadius = raggio (simmetrico)
//   7. swFeat = FeatureManager.CreateFeature(swFilletData)
//
//  Il pezzo: lo stesso cubo 40x40x40 gia' usato nei 4 tentativi (stessa
//  geometria, per confronto diretto). Faccia superiore (normale +Z) e faccia
//  laterale (normale +X): condividono lo spigolo verticale a X=+20,Y=+20.
//  Un raccordo r=5 su quello spigolo (lungo 40) toglie 40*25*(1-pi/4) =
//  214,6 mm3 — lo stesso numero di controllo gia' usato nella sonda
//  precedente, per poter confrontare riga per riga.
//
//  Niente EditDelete. Niente finestre: nessuna chiamata qui apre un modale
//  (CreateFeature su un fillet malformato fallisce silenziosamente o solleva
//  un'eccezione COM, non apre una finestra). Ogni pezzo si chiude senza
//  salvare, in un blocco finally.
//
//  ESITO DEL 28/9/2026, SOLIDWORKS 29.5.1 (l'uscita della sonda, com'era):
//
//    0. CONTROLLO (bug originale: FeatureFillet3 + Ftyp=Face + marche 1/2) : NULL   volume 64000 -> 64000 mm3 (tolti 0, atteso 214,6)   corpi 1 -> 1
//    1. RICETTA GUIDA (CreateDefinition+Initialize(swFaceFillet)+marche 2/4+profilo) : «Raccordo1» (Fillet)   volume 64000 -> 63785,4 mm3 (tolti 214,6, atteso 214,6)   corpi 1 -> 1
//    2. VARIANTE H2 (API nuova, marche SBAGLIATE 1/2) : NULL   volume 64000 -> 64000 mm3 (tolti 0, atteso 214,6)   corpi 1 -> 1
//    3. VARIANTE H3 (API e marche corrette, SENZA ConicTypeForCrossSectionProfile) : «Raccordo1» (Fillet)   volume 64000 -> 63785,4 mm3 (tolti 214,6, atteso 214,6)   corpi 1 -> 1
//    4. BONUS (SetFaces esplicito, Face2[] tipizzato, senza selezione) : «Raccordo1» (Fillet)   volume 64000 -> 63785,4 mm3 (tolti 214,6, atteso 214,6)   corpi 1 -> 1
//       SelectByID2: True/True
//    5. RICETTA GUIDA con SelectByID2 per coordinate (marche 2/4) : «Raccordo1» (Fillet)   volume 64000 -> 63785,4 mm3 (tolti 214,6, atteso 214,6)   corpi 1 -> 1
//
//  USO   Checks\FaceFillet\bin\FaceFillet.exe, con SOLIDWORKS aperto
//        (costruita da Checks\build-checks.ps1 (e da build.ps1, passo 1c)).
// =============================================================================
internal static class FaceFilletProbe
{
    private static ISldWorks _sw;

    private static int Main()
    {
        _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");

        Check_OriginalBug();
        Ricetta_Guida();
        Variant_H2_WrongMarks();
        Variant_H3_WithoutProfile();
        Bonus_SetFacesTipizzato();
        // REVISIONE 28/9/2026: H4 tolta dal giro. Le due facce del "gradino"
        // hanno la STESSA normale (+X) e stanno su piani paralleli sfalsati di
        // 5 mm: nessuna sfera puo' essere tangente a tutte e due dal lato
        // dell'aria, quindi quel raccordo e' impossibile per geometria e un
        // NULL non direbbe niente sull'API. Al suo posto la ricetta con la
        // selezione ESATTA delle Remarks (SelectByID2 per coordinate).
        // Variante_H4_FacceNonAdiacenti();
        Ricetta_SelectByID2();

        return 0;
    }

    // ------------------------------------------------------------------
    // 5. RICETTA con SelectByID2 — le Remarks di ISimpleFilletFeatureData2
    //    dicono "Call IModelDocExtension::SelectByID2 with Mark = 2 ... Mark = 4":
    //    stessa ricetta della 1, ma selezione per coordinate invece che con
    //    IEntity.Select4. Cubo: X,Y in [-20,20], Z in [0,40] (estruso dal
    //    frontale lungo +Z): faccia alta z=40, fianco x=+20.
    // ------------------------------------------------------------------
    private static void Ricetta_SelectByID2()
    {
        Run("5. RICETTA GUIDA con SelectByID2 per coordinate (marche 2/4)",
            (doc, up, side) =>
            {
                var data = (SimpleFilletFeatureData2)doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
                data.Initialize((int)swSimpleFilletType_e.swFaceFillet);
                // punti presi dal box reale delle due facce, non assunti
                var ba = up.GetBox() as double[]; var bf = side.GetBox() as double[];
                bool s1 = doc.Extension.SelectByID2("", "FACE", (ba[0] + ba[3]) / 2, (ba[1] + ba[4]) / 2, (ba[2] + ba[5]) / 2, false, 2, null, 0);
                bool s2 = doc.Extension.SelectByID2("", "FACE", (bf[0] + bf[3]) / 2, (bf[1] + bf[4]) / 2, (bf[2] + bf[5]) / 2, true, 4, null, 0);
                Console.WriteLine("   SelectByID2: " + s1 + "/" + s2);
                data.ConicTypeForCrossSectionProfile = (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
                data.DefaultRadius = 0.005;
                return doc.FeatureManager.CreateFeature(data) as IFeature;
            });
    }

    // ------------------------------------------------------------------
    // 0. CONTROLLO — riproduce ESATTAMENTE il tentativo gia' fatto (API
    //    vecchia, marche 1/2), per avere nella stessa sonda il numero di
    //    partenza. Atteso: NULL, come nei 4 giri gia' registrati.
    // ------------------------------------------------------------------
    private static void Check_OriginalBug()
    {
        Run("0. CONTROLLO (bug originale: FeatureFillet3 + Ftyp=Face + marche 1/2)",
            (doc, up, side) =>
            {
                var handler = (ISelectionMgr)doc.SelectionManager;
                var d1 = handler.CreateSelectData();
                d1.Mark = (int)swSimpleFilletWhichFaces_e.swFaceFilletSet1; // = 1 (SBAGLIATA come Mark)
                (up as IEntity).Select4(true, d1);
                var d2 = handler.CreateSelectData();
                d2.Mark = (int)swSimpleFilletWhichFaces_e.swFaceFilletSet2; // = 2 (SBAGLIATA come Mark)
                (side as IEntity).Select4(true, d2);

                return doc.FeatureManager.FeatureFillet3(
                    Options: (int)(swFeatureFilletOptions_e.swFeatureFilletUniformRadius |
                                   swFeatureFilletOptions_e.swFeatureFilletPropagate),
                    R1: 0.005, R2: 0.005, Rho: 0,
                    Ftyp: (int)swFeatureFilletType_e.swFeatureFilletType_Face,
                    OverflowType: (int)swFilletOverFlowType_e.swFilletOverFlowType_Default,
                    ConicRhoType: (int)swFeatureFilletProfileType_e.swFeatureFilletCircular,
                    Radii: null, Dist2Arr: null, RhoArr: null, SetBackDistances: null,
                    PointRadiusArray: null, PointDist2Array: null, PointRhoArray: null) as IFeature;
            });
    }

    // ------------------------------------------------------------------
    // 1. RICETTA DELLA GUIDA — CreateDefinition/Initialize/CreateFeature,
    //    marche 2 e 4, profilo dichiarato. Atteso: feature creata, tolti
    //    214,6 mm3.
    // ------------------------------------------------------------------
    private static void Ricetta_Guida()
    {
        Run("1. RICETTA GUIDA (CreateDefinition+Initialize(swFaceFillet)+marche 2/4+profilo)",
            (doc, up, side) =>
            {
                var dataObj = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
                var data = (SimpleFilletFeatureData2)dataObj;
                if (!data.Initialize((int)swSimpleFilletType_e.swFaceFillet))
                {
                    Console.WriteLine("   Initialize(swFaceFillet) ha risposto false.");
                    return null;
                }

                var handler = (ISelectionMgr)doc.SelectionManager;
                var d1 = handler.CreateSelectData(); d1.Mark = 2; // Face Set 1 — dalla guida
                (up as IEntity).Select4(true, d1);
                var d2 = handler.CreateSelectData(); d2.Mark = 4; // Face Set 2 — dalla guida
                (side as IEntity).Select4(true, d2);

                data.ConicTypeForCrossSectionProfile = (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
                data.DefaultRadius = 0.005;

                return doc.FeatureManager.CreateFeature(data) as IFeature;
            });
    }

    // ------------------------------------------------------------------
    // 2. VARIANTE H2 — stessa API nuova, ma con le marche SBAGLIATE (1/2,
    //    quelle riusate dall'enum WhichFaceList) per isolare se la marca da
    //    sola basta a far fallire anche l'API corretta.
    // ------------------------------------------------------------------
    private static void Variant_H2_WrongMarks()
    {
        Run("2. VARIANTE H2 (API nuova, marche SBAGLIATE 1/2)",
            (doc, up, side) =>
            {
                var dataObj = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
                var data = (SimpleFilletFeatureData2)dataObj;
                data.Initialize((int)swSimpleFilletType_e.swFaceFillet);

                var handler = (ISelectionMgr)doc.SelectionManager;
                var d1 = handler.CreateSelectData();
                d1.Mark = (int)swSimpleFilletWhichFaces_e.swFaceFilletSet1; // = 1, sbagliata
                (up as IEntity).Select4(true, d1);
                var d2 = handler.CreateSelectData();
                d2.Mark = (int)swSimpleFilletWhichFaces_e.swFaceFilletSet2; // = 2, sbagliata
                (side as IEntity).Select4(true, d2);

                data.ConicTypeForCrossSectionProfile = (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
                data.DefaultRadius = 0.005;

                return doc.FeatureManager.CreateFeature(data) as IFeature;
            });
    }

    // ------------------------------------------------------------------
    // 3. VARIANTE H3 — API e marche corrette, ma SENZA dichiarare
    //    ConicTypeForCrossSectionProfile, per vedere se e' davvero
    //    necessario o se CreateFeature si arrangia con un default.
    // ------------------------------------------------------------------
    private static void Variant_H3_WithoutProfile()
    {
        Run("3. VARIANTE H3 (API e marche corrette, SENZA ConicTypeForCrossSectionProfile)",
            (doc, up, side) =>
            {
                var dataObj = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
                var data = (SimpleFilletFeatureData2)dataObj;
                data.Initialize((int)swSimpleFilletType_e.swFaceFillet);

                var handler = (ISelectionMgr)doc.SelectionManager;
                var d1 = handler.CreateSelectData(); d1.Mark = 2;
                (up as IEntity).Select4(true, d1);
                var d2 = handler.CreateSelectData(); d2.Mark = 4;
                (side as IEntity).Select4(true, d2);

                // Di proposito NON impostato: dati.ConicTypeForCrossSectionProfile
                data.DefaultRadius = 0.005;

                return doc.FeatureManager.CreateFeature(data) as IFeature;
            });
    }

    // ------------------------------------------------------------------
    // BONUS — la stessa ricetta ma senza passare dalla selezione: le facce
    // si passano direttamente a SetFaces con un array TIPIZZATO Face2[]
    // (mai object[]: e' il tranello gia' pagato su GP8/combina_corpi e
    // sulle API con Body2[]/Face2[]). Usa i valori CORRETTI di
    // swSimpleFilletWhichFaces_e (qui e' il posto giusto per quell'enum:
    // e' proprio il parametro WhichFaceList di SetFaces, non una Mark).
    // ------------------------------------------------------------------
    private static void Bonus_SetFacesTipizzato()
    {
        Run("4. BONUS (SetFaces esplicito, Face2[] tipizzato, senza selezione)",
            (doc, up, side) =>
            {
                var dataObj = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
                var data = (SimpleFilletFeatureData2)dataObj;
                data.Initialize((int)swSimpleFilletType_e.swFaceFillet);

                doc.ClearSelection2(true); // qui NON si seleziona nulla: si passa via SetFaces
                // Array TIPIZZATO Face2[], non object[] (vedi memoria
                // api_sw_array_tipizzati.md): con object[] l'interop passa un
                // SAFEARRAY di VARIANT e la chiamata puo' rispondere con una
                // feature fantasma invece che con NULL o un errore vero.
                data.SetFaces((int)swSimpleFilletWhichFaces_e.swFaceFilletSet1, new Face2[] { (Face2)up });
                data.SetFaces((int)swSimpleFilletWhichFaces_e.swFaceFilletSet2, new Face2[] { (Face2)side });

                data.ConicTypeForCrossSectionProfile = (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
                data.DefaultRadius = 0.005;

                return doc.FeatureManager.CreateFeature(data) as IFeature;
            });
    }

    // ------------------------------------------------------------------
    // VARIANTE H4 — demotata dalla guida (l'esempio VBA di
    // ISimpleFilletFeatureData2 dichiara nel proprio commento di selezionare
    // facce ADIACENTI con marche 2 e 4), ma si prova comunque per chiudere
    // la domanda con un numero: due facce con la STESSA
    // normale (+X), NON adiacenti, separate da un gradino (un secondo
    // estruso piu' piccolo, ricavato dalla faccia superiore del cubo e
    // rientrato su tutti i lati — cosi' il gradino nasce lungo qualunque
    // sia il vero asse di "fianco", senza doverlo indovinare). API e
    // marche corrette (2/4).
    // ------------------------------------------------------------------
    private static void Variant_H4_FacesNotAdjacent()
    {
        IModelDoc2 doc = null;
        try
        {
            doc = CubeWithStep();
            var partDoc = (IPartDoc)doc;
            double vBefore = Volume(partDoc);
            int bodiesBefore = CountBodies(partDoc);
            if (bodiesBefore != 1)
                Console.WriteLine("5. VARIANTE H4 : ATTENZIONE, " + bodiesBefore +
                                   " corpi invece di 1 — il secondo estruso non si e' fuso col cubo.");

            var body = (partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[])[0] as IBody2;
            var faces = (body.GetFaces() as object[]).Cast<IFace2>().ToList();

            // Le due facce con normale +X: quella del blocco base (piu'
            // bassa lungo Y, l'asse di estrusione) e quella del blocco
            // rialzato, rientrata e piu' alta lungo Y — NON condividono
            // uno spigolo, sono separate dalla faccia orizzontale del
            // gradino (la vecchia "cima" del cubo, ora nel mezzo).
            var candidate = faces.Where(x => Normal(x)[0] > 0.9)
                                  .OrderByDescending(x => MeanDimension(x, 1))
                                  .ToList();
            if (candidate.Count < 2)
            {
                Console.WriteLine("5. VARIANTE H4 (facce non adiacenti, gradino) : niente facce candidate " +
                                   "(trovate " + candidate.Count + " facce a normale +X; atteso 2)");
                return;
            }
            var top = candidate[0];
            var lower = candidate[candidate.Count - 1];

            var dataObj = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
            var data = (SimpleFilletFeatureData2)dataObj;
            data.Initialize((int)swSimpleFilletType_e.swFaceFillet);

            doc.ClearSelection2(true);
            var handler = (ISelectionMgr)doc.SelectionManager;
            var d1 = handler.CreateSelectData(); d1.Mark = 2;
            (top as IEntity).Select4(true, d1);
            var d2 = handler.CreateSelectData(); d2.Mark = 4;
            (lower as IEntity).Select4(true, d2);

            data.ConicTypeForCrossSectionProfile = (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
            data.DefaultRadius = 0.005;

            IFeature f = null;
            try { f = doc.FeatureManager.CreateFeature(data) as IFeature; }
            catch (Exception ex) { Console.WriteLine("5. VARIANTE H4 (facce non adiacenti, gradino) : eccezione " + ex.Message); return; }

            try { doc.EditRebuild3(); } catch { }
            double removed = vBefore - Volume(partDoc);
            int bodiesAfter = CountBodies(partDoc);
            Console.WriteLine("5. VARIANTE H4 (facce non adiacenti, gradino) : " +
                (f == null ? "NULL" : "«" + f.Name + "» (" + f.GetTypeName2() + ")") +
                "   volume " + vBefore.ToString("0.#") + " -> " + Volume(partDoc).ToString("0.#") +
                " mm3 (tolti " + removed.ToString("0.#") + ")" +
                "   corpi " + bodiesBefore + " -> " + bodiesAfter);
        }
        catch (Exception ex)
        {
            Console.WriteLine("5. VARIANTE H4 (facce non adiacenti, gradino) : " + ex.Message);
        }
        finally
        {
            if (doc != null) { try { _sw.CloseDoc(doc.GetTitle()); } catch { } }
        }
    }

    // ------------------------------------------------------------------
    // Motore comune alle prime 4 scene: stesso cubo, stesse due facce
    // (alto = normale +Z, fianco = normale +X, adiacenti su uno spigolo),
    // stessa fotografia prima/dopo, stessa chiusura senza salvare.
    // `costruisci` riceve (doc, faccia_alto, faccia_fianco) e ritorna la
    // IFeature (o null) — tutta la parte che cambia fra le scene.
    // ------------------------------------------------------------------
    private static void Run(string label, Func<IModelDoc2, IFace2, IFace2, IFeature> build)
    {
        IModelDoc2 doc = null;
        try
        {
            doc = Cube();
            var partDoc = (IPartDoc)doc;
            double vBefore = Volume(partDoc);
            int bodiesBefore = CountBodies(partDoc);

            var body = (partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[])[0] as IBody2;
            var faces = (body.GetFaces() as object[]).Cast<IFace2>().ToList();
            var up = faces.FirstOrDefault(x => Normal(x)[2] > 0.9);
            var side = faces.FirstOrDefault(x => Normal(x)[0] > 0.9);
            if (up == null || side == null)
            {
                Console.WriteLine(label + " : niente facce");
                return;
            }

            doc.ClearSelection2(true);

            IFeature f = null;
            try { f = build(doc, up, side); }
            catch (Exception ex) { Console.WriteLine(label + " : eccezione " + ex.Message); return; }

            try { doc.EditRebuild3(); } catch { }
            double vAfter = Volume(partDoc);
            double removed = vBefore - vAfter;
            int bodiesAfter = CountBodies(partDoc);

            string typ = "";
            try { if (f != null) typ = " (" + f.GetTypeName2() + ")"; } catch { }

            Console.WriteLine(label + " : " +
                (f == null ? "NULL" : "«" + f.Name + "»" + typ) +
                "   volume " + vBefore.ToString("0.#") + " -> " + vAfter.ToString("0.#") +
                " mm3 (tolti " + removed.ToString("0.#") + ", atteso 214,6)" +
                "   corpi " + bodiesBefore + " -> " + bodiesAfter);
        }
        catch (Exception ex)
        {
            Console.WriteLine(label + " : " + ex.Message);
        }
        finally
        {
            if (doc != null) { try { _sw.CloseDoc(doc.GetTitle()); } catch { } }
        }
    }

    // La normale di una faccia PIANA, dalla superficie sottostante, girata se
    // la faccia e' al contrario: la normale di `PlaneParams` e' quella del
    // piano, non quella della faccia. (Copiata identica da RaccordoProva.cs.)
    private static double[] Normal(IFace2 f)
    {
        try
        {
            var s = f.IGetSurface();
            if (s == null || !s.IsPlane()) return new double[] { 0, 0, 0 };
            var p = s.PlaneParams as double[];
            if (p == null || p.Length < 3) return new double[] { 0, 0, 0 };
            double facing = f.FaceInSurfaceSense() ? 1 : -1;
            return new[] { p[0] * facing, p[1] * facing, p[2] * facing };
        }
        catch { return new double[] { 0, 0, 0 }; }
    }

    // Quota media di una faccia piana lungo l'asse `indice` (0=X,1=Y,2=Z),
    // dal suo box di ingombro — basta per ORDINARE due facce parallele fra
    // loro, non per misurarle.
    private static double MeanDimension(IFace2 f, int index)
    {
        try
        {
            var box = f.GetBox() as double[]; // [xmin,ymin,zmin,xmax,ymax,zmax]
            if (box == null || box.Length < 6) return 0;
            return (box[index] + box[index + 3]) / 2.0;
        }
        catch { return 0; }
    }

    private static double Volume(IPartDoc partDoc)
    {
        double v = 0;
        var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies == null) return 0;
        foreach (var c in bodies)
        {
            var mp = (c as IBody2).GetMassProperties(1000.0) as double[];
            if (mp != null && mp.Length > 4) v += mp[3] * 1e9;
        }
        return v;
    }

    private static int CountBodies(IPartDoc partDoc)
    {
        var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        return bodies == null ? 0 : bodies.Length;
    }

    // Lo stesso cubo 40x40x40 di RaccordoProva.cs::Cubo(), per confrontare
    // riga per riga con i 4 tentativi gia' registrati.
    private static IModelDoc2 Cube()
    {
        string tpl = _sw.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        var doc = (IModelDoc2)_sw.NewDocument(tpl, 0, 0, 0);
        doc.ClearSelection2(true);
        SelectBasePlane(doc, 1);
        var sm = doc.SketchManager;
        sm.InsertSketch(true);
        sm.CreateCornerRectangle(-0.02, -0.02, 0, 0.02, 0.02, 0);
        sm.InsertSketch(false);
        doc.Extension.SelectByID2("Schizzo1", "SKETCH", 0, 0, 0, false, 0, null, 0);
        doc.FeatureManager.FeatureExtrusion3(
            true, false, false, (int)swEndConditions_e.swEndCondBlind, 0, 0.040, 0,
            false, false, false, false, 0, 0, false, false, false, false,
            true, true, true, 0, 0, false);
        return doc;
    }

    // Un blocco base 40x40x40 (Cubo()) con sopra un secondo blocco piu'
    // piccolo, rientrato SIMMETRICAMENTE su tutti i lati rispetto alla
    // faccia su cui nasce — cosi' il gradino si forma su qualunque coppia
    // di assi sia davvero il piano di quella faccia, SENZA dover indovinare
    // quale coordinata di `CreateCornerRectangle` corrisponda a quale asse
    // globale (e senza selezionare per COORDINATE, che e' fragile: qui si
    // seleziona la IFace2 vera, trovata per normale come "alto"/"fianco").
    //
    // Si parte dalla faccia con normale +Y (nessuna delle due gia' usate,
    // "alto"=+Z e "fianco"=+X): e' comunque UNA delle sei facce del cubo,
    // e il suo piano contiene per costruzione i DUE assi diversi dalla sua
    // normale — quindi contiene sempre X, l'asse di "fianco". Un rettangolo
    // piu' piccolo e centrato sull'origine del nuovo schizzo (che SOLIDWORKS
    // mette di default nella proiezione dell'origine del pezzo, e l'origine
    // e' dentro questa faccia perche' il cubo e' centrato su X e Z) resta
    // rientrato su tutti e quattro i lati, qualunque sia l'ordine reale
    // degli assi in quello schizzo.
    private static IModelDoc2 CubeWithStep()
    {
        var doc = Cube();
        var partDoc = (IPartDoc)doc;
        var body = (partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[])[0] as IBody2;
        var faces = (body.GetFaces() as object[]).Cast<IFace2>().ToList();
        var top = faces.FirstOrDefault(x => Normal(x)[1] > 0.9);
        if (top == null) throw new Exception("CuboConGradino: non trovo la faccia di normale +Y.");

        doc.ClearSelection2(true);
        (top as IEntity).Select4(true, null);
        var sm = doc.SketchManager;
        sm.InsertSketch(true);
        // Rientrato di 0,005 su ogni lato rispetto al mezzo lato del cubo
        // (0,020): il blocco rialzato risulta 0,030 x 0,030, centrato.
        sm.CreateCornerRectangle(-0.015, -0.015, 0, 0.015, 0.015, 0);
        sm.InsertSketch(false);
        doc.Extension.SelectByID2("Schizzo2", "SKETCH", 0, 0, 0, false, 0, null, 0);
        doc.FeatureManager.FeatureExtrusion3(
            true, false, false, (int)swEndConditions_e.swEndCondBlind, 0, 0.020, 0,
            false, false, false, false, 0, 0, false, false, false, false,
            true, true, true, 0, 0, false);
        return doc;
    }

    // W3.1-bis.2 point 10: base planes by position, not by their localized name
    // (this probe used to try the plane's Italian name, then its English one,
    // which only ever works on those two SOLIDWORKS UI languages).
    // The first three RefPlane features of the tree, same rule as
    // Checks/Rib, Checks/Fill and Checks/ExtrusionType.
    // Since Z-C21.5 the library itself (Oce.Solid.Agent.Core/References.cs,
    // BasePlane) recognises a base plane by its canonical normal through the
    // world origin instead, because a template with an extra plane ahead of the
    // three defaults fools a position-based rule with no error; this probe
    // stays positional because it is a throwaway part on SOLIDWORKS' own
    // default template and cannot reference the library (single-file compile,
    // Checks/build-checks.ps1).
    private static bool SelectBasePlane(IModelDoc2 doc, int which)
    {
        int seen = 0;
        var f = doc.FirstFeature() as IFeature;
        while (f != null)
        {
            if (f.GetTypeName2() == "RefPlane" && ++seen == which) { doc.ClearSelection2(true); return f.Select2(false, 0); }
            f = f.GetNextFeature() as IFeature;
        }
        return false;
    }
}
