# Quello che non deve uscire da questa macchina.
#
# Questo repository e' destinato a diventare pubblico. Un percorso con dentro il
# nome di chi lavora, o l'indirizzo di un server aziendale, non e' un difetto di
# stile: e' roba di qualcun altro che esce senza che nessuno l'abbia deciso.
#
# NON BASTAVA IL CONTROLLO DEL CATALOGO. `controlla-catalogo.ps1` guarda gli
# stessi vietati, ma solo dentro `catalogo.json` - il file che va al modello. Il
# 14/9/2026, mettendo il banco nel repository, sono entrati DICIANNOVE file con
# dentro il percorso della scratchpad di sessione, nome utente compreso. Il
# controllo c'era e guardava altrove.
#
#   .\Controlli\controlla-pubblicabilita.ps1
#
# Esce 0 se e' pulito, 1 se no.
#
# DUE ACCORGIMENTI CHE NON SI DEDUCONO, e sono costati un falso positivo a
# testa:
#
#   1. `Select-String` NON distingue maiuscole e minuscole di suo. Cercando
#      "O:\" trovava `esempio:\n` dentro una descrizione JSON - cioe' "o" +
#      ":" + l'escape di un a capo. Le lettere di unita' si cercano con
#      -CaseSensitive e con la regola che prima non ci sia una lettera.
#   2. bin\ e obj\ si saltano: li' non ci finisce niente che venga committato,
#      e scandirli raddoppia il tempo per niente.
#
# NOTA: in ASCII puro, come tutti gli .ps1 del progetto.

$ErrorActionPreference = 'Stop'
$radice = Split-Path -Parent $PSScriptRoot
$guai = 0
$estensioni = @('*.cs', '*.json', '*.md', '*.ps1', '*.js', '*.csproj', '*.txt')

# I file che possono NOMINARE i vietati, perche' il loro mestiere e' cercarli.
# Si elencano uno per uno: una regola larga qui sarebbe un buco.
$possonoNominarli = @(
    'Controlli\controlla-catalogo.ps1',
    'Controlli\controlla-pubblicabilita.ps1'
)

function Righe($pattern, $caseSensitive) {
    $trovate = Get-ChildItem -Path $radice -Include $estensioni -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' }
    if ($caseSensitive) { $trovate = $trovate | Select-String -Pattern $pattern -CaseSensitive }
    else { $trovate = $trovate | Select-String -Pattern $pattern }
    @($trovate | Where-Object {
        $relativo = $_.Path.Substring($radice.Length + 1)
        -not ($possonoNominarli -contains $relativo)
    })
}

function Guarda($nome, $pattern, $perche, $caseSensitive, $eccetto = @()) {
    $trovati = @(Righe $pattern $caseSensitive | Where-Object {
        $relativo = $_.Path.Substring($radice.Length + 1)
        -not ($eccetto -contains $relativo)
    })
    if ($trovati.Count -eq 0) { Write-Host "  ok  nessun $nome"; return }
    Write-Host "  XX  $nome ($perche) in $($trovati.Count) punti:"
    $script:guai++
    $visti = @{}
    foreach ($t in $trovati) {
        $relativo = $t.Path.Substring($radice.Length + 1)
        if ($visti.ContainsKey($relativo)) { continue }
        $visti[$relativo] = $true
        Write-Host "        ${relativo}:$($t.LineNumber)"
    }
    if ($visti.Count -lt $trovati.Count) { Write-Host "        (e altre righe negli stessi file)" }
}

Write-Host "=== Cosa non deve uscire da questa macchina"
Guarda 'il nome utente' 'oscar' 'il nome di chi lavora' $true
Guarda 'un percorso di sessione' 'AppData.Local.Temp' 'esiste solo su questa macchina' $false
# IL TODO NOMINA I VIETATI PERCHE' E' LI' CHE SONO SCRITTI.
# La voce A1 elenca cosa non deve uscire, e per elencarlo lo deve scrivere.
# L'eccezione vale SOLO per i pattern che quella voce discute: il nome utente
# non e' fra questi, e se compare nel TODO e' una fuga come le altre.
$loDiceIlTodo = @('TODO.md')

Guarda 'un indirizzo interno' '192\.168\.' 'una rete privata' $false $loDiceIlTodo
Guarda 'il disco O:' '(?<![A-Za-z])O:\\' 'un disco di rete aziendale' $true $loDiceIlTodo
Guarda 'il disco W:' '(?<![A-Za-z])W:\\' 'un disco di rete aziendale' $true $loDiceIlTodo

# IL DISCO S: E' UN CASO A PARTE, e va detto invece di essere nascosto.
#
# `<impostazioni SOLIDWORKS>` e' dove stanno i profili normati di chi lavora,
# e il banco ci punta davvero per provare le saldature. Non e' un segreto ed e'
# una precondizione vera: ma chi clona il repository non ce l'ha, e deve saperlo
# prima di lanciare il banco, non dopo.
Write-Host ""
Write-Host "=== Percorsi di questa installazione (non vietati, ma da sapere)"
$suDiscoS = Righe '(?<![A-Za-z])S:\\' $true
if ($suDiscoS.Count -eq 0) {
    Write-Host "  nessuno"
} else {
    $file = $suDiscoS | ForEach-Object { $_.Path.Substring($radice.Length + 1) } | Sort-Object -Unique
    Write-Host "  $($suDiscoS.Count) riferimenti a S: in $($file.Count) file:"
    foreach ($f in $file) { Write-Host "     $f" }
    Write-Host "  Vanno bene solo se chi clona puo' scavalcarli con una variabile o un"
    Write-Host "  argomento. Se sono costanti, non e' un repository che si clona."
}

Write-Host ""
if ($guai -eq 0) { Write-Host 'Pulito.'; exit 0 }
Write-Host "$guai categorie da ripulire."
exit 1
