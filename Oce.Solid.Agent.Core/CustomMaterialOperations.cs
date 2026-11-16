using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// A material that no SOLIDWORKS library has, written into a library of the bridge (bridge 0.131.0, rule R5).
    ///
    /// THE DEFECT THIS FILE WAS BORN FROM
    /// On 12 October 2026 a plate was to be printed in PLA. PLA is in no material library of this workstation:
    /// `apply_material` found «Custom Plastic» and «Plastiche personalizzate» (the empty custom plastic of the
    /// custom library, in English and in Italian), called them ambiguous and stopped. Nothing else was possible,
    /// and that is right: the bridge must not pick a plastic for a material it does not know. What was missing
    /// was a way to TELL it what PLA is, once, with a source, so that the next `apply_material` finds it.
    ///
    /// WHAT IS DONE HERE
    /// `create_material` writes the material into ONE database file owned by the bridge,
    /// «OCE custom materials.sldmat», in a folder SOLIDWORKS already reads for material databases
    /// (system option File Locations > Material Databases, read with
    /// `GetUserPreferenceStringValue(swFileLocationsMaterialDatabases)`). The file is never edited in
    /// place: it is written beside itself and moved over the old one, so SOLIDWORKS never reads half of it.
    /// Then it is read back THROUGH SOLIDWORKS (the database must be listed by `GetMaterialDatabases` and the
    /// bridge's own search must find the name in it), and only then does the answer say «created».
    ///
    /// WHY THE SOURCE IS NOT OPTIONAL
    /// A density written from memory is how a part gets a false weight that nobody checks again. The source (a
    /// supplier datasheet, a standard) is written into the database entry (`propertysource`) and returned in the
    /// answer, so the number can be traced by whoever finds it a year later.
    ///
    /// ── GUIDE PAGES READ (sldworksapi.chm, extracted 28/9/2026) ──────────────────
    ///   ISldWorks~GetMaterialDatabases.html — «Material database names must be unique. Do not re-use the name of
    ///       a material database.» (hence the name of the file, which is the name of the database, is fixed and
    ///       not derived from the material)
    ///   IPartDoc~SetMaterialPropertyName2.html — Remarks: the folder of the database must be specified in
    ///       Tools > Options > System Options > File Locations > Material Databases
    ///   ISldWorks~GetUserPreferenceStringValue.html / ~SetUserPreferenceStringValue.html with
    ///       swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases
    ///   data\xmlschema\sldmaterials.xsd of the installation (GetMaterialSchemaPathName): `DENS` is the only
    ///       required property; the others are optional and come in the order of the schema.
    /// The measurements are in Checks\CustomMaterial (see the CHANGELOG entry «Part of 0.131.0 — custom materials»).
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// The database the bridge owns. SOLIDWORKS names a database by its file name without extension.
        /// Public because the MCP layer reads it too: the material check before a plan lets through a name
        /// that an earlier «create_material» step of the same plan writes into this library.
        /// </summary>
        public const string BridgeMaterialLibrary = "OCE custom materials";

        private static readonly XNamespace MaterialsNs = "http://www.solidworks.com/sldmaterials";

        /// <summary>One property of a material: the tag of the schema, the label SOLIDWORKS shows, the factor to SI.</summary>
        private sealed class MaterialValue
        {
            public MaterialProperty Property;
            public double Amount;
        }

        private sealed class MaterialProperty
        {
            public string Tag, Caption, Argument;
            public double ToSi;
            public double Min, Max;
            public bool MinOpen, MaxOpen;
        }

        // The order is the order of the schema (sldmaterials.xsd): EX, NUXY, ALPX, DENS, KX, C, SIGXT, SIGYLD.
        // Limits are the physical ones, not the ones of a catalogue: they catch a unit slip (a modulus given in GPa
        // where MPa is asked is a thousand times too small) without refusing a real material.
        private static readonly MaterialProperty[] MaterialProperties =
        {
            new MaterialProperty { Tag = "EX", Caption = "Elastic Modulus", Argument = "elastic_modulus_mpa", ToSi = 1e6, Min = 0, Max = 1500000, MinOpen = true },
            new MaterialProperty { Tag = "NUXY", Caption = "Poisson's Ratio", Argument = "poisson_ratio", ToSi = 1, Min = 0, Max = 0.5, MinOpen = true, MaxOpen = true },
            new MaterialProperty { Tag = "ALPX", Caption = "Thermal Expansion Coefficient", Argument = "thermal_expansion_per_k", ToSi = 1, Min = 0, Max = 0.001, MinOpen = true },
            new MaterialProperty { Tag = "DENS", Caption = "Density", Argument = "density_kg_m3", ToSi = 1, Min = 0, Max = 25000, MinOpen = true },
            new MaterialProperty { Tag = "KX", Caption = "Thermal Conductivity", Argument = "thermal_conductivity_w_mk", ToSi = 1, Min = 0, Max = 1000, MinOpen = true },
            new MaterialProperty { Tag = "C", Caption = "Specific Heat", Argument = "specific_heat_j_kgk", ToSi = 1, Min = 0, Max = 20000, MinOpen = true },
            new MaterialProperty { Tag = "SIGXT", Caption = "Tensile Strength in X", Argument = "tensile_strength_mpa", ToSi = 1e6, Min = 0, Max = 10000, MinOpen = true },
            new MaterialProperty { Tag = "SIGYLD", Caption = "Yield Strength", Argument = "yield_strength_mpa", ToSi = 1e6, Min = 0, Max = 10000, MinOpen = true },
        };

        private Outcome CreateMaterial(IModelDoc2 model, Dictionary<string, object> a)
        {
            string name = (Str(a, "name", "") ?? "").Trim();
            string source = (Str(a, "source", "") ?? "").Trim();
            string className = (Str(a, "class", "") ?? "").Trim();
            string description = (Str(a, "description", "") ?? "").Trim();
            string folderAsked = (Str(a, "folder", "") ?? "").Trim();
            bool replace = Bool(a, "replace", false);
            if (className.Length == 0) className = "Custom";

            // ── WHAT IS REFUSED BEFORE ANYTHING IS WRITTEN ───────────────
            if (name.Length == 0) return Failed("The name of the material is missing («name»).");
            if (name.Length > 100)
                return Failed("The name of the material is " + name.Length + " characters long; SOLIDWORKS shows it in a list " +
                              "and in the cut list. Keep it under 100.");
            foreach (char bad in new[] { '|', '<', '>', '"', '&', '\r', '\n', '\t' })
                if (name.IndexOf(bad) >= 0 || className.IndexOf(bad) >= 0)
                    return Failed("The name of the material and its class may not contain «" + (bad < ' ' ? "a control character" : bad.ToString()) +
                                  "»: SOLIDWORKS keeps «database|material» pairs and a name with it can not be told back. Write it without.");
            if (source.Length == 0)
                return Failed("«source» is missing. A material whose density nobody can trace gives a false weight that is " +
                              "never checked again: write where the numbers come from (the supplier datasheet with its " +
                              "code or date, a standard, or «measured here, <date>»).");

            // Arrays and counters, not List.Add: the obsolete-call scanner can not type the receiver of an Add.
            MaterialValue[] values = new MaterialValue[MaterialProperties.Length];
            int valueCount = 0;
            foreach (MaterialProperty property in MaterialProperties)
            {
                object raw;
                if (a == null || !a.TryGetValue(property.Argument, out raw) || raw == null)
                {
                    if (property.Tag == "DENS")
                        return Failed("«density_kg_m3» is missing. Without density there is no weight; it is the one property " +
                                      "a material can not do without.");
                    continue;
                }
                double v;
                string why;
                if (!RequiredNum(a, property.Argument, out v, out why)) return Failed(why);
                bool lowOk = property.MinOpen ? v > property.Min : v >= property.Min;
                bool highOk = property.MaxOpen ? v < property.Max : v <= property.Max;
                if (!lowOk || !highOk)
                    return Failed("«" + property.Argument + "» is " + v.ToString(CultureInfo.InvariantCulture) +
                                  ", which is outside what a material can have (" + property.Min.ToString(CultureInfo.InvariantCulture) +
                                  (property.MinOpen ? " excluded" : "") + " to " + property.Max.ToString(CultureInfo.InvariantCulture) +
                                  (property.MaxOpen ? " excluded" : "") + "). Check the unit: " + UnitOf(property) + ".");
                values[valueCount++] = new MaterialValue { Property = property, Amount = v };
            }

            // ── WHERE THE DATABASE GOES ──────────────────────────────────
            string folder, folderNote, folderWhy;
            if (!BridgeMaterialFolder(folderAsked, out folder, out folderNote, out folderWhy))
                return Failed(folderWhy);
            string path = Path.Combine(folder, BridgeMaterialLibrary + ".sldmat");

            // ── THE NAME IS UNIQUE ACROSS THE LIBRARIES ──────────────────
            // A second «PLA» in another library would make `apply_material PLA` ambiguous again, which is the
            // problem this operation exists to remove.
            SolidWorksMaterials.Invalidate();
            List<SolidWorksMaterials.Material> everything = SolidWorksMaterials.All(_sw, _log);
            foreach (SolidWorksMaterials.Material other in everything)
            {
                if (!string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                bool ours = string.Equals(other.Archive, BridgeMaterialLibrary, StringComparison.OrdinalIgnoreCase);
                if (ours && replace) continue;
                if (ours)
                    return Failed("The material «" + other.Name + "» is already in the library «" + BridgeMaterialLibrary +
                                  "». Say «replace»: true to overwrite it with these values, or choose another name.");
                return Failed("The name «" + other.Name + "» is already used in the library «" + other.Archive + "» (class «" + other.Class +
                              "»). A second material with the same name would make «apply_material» ambiguous. Choose another " +
                              "name, for example one that says the supplier or the grade.");
            }

            // ── WRITE ────────────────────────────────────────────────────
            XDocument document = null;
            if (File.Exists(path))
            {
                try { document = XDocument.Load(path); }
                catch (Exception ex)
                {
                    return Failed("The library file «" + path + "» exists but can not be read as XML (" + ex.Message +
                                  "). I did not touch it. Move it away, or fix it, and ask again.");
                }
            }
            if (document == null || document.Root == null) document = NewMaterialDocument();
            XElement root = document.Root;

            XElement entry = BuildMaterialEntry(name, description, source, values, valueCount);
            bool replaced = false;
            XElement classification = null;
            XElement[] sections = root.Elements().ToArray();
            foreach (XElement section in sections)
            {
                if (section.Name.LocalName != "classification") continue;
                if ((string)section.Attribute("name") == className) classification = section;
                XElement[] items = section.Elements().ToArray();
                foreach (XElement item in items)
                    if (item.Name.LocalName == "material" &&
                        string.Equals((string)item.Attribute("name"), name, StringComparison.OrdinalIgnoreCase))
                    {
                        replaced = true;
                        item.Remove();
                    }
            }
            // A classification left empty by the removal is dropped: SOLIDWORKS lists it as an empty folder.
            foreach (XElement section in sections)
            {
                if (section.Name.LocalName != "classification" || section.HasElements) continue;
                if (ReferenceEquals(section, classification)) continue;
                section.Remove();
            }
            if (classification == null)
            {
                // after the last element: the schema wants curves first, then the classifications
                classification = new XElement("classification", new XAttribute("name", className));
                root.Elements().Last().AddAfterSelf(classification);
            }
            // the old nodes plus the new one, in one replacement
            object[] kept = classification.Nodes().ToArray();
            classification.ReplaceNodes(kept, entry);

            string temporary = path + ".writing";
            try
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = new UnicodeEncoding(false, true);
                settings.Indent = true;
                settings.IndentChars = "  ";
                using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write))
                using (XmlWriter writer = XmlWriter.Create(stream, settings))
                    document.Save(writer);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (Exception ex)
            {
                RemoveFile(temporary);
                return Failed("I could not write the library file «" + path + "»: " + ex.Message +
                              ". The folder is one SOLIDWORKS reads, but this account can not write in it; pass «folder» with another " +
                              "folder of that list.");
            }

            // ── READ BACK, THROUGH SOLIDWORKS ────────────────────────────
            string readBackNote;
            string readBack = MakeSolidWorksReadMaterials(path, name, out readBackNote);
            if (readBack != null) return Failed(readBack);

            Dictionary<string, object> written = new Dictionary<string, object>();
            for (int i = 0; i < valueCount; i++) written[values[i].Property.Argument] = values[i].Amount;
            var measurements = new Dictionary<string, object>
            {
                ["material"] = name,
                ["library"] = BridgeMaterialLibrary,
                ["file"] = path,
                ["class_name"] = className,
                ["source"] = source,
                ["replaced"] = replaced,
                ["folder_note"] = folderNote,
                ["written"] = written,
                ["next_step"] = "apply_material {\"material\": \"" + name + "\"}",
            };
            if (!string.IsNullOrEmpty(readBackNote)) measurements["read_back_note"] = readBackNote;
            return Succeeded(null, measurements);
        }

        private static void RemoveFile(string file)
        {
            try
            {
                FileInfo info = new FileInfo(file);
                if (info.Exists) info.Delete();
            }
            catch { }
        }

        private static string UnitOf(MaterialProperty p)
        {
            switch (p.Argument)
            {
                case "elastic_modulus_mpa": return "megapascal (steel is 210000, a plastic 1000 to 4000)";
                case "tensile_strength_mpa": return "megapascal";
                case "yield_strength_mpa": return "megapascal";
                case "density_kg_m3": return "kilograms per cubic metre (water is 1000, steel 7850)";
                case "poisson_ratio": return "a pure number between 0 and 0.5";
                case "thermal_expansion_per_k": return "per kelvin (steel is 0.000012)";
                case "thermal_conductivity_w_mk": return "watts per metre kelvin";
                case "specific_heat_j_kgk": return "joules per kilogram kelvin";
                default: return "see the schema";
            }
        }

        private static XDocument NewMaterialDocument()
        {
            XNamespace msdata = "urn:schemas-microsoft-com:xml-msdata";
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            XNamespace swatch = "http://www.solidworks.com/sldcolorswatch";
            XElement root = new XElement(MaterialsNs + "materials",
                new XAttribute(XNamespace.Xmlns + "mstns", MaterialsNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "msdata", msdata.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "xsi", xsi.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "sldcolorswatch", swatch.NamespaceName),
                new XAttribute("version", "2008.03"),
                // The schema asks for one `curves` element before the classifications; the property curves are
                // not used by a material with plain values, but SOLIDWORKS's own files carry this one.
                new XElement("curves", new XAttribute("id", "curve0"),
                    new XElement("point", new XAttribute("x", "1.0"), new XAttribute("y", "1.0")),
                    new XElement("point", new XAttribute("x", "2.0"), new XAttribute("y", "1.0")),
                    new XElement("point", new XAttribute("x", "3.0"), new XAttribute("y", "1.0"))));
            return new XDocument(new XDeclaration("1.0", "utf-16", "no"), root);
        }

        private static XElement BuildMaterialEntry(
            string name, string description, string source, MaterialValue[] values, int valueCount)
        {
            object[] properties = new object[valueCount];
            for (int i = 0; i < valueCount; i++)
                properties[i] = new XElement(values[i].Property.Tag,
                    new XAttribute("displayname", values[i].Property.Caption),
                    new XAttribute("value", (values[i].Amount * values[i].Property.ToSi).ToString("R", CultureInfo.InvariantCulture)));
            XElement physical = new XElement("physicalproperties", properties);
            return new XElement("material",
                new XAttribute("name", name),
                new XAttribute("description", description.Length > 0 ? description : name),
                new XAttribute("propertysource", source),
                physical);
        }

        private static string Quoted(string[] names)
        {
            string[] quoted = new string[names.Length];
            for (int i = 0; i < names.Length; i++) quoted[i] = "«" + names[i] + "»";
            return string.Join(", ", quoted);
        }

        /// <summary>
        /// The folder the bridge's database lives in. One of the folders SOLIDWORKS reads for material databases,
        /// the first that can be written; the one that already holds the file wins, so the file does not wander.
        /// </summary>
        private bool BridgeMaterialFolder(string asked, out string folder, out string note, out string why)
        {
            folder = null; note = ""; why = null;
            string raw = "";
            try { raw = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases) ?? ""; }
            catch (Exception ex)
            {
                why = "I could not read the folders SOLIDWORKS reads material databases from (" + ex.Message + ").";
                return false;
            }
            string[] pieces = raw.Split(';');
            string[] read = new string[pieces.Length];
            int readCount = 0;
            foreach (string piece in pieces)
            {
                string trimmed = piece.Trim().TrimEnd('\\');
                if (trimmed.Length > 0) read[readCount++] = trimmed;
            }
            Array.Resize(ref read, readCount);
            if (asked.Length > 0)
            {
                string wanted = asked.Trim().TrimEnd('\\');
                bool listed = false;
                foreach (string candidate in read)
                    if (string.Equals(candidate, wanted, StringComparison.OrdinalIgnoreCase)) { listed = true; wanted = candidate; }
                if (!listed)
                {
                    why = "«" + asked + "» is not a folder SOLIDWORKS reads material databases from, so a database put there would " +
                          "not be seen. The folders it reads are: " + Quoted(read) + ". Use one of them, or leave «folder» out.";
                    return false;
                }
                read = new[] { wanted };
            }
            string holder = null;
            foreach (string candidate in read)
                if (holder == null && File.Exists(Path.Combine(candidate, BridgeMaterialLibrary + ".sldmat"))) holder = candidate;
            if (holder != null) read = new[] { holder };
            foreach (string candidate in read)
            {
                if (!Directory.Exists(candidate)) continue;
                if (!CanWriteIn(candidate)) continue;
                folder = candidate;
                note = "written in «" + candidate + "», a folder SOLIDWORKS already reads for material databases (File Locations > Material Databases); " +
                       "no option was changed";
                return true;
            }
            why = "None of the folders SOLIDWORKS reads material databases from can be written by this account: " +
                  (read.Length == 0 ? "the list is empty" : Quoted(read)) +
                  ". Add a folder you can write to in Tools > Options > System Options > File Locations > Material Databases " +
                  "and ask again.";
            return false;
        }

        private static bool CanWriteIn(string folder)
        {
            string probe = Path.Combine(folder, "oce-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(probe, "x");
                RemoveFile(probe);
                return true;
            }
            catch { RemoveFile(probe); return false; }
        }

        /// <summary>
        /// Makes SOLIDWORKS read the database just written and checks that it did. Returns null when it did,
        /// otherwise the sentence of the refusal.
        /// </summary>
        private string MakeSolidWorksReadMaterials(string path, string name, out string note)
        {
            note = null;
            // MEASUREMENT-DEPENDENT PART: see Checks\CustomMaterial. Filled in after the probe.
            SolidWorksMaterials.Invalidate();
            string why;
            SolidWorksMaterials.Material found = SolidWorksMaterials.Resolve(_sw, name, BridgeMaterialLibrary, _log, out why);
            if (found == null || !string.Equals(found.Name, name, StringComparison.Ordinal))
                return "I wrote «" + path + "» but my search of the material databases does not find «" + name + "» in it: " + why;
            return null;
        }
    }
}
