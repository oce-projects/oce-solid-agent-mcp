using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LA FORMA DEL PEZZO, DETTA IN UNA FRASE (voce V3).
    ///
    /// Lo stato elenca fino a sessanta facce con area, punto e normale. Sono i
    /// numeri giusti, ed e' un elenco: per sapere che il pezzo e' «una scatola
    /// 120x80x50 svuotata 4, con quattro fori Ø12» bisogna ricavarlo. Questa
    /// classe lo ricava una volta sola, qui, e lo dice.
    ///
    /// ── PERCHE' PUO' VALERE PIU' DI UN'IMMAGINE ───────────────────────────
    ///
    ///   * **costa caratteri, non token di immagine.** Una vista sta sopra i
    ///     mille token; questa frase sta sotto i trecento CARATTERI.
    ///   * **sopravvive a un modello che non vede immagini.**
    ///   * **si puo' verificare.** Una frase o e' vera o e' falsa, e il numero
    ///     che porta si confronta col pezzo. Di una PNG non si verifica niente.
    ///   * **resta leggibile da una persona** dentro un log, sei mesi dopo.
    ///
    /// ── LA REGOLA CHE TIENE INSIEME TUTTO: NON SI INVENTA ─────────────────
    ///
    /// «Sembra una staffa» non e' una misura. Ogni parola di questa frase deve
    /// venire da un numero che sta nello stato, e **dove il numero non c'e' la
    /// frase tace** — come `positions_not_checked`, che dice «non posso
    /// dirlo» invece di tirare a indovinare.
    ///
    /// Concretamente: **il nome della famiglia si scrive solo quando un TIPO di
    /// lavorazione lo autorizza.** `Shell` dice «svuotato», `Revolution` dice
    /// «di rivoluzione»: sono cio' che il progettista ha fatto, non cio' che
    /// sembra. Per tutto il resto si danno le misure e basta.
    ///
    /// Non si dice «piastra» perche' una quota e' piccola rispetto alle altre:
    /// sarebbe una soglia inventata da me, e il giorno che sbaglia — su un
    /// pezzo che piastra non e' — chi legge ci crede lo stesso, perche' la
    /// frase suona sicura come tutte le altre. **Una frase sbagliata e' peggio
    /// di nessuna frase.**
    ///
    /// ── E CIO' CHE LA MISURA V2 HA GIA' DETTO ────────────────────────────
    ///
    /// La voce V3 dava per scontato che servisse a far CAPIRE la forma. La
    /// misura di V2 ha mostrato che col solo stato testuale un modello prende
    /// la famiglia **5 volte su 5**: capire non era il problema. Quello che il
    /// solo stato faceva male erano i numeri DERIVATI — tre interassi sbagliati
    /// con confidenza piena.
    ///
    /// Quindi questa frase serve soprattutto a **dire i numeri derivati una
    /// volta sola e bene**, invece di lasciarli ricavare a ognuno per conto suo:
    /// i centri dei fori, il passo, lo spessore di parete. E' compressione con
    /// dentro un conto gia' fatto.
    /// </summary>
    public static class FormaAParole
    {
        private static readonly CultureInfo Punto = CultureInfo.InvariantCulture;

        /// <summary>
        /// La frase. Null quando non c'e' abbastanza per dirne una: **meglio
        /// niente che una frase vuota che sembra una descrizione**.
        /// </summary>
        public static string Descrivi(Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            if (!Vero(stato, "solid_body")) return null;

            var pezzi = new List<string>();

            // ── 1. QUANTO E' GRANDE, e quanto e' pieno ──────────────────
            //
            // Il rapporto fra volume e ingombro dice una cosa vera e utile
            // senza classificare niente: 98% e' un pieno, 23% e' qualcosa di
            // cavo o di sottile. E' un numero, non un aggettivo.
            double[] ing = Vettore(stato, "bounding_box_mm");
            double volume = Numero(stato, "volume_mm3");
            if (ing != null && ing.Length == 3)
            {
                string misure = Mm(ing[0]) + " x " + Mm(ing[1]) + " x " + Mm(ing[2]) + " mm";
                double scatola = ing[0] * ing[1] * ing[2];
                if (volume > 0 && scatola > 0)
                {
                    int pieno = (int)Math.Round(volume / scatola * 100.0);
                    pezzi.Add("Ingombro " + misure + ", volume " + Mm(volume) +
                              " mm3 (il " + pieno + "% dell'ingombro)");
                }
                else pezzi.Add("Ingombro " + misure);
            }
            else if (volume > 0) pezzi.Add("Volume " + Mm(volume) + " mm3");

            int corpi = (int)Numero(stato, "bodies_total");
            if (corpi > 1) pezzi.Add(corpi + " corpi separati");

            // ── 2. COSA CI HANNO FATTO SOPRA ────────────────────────────
            //
            // Solo tipi che il progettista ha scelto: `Shell` e `Revolution`
            // dicono cos'e' il pezzo perche' cos'e' l'ha deciso lui.
            var feature = Voci(stato, "feature").ToList();
            if (feature.Any(f => Testo(f, "kind") == "Revolution"))
            {
                pezzi.Add("pezzo di rivoluzione");
                string profilo = ProfiloTornito(stato, ing, volume);
                if (profilo != null) pezzi.Add(profilo);
            }

            var guscio = feature.FirstOrDefault(f => Testo(f, "kind") == "Shell");
            if (guscio != null)
            {
                double parete = PrimaQuota(guscio);
                pezzi.Add(parete > 0
                    ? "svuotato, parete " + Mm(parete) + " mm"
                    : "svuotato");
            }

            // ── 3. I FORI, col centro gia' calcolato ────────────────────
            pezzi.AddRange(Fori(stato));

            // ── 4. QUELLO CHE NON SI E' POTUTO DIRE ─────────────────────
            //
            // Un elenco troncato o delle lavorazioni in errore cambiano il
            // senso di tutta la frase, e tacerle la renderebbe una bugia per
            // omissione: chi legge crederebbe di avere il pezzo intero.
            int fuori = (int)Numero(stato, "feature_not_listed");
            if (fuori > 0)
                pezzi.Add("piu' altre " + fuori + " lavorazioni che non sono nell'elenco");
            int inErrore = (int)Numero(stato, "features_in_error");
            if (inErrore > 0)
                pezzi.Add(inErrore + " lavorazion" + (inErrore == 1 ? "e" : "i") +
                          " IN ERRORE: il pezzo non e' quello che l'albero dice");

            if (pezzi.Count == 0) return null;
            return string.Join(", ", pezzi) + ".";
        }

        /// <summary>
        /// IL PROFILO DI UN PEZZO TORNITO: «Ø30 per 40, Ø20 per 30, Ø34 per 20».
        /// Null quando non si puo' dire — ed e' il caso piu' importante.
        ///
        /// ── PERCHE' ESISTE (voce V7) ──────────────────────────────────────
        ///
        /// Nella misura di V3, quattro pezzi su cinque si sono ridisegnati
        /// dalla sola frase col volume **esatto**. Il quinto, un albero a
        /// gradini, e' uscito al 3,8% — e il modello aveva detto in anticipo
        /// perche': «non dice quanti gradini ha il profilo, dove cade lo
        /// scalino, ne' i diametri intermedi». La frase diceva «pezzo di
        /// rivoluzione» e nient'altro, cioe' il nome della famiglia senza una
        /// quota.
        ///
        /// ── COME SI RICAVA, E DA COSA ────────────────────────────────────
        ///
        /// Le facce cilindriche di un tornito portano `diameter_mm`, `axis` e
        /// l'area. La **lunghezza** di un tratto non c'e' da nessuna parte, ma
        /// un cilindro di diametro d e lunghezza L ha area **π·d·L**: la
        /// lunghezza e' `area / (π·d)`.
        ///
        /// L'ORDINE viene dal `point_mm`, proiettato sull'asse. E' un punto
        /// sulla superficie e non sull'asse — vedi E16 — ma per ORDINARE
        /// basta: i tratti di un tornito non si sovrappongono lungo l'asse,
        /// quindi qualunque punto dentro un tratto lo colloca giusto. Per una
        /// posizione non basterebbe, e infatti non se ne danno.
        ///
        /// ── E LA FORMULA E' FRAGILE, QUINDI SI CONTROLLA ─────────────────
        ///
        /// `area / (π·d)` vale su un cilindro **intero**. Se la faccia e'
        /// tagliata da un piatto, da una cava o da un foro trasversale, l'area
        /// e' minore e la lunghezza esce **corta** — e nessuno se ne
        /// accorgerebbe, perche' il numero ha la forma giusta. Un pezzo con un
        /// foro passante otterrebbe un profilo plausibile e falso: e'
        /// esattamente la corona di bulloni inventata, con un'altra formula.
        ///
        /// Quindi il profilo si dichiara **solo se si ricostruisce**. Due
        /// controlli, e devono tornare tutti e due:
        ///
        ///   1. la somma delle lunghezze fa l'ingombro lungo l'asse;
        ///   2. il volume ricostruito — Σ π(d/2)²·L — fa il volume vero.
        ///
        /// Il secondo e' quello forte: prende i fori trasversali, le cave, e un
        /// pezzo cavo (dove una faccia cilindrica e' interna e toglie materiale
        /// invece di aggiungerlo). Se non torna, **si tace**: resta «pezzo di
        /// rivoluzione», che e' poco ma e' vero.
        /// </summary>
        private static string ProfiloTornito(
            Dictionary<string, object> stato, double[] ingombro, double volume)
        {
            if (ingombro == null || ingombro.Length != 3 || volume <= 0) return null;

            // ── l'asse, e dev'essere uno solo ────────────────────────────
            int lungoQuale = -1;
            var tratti = new List<double[]>();   // {posizione, diametro, lunghezza}

            foreach (var f in Voci(stato, "faces"))
            {
                if (Vero(f, "planar")) continue;
                double d = Numero(f, "diameter_mm");
                double area = Numero(f, "area_mm2");
                var asse = Vettore(f, "axis");
                var punto = Vettore(f, "point_mm");
                if (d <= 0 || area <= 0 || asse == null || punto == null) return null;
                if (asse.Length != 3 || punto.Length != 3) return null;

                // L'asse dev'essere allineato a X, Y o Z: un tornito storto
                // rispetto agli assi del modello non si sa raccontare, e
                // provarci vorrebbe dire indovinare.
                int quale = -1;
                for (int i = 0; i < 3; i++)
                    if (Math.Abs(Math.Abs(asse[i]) - 1) < 1e-6) quale = i;
                if (quale < 0) return null;
                if (lungoQuale < 0) lungoQuale = quale;
                else if (lungoQuale != quale) return null;   // due assi: non e' un tornito semplice

                tratti.Add(new[] { punto[quale], d, area / (Math.PI * d) });
            }

            if (lungoQuale < 0 || tratti.Count == 0) return null;

            tratti.Sort((a, b) => a[0].CompareTo(b[0]));

            // ── 1. le lunghezze fanno l'ingombro? ────────────────────────
            double somma = tratti.Sum(t => t[2]);
            if (Math.Abs(somma - ingombro[lungoQuale]) > 0.05) return null;

            // ── 2. e il volume torna? ────────────────────────────────────
            double ricostruito = tratti.Sum(t => Math.PI * (t[1] / 2) * (t[1] / 2) * t[2]);
            if (Math.Abs(ricostruito - volume) > Math.Max(0.5, volume * 0.005)) return null;

            return "profilo " + string.Join(", ",
                tratti.Select(t => Diam(Mm(t[1])) + " per " + Mm(t[2]))) + " mm";
        }

        /// <summary>
        /// I fori, raggruppati per diametro, col CENTRO — non col punto sulla
        /// superficie.
        ///
        /// **E' il conto che due modelli hanno sbagliato** (voce E16): dal
        /// `point_mm` di una faccia cilindrica, che sta sulla superficie, un
        /// interasse esce corto di un raggio per foro. Una corona Ø90 letta
        /// Ø79. Qui il conto si fa una volta, con `center_mm`, e chi legge non
        /// lo rifa'.
        ///
        /// Si contano solo le facce cilindriche che vengono da una lavorazione
        /// (`from_feature`): sono i fori e le tasche tonde. I raccordi
        /// d'angolo di un profilo sono cilindrici anche loro e fori non sono.
        /// </summary>
        private static IEnumerable<string> Fori(Dictionary<string, object> stato)
        {
            var perDiametro = new Dictionary<string, List<double[]>>();
            foreach (var f in Voci(stato, "faces"))
            {
                if (Vero(f, "planar")) continue;
                double d = Numero(f, "diameter_mm");
                if (d <= 0) continue;
                if (string.IsNullOrEmpty(Testo(f, "from_feature"))) continue;
                var centro = Vettore(f, "center_mm");
                if (centro == null) continue;

                string chiave = Mm(d);
                if (!perDiametro.ContainsKey(chiave)) perDiametro[chiave] = new List<double[]>();
                perDiametro[chiave].Add(centro);
            }

            foreach (var g in perDiametro.OrderByDescending(x => Convert.ToDouble(x.Key, Punto)))
            {
                int quanti = g.Value.Count;
                string voce = quanti + (quanti == 1 ? " foro" : " fori") + " " + Diam(g.Key);

                // SE STANNO SU UNA CIRCONFERENZA, LO SI DICE.
                // E' il dato che serve a chi deve imbullonare, ed e' proprio
                // quello che il solo elenco delle facce faceva sbagliare.
                double diametroCorona;
                if (quanti >= 3 && SuUnaCirconferenza(g.Value, out diametroCorona))
                    voce += " su una circonferenza " + Diam(Mm(diametroCorona)) +
                            " a passo " + Mm(360.0 / quanti) + " gradi";
                else if (quanti <= 6)
                    voce += (quanti == 1 ? " (centro " : " (centri ") + string.Join("; ",
                        g.Value.Select(c => Mm(c[0]) + "," + Mm(c[1]) + "," + Mm(c[2]))) + ")";

                yield return voce;
            }
        }

        /// <summary>
        /// I fori stanno su una circonferenza di bulloneria, a passo regolare?
        ///
        /// ── TRE CONDIZIONI, E LE PRIME DUE DA SOLE MENTONO ────────────────
        ///
        /// La prima scrittura ne chiedeva una sola — «tutti alla stessa
        /// distanza da un asse» — e sulla **prima frase generata in assoluto**
        /// ha dichiarato che i quattro fori di una piastra rettangolare stanno
        /// «su una circonferenza Ø90 a passo 90 gradi». Stanno ai vertici di un
        /// rettangolo **160 x 90**.
        ///
        /// Equidistanti da un asse vuol dire su un CILINDRO, non su una
        /// circonferenza: mancava che stessero tutti sullo stesso piano. E
        /// stare su una circonferenza non vuol dire starci **a passo
        /// regolare**: i vertici di un rettangolo 160x90 stanno su un cerchio,
        /// e i loro angoli sono 29,4° e 150,6° — non 90° l'uno dall'altro.
        ///
        /// Quindi:
        ///
        ///   1. **stesso piano** — la coordinata lungo l'asse e' la stessa;
        ///   2. **stesso raggio** dall'asse;
        ///   3. **passo angolare regolare**, che e' la condizione che il
        ///      rettangolo non rispetta e che nessuna delle altre due cattura.
        ///
        /// Se una qualunque non torna, la frase **non dice niente** e lascia i
        /// centri: chi legge fa il conto suo su numeri veri, invece di credere
        /// a un conto sbagliato fatto da me. Una corona dichiarata dove non c'e'
        /// e' esattamente il difetto che questa classe esiste per non avere —
        /// ed e' lo stesso di E16, commesso dal codice scritto per ripararlo.
        /// </summary>
        private static bool SuUnaCirconferenza(List<double[]> centri, out double diametro)
        {
            diametro = 0;
            if (centri.Count < 3) return false;

            for (int fisso = 0; fisso < 3; fisso++)
            {
                int a = (fisso + 1) % 3, b = (fisso + 2) % 3;

                // 1. stesso piano
                double quota = centri[0][fisso];
                if (!centri.All(c => Math.Abs(c[fisso] - quota) < 0.05)) continue;

                // 2. stesso raggio
                double raggio = Math.Sqrt(centri[0][a] * centri[0][a] + centri[0][b] * centri[0][b]);
                if (raggio < 0.05) continue;
                if (!centri.All(c =>
                        Math.Abs(Math.Sqrt(c[a] * c[a] + c[b] * c[b]) - raggio) < 0.05))
                    continue;

                // 3. passo angolare regolare
                if (!PassoRegolare(centri, a, b)) continue;

                diametro = raggio * 2;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gli angoli sono equidistanti fra loro?
        ///
        /// Si ordinano e si guardano i salti: devono essere tutti uguali a
        /// 360/n. Mezzo grado di tolleranza — un foro messo a mano su un
        /// disegno vero non e' mai esatto al centesimo, e mezzo grado su una
        /// corona Ø90 sono 0,4 mm.
        /// </summary>
        private static bool PassoRegolare(List<double[]> centri, int a, int b)
        {
            var angoli = centri
                .Select(c => Math.Atan2(c[b], c[a]) * 180.0 / Math.PI)
                .Select(g => g < 0 ? g + 360 : g)
                .OrderBy(g => g)
                .ToList();

            double atteso = 360.0 / angoli.Count;
            for (int i = 0; i < angoli.Count; i++)
            {
                double salto = (i + 1 < angoli.Count)
                    ? angoli[i + 1] - angoli[i]
                    : angoli[0] + 360 - angoli[i];
                if (Math.Abs(salto - atteso) > 0.5) return false;
            }
            return true;
        }

        // ── letture dallo stato, tutte tolleranti ────────────────────────

        private static IEnumerable<Dictionary<string, object>> Voci(
            Dictionary<string, object> stato, string chiave)
        {
            object g;
            if (stato == null || !stato.TryGetValue(chiave, out g) || g == null) yield break;
            var elenco = g as System.Collections.IEnumerable;
            if (elenco == null) yield break;
            foreach (var v in elenco)
            {
                var voce = v as Dictionary<string, object>;
                if (voce != null) yield return voce;
            }
        }

        private static double PrimaQuota(Dictionary<string, object> feature)
        {
            foreach (var q in Voci(feature, "dimensions"))
            {
                double mm = Numero(q, "mm");
                if (mm > 0) return mm;
            }
            return 0;
        }

        private static double Numero(Dictionary<string, object> d, string chiave)
        {
            object v;
            if (d == null || !d.TryGetValue(chiave, out v) || v == null) return 0;
            try { return Convert.ToDouble(v, Punto); } catch { return 0; }
        }

        private static string Testo(Dictionary<string, object> d, string chiave)
        {
            object v;
            if (d == null || !d.TryGetValue(chiave, out v) || v == null) return null;
            return Convert.ToString(v);
        }

        private static bool Vero(Dictionary<string, object> d, string chiave)
        {
            object v;
            if (d == null || !d.TryGetValue(chiave, out v) || v == null) return false;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }

        private static double[] Vettore(Dictionary<string, object> d, string chiave)
        {
            object v;
            if (d == null || !d.TryGetValue(chiave, out v) || v == null) return null;
            var pronto = v as double[];
            if (pronto != null) return pronto;
            var elenco = v as System.Collections.IEnumerable;
            if (elenco == null) return null;
            var fuori = new List<double>();
            foreach (var x in elenco)
            {
                try { fuori.Add(Convert.ToDouble(x, Punto)); } catch { return null; }
            }
            return fuori.Count == 0 ? null : fuori.ToArray();
        }

        /// <summary>Un numero come lo scriverebbe un disegnatore: senza zeri inutili.</summary>
        private static string Mm(double v)
        {
            return v.ToString("0.##", Punto);
        }

        private static string Diam(string misura)
        {
            return "Ø" + misura;
        }
    }
}
