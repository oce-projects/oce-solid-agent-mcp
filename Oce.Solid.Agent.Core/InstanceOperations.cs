using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace Riscontro.Disegno
{
    /// <summary>
    /// TOGLIERE UNA COPIA DA UNA RIPETIZIONE. (voce GP14)
    ///
    /// Una griglia di fori che ne salta uno dove passa una nervatura, una fila
    /// di asole che si ferma prima della piega: in officina e' normale. Nel
    /// catalogo mancava, e non si poteva fare in nessun altro modo — una
    /// ripetizione o e' regolare o non e'.
    ///
    /// ── NON C'E' UN METODO: SI SCRIVE UN ARRAY ────────────────────────
    ///
    /// Non esiste una «SkipInstance». Si prende il dato della feature, si
    /// scrive `SkippedItemArray`, e si rimette:
    ///
    ///     var dati = ripetizione.GetDefinition();
    ///     dati.AccessSelections(modello, null);
    ///     dati.SkippedItemArray = new int[] { 2 };
    ///     ripetizione.ModifyDefinition(dati, modello, null);
    ///
    /// E' una scrittura DOPO la creazione, non un parametro: quindi la
    /// ripetizione si fa prima, con le operazioni che gia' ci sono, e poi si
    /// toglie quello che non deve esserci.
    ///
    /// ── GLI INDICI: MISURATI, NON DEDOTTI ─────────────────────────────
    ///
    /// Piastra 200x100, foro a x=-50, ripetizione di 4 con passo 25 verso -X.
    /// Le posizioni sono -50 (il seme), -75, -100, -125.
    ///
    ///     SkippedItemArray = { 2 }   ->  sparisce quello a -100
    ///
    /// Cioe' **l'indice 2 e' la SECONDA COPIA**: il seme non si conta come
    /// copia, e il conto parte da 1. E' anche il modo in cui chi chiede
    /// conterebbe («togli la seconda»), quindi il numero che arriva
    /// dall'esterno si passa cosi' com'e'.
    ///
    /// ── IL DIFETTO: SI PUO' TOGLIERE UNA COPIA CHE NON C'E' ──────────
    ///
    /// E questo e' il punto. Nella stessa prova, la QUARTA posizione (-125)
    /// cadeva fuori dalla piastra: SOLIDWORKS aveva costruito tre copie su
    /// quattro e aveva risposto «riuscito» (e' la nota 80y, ed e' ancora vera).
    ///
    /// Chiedere di saltare una copia che non e' mai esistita **riesce**:
    /// `ModifyDefinition` risponde `true`, l'array si scrive, e nel pezzo non
    /// cambia niente. Chi legge la risposta crede di aver tolto un foro che
    /// non c'era.
    ///
    /// Percio' qui si contano le FACCE della ripetizione prima e dopo, e se
    /// non sono calate ci si rifiuta rimettendo l'array com'era. E' lo stesso
    /// conto di `CopieMancanti`: le facce che la ripetizione possiede divise
    /// per quelle della lavorazione ripetuta sono le copie che esistono
    /// davvero.
    ///
    /// ── E `ModifyDefinition` RISPONDE `true` A COSE CHE NON FA ────────
    ///
    /// Non e' un sospetto: su tre modi di svuotare la lista, **due rispondono
    /// `true` senza cambiare niente** — e uno dei due alza perfino
    /// un'eccezione COM prima di rispondere di si'. Il dettaglio sta su
    /// `DaScrivere`, ed e' il motivo per cui qui il valore di ritorno non e'
    /// mai il criterio.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito SopprimiIstanza(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string motivo;
            var ripetizione = LavorazioneDaToccare(modello, a, out motivo);
            if (ripetizione == null) return Fallito(motivo);
            string uid = Str(a, "feature_uid", "");

            string tipo = "";
            try { tipo = ripetizione.GetTypeName2(); } catch { }

            var copie = Interi(a, "copies");
            if (copie.Count == 0)
                return Fallito(
                    "Manca «copies»: quali copie togliere, per numero. La prima copia " +
                    "dopo l'originale e' la 1.");
            if (copie.Any(c => c < 1))
                // LO ZERO NON E' UNA COPIA, ED E' PEGGIO DI UN NUMERO FUORI
                // INTERVALLO: per SOLIDWORKS `{0}` vuol dire «nessuna copia
                // tolta», quindi passato di la' cosi' com'e' CANCELLEREBBE
                // tutte le copie tolte prima, in silenzio e rispondendo che e'
                // andata bene. Vedi `DaScrivere`.
                return Fallito(
                    "Le copie si contano da 1, e lo ZERO non e' una copia: e' " +
                    "l'originale, quello da cui la ripetizione e' nata. E dall'altra " +
                    "parte lo zero vuol dire «rimettile tutte», per cui passarlo qui " +
                    "cancellerebbe le copie tolte prima invece di toglierne una. " +
                    "L'originale non si toglie da qui — si sopprime la lavorazione, " +
                    "oppure si cancella la ripetizione.");

            bool riattiva = Bool(a, "reactivate", false);

            var dati = LeggiDefinizione(ripetizione, out ISaltaIstanze porta, out string perche);
            if (dati == null)
                return Fallito(
                    "I do not remove copies from «" + uid + "»" +
                    (string.IsNullOrEmpty(tipo) ? "" : " (type " + tipo + ")") + ": " + perche);

            // ── UNA GRIGLIA NON SI CONTA COME UNA FILA (voce Z-C21.2, M6) ──
            //
            // ILinearPatternFeatureData~SkippedItemArray.html, Remarks: «For a
            // bi-directional pattern: I = n2 * ( i - 1) + ( j - 1)», con n2 le
            // istanze in direzione 2 e [i, j] gli indici nelle due direzioni.
            // Qui «copia 2» si scriveva com'era: su una griglia 3 x 2 toglieva
            // l'istanza [2, 1], non la seconda copia di nessuna fila, e
            // rispondeva riuscito. Un numero solo non dice una posizione su una
            // griglia: si rifiuta invece di indovinare.
            // (IsDirection2Specified.html: «True if direction 2 is specified».)
            // Vale anche per la circolare e la guidata da curva, che possono
            // avere una seconda direzione: vedi i loro `Griglia` qui sotto.
            string griglia = porta.Griglia(dati);
            if (griglia != null)
                return Fallito(
                    "«" + uid + "» e' una griglia a due direzioni (" + griglia + "): un numero di copia " +
                    "solo non dice quale posizione togliere, perche' SOLIDWORKS le numera riga per " +
                    "riga. Non indovino. Se ti serve togliere una posizione, fai la ripetizione con " +
                    "una direzione sola e togli la copia da quella fila. Non ho toccato niente.");

            // I SEMI, per sapere quante facce vale una copia (rilievo L6-5).
            // Letti prima di AccessSelections; se non si leggono, il controllo
            // qui sotto non si fa invece di inventarsi un numero.
            int facceDelSeme = 0;
            try
            {
                var semi = porta.Semi(dati) as object[];
                if (semi != null)
                    foreach (var s in semi)
                    {
                        int n = FacceDellaFeature(s as IFeature);
                        if (n < 0) { facceDelSeme = 0; break; }
                        facceDelSeme += n;
                    }
            }
            catch { facceDelSeme = 0; }

            int[] giaSaltate;
            try { giaSaltate = porta.Leggi(dati) ?? new int[0]; }
            catch (Exception ex) { return Fallito("Non riesco a leggere le copie gia' tolte: " + ex.Message); }

            var nuove = riattiva
                ? giaSaltate.Where(x => !copie.Contains(x)).ToArray()
                : giaSaltate.Union(copie).OrderBy(x => x).ToArray();

            if (nuove.Length == giaSaltate.Length)
                // GIA' COM'E' CHIESTA. Non e' un errore ma non e' «fatto»:
                // stessa regola di `suppress_feature`.
                return Fallito(
                    "Le copie " + Elenco(copie) + " di «" + uid + "» sono gia' " +
                    (riattiva ? "attive" : "tolte") + ": non ho cambiato niente. " +
                    "Quelle tolte adesso sono " +
                    (giaSaltate.Length == 0 ? "nessuna" : Elenco(giaSaltate)) + ".");

            int faccePrima = FacceDellaFeature(ripetizione);
            double? volumePrima = VolumeMm3(modello);
            int corpiPrima = ContaCorpiSolidi(modello);

            bool fatto;
            try
            {
                // `AccessSelections` prima di toccare il dato, e in caso di
                // guaio `ReleaseSelectionAccess`: senza, la feature resta
                // bloccata e le operazioni dopo falliscono per un motivo che
                // non ha piu' niente a che vedere con questa.
                //
                // THE ACCESS IS READ (Z-C21.8, the rule of L5-5): each family's
                // AccessSelections.html, «True if the selections are
                // successfully accessed, false if not». A refused access stops
                // before anything is written, with nothing to release.
                if (!porta.Apri(dati, modello))
                    return Fallito("SOLIDWORKS did not give access to the definition of «" + uid +
                                   "» (AccessSelections answered false): nothing was written, the pattern is as it was.");
                porta.Scrivi(dati, DaScrivere(nuove));
                fatto = ApiHelpers.ModifyAndVerify(ripetizione, dati, modello).Applied;
                if (!fatto) porta.Chiudi(dati);
            }
            catch (Exception ex)
            {
                try { porta.Chiudi(dati); } catch { }
                return Fallito("SOLIDWORKS non ha accettato la modifica: " + ex.Message);
            }
            if (!fatto)
                return Fallito(
                    "SOLIDWORKS ha rifiutato di togliere le copie " + Elenco(copie) +
                    " da «" + uid + "», e non dice perche'.");

            int facceDopo = FacceDellaFeature(ripetizione);
            double? volumeDopo = VolumeMm3(modello);
            int quante = Math.Abs(nuove.Length - giaSaltate.Length);

            // ── IN ERRORE DOPO LA MODIFICA? (voce Z-C21.2, rilievo L6-5) ────
            //
            // IFeature~ModifyDefinition.html, Remarks: «this method may leave
            // the feature in an unpredictable state due to errors. After calling
            // this method, call IFeature::GetErrorCode2 to ascertain whether the
            // feature has rebuild errors». Si guardava solo il bool, che qui —
            // lo dice gia' il commento in testa — non e' mai il criterio.
            // Read again here, after faces and volume, where it was read before
            // the helper existed (ApiHelpers.RebuildError, same rule).
            string ripetizioneInErrore = ApiHelpers.RebuildError(ripetizione);
            if (ripetizioneInErrore != null)
            {
                string rimesso = Rimetti(modello, ripetizione, porta, giaSaltate);
                return Fallito("Dopo aver tolto le copie " + Elenco(copie) + " «" + uid + "» e' in errore (" +
                               ripetizioneInErrore + "). " + rimesso);
            }

            // ── UNA RIPETIZIONE RIMASTA SENZA FACCE NON HA «TOLTO UNA COPIA» ──
            //
            // (rilievo L6-5.) Il controllo qui sotto chiedeva solo che le facce
            // calassero: una ripetizione andata in errore che le perde TUTTE
            // (GetFaces a null da' 0) lo passava. E la perdita deve valere le
            // facce di una copia per le copie tolte: la stessa aritmetica di
            // CopieMancanti (OperazioniSw.cs), che misura le copie che esistono
            // davvero dividendo le facce della ripetizione per quelle del seme.
            if (!riattiva && faccePrima > 0 && facceDopo == 0)
            {
                string rimesso = Rimetti(modello, ripetizione, porta, giaSaltate);
                return Fallito("Dopo aver tolto le copie " + Elenco(copie) + " «" + uid + "» non ha piu' " +
                               "nessuna faccia: e' sparita tutta la ripetizione, non una copia. " + rimesso);
            }
            if (facceDelSeme > 0 && faccePrima >= 0 && facceDopo >= 0 &&
                Math.Abs(facceDopo - faccePrima) != 0 &&
                Math.Abs(facceDopo - faccePrima) != facceDelSeme * quante)
            {
                string rimesso = Rimetti(modello, ripetizione, porta, giaSaltate);
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "Le facce della ripetizione sono passate da {0} a {1}, ma {2} {3} di «{4}» ne " +
                    "valgono {5} ({6} per copia): {7} Guarda «copies_done» su questa ripetizione. {8}",
                    faccePrima, facceDopo, quante, quante == 1 ? "copia" : "copie", uid,
                    facceDelSeme * quante, facceDelSeme,
                    riattiva ? "qualche copia rimessa non e' tornata." : "qualche copia tolta non c'era.",
                    rimesso));
            }

            // ── E QUI SI GUARDA SE E' SUCCESSO QUALCOSA ──────────────────
            //
            // `ModifyDefinition` risponde `true` anche quando si chiede di
            // togliere una copia che non e' mai esistita -- perche' cadeva
            // fuori dal materiale, e SOLIDWORKS quelle le costruisce e basta
            // senza dirlo (nota 80y). L'array si scrive, il pezzo non cambia,
            // e chi legge crede di aver tolto un foro.
            bool andataNellaDirezioneGiusta = riattiva
                ? facceDopo > faccePrima
                : facceDopo < faccePrima;
            if (faccePrima > 0 && facceDopo >= 0 && !andataNellaDirezioneGiusta)
            {
                string rimesso = Rimetti(modello, ripetizione, porta, giaSaltate);
                return Fallito(string.Format(
                    CultureInfo.InvariantCulture,
                    "Ho scritto la modifica e SOLIDWORKS ha detto di si', ma il pezzo non " +
                    "e' cambiato: le facce della ripetizione erano {0} e sono {1}. Quasi " +
                    "sempre vuol dire che {2} non {3}: una ripetizione costruisce le copie " +
                    "che stanno nel materiale e risponde «riuscito» lo stesso, quindi la " +
                    "copia {4} puo' non esserci mai stata. Guarda «copies_done» su questa " +
                    "ripetizione prima di togliere. {5}",
                    faccePrima, facceDopo,
                    copie.Count == 1 ? "quella copia" : "quelle copie",
                    riattiva ? "mancava" : "c'era",
                    Elenco(copie),
                    rimesso));
            }

            int corpiDopo = ContaCorpiSolidi(modello);
            var misure = new Dictionary<string, object>
            {
                ["copies_removed_now"] = riattiva ? new int[0] : copie.ToArray(),
                ["copies_restored_now"] = riattiva ? copie.ToArray() : new int[0],
                ["copies_removed_total"] = nuove,
                ["pattern_faces"] = NumeroOppureNull(facceDopo),
                ["faces_before"] = NumeroOppureNull(faccePrima),
                // QUANTE FACCE VALE UNA COPIA, o null se i semi non si sono letti
                // (revisione di Z-C21.2): con null il controllo «perdita = facce
                // del seme x copie tolte» qui sopra NON si e' fatto, e chi legge
                // lo deve poter vedere. Nome nuovo, in inglese.
                ["faces_per_copy"] = facceDelSeme > 0 ? (object)facceDelSeme : null,
                ["bodies"] = NumeroOppureNull(corpiDopo),
                [riattiva ? "material_removed_mm3" : "material_returned_mm3"] =
                    NumeroOppureNull(volumeDopo.HasValue && volumePrima.HasValue
                        ? Math.Abs(volumeDopo.Value - volumePrima.Value) : (double?)null, 2),
            };
            // «Spezzato» si dice di due conteggi LETTI (voce Z-C8): prima il
            // confronto era fra un int e un oggetto, e con una lettura fallita
            // lo zero diceva che il pezzo si era rotto.
            if (corpiPrima >= 0 && corpiDopo >= 0 && corpiPrima != corpiDopo)
                misure["warning_bodies"] =
                    "I corpi sono passati da " + corpiPrima + " a " + corpiDopo +
                    ": togliere una copia ha spezzato il pezzo.";

            _log("[agente] «" + uid + "»: copie " + (riattiva ? "rimesse " : "tolte ") +
                 Elenco(copie) + ", facce " + faccePrima + " -> " + facceDopo);

            return Riuscito(ripetizione, misure);
        }

        // =================================================================

        /// <summary>
        /// Rimette l'array com'era. Torna la frase da appendere al rifiuto:
        /// chi legge deve sapere se il pezzo e' stato lasciato a posto o no.
        /// </summary>
        private static string Rimetti(
            IModelDoc2 modello, IFeature ripetizione, ISaltaIstanze porta, int[] comEra)
        {
            try
            {
                var d = ripetizione.GetDefinition();
                if (d == null) return "Non sono riuscito a rimettere le cose com'erano.";
                // The access is read (Z-C21.8, rule of L5-5): refused, nothing
                // can be written back.
                if (!porta.Apri(d, modello))
                    return "I could not put the skipped copies back: AccessSelections answered false. Check the " +
                           "pattern, and undo the step (" + Frasi.CosiSiRitira + ").";
                porta.Scrivi(d, DaScrivere(comEra));
                // S11: Applied alone is not the verdict (IFeature~ModifyDefinition.html,
                // Remarks: it can return true and leave a new rebuild error). The
                // sentence "put back as it was" is said only when the feature is
                // known to be sound.
                var change = ApiHelpers.ModifyAndVerify(ripetizione, d, modello);
                if (change.Applied && change.InError)
                    return "I put the pattern definition back, but the pattern is now in a rebuild error (" +
                           change.ErrorPhrase + "): check the pattern.";
                if (change.Applied && !change.ErrorCodeKnown)
                    return "I put the pattern definition back, but I could not read whether it rebuilds " +
                           "without errors: check the pattern.";
                if (change.Applied)
                {
                    // «Put back» after a reread (Z-C21.8, finding M42's rule): the
                    // skipped copies read from a fresh definition must be the ones
                    // read before the change.
                    int[] now = null;
                    try { now = porta.Leggi(ripetizione.GetDefinition()); } catch { now = null; }
                    if (now != null && now.OrderBy(x => x).SequenceEqual((comEra ?? new int[0]).OrderBy(x => x)))
                        return "Ho rimesso la ripetizione com'era.";
                    return "I put the pattern definition back, but reading it back the skipped copies are " +
                           (now == null ? "not readable" : "[" + Elenco(now) + "]") + " instead of [" +
                           Elenco(comEra ?? new int[0]) + "]: check the pattern.";
                }
                porta.Chiudi(d);
            }
            catch { }
            return "Non sono riuscito a rimettere le cose com'erano: guarda la ripetizione.";
        }

        /// <summary>
        /// Come si leggono e si scrivono le copie saltate di UNA famiglia di
        /// ripetizioni.
        ///
        /// Serve perche' `SkippedItemArray` c'e' su sei interfacce diverse e
        /// **non c'e' un'interfaccia comune**. Non e' una supposizione: scritto
        /// `IFeatureData` come tipo del parametro, il compilatore risponde
        /// «il nome di tipo non e' stato trovato». `ILinearPatternFeatureData` e
        /// `ICircularPatternFeatureData` non hanno niente in mezzo — nemmeno
        /// `AccessSelections`, che pure hanno tutte e due con la stessa firma.
        ///
        /// Quindi il dato gira come `object` e ogni famiglia sa fare le sue
        /// quattro cose. Si prova una per volta, e quella che risponde vince.
        /// </summary>
        private interface ISaltaIstanze
        {
            int[] Leggi(object dati);
            void Scrivi(object dati, int[] copie);
            // The answer of AccessSelections (Z-C21.8, rule of L5-5).
            bool Apri(object dati, IModelDoc2 modello);
            void Chiudi(object dati);
            // Voce Z-C21.2: le lavorazioni ripetute (PatternFeatureArray), e
            // se la ripetizione e' una griglia a due direzioni (null se no).
            object Semi(object dati);
            string Griglia(object dati);
        }

        private sealed class SaltaLineare : ISaltaIstanze
        {
            public int[] Leggi(object d) { return Numeri(((ILinearPatternFeatureData)d).SkippedItemArray); }
            public void Scrivi(object d, int[] c) { ((ILinearPatternFeatureData)d).SkippedItemArray = c; }
            public bool Apri(object d, IModelDoc2 m) { return ((ILinearPatternFeatureData)d).AccessSelections(m, null); }
            public void Chiudi(object d) { ((ILinearPatternFeatureData)d).ReleaseSelectionAccess(); }
            public object Semi(object d) { return ((ILinearPatternFeatureData)d).PatternFeatureArray; }
            public string Griglia(object d)
            {
                var l = (ILinearPatternFeatureData)d;
                bool due = false; int n1 = 0, n2 = 0;
                try { due = l.IsDirection2Specified(); n1 = l.D1TotalInstances; n2 = l.D2TotalInstances; }
                catch { return null; }
                return due && n2 > 1 ? n1 + " x " + n2 : null;
            }
        }

        private sealed class SaltaCircolare : ISaltaIstanze
        {
            public int[] Leggi(object d) { return Numeri(((ICircularPatternFeatureData)d).SkippedItemArray); }
            public void Scrivi(object d, int[] c) { ((ICircularPatternFeatureData)d).SkippedItemArray = c; }
            public bool Apri(object d, IModelDoc2 m) { return ((ICircularPatternFeatureData)d).AccessSelections(m, null); }
            public void Chiudi(object d) { ((ICircularPatternFeatureData)d).ReleaseSelectionAccess(); }
            public object Semi(object d) { return ((ICircularPatternFeatureData)d).PatternFeatureArray; }
            // ANCHE LA CIRCOLARE PUO' AVERE DUE DIREZIONI (revisione di Z-C21.2,
            // M6 corretto a meta'). ICircularPatternFeatureData~SkippedItemArray.html
            // porta la stessa formula della lineare: «For a bi-directional
            // pattern: I = n2 * ( i - 1) + ( j - 1)». Direction2.html: «True to
            // create a bidirectional circular pattern feature»; TotalInstances2.html:
            // «only available when Direction2 is true, and Symmetric is false» —
            // con Symmetric la seconda direzione ripete il numero della prima
            // (Symmetric.html: «symmetric or asymmetric circular pattern feature
            // in Direction 2»).
            public string Griglia(object d)
            {
                var c = (ICircularPatternFeatureData)d;
                bool due = false, simmetrica = false; int n1 = 0, n2 = 0;
                try
                {
                    due = c.Direction2;
                    if (!due) return null;
                    n1 = c.TotalInstances;
                    simmetrica = c.Symmetric;
                    n2 = simmetrica ? n1 : c.TotalInstances2;
                }
                catch { return null; }
                return n2 > 1 ? n1 + " x " + n2 + (simmetrica ? ", simmetrica" : "") : null;
            }
        }

        // E DELLE NOVE FAMIGLIE DI RIPETIZIONE, TRE NON HANNO L'ARRAY.
        //
        // Misurato per riflessione sull'interop, non supposto:
        //
        //   ILinearPatternFeatureData        SkippedItemArray  si'
        //   ICircularPatternFeatureData      si'
        //   ICurveDrivenPatternFeatureData   si'
        //   IFillPatternFeatureData          si'
        //   ITablePatternFeatureData         si'
        //   IDerivedPatternFeatureData       si'   (an assembly pattern: never here, C7-3)
        //   ISketchPatternFeatureData        NO
        //   IChainPatternFeatureData         NO
        //   IDimPatternFeatureData           NO
        //
        // Tutte e nove hanno `AccessSelections`, che e' il motivo per cui
        // verrebbe da credere che siano intercambiabili. Non lo sono, e
        // scriverlo ha fatto fallire la compilazione — che e' il modo migliore
        // di scoprirlo: una riflessione dimenticata sarebbe diventata un
        // «SkippedItemArray non esiste» a runtime, su un pezzo di qualcuno.

        private sealed class SaltaCurva : ISaltaIstanze
        {
            public int[] Leggi(object d) { return Numeri(((ICurveDrivenPatternFeatureData)d).SkippedItemArray); }
            public void Scrivi(object d, int[] c) { ((ICurveDrivenPatternFeatureData)d).SkippedItemArray = c; }
            public bool Apri(object d, IModelDoc2 m) { return ((ICurveDrivenPatternFeatureData)d).AccessSelections(m, null); }
            public void Chiudi(object d) { ((ICurveDrivenPatternFeatureData)d).ReleaseSelectionAccess(); }
            public object Semi(object d) { return ((ICurveDrivenPatternFeatureData)d).PatternFeatureArray; }
            // E LA GUIDATA DA CURVA (revisione di Z-C21.2): Dir2Specified.html,
            // «whether Direction 2 is specified for this curve-driven pattern
            // feature»; D2InstanceCount.html, «the number of instances in
            // Direction 2 of this bidirectional curve-driven pattern feature».
            public string Griglia(object d)
            {
                var c = (ICurveDrivenPatternFeatureData)d;
                bool due = false; int n1 = 0, n2 = 0;
                try { due = c.Dir2Specified; n1 = c.D1InstanceCount; n2 = c.D2InstanceCount; }
                catch { return null; }
                return due && n2 > 1 ? n1 + " x " + n2 : null;
            }
        }

        private static object LeggiDefinizione(
            IFeature ripetizione, out ISaltaIstanze porta, out string perche)
        {
            porta = null;
            perche = null;
            object grezzo;
            try { grezzo = ripetizione.GetDefinition(); }
            catch (Exception ex) { perche = "I cannot read its definition: " + ex.Message; return null; }
            if (grezzo == null)
            {
                perche = "SOLIDWORKS gives no definition for it.";
                return null;
            }

            if (grezzo is ILinearPatternFeatureData) porta = new SaltaLineare();
            else if (grezzo is ICircularPatternFeatureData) porta = new SaltaCircolare();
            else if (grezzo is ICurveDrivenPatternFeatureData) porta = new SaltaCurva();

            if (porta == null) perche = NotANumberedPattern(grezzo is IFillPatternFeatureData, grezzo is ITablePatternFeatureData);
            return porta == null ? null : grezzo;
        }

        /// <summary>
        /// Why the copies of this pattern are not removed here, by family
        /// (Z-C21.8, finding C7-3). The refusal stays right; what it said was
        /// wrong. «Not a pattern with numbered copies» was said of every family
        /// but linear, circular and curve-driven, and a FILL pattern and a
        /// TABLE pattern do skip instances: IFillPatternFeatureData~SkippedItemArray.html,
        /// «contains the (row_id, column_id) of each pattern instance to skip»,
        /// and ITablePatternFeatureData~SkippedItemArray.html, «the skipped items
        /// for this table-driven pattern feature». They are not numbered as one
        /// row of copies, and a copy number is not translated into their
        /// positions here. The derived pattern also has the array, but it is an
        /// assembly pattern and suppress_instance runs on parts (DocumentoRichiesto
        /// «part»), so it is not named. Kept apart from the document so that
        /// Prove.Contratti can call it without SOLIDWORKS.
        /// </summary>
        internal static string NotANumberedPattern(bool fill, bool table)
        {
            const string instead =
                " Copies are removed here from «linear_pattern» and «circular_pattern» patterns " +
                "(and curve-driven ones), whose copies are counted 1, 2, 3 along one row.";
            if (fill)
                return "it is a FILL pattern: SOLIDWORKS skips its instances by (row, column), not by one copy " +
                       "number, and I do not translate a copy number into a row and a column." + instead +
                       " Remove the instance of the fill pattern by hand.";
            if (table)
                return "it is a TABLE-driven pattern: its instances are the rows of its table, not copies " +
                       "counted along one row." + instead + " Remove the row of the table by hand.";
            return "it is not a pattern with numbered copies: patterns driven by a sketch, a chain or a " +
                   "dimension keep no list of skipped instances, and a mirror has one copy only, removed by " +
                   "deleting it." + instead;
        }

        /// <summary>
        /// L'array come SOLIDWORKS vuole riceverlo.
        ///
        /// ── SVUOTARE LA LISTA SI SCRIVE `{0}`, NON `{}` ──────────────────
        ///
        /// Misurato il 16/9/2026, tre modi su tre documenti nuovi, dopo aver
        /// tolto la copia 2 da una fila di quattro:
        ///
        ///   SkippedItemArray = new int[0]   ModifyDefinition -> TRUE
        ///                                   facce 2 (erano 2)   rilettura {2}
        ///                                   -> IGNORATO IN SILENZIO
        ///   SkippedItemArray = null         la SCRITTURA alza
        ///                                   RPC_E_SERVERFAULT (0x80010105),
        ///                                   poi ModifyDefinition -> TRUE
        ///                                   e non e' cambiato niente
        ///   SkippedItemArray = new int[]{0} ModifyDefinition -> TRUE
        ///                                   facce 2 -> 3, volume tornato
        ///                                   rilettura: int[0] {}
        ///
        /// **Lo zero e' il modo di dire «none», non un indice.** E si scrive
        /// in un modo e si rilegge in un altro: si manda `{0}`, torna `{}`.
        ///
        /// E' anche la ragione vera per cui `copies: [0]` si rifiuta dal di
        /// fuori. Non perche' «lo zero e' l'originale» — perche' passato di la'
        /// cosi' com'e' **cancellerebbe tutte le copie tolte prima**, in
        /// silenzio, rispondendo che e' andata bene.
        ///
        /// Delle tre risposte, due sono `true` su una cosa che non e'
        /// successa: il valore di ritorno qui non vale niente, e l'unica prova
        /// e' il conto delle facce.
        /// </summary>
        private static int[] DaScrivere(int[] copie)
        {
            return copie == null || copie.Length == 0 ? new[] { 0 } : copie;
        }

        private static int[] Numeri(object grezzo)
        {
            if (grezzo == null) return new int[0];
            var i = grezzo as int[];
            if (i != null) return i;
            var o = grezzo as System.Collections.IEnumerable;
            if (o == null) return new int[0];
            var fuori = new List<int>();
            foreach (var v in o)
            {
                try { fuori.Add(Convert.ToInt32(v, CultureInfo.InvariantCulture)); }
                catch { }
            }
            return fuori.ToArray();
        }

        /// <summary>Un elenco di interi da un argomento, che puo' anche essere un numero solo.</summary>
        private static List<int> Interi(Dictionary<string, object> a, string chiave)
        {
            var fuori = new List<int>();
            object v;
            if (a == null || !a.TryGetValue(chiave, out v) || v == null) return fuori;
            if (v is string)
            {
                int uno;
                if (int.TryParse((string)v, NumberStyles.Any, CultureInfo.InvariantCulture, out uno))
                    fuori.Add(uno);
                return fuori;
            }
            var elenco = v as System.Collections.IEnumerable;
            if (elenco == null)
            {
                try { fuori.Add(Convert.ToInt32(v, CultureInfo.InvariantCulture)); }
                catch { }
                return fuori;
            }
            foreach (var x in elenco)
            {
                try { fuori.Add(Convert.ToInt32(x, CultureInfo.InvariantCulture)); }
                catch { }
            }
            return fuori;
        }

        private static string Elenco(IEnumerable<int> numeri)
        {
            var v = numeri.ToArray();
            if (v.Length == 0) return "none";
            if (v.Length == 1) return v[0].ToString(CultureInfo.InvariantCulture);
            return string.Join(", ", v.Take(v.Length - 1).Select(x => x.ToString(CultureInfo.InvariantCulture))) +
                   " e " + v[v.Length - 1].ToString(CultureInfo.InvariantCulture);
        }
    }
}
