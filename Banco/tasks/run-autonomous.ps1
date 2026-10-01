# =============================================================================
#  The autonomous run of the tasks (decision D9 of W3.1-bis, entry W3.1-bis.5)
# =============================================================================
#
#  Each task of this folder is handed to a model that has ONLY the bridge, as
#  a person would write it (the blockquote under "The task"), with no hint of
#  the operations. The session runs on its own (claude -p, no questions back),
#  and the run keeps what is needed to score it afterwards:
#
#    <out>/<task>-<n>.jsonl        the transcript (stream-json): every call to
#                                  the bridge and its answer, with the outcomes
#                                  of every step of every plan
#    <out>/<task>-<n>.state.json   the state of the document in front when the
#                                  session ended (inspect_part, read by a fresh
#                                  bridge): the FINISHED part the tables of the
#                                  tasks measure
#    <out>/run.json                what ran: language, bridge, catalogue hash,
#                                  repeats, model, start and end
#
#  Scoring is another step (score-autonomous.js), so that a scorer that gets
#  better never needs the model to run again.
#
#  D9: every task x3 on the ITALIAN version (the threshold) and x1 on the
#  English one (a signal); the medians per task of the bridge before the rename
#  window (the catalogue of the snapshot) against the bridge after it, at most
#  one task of fifteen worse. The commands are in Banco/tasks/README.md.
#
#  NEEDS SOLIDWORKS, a built bridge and the claude CLI. It starts every task
#  with -Between (a command that closes the documents left open by the task
#  before, for example), because a task that finds the part of the previous
#  one in front is not measuring the model.
#
#      .\Banco\tasks\run-autonomous.ps1 -Language en -DryRun
#      .\Banco\tasks\run-autonomous.ps1 -Language it -TasksDir <Italian tasks> -Repeats 3 -Label after
#
#  Exit 0 when every session ran (whatever the model made of it), 1 when a
#  session could not start, 2 on a bad argument.
# =============================================================================

param(
    [ValidateSet('it', 'en')] [string] $Language = 'en',
    [string]   $TasksDir = '',
    [int]      $Repeats = 1,
    [string]   $Bridge = '',
    [string]   $Label = '',
    [string[]] $Only = @(),
    [int]      $MaxTurns = 80,
    [string]   $Model = '',
    [string]   $Claude = 'claude',
    [string]   $Between = '',
    [string]   $Out = '',
    [switch]   $DryRun
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $TasksDir) {
    if ($Language -eq 'it') {
        Write-Host "-Language it needs -TasksDir: the Italian version of the tasks is not in the repository (decision D9)." -ForegroundColor Red
        exit 2
    }
    $TasksDir = $PSScriptRoot
}
if (-not (Test-Path -LiteralPath $TasksDir)) { Write-Host "no folder $TasksDir" -ForegroundColor Red; exit 2 }
if (-not $Bridge) { $Bridge = Join-Path $root 'bin\oce-solid-agent-mcp.exe' }
if (-not (Test-Path -LiteralPath $Bridge)) { Write-Host "no bridge at $Bridge (build it with .\build.ps1)" -ForegroundColor Red; exit 2 }
if ($Repeats -lt 1) { Write-Host "-Repeats is at least 1" -ForegroundColor Red; exit 2 }

# The tasks: the files NN_name.md, and in each the first blockquote (the words
# of the person who asks).
$tasks = @(Get-ChildItem -LiteralPath $TasksDir -Filter '*.md' |
    Where-Object { $_.Name -match '^\d\d_.+\.md$' } | Sort-Object Name)
if ($Only.Count -gt 0) { $tasks = @($tasks | Where-Object { $Only -contains $_.BaseName -or $Only -contains $_.Name.Substring(0, 2) }) }
if ($tasks.Count -eq 0) { Write-Host "no task NN_*.md in $TasksDir" -ForegroundColor Red; exit 2 }

