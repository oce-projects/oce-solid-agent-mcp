using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
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
    /// e si scrive `troncato: true` invece di far credere che siano tutte.
    /// </summary>
    public partial class StatoDocumento
    {
        public const int MaxFacce = 60;
        public const int MaxSpigoli = 60;
        public const int MaxFeature = 80;
        public const int MaxCorpi = 24;

        /// <summary>
        /// I tipi di feature che il modello puo' davvero citare o su cui puo'
        /// lavorare. Sono i nomi di IFeature.GetTypeName2(), che NON sono
        /// localizzati (il nome nell'albero si', il tipo no).
        /// </summary>
        private static readonly HashSet<string> FeatureCheContano =
            new HashSet<string>(StringComparer.Ordinal)
        {
            // «ICE» E' UN'ESTRUSIONE, E NON SI CAPISCE DAL NOME.
            //
            // Un'estrusione fatta su una FACCIA — un rialzo, un labbro, una
            // torretta — non si chiama «Extrusion»: `GetTypeName2()` torna
            // «ICE». Misurato il 10 settembre 2026, appena nata
            // `estrudi_su_faccia`: il modello ha fatto un labbro di tenuta
            // e nello stato del passo dopo quel labbro NON C'ERA. Era
            // finito fra le «altre feature», cioe' in un numero.
            //
            // Se ne e' accorto il campo `altre_feature_tipi`, aggiunto
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
        private static readonly HashSet<string> CartelleDiServizio =
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
        public Dictionary<string, FirmaEntita> Firme { get; } =
            new Dictionary<string, FirmaEntita>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, object> Dati { get; private set; } =
            new Dictionary<string, object>();

        public class FirmaEntita
        {
            public string Tipo;          // "FACE" | "EDGE"
            public double Misura;        // area in mm2, oppure lunghezza in mm
            public double[] PuntoMm;     // un punto sull'entita', in mm
            public double[] Normale;     // solo per le facce piane
            public object Entita;        // IFace2 / IEdge, valido in questa sessione
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
        public static StatoDocumento Leggi(IModelDoc2 modello, ISldWorks sw = null)
        {
            var stato = new StatoDocumento();
            if (modello == null) return stato;

            int tipoDoc = 0;
            try { tipoDoc = modello.GetType(); } catch { }

            var dati = new Dictionary<string, object>
            {
                ["percorso"] = modello.GetPathName() ?? "",
                ["titolo"] = modello.GetTitle() ?? "",
                ["tipo"] = OperazioniSw.NomeTipoDocumento(tipoDoc),
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
            var altri = DocumentiAperti(sw, modello);
            if (altri != null && altri.Count > 0) dati["documenti_aperti"] = altri;

            if (tipoDoc == (int)swDocumentTypes_e.swDocDRAWING)
            {
                DescriviTavola(modello as IDrawingDoc, dati);
                stato.Dati = dati;
                return stato;
            }

            var parte = modello as IPartDoc;
            if (parte == null)
            {
                // Gli assiemi non si toccano ancora, e va detto qui: cosi' il
                // server non dichiara al modello operazioni che poi
                // fallirebbero.
                dati["corpo_solido"] = false;
                dati["nota"] = "documento di assieme: l'assistente non lo modifica ancora";
                stato.Dati = dati;
                return stato;
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
            var assi = new List<Dictionary<string, object>>();
            int nPiani = 0;
            int altreFeature = 0;
            // QUALI, non solo QUANTE.
            //
            // «altre_feature: 7» dice che c'e' dell'altro e non dice cosa,
            // e quel numero e' un posto dove le cose spariscono. Misurato
            // il 10 settembre 2026: appena nata `estrudi_su_faccia`, la
            // lavorazione che aveva appena creato NON compariva nello
            // stato — era finita fra le sette «altre», perche' il suo tipo
            // non e' quello che credevo. Il modello aveva appena fatto una
            // cosa e non la vedeva piu'.
            //
            // I nomi dei tipi sono corti e pochi: elencarli costa niente e
            // rende visibile un punto cieco invece di nasconderlo dietro un
            // numero.
            var tipiIgnoti = new SortedSet<string>(StringComparer.Ordinal);
            int lavorazioniInErrore = 0;
            IFeature f = parte.IFirstFeature();
            while (f != null)
            {
                string tipo = "";
                string nome = "";
                try { tipo = f.GetTypeName2(); nome = f.Name; } catch { }

                if (tipo == "RefPlane") nPiani++;
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
                else if (tipo == "RefAxis")
                {
                    // «uid» e «nome» erano LA STESSA STRINGA, scritta due
                    // volte. Su ottanta lavorazioni con nomi da venti caratteri
                    // erano oltre due chilobyte di messaggio che non dicevano
                    // niente — e il posto che tolgono e' esattamente quello che
                    // serve ai parametri qui sotto. Resta «uid», che e' il nome
                    // con cui tutto il resto del sistema chiama le entita'.
                    assi.Add(new Dictionary<string, object> { ["uid"] = nome, ["tipo"] = tipo });
                }
                else if (FeatureCheContano.Contains(tipo))
                {
                    if (feature.Count < MaxFeature)
                    {
                        var voceF = new Dictionary<string, object>
                        { ["uid"] = nome, ["tipo"] = tipo };

                        // UNA LAVORAZIONE SOPPRESSA E UNA VIVA ERANO
                        // INDISTINGUIBILI. Il modello proponeva di raccordare
                        // gli spigoli di un foro che nel pezzo non c'e'.
                        // Si scrive solo quando e' vera: e' il caso raro, e
                        // scriverla sempre costerebbe posto per dire «tutto
                        // normale» ottanta volte.
                        try { if (f.IsSuppressed()) voceF["soppressa"] = true; } catch { }

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
                        // Si scrive solo quando c'e', come per `soppressa`:
                        // e' il caso raro.
                        try
                        {
                            bool soloAvviso;
                            int codice = f.GetErrorCode2(out soloAvviso);
                            if (codice != 0)
                            {
                                voceF["errore"] = ErroriFeature.Frase(codice);
                                voceF["errore_codice"] = codice;
                                if (soloAvviso) voceF["solo_avviso"] = true;
                                lavorazioniInErrore++;
                            }
                        }
                        catch { }

                        // I PARAMETRI: quello che rende un foro «un M8» invece
                        // che «una lavorazione di nome Foro1».
                        ParametriDellaFeature(f, tipo, voceF);

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
                        // che rende possibile `modifica_quota`.
                        QuoteDellaFeature(f, voceF);
                        feature.Add(voceF);
                    }
                    else altreFeature++;
                }
                else if (!CartelleDiServizio.Contains(tipo))
                {
                    altreFeature++;
                    if (!string.IsNullOrEmpty(tipo)) tipiIgnoti.Add(tipo);
                }
                try { f = (IFeature)f.GetNextFeature(); } catch { f = null; }
            }

            // ── corpi, facce, spigoli ─────────────────────────────────────
            //
            // I CORPI SONO LA NOVITA' DELLA 0.30.0, E RISPONDONO A UNA DOMANDA
            // CHE PRIMA NON AVEVA RISPOSTA.
            //
            // Fino alla 0.29.1 di cio' che c'era nel documento si diceva una
            // cosa sola: `corpo_solido`, si' o no. A chi deve lavorare su un
            // telaio gia' disegnato — «tappa le estremita' di quelle barre»,
            // «fai i fori sul montante» — un booleano non serve a niente, e il
            // committente lo ha chiesto per nome: «mancano dei tools per
            // sapere quello che e' gia' stato disegnato?».
            //
            // BVisibleOnly: false, e il false e' voluto. Prima era true: un
            // corpo nascosto per guardare dentro il pezzo spariva dallo stato
            // senza che niente lo dicesse, e le lavorazioni su di lui
            // diventavano impossibili senza una ragione leggibile.
            var facce = new List<Dictionary<string, object>>();
            var spigoli = new List<Dictionary<string, object>>();
            var corpiDescritti = new List<Dictionary<string, object>>();
            bool haCorpo = false;
            bool troncato = false;

            object corpiObj = null;
            try { corpiObj = parte.GetBodies2((int)swBodyType_e.swSolidBody, false); } catch { }
            var corpi = (corpiObj as object[] ?? new object[0])
                .Select(o => o as IBody2).Where(b => b != null).ToList();

            // Il materiale: null ESPLICITO quando non c'e'.
            //
            // Omettere il campo e scriverlo null non sono la stessa cosa. Un
            // campo che manca il modello non lo vede; un `materiale: null` gli
            // dice che quel posto esiste ed e' vuoto — ed e' l'unico modo
            // perche' si accorga di doverlo riempire. Fino alla 0.29.1 la
            // cartella dei materiali era anzi esclusa apposta dall'albero.
            string materiale = null;
            try
            {
                string archivio;
                var letto = parte.GetMaterialPropertyName2("", out archivio);
                if (!string.IsNullOrWhiteSpace(letto)) materiale = letto;
            }
            catch { }
            dati["materiale"] = materiale;

            if (corpi.Count > 0)
            {
                haCorpo = true;

                // ── i corpi, uno per uno ──────────────────────────────────
                for (int i = 0; i < corpi.Count && i < MaxCorpi; i++)
                {
                    var corpo = corpi[i];
                    var voce = new Dictionary<string, object> { ["uid"] = "C" + (i + 1) };
                    try { voce["nome"] = corpo.Name; } catch { }
                    var scatola = Ingombro(corpo);
                    if (scatola != null)
                    {
                        voce["ingombro_mm"] = new[]
                        {
                            Math.Round(scatola[3] - scatola[0], 2),
                            Math.Round(scatola[4] - scatola[1], 2),
                            Math.Round(scatola[5] - scatola[2], 2),
                        };
                        voce["centro_mm"] = new[]
                        {
                            Math.Round((scatola[0] + scatola[3]) / 2, 2),
                            Math.Round((scatola[1] + scatola[4]) / 2, 2),
                            Math.Round((scatola[2] + scatola[5]) / 2, 2),
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
                    //   fatto_da = TUBO STRUTTURALE QUADRO 80X80X5(1)
                    // Il 10 settembre 2026 questa somiglianza mi ha fatto
                    // togliere il campo quando il nome comincia per la
                    // lavorazione. Sbagliato, per due ragioni che il banco ha
                    // detto subito:
                    //
                    // 1. il risparmio era 192 caratteri su un telaio, non 840:
                    //    le barre sono QUATTRO, non ventiquattro (avevo usato
                    //    il tetto `MaxCorpi` al posto del numero vero);
                    // 2. e soprattutto: `nome` e `fatto_da` condividono la
                    //    stringa ma NON sono la stessa cosa. Uno e' come si
                    //    chiama il corpo, l'altro e' da dove viene. Che oggi
                    //    l'uno si possa ricavare dall'altro e' una coincidenza
                    //    di come SolidWorks battezza i corpi saldati, non una
                    //    proprieta' su cui appoggiarsi — e ricavarlo sarebbe
                    //    comunque una DEDUZIONE, che e' esattamente cio' che
                    //    stiamo togliendo di mezzo al modello.
                    //
                    // La regola «si scrive solo cio' che aggiunge» resta buona
                    // per i campi identici (`uid`/`nome`) e per quelli che
                    // ripetono un altro campo (`da_lavorazione` su un saldato).
                    // Non vale per due campi che dicono cose diverse.
                    string lav = FeatureDelCorpo(corpo);
                    if (lav != null) voce["fatto_da"] = lav;
                    corpiDescritti.Add(voce);

                    // E il corpo si deve poter RITROVARE. Senza questa firma,
                    // «C1» nello stato sarebbe un'etichetta che il modello
                    // legge e nessuno sa riportare a un IBody2 al momento di
                    // eseguire: e' il ponte che manca a «tappa le estremita'
                    // di questo telaio» in un messaggio nuovo.
                    //
                    // La firma di un corpo e' il suo centro piu' l'ingombro:
                    // sono i due numeri che cambiano se qualcuno modifica il
                    // pezzo fra la proposta e l'esecuzione.
                    stato.Firme["C" + (i + 1)] = new FirmaEntita
                    {
                        Tipo = "BODY",
                        Misura = scatola == null ? 0 : Math.Round(
                            (scatola[3] - scatola[0]) * (scatola[4] - scatola[1]) *
                            (scatola[5] - scatola[2]), 2),
                        PuntoMm = voce.ContainsKey("centro_mm")
                            ? voce["centro_mm"] as double[]
                            : null,
                        Entita = corpo,
                    };
                }
                if (corpi.Count > MaxCorpi) dati["corpi_non_elencati"] = corpi.Count - MaxCorpi;
                dati["corpi_totali"] = corpi.Count;

                // ── l'ingombro di tutto il pezzo ──────────────────────────
                double[] tutto = null;
                foreach (var corpo in corpi)
                {
                    var b = Ingombro(corpo);
                    if (b == null) continue;
                    if (tutto == null) { tutto = (double[])b.Clone(); continue; }
                    for (int k = 0; k < 3; k++) tutto[k] = Math.Min(tutto[k], b[k]);
                    for (int k = 3; k < 6; k++) tutto[k] = Math.Max(tutto[k], b[k]);
                }
                if (tutto != null)
                    dati["ingombro_mm"] = new[]
                    {
                        Math.Round(tutto[3] - tutto[0], 2),
                        Math.Round(tutto[4] - tutto[1], 2),
                        Math.Round(tutto[5] - tutto[2], 2),
                    };

                // ── la massa, SOLO se c'e' un materiale ───────────────────
                //
                // Senza materiale SolidWorks usa comunque una densita'
                // predefinita e restituisce un numero. Un peso plausibile e
                // falso e' il difetto peggiore che ci sia qui: chi legge «12,4
                // kg» non ha modo di sapere che e' inventato. Quindi non si
                // manda affatto.
                if (materiale != null)
                {
                    double? kg = Massa(modello, corpi);
                    if (kg.HasValue) dati["massa_kg"] = Math.Round(kg.Value, 3);
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
                double volume = 0;
                foreach (var c in corpi)
                {
                    try
                    {
                        var mp = c.GetMassProperties(1000.0) as double[];
                        if (mp != null && mp.Length > 4 && mp[3] > 0) volume += mp[3] * 1e9;
                    }
                    catch { }
                }
                if (volume > 0) dati["volume_mm3"] = Math.Round(volume, 1);

                // ── facce e spigoli, con il tetto sul TOTALE ──────────────
                //
                // Il tetto stava DENTRO il ciclo sui corpi: su un telaio da
                // quattro barre uscivano 240 facce invece di 60, e `troncato`
                // restava false perche' nessun corpo singolo superava il tetto.
                // Il payload cresceva con le barre: ~11 KB per quattro, oltre
                // 50 KB per venti. Adesso si raccoglie tutto, si ordina una
                // volta e si taglia una volta.
                var tutteLeFacce = new List<Tuple<IFace2, int>>();
                var tuttiGliSpigoli = new List<Tuple<IEdge, int>>();
                // Da quale lavorazione nasce OGNI CORPO. Serve a non ripetere
                // su ogni faccia una cosa che il campo «corpo» dice gia': vedi
                // il commento su `da_lavorazione` piu' sotto.
                var lavorazioneDelCorpo = new string[corpi.Count];
                for (int i = 0; i < corpi.Count; i++) lavorazioneDelCorpo[i] = FeatureDelCorpo(corpi[i]);
                for (int i = 0; i < corpi.Count; i++)
                {
                    var facceObj = corpi[i].GetFaces() as object[];
                    if (facceObj != null)
                        foreach (var o in facceObj)
                        {
                            var x = o as IFace2;
                            if (x != null) tutteLeFacce.Add(Tuple.Create(x, i));
                        }
                    var spigoliObj = corpi[i].GetEdges() as object[];
                    if (spigoliObj != null)
                        foreach (var o in spigoliObj)
                        {
                            var x = o as IEdge;
                            if (x != null) tuttiGliSpigoli.Add(Tuple.Create(x, i));
                        }
                }

                // Le piu' grandi per prime: sono quelle su cui un progettista
                // lavora, e sono quelle che una descrizione corta deve avere.
                var ordinate = tutteLeFacce
                    .OrderByDescending(t => { try { return t.Item1.GetArea(); } catch { return 0.0; } })
                    .ToList();
                if (ordinate.Count > MaxFacce)
                {
                    troncato = true;
                    dati["facce_totali"] = ordinate.Count;
                }
                // Faccia -> uid, per poter dire su quali facce si appoggia
                // uno spigolo. Si riempie qui perche' le facce si numerano
                // prima degli spigoli, e serve li'.
                var uidDellaFaccia = new Dictionary<IFace2, string>();
                int iFaccia = 0;
                foreach (var t in ordinate.Take(MaxFacce))
                {
                    iFaccia++;
                    var uid = "F" + iFaccia;
                    double areaMm2 = 0;
                    try { areaMm2 = Unita.Mm2(t.Item1.GetArea()); } catch { }
                    var punto = PuntoSuFaccia(t.Item1);
                    var normale = NormaleFaccia(t.Item1);
                    var voce = new Dictionary<string, object>
                    {
                        ["uid"] = uid,
                        ["area_mm2"] = Math.Round(areaMm2, 2),
                        ["punto_mm"] = punto,
                        ["normale"] = normale,
                        ["piana"] = normale != null,
                    };
                    // UN FORO DEVE VEDERSI COME UN FORO, NON COME «UNA FACCIA
                    // NON PIANA DI AREA 1256».
                    //
                    // Fino al 10 settembre 2026 di una faccia cilindrica lo
                    // stato diceva soltanto `piana: false`. Alla richiesta
                    // «quel foro portalo da M8 a M10» il modello non aveva
                    // modo di sapere quale faccia fosse un M8: il diametro
                    // c'era nella geometria e non glielo dicevamo.
                    //
                    // Il raggio si legge da `CylinderParams`, che torna un
                    // vettore di sette numeri: punto (0-2), asse (3-5), raggio
                    // in metri (6). E' la stessa ricetta gia' in produzione
                    // per riconoscere i fori in `OperazioniFori`.
                    double raggioMm;
                    double[] asseCilindro;
                    if (normale == null && CilindroDi(t.Item1, out raggioMm, out asseCilindro))
                    {
                        voce["raggio_mm"] = Math.Round(raggioMm, 3);
                        voce["diametro_mm"] = Math.Round(raggioMm * 2, 3);
                        voce["asse"] = asseCilindro;
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
                    // esattamente quello che il campo «corpo» diceva gia'.
                    // Costo pieno, informazione zero.
                    //
                    // La regola e' quindi: si scrive quando la faccia viene da
                    // una lavorazione DIVERSA da quella che ha fatto il corpo.
                    // E' il caso in cui il campo dice qualcosa di nuovo — un
                    // foro, una tasca, un raccordo su un pezzo — ed e'
                    // esattamente il caso che serve per «quel foro portalo a
                    // M10».
                    string daFeature = FeatureDellaFaccia(t.Item1);
                    if (daFeature != null
                        && (t.Item2 >= lavorazioneDelCorpo.Length
                            || !string.Equals(daFeature, lavorazioneDelCorpo[t.Item2], StringComparison.Ordinal)))
                    {
                        voce["da_lavorazione"] = daFeature;
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
                    string verso = Verso(normale);
                    if (verso != null) voce["verso"] = verso;
                    // A quale corpo appartiene. Su un saldato senza questo
                    // campo arrivano quaranta facce indistinguibili.
                    if (corpi.Count > 1 && t.Item2 < MaxCorpi) voce["corpo"] = "C" + (t.Item2 + 1);
                    facce.Add(voce);
                    uidDellaFaccia[t.Item1] = uid;
                    stato.Firme[uid] = new FirmaEntita
                    {
                        Tipo = "FACE",
                        Misura = Math.Round(areaMm2, 2),
                        PuntoMm = punto,
                        Normale = normale,
                        Entita = t.Item1,
                    };
                }

                var ordinati = tuttiGliSpigoli
                    .OrderByDescending(t => LunghezzaMm(t.Item1))
                    .ToList();
                if (ordinati.Count > MaxSpigoli)
                {
                    troncato = true;
                    dati["spigoli_totali"] = ordinati.Count;
                }
                int iSpigolo = 0;
                foreach (var t in ordinati.Take(MaxSpigoli))
                {
                    iSpigolo++;
                    var uid = "E" + iSpigolo;
                    double lung = LunghezzaMm(t.Item1);
                    var punto = PuntoSuSpigolo(t.Item1);
                    var voce = new Dictionary<string, object>
                    {
                        ["uid"] = uid,
                        ["lunghezza_mm"] = Math.Round(lung, 2),
                        ["punto_mm"] = punto,
                    };
                    // UNO SPIGOLO SENZA RUOLO NON SI PUO' NOMINARE.
                    //
                    // Le facce dicono da che parte guardano — «alto»,
                    // «davanti» — e il modello le sa scegliere. Gli spigoli
                    // dicevano soltanto quanto sono lunghi. Misurato il 10
                    // settembre 2026 su una piastra 300x200x15: venti
                    // spigoli, tutti uguali a leggersi, e alla richiesta
                    // «raccorda i quattro angoli» il modello non ha nemmeno
                    // provato a nominarli — ha citato un passo inesistente.
                    //
                    // «lungo: Y» costa quattordici caratteri e dice quello
                    // che direbbe un progettista: gli angoli di una piastra
                    // sono gli spigoli nella direzione dello spessore.
                    string lungoAsse;
                    double raggioSpigolo;
                    if (DirezioneSpigolo(t.Item1, out lungoAsse, out raggioSpigolo))
                    {
                        if (lungoAsse != null) voce["lungo"] = lungoAsse;
                    }
                    else
                    {
                        // Non dritto: si dice, perche' come DIREZIONE non
                        // vale e la ripetizione lo rifiuterebbe.
                        voce["curvo"] = true;
                        if (raggioSpigolo > 0) voce["raggio_mm"] = Math.Round(raggioSpigolo, 2);
                    }
                    // SU QUALI FACCE SI APPOGGIA: e' cosi' che si dice «il
                    // bordo della faccia in alto» senza contare i millimetri.
                    var fra = FacceDelloSpigolo(t.Item1, uidDellaFaccia);
                    if (fra != null) voce["fra"] = fra;
                    if (corpi.Count > 1 && t.Item2 < MaxCorpi) voce["corpo"] = "C" + (t.Item2 + 1);
                    spigoli.Add(voce);
                    stato.Firme[uid] = new FirmaEntita
                    {
                        Tipo = "EDGE",
                        Misura = Math.Round(lung, 2),
                        PuntoMm = punto,
                        Entita = t.Item1,
                    };
                }
            }

            dati["corpo_solido"] = haCorpo;
            dati["piani_disponibili"] = nPiani;

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
            if (lavorazioniInErrore > 0)
                dati["lavorazioni_in_errore"] = lavorazioniInErrore;
            try
            {
                if (modello.Extension.NeedsRebuild2 != 0)
                    dati["ricostruzione_necessaria"] = true;
            }
            catch { }

            dati["feature"] = feature;
            if (altreFeature > 0) dati["altre_feature"] = altreFeature;
            // I tipi si mandano SEMPRE quando ce ne sono: e' cio' che
            // permette di accorgersi che una lavorazione e' invisibile.
            if (tipiIgnoti.Count > 0) dati["altre_feature_tipi"] = tipiIgnoti.ToArray();
            dati["assi"] = assi;
            if (corpiDescritti.Count > 0) dati["corpi"] = corpiDescritti;
            dati["facce"] = facce;
            dati["spigoli"] = spigoli;
            dati["troncato"] = troncato;
            dati["unita_protocollo"] = "mm";
            stato.Dati = dati;
            return stato;
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
        private static double[] Ingombro(IBody2 corpo)
        {
            try
            {
                var direzioni = new[]
                {
                    new double[] { -1, 0, 0 }, new double[] { 0, -1, 0 }, new double[] { 0, 0, -1 },
                    new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 },
                };
                var fuori = new double[6];
                for (int i = 0; i < 6; i++)
                {
                    double x, y, z;
                    if (!corpo.GetExtremePoint(direzioni[i][0], direzioni[i][1], direzioni[i][2],
                                               out x, out y, out z))
                        return ScatolaApprossimata(corpo);
                    double[] p = { x, y, z };
                    fuori[i] = p[i % 3] * 1000.0;
                }
                return fuori;
            }
            catch { return ScatolaApprossimata(corpo); }
        }

        private static double[] ScatolaApprossimata(IBody2 corpo)
        {
            try
            {
                var b = corpo.GetBodyBox() as double[];
                if (b == null || b.Length < 6) return null;
                var fuori = new double[6];
                for (int i = 0; i < 6; i++) fuori[i] = b[i] * 1000.0;
                return fuori;
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
        private static string Verso(double[] normale)
        {
            if (normale == null || normale.Length < 3) return null;
            const double quasiUno = 0.99;
            if (normale[0] > quasiUno) return "destra";
            if (normale[0] < -quasiUno) return "sinistra";
            if (normale[1] > quasiUno) return "alto";
            if (normale[1] < -quasiUno) return "basso";
            if (normale[2] > quasiUno) return "davanti";
            if (normale[2] < -quasiUno) return "dietro";
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
        ///       FilletItemsCount
        ///   IChamferFeatureData.GetEdgeChamferDistance(Int32),
        ///       .EdgeChamferAngle
        /// </summary>
        private static void ParametriDellaFeature(
            IFeature f, string tipo, Dictionary<string, object> voce)
        {
            object definizione;
            try { definizione = f.GetDefinition(); }
            catch { return; }
            if (definizione == null) return;

            try
            {
                if (tipo == "HoleWzd")
                {
                    var d = definizione as IWizardHoleFeatureData2;
                    if (d == null) return;
                    // LA NORMA E LA TAGLIA SONO STRINGHE, e sono quelle che un
                    // progettista dice a voce: «un M8 ISO passante».
                    Metti(voce, "norma", Testo(() => d.Standard));
                    Metti(voce, "tipo_foro", Testo(() => d.FastenerType));
                    Metti(voce, "taglia", Testo(() => d.FastenerSize));
                    // IL DIAMETRO LAVORATO, con la ricetta a quattro famiglie
                    // gia' in produzione in `OperazioniFori`: quale dei quattro
                    // campi sia valorizzato dipende dal tipo di foro, e
                    // leggerne uno solo darebbe zero senza dirlo.
                    double diam = Math.Max(
                        Math.Max(d.ThruTapDrillDiameter, d.TapDrillDiameter),
                        Math.Max(d.ThruHoleDiameter, d.HoleDiameter)) * 1000.0;
                    if (diam > 0) voce["diametro_mm"] = Math.Round(diam, 2);
                    if (d.ThreadDiameter > 0)
                        voce["filetto_mm"] = Math.Round(d.ThreadDiameter * 1000.0, 2);
                    double prof = Math.Max(d.HoleDepth, d.Depth) * 1000.0;
                    if (prof > 0) voce["profondita_mm"] = Math.Round(prof, 2);
                }
                else if (tipo == "Extrusion" || tipo == "Cut")
                {
                    var d = definizione as IExtrudeFeatureData2;
                    if (d == null) return;
                    double prof = d.GetDepth(true) * 1000.0;
                    if (prof > 0) voce["profondita_mm"] = Math.Round(prof, 2);
                    if (d.BothDirections)
                    {
                        voce["due_versi"] = true;
                        double prof2 = d.GetDepth(false) * 1000.0;
                        if (prof2 > 0) voce["profondita2_mm"] = Math.Round(prof2, 2);
                    }
                    // «Fino in fondo» e «profondo 12» sono due pezzi diversi, e
                    // la profondita' da sola non li distingue: su un passante
                    // resta scritto un numero che non comanda niente.
                    voce["condizione"] = CondizioneFine(d.GetEndCondition(true));
                }
                else if (tipo == "Fillet")
                {
                    var d = definizione as ISimpleFilletFeatureData2;
                    if (d == null) return;
                    if (d.DefaultRadius > 0)
                        voce["raggio_mm"] = Math.Round(d.DefaultRadius * 1000.0, 2);
                    if (d.IsMultipleRadius) voce["raggio_variabile"] = true;
                    if (d.FilletItemsCount > 0) voce["spigoli"] = d.FilletItemsCount;
                }
                else if (tipo == "Chamfer")
                {
                    var d = definizione as IChamferFeatureData;
                    if (d == null) return;
                    // DUE DISTANZE, NON UNA. Uno smusso 3x6 e uno 3x3 tolgono
                    // materiale diverso, e con un lato solo si leggono uguali.
                    double l1 = d.GetEdgeChamferDistance(0) * 1000.0;
                    double l2 = d.GetEdgeChamferDistance(1) * 1000.0;
                    if (l1 > 0) voce["lato1_mm"] = Math.Round(l1, 2);
                    if (l2 > 0 && Math.Abs(l2 - l1) > 0.001) voce["lato2_mm"] = Math.Round(l2, 2);
                    if (d.EdgeChamferAngle > 0)
                        voce["angolo_gradi"] = Math.Round(Unita.Gradi(d.EdgeChamferAngle), 1);
                }
            }
            catch { }
        }

        private static void Metti(Dictionary<string, object> voce, string chiave, string valore)
        {
            if (!string.IsNullOrWhiteSpace(valore)) voce[chiave] = valore.Trim();
        }

        private static string Testo(Func<string> leggi)
        {
            try { return leggi(); }
            catch { return null; }
        }

        /// <summary>
        /// La condizione di fine, in italiano. I numeri di `swEndConditions_e`
        /// non vogliono dire niente per chi legge il piano, e sono proprio la
        /// cosa che distingue «fino in fondo» da «profondo 12».
        /// </summary>
        private static string CondizioneFine(int codice)
        {
            switch (codice)
            {
                case (int)swEndConditions_e.swEndCondBlind: return "profondita' data";
                case (int)swEndConditions_e.swEndCondThroughAll: return "passante";
                case (int)swEndConditions_e.swEndCondThroughAllBoth: return "passante nei due versi";
                case (int)swEndConditions_e.swEndCondMidPlane: return "simmetrica sul piano";
                case (int)swEndConditions_e.swEndCondUpToVertex: return "fino a un vertice";
                case (int)swEndConditions_e.swEndCondUpToSurface: return "fino a una superficie";
                case (int)swEndConditions_e.swEndCondOffsetFromSurface: return "sfalsata da una superficie";
                case (int)swEndConditions_e.swEndCondUpToBody: return "fino a un corpo";
                case (int)swEndConditions_e.swEndCondUpToNext: return "fino al successivo";
                default: return "condizione " + codice;
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
        private static bool CilindroDi(IFace2 faccia, out double raggioMm, out double[] asse)
        {
            raggioMm = 0;
            asse = null;
            try
            {
                var superficie = faccia.IGetSurface();
                if (superficie == null || !superficie.IsCylinder()) return false;
                var par = superficie.CylinderParams as double[];
                if (par == null || par.Length < 7) return false;
                raggioMm = Unita.Mm(par[6]);
                asse = new[]
                {
                    Math.Round(par[3], 4),
                    Math.Round(par[4], 4),
                    Math.Round(par[5], 4),
                };
                return raggioMm > 0;
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
        private static bool DirezioneSpigolo(IEdge spigolo, out string lungo, out double raggio)
        {
            lungo = null;
            raggio = 0;
            try
            {
                var curva = spigolo.IGetCurve();
                if (curva == null) return false;
                if (!curva.IsLine())
                {
                    if (curva.IsCircle())
                    {
                        var c = curva.CircleParams as double[];
                        if (c != null && c.Length >= 7) raggio = Unita.Mm(c[6]);
                    }
                    return false;
                }
                var par = curva.LineParams as double[];
                if (par == null || par.Length < 6) return true;
                // Si dice «lungo un asse» solo se ci sta davvero: una
                // diagonale non ha un asse, e chiamarla «lungo X» sarebbe
                // una bugia comoda — di quelle che non danno errore.
                double[] d = { Math.Abs(par[3]), Math.Abs(par[4]), Math.Abs(par[5]) };
                const double quasiUno = 0.99;
                if (d[0] > quasiUno) lungo = "X";
                else if (d[1] > quasiUno) lungo = "Y";
                else if (d[2] > quasiUno) lungo = "Z";
                return true;
            }
            catch { return true; }
        }

        /// <summary>Le due facce che si toccano su questo spigolo, per uid.</summary>
        private static string[] FacceDelloSpigolo(
            IEdge spigolo, Dictionary<IFace2, string> uidDellaFaccia)
        {
            try
            {
                var due = spigolo.GetTwoAdjacentFaces2() as object[];
                if (due == null) return null;
                var nomi = new List<string>();
                foreach (var o in due)
                {
                    var f = o as IFace2;
                    string u;
                    if (f != null && uidDellaFaccia.TryGetValue(f, out u)) nomi.Add(u);
                }
                // Se una delle due e' fuori dal tetto delle facce, meglio
                // niente che una coppia monca che sembra completa.
                return nomi.Count == 2 ? nomi.ToArray() : null;
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
        private static void QuoteDellaFeature(IFeature f, Dictionary<string, object> voce)
        {
            try
            {
                var quote = new List<Dictionary<string, object>>();
                int altre = 0;
                object display = f.GetFirstDisplayDimension();
                while (display != null)
                {
                    var dd = display as IDisplayDimension;
                    var d = dd?.GetDimension2(0);
                    if (d != null)
                    {
                        if (quote.Count < 4)
                        {
                            string nome = null;
                            try { nome = d.FullName; } catch { }
                            if (string.IsNullOrEmpty(nome)) { try { nome = d.Name; } catch { } }
                            if (!string.IsNullOrEmpty(nome))
                            {
                                quote.Add(new Dictionary<string, object>
                                {
                                    ["nome"] = nome,
                                    ["mm"] = Math.Round(Unita.Mm(d.SystemValue), 3),
                                });
                            }
                        }
                        else altre++;
                    }
                    object prossimo = null;
                    try { prossimo = f.GetNextDisplayDimension(display); } catch { }
                    display = prossimo;
                }
                if (quote.Count > 0) voce["quote"] = quote;
                if (altre > 0) voce["altre_quote"] = altre;
            }
            catch { }
        }

        /// <summary>Il nome della lavorazione che ha generato questa faccia.</summary>
        private static string FeatureDellaFaccia(IFace2 faccia)
        {
            try { return (faccia.GetFeature() as IFeature)?.Name; }
            catch { return null; }
        }

        private static string FeatureDelCorpo(IBody2 corpo)
        {
            try
            {
                var faccia = corpo.GetFirstFace() as IFace2;
                var feature = faccia?.GetFeature() as IFeature;
                return feature?.Name;
            }
            catch { return null; }
        }

        /// <summary>
        /// La massa del pezzo in kg, o null se non si puo' dire.
        ///
        /// UseSystemUnits si scrive ESPLICITAMENTE: senza, il valore arriva
        /// nelle unita' del documento, che sul pezzo di un altro possono
        /// essere pollici e libbre, e nessuno se ne accorgerebbe.
        /// </summary>
        private static double? Massa(IModelDoc2 modello, List<IBody2> corpi)
        {
            try
            {
                var proprieta = modello.Extension.CreateMassProperty() as IMassProperty;
                if (proprieta == null) return null;
                proprieta.UseSystemUnits = true;
                if (corpi != null && corpi.Count > 0)
                    proprieta.AddBodies(corpi.Cast<object>().ToArray());
                double kg = proprieta.Mass;
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
        private static bool Estremi(IEdge spigolo, out double[] inizio, out double[] fine)
        {
            inizio = null;
            fine = null;
            try
            {
                var dati = spigolo.GetCurveParams3();
                if (dati == null) return false;
                inizio = dati.StartPoint as double[];
                fine = dati.EndPoint as double[];
                return inizio != null && fine != null && inizio.Length >= 3 && fine.Length >= 3;
            }
            catch { return false; }
        }

        private static double LunghezzaMm(IEdge spigolo)
        {
            // Distanza fra gli estremi: per uno spigolo curvo NON e' la
            // lunghezza vera, ed e' voluto. Serve solo a ordinare gli spigoli
            // per grandezza e a fare da firma per ritrovarli: una misura
            // approssimata ma stabile va bene per tutte e due le cose, e non
            // viene mai mostrata come quota.
            double[] a, b;
            if (!Estremi(spigolo, out a, out b)) return 0;
            double dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2];
            return Unita.Mm(Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }

        private static double[] PuntoSuSpigolo(IEdge spigolo)
        {
            double[] a, b;
            if (!Estremi(spigolo, out a, out b)) return null;
            return new[]
            {
                Math.Round(Unita.Mm((a[0] + b[0]) / 2), 3),
                Math.Round(Unita.Mm((a[1] + b[1]) / 2), 3),
                Math.Round(Unita.Mm((a[2] + b[2]) / 2), 3),
            };
        }

        private static double[] PuntoSuFaccia(IFace2 faccia)
        {
            try
            {
                // Un punto GARANTITO sulla faccia, non il centro del riquadro:
                // il centro di una faccia con un foro in mezzo puo' cadere nel
                // vuoto, e SelectByID2 su un punto che non sta sulla faccia
                // risponde False in silenzio.
                var p = faccia.GetClosestPointOn(0, 0, 0) as double[];
                if (p == null) return null;
                return new[]
                {
                    Math.Round(Unita.Mm(p[0]), 3),
                    Math.Round(Unita.Mm(p[1]), 3),
                    Math.Round(Unita.Mm(p[2]), 3),
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
        /// Due facce opposte, la stessa normale, e nessuna faccia «davanti» in
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
        private static double[] NormaleFaccia(IFace2 faccia)
        {
            try
            {
                var superficie = faccia.IGetSurface();
                if (superficie == null || !superficie.IsPlane()) return null;
                var piano = superficie.PlaneParams as double[];
                if (piano == null || piano.Length < 3) return null;
                double segno = faccia.FaceInSurfaceSense() ? -1.0 : 1.0;
                return new[]
                {
                    Math.Round(piano[0] * segno, 4),
                    Math.Round(piano[1] * segno, 4),
                    Math.Round(piano[2] * segno, 4),
                };
            }
            catch { return null; }
        }
    }
}
