using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LE DUE DOMANDE CHE SI FANNO PRIMA DI ORDINARE. (voce GP4)
    ///
    /// «Quanto pesa questo pezzo in alluminio» e «entra nella scatola
    /// 300x260x40». Tutte e due si calcolavano gia' dentro `StatoDocumento` —
    /// la massa con `IMassProperty`, l'ingombro con `GetExtremePoint` sei
    /// volte — ma **nessuna operazione poteva chiederle**: arrivavano
    /// nell'intero stato del documento, o non arrivavano affatto.
    ///
    /// Qui diventano due domande che si possono fare da sole, per esempio
    /// dopo una modifica per verificare solo quella.
    ///
    /// ── LE UNITA' SI DICHIARANO, SEMPRE ─────────────────────────────────
    ///
    /// `UseSystemUnits = true` si scrive ESPLICITAMENTE: senza, i valori
    /// arrivano nelle unita' del DOCUMENTO, che sul pezzo di un altro possono
    /// essere pollici e libbre — e nessuno se ne accorgerebbe, perche' 0,468
    /// e 1,03 sono tutti e due numeri credibili. Con `UseSystemUnits` la
    /// risposta e' in metri e chilogrammi, e la conversione a millimetri la
    /// facciamo noi, in un punto solo.
    ///
    /// ── LA TRAPPOLA DELLE DUE GRAFIE ────────────────────────────────────
    ///
    /// `IMassProperty` scrive **«Princip*le*»**:
    ///
    ///   IMassProperty.PrincipleMomentsOfInertia
    ///   IMassProperty.PrincipleAxesOfInertia
    ///
    /// `IMassProperty2` scrive **«Princip*al*»**:
    ///
    ///   IMassProperty2.PrincipalMomentsOfInertia
    ///   IMassProperty2.PrincipalAxesOfInertia
    ///
    /// Due grafie diverse per la stessa cosa, su due interfacce che si
    /// somigliano. Con l'interop tipizzato e' un errore di compilazione — e
    /// meno male; in late binding sarebbe un `MissingMemberException` a
    /// runtime, o peggio un `catch` che lo ingoia e una risposta senza i
    /// momenti che nessuno nota.
    ///
    /// E MISURANDOLA SI E' TROVATO DI PEGGIO: tutte e due le grafie, su un
    /// cubo di acciaio da 200.000 mm³, rispondono **tre zeri**. I momenti veri
    /// stanno in `Extension.GetMassProperties(1)`, posizioni 6, 7, 8. Un campo
    /// che si chiama «momenti principali» e risponde zero non e' un errore: e'
    /// un numero credibile, e nessun controllo lo prenderebbe. Qui non si
    /// riportano affatto.
    ///
    /// ── E LA MISURA CHE HA DECISO IL RIFIUTO ────────────────────────────
    ///
    /// Sullo stesso cubo da 200.000 mm³:
    ///
    ///   senza materiale        densita' 1000   massa 0,2 kg
    ///   con AISI 304           densita' 8000   massa 1,6 kg
    ///
    /// Senza materiale SOLIDWORKS **non fallisce**: risponde con la densita'
    /// predefinita del modello, che e' quella dell'acqua. Il numero e'
    /// credibile e sbagliato di OTTO VOLTE, e il posto in cui si scopre e' il
    /// preventivo. Percio' qui, senza materiale, la massa e' `null` con
    /// scritto perche'.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito ProprietaDiMassa(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var parte = modello as IPartDoc;
            if (parte == null)
                return Fallito("Questo documento non e' un pezzo: non ha corpi da pesare.");

            var corpi = CorpiScelti(parte, Str(a, "corpo_uid", null), out string motivo);
            if (corpi == null) return Fallito(motivo);

            // ── UNA MISURA NUOVA PER OGNI NUMERO, E NON E' PIGNOLERIA ────
            //
            // `IMassProperty` **cambia le sue risposte a seconda di che cosa
            // gli si e' chiesto prima**. Misurato il 15/9/2026 su un cubo di
            // AISI 304 da 200.000 mm³, con lo stesso oggetto:
            //
            //   letto subito `Density`                  ->  8000   (giusto)
            //   letto prima `Volume`, poi `Density`     ->     1
            //   letto prima `SurfaceArea`, poi `Density`->     1
            //   letto prima `CenterOfMass`, poi `Density`-> 8000   (indenne)
            //
            // E la massa segue la densita': 0,0002 kg invece di 1,6 — OTTOMILA
            // VOLTE piu' leggera, con volume, area e baricentro tutti giusti
            // accanto. Nessuna eccezione, nessun valore assurdo: un numero
            // credibile, e il posto in cui si scopre e' il preventivo.
            //
            // Si potrebbe scrivere «leggi prima la massa», ma un ordine di
            // righe e' un contratto che si rompe il giorno in cui qualcuno le
            // riordina — e non fallirebbe niente. Un oggetto per numero non si
            // rompe: costa quattro chiamate COM, che non le sente nessuno.
            var prendi = new Func<Func<IMassProperty, object>, string, object>((leggi, cosa) =>
            {
                try
                {
                    var mp = modello.Extension.CreateMassProperty() as IMassProperty;
                    if (mp == null) return null;
                    // ESPLICITO, non per abitudine: vedi il commento in testa.
                    mp.UseSystemUnits = true;
                    // `AddBodies` torna un booleano: senza i corpi agganciati
                    // la misura non e' del pezzo che si crede.
                    if (corpi.Count > 0 && !mp.AddBodies(corpi.ToArray())) return null;
                    return leggi(mp);
                }
                catch { return null; }
            });

            object m = prendi(p => p.Mass, "massa");
            object d = prendi(p => p.Density, "densita");
            object v = prendi(p => p.Volume, "volume");
            object s = prendi(p => p.SurfaceArea, "area");
            object c = prendi(p => p.CenterOfMass, "baricentro");

            if (m == null || d == null || v == null || s == null)
                return Fallito(
                    "SOLIDWORKS non ha dato le proprieta' di massa: o non ha agganciato i " +
                    "corpi, o la misura non e' riuscita. Non ti do numeri a meta'.");

            double massaKg = Convert.ToDouble(m);
            double densita = Convert.ToDouble(d);
            double volumeM3 = Convert.ToDouble(v);
            double areaM2 = Convert.ToDouble(s);
            var centro = Scala(c as double[], 1000.0);

            string materiale = MaterialeDelPezzo(modello);

            var misure = new Dictionary<string, object>
            {
                ["corpi"] = corpi.Count,
                ["volume_mm3"] = Math.Round(volumeM3 * 1e9, 2),
                ["area_mm2"] = Math.Round(areaM2 * 1e6, 2),
                ["centro_massa_mm"] = centro,
                // LE UNITA' NELLA RISPOSTA, non solo nei nomi dei campi. E' la
                // stessa ragione dello STL: chi legge puo' non aver mai visto
                // la descrizione dell'operazione.
                ["unita"] = "millimetri e chilogrammi; la densita' in kg/m3",
            };

            // LA MASSA SENZA MATERIALE NON E' ZERO, E' «NON SI SA».
            //
            // SOLIDWORKS risponde lo stesso, con la densita' predefinita del
            // modello — di solito 1000 kg/m3, cioe' acqua. Un pezzo d'acciaio
            // che pesa come l'acqua e' un numero credibile e sbagliato di otto
            // volte, e il posto in cui si scopre e' il preventivo.
            if (string.IsNullOrEmpty(materiale))
            {
                misure["materiale"] = null;
                misure["massa_kg"] = null;
                misure["densita_kg_m3"] = null;
                misure["perche_niente_massa"] =
                    "Al pezzo non e' stato dato nessun materiale, quindi la massa non si sa. " +
                    "SOLIDWORKS risponderebbe lo stesso usando la densita' predefinita del " +
                    "modello (di solito 1000 kg/m3, cioe' acqua): un numero credibile e " +
                    "sbagliato di otto volte su un pezzo d'acciaio. Usa «applica_materiale» " +
                    "e richiedimelo.";
            }
            else
            {
                misure["materiale"] = materiale;
                misure["massa_kg"] = Math.Round(massaKg, 4);
                misure["densita_kg_m3"] = Math.Round(densita, 2);
                // IL CONTO RIFATTO A MANO, accanto a quello di SOLIDWORKS. Se
                // i due divergono, una delle due misure e' in unita' diverse
                // da quelle che dice — ed e' il modo in cui un fattore 1000 si
                // fa vedere invece di nascondersi.
                double aMano = volumeM3 * densita;
                misure["massa_ricalcolata_kg"] = Math.Round(aMano, 4);
                misure["scarto_sul_ricalcolo"] = massaKg > 0
                    ? (object)Math.Round(Math.Abs(aMano - massaKg) / massaKg, 6)
                    : null;
            }

            // ── I MOMENTI PRINCIPALI NON CI SONO, ED E' MISURATO ──────────
            //
            // `IMassProperty.PrincipleMomentsOfInertia` risponde **tre zeri**
            // su un cubo di acciaio da 200.000 mm³ — e cosi' fa
            // `IMassProperty2.PrincipalMomentsOfInertia`, con l'altra grafia.
            // I momenti veri ci sono, ma da un'altra parte:
            // `Extension.GetMassProperties(1)` torna tredici numeri e i
            // momenti stanno nelle posizioni 6, 7 e 8 (0,001387, 0,001387,
            // 0,002667 kg·m² sullo stesso cubo).
            //
            // Tre zeri in un campo che si chiama «momenti principali» sono il
            // difetto che non da' errore: un numero credibile — un momento
            // nullo e' un momento — che nessun controllo prenderebbe. Quindi
            // NON si riportano: nessuno li ha chiesti, e riportarli sbagliati
            // e' peggio che non averli. Se un giorno servissero, la via e'
            // `GetMassProperties`, non la proprieta'.

            _log("[agente] proprieta' di massa: volume " +
                 (volumeM3 * 1e9).ToString("0.#") + " mm3" +
                 (string.IsNullOrEmpty(materiale) ? ", senza materiale" : ", " + materiale +
                  ", " + massaKg.ToString("0.###") + " kg"));
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// LA SCATOLA VERA DEL PEZZO, corpo per corpo e tutta insieme.
        ///
        /// `GetExtremePoint` sei volte, NON `GetBodyBox`. `GetBodyBox` e'
        /// dichiaratamente APPROSSIMATA per le superfici non piane — puo'
        /// essere piu' grande del pezzo — e un ingombro dichiarato piu' grande
        /// del vero fa dimensionare male tutto quello che ci sta intorno: la
        /// scatola per spedirlo, il pezzo che gli va accanto, la tasca in cui
        /// deve entrare.
        ///
        /// E' la stessa scelta gia' fatta in `StatoDocumento`, e sta scritta
        /// in tutti e due i posti perche' e' il genere di cosa che qualcuno
        /// «semplifica» leggendo solo uno dei due.
        /// </summary>
        private Esito IngombroCorpo(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var parte = modello as IPartDoc;
            if (parte == null)
                return Fallito("Questo documento non e' un pezzo: non ha corpi da misurare.");

            var corpi = CorpiScelti(parte, Str(a, "corpo_uid", null), out string motivo);
            if (corpi == null) return Fallito(motivo);
            if (corpi.Count == 0)
                return Fallito("Il pezzo non ha nessun corpo solido da misurare.");

            var elenco = new List<object>();
            double[] tutto = null;
            for (int i = 0; i < corpi.Count; i++)
            {
                var scatola = ScatolaDelCorpo(corpi[i] as IBody2);
                if (scatola == null)
                    return Fallito(
                        "Non sono riuscito a leggere l'ingombro del corpo " + (i + 1) +
                        ": GetExtremePoint non ha risposto.");
                elenco.Add(new Dictionary<string, object>
                {
                    ["corpo"] = "C" + (i + 1),
                    ["da_mm"] = new[] { Tondo(scatola[0]), Tondo(scatola[1]), Tondo(scatola[2]) },
                    ["a_mm"] = new[] { Tondo(scatola[3]), Tondo(scatola[4]), Tondo(scatola[5]) },
                    ["ingombro_mm"] = new[]
                    {
                        Tondo(scatola[3] - scatola[0]),
                        Tondo(scatola[4] - scatola[1]),
                        Tondo(scatola[5] - scatola[2]),
                    },
                });
                if (tutto == null) tutto = (double[])scatola.Clone();
                else
                {
                    for (int k = 0; k < 3; k++) tutto[k] = Math.Min(tutto[k], scatola[k]);
                    for (int k = 3; k < 6; k++) tutto[k] = Math.Max(tutto[k], scatola[k]);
                }
            }

            var misure = new Dictionary<string, object>
            {
                ["corpi"] = elenco.ToArray(),
                ["da_mm"] = new[] { Tondo(tutto[0]), Tondo(tutto[1]), Tondo(tutto[2]) },
                ["a_mm"] = new[] { Tondo(tutto[3]), Tondo(tutto[4]), Tondo(tutto[5]) },
                ["ingombro_mm"] = new[]
                {
                    Tondo(tutto[3] - tutto[0]),
                    Tondo(tutto[4] - tutto[1]),
                    Tondo(tutto[5] - tutto[2]),
                },
                ["unita"] = "millimetri, nel sistema di coordinate del modello",
                // IL LIMITE DICHIARATO: e' la scatola ALLINEATA AGLI ASSI. Un
                // pezzo lungo messo in diagonale ha una scatola molto piu'
                // grande di quanto serva per imballarlo, e questa misura non
                // dice come girarlo.
                ["cosa_non_guarda"] =
                    "E' la scatola allineata agli assi del modello, non la piu' piccola " +
                    "possibile: un pezzo messo in diagonale ha un ingombro molto piu' " +
                    "grande di quello che gli servirebbe girato bene.",
            };
            _log("[agente] ingombro: " +
                 string.Join(" x ", ((double[])misure["ingombro_mm"]).Select(x => x.ToString("0.##"))) +
                 " mm su " + corpi.Count + " corpi");
            return RiuscitoSenzaCreare(misure);
        }

        // =================================================================

        /// <summary>
        /// I corpi da misurare: tutti, oppure quello chiesto per
        /// identificativo. Null (con un motivo) quando l'identificativo non
        /// esiste: un elenco vuoto darebbe una risposta con dei numeri, e dei
        /// numeri su niente sono peggio di un rifiuto.
        /// </summary>
        private static List<object> CorpiScelti(IPartDoc parte, string uid, out string motivo)
        {
            motivo = null;
            var fuori = new List<object>();
            var grezzi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (grezzi == null || grezzi.Length == 0)
            {
                motivo = "Il pezzo non ha nessun corpo solido. Fai prima un'estrusione.";
                return null;
            }
            for (int i = 0; i < grezzi.Length; i++)
            {
                if (grezzi[i] == null) continue;
                if (!string.IsNullOrEmpty(uid) &&
                    !string.Equals("C" + (i + 1), uid, StringComparison.OrdinalIgnoreCase))
                    continue;
                // SI TIENE L'OGGETTO COM'E' USCITO da `GetBodies2`, non
                // ricastato: e' quello che `AddBodies` aggancia. Vedi il
                // commento accanto ad `AddBodies`.
                fuori.Add(grezzi[i]);
            }
            if (fuori.Count == 0)
            {
                motivo = "Non trovo il corpo «" + uid + "». Gli identificativi dei corpi " +
                         "stanno nello stato del documento, dentro «corpi».";
                return null;
            }
            return fuori;
        }

        /// <summary>
        /// La scatola di un corpo in mm: {minX, minY, minZ, maxX, maxY, maxZ}.
        /// Null se `GetExtremePoint` non risponde — e allora si fallisce,
        /// invece di ripiegare su `GetBodyBox` e dare per buono un numero
        /// approssimato senza dirlo.
        /// </summary>
        private static double[] ScatolaDelCorpo(IBody2 corpo)
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
                        return null;
                    double[] p = { x, y, z };
                    fuori[i] = p[i % 3] * 1000.0;
                }
                return fuori;
            }
            catch { return null; }
        }

        private static string MaterialeDelPezzo(IModelDoc2 modello)
        {
            try
            {
                string database;
                var parte = modello as IPartDoc;
                if (parte == null) return null;
                // La stringa vuota vuol dire «la configurazione attiva», ed e'
                // come lo chiedono gia' `StatoDocumento` e `applica_materiale`.
                string nome = parte.GetMaterialPropertyName2("", out database);
                return string.IsNullOrWhiteSpace(nome) ? null : nome;
            }
            catch { return null; }
        }

        private static double[] Scala(double[] v, double fattore)
        {
            if (v == null || v.Length < 3) return null;
            return new[] { Tondo(v[0] * fattore), Tondo(v[1] * fattore), Tondo(v[2] * fattore) };
        }

        private static double Tondo(double x)
        {
            return Math.Round(x, 4);
        }
    }
}
