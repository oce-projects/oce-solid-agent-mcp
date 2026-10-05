using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.BrokenBody
{
    /// <summary>
    /// CHE COSA RISPONDE `Check3` SU UN CORPO SANO, E CHE COSA SU UNO CHE NON
    /// LO E'. (voce GP1)
    ///
    /// ── LA DOMANDA APERTA CHE QUESTO ATTREZZO CHIUDE ──────────────────────
    ///
    /// Il censimento delle API (RAG 80p, punto 4) l'ha lasciata scritta cosi':
    ///
    ///   «`IBody2.Check()` e `Check2()` restituiscono `Int32` e non so se zero
    ///    voglia dire *sano* o *zero difetti controllati*. La risposta
    ///    sbagliata e' indistinguibile da una giusta.»
    ///
    /// Un'operazione `check_body_geometry` costruita sopra a uno zero di
    /// cui non si conosce il significato e' il difetto che non da' errore
    /// nella sua forma piu' cara: dice «sano» su qualunque cosa, e chi la
    /// chiama esporta uno STL con un buco.
    ///
    /// ── PERCHE' UN ESEGUIBILE E NON UNO SCRIPT ───────────────────────────
    ///
    /// Stessa ragione di `NewPart`: `ISldWorks.NewDocument` per late
    /// binding da PowerShell 5.1 risponde `TYPE_E_ELEMENTNOTFOUND`. Provato
    /// oggi di nuovo, per non ricordarlo male.
    ///
    /// ── LE FIRME, LETTE DALL'INTEROP 29.5.1.1 ────────────────────────────
    ///
    ///   IBody2.Check()  -> Int32
    ///   IBody2.Check2() -> Int32
    ///   IBody2.Check3   -> FaultEntity     ** E' UNA PROPRIETA', NON UN METODO **
    ///   IBody2.Diagnose() -> DiagnoseResult
    ///   IFaultEntity.Count, get_ErrorCode(i), get_Entity(i), get_Entity2(i)
    ///   IDiagnoseResult.GetGapsCount(), GetCoEdgesAtGap(i)
    ///
    /// `Check3` proprieta' e non metodo e' una trappola: scritto `Check3()` in
    /// late binding non da' errore di compilazione, da' un fallimento a
    /// runtime dentro un `catch` che lo ingoia. I codici hanno un nome —
    /// `swFaultEntityErrorCode_e`, 36 valori da `swBodyCorrupt = 1` a
    /// `swEdgeTouchEdge = 36` — e lo zero NON e' fra quelli.
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException)
            {
                Console.Error.WriteLine("SOLIDWORKS non e' aperto.");
                return 2;
            }
            var sw = (ISldWorks)raw;

            string model = Preferred(sw) ?? SearchedOnDisk();
            if (model == null)
            {
                Console.Error.WriteLine("Nessun modello di parte (.prtdot) trovato.");
                return 2;
            }

            var doc = (IModelDoc2)sw.NewDocument(model, 0, 0, 0);
            if (doc == null)
            {
                Console.Error.WriteLine("NewDocument ha dato null.");
                return 2;
            }
            Console.WriteLine("pezzo di prova: " + doc.GetTitle());

            // ── 1. IL SANO: un parallelepipedo 100 x 100 x 20 ─────────────
            if (!Extrude(doc, -0.05, -0.05, 0.05, 0.05, 0.02, false))
            {
                Console.Error.WriteLine("non sono riuscito a estrudere il corpo sano.");
                return 2;
            }
            var healthy = Solids(doc);
            Tell("il sano: 100 x 100 x 20 estruso", healthy);
            Console.WriteLine();
            Expect("il solido sano: Check() dice 1, non 0", One(healthy[0], c => c.Check()) == 1,
                     "Check() = " + One(healthy[0], c => c.Check()));
            Expect("il solido sano: Check2() dice 0, non 1", One(healthy[0], c => c.Check2()) == 0,
                     "Check2() = " + One(healthy[0], c => c.Check2()));
            Expect("il solido sano: nessun difetto in Check3", Defects(healthy[0]) == 0,
                     "Check3.Count = " + Defects(healthy[0]));
            Expect("il solido sano: nessuna fessura", Gaps(healthy[0]) == 0,
                     "fessure = " + Gaps(healthy[0]));

            // ── 2. DUE CORPI CHE SI TOCCANO PER UNO SPIGOLO ───────────────
            //
            // E' il caso che la voce GP1 nomina. Il secondo blocco parte
            // esattamente dove finisce il primo, sfalsato: i due si toccano
            // lungo uno spigolo e non si fondono, perche' `Merge` e' false.
            if (!Extrude(doc, 0.05, 0.05, 0.15, 0.15, 0.02, true))
                Console.WriteLine("(il secondo blocco non e' venuto: il caso 2 salta)");
            else
                Tell("due blocchi che si toccano per uno spigolo", Solids(doc));

            // ── 3. UN CORPO DI FOGLIO, CHE SOLIDO NON E' ──────────────────
            //
            // Non e' rotto: e' APERTO. Per una stampa 3D e' lo stesso guaio —
            // uno STL che esce da un foglio non racchiude niente — e la
            // domanda e' se `Check3` lo distingua da un solido.
            var sheet = Surface(doc);
            if (sheet == null)
            {
                // NON E' UNA PROVA SALTATA, E' L'ATTREZZO CHE NON HA FATTO IL
                // SUO LAVORO: senza il foglio non si misura niente di quello
                // per cui esiste, e dirlo verde sarebbe la bugia peggiore.
                Console.Error.WriteLine("la superficie non e' venuta: senza il corpo di foglio " +
                                        "questo attrezzo non misura niente.");
                return 2;
            }
            Tell("un corpo di foglio (superficie piana)", new[] { sheet });
            Console.WriteLine();

            // ── IL ROSSO CHE QUESTO ATTREZZO ESISTE PER TENERE ────────────
            //
            // Un corpo APERTO passa tre controlli su quattro. Se un giorno
            // `Check3` imparasse a vederlo, queste tre righe diventerebbero
            // rosse — ed e' proprio allora che l'operazione va riscritta.
            Expect("il foglio e' un corpo di foglio, non un solido",
                     TypeName(sheet) == "foglio", "tipo " + TypeName(sheet));
            Expect("il foglio: Check() lo dichiara sano lo stesso", One(sheet, c => c.Check()) == 1,
                     "Check() = " + One(sheet, c => c.Check()));
            Expect("il foglio: Check2() lo dichiara sano lo stesso", One(sheet, c => c.Check2()) == 0,
                     "Check2() = " + One(sheet, c => c.Check2()));
            Expect("il foglio: Check3 non trova NIENTE su un corpo aperto", Defects(sheet) == 0,
                     "Check3.Count = " + Defects(sheet));
            Expect("SOLO Diagnose() vede la fessura", Gaps(sheet) >= 1,
                     "fessure = " + Gaps(sheet));

            Console.WriteLine();
            Console.WriteLine("Il pezzo resta aperto: guardalo, poi chiudilo senza salvare.");
            return _broken == 0 ? 0 : 1;
        }

        // ── CHE COSA E' USCITO, IL 15/9/2026, SU SOLIDWORKS 2021 ──────────
        //
        //                          Check()  Check2()  Check3.Count  fessure
        //   solido sano               1        0           0           0
        //   due solidi che si toccano 1        0           0           0   (ognuno)
        //   CORPO DI FOGLIO (aperto)  1        0           0           1
        //
        // DUE COSE, E TUTTE E DUE CAMBIANO COME SI SCRIVE L'OPERAZIONE.
        //
        // 1. `Check()` E `Check2()` HANNO CONVENZIONI OPPOSTE. Sullo stesso
        //    corpo sano il primo dice 1 e il secondo dice 0. Chi legge uno
        //    zero di `Check2()` come «falso, cioe' rotto» — o un 1 di
        //    `Check()` come «un difetto» — ottiene un risultato coerente,
        //    stabile e rovesciato. Era la domanda aperta del RAG 80p punto 4,
        //    e la risposta e' peggio dell'incertezza: le due convenzioni
        //    convivono nella stessa interfaccia.
        //
        // 2. UN CORPO APERTO PASSA TRE CONTROLLI SU QUATTRO. Il foglio non e'
        //    «rotto»: e' un corpo valido che non racchiude niente. `Check`,
        //    `Check2` e `Check3` lo dichiarano sano tutti e tre, e **solo
        //    `Diagnose().GetGapsCount()` lo vede**. Uno STL che esce di li'
        //    non contiene un volume, e si stampa come una lamina.
        //
        //    Un'operazione `check_body_geometry` costruita su `Check3` —
        //    che e' la via che la voce GP1 nominava per prima — direbbe
        //    «nessun problema» esattamente sul caso per cui esiste.
        private static int _broken;

        /// <summary>
        /// Il controllo, non il racconto. Senza questo l'attrezzo stampa una
        /// tabella che nessuno confronta col mese scorso.
        /// </summary>
        private static void Expect(string what, bool real, string seen)
        {
            Console.WriteLine((real ? "  OK   " : "  NO   ") + what.PadRight(56) + seen);
            if (!real) _broken++;
        }

        /// <summary>
        /// Quello che le quattro chiamate dicono, senza interpretarle: i numeri
        /// grezzi accanto ai nomi degli enum, perche' l'interpretazione e' la
        /// cosa che questo attrezzo esiste per NON dare per scontata.
        /// </summary>
        private static void Tell(string label, IBody2[] bodies)
        {
            Console.WriteLine();
            Console.WriteLine("=== " + label + " — " + bodies.Length + " corpi");
            for (int i = 0; i < bodies.Length; i++)
            {
                var c = bodies[i];
                Console.WriteLine("  corpo " + (i + 1) + "  tipo " + TypeName(c));
                Console.WriteLine("    facce            " + Number(() => c.GetFaceCount()));
                Console.WriteLine("    Check()          " + Number(() => c.Check()));
                Console.WriteLine("    Check2()         " + Number(() => c.Check2()));

                try
                {
                    var f = c.Check3 as IFaultEntity;
                    if (f == null) Console.WriteLine("    Check3           null");
                    else
                    {
                        int n = f.Count;
                        Console.WriteLine("    Check3.Count     " + n);
                        for (int k = 0; k < n; k++)
                        {
                            int code = f.get_ErrorCode(k);
                            Console.WriteLine("      difetto " + k + ": codice " + code +
                                              " (" + CodeName(code) + ")");
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine("    Check3           ERRORE " + ex.Message); }

                try
                {
                    var d = c.Diagnose() as IDiagnoseResult;
                    if (d == null) Console.WriteLine("    Diagnose()       null");
                    else Console.WriteLine("    Diagnose fessure " + d.GetGapsCount());
                }
                catch (Exception ex) { Console.WriteLine("    Diagnose()       ERRORE " + ex.Message); }
            }
        }

        private static string Number(Func<int> f)
        {
            try { return f().ToString(); }
            catch (Exception ex) { return "ERRORE " + ex.Message; }
        }

        /// <summary>Un intero da una chiamata COM, o -999 se non si e' potuto.</summary>
        private static int One(IBody2 c, Func<IBody2, int> f)
        {
            try { return f(c); }
            catch { return -999; }
        }

        private static int Defects(IBody2 c)
        {
            try
            {
                var f = c.Check3 as IFaultEntity;
                return f == null ? -1 : f.Count;
            }
            catch { return -999; }
        }

        private static int Gaps(IBody2 c)
        {
            try
            {
                var d = c.Diagnose() as IDiagnoseResult;
                return d == null ? -1 : d.GetGapsCount();
            }
            catch { return -999; }
        }

        private static string TypeName(IBody2 c)
        {
            try
            {
                int t = c.GetType();
                if (t == (int)swBodyType_e.swSolidBody) return "solid";
                if (t == (int)swBodyType_e.swSheetBody) return "foglio";
                if (t == (int)swBodyType_e.swWireBody)  return "filo";
                return t.ToString();
            }
            catch { return "?"; }
        }

        /// <summary>
        /// Il nome dell'enum per un codice di difetto. Lo zero non sta
        /// nell'enum: se compare, e' un valore che la documentazione non
        /// prevede e va detto cosi' invece di chiamarlo «nessun problema».
        /// </summary>
        private static string CodeName(int code)
        {
            try
            {
                if (Enum.IsDefined(typeof(swFaultEntityErrorCode_e), code))
                    return ((swFaultEntityErrorCode_e)code).ToString();
            }
            catch { }
            return "NON E' UN VALORE DI swFaultEntityErrorCode_e";
        }

        private static IBody2[] Solids(IModelDoc2 doc)
        {
            var partDoc = doc as IPartDoc;
            if (partDoc == null) return new IBody2[0];
            var rawItems = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (rawItems == null) return new IBody2[0];
            var outside = new IBody2[rawItems.Length];
            for (int i = 0; i < rawItems.Length; i++) outside[i] = rawItems[i] as IBody2;
            return outside;
        }

        private static bool Extrude(IModelDoc2 doc, double x1, double y1,
                                    double x2, double y2, double thickness, bool separate)
        {
            try
            {
                doc.ClearSelection2(true);
                if (!SelectFrontPlane(doc))
                {
                    Console.WriteLine("  il piano frontale non si e' lasciato selezionare: " +
                                      PlaneNames(doc));
                    return false;
                }
                var sm = doc.SketchManager;
                sm.InsertSketch(true);
                var rect = sm.CreateCornerRectangle(x1, y1, 0, x2, y2, 0) as object[];
                Console.WriteLine("  rettangolo: " + (rect == null ? "null" : rect.Length + " segmenti"));
                sm.InsertSketch(false);
                if (!SelectLastSketch(doc))
                {
                    Console.WriteLine("  lo schizzo appena fatto non si e' lasciato selezionare");
                    return false;
                }

                var f = doc.FeatureManager.FeatureExtrusion3(
                    Sd: true, Flip: false, Dir: false,
                    T1: (int)swEndConditions_e.swEndCondBlind,
                    T2: 0, D1: thickness, D2: 0,
                    Dchk1: false, Dchk2: false, Ddir1: false, Ddir2: false,
                    Dang1: 0, Dang2: 0,
                    OffsetReverse1: false, OffsetReverse2: false,
                    TranslateSurface1: false, TranslateSurface2: false,
                    // MERGE: false vuol dire «lascialo un corpo a se'», ed e'
                    // quello che serve per avere due solidi che si toccano
                    // senza fondersi.
                    Merge: !separate,
                    UseFeatScope: true, UseAutoSelect: true,
                    T0: 0, StartOffset: 0, FlipStartOffset: false);
                return f != null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("estrusione fallita: " + ex.Message);
                return false;
            }
        }

        private static IBody2 Surface(IModelDoc2 doc)
        {
            try
            {
                doc.ClearSelection2(true);
                if (!SelectTopPlane(doc)) return null;
                var sm = doc.SketchManager;
                sm.InsertSketch(true);
                sm.CreateCornerRectangle(-0.03, -0.03, 0, 0.03, 0.03, 0);
                sm.InsertSketch(false);
                if (!SelectLastSketch(doc))
                {
                    Console.WriteLine("  lo schizzo della superficie non si e' lasciato selezionare");
                    return null;
                }
                // STA SU `IModelDoc2` E TORNA UN BOOLEANO, non sul
                // FeatureManager e non una feature: tutte le altre
                // `Insert*RefSurface` che tornano una `Feature` stanno sul
                // FeatureManager, questa no. E' la stessa famiglia di trappole
                // gia' pagata — «la famiglia non si indovina dal nome».
                if (!doc.InsertPlanarRefSurface()) return null;
                var partDoc = doc as IPartDoc;
                var rawItems = partDoc.GetBodies2((int)swBodyType_e.swSheetBody, false) as object[];
                if (rawItems == null || rawItems.Length == 0) return null;
                return rawItems[0] as IBody2;
            }
            catch (Exception ex)
            {
                Console.WriteLine("superficie fallita: " + ex.Message);
                return null;
            }
        }

        // W3.1-bis.2 point 10: this used to try the plane's Italian name and
        // then its English one, because the name only exists in the SOLIDWORKS
        // UI language of the machine that created the part. It now selects by
        // tree position instead (SelectBasePlane below), which needs no
        // language at all.
        private static bool SelectFrontPlane(IModelDoc2 doc)
        {
            return SelectBasePlane(doc, 1);
        }

        private static bool SelectTopPlane(IModelDoc2 doc)
        {
            return SelectBasePlane(doc, 2);
        }

        // The first three RefPlane features of the tree, same rule as
        // Checks/Rib, Checks/Fill and Checks/ExtrusionType.
        // Since Z-C21.5 the library itself (Oce.Solid.Agent.Core/References.cs,
        // BasePlane) recognises a base plane by its canonical normal through the
        // world origin instead, because a template with an extra plane ahead of
        // the three defaults fools a position-based rule with no error; this
        // probe stays positional because it is a throwaway part on SOLIDWORKS'
        // own default template and cannot reference the library (single-file
        // compile, Checks/build-checks.ps1).
        private static bool SelectBasePlane(IModelDoc2 doc, int which)
        {
            try
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
            catch { return false; }
        }

        /// <summary>
        /// Lo schizzo appena chiuso, preso dall'albero e non per nome: dopo il
        /// primo «Schizzo1» i nomi cambiano, e un nome sbagliato qui vuol dire
        /// un'estrusione che non parte senza dire perche'.
        /// </summary>
        private static bool SelectLastSketch(IModelDoc2 doc)
        {
            try
            {
                IFeature last = null;
                var f = (doc as IPartDoc).IFirstFeature();
                while (f != null)
                {
                    try { if (f.GetTypeName2() == "ProfileFeature") last = f; } catch { }
                    f = f.IGetNextFeature();
                }
                if (last == null) return false;
                doc.ClearSelection2(true);
                return doc.Extension.SelectByID2(last.Name, "SKETCH", 0, 0, 0, false, 0, null, 0);
            }
            catch { return false; }
        }

        /// <summary>I nomi dei piani che l'albero ha davvero, per un messaggio.</summary>
        private static string PlaneNames(IModelDoc2 doc)
        {
            try
            {
                var names = new System.Collections.Generic.List<string>();
                var f = (doc as IPartDoc).IFirstFeature();
                while (f != null)
                {
                    try { if (f.GetTypeName2() == "RefPlane") names.Add("«" + f.Name + "»"); } catch { }
                    f = f.IGetNextFeature();
                }
                return names.Count == 0 ? "l'albero non ha piani" : string.Join(", ", names);
            }
            catch (Exception ex) { return "non ho potuto leggere l'albero: " + ex.Message; }
        }

        private static string Preferred(ISldWorks sw)
        {
            try
            {
                var p = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        private static string SearchedOnDisk()
        {
            // `C:\ProgramData\SolidWorks` e' dove SOLIDWORKS mette i suoi
            // modelli su qualunque installazione. Accanto c'era il percorso di
            // un disco di rete di UNA postazione: non serviva a nessun altro e
            // diceva com'e' fatto quel disco. Adesso e' una variabile, e chi
            // tiene i modelli altrove lo dice.
            var folders = new System.Collections.Generic.List<string>
            {
                @"C:\ProgramData\SolidWorks",
            };
            // `System.` per esteso: l'interop di SOLIDWORKS ha un suo
            // `Environment`, e con i due `using` il nome nudo non compila.
            var its = System.Environment.GetEnvironmentVariable("MODELLI_SOLIDWORKS");
            if (!string.IsNullOrWhiteSpace(its)) folders.Add(its);
            foreach (var c in folders)
            {
                if (!Directory.Exists(c)) continue;
                string[] foundItems;
                try { foundItems = Directory.GetFiles(c, "*.prtdot", SearchOption.AllDirectories); }
                catch { continue; }
                foreach (var f in foundItems)
                {
                    string b = f.ToLowerInvariant();
                    if (b.Contains(@"\mbd\")) continue;
                    if (b.Contains("0001mm")) continue;
                    if (b.Contains("ansi-inch") || b.Contains("inch")) continue;
                    return f;
                }
                if (foundItems.Length > 0) return foundItems[0];
            }
            return null;
        }
    }
}
