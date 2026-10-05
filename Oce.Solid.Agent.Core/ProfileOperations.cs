using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// QUALI PROFILI CI SONO, E COME SI SCRIVONO. (voce GA0)
    ///
    /// ── UN'OPERAZIONE CHE IL CATALOGO CHIEDEVA E CHE NON C'ERA ────────
    ///
    /// `structural_member` e `rectangular_frame` dicono, nella loro stessa
    /// descrizione: *«PRIMA CHIAMA "weldment_profiles" e copia da li'
    /// "profile_path" e "configuration" ALLA LETTERA»*.
    ///
    /// **Quell'operazione non esisteva.** Il nome compariva quattro volte nel
    /// catalogo — tutte e quattro dentro il testo di chi la chiedeva — e zero
    /// volte fra i nomi eseguibili. Il modello linguistico leggeva
    /// un'istruzione che non portava da nessuna parte, e l'unica cosa che
    /// poteva fare era **inventarsi** un percorso `.sldlfp` e un nome di misura.
    ///
    /// E inventarsi la misura non da' errore: `OperazioniSaldatura` lo dice
    /// gia' nel suo commento — *«se la misura non c'e', SOLIDWORKS NON
    /// fallisce: usa la configurazione attiva del profilo. Il pezzo esce con
    /// una parete diversa da quella confermata, il rebuild e' pulito, e nessuno
    /// se ne accorge fino all'officina»*. C'e' un controllo che lo prende, ed
    /// e' buono — ma puo' scattare solo **dopo** che qualcuno ha indovinato.
    /// Questa operazione toglie l'indovinare.
    ///
    /// ── SI LEGGE SENZA APRIRE NIENTE ──────────────────────────────────
    ///
    /// `ISldWorks.GetConfigurationNames(percorso)` legge le misure di un
    /// `.sldlfp` **senza aprire il documento**: nessuna finestra, nessun
    /// documento in piu' fra quelli aperti, niente da richiudere. E' la stessa
    /// chiamata che il controllo di `structural_member` usa gia'.
    ///
    /// ── DOVE STA LA LIBRERIA ──────────────────────────────────────────
    ///
    /// La cartella si scavalca con la variabile d'ambiente
    /// `PROFILI_SALDATURA`, come fa gia' il banco. Un percorso costante nel
    /// codice vorrebbe dire che chi clona il repository deve modificarlo per
    /// far girare qualsiasi cosa di carpenteria.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>Quanti profili si elencano prima di troncare.</summary>
        private const int MaxProfiles = 40;

        /// <summary>Quante misure si elencano per profilo.</summary>
        private const int MaxMeasurementsPerProfile = 24;

        private Outcome WeldmentProfiles(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string folder = ProfilesFolder();

            // DUE MESSAGGI E NON UNO. `Directory.Exists(null)` risponde `false`
            // senza sollevare niente, quindi con la variabile non impostata il
            // rifiuto diceva «Non trovo la cartella dei profili: ««»» — un
            // percorso vuoto fra virgolette, che si legge come un guasto e non
            // come una cosa da fare. *Non impostata e sbagliata sono due
            // problemi diversi e chiedono due azioni diverse.*
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
                    // IL PERCORSO SI DA' INTERO E SI COPIA INTERO. E' quello
                    // che `structural_member` vuole in `profile_path`, e
                    // ricostruirlo da nome e cartella e' il modo di sbagliarlo.
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
                        // NON SI TACE. Un profilo di cui non si sono potute
                        // leggere le misure e' diverso da uno senza misure, e
                        // tutti e due sono diversi da uno a posto.
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
        /// La cartella della libreria dei profili. Si dice con
        /// `PROFILI_SALDATURA`, la stessa variabile che usa il banco: due nomi
        /// diversi per la stessa cosa sono due cose che un giorno divergono.
        ///
        /// NON C'E' PIU' UN RIPIEGO, e prima c'era: il percorso della libreria
        /// normata di chi ha scritto questo codice, su un disco di rete che
        /// esiste su una postazione sola. Su ogni altra macchina non esisteva e
        /// l'operazione falliva comunque - cioe' il ripiego non ha mai aiutato
        /// nessuno tranne chi l'aveva scritto, e intanto pubblicava com'e'
        /// fatto quel disco.
        ///
        /// Vuoto vuol dire vuoto, e chi chiama lo dice.
        /// </summary>
        private static string ProfilesFolder()
        {
            var its = System.Environment.GetEnvironmentVariable("PROFILI_SALDATURA");
            return string.IsNullOrWhiteSpace(its) ? null : its;
        }
    }
}