function TaskText([string] $file) {
    $lines = Get-Content -LiteralPath $file -Encoding UTF8
    $quote = @()
    foreach ($l in $lines) {
        if ($l -match '^>\s?(.*)$') { $quote += $Matches[1] }
        elseif ($quote.Count -gt 0) { break }
    }
    return ($quote -join "`n").Trim()
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
if (-not $Out) {
    $name = $stamp + '-' + $Language + $(if ($Label) { '-' + $Label } else { '' })
    $Out = Join-Path $root (Join-Path 'Banco\report\autonomous' $name)
}
$catalogue = Join-Path (Split-Path -Parent $Bridge) 'catalogo.json'
$catalogueHash = if (Test-Path -LiteralPath $catalogue) { (Get-FileHash -LiteralPath $catalogue -Algorithm SHA256).Hash.Substring(0, 12) } else { '(none next to the bridge)' }

# The bridge as the only MCP server of the session.
$config = [ordered]@{ mcpServers = [ordered]@{ solidworks = [ordered]@{ command = $Bridge; args = @() } } }
$configJson = $config | ConvertTo-Json -Depth 5

Write-Host ("autonomous run: " + $tasks.Count + " tasks x " + $Repeats + ", language " + $Language +
            ", bridge " + $Bridge + " (catalogue " + $catalogueHash + ")")
Write-Host ("output: " + $Out)

$commands = @()
foreach ($t in $tasks) {
    for ($n = 1; $n -le $Repeats; $n++) {
        $commands += [pscustomobject]@{ Task = $t.BaseName; N = $n; Text = (TaskText $t.FullName) }
    }
}
foreach ($c in $commands) {
    if (-not $c.Text) { Write-Host ("no blockquote in " + $c.Task + ": the words of the task are the first '> ' lines") -ForegroundColor Red; exit 2 }
}

if ($DryRun) {
    foreach ($c in $commands) {
        Write-Host ("  " + $c.Task + " #" + $c.N + ": " + $Claude + " -p <" + $c.Text.Length + " characters> --mcp-config <bridge only> " +
                    "--strict-mcp-config --allowedTools mcp__solidworks --output-format stream-json --verbose --max-turns " + $MaxTurns +
                    $(if ($Model) { " --model " + $Model } else { "" }))
    }
    Write-Host "dry run: nothing started"
    exit 0
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$configFile = Join-Path $Out 'mcp-config.json'
[IO.File]::WriteAllText($configFile, $configJson, (New-Object System.Text.UTF8Encoding($false)))
$started = Get-Date
$failed = 0

foreach ($c in $commands) {
    $base = Join-Path $Out ($c.Task + '-' + $c.N)
    if ($Between) {
        Write-Host ("  before " + $c.Task + " #" + $c.N + ": " + $Between)
        & powershell -NoProfile -Command $Between | Out-Host
    }
    Write-Host ("  " + $c.Task + " #" + $c.N + " ...")
    $arguments = @('-p', $c.Text, '--mcp-config', $configFile, '--strict-mcp-config',
                   '--allowedTools', 'mcp__solidworks', '--output-format', 'stream-json', '--verbose',
                   '--max-turns', [string]$MaxTurns)
    if ($Model) { $arguments += @('--model', $Model) }
    try {
        & $Claude @arguments 2> ($base + '.stderr.txt') | Out-File -LiteralPath ($base + '.jsonl') -Encoding utf8
        if ($LASTEXITCODE -ne 0) { Write-Host ("    the session ended with " + $LASTEXITCODE) -ForegroundColor Yellow }
    } catch {
        Write-Host ("    the session did not start: " + $_.Exception.Message) -ForegroundColor Red
        $failed++
        continue
    }
    # The finished part, read by a fresh bridge.
    & node (Join-Path $PSScriptRoot 'score-autonomous.js') --read-state $Bridge ($base + '.state.json') | Out-Host
}

$run = [ordered]@{
    language = $Language; tasks_dir = $TasksDir; repeats = $Repeats; bridge = $Bridge
    catalogue_sha256 = $catalogueHash; model = $Model; max_turns = $MaxTurns; label = $Label
    started = $started.ToString('s'); ended = (Get-Date).ToString('s'); sessions = $commands.Count; not_started = $failed
}
[IO.File]::WriteAllText((Join-Path $Out 'run.json'), ($run | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("done: " + ($commands.Count - $failed) + " of " + $commands.Count + " sessions; score with:")
Write-Host ("  node Banco\tasks\score-autonomous.js " + $Out)
if ($failed -gt 0) { exit 1 }
exit 0
