using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// `Sessione.StessoDocumento()` — LA GUARDIA CHE NON AVEVA UNA PROVA.
    ///
    /// E' la funzione che impedisce al ponte di lavorare sul documento di
    /// qualcun altro: piu' sessioni sullo stesso SOLIDWORKS, il documento
    /// «attivo» e' stato globale condiviso, e il 14/9/2026 un blocco estruso e'
    /// finito nel pezzo dell'altra sessione. Fino a oggi quella guardia non
    /// aveva **nemmeno una prova**.
    ///
    /// Confronta due dizionari. Non tocca SOLIDWORKS, e infatti sta qui.
    /// </summary>
    internal static class ProveSessione
    {
        internal static void Tutte()
        {
            Gruppo("Sessione.StessoDocumento");

            var ponte = PonteOppureNonQui("Sessione.StessoDocumento, per intero");
            if (ponte == null) return;

            MethodInfo stesso;
            try
            {
                var asm = Carica.Ponte(ponte);
                var tipo = Carica.Classe(asm, "Sessione");
                if (tipo == null) { Segna("la classe Sessione c'e'", false, ""); return; }
                stesso = tipo.GetMethod("StessoDocumento",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (stesso == null) { Segna("StessoDocumento c'e'", false, ""); return; }
            }
            catch (Exception ex) { Segna("il ponte si carica", false, ex.ToString()); return; }

            Func<Dictionary<string, object>, Dictionary<string, object>, bool> uguali =
                (a, b) => (bool)stesso.Invoke(null, new object[] { a, b });

            Func<string, string, Dictionary<string, object>> id = (percorso, titolo) =>
                new Dictionary<string, object> { ["percorso"] = percorso, ["titolo"] = titolo };

            // ── IL PERCORSO VINCE SUL TITOLO ─────────────────────────────
            //
            // E' l'unica cosa che non cambia. Due documenti col titolo uguale e
            // il percorso diverso sono due pezzi diversi, e prenderli per uno
            // e' esattamente il difetto da cui questa funzione difende.
            Segna("stesso percorso, titolo diverso: lo stesso documento",
                  uguali(id(@"S:\a\b.SLDPRT", "Parte1"), id(@"S:\a\b.SLDPRT", "Parte2")));
            Segna("percorso diverso, titolo uguale: NON lo stesso documento",
                  !uguali(id(@"S:\a\b.SLDPRT", "Parte1"), id(@"S:\a\c.SLDPRT", "Parte1")));

            // ── LE DUE BARRE SONO LA STESSA BARRA ────────────────────────
            Segna("«/» e «\\» sono lo stesso percorso",
                  uguali(id("S:/a/b.SLDPRT", ""), id(@"S:\a\b.SLDPRT", "")));
            Segna("maiuscole e minuscole non contano",
                  uguali(id(@"S:\A\B.sldprt", ""), id(@"s:\a\b.SLDPRT", "")));
            Segna("gli spazi attorno non contano",
                  uguali(id(@"  S:\a\b.SLDPRT  ", ""), id(@"S:\a\b.SLDPRT", "")));

            // ── I MAI SALVATI SI CONFRONTANO PER TITOLO ──────────────────
            Segna("senza percorso, il titolo decide",
                  uguali(id("", "Parte286"), id("", "Parte286")));
            Segna("senza percorso, titoli diversi: documenti diversi",
                  !uguali(id("", "Parte286"), id("", "Parte287")));

            // ── DUE IDENTITA' VUOTE NON SONO LO STESSO DOCUMENTO ─────────
            //
            // E' il caso che conta: «non so chi sei» e «non so chi sei» non
            // fanno «siete la stessa persona». Rispondere di si' qui vorrebbe
            // dire lasciar passare qualunque documento.
            Segna("due identita' vuote NON sono lo stesso documento",
                  !uguali(id("", ""), id("", "")));
            Segna("null non e' lo stesso di niente", !uguali(null, id("", "Parte1")));
            Segna("null e null non sono lo stesso documento", !uguali(null, null));

            // ── UNO SALVATO E UNO NO: diversi ────────────────────────────
            Segna("uno salvato e uno mai salvato, stesso titolo: diversi",
                  !uguali(id(@"S:\a\Parte1.SLDPRT", "Parte1"), id("", "Parte1")));

            LaChiaveDelTurno(ponte);
            IlTurnoScatta(ponte);
            IlPezzoECambiato(ponte);
        }

        /// <summary>
        /// IL LUCCHETTO FRA DUE PONTI, VISTO SCATTARE (voce W2.9).
        ///
        /// La voce era «scritta il 16/9/2026, da vedere scattare» da otto
        /// giorni, e diceva che per vederla servivano due ponti veri su un
        /// documento vero — cioe' SOLIDWORKS. Non e' cosi': il lucchetto e' un
        /// mutex con nome in `Local\`, e un mutex con nome lo tiene qualunque
        /// processo della stessa sessione. Qui lo tiene un SECONDO PROCESSO
        /// (una PowerShell figlia), e `Prendi` deve rispondere `null` — che e'
        /// il rifiuto del ponte. Poi il processo muore senza rilasciarlo, e
        /// `Prendi` deve riuscire: Windows dice «abbandonato», e il ponte lo
        /// tratta come libero.
        ///
        /// *Un controllo mai visto scattare non e' un controllo* — e questo si
        /// poteva vedere da sempre, senza il CAD.
        ///
        /// COSA NON PROVA: che il ponte, in `Sessione.cs`, chiami `Prendi` sul
        /// documento giusto. Quella meta' vuole due ponti e SOLIDWORKS.
        /// </summary>
        private static void IlTurnoScatta(string ponte)
        {
            Gruppo("TurnoSulDocumento: il lucchetto scatta fra due processi (W2.9)");

            MethodInfo prendi, chiaveDi;
            try
            {
                var asm = Carica.Ponte(ponte);
                var tipo = Carica.Classe(asm, "TurnoSulDocumento");
                if (tipo == null) { Segna("la classe TurnoSulDocumento c'e'", false, ""); return; }
                prendi = tipo.GetMethod("Prendi", BindingFlags.Public | BindingFlags.Static);
                chiaveDi = tipo.GetMethod("ChiaveDi", BindingFlags.NonPublic | BindingFlags.Static);
                if (prendi == null || chiaveDi == null) { Segna("Prendi e ChiaveDi ci sono", false, ""); return; }
            }
            catch (Exception ex) { Segna("TurnoSulDocumento si carica", false, ex.ToString()); return; }

            // Un percorso che non esiste e che nessun ponte vero sta usando:
            // il lucchetto e' suo e basta.
            var finto = new DocumentoFinto(
                @"C:\prova-turno\" + Guid.NewGuid().ToString("N") + ".sldprt", "prova");
            var chiave = chiaveDi.Invoke(null, new object[] { finto, null }) as string;
            if (string.IsNullOrEmpty(chiave)) { Segna("la chiave di prova c'e'", false, ""); return; }
            string nome = @"Local\OceSolidAgent_" + chiave;

            Func<IDisposable> prova = () =>
                prendi.Invoke(null, new object[] { finto, null }) as IDisposable;

            // 1. libero: si prende, e rilasciandolo torna libero.
            var t1 = prova();
            Segna("libero: il turno si prende", t1 != null);
            if (t1 != null) t1.Dispose();

            // 2. lo tiene UN ALTRO PROCESSO: il ponte deve rifiutare.
            //    La figlia prende il mutex, scrive «preso» e aspetta.
            string pronto = Path.Combine(Path.GetTempPath(), "turno-" + chiave.Substring(0, 12) + ".txt");
            try { File.Delete(pronto); } catch { }
            string comando =
                "$m = New-Object System.Threading.Mutex($false, '" + nome + "'); " +
                "[void]$m.WaitOne(); Set-Content -Path '" + pronto + "' -Value preso; " +
                "Start-Sleep -Seconds 20";
            System.Diagnostics.Process figlia = null;
            try
            {
                figlia = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -Command \"" + comando + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                var entro = DateTime.UtcNow.AddSeconds(15);
                while (!File.Exists(pronto) && DateTime.UtcNow < entro && !figlia.HasExited)
                    System.Threading.Thread.Sleep(100);
                if (!File.Exists(pronto))
                {
                    Segna("un secondo processo tiene il lucchetto", false,
                          "la PowerShell figlia non ha preso il mutex entro 15 s");
                    return;
                }

                var t2 = prova();
                Segna("lo tiene un ALTRO processo: il ponte rifiuta", t2 == null,
                      t2 == null ? "Prendi -> null, cioe' «il documento e' in uso da un altro ponte»"
                                 : "Prendi ha dato il turno anche se un altro processo lo teneva");
                if (t2 != null) t2.Dispose();

                // 3. il processo MUORE senza rilasciare: il turno torna libero.
                figlia.Kill();
                figlia.WaitForExit(5000);
                var t3 = prova();
                Segna("chi lo teneva e' morto senza rilasciarlo: il turno si riprende", t3 != null,
                      "un lucchetto che resta chiuso dopo che chi l'ha chiuso e' morto non e' un lucchetto, e' un guasto");
                if (t3 != null) t3.Dispose();
            }
            finally
            {
                try { if (figlia != null && !figlia.HasExited) figlia.Kill(); } catch { }
                try { File.Delete(pronto); } catch { }
            }

            // 4. un altro documento non e' toccato dal lucchetto del primo.
            var tA = prova();
            var altro = new DocumentoFinto(@"C:\prova-turno\altro-" + Guid.NewGuid().ToString("N") + ".sldprt", "altro");
            var tB = prendi.Invoke(null, new object[] { altro, null }) as IDisposable;
            Segna("un documento diverso non aspetta il lucchetto del primo", tA != null && tB != null);
            if (tA != null) tA.Dispose();
            if (tB != null) tB.Dispose();
        }

        /// <summary>
        /// «E' lo stesso documento?» e «e' ancora lo stesso pezzo?» (voce X2).
        ///
        /// `StessoDocumento` risponde alla prima e basta: titolo e percorso
        /// combaciano benissimo mentre qualcuno tira una quota a mano nella
        /// finestra di SOLIDWORKS. `CosaECambiato` risponde alla seconda,
        /// confrontando due firme leggere — volume, numero di lavorazioni,
        /// data di scrittura del file.
        ///
        /// SI PROVA QUI E NON SUL CAD perche' e' una funzione pura su due
        /// dizionari: metterla dietro a un documento vero vorrebbe dire una
        /// prova che gira solo su questa macchina, per misurare del confronto
        /// di stringhe.
        /// </summary>
        private static void IlPezzoECambiato(string ponte)
        {
            Gruppo("Attrezzi.CosaECambiato");

            MethodInfo cambiato;
            try
            {
                var asm = Carica.Ponte(ponte);
                var tipo = Carica.Classe(asm, "Attrezzi");
                if (tipo == null) { Segna("la classe Attrezzi c'e'", false, ""); return; }
                cambiato = tipo.GetMethod("CosaECambiato",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (cambiato == null) { Segna("CosaECambiato c'e'", false, ""); return; }
            }
            catch (Exception ex) { Segna("il ponte si carica", false, ex.ToString()); return; }

            Func<Dictionary<string, object>, Dictionary<string, object>, string> che =
                (a, b) => (string)cambiato.Invoke(null, new object[] { a, b });

            Func<object, object, Dictionary<string, object>> firma = (vol, lav) =>
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                if (vol != null) d["volume_mm3"] = vol;
                if (lav != null) d["lavorazioni"] = lav;
                return d;
            };

            // ── NIENTE DI CAMBIATO E' `null`, NON UNA FRASE VUOTA ────────
            //
            // Il chiamante scrive `if (cambiato != null)`. Una stringa vuota
            // passerebbe quel controllo e fermerebbe «disegna» dicendo niente:
            // il peggiore dei due modi di sbagliare, perche' l'avviso c'e' e
            // non si capisce di cosa parli.
            Segna("due firme uguali: non e' cambiato niente",
                  che(firma(96000.0, 3), firma(96000.0, 3)) == null,
                  Convert.ToString(che(firma(96000.0, 3), firma(96000.0, 3)) ?? "(null)"));

            // ── IL VOLUME, COL NUMERO DI PRIMA E QUELLO DI ADESSO ────────
            //
            // «Il volume e' cambiato» obbliga a fidarsi; «da 96000 a 94500» si
            // controlla. E' il criterio scritto nella voce X2, ed e' la
            // differenza fra un avviso che si legge e uno che si impara a
            // ignorare.
            var v = che(firma(96000.0, 3), firma(94500.0, 3));
            Segna("il volume cambiato si dice, con i due numeri",
                  v != null && v.Contains("96000") && v.Contains("94500"), v ?? "(null)");

            var l = che(firma(96000.0, 3), firma(96000.0, 4));
            Segna("una lavorazione in piu' si dice, con i due conti",
                  l != null && l.Contains("3") && l.Contains("4"), l ?? "(null)");

            // ── DUE COSE CAMBIATE SI DICONO TUTTE E DUE ──────────────────
            //
            // Fermarsi alla prima differenza farebbe leggere «il volume e'
            // cambiato» a chi ha anche una lavorazione in piu', e la seconda
            // verrebbe fuori solo al giro dopo.
            var due = che(firma(96000.0, 3), firma(94500.0, 4));
            Segna("due differenze si dicono tutte e due",
                  due != null && due.Contains("volume") && due.Contains("lavorazioni"),
                  due ?? "(null)");

            // ── «NON C'ERA» E «ADESSO C'E'» E' UNA DIFFERENZA ───────────
            //
            // Un documento vuoto non ha volume. Trattare la chiave mancante
            // come zero direbbe «il volume e' passato da 0 a 96000», che e' una
            // frase su un pezzo che non e' mai esistito.
            var nato = che(firma(null, 0), firma(96000.0, 1));
            Segna("da «nessun volume» a un volume: e' una differenza",
                  nato != null && nato.Contains("nessuno"), nato ?? "(null)");

            // ── SENZA UN TERMINE NON SI CONFRONTA ────────────────────────
            //
            // `null` vuol dire «non ho guardato niente», e non e' «e' tutto
            // uguale» ne' «e' cambiato tutto»: e' l'assenza della domanda. Se
            // rispondesse «cambiato», «disegna» si fermerebbe sempre alla prima
            // chiamata dopo un ponte appena partito.
            Segna("senza la firma di prima non si dice niente",
                  che(null, firma(96000.0, 3)) == null);
            Segna("senza la firma di adesso non si dice niente",
                  che(firma(96000.0, 3), null) == null);

            // ── LA DATA DEL FILE ─────────────────────────────────────────
            var scritto1 = new Dictionary<string, object>(StringComparer.Ordinal)
                { ["file_scritto"] = "2026-09-23T05:00:00.0000000Z" };
            var scritto2 = new Dictionary<string, object>(StringComparer.Ordinal)
                { ["file_scritto"] = "2026-09-23T07:00:00.0000000Z" };
            var riscritto = che(scritto1, scritto2);
            Segna("un file riscritto si dice", riscritto != null && riscritto.Contains("riscritto"),
                  riscritto ?? "(null)");
            Segna("stessa data: non e' cambiato niente", che(scritto1, scritto1) == null);
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
        /// Si prova per riflessione su `ChiaveDi`, che e' privata: i documenti
        /// sono finti — basta che rispondano a `GetPathName` e `GetTitle`,
        /// perche' il ponte li interroga cosi'.
        /// </summary>
        private static void LaChiaveDelTurno(string ponte)
        {
            Gruppo("TurnoSulDocumento: la chiave");

            MethodInfo chiaveDi;
            try
            {
                var asm = Carica.Ponte(ponte);
                var tipo = Carica.Classe(asm, "TurnoSulDocumento");
                if (tipo == null) { Segna("la classe TurnoSulDocumento c'e'", false, ""); return; }
                chiaveDi = tipo.GetMethod("ChiaveDi",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (chiaveDi == null) { Segna("ChiaveDi c'e'", false, ""); return; }
            }
            catch (Exception ex) { Segna("TurnoSulDocumento si carica", false, ex.ToString()); return; }

            Func<string, string, string> chiave = (percorso, titolo) =>
            {
                var arg = new object[] { new DocumentoFinto(percorso, titolo), null };
                return chiaveDi.Invoke(null, arg) as string;
            };

            Segna("lo stesso percorso da' la stessa chiave",
                  chiave(@"S:\a\b.sldprt", "b") == chiave(@"S:\a\b.sldprt", "altro"),
                  "e il titolo non c'entra: conta il file");
            Segna("maiuscole diverse, stessa chiave",
                  chiave(@"S:\A\B.SLDPRT", "") == chiave(@"s:\a\b.sldprt", ""),
                  "su Windows due percorsi che differiscono per il caso sono lo stesso file");
            Segna("percorsi diversi, chiavi diverse",
                  chiave(@"S:\a\b.sldprt", "") != chiave(@"S:\a\c.sldprt", ""));

            // IL CASO CHE UN TRONCAMENTO AVREBBE ROTTO: due file nella stessa
            // cartella profondissima, diversi solo per l'ultima lettera. Con la
            // chiave ricavata dal percorso troncato sarebbero la stessa cosa, e
            // due pezzi diversi si bloccherebbero a vicenda.
            string lungo = @"S:\" + new string('x', 200) + @"\";
            Segna("due file in una cartella profondissima restano distinti",
                  chiave(lungo + "uno.sldprt", "") != chiave(lungo + "due.sldprt", ""),
                  "e' il caso che un troncamento del percorso avrebbe fuso");

            Segna("senza percorso si usa il titolo",
                  chiave("", "Parte286") != null &&
                  chiave("", "Parte286") == chiave("", "Parte286"));
            Segna("titoli diversi, chiavi diverse",
                  chiave("", "Parte286") != chiave("", "Parte287"));

            // NIENTE DI NIENTE: nessuna chiave, e quindi nessun lucchetto.
            // Bloccare su una chiave inventata unirebbe documenti diversi e
            // lascerebbe passare quelli uguali: farebbe l'opposto di cio' che
            // serve.
            Segna("senza percorso NE titolo non c'e' chiave", chiave("", "") == null,
                  "un lucchetto su una chiave inventata e' peggio di nessun lucchetto");
        }

        /// <summary>
        /// Un documento finto: risponde solo alle due domande che il ponte fa.
        ///
        /// `TurnoSulDocumento` interroga per riflessione (`InvokeMember`), quindi
        /// non serve implementare `IModelDoc2` — cosa che qui, senza SOLIDWORKS,
        /// non si potrebbe nemmeno fare.
        /// </summary>
        private sealed class DocumentoFinto
        {
            private readonly string _percorso, _titolo;
            public DocumentoFinto(string percorso, string titolo)
            {
                _percorso = percorso; _titolo = titolo;
            }
            public string GetPathName() { return _percorso; }
            public string GetTitle() { return _titolo; }
        }
    }
}
