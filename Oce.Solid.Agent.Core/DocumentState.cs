using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// Fotografia di cio' che c'e' adesso nel documento aperto.
    ///
    /// A COSA SERVE
    /// Il modello linguistico gira sul server e SolidWorks no. Se deve proporre
    /// "un foro su quella faccia", qualcuno gli deve dire quali facce esistono.
    /// Questa e' quella descrizione: piccola, numerica, senza niente che si
    /// possa scambiare per un comando.
    ///
    /// GLI IDENTIFICATIVI SONO NOSTRI E DURANO POCO
    /// F1, F2, E7 non sono nomi di SOLIDWORKS: sono etichette che assegniamo
    /// noi enumerando in ordine deterministico. Insieme a ognuna teniamo una
    /// FIRMA (area o lunghezza, e un punto sull'entita'): prima di eseguire
    /// un'operazione si rienumera e si ricontrolla la firma. Se non combacia —
    /// perche' l'utente nel frattempo ha aggiunto una feature — l'operazione si
    /// rifiuta invece di lavorare sulla faccia sbagliata.
    ///
    /// Nessun riferimento persistente di SOLIDWORKS (GetPersistReference e
    /// simili): non serve per un piano che dura minuti, e sarebbe una API in
    /// piu' da verificare per un problema che non abbiamo.
    ///
    /// I TETTI CI SONO E SI DICHIARANO
    /// Un pezzo di lamiera piegata ha centinaia di facce. Mandarle tutte a un
    /// modello linguistico costa token e non aiuta: si mandano le piu' grandi,
    /// e si scrive `truncated: true` invece di far credere che siano tutte.
    ///
    /// Sono quattro, e ognuno dice di essere scattato:
    ///
    ///   MaxFaces, MaxEdges  -> `truncated: true`
    ///   MaxBodies              -> `bodies_not_listed` + `bodies_total`
    ///   MaxFeature            -> `feature_not_listed` + `feature_total`
    ///                            + `truncated: true`
    ///
    /// L'ultimo e' stato muto fino alla 0.25.2: le lavorazioni oltre
    /// l'ottantesima finivano in `other_feature`, insieme a quelle di tipo
    /// ignorato, e `truncated` restava false.
    ///
    /// E fino alla 0.30.0 teneva le PIU' VECCHIE, che era il peggiore dei due
    /// modi di troncare: l'albero si scorre in ordine di creazione, quindi la
    /// lavorazione che il piano ha appena fatto — l'unica che chi legge sta
    /// davvero cercando — era sempre fra le scartate. Adesso si tengono le
    /// ultime (voce E12), e il dettaglio si paga solo su quelle.
    ///
    /// UN TETTO CHE NON DICE DI ESSERE SCATTATO NON E' UN TETTO: e' una
    /// risposta sbagliata che si presenta come completa.
    /// </summary>
    public partial class DocumentState
    {
        public const int MaxFaces = 60;

        /// <summary>
        /// TRENTA, NON SESSANTA, E IL NUMERO VIENE DA UNA MISURA (voce K13).
        ///
        /// ── IL TETTO DEI 16 KB ERA GIA' SUPERATO ─────────────────────
        ///
        /// La prova del banco «stato: sotto i 16 KB su un telaio da quattro
        /// barre» passava a **15.922** caratteri contro 16.000: settantotto di
        /// margine. Ma il banco gira con **un solo documento aperto**, perche'
        /// si rifiuta di partire altrimenti (voce K12). Con due documenti —
        /// la situazione normale di chi lavora — lo stesso telaio misura
        /// **16.089**. *Il tetto non aveva un margine sottile: era gia'
        /// superato, e la prova non lo vedeva perche' il banco si pulisce la
        /// scrivania.*
        ///
        /// ── DOVE STANNO I CARATTERI, misurato il 23/9/2026 ──────────
        ///
        ///     8.718  facce      60 voci   (145 l'una)
        ///     5.572  spigoli    60 voci   ( 93 l'uno)
        ///     1.799  tutto il resto
        ///
        /// L'ottantanove per cento sono due elenchi. Le voci sono gia'
        /// asciutte — uno spigolo e' `{"uid":"E1","length_mm":1000,
        /// "point_mm":[...],"along":"X","between":["F36","F1"],"body":"C3"}` —
        /// quindi accorciare i campi non sposta niente: **si puo' solo
        /// cambiare QUANTE**. E' la domanda che il commento della prova
        /// poneva gia': *«servono davvero sessanta spigoli?»*.
        ///
        /// ── PERCHE' GLI SPIGOLI E NON LE FACCE ──────────────────────
        ///
        /// Le facce sono la superficie di aggancio delle operazioni: quasi
        /// tutte prendono una `face_uid`. Gli spigoli li usano in poche —
        /// raccordi, smussi, quote in tavola — e su un saldato a quattro barre
        /// sono rumore: trecento e passa spigoli di cui se ne mandano sessanta
        /// sono un campione su cui non si puo' contare comunque.
        ///
        /// Trenta portano lo stato dello stesso telaio a ~13.300: **il 17 per
        /// cento di margine**, che e' la soglia che la voce K13 si era data.
        ///
        /// E il tetto continua a dichiararsi: `edges_total` dice quanti ce
        /// n'erano e `truncated` diventa vero. *Un tetto che non dice di essere
        /// scattato non e' un tetto.*
        /// </summary>
        public const int MaxEdges = 30;
        public const int MaxFeature = 80;
        public const int MaxBodies = 24;

        /// <summary>
        /// I tipi di feature che il modello puo' davvero citare o su cui puo'
        /// lavorare. Sono i nomi di IFeature.GetTypeName2(), che NON sono
        /// localizzati (il nome nell'albero si', il tipo no).
        /// </summary>
        private static readonly HashSet<string> FeaturesThatCount =
            new HashSet<string>(StringComparer.Ordinal)
        {
            // «ICE» E' UN'ESTRUSIONE, E NON SI CAPISCE DAL NOME.
            //
            // Un'estrusione fatta su una FACCIA — un rialzo, un labbro, una
            // torretta — non si chiama «Extrusion»: `GetTypeName2()` torna
            // «ICE». Misurato il 10 settembre 2026, appena nata
            // `extrude_on_face`: il modello ha fatto un labbro di tenuta
            // e nello stato del passo dopo quel labbro NON C'ERA. Era
            // finito fra le «altre feature», cioe' in un numero.
            //
            // Se ne e' accorto il campo `other_feature_kinds`, aggiunto
            // mezz'ora prima proprio per questo: un conteggio nasconde, un
            // elenco no.
            "Extrusion", "ICE", "Cut", "HoleWzd", "Fillet", "Chamfer",
            "CircularPattern", "LPattern", "MirrorPattern", "Revolution",
            "Sweep", "Loft", "Shell", "Draft", "Rib", "Dome",
            "WeldMemberFeat", "WeldmentTrimExtend", "WeldmentBead",
            "EndCapFeature", "GussetFeature", "CutListFolder",
            "SheetMetal", "SketchBend", "FlatPattern",
        };

        /// <summary>
        /// Le cartelle di servizio che non si contano nemmeno come «altre».
        /// Ci sono in ogni documento e non dicono niente di questo pezzo.
        /// </summary>
        private static readonly HashSet<string> ServiceFolders =
            new HashSet<string>(StringComparer.Ordinal)
        {
            "HistoryFolder", "SensorFolder", "DetailCabinet", "SolidBodyFolder",
            "SurfaceBodyFolder", "CommentFolder", "FavoriteFolder",
            "MaterialFolder", "LightsFolder", "OriginProfileFeature",
            "EnvFolder", "DocsFolder", "SketchFolder", "BlockFolder",
            // Aggiunte il 10 settembre 2026 dopo averle VISTE: erano fra i
            // tipi che lo stato non sapeva leggere, e gonfiavano il
            // conteggio delle «altre» facendo credere che nel pezzo ci
            // fosse dell'altro da guardare.
            //
            // «CommentsFolder» al PLURALE: la riga sopra dice
            // «CommentFolder» al singolare, e non ha mai combaciato con
            // niente. Un elenco di nomi scritto a memoria sbaglia cosi': non
            // con un nome assente, con un nome quasi giusto.
            "CommentsFolder", "EqnFolder", "InkMarkupFolder", "SelectionSetFolder",
        };

        /// <summary>uid -> firma, per ritrovare la stessa entita' al momento giusto.</summary>
        public Dictionary<string, EntitySignature> Signatures { get; } =
            new Dictionary<string, EntitySignature>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, object> Data { get; private set; } =
            new Dictionary<string, object>();

        /// <summary>
        /// La scatola di tutto il pezzo, in mm: {minX, minY, minZ, maxX, maxY,
        /// maxZ}. Null quando il documento non ha corpi.
        ///
        /// E' una PROPRIETA' NUOVA e non un parametro in piu' da qualche parte:
        /// il banco chiama questa classe per riflessione, e cambiare l'insieme
        /// delle firme di un metodo rompe chi lo chiama anche quando il
        /// compilatore tace.
        /// </summary>
        public double[] BoxPartMm { get; private set; }

        /// <summary>
        /// Il documento da cui questo stato e' stato letto.
        ///
        /// Serve a risolvere i riferimenti persistenti (voce E6):
        /// `GetObjectByPersistReference3` sta su `IModelDocExtension`, e chi
        /// ritrova una faccia ha in mano lo stato, non il documento.
        ///
        /// E' una PROPRIETA' e non un parametro in piu' su `Read`: il banco
        /// chiama questa classe per riflessione, e cambiare l'insieme delle
        /// firme di un metodo rompe chi lo chiama anche quando il compilatore
        /// tace. Una proprieta' nuova non la vede nessuno. E' la lezione di
        /// `ReadWithAnchor`, applicata prima di sbatterci di nuovo.
        ///
        /// Vale per QUESTA sessione: non si serializza e non si porta in giro.
        /// </summary>
        public IModelDoc2 Model { get; private set; }

        /// <summary>
        /// Una lavorazione in attesa di sapere se entra nell'elenco.
        ///
        /// Porta solo cio' che il giro sull'albero ha gia' letto — il tipo e il
        /// nome, che si leggono comunque per capire di che si tratta — piu' il
        /// riferimento COM. Il dettaglio, che costa, si paga dopo e solo sulle
        /// sopravvissute.
        /// </summary>
        private sealed class Candidate
        {
            public IFeature F;
            public string Type;
            public string Name;
        }

        public class EntitySignature
        {
            public string Type;          // "FACE" | "EDGE"
            public double Measure;        // area in mm2, oppure lunghezza in mm
            public double[] PointMm;     // un punto sull'entita', in mm
            public double[] Normal;     // solo per le facce piane
            public object Entity;        // IFace2 / IEdge, valido in questa sessione

            /// <summary>
            /// DA QUALE LAVORAZIONE VIENE QUESTA FACCIA, per identificativo e
            /// per nome. `-1` quando non si e' potuto leggere.
            ///
            /// PERCHE' L'IDENTIFICATIVO E NON IL NOME. Il nome e' localizzato
            /// («Estrusione1» / «Boss-Extrude1») e si puo' rinominare a mano:
            /// confrontarlo vorrebbe dire che la stessa faccia smette di
            /// essere la stessa perche' qualcuno ha cambiato la lingua di
            /// SOLIDWORKS. `IFeature.GetID()` non cambia.
            ///
            /// Il nome si tiene lo stesso, ma solo PER I MESSAGGI: chi legge un
            /// rifiuto vuole sapere «viene da Taglio-Estrusione1», non «viene
            /// dalla lavorazione 42».
            ///
            /// `IFace2.GetFeature()` da' la GENERATRICE, non l'ultima che ha
            /// toccato la faccia: misurato il 14/9/2026 (voce 0.5), un raccordo
            /// rimpicciolisce tre facce e tutte e tre continuano a dire
            /// «extrusion».
            /// </summary>
            public int FeatureId = -1;
            public string Feature;

            /// <summary>
            /// LA SCATOLA DEL PEZZO quando questa firma e' stata misurata, in
            /// mm: {minX, minY, minZ, maxX, maxY, maxZ}. Null se non si e'
            /// potuta leggere.
            ///
            /// NON E' UN CRITERIO E NON PUO' DIVENTARLO. Serve a una sola
            /// cosa: scrivere in un messaggio dove stava la faccia rispetto al
            /// pezzo, quando il pezzo e' cambiato. La ragione per cui non puo'
            /// entrare nell'identita' sta per esteso in
            /// <see cref="Sentences.WhereItWasAndWhereItIs"/>, che e' l'unico posto in
            /// tutta la libreria dove questi numeri si dividono fra loro — e
            /// torna una FRASE, non un numero.
            /// </summary>
            public double[] BoxPartMm;
        }

        /// <summary>
        /// CHE COSA E' SELEZIONATO, con gli identificativi dello stato.
        ///
        /// I DUE CASI SCOMODI SI DECIDONO QUI, non si scoprono dopo:
        ///
        ///   * **niente selezionato** — si torna una lista vuota, e chi chiama
        ///     non scrive il campo. Non e' un errore: la maggior parte delle
        ///     volte non c'e' niente selezionato;
        ///   * **piu' cose insieme** — si tornano tutte, nell'ordine in cui
        ///     SOLIDWORKS le da'. Sceglierne una sarebbe indovinare, e
        ///     rifiutare sarebbe inutile: chi ha selezionato quattro facce
        ///     probabilmente voleva selezionarne quattro.
        ///
        /// E un terzo caso, che il criterio della voce non nominava: un'entita'
        /// selezionata che nello stato **non ha un identificativo** — perche' e'
        /// oltre il tetto delle sessanta facce, o perche' e' un vertice, che lo
        /// stato non descrive affatto. Li' si dice **che cosa e'** senza dargli
        /// un nome: «una faccia che non ho descritto» e' un'informazione, «non
        /// c'e' niente» sarebbe una bugia.
        ///
        /// Il tipo si legge dall'OGGETTO e non dall'enum della selezione: un
        /// `as IFace2` non si puo' sbagliare, un numero di enum letto male si'.
        /// </summary>
        private static List<Dictionary<string, object>> TheSelection(
            IModelDoc2 model, Dictionary<string, EntitySignature> signatures)
        {
            var outside = new List<Dictionary<string, object>>();
            try
            {
                var selection = model?.SelectionManager as ISelectionMgr;
                if (selection == null) return outside;
                int howMany = selection.GetSelectedObjectCount2(-1);
                for (int i = 1; i <= howMany && i <= 20; i++)
                {
                    object obj = null;
                    try { obj = selection.GetSelectedObject6(i, -1); } catch { }
                    if (obj == null) continue;

                    string what =
                        obj is IFace2 ? "faccia" :
                        obj is IEdge ? "spigolo" :
                        obj is IBody2 ? "corpo" :
                        obj is IVertex ? "vertice" :
                        obj is IFeature ? "lavorazione" : null;

                    var entry = new Dictionary<string, object>();
                    if (what != null) entry["kind"] = what;

                    // Il nome, se lo stato gliene ha dato uno.
                    foreach (var f in signatures)
                    {
                        if (f.Value.Entity == null) continue;
                        if (!ReferenceEquals(f.Value.Entity, obj) &&
                            !Equals(f.Value.Entity, obj)) continue;
                        entry["uid"] = f.Key;
                        break;
                    }

                    if (!entry.ContainsKey("uid"))
                    {
                        // Una lavorazione si chiama col suo nome, che nello
                        // stato E' l'uid.
                        var featureItem = obj as IFeature;
                        if (featureItem != null)
                        {
                            try { entry["uid"] = featureItem.Name; } catch { }
                        }
                    }
                    if (!entry.ContainsKey("uid"))
                        entry["note"] =
                            "selezionata, ma non e' fra quelle che ho descritto: " +
                            "non posso nominarla.";

                    if (entry.Count > 0) outside.Add(entry);
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// L'ANCORA DI UNA FACCIA: il baricentro d'area, in mm.
        ///
        /// PERCHE' NON BASTA `point_mm`. Quel punto fa un mestiere diverso e lo
        /// fa bene: e' il punto GARANTITO sulla faccia da cui si seleziona, e
        /// per questo si prende con `GetClosestPointOn(0,0,0)` invece del
        /// centro del riquadro — il centro di una faccia bucata cade nel vuoto.
        ///
        /// Come IMPRONTA DIGITALE pero' e' cattivo, perche' per quasi tutte le
        /// facce che non guardano l'origine e' un punto di **frontiera**: si
        /// sposta quando si muove il BORDO, non la faccia.
        ///
        /// Misurato il 14/9/2026: piastra 100x60x20, faccia in alto con ancora
        /// `[0,20,0]`. Una tacca di 15x15 in un angolo — il **3,75%** dell'area
        /// — sposta quel punto a `[15,20,0]`, cioe' di **15 mm**. Il baricentro
        /// d'area, nello stesso caso, si sposta di circa 1,9 mm: proporzionale a
        /// quanto la faccia e' davvero cambiata, invece che a dove capitava il
        /// bordo.
        ///
        /// E QUESTO DA SOLO NON TOGLIE NESSUN RIFIUTO. La tolleranza e' 0,05 mm,
        /// e 1,9 mm la supera lo stesso. Serve a rendere possibile una
        /// tolleranza PROPORZIONALE al cambiamento, che e' la voce E8: qui si
        /// prepara il numero, li' si decidera' cosa farne.
        ///
        /// SI USA LA TASSELLATURA, e non e' senza conseguenze: `GetTessTriangles`
        /// dipende dalla qualita' di visualizzazione del documento. Dentro una
        /// sessione e' deterministica — misurato, dieci letture identiche — ma
        /// fra due macchine con impostazioni diverse potrebbe non esserlo, e per
        /// questo l'ancora **si affianca** a `point_mm` invece di sostituirlo.
        /// </summary>
        /// <summary>
        /// Il riferimento persistente che SOLIDWORKS da' a un'entita', in
        /// base64. Null se non si riesce a prenderlo.
        ///
        /// E' UN BLOB OPACO, e va trattato come tale: non si interpreta, non si
        /// confronta a pezzi, non si scrive su disco e non si porta da un
        /// documento all'altro. Vale dentro QUESTO documento e per questa
        /// sessione di piano, che dura minuti.
        ///
        /// `GetPersistReference3` risponde un `byte[]`: misurato il 14/9/2026,
        /// 375-423 byte per faccia su un pezzo di otto facce. Non e' un intero
        /// e non e' un nome — e' una struttura, e l'unico modo onesto di
        /// spedirla in un JSON e' base64.
        ///
        /// PERCHE' ADESSO SI USANO. `DocumentState` dichiarava di non usarli:
        /// «non serve per un piano che dura minuti, e sarebbe una API in piu' da
        /// verificare per un problema che non abbiamo». Era giusto per il
        /// problema di allora. Il problema adesso c'e': una quota a monte sposta
        /// una faccia, l'ancora sul punto si perde, e il piano viene rifiutato
        /// pur essendo giusto. Misurato in 0.1: il persist ID ha ritrovato
        /// **8 facce su 8** in tutte e cinque le modifiche riuscite, comprese le
        /// tre in cui l'ancora sul punto si perde.
        /// </summary>
        private static string PersistIdOf(IModelDoc2 model, object entity)
        {
            if (model == null || entity == null) return null;
            try
            {
                var ext = model.Extension;
                if (ext == null) return null;
                var blob = ext.GetPersistReference3(entity) as byte[];
                if (blob == null || blob.Length == 0) return null;
                return Convert.ToBase64String(blob);
            }
            catch { return null; }
        }

        private static double[] FaceAnchor(IFace2 face)
        {
            try
            {
                // LA TASSELLATURA ARRIVA IN `float`, NON IN `double`.
                // `IGetTessTriangles` nell'interop e' dichiarata `Single`, e
                // un `as double[]` su un array di float torna **null in
                // silenzio**: l'ancora usciva vuota su ogni faccia e non c'era
                // nessun errore da nessuna parte. E' la stessa famiglia
                // dell'`as object[]` su una List<>, gia' costata un controllo
                // spento per mezza giornata.
                var raw = face?.GetTessTriangles(true);
                double[] t = raw as double[];
                if (t == null && raw is float[] f)
                {
                    t = new double[f.Length];
                    for (int k = 0; k < f.Length; k++) t[k] = f[k];
                }
                if (t == null || t.Length < 9) return null;

                // Baricentro pesato sull'AREA dei triangoli, non media dei
                // vertici: una faccia tassellata fitta da una parte e larga
                // dall'altra tirerebbe la media dove i triangoli sono piu'
                // numerosi, che non vuol dire niente.
                double sx = 0, sy = 0, sz = 0, weight = 0;
                for (int i = 0; i + 8 < t.Length; i += 9)
                {
                    double ax = t[i], ay = t[i + 1], az = t[i + 2];
                    double bx = t[i + 3], by = t[i + 4], bz = t[i + 5];
                    double cx = t[i + 6], cy = t[i + 7], cz = t[i + 8];

                    double ux = bx - ax, uy = by - ay, uz = bz - az;
                    double vx = cx - ax, vy = cy - ay, vz = cz - az;
                    double nx = uy * vz - uz * vy;
                    double ny = uz * vx - ux * vz;
                    double nz = ux * vy - uy * vx;
                    double area = Math.Sqrt(nx * nx + ny * ny + nz * nz) / 2.0;
                    if (area <= 0) continue;

                    sx += area * (ax + bx + cx) / 3.0;
                    sy += area * (ay + by + cy) / 3.0;
                    sz += area * (az + bz + cz) / 3.0;
                    weight += area;
                }
                if (weight <= 0) return null;
                return new[]
                {
                    Math.Round(Units.Mm(sx / weight), 3),
                    Math.Round(Units.Mm(sy / weight), 3),
                    Math.Round(Units.Mm(sz / weight), 3),
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// La lavorazione che ha generato una faccia: identificativo e nome.
        ///
        /// Non solleva e non si lamenta: una faccia senza lavorazione
        /// leggibile esce con `-1` e `null`, e chi la usa deve saper vivere
        /// senza. Meglio nessun dato che un dato inventato — un confronto fra
        /// due `-1` direbbe «stessa lavorazione» di due facce qualunque.
        /// </summary>
        private static void FeatureOfFace(
            IFace2 face, out int id, out string itemName)
        {
            id = -1;
            itemName = null;
            try
            {
                var f = face?.GetFeature() as IFeature;
                if (f == null) return;
                itemName = f.Name;
                id = f.GetID();
            }
            catch { id = -1; }
        }

        /// <summary>
        /// Legge il documento. Va chiamata SUL THREAD DI SOLIDWORKS.
        /// </summary>
        /// <param name="sw">
        /// L'applicazione, se chi chiama ce l'ha. Serve a una cosa sola e
        /// importante: dire QUALI DOCUMENTI SONO APERTI. Da un `IModelDoc2`
        /// non si risale all'applicazione — cercato per riflessione l'11
        /// settembre 2026: ne' `IModelDoc2` ne' `IModelDocExtension` hanno
        /// un accesso all'applicazione — quindi o lo passa chi chiama o non
        /// si sa. Restava facoltativo per non rompere chi chiamava prima.
        /// </param>
        /// <param name="withAnchor">
        /// Scrivere l'ancora di ogni faccia dentro lo stato.
        ///
        /// SPENTA DI SERIE, e non per prudenza generica. L'ancora e' il
        /// baricentro d'area e serve a RICONOSCERE una faccia fra due letture;
        /// non serve a chi legge lo stato per decidere cosa fare, e offrirgliela
        /// accanto a `point_mm` sarebbe dargli due punti quando ne deve usare
        /// uno — e la scelta sbagliata non darebbe errore, darebbe un foro
        /// fuori dal materiale.
        ///
        /// E costa: su un telaio da quattro barre sono **1.400 caratteri**, che
        /// portano lo stato da 15.7 KB a 17.1 — oltre il tetto dei 16 KB che il
        /// banco sorveglia.
        ///
        /// La accende chi costruisce le FIRME dei passi, e la toglie prima di
        /// mandare lo stato a qualcuno.
        /// </param>
        /// <remarks>
        /// QUESTA FIRMA NON SI TOCCA, E NON E' PRUDENZA GENERICA.
        ///
        /// `Read` la chiama anche il banco, **per riflessione**, e li' due
        /// cose che per un compilatore sono innocue non lo sono:
        ///
        ///   * aggiungere un parametro con valore predefinito → `Invoke` con
        ///     due argomenti su un metodo che ne vuole tre risponde «Mancata
        ///     corrispondenza nel numero dei parametri». Misurato il
        ///     14/9/2026: il banco da 88 prove e 0 fallite a **70 e 58**;
        ///   * aggiungere un OVERLOAD → `GetMethod("Read")` ne trova due e
        ///     risponde «Trovata corrispondenza ambigua». Misurato mezz'ora
        ///     dopo, riparando il primo errore con il secondo.
        ///
        /// **Qualunque cambiamento all'insieme delle firme di un metodo rompe
        /// chi lo chiama per riflessione**, anche quello che il compilatore
        /// accetta senza dire niente. Cio' che serve in piu' si mette in un
        /// metodo con un NOME diverso — vedi <see cref="ReadWithAnchor"/>.
        /// </remarks>
        public static DocumentState Read(IModelDoc2 model, ISldWorks sw = null)
        {
            return ReadWithAnchor(model, sw, false);
        }

        /// <summary>
        /// Come <see cref="Read"/>, dicendo se si vuole anche l'ancora di
        /// ogni faccia.
        ///
        /// Nome diverso e non un overload: vedi la nota su `Read`.
        /// </summary>
        public static DocumentState ReadWithAnchor(
            IModelDoc2 model, ISldWorks sw, bool withAnchor)
        {
            var docState = new DocumentState();
            if (model == null) return docState;
            docState.Model = model;

            int docType = 0;
            try { docType = model.GetType(); } catch { }

            var data = new Dictionary<string, object>
            {
                ["path"] = model.GetPathName() ?? "",
                ["document_title"] = model.GetTitle() ?? "",
                ["kind"] = SwOperations.DocumentTypeName(docType),
            };

            // CHI ALTRO E' APERTO.
            //
            // Fino alla 0.39.0 lo stato descriveva UN documento e dava per
            // sottinteso che fosse l'unico. Non lo e' piu': una tavola vive
            // accanto al suo modello, un assieme accanto ai suoi pezzi, e
            // `CreateDrawViewFromModelView3` come `AddComponent5` vogliono
            // il PERCORSO DI UN DOCUMENTO APERTO. Senza questo elenco il
            // modello non ha modo di nominarlo, e una vista agganciata a un
            // documento chiuso esce vuota senza dare errore.
            var others = OpenDocuments(sw, model);
            if (others != null && others.Count > 0) data["documents_open"] = others;

            if (docType == (int)swDocumentTypes_e.swDocDRAWING)
            {
                DescribeDrawing(model as IDrawingDoc, data, sw);
                docState.Data = data;
                return docState;
            }

            // L'ASSIEME SI DESCRIVE, NON SI RIFIUTA (voce GA1).
            //
            // Fino alla 0.91.0 qui c'era una riga sola — «documento di assieme:
            // l'assistente non lo modifica ancora» — e si tornava. Era vera e
            // rendeva il modello cieco: lo stato e' l'unica cosa che vede, e da
            // quella riga non poteva sapere quanti componenti ci fossero, come
            // si chiamassero, quali fossero soppressi. Non poteva nemmeno
            // rifiutare con cognizione.
            //
            // Adesso si descrive (`AssemblyState.cs`), e cosa NON si sa ancora
            // fare si dice li' dentro, per esteso. Il ramo sta PRIMA del cast a
            // `IPartDoc` perche' un assieme non e' una parte e cadrebbe nel
            // `parte == null` qui sotto.
            if (docType == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                DescribeAssembly(model as IAssemblyDoc, data);
                docState.Data = data;
                return docState;
            }

            var partDoc = model as IPartDoc;
            if (partDoc == null)
            {
                // Non e' ne' una parte, ne' una tavola, ne' un assieme: qui non
                // si indovina. Dire «di assieme» come faceva la versione
                // precedente sarebbe una diagnosi sbagliata su un documento che
                // nessuno ha guardato.
                data["solid_body"] = false;
                data["note"] =
                    "documento di tipo «" + SwOperations.DocumentTypeName(docType) +
                    "»: non lo so leggere.";
                docState.Data = data;
                return docState;
            }

            // ── feature dell'albero, e assi ────────────────────────────────
            //
            // FILTRO A INCLUSIONE, NON A ESCLUSIONE.
            //
            // Fino alla 0.29.1 qui si escludevano tre voci e tutto il resto
            // entrava: HistoryFolder, SensorFolder, DetailCabinet,
            // SolidBodyFolder, CommentFolder, FavoriteFolder,
            // OriginProfileFeature. Sono cartelle di servizio che non si
            // possono ne' citare ne' lavorare, e occupavano ~600 caratteri e
            // SETTE posti del tetto in ogni messaggio, scacciando feature vere.
            // Adesso si dichiara cio' che si sa leggere; il resto si conta e
            // basta, cosi' il modello sa che c'e' dell'altro senza pagarlo.
            var feature = new List<Dictionary<string, object>>();
            var axes = new List<Dictionary<string, object>>();
            int nPlanes = 0;
            int otherFeatures = 0;
            // DUE CONTATORI, NON UNO. `otherFeatures` conta le lavorazioni di un
            // TIPO che non entra nell'elenco — invisibili per scelta, e i tipi
            // si dicono in `other_feature_kinds`. `unlistedFeatures` conta
            // quelle che sarebbero entrate e non ci stanno per il tetto: sono
            // due cose diverse, e sommarle nascondeva la seconda.
            int unlistedFeatures = 0;
            // Le lavorazioni che POTREBBERO entrare nell'elenco, raccolte tutte
            // e scelte dopo. Vedi il commento su `candidate.Add` piu' sotto.
            var candidate = new List<Candidate>();
            // QUALI, non solo QUANTE.
            //
            // «other_feature: 7» dice che c'e' dell'altro e non dice cosa,
            // e quel numero e' un posto dove le cose spariscono. Misurato
            // il 10 settembre 2026: appena nata `extrude_on_face`, la
            // lavorazione che aveva appena creato NON compariva nello
            // stato — era finita fra le sette «altre», perche' il suo tipo
            // non e' quello che credevo. Il modello aveva appena fatto una
            // cosa e non la vedeva piu'.
            //
            // I nomi dei tipi sono corti e pochi: elencarli costa niente e
            // rende visibile un punto cieco invece di nasconderlo dietro un
            // numero.
            var unknownTypes = new SortedSet<string>(StringComparer.Ordinal);
            // I nomi delle lavorazioni citate dalle facce, una volta sola.
            var featureNames = new SortedDictionary<int, string>();
            int featuresInError = 0;
            IFeature f = partDoc.IFirstFeature();
            while (f != null)
            {
                string typ = "";
                string itemName = "";
                try { typ = f.GetTypeName2(); itemName = f.Name; } catch { }

                if (typ == "RefPlane") nPlanes++;
                // SOLO GLI ASSI VERI.
                //
                // Qui dentro c'era anche "OriginProfileFeature", cioe' l'origine
                // del pezzo — che c'e' sempre. Il risultato: la precondizione
                // «esiste un asse» risultava vera in QUALUNQUE documento, il
                // modello si vedeva offrire la ripetizione circolare, e
                // all'esecuzione SelectByID2(nome, "AXIS", ...) falliva perche'
                // l'origine non e' un asse. Uno strumento offerto sempre e
                // utilizzabile quasi mai: il modello ci prova, il piano cade al
                // primo passo, e sembra colpa sua.
                else if (typ == "RefAxis")
                {
                    // «uid» e «name» erano LA STESSA STRINGA, scritta due
                    // volte. Su ottanta lavorazioni con nomi da venti caratteri
                    // erano oltre due chilobyte di messaggio che non dicevano
                    // niente — e il posto che tolgono e' esattamente quello che
                    // serve ai parametri qui sotto. Resta «uid», che e' il nome
                    // con cui tutto il resto del sistema chiama le entita'.
                    var axisEntry = new Dictionary<string, object> { ["uid"] = itemName, ["kind"] = typ };

                    // LA FIRMA DELL'ASSE (voce E4, la parte che mancava). Fino
                    // alla 0.113.0 lo stato di un asse era {uid, tipo} e basta:
                    // niente da firmare, e `axis_uid` nelle ripetizioni era
                    // controllato per ESISTENZA e non per IDENTITA'. Un asse
                    // rifatto altrove col nome di prima passava. Adesso: un punto
                    // sulla retta, la direzione, l'identificativo della lavorazione.
                    try
                    {
                        var par = (f.GetSpecificFeature2() as IRefAxis)?.GetRefAxisParams() as double[];
                        if (par != null && par.Length >= 6)
                        {
                            double dx = par[3] - par[0], dy = par[4] - par[1], dz = par[5] - par[2];
                            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (len > 1e-12)
                            {
                                var point = new[] { Math.Round(Units.Mm(par[0]), 3), Math.Round(Units.Mm(par[1]), 3), Math.Round(Units.Mm(par[2]), 3) };
                                var direction = new[] { Math.Round(dx / len, 5), Math.Round(dy / len, 5), Math.Round(dz / len, 5) };
                                axisEntry["point_mm"] = point;
                                axisEntry["direction"] = direction;
                                docState.Signatures[itemName] = new EntitySignature
                                {
                                    Type = "AXIS", PointMm = point, Normal = direction, Entity = f,
                                    Feature = itemName,
                                };
                            }
                        }
                    }
                    catch { }
                    try { axisEntry["feature_id"] = f.GetID(); } catch { }
                    axes.Add(axisEntry);
                }
                else if (FeaturesThatCount.Contains(typ))
                {
                    // SI RACCOLGONO TUTTE, E SI SCEGLIE DOPO (voce E12).
                    //
                    // Fino alla 0.30.0 qui c'era `if (feature.Count <
                    // MaxFeature)`, cioe' si tenevano le prime ottanta
                    // INCONTRATE. E l'albero si scorre con `GetNextFeature`,
                    // in ordine di CREAZIONE: le prime ottanta sono le piu'
                    // VECCHIE, e la lavorazione che il piano ha appena creato
                    // e' fra quelle scartate. Il modello non la trovava, e
                    // poteva concluderne che l'operazione non aveva fatto
                    // niente — e rifarla.
                    //
                    // IL COSTO NON CAMBIA, ed e' il motivo per cui si puo'
                    // fare. `GetTypeName2()` e `Name` si leggono gia' per OGNI
                    // lavorazione dell'albero, prima di questo controllo, per
                    // decidere di che tipo e': il giro leggero esisteva gia' ed
                    // era gia' pagato. Qui si aggiunge solo un riferimento a
                    // una lista. La parte cara — `GetID`, `IsSuppressed`,
                    // `GetErrorCode2`, i parametri, le quote — resta su ottanta
                    // lavorazioni esatte, solo che adesso sono le ULTIME.
                    candidate.Add(new Candidate { F = f, Type = typ, Name = itemName });
                }
                else if (!ServiceFolders.Contains(typ))
                {
                    otherFeatures++;
                    if (!string.IsNullOrEmpty(typ)) unknownTypes.Add(typ);
                }
                try { f = (IFeature)f.GetNextFeature(); } catch { f = null; }
            }

            // ── QUALI OTTANTA (voce E12) ──────────────────────────────────
            //
            // L'albero si scorre in ordine di CREAZIONE, quindi le prime sono
            // le piu' vecchie. Tenere le prime ottanta vuol dire buttare via
            // proprio la lavorazione che il piano ha appena creato — quella che
            // chi legge sta cercando. Si tengono le ULTIME.
            //
            // Il dettaglio si paga QUI, e solo su queste: e' la stessa spesa di
            // prima, su lavorazioni diverse.
            //
            // IL TETTO SI DICHIARA, come gli altri tre. Un elenco incompleto
            // dichiarato e' una cosa diversa da un elenco incompleto spacciato
            // per intero — ed e' quello che questo stato faceva fino alla
            // 0.25.2, con `truncated: false` su un elenco troncato.
            if (candidate.Count > MaxFeature)
            {
                unlistedFeatures = candidate.Count - MaxFeature;
                candidate.RemoveRange(0, unlistedFeatures);
            }
            foreach (var c in candidate)
            {
                var entryF = FeatureDetail(c.F, c.Type, c.Name, ref featuresInError);
                if (entryF != null) feature.Add(entryF);
            }

            // ── corpi, facce, spigoli ─────────────────────────────────────
            //
            // I CORPI SONO LA NOVITA' DELLA 0.30.0, E RISPONDONO A UNA DOMANDA
            // CHE PRIMA NON AVEVA RISPOSTA.
            //
            // Fino alla 0.29.1 di cio' che c'era nel documento si diceva una
            // cosa sola: `solid_body`, si' o no. A chi deve lavorare su un
            // telaio gia' disegnato — «tappa le estremita' di quelle barre»,
            // «fai i fori sul montante» — un booleano non serve a niente, e il
            // committente lo ha chiesto per nome: «mancano dei tools per
            // sapere quello che e' gia' stato disegnato?».
            //
            // BVisibleOnly: false, e il false e' voluto. Prima era true: un
            // corpo nascosto per guardare dentro il pezzo spariva dallo stato
            // senza che niente lo dicesse, e le lavorazioni su di lui
            // diventavano impossibili senza una ragione leggibile.
            var faces = new List<Dictionary<string, object>>();
            var edges = new List<Dictionary<string, object>>();
            var describedBodies = new List<Dictionary<string, object>>();
            bool hasBody = false;
            bool truncated = false;

            // LA SCATOLA DEL PEZZO, non solo quanto e' grande: DOVE STA.
            // Serve fuori dal blocco dei corpi, quindi si dichiara qui. Vedi
            // il commento su `bounding_box_from_mm` piu' sotto.
            double[] partBox = null;

            object bodiesObj = null;
            try { bodiesObj = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false); } catch { }
            var bodies = (bodiesObj as object[] ?? new object[0])
                .Select(o => o as IBody2).Where(b => b != null).ToList();

            // Il materiale: null ESPLICITO quando non c'e'.
            //
            // Omettere il campo e scriverlo null non sono la stessa cosa. Un
            // campo che manca il modello non lo vede; un `material: null` gli
            // dice che quel posto esiste ed e' vuoto — ed e' l'unico modo
            // perche' si accorga di doverlo riempire. Fino alla 0.29.1 la
            // cartella dei materiali era anzi esclusa apposta dall'albero.
            string material = null;
            try
            {
                string archive;
                // "" IS THE ACTIVE CONFIGURATION (finding L17-1, NOT REAL). The
                // Z-C21.3 probe of L17-1 and M10 (28/9/2026) read "" against each
                // configuration name, activating each in turn: "" followed the
                // active configuration four times out of four. So the material field is
                // the material of the active configuration only.
                var read = partDoc.GetMaterialPropertyName2("", out archive);
                if (!string.IsNullOrWhiteSpace(read)) material = read;
            }
            catch { }
            data["material"] = material;

            // LA CONFIGURAZIONE ATTIVA, E LE ALTRE.
            //
            // Un pezzo con piu' configurazioni e' lo stesso file che descrive
            // piu' pezzi: la staffa sinistra e la destra, con e senza foro. Le
            // facce, i corpi, il volume e la massa scritti qui sopra sono
            // quelli della configurazione ATTIVA, e di nessun'altra.
            //
            // Senza questo campo lo stato dice «volume 84200 mm3» e chi legge
            // non ha modo di sapere DI QUALE variante: e' esattamente il
            // difetto che non da' errore. Si scrive sempre, anche quando la
            // configurazione e' una sola, perche' un campo che compare solo a
            // volte insegna a non cercarlo.
            //
            // Il nome ha spesso qualcosa fra parentesi angolari
            // («Default<A macchina>»), e QUELLO E' IL NOME: non e' un'etichetta
            // che il nome si porta appresso. Un pezzo nato dal modello di
            // questa azienda ha due configurazioni vere, «Default<A macchina>»
            // e «Default<Saldato>» — la coppia dei saldati. Si riporta com'e',
            // perche' e' quello che `activate_configuration` vuole indietro.
            try
            {
                var configHandler = model.ConfigurationManager as IConfigurationManager;
                if (configHandler != null) data["configuration"] = configHandler.ActiveConfiguration.Name;
            }
            catch { }
            try
            {
                var configNames = model.GetConfigurationNames() as string[];
                if (configNames != null && configNames.Length > 0) data["configurations"] = configNames;
            }
            catch { }
            // QUANTE NE DICHIARA LUI, che non e' per forza quante sono le
            // righe di «configurations»: i nomi li da' `GetConfigurationNames`,
            // il conto lo da' `GetConfigurationCount`, e sono due chiamate
            // diverse. Si scrivono tutti e due, perche' il giorno in cui non
            // combaciano quello e' il fatto da sapere -- non un dettaglio da
            // nascondere scegliendo quale dei due mostrare.
            try { data["how_many_configurations"] = model.GetConfigurationCount(); }
            catch { }

            // LE PROPRIETA' PERSONALIZZATE, NEI DUE CASSETTI SEPARATI.
            //
            // Sono cio' che finisce nel cartiglio e nella scheda del PDM.
            // Stanno in due campi e non in uno perche' sono in due posti
            // diversi: quelle del documento e quelle della configurazione
            // attiva non si ereditano e non si vedono a vicenda. Un campo
            // solo, «proprieta», sarebbe una fusione che nel file non esiste,
            // e chi rileggesse non avrebbe modo di sapere dove cercare.
            var document = ReadProperty(model, "");
            if (document.Count > 0) data["property"] = document;
            try
            {
                object activate;
                if (data.TryGetValue("configuration", out activate))
                {
                    var config = ReadProperty(model, Convert.ToString(activate) ?? "");
                    if (config.Count > 0) data["configuration_properties"] = config;
                }
            }
            catch { }

            if (bodies.Count > 0)
            {
                hasBody = true;

                // ── i corpi, uno per uno ──────────────────────────────────
                for (int i = 0; i < bodies.Count && i < MaxBodies; i++)
                {
                    var body = bodies[i];
                    var entry = new Dictionary<string, object> { ["uid"] = "C" + (i + 1) };
                    try { entry["name"] = body.Name; } catch { }
                    var box = BoundingBox(body);
                    if (box != null)
                    {
                        entry["bounding_box_mm"] = new[]
                        {
                            Math.Round(box[3] - box[0], 2),
                            Math.Round(box[4] - box[1], 2),
                            Math.Round(box[5] - box[2], 2),
                        };
                        entry["center_mm"] = new[]
                        {
                            Math.Round((box[0] + box[3]) / 2, 2),
                            Math.Round((box[1] + box[4]) / 2, 2),
                            Math.Round((box[2] + box[5]) / 2, 2),
                        };
                    }
                    // Il ponte fra un corpo e la lavorazione che lo ha fatto:
                    // e' cio' che permette di dire «le barre» invece di «C3».
                    //
                    // E QUI NON SI TOGLIE, ANCHE SE SEMBRA RIDONDANTE.
                    //
                    // SolidWorks battezza i corpi di un saldato con il nome
                    // della lavorazione piu' un suffisso:
                    //   nome     = TUBO STRUTTURALE QUADRO 80X80X5(1)[4]
                    //   created_by = TUBO STRUTTURALE QUADRO 80X80X5(1)
                    // Il 10 settembre 2026 questa somiglianza mi ha fatto
                    // togliere il campo quando il nome comincia per la
                    // lavorazione. Sbagliato, per due ragioni che il banco ha
                    // detto subito:
                    //
                    // 1. il risparmio era 192 caratteri su un telaio, non 840:
                    //    le barre sono QUATTRO, non ventiquattro (avevo usato
                    //    il tetto `MaxBodies` al posto del numero vero);
                    // 2. e soprattutto: `name` e `created_by` condividono la
                    //    stringa ma NON sono la stessa cosa. Uno e' come si
                    //    chiama il corpo, l'altro e' da dove viene. Che oggi
                    //    l'uno si possa ricavare dall'altro e' una coincidenza
                    //    di come SolidWorks battezza i corpi saldati, non una
                    //    proprieta' su cui appoggiarsi — e ricavarlo sarebbe
                    //    comunque una DEDUZIONE, che e' esattamente cio' che
                    //    stiamo togliendo di mezzo al modello.
                    //
                    // La regola «si scrive solo cio' che aggiunge» resta buona
                    // per i campi identici (`uid`/`name`) e per quelli che
                    // ripetono un altro campo (`from_feature` su un saldato).
                    // Non vale per due campi che dicono cose diverse.
                    string feat = BodyFeature(body);
                    if (feat != null) entry["created_by"] = feat;
                    describedBodies.Add(entry);

                    // E il corpo si deve poter RITROVARE. Senza questa firma,
                    // «C1» nello stato sarebbe un'etichetta che il modello
                    // legge e nessuno sa riportare a un IBody2 al momento di
                    // eseguire: e' il ponte che manca a «tappa le estremita'
                    // di questo telaio» in un messaggio nuovo.
                    //
                    // La firma di un corpo e' il suo centro piu' l'ingombro:
                    // sono i due numeri che cambiano se qualcuno modifica il
                    // pezzo fra la proposta e l'esecuzione.
                    docState.Signatures["C" + (i + 1)] = new EntitySignature
                    {
                        Type = "BODY",
                        // ZERO-E-ZERO: chi legge `Measure` guarda sempre `> 0`, e
                        // nessuno la legge per i corpi; zero qui e' «non misurata».
                        Measure = box == null ? 0 : Math.Round(
                            (box[3] - box[0]) * (box[4] - box[1]) *
                            (box[5] - box[2]), 2),
                        PointMm = entry.ContainsKey("center_mm")
                            ? entry["center_mm"] as double[]
                            : null,
                        Entity = body,
                    };
                }
                if (bodies.Count > MaxBodies) data["bodies_not_listed"] = bodies.Count - MaxBodies;
                data["bodies_total"] = bodies.Count;

                // ── l'ingombro di tutto il pezzo ──────────────────────────
                double[] everything = null;
                foreach (var body in bodies)
                {
                    var b = BoundingBox(body);
                    if (b == null) continue;
                    if (everything == null) { everything = (double[])b.Clone(); continue; }
                    for (int k = 0; k < 3; k++) everything[k] = Math.Min(everything[k], b[k]);
                    for (int k = 3; k < 6; k++) everything[k] = Math.Max(everything[k], b[k]);
                }
                if (everything != null)
                {
                    partBox = everything;
                    data["bounding_box_mm"] = new[]
                    {
                        Math.Round(everything[3] - everything[0], 2),
                        Math.Round(everything[4] - everything[1], 2),
                        Math.Round(everything[5] - everything[2], 2),
                    };

                    // ── DOVE STA LA SCATOLA, e non solo quanto e' grande ──
                    //
                    // `bounding_box_mm` dice «140 x 16 x 140» e non dice dove sia
                    // quel volume: un piano che vuole mettere un foro «a 20 mm
                    // dal bordo» deve indovinare se il pezzo e' centrato
                    // sull'origine o appoggiato a essa. Sono tre numeri, una
                    // trentina di caratteri sul tetto di 16.000.
                    //
                    // E' anche l'unico ingresso che rende possibile la
                    // SPIEGAZIONE della voce E10 — dire dove stava una faccia
                    // rispetto al pezzo — che senza l'angolo non si puo'
                    // nemmeno scrivere: 50 su un lato lungo 100 vuol dire meta'
                    // se la scatola parte da zero e tutto se parte da -50.
                    data["bounding_box_from_mm"] = new[]
                    {
                        Math.Round(everything[0], 2),
                        Math.Round(everything[1], 2),
                        Math.Round(everything[2], 2),
                    };
                }

                // ── la massa, SOLO se c'e' un materiale ───────────────────
                //
                // Senza materiale SolidWorks usa comunque una densita'
                // predefinita e restituisce un numero. Un peso plausibile e
                // falso e' il difetto peggiore che ci sia qui: chi legge «12,4
                // kg» non ha modo di sapere che e' inventato. Quindi non si
                // manda affatto.
                if (material != null)
                {
                    double? kg = Mass(model, bodies);
                    if (kg.HasValue) data["mass_kg"] = Math.Round(kg.Value, 3);
                }

                // IL VOLUME: l'unica misura che si muove quando il pezzo cambia
                // DENTRO senza cambiare fuori.
                //
                // Lo stato aveva l'ingombro e la massa. L'ingombro non si
                // accorge di un foro allargato, di un raccordo tolto, di una
                // nervatura soppressa: il pezzo occupa lo stesso spazio. La
                // massa c'e' solo se il materiale e' assegnato, e in officina
                // meta' dei pezzi non ce l'ha finche' non si va in distinta.
                // Il volume c'e' sempre e cambia sempre — ed e' il numero con
                // cui il modello puo' capire se una modifica ha fatto
                // qualcosa. L'add-in lo calcolava gia' per le proprie
                // verifiche e non lo diceva a nessuno.
                //
                // UN CORPO CHE NON RISPONDE FA «NON LO SO», NON UN VOLUME PIU'
                // BASSO (voce Z-C8). Prima si saltava, e la chiave spariva anche
                // quando il volume non si era letto: assente voleva dire zero
                // OPPURE «non letto», e `undo` poteva rifare un passo dopo un
                // annullamento giusto. Adesso: chiave assente = nessun volume
                // solido; chiave con null = non si e' potuto leggere.
                double volume = 0;
                bool unreadableVolume = false;
                foreach (var c in bodies)
                {
                    try
                    {
                        var mp = c.GetMassProperties(1000.0) as double[];
                        if (mp == null || mp.Length <= 4) { unreadableVolume = true; break; }
                        if (mp[3] > 0) volume += mp[3] * 1e9;
                    }
                    catch { unreadableVolume = true; break; }
                }
                if (unreadableVolume) data["volume_mm3"] = null;
                else if (volume > 0) data["volume_mm3"] = Math.Round(volume, 1);

                // ── facce e spigoli, con il tetto sul TOTALE ──────────────
                //
                // Il tetto stava DENTRO il ciclo sui corpi: su un telaio da
                // quattro barre uscivano 240 facce invece di 60, e `truncated`
                // restava false perche' nessun corpo singolo superava il tetto.
                // Il payload cresceva con le barre: ~11 KB per quattro, oltre
                // 50 KB per venti. Adesso si raccoglie tutto, si ordina una
                // volta e si taglia una volta.
                var allFaces = new List<Tuple<IFace2, int>>();
                var allEdges = new List<Tuple<IEdge, int>>();
                // Da quale lavorazione nasce OGNI CORPO. Serve a non ripetere
                // su ogni faccia una cosa che il campo «body» dice gia': vedi
                // il commento su `from_feature` piu' sotto.
                var bodyFeature = new string[bodies.Count];
                for (int i = 0; i < bodies.Count; i++) bodyFeature[i] = BodyFeature(bodies[i]);
                for (int i = 0; i < bodies.Count; i++)
                {
                    var facesObj = bodies[i].GetFaces() as object[];
                    if (facesObj != null)
                        foreach (var o in facesObj)
                        {
                            var x = o as IFace2;
                            if (x != null) allFaces.Add(Tuple.Create(x, i));
                        }
                    var edgesObj = bodies[i].GetEdges() as object[];
                    if (edgesObj != null)
                        foreach (var o in edgesObj)
                        {
                            var x = o as IEdge;
                            if (x != null) allEdges.Add(Tuple.Create(x, i));
                        }
                }

                // Le piu' grandi per prime: sono quelle su cui un progettista
                // lavora, e sono quelle che una descrizione corta deve avere.
                // ZERO-E-ZERO: e' una chiave d'ordinamento, non un'affermazione —
                // una faccia che non da' l'area finisce in fondo, e basta.
                var sorted = allFaces
                    .OrderByDescending(t => { try { return t.Item1.GetArea(); } catch { return 0.0; } })
                    .ToList();
                if (sorted.Count > MaxFaces)
                {
                    truncated = true;
                    data["faces_total"] = sorted.Count;
                }
                // Faccia -> uid, per poter dire su quali facce si appoggia
                // uno spigolo. Si riempie qui perche' le facce si numerano
                // prima degli spigoli, e serve li'.
                var faceUid = new Dictionary<IFace2, string>();
                int faceIndex = 0;
                foreach (var t in sorted.Take(MaxFaces))
                {
                    faceIndex++;
                    var uid = "F" + faceIndex;
                    // L'area che non si legge e' null nello stato, non 0 mm²
                    // (voce Z-C8). ZERO-E-ZERO: nella firma resta 0, perche' chi
                    // legge `Measure` guarda sempre `> 0` e lo prende per «non
                    // misurata».
                    double? readArea = null;
                    try { readArea = Units.Mm2(t.Item1.GetArea()); } catch { }
                    double areaMm2 = readArea ?? 0;
                    var point = PointOnFace(t.Item1);
                    var normal = FaceNormalVector(t.Item1);
                    var entry = new Dictionary<string, object>
                    {
                        ["uid"] = uid,
                        ["area_mm2"] = readArea.HasValue ? (object)Math.Round(readArea.Value, 2) : null,
                        ["point_mm"] = point,
                        ["normal"] = normal,
                        ["planar"] = normal != null,
                    };
                    // UN FORO DEVE VEDERSI COME UN FORO, NON COME «UNA FACCIA
                    // NON PIANA DI AREA 1256».
                    //
                    // Fino al 10 settembre 2026 di una faccia cilindrica lo
                    // stato diceva soltanto `planar: false`. Alla richiesta
                    // «quel foro portalo da M8 a M10» il modello non aveva
                    // modo di sapere quale faccia fosse un M8: il diametro
                    // c'era nella geometria e non glielo dicevamo.
                    //
                    // Il raggio si legge da `CylinderParams`, che torna un
                    // vettore di sette numeri: punto (0-2), asse (3-5), raggio
                    // in metri (6). E' la stessa ricetta gia' in produzione
                    // per riconoscere i fori in `OperazioniFori`.
                    double radiusMm;
                    double[] cylinderAxis;
                    // `= null` e non e' pignoleria: `CylinderOf` lo assegna con
                    // un `out`, ma la chiamata sta a destra di un `&&` e su una
                    // faccia piana non viene eseguita. Senza l'inizializzazione
                    // il compilatore rifiuta l'uso piu' in basso — ed e' un
                    // rifiuto giusto: quella variabile li' non avrebbe un
                    // valore.
                    double[] cylinderCenter = null;
                    if (normal == null &&
                        CylinderOf(t.Item1, out radiusMm, out cylinderAxis, out cylinderCenter))
                    {
                        entry["radius_mm"] = Math.Round(radiusMm, 3);
                        entry["diameter_mm"] = Math.Round(radiusMm * 2, 3);
                        entry["axis"] = cylinderAxis;
                    }
                    // DA QUALE LAVORAZIONE VIENE — MA SOLO SE LO AGGIUNGE.
                    //
                    // Il legame faccia→lavorazione e' quello che fa smettere la
                    // faccia F17 di essere anonima e la fa diventare «la parete
                    // del foro M8».
                    //
                    // SCRITTO SU OGNI FACCIA PERO' COSTA E MENTE. Misurato il
                    // 10 settembre 2026: su un telaio da quattro barre lo stato
                    // passava da 11.703 a 15.849 caratteri, e tutte le facce di
                    // una barra dicevano «Membro strutturale1[1]» — cioe'
                    // esattamente quello che il campo «body» diceva gia'.
                    // Costo pieno, informazione zero.
                    //
                    // La regola e' quindi: si scrive quando la faccia viene da
                    // una lavorazione DIVERSA da quella che ha fatto il corpo.
                    // E' il caso in cui il campo dice qualcosa di nuovo — un
                    // foro, una tasca, un raccordo su un pezzo — ed e'
                    // esattamente il caso che serve per «quel foro portalo a
                    // M10».
                    string fromFeature = FaceFeature(t.Item1);
                    if (fromFeature != null
                        && (t.Item2 >= bodyFeature.Length
                            || !string.Equals(fromFeature, bodyFeature[t.Item2], StringComparison.Ordinal)))
                    {
                        entry["from_feature"] = fromFeature;

                        // ── DOVE STA IL FORO, e non e' `point_mm` ──────────
                        //
                        // `point_mm` sta sulla SUPERFICIE — serve a
                        // SELEZIONARE — e dista un raggio dall'asse in una
                        // direzione che nessuno dichiara. Chi lo usa per
                        // misurare un interasse sbaglia di un raggio per ogni
                        // foro e non se ne accorge: il numero ha la forma
                        // giusta.
                        //
                        // Misurato il 15/9/2026 (voce E16) su una flangia con
                        // sei fori su Ø90: due modelli indipendenti hanno
                        // risposto **Ø79**, tutti e due con confidenza 5 su 5.
                        //
                        // `center_mm` e non `asse_mm`: `axis` qui sopra e' gia'
                        // la DIREZIONE, e due campi quasi omonimi che vogliono
                        // dire cose diverse sono il modo piu' sicuro di farli
                        // scambiare.
                        //
                        // ── E SI SCRIVE SOLO QUI DENTRO, non su ogni
                        //    faccia cilindrica. IL TETTO L'HA DETTO.
                        //
                        // La prima scrittura lo metteva su TUTTE, e il banco
                        // ha fermato il commit: lo stato di un telaio da
                        // quattro barre e' passato da 15.633 a **16.163**
                        // caratteri, oltre il tetto di 16.000, con **un solo
                        // documento aperto** — quindi crescita vera, non
                        // scarti lasciati in giro.
                        //
                        // Le facce cilindriche di un telaio tubolare sono i
                        // raccordi d'angolo dei profili: decine, e fra i loro
                        // assi non misura un interasse nessuno. Quelle che
                        // contano sono le facce che vengono da una lavorazione
                        // DIVERSA da quella che ha fatto il corpo — cioe' un
                        // foro, una tasca, una svasatura: le cose che si
                        // posizionano. E' la stessa regola che decide
                        // `from_feature`, applicata dove serve, ed e' il
                        // motivo per cui questa riga sta dentro quel blocco
                        // invece che accanto al raggio.
                        //
                        // **Cosa questo NON copre, e va detto:** una faccia
                        // cilindrica che E' il corpo — l'interno di un tubo —
                        // non prende il centro. Se un giorno servira'
                        // misurarci qualcosa, e' una voce nuova, non una riga
                        // da aggiungere qui di nascosto.
                        if (cylinderCenter != null) entry["center_mm"] = cylinderCenter;
                    }
                    // DA CHE PARTE GUARDA, DETTO IN ITALIANO.
                    //
                    // La normale c'era gia', ma e' una terna di numeri: per
                    // sapere quale faccia e' «quella in alto» bisogna capire
                    // che in SolidWorks l'alto e' +Y, cercare la normale
                    // [0,1,0] e poi scegliere fra quelle la piu' grande. Il 9
                    // settembre 2026 il modello non ci e' arrivato: alla
                    // richiesta «sulla faccia in alto della piastra fammi due
                    // fori M8» ha calcolato bene le coordinate, ha scelto bene
                    // l'operazione, e si e' fermato scrivendo «mi serve
                    // l'identificativo della faccia in alto».
                    //
                    // Sedici caratteri per faccia tolgono di mezzo una
                    // deduzione. Il verso e' quello del sistema di SolidWorks:
                    // +X destra, +Y alto, +Z davanti.
                    string facing = Facing(normal);
                    if (facing != null) entry["facing"] = facing;
                    // A quale corpo appartiene. Su un saldato senza questo
                    // campo arrivano quaranta facce indistinguibili.
                    if (bodies.Count > 1 && t.Item2 < MaxBodies) entry["body"] = "C" + (t.Item2 + 1);

                    // DA QUALE LAVORAZIONE VIENE. Serve a due cose diverse: a
                    // chi legge lo stato, per capire cos'e' quella faccia; e al
                    // ritrovamento, che senza non puo' distinguere «la stessa
                    // faccia spostata» da «un'altra faccia finita nello stesso
                    // posto».
                    // IL NOME SI SCRIVE UNA VOLTA SOLA, IN FONDO.
                    //
                    // Scriverlo su ogni faccia costa: su un telaio saldato i
                    // nomi delle lavorazioni sono cose come «TUBO STRUTTURALE
                    // QUADRO 80X80X5(1)», e sessanta facce che vengono da
                    // quattro lavorazioni li ripetevano sessanta volte.
                    // Misurato: lo stato di un telaio da quattro barre e'
                    // passato da sotto i 16 KB a **18.629 caratteri**, e c'e'
                    // una prova di banco che quel tetto lo sorveglia.
                    //
                    // Con la mappa in fondo i nomi lunghi sono quattro, e su
                    // ogni faccia resta il numero.
                    int featureId;
                    string featureItem;
                    FeatureOfFace(t.Item1, out featureId, out featureItem);
                    if (featureItem != null)
                    {
                        entry["feature_id"] = featureId;
                        featureNames[featureId] = featureItem;
                    }

                    // LE DUE ANCORE, tutt'e due dietro lo stesso interruttore e
                    // tutt'e due destinate alla FIRMA e non a chi legge.
                    //
                    //   `anchor`     il baricentro d'area: una nostra congettura
                    //                sulla geometria, che si muove quando la
                    //                faccia cambia davvero;
                    //   `persist_id` la risposta che da' SOLIDWORKS alla stessa
                    //                domanda, e l'unica che non sia una nostra
                    //                congettura.
                    //
                    // Il persist ID e' 375-423 byte per faccia, cioe' ~530
                    // caratteri in base64: su sessanta facce sono ~32 KB, il
                    // DOPPIO dell'intero tetto dello stato. Non puo' arrivare a
                    // chi legge, e infatti il ponte lo toglie subito dopo aver
                    // costruito le firme — la stessa strada dell'ancora,
                    // moltiplicata per venti.
                    if (withAnchor)
                    {
                        var anchor = FaceAnchor(t.Item1);
                        if (anchor != null) entry["anchor"] = anchor;

                        var persistId = PersistIdOf(model, t.Item1);
                        if (persistId != null) entry["persist_id"] = persistId;
                    }

                    faces.Add(entry);
                    faceUid[t.Item1] = uid;

                    docState.Signatures[uid] = new EntitySignature
                    {
                        Type = "FACE",
                        Measure = Math.Round(areaMm2, 2),
                        PointMm = point,
                        Normal = normal,
                        Entity = t.Item1,
                        FeatureId = featureId,
                        Feature = featureItem,
                    };
                }

                var sortedItems = allEdges
                    .OrderByDescending(t => LengthMm(t.Item1))
                    .ToList();
                if (sortedItems.Count > MaxEdges)
                {
                    truncated = true;
                    data["edges_total"] = sortedItems.Count;
                }
                int edgeIndex = 0;
                foreach (var t in sortedItems.Take(MaxEdges))
                {
                    edgeIndex++;
                    var uid = "E" + edgeIndex;
                    double len = LengthMm(t.Item1);
                    var point = PointOnEdge(t.Item1);
                    var entry = new Dictionary<string, object>
                    {
                        ["uid"] = uid,
                        // null quando non si e' potuta leggere: -1 e' il «non lo
                        // so» di chi la usa qui dentro, non una lunghezza.
                        ["length_mm"] = len >= 0 ? (object)Math.Round(len, 2) : null,
                        ["point_mm"] = point,
                    };
                    // UNO SPIGOLO SENZA RUOLO NON SI PUO' NOMINARE.
                    //
                    // Le facce dicono da che parte guardano — «up»,
                    // «front» — e il modello le sa scegliere. Gli spigoli
                    // dicevano soltanto quanto sono lunghi. Misurato il 10
                    // settembre 2026 su una piastra 300x200x15: venti
                    // spigoli, tutti uguali a leggersi, e alla richiesta
                    // «raccorda i quattro angoli» il modello non ha nemmeno
                    // provato a nominarli — ha citato un passo inesistente.
                    //
                    // «along: Y» costa quattordici caratteri e dice quello
                    // che direbbe un progettista: gli angoli di una piastra
                    // sono gli spigoli nella direzione dello spessore.
                    string axisLength;
                    double edgeRadius;
                    if (EdgeDirection(t.Item1, out axisLength, out edgeRadius))
                    {
                        if (axisLength != null) entry["along"] = axisLength;
                    }
                    else
                    {
                        // Non dritto: si dice, perche' come DIREZIONE non
                        // vale e la ripetizione lo rifiuterebbe.
                        entry["curved"] = true;
                        if (edgeRadius > 0) entry["radius_mm"] = Math.Round(edgeRadius, 2);
                    }
                    // SU QUALI FACCE SI APPOGGIA: e' cosi' che si dice «il
                    // bordo della faccia in alto» senza contare i millimetri.
                    var fra = EdgeFaces(t.Item1, faceUid);
                    if (fra != null) entry["between"] = fra;
                    if (bodies.Count > 1 && t.Item2 < MaxBodies) entry["body"] = "C" + (t.Item2 + 1);
                    edges.Add(entry);
                    docState.Signatures[uid] = new EntitySignature
                    {
                        Type = "EDGE",
                        Measure = Math.Round(len, 2),
                        PointMm = point,
                        Entity = t.Item1,
                    };
                }
            }

            data["solid_body"] = hasBody;
            data["planes_available"] = nPlanes;

            // L'ALBERO E' ROSSO? SI DICE IN TESTA, NON SOLO DENTRO.
            //
            // Le lavorazioni in errore sono gia' segnate una per una, ma chi
            // legge lo stato scorre l'elenco delle feature solo se ha motivo
            // di farlo. Un numero in cima e' il motivo.
            //
            // **`NeedsRebuild2` NON basta, ed e' misurato**: sul pezzo col
            // raccordo impossibile rispondeva **0** — nessuna ricostruzione
            // necessaria — mentre `GetWhatsWrongCount` diceva 1 e il raccordo
            // aveva codice 12. Le due domande sono diverse: «va ricostruito» e
            // «e' rotto» non sono la stessa cosa, e fidarsi della prima per
            // rispondere alla seconda avrebbe dato una guardia sempre verde.
            if (featuresInError > 0)
                data["features_in_error"] = featuresInError;
            try
            {
                if (model.Extension.NeedsRebuild2 != 0)
                    data["rebuild_needed"] = true;
            }
            catch { }

            data["feature"] = feature;
            if (otherFeatures > 0) data["other_feature"] = otherFeatures;
            // Il tetto sulle lavorazioni, dichiarato come quelli su facce,
            // spigoli e corpi. `truncated` vale per tutto lo stato.
            if (unlistedFeatures > 0)
            {
                data["feature_not_listed"] = unlistedFeatures;
                data["feature_total"] = feature.Count + unlistedFeatures;
                truncated = true;
            }
            // I tipi si mandano SEMPRE quando ce ne sono: e' cio' che
            // permette di accorgersi che una lavorazione e' invisibile.
            if (unknownTypes.Count > 0) data["other_feature_kinds"] = unknownTypes.ToArray();
            data["axes"] = axes;
            if (describedBodies.Count > 0) data["bodies"] = describedBodies;
            data["faces"] = faces;
            data["edges"] = edges;
            // COSA C'E' SELEZIONATO ADESSO, detto con gli stessi nomi.
            //
            // E' l'altra meta' della firma: invece di descrivere a parole quale
            // faccia si intende, il progettista la INDICA col dito e lo stato
            // dice come si chiama. L'ambiguita' si toglie prima
            // dell'operazione, non si scopre dopo.
            //
            // Non e' uno strumento nuovo, ed e' voluto: il ponte ne ha quattro
            // e il motivo e' scritto in `McpTools`. Questa e' una riga in piu'
            // in una risposta che si legge gia', e si scrive **solo quando c'e'
            // qualcosa di selezionato** — su un documento senza selezione non
            // costa niente.
            // Il dizionario dei nomi, solo se qualcuno lo usa.
            if (featureNames.Count > 0)
            {
                var map = new Dictionary<string, object>();
                foreach (var v in featureNames)
                    map[v.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] = v.Value;
                data["features"] = map;
            }

            var selected = TheSelection(model, docState.Signatures);
            if (selected != null && selected.Count > 0) data["selection"] = selected;

            data["truncated"] = truncated;
            data["protocol_units"] = "mm";

            // LE EQUAZIONI E LE VARIABILI GLOBALI (voce Z-F1), se ce ne sono: chi
            // legge lo stato deve sapere che una quota e' guidata prima di provare
            // a cambiarla a mano.
            int totalEquations;
            var equations = SwOperations.DocumentEquations(model, out totalEquations);
            if (equations != null)
            {
                data["equations"] = equations;
                if (totalEquations > equations.Length)
                {
                    data["equations_total"] = totalEquations;
                    data["truncated"] = true;
                }
            }

            // ── LA FORMA IN UNA FRASE (voce V3) ──────────────────────────
            //
            // Va per ULTIMA perche' legge `dati`: l'ingombro, il volume, le
            // lavorazioni e le facce devono esserci gia' tutti. Metterla prima
            // vorrebbe dire descrivere meta' pezzo senza che nessuno se ne
            // accorga — la frase uscirebbe lo stesso, solo piu' corta.
            //
            // Costa **meno di trecento caratteri** su un tetto di 16.000, e
            // dice cose che oggi ognuno si ricava per conto suo: quanto e'
            // pieno l'ingombro, lo spessore di parete, i centri dei fori e il
            // diametro della corona su cui stanno. Quest'ultimo e' il conto che
            // due modelli hanno sbagliato con confidenza piena (voce E16).
            //
            // Null quando non c'e' abbastanza per dirne una: la chiave non
            // compare, invece di comparire vuota. Una descrizione vuota si
            // legge come «non c'e' niente da dire su questo pezzo», che e' una
            // cosa diversa da «non l'ho saputo dire».
            try
            {
                string shape = ShapeToWords.Describe(data);
                if (!string.IsNullOrEmpty(shape)) data["shape"] = shape;
            }
            catch { }

            // ── LA SCATOLA DEL PEZZO IN OGNI FIRMA ───────────────────────
            //
            // Un giro solo, alla fine, perche' la scatola si conosce dopo aver
            // letto i corpi mentre le firme delle facce si riempiono dopo. E'
            // lo STESSO oggetto in tutte: e' una proprieta' del pezzo, non
            // dell'entita', e copiarla non servirebbe a niente.
            docState.BoxPartMm = partBox;
            if (partBox != null)
                foreach (var signed in docState.Signatures.Values)
                    if (signed != null) signed.BoxPartMm = partBox;

            docState.Data = data;
            return docState;
        }

        /// <summary>
        /// Le proprieta' personalizzate di un cassetto: `""` per il documento,
        /// il nome di una configurazione per quella configurazione.
        ///
        /// SI LEGGONO RISOLTE. Una proprieta' puo' essere una formula
        /// («"SW-Mass@pezzo.SLDPRT"») e allora ha due valori: quello scritto e
        /// quello che ne esce. Chi guarda la scheda vede il secondo, e questa
        /// e' una descrizione di cio' che si vede.
        /// </summary>
        private static Dictionary<string, object> ReadProperty(
            IModelDoc2 model, string configuration)
        {
            var outside = new Dictionary<string, object>(StringComparer.Ordinal);
            try
            {
                var propertyManager = model.Extension.get_CustomPropertyManager(configuration)
                               as ICustomPropertyManager;
                if (propertyManager == null) return outside;
                var names = propertyManager.GetNames() as string[];
                if (names == null) return outside;
                foreach (var itemName in names)
                {
                    if (string.IsNullOrEmpty(itemName)) continue;
                    try
                    {
                        string val, resolved;
                        bool wasResolved, connected;
                        // THE RESULT CODE IS READ (Z-C21.8, finding L17-8).
                        // ICustomPropertyManager~Get6.html: «Result code as
                        // defined in swCustomInfoGetResult_e». The names come
                        // from GetNames, so NotPresent should not happen (the
                        // audit said so, L9-7); if it does, the name is left out
                        // instead of being written as an empty property.
                        int outcome = propertyManager.Get6(itemName, false, out val, out resolved,
                                                  out wasResolved, out connected);
                        if (outcome == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent) continue;
                        outside[itemName] = resolved ?? val ?? "";
                    }
                    catch { }
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// La scatola di un corpo, in mm: {minX, minY, minZ, maxX, maxY, maxZ}.
        ///
        /// GetExtremePoint sei volte, non GetBodyBox. GetBodyBox e'
        /// dichiaratamente APPROSSIMATA per le superfici non piane — puo'
        /// essere piu' grande del pezzo — e uno stato che dichiara un ingombro
        /// piu' grande del vero fa dimensionare male tutto quello che ci sta
        /// intorno. GetExtremePoint restituisce il punto vero del corpo piu'
        /// lontano in una direzione.
        /// </summary>
        private static double[] BoundingBox(IBody2 body)
        {
            try
            {
                var box = ApiHelpers.TrueBoundingBox(body);
                if (box == null) return ApproximateBox(body);
                var outside = new double[6];
                for (int i = 0; i < 6; i++) outside[i] = Units.Mm(box[i]);
                return outside;
            }
            catch { return ApproximateBox(body); }
        }

        private static double[] ApproximateBox(IBody2 body)
        {
            try
            {
                // Only the fallback when GetExtremePoint does not answer (Ingombro).
                var b = ApiHelpers.ApproximateBoundingBox(body);
                if (b == null || b.Length < 6) return null;
                var outside = new double[6];
                for (int i = 0; i < 6; i++) outside[i] = b[i] * 1000.0;
                return outside;
            }
            catch { return null; }
        }

        /// <summary>
        /// Da che parte guarda una faccia piana, nel sistema di SolidWorks:
        /// +X destra, +Y alto, +Z davanti. null se la faccia non e' piana o e'
        /// inclinata rispetto agli assi — e in quel caso non si inventa una
        /// parola: una faccia «quasi in alto» descritta come «in alto» farebbe
        /// scegliere quella sbagliata.
        /// </summary>
        private static string Facing(double[] normal)
        {
            if (normal == null || normal.Length < 3) return null;
            const double almostOne = 0.99;
            if (normal[0] > almostOne) return "right";
            if (normal[0] < -almostOne) return "left";
            if (normal[1] > almostOne) return "up";
            if (normal[1] < -almostOne) return "down";
            if (normal[2] > almostOne) return "front";
            if (normal[2] < -almostOne) return "back";
            return null;
        }

        /// <summary>Il nome della feature che ha generato il corpo, o null.</summary>
        /// <summary>
        /// I PARAMETRI DI UNA LAVORAZIONE: cio' che la rende «un M8» invece che
        /// «una lavorazione di nome Foro1».
        ///
        /// PERCHE' ESISTE. Fino al 10 settembre 2026 lo stato mandava di ogni
        /// feature tre campi: uid, nome, tipo. Un M12 passante e un M6 cieco
        /// arrivavano al modello come due righe identiche a meno del nome che
        /// l'utente aveva scritto in albero. Alla richiesta «quel foro portalo
        /// da M8 a M10» il modello non poteva sapere QUALE foro fosse M8: il
        /// dato c'era nel pezzo e non glielo dicevamo.
        ///
        /// POCHI CAMPI, NON TUTTI. `IWizardHoleFeatureData2` espone
        /// cinquantotto proprieta'. Nella riga che una persona legge prima di
        /// confermare ne entrano quattro. Il costo qui non e' il tempo di
        /// SolidWorks: e' il posto che ogni campo toglie a un altro campo nel
        /// messaggio. Percio' si sceglie, e la scelta e' «cosa serve per
        /// riconoscere questa lavorazione fra le altre».
        ///
        /// SI SCRIVE SOLO QUELLO CHE C'E'. Un campo a zero non e' un dato: e'
        /// un buco che occupa posto e che il modello potrebbe leggere come una
        /// misura vera.
        ///
        /// Firme verificate per riflessione sull'interop 29.5.1.1:
        ///   IFeature.GetDefinition() -> Object
        ///   IWizardHoleFeatureData2: Standard, FastenerType, FastenerSize
        ///       (String); Type, EndCondition (Int32); HoleDepth, Depth,
        ///       ThreadDiameter, e i quattro diametri della ricetta sotto
        ///   IExtrudeFeatureData2.GetDepth(Boolean), .GetEndCondition(Boolean),
        ///       .BothDirections, .Merge
        ///   ISimpleFilletFeatureData2: DefaultRadius, IsMultipleRadius,
        ///       GetEdgeCount()
        ///   IChamferFeatureData2: Type, GetEdgeChamferDistance(Int32),
        ///       EdgeChamferAngle, GetVertexChamferDistance(Int32), GetEdgeCount()
        ///   IFeature.GetFaceCount()
        /// </summary>
        private static void FeatureParameters(
            IFeature f, string typ, Dictionary<string, object> entry)
        {
            object definition;
            try { definition = f.GetDefinition(); }
            catch { return; }
            if (definition == null) return;

            try
            {
                if (typ == "HoleWzd")
                {
                    var d = definition as IWizardHoleFeatureData2;
                    if (d == null) return;
                    // LA NORMA E LA TAGLIA SONO STRINGHE, e sono quelle che un
                    // progettista dice a voce: «un M8 ISO passante».
                    Put(entry, "norma", Text(() => d.Standard));
                    Put(entry, "tipo_foro", Text(() => d.FastenerType));
                    Put(entry, "size", Text(() => d.FastenerSize));
                    // IL DIAMETRO LAVORATO, con la ricetta a quattro famiglie
                    // gia' in produzione in `OperazioniFori`: quale dei quattro
                    // campi sia valorizzato dipende dal tipo di foro, e
                    // leggerne uno solo darebbe zero senza dirlo.
                    double diam = Math.Max(
                        Math.Max(d.ThruTapDrillDiameter, d.TapDrillDiameter),
                        Math.Max(d.ThruHoleDiameter, d.HoleDiameter)) * 1000.0;
                    if (diam > 0) entry["diameter_mm"] = Math.Round(diam, 2);
                    if (d.ThreadDiameter > 0)
                        entry["thread_mm"] = Math.Round(d.ThreadDiameter * 1000.0, 2);
                    double prof = Math.Max(d.HoleDepth, d.Depth) * 1000.0;
                    if (prof > 0) entry["depth_mm"] = Math.Round(prof, 2);
                }
                else if (typ == "Extrusion" || typ == "Cut")
                {
                    var d = definition as IExtrudeFeatureData2;
                    if (d == null) return;
                    double prof = d.GetDepth(true) * 1000.0;
                    if (prof > 0) entry["depth_mm"] = Math.Round(prof, 2);
                    if (d.BothDirections)
                    {
                        entry["two_directions"] = true;
                        double prof2 = d.GetDepth(false) * 1000.0;
                        if (prof2 > 0) entry["depth2_mm"] = Math.Round(prof2, 2);
                    }
                    // «Fino in fondo» e «profondo 12» sono due pezzi diversi, e
                    // la profondita' da sola non li distingue: su un passante
                    // resta scritto un numero che non comanda niente.
                    entry["end_condition"] = EndCondition(d.GetEndCondition(true));
                }
                else if (typ == "Fillet")
                {
                    var d = definition as ISimpleFilletFeatureData2;
                    if (d == null) return;
                    if (d.DefaultRadius > 0)
                        entry["radius_mm"] = Math.Round(d.DefaultRadius * 1000.0, 2);
                    if (d.IsMultipleRadius) entry["radius_variable"] = true;
                    // NOT FilletItemsCount (C7-2): «This method returns -1 if
                    // this simple fillet feature is not multiple radius»
                    // (ISimpleFilletFeatureData2~FilletItemsCount.html,
                    // Remarks), and the bridge only makes uniform fillets, so
                    // the edge count it guarded never came out (-1 on every
                    // fillet of the Z-C21.3 edge-count probe, 28/9/2026).
                    EdgeAndFaceCounts(f, () => d.GetEdgeCount(), entry);
                }
                else if (typ == "Chamfer")
                {
                    // IChamferFeatureData2, not the obsolete IChamferFeatureData:
                    // the type that decides how to read the sizes is
                    // IChamferFeatureData2::Type (IChamferFeatureData~Type.html:
                    // «Obsolete. Superseded by IChamferFeatureData2::Type»).
                    var d = definition as IChamferFeatureData2;
                    if (d == null) return;
                    ChamferSizes(d.Type, side => d.GetEdgeChamferDistance(side),
                                 () => d.EdgeChamferAngle,
                                 side => d.GetVertexChamferDistance(side), entry);
                    EdgeAndFaceCounts(f, () => d.GetEdgeCount(), entry);
                }
            }
            catch { }
        }

        /// <summary>
        /// HOW MANY EDGES WERE SELECTED, AND HOW MANY FACES CAME OUT (C7-2,
        /// N14, Z-C21.3).
        ///
        /// GetEdgeCount on the definition counts the SELECTED edges, not the
        /// worked ones. Measured on 28/9/2026 (Z-C21.3, edge-count probe): a block
        /// 40 x 30 x 20 high with its four vertical edges rounded r 5, then a
        /// fillet r 2 with the bridge's options on ONE top edge. The fillet
        /// propagates along the tangent loop, removes 110.399 mm3 (110.399 by
        /// hand) and owns 8 faces; GetEdgeCount says 1, with or without
        /// AccessSelections. The same for a chamfer 2 x 45 on one edge of the
        /// same loop: 254.454 mm3, 8 faces, GetEdgeCount 1. With nothing to
        /// propagate to, one edge and one face (F1, C1); four vertical edges,
        /// 4 and 4 (F4).
        ///
        /// So the count goes out under the name of what it is,
        /// «selected_edges», and next to it the feature's own faces, which is
        /// what tells a reader that one selected edge became a whole loop:
        ///   ISimpleFilletFeatureData2~GetEdgeCount.html: «Gets the number of
        ///     edges on which to create a simple radius fillet»;
        ///   IChamferFeatureData2~GetEdgeCount.html: «Gets the number of edges
        ///     that are chamfered» — both measured as the selection;
        ///   IFeature~GetFaceCount.html: «Gets the number of faces in this
        ///     feature».
        /// Only what is there is written: a vertex chamfer has no edges, and a
        /// suppressed feature has no faces.
        /// </summary>
        private static void EdgeAndFaceCounts(
            IFeature f, Func<int> selectedEdges, Dictionary<string, object> entry)
        {
            try
            {
                int n = selectedEdges();
                if (n > 0) entry["selected_edges"] = n;
            }
            catch { }
            try
            {
                int n = f.GetFaceCount();
                if (n > 0) entry["faces"] = n;
            }
            catch { }
        }

        /// <summary>
        /// THE SIZES OF A CHAMFER, READ BY ITS TYPE (N29, Z-C21.3).
        ///
        /// Up to library 0.141.0 every chamfer was read as an edge chamfer:
        /// side 0, side 1 when different, the angle when not zero. A VERTEX
        /// chamfer has none of them — its three sizes are distances along the
        /// three edges that meet at the corner — so it went into the state
        /// with no size at all. Measured on 28/9/2026 (Z-C21.3, chamfer probe,
        /// V1 and V2, InsertFeatureChamfer with swChamferVertex on a corner of
        /// a block): Type 3, edge distances 0 and 0, angle 0, vertex distances
        /// 2/3/4 and 3/3/3, the ones asked; the triangle cut from the corner
        /// measured 2/3/4 and 3/3/3, and the removed volume was the
        /// tetrahedron's, 4 and 4.5 mm3. The same probe read the edge chamfers
        /// back by type as they were made: angle-distance 5 at 30 degrees as
        /// Type 1 with side 0 = 5 and angle 30; distance-distance 3 x 6 as
        /// Type 2 with sides 3 and 6 and angle 0.
        ///
        /// Two distances, not one, on a distance-distance chamfer: a 3 x 6 and
        /// a 3 x 3 remove different material, and with one side they would
        /// read the same.
        ///
        /// IChamferFeatureData2~Type.html, Remarks: «An angle-distance chamfer
        /// requires an angle and a distance; a distance-distance chamfer
        /// requires two distances, one for each side of the chamfered edge»;
        /// «A vertex chamfer requires three distances along three adjacent
        /// edges» (their order is the edge order of IVertex::EnumEdgesOriented,
        /// not X, Y, Z). ~EdgeChamferAngle.html: «relevant only for an
        /// angle-distance type of chamfer»; ~GetVertexChamferDistance.html:
        /// side 0, 1 or 2; ~GetEdgeChamferDistance.html: side 0 or 1, and 0
        /// only for angle-distance. swChamferType_e: swChamferAngleDistance 1,
        /// swChamferDistanceDistance 2, swChamferVertex 3,
        /// swChamferEqualDistance 16. A type none of these three branches
        /// knows (equal distance, or a combination) keeps the reading of
        /// before: side 0, side 1 when different, the angle when not zero.
        ///
        /// No interop in the signature: the reads come in as functions, so the
        /// rule runs without SOLIDWORKS (Prove, «the state reads a chamfer by
        /// its type»), and it can be checked there that a vertex chamfer never
        /// asks for an edge distance.
        /// </summary>
        internal static void ChamferSizes(
            int type, Func<int, double> edgeDistance, Func<double> angle,
            Func<int, double> vertexDistance, Dictionary<string, object> entry)
        {
            const int angleDistance = 1, distanceDistance = 2, vertex = 3;
            if (type == vertex)
            {
                var sides = new double[3];
                for (int i = 0; i < 3; i++) sides[i] = Math.Round(vertexDistance(i) * 1000.0, 2);
                if (sides.Any(s => s > 0)) entry["vertex_distances_mm"] = sides;
                return;
            }
            double first = edgeDistance(0) * 1000.0;
            if (first > 0) entry["leg1_mm"] = Math.Round(first, 2);
            if (type != angleDistance)
            {
                double second = edgeDistance(1) * 1000.0;
                if (second > 0 && Math.Abs(second - first) > 0.001) entry["leg2_mm"] = Math.Round(second, 2);
            }
            if (type != distanceDistance)
            {
                double radians = angle();
                if (radians > 0) entry["angle_deg"] = Math.Round(Units.Degrees(radians), 1);
            }
        }

        private static void Put(Dictionary<string, object> entry, string key, string val)
        {
            if (!string.IsNullOrWhiteSpace(val)) entry[key] = val.Trim();
        }

        private static string Text(Func<string> read)
        {
            try { return read(); }
            catch { return null; }
        }

        /// <summary>
        /// La condizione di fine, in italiano. I numeri di `swEndConditions_e`
        /// non vogliono dire niente per chi legge il piano, e sono proprio la
        /// cosa che distingue «fino in fondo» da «profondo 12».
        /// </summary>
        private static string EndCondition(int code)
        {
            switch (code)
            {
                case (int)swEndConditions_e.swEndCondBlind: return "profondita' data";
                case (int)swEndConditions_e.swEndCondThroughAll: return "through_all";
                case (int)swEndConditions_e.swEndCondThroughAllBoth: return "passante nei due versi";
                case (int)swEndConditions_e.swEndCondMidPlane: return "simmetrica sul piano";
                case (int)swEndConditions_e.swEndCondUpToVertex: return "fino a un vertice";
                case (int)swEndConditions_e.swEndCondUpToSurface: return "fino a una superficie";
                case (int)swEndConditions_e.swEndCondOffsetFromSurface: return "sfalsata da una superficie";
                case (int)swEndConditions_e.swEndCondUpToBody: return "fino a un corpo";
                case (int)swEndConditions_e.swEndCondUpToNext: return "fino al successivo";
                default: return "condizione " + code;
            }
        }

        /// <summary>
        /// Se la faccia e' cilindrica: il raggio in mm e la direzione dell'asse.
        ///
        /// `CylinderParams` torna sette numeri — punto (0-2), asse (3-5),
        /// raggio in METRI (6). La ricetta e' copiata da `OperazioniFori`, che
        /// la usa gia' in produzione per riconoscere i fori: si riusa invece di
        /// riscriverla, perche' due copie divergono alla prima modifica.
        /// </summary>
        /// <summary>
        /// Un cilindro: raggio, direzione dell'asse, e UN PUNTO SULL'ASSE.
        ///
        /// ── IL PUNTO SULL'ASSE E' ARRIVATO TARDI, E COSTAVA CARO ──────────
        ///
        /// `CylinderParams` torna sette numeri: **punto (0-2)**, asse (3-5),
        /// raggio in metri (6). Fino alla 0.66.0 questo metodo leggeva
        /// `par[3..6]` e buttava via `par[0..2]` — cioe' l'unica cosa che dice
        /// DOVE sta il foro.
        ///
        /// **Cosa restava a chi legge.** Di una faccia cilindrica lo stato
        /// diceva `point_mm`, `radius_mm`, `diameter_mm` e `axis`. Ma
        /// `point_mm` viene da `PointOnFace`, ed e' un punto **sulla
        /// superficie**, garantito a un raggio dall'asse in una direzione
        /// qualunque: serve a SELEZIONARE, non a dire dove sia il foro. E
        /// `axis` e' una direzione, non una posizione. Il centro non era
        /// ricavabile: per una faccia cilindrica `normal` e' null, quindi non
        /// c'e' nemmeno la direzione in cui correggere.
        ///
        /// **La misura, del 15/9/2026 (voce V2).** Due modelli, ognuno per
        /// conto suo e senza vedere le risposte dell'altro, hanno letto lo
        /// stato di una flangia con sei fori su una circonferenza Ø90 e hanno
        /// risposto **Ø79**, tutti e due con confidenza 5 su 5. Ø79 e' Ø90
        /// meno il diametro di un foro: avevano misurato fra i BORDI vicini
        /// invece che fra gli assi, perche' e' l'unica cosa che lo stato dava.
        /// Sulla stessa piastra l'interasse 160 usciva 149,5.
        ///
        /// Nessuno dei due ha sbagliato a ragionare. Hanno usato un numero
        /// corretto per la cosa per cui non era: **il difetto che non da'
        /// errore**, prodotto dallo stato invece che dalla geometria. Una
        /// flangia con la corona sbagliata di 11 mm non si imbullona.
        ///
        /// ── IL PARAMETRO IN PIU' QUI SI PUO' FARE ────────────────────────
        ///
        /// La regola del progetto e' che cambiare l'insieme delle firme di un
        /// metodo rompe chi lo chiama per riflessione anche quando il
        /// compilatore tace. Vale per cio' che il banco invoca per nome —
        /// `Read`, `ReadWithAnchor`, `SwOperations.Run`. Questo e'
        /// `private static` dentro il file che lo usa, con un solo punto di
        /// chiamata: fuori di qui non lo puo' invocare nessuno.
        /// </summary>
        private static bool CylinderOf(
            IFace2 face, out double radiusMm, out double[] axis, out double[] center)
        {
            radiusMm = 0;
            axis = null;
            center = null;
            try
            {
                var surface = face.IGetSurface();
                if (surface == null || !surface.IsCylinder()) return false;
                var par = surface.CylinderParams as double[];
                if (par == null || par.Length < 7) return false;
                radiusMm = Units.Mm(par[6]);
                axis = new[]
                {
                    Math.Round(par[3], 4),
                    Math.Round(par[4], 4),
                    Math.Round(par[5], 4),
                };
                center = new[]
                {
                    Math.Round(Units.Mm(par[0]), 3),
                    Math.Round(Units.Mm(par[1]), 3),
                    Math.Round(Units.Mm(par[2]), 3),
                };
                return radiusMm > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Uno spigolo e' dritto? E se si', lungo quale asse del modello?
        ///
        /// Firme verificate per riflessione sull'interop 29.5.1.1:
        ///   IEdge.IGetCurve() -> Curve
        ///   IEdge.GetTwoAdjacentFaces2() -> Object
        ///   ICurve.IsLine() / IsCircle() -> Boolean
        ///   ICurve.LineParams -> Object    (punto 0-2, direzione 3-5)
        ///   ICurve.CircleParams -> Object  (centro 0-2, asse 3-5, raggio 6)
        ///
        /// Torna false se lo spigolo NON e' dritto; in quel caso `raggio` e'
        /// il raggio del cerchio, quando e' un cerchio.
        /// </summary>
        private static bool EdgeDirection(IEdge edge, out string along, out double radius)
        {
            along = null;
            radius = 0;
            try
            {
                var curva = edge.IGetCurve();
                if (curva == null) return false;
                if (!curva.IsLine())
                {
                    if (curva.IsCircle())
                    {
                        var c = curva.CircleParams as double[];
                        if (c != null && c.Length >= 7) radius = Units.Mm(c[6]);
                    }
                    return false;
                }
                var par = curva.LineParams as double[];
                if (par == null || par.Length < 6) return true;
                // Si dice «lungo un asse» solo se ci sta davvero: una
                // diagonale non ha un asse, e chiamarla «lungo X» sarebbe
                // una bugia comoda — di quelle che non danno errore.
                double[] d = { Math.Abs(par[3]), Math.Abs(par[4]), Math.Abs(par[5]) };
                const double almostOne = 0.99;
                if (d[0] > almostOne) along = "X";
                else if (d[1] > almostOne) along = "Y";
                else if (d[2] > almostOne) along = "Z";
                return true;
            }
            catch { return true; }
        }

        /// <summary>Le due facce che si toccano su questo spigolo, per uid.</summary>
        private static string[] EdgeFaces(
            IEdge edge, Dictionary<IFace2, string> faceUid)
        {
            try
            {
                var due = edge.GetTwoAdjacentFaces2() as object[];
                if (due == null) return null;
                var names = new List<string>();
                foreach (var o in due)
                {
                    var f = o as IFace2;
                    string u;
                    if (f != null && faceUid.TryGetValue(f, out u)) names.Add(u);
                }
                // Se una delle due e' fuori dal tetto delle facce, meglio
                // niente che una coppia monca che sembra completa.
                return names.Count == 2 ? names.ToArray() : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Le quote di una lavorazione: nome e valore in mm.
        ///
        /// Firme verificate per riflessione sull'interop 29.5.1.1:
        ///   IFeature.GetFirstDisplayDimension() -> Object
        ///   IFeature.GetNextDisplayDimension(Object DispIn) -> Object
        ///   IDisplayDimension.GetDimension2(Int32 Index) -> Dimension
        ///   IDimension.Name / .FullName -> String
        ///   IDimension.SystemValue -> Double   (in METRI)
        ///
        /// QUATTRO AL MASSIMO. Una feature complicata ne ha molte, e
        /// elencarle tutte riempirebbe il messaggio di numeri fra cui il
        /// modello dovrebbe pescare. Le prime quattro sono quelle che
        /// contano — profondita', diametro, raggio — e se ce ne sono altre
        /// si dice quante, cosi' il buco non e' invisibile.
        ///
        /// IL NOME E' QUELLO CHE SERVE, NON L'INDICE. Un indice cambia se
        /// qualcuno tocca l'albero; «D1@Schizzo1» no, ed e' quello che si
        /// scrive per cambiarla.
        /// </summary>
        private static void FeatureDimensions(IFeature f, Dictionary<string, object> entry)
        {
            try
            {
                var quote = new List<Dictionary<string, object>>();
                int other = 0;
                object display = f.GetFirstDisplayDimension();
                while (display != null)
                {
                    var dd = display as IDisplayDimension;
                    var d = dd?.GetDimension2(0);
                    if (d != null)
                    {
                        if (quote.Count < 4)
                        {
                            string itemName = null;
                            try { itemName = d.FullName; } catch { }
                            if (string.IsNullOrEmpty(itemName)) { try { itemName = d.Name; } catch { } }
                            if (!string.IsNullOrEmpty(itemName))
                            {
                                quote.Add(Dimension(d, itemName));
                            }
                        }
                        else other++;
                    }
                    object next = null;
                    try { next = f.GetNextDisplayDimension(display); } catch { }
                    display = next;
                }
                if (quote.Count > 0) entry["dimensions"] = quote;
                if (other > 0) entry["other_dimensions"] = other;
            }
            catch { }
        }

        /// <summary>
        /// Una quota, nell'unita' che le appartiene.
        ///
        /// ── UN ANGOLO NON SI MISURA IN MILLIMETRI ─────────────────────────
        ///
        /// `SystemValue` torna tutto nelle unita' di sistema, e per un angolo
        /// quelle sono i **radianti**. Fino alla 0.69.0 questo metodo
        /// moltiplicava per mille qualunque cosa — la conversione metri→mm — e
        /// la usciva come `mm`. Su una rivoluzione a 360 gradi:
        ///
        ///     "name": "D1@Rivoluzione1", "mm": 6283.185
        ///
        /// che e' **2π × 1000**. Lo stato dichiarava una quota di sei metri e
        /// ventotto su un pezzo lungo novanta millimetri, e nessuno se ne
        /// accorgeva perche' il campo si chiama `mm` e il numero e' un numero.
        ///
        /// **Trovato per strada, misurando un'altra cosa** (voce V3): un
        /// modello a cui era stata data la sola descrizione dell'albero a
        /// gradini non e' riuscito a ridisegnarlo, ed era l'unico dei cinque.
        /// Guardando perche', l'unica quota che quel pezzo offriva era questa.
        ///
        /// ── IL NOME DEL CAMPO CAMBIA CON L'UNITA' ────────────────────────
        ///
        /// Un angolo esce come `"deg": 360`, non come `"mm": 360`. Tenere il
        /// nome `mm` e cambiare solo il numero sarebbe stato peggio di prima:
        /// un valore giusto sotto un'etichetta falsa non si puo' nemmeno
        /// sospettare, mentre 6283 almeno saltava all'occhio.
        ///
        /// Le altre — lineari, raggi, diametri, smussi — restano in `mm`, che
        /// e' l'unita' vera. Quando il tipo non si lascia leggere si tiene
        /// `mm`: e' il comportamento di prima, ed e' giusto per la
        /// stragrande maggioranza delle quote.
        /// </summary>
        private static Dictionary<string, object> Dimension(IDimension d, string itemName)
        {
            // ── E L'ENUM GIUSTO NON E' QUELLO CHE SEMBRA ─────────────────
            //
            // `IDimension.GetType()` e' un metodo COM che torna un `int`, e non
            // ha niente a che vedere con `Object.GetType()` (verificato per
            // riflessione sull'interop 29.5.1: torna `System.Int32` ed e'
            // dichiarato su `IDimension`).
            //
            // Ma **non torna `swDimensionType_e`**, che e' l'enum col nome piu'
            // somigliante e dove «angolare» vale 3. La prima scrittura usava
            // quello, e la riparazione non e' scattata: la quota continuava a
            // uscire 6283,185 mm.
            //
            // Misurato mettendo il numero nello stato e guardandolo:
            //
            //     rivoluzione a 360 gradi  ->  1
            //     spessore, lunghezza, Ø   ->  0
            //
            // che e' `swDimensionParamType_e`: `DoubleLinear = 0`,
            // `DoubleAngular = 1`, `Integer = 2`, `Unknown = -1`.
            //
            // Due enum quasi omonimi per due cose diverse, e il compilatore non
            // puo' dire niente perche' tutti e due sono `int`. L'unico modo di
            // saperlo era chiederlo al pezzo.
            const int Angular = 1;   // swDimensionParamType_e.swDimensionParamTypeDoubleAngular
            int typ = 0;
            try { typ = d.GetType(); } catch { }

            // A VALUE THAT CANNOT BE READ IS NULL, NOT ZERO (Z-C21.13 follow-up,
            // S1 in parts). ApiHelpers.ReadDimension moved to GetSystemValue3,
            // which throws on an answer that is not one number (shapes the probe
            // did not see: a driven dimension, a suppressed feature). Until
            // library 0.145.0 the angle stayed at the 0 it was initialised to,
            // «0 degrees», a plausible wrong number, and a linear dimension that
            // threw dropped the dimensions of the whole feature, because the only
            // catch was the one around the walk in FeatureDimensions. Now the
            // row stays, with its name and a null value: not read, not zero.
            object readValue = null;
            if (typ == Angular)
            {
                try { readValue = Math.Round(ApiHelpers.ReadDimension(d) * 180.0 / Math.PI, 3); } catch { }
                return new Dictionary<string, object>
                {
                    ["name"] = itemName,
                    ["deg"] = readValue,
                };
            }

            try { readValue = Math.Round(Units.Mm(ApiHelpers.ReadDimension(d)), 3); } catch { }
            return new Dictionary<string, object>
            {
                ["name"] = itemName,
                ["mm"] = readValue,
            };
        }

        /// <summary>Il nome della lavorazione che ha generato questa faccia.</summary>
        private static string FaceFeature(IFace2 face)
        {
            try { return (face.GetFeature() as IFeature)?.Name; }
            catch { return null; }
        }

        private static string BodyFeature(IBody2 body)
        {
            try
            {
                var face = body.GetFirstFace() as IFace2;
                var feature = face?.GetFeature() as IFeature;
                return feature?.Name;
            }
            catch { return null; }
        }

        /// <summary>
        /// La massa del pezzo in kg, o null se non si puo' dire.
        ///
        /// UseSystemUnits is written EXPLICITLY (ApiHelpers.NewMassProperty) as
        /// a statement of the units, not as a fix: IMassProperty~UseSystemUnits.html,
        /// «The default value is True. Thus, system units (meters, radians, and
        /// kilograms) are used» (Z-C21.9, finding N74; until library 0.144.0
        /// this comment said the value would otherwise come in document units).
        /// </summary>
        private static double? Mass(IModelDoc2 model, List<IBody2> bodies)
        {
            try
            {
                // S2 (ApiHelpers.NewMassProperty). As before, a refused
                // AddBodies is not checked here.
                var property = ApiHelpers.NewMassProperty(model, bodies?.Cast<object>(), out _);
                if (property == null) return null;
                double kg = property.Mass;
                return kg > 0 ? (double?)kg : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Estremi dello spigolo, in metri. Null se non si riescono a leggere.
        ///
        /// GetCurveParams3 NON restituisce un double[]: restituisce
        /// ICurveParamData, con StartPoint e EndPoint che sono object da
        /// convertire in double[3]. Verificato per riflessione sull'interop
        /// 29.5.1.1 (SOLIDWORKS 2021), non ricordato: la firma sbagliata qui
        /// avrebbe dato una InvalidCastException al primo spigolo letto.
        /// </summary>
        private static bool Ends(IEdge edge, out double[] start, out double[] fine)
        {
            start = null;
            fine = null;
            try
            {
                var data = ApiHelpers.EdgeParameters(edge);
                if (data == null) return false;
                start = data.StartPoint as double[];
                fine = data.EndPoint as double[];
                return start != null && fine != null && start.Length >= 3 && fine.Length >= 3;
            }
            catch { return false; }
        }

        /// <summary>
        /// Lunghezza approssimata di uno spigolo, in mm. Zero se non si legge.
        ///
        /// `internal` e non `private` perche' la usa anche `SelectEdges`
        /// quando gli spigoli arrivano per riferimento a un passo e non per
        /// uid: li' non c'e' una firma da cui leggere la misura, e senza questa
        /// il controllo «il raggio ci sta sullo spigolo» veniva saltato.
        /// Copiarla sarebbe stato il modo di farle divergere.
        /// </summary>
        internal static double LengthMm(IEdge edge)
        {
            if (edge == null) return -1;
            // LA LUNGHEZZA VERA, NON LA CORDA (voce E19, 25/9/2026).
            //
            // Fino alla libreria 0.119.0 qui c'era la distanza fra gli estremi,
            // «approssimata ma stabile, mai mostrata come quota». Era mostrata
            // eccome: finisce in `length_mm` nello stato, e per uno spigolo
            // CHIUSO — il bordo di un foro — gli estremi coincidono e la corda
            // vale ZERO. Misurato col banchetto: una piastra con due fori O12
            // pubblicava quattro spigoli «lunghi 0», e chi legge lo stato vedeva
            // una circonferenza da 37,7 descritta come uno spigolo che non c'e'.
            // Per un arco la corda e' meno dell'arco. E la classifica degli uid,
            // che e' per lunghezza, metteva i fori sempre in fondo: un foro non
            // rinumerava mai niente, e il ritrovamento per firma degli spigoli
            // non si poteva nemmeno provare.
            //
            // `ICurve.GetLength3` fra i due parametri dello spigolo: e' gia'
            // usata e misurata in `OperazioniSaldatura` per il perimetro. Se non
            // risponde si torna alla corda, che per un segmento e' la stessa
            // cosa; se non si leggono nemmeno gli estremi, -1: «non lo so», non
            // «lungo zero» (voce Z-C8).
            try
            {
                var parameters = ApiHelpers.EdgeParameters(edge, out ICurve curva);
                if (curva != null && parameters != null)
                {
                    double real = curva.GetLength3(parameters.UMinValue, parameters.UMaxValue);
                    if (real > 0) return Units.Mm(real);
                }
            }
            catch { }
            double[] a, b;
            if (!Ends(edge, out a, out b)) return -1;
            double dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2];
            return Units.Mm(Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }

        private static double[] PointOnEdge(IEdge edge)
        {
            double[] a, b;
            if (!Ends(edge, out a, out b)) return null;
            return new[]
            {
                Math.Round(Units.Mm((a[0] + b[0]) / 2), 3),
                Math.Round(Units.Mm((a[1] + b[1]) / 2), 3),
                Math.Round(Units.Mm((a[2] + b[2]) / 2), 3),
            };
        }

        /// <summary>
        /// La firma di una faccia MISURATA ADESSO, senza rileggere tutto il
        /// documento.
        ///
        /// Serve al ritrovamento per persist ID (voce E6): quando SOLIDWORKS
        /// propone una faccia, il giudice ha bisogno della firma di QUELLA
        /// faccia com'e' in questo momento — non di quella che il piano porta
        /// con se', che e' proprio cio' che si sta verificando.
        ///
        /// Niente uid: un uid e' una posizione in una classifica, e qui non
        /// c'e' nessuna classifica. Chi la usa ha gia' l'uid che cercava.
        /// </summary>
        internal static EntitySignature FaceSignature(IFace2 face)
        {
            if (face == null) return null;
            try
            {
                // ZERO-E-ZERO: 0 in `Measure` vuol dire «non misurata» per tutti
                // quelli che la leggono (guardano `> 0`).
                double areaMm2 = 0;
                try { areaMm2 = Units.Mm2(face.GetArea()); } catch { }

                int featureId;
                string featureItem;
                FeatureOfFace(face, out featureId, out featureItem);

                return new EntitySignature
                {
                    Type = "FACE",
                    Measure = Math.Round(areaMm2, 2),
                    PointMm = PointOnFace(face),
                    Normal = FaceNormalVector(face),
                    Entity = face,
                    FeatureId = featureId,
                    Feature = featureItem,
                };
            }
            catch { return null; }
        }


        /// <summary>
        /// Il DETTAGLIO di una lavorazione: tutto cio' che costa chiamate COM.
        ///
        /// Sta in un metodo suo perche' si chiama DOPO aver deciso quali
        /// lavorazioni sopravvivono al tetto, e non mentre si scorre l'albero
        /// (voce E12). Il giro sull'albero legge solo tipo e nome — che erano
        /// gia' letti comunque — e mette da parte un riferimento; il dettaglio
        /// si paga solo sulle ottanta che restano.
        /// </summary>
        /// <param name="inError">
        /// Si incrementa per ogni lavorazione che porta un errore. E' un
        /// contatore di chi chiama, e passa per riferimento perche' il
        /// conteggio finisce in `features_in_error` sullo stato intero:
        /// tenerlo qui dentro vorrebbe dire perderlo.
        /// </param>
        private static Dictionary<string, object> FeatureDetail(
            IFeature f, string typ, string itemName, ref int inError)
        {
            var entryF = new Dictionary<string, object>
            { ["uid"] = itemName, ["kind"] = typ };

            // L'UID DI UNA LAVORAZIONE E' IL SUO NOME, e un nome
            // si rinomina. Chi rinomina «Taglio1» in «Foro cavo» e
            // poi chiama «Taglio1» un'altra lavorazione ha appena
            // trasferito l'identificativo da una cosa a un'altra,
            // e nessuno se ne accorge.
            //
            // `GetID()` non si rinomina. Non sostituisce l'uid —
            // il nome serve a chi legge — ma permette di
            // controllare che il nome indichi ancora quella.
            try { entryF["feature_id"] = f.GetID(); } catch { }

            // UNA LAVORAZIONE SOPPRESSA E UNA VIVA ERANO
            // INDISTINGUIBILI. Il modello proponeva di raccordare
            // gli spigoli di un foro che nel pezzo non c'e'.
            // Si scrive solo quando e' vera: e' il caso raro, e
            // scriverla sempre costerebbe posto per dire «tutto
            // normale» ottanta volte.
            // Read in the active configuration with IsSuppressed2 since Z-C21.7
            // (S3, M44; ApiHelpers.IsSuppressedHere has the probe numbers).
            try { if (ApiHelpers.IsSuppressedHere(f)) entryF["suppressed"] = true; } catch { }

            // UNA LAVORAZIONE IN ERRORE E UNA SANA ERANO
            // INDISTINGUIBILI, ed e' peggio della soppressa.
            //
            // Misurato il 14/9/2026: piastra 100x60x20 con un
            // raccordo da 8 sugli spigoli in cima, poi la larghezza
            // portata a 10. Due raccordi da 8 su 10 mm non ci
            // stanno, e SOLIDWORKS lo dice —
            // `GetErrorCode2` = 12, `GetWhatsWrongCount` = 1 — ma
            // lo stato descriveva «Raccordo1, raggio 8» come una
            // lavorazione qualunque. Il volume era 3.000, cioe'
            // **esattamente** 100x10x3: il raccordo non aveva
            // fatto niente, e chi leggeva vedeva un pezzo sano.
            //
            // Si scrive solo quando c'e', come per `suppressed`:
            // e' il caso raro.
            try
            {
                bool warningOnly;
                int code = f.GetErrorCode2(out warningOnly);
                if (code != 0)
                {
                    entryF["error"] = FeatureErrors.Sentence(code);
                    entryF["error_code"] = code;
                    if (warningOnly) entryF["warning_only"] = true;
                    inError++;
                }
            }
            catch { }

            // I PARAMETRI: quello che rende un foro «un M8» invece
            // che «una lavorazione di nome Foro1».
            FeatureParameters(f, typ, entryF);

            // LE QUOTE: senza queste non si modifica niente.
            //
            // «quel foro portalo da M8 a M10» e «lo spessore
            // portalo a 3» sono la richiesta piu' frequente in
            // officina, e finora erano impossibili per una
            // ragione semplice: il modello non sapeva che le
            // quote esistessero. Vedeva un nome e dei parametri
            // in sola lettura.
            //
            // Una quota ha un NOME che si puo' citare
            // («D1@Schizzo1») e un valore. Metterli qui e' cio'
            // che rende possibile `set_dimension`.
            FeatureDimensions(f, entryF);
            return entryF;
        }

        private static double[] PointOnFace(IFace2 face)
        {
            try
            {
                // Un punto GARANTITO sulla faccia, non il centro del riquadro:
                // il centro di una faccia con un foro in mezzo puo' cadere nel
                // vuoto, e SelectByID2 su un punto che non sta sulla faccia
                // risponde False in silenzio.
                var p = face.GetClosestPointOn(0, 0, 0) as double[];
                if (p == null) return null;
                return new[]
                {
                    Math.Round(Units.Mm(p[0]), 3),
                    Math.Round(Units.Mm(p[1]), 3),
                    Math.Round(Units.Mm(p[2]), 3),
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// La normale di una faccia piana — quella VERA, non quella della
        /// superficie sotto.
        ///
        /// LA DIFFERENZA NON E' UN DETTAGLIO, ED E' COSTATA UN GIORNO.
        /// `PlaneParams` descrive il PIANO INFINITO su cui la faccia sta, e le
        /// due facce opposte di una piastra stanno su piani paralleli con la
        /// stessa giacitura: la superficie non sa da che parte guarda la
        /// faccia. Il 10 settembre 2026, al primo giro dentro SolidWorks, la
        /// piastra di prova 200x120x20 usciva cosi':
        ///
        ///     F1  verso=dietro  area=24000  normale=[0,0,-1]
        ///     F2  verso=dietro  area=24000  normale=[0,0,-1]
        ///
        /// Due facce opposte, la stessa normale, e nessuna faccia «front» in
        /// tutto il documento. Quindici prove sono morte li'. E non era un
        /// difetto del banco: e' cio' che il MODELLO leggeva. A chi chiede «un
        /// foro sulla faccia davanti» lo stato rispondeva che una faccia
        /// davanti non c'e', e le due facce grandi erano indistinguibili fra
        /// loro — stessa area, stesso verso, stessa firma.
        ///
        /// `IFace2.FaceInSurfaceSense()` e' il pezzo che mancava: dice se la
        /// faccia e' orientata al contrario della sua superficie. Verificata
        /// per riflessione sull'interop 29.5.1.1.
        /// </summary>
        private static double[] FaceNormalVector(IFace2 face)
        {
            try
            {
                var surface = face.IGetSurface();
                if (surface == null || !surface.IsPlane()) return null;
                var plane = surface.PlaneParams as double[];
                if (plane == null || plane.Length < 3) return null;
                double sign = face.FaceInSurfaceSense() ? -1.0 : 1.0;
                return new[]
                {
                    Math.Round(plane[0] * sign, 4),
                    Math.Round(plane[1] * sign, 4),
                    Math.Round(plane[2] * sign, 4),
                };
            }
            catch { return null; }
        }
    }
}
