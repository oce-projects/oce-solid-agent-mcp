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

            var ponte = Path.Combine(Radice(), "bin", "oce-solid-agent-mcp.exe");
            if (!File.Exists(ponte))
            {
                Segna("il ponte c'e'", false, "non trovo " + ponte);
                return;
            }

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
