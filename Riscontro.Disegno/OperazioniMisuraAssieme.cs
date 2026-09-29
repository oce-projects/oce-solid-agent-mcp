using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// QUANTO PESA L'ASSIEME, IN CHE SCATOLA STA, E LO STEP DELL'ASSIEME
    /// (voci Z-D1 e Z-D6).
    ///
    /// «Quanto pesa» e «in che ingombro ci sta» sono le prime due domande su una
    /// scatola stagna, e `proprieta_di_massa` / `ingombro_corpo` rifiutavano un
    /// assieme con «questo documento non e' un pezzo». Misurato il 27/9/2026
    /// (`prova0089 zd1`, base AISI 304 + coperchio S235JR + una terza istanza
    /// SOPPRESSA):
    ///
    ///  * `CreateMassProperty()` sull'assieme, senza corpi aggiunti: 1,06752 kg,
    ///    ESATTAMENTE la somma delle due parti (0,768 + 0,29952); il componente
    ///    soppresso non pesa.
    ///  * `IComponent2.GetBodies3` da' i corpi nello spazio della PARTE (il
    ///    coperchio a y 0..4 invece di 5..9): per l'ingombro vanno portati
    ///    nell'assieme con `Transform2` — una copia del corpo e `ApplyTransform`.
    ///  * Lo STEP dell'assieme si scrive (`SaveAs3`, come per la parte) e nel
    ///    testo ha un `MANIFOLD_SOLID_BREP` per corpo. RILEGGERLO con `LoadFile4`
    ///    apre il modale «Nuovo documento SOLIDWORKS» e la chiamata non torna:
    ///    la verifica e' il conto dei solidi nel testo, non la riapertura.
    /// </summary>
    public partial class OperazioniSw
    {
        /// <summary>
        /// I componenti che sono PEZZI e non sono soppressi, a tutti i livelli.
        /// I soppressi e quelli il cui documento non si legge (alleggeriti) si
        /// elencano a parte, per dirli.
        /// </summary>
        private static List<IComponent2> PezziDellAssieme(IAssemblyDoc assieme, List<string> soppressi, List<string> nonLetti)
        {
            var fuori = new List<IComponent2>();
            foreach (var o in (assieme.GetComponents(false) as object[]) ?? new object[0])
            {
                var c = o as IComponent2;
                if (c == null) continue;
                string nome = "?";
                try { nome = c.Name2; } catch { }
                bool soppresso = false;
                try { soppresso = c.IsSuppressed(); } catch { }
                if (soppresso) { soppressi.Add(nome); continue; }
                object doc = null;
                try { doc = c.GetModelDoc2(); } catch { }
                if (doc is IAssemblyDoc) continue;          // i suoi pezzi sono gia' nell'elenco
                if (doc is IPartDoc) { fuori.Add(c); continue; }
                nonLetti.Add(nome);
            }
            return fuori;
        }

        private static double? MassaDelPezzo(IModelDoc2 doc)
        {
            try
            {
                // A PART document (a component's): CreateMassProperty2 since
                // Z-C21.7 (ApiHelpers.NewPartMassProperty; the probe's whole-part
                // reading N1 equals the obsolete call's M0). The assembly itself
                // stays on the obsolete call below.
                var mp = ApiHelpers.NewPartMassProperty(doc, null, out _);
                if (mp == null) return null;
                return mp.Mass;
            }
            catch { return null; }
        }

        /// <summary>Z-D1: la massa dell'assieme intero, controllata contro la somma dei suoi pezzi.</summary>
        private Esito ProprietaDiMassaAssieme(IModelDoc2 modello, Dictionary<string, object> a)
        {
            if (!string.IsNullOrWhiteSpace(Str(a, "corpo_uid", null)))
                return Fallito(
                    "Su un assieme «corpo_uid» non vale: peso l'assieme intero, cioe' i componenti non " +
                    "soppressi. Per un pezzo solo, aprilo e chiedimelo li'.");
            var assieme = modello as IAssemblyDoc;
            var soppressi = new List<string>();
            var nonLetti = new List<string>();
            var pezzi = PezziDellAssieme(assieme, soppressi, nonLetti);
            if (pezzi.Count == 0)
                return Fallito(
                    "L'assieme non ha componenti da pesare" +
                    (soppressi.Count > 0 ? ": sono tutti soppressi (" + string.Join(", ", soppressi) + ")" : "") +
                    (nonLetti.Count > 0 ? "; di " + string.Join(", ", nonLetti) + " non leggo il documento (alleggeriti? risolvili)" : "") + ".");

            var senzaMateriale = new List<string>();
            var altraConfigurazione = new List<string>();
            double somma = 0;
            bool sommaLetta = true;
            foreach (var c in pezzi)
            {
                var doc = c.GetModelDoc2() as IModelDoc2;
                // LA CONFIGURAZIONE DEL COMPONENTE, NON QUELLA ATTIVA DEL PEZZO
                // (revisione avversariale del 27/9/2026). Una vite Toolbox — un file,
                // una configurazione per misura — usata M10x40 in un assieme e salvata
                // attiva su M6x16: il pezzo pesato da solo e' l'altro, la somma non
                // torna, e l'operazione si fermava con una causa falsa. Quel pezzo
                // resta fuori dalla somma di controllo (la massa dell'assieme e'
                // giusta: SOLIDWORKS pesa la configurazione che il componente usa), e
                // il materiale si chiede alla configurazione usata.
                string usata = null, attiva = null;
                try { usata = c.ReferencedConfiguration; } catch { }
                try { attiva = (doc.GetActiveConfiguration() as IConfiguration)?.Name; } catch { }
                bool altra = !string.IsNullOrEmpty(usata) && !string.IsNullOrEmpty(attiva) &&
                             !string.Equals(usata, attiva, StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrEmpty(MaterialeDelPezzo(doc, altra ? usata : ""))) senzaMateriale.Add(c.Name2);
                if (altra)
                {
                    altraConfigurazione.Add(c.Name2 + " (usa «" + usata + "», il pezzo e' aperto su «" + attiva + "»)");
                    sommaLetta = false;
                    continue;
                }
                var m = MassaDelPezzo(doc);
                if (m == null) sommaLetta = false; else somma += m.Value;
            }

            // UN OGGETTO PER NUMERO, come per la parte: IMassProperty cambia le sue
            // risposte a seconda di cosa gli si e' chiesto prima (OperazioniMisura).
            var prendi = new Func<Func<IMassProperty, object>, object>(leggi =>
            {
                try
                {
                    var mp = ApiHelpers.NewMassProperty(modello, null, out _);
                    if (mp == null) return null;
                    return leggi(mp);
                }
                catch { return null; }
            });
            object m0 = prendi(p => p.Mass);
            object v0 = prendi(p => p.Volume);
            object c0 = prendi(p => p.CenterOfMass);
            if (m0 == null || v0 == null)
                return Fallito("SOLIDWORKS non ha dato le proprieta' di massa dell'assieme. Non ti do numeri a meta'.");
            double massa = Convert.ToDouble(m0);

            var misure = new Dictionary<string, object>
            {
                ["documento"] = "assieme",
                ["componenti_pesati"] = pezzi.Count,
                ["componenti_soppressi"] = soppressi.ToArray(),
                ["volume_mm3"] = Math.Round(Convert.ToDouble(v0) * 1e9, 2),
                ["centro_massa_mm"] = Scala(c0 as double[], 1000.0),
                ["unita"] = "millimetri e chilogrammi",
            };
            if (nonLetti.Count > 0) misure["componenti_non_letti"] = nonLetti.ToArray();
            if (altraConfigurazione.Count > 0)
                misure["somma_non_controllata_per"] = altraConfigurazione.ToArray();

            if (senzaMateriale.Count > 0)
            {
                // LA MASSA SENZA MATERIALE NON E' ZERO, E' «NON SI SA» — come per la
                // parte: SOLIDWORKS risponderebbe con la densita' dell'acqua.
                misure["massa_kg"] = null;
                misure["senza_materiale"] = senzaMateriale.ToArray();
                misure["perche_niente_massa"] =
                    "A " + string.Join(", ", senzaMateriale) + " non e' stato dato un materiale: la massa " +
                    "dell'assieme non si sa. SOLIDWORKS risponderebbe lo stesso con la densita' predefinita " +
                    "(1000 kg/m3, l'acqua): un numero credibile e sbagliato. Dai il materiale ai pezzi " +
                    "(«applica_materiale» su ognuno) e richiedimelo.";
            }
            else
            {
                misure["massa_kg"] = Math.Round(massa, 4);
                // IL CONTO RIFATTO: la massa dell'assieme contro la somma dei suoi
                // pezzi letti uno per uno. Se divergono, una delle due letture non e'
                // quella che sembra (un materiale messo sul componente, diverso da
                // quello del pezzo; un componente che conta due volte).
                if (sommaLetta)
                {
                    misure["somma_dei_pezzi_kg"] = Math.Round(somma, 4);
                    double scarto = massa > 0 ? Math.Abs(somma - massa) / massa : double.NaN;
                    misure["scarto_sulla_somma"] = double.IsNaN(scarto) ? null : (object)Math.Round(scarto, 6);
                    if (double.IsNaN(scarto) || scarto > 0.001)
                        return Fallito(
                            "La massa dell'assieme (" + massa.ToString("0.####") + " kg) e la somma dei suoi pezzi (" +
                            somma.ToString("0.####") + " kg) non combaciano: una delle due letture non e' quella che " +
                            "sembra (un materiale messo sul componente nell'assieme? un componente contato due volte?). " +
                            "Non ti do un numero.");
                }
                else misure["somma_dei_pezzi_kg"] = null;   // un pezzo non si e' lasciato pesare, o usa un'altra configurazione: niente controllo
            }
            misure["cosa_non_guarda"] =
                "I componenti soppressi non pesano, e li elenco. Un materiale messo sul COMPONENTE " +
                "nell'assieme, diverso da quello del pezzo, la somma dei pezzi non lo vede: se c'e', lo " +
                "scarto lo dice e mi fermo.";
            _log("[agente] massa dell'assieme: " + pezzi.Count + " pezzi, " +
                 (senzaMateriale.Count > 0 ? "senza materiale" : massa.ToString("0.###") + " kg"));
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>Z-D1: la scatola vera dell'assieme, pezzo per pezzo, nelle sue coordinate.</summary>
        private Esito IngombroAssieme(IModelDoc2 modello, Dictionary<string, object> a)
        {
            if (!string.IsNullOrWhiteSpace(Str(a, "corpo_uid", null)))
                return Fallito(
                    "Su un assieme «corpo_uid» non vale: misuro l'ingombro dell'assieme intero. Per un pezzo " +
                    "solo, aprilo e chiedimelo li'.");
            var assieme = modello as IAssemblyDoc;
            var soppressi = new List<string>();
            var nonLetti = new List<string>();
            var pezzi = PezziDellAssieme(assieme, soppressi, nonLetti);
            if (pezzi.Count == 0)
                return Fallito("L'assieme non ha componenti da misurare" +
                               (soppressi.Count > 0 ? ": sono tutti soppressi" : "") + ".");
            var elenco = new List<object>();
            double[] tutto = null;
            foreach (var c in pezzi)
            {
                object info;
                var corpi = c.GetBodies3((int)swBodyType_e.swSolidBody, out info) as object[];
                if (corpi == null || corpi.Length == 0) continue;
                MathTransform tr = null;
                try { tr = c.Transform2; } catch { }
                if (tr == null)
                    return Fallito("Non leggo dove sta «" + c.Name2 + "» nell'assieme (Transform2): senza, la " +
                                   "sua scatola starebbe nel posto sbagliato. Non ti do un ingombro.");
                double[] suo = null;
                foreach (var ob in corpi)
                {
                    // I CORPI ARRIVANO NELLO SPAZIO DELLA PARTE (misurato): una copia,
                    // portata nell'assieme, e la scatola di quella.
                    IBody2 copia = null;
                    try { copia = (ob as IBody2)?.Copy() as IBody2; } catch { }
                    bool portato = false;
                    try { portato = copia != null && copia.ApplyTransform(tr); } catch { }
                    var sc = portato ? ScatolaDelCorpo(copia) : null;
                    if (sc == null)
                        return Fallito("Non riesco a portare nell'assieme un corpo di «" + c.Name2 + "» o a " +
                                       "leggerne la scatola: non ti do un ingombro a meta'.");
                    if (suo == null) suo = (double[])sc.Clone();
                    else { for (int k = 0; k < 3; k++) suo[k] = Math.Min(suo[k], sc[k]); for (int k = 3; k < 6; k++) suo[k] = Math.Max(suo[k], sc[k]); }
                }
                if (suo == null) continue;
                elenco.Add(new Dictionary<string, object>
                {
                    ["componente"] = c.Name2,
                    ["da_mm"] = new[] { Tondo(suo[0]), Tondo(suo[1]), Tondo(suo[2]) },
                    ["a_mm"] = new[] { Tondo(suo[3]), Tondo(suo[4]), Tondo(suo[5]) },
                });
                if (tutto == null) tutto = (double[])suo.Clone();
                else { for (int k = 0; k < 3; k++) tutto[k] = Math.Min(tutto[k], suo[k]); for (int k = 3; k < 6; k++) tutto[k] = Math.Max(tutto[k], suo[k]); }
            }
            if (tutto == null) return Fallito("Nessun componente dell'assieme ha un corpo solido da misurare.");
            var misure = new Dictionary<string, object>
            {
                ["documento"] = "assieme",
                ["componenti"] = elenco.ToArray(),
                ["componenti_soppressi"] = soppressi.ToArray(),
                ["da_mm"] = new[] { Tondo(tutto[0]), Tondo(tutto[1]), Tondo(tutto[2]) },
                ["a_mm"] = new[] { Tondo(tutto[3]), Tondo(tutto[4]), Tondo(tutto[5]) },
                ["ingombro_mm"] = new[] { Tondo(tutto[3] - tutto[0]), Tondo(tutto[4] - tutto[1]), Tondo(tutto[5] - tutto[2]) },
                ["unita"] = "millimetri, nel sistema di coordinate dell'assieme",
                ["cosa_non_guarda"] =
                    "E' la scatola allineata agli assi dell'assieme, non la piu' piccola possibile. I " +
                    "componenti soppressi non ci sono (li elenco).",
            };
            if (nonLetti.Count > 0) misure["componenti_non_letti"] = nonLetti.ToArray();
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// Z-D6: i corpi solidi che lo STEP di questo assieme deve contenere.
        ///
        /// PER PEZZO, NON PER ISTANZA (revisione avversariale del 27/9/2026). Lo
        /// STEP scrive la struttura dell'assieme: la forma di un pezzo compare UNA
        /// volta e le istanze sono occorrenze con la loro trasformazione. Misurato
        /// il 28/9/2026 (caso assiemi/due_basi_uguali_uno_step: la base due volte
        /// e il coperchio una) — nel file 2 solidi. Il conto per istanza
        /// ne aspettava 3, e rifiutava uno STEP giusto su ogni assieme con viti o
        /// piedini ripetuti. Un pezzo e' il suo file PIU' la configurazione: due
        /// misure della stessa vite sono due forme.
        ///
        /// AN INSTANCE CUT IN THE ASSEMBLY IS A SHAPE OF ITS OWN (finding C4-2,
        /// measured on 28/9/2026 by the Z-C21.3 probe of the user bodies in a STEP:
        /// part P 40 x 40 x 10 inserted twice, an assembly cut through the first
        /// instance only). IComponent2~GetBodies3.html, Remarks: «Returns an array
        /// in BodiesInfo containing information about bodies that indicates whether
        /// they are normal or user bodies. User bodies are original component bodies
        /// that have been modified in an assembly». Measured: a 10 x 10 hole gave
        /// the cut instance one swUserBody_e body (15000 mm3) and the other one
        /// swNormalBody_e (16000 mm3), and the STEP held 2 solids where this count
        /// said 1; a slot splitting the instance gave 2 user bodies (7200 + 7200)
        /// and 3 solids in the STEP against 2 here. So a STEP that had lost the cut
        /// instance would have passed. Now every instance with a user body adds
        /// ALL its bodies, and only the instances with normal bodies are counted
        /// once per file and configuration: 2 and 3, as in the file (and 1 and 2
        /// for the uncut assembly and two configurations of the same file, as before).
        /// </summary>
        private static int SolidiAttesiNellAssieme(IAssemblyDoc assieme)
        {
            var keys = new List<string>();
            var bodies = new List<int>();
            var userBodies = new List<int>();
            foreach (var c in PezziDellAssieme(assieme, new List<string>(), new List<string>()))
            {
                string chiave;
                try { chiave = (c.GetPathName() ?? "") + "|" + (c.ReferencedConfiguration ?? ""); }
                catch { return -1; }
                object info;
                object[] found;
                try { found = c.GetBodies3((int)swBodyType_e.swSolidBody, out info) as object[]; }
                catch { return -1; }
                keys.Add(chiave);
                bodies.Add(found == null ? 0 : found.Length);
                userBodies.Add(UserBodiesIn(info));
            }
            return ExpectedStepSolids(keys.ToArray(), bodies.ToArray(), userBodies.ToArray());
        }

        /// <summary>
        /// C4-2: how many entries of GetBodies3's BodiesInfo are swUserBody_e (0;
        /// swNormalBody_e is 1, swconst page swBodyInfo_e.html). The probe read an
        /// Int32[]; an object[] of numbers is read the same way. Nothing readable
        /// is 0: the instance is then counted as before, once per file.
        /// </summary>
        private static int UserBodiesIn(object info)
        {
            int users = 0;
            var numbers = info as int[];
            if (numbers != null)
            {
                foreach (int k in numbers) if (k == (int)swBodyInfo_e.swUserBody_e) users++;
                return users;
            }
            var boxed = info as object[];
            if (boxed == null) return 0;
            foreach (object o in boxed)
            {
                try { if (Convert.ToInt32(o) == (int)swBodyInfo_e.swUserBody_e) users++; } catch { }
            }
            return users;
        }

        /// <summary>
        /// C4-2, the rule without SOLIDWORKS (the contract group in Prove calls it
        /// with the probe's numbers): per non-suppressed part instance its file and
        /// configuration key, its solid bodies and how many of them are user
        /// bodies. An instance with a user body was changed in the assembly and is
        /// a shape of its own in the STEP: all its bodies count. The others count
        /// once per key. -1 if the arrays do not line up or a count is negative.
        /// </summary>
        internal static int ExpectedStepSolids(string[] keys, int[] bodies, int[] userBodies)
        {
            if (keys == null || bodies == null || userBodies == null ||
                bodies.Length != keys.Length || userBodies.Length != keys.Length) return -1;
            int n = 0;
            var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keys.Length; i++)
            {
                if (bodies[i] < 0 || userBodies[i] < 0) return -1;
                if (userBodies[i] > 0) { n += bodies[i]; continue; }
                if (counted.Add(keys[i] ?? "")) n += bodies[i];
            }
            return n;
        }

        /// <summary>
        /// Quanti solidi ci sono nel testo dello STEP; -1 se non si legge. Si conta
        /// per ISTANZA di entita' (il testo fra due «;»): un solido con cavita' si
        /// scrive BREP_WITH_VOIDS, che non contiene la parola MANIFOLD_SOLID_BREP, e
        /// un'entita' complessa puo' nominare tutte e due — contata una volta sola
        /// (revisione avversariale del 27/9/2026).
        /// </summary>
        private static int SolidiNelloStep(string percorso)
        {
            try
            {
                int n = 0;
                foreach (var istanza in File.ReadAllText(percorso).Split(';'))
                    if (istanza.IndexOf("MANIFOLD_SOLID_BREP", StringComparison.Ordinal) >= 0 ||
                        istanza.IndexOf("BREP_WITH_VOIDS", StringComparison.Ordinal) >= 0) n++;
                return n;
            }
            catch { return -1; }
        }
    }
}
