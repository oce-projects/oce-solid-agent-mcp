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
#    <out>/run.json                what ran: language, bridge, its version and
#                                  protocol (en, or it for the bridge before
#                                  the rename), catalogue hash, repeats, model,
#                                  the isolation of the sessions, start and end
#
#  Scoring is another step (score-autonomous.js), so that a scorer that gets
#  better never needs the model to run again.
#
#  D9: every task x3 on the ITALIAN version (the threshold) and x1 on the
#  English one (a signal); the medians per task of the bridge before the rename
#  window (the catalogue of the snapshot) against the bridge after it, at most
#  one task of fifteen worse. The commands are in Bench/tasks/README.md.
#
#  NEEDS SOLIDWORKS, a built bridge and the claude CLI. It starts every task
#  with -Between (a command that closes the documents left open by the task
#  before, for example), because a task that finds the part of the previous
#  one in front is not measuring the model.
#
#  THE SESSION SEES THE BRIDGE AND NOTHING ELSE. A session started in the
#  repository could read Bench/tasks/NN_*.md, with the table of the expected
#  answers, and the cases; it would load the CLAUDE.md and the memories of the
#  person who runs it. So every session runs in an empty temporary folder of
#  its own (deleted after), with no built-in tool (--tools ""), no settings
#  file (--setting-sources ""), no skill (--disable-slash-commands) and the
#  bridge as its only MCP server (--strict-mcp-config). The user CLAUDE.md
#  (~/.claude/CLAUDE.md) is skipped only by --bare, which takes the API key
#  from ANTHROPIC_API_KEY and never the login: with that variable set the run
#  uses --bare, without it the run says so and records it in run.json
#  (isolation.user_claude_md). The options are those of claude 2.1.281
#  (claude --help); check them on the CLI that runs.
#
#      .\Bench\tasks\run-autonomous.ps1 -Language en -DryRun
#      .\Bench\tasks\run-autonomous.ps1 -Language it -TasksDir <Italian tasks> -Repeats 3 -Label after
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
    $Out = Join-Path $root (Join-Path 'Bench\report\autonomous' $name)
}
# absolute: every session runs in a temporary folder of its own
$Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
$Bridge = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Bridge)
$catalogue = Join-Path (Split-Path -Parent $Bridge) 'catalog.json'
$catalogueHash = if (Test-Path -LiteralPath $catalogue) { (Get-FileHash -LiteralPath $catalogue -Algorithm SHA256).Hash.Substring(0, 12) } else { '(none next to the bridge)' }

# The bridge as the only MCP server of the session.
$config = [ordered]@{ mcpServers = [ordered]@{ solidworks = [ordered]@{ command = $Bridge; args = @() } } }
$configJson = $config | ConvertTo-Json -Depth 5

# The isolation of every session (see the head of this file). An empty string
# reaches a native program from Windows PowerShell only written as "".
$empty = $(if ($PSVersionTable.PSVersion.Major -ge 7) { '' } else { '""' })
$bare = -not [string]::IsNullOrEmpty($env:ANTHROPIC_API_KEY)
$isolation = [ordered]@{
    working_folder = 'an empty temporary folder per session, deleted after'
    built_in_tools = 'none (--tools "")'
    settings_files = 'none (--setting-sources "")'
    skills = 'none (--disable-slash-commands)'
    mcp_servers = 'the bridge only (--strict-mcp-config)'
    user_claude_md = $(if ($bare) { 'skipped (--bare, ANTHROPIC_API_KEY)' } else { 'LOADED: without ANTHROPIC_API_KEY --bare cannot run, and ~/.claude/CLAUDE.md reaches the session' })
}
if (-not $bare) {
    Write-Host "ANTHROPIC_API_KEY is not set: the sessions cannot use --bare and will load ~/.claude/CLAUDE.md (recorded in run.json)." -ForegroundColor Yellow
}
function SessionArguments([string] $text, [string] $configFile) {
    $a = @('-p', $text, '--mcp-config', $configFile, '--strict-mcp-config',
           '--allowedTools', 'mcp__solidworks', '--tools', $empty, '--setting-sources', $empty,
           '--disable-slash-commands', '--output-format', 'stream-json', '--verbose',
           '--max-turns', [string]$MaxTurns)
    if ($bare) { $a += '--bare' }
    if ($Model) { $a += @('--model', $Model) }
    return $a
}

# The bridge's version and protocol, read before anything runs: the run before
# the rename speaks Italian, and the scorer reads it through the name map.
$bridgeInfo = $null
if (-not $DryRun) {
    $infoLine = & node (Join-Path $PSScriptRoot 'score-autonomous.js') --bridge-info $Bridge
    try { $bridgeInfo = $infoLine | ConvertFrom-Json } catch { $bridgeInfo = $null }
    if ($null -eq $bridgeInfo -or $bridgeInfo.protocol -eq 'unknown') {
        Write-Host ("the bridge did not say its tools (" + $infoLine + "): no run") -ForegroundColor Red
        exit 1
    }
    Write-Host ("bridge " + $bridgeInfo.version + ", protocol " + $bridgeInfo.protocol)
}

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
        $shown = (SessionArguments ('<' + $c.Text.Length + ' characters>') '<out>\mcp-config.json') | ForEach-Object { if ($_ -eq '' -or $_ -eq '""') { '""' } else { $_ } }
        Write-Host ("  " + $c.Task + " #" + $c.N + ", in an empty temporary folder: " + $Claude + " " + ($shown -join ' '))
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
    $arguments = SessionArguments $c.Text $configFile
    # an empty folder of its own: nothing of the repository, no project memory
    $work = Join-Path ([IO.Path]::GetTempPath()) ('d9-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    Push-Location -LiteralPath $work
    try {
        & $Claude @arguments 2> ($base + '.stderr.txt') | Out-File -LiteralPath ($base + '.jsonl') -Encoding utf8
        if ($LASTEXITCODE -ne 0) { Write-Host ("    the session ended with " + $LASTEXITCODE) -ForegroundColor Yellow }
    } catch {
        Write-Host ("    the session did not start: " + $_.Exception.Message) -ForegroundColor Red
        $failed++
        continue
    } finally {
        Pop-Location
        try { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction Stop } catch { Write-Host ("    could not remove " + $work) -ForegroundColor Yellow }
    }
    # The finished part, read by a fresh bridge.
    & node (Join-Path $PSScriptRoot 'score-autonomous.js') --read-state $Bridge ($base + '.state.json') | Out-Host
}

$run = [ordered]@{
    language = $Language; tasks_dir = $TasksDir; repeats = $Repeats; bridge = $Bridge
    bridge_version = $bridgeInfo.version; bridge_protocol = $bridgeInfo.protocol; isolation = $isolation
    catalogue_sha256 = $catalogueHash; model = $Model; max_turns = $MaxTurns; label = $Label
    started = $started.ToString('s'); ended = (Get-Date).ToString('s'); sessions = $commands.Count; not_started = $failed
}
[IO.File]::WriteAllText((Join-Path $Out 'run.json'), ($run | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("done: " + ($commands.Count - $failed) + " of " + $commands.Count + " sessions; score with:")
Write-Host ("  node Bench\tasks\score-autonomous.js " + $Out)
if ($failed -gt 0) { exit 1 }
exit 0
