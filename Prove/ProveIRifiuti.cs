using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// UN «NO» SENZA UNA STRADA COSTA UN TURNO A CHI LO LEGGE (voce Z-G4).
    ///
    /// ── IL FATTO ─────────────────────────────────────────────────────────
    ///
    /// «Il profilo non ha la misura 40X40X3. Nel file quella misura si scrive
    /// 40x40x3» ha corretto il modello **in un turno**. «Il profilo non ha
    /// quella misura» lo avrebbe mandato a indovinare.
    ///
    /// La differenza non e' cortesia: e' il numero di giri che serve per
    /// arrivare al pezzo. Un rifiuto che nomina l'alternativa e' una
    /// correzione; uno che dice solo no e' un ostacolo.
    ///
    /// ── LA MISURA, E PERCHE' NON E' QUELLA DELLA VOCE ────────────────────
    ///
    /// La voce Z-G4 diceva «~162 punti di rifiuto, ~39 (24%) dicono anche cosa
    /// fare». Contati il 23/9/2026 su tutti i `.cs` della libreria: i punti
    /// sono **576** — `grep -c "Fallito("` ne trova 577, meno la definizione —
    /// e quelli che offrono una strada sono **66**, cioe' l'**11%**.
    ///
    /// Il numero della voce era una stima su un sottoinsieme; questo e' il
    /// conto intero. *Piu' punti e percentuale piu' bassa: la voce sottostimava
    /// il lavoro, non lo sopravvalutava.*
    ///
    /// ── PERCHE' UNA CRICCA E NON UNA CORREZIONE ──────────────────────────
    ///
    /// Cinquecentodieci messaggi da riscrivere sono le due giornate che la voce
    /// stima, e non si fanno in un pomeriggio. Ma **si puo' impedire che
    /// peggiori**: il numero qui sotto e' un pavimento, e ogni messaggio che
    /// impara a dire cosa fare lo alza. Un rifiuto nuovo scritto muto fa
    /// scendere la percentuale e il controllo diventa rosso.
    ///
    /// ── E IL RILEVATORE E' GENEROSO APPOSTA ──────────────────────────────
    ///
    /// Le parole che contano come «strada» sono un elenco largo — «usa»,
    /// «metti», «oppure», «si scrive»… Largo vuol dire che **sovrastima** i
    /// messaggi buoni: il vero 11% e' probabilmente piu' basso. Un rilevatore
    /// severo avrebbe prodotto un numero piu' lusinghiero da migliorare e meno
    /// vero, e *si puo' barare aggiungendo la parola «oppure»* — ma barare
    /// qui vorrebbe dire scrivere un messaggio peggiore apposta, e a quel
    /// punto il problema non e' il controllo.
    /// </summary>
    internal static class ProveIRifiuti
    {
        /// <summary>
        /// Quanti rifiuti, sui **537 che hanno un messaggio proprio**, offrono
        /// una strada. **Ottantatre il 23/9/2026, cioe' il 15%.** Puo' solo
        /// salire.
        ///
        /// ── DUE NUMERI NELLO STESSO GIORNO, E IL SECONDO NON E' UN PROGRESSO ──
        ///
        /// La prima misura diceva **66**. Poi l'elenco dei rimedi e' stato
        /// guardato accanto ai messaggi veri, e contava muto
        /// «So fare: filettato, passaggio, lamato, svasato» — che **nomina le
        /// alternative**, cioe' fa esattamente quello che la voce chiede. Con
        /// le forme che il codice usa davvero il conto e' 83.
        ///
        /// *Fra 66 e 83 non ha lavorato nessuno: e' migliorato il rilevatore.*
        /// Scriverlo qui e' il punto — un pavimento che sale senza che nessuno
        /// abbia scritto un messaggio migliore insegna a leggere quel numero
        /// come un risultato, e la volta dopo nessuno controlla.
        ///
        /// **84 dal 24/9/2026, e stavolta e' lavoro vero.** Togliendo il
        /// ripiego cablato alla libreria dei profili, `profili_saldatura` ha
        /// guadagnato un rifiuto che distingue «non so dove sia» da «non c'e'»
        /// e dice quale variabile impostare. Prima i due casi davano lo stesso
        /// messaggio, con un percorso vuoto fra virgolette.
        ///
        /// **85 dal 24/9/2026 sera (libreria 0.113.0).** `modifica_quota` ha un
        /// rifiuto nuovo — l'apertura che esce dal materiale (voce E18 c) — e
        /// dice la strada: «sposta prima l'apertura, poi cambia la quota».
        ///
        /// **87 con le operazioni nuove della stessa versione**: `flangia_base`
        /// («allunga il tratto o stringi il raggio») e `sviluppo_dxf` («salvalo
        /// prima»).
        ///
        /// **88 dal 25/9/2026 (libreria 0.114.0)**: `cordone_saldatura`
        /// riscritto su due facce, coi rifiuti nuovi che dicono quale faccia
        /// indicare invece.
        ///
        /// **90 dal 25/9/2026 (libreria 0.116.0)**: l'ancora (voce E18), coi
        /// rifiuti che dicono come ancorare l'asse mancante e a quale spigolo
        /// vivo attaccarsi invece che al bordo di uno smusso.
        ///
        /// **75 dal 25/9/2026 (libreria 0.117.0), e SCENDE senza che nessun
        /// messaggio sia peggiorato.** I rifiuti nuovi di Z-C8 avevano fatto
        /// salire il conto a 92, e guardandoli uno per uno nessuno dei due
        /// scatti era una strada: «prima di scrivere» contiene «scriv», e un
        /// rifiuto chiuso sulla sua riga si prendeva il «metti» del rifiuto
        /// SUCCESSIVO, perche' il rilevatore leggeva sempre la riga dopo.
        /// Corretto il rilevatore (si ferma a fine messaggio), il conto vero e'
        /// 75: quindici «strade» erano in prestito dal vicino. Misurato con
        /// una replica del rilevatore su HEAD e sull'albero: 75 e 75.
        /// *E' la storia del 66 e dell'83 al contrario: un rilevatore piu'
        /// onesto abbassa il numero, e va scritto per la stessa ragione.*
        ///
        /// **76 dal 26/9/2026 (libreria 0.121.0):** il rifiuto nuovo di
        /// `modifica_quota` sullo smusso o il raccordo che non ci sta piu' dice
        /// la strada («cambia o togli prima lo smusso o il raccordo, poi la
        /// quota»).
        ///
        /// **77 dal 26/9/2026 (libreria 0.122.0):** `flangia_spigolo` rifiuta
        /// una flangia senza niente di dritto dicendo fin dove si puo' scendere.
        /// **78, stessa libreria:** `orlo` rifiuta un aperto senza gioco dicendo
        /// che senza gioco si chiama «chiuso».
        /// **79 dalla libreria 0.123.0:** `piega_da_schizzo` rifiuta i 180 gradi
        /// nominando «orlo».
        /// **81 dalla libreria 0.124.0:** `tabella_fori_in_tavola` dice su quale
        /// vista va (V2) e cosa fare della doppia quotatura; `quota_in_tavola`
        /// nomina la tabella fori fra le cose gia' quotate.
        /// **89, stessa libreria:** le tre annotazioni (rugosita', datum,
        /// tolleranza geometrica) dicono cosa fare per ogni rifiuto — la lettera
        /// libera, togliere il riferimento, mettere il datum prima.
        /// </summary>
        private const int Pavimento = 89;

        /// <summary>
        /// Le aperture con cui questo codice dice no.
        /// </summary>
        private static readonly string[] Aperture = { "Fallito(", "Rifiuta(", "NonPosso(" };

        /// <summary>
        /// Le parole con cui un messaggio indica una strada. Elenco volutamente
        /// largo: vedi il commento in testa.
        /// </summary>
        private static readonly string[] Rimedi =
        {
            "usa ", "usa«", "metti ", "chiedi ", "aggiungi ", "prova ", "togli",
            "salvalo", "scriv", "riattiva", "si fa con", "passa ",
            "indica ", "per farlo", "se vuoi", "chiamala", "rifai", "apri ",
            "fai il check-out", "con «", "oppure",

            // AGGIUNTE IL 23/9/2026, GUARDANDO I MESSAGGI VERI. La prima
            // stesura dell'elenco era scritta a tavolino e contava muto
            // «So fare: filettato, passaggio, lamato, svasato», che **nomina
            // le alternative** ed e' esattamente cio' che la voce chiede.
            //
            // Il numero che si alza qui si alza perche' il RILEVATORE e'
            // migliorato, non i messaggi: va detto, altrimenti il prossimo
            // che legge il pavimento crede che qualcuno abbia lavorato.
            "so fare", "per esempio", "dimmi", "dimmela", "puoi ",
            "ti serve", "va scritt", "si indica",
        };

        internal static void Tutte()
        {
            Gruppo("i rifiuti che dicono anche cosa fare (Z-G4)");

            var cartella = Path.Combine(Radice(), "Riscontro.Disegno");
            if (!Directory.Exists(cartella))
            {
                Segna("la cartella della libreria c'e'", false, "manca " + cartella);
                return;
            }

            int totale = 0, conStrada = 0, conTesto = 0, inoltrano = 0;
            var perFile = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var f in Directory.GetFiles(cartella, "*.cs", SearchOption.AllDirectories))
            {
                var righe = File.ReadAllLines(f);
                for (int i = 0; i < righe.Length; i++)
                {
                    var r = righe[i];
                    var nudo = r.TrimStart();
                    if (nudo.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!Aperture.Any(a => r.Contains(a))) continue;

                    // IL MESSAGGIO CONTINUA SULLE RIGHE DOPO. Fermarsi alla
                    // prima vorrebbe dire giudicare mute quasi tutte: qui i
                    // messaggi lunghi sono la norma, e sono proprio quelli che
                    // la strada ce l'hanno.
                    //
                    // MA NON OLTRE LA FINE DEL SUO. Fino al 25/9/2026 il ciclo
                    // leggeva sempre almeno la riga dopo, anche quando il
                    // messaggio si chiudeva sulla sua: un rifiuto breve si
                    // prendeva il «metti» del rifiuto successivo, e il
                    // pavimento e' salito di uno senza che nessuno scrivesse un
                    // messaggio migliore.
                    var testo = r;
                    if (!r.Contains(");"))
                    for (int j = i + 1; j < righe.Length && j < i + 12; j++)
                    {
                        if (righe[j].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                        testo += " " + righe[j];
                        if (righe[j].Contains(");")) break;
                    }

                    totale++;

                    // ── TRE SPECIE, NON DUE ──────────────────────────────
                    //
                    // `return Fallito(errore)` non e' un rifiuto muto: e' un
                    // rifiuto che **inoltra** un messaggio scritto altrove.
                    // Giudicarlo qui vorrebbe dire contarlo due volte — una
                    // dove il testo c'e' e una dove passa — e gonfiare il
                    // problema. Un punto senza nemmeno una virgoletta non ha
                    // un messaggio da giudicare.
                    if (testo.IndexOf('"') < 0) { inoltrano++; continue; }

                    conTesto++;
                    var basso = testo.ToLowerInvariant();
                    if (Rimedi.Any(x => basso.Contains(x))) conStrada++;
                    else
                    {
                        var nome = Path.GetFileName(f);
                        int q; perFile.TryGetValue(nome, out q);
                        perFile[nome] = q + 1;
                    }
                }
            }

            Segna("i punti di rifiuto si contano", totale > 0,
                  totale + " punti: " + conTesto + " con un messaggio proprio, " +
                  inoltrano + " che ne inoltrano uno scritto altrove");

            // IL PAVIMENTO SALE E NON SCENDE.
            Segna("i rifiuti con una strada non sono diminuiti",
                  conStrada >= Pavimento,
                  conStrada + " su " + conTesto + " (" + (conTesto == 0 ? 0 : conStrada * 100 / conTesto) +
                  "%), pavimento " + Pavimento);

            Segna("e il pavimento e' quello vero", conStrada == Pavimento,
                  conStrada == Pavimento
                      ? "ottantaquattro, come misurati"
                      : "sono " + conStrada + ": alza il pavimento a questo numero");

            // DOVE STANNO I MUTI, perche' un numero senza un indirizzo non fa
            // cominciare nessuno.
            var peggiori = perFile.OrderByDescending(x => x.Value).Take(5)
                .Select(x => x.Key + " (" + x.Value + ")");
            Segna("si sa da dove cominciare", perFile.Count > 0 || conStrada == totale,
                  perFile.Count == 0 ? "nessun rifiuto muto"
                                     : "i piu' muti: " + string.Join(", ", peggiori));
        }
    }
}
