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
#                                  protocol (en), catalogue hash, repeats, model,
#                                  the isolation of the sessions, start and end
#
#  Scoring is another step (score-autonomous.js), so that a scorer that gets
#  better never needs the model to run again.
#
#  D9: every task x3 on the ITALIAN version (the threshold) and x1 on the
#  English one (a signal); the medians per task of the run before the rename
#  window (its score.json, kept) against the run after it, at most one task of
#  fifteen worse. The commands are in Bench/tasks/README.md.
#
#  NEEDS SOLIDWORKS, a built bridge and the claude CLI.
#
#  EVERY SESSION STARTS WITH NO DOCUMENT OPEN. The run of 6/10 closed between
#  two tasks only the documents never saved, so what task 08 saved in its
#  temporary folder (the assembly and its parts) stayed open for every later
#  session, and a weld profile of the library stayed open after tasks 06-07: a
#  task that finds the part of the previous one in front is not measuring the
#  model. Now the runner reads the documents open BEFORE the run
#  (session-documents.js --snapshot, written in <out>/documents-before-run.json:
#  the person's, never closed) and, before and after every session, closes
#  every other open document whatever its state (session-documents.js
#  --close-all: saved files of the temporary folder, library files, drawings,
#  unsaved ones). A document that cannot be closed stops that session, which is
#  counted as not started: it would start from something else than an empty
#  SOLIDWORKS. -Between stays as an extra command, run after that.
#
#  THE PREREQUISITES. Seven tasks start from the result of an earlier one ("the
#  L-bracket of task 01"); each session runs in an empty folder, so the model
#  found a box and said the bracket was not there. The runner builds that part
#  BEFORE the session through the same bridge, from a reference plan of
#  Bench/tasks/prerequisites/ (index.json says which task needs which plan),
#  saved in the folder of the session and open in front, and checks it against
#  the numbers of its own task (prerequisite.js --build). The text of the task
#  and its measure are not changed. A prerequisite that does not hold means the
#  session would measure another part: the session is NOT run, the run says why
#  (<task>-<n>.prerequisite.json, and run.json) and exits 1.
#
#  THE PREREQUISITES ARE BUILT WITH ONE BRIDGE FOR BOTH SIDES OF D9
#  (-PrerequisiteBridge, by default the bridge of this repository, the one
#  AFTER). The part a session starts from is a FIXTURE, not a measure: the run
#  measures what the model does with it. Built by the bridge under test, the
#  starting part of the two sides could differ by what the bridge builds
#  differently (the stand of task 07 cannot even be built by the bridge before,
#  which lists only the 60 largest faces), and the comparison would then
#  measure the fixtures. So both sides start from the same file, built by the
#  same bridge, and -Bridge is only what the session talks to. run.json says
#  which bridge built them.
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
#  session could not start (or its prerequisite did not hold, or a document
#  could not be closed), 2 on a bad argument.
# =============================================================================

param(
    [ValidateSet('it', 'en')] [string] $Language = 'en',
    [string]   $TasksDir = '',
    [int]      $Repeats = 1,
    [string]   $Bridge = '',
    [string]   $PrerequisiteBridge = '',
    [string]   $Label = '',
    [string[]] $Only = @(),
    [int]      $MaxTurns = 80,
    [string]   $Model = '',
    [string]   $Claude = 'claude',
    [string]   $Between = '',
    [string]   $PrerequisitesDir = '',
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
# The bridge that builds the starting parts: the one of this repository unless said (see the head of this file).
if (-not $PrerequisiteBridge) { $PrerequisiteBridge = Join-Path $root 'bin\oce-solid-agent-mcp.exe' }
if (-not (Test-Path -LiteralPath $PrerequisiteBridge)) { Write-Host "no bridge at $PrerequisiteBridge for the prerequisites (build it with .\build.ps1, or give -PrerequisiteBridge)" -ForegroundColor Red; exit 2 }
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

# Which task needs which reference plan (prerequisites/index.json).
if (-not $PrerequisitesDir) { $PrerequisitesDir = Join-Path $PSScriptRoot 'prerequisites' }
if (-not (Test-Path -LiteralPath (Join-Path $PrerequisitesDir 'index.json'))) { Write-Host "no index.json in $PrerequisitesDir" -ForegroundColor Red; exit 2 }
$prerequisiteIndex = Get-Content -LiteralPath (Join-Path $PrerequisitesDir 'index.json') -Raw -Encoding UTF8 | ConvertFrom-Json
function PrerequisiteOf([string] $task) {
    $entry = $prerequisiteIndex.tasks.PSObject.Properties | Where-Object { $_.Name -eq $task.Substring(0, 2) } | Select-Object -First 1
    if ($entry) { return $entry.Value }
    return $null
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
if (-not $Out) {
    $name = $stamp + '-' + $Language + $(if ($Label) { '-' + $Label } else { '' })
    $Out = Join-Path $root (Join-Path 'Bench\report\autonomous' $name)
}
# absolute: every session runs in a temporary folder of its own
$Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
$Bridge = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Bridge)
$PrerequisiteBridge = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PrerequisiteBridge)
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

