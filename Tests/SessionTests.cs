using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// `Session.SameDocument()` — LA GUARDIA CHE NON AVEVA UNA PROVA.
    ///
    /// E' la funzione che impedisce al ponte di lavorare sul documento di
    /// qualcun altro: piu' sessioni sullo stesso SOLIDWORKS, il documento
    /// «active» e' stato globale condiviso, e il 14/9/2026 un blocco estruso e'
    /// finito nel pezzo dell'altra sessione. Fino a oggi quella guardia non
    /// aveva **nemmeno una prova**.
    ///
    /// Confronta due dizionari. Non tocca SOLIDWORKS, e infatti sta qui.
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

            // ── IL PERCORSO VINCE SUL TITOLO ─────────────────────────────
            //
            // E' l'unica cosa che non cambia. Due documenti col titolo uguale e
            // il percorso diverso sono due pezzi diversi, e prenderli per uno
            // e' esattamente il difetto da cui questa funzione difende.
            Check("stesso percorso, titolo diverso: lo stesso documento",
                  equal(id(@"S:\a\b.SLDPRT", "Parte1"), id(@"S:\a\b.SLDPRT", "Parte2")));
            Check("percorso diverso, titolo uguale: NON lo stesso documento",
                  !equal(id(@"S:\a\b.SLDPRT", "Parte1"), id(@"S:\a\c.SLDPRT", "Parte1")));

            // ── LE DUE BARRE SONO LA STESSA BARRA ────────────────────────
            Check("«/» e «\\» sono lo stesso percorso",
                  equal(id("S:/a/b.SLDPRT", ""), id(@"S:\a\b.SLDPRT", "")));
            Check("maiuscole e minuscole non contano",
                  equal(id(@"S:\A\B.sldprt", ""), id(@"s:\a\b.SLDPRT", "")));
            Check("gli spazi attorno non contano",
                  equal(id(@"  S:\a\b.SLDPRT  ", ""), id(@"S:\a\b.SLDPRT", "")));

            // ── I MAI SALVATI SI CONFRONTANO PER TITOLO ──────────────────
            Check("senza percorso, il titolo decide",
                  equal(id("", "Parte286"), id("", "Parte286")));
            Check("senza percorso, titoli diversi: documenti diversi",
                  !equal(id("", "Parte286"), id("", "Parte287")));

            // ── DUE IDENTITA' VUOTE NON SONO LO STESSO DOCUMENTO ─────────
            //
            // E' il caso che conta: «non so chi sei» e «non so chi sei» non
            // fanno «siete la stessa persona». Rispondere di si' qui vorrebbe
            // dire lasciar passare qualunque documento.
            Check("due identita' vuote NON sono lo stesso documento",
                  !equal(id("", ""), id("", "")));
            Check("null non e' lo stesso di niente", !equal(null, id("", "Parte1")));
            Check("null e null non sono lo stesso documento", !equal(null, null));

            // ── UNO SALVATO E UNO NO: diversi ────────────────────────────
            Check("uno salvato e uno mai salvato, stesso titolo: diversi",
                  !equal(id(@"S:\a\Parte1.SLDPRT", "Parte1"), id("", "Parte1")));

            TheTurnKey(bridge);
            TheTurnFires(bridge);
            ThePartHasChanged(bridge);
            NotInstalledOrNotOpen(bridge);
            TheFileVersion(bridge);
        }

        /// <summary>
        /// LE PROPRIETA' DEL FILE DICONO LA VERSIONE (voce H14).
        ///
        /// Tasto destro -> Proprieta' -> Dettagli sul ponte e sull'installatore
        /// diceva 0.0.0.0: la versione vera era solo una risorsa letta a run
        /// time. Adesso un bersaglio di MSBuild la scrive da VERSION.txt, e qui
        /// si rilegge dal FILE, come la legge chi guarda — non dalla risorsa.
        /// L'installatore si controlla solo se e' stato costruito; se non c'e'
        /// lo si dice, e non si da' verde.
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
        /// «NON INSTALLATO» E «NON APERTO» SONO DUE COSE (voce H16).
        ///
        /// Un solo `catch (COMException)` dava la stessa frase per tutte e due,
        /// e chi installa per conto di qualcuno faceva aprire un programma che
        /// non c'era. Le tre risposte si provano per codice, senza SOLIDWORKS.
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
        /// IL LUCCHETTO FRA DUE PONTI, VISTO SCATTARE (voce W2.9).
        ///
        /// La voce era «scritta il 16/9/2026, da vedere scattare» da otto
        /// giorni, e diceva che per vederla servivano due ponti veri su un
        /// documento vero — cioe' SOLIDWORKS. Non e' cosi': il lucchetto e' un
        /// mutex con nome in `Local\`, e un mutex con nome lo tiene qualunque
        /// processo della stessa sessione. Qui lo tiene un SECONDO PROCESSO
        /// (una PowerShell figlia), e `Take` deve rispondere `null` — che e'
        /// il rifiuto del ponte. Poi il processo muore senza rilasciarlo, e
        /// `Take` deve riuscire: Windows dice «abbandonato», e il ponte lo
        /// tratta come libero.
        ///
        /// *Un controllo mai visto scattare non e' un controllo* — e questo si
        /// poteva vedere da sempre, senza il CAD.
        ///
        /// COSA NON PROVA: che il ponte, in `Session.cs`, chiami `Take` sul
        /// documento giusto. Quella meta' vuole due ponti e SOLIDWORKS.
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

            // Un percorso che non esiste e che nessun ponte vero sta usando:
            // il lucchetto e' suo e basta.
            var fake = new FakeDocument(
                @"C:\prova-turno\" + Guid.NewGuid().ToString("N") + ".sldprt", "prova");
            var key = keyOf.Invoke(null, new object[] { fake, null }) as string;
            if (string.IsNullOrEmpty(key)) { Check("la chiave di prova c'e'", false, ""); return; }
            string itemName = @"Local\OceSolidAgent_" + key;

            Func<IDisposable> test = () =>
                take.Invoke(null, new object[] { fake, null }) as IDisposable;

            // 1. libero: si prende, e rilasciandolo torna libero.
            var t1 = test();
            Check("libero: il turno si prende", t1 != null);
            if (t1 != null) t1.Dispose();

            // 2. lo tiene UN ALTRO PROCESSO: il ponte deve rifiutare.
            //    La figlia prende il mutex, scrive «preso» e aspetta.
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

                // 3. il processo MUORE senza rilasciare: il turno torna libero.
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

            // 4. un altro documento non e' toccato dal lucchetto del primo.
            var tA = test();
            var other = new FakeDocument(@"C:\prova-turno\altro-" + Guid.NewGuid().ToString("N") + ".sldprt", "altro");
            var tB = take.Invoke(null, new object[] { other, null }) as IDisposable;
            Check("un documento diverso non aspetta il lucchetto del primo", tA != null && tB != null);
            if (tA != null) tA.Dispose();
            if (tB != null) tB.Dispose();
        }

        /// <summary>
        /// «E' lo stesso documento?» e «e' ancora lo stesso pezzo?» (voce X2).
        ///
        /// `SameDocument` risponde alla prima e basta: titolo e percorso
        /// combaciano benissimo mentre qualcuno tira una quota a mano nella
        /// finestra di SOLIDWORKS. `WhatChanged` risponde alla seconda,
        /// confrontando due firme leggere — volume, numero di lavorazioni,
        /// data di scrittura del file.
        ///
        /// SI PROVA QUI E NON SUL CAD perche' e' una funzione pura su due
        /// dizionari: metterla dietro a un documento vero vorrebbe dire una
        /// prova che gira solo su questa macchina, per misurare del confronto
        /// di stringhe.
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

            // ── NIENTE DI CAMBIATO E' `null`, NON UNA FRASE VUOTA ────────
            //
            // Il chiamante scrive `if (cambiato != null)`. Una stringa vuota
            // passerebbe quel controllo e fermerebbe «build» dicendo niente:
            // il peggiore dei due modi di sbagliare, perche' l'avviso c'e' e
            // non si capisce di cosa parli.
            Check("due firme uguali: non e' cambiato niente",
                  whichOne(signature(96000.0, 3), signature(96000.0, 3)) == null,
                  Convert.ToString(whichOne(signature(96000.0, 3), signature(96000.0, 3)) ?? "(null)"));

            // ── IL VOLUME, COL NUMERO DI PRIMA E QUELLO DI ADESSO ────────
            //
            // «Il volume e' cambiato» obbliga a fidarsi; «da 96000 a 94500» si
            // controlla. E' il criterio scritto nella voce X2, ed e' la
            // differenza fra un avviso che si legge e uno che si impara a
            // ignorare.
            var v = whichOne(signature(96000.0, 3), signature(94500.0, 3));
            Check("il volume cambiato si dice, con i due numeri",
                  v != null && v.Contains("96000") && v.Contains("94500"), v ?? "(null)");

            var l = whichOne(signature(96000.0, 3), signature(96000.0, 4));
            Check("una lavorazione in piu' si dice, con i due conti",
                  l != null && l.Contains("3") && l.Contains("4"), l ?? "(null)");

            // ── DUE COSE CAMBIATE SI DICONO TUTTE E DUE ──────────────────
            //
            // Fermarsi alla prima differenza farebbe leggere «il volume e'
            // cambiato» a chi ha anche una lavorazione in piu', e la seconda
            // verrebbe fuori solo al giro dopo.
            var due = whichOne(signature(96000.0, 3), signature(94500.0, 4));
            Check("due differenze si dicono tutte e due",
                  due != null && due.Contains("volume") && due.Contains("lavorazioni"),
                  due ?? "(null)");

            // ── «NON C'ERA» E «ADESSO C'E'» E' UNA DIFFERENZA ───────────
            //
            // Un documento vuoto non ha volume. Trattare la chiave mancante
            // come zero direbbe «il volume e' passato da 0 a 96000», che e' una
            // frase su un pezzo che non e' mai esistito.
            var born = whichOne(signature(null, 0), signature(96000.0, 1));
            Check("da «nessun volume» a un volume: e' una differenza",
                  born != null && born.Contains("nessuno"), born ?? "(null)");

            // ── SENZA UN TERMINE NON SI CONFRONTA ────────────────────────
            //
            // `null` vuol dire «non ho guardato niente», e non e' «e' tutto
            // uguale» ne' «e' cambiato tutto»: e' l'assenza della domanda. Se
            // rispondesse «changed», «build» si fermerebbe sempre alla prima
            // chiamata dopo un ponte appena partito.
            Check("senza la firma di prima non si dice niente",
                  whichOne(null, signature(96000.0, 3)) == null);
            Check("senza la firma di adesso non si dice niente",
                  whichOne(signature(96000.0, 3), null) == null);

            // ── LA DATA DEL FILE ─────────────────────────────────────────
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
        /// LA CHIAVE DEL LUCCHETTO FRA PROCESSI (voce W2.9).
        ///
        /// Che due processi si escludano a vicenda lo garantisce Windows: non
        /// c'e' niente da provare li'. **Quello che si puo' sbagliare e' la
        /// chiave**, e sbagliarla ha due facce, tutt'e due silenziose:
        ///
        ///   - due documenti DIVERSI con la stessa chiave si bloccano a vicenda
        ///     senza motivo, e chi lavora vede un rifiuto che non capisce;
        ///   - lo stesso documento con due chiavi diverse **non si blocca**, e
        ///     il lucchetto non serve a niente pur sembrando esserci.
        ///
        /// La seconda e' quella pericolosa: la difesa c'e', si vede nel codice,
        /// e non protegge. E' il difetto che non da' errore applicato a un
        /// lucchetto.
        ///
        /// Si prova per riflessione su `KeyOf`, che e' privata: i documenti
        /// sono finti — basta che rispondano a `GetPathName` e `GetTitle`,
        /// perche' il ponte li interroga cosi'.
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

            // IL CASO CHE UN TRONCAMENTO AVREBBE ROTTO: due file nella stessa
            // cartella profondissima, diversi solo per l'ultima lettera. Con la
            // chiave ricavata dal percorso troncato sarebbero la stessa cosa, e
            // due pezzi diversi si bloccherebbero a vicenda.
            string along = @"S:\" + new string('x', 200) + @"\";
            Check("due file in una cartella profondissima restano distinti",
                  key(along + "uno.sldprt", "") != key(along + "due.sldprt", ""),
                  "e' il caso che un troncamento del percorso avrebbe fuso");

            Check("senza percorso si usa il titolo",
                  key("", "Parte286") != null &&
                  key("", "Parte286") == key("", "Parte286"));
            Check("titoli diversi, chiavi diverse",
                  key("", "Parte286") != key("", "Parte287"));

            // NIENTE DI NIENTE: nessuna chiave, e quindi nessun lucchetto.
            // Bloccare su una chiave inventata unirebbe documenti diversi e
            // lascerebbe passare quelli uguali: farebbe l'opposto di cio' che
            // serve.
            Check("senza percorso NE titolo non c'e' chiave", key("", "") == null,
                  "un lucchetto su una chiave inventata e' peggio di nessun lucchetto");
        }

        /// <summary>
        /// Un documento finto: risponde solo alle due domande che il ponte fa.
        ///
        /// `DocumentTurn` interroga per riflessione (`InvokeMember`), quindi
        /// non serve implementare `IModelDoc2` — cosa che qui, senza SOLIDWORKS,
        /// non si potrebbe nemmeno fare.
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
