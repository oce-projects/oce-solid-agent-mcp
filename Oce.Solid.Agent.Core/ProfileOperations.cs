using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// WHICH PROFILES THERE ARE, AND HOW THEY ARE WRITTEN. (entry GA0)
    ///
    /// ── AN OPERATION THAT THE CATALOGUE ASKED FOR AND THAT WAS NOT THERE ──
    ///
    /// `structural_member` and `rectangular_frame` say, in their own
    /// description: *«FIRST CALL "weldment_profiles" and copy from there
    /// "profile_path" and "configuration" TO THE LETTER»*.
    ///
    /// **That operation did not exist.** The name appeared four times in the
    /// catalogue — all four inside the text of whoever asked for it — and zero
    /// times among the executable names. The language model read
    /// an instruction that led nowhere, and the only thing it
    /// could do was to **make up** an `.sldlfp` path and a size name.
    ///
    /// And making up the size gives no error: `OperazioniSaldatura` already says it
    /// in its comment — *«if the size is not there, SOLIDWORKS does NOT
    /// fail: it uses the active configuration of the profile. The part comes out with
    /// a different wall from the confirmed one, the rebuild is clean, and nobody
    /// notices until the workshop»*. There is a check that catches it, and it
    /// is good — but it can fire only **after** someone has guessed.
    /// This operation takes the guessing away.
    ///
    /// ── IT IS READ WITHOUT OPENING ANYTHING ──────────────────────────
    ///
    /// `ISldWorks.GetConfigurationNames(percorso)` reads the sizes of an
    /// `.sldlfp` **without opening the document**: no window, no
    /// extra document among the open ones, nothing to close again. It is the same
    /// call that the `structural_member` check already uses.
    ///
    /// ── WHERE THE LIBRARY IS ─────────────────────────────────────────
    ///
    /// The folder is overridden with the environment variable
    /// `PROFILI_SALDATURA`, as the bench already does. A constant path in the
    /// code would mean that whoever clones the repository has to modify it to
    /// run anything that involves steel structures.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>How many profiles are listed before truncating.</summary>
        private const int MaxProfiles = 40;

        /// <summary>How many sizes are listed per profile.</summary>
        private const int MaxMeasurementsPerProfile = 24;

        private Outcome WeldmentProfiles(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string folder = ProfilesFolder();

            // TWO MESSAGES AND NOT ONE. `Directory.Exists(null)` answers `false`
            // without raising anything, so with the variable unset the
            // refusal said «Non trovo la cartella dei profili: ««»» — an
            // empty path between quotation marks, which reads as a failure and not
            // as something to do. *Unset and wrong are two different
            // problems and ask for two different actions.*
            if (folder == null)
                return Failed(
                    "Non so dove sia la libreria dei profili. E' una cartella di file " +
                    ".sldlfp, uno per forma: dimmi dov'e' con la variabile d'ambiente " +
                    "PROFILI_SALDATURA. Non c'e' un percorso predefinito, perche' " +
                    "ogni officina tiene la propria libreria dove vuole.");

            if (!Directory.Exists(folder))
                return Failed(
                    "Non trovo la cartella dei profili: «" + folder + "», che e' quello " +
                    "che dice PROFILI_SALDATURA. La libreria e' una cartella di file " +
                    ".sldlfp; controlla il percorso.");

            string searched = Str(a, "search", null);
            bool withMeasurements = Bool(a, "with_measurements", true);

            string[] file;
            try { file = Directory.GetFiles(folder, "*.sldlfp", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                return Failed("Non riesco a leggere la cartella dei profili: " + ex.Message);
            }
            if (file.Length == 0)
                return Failed(
                    "Nella cartella «" + folder + "» non c'e' nessun file .sldlfp. E' la " +
                    "cartella giusta? I profili di saldatura sono file .sldlfp, uno per " +
                    "forma, e ognuno contiene tutte le misure come configurazioni.");

            var kept = file.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searched))
                kept = kept.Where(f =>
                    Path.GetFileNameWithoutExtension(f)
                        .IndexOf(searched, StringComparison.OrdinalIgnoreCase) >= 0);

            var listed = kept.OrderBy(f => Path.GetFileNameWithoutExtension(f),
                                          StringComparer.OrdinalIgnoreCase).ToList();
            if (listed.Count == 0)
                return Failed(
                    "Nessun profilo si chiama come «" + searched + "». Ce ne sono " +
                    file.Length + " in tutto: richiama senza «search» per vederli.");

            int truncated = Math.Max(0, listed.Count - MaxProfiles);
            var entries = new List<Dictionary<string, object>>();
            int withoutMeasurements = 0;

            foreach (var f in listed.Take(MaxProfiles))
            {
                var entry = new Dictionary<string, object>
                {
                    ["name"] = Path.GetFileNameWithoutExtension(f),
                    // THE PATH IS GIVEN WHOLE AND COPIED WHOLE. It is what
                    // `structural_member` wants in `profile_path`, and
                    // rebuilding it from name and folder is the way to get it wrong.
                    ["profile_path"] = f,
                };

                if (withMeasurements)
                {
                    string[] measurements = null;
                    try { measurements = _sw.GetConfigurationNames(f) as string[]; }
                    catch { }
                    if (measurements == null || measurements.Length == 0)
                    {
                        withoutMeasurements++;
                        // IT IS NOT KEPT QUIET. A profile whose sizes could not be
                        // read is different from one without sizes, and
                        // both are different from one that is fine.
                        entry["measurements"] = null;
                        entry["note"] =
                            "Non sono riuscito a leggere le misure di questo profilo: non " +
                            "copiarne una a memoria.";
                    }
                    else
                    {
                        entry["how_many_measurements"] = measurements.Length;
                        entry["measurements"] = measurements.Take(MaxMeasurementsPerProfile).ToArray();
                        if (measurements.Length > MaxMeasurementsPerProfile)
                            entry["measurements_not_listed"] = measurements.Length - MaxMeasurementsPerProfile;
                    }
                }
                entries.Add(entry);
            }

            var totalMeasurements = new Dictionary<string, object>
            {
                ["folder"] = folder,
                ["profiles_total"] = file.Length,
                ["profiles_listed"] = entries.Count,
                ["profiles"] = entries.ToArray(),
                ["how_to_use_it"] =
                    "Copia «profile_path» e una delle «measurements» ALLA LETTERA dentro " +
                    "«structural_member» o «rectangular_frame», in «profile_path» e " +
                    "«configuration». Alla lettera davvero: maiuscole, spazi e virgole " +
                    "contano, e una misura che non c'e' non da' errore -- SOLIDWORKS usa " +
                    "quella attiva del profilo, e il pezzo esce con una parete diversa da " +
                    "quella che credi.",
            };
            if (truncated > 0)
            {
                totalMeasurements["profiles_not_listed"] = truncated;
                totalMeasurements["truncated"] = true;
                totalMeasurements["how_to_see_them_all"] =
                    "Ce ne sono altri " + truncated + ". Richiama con «search» e un pezzo del " +
                    "nome, per esempio «tube» o «IPE».";
            }
            if (!string.IsNullOrWhiteSpace(searched)) totalMeasurements["search"] = searched;
            if (withoutMeasurements > 0) totalMeasurements["profiles_without_readable_measurements"] = withoutMeasurements;

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] profili di saldatura: {0} in «{1}», elencati {2}",
                file.Length, folder, entries.Count));

            return SucceededWithoutCreating(totalMeasurements);
        }

        /// <summary>
        /// The folder of the profile library. It is given with
        /// `PROFILI_SALDATURA`, the same variable the bench uses: two different
        /// names for the same thing are two things that one day diverge.
        ///
        /// THERE IS NO FALLBACK ANY MORE, and there used to be one: the path of the standards
        /// library of whoever wrote this code, on a network drive that
        /// exists on one workstation only. On every other machine it did not exist and
        /// the operation failed anyway - that is, the fallback never helped
        /// anyone except whoever had written it, and meanwhile it published how that
        /// drive is laid out.
        ///
        /// Empty means empty, and whoever calls says so.
        /// </summary>
        private static string ProfilesFolder()
        {
            var its = System.Environment.GetEnvironmentVariable("PROFILI_SALDATURA");
            return string.IsNullOrWhiteSpace(its) ? null : its;
        }
    }
}