# The bridge's version and protocol, read before anything runs.
$bridgeInfo = $null
if (-not $DryRun) {
    $infoLine = & node (Join-Path $PSScriptRoot 'score-autonomous.js') --bridge-info $Bridge
    try { $bridgeInfo = $infoLine | ConvertFrom-Json } catch { $bridgeInfo = $null }
    if ($null -eq $bridgeInfo -or $bridgeInfo.protocol -eq 'unknown') {
        Write-Host ("the bridge did not say its tools (" + $infoLine + "): no run") -ForegroundColor Red
        exit 1
    }
    Write-Host ("bridge " + $bridgeInfo.version + ", protocol " + $bridgeInfo.protocol)
    $prerequisiteInfo = $null
    $prerequisiteLine = & node (Join-Path $PSScriptRoot 'score-autonomous.js') --bridge-info $PrerequisiteBridge
    try { $prerequisiteInfo = $prerequisiteLine | ConvertFrom-Json } catch { $prerequisiteInfo = $null }
    if ($null -eq $prerequisiteInfo -or $prerequisiteInfo.protocol -eq 'unknown') {
        Write-Host ("the bridge of the prerequisites did not say its tools (" + $prerequisiteLine + "): no run") -ForegroundColor Red
        exit 1
    }
    Write-Host ("prerequisites built by bridge " + $prerequisiteInfo.version + " (" + $PrerequisiteBridge + ")")
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
        $needs = PrerequisiteOf $c.Task
        if ($needs) { Write-Host ("      before it: the prerequisite " + $needs.plan + " (the result of task " + $needs.from_task + "), built in that folder by " + $PrerequisiteBridge + ", saved and open in front") }
    }
    # What can be checked without SOLIDWORKS: the reference plans against the
    # catalogue and the dry run of the bridge, the index against the texts of
    # the tasks, and the self-tests of the two scripts that close and prepare.
    $dryProblems = 0
    foreach ($check in @(
            @('prerequisite.js', '--check', '--bridge', $PrerequisiteBridge, '--dir', $PrerequisitesDir),
            @('prerequisite.js', '--self-test'),
            @('session-documents.js', '--self-test'))) {
        & node (Join-Path $PSScriptRoot $check[0]) @($check[1..($check.Count - 1)]) | Out-Host
        if ($LASTEXITCODE -ne 0) { $dryProblems++ }
    }
    if ($dryProblems -gt 0) { Write-Host ("dry run: " + $dryProblems + " check(s) failed") -ForegroundColor Red; exit 1 }
    Write-Host "dry run: nothing started"
    exit 0
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$configFile = Join-Path $Out 'mcp-config.json'
[IO.File]::WriteAllText($configFile, $configJson, (New-Object System.Text.UTF8Encoding($false)))
$started = Get-Date
$failed = 0
$prerequisitesBuilt = @()
$prerequisitesFailed = @()

# The documents open before the run are the person's: written down once, never
# closed (session-documents.js, which holds the rule and its self-test).
$spareFile = Join-Path $Out 'documents-before-run.json'
& node (Join-Path $PSScriptRoot 'session-documents.js') --snapshot $spareFile | Out-Host
if ($LASTEXITCODE -ne 0) { Write-Host "could not read the documents open in SOLIDWORKS: no run" -ForegroundColor Red; exit 1 }
$sparedDocuments = @((Get-Content -LiteralPath $spareFile -Raw -Encoding UTF8 | ConvertFrom-Json).documents)
if ($sparedDocuments.Count -gt 0) {
    Write-Host ("WARNING: " + $sparedDocuments.Count + " document(s) were open before the run and stay open: every session will find them in front of it. Close them first if the run must start from an empty SOLIDWORKS.") -ForegroundColor Yellow
}

