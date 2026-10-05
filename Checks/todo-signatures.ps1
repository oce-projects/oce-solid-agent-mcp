# =============================================================================
#  todo-signatures.ps1 - le API citate nel TODO esistono davvero?
# =============================================================================
#
#  QUESTO FILE E' IN ASCII PURO, come bench.ps1 e per la stessa ragione.
#
#  PERCHE' ESISTE
#
#  Decine di voci del TODO poggiano su una firma copiata da una pagina di
#  documentazione: "IModelDocExtension::InsertSurfaceFinishSymbol3",
#  "IFace2::GetCorrespondingEntity", "IEquationMgr". Sono promesse, e finche'
#  nessuno le verifica restano promesse: **una voce che pianifica un lavoro
#  attorno a un metodo che non esiste e' un lavoro che comincia male.**
#
#  La regola di casa: si verifica la cosa, non cio' che la descrive. Qui la
#  cosa e' l'interop installato su QUESTA postazione -- cioe' esattamente
#  quello contro cui il codice girera'.
#
#  NON SERVE CHE SOLIDWORKS SIA APERTO: serve che sia installato.
#
#  ── COSA VUOL DIRE "NON TROVATO", E COSA NON VUOL DIRE ──────────────────────
#
#  Vuol dire: con quel nome, su quel tipo, in QUESTA versione, non c'e'.
#  NON vuol dire che la voce sia sbagliata: puo' essere un refuso nel TODO, un
#  metodo arrivato in una versione piu' nuova, o un membro che sta su un tipo
#  diverso da quello citato. **E' un elenco di cose da guardare, non di colpe.**
#
#  ── COSA QUESTA SONDA NON VEDE ─────────────────────────────────────────────
#
#  I nomi scritti SENZA il tipo davanti. Il 23/9/2026 due citazioni sbagliate
#  le sono passate sotto il naso proprio cosi':
#
#      `AutoInsertCenterMarks2`   -- sta su IView, il TODO non lo diceva
#      `GetRemainingDOFs`         -- sta su IComponent2, idem
#
#  Cercare ogni parola che assomigli a un metodo, senza tipo, vorrebbe dire
#  raccogliere meta' della prosa e produrre un elenco che nessuno legge --
#  la malattia che questo file evita per costruzione. Il rimedio non e' una
#  regex piu' larga: **e' scrivere il tipo davanti al metodo nel TODO.**
#
#  USO
#      .\Checks\todo-signatures.ps1
#      .\Checks\todo-signatures.ps1 -All     anche quelle che ci sono
# =============================================================================

param([switch]$All)

$ErrorActionPreference = "Continue"

$redist = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist"
if ($env:SW_INSTALL_PATH) { $redist = Join-Path $env:SW_INSTALL_PATH "api\redist" }
if (-not (Test-Path $redist)) { Write-Output "non trovo $redist"; exit 2 }

# TUTTE le interop, non solo sldworks: le voci citano anche swconst,
# swdocumentmgr, gtswutilities, cosworks.
$types = @{}
foreach ($dll in (Get-ChildItem $redist -Filter "SolidWorks.Interop.*.dll")) {
    try {
        $asm = [Reflection.Assembly]::LoadFrom($dll.FullName)
        foreach ($t in $asm.GetTypes()) { if (-not $types.ContainsKey($t.Name)) { $types[$t.Name] = $t } }
    } catch { }
}
Write-Output ""
Write-Output ("interop caricate: " + (Get-ChildItem $redist -Filter 'SolidWorks.Interop.*.dll').Count +
              ",  tipi visti: " + $types.Count)

# IL TODO NON E' PIU' NEL REPOSITORY PUBBLICO: e' un diario di lavoro, vive in
# `note-interne/` e il .gitignore lo esclude. Si cerca dove sta oggi, dove
# stava prima, e dove lo dice chi lo tiene altrove. Se non c'e', questa sonda
# NON e' passata: non ha controllato, e lo dice uscendo 0 - perche' tre sonde
# del TODO girano nel gancio di pre-push, e farle morire qui romperebbe il push
# a chiunque cloni, per un file che non ha e non deve avere.
$repoRoot = Split-Path $PSScriptRoot -Parent
$todo = $env:TODO_MD
if (-not $todo -or -not (Test-Path $todo)) {
    $todo = $null
    foreach ($c in @((Join-Path $repoRoot 'note-interne\TODO.md'),
                     (Join-Path $repoRoot 'TODO.md'))) {
        if (Test-Path $c) { $todo = $c; break }
    }
}
if (-not $todo) {
    Write-Output ""
    Write-Output "Il TODO non c'e' in questo clone: e' un documento interno e non"
    Write-Output "viene pubblicato. QUESTA SONDA NON HA CONTROLLATO NIENTE."
    Write-Output "Se ne hai una copia, indicala con TODO_MD."
    exit 0
}
$text = Get-Content $todo -Raw

