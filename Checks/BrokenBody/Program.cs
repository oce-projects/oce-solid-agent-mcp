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
    internal static class Programma
    {
        private static int Main()
        {
            object grezzo;
            try { grezzo = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException)
            {
                Console.Error.WriteLine("SOLIDWORKS non e' aperto.");
                return 2;
            }
            var sw = (ISldWorks)grezzo;

            string modello = Preferito(sw) ?? CercatoSulDisco();
            if (modello == null)
            {
                Console.Error.WriteLine("Nessun modello di parte (.prtdot) trovato.");
                return 2;
            }

            var doc = (IModelDoc2)sw.NewDocument(modello, 0, 0, 0);
            if (doc == null)
            {
                Console.Error.WriteLine("NewDocument ha dato null.");
                return 2;
            }
            Console.WriteLine("pezzo di prova: " + doc.GetTitle());

            // ── 1. IL SANO: un parallelepipedo 100 x 100 x 20 ─────────────
            if (!Estrudi(doc, -0.05, -0.05, 0.05, 0.05, 0.02, false))
            {
                Console.Error.WriteLine("non sono riuscito a estrudere il corpo sano.");
                return 2;
            }
            var sani = Solidi(doc);
            Racconta("il sano: 100 x 100 x 20 estruso", sani);
            Console.WriteLine();
            Pretendi("il solido sano: Check() dice 1, non 0", Uno(sani[0], c => c.Check()) == 1,
                     "Check() = " + Uno(sani[0], c => c.Check()));
            Pretendi("il solido sano: Check2() dice 0, non 1", Uno(sani[0], c => c.Check2()) == 0,
                     "Check2() = " + Uno(sani[0], c => c.Check2()));
            Pretendi("il solido sano: nessun difetto in Check3", Difetti(sani[0]) == 0,
                     "Check3.Count = " + Difetti(sani[0]));
            Pretendi("il solido sano: nessuna fessura", Fessure(sani[0]) == 0,
                     "fessure = " + Fessure(sani[0]));

            // ── 2. DUE CORPI CHE SI TOCCANO PER UNO SPIGOLO ───────────────
            //
            // E' il caso che la voce GP1 nomina. Il secondo blocco parte
            // esattamente dove finisce il primo, sfalsato: i due si toccano
            // lungo uno spigolo e non si fondono, perche' `Merge` e' false.
            if (!Estrudi(doc, 0.05, 0.05, 0.15, 0.15, 0.02, true))
                Console.WriteLine("(il secondo blocco non e' venuto: il caso 2 salta)");
            else
                Racconta("due blocchi che si toccano per uno spigolo", Solidi(doc));

            // ── 3. UN CORPO DI FOGLIO, CHE SOLIDO NON E' ──────────────────
            //
            // Non e' rotto: e' APERTO. Per una stampa 3D e' lo stesso guaio —
            // uno STL che esce da un foglio non racchiude niente — e la
            // domanda e' se `Check3` lo distingua da un solido.
            var foglio = Superficie(doc);
            if (foglio == null)
            {
                // NON E' UNA PROVA SALTATA, E' L'ATTREZZO CHE NON HA FATTO IL
                // SUO LAVORO: senza il foglio non si misura niente di quello
                // per cui esiste, e dirlo verde sarebbe la bugia peggiore.
                Console.Error.WriteLine("la superficie non e' venuta: senza il corpo di foglio " +
                                        "questo attrezzo non misura niente.");
                return 2;
            }
            Racconta("un corpo di foglio (superficie piana)", new[] { foglio });
            Console.WriteLine();

            // ── IL ROSSO CHE QUESTO ATTREZZO ESISTE PER TENERE ────────────
            //
            // Un corpo APERTO passa tre controlli su quattro. Se un giorno
            // `Check3` imparasse a vederlo, queste tre righe diventerebbero
            // rosse — ed e' proprio allora che l'operazione va riscritta.
            Pretendi("il foglio e' un corpo di foglio, non un solido",
                     NomeDelTipo(foglio) == "foglio", "tipo " + NomeDelTipo(foglio));
            Pretendi("il foglio: Check() lo dichiara sano lo stesso", Uno(foglio, c => c.Check()) == 1,
                     "Check() = " + Uno(foglio, c => c.Check()));
            Pretendi("il foglio: Check2() lo dichiara sano lo stesso", Uno(foglio, c => c.Check2()) == 0,
                     "Check2() = " + Uno(foglio, c => c.Check2()));
            Pretendi("il foglio: Check3 non trova NIENTE su un corpo aperto", Difetti(foglio) == 0,
                     "Check3.Count = " + Difetti(foglio));
            Pretendi("SOLO Diagnose() vede la fessura", Fessure(foglio) >= 1,
                     "fessure = " + Fessure(foglio));

            Console.WriteLine();
            Console.WriteLine("Il pezzo resta aperto: guardalo, poi chiudilo senza salvare.");
            return _rotti == 0 ? 0 : 1;
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
        private static int _rotti;

        /// <summary>
        /// Il controllo, non il racconto. Senza questo l'attrezzo stampa una
        /// tabella che nessuno confronta col mese scorso.
        /// </summary>
        private static void Pretendi(string cosa, bool vero, string visto)
        {
            Console.WriteLine((vero ? "  OK   " : "  NO   ") + cosa.PadRight(56) + visto);
            if (!vero) _rotti++;
        }

        /// <summary>
        /// Quello che le quattro chiamate dicono, senza interpretarle: i numeri
        /// grezzi accanto ai nomi degli enum, perche' l'interpretazione e' la
        /// cosa che questo attrezzo esiste per NON dare per scontata.
        /// </summary>
        private static void Racconta(string etichetta, IBody2[] corpi)
        {
            Console.WriteLine();
            Console.WriteLine("=== " + etichetta + " — " + corpi.Length + " corpi");
            for (int i = 0; i < corpi.Length; i++)
            {
                var c = corpi[i];
                Console.WriteLine("  corpo " + (i + 1) + "  tipo " + NomeDelTipo(c));
                Console.WriteLine("    facce            " + Numero(() => c.GetFaceCount()));
                Console.WriteLine("    Check()          " + Numero(() => c.Check()));
                Console.WriteLine("    Check2()         " + Numero(() => c.Check2()));

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
                            int codice = f.get_ErrorCode(k);
                            Console.WriteLine("      difetto " + k + ": codice " + codice +
                                              " (" + NomeDelCodice(codice) + ")");
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

        private static string Numero(Func<int> f)
        {
            try { return f().ToString(); }
            catch (Exception ex) { return "ERRORE " + ex.Message; }
        }

        /// <summary>Un intero da una chiamata COM, o -999 se non si e' potuto.</summary>
        private static int Uno(IBody2 c, Func<IBody2, int> f)
        {
            try { return f(c); }
            catch { return -999; }
        }

        private static int Difetti(IBody2 c)
        {
            try
            {
                var f = c.Check3 as IFaultEntity;
                return f == null ? -1 : f.Count;
            }
            catch { return -999; }
        }

        private static int Fessure(IBody2 c)
        {
            try
            {
                var d = c.Diagnose() as IDiagnoseResult;
                return d == null ? -1 : d.GetGapsCount();
            }
            catch { return -999; }
        }

        private static string NomeDelTipo(IBody2 c)
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
        private static string NomeDelCodice(int codice)
        {
            try
            {
                if (Enum.IsDefined(typeof(swFaultEntityErrorCode_e), codice))
                    return ((swFaultEntityErrorCode_e)codice).ToString();
            }
            catch { }
            return "NON E' UN VALORE DI swFaultEntityErrorCode_e";
        }

        private static IBody2[] Solidi(IModelDoc2 doc)
        {
            var parte = doc as IPartDoc;
            if (parte == null) return new IBody2[0];
            var grezzi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (grezzi == null) return new IBody2[0];
            var fuori = new IBody2[grezzi.Length];
            for (int i = 0; i < grezzi.Length; i++) fuori[i] = grezzi[i] as IBody2;
            return fuori;
        }

        private static bool Estrudi(IModelDoc2 doc, double x1, double y1,
                                    double x2, double y2, double spessore, bool separato)
        {
            try
            {
                doc.ClearSelection2(true);
                if (!SelezionaPianoFrontale(doc))
                {
                    Console.WriteLine("  il piano frontale non si e' lasciato selezionare: " +
                                      NomiDeiPiani(doc));
                    return false;
                }
                var sm = doc.SketchManager;
                sm.InsertSketch(true);
                var rett = sm.CreateCornerRectangle(x1, y1, 0, x2, y2, 0) as object[];
                Console.WriteLine("  rettangolo: " + (rett == null ? "null" : rett.Length + " segmenti"));
                sm.InsertSketch(false);
                if (!SelezionaUltimoSchizzo(doc))
                {
                    Console.WriteLine("  lo schizzo appena fatto non si e' lasciato selezionare");
                    return false;
                }

                var f = doc.FeatureManager.FeatureExtrusion3(
                    Sd: true, Flip: false, Dir: false,
                    T1: (int)swEndConditions_e.swEndCondBlind,
                    T2: 0, D1: spessore, D2: 0,
                    Dchk1: false, Dchk2: false, Ddir1: false, Ddir2: false,
                    Dang1: 0, Dang2: 0,
                    OffsetReverse1: false, OffsetReverse2: false,
                    TranslateSurface1: false, TranslateSurface2: false,
                    // MERGE: false vuol dire «lascialo un corpo a se'», ed e'
                    // quello che serve per avere due solidi che si toccano
                    // senza fondersi.
                    Merge: !separato,
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

        private static IBody2 Superficie(IModelDoc2 doc)
        {
            try
            {
                doc.ClearSelection2(true);
                if (!SelezionaPianoSuperiore(doc)) return null;
                var sm = doc.SketchManager;
                sm.InsertSketch(true);
                sm.CreateCornerRectangle(-0.03, -0.03, 0, 0.03, 0.03, 0);
                sm.InsertSketch(false);
                if (!SelezionaUltimoSchizzo(doc))
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
                var parte = doc as IPartDoc;
                var grezzi = parte.GetBodies2((int)swBodyType_e.swSheetBody, false) as object[];
                if (grezzi == null || grezzi.Length == 0) return null;
                return grezzi[0] as IBody2;
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
        private static bool SelezionaPianoFrontale(IModelDoc2 doc)
        {
            return SelectBasePlane(doc, 1);
        }

        private static bool SelezionaPianoSuperiore(IModelDoc2 doc)
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
        private static bool SelezionaUltimoSchizzo(IModelDoc2 doc)
        {
            try
            {
                IFeature ultimo = null;
                var f = (doc as IPartDoc).IFirstFeature();
                while (f != null)
                {
                    try { if (f.GetTypeName2() == "ProfileFeature") ultimo = f; } catch { }
                    f = f.IGetNextFeature();
                }
                if (ultimo == null) return false;
                doc.ClearSelection2(true);
                return doc.Extension.SelectByID2(ultimo.Name, "SKETCH", 0, 0, 0, false, 0, null, 0);
            }
            catch { return false; }
        }

        /// <summary>I nomi dei piani che l'albero ha davvero, per un messaggio.</summary>
        private static string NomiDeiPiani(IModelDoc2 doc)
        {
            try
            {
                var nomi = new System.Collections.Generic.List<string>();
                var f = (doc as IPartDoc).IFirstFeature();
                while (f != null)
                {
                    try { if (f.GetTypeName2() == "RefPlane") nomi.Add("«" + f.Name + "»"); } catch { }
                    f = f.IGetNextFeature();
                }
                return nomi.Count == 0 ? "l'albero non ha piani" : string.Join(", ", nomi);
            }
            catch (Exception ex) { return "non ho potuto leggere l'albero: " + ex.Message; }
        }

        private static string Preferito(ISldWorks sw)
        {
            try
            {
                var p = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        private static string CercatoSulDisco()
        {
            // `C:\ProgramData\SolidWorks` e' dove SOLIDWORKS mette i suoi
            // modelli su qualunque installazione. Accanto c'era il percorso di
            // un disco di rete di UNA postazione: non serviva a nessun altro e
            // diceva com'e' fatto quel disco. Adesso e' una variabile, e chi
            // tiene i modelli altrove lo dice.
            var cartelle = new System.Collections.Generic.List<string>
            {
                @"C:\ProgramData\SolidWorks",
            };
            // `System.` per esteso: l'interop di SOLIDWORKS ha un suo
            // `Environment`, e con i due `using` il nome nudo non compila.
            var sua = System.Environment.GetEnvironmentVariable("MODELLI_SOLIDWORKS");
            if (!string.IsNullOrWhiteSpace(sua)) cartelle.Add(sua);
            foreach (var c in cartelle)
            {
                if (!Directory.Exists(c)) continue;
                string[] trovati;
                try { trovati = Directory.GetFiles(c, "*.prtdot", SearchOption.AllDirectories); }
                catch { continue; }
                foreach (var f in trovati)
                {
                    string b = f.ToLowerInvariant();
                    if (b.Contains(@"\mbd\")) continue;
                    if (b.Contains("0001mm")) continue;
                    if (b.Contains("ansi-inch") || b.Contains("inch")) continue;
                    return f;
                }
                if (trovati.Length > 0) return trovati[0];
            }
            return null;
        }
    }
}
