using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LA MARCATURA SU UNA FACCIA. (voce GP13)
    ///
    /// Numero di serie, verso di montaggio, nome del cliente, «ALTO»: e'
    /// quello che in officina si chiede su un pezzo su due, e fino a qui il
    /// catalogo non sapeva scrivere niente sul pezzo.
    ///
    /// ── NON E' UN'API SOLA ─────────────────────────────────────────────
    ///
    ///   IModelDoc2.InsertSketchText(Ptx, Pty, Ptz, Text, Alignment,
    ///                               FlipDirection, HorizontalMirror,
    ///                               WidthFactor, SpaceBetweenChars)
    ///                               -> ISketchText
    ///
    /// fa lo SCHIZZO. Il materiale lo toglie (o lo aggiunge) il taglio o
    /// l'estrusione che vengono dopo, che sono gli stessi gia' misurati.
    ///
    /// Misurata il 16/9/2026: **non apre nessuna finestra** (non e' come
    /// `AddDimension`), e funziona anche con `AddToDB = true`.
    ///
    /// ── IL DIFETTO CHE NON DA' ERRORE, E QUI E' GROSSO ─────────────────
    ///
    /// «PROVA» alta 10 mm, incisa 2 mm, su un quadrato di 100x100:
    ///
    ///     ancorata a (0, 0)     ->  tolti 230,70 mm3
    ///     ancorata a (30, 10)   ->  tolti 111,20 mm3
    ///
    /// **Meta' della scritta cade fuori dalla faccia e sparisce in silenzio.**
    /// La feature nasce verde, si chiama `Taglio-Estrusione1`, e nel pezzo c'e'
    /// «PRO». Nessun errore, da nessuna parte — e su un numero di serie non c'e'
    /// niente da cui accorgersene guardando l'albero.
    ///
    /// Tutta fuori invece si prende gia': `FeatureCut4` torna `null`. E' il
    /// caso facile. Quello che conta e' la sovrapposizione PARZIALE.
    ///
    /// ── COME SI PRENDE: LA SCATOLA DELLA SCRITTA ──────────────────────
    ///
    /// `ISketchText.GetEdges()` torna i contorni delle lettere — 56 `IEdge`
    /// per «PROVA» — e da quelli si legge **dove finisce davvero la scritta**,
    /// in coordinate del modello e in metri. Si controlla PRIMA di tagliare, e
    /// si rifiuta.
    ///
    /// Prima non si poteva sapere: la larghezza di una scritta non si calcola
    /// dalla stringa, dipende dal font. Misurata, invece, si sa — e per
    /// «PROVA» in Century Gothic alta 10 mm e' **47,05 x 10,51 mm**.
    ///
    /// ── QUELLO CHE HA UN NUMERO, E QUELLO CHE NON CE L'HA ─────────────
    ///
    /// L'area delle lettere non si puo' prevedere. Ma va col **quadrato
    /// dell'altezza**, ed e' misurato:
    ///
    ///     altezza  5 mm  ->  area 28,83 mm2   ->  area/h2 = 1,1533
    ///     altezza 10 mm  ->  area 115,35 mm2  ->  area/h2 = 1,1535
    ///     altezza 20 mm  ->  area 265,88 mm2  ->  area/h2 = 0,6647   <- rotto
    ///
    /// Il terzo numero e' rotto perche' a 20 mm «PROVA» e' larga 94 mm e su una
    /// faccia di 100 ancorata a x=0 non ci sta. **La stessa legge che tiene i
    /// primi due e' quella che denuncia il terzo**: due misure che si
    /// controllano a vicenda, e nessuna delle due e' una costante scritta a
    /// mano.
    ///
    /// ── E `Alignment` NON FA NIENTE ───────────────────────────────────
    ///
    /// La voce lo dava per «non verificato nel significato pratico». Provati
    /// tutti e cinque i valori 0..4 sulla stessa scritta: la scatola torna
    /// **identica al millesimo** in tutti e cinque. Non e' un parametro che non
    /// si e' capito: e' un parametro che questa chiamata ignora. Percio' non si
    /// espone, e si passa 0.
    /// </summary>
    public partial class OperazioniSw
    {
        /// <summary>Quanto si tollera che la scritta sbordi, in mm.</summary>
        private const double SbordoTollerato = 0.01;

        private Esito IncidiTesto(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string testo = Str(a, "testo", null);
            if (string.IsNullOrEmpty(testo))
                return Fallito("Manca il «testo» da incidere.");

            var faccia = RisolviFaccia(stato, a, Str(a, "faccia_uid", ""), out string motivo);
            if (faccia == null) return Fallito(motivo);

            double[] assi = AssiDellaFaccia(faccia, out double offsetM, out string errore);
            if (assi == null) return Fallito(errore);

            double altezza = Num(a, "altezza_mm", 5);
            if (altezza <= 0)
                return Fallito("Un testo di altezza nulla o negativa non e' un testo.");
            double profondita = Num(a, "profondita_mm", 0.5);
            if (profondita <= 0)
                return Fallito(
                    "Una marcatura di profondita' nulla non lascia segno. Se volevi solo " +
                    "un'annotazione e non una lavorazione, questa non e' l'operazione.");

            bool inRilievo = Bool(a, "in_rilievo", false);
            double xMm = Num(a, "x_mm", 0);
            double yMm = Num(a, "y_mm", 0);
            string font = Str(a, "font", null);
            bool grassetto = Bool(a, "grassetto", false);

            int corpiPrima = ContaCorpiSolidi(modello);
            double? volumePrima = VolumeMm3(modello);
            if (corpiPrima < 0 || volumePrima == null)
                return Fallito(VolumeNonLettoPrima("marcare la faccia"));

            // La scatola della FACCIA, per sapere dove la scritta puo' stare.
            double[] scatolaFaccia = ScatolaDellaFaccia(faccia);
            // Without the face box the overflow check (SbordaFuori) cannot run,
            // and a truncated inscription would be engraved without a word.
            // Refuse before touching the part (S6: the true box can fail where
            // IFace2.GetBox always answered).
            if (scatolaFaccia == null)
                return Fallito(
                    "I cannot measure the face: its true bounding box did not answer, so I " +
                    "cannot check that the text stays inside it. Nothing was changed.");
            double areaFaccia = 0;
            try { areaFaccia = faccia.GetArea() * 1e6; } catch { }

            if (!SelezionaFaccia(faccia)) return Fallito("Non sono riuscito a selezionare la faccia.");

            var schizzi = modello.SketchManager;
            bool addToDbPrima = false, displayPrima = false;
            try { addToDbPrima = schizzi.AddToDB; displayPrima = schizzi.DisplayWhenAdded; } catch { }

            ISketchText scritta = null;
            double[] scatolaTesto = null;
            int contorni = 0;
            try
            {
                schizzi.InsertSketch(true);
                try { schizzi.AddToDB = true; schizzi.DisplayWhenAdded = false; } catch { }

                var punto = PuntoNelloSchizzo(modello, assi, offsetM, xMm, yMm, out errore);
                if (punto == null) throw new InvalidOperationException(errore);

                scritta = modello.InsertSketchText(
                    punto[0], punto[1], punto[2],
                    testo,
                    // Alignment: misurato, non fa niente. Vedi la nota in testa.
                    0,
                    0,   // FlipDirection
                    0,   // HorizontalMirror
                    100, // WidthFactor, in centesimi
                    100  // SpaceBetweenChars, in centesimi
                ) as ISketchText;
                if (scritta == null)
                    throw new InvalidOperationException(
                        "SOLIDWORKS non ha creato il testo di schizzo, e non dice perche'.");

                var formato = scritta.GetTextFormat() as ITextFormat;
                if (formato == null)
                    throw new InvalidOperationException(
                        "Non riesco a leggere il formato del testo: senza, l'altezza " +
                        "resterebbe quella predefinita e tu crederesti di averla scelta.");
                formato.CharHeight = Unita.M(altezza);
                formato.Bold = grassetto;
                if (!string.IsNullOrWhiteSpace(font)) formato.TypeFaceName = font;
                if (!scritta.SetTextFormat(false, formato))
                    throw new InvalidOperationException(
                        "SOLIDWORKS ha rifiutato il formato del testo (altezza " + altezza +
                        " mm" + (font == null ? "" : ", font «" + font + "»") + ").");

                // E SI RILEGGE. `CharHeight` e' l'unica cosa che distingue una
                // marcatura da leggere da una da indovinare.
                var riletto = scritta.GetTextFormat() as ITextFormat;
                double altezzaVera = riletto == null ? -1 : Unita.Mm(riletto.CharHeight);
                if (Math.Abs(altezzaVera - altezza) > 0.001)
                    throw new InvalidOperationException(string.Format(
                        CultureInfo.InvariantCulture,
                        "Ho chiesto lettere alte {0:0.###} mm e rileggendo trovo {1}.",
                        altezza,
                        altezzaVera < 0 ? "niente"
                            : altezzaVera.ToString("0.###", CultureInfo.InvariantCulture) + " mm"));

                scatolaTesto = ScatolaDelleCurve(scritta, out contorni);
            }
            catch (Exception ex) { errore = ex.Message; scritta = null; }
            finally
            {
                try { schizzi.AddToDB = addToDbPrima; schizzi.DisplayWhenAdded = displayPrima; } catch { }
                try { schizzi.InsertSketch(false); } catch { }
            }
            if (scritta == null)
                return Fallito((errore ?? "il testo non e' stato creato") + SenzaLoSchizzo(modello));

            // ── E QUI SI GUARDA SE CI STA, PRIMA DI TAGLIARE ─────────────
            //
            // Dopo non si puo' piu': la feature e' verde e le lettere di
            // troppo non ci sono. Lo schizzo si porta via — un pezzo con uno
            // schizzo orfano che nessuno vede e' peggio di un rifiuto.
            if (scatolaTesto == null)
                return Fallito(
                    "Non riesco a leggere dove finisce la scritta (" + contorni + " contorni " +
                    "letti). Senza, non posso dirti se ci sta sulla faccia, e una scritta " +
                    "tagliata a meta' non da' nessun errore." + SenzaLoSchizzo(modello));

            string dove = SbordaFuori(scatolaTesto, scatolaFaccia, assi);
            if (dove != null)
                return Fallito(string.Format(
                    CultureInfo.InvariantCulture,
                    "«{0}» alto {1:0.###} mm occupa {2:0.##} x {3:0.##} mm e {4} Non lo " +
                    "incido: la parte che esce sparirebbe senza che niente dia errore, e " +
                    "nel pezzo resterebbe una scritta monca. Sposta l'ancora, abbassa " +
                    "«altezza_mm», o accorcia il testo.{5}",
                    testo, altezza,
                    scatolaTesto[3] - scatolaTesto[0],
                    scatolaTesto[4] - scatolaTesto[1],
                    dove, SenzaLoSchizzo(modello)));

            if (!SelezionaSchizzoAppenaCreato(modello, out string erroreSel))
                return Fallito(erroreSel + SenzaLoSchizzo(modello));

            IFeature feature;
            try
            {
                feature = inRilievo
                    ? Estrudi(modello, profondita, unisci: true)
                    : Taglia(modello, passante: false, profonditaMm: profondita);
            }
            catch (Exception ex)
            {
                return Fallito(
                    (inRilievo ? "Il rilievo" : "L'incisione") + " non e' riuscita: " +
                    ex.Message + SenzaLoSchizzo(modello));
            }

            if (feature == null)
                return Fallito(
                    (inRilievo ? "Il rilievo" : "L'incisione") + " non e' stata creata, e " +
                    "SOLIDWORKS non dice perche'. Con un testo che sta tutto fuori dalla " +
                    "faccia risponde cosi'." + SenzaLoSchizzo(modello));

            // ── e adesso si conta ────────────────────────────────────────
            double? dopo = VolumeMm3(modello);
            int corpiDopo = ContaCorpiSolidi(modello);
            if (dopo == null || corpiDopo < 0)
                return Riuscito(feature, new Dictionary<string, object>
                {
                    ["testo"] = testo,
                    ["altezza_mm"] = altezza,
                    ["profondita_mm"] = profondita,
                    ["in_rilievo"] = inRilievo,
                    ["contorni"] = contorni,
                    ["larghezza_scritta_mm"] = Tondo(scatolaTesto[3] - scatolaTesto[0]),
                    ["altezza_scritta_mm"] = Tondo(scatolaTesto[4] - scatolaTesto[1]),
                    [inRilievo ? "materiale_aggiunto_mm3" : "materiale_tolto_mm3"] = null,
                    ["corpi"] = NumeroOppureNull(corpiDopo),
                    ["volume_non_riletto"] = NotaVolumeNonRiletto,
                }, NormaleDellaFaccia(faccia));
            double mosso = inRilievo ? dopo.Value - volumePrima.Value : volumePrima.Value - dopo.Value;

            if (mosso <= 0)
                return Fallito(
                    "La marcatura non ha " + (inRilievo ? "aggiunto" : "tolto") +
                    " materiale: il volume e' " +
                    (Math.Abs(mosso) < 1e-9 ? "rimasto uguale" : "andato dall'altra parte") +
                    ". " + (inRilievo
                        ? "Di solito vuol dire che il rilievo e' cresciuto DENTRO il pezzo."
                        : "Di solito vuol dire che l'incisione e' andata in fuori.") +
                    " Ritirala — " + Frasi.CosiSiRitira + ".");

            if (corpiDopo != corpiPrima)
                return Fallito(
                    "I corpi sono passati da " + corpiPrima + " a " + corpiDopo +
                    ": una marcatura non deve spezzare ne' staccare niente. Ritirala — " +
                    Frasi.CosiSiRitira + ".");

            double areaLettere = mosso / profondita;
            var misure = new Dictionary<string, object>
            {
                ["testo"] = testo,
                ["altezza_mm"] = altezza,
                ["profondita_mm"] = profondita,
                ["in_rilievo"] = inRilievo,
                ["contorni"] = contorni,
                ["larghezza_scritta_mm"] = Tondo(scatolaTesto[3] - scatolaTesto[0]),
                ["altezza_scritta_mm"] = Tondo(scatolaTesto[4] - scatolaTesto[1]),
                [inRilievo ? "materiale_aggiunto_mm3" : "materiale_tolto_mm3"] = Tondo(mosso),
                // L'AREA DELLE LETTERE, che e' il volume diviso la profondita'.
                // Non si puo' prevedere dalla stringa -- dipende dal font -- ma
                // va col QUADRATO dell'altezza, e quel rapporto e' l'unica cosa
                // che due marcature diverse hanno in comune.
                ["area_lettere_mm2"] = Tondo(areaLettere),
                ["area_su_altezza_quadra"] = Tondo(areaLettere / (altezza * altezza), 4),
                ["corpi"] = corpiDopo,
            };
            if (!string.IsNullOrWhiteSpace(font)) misure["font"] = font;

            // COSA NON HO GUARDATO, detto a ogni chiamata e non una volta nel
            // catalogo: chi legge la risposta potrebbe non averlo mai letto.
            double areaScatola = 0;
            if (scatolaFaccia != null)
            {
                var lati = LatiSulPiano(scatolaFaccia, assi);
                areaScatola = lati[0] * lati[1];
            }
            bool facciaPiena = areaScatola > 0 && Math.Abs(areaFaccia - areaScatola) / areaScatola < 0.01;
            misure["cosa_non_guarda"] = string.Format(
                CultureInfo.InvariantCulture,
                "Ho controllato che la scritta stia dentro la SCATOLA della faccia{0} E non " +
                "guardo se la scritta finisce sopra un'altra lavorazione.",
                facciaPiena
                    ? string.Format(CultureInfo.InvariantCulture,
                        ", e questa faccia la riempie tutta (area {0:0.##} mm2 contro " +
                        "{1:0.##} di scatola), quindi il controllo e' completo.",
                        areaFaccia, areaScatola)
                    : string.Format(CultureInfo.InvariantCulture,
                        ", ma questa faccia NON riempie la sua scatola (area {0:0.##} mm2 " +
                        "contro {1:0.##} di scatola): ha un foro o un contorno rientrante, " +
                        "e una lettera potrebbe cadere li' sopra senza che io me ne " +
                        "accorga. Guarda il materiale tolto.",
                        areaFaccia, areaScatola));

            _log("[agente] «" + testo + "» " + (inRilievo ? "in rilievo " : "incisa ") +
                 profondita + " mm: " + Tondo(mosso) + " mm3, scritta " +
                 Tondo(scatolaTesto[3] - scatolaTesto[0]) + " mm larga");

            return Riuscito(feature, misure, NormaleDellaFaccia(faccia));
        }

        // =================================================================

        /// <summary>
        /// La scatola dei contorni delle lettere, in mm e in coordinate del
        /// MODELLO: {minX, minY, minZ, maxX, maxY, maxZ}.
        ///
        /// `ISketchText.GetEdges()` torna un `IEdge` per ogni contorno — 56
        /// per «PROVA», perche' ogni lettera ne ha uno esterno e le tonde anche
        /// quello interno. Di ognuno si guardano i due estremi **e cinque punti
        /// in mezzo**: un arco sta fuori dal segmento che unisce i suoi
        /// estremi, e la pancia di una «O» e' esattamente il punto che
        /// sborderebbe per primo.
        /// </summary>
        private static double[] ScatolaDelleCurve(ISketchText scritta, out int contorni)
        {
            contorni = 0;
            object[] bordi;
            try { bordi = scritta.GetEdges() as object[]; }
            catch { return null; }
            if (bordi == null || bordi.Length == 0) return null;

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (var o in bordi)
            {
                var bordo = o as IEdge;
                if (bordo == null) continue;
                double[] p;
                try { p = ApiHelpers.EdgeParameterArray(bordo); }
                catch { continue; }
                if (p == null || p.Length < 8) continue;
                contorni++;
                Allarga(p[0], p[1], p[2], ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                Allarga(p[3], p[4], p[5], ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                try
                {
                    var curva = bordo.GetCurve() as ICurve;
                    if (curva == null) continue;
                    for (int i = 1; i < 6; i++)
                    {
                        double t = p[6] + (p[7] - p[6]) * i / 6.0;
                        var punto = curva.Evaluate(t) as double[];
                        if (punto != null && punto.Length >= 3)
                            Allarga(punto[0], punto[1], punto[2],
                                    ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                    }
                }
                catch { }
            }
            if (contorni == 0) return null;
            return new[]
            {
                Unita.Mm(minX), Unita.Mm(minY), Unita.Mm(minZ),
                Unita.Mm(maxX), Unita.Mm(maxY), Unita.Mm(maxZ),
            };
        }

        private static void Allarga(
            double x, double y, double z,
            ref double minX, ref double minY, ref double minZ,
            ref double maxX, ref double maxY, ref double maxZ)
        {
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
            if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
        }

        /// <summary>La scatola di una faccia, in mm e in coordinate del modello.</summary>
        private static double[] ScatolaDellaFaccia(IFace2 faccia)
        {
            try
            {
                var b = ApiHelpers.TrueBoundingBox(faccia);   // S6: not IFace2.GetBox, approximate
                if (b == null || b.Length < 6) return null;
                var fuori = new double[6];
                for (int i = 0; i < 6; i++) fuori[i] = Unita.Mm(b[i]);
                return fuori;
            }
            catch { return null; }
        }

        /// <summary>I due lati della scatola sul piano della faccia, in mm.</summary>
        private static double[] LatiSulPiano(double[] scatola, double[] assi)
        {
            int a0 = (int)assi[0], a1 = (int)assi[1];
            return new[]
            {
                scatola[3 + a0] - scatola[a0],
                scatola[3 + a1] - scatola[a1],
            };
        }

        /// <summary>
        /// Dove la scritta esce dalla faccia, o null se ci sta.
        ///
        /// Si guardano i DUE assi del piano della faccia e non tutti e tre: il
        /// terzo e' lo spessore, la scritta ci sta per costruzione, e
        /// confrontarlo darebbe un rifiuto per un millesimo di arrotondamento.
        /// </summary>
        private static string SbordaFuori(double[] testo, double[] faccia, double[] assi)
        {
            if (faccia == null) return null;
            var nomi = new[] { "X", "Y", "Z" };
            var fuori = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                int asse = (int)assi[i];
                if (testo[asse] < faccia[asse] - SbordoTollerato)
                    fuori.Add(string.Format(CultureInfo.InvariantCulture,
                        "esce di {0:0.##} mm dal bordo {1} basso",
                        faccia[asse] - testo[asse], nomi[asse]));
                if (testo[3 + asse] > faccia[3 + asse] + SbordoTollerato)
                    fuori.Add(string.Format(CultureInfo.InvariantCulture,
                        "esce di {0:0.##} mm dal bordo {1} alto",
                        testo[3 + asse] - faccia[3 + asse], nomi[asse]));
            }
            if (fuori.Count == 0) return null;
            return string.Join(", ", fuori) + " della faccia.";
        }

        private static double Tondo(double v, int cifre = 2)
        {
            return Math.Round(v, cifre);
        }
    }
}
