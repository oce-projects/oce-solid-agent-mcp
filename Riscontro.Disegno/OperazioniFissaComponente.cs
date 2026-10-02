using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace Riscontro.Disegno
{
    /// <summary>
    /// FISSA O LIBERA UN COMPONENTE (voce Z-D5).
    ///
    /// E' l'unico dei comandi di stato di un componente che cambia davvero come
    /// l'assieme si ricostruisce: nascosto e trasparente non cambiano il pezzo
    /// fabbricato, fisso si'. Un componente fisso non si muove perche' qualcuno
    /// l'ha inchiodato, non perche' sia accoppiato.
    ///
    /// Misurato il 27/9/2026 (`prova0089 zd345`): il componente selezionato
    /// (`IComponent2.Select4`) e `IAssemblyDoc.FixComponent()` /
    /// `UnfixComponent()` — tutte e due `void`: l'esito si rilegge con
    /// `IsFixed`. Funziona nei due versi, ANCHE sul primo componente, quello che
    /// SOLIDWORKS fissa da solo all'inserimento (lezione GA5: si comporta come
    /// uno fissato a mano).
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito FissaComponente(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var assieme = modello as IAssemblyDoc;
            if (assieme == null)
                return Fallito("Questo non e' un assieme: fissare o liberare vale per un componente di un " +
                               "assieme. Aprimi l'assieme.");
            object grezzoFisso;
            if (a == null || !a.TryGetValue("grounded", out grezzoFisso) || !(grezzoFisso is bool))
                return Fallito("Dimmi «grounded»: true per fissarlo (non si muove piu'), false per liberarlo. " +
                               "Non c'e' un verso d'ufficio: sono due cose opposte.");
            bool voglio = (bool)grezzoFisso;
            object[] grezzi = null;
            try { grezzi = assieme.GetComponents(false) as object[]; } catch { }
            var componenti = StatoDocumento.ComponentiInOrdine(grezzi);
            string uid = Str(a, "component", null);
            var c = PerUid(componenti, uid);
            if (c == null) return Fallito(NonTrovato(uid, "component", componenti));
            // TOP LEVEL ONLY (finding L11-3, lot Z-C21.5). Up to library 0.145.0
            // a nested component was read with IsFixed from the root, which the
            // guide says does not apply to it, and could come back «it was
            // already free: nothing to change».
            string notTopLevel = NotTopLevel(assieme, c, uid, componenti,
                "fixing or freeing works on top-level components only: IsFixed «only applies to the top " +
                "level of components» (IComponent2~IsFixed.html), and from the root «all of the " +
                "components in the subassembly are allowed to move»");
            if (notTopLevel != null) return Fallito(notTopLevel);
            bool prima = EFisso(c);
            if (prima == voglio)
                return RiuscitoSenzaCreare(new Dictionary<string, object>
                {
                    ["component"] = uid,
                    ["name"] = NomeDi(c),
                    ["grounded_before"] = prima,
                    ["grounded_after"] = prima,
                    ["verdict"] = "«" + NomeDi(c) + "» era gia' " + (voglio ? "fisso" : "libero") + ": niente da cambiare.",
                });
            bool presa = false;
            try
            {
                modello.ClearSelection2(true);
                presa = c.Select4(false, null, false);
                if (presa)
                {
                    if (voglio) assieme.FixComponent(); else assieme.UnfixComponent();
                }
            }
            catch (Exception ex) { try { modello.ClearSelection2(true); } catch { } return Fallito("Non ci sono riuscito: " + ex.Message); }
            try { modello.ClearSelection2(true); } catch { }
            if (!presa) return Fallito("«" + NomeDi(c) + "» non si lascia selezionare: non l'ho toccato.");
            try { modello.EditRebuild3(); } catch { }
            // SI RILEGGE: FixComponent e UnfixComponent non rispondono niente.
            bool dopo = EFisso(c);
            if (dopo != voglio)
                return Fallito("Ho chiesto di " + (voglio ? "fissare" : "liberare") + " «" + NomeDi(c) + "», e " +
                               "rileggendolo e' ancora " + (dopo ? "fisso" : "libero") + ". Non e' successo niente.");
            int acc = AccoppiamentiDi(c);
            var misure = new Dictionary<string, object>
            {
                ["component"] = uid,
                ["name"] = NomeDi(c),
                ["grounded_before"] = prima,
                ["grounded_after"] = dopo,
                ["mates"] = acc >= 0 ? (object)acc : null,
                ["verdict"] = "«" + NomeDi(c) + "» adesso e' " + (dopo ? "FISSO: non si muove piu', qualunque accoppiamento abbia." :
                    "LIBERO: lo tengono solo i suoi accoppiamenti" + (acc == 0 ? " — e non ne ha: e' appoggiato, si muove." : ".")),
            };
            _log("[agente] " + NomeDi(c) + (dopo ? " fissato" : " liberato"));
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// THE TOP-LEVEL CHECK shared by fix_component (L11-3) and
        /// replace_component (L11-5), lot Z-C21.5: null when
        /// <paramref name="component"/> is a top-level component of
        /// <paramref name="assembly"/>, otherwise the refusal, which names the
        /// top-level sub-assembly that holds it and its uid.
        ///
        /// The C1..Cn list is built from GetComponents(false), every level
        /// (the state lists them all on purpose), while both SOLIDWORKS calls
        /// behind these operations work on the top level only. «Top level» is
        /// decided by StatoDocumento.TopLevelNames, the same rule the state
        /// uses for «top_level». A top-level list that cannot be read refuses
        /// too: acting on a component that may be nested is the defect itself.
        /// </summary>
        private static string NotTopLevel(
            IAssemblyDoc assembly, IComponent2 component, string uid,
            List<KeyValuePair<string, IComponent2>> components, string why)
        {
            object[] topLevel = null;
            try { topLevel = assembly.GetComponents(true) as object[]; } catch { }
            var names = StatoDocumento.TopLevelNames(topLevel);
            string name = NomeDi(component);
            if (names == null || string.IsNullOrEmpty(name))
                return "I could not tell whether «" + uid + "» is a top-level component (" +
                       (names == null ? "IAssemblyDoc.GetComponents(true) did not answer" : "its name did not answer") +
                       "), and " + why + ". Nothing was changed.";
            if (names.Contains(name)) return null;

            // Name2 of a nested component is the whole path, «subAssem1-2/Part1-1»
            // (IComponent2~Name2.html): its first segment is the top-level
            // sub-assembly that holds it.
            int slash = name.IndexOf('/');
            string holder = slash > 0 ? name.Substring(0, slash) : null;
            string holderUid = null;
            if (holder != null)
                for (int i = 0; i < components.Count; i++)
                    if (string.Equals(components[i].Key, holder, StringComparison.Ordinal)) { holderUid = "C" + (i + 1); break; }
            return "«" + uid + "» («" + name + "») is not a top-level component: it sits inside the sub-assembly «" +
                   (holder ?? "?") + "»" + (holderUid == null ? "" : " (" + holderUid + ")") + ", and " + why +
                   ". Nothing was changed. Open that sub-assembly and do it there, or act on the sub-assembly " +
                   "itself" + (holderUid == null ? "" : ", " + holderUid) + ".";
        }
    }
}
