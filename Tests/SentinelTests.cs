using System;
using System.IO;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// LA GUARDIA CHE NESSUNO POTREBBE MAI VEDERE SCATTARE (voce K17).
    ///
    /// `SolidWorksSentinel` decide quando il banco deve smettere di provare
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
    internal static class SentinelTests
    {
        internal static void All()
        {
            Group("la sentinella: quando SOLIDWORKS se n'e' andato");

            // ── DUE FALLIMENTI NON BASTANO ───────────────────────────────
            //
            // La meta' che di solito non si prova. Una guardia che scattasse
            // al primo fallimento fermerebbe un giro intero per un modello di
            // documento momentaneamente occupato — e a quel punto la si
            // toglierebbe, ed e' peggio che non averla.
            var s1 = new Shared.SolidWorksSentinel(3);
            s1.Failed();
            s1.Failed();
            Check("due fallimenti di fila non bastano", !s1.HasGone,
                  "di fila: " + s1.InARow + " su soglia " + s1.Threshold);

            // ── TRE SI' ──────────────────────────────────────────────────
            bool fired = s1.Failed();
            Check("al terzo la sentinella scatta", fired && s1.HasGone,
                  "di fila: " + s1.InARow);

            // ── E NON TORNA PIU' INDIETRO ────────────────────────────────
            //
            // Dopo che il programma e' morto, un documento che nasce per caso
            // non lo resuscita: da li' in poi il giro non e' piu' valido
            // comunque, perche' in mezzo ci sono prove non fatte.
            s1.Succeeded();
            Check("una volta scattata non si spegne", s1.HasGone,
                  "dopo Succeeded(): " + s1.HasGone);

            // ── «DI FILA» E' LA PAROLA CHE CONTA ─────────────────────────
            //
            // Tre fallimenti SPARSI in ottocento controlli sono rumore
            // normale: un caso che lascia SOLIDWORKS storto e il successivo
            // che si riprende. Se la sentinella li sommasse, fermerebbe i giri
            // sani — e il primo che la vede fermare un giro sano la disattiva.
            var s2 = new Shared.SolidWorksSentinel(3);
            s2.Failed(); s2.Succeeded();
            s2.Failed(); s2.Succeeded();
            s2.Failed(); s2.Succeeded();
            s2.Failed();
            Check("tre fallimenti SPARSI non la fanno scattare", !s2.HasGone,
                  "quattro fallimenti in tutto, mai due di fila: di fila = " + s2.InARow);

            // ── LA SOGLIA E' UN PARAMETRO, NON IL NUMERO TRE ─────────────
            var s3 = new Shared.SolidWorksSentinel(1);
            s3.Failed();
            Check("con soglia 1 scatta al primo", s3.HasGone, "soglia " + s3.Threshold);

            bool refused = false;
            try { new Shared.SolidWorksSentinel(0); }
            catch (ArgumentOutOfRangeException) { refused = true; }
            Check("una soglia di zero viene rifiutata", refused,
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
            string report = s1.Report();
            Check("il referto dice quanti tentativi", report.Contains("3 tentativi"), report);
            Check("il referto dice che NON sono rosse",
                  report.IndexOf("NON SONO STATE", StringComparison.Ordinal) >= 0,
                  "la differenza fra «fallita» e «non fatta» e' tutto il punto");

            // ── E IL BANCO LA USA DAVVERO ────────────────────────────────
            //
            // Una sentinella corretta che nessuno consulta e' una classe
            // corretta e un banco rotto. Le prove di contratto non possono
            // lanciare il banco — serve SOLIDWORKS — ma possono leggere il
            // sorgente e pretendere che i tre punti che creano documenti ci
            // passino, e che il verdetto abbia la sua uscita.
            TheBenchConsultsIt();
        }

        private static void TheBenchConsultsIt()
        {
            string program = Path.Combine(Root(), "Bench", "Program.cs");
            string assemblyDoc = Path.Combine(Root(), "Bench", "TestAssembly.cs");
            string cases = Path.Combine(Root(), "Bench", "DeclaredCases.cs");

            foreach (var f in new[] { program, assemblyDoc, cases })
                if (!File.Exists(f))
                {
                    Check("i sorgenti del banco ci sono", false, "manca " + f);
                    return;
                }

            string p = File.ReadAllText(program);
            string a = File.ReadAllText(assemblyDoc);
            string c = File.ReadAllText(cases);

            Check("il banco costruisce la sentinella",
                  Regex.IsMatch(p, @"new Shared\.SolidWorksSentinel\(\s*3\s*\)"),
                  "soglia 3 in Bench/Program.cs");

            // I TRE PUNTI CHE CREANO DOCUMENTI, uno per uno. Un punto che non
            // ci passa e' una strada per cui il banco continua a provare
            // contro un programma morto — cioe' il difetto, intatto.
            Check("NuovaParte consulta la sentinella",
                  p.Contains("if (Sentinel.HasGone)") &&
                  p.Contains("Sentinel.Failed()") &&
                  p.Contains("Sentinel.Succeeded()"),
                  "Bench/Program.cs");

            Check("anche l'assieme di prova ci passa",
                  a.Contains("Program.Sentinel.HasGone") &&
                  a.Contains("Program.Sentinel.Failed()"),
                  "Bench/TestAssembly.cs: un assieme comincia da parti nuove");

            Check("i casi dichiarati si SALTANO, non falliscono",
                  c.Contains("Sentinel.HasGone") && c.Contains("Skipped("),
                  "Bench/DeclaredCases.cs: saltata non conta ne' verde ne' rossa");

            // ── E ANCHE PRIMA DELLO SCATTO, ROSSO E' LA PAROLA SBAGLIATA ─
            //
            // I due casi che consumano i primi due fallimenti non sono rossi:
            // sono non misurati. Prima finivano in un `catch (Exception)` che
            // segnava rosso qualunque cosa fosse successa.
            Check("una preparazione fallita non diventa rossa",
                  c.Contains("catch (Shared.PreparationFailed"),
                  "Bench/DeclaredCases.cs: errore di banco, o saltata se gia' morto");

            // E IL TIPO, NON IL TESTO. Un `catch` che riconoscesse la
            // preparazione dal messaggio tornerebbe a segnare rosso il giorno
            // in cui qualcuno riscrive la frase — senza che niente lo dica.
            Check("la preparazione si riconosce dal TIPO, non dal messaggio",
                  p.Contains("throw new Shared.PreparationFailed("),
                  "Bench/Program.cs: OppureDillo e la sentinella alzano lo stesso tipo");

            // ── L'USCITA 4, E CHE VENGA PRIMA DELLE ALTRE ────────────────
            //
            // Se il 2 o l'1 uscissero per primi, chi legge il codice di uscita
            // andrebbe a cercare cosa ha rotto — e non ha rotto niente: il
            // giro non e' stato fatto.
            int where4 = p.IndexOf("return 4;", StringComparison.Ordinal);
            int where2 = p.LastIndexOf("if (_benchErrors > 0) return 2;", StringComparison.Ordinal);
            Check("il verdetto ha l'uscita 4", where4 >= 0, "Bench/Program.cs");
            Check("la 4 viene prima della 2", where4 >= 0 && where2 >= 0 && where4 < where2,
                  "un giro non fatto non e' un giro con errori");

            // ── E SI PUO' FAR MORIRE SOLIDWORKS A COMANDO ────────────────
            Check("c'e' un modo di vederla scattare sul banco vero",
                  p.Contains("--fingi-senza-solidworks"),
                  "senza, resterebbe una guardia creduta e mai vista");

            // ── IL MODELLO DI DOCUMENTO NON SI LEGGE DALLA RETE (K19) ────
            //
            // Ottocento parti nuove per giro vuol dire ottocento letture del
            // `.prtdot`. Quando quel file sta su una condivisione e SOLIDWORKS
            // si pianta mentre lo apre, tiene la presa e il file non si legge
            // piu' nemmeno da fuori: il 23/9/2026 sono morti tre giri di fila,
            // e sembrava la rete.
            Check("il modello di documento si copia in locale",
                  p.Contains("LocalModel("),
                  "Bench/Program.cs");

            Check("la parte nuova nasce dalla copia locale",
                  p.Contains("LocalModel(_sw.GetUserPreferenceStringValue("),
                  "non dal percorso di rete");

            Check("e anche l'assieme di prova",
                  a.Contains("Program.LocalModel("),
                  "Bench/TestAssembly.cs");

            // E LA COPIA SI ANNUNCIA. Un banco che usa in silenzio un modello
            // diverso da quello della postazione misura un'altra cosa.
            Check("la copia si dice, e anche quando non riesce",
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
            Check("se la copia fallisce si usa quella del giro prima",
                  p.Contains("uso la copia locale del") &&
                  p.Contains("File.GetLastWriteTime("),
                  "e la sua data si legge nel resoconto");
        }
    }
}
