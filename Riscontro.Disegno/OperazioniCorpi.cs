using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LE BOOLEANE FRA CORPI: unisci, sottrai, interseca. (voce GP8)
    ///
    /// Servono agli stampi e alle dime, ai pezzi compositi disegnati come piu'
    /// corpi e poi uniti, e ai pezzi da dividere per la stampa 3D quando non
    /// stanno nel piatto.
    ///
    /// ── DUE `InsertCombineFeature`, E UNA SOLA E' QUELLA GIUSTA ─────────
    ///
    /// Lette per riflessione dall'interop 29.5.1.1:
    ///
    ///   IFeatureManager.InsertCombineFeature(int, Body2, object) -> Feature
    ///   IPartDoc       .InsertCombineFeature(int, object, object) -> Boolean
    ///
    /// Stessa arita', stesso nome, tipi e ritorno diversi, e **non sono l'una
    /// l'evoluzione dell'altra**. Quella su `IPartDoc` torna un booleano: la
    /// feature, se nasce, non e' raggiungibile — e un passo successivo che
    /// volesse citarla non avrebbe niente da citare. Qui si usa quella del
    /// FeatureManager.
    ///
    /// ── L'ENUM COL NOME GIUSTO E' QUELLO SBAGLIATO ──────────────────────
    ///
    /// In `swconst` c'e' `swCombineBodiesOperationType_e`, e i suoi tre valori
    /// si chiamano **Add = 0, Subtract = 1, Common = 2**: i nomi esatti delle
    /// tre booleane. Passati a `InsertCombineFeature` **tornano `null` tutti e
    /// tre**, senza un'eccezione e senza un codice.
    ///
    /// Quello che il metodo vuole e' `swBodyOperationType_e`:
    ///
    ///     SWBODYADD       = 15903     unisci
    ///     SWBODYCUT       = 15902     sottrai
    ///     SWBODYINTERSECT = 15901     interseca
    ///
    /// **Un nome dice come si chiama una costante, non a quale funzione
    /// appartiene.** Era il rischio scritto nella voce — «un valore sbagliato
    /// consuma il corpo sbagliato senza errore» — e la realta' e' stata piu'
    /// mite di cosi': non consuma niente, non fa niente, e non lo dice.
    ///
    /// ── E L'ARRAY VA AVVOLTO, E CONTIENE ANCHE IL PRINCIPALE ────────────
    ///
    /// Misurato su pezzi nuovi, una variante per pezzo (provarle di fila sullo
    /// stesso documento dava risultati incoerenti):
    ///
    ///                              ADD 15903   CUT 15902   INTERSECT 15901
    ///   object[] col solo utensile    NULL        NULL          NULL
    ///   Body2[] tipizzato             NULL      «Abbina1»       NULL
    ///   DispatchWrapper[] utensile    NULL      «Abbina1»       NULL
    ///   DispatchWrapper[] TUTTI E DUE «Abbina1» «Abbina1»     «Abbina1»
    ///
    /// Due cose insieme, e nessuna delle due protesta quando manca:
    ///
    /// 1. **`object[]` di RCW non arriva mai.** Il parametro e' un `VARIANT`
    ///    che vuole un SAFEARRAY di `IDispatch*`, e un `object[]` di oggetti
    ///    COM non lo diventa da solo: ci vuole `DispatchWrapper`.
    /// 2. **L'array deve contenere anche il corpo principale**, non solo gli
    ///    utensili — per `unisci` e `interseca` senza di lui non succede
    ///    niente. Per `sottrai` basterebbe l'utensile, ma metterlo dentro non
    ///    cambia il risultato (600 mm³ in tutti e due i modi), e una regola
    ///    sola vale piu' di tre.
    ///
    /// ── I TRE NUMERI, SU UN CASO CHE LI DISTINGUE ───────────────────────
    ///
    /// Due cubi da 10 sfalsati di 2 in X e 5 in Z, sovrapposti per
    /// 8 x 10 x 5 = 400 mm³:
    ///
    ///     unisci      1000 + 1000 - 400 = 1600 mm³
    ///     sottrai     1000 - 400        =  600 mm³
    ///     interseca                        400 mm³
    ///
    /// Con due cubi uguali e centrati i tre numeri sarebbero stati 1500, 500 e
    /// 500 — e «sottrai» e «interseca» non si sarebbero distinti. Un caso in
    /// cui due valori sbagliati danno lo stesso risultato non tabella niente.
    ///
    /// ── E SI CONTANO I CORPI ────────────────────────────────────────────
    ///
    /// Un'unione che fallisce in silenzio lascia **due corpi** dove ne serviva
    /// uno: l'albero cresce uguale, la forma sembra la stessa, e la differenza
    /// si vede solo contando. E' lo stesso controllo che `estrudi_su_faccia`
    /// fa gia' sul suo `Merge`.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito CombinaCorpi(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var parte = modello as IPartDoc;
            if (parte == null)
                return Fallito("Questo documento non e' un pezzo: le booleane si fanno fra corpi.");

            string comeChiesta = (Str(a, "operazione", "") ?? "").Trim().ToLowerInvariant();
            int tipo;
            // `swBodyOperationType_e`, NON `swCombineBodiesOperationType_e`:
            // vedi il commento in testa al file.
            if (comeChiesta == "unisci") tipo = (int)swBodyOperationType_e.SWBODYADD;
            else if (comeChiesta == "sottrai") tipo = (int)swBodyOperationType_e.SWBODYCUT;
            else if (comeChiesta == "interseca") tipo = (int)swBodyOperationType_e.SWBODYINTERSECT;
            else return Fallito(
                "Non hai detto che booleana fare, o non la conosco: «" + comeChiesta +
                "». Ci sono «unisci», «sottrai» e «interseca».");

            var grezzi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (grezzi == null || grezzi.Length < 2)
                return Fallito(
                    "Per una booleana ci vogliono almeno due corpi, e questo pezzo ne ha " +
                    (grezzi == null ? 0 : grezzi.Length) + ". I corpi separati nascono con " +
                    "«sposta_corpi» e la copia, o con un'estrusione che non si fonde.");

            // ── CHI E' IL PRINCIPALE, E CHI GLI VA ADDOSSO ───────────────
            //
            // Nella sottrazione l'ordine CAMBIA IL PEZZO: A meno B non e' B
            // meno A. Il principale si dice, e se non si dice e' il primo —
            // ma allora si scrive nella risposta, perche' chi legge deve
            // sapere quale dei due e' rimasto.
            string uidPrincipale = Str(a, "corpo_principale_uid", null);
            int indicePrincipale = 0;
            if (!string.IsNullOrWhiteSpace(uidPrincipale))
            {
                indicePrincipale = IndiceDelCorpo(uidPrincipale, grezzi.Length);
                if (indicePrincipale < 0)
                    return Fallito(
                        "Non trovo il corpo «" + uidPrincipale + "»: questo pezzo ne ha " +
                        grezzi.Length + ", da C1 a C" + grezzi.Length + ".");
            }

            var utensili = new List<object>();
            var nomiUtensili = new List<string>();
            var chiesti = Lista(a, "corpi_utensile_uid");
            if (chiesti != null && chiesti.Count > 0)
            {
                foreach (var u in chiesti)
                {
                    int i = IndiceDelCorpo(u, grezzi.Length);
                    if (i < 0)
                        return Fallito("Non trovo il corpo «" + u + "»: questo pezzo ne ha " +
                                       grezzi.Length + ", da C1 a C" + grezzi.Length + ".");
                    if (i == indicePrincipale)
                        return Fallito(
                            "«" + u + "» e' anche il corpo principale: un corpo non si " +
                            "combina con se stesso.");
                    utensili.Add(grezzi[i]);
                    nomiUtensili.Add("C" + (i + 1));
                }
            }
            else
            {
                for (int i = 0; i < grezzi.Length; i++)
                {
                    if (i == indicePrincipale) continue;
                    utensili.Add(grezzi[i]);
                    nomiUtensili.Add("C" + (i + 1));
                }
            }
            if (utensili.Count == 0)
                return Fallito("Non e' rimasto nessun corpo da combinare col principale.");

            double volumePrima = VolumeMm3(modello);
            int corpiPrima = ContaCorpiSolidi(modello);

            IFeature feature;
            try
            {
                modello.ClearSelection2(true);
                // AVVOLTI, E COL PRINCIPALE DENTRO: vedi il commento in testa.
                // Un `object[]` di oggetti COM non diventa un SAFEARRAY di
                // IDispatch*, e senza il principale nell'elenco «unisci» e
                // «interseca» non fanno niente — senza dirlo.
                var daPassare = new List<DispatchWrapper>();
                daPassare.Add(new DispatchWrapper(grezzi[indicePrincipale]));
                foreach (var u in utensili) daPassare.Add(new DispatchWrapper(u));

                feature = modello.FeatureManager.InsertCombineFeature(
                    tipo, grezzi[indicePrincipale] as Body2, daPassare.ToArray()) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("La booleana non e' riuscita: " + ex.Message);
            }

            // NULL SENZA UN'ECCEZIONE E SENZA UN CODICE: e' tutto quello che
            // questa API dice quando non le va bene qualcosa. Il motivo piu'
            // frequente e' geometrico — i corpi non si toccano davvero — ma
            // non e' l'unico, e la frase lo dice invece di far credere che sia
            // certa.
            if (feature == null)
                return Fallito(
                    "La booleana «" + comeChiesta + "» non ha creato niente, e SOLIDWORKS " +
                    "non dice perche': questa chiamata risponde «niente» e basta. Di solito " +
                    "i corpi non si toccano davvero: «unisci» vuole corpi che si " +
                    "compenetrano o si toccano, «sottrai» e «interseca» vogliono che si " +
                    "sovrappongano. Guarda gli ingombri con «ingombro_corpo».");

            try { modello.EditRebuild3(); } catch { }

            double volumeDopo = VolumeMm3(modello);
            int corpiDopo = ContaCorpiSolidi(modello);

            // UN'UNIONE CHE LASCIA DUE CORPI E' FALLITA IN SILENZIO. La
            // feature c'e', l'albero e' cresciuto, e il pezzo e' un altro.
            if (tipo == (int)swCombineBodiesOperationType_e.swCombineBodiesOperationAdd &&
                corpiDopo >= corpiPrima)
                return Fallito(
                    "L'unione ha creato la feature ma i corpi sono passati da " + corpiPrima +
                    " a " + corpiDopo + ": non si sono fusi. Di solito e' che non si toccano " +
                    "davvero. Non ti confermo un pezzo diverso da quello che ti ho mostrato: " +
                    "ritiralo — " + Frasi.CosiSiRitira + ".");

            if (volumeDopo <= 0)
                return Fallito(
                    "Dopo la booleana il pezzo non ha piu' volume: «" + comeChiesta +
                    "» ha consumato tutto. Di solito e' l'ordine — nella sottrazione il " +
                    "corpo principale e' quello che RESTA.");

            var misure = new Dictionary<string, object>
            {
                ["operazione"] = comeChiesta,
                ["corpo_principale"] = "C" + (indicePrincipale + 1),
                ["corpi_utensile"] = nomiUtensili.ToArray(),
                ["corpi_prima"] = corpiPrima,
                ["corpi"] = corpiDopo,
                ["volume_prima_mm3"] = Math.Round(volumePrima, 2),
                ["volume_mm3"] = Math.Round(volumeDopo, 2),
                ["volume_cambiato_mm3"] = Math.Round(volumeDopo - volumePrima, 2),
                // GLI IDENTIFICATIVI NON VALGONO PIU', e si dice sempre: una
                // booleana rifa' le facce da capo.
                ["nota_identificativi"] =
                    "I corpi e le facce sono cambiati: gli identificativi (C2, F1, E4) di " +
                    "prima non valgono piu'. Rileggi il pezzo prima del passo seguente.",
            };
            _log("[agente] " + comeChiesta + ": corpi " + corpiPrima + " -> " + corpiDopo +
                 ", volume " + volumePrima.ToString("0.#") + " -> " + volumeDopo.ToString("0.#"));
            return Riuscito(feature, misure);
        }

        /// <summary>
        /// Da «C2» all'indice 1. Meno uno se non e' un identificativo di corpo
        /// o se il corpo non c'e': i due casi si dicono uguale a chi chiama,
        /// perche' per chi ha scritto «C9» su un pezzo con due corpi la
        /// differenza non aiuta.
        /// </summary>
        private static int IndiceDelCorpo(object uid, int quanti)
        {
            string s = Convert.ToString(uid);
            if (string.IsNullOrWhiteSpace(s)) return -1;
            s = s.Trim();
            if (s.Length < 2 || (s[0] != 'C' && s[0] != 'c')) return -1;
            int n;
            if (!int.TryParse(s.Substring(1), out n)) return -1;
            if (n < 1 || n > quanti) return -1;
            return n - 1;
        }
    }
}