# `ITipo::Membro` oppure `ITipo.Membro`. Si prende solo cio' che comincia con
# I maiuscola seguita da maiuscola: e' la convenzione delle interfacce COM di
# SOLIDWORKS, e senza quel filtro si raccoglierebbe meta' della prosa.
# IL MEMBRO E' L'ULTIMO IDENTIFICATORE PRIMA DELLA PARENTESI.
#
# La prima stesura prendeva il primo dopo i due punti, e il TODO scrive le
# firme come le stampa l'API:
#
#     IModelDocExtension :: SFSymbol InsertSurfaceFinishSymbol3(Int32 ...)
#
# cioe' `Tipo :: Ritorno Metodo(`. Risultato: ventitre' "membri mancanti"
# che erano **tipi di ritorno** -- IView.Object, IDrawingDoc.Void,
# IFeatureManager.Feature. *Una sonda che urla ventitre' falsi allarmi non
# la legge nessuno*, ed e' il modo in cui un controllo muore.
#
# Due forme, in quest'ordine: col metodo e la parentesi, oppure una
# proprieta' senza parentesi.
$foundOnes = [ordered]@{}

# 1. `ITipo :: [Ritorno] Metodo(`
foreach ($m in [regex]::Matches($text, '\b(I[A-Z][a-z]\w*)\s*(?:::|\.)\s*(?:[A-Za-z_]\w*\s+)?([A-Za-z_]\w*)\s*\(')) {
    $key = $m.Groups[1].Value + "." + $m.Groups[2].Value
    if (-not $foundOnes.Contains($key)) { $foundOnes[$key] = $true }
}

# 2. `ITipo.Property` senza parentesi. Il filtro `I[A-Z][a-z]` tiene fuori
#    le sigle scritte in prosa -- «ISO», «IGES» -- che non sono interfacce.
# Due negazioni, e servono tutt'e due:
#   (?!\s*\()            non e' un metodo -- quello lo prende la forma 1
#   (?!\s+\w+\s*\()     e non e' un TIPO DI RITORNO seguito dal metodo
# Senza la seconda, `IView :: Object InsertHoleTable3(` produce
# «IView.Object», che e' il falso allarme di prima travestito.
foreach ($m in [regex]::Matches($text, '\b(I[A-Z][a-z]\w*)\s*(?:::|\.)\s*([A-Z]\w*)\b(?!\s*\()(?!\s+[A-Za-z_]\w*\s*\()')) {
    $key = $m.Groups[1].Value + "." + $m.Groups[2].Value
    if (-not $foundOnes.Contains($key)) { $foundOnes[$key] = $true }
}

# ── LE CITAZIONI CHE PARLANO DI UN NOME SBAGLIATO ───────────────────────────
#
# Il 23/9/2026 questa sonda ha trovato quattro firme inesistenti nel TODO. Le
# correzioni sono state scritte NEL TODO, e le correzioni **nominano il nome
# sbagliato** per spiegare che e' sbagliato -- cosi' il giro dopo la sonda
# segnalava la propria documentazione.
#
# Non si stringe la ricerca: si dichiara l'eccezione col suo perche', come
# nell'elenco della voce Z-C7. **Una sonda che si zittisce da sola smette di
# vedere anche i casi veri.**
$sorry = @{
    "IMate2.Name"            = "la voce Z-A8 la cita per dire che NON esiste: verificato a mano prima, e da questa sonda dopo"
    "IAssemblyDoc.ViewMates" = "citata nella correzione che spiega che non esiste (i membri veri sono AddMate4/5, EditMate3/4, CopyWithMates)"
    "INewGtol.NewGtol"       = "citata nella correzione che spiega che INewGtol e' un METODO di IDrawingDoc, non un tipo"
}

Write-Output ("firme citate nel TODO: " + $foundOnes.Count +
              ",  di cui dichiarate inesistenti apposta: " + $sorry.Count)
Write-Output ""

$missingItems = @()
$absentTypes = @()
$ok = 0
foreach ($f in $foundOnes.Keys) {
    $p = $f.Split(".")
    $typeName = $p[0]; $memberName = $p[1]
    if ($sorry.ContainsKey($f)) { continue }
    if (-not $types.ContainsKey($typeName)) { $absentTypes += $f; continue }
    $t = $types[$typeName]
    $c = @($t.GetMembers() | Where-Object { $_.Name -eq $memberName })
    # le proprieta' COM compaiono anche come get_Xxx
    if ($c.Count -eq 0) { $c = @($t.GetMembers() | Where-Object { $_.Name -eq ("get_" + $memberName) }) }
    if ($c.Count -eq 0) { $missingItems += $f } else { $ok++; if ($All) { Write-Output ("  ok   " + $f) } }
}

Write-Output ("verificate e presenti: " + $ok)
Write-Output ""
if ($absentTypes.Count -gt 0) {
    Write-Output ("TIPI che non esistono in queste interop: " + $absentTypes.Count)
    foreach ($x in $absentTypes) { Write-Output ("  ?  " + $x) }
    Write-Output ""
}
if ($missingItems.Count -gt 0) {
    Write-Output ("MEMBRI che il loro tipo NON ha: " + $missingItems.Count)
    foreach ($x in $missingItems) { Write-Output ("  !  " + $x) }
    Write-Output ""
}
if ($absentTypes.Count -eq 0 -and $missingItems.Count -eq 0) {
    Write-Output "Tutte le firme citate nel TODO esistono in questa installazione."
}

Write-Output "«Non trovato» e' una cosa da guardare, non una colpa: puo' essere un"
Write-Output "refuso, una versione piu' nuova, o un membro che sta su un altro tipo."