# Closes every open document that was not open before the run; false when one
# stays open (the session after it would not start from an empty SOLIDWORKS).
function CloseAll {
    & node (Join-Path $PSScriptRoot 'session-documents.js') --close-all --spare $spareFile | Out-Host
    return ($LASTEXITCODE -eq 0)
}

foreach ($c in $commands) {
    $base = Join-Path $Out ($c.Task + '-' + $c.N)
    if (-not (CloseAll)) {
        Write-Host ("  " + $c.Task + " #" + $c.N + " NOT STARTED: a document that must be closed is still open") -ForegroundColor Red
        $failed++
        continue
    }
    if ($Between) {
        Write-Host ("  before " + $c.Task + " #" + $c.N + ": " + $Between)
        & powershell -NoProfile -Command $Between | Out-Host
    }
    Write-Host ("  " + $c.Task + " #" + $c.N + " ...")
    $arguments = SessionArguments $c.Text $configFile
    # an empty folder of its own: nothing of the repository, no project memory
    $work = Join-Path ([IO.Path]::GetTempPath()) ('d9-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $work | Out-Null

    # The part the task starts from, built before the session, in its folder.
    $ready = $true
    $needs = PrerequisiteOf $c.Task
    if ($needs) {
        Write-Host ("    prerequisite: " + $needs.plan + " (the result of task " + $needs.from_task + ")")
        & node (Join-Path $PSScriptRoot 'prerequisite.js') --build $PrerequisiteBridge $needs.plan $work --out ($base + '.prerequisite.json') --dir $PrerequisitesDir | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Host "    the prerequisite did not hold: the session is NOT run (it would measure another part); see the .prerequisite.json" -ForegroundColor Red
            $prerequisitesFailed += ($c.Task + '-' + $c.N)
            $failed++
            $ready = $false
        } else { $prerequisitesBuilt += ($c.Task + '-' + $c.N + ': ' + $needs.plan) }
    }

    if ($ready) {
        Push-Location -LiteralPath $work
        try {
            & $Claude @arguments 2> ($base + '.stderr.txt') | Out-File -LiteralPath ($base + '.jsonl') -Encoding utf8
            if ($LASTEXITCODE -ne 0) { Write-Host ("    the session ended with " + $LASTEXITCODE) -ForegroundColor Yellow }
            $ran = $true
        } catch {
            Write-Host ("    the session did not start: " + $_.Exception.Message) -ForegroundColor Red
            $failed++
            $ran = $false
        } finally {
            Pop-Location
        }
        # The finished part, read by a fresh bridge (before the documents are closed).
        if ($ran) { & node (Join-Path $PSScriptRoot 'score-autonomous.js') --read-state $Bridge ($base + '.state.json') | Out-Host }
    }

    # What the session opened is closed BEFORE its folder goes: an open file of
    # that folder keeps it from being removed.
    [void](CloseAll)
    try { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction Stop } catch { Write-Host ("    could not remove " + $work) -ForegroundColor Yellow }
}
# and the run leaves SOLIDWORKS as it found it
if (-not (CloseAll)) { Write-Host "a document opened by the run is still open" -ForegroundColor Yellow }

$run = [ordered]@{
    language = $Language; tasks_dir = $TasksDir; repeats = $Repeats; bridge = $Bridge
    bridge_version = $bridgeInfo.version; bridge_protocol = $bridgeInfo.protocol; isolation = $isolation
    prerequisite_bridge = $PrerequisiteBridge; prerequisite_bridge_version = $prerequisiteInfo.version
    catalogue_sha256 = $catalogueHash; model = $Model; max_turns = $MaxTurns; label = $Label
    started = $started.ToString('s'); ended = (Get-Date).ToString('s'); sessions = $commands.Count; not_started = $failed
    documents_before_run = 'documents-before-run.json'
    prerequisites_built = @($prerequisitesBuilt); prerequisites_failed = @($prerequisitesFailed)
}
[IO.File]::WriteAllText((Join-Path $Out 'run.json'), ($run | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("done: " + ($commands.Count - $failed) + " of " + $commands.Count + " sessions; score with:")
Write-Host ("  node Bench\tasks\score-autonomous.js " + $Out)
if ($failed -gt 0) { exit 1 }
exit 0
