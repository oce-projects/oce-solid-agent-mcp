using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// SPECCHIARE UN COMPONENTE (voce GA11): la staffa destra e la sinistra, il
    /// caso piu' comune di carpenteria.
    ///
    /// Misurato il 27/9/2026 (`prova0089 ga11`, base 120x80x10 + staffa
    /// 20x40x10 a x = +40, piano destro dell'assieme) con la via moderna,
    /// `FeatureManager.CreateDefinition(swFmMirrorComponent)` →
    /// `IMirrorComponentFeatureData` → `CreateFeature` («SpecchiaComponente1»,
    /// `MirrorCompFeat`), array TIPIZZATI (`Component2[]`):
    ///
    ///  * ISTANZA (`ComponentsToInstanceAlignToComponentOrigin`): un componente
    ///    in piu' dello STESSO file, a x = -40, orientamento rovesciato.
    ///  * OPPOSTO LEGATO (`OppositeHandComponents`, `BreakLinksToOriginalPart`
    ///    false): un FILE NUOVO («…_spec»), il cui albero e' una lavorazione
    ///    sola, `MirrorStock`: cambi la destra e cambia la sinistra.
    ///  * OPPOSTO SLEGATO (lo stesso con `BreakLinksToOriginalPart` true): file
    ///    nuovo con le lavorazioni COPIATE (schizzo, estrusione, sposta/copia
    ///    corpo): correggi la destra e la sinistra resta com'era, senza dirlo.
    ///
    /// La scelta si chiede sempre (nessun verso d'ufficio) e dopo si VERIFICA:
    /// il centro del pezzo nuovo contro lo specchio del centro dell'originale,
    /// e — per gli opposti — l'albero del file nuovo (c'e' `MirrorStock` = e'
    /// legato).
    /// </summary>
    public partial class OperazioniSw
    {
        /// <summary>Il centro della scatola vera di un componente, nell'assieme, in MILLIMETRI; null se non si legge.</summary>
        private static double[] CentroDelComponente(IComponent2 c)
        {
            try
            {
                object info;
                var corpi = c.GetBodies3((int)swBodyType_e.swSolidBody, out info) as object[];
                var tr = c.Transform2 as MathTransform;
                if (corpi == null || corpi.Length == 0 || tr == null) return null;
                double[] tutto = null;
                foreach (var ob in corpi)
                {
                    var copia = (ob as IBody2)?.Copy() as IBody2;
                    if (copia == null || !copia.ApplyTransform(tr)) return null;
                    var sc = ScatolaDelCorpo(copia);
                    if (sc == null) return null;
                    if (tutto == null) tutto = (double[])sc.Clone();
                    else { for (int k = 0; k < 3; k++) tutto[k] = Math.Min(tutto[k], sc[k]); for (int k = 3; k < 6; k++) tutto[k] = Math.Max(tutto[k], sc[k]); }
                }
                return new[] { (tutto[0] + tutto[3]) / 2, (tutto[1] + tutto[4]) / 2, (tutto[2] + tutto[5]) / 2 };
            }
            catch { return null; }
        }

        private Esito SpecchiaComponente(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var assieme = modello as IAssemblyDoc;
            if (assieme == null)
                return Fallito("Specchio un componente di un assieme: questo non lo e'. Per specchiare una lavorazione " +
                               "in un pezzo c'e' «specchia».");
            string modo = (Str(a, "modo", "") ?? "").Trim().ToLowerInvariant();
            if (modo != "istanza" && modo != "opposto_legato" && modo != "opposto_slegato")
                return Fallito("Dimmi «modo», e' la scelta che conta: «istanza» (lo stesso file, specchiato: per un " +
                               "pezzo simmetrico), «opposto_legato» (un file nuovo che segue l'originale: cambi la " +
                               "destra e cambia la sinistra) o «opposto_slegato» (un file nuovo indipendente: " +
                               "correggi la destra e la sinistra resta com'era).");
            string piano = (Str(a, "piano", "") ?? "").Trim().ToLowerInvariant();
            int quale = piano == "frontale" ? 1 : piano == "superiore" ? 2 : piano == "destro" ? 3 : 0;
            if (quale == 0)
                return Fallito("«piano» e' un piano dell'assieme: «frontale», «superiore» o «destro», non «" + piano + "».");
            object[] grezzi = null;
            try { grezzi = assieme.GetComponents(false) as object[]; } catch { }
            var componenti = StatoDocumento.ComponentiInOrdine(grezzi);
            string uid = Str(a, "componente", null);
            var c = PerUid(componenti, uid);
            if (c == null) return Fallito(NonTrovato(uid, "componente", componenti));
            string suffisso = (Str(a, "suffisso", "_spec") ?? "_spec").Trim();
            if (suffisso.Length == 0 || suffisso.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return Fallito("«suffisso» va nel nome del file nuovo: lettere e cifre, per esempio «_sx».");

            IFeature pianoF = null;
            int visti = 0;
            var f = modello.FirstFeature() as IFeature;
            while (f != null)
            {
                string t = "";
                try { t = f.GetTypeName2(); } catch { }
                if (t == "RefPlane" && ++visti == quale) { pianoF = f; break; }
                f = f.GetNextFeature() as IFeature;
            }
            if (pianoF == null) return Fallito("Non trovo il piano " + piano + " dell'assieme.");
            var centroPrima = CentroDelComponente(c);
            if (centroPrima == null)
                return Fallito("Non leggo dove sta «" + NomeDi(c) + "» nell'assieme: senza, non saprei verificare lo " +
                               "specchio. Non ho toccato niente.");
            string fileOriginale = "";
            try { fileOriginale = c.GetPathName() ?? ""; } catch { }
            var primaNomi = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in componenti) primaNomi.Add(v.Key);

            IFeature nuova = null;
            try
            {
                var fm = modello.FeatureManager;
                var dati = fm.CreateDefinition((int)swFeatureNameID_e.swFmMirrorComponent) as IMirrorComponentFeatureData;
                if (dati == null) return Fallito("SOLIDWORKS non ha dato la definizione dello specchio di componenti.");
                dati.MirrorPlane = pianoF;
                var quali = new Component2[] { (Component2)c };   // array TIPIZZATO (object[] fa feature fantasma)
                if (modo == "istanza") dati.ComponentsToInstanceAlignToComponentOrigin = quali;
                else
                {
                    dati.OppositeHandComponents = quali;
                    dati.NameModifierType = (int)swMirrorComponentNameModifier_e.swMirrorComponentName_Suffix;
                    dati.NameModifier = suffisso;
                    dati.PlaceFilesInOneFolder = true;
                    dati.MirrorComponentsFolderLocation = Path.GetDirectoryName(fileOriginale) ?? "";
                    dati.BreakLinksToOriginalPart = modo == "opposto_slegato";
                    dati.CreateDerivedConfigurations = false;
                }
                dati.MirrorType = (int)swMirrorComponentMirrorType_e.swMirrorType_CenterOfBoundingBox;
                nuova = fm.CreateFeature(dati) as IFeature;
            }
            catch (Exception ex) { return Fallito("Lo specchio non e' riuscito: " + ex.Message); }
            try { modello.EditRebuild3(); } catch { }
            if (nuova == null)
                return Fallito("SOLIDWORKS non ha creato lo specchio (CreateFeature ha risposto null). Se il file «" +
                               Path.GetFileNameWithoutExtension(fileOriginale) + suffisso + "» c'e' gia', cambia «suffisso».");

            // IL COMPONENTE NUOVO: quello che prima non c'era.
            object[] dopo = null;
            try { dopo = assieme.GetComponents(false) as object[]; } catch { }
            IComponent2 nuovo = null;
            foreach (var v in StatoDocumento.ComponentiInOrdine(dopo))
                if (!primaNomi.Contains(v.Key)) { nuovo = v.Value; break; }
            if (nuovo == null)
                return Fallito("Lo specchio «" + SafeNome(nuova) + "» c'e' ma non vedo un componente nuovo nell'assieme." +
                               TogliLoSpecchio(modello, nuova, null));
            string fileNuovo = "";
            try { fileNuovo = nuovo.GetPathName() ?? ""; } catch { }
            bool stessoFile = string.Equals(fileNuovo, fileOriginale, StringComparison.OrdinalIgnoreCase);
            if (modo == "istanza" && !stessoFile)
                return Fallito("Ho chiesto un'istanza dello stesso file e SOLIDWORKS ha messo «" + Path.GetFileName(fileNuovo) + "»." +
                               TogliLoSpecchio(modello, nuova, fileNuovo));
            if (modo != "istanza" && stessoFile)
                return Fallito("Ho chiesto un file opposto e il componente nuovo usa ancora «" + Path.GetFileName(fileOriginale) + "»." +
                               TogliLoSpecchio(modello, nuova, null));
            // IL LEGAME, LETTO DALL'ALBERO DEL FILE NUOVO: MirrorStock = legato.
            bool? legato = null;
            if (modo != "istanza")
            {
                try
                {
                    var dn = nuovo.GetModelDoc2() as IModelDoc2;
                    var ff = dn?.FirstFeature() as IFeature;
                    bool trovato = false;
                    while (ff != null) { if (ff.GetTypeName2() == "MirrorStock") { trovato = true; break; } ff = ff.GetNextFeature() as IFeature; }
                    if (dn != null) legato = trovato;
                }
                catch { }
                if (legato == null)
                    return Fallito("Il file nuovo «" + Path.GetFileName(fileNuovo) + "» c'e', ma non leggo il suo albero: non " +
                                   "so dirti se segue l'originale. Aprilo e guarda se la prima lavorazione e' «Specchiato»." +
                                   TogliLoSpecchio(modello, nuova, fileNuovo));
                if (legato.Value != (modo == "opposto_legato"))
                    return Fallito("Ho chiesto un file " + (modo == "opposto_legato" ? "legato" : "slegato") + " all'originale e «" +
                                   Path.GetFileName(fileNuovo) + "» e' " + (legato.Value ? "legato" : "slegato") + "." +
                                   TogliLoSpecchio(modello, nuova, fileNuovo));
            }
            // IL POSTO: il centro del nuovo e lo specchio del centro dell'originale.
            var centroNuovo = CentroDelComponente(nuovo);
            var atteso = (double[])centroPrima.Clone();
            atteso[quale == 1 ? 2 : quale == 2 ? 1 : 0] *= -1;   // frontale XY -> z, superiore XZ -> y, destro YZ -> x
            double scarto = centroNuovo == null ? double.NaN :
                Math.Sqrt(Math.Pow(centroNuovo[0] - atteso[0], 2) + Math.Pow(centroNuovo[1] - atteso[1], 2) + Math.Pow(centroNuovo[2] - atteso[2], 2));   // ScatolaDelCorpo da' gia' mm
            if (double.IsNaN(scarto) || scarto > 0.01)
                return Fallito("«" + NomeDi(nuovo) + "» e' nato, ma il suo centro " + (centroNuovo == null ? "non si legge" :
                               "sta a " + scarto.ToString("0.###") + " mm dallo specchio dell'originale") + ": non e' lo specchio che volevi." +
                               TogliLoSpecchio(modello, nuova, modo == "istanza" ? null : fileNuovo));
            var misure = new Dictionary<string, object>
            {
                ["specchio"] = SafeNome(nuova),
                ["modo"] = modo,
                ["piano"] = piano,
                ["nuovo_componente"] = NomeDi(nuovo),
                ["file"] = fileNuovo,
                ["nuovo_file"] = !stessoFile,
                ["legato_all_originale"] = legato,
                ["centro_originale_mm"] = new[] { Tondo(centroPrima[0]), Tondo(centroPrima[1]), Tondo(centroPrima[2]) },
                ["centro_nuovo_mm"] = new[] { Tondo(centroNuovo[0]), Tondo(centroNuovo[1]), Tondo(centroNuovo[2]) },
                ["giudizio"] = modo == "istanza"
                    ? "Un'istanza dello stesso file, specchiata: vale per un pezzo simmetrico."
                    : modo == "opposto_legato"
                        ? "Un file nuovo che SEGUE l'originale: cambi la destra e cambia anche la sinistra."
                        : "Un file nuovo INDIPENDENTE: correggi la destra e la sinistra resta com'era.",
            };
            _log("[agente] specchiato " + NomeDi(c) + " (" + modo + ") -> " + NomeDi(nuovo));
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// Toglie lo specchio appena nato quando la verifica lo rifiuta (revisione del
        /// 27/9/2026): prima restava nell'assieme un componente che nessuno aveva
        /// accettato. Il FILE nuovo, se c'e', non lo cancello — un file sul disco non
        /// lo tolgo per conto di nessuno — ma lo dico, perche' con lo stesso suffisso
        /// lo specchio non rinasce. La coda della frase di rifiuto.
        /// </summary>
        private static string TogliLoSpecchio(IModelDoc2 modello, IFeature f, string fileNuovo)
        {
            bool tolto = false;
            try
            {
                modello.ClearSelection2(true);
                if (f != null && f.Select2(false, 0))
                    tolto = modello.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                modello.ClearSelection2(true);
                modello.EditRebuild3();
            }
            catch { }
            string file = string.IsNullOrEmpty(fileNuovo) ? "" :
                " Il file «" + Path.GetFileName(fileNuovo) + "» e' rimasto sul disco: cancellalo, o usa un altro «suffisso».";
            return (tolto ? " Ho tolto lo specchio: l'assieme e' com'era." : " Lo specchio «" + SafeNome(f) + "» e' rimasto nell'assieme: toglilo tu.") + file;
        }

        private static string SafeNome(IFeature f)
        {
            try { return f?.Name; } catch { return null; }
        }
    }
}
