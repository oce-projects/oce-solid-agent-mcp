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
    ///
    /// THE ORIENTATION OF AN INSTANCE (finding N26, measured on 28/9/2026 by
    /// the Z-C21.3 probe of the mirrored instance's axes: bracket 20 x 40 x 10
    /// with a corner boss, inserted at x = +40, mirrored on the assembly Right
    /// plane). An instance of the same file can come out in FOUR rigid
    /// orientations, one per swMirrorComponentOrientation2_e value, and the
    /// four have the SAME box
    /// centre: the centre check passed on all of them (0 mm), so it cannot
    /// tell them apart. Measured axes X' / Y' / Z' of Transform2:
    ///   0 MirroredX_MirroredY                     (-1,0,0) (0,1,0) (0,0,-1)
    ///   1 MirroredAndFlippedX_MirroredY           (1,0,0) (0,1,0) (0,0,1)
    ///   2 MirroredX_MirroredAndFlippedY           (-1,0,0) (0,-1,0) (0,0,1)
    ///   3 MirroredAndFlippedX_MirroredAndFlippedY (1,0,0) (0,-1,0) (0,0,-1)
    /// With no orientation array SOLIDWORKS gave 0, the default the guide
    /// documents (IMirrorComponentFeatureData~ComponentOrientationsAlignToComponentOrigin.html,
    /// Remarks: «missing orientations default to
    /// swMirrorComponentOrientation2_e.swOrientation_MirroredX_MirroredY»);
    /// an int[] gave the same axes as the typed enum array. So the orientation
    /// is now always SET (argument «orientation», default 0) and the axes of
    /// the new instance are CHECKED against it: X' = ±(X mirrored in the plane),
    /// Y' = ±(Y mirrored in the plane), Z' = X' x Y' (see MirrorAxesProblem).
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

        /// <summary>
        /// N26: the «orientation» argument, as the catalogue spells it, and the
        /// swMirrorComponentOrientation2_e value it stands for (swconst page
        /// swMirrorComponentOrientation2_e.html: MirroredX_MirroredY 0,
        /// MirroredAndFlippedX_MirroredY 1, MirroredX_MirroredAndFlippedY 2,
        /// MirroredAndFlippedX_MirroredAndFlippedY 3). The first one is the
        /// default, as it is for SOLIDWORKS.
        /// </summary>
        internal static readonly string[] MirrorOrientationNames =
        {
            "mirrored_x_mirrored_y",
            "mirrored_and_flipped_x_mirrored_y",
            "mirrored_x_mirrored_and_flipped_y",
            "mirrored_and_flipped_x_mirrored_and_flipped_y",
        };

        private static readonly swMirrorComponentOrientation2_e[] MirrorOrientationValues =
        {
            swMirrorComponentOrientation2_e.swOrientation_MirroredX_MirroredY,
            swMirrorComponentOrientation2_e.swOrientation_MirroredAndFlippedX_MirroredY,
            swMirrorComponentOrientation2_e.swOrientation_MirroredX_MirroredAndFlippedY,
            swMirrorComponentOrientation2_e.swOrientation_MirroredAndFlippedX_MirroredAndFlippedY,
        };

        /// <summary>
        /// N26: the X, Y and Z axes of a component in the assembly, as unit
        /// vectors: Transform2 applied to (1,0,0), (0,1,0), (0,0,1) with
        /// IMathVector.MultiplyTransform (a vector takes the rotation, not the
        /// translation), as the probe read them. Null if any is unreadable.
        /// </summary>
        private double[][] ComponentAxes(IComponent2 component)
        {
            try
            {
                var math = _sw.GetMathUtility() as IMathUtility;
                var transform = component.Transform2 as MathTransform;
                if (math == null || transform == null) return null;
                var axes = new double[3][];
                for (int k = 0; k < 3; k++)
                {
                    var unit = new double[3];
                    unit[k] = 1;
                    var moved = ((IMathVector)math.CreateVector(unit)).MultiplyTransform(transform) as IMathVector;
                    var data = moved == null ? null : moved.ArrayData as double[];
                    if (data == null || data.Length < 3) return null;
                    axes[k] = new[] { data[0], data[1], data[2] };
                }
                return axes;
            }
            catch { return null; }
        }

        /// <summary>
        /// N26: the axes a mirrored instance must have, from the original's
        /// axes, the assembly axis the mirror plane flips (0 = x for the Right
        /// plane, 1 = y for Top, 2 = z for Front) and the orientation index
        /// (see <see cref="MirrorOrientationNames"/>): X' = ±(X mirrored),
        /// Y' = ±(Y mirrored), minus where the orientation says «flipped», and
        /// Z' = X' x Y' so that the result is rigid. Null if an input is not
        /// three vectors of three numbers.
        ///
        /// The probe measured this rule on the Right plane with the original
        /// unrotated (all four orientations, table in the class comment). The
        /// names speak of the COMPONENT's X and Y, which do not depend on the
        /// plane, so the same rule is applied to the other two planes.
        /// </summary>
        internal static double[][] ExpectedMirrorAxes(double[][] before, int planeNormalIndex, int orientation)
        {
            if (before == null || before.Length < 3 || planeNormalIndex < 0 || planeNormalIndex > 2 ||
                orientation < 0 || orientation > 3) return null;
            for (int k = 0; k < 3; k++) if (before[k] == null || before[k].Length < 3) return null;
            double sx = (orientation & 1) != 0 ? -1 : 1;   // 1 and 3: X mirrored AND flipped
            double sy = (orientation & 2) != 0 ? -1 : 1;   // 2 and 3: Y mirrored AND flipped
            var x = new[] { before[0][0], before[0][1], before[0][2] };
            var y = new[] { before[1][0], before[1][1], before[1][2] };
            x[planeNormalIndex] = -x[planeNormalIndex];
            y[planeNormalIndex] = -y[planeNormalIndex];
            for (int k = 0; k < 3; k++) { x[k] *= sx; y[k] *= sy; }
            var z = new[] { x[1] * y[2] - x[2] * y[1], x[2] * y[0] - x[0] * y[2], x[0] * y[1] - x[1] * y[0] };
            return new[] { x, y, z };
        }

        /// <summary>
        /// N26: null when the new instance's axes are the ones the orientation
        /// asks for (1e-6 on each component: the probe read exact 0 and ±1), else
        /// the sentence that says which axis is off and how.
        /// </summary>
        internal static string MirrorAxesProblem(double[][] before, double[][] after, int planeNormalIndex, int orientation)
        {
            var expected = ExpectedMirrorAxes(before, planeNormalIndex, orientation);
            if (expected == null) return "the axes of the original component could not be read";
            if (after == null || after.Length < 3) return "the axes of the new component could not be read";
            var names = new[] { "X", "Y", "Z" };
            var wrong = new List<string>();
            for (int k = 0; k < 3; k++)
            {
                if (after[k] == null || after[k].Length < 3) return "the " + names[k] + " axis of the new component could not be read";
                double off = 0;
                for (int j = 0; j < 3; j++) off = Math.Max(off, Math.Abs(after[k][j] - expected[k][j]));
                if (off > 1e-6) wrong.Add(names[k] + "' is " + AxisText(after[k]) + " instead of " + AxisText(expected[k]));
            }
            return wrong.Count == 0 ? null : string.Join(", ", wrong);
        }

        private static string AxisText(double[] v)
        {
            var parts = new string[3];
            for (int k = 0; k < 3; k++)
            {
                double r = Math.Round(v[k], 4);
                if (r == 0) r = 0;   // no «-0»
                parts[k] = r.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            }
            return "(" + string.Join(", ", parts) + ")";
        }

        private static double[] RoundedAxis(double[] v)
        {
            var r = new double[3];
            for (int k = 0; k < 3; k++) { r[k] = Math.Round(v[k], 6); if (r[k] == 0) r[k] = 0; }
            return r;
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
            // N26: the orientation of a mirrored instance, always set and then checked.
            // An opposite-hand file is a new, mirrored part: SOLIDWORKS has no
            // orientation to choose for it (the orientations go with
            // ComponentsToInstanceAlignToComponentOrigin), so asking for one
            // there is refused rather than ignored.
            string orientationAsked = (Str(a, "orientation", "") ?? "").Trim().ToLowerInvariant();
            int orientation = orientationAsked.Length == 0 ? 0 : Array.IndexOf(MirrorOrientationNames, orientationAsked);
            if (orientationAsked.Length > 0 && modo != "istanza")
                return Fallito("«orientation» applies only to a mirrored instance of the same file: an opposite-hand " +
                               "file is a new mirrored part and has no orientation to choose. Leave «orientation» out. " +
                               "Nothing was changed.");
            if (orientation < 0)
                return Fallito("«orientation» is one of «" + string.Join("», «", MirrorOrientationNames) + "» (the first is " +
                               "the default), not «" + orientationAsked + "». Nothing was changed.");

            // THE ASSEMBLY PLANE BY ITS GEOMETRY, NOT AS THE N-TH RefPlane OF THE
            // TREE (Z-C21.5, finding C3-6 extended, scheme S19): the plane with the
            // canonical normal through the origin, Riferimenti.BasePlane. Counting
            // the RefPlanes («the third in order») mirrored on another plane as
            // soon as a plane sat before the default ones. Not N26, the
            // orientation of the instance, fixed in Z-C21.2-ter.
            IFeature pianoF = Riferimenti.BasePlane(modello, piano, out string planeProblem);
            if (pianoF == null)
                return Fallito("I cannot find the assembly's «" + piano + "» plane: " + planeProblem +
                               " Nothing was changed.");
            var centroPrima = CentroDelComponente(c);
            if (centroPrima == null)
                return Fallito("Non leggo dove sta «" + NomeDi(c) + "» nell'assieme: senza, non saprei verificare lo " +
                               "specchio. Non ho toccato niente.");
            double[][] axesBefore = modo == "istanza" ? ComponentAxes(c) : null;
            if (modo == "istanza" && axesBefore == null)
                return Fallito("I cannot read the axes of «" + NomeDi(c) + "» in the assembly (Transform2): without them I " +
                               "could not check the orientation of the mirrored instance. Nothing was changed.");
            string fileOriginale = "";
            try { fileOriginale = c.GetPathName() ?? ""; } catch { }
            var primaNomi = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in componenti) primaNomi.Add(v.Key);

            IFeature nuova = null;
            ApiHelpers.FeatureCreation creation = null;
            try
            {
                // S10: CreateDefinition -> fill -> CreateFeature, and the error code
                // of what was born, through ApiHelpers.CreateFeatureFromDefinition.
                creation = ApiHelpers.CreateFeatureFromDefinition<IMirrorComponentFeatureData>(
                    modello.FeatureManager, (int)swFeatureNameID_e.swFmMirrorComponent, dati =>
                {
                    dati.MirrorPlane = pianoF;
                    var quali = new Component2[] { (Component2)c };   // array TIPIZZATO (object[] fa feature fantasma)
                    if (modo == "istanza")
                    {
                        dati.ComponentsToInstanceAlignToComponentOrigin = quali;
                        // N26: one orientation per component, typed as in
                        // Mirror_Components_II_Example_CSharp.htm; the page of
                        // ComponentsToInstanceAlignToComponentOrigin says «Use
                        // IMirrorComponentFeatureData::ComponentOrientationsAlignToComponentOrigin
                        // to specify the orientation of each component in this array».
                        dati.ComponentOrientationsAlignToComponentOrigin =
                            new swMirrorComponentOrientation2_e[] { MirrorOrientationValues[orientation] };
                    }
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
                    return true;
                });
                if (creation.Stage == ApiHelpers.CreationStage.Definition)
                    return Fallito("SOLIDWORKS non ha dato la definizione dello specchio di componenti.");
                nuova = creation.Feature;
            }
            catch (Exception ex) { return Fallito("Lo specchio non e' riuscito: " + ex.Message); }
            try { modello.EditRebuild3(); } catch { }
            if (nuova == null)
                return Fallito("SOLIDWORKS non ha creato lo specchio (CreateFeature ha risposto null). Se il file «" +
                               Path.GetFileNameWithoutExtension(fileOriginale) + suffisso + "» c'e' gia', cambia «suffisso».");

            // THE ERROR CODE OF THE MIRROR THAT WAS BORN (finding L11-7).
            // IFeatureManager~CreateFeature.html: «Use IFeature::GetErrorCode2
            // to get any errors». ApiHelpers.CreateFeatureFromDefinition reads it
            // right after CreateFeature, but until library 0.144.0 this caller
            // kept only the feature, so a mirror born in error went on to the
            // component checks and could pass them. It is read again after the
            // rebuild above, because a rebuild can put a feature in error (the
            // same rule as ModifyAndVerify). A warning is not an error
            // (IFeature~GetErrorCode2.html, «IsWarning»). When CreateFeature
            // answers null there is no feature to ask, and the message above
            // says what else to check.
            // If the second reading cannot be made, the first one decides.
            string mirrorError = ApiHelpers.RebuildError(nuova, out bool mirrorErrorKnown);
            if (mirrorError == null && !mirrorErrorKnown && creation != null) mirrorError = creation.ErrorPhrase;
            if (mirrorError != null)
                return Fallito("The component mirror «" + SafeNome(nuova) + "» was born in error: " + mirrorError +
                               "." + TogliLoSpecchio(modello, nuova, null));

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
            // N26: THE AXES, which the centre cannot see (all four orientations
            // measured with the centre 0 mm off). Read from Transform2 of the new
            // instance and compared with the orientation that was set.
            double[][] axesAfter = null;
            if (modo == "istanza")
            {
                axesAfter = ComponentAxes(nuovo);
                string axesProblem = MirrorAxesProblem(axesBefore, axesAfter, quale == 1 ? 2 : quale == 2 ? 1 : 0, orientation);
                if (axesProblem != null)
                    return Fallito("«" + NomeDi(nuovo) + "» was born in the right place but not with the orientation " +
                                   "«" + MirrorOrientationNames[orientation] + "»: " + axesProblem + "." +
                                   TogliLoSpecchio(modello, nuova, null));
            }
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
                // N26: the orientation set, and the axes read back that match it
                // (null for an opposite-hand file, which has no orientation to set).
                ["orientation"] = modo == "istanza" ? MirrorOrientationNames[orientation] : null,
                ["new_axes"] = axesAfter == null ? null : new Dictionary<string, object>
                {
                    ["x"] = RoundedAxis(axesAfter[0]),
                    ["y"] = RoundedAxis(axesAfter[1]),
                    ["z"] = RoundedAxis(axesAfter[2]),
                },
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
        /// <summary>
        /// Removes the mirror this step made and returns the tail of the
        /// refusal: «removed» only after the tree is read again and the mirror
        /// is no longer in it (Z-C21.8, the rule of M42; until the review of
        /// this lot it said «the assembly is as it was» on the answer of
        /// DeleteSelection2 alone, and L11-7 added a caller).
        /// </summary>
        private static string TogliLoSpecchio(IModelDoc2 modello, IFeature f, string fileNuovo)
        {
            string name = SafeNome(f);
            bool deleted = false;
            try
            {
                modello.ClearSelection2(true);
                if (f != null && f.Select2(false, 0))
                    deleted = modello.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                modello.ClearSelection2(true);
                modello.EditRebuild3();
            }
            catch { }
            bool? stillInTree = deleted ? FeatureStillInTree(modello, name) : (bool?)null;
            return MirrorRemovalNote(name, deleted, stillInTree,
                                     string.IsNullOrEmpty(fileNuovo) ? null : Path.GetFileName(fileNuovo));
        }

        /// <summary>
        /// Whether a feature of that name is still in the tree: null when the
        /// name is unknown or the tree cannot be walked to the end.
        /// </summary>
        private static bool? FeatureStillInTree(IModelDoc2 modello, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (string.Equals(f.Name, name, StringComparison.Ordinal)) return true;
                    f = f.GetNextFeature() as IFeature;
                }
                return false;
            }
            catch { return null; }
        }

        /// <summary>
        /// The words of <see cref="TogliLoSpecchio"/>: «removed» only when the
        /// deletion answered true AND the tree read again no longer holds the
        /// mirror. <paramref name="stillInTree"/> null is «not read».
        /// <paramref name="leftFile"/> is the file of the new component left on
        /// disk, or null. No interop type in the signature: Prove.Contratti
        /// calls it by reflection.
        /// </summary>
        internal static string MirrorRemovalNote(string name, bool deleted, bool? stillInTree, string leftFile)
        {
            string mirror = "the mirror «" + (name ?? "?") + "»";
            string file = string.IsNullOrEmpty(leftFile) ? "" :
                " The file «" + leftFile + "» stayed on the disk: delete it, or use another «suffisso».";
            if (!deleted)
                return " DeleteSelection2 did not remove " + mirror + ": it is still in the assembly, remove it yourself." + file;
            if (stillInTree == false)
                return " I removed " + mirror + ": reading the tree again, it is gone." + file;
            if (stillInTree == true)
                return " DeleteSelection2 answered true, but " + mirror + " is still in the tree: remove it yourself." + file;
            return " DeleteSelection2 answered true, but I could not read the tree again to check that " + mirror +
                   " is gone: look at the assembly before going on." + file;
        }

        private static string SafeNome(IFeature f)
        {
            try { return f?.Name; } catch { return null; }
        }
    }
}
