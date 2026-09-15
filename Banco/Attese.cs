using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProvaSaldatura
{
    /// <summary>
    /// IL VALUTATORE DELLE ATTESE E I SELETTORI, SENZA SOLIDWORKS (voce F7).
    ///
    /// â”€â”€ PERCHE' STA IN UNA CLASSE SUA â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    ///
    /// Questo file lo compilano DUE progetti: il banco, che apre il CAD, e
    /// `Prove.Contratti`, che gira su una macchina qualunque in pochi secondi e
    /// **non referenzia l'interop di SOLIDWORKS**. Finche' il valutatore stava
    /// in una classe parziale che tocca `IModelDoc2`, il secondo progetto non
    /// poteva nemmeno compilare.
    ///
    /// E deve poter girare li': **se e' rotto il valutatore, tutta la suite col
    /// CAD diventa verde gratis.** E' l'ultimo posto che ci si puo' permettere
    /// di non provare.
    ///
    /// Le misure che vengono dal DOCUMENTO â€” volume, corpi, ingombro â€” arrivano
    /// da un `Func&lt;string, object&gt;`: chi ha il documento in mano lo passa, chi
    /// non ce l'ha passa null e prova tutto il resto.
    /// </summary>
    internal static class Attese
    {
        /// <summary>
        /// Quello che una prova non ha potuto misurare: un campo assente, un
        /// selettore che non risolve, un caso scritto male (voce F4).
        /// </summary>
        internal sealed class NonHoPotutoMisurare : Exception
        {
            public NonHoPotutoMisurare(string messaggio) : base(messaggio) { }
        }

        /// <summary>
        /// Dove sta la libreria dei profili saldati su QUESTA installazione.
        /// Si cambia con `PROFILI_SALDATURA`, come nel banco.
        /// </summary>
        internal static string Profili
        {
            get
            {
                var suo = Environment.GetEnvironmentVariable("PROFILI_SALDATURA");
                return string.IsNullOrWhiteSpace(suo)
                    ? @"<impostazioni SOLIDWORKS>\ELEMENTI SALDATURA\NORMATI"
                    : suo;
            }
        }

        /// <summary>Un testo lungo, accorciato per stare su una riga di esito.</summary>
        internal static string Breve(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(nessun motivo)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 110 ? s.Substring(0, 110) + "..." : s;
        }

        // ==================================================================
        // LE ATTESE
        // ==================================================================
        internal static bool Combacia(
            Dictionary<string, object> attesa, Func<string, object> dalDocumento,
            Dictionary<string, object> stato, Dictionary<string, object> misure,
            out string dettaglio)
        {
            dettaglio = "";
            string cosa = Testo(attesa, "cosa");
            if (cosa == null) throw new NonHoPotutoMisurare("un'attesa senza «cosa»");
            if (!attesa.ContainsKey("uguale")) throw new NonHoPotutoMisurare("«" + cosa + "» senza «uguale»");

            double tolleranza = 0;
            if (attesa.ContainsKey("tolleranza") && attesa["tolleranza"] != null)
            {
                tolleranza = Convert.ToDouble(attesa["tolleranza"], CultureInfo.InvariantCulture);
                // IL PERCHE' E' OBBLIGATORIO, e non e' burocrazia: una
                // tolleranza senza motivo e' il posto in cui un giorno qualcuno
                // allarghera' il numero invece di guardare il pezzo.
                if (Math.Abs(tolleranza) > 1e-12 && Testo(attesa, "perche_questa_tolleranza") == null)
                    throw new NonHoPotutoMisurare("«" + cosa + "» ha tolleranza " +
                                        tolleranza.ToString(CultureInfo.InvariantCulture) +
                                        " e non dice «perche_questa_tolleranza»");
            }

            object misurato = Misura(cosa, dalDocumento, stato, misure);
            object atteso = attesa["uguale"];

            // Un elenco di numeri: l'ingombro, un punto, una terna.
            var attesoLista = atteso as System.Collections.IEnumerable;
            if (attesoLista != null && !(atteso is string))
            {
                var attesi = Numeri(attesoLista);
                var misurati = Numeri(misurato as System.Collections.IEnumerable);
                if (misurati == null)
                {
                    dettaglio = cosa + " non e' un elenco di numeri: " + CosaEra(misurato);
                    return false;
                }
                dettaglio = cosa + " " + Insieme(misurati) + " (atteso " + Insieme(attesi) +
                            (tolleranza > 0 ? " ±" + tolleranza.ToString(CultureInfo.InvariantCulture) : "") + ")";
                if (attesi.Length != misurati.Length) return false;
                for (int i = 0; i < attesi.Length; i++)
                    if (Math.Abs(attesi[i] - misurati[i]) > tolleranza) return false;
                return true;
            }

            // Un numero.
            double numeroAtteso;
            if (atteso != null && !(atteso is string) && !(atteso is bool) &&
                double.TryParse(Convert.ToString(atteso, CultureInfo.InvariantCulture),
                                NumberStyles.Any, CultureInfo.InvariantCulture, out numeroAtteso))
            {
                double numeroMisurato;
                if (misurato == null ||
                    !double.TryParse(Convert.ToString(misurato, CultureInfo.InvariantCulture),
                                     NumberStyles.Any, CultureInfo.InvariantCulture, out numeroMisurato))
                {
                    dettaglio = cosa + " non e' un numero: " + CosaEra(misurato);
                    return false;
                }
                dettaglio = string.Format(CultureInfo.InvariantCulture,
                    "{0} {1:0.###} (atteso {2:0.###}{3})", cosa, numeroMisurato, numeroAtteso,
                    tolleranza > 0 ? " ±" + tolleranza.ToString(CultureInfo.InvariantCulture) : "");
                return Math.Abs(numeroMisurato - numeroAtteso) <= tolleranza;
            }

            // Tutto il resto si confronta come testo: «materiale», un booleano.
            string a = Convert.ToString(atteso, CultureInfo.InvariantCulture);
            string m = Convert.ToString(misurato, CultureInfo.InvariantCulture);
            dettaglio = cosa + " «" + m + "» (atteso «" + a + "»)";
            return string.Equals(a, m, StringComparison.Ordinal);
        }

        /// <summary>
        /// Da dove si legge un `cosa`. Tre sorgenti, e il prefisso lo dice:
        /// niente = quello che misura il banco per conto suo, `stato.` = lo
        /// stato dell'add-in, `misure.` = quello che l'operazione ha riportato.
        /// </summary>
        internal static object Misura(
            string cosa, Func<string, object> dalDocumento,
            Dictionary<string, object> stato, Dictionary<string, object> misure)
        {
            // LE TRE MISURE DEL DOCUMENTO ARRIVANO DA FUORI, e chi non ha un
            // documento in mano passa null: e' quello che rende questo file
            // compilabile dove SOLIDWORKS non c'e' (voce F7).
            if (cosa == "volume_mm3" || cosa == "corpi" || cosa == "ingombro_mm" ||
                cosa == "centri_x_mm" || cosa == "centri_y_mm" || cosa == "centri_z_mm" ||
                cosa == "diametri_mm" || cosa == "centro_massa_mm")
            {
                if (dalDocumento == null)
                    throw new NonHoPotutoMisurare(
                        "«" + cosa + "» si misura sul documento, e qui non ce n'e' uno");
                return dalDocumento(cosa);
            }
            if (cosa.StartsWith("stato.", StringComparison.Ordinal))
                return Dentro(stato, cosa.Substring(6), cosa);
            if (cosa.StartsWith("misure.", StringComparison.Ordinal))
                return Dentro(misure, cosa.Substring(7), cosa);
            throw new NonHoPotutoMisurare(
                "non so misurare «" + cosa + "»: i nomi sono volume_mm3, corpi, " +
                "ingombro_mm, centri_x_mm, centri_y_mm, centri_z_mm, diametri_mm, " +
                "centro_massa_mm, stato.… e misure.…");
        }

        /// <summary>
        /// Un percorso dentro un dizionario: `facce[0].area_mm2`.
        /// Null quando il percorso non porta da nessuna parte — e chi chiama lo
        /// fa diventare un rosso, perche' un campo assente non e' uno zero.
        /// </summary>
        internal static object Dentro(object radice, string percorso, string nomeIntero)
        {
            object qui = radice;
            foreach (var pezzo in percorso.Split('.'))
            {
                string chiave = pezzo;
                int indice = -1;
                int quadra = pezzo.IndexOf('[');
                if (quadra >= 0 && pezzo.EndsWith("]", StringComparison.Ordinal))
                {
                    chiave = pezzo.Substring(0, quadra);
                    int.TryParse(pezzo.Substring(quadra + 1, pezzo.Length - quadra - 2), out indice);
                }

                var mappa = qui as Dictionary<string, object>;
                if (mappa == null)
                    throw new NonHoPotutoMisurare(
                        "campo assente: " + nomeIntero + "; «" + chiave +
                        "» andrebbe cercato dentro qualcosa che non e' un oggetto");

                // LA CHIAVE CHE NON C'E' E UN VALORE NULL NON SONO LA STESSA
                // COSA, ed e' tutta la voce F4. `materiale: null` vuol dire
                // «quel posto esiste ed e' vuoto»; una chiave che manca vuol
                // dire che il caso sta leggendo un campo che non esiste piu' —
                // e un `null` confrontato con un `null` passerebbe.
                if (!mappa.ContainsKey(chiave))
                    throw new NonHoPotutoMisurare(
                        "campo assente: " + nomeIntero + "; ci sono: " +
                        string.Join(", ", mappa.Keys.OrderBy(k => k, StringComparer.Ordinal)));
                qui = mappa[chiave];

                if (indice >= 0)
                {
                    var lista = qui as System.Collections.IList;
                    if (lista == null)
                        throw new NonHoPotutoMisurare(
                            "campo assente: " + nomeIntero + "; «" + chiave + "» non e' un elenco");
                    if (indice >= lista.Count)
                        throw new NonHoPotutoMisurare(
                            "campo assente: " + nomeIntero + "; «" + chiave + "» ha " +
                            lista.Count + " voci e il caso chiede la numero " + indice);
                    qui = lista[indice];
                }
            }
            return qui;
        }

        // ==================================================================
        // I SELETTORI
        // ==================================================================
        internal static object Risolvi(object grezzo, Dictionary<string, object> stato)
        {
            var mappa = grezzo as Dictionary<string, object>;
            if (mappa != null)
            {
                var fuori = new Dictionary<string, object>();
                foreach (var v in mappa) fuori[v.Key] = Risolvi(v.Value, stato);
                return fuori;
            }
            var lista = grezzo as System.Collections.IList;
            if (lista != null)
            {
                var fuori = new List<object>();
                foreach (var v in lista) fuori.Add(Risolvi(v, stato));
                return fuori;
            }
            var testo = grezzo as string;
            if (testo != null && testo.StartsWith("@", StringComparison.Ordinal))
                return Seleziona(testo, stato);

            // I SEGNAPOSTO: un caso che nomina un percorso di QUESTA macchina
            // non e' una prova, e' un ricordo. La libreria dei profili saldati
            // sta dove la mette ogni installazione, e il caso dice «{PROFILI}»
            // invece di sapere dov'e'.
            if (testo != null && testo.Contains("{PROFILI}"))
                return testo.Replace("{PROFILI}", Profili);
            return grezzo;
        }

        internal static string Seleziona(string espressione, Dictionary<string, object> stato)
        {
            int apre = espressione.IndexOf('(');
            if (apre < 0 || !espressione.EndsWith(")", StringComparison.Ordinal))
                throw new NonHoPotutoMisurare("selettore storto: «" + espressione + "»");
            string tipo = espressione.Substring(1, apre - 1);
            var condizioni = Condizioni(espressione.Substring(apre + 1, espressione.Length - apre - 2));

            switch (tipo)
            {
                // `n=` VALE PER TUTTI, non solo per le feature: fra quattro
                // spigoli lunghi uguali non esiste nessun criterio, e chi
                // scrive il caso l'ordine puo' deciderlo — purche' lo DICA.
                // Senza `n=` l'ambiguita' resta un rifiuto.
                case "faccia":  return UnoSolo(espressione, "facce", stato, condizioni, "uid", Ennesimo(espressione, condizioni));
                case "spigolo": return UnoSolo(espressione, "spigoli", stato, condizioni, "uid", Ennesimo(espressione, condizioni));
                case "feature": return UnaFeature(espressione, stato, condizioni);
                case "quota":   return UnaQuota(espressione, stato, condizioni);
                default:
                    throw new NonHoPotutoMisurare("selettore sconosciuto «@" + tipo + "»: ci sono " +
                                        "@faccia, @spigolo, @feature, @quota");
            }
        }

        /// <summary>
        /// `verso=davanti, lungo~=100`: il `~` vuol dire «circa», e serve per le
        /// lunghezze, che non sono mai intere quando il pezzo e' stato ricostruito.
        /// </summary>
        internal sealed class Condizione
        {
            public string Campo;
            public string Valore;
            public bool Circa;
        }

        internal static List<Condizione> Condizioni(string dentro)
        {
            var fuori = new List<Condizione>();
            foreach (var pezzo in dentro.Split(','))
            {
                var t = pezzo.Trim();
                if (t.Length == 0) continue;
                bool circa = t.Contains("~=");
                int uguale = circa ? t.IndexOf("~=", StringComparison.Ordinal) : t.IndexOf('=');
                if (uguale < 0) throw new NonHoPotutoMisurare("condizione senza «=»: «" + t + "»");
                fuori.Add(new Condizione
                {
                    Campo = t.Substring(0, uguale).Trim(),
                    Valore = t.Substring(uguale + (circa ? 2 : 1)).Trim(),
                    Circa = circa,
                });
            }
            return fuori;
        }

        // I NOMI DEL CASO E I NOMI DELLO STATO NON SONO GLI STESSI, e la
        // traduzione sta qui e in un punto solo. `@spigolo(asse=Z)` si legge
        // bene; il campo dello stato si chiama `lungo`, che da solo vorrebbe
        // dire un'altra cosa.
        internal static string CampoDelloStato(string nomeNelCaso)
        {
            switch (nomeNelCaso)
            {
                case "asse":  return "lungo";
                case "lungo": return "lunghezza_mm";
                case "area":  return "area_mm2";
                default:      return nomeNelCaso;
            }
        }

        internal static string UnoSolo(
            string espressione, string famiglia, Dictionary<string, object> stato,
            List<Condizione> condizioni, string campoDaTornare, int? ennesimo)
        {
            var voci = (stato != null && stato.ContainsKey(famiglia)
                ? stato[famiglia] : null) as System.Collections.IList;
            if (voci == null)
                throw new NonHoPotutoMisurare("lo stato non ha «" + famiglia + "»: " + espressione);

            var trovate = new List<Dictionary<string, object>>();
            foreach (var v in voci)
            {
                var voce = v as Dictionary<string, object>;
                if (voce == null) continue;
                bool va = true;
                foreach (var c in condizioni)
                {
                    if (c.Campo == "n") continue;
                    object valore;
                    if (!voce.TryGetValue(CampoDelloStato(c.Campo), out valore) || valore == null)
                    { va = false; break; }
                    if (c.Circa)
                    {
                        double a, b;
                        if (!double.TryParse(Convert.ToString(valore, CultureInfo.InvariantCulture),
                                             NumberStyles.Any, CultureInfo.InvariantCulture, out a) ||
                            !double.TryParse(c.Valore, NumberStyles.Any,
                                             CultureInfo.InvariantCulture, out b) ||
                            Math.Abs(a - b) > 0.5)
                        { va = false; break; }
                    }
                    else if (!string.Equals(
                        Convert.ToString(valore, CultureInfo.InvariantCulture), c.Valore,
                        StringComparison.OrdinalIgnoreCase))
                    { va = false; break; }
                }
                if (va) trovate.Add(voce);
            }

            if (ennesimo.HasValue)
            {
                if (ennesimo.Value < 1 || ennesimo.Value > trovate.Count)
                    throw new NonHoPotutoMisurare(espressione + ": ne ho trovate " + trovate.Count +
                                        ", e il caso chiede la numero " + ennesimo.Value);
                return Convert.ToString(trovate[ennesimo.Value - 1][campoDaTornare]);
            }

            // ZERO O PIU' D'UNA: SI RIFIUTA, non si sceglie la prima.
            // E' il difetto silenzioso che questa voce nomina: un caso che gira
            // su una faccia diversa da quella che il nome del file promette, e
            // che magari torna lo stesso volume.
            if (trovate.Count == 0)
                throw new NonHoPotutoMisurare(espressione + ": non l'ho trovata");
            if (trovate.Count > 1)
                throw new NonHoPotutoMisurare(espressione + ": ne ho trovate " + trovate.Count +
                                    ". Sceglierne una sarebbe indovinare: aggiungi una " +
                                    "condizione, oppure «n=» se l'ordine lo decidi tu");
            return Convert.ToString(trovate[0][campoDaTornare]);
        }

        internal static string UnaFeature(
            string espressione, Dictionary<string, object> stato, List<Condizione> condizioni)
        {
            return UnoSolo(espressione, "feature", stato, condizioni, "uid",
                           Ennesimo(espressione, condizioni));
        }

        /// <summary>Il `n=` di un selettore, se c'e'.</summary>
        internal static int? Ennesimo(string espressione, List<Condizione> condizioni)
        {
            var n = condizioni.FirstOrDefault(c => c.Campo == "n");
            if (n == null) return null;
            int k;
            if (!int.TryParse(n.Valore, out k))
                throw new NonHoPotutoMisurare(espressione + ": «n» non e' un numero");
            return k;
        }

        /// <summary>
        /// `@quota(feature=Schizzo1,nome=D1)` -> il nome ESTESO della quota, come
        /// lo scrive lo stato: `D1@Schizzo1@Parte286.Part`.
        ///
        /// E' il selettore che la voce chiamava «non un lusso»:
        /// `piani/cambia_diametro.json` scrive il nome col titolo del documento
        /// dentro, e quel `Parte76` cambia a ogni giro — **quel piano oggi non
        /// si puo' rieseguire.** Risolvendolo sullo stato, il caso nomina la
        /// quota e il titolo lo mette il banco.
        /// </summary>
        internal static string UnaQuota(
            string espressione, Dictionary<string, object> stato, List<Condizione> condizioni)
        {
            string dentroA = condizioni.FirstOrDefault(c => c.Campo == "feature")?.Valore;
            string chiamata = condizioni.FirstOrDefault(c => c.Campo == "nome")?.Valore;
            if (chiamata == null) throw new NonHoPotutoMisurare(espressione + ": serve «nome»");

            var feature = (stato != null && stato.ContainsKey("feature")
                ? stato["feature"] : null) as System.Collections.IList;
            if (feature == null) throw new NonHoPotutoMisurare("lo stato non ha «feature»: " + espressione);

            var trovate = new List<string>();
            foreach (var f in feature)
            {
                var voce = f as Dictionary<string, object>;
                if (voce == null || !voce.ContainsKey("quote")) continue;
                var quote = voce["quote"] as System.Collections.IList;
                if (quote == null) continue;
                foreach (var q in quote)
                {
                    var quota = q as Dictionary<string, object>;
                    if (quota == null || !quota.ContainsKey("nome")) continue;
                    string esteso = Convert.ToString(quota["nome"]);
                    // «D1@Schizzo1@Parte286.Part» -> corto «D1», dentro «Schizzo1»
                    var pezzi = esteso.Split('@');
                    if (pezzi.Length < 2) continue;
                    if (!string.Equals(pezzi[0], chiamata, StringComparison.OrdinalIgnoreCase)) continue;
                    if (dentroA != null &&
                        !string.Equals(pezzi[1], dentroA, StringComparison.OrdinalIgnoreCase)) continue;
                    trovate.Add(esteso);
                }
            }

            if (trovate.Count == 0) throw new NonHoPotutoMisurare(espressione + ": non l'ho trovata");
            if (trovate.Count > 1)
                throw new NonHoPotutoMisurare(espressione + ": ne ho trovate " + trovate.Count +
                                    " (" + string.Join(", ", trovate) + ")");
            return trovate[0];
        }

        // ==================================================================
        // MINUTERIA
        // ==================================================================
        internal static string Testo(Dictionary<string, object> mappa, string chiave)
        {
            object v;
            if (mappa == null || !mappa.TryGetValue(chiave, out v) || v == null) return null;
            var s = Convert.ToString(v);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        internal static bool Vero(Dictionary<string, object> mappa, string chiave)
        {
            object v;
            if (mappa == null || !mappa.TryGetValue(chiave, out v) || v == null) return false;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }

        internal static List<object> Elenco(Dictionary<string, object> mappa, string chiave)
        {
            object v;
            if (mappa == null || !mappa.TryGetValue(chiave, out v) || v == null) return null;
            var lista = v as System.Collections.IList;
            if (lista == null) return null;
            var fuori = new List<object>();
            foreach (var x in lista) fuori.Add(x);
            return fuori;
        }

        internal static double[] Numeri(System.Collections.IEnumerable lista)
        {
            if (lista == null) return null;
            var fuori = new List<double>();
            foreach (var x in lista)
            {
                double d;
                if (!double.TryParse(Convert.ToString(x, CultureInfo.InvariantCulture),
                                     NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    return null;
                fuori.Add(d);
            }
            return fuori.ToArray();
        }

        internal static string Insieme(double[] n)
        {
            return "[" + string.Join(", ", n.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture))) + "]";
        }

        /// <summary>
        /// Che cos'e' quel valore, per un messaggio. Si chiama cosi' e non
        /// `Descrivi` perche' in `ProvaNuove.cs` un `Descrivi` c'e' gia': due
        /// metodi con lo stesso nome nella stessa classe parziale non
        /// compilano, ed e' la fine migliore possibile per un nome ripetuto.
        /// </summary>
        internal static string CosaEra(object x)
        {
            if (x == null) return "(non c'e')";
            return Breve(Convert.ToString(x, CultureInfo.InvariantCulture));
        }

        internal static int QuanteFeature(Dictionary<string, object> stato)
        {
            var f = (stato != null && stato.ContainsKey("feature")
                ? stato["feature"] : null) as System.Collections.IList;
            return f == null ? 0 : f.Count;
        }
    }
}
