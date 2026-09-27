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
            if (a == null || !a.TryGetValue("fisso", out grezzoFisso) || !(grezzoFisso is bool))
                return Fallito("Dimmi «fisso»: true per fissarlo (non si muove piu'), false per liberarlo. " +
                               "Non c'e' un verso d'ufficio: sono due cose opposte.");
            bool voglio = (bool)grezzoFisso;
            object[] grezzi = null;
            try { grezzi = assieme.GetComponents(false) as object[]; } catch { }
            var componenti = StatoDocumento.ComponentiInOrdine(grezzi);
            string uid = Str(a, "componente", null);
            var c = PerUid(componenti, uid);
            if (c == null) return Fallito(NonTrovato(uid, "componente", componenti));
            bool prima = EFisso(c);
            if (prima == voglio)
                return RiuscitoSenzaCreare(new Dictionary<string, object>
                {
                    ["componente"] = uid,
                    ["nome"] = NomeDi(c),
                    ["fisso_prima"] = prima,
                    ["fisso_dopo"] = prima,
                    ["giudizio"] = "«" + NomeDi(c) + "» era gia' " + (voglio ? "fisso" : "libero") + ": niente da cambiare.",
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
                ["componente"] = uid,
                ["nome"] = NomeDi(c),
                ["fisso_prima"] = prima,
                ["fisso_dopo"] = dopo,
                ["accoppiamenti"] = acc >= 0 ? (object)acc : null,
                ["giudizio"] = "«" + NomeDi(c) + "» adesso e' " + (dopo ? "FISSO: non si muove piu', qualunque accoppiamento abbia." :
                    "LIBERO: lo tengono solo i suoi accoppiamenti" + (acc == 0 ? " — e non ne ha: e' appoggiato, si muove." : ".")),
            };
            _log("[agente] " + NomeDi(c) + (dopo ? " fissato" : " liberato"));
            return RiuscitoSenzaCreare(misure);
        }
    }
}
