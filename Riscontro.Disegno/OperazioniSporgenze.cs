using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LE SOVRASPORGENZE: cosa non si stampa senza supporti. (voce GP16)
    ///
    /// Una parete che pende nel vuoto oltre un certo angolo si stampa a
    /// filamenti persi. SOLIDWORKS non dice niente — il pezzo e' sano, il
    /// volume e' giusto, la geometria e' chiusa — e il difetto si vede solo
    /// sulla macchina, a stampa finita.
    ///
    /// ── LA NORMALE: MISURATA, NON CORRETTA A CASO ─────────────────────
    ///
    /// La voce avvertiva che la normale di `ISurface` e' quella della
    /// superficie sottostante e va girata quando `FaceInSurfaceSense()` e'
    /// falso, e che sbagliarlo da' *«un'analisi che dichiara supportata proprio
    /// la faccia che pende nel vuoto»*.
    ///
    /// Misurato il 16/9/2026 su un blocco di cui si sa tutto (100x100x20, sei
    /// facce con normali uscenti note):
    ///
    ///   IFace2.Normal esce dal materiale   6 volte su 6
    ///   Normal concorde con PlaneParams    1 volta su 6
    ///   FaceInSurfaceSense() = true        sulle 5 in cui sono OPPOSTE
    ///   FaceInSurfaceSense() = false       sulla 1 in cui sono concordi
    ///
    /// Due cose, e tutt'e due contano:
    ///
    ///   1. **`IFace2.Normal` e' gia' giusta.** Punta sempre fuori dal
    ///      materiale, e non va corretta con niente. E' quella che questo
    ///      progetto usa dappertutto, e adesso e' misurato invece che sperato.
    ///   2. **La regola «se il senso e' falso, gira» sarebbe stata sbagliata
    ///      al contrario**, e non su meta' delle facce ma su cinque sesti.
    ///      Qui il senso non si usa: si usa `Normal`.
    ///
    /// ── LE FACCE CURVE ────────────────────────────────────────────────
    ///
    /// Una faccia curva non ha UNA normale: il sotto di un cilindro
    /// orizzontale pende nel vuoto nella sua parte bassa e non nella parte
    /// alta. Percio' le facce si tassellano e si guarda triangolo per
    /// triangolo, pesando per area.
    ///
    /// Il verso dei triangoli lo decide l'avvolgimento, che e' una convenzione
    /// e non un fatto: **si controlla contro `IFace2.Normal` su tutte le facce
    /// PIANE**, dove la risposta giusta si conosce. Se non combacia, ci si
    /// rifiuta invece di rispondere con un'analisi rovesciata. E' un controllo
    /// che si porta dietro la sua stessa taratura.
    ///
    /// `GetTessTriangles` risponde **`float[]`, non `double[]`**: il cast
    /// sbagliato torna null in silenzio (nota 80zf del RAG).
    ///
    /// ── E QUELLO CHE STA SUL PIANO NON E' UNA SPORGENZA ───────────────
    ///
    /// La faccia di sotto guarda dritta in giu' — 90 gradi di sporgenza, il
    /// caso peggiore possibile — ma appoggia sul piano della macchina e si
    /// stampa benissimo. Contarla sarebbe dichiarare non stampabile ogni pezzo
    /// esistente.
    /// </summary>
    public partial class OperazioniSw
    {
        /// <summary>Quanto si tollera sul piano di appoggio, in mm.</summary>
        private const double SulPianoMm = 0.05;

        private Esito VerificaSovrasporgenze(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            double soglia = Num(a, "soglia_gradi", 45);
            if (soglia <= 0 || soglia >= 90)
                return Fallito(
                    "«soglia_gradi» va fra 0 e 90 esclusi. E' l'angolo oltre il quale una " +
                    "parete non si regge: 45 per una stampante a filamento normale, 30-35 " +
                    "se vuoi stare largo, 60 se la macchina raffredda bene.");

            double[] su = Direzione(a, "direzione_di_stampa");
            if (su == null)
                return Fallito(
                    "«direzione_di_stampa» dev'essere tre numeri, per esempio [0, 0, 1]. " +
                    "E' il verso in cui cresce la stampa: il piano della macchina sta sotto, " +
                    "perpendicolare a questo.");

            var corpi = CorpiDaGuardare(modello, a, stato, out string motivo);
            if (corpi == null) return Fallito(motivo);
            int corpiNascosti = 0;
            foreach (var c in corpi)
            {
                try { if (!c.Visible) corpiNascosti++; } catch { }
            }

            // La quota del piano di appoggio: il punto piu' basso del pezzo
            // nella direzione di stampa.
            double quotaPiano = double.MaxValue;
            foreach (var c in corpi)
            {
                var estremo = new double[3];
                if (c.GetExtremePoint(-su[0], -su[1], -su[2],
                                      out estremo[0], out estremo[1], out estremo[2]))
                {
                    double q = estremo[0] * su[0] + estremo[1] * su[1] + estremo[2] * su[2];
                    if (q < quotaPiano) quotaPiano = q;
                }
            }
            if (quotaPiano == double.MaxValue)
                return Fallito("Non riesco a trovare il punto piu' basso del pezzo.");

            double areaTotale = 0, areaSporgente = 0, areaSulPiano = 0;
            double peggiore = 0;
            int facceGuardate = 0, facceSporgenti = 0, facceCurve = 0;
            int facceNonAnalizzate = 0;
            double areaNonAnalizzata = 0;
            // Planar faces that hang over the threshold but whose true box did
            // not answer: whether they rest on the plate is unknown, so they
            // count as not analysed (part of facceNonAnalizzate), never as
            // overhangs and never as resting.
            int planarWithoutBox = 0;
            double planarWithoutBoxArea = 0;
            int concordi = 0, discordi = 0;
            var elenco = new List<Dictionary<string, object>>();

            foreach (var corpo in corpi)
            {
                var f = corpo.GetFirstFace() as IFace2;
                while (f != null)
                {
                    var faccia = f;
                    f = f.GetNextFace() as IFace2;
                    facceGuardate++;

                    double[] normale = null;
                    try { normale = ApiHelpers.NormalIfPlanar(faccia); } catch { }
                    bool piana = false;
                    try { var s = faccia.GetSurface() as ISurface; piana = s != null && s.IsPlane(); }
                    catch { }
                    if (!piana) facceCurve++;

                    double areaFaccia = 0;
                    try { areaFaccia = faccia.GetArea() * 1e6; } catch { }
                    areaTotale += areaFaccia;

                    // ── i triangoli, e il controllo dell'avvolgimento ────
                    var tri = Triangoli(faccia);
                    if (tri == null || tri.Count == 0)
                    {
                        // Niente tassellazione: si ripiega sulla normale, che
                        // per una faccia piana e' esatta. PER UNA CURVA NON C'E'
                        // (voce Z-C21.2, rilievo L12-2).
                        //
                        // IFace2~Normal.html, Remarks: «If the face is not a
                        // planar face, then this property returns 0,0,0.» Il
                        // commento che stava qui prometteva «e si dice», e il
                        // codice guardava solo `normale == null`: con (0,0,0)
                        // Prodotto dava 0, la faccia risultava «in su» e non
                        // veniva mai segnalata, qualunque fosse la sua
                        // pendenza. Se nessuna faccia tassellava, la risposta
                        // era «si stampa senza supporti» senza aver guardato
                        // niente. Adesso la faccia curva senza triangoli si
                        // CONTA come non analizzata, e il giudizio diventa non
                        // conclusivo (qui sotto).
                        if (!piana)
                        {
                            facceNonAnalizzate++;
                            areaNonAnalizzata += areaFaccia;
                            continue;
                        }
                        if (normale == null) continue;
                        double gradiP = Sporgenza(normale, su);
                        bool giuP = Prodotto(normale, su) < 0;
                        bool? resting = giuP && gradiP > soglia ? SulPiano(faccia, su, quotaPiano) : true;
                        if (resting == null)
                        {
                            // S6: the true box replaced IFace2.GetBox, which always
                            // answered. A box that does not answer is not "not on
                            // the plate": it is a face this check did not look at.
                            facceNonAnalizzate++;
                            areaNonAnalizzata += areaFaccia;
                            planarWithoutBox++;
                            planarWithoutBoxArea += areaFaccia;
                            continue;
                        }
                        if (resting == false)
                        {
                            facceSporgenti++;
                            areaSporgente += areaFaccia;
                            if (gradiP > peggiore) peggiore = gradiP;
                            Aggiungi(elenco, gradiP, areaFaccia, piana, faccia, su);
                        }
                        continue;
                    }

                    double areaGiu = 0;
                    double peggioreQui = 0;
                    foreach (var t in tri)
                    {
                        // Il verso dell'avvolgimento contro quello della
                        // faccia: si controlla dove la risposta si conosce.
                        if (piana && normale != null)
                        {
                            if (Prodotto(t.Normale, normale) > 0) concordi++; else discordi++;
                        }
                        if (Prodotto(t.Normale, su) >= 0) continue;   // guarda in su
                        double gradi = Sporgenza(t.Normale, su);
                        if (gradi <= soglia) continue;
                        if (SulPianoPunto(t.Centro, su, quotaPiano)) { areaSulPiano += t.Area; continue; }
                        areaGiu += t.Area;
                        if (gradi > peggioreQui) peggioreQui = gradi;
                    }

                    if (areaGiu > 0)
                    {
                        facceSporgenti++;
                        areaSporgente += areaGiu;
                        if (peggioreQui > peggiore) peggiore = peggioreQui;
                        Aggiungi(elenco, peggioreQui, areaGiu, piana, faccia, su);
                    }
                }
            }

            // ── IL CONTROLLO CHE SI PORTA DIETRO LA SUA TARATURA ────────
            //
            // Se l'avvolgimento dei triangoli non concorda con la normale
            // delle facce PIANE -- dove la risposta si conosce -- allora
            // l'analisi delle facce CURVE e' rovesciata, e non c'e' modo di
            // accorgersene guardando il risultato.
            if (concordi + discordi > 0 && discordi > 0)
                return Fallito(string.Format(
                    CultureInfo.InvariantCulture,
                    "Non mi fido della mia stessa analisi: su {0} triangoli di facce piane, " +
                    "{1} hanno il verso opposto a quello della faccia. Vuol dire che " +
                    "l'avvolgimento della tassellazione qui non segue la regola che ho " +
                    "verificato, e allora sulle facce curve direi il contrario del vero " +
                    "senza che si veda. Non ti do un'analisi rovesciata.",
                    concordi + discordi, discordi));

            elenco = elenco.OrderByDescending(v => Convert.ToDouble(v["sporgenza_gradi"])).ToList();
            const int quanteNeDico = 12;
            bool troncato = elenco.Count > quanteNeDico;

            var misure = new Dictionary<string, object>
            {
                ["soglia_gradi"] = soglia,
                ["direzione_di_stampa"] = su.Select(x => Math.Round(x, 4)).ToArray(),
                ["facce_guardate"] = facceGuardate,
                ["facce_curve"] = facceCurve,
                ["facce_sporgenti"] = facceSporgenti,
                ["sporgenza_peggiore_gradi"] = Math.Round(peggiore, 2),
                ["area_sporgente_mm2"] = Math.Round(areaSporgente, 2),
                ["area_totale_mm2"] = Math.Round(areaTotale, 2),
                ["area_appoggiata_al_piano_mm2"] = Math.Round(areaSulPiano, 2),
                // NON CONCLUSIVO E' UN TERZO VALORE, non un «si'» (rilievo
                // L12-2): con una faccia curva che non si e' potuta guardare,
                // «nessuna sporgenza trovata» non vuol dire «nessuna sporgenza».
                // Una sporgenza trovata resta un «no» anche cosi'.
                ["si_stampa_senza_supporti"] = facceSporgenti > 0
                    ? (object)false
                    : facceNonAnalizzate > 0 ? null : (object)true,
                ["sporgenze"] = elenco.Take(quanteNeDico).ToArray(),
                ["triangoli_controllati"] = concordi + discordi,
                // Nomi nuovi, in inglese come i prossimi del ponte.
                ["faces_not_analyzed"] = facceNonAnalizzate,
                ["hidden_bodies"] = corpiNascosti,
            };
            if (facceNonAnalizzate > 0)
                misure["area_not_analyzed_mm2"] = Math.Round(areaNonAnalizzata, 2);
            if (planarWithoutBox > 0)
            {
                misure["planar_faces_without_true_box"] = planarWithoutBox;
                misure["planar_area_without_true_box_mm2"] = Math.Round(planarWithoutBoxArea, 2);
            }
            if (troncato)
            {
                misure["sporgenze_non_elencate"] = elenco.Count - quanteNeDico;
                misure["troncato"] = true;
            }

            int curvedNotAnalysed = facceNonAnalizzate - planarWithoutBox;
            string nonGuardate = curvedNotAnalysed == 0 ? "" : string.Format(
                CultureInfo.InvariantCulture,
                " ATTENZIONE: {0} facce curve ({1:0.##} mm2) non si sono lasciate tassellare " +
                "e non le ho potute guardare: una faccia curva non ha una normale sola.",
                curvedNotAnalysed, areaNonAnalizzata - planarWithoutBoxArea);
            if (planarWithoutBox > 0)
                nonGuardate += string.Format(
                    CultureInfo.InvariantCulture,
                    " WARNING: {0} planar faces ({1:0.##} mm2) hang over the threshold, but their true " +
                    "bounding box could not be measured, so I cannot tell whether they rest on the plate.",
                    planarWithoutBox, planarWithoutBoxArea);
            misure["giudizio"] = facceSporgenti > 0
                ? string.Format(CultureInfo.InvariantCulture,
                    "NON si stampa senza supporti: {0} facce pendono oltre {1:0.#} gradi, " +
                    "la peggiore a {2:0.#}, per {3:0.##} mm2 su {4:0.##} di superficie. " +
                    "Gira il pezzo, o mettici i supporti, o smussa quelle facce.",
                    facceSporgenti, soglia, peggiore, areaSporgente, areaTotale) + nonGuardate
                : facceNonAnalizzate > 0
                    ? string.Format(CultureInfo.InvariantCulture,
                        "NON POSSO DIRE se si stampa senza supporti con la soglia di {0:0.#} " +
                        "gradi: sulle facce che ho guardato non pende niente oltre.", soglia) +
                      nonGuardate +
                      " Riprova dopo una ricostruzione, o guarda quelle facce a occhio."
                    : string.Format(CultureInfo.InvariantCulture,
                        "Si stampa senza supporti con la soglia di {0:0.#} gradi: non c'e' " +
                        "nessuna superficie che pende oltre.", soglia);

            misure["cosa_non_guarda"] =
                "Guardo l'angolo di ogni superficie rispetto alla direzione di stampa, e " +
                "basta. NON guardo: i ponti fra due appoggi (una traversa corta si stampa " +
                "anche se e' orizzontale), lo spessore delle pareti, le isole staccate che " +
                "nascono a mezz'aria, e se il pezzo sta nel volume della macchina. E quello " +
                "che appoggia sul piano non lo conto come sporgenza, perche' il piano lo " +
                "regge.";

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] sporgenze oltre {0:0.#} gradi: {1} facce, peggiore {2:0.#}, " +
                "{3:0.##} mm2", soglia, facceSporgenti, peggiore, areaSporgente));

            return RiuscitoSenzaCreare(misure);
        }

        // =================================================================

        private sealed class Triangolo
        {
            public double[] Normale;
            public double[] Centro;
            public double Area;
        }

        /// <summary>
        /// I triangoli di una faccia, con normale, centro e area.
        ///
        /// `GetTessTriangles` risponde **float[]**: nove numeri per triangolo,
        /// tre vertici. Il cast a `double[]` torna null in silenzio — e' la
        /// trappola gia' documentata nel RAG, e qui e' scritta perche' chi
        /// tocca questo codice non la ripaghi.
        /// </summary>
        private static List<Triangolo> Triangoli(IFace2 faccia)
        {
            float[] grezzo;
            try { grezzo = faccia.GetTessTriangles(true) as float[]; }
            catch { return null; }
            if (grezzo == null || grezzo.Length < 9) return null;

            var fuori = new List<Triangolo>(grezzo.Length / 9);
            for (int i = 0; i + 8 < grezzo.Length; i += 9)
            {
                var ax = grezzo[i]; var ay = grezzo[i + 1]; var az = grezzo[i + 2];
                var bx = grezzo[i + 3]; var by = grezzo[i + 4]; var bz = grezzo[i + 5];
                var cx = grezzo[i + 6]; var cy = grezzo[i + 7]; var cz = grezzo[i + 8];

                double ux = bx - ax, uy = by - ay, uz = bz - az;
                double vx = cx - ax, vy = cy - ay, vz = cz - az;
                double nx = uy * vz - uz * vy;
                double ny = uz * vx - ux * vz;
                double nz = ux * vy - uy * vx;
                double lung = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (lung < 1e-15) continue;   // triangolo degenere
                fuori.Add(new Triangolo
                {
                    Normale = new[] { nx / lung, ny / lung, nz / lung },
                    Centro = new[] { (ax + bx + cx) / 3.0, (ay + by + cy) / 3.0, (az + bz + cz) / 3.0 },
                    // meta' del modulo del prodotto vettoriale, in mm2
                    Area = lung / 2.0 * 1e6,
                });
            }
            return fuori;
        }

        /// <summary>
        /// Di quanto una superficie pende, in gradi, contati DALLA VERTICALE:
        /// una parete dritta e' 0, un soffitto orizzontale e' 90.
        ///
        /// E' la convenzione degli slicer -- «soglia 45 gradi» vuol dire quello
        /// -- e non quella dell'angolo col piano, che e' il complementare. Due
        /// numeri che si somigliano e dicono il contrario: qui si scrive quale
        /// dei due e', e il campo si chiama `sporgenza_gradi`.
        /// </summary>
        private static double Sporgenza(double[] normale, double[] su)
        {
            double c = Math.Abs(Prodotto(normale, su));
            if (c > 1) c = 1;
            return 90.0 - Math.Acos(c) * 180.0 / Math.PI;
        }

        private static double Prodotto(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        /// <summary>
        /// Whether the face rests on the plate; null when its true box cannot
        /// be measured (the caller counts it as not analysed).
        /// </summary>
        private static bool? SulPiano(IFace2 faccia, double[] su, double quotaPiano)
        {
            try
            {
                var b = ApiHelpers.TrueBoundingBox(faccia);   // S6: not IFace2.GetBox, approximate
                if (b == null || b.Length < 6) return null;
                // l'angolo piu' alto della scatola, proiettato sulla direzione
                double alto = double.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    double x = (i & 1) == 0 ? b[0] : b[3];
                    double y = (i & 2) == 0 ? b[1] : b[4];
                    double z = (i & 4) == 0 ? b[2] : b[5];
                    double q = x * su[0] + y * su[1] + z * su[2];
                    if (q > alto) alto = q;
                }
                return alto - quotaPiano < Unita.M(SulPianoMm);
            }
            catch { return null; }
        }

        private static bool SulPianoPunto(double[] punto, double[] su, double quotaPiano)
        {
            double q = punto[0] * su[0] + punto[1] * su[1] + punto[2] * su[2];
            return q - quotaPiano < Unita.M(SulPianoMm);
        }

        private static void Aggiungi(
            List<Dictionary<string, object>> elenco, double gradi, double area,
            bool piana, IFace2 faccia, double[] su)
        {
            var voce = new Dictionary<string, object>
            {
                ["sporgenza_gradi"] = Math.Round(gradi, 2),
                ["area_mm2"] = Math.Round(area, 2),
                ["piana"] = piana,
            };
            try
            {
                var b = ApiHelpers.TrueBoundingBox(faccia);   // S6: not IFace2.GetBox, approximate
                if (b != null && b.Length >= 6)
                    voce["sta_fra_mm"] = new[]
                    {
                        Math.Round(Unita.Mm(b[0]), 2), Math.Round(Unita.Mm(b[1]), 2),
                        Math.Round(Unita.Mm(b[2]), 2), Math.Round(Unita.Mm(b[3]), 2),
                        Math.Round(Unita.Mm(b[4]), 2), Math.Round(Unita.Mm(b[5]), 2),
                    };
            }
            catch { }
            elenco.Add(voce);
        }

        private static double[] Direzione(Dictionary<string, object> a, string chiave)
        {
            object v;
            if (a == null || !a.TryGetValue(chiave, out v) || v == null)
                return new double[] { 0, 0, 1 };
            var e = v as System.Collections.IEnumerable;
            if (e == null || v is string) return null;
            var n = new List<double>();
            foreach (var x in e)
            {
                try { n.Add(Convert.ToDouble(x, CultureInfo.InvariantCulture)); }
                catch { return null; }
            }
            if (n.Count != 3) return null;
            double lung = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            if (lung < 1e-9) return null;
            return new[] { n[0] / lung, n[1] / lung, n[2] / lung };
        }

        private List<IBody2> CorpiDaGuardare(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato,
            out string motivo)
        {
            motivo = null;
            var parte = modello as IPartDoc;
            if (parte == null) { motivo = "Questo non e' un documento di parte."; return null; }
            // TUTTI I CORPI, ANCHE QUELLI NASCOSTI (voce Z-C21.2, rilievo L12-1).
            //
            // IPartDoc~GetBodies2.html, parametro BVisibleOnly: «True gets only
            // the visible bodies, false gets all of the bodies in the part». Qui
            // c'era `true`: un corpo nascosto per guardare dentro il pezzo usciva
            // dall'analisi senza che niente lo dicesse, e «si_stampa_senza_supporti»
            // poteva dire si' su un corpo che pende. Lo stato usa `false` da tempo
            // (StatoDocumento.cs, «e il false e' voluto»), e con `true` qui «C2»
            // indicava un corpo diverso da quello dello stato.
            //
            // La guida NON dice se `GetTessTriangles` risponde sulle facce di un
            // corpo nascosto. Per questo il ramo senza triangoli (rilievo L12-2)
            // conta le facce curve come non analizzate invece di saltarle: se il
            // corpo nascosto non tassella, il giudizio diventa «non posso dire»
            // e non torna il «si'» silenzioso. La mini-sonda che lo misura e'
            // ancora da lanciare (voce Z-C21.2, CHANGELOG).
            var grezzi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            var tutti = (grezzi ?? new object[0]).Select(o => o as IBody2).Where(b => b != null).ToList();
            if (tutti.Count == 0) { motivo = "Il pezzo non ha corpi solidi da guardare."; return null; }

            string uid = Str(a, "corpo_uid", null);
            if (string.IsNullOrWhiteSpace(uid)) return tutti;

            // IL CORPO PER FIRMA, come in combina_corpi e sposta_corpi (schema S5,
            // rilievo L6-1): GetBodies2.html, «May vary the order in which bodies
            // are returned».
            var trovati = CorpiDaUid(stato, new List<string> { uid.Trim() }, a, out motivo);
            if (trovati == null) return null;
            return new List<IBody2> { trovati[0] };
        }
    }
}
