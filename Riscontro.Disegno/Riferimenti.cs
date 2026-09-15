using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace Riscontro.Disegno
{
    /// <summary>
    /// Traduce «la faccia in cima a quello che hai fatto al passo 1» nella
    /// faccia vera, dentro SolidWorks, dopo che il passo 1 e' stato eseguito.
    ///
    /// SERVE PERCHE' UN PIANO SI SCRIVE TUTTO PRIMA DI ESEGUIRNE UN PEZZO.
    /// Al momento in cui il modello propone il passo 2, la faccia su cui va il
    /// foro non esiste: non ha un identificativo da citare, e nessuno gliene
    /// puo' dare uno. Il riferimento e' il nome che si puo' dare a una cosa
    /// che non c'e' ancora; questo file e' il posto in cui quel nome diventa
    /// una faccia con un'area.
    ///
    /// QUI NON SI INDOVINA MAI. Se un genere non si risolve in modo univoco —
    /// due facce cilindriche dove ne serviva una, nessuna faccia piana in
    /// cima, una tasca passante a cui si chiede il fondo — la risposta e' un
    /// motivo scritto, e il passo fallisce. La faccia «piu' probabile» e'
    /// esattamente il tipo di scelta che qui produce un pezzo sbagliato senza
    /// nessun errore: la lavorazione riesce, il rebuild e' pulito, e il foro
    /// e' dall'altra parte.
    /// </summary>
    public static class Riferimenti
    {
        /// <summary>Coseno oltre il quale due direzioni si dicono la stessa.</summary>
        /// <remarks>
        /// 0,999 e' circa 2,5 gradi. Non e' una tolleranza di misura: le facce
        /// di un'estrusione sono esattamente parallele o esattamente
        /// perpendicolari all'asse, e questo margine serve solo a coprire
        /// l'errore di rappresentazione dei double.
        /// </remarks>
        private const double Coseno = 0.999;

        /// <summary>Che cosa ha lasciato dietro di se' un passo gia' eseguito.</summary>
        public class Passo
        {
            /// <summary>La feature creata da quel passo.</summary>
            public IFeature Feature;

            /// <summary>
            /// La direzione lungo cui quel passo ha estruso o tagliato, in
            /// coordinate del modello. Null se il passo non ne ha una (un
            /// raccordo non estrude in nessuna direzione).
            ///
            /// Non si ricava dalla geometria e non ci si prova: per un
            /// parallelepipedo le sei normali sono +-X, +-Y, +-Z e non c'e'
            /// niente che distingua l'asse di estrusione dagli altri due. Chi
            /// esegue l'operazione la direzione la SA — e' la normale del piano
            /// di schizzo, o la normale della faccia forata — e la scrive qui.
            /// </summary>
            public double[] Direzione;

            /// <summary>Nome dell'operazione, per i messaggi di errore.</summary>
            public string Operazione = "";
        }

        /// <summary>La normale di un piano di riferimento, in coordinate del modello.</summary>
        /// <remarks>
        /// I tre piani si scelgono per ordine nell'albero, non per nome (i nomi
        /// sono localizzati): il primo e' il frontale, il secondo il superiore,
        /// il terzo il destro. Le normali sono quelle dell'orientamento
        /// standard di SolidWorks.
        /// </remarks>
        public static double[] NormaleDelPiano(string piano)
        {
            switch (piano)
            {
                case "superiore": return new double[] { 0, 1, 0 };
                case "destro": return new double[] { 1, 0, 0 };
                default: return new double[] { 0, 0, 1 }; // frontale
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Risoluzione
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// La faccia indicata da un genere della famiglia «faccia».
        /// Ritorna false con un motivo leggibile se non ce n'e' esattamente una.
        /// </summary>
        public static bool Faccia(
            Passo passo, string genere, out IFace2 faccia, out string motivo)
        {
            faccia = null;
            motivo = null;
            var facce = FacceDi(passo, out motivo);
            if (facce == null) return false;

            switch (genere)
            {
                case "faccia_superiore":
                    return Estremo(passo, facce, +1, out faccia, out motivo);
                case "faccia_inferiore":
                    return Estremo(passo, facce, -1, out faccia, out motivo);

                case "faccia_del_foro":
                {
                    // ── UN FORO E' CONCAVO, UN RACCORDO E' CONVESSO ───────
                    //
                    // Fino alla 0.73.0 qui bastava «cilindrica», e un raccordo
                    // lascia facce cilindriche come un foro. Se un passo ne
                    // lasciava **una sola** e quella era un raccordo, questo
                    // ramo la restituiva **senza dire niente**: chi aveva
                    // chiesto la parete di un foro si ritrovava la superficie
                    // di un raccordo, e la lavorazione successiva ci finiva
                    // sopra. Un difetto che non da' errore.
                    //
                    // Misurato da `Controlli\ForoOppureRaccordo` su una piastra
                    // con un foro Ø12 e un raccordo R5:
                    //
                    //     parete del foro         FaceInSurfaceSense = true
                    //     superficie del raccordo FaceInSurfaceSense = false
                    //
                    // La parete di un foro ha il materiale FUORI dal cilindro,
                    // e la sua normale guarda verso l'asse: e' concava.
                    var concave = new List<IFace2>();
                    int convesse = 0;
                    foreach (var f in facce)
                    {
                        if (!Cilindrica(f)) continue;
                        if (Concava(f)) concave.Add(f); else convesse++;
                    }

                    if (concave.Count == 0)
                    {
                        motivo = convesse > 0
                            ? "Il passo «" + passo.Operazione + "» ha lasciato " +
                              convesse + " fac" + (convesse == 1 ? "cia" : "ce") +
                              " cilindric" + (convesse == 1 ? "a" : "he") +
                              ", ma convess" + (convesse == 1 ? "a" : "e") + ": " +
                              "sono superfici di raccordo o di un cilindro pieno, " +
                              "non pareti di foro. La parete di un foro e' " +
                              "concava — il materiale sta fuori."
                            : "Il passo «" + passo.Operazione + "» non ha lasciato " +
                              "nessuna faccia cilindrica: non c'e' nessuna parete " +
                              "di foro da prendere.";
                        return false;
                    }

                    // PIU' DI UNA PARETE DI FORO RESTA UN RIFIUTO, e non e' una
                    // mancanza da riparare.
                    //
                    // La voce E14 proponeva di sceglierne una «con un criterio —
                    // il diametro, oppure la posizione». Fra quattro M10 uguali
                    // non esiste nessun criterio: qualunque cosa si scelga e'
                    // **indovinare**, e il passo seguente finirebbe su un foro a
                    // caso senza che niente fallisca. E' esattamente il difetto
                    // che questo progetto esiste per rendere impossibile, offerto
                    // come comodita'.
                    //
                    // Il rifiuto pero' adesso dice quanti sono e di che
                    // diametro: chi legge ha gia' in mano cio' che gli serve per
                    // nominarne uno.
                    if (concave.Count > 1)
                    {
                        motivo =
                            "Il passo «" + passo.Operazione + "» ha lasciato " +
                            concave.Count + " pareti di foro (" + Diametri(concave) +
                            "): «la» parete del foro non esiste. Rileggi il pezzo e " +
                            "indica quale, per identificativo.";
                        return false;
                    }
                    faccia = concave[0];
                    return true;
                }

                case "faccia_di_fondo":
                {
                    // Il fondo di una tasca e' la faccia piana perpendicolare
                    // alla direzione del taglio. Se la tasca e' passante non
                    // esiste, e dirlo e' l'unica risposta corretta: la faccia
                    // «piu' simile» sarebbe una parete laterale, e la
                    // lavorazione successiva finirebbe di traverso.
                    if (passo.Direzione == null)
                    {
                        motivo =
                            "Del passo «" + passo.Operazione + "» non conosco la " +
                            "direzione del taglio, quindi non so dire qual e' il fondo.";
                        return false;
                    }
                    var piane = new List<IFace2>();
                    foreach (var f in facce)
                    {
                        var n = Normale(f);
                        if (n != null && Math.Abs(Prodotto(n, passo.Direzione)) > Coseno)
                            piane.Add(f);
                    }
                    if (piane.Count != 1)
                    {
                        motivo = piane.Count == 0
                            ? "La tasca del passo «" + passo.Operazione + "» non ha " +
                              "un fondo: e' passante."
                            : "La tasca del passo «" + passo.Operazione + "» ha " +
                              piane.Count + " facce che potrebbero essere il fondo: " +
                              "indica la faccia per identificativo.";
                        return false;
                    }
                    faccia = piane[0];
                    return true;
                }

                default:
                    motivo = "Genere di faccia sconosciuto: «" + genere + "».";
                    return false;
            }
        }

        /// <summary>
        /// Gli spigoli indicati da un genere della famiglia «spigoli».
        ///
        /// Gli spigoli NON si deduplicano, ed e' voluto: uno spigolo appartiene
        /// a due facce, quindi scorrendo le facce lo si incontra due volte.
        /// Selezionarlo due volte in SolidWorks non lo aggiunge due volte, e
        /// confrontare l'identita' di due oggetti COM per evitarlo sarebbe piu'
        /// fragile del problema che risolve. Il numero VERO di spigoli si
        /// chiede dopo a SolidWorks, con GetSelectedObjectCount2.
        /// </summary>
        public static bool Spigoli(
            Passo passo, string genere, out List<IEntity> spigoli, out string motivo)
        {
            spigoli = new List<IEntity>();
            motivo = null;
            var facce = FacceDi(passo, out motivo);
            if (facce == null) return false;

            if (genere == "spigoli_superiori" || genere == "spigoli_inferiori")
            {
                IFace2 cima;
                if (!Estremo(passo, facce, genere == "spigoli_superiori" ? +1 : -1,
                             out cima, out motivo))
                    return false;

                // SOLO IL LOOP ESTERNO. IFace2.GetEdges() restituisce gli
                // spigoli di TUTTI i loop, e una faccia in cima puo' averne
                // piu' d'uno: basta che un passo precedente ci abbia fatto un
                // foro, o che il profilo sia cavo.
                //
                // Il difetto che questo chiude: «piastra 100x60x8, un foro da
                // 12 al centro, arrotonda con R2 gli spigoli in cima». Il
                // pannello mostrava «raccordo R2 su spigoli superiori del
                // passo 1» — esattamente cio' che era stato chiesto — e
                // venivano raccordati anche i bordi del foro. Su una piastra
                // da 8 la parte cilindrica utile scendeva da 8 a 6 mm e la
                // testa della vite non appoggiava piu'. Nessun errore, rebuild
                // pulito, passo «riuscito». E lo stesso identico testo con i
                // passi in ordine inverso dava il pezzo giusto.
                if (!SpigoliDelBordoEsterno(cima, spigoli, out motivo)) return false;
            }
            else if (genere == "spigoli_laterali")
            {
                if (passo.Direzione == null)
                {
                    motivo =
                        "Del passo «" + passo.Operazione + "» non conosco la " +
                        "direzione di estrusione, quindi non so dire quali " +
                        "spigoli siano i laterali.";
                    return false;
                }
                // Uno spigolo e' «laterale» se corre lungo la direzione di
                // estrusione. Si guarda la sua direzione, non a quale faccia
                // appartiene: cosi' non serve confrontare identita' di facce.
                foreach (var f in facce)
                {
                    var elenco = new List<IEntity>();
                    AggiungiSpigoli(f, elenco);
                    foreach (var e in elenco)
                    {
                        var d = DirezioneSpigolo(e as IEdge);
                        if (d != null && Math.Abs(Prodotto(d, passo.Direzione)) > Coseno)
                            spigoli.Add(e);
                    }
                }
            }
            else if (genere == "tutti_gli_spigoli")
            {
                foreach (var f in facce) AggiungiSpigoli(f, spigoli);
            }
            else
            {
                motivo = "Genere di spigoli sconosciuto: «" + genere + "».";
                return false;
            }

            if (spigoli.Count == 0)
            {
                motivo =
                    "Il passo «" + passo.Operazione + "» non ha lasciato nessuno " +
                    "spigolo del genere «" + genere + "».";
                return false;
            }
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // Saldature: i corpi e le estremita' di un passo
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// I CORPI creati da un passo — le barre di un gruppo di membri
        /// strutturali.
        ///
        /// PERCHE' SI PASSA DALLE FACCE E NON SI CHIEDONO I CORPI DIRETTAMENTE
        /// `IFeature` non ha un «dammi i corpi che hai creato». Ha `GetFaces()`,
        /// e ogni faccia sa a quale corpo appartiene (`IFace2.GetBody()`). Si
        /// risale di li', raccogliendo i corpi distinti.
        ///
        /// Il confronto fra corpi si fa sull'identita' COM: due facce dello
        /// stesso corpo restituiscono lo stesso oggetto, e un corpo contato due
        /// volte diventerebbe una barra contata due volte nella distinta.
        /// </summary>
        public static List<IBody2> CorpiDi(Passo passo, out string motivo)
        {
            motivo = null;
            var facce = FacceDi(passo, out motivo);
            if (facce == null) return null;

            var corpi = new List<IBody2>();
            foreach (var f in facce)
            {
                IBody2 corpo;
                try { corpo = f.GetBody() as IBody2; }
                catch { continue; }
                if (corpo == null) continue;
                bool gia = false;
                foreach (var c in corpi)
                {
                    if (ReferenceEquals(c, corpo) || Equals(c, corpo)) { gia = true; break; }
                }
                if (!gia) corpi.Add(corpo);
            }

            if (corpi.Count == 0)
            {
                motivo =
                    "Il passo «" + passo.Operazione + "» non ha lasciato nessun " +
                    "corpo: non ci sono barre su cui lavorare.";
                return null;
            }
            return corpi;
        }

        /// <summary>
        /// Le facce di ESTREMITA' ancora aperte delle barre di un passo:
        /// quelle che si tappano.
        ///
        /// COME SI RICONOSCE UN'ESTREMITA', E PERCHE' NON SI INDOVINA
        /// Una barra e' un profilo estruso lungo un segmento. Le facce che
        /// contano sono quelle PIANE la cui normale corre lungo l'asse della
        /// barra: sono i due tagli. Tutto il resto e' la superficie laterale.
        ///
        /// L'asse della barra qui non lo sappiamo (i segmenti sono nel piano,
        /// non nella feature), quindi si usa un invariante piu' robusto: una
        /// faccia di estremita' di un profilo CAVO ha piu' di un contorno —
        /// il perimetro esterno e quello del vuoto. E' esattamente cio' che si
        /// tappa. Su un profilo pieno di estremita' da tappare non ce ne sono,
        /// e dirlo e' la risposta giusta.
        ///
        /// Le facce tagliate a quartabuono restano piane e cave: si tappano
        /// anche quelle, ed e' corretto — un telaio con gli angoli a 45 ha
        /// comunque le estremita' aperte dove non incontra un'altra barra.
        /// </summary>
        public static List<IFace2> EstremitaLibere(Passo passo, out string motivo)
        {
            motivo = null;
            var facce = FacceDi(passo, out motivo);
            if (facce == null) return null;
            return SoloEstremita(facce);
        }

        /// <summary>
        /// Le estremita' aperte di CORPI gia' nel documento, non di un passo.
        ///
        /// PERCHE' SERVE UNA SECONDA STRADA
        /// `EstremitaLibere(Passo)` funziona solo dentro lo stesso piano: cita
        /// il passo che ha creato le barre. In un messaggio NUOVO — «tappa le
        /// estremita' del telaio che hai fatto prima» — quel passo non c'e'
        /// piu', e fino alla 0.29.1 la richiesta non aveva nessuna forma
        /// valida: il modello poteva solo ridisegnare le barre da capo, e
        /// sarebbero uscite barre sovrapposte.
        ///
        /// Adesso lo stato del documento porta i corpi con il loro
        /// identificativo, e questa e' la strada che li usa.
        /// </summary>
        public static List<IFace2> EstremitaLibereDi(
            IEnumerable<IBody2> corpi, out string motivo)
        {
            motivo = null;
            var facce = new List<IFace2>();
            foreach (var corpo in corpi)
            {
                object[] elenco;
                try { elenco = corpo.GetFaces() as object[]; }
                catch { continue; }
                if (elenco == null) continue;
                foreach (var o in elenco)
                {
                    var f = o as IFace2;
                    if (f != null) facce.Add(f);
                }
            }
            if (facce.Count == 0)
            {
                motivo = "I corpi indicati non hanno facce leggibili.";
                return null;
            }
            return SoloEstremita(facce);
        }

        /// <summary>
        /// Fra un mucchio di facce, quelle che sono estremita' aperte di un
        /// profilo cavo: PIANE e con piu' di un contorno (il perimetro esterno
        /// e quello del vuoto). Su un profilo pieno non ce ne sono, e dirlo e'
        /// la risposta giusta.
        /// </summary>
        private static List<IFace2> SoloEstremita(IEnumerable<IFace2> facce)
        {
            var fuori = new List<IFace2>();
            foreach (var f in facce)
            {
                if (Normale(f) == null) continue;  // non piana: e' il fianco
                int contorni;
                try { contorni = f.GetLoopCount(); }
                catch { continue; }
                if (contorni > 1) fuori.Add(f);
            }
            return fuori;
        }

        // ─────────────────────────────────────────────────────────────────
        // Geometria di supporto
        // ─────────────────────────────────────────────────────────────────

        private static IFace2[] FacceDi(Passo passo, out string motivo)
        {
            motivo = null;
            if (passo == null || passo.Feature == null)
            {
                motivo =
                    "Il passo a cui ti riferisci non ha lasciato nessuna " +
                    "geometria da nominare.";
                return null;
            }
            object[] grezze;
            try { grezze = passo.Feature.GetFaces() as object[]; }
            catch (Exception ex)
            {
                motivo = "Non riesco a leggere le facce del passo precedente: " + ex.Message;
                return null;
            }
            if (grezze == null || grezze.Length == 0)
            {
                motivo =
                    "Il passo «" + passo.Operazione + "» non ha lasciato facce " +
                    "proprie: non c'e' niente da nominare.";
                return null;
            }
            var facce = new List<IFace2>();
            foreach (var g in grezze)
            {
                var f = g as IFace2;
                if (f != null) facce.Add(f);
            }
            if (facce.Count == 0)
            {
                motivo = "Le facce del passo precedente non sono leggibili.";
                return null;
            }
            return facce.ToArray();
        }

        /// <summary>
        /// La faccia piana perpendicolare alla direzione, all'estremo indicato
        /// (+1 = quella piu' avanti lungo la direzione, -1 = quella piu' indietro).
        /// </summary>
        private static bool Estremo(
            Passo passo, IFace2[] facce, int verso, out IFace2 scelta, out string motivo)
        {
            scelta = null;
            motivo = null;
            if (passo.Direzione == null)
            {
                motivo =
                    "Del passo «" + passo.Operazione + "» non conosco la direzione " +
                    "di estrusione, quindi non so dire quale faccia sia in cima. " +
                    "Indica la faccia per identificativo.";
                return false;
            }

            double miglioreProiezione = 0;
            foreach (var f in facce)
            {
                var n = Normale(f);
                if (n == null) continue;
                if (Math.Abs(Prodotto(n, passo.Direzione)) <= Coseno) continue;

                double[] box;
                try { box = f.GetBox() as double[]; }
                catch { continue; }
                if (box == null || box.Length < 6) continue;
                var centro = new double[]
                {
                    (box[0] + box[3]) / 2,
                    (box[1] + box[4]) / 2,
                    (box[2] + box[5]) / 2,
                };
                double proiezione = Prodotto(centro, passo.Direzione) * verso;
                if (scelta == null || proiezione > miglioreProiezione)
                {
                    scelta = f;
                    miglioreProiezione = proiezione;
                }
            }

            if (scelta == null)
            {
                motivo =
                    "Il passo «" + passo.Operazione + "» non ha lasciato nessuna " +
                    "faccia piana perpendicolare alla direzione di estrusione: " +
                    (verso > 0 ? "non c'e' una faccia in cima." : "non c'e' un fondo.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Gli spigoli del solo bordo ESTERNO di una faccia.
        ///
        /// «Gli spigoli in cima» vuol dire il perimetro, non i bordi dei fori
        /// che ci sono stati aperti dentro. Distinguerli non e' una finezza:
        /// raccordare la bocca di un foro accorcia la parte cilindrica utile,
        /// e chi ha confermato «spigoli superiori» non ha confermato quello.
        /// </summary>
        private static bool SpigoliDelBordoEsterno(
            IFace2 faccia, List<IEntity> dentro, out string motivo)
        {
            motivo = null;
            object[] loop;
            try { loop = faccia.GetLoops() as object[]; }
            catch (Exception ex)
            {
                motivo = "Non riesco a leggere i bordi della faccia: " + ex.Message;
                return false;
            }
            if (loop == null || loop.Length == 0)
            {
                motivo = "La faccia non ha bordi leggibili.";
                return false;
            }

            int esterni = 0;
            foreach (var g in loop)
            {
                var l = g as ILoop2;
                if (l == null) continue;
                bool fuori;
                try { fuori = l.IsOuter(); }
                catch { continue; }
                if (!fuori) continue;
                esterni++;

                object[] grezzi;
                try { grezzi = l.GetEdges() as object[]; }
                catch { continue; }
                if (grezzi == null) continue;
                foreach (var e in grezzi)
                {
                    var ent = e as IEntity;
                    if (ent != null) dentro.Add(ent);
                }
            }

            if (esterni == 0)
            {
                // Qui non si ripiega su «prendi tutti gli spigoli»: sarebbe
                // esattamente il difetto che questo metodo esiste per chiudere.
                motivo =
                    "Non ho trovato il bordo esterno della faccia, quindi non " +
                    "so distinguerlo dai bordi dei fori. Indica gli spigoli " +
                    "per identificativo.";
                return false;
            }
            return true;
        }

        private static void AggiungiSpigoli(IFace2 faccia, List<IEntity> dentro)
        {
            object[] grezzi;
            try { grezzi = faccia.GetEdges() as object[]; }
            catch { return; }
            if (grezzi == null) return;
            foreach (var g in grezzi)
            {
                var e = g as IEntity;
                if (e != null) dentro.Add(e);
            }
        }

        /// <summary>La normale uscente di una faccia PIANA, o null se non e' piana.</summary>
        private static double[] Normale(IFace2 faccia)
        {
            try
            {
                var superficie = faccia.GetSurface() as ISurface;
                if (superficie == null || !superficie.IsPlane()) return null;
                // Face2.Normal e' gia' la normale USCENTE della faccia: tiene
                // conto del verso con cui la faccia usa la sua superficie.
                // Prendere PlaneParams della superficie darebbe la normale
                // della superficie, che per meta' delle facce di un solido
                // punta dentro.
                return Normalizza(faccia.Normal as double[]);
            }
            catch { return null; }
        }

        private static bool Cilindrica(IFace2 faccia)
        {
            try
            {
                var superficie = faccia.GetSurface() as ISurface;
                return superficie != null && superficie.IsCylinder();
            }
            catch { return false; }
        }

        /// <summary>
        /// Una faccia cilindrica e' CONCAVA — cioe' la parete di un foro?
        ///
        /// `FaceInSurfaceSense()` dice se la normale della faccia e' rovesciata
        /// rispetto a quella della superficie. Per un cilindro la normale
        /// naturale punta in fuori dall'asse: se la faccia la rovescia, il
        /// materiale sta FUORI dal cilindro, ed e' un foro.
        ///
        /// Misurato, non dedotto (`Controlli\ForoOppureRaccordo`, 15/9/2026):
        /// su una piastra con un foro Ø12 e un raccordo R5, la parete del foro
        /// risponde **true** e la superficie del raccordo **false**.
        ///
        /// Nel dubbio si risponde **false**, cioe' «non e' un foro»: il costo
        /// di sbagliare nei due versi non e' lo stesso. Scambiare un raccordo
        /// per un foro fa finire una lavorazione sulla faccia sbagliata senza
        /// che niente fallisca; scambiare un foro per un raccordo fa uscire un
        /// rifiuto, che si legge.
        /// </summary>
        private static bool Concava(IFace2 faccia)
        {
            try { return faccia.FaceInSurfaceSense(); }
            catch { return false; }
        }

        /// <summary>I diametri di queste facce cilindriche, per il rifiuto.</summary>
        private static string Diametri(List<IFace2> facce)
        {
            var visti = new List<string>();
            foreach (var f in facce)
            {
                try
                {
                    var par = (f.GetSurface() as ISurface)?.CylinderParams as double[];
                    if (par == null || par.Length < 7) continue;
                    string d = "Ø" + Unita.Mm(par[6] * 2).ToString(
                        "0.##", System.Globalization.CultureInfo.InvariantCulture);
                    if (!visti.Contains(d)) visti.Add(d);
                }
                catch { }
            }
            return visti.Count == 0 ? "diametri non leggibili" : string.Join(", ", visti);
        }

        /// <summary>La direzione di uno spigolo RETTILINEO, o null se non lo e'.</summary>
        private static double[] DirezioneSpigolo(IEdge spigolo)
        {
            if (spigolo == null) return null;
            try
            {
                var a = (spigolo.GetStartVertex() as IVertex)?.GetPoint() as double[];
                var b = (spigolo.GetEndVertex() as IVertex)?.GetPoint() as double[];
                if (a == null || b == null || a.Length < 3 || b.Length < 3) return null;
                var d = new double[] { b[0] - a[0], b[1] - a[1], b[2] - a[2] };
                return Normalizza(d);
            }
            catch { return null; }
        }

        private static double[] Normalizza(double[] v)
        {
            if (v == null || v.Length < 3) return null;
            double n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (n < 1e-12) return null;
            return new double[] { v[0] / n, v[1] / n, v[2] / n };
        }

        private static double Prodotto(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }
    }
}
