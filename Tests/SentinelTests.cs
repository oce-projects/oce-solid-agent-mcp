using System;
using System.IO;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Programma;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// LA GUARDIA CHE NESSUNO POTREBBE MAI VEDERE SCATTARE (voce K17).
    ///
    /// `SentinellaSolidWorks` decide quando il banco deve smettere di provare
    /// perche' SOLIDWORKS non c'e' piu'. La condizione che la accende — tre
    /// documenti nuovi che non nascono di fila — nella vita vera capita dopo
    /// ore di giro e non si sa riprodurre a comando: e' esattamente il tipo di
    /// controllo che si scrive, si crede, e si scopre rotto il giorno in cui
    /// servirebbe.
    ///
    /// Qui SOLIDWORKS non c'e' affatto, e proprio per questo la si puo' far
    /// fallire apposta: la sentinella non tocca il CAD, conta e basta. Ogni
    /// giro di `Oce.Solid.Agent.ContractTests` la fa scattare, e la fa NON scattare quando
    /// non deve — che e' la meta' che di solito nessuno prova.
    /// </summary>
    internal static class ProveLaSentinella
    {
        internal static void Tutte()
        {
            Gruppo("la sentinella: quando SOLIDWORKS se n'e' andato");

            // ── DUE FALLIMENTI NON BASTANO ───────────────────────────────
            //
            // La meta' che di solito non si prova. Una guardia che scattasse
            // al primo fallimento fermerebbe un giro intero per un modello di
            // documento momentaneamente occupato — e a quel punto la si
            // toglierebbe, ed e' peggio che non averla.
            var s1 = new Shared.SentinellaSolidWorks(3);
            s1.Fallita();
            s1.Fallita();
            Segna("due fallimenti di fila non bastano", !s1.SeNEAndato,
                  "di fila: " + s1.DiFila + " su soglia " + s1.Soglia);

            // ── TRE SI' ──────────────────────────────────────────────────
            bool scattata = s1.Fallita();
            Segna("al terzo la sentinella scatta", scattata && s1.SeNEAndato,
                  "di fila: " + s1.DiFila);

            // ── E NON TORNA PIU' INDIETRO ────────────────────────────────
            //
            // Dopo che il programma e' morto, un documento che nasce per caso
            // non lo resuscita: da li' in poi il giro non e' piu' valido
            // comunque, perche' in mezzo ci sono prove non fatte.
            s1.Riuscita();
            Segna("una volta scattata non si spegne", s1.SeNEAndato,
                  "dopo Riuscita(): " + s1.SeNEAndato);

            // ── «DI FILA» E' LA PAROLA CHE CONTA ─────────────────────────
            //
            // Tre fallimenti SPARSI in ottocento controlli sono rumore
            // normale: un caso che lascia SOLIDWORKS storto e il successivo
            // che si riprende. Se la sentinella li sommasse, fermerebbe i giri
            // sani — e il primo che la vede fermare un giro sano la disattiva.
            var s2 = new Shared.SentinellaSolidWorks(3);
            s2.Fallita(); s2.Riuscita();
            s2.Fallita(); s2.Riuscita();
            s2.Fallita(); s2.Riuscita();
            s2.Fallita();
            Segna("tre fallimenti SPARSI non la fanno scattare", !s2.SeNEAndato,
                  "quattro fallimenti in tutto, mai due di fila: di fila = " + s2.DiFila);

            // ── LA SOGLIA E' UN PARAMETRO, NON IL NUMERO TRE ─────────────
            var s3 = new Shared.SentinellaSolidWorks(1);
            s3.Fallita();
            Segna("con soglia 1 scatta al primo", s3.SeNEAndato, "soglia " + s3.Soglia);

            bool rifiutata = false;
            try { new Shared.SentinellaSolidWorks(0); }
            catch (ArgumentOutOfRangeException) { rifiutata = true; }
            Segna("una soglia di zero viene rifiutata", rifiutata,
                  "scatterebbe prima di guardare");

            // ── IL REFERTO PORTA I NUMERI ────────────────────────────────
            //
            // «SOLIDWORKS non risponde» senza numeri non si distingue da
            // un'opinione. Chi legge il resoconto deve poter vedere quanti
            // tentativi e su quale soglia, senza aprire il codice.
            // NON «contiene un 3»: contiene «3 tentativi». La prima versione
            // di questa riga cercava la cifra e passava mentre il referto
            // diceva «0 tentativi di fila andati a vuoto» — il 3 che trovava
            // era la soglia, nella stessa frase. Vedi `_quandoScatto`.
            string referto = s1.Referto();
            Segna("il referto dice quanti tentativi", referto.Contains("3 tentativi"), referto);
            Segna("il referto dice che NON sono rosse",
                  referto.IndexOf("NON SONO STATE", StringComparison.Ordinal) >= 0,
                  "la differenza fra «fallita» e «non fatta» e' tutto il punto");

            // ── E IL BANCO LA USA DAVVERO ────────────────────────────────
            //
            // Una sentinella corretta che nessuno consulta e' una classe
            // corretta e un banco rotto. Le prove di contratto non possono
            // lanciare il banco — serve SOLIDWORKS — ma possono leggere il
            // sorgente e pretendere che i tre punti che creano documenti ci
            // passino, e che il verdetto abbia la sua uscita.
            IlBancoLaConsulta();
        }

        private static void IlBancoLaConsulta()
        {
            string programma = Path.Combine(Radice(), "Bench", "Program.cs");
            string assieme = Path.Combine(Radice(), "Bench", "TestAssembly.cs");
            string casi = Path.Combine(Radice(), "Bench", "DeclaredCases.cs");

            foreach (var f in new[] { programma, assieme, casi })
                if (!File.Exists(f))
                {
                    Segna("i sorgenti del banco ci sono", false, "manca " + f);
                    return;
                }

            string p = File.ReadAllText(programma);
            string a = File.ReadAllText(assieme);
            string c = File.ReadAllText(casi);

            Segna("il banco costruisce la sentinella",
                  Regex.IsMatch(p, @"new Shared\.SentinellaSolidWorks\(\s*3\s*\)"),
                  "soglia 3 in Bench/Program.cs");

            // I TRE PUNTI CHE CREANO DOCUMENTI, uno per uno. Un punto che non
            // ci passa e' una strada per cui il banco continua a provare
            // contro un programma morto — cioe' il difetto, intatto.
            Segna("NuovaParte consulta la sentinella",
                  p.Contains("if (Sentinella.SeNEAndato)") &&
                  p.Contains("Sentinella.Fallita()") &&
                  p.Contains("Sentinella.Riuscita()"),
                  "Bench/Program.cs");

            Segna("anche l'assieme di prova ci passa",
                  a.Contains("Program.Sentinella.SeNEAndato") &&
                  a.Contains("Program.Sentinella.Fallita()"),
                  "Bench/TestAssembly.cs: un assieme comincia da parti nuove");

            Segna("i casi dichiarati si SALTANO, non falliscono",
                  c.Contains("Sentinella.SeNEAndato") && c.Contains("Saltata("),
                  "Bench/DeclaredCases.cs: saltata non conta ne' verde ne' rossa");

            // ── E ANCHE PRIMA DELLO SCATTO, ROSSO E' LA PAROLA SBAGLIATA ─
            //
            // I due casi che consumano i primi due fallimenti non sono rossi:
            // sono non misurati. Prima finivano in un `catch (Exception)` che
            // segnava rosso qualunque cosa fosse successa.
            Segna("una preparazione fallita non diventa rossa",
                  c.Contains("catch (Shared.PreparazioneFallita"),
                  "Bench/DeclaredCases.cs: errore di banco, o saltata se gia' morto");

            // E IL TIPO, NON IL TESTO. Un `catch` che riconoscesse la
            // preparazione dal messaggio tornerebbe a segnare rosso il giorno
            // in cui qualcuno riscrive la frase — senza che niente lo dica.
            Segna("la preparazione si riconosce dal TIPO, non dal messaggio",
                  p.Contains("throw new Shared.PreparazioneFallita("),
                  "Bench/Program.cs: OppureDillo e la sentinella alzano lo stesso tipo");

            // ── L'USCITA 4, E CHE VENGA PRIMA DELLE ALTRE ────────────────
            //
            // Se il 2 o l'1 uscissero per primi, chi legge il codice di uscita
            // andrebbe a cercare cosa ha rotto — e non ha rotto niente: il
            // giro non e' stato fatto.
            int dove4 = p.IndexOf("return 4;", StringComparison.Ordinal);
            int dove2 = p.LastIndexOf("if (_erroriDiBanco > 0) return 2;", StringComparison.Ordinal);
            Segna("il verdetto ha l'uscita 4", dove4 >= 0, "Bench/Program.cs");
            Segna("la 4 viene prima della 2", dove4 >= 0 && dove2 >= 0 && dove4 < dove2,
                  "un giro non fatto non e' un giro con errori");

            // ── E SI PUO' FAR MORIRE SOLIDWORKS A COMANDO ────────────────
            Segna("c'e' un modo di vederla scattare sul banco vero",
                  p.Contains("--fingi-senza-solidworks"),
                  "senza, resterebbe una guardia creduta e mai vista");

            // ── IL MODELLO DI DOCUMENTO NON SI LEGGE DALLA RETE (K19) ────
            //
            // Ottocento parti nuove per giro vuol dire ottocento letture del
            // `.prtdot`. Quando quel file sta su una condivisione e SOLIDWORKS
            // si pianta mentre lo apre, tiene la presa e il file non si legge
            // piu' nemmeno da fuori: il 23/9/2026 sono morti tre giri di fila,
            // e sembrava la rete.
            Segna("il modello di documento si copia in locale",
                  p.Contains("ModelloInLocale("),
                  "Bench/Program.cs");

            Segna("la parte nuova nasce dalla copia locale",
                  p.Contains("ModelloInLocale(_sw.GetUserPreferenceStringValue("),
                  "non dal percorso di rete");

            Segna("e anche l'assieme di prova",
                  a.Contains("Program.ModelloInLocale("),
                  "Bench/TestAssembly.cs");

            // E LA COPIA SI ANNUNCIA. Un banco che usa in silenzio un modello
            // diverso da quello della postazione misura un'altra cosa.
            Segna("la copia si dice, e anche quando non riesce",
                  p.Contains("modello copiato in locale") &&
                  p.Contains("NON ho potuto copiare il modello in locale"),
                  "tutte e due le strade parlano");

            // ── E SE LA COPIA FALLISCE, NON SI TORNA ALLA RETE PER NIENTE ─
            //
            // La copia fallisce proprio quando l'originale non si legge:
            // ricadere sul percorso di rete vuol dire ripiantarsi. Se in
            // locale c'e' la copia di un giro precedente si usa quella, e si
            // dice **di quando e'** — un modello vecchio spacciato per quello
            // di adesso sarebbe lo stesso silenzio di sempre.
            Segna("se la copia fallisce si usa quella del giro prima",
                  p.Contains("uso la copia locale del") &&
                  p.Contains("File.GetLastWriteTime("),
                  "e la sua data si legge nel resoconto");
        }
    }
}
