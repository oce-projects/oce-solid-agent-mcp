using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// `Session.SameDocument()` — THE GUARD THAT HAD NO CHECK.
    ///
    /// It is the function that stops the bridge from working on somebody
    /// else's document: several sessions on the same SOLIDWORKS, the
    /// «active» document is a shared global, and on 14/9/2026 an extruded block
    /// ended up in the part of the other session. Until today that guard
    /// had **not even one check**.
    ///
    /// It compares two dictionaries. It does not touch SOLIDWORKS, and indeed it is here.
    /// </summary>
    internal static class SessionTests
    {
        internal static void All()
        {
            Group("Session.SameDocument");

            var bridge = BridgeOrNotHere("Session.SameDocument, per intero");
            if (bridge == null) return;

            MethodInfo same;
            try
            {
                var asm = Load.Bridge(bridge);
                var typ = Load.Class(asm, "Session");
                if (typ == null) { Check("la classe Session c'e'", false, ""); return; }
                same = typ.GetMethod("SameDocument",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (same == null) { Check("SameDocument c'e'", false, ""); return; }
            }
            catch (Exception ex) { Check("il ponte si carica", false, ex.ToString()); return; }

            Func<Dictionary<string, object>, Dictionary<string, object>, bool> equal =
                (a, b) => (bool)same.Invoke(null, new object[] { a, b });

            Func<string, string, Dictionary<string, object>> id = (filePath, docTitle) =>
                new Dictionary<string, object> { ["path"] = filePath, ["document_title"] = docTitle };

            // ── THE PATH WINS OVER THE TITLE ─────────────────────────────
            //
            // It is the only thing that does not change. Two documents with the same title and
            // a different path are two different parts, and taking them for one
            // is exactly the defect that this function defends against.
            Check("stesso percorso, titolo diverso: lo stesso documento",
                  equal(id(@"S:\a\b.SLDPRT", "Parte1"), id(@"S:\a\b.SLDPRT", "Parte2")));
            Check("percorso diverso, titolo uguale: NON lo stesso documento",
                  !equal(id(@"S:\a\b.SLDPRT", "Parte1"), id(@"S:\a\c.SLDPRT", "Parte1")));

            // ── THE TWO BACKSLASHES ARE THE SAME BACKSLASH ───────────────
            Check("«/» e «\\» sono lo stesso percorso",
                  equal(id("S:/a/b.SLDPRT", ""), id(@"S:\a\b.SLDPRT", "")));
            Check("maiuscole e minuscole non contano",
                  equal(id(@"S:\A\B.sldprt", ""), id(@"s:\a\b.SLDPRT", "")));
            Check("gli spazi attorno non contano",
                  equal(id(@"  S:\a\b.SLDPRT  ", ""), id(@"S:\a\b.SLDPRT", "")));

            // ── THE NEVER-SAVED ARE COMPARED BY TITLE ────────────────────
            Check("senza percorso, il titolo decide",
                  equal(id("", "Parte286"), id("", "Parte286")));
            Check("senza percorso, titoli diversi: documenti diversi",
                  !equal(id("", "Parte286"), id("", "Parte287")));

            // ── TWO EMPTY IDENTITIES ARE NOT THE SAME DOCUMENT ───────────
            //
            // It is the case that counts: «I do not know who you are» and «I do not know who you are» do not
            // make «you are the same person». Answering yes here would mean
            // letting any document through.
            Check("due identita' vuote NON sono lo stesso documento",
                  !equal(id("", ""), id("", "")));
            Check("null non e' lo stesso di niente", !equal(null, id("", "Parte1")));
            Check("null e null non sono lo stesso documento", !equal(null, null));

            // ── ONE SAVED AND ONE NOT: different ─────────────────────────
            Check("uno salvato e uno mai salvato, stesso titolo: diversi",
                  !equal(id(@"S:\a\Parte1.SLDPRT", "Parte1"), id("", "Parte1")));

            TheTurnKey(bridge);
            TheTurnFires(bridge);
            ThePartHasChanged(bridge);
            NotInstalledOrNotOpen(bridge);
            TheFileVersion(bridge);
        }

        /// <summary>
        /// THE PROPERTIES OF THE FILE SAY THE VERSION (entry H14).
        ///
        /// Right click -> Properties -> Details on the bridge and on the installer
        /// said 0.0.0.0: the real version was only a resource read at run
        /// time. Now an MSBuild target writes it from VERSION.txt, and here
        /// it is read back from the FILE, as whoever looks reads it — not from the resource.
        /// The installer is checked only if it was built; if it is not there
        /// that is said, and no green is given.
        /// </summary>
        private static void TheFileVersion(string bridge)
        {
            Group("le proprieta' del file dicono la versione (H14)");
            string expectation;
            try { expectation = File.ReadAllText(Path.Combine(Root(), "VERSION.txt")).Trim(); }
            catch (Exception ex) { Check("VERSION.txt si legge", false, ex.Message); return; }

            var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(bridge);
            Check("il ponte: versione del file = VERSION.txt",
                  vi.FileVersion == expectation + ".0", "file " + vi.FileVersion + ", VERSION.txt " + expectation);
            Check("il ponte: versione del prodotto = VERSION.txt",
                  vi.ProductVersion == expectation, "prodotto " + vi.ProductVersion);

            var install = Path.Combine(Root(), "dist", "Installer.exe");
            if (!File.Exists(install))
            {
                Check("l'installatore c'e' (dist\\Installer.exe) per controllarne la versione", false,
                      "non costruito: .\\build.ps1");
                return;
            }
            var vj = System.Diagnostics.FileVersionInfo.GetVersionInfo(install);
            Check("l'installatore: versione del file = VERSION.txt",
                  vj.FileVersion == expectation + ".0", "file " + vj.FileVersion + ", VERSION.txt " + expectation);
        }

        /// <summary>
        /// «NOT INSTALLED» AND «NOT OPEN» ARE TWO THINGS (entry H16).
        ///
        /// A single `catch (COMException)` gave the same sentence for both,
        /// and whoever installs on behalf of somebody made a program open that
        /// was not there. The three answers are tested by code, without SOLIDWORKS.
        /// Since entry H19 (B3) the installer reads the exit code of --prova and
        /// no sentence (H19InstallerContract checks the codes): these checks
        /// keep the two sentences for a person apart, nothing more.
        /// </summary>
        private static void NotInstalledOrNotOpen(string bridge)
        {
            Group("Session.AttachMessage: non installato o non aperto (H16)");
            MethodInfo m;
            try
            {
                var typ = Load.Class(Load.Bridge(bridge), "Session");
                m = typ?.GetMethod("AttachMessage", BindingFlags.NonPublic | BindingFlags.Static);
                if (m == null) { Check("AttachMessage c'e'", false, ""); return; }
            }
            catch (Exception ex) { Check("il ponte si carica", false, ex.ToString()); return; }

            Func<uint, string> sentence = c => (string)m.Invoke(null, new object[] { unchecked((int)c) });

            var open = sentence(0x800401E3);
            Check("MK_E_UNAVAILABLE: the sentence says SOLIDWORKS is not running",
                  open.IndexOf("non risulta aperto", StringComparison.Ordinal) >= 0, open);
            foreach (var c in new uint[] { 0x800401F3, 0x80040154 })
            {
                var t = sentence(c);
                Check("0x" + c.ToString("X8") + ": dice «non e' installato»",
                      t.IndexOf("non e' installato", StringComparison.Ordinal) >= 0, t);
                Check("0x" + c.ToString("X8") + ": and does NOT say it is not running (it is not installed)",
                      t.IndexOf("non risulta aperto", StringComparison.Ordinal) < 0, t);
            }
            var other = sentence(0x80004005);
            Check("un codice qualunque si dice in esadecimale", other.Contains("0x80004005"), other);
        }

        /// <summary>
        /// THE LOCK BETWEEN TWO BRIDGES, SEEN TRIGGERING (entry W2.9).
        ///
        /// The entry had been «written on 16/9/2026, to be seen triggering» for eight
        /// days, and said that to see it two real bridges on a real
        /// document were needed — that is SOLIDWORKS. It is not so: the lock is a
        /// named mutex in `Local\`, and a named mutex is held by any
        /// process of the same session. Here it is held by a SECOND PROCESS
        /// (a child PowerShell), and `Take` must answer `null` — which is
        /// the refusal of the bridge. Then the process dies without releasing it, and
        /// `Take` must succeed: Windows says «abandoned», and the bridge
        /// treats it as free.
        ///
        /// *A check never seen triggering is not a check* — and this one
        /// could have been seen all along, without the CAD.
        ///
        /// WHAT IT DOES NOT TEST: that the bridge, in `Session.cs`, calls `Take` on the
        /// right document. That half wants two bridges and SOLIDWORKS.
        /// </summary>
        private static void TheTurnFires(string bridge)
        {
            Group("DocumentTurn: il lucchetto scatta fra due processi (W2.9)");

            MethodInfo take, keyOf;
            try
            {
                var asm = Load.Bridge(bridge);
                var typ = Load.Class(asm, "DocumentTurn");
                if (typ == null) { Check("la classe DocumentTurn c'e'", false, ""); return; }
                take = typ.GetMethod("Take", BindingFlags.Public | BindingFlags.Static);
                keyOf = typ.GetMethod("KeyOf", BindingFlags.NonPublic | BindingFlags.Static);
                if (take == null || keyOf == null) { Check("Take e KeyOf ci sono", false, ""); return; }
            }
            catch (Exception ex) { Check("DocumentTurn si carica", false, ex.ToString()); return; }

            // A path that does not exist and that no real bridge is using:
            // the lock is its own and that is all.
            var fake = new FakeDocument(
                @"C:\prova-turno\" + Guid.NewGuid().ToString("N") + ".sldprt", "prova");
            var key = keyOf.Invoke(null, new object[] { fake, null }) as string;
            if (string.IsNullOrEmpty(key)) { Check("la chiave di prova c'e'", false, ""); return; }
            string itemName = @"Local\OceSolidAgent_" + key;

            Func<IDisposable> test = () =>
                take.Invoke(null, new object[] { fake, null }) as IDisposable;

            // 1. free: it is taken, and by releasing it it becomes free again.
            var t1 = test();
            Check("libero: il turno si prende", t1 != null);
            if (t1 != null) t1.Dispose();

            // 2. held by ANOTHER PROCESS: the bridge must refuse.
            //    The child takes the mutex, writes «preso» and waits.
            string ready = Path.Combine(Path.GetTempPath(), "turno-" + key.Substring(0, 12) + ".txt");
            try { File.Delete(ready); } catch { }
            string command =
                "$m = New-Object System.Threading.Mutex($false, '" + itemName + "'); " +
                "[void]$m.WaitOne(); Set-Content -Path '" + ready + "' -Value preso; " +
                "Start-Sleep -Seconds 20";
            System.Diagnostics.Process child = null;
            try
            {
                child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -Command \"" + command + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                var within = DateTime.UtcNow.AddSeconds(15);
                while (!File.Exists(ready) && DateTime.UtcNow < within && !child.HasExited)
                    System.Threading.Thread.Sleep(100);
                if (!File.Exists(ready))
                {
                    Check("un secondo processo tiene il lucchetto", false,
                          "la PowerShell figlia non ha preso il mutex entro 15 s");
                    return;
                }

                var t2 = test();
                Check("lo tiene un ALTRO processo: il ponte rifiuta", t2 == null,
                      t2 == null ? "Take -> null, cioe' «il documento e' in uso da un altro ponte»"
                                 : "Take ha dato il turno anche se un altro processo lo teneva");
                if (t2 != null) t2.Dispose();

                // 3. the process DIES without releasing: the turn becomes free again.
                child.Kill();
                child.WaitForExit(5000);
                var t3 = test();
                Check("chi lo teneva e' morto senza rilasciarlo: il turno si riprende", t3 != null,
                      "un lucchetto che resta chiuso dopo che chi l'ha chiuso e' morto non e' un lucchetto, e' un guasto");
                if (t3 != null) t3.Dispose();
            }
            finally
            {
                try { if (child != null && !child.HasExited) child.Kill(); } catch { }
                try { File.Delete(ready); } catch { }
            }

            // 4. another document is not touched by the lock of the first.
            var tA = test();
            var other = new FakeDocument(@"C:\prova-turno\altro-" + Guid.NewGuid().ToString("N") + ".sldprt", "altro");
            var tB = take.Invoke(null, new object[] { other, null }) as IDisposable;
            Check("un documento diverso non aspetta il lucchetto del primo", tA != null && tB != null);
            if (tA != null) tA.Dispose();
            if (tB != null) tB.Dispose();
        }

        /// <summary>
        /// «Is it the same document?» and «is it still the same part?» (entry X2).
        ///
        /// `SameDocument` answers the first and nothing more: title and path
        /// match perfectly well while somebody drags a dimension by hand in the
        /// SOLIDWORKS window. `WhatChanged` answers the second,
        /// comparing two light signatures — volume, number of features,
        /// file write date.
        ///
        /// IT IS TESTED HERE AND NOT ON THE CAD because it is a pure function on two
        /// dictionaries: putting it behind a real document would mean a
        /// check that runs only on this machine, to measure a string
        /// comparison.
        /// </summary>
        private static void ThePartHasChanged(string bridge)
        {
            Group("McpTools.WhatChanged");

            MethodInfo changed;
            try
            {
                var asm = Load.Bridge(bridge);
                var typ = Load.Class(asm, "McpTools");
                if (typ == null) { Check("la classe McpTools c'e'", false, ""); return; }
                changed = typ.GetMethod("WhatChanged",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (changed == null) { Check("WhatChanged c'e'", false, ""); return; }
            }
            catch (Exception ex) { Check("il ponte si carica", false, ex.ToString()); return; }

            Func<Dictionary<string, object>, Dictionary<string, object>, string> whichOne =
                (a, b) => (string)changed.Invoke(null, new object[] { a, b });

            Func<object, object, Dictionary<string, object>> signature = (vol, feat) =>
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                if (vol != null) d["volume_mm3"] = vol;
                if (feat != null) d["features"] = feat;
                return d;
            };

            // ── NOTHING CHANGED IS `null`, NOT AN EMPTY SENTENCE ─────────
            //
            // The caller writes `if (cambiato != null)`. An empty string
            // would pass that check and would stop «build» saying nothing:
            // the worse of the two ways of getting it wrong, because the warning is there and
            // it is not clear what it is about.
            Check("due firme uguali: non e' cambiato niente",
                  whichOne(signature(96000.0, 3), signature(96000.0, 3)) == null,
                  Convert.ToString(whichOne(signature(96000.0, 3), signature(96000.0, 3)) ?? "(null)"));

            // ── THE VOLUME, WITH THE PREVIOUS NUMBER AND THE CURRENT ONE ──
            //
            // «The volume has changed» obliges one to trust; «da 96000 a 94500» can be
            // checked. It is the criterion written in entry X2, and it is the
            // difference between a warning that is read and one that one learns to
            // ignore.
            var v = whichOne(signature(96000.0, 3), signature(94500.0, 3));
            Check("il volume cambiato si dice, con i due numeri",
                  v != null && v.Contains("96000") && v.Contains("94500"), v ?? "(null)");

            var l = whichOne(signature(96000.0, 3), signature(96000.0, 4));
            Check("una lavorazione in piu' si dice, con i due conti",
                  l != null && l.Contains("3") && l.Contains("4"), l ?? "(null)");

            // ── TWO THINGS CHANGED ARE BOTH TOLD ─────────────────────────
            //
            // Stopping at the first difference would make «the volume has
            // changed» be read by whoever also has one more feature, and the second
            // would come out only on the next run.
            var due = whichOne(signature(96000.0, 3), signature(94500.0, 4));
            Check("due differenze si dicono tutte e due",
                  due != null && due.Contains("volume") && due.Contains("lavorazioni"),
                  due ?? "(null)");

            // ── «IT WAS NOT THERE» AND «NOW IT IS» IS A DIFFERENCE ───────
            //
            // An empty document has no volume. Treating the missing key
            // as zero would say «the volume went from 0 to 96000», which is a
            // sentence about a part that has never existed.
            var born = whichOne(signature(null, 0), signature(96000.0, 1));
            Check("da «nessun volume» a un volume: e' una differenza",
                  born != null && born.Contains("nessuno"), born ?? "(null)");

            // ── WITHOUT A TERM, NO COMPARISON ────────────────────────────
            //
            // `null` means «I looked at nothing», and it is not «everything is the
            // same» nor «everything has changed»: it is the absence of the question. If it
            // answered «changed», «build» would always stop at the first
            // call after a bridge that has just started.
            Check("senza la firma di prima non si dice niente",
                  whichOne(null, signature(96000.0, 3)) == null);
            Check("senza la firma di adesso non si dice niente",
                  whichOne(signature(96000.0, 3), null) == null);

            // ── THE DATE OF THE FILE ─────────────────────────────────────
            var written1 = new Dictionary<string, object>(StringComparer.Ordinal)
                { ["file_written"] = "2026-09-23T05:00:00.0000000Z" };
            var written2 = new Dictionary<string, object>(StringComparer.Ordinal)
                { ["file_written"] = "2026-09-23T07:00:00.0000000Z" };
            var rewritten = whichOne(written1, written2);
            Check("un file riscritto si dice", rewritten != null && rewritten.Contains("riscritto"),
                  rewritten ?? "(null)");
            Check("stessa data: non e' cambiato niente", whichOne(written1, written1) == null);
        }

        /// <summary>
        /// THE KEY OF THE LOCK BETWEEN PROCESSES (entry W2.9).
        ///
        /// That two processes exclude each other is guaranteed by Windows: there is
        /// nothing to test there. **What can go wrong is the
        /// key**, and getting it wrong has two faces, both silent:
        ///
        ///   - two DIFFERENT documents with the same key block each other
        ///     for no reason, and whoever works sees a refusal they do not understand;
        ///   - the same document with two different keys **does not block**, and
        ///     the lock is of no use although it seems to be there.
        ///
        /// The second is the dangerous one: the defense is there, it can be seen in the code,
        /// and it does not protect. It is the defect that gives no error applied to a
        /// lock.
        ///
        /// It is tested by reflection on `KeyOf`, which is private: the documents
        /// are fake — it is enough that they answer `GetPathName` and `GetTitle`,
        /// because the bridge interrogates them that way.
        /// </summary>
        private static void TheTurnKey(string bridge)
        {
            Group("DocumentTurn: la chiave");

            MethodInfo keyOf;
            try
            {
                var asm = Load.Bridge(bridge);
                var typ = Load.Class(asm, "DocumentTurn");
                if (typ == null) { Check("la classe DocumentTurn c'e'", false, ""); return; }
                keyOf = typ.GetMethod("KeyOf",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (keyOf == null) { Check("KeyOf c'e'", false, ""); return; }
            }
            catch (Exception ex) { Check("DocumentTurn si carica", false, ex.ToString()); return; }

            Func<string, string, string> key = (filePath, docTitle) =>
            {
                var arg = new object[] { new FakeDocument(filePath, docTitle), null };
                return keyOf.Invoke(null, arg) as string;
            };

            Check("lo stesso percorso da' la stessa chiave",
                  key(@"S:\a\b.sldprt", "b") == key(@"S:\a\b.sldprt", "altro"),
                  "e il titolo non c'entra: conta il file");
            Check("maiuscole diverse, stessa chiave",
                  key(@"S:\A\B.SLDPRT", "") == key(@"s:\a\b.sldprt", ""),
                  "su Windows due percorsi che differiscono per il caso sono lo stesso file");
            Check("percorsi diversi, chiavi diverse",
                  key(@"S:\a\b.sldprt", "") != key(@"S:\a\c.sldprt", ""));

            // THE CASE THAT A TRUNCATION WOULD HAVE BROKEN: two files in the same
            // very deep folder, different only in the last letter. With the
            // key derived from the truncated path they would be the same thing, and
            // two different parts would block each other.
            string along = @"S:\" + new string('x', 200) + @"\";
            Check("due file in una cartella profondissima restano distinti",
                  key(along + "uno.sldprt", "") != key(along + "due.sldprt", ""),
                  "e' il caso che un troncamento del percorso avrebbe fuso");

            Check("senza percorso si usa il titolo",
                  key("", "Parte286") != null &&
                  key("", "Parte286") == key("", "Parte286"));
            Check("titoli diversi, chiavi diverse",
                  key("", "Parte286") != key("", "Parte287"));

            // NOTHING IN, NOTHING OUT: no key, and therefore no lock.
            // Locking on an invented key would merge different documents and
            // let the equal ones through: it would do the opposite of what
            // is needed.
            Check("senza percorso NE titolo non c'e' chiave", key("", "") == null,
                  "un lucchetto su una chiave inventata e' peggio di nessun lucchetto");
        }

        /// <summary>
        /// A fake document: it answers only the two questions that the bridge asks.
        ///
        /// `DocumentTurn` interrogates by reflection (`InvokeMember`), so
        /// there is no need to implement `IModelDoc2` — something that here, without SOLIDWORKS,
        /// could not even be done.
        /// </summary>
        private sealed class FakeDocument
        {
            private readonly string _filePath, _title;
            public FakeDocument(string filePath, string docTitle)
            {
                _filePath = filePath; _title = docTitle;
            }
            public string GetPathName() { return _filePath; }
            public string GetTitle() { return _title; }
        }
    }
}
