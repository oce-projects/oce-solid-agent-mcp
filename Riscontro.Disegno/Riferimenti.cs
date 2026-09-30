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
        /// The canonical normal of each base plane, and since Z-C21.5 also the
        /// rule that FINDS the plane (<see cref="BasePlane"/>): the plane of
        /// that name is the reference plane with this normal, same sign,
        /// through the world origin. Not the first, second or third RefPlane of
        /// the tree, and not a name (the names are localized). Measured on the
        /// template by the Z-C21.3 probe of C3-6: front (0, 0, 1), top
        /// (0, 1, 0), right (1, 0, 0).
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
        // The three base planes, found by their geometry (scheme S19)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The cosine above which a plane's normal is the canonical one. The
        /// base planes read (0, 0, 1), (0, 1, 0) and (1, 0, 0) exactly (probe of
        /// C3-6), so this only absorbs the representation error of doubles: a
        /// plane turned by a tenth of a degree is not a base plane.
        /// </summary>
        private const double BasePlaneCosine = 0.999999;

        /// <summary>
        /// How far from the world origin, in metres, a base plane may pass. The
        /// three base planes pass through it exactly (probe of C3-6: 0 mm), an
        /// offset plane parallel to one of them does not (20 mm in the probe).
        /// </summary>
        private const double BasePlaneOffsetM = 1e-8;

        /// <summary>
        /// ONE OF THE THREE BASE PLANES, RECOGNISED BY ITS GEOMETRY (scheme S19:
        /// findings L10-6, C3-6 and C3-6 extended; Z-C21.5).
        ///
        /// Until Z-C21.5 the bridge took «frontale / superiore / destro» as the
        /// first / second / third RefPlane of the tree, in four places
        /// (TrovaPianoBase, SelezionaPiano, the section of VistaDelPezzo and
        /// specchia_componente). IPartDoc~IFirstFeature.html: «This method
        /// returns features in the current order for the part, and this order
        /// changes when the part is edited»; the same page rules out the names
        /// too («you cannot assume that the first reference plane feature is
        /// named Plane1»). A part template with one plane more before the three
        /// default ones moved every sketch, offset plane, mirror and section of
        /// the catalogue onto the wrong plane, with no error.
        ///
        /// THE RULE is in <see cref="BasePlaneIndex"/>: the plane whose normal
        /// is the canonical one of that name (<see cref="NormaleDelPiano"/>),
        /// with the same sign, and which passes through the world origin. The
        /// normal and the origin come from IRefPlane.Transform
        /// (<see cref="PlaneFrame"/>).
        ///
        /// Parts and assemblies alike (specchia_componente mirrors on the
        /// assembly planes). The probe of C3-6 read the frames on a PART; on an
        /// assembly the reading is not probed, and the bench cases of
        /// specchia_componente (la_staffa_destra_e_la_sinistra,
        /// the_mirrored_instance_turns_as_asked) mirror on the assembly's Right,
        /// Top and Front planes: they are the check. An unknown name, or no
        /// plane passing the rule, is null with <paramref name="reason"/> saying
        /// which normal was looked for and how many planes were read.
        /// </summary>
        public static IFeature BasePlane(IModelDoc2 model, string name, out string reason)
        {
            reason = null;
            if (Array.IndexOf(BasePlaneNames, name) < 0)
            {
                reason = "«" + name + "» is not a base plane: they are " + string.Join(", ", BasePlaneNames) + ".";
                return null;
            }
            if (model == null) { reason = "There is no document."; return null; }

            var features = new List<IFeature>();
            var normals = new List<double[]>();
            var origins = new List<double[]>();
            int unread = 0;
            try
            {
                var feature = model.FirstFeature() as IFeature;
                while (feature != null)
                {
                    string type = "";
                    try { type = feature.GetTypeName2(); } catch { }
                    if (type == "RefPlane")
                    {
                        IRefPlane plane = null;
                        try { plane = feature.GetSpecificFeature2() as IRefPlane; } catch { }
                        double[] normal, origin;
                        if (!PlaneFrame(plane, out normal, out origin)) unread++;
                        features.Add(feature);
                        normals.Add(normal);
                        origins.Add(origin);
                    }
                    feature = feature.GetNextFeature() as IFeature;
                }
            }
            catch (Exception ex)
            {
                reason = "The feature tree could not be read to the end (" + ex.Message + "), so the «" + name +
                         "» plane was not looked for among all the planes.";
                return null;
            }

            int index = BasePlaneIndex(normals, origins, name);
            if (index >= 0) return features[index];
            var wanted = NormaleDelPiano(name);
            reason = "no reference plane of this document has the normal (" + string.Join(", ", wanted) +
                     ") and passes through the origin, which is what the «" + name + "» plane is (" +
                     features.Count + " reference planes read" +
                     (unread > 0 ? ", " + unread + " of them without a readable transform" : "") + ").";
            return null;
        }

        /// <summary>The names of the three base planes, in the catalogue's spelling.</summary>
        public static readonly string[] BasePlaneNames = { "frontale", "superiore", "destro" };

        /// <summary>
        /// THE DECISION OF <see cref="BasePlane"/>, WITH NO INTEROP: which of the
        /// reference planes, given in tree order by their unit normal and their
        /// origin in metres (null = not read), is the base plane of that name.
        /// -1 for an unknown name or when none qualifies. Pure, so that
        /// Prove.Contratti checks it without SOLIDWORKS (Zc215IdentityContract).
        ///
        /// A plane qualifies with the canonical normal of the name, SAME SIGN,
        /// and passing through the world origin. Measured by the Z-C21.3 probe of
        /// C3-6 (28/9/2026, SOLIDWORKS 29.5.1) on the template: «Piano frontale»
        /// normal (0, 0, 1), «Piano superiore» (0, 1, 0), «Piano destro»
        /// (1, 0, 0), all three with origin (0, 0, 0); a plane offset 20 mm from
        /// the front read (0, 0, 1) through (0, 0, 20), and a plane through three
        /// points at y = 30 read (0, -1, 0): neither qualifies. The sign is
        /// required because the side of an offset (OptionFlip), the direction of
        /// an extrusion and the orientation of a sketch follow the normal: a
        /// plane through the origin facing -Y would turn the work on the top
        /// plane upside down.
        ///
        /// TWO PLANES QUALIFY only when a second plane coincides with a base
        /// plane, normal and sign included. The first in the tree is taken: the
        /// probe found every ReorderFeature that tried to put a plane or a sketch
        /// before the base planes refused (its cases P1 and P2), so the first is
        /// the template's own, and for a sketch, an offset, a mirror or a section
        /// the two are the same plane. It is the only use of the tree order
        /// left, and only between planes the geometry cannot tell apart.
        /// </summary>
        public static int BasePlaneIndex(IList<double[]> normals, IList<double[]> origins, string name)
        {
            if (normals == null || origins == null || Array.IndexOf(BasePlaneNames, name) < 0) return -1;
            var wanted = NormaleDelPiano(name);
            for (int i = 0; i < normals.Count && i < origins.Count; i++)
            {
                var normal = normals[i];
                var origin = origins[i];
                if (normal == null || origin == null || normal.Length < 3 || origin.Length < 3) continue;
                if (Prodotto(normal, wanted) < BasePlaneCosine) continue;
                if (Math.Abs(Prodotto(normal, origin)) > BasePlaneOffsetM) continue;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// The unit normal and the origin (metres) of a reference plane, in
        /// world coordinates, from IRefPlane.Transform. False when unreadable.
        ///
        /// IRefPlane~Transform.html: «Gets the plane transform with respect to
        /// the world»; its example (Get_the_Normal_and_Origin_of_a_Reference_
        /// Plane_Using_Its_Transform_Example_VB.htm) takes the normal as
        /// (0, 0, 1) and the origin as (0, 0, 0) through the transform, the
        /// canonical position being the Front Plane. IMathTransform~ArrayData.html:
        /// «The first 9 elements define the 3x3 rotation matrix. The next 3
        /// elements define the translation component». So the NORMAL IS THE
        /// THIRD ROW (indices 6, 7, 8), not the third column, measured on
        /// 16/9/2026 on an oblique plane where the column gives a turned vector
        /// (piano_per_tre_punti, OperazioniPiani); the ORIGIN is the translation
        /// (indices 9, 10, 11), which is what the bench reads as
        /// «ultimo_piano_mm» (piano_superiore_piu_100: [0, 100, 0]).
        /// </summary>
        public static bool PlaneFrame(IRefPlane plane, out double[] normal, out double[] origin)
        {
            normal = null;
            origin = null;
            if (plane == null) return false;
            try
            {
                var transform = plane.Transform as IMathTransform;
                var data = transform == null ? null : transform.ArrayData as double[];
                if (data == null || data.Length < 12) return false;
                double length = Math.Sqrt(data[6] * data[6] + data[7] * data[7] + data[8] * data[8]);
                if (length < 1e-9) return false;
                normal = new[] { data[6] / length, data[7] / length, data[8] / length };
                origin = new[] { data[9], data[10], data[11] };
                return true;
            }
            catch { return false; }
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
            int unmeasured = 0;
            foreach (var f in facce)
            {
                var n = Normale(f);
                if (n == null) continue;
                if (Math.Abs(Prodotto(n, passo.Direzione)) <= Coseno) continue;

                var centro = PointToProject(f);
                if (centro == null) { unmeasured++; continue; }
                double proiezione = Prodotto(centro, passo.Direzione) * verso;
                if (scelta == null || proiezione > miglioreProiezione)
                {
                    scelta = f;
                    miglioreProiezione = proiezione;
                }
            }

            if (scelta == null && unmeasured > 0)
            {
                // Not "there is no such face": there are, and none could be measured.
                motivo =
                    "The step «" + passo.Operazione + "» left " + unmeasured + " planar face(s) " +
                    "perpendicular to the extrusion direction, but I could not measure where any " +
                    "of them is (neither the true bounding box nor a point on the face answered). " +
                    "Name the face by its identifier.";
                return false;
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
        /// The point of a planar face perpendicular to the direction that is
        /// projected on the direction to rank the faces: the centre of its true
        /// box, or, when the box does not answer, a point ON the face. Null
        /// only when neither answers.
        ///
        /// For a face perpendicular to the direction every point of the face
        /// has (within the Coseno tolerance) the same projection, so a point on
        /// the face ranks it as well as the centre of its box. Guide:
        /// IFace2~GetClosestPointOn.html, «Array of five doubles representing
        /// the X,Y,Z point on the face followed by the U, V parameter»; the box
        /// is ApiHelpers.TrueBoundingBox (S6), which can fail where the
        /// approximate IFace2.GetBox always answered.
        /// </summary>
        private static double[] PointToProject(IFace2 face)
        {
            try
            {
                var box = ApiHelpers.TrueBoundingBox(face);   // S6: not IFace2.GetBox, approximate
                if (box != null && box.Length >= 6)
                    return new[] { (box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2 };
            }
            catch { }
            try
            {
                var p = face.GetClosestPointOn(0, 0, 0) as double[];
                if (p != null && p.Length >= 3) return new[] { p[0], p[1], p[2] };
            }
            catch { }
            return null;
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
                // S17: the rule (and why Face2.Normal and not PlaneParams)
                // now lives in ApiHelpers.NormalIfPlanar.
                var normal = ApiHelpers.NormalIfPlanar(faccia);
                return normal == null ? null : Normalizza(normal);
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
        /// The guide page says why this works (Z-C21.9, finding L10-7):
        /// IFace2~FaceInSurfaceSense.html, Remarks, «True if face normal and
        /// surface normal are in the opposite direction», and «The normal vector
        /// of faces, however, are directed away from the body material». The
        /// face normal of a hole wall points into the hole, away from the
        /// material, so against the cylinder's own outward normal: true. The
        /// same page is the reason IFace2.Normal is taken as the outward normal
        /// everywhere (ApiHelpers.NormalIfPlanar).
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
