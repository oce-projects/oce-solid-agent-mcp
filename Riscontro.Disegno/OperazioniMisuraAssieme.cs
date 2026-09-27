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
                var mp = doc.Extension.CreateMassProperty() as IMassProperty;
                if (mp == null) return null;
                mp.UseSystemUnits = true;
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
            double somma = 0;
            bool sommaLetta = true;
            foreach (var c in pezzi)
            {
                var doc = c.GetModelDoc2() as IModelDoc2;
                if (string.IsNullOrEmpty(MaterialeDelPezzo(doc))) senzaMateriale.Add(c.Name2);
                var m = MassaDelPezzo(doc);
                if (m == null) sommaLetta = false; else somma += m.Value;
            }

            // UN OGGETTO PER NUMERO, come per la parte: IMassProperty cambia le sue
            // risposte a seconda di cosa gli si e' chiesto prima (OperazioniMisura).
            var prendi = new Func<Func<IMassProperty, object>, object>(leggi =>
            {
                try
                {
                    var mp = modello.Extension.CreateMassProperty() as IMassProperty;
                    if (mp == null) return null;
                    mp.UseSystemUnits = true;
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
                else misure["somma_dei_pezzi_kg"] = null;   // un pezzo non si e' lasciato pesare: niente controllo
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

        /// <summary>Z-D6: i corpi solidi che lo STEP di questo assieme deve contenere.</summary>
        private static int SolidiAttesiNellAssieme(IAssemblyDoc assieme)
        {
            int n = 0;
            foreach (var c in PezziDellAssieme(assieme, new List<string>(), new List<string>()))
            {
                object info;
                try { n += ((c.GetBodies3((int)swBodyType_e.swSolidBody, out info) as object[]) ?? new object[0]).Length; }
                catch { return -1; }
            }
            return n;
        }

        /// <summary>Quanti MANIFOLD_SOLID_BREP ci sono nel testo dello STEP; -1 se non si legge.</summary>
        private static int SolidiNelloStep(string percorso)
        {
            try
            {
                int n = 0;
                foreach (var riga in File.ReadLines(percorso))
                {
                    int i = 0;
                    while ((i = riga.IndexOf("MANIFOLD_SOLID_BREP", i, StringComparison.Ordinal)) >= 0) { n++; i += 19; }
                }
                return n;
            }
            catch { return -1; }
        }
    }
}
