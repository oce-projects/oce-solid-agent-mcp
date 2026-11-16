using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.CustomMaterial
{
    /// <summary>
    /// CUSTOM MATERIALS (bridge 0.131.0, branch custom-material-131): WHERE SOLIDWORKS READS THEM, AND WHEN.
    ///
    /// House method: the probe measures the real behaviour BEFORE the operation is written, and prints
    /// numbers, never a verdict. Questions, in the order they are asked:
    ///
    ///   CustomMaterial.exe where     which folders SOLIDWORKS reads material databases from (system option
    ///                                File Locations > Material Databases), which database files it lists, which of
    ///                                those folders can be written, what the document says for the databases
    ///   CustomMaterial.exe drop      a database file written by hand into a folder SOLIDWORKS reads, while it is
    ///                                running: is it listed at once? can SetMaterialPropertyName2 use it? does the part
    ///                                weigh volume x density? is a REWRITTEN file (new density) seen, and when? what
    ///                                happens when the file is deleted again?
    ///
    /// `drop` writes ONE file («OCE probe materials.sldmat») and deletes it at the end, whatever happens. It builds its
    /// own part (a 150 x 150 x 5 plate) and closes it. It never touches the active document: every document is kept by
    /// the reference NewDocument returned.
    ///
    /// ── GUIDE PAGES READ (sldworksapi.chm, extracted 28/9/2026) ──────────────────
    ///   ISldWorks~GetMaterialDatabases.html: «Array of strings of the names of the material databases»; Remarks:
    ///       «Material database names must be unique. Do not re-use the name of a material database.»
    ///   ISldWorks~GetMaterialDatabaseCount.html, ISldWorks~GetMaterialSchemaPathName.html
    ///   IPartDoc~SetMaterialPropertyName2.html: Remarks «Click Tools > Options > System Options > File Locations >
    ///       Material Databases to verify that a folder for Database is specified. If Database is an empty string, then
    ///       the default sldmaterials is used. Otherwise, specify the material database file name and the .sldmat
    ///       filename extension.» (the existing bridge passes the name WITHOUT extension: this probe measures both)
    ///   IPartDoc~GetMaterialPropertyName2.html
    ///   ISldWorks~GetUserPreferenceStringValue.html, ~SetUserPreferenceStringValue.html with
    ///       swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases (swconst, SO_FileLocations.htm)
    ///   IModelDocExtension~CreateMassProperty.html, IMassProperty~Mass.html, ~Volume.html, ~Density.html
    ///   data\xmlschema\sldmaterials.xsd of the installation: DENS is the only property that is required;
    ///       EX, NUXY, SIGXT, SIGYLD, ALPX, KX, C are optional
    /// </summary>
    internal static class Program
    {
        private static ISldWorks _sw;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const string ProbeDatabase = "OCE probe materials";
        private const string ProbeMaterial = "OCE Probe PLA";

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: CustomMaterial.exe where|drop");
                return 2;
            }
            try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }

            switch (args[0])
            {
                case "where": return Where();
                case "drop": return Drop();
                default: Console.Error.WriteLine("unknown subcommand " + args[0]); return 2;
            }
        }

        private static void Say(string s) { Console.WriteLine(s); }

        // ─────────────────────────────────────────────────────────────────
        // where
        // ─────────────────────────────────────────────────────────────────

        private static string[] Folders()
        {
            string raw = "";
            try { raw = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases) ?? ""; }
            catch (Exception ex) { Say("GetUserPreferenceStringValue threw " + ex.GetType().Name + ": " + ex.Message); }
            Say("swFileLocationsMaterialDatabases = «" + raw + "»");
            var list = new List<string>();
            foreach (var part in raw.Split(';'))
                if (part.Trim().Length > 0) list.Add(part.Trim());
            return list.ToArray();
        }

        private static string[] Databases(string label)
        {
            string[] dbs = null;
            try { dbs = _sw.GetMaterialDatabases() as string[]; } catch (Exception ex) { Say(label + ": GetMaterialDatabases threw " + ex.Message); }
            int count = -1;
            try { count = _sw.GetMaterialDatabaseCount(); } catch { }
            Say(label + ": GetMaterialDatabases -> " + (dbs == null ? "null" : dbs.Length.ToString()) + " files, count says " + count);
            if (dbs != null) foreach (var d in dbs) Say("    " + d);
            return dbs ?? new string[0];
        }

        private static bool Writable(string folder)
        {
            try
            {
                string probe = Path.Combine(folder, "oce-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        private static int Where()
        {
            Say("SOLIDWORKS " + _sw.RevisionNumber());
            string schema = "";
            try { schema = _sw.GetMaterialSchemaPathName(); } catch { }
            Say("GetMaterialSchemaPathName -> «" + schema + "» exists " + File.Exists(schema));
            var folders = Folders();
            foreach (var f in folders)
                Say("  folder «" + f + "»: exists " + Directory.Exists(f) + ", writable " + (Directory.Exists(f) && Writable(f)) +
                    ", files: " + (Directory.Exists(f) ? string.Join(" | ", Directory.GetFiles(f, "*.sldmat")) : "-"));
            Databases("now");
            return 0;
        }

        // ─────────────────────────────────────────────────────────────────
        // drop
        // ─────────────────────────────────────────────────────────────────

        /// <summary>The database file, in the form SOLIDWORKS writes it (UTF-16, the schema header of the shipped Custom Materials file).</summary>
        private static string Sldmat(string material, double densityKgM3)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-16\" standalone=\"no\"?>\r\n");
            sb.Append("<mstns:materials xmlns:mstns=\"http://www.solidworks.com/sldmaterials\" xmlns:msdata=\"urn:schemas-microsoft-com:xml-msdata\" ");
            sb.Append("xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" ");
            sb.Append("xmlns:sldcolorswatch=\"http://www.solidworks.com/sldcolorswatch\" version=\"2008.03\">\r\n");
            sb.Append("  <curves id=\"curve0\">\r\n    <point x=\"1.0\" y=\"1.0\" />\r\n    <point x=\"2.0\" y=\"1.0\" />\r\n    <point x=\"3.0\" y=\"1.0\" />\r\n  </curves>\r\n");
            sb.Append("  <classification name=\"Plastic\">\r\n");
            sb.Append("    <material name=\"" + material + "\" description=\"probe\">\r\n");
            sb.Append("      <physicalproperties>\r\n");
            sb.Append("        <EX displayname=\"Elastic Modulus\" value=\"3.6E+09\" />\r\n");
            sb.Append("        <DENS displayname=\"Density\" value=\"" + densityKgM3.ToString("R", Inv) + "\" />\r\n");
            sb.Append("        <SIGYLD displayname=\"Yield Strength\" value=\"6.0E+07\" />\r\n");
            sb.Append("      </physicalproperties>\r\n");
            sb.Append("    </material>\r\n  </classification>\r\n</mstns:materials>\r\n");
            return sb.ToString();
        }

        private static void Write(string path, string xml)
        {
            File.WriteAllText(path, xml, new UnicodeEncoding(false, true));
        }

        private static string Template()
        {
            string p = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            foreach (var f in Directory.GetFiles(@"C:\ProgramData\SolidWorks", "*.prtdot", SearchOption.AllDirectories))
            {
                string b = f.ToLowerInvariant();
                if (b.Contains(@"\mbd\") || b.Contains("0001mm") || b.Contains("inch")) continue;
                return f;
            }
            return null;
        }

        /// <summary>A 150 x 150 x 5 mm plate. The reference NewDocument gave is kept: the active document is never asked.</summary>
        private static IModelDoc2 Plate()
        {
            var doc = (IModelDoc2)_sw.NewDocument(Template(), 0, 0, 0);
            if (doc == null) { Say("NewDocument -> null"); return null; }
            var f = (IFeature)doc.FirstFeature();
            while (f != null && f.GetTypeName2() != "RefPlane") f = (IFeature)f.GetNextFeature();
            if (f == null || !f.Select2(false, 0)) { Say("no plane to sketch on"); return doc; }
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCenterRectangle(0, 0, 0, 0.075, 0.075, 0);
            doc.SketchManager.InsertSketch(true);
            var ft = doc.FeatureManager.FeatureExtrusion2(true, false, false, 0, 0, 0.005, 0, false, false, false, false, 0, 0,
                false, false, false, false, true, true, true, 0, 0, false);
            Say("  plate: feature " + (ft != null));
            return doc;
        }

        private static void Weigh(string label, IModelDoc2 doc)
        {
            var part = (IPartDoc)doc;
            string db;
            string name = part.GetMaterialPropertyName2("", out db);
            var mp = doc.Extension.CreateMassProperty();
            double volumeM3 = mp == null ? double.NaN : mp.Volume;
            double mass = mp == null ? double.NaN : mp.Mass;
            double density = mp == null ? double.NaN : mp.Density;
            Say("  " + label + ": material «" + name + "» db «" + db + "»; volume " + (volumeM3 * 1e9).ToString("F1", Inv) +
                " mm3, mass " + mass.ToString("R", Inv) + " kg, density " + density.ToString("R", Inv) +
                ", mass / volume " + (mass / volumeM3).ToString("F3", Inv) + " kg/m3");
        }

        /// <summary>SetMaterialPropertyName2 returns void: the only evidence is the re-read.</summary>
        private static void TryApply(string label, string database, IModelDoc2 doc)
        {
            var part = (IPartDoc)doc;
            try { part.SetMaterialPropertyName2("", database, ProbeMaterial); }
            catch (Exception ex) { Say("  " + label + " (db «" + database + "»): THREW " + ex.GetType().Name + ": " + ex.Message); return; }
            Weigh(label + " (db «" + database + "»)", doc);
        }

        private static void Clear(IModelDoc2 doc)
        {
            try { ((IPartDoc)doc).SetMaterialPropertyName2("", "", ""); } catch { }
        }

        private static int Drop()
        {
            var folders = Folders();
            string folder = null;
            foreach (var f in folders) if (Directory.Exists(f) && Writable(f)) { folder = f; break; }
            if (folder == null) { Say("no writable folder among those SOLIDWORKS reads: nothing to measure"); return 1; }
            string file = Path.Combine(folder, ProbeDatabase + ".sldmat");
            Say("folder chosen (first one that can be written): «" + folder + "»");
            string prefBefore = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases);

            var docs = new List<IModelDoc2>();
            try
            {
                var before = Databases("A. before writing the file");

                Write(file, Sldmat(ProbeMaterial, 1240));
                Say("B. file written (" + new FileInfo(file).Length + " bytes), SOLIDWORKS not told");
                Databases("B. listed at once?");

                var p1 = Plate(); docs.Add(p1);
                Weigh("C0. plate with no material", p1);
                TryApply("C1. apply, name without extension", ProbeDatabase, p1);
                Clear(p1);
                TryApply("C2. apply, name with .sldmat", ProbeDatabase + ".sldmat", p1);
                Clear(p1);
                TryApply("C3. apply, full path", file, p1);
                Clear(p1);

                Say("D. SetUserPreferenceStringValue(swFileLocationsMaterialDatabases, same value)");
                bool set = _sw.SetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases, prefBefore);
                Say("  returned " + set + ", value now «" + _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases) + "»");
                Databases("D. listed after the refresh?");
                TryApply("D1. apply, name without extension", ProbeDatabase, p1);
                Clear(p1);

                Say("E. the file is REWRITTEN with density 1000 (replace)");
                Write(file, Sldmat(ProbeMaterial, 1000));
                var p2 = Plate(); docs.Add(p2);
                TryApply("E1. fresh plate, no refresh", ProbeDatabase, p2);
                Clear(p2);
                _sw.SetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases, prefBefore);
                TryApply("E2. same plate, after the refresh", ProbeDatabase, p2);
                Clear(p2);

                Say("F. the file is DELETED");
                File.Delete(file);
                Databases("F0. listed with the file gone, no refresh?");
                TryApply("F1. apply with the file gone, no refresh", ProbeDatabase, p2);
                Clear(p2);
                _sw.SetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases, prefBefore);
                Databases("F2. listed after the refresh?");
                TryApply("F3. apply with the file gone, after the refresh", ProbeDatabase, p2);
                return 0;
            }
            finally
            {
                try { if (File.Exists(file)) File.Delete(file); } catch { }
                try { _sw.SetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases, prefBefore); } catch { }
                foreach (var d in docs)
                    try { _sw.CloseDoc(d.GetTitle()); } catch { }
                Say("clean: file exists " + File.Exists(file) + ", folders value restored: " +
                    (_sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases) == prefBefore));
            }
        }
    }
}
