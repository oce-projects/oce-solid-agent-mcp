using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.SaveHang
{
    /// <summary>
    /// THE SAVE THAT FAILED AFTER A LINKED PROPERTY (the integrator, 13/10/2026 about 06:40), MEASURED BEFORE
    /// ANYTHING IS CHANGED.
    ///
    /// The defect: an L bracket in sheet metal (base flange, hole, slot, chamfer) was saved once with
    /// `save_document`; then three `set_property` (one was LinkedMass = "SW-Mass@BRACKET_L.SLDPRT") and a second
    /// `save_document` answered swGenericSaveError, and every later Save3/SaveAs of that part failed (a new part saved
    /// fine). Then SOLIDWORKS sat at 100 % CPU on its UI thread and a CloseDoc never returned. The same save failure had been
    /// seen on 12/10/2026. The suspects: the linked value, the sheet-metal part, the path, the order.
    ///
    /// House method: the probe writes NOTHING that is not its own and prints numbers, never a verdict. Every trial
    /// opens a COPY of a saved part under the probe's folder (the original is never opened), does ONE thing to it,
    /// and then tries Save3 and SaveAs on it, with the error and warning masks and the file's timestamp and size
    /// read back. A call that does not come back in OCE_SAVEHANG_LIMIT seconds (default 120) is reported by the
    /// watchdog and the probe exits with 3: a hung COM call cannot be cancelled, so the probe says WHICH one and stops.
    ///
    ///   SaveHang.exe state                          the open documents: title, path, dirty flag (no change)
    ///   SaveHang.exe trial RECIPE SOURCE FOLDER     one recipe on a copy of SOURCE (recipes below)
    ///
    /// Recipes: control (SetSaveFlag only), plain (a text property), linked (LinkedMass = "SW-Mass@file"),
    /// linked-config (the same on the configuration's list), linked-cfg-syntax ("SW-Mass@@config@file"),
    /// three (the three of the integrator, in order), linked-read (linked, then Get6 as the bridge does),
    /// linked-rebuild (linked, ForceRebuild3), linked-undo (inside StartRecordingUndoObject),
    /// linked-other-file (an expression naming ANOTHER file), linked-wrongcase (the file name in other case),
    /// text-only-quotes (the same text without the quotes), resaved (linked, save, then a second property, save).
    ///
    /// -- GUIDE PAGES READ (sldworksapi.chm, extracted 28/9/2026) ------------------
    ///   ICustomPropertyManager~Add3.html (FieldType swCustomInfoType_e, OverwriteExisting swCustomPropertyAddOption_e;
    ///       result swCustomInfoAddResult_e), ~Get6.html (ValOut and ResolvedValOut, UseCached, WasResolved),
    ///       ~LinkProperty.html (only a value of the form that links, per swCustomLinkSetResult_e),
    ///       ~Set2.html
    ///   IModelDoc2~Save3.html, IModelDocExtension~SaveAs.html (Errors swFileSaveError_e, Warnings swFileSaveWarning_e)
    ///   IModelDoc2~GetSaveFlag.html, ~SetSaveFlag.html, ~ForceRebuild3.html
    ///   IModelDocExtension~StartRecordingUndoObject.html, ~FinishRecordingUndoObject.html
    ///   ISldWorks~OpenDoc6.html, ~CloseDoc.html
    /// </summary>
    internal static class Program
    {
        private static ISldWorks _sw;
        private static volatile string _what;
        private static DateTime _since;
        private static readonly StringBuilder Log = new StringBuilder();

        private static void Say(string line)
        {
            Console.WriteLine(line);
            Console.Out.Flush();
        }

        /// <summary>A call is about to start: the watchdog will say it if it does not come back.</summary>
        private static void Begin(string what) { _since = DateTime.UtcNow; _what = what; }
        private static void End() { _what = null; }

        private static void Watchdog(int limitSeconds)
        {
            var t = new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(2000);
                    string w = _what;
                    if (w != null && (DateTime.UtcNow - _since).TotalSeconds > limitSeconds)
                    {
                        Say("HANG: '" + w + "' has not come back in " + limitSeconds + " s. A COM call cannot be cancelled: " +
                            "the probe stops here. SOLIDWORKS is not touched.");
                        System.Environment.Exit(3);
                    }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private static int Main(string[] args)
        {
            if (args.Length == 0) { Say("usage: SaveHang.exe state | trial RECIPE SOURCE FOLDER"); return 2; }
            int limit = 120;
            string fromEnv = System.Environment.GetEnvironmentVariable("OCE_SAVEHANG_LIMIT");
            if (!string.IsNullOrWhiteSpace(fromEnv)) int.TryParse(fromEnv, out limit);
            Watchdog(limit);

            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Say("SOLIDWORKS is not open."); return 2; }
            _sw = (ISldWorks)raw;

            if (args[0] == "state") return State();
            if (args[0] == "trial" && args.Length >= 4) return Trial(args[1], args[2], args[3]);
            Say("usage: SaveHang.exe state | trial RECIPE SOURCE FOLDER");
            return 2;
        }

        private static int State()
        {
            Begin("GetDocuments");
            var docs = _sw.GetDocuments() as object[];
            End();
            if (docs == null) { Say("no documents"); return 0; }
            foreach (object o in docs)
            {
                var d = o as IModelDoc2;
                if (d == null) continue;
                Begin("read a document");
                string line = d.GetTitle() + " | " + d.GetPathName() + " | dirty " + d.GetSaveFlag();
                End();
                Say(line);
            }
            return 0;
        }

        private static string Stamp(string path)
        {
            if (!File.Exists(path)) return "no file";
            var f = new FileInfo(path);
            return f.Length + " bytes, written " + f.LastWriteTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static void TrySave(IModelDoc2 d, string path, string label)
        {
            string before = Stamp(path);
            int e = 0, w = 0;
            Begin(label + " Save3");
            DateTime t0 = DateTime.UtcNow;
            bool ok = d.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref e, ref w);
            End();
            Say("  " + label + " Save3: ok=" + ok + " errors=" + e + " warnings=" + w + " in " +
                (DateTime.UtcNow - t0).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s; file before [" + before + "] after [" + Stamp(path) + "]");
            e = 0; w = 0;
            Begin(label + " SaveAs same path");
            t0 = DateTime.UtcNow;
            ok = d.Extension.SaveAs(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w);
            End();
            Say("  " + label + " SaveAs same path: ok=" + ok + " errors=" + e + " warnings=" + w + " in " +
                (DateTime.UtcNow - t0).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s; file [" + Stamp(path) + "]");
        }

        private static string Add(IModelDoc2 d, string configuration, string name, string value)
        {
            var pm = d.Extension.get_CustomPropertyManager(configuration) as ICustomPropertyManager;
            if (pm == null) return "no property manager";
            Begin("Add3 " + name);
            int r = pm.Add3(name, (int)swCustomInfoType_e.swCustomInfoText, value,
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            End();
            string read, resolved;
            bool was, link;
            Begin("Get6 " + name);
            int g = pm.Get6(name, false, out read, out resolved, out was, out link);
            End();
            return "Add3(" + name + ") = " + r + " -> Get6 = " + g + " value [" + read + "] resolved [" + resolved + "]";
        }

        private static int Trial(string recipe, string source, string folder)
        {
            Directory.CreateDirectory(folder);
            string name = "T_" + recipe.Replace("-", "_");
            string path = Path.Combine(folder, name + ".SLDPRT");
            File.Copy(source, path, true);
            new FileInfo(path).IsReadOnly = false;
            string file = name + ".SLDPRT";
            Say("recipe " + recipe + " on " + path);

            int e = 0, w = 0;
            Begin("OpenDoc6");
            var d = _sw.OpenDoc6(path, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref e, ref w) as IModelDoc2;
            End();
            if (d == null) { Say("  OpenDoc6 gave null, errors=" + e + " warnings=" + w); return 1; }
            string title = d.GetTitle();
            string active = "";
            try { active = ((IConfigurationManager)d.ConfigurationManager).ActiveConfiguration.Name; } catch { }
            Say("  opened '" + title + "', active configuration '" + active + "', dirty " + d.GetSaveFlag());

            try
            {
                switch (recipe)
                {
                    case "control":
                        d.SetSaveFlag();
                        break;
                    case "plain":
                        Say("  " + Add(d, "", "Description", "BRACKET L"));
                        break;
                    case "linked":
                        Say("  " + Add(d, "", "LinkedMass", "\"SW-Mass@" + file + "\""));
                        break;
                    case "linked-config":
                        Say("  " + Add(d, active, "LinkedMass", "\"SW-Mass@" + file + "\""));
                        break;
                    case "linked-cfg-syntax":
                        Say("  " + Add(d, "", "LinkedMass", "\"SW-Mass@@" + active + "@" + file + "\""));
                        break;
                    case "three":
                        Say("  " + Add(d, "", "Description", "BRACKET L"));
                        Say("  " + Add(d, "", "Material", "S235JR"));
                        Say("  " + Add(d, "", "LinkedMass", "\"SW-Mass@" + file + "\""));
                        break;
                    case "linked-read":
                        Say("  " + Add(d, "", "LinkedMass", "\"SW-Mass@" + file + "\""));
                        {
                            var pm = d.Extension.get_CustomPropertyManager("") as ICustomPropertyManager;
                            string v, r; bool a, b;
                            Begin("Get6 again");
                            pm.Get6("LinkedMass", true, out v, out r, out a, out b);
                            End();
                            Say("  Get6 cached: [" + v + "] [" + r + "]");
                        }
                        break;
                    case "linked-rebuild":
                        Say("  " + Add(d, "", "LinkedMass", "\"SW-Mass@" + file + "\""));
                        Begin("ForceRebuild3");
                        bool rb = d.ForceRebuild3(false);
                        End();
                        Say("  ForceRebuild3 = " + rb);
                        break;
                    case "linked-undo":
                        d.Extension.StartRecordingUndoObject();
                        Say("  " + Add(d, "", "LinkedMass", "\"SW-Mass@" + file + "\""));
                        TrySave(d, path, "inside the undo recording");
                        Begin("FinishRecordingUndoObject");
                        bool fin = d.Extension.FinishRecordingUndoObject("probe");
                        End();
                        Say("  FinishRecordingUndoObject = " + fin);
                        break;
                    case "linked-other-file":
                        Say("  " + Add(d, "", "LinkedMass", "\"SW-Mass@NoSuchPart.SLDPRT\""));
                        break;
                    case "linked-wrongcase":
                        Say("  " + Add(d, "", "LinkedMass", "\"SW-Mass@" + file.ToLowerInvariant() + "\""));
                        break;
                    case "text-only-quotes":
                        Say("  " + Add(d, "", "LinkedMass", "SW-Mass@" + file));
                        break;
                    case "resaved":
                        Say("  " + Add(d, "", "LinkedMass", "\"SW-Mass@" + file + "\""));
                        TrySave(d, path, "first save with the link");
                        Say("  " + Add(d, "", "Description", "BRACKET L"));
                        break;
                    default:
                        Say("  unknown recipe");
                        return 2;
                }
                TrySave(d, path, "after " + recipe);
            }
            finally
            {
                Begin("CloseDoc");
                DateTime t0 = DateTime.UtcNow;
                _sw.CloseDoc(title);
                End();
                Say("  closed '" + title + "' in " + (DateTime.UtcNow - t0).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            }
            return 0;
        }
    }
}
