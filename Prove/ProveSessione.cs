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
        }
    }
}
