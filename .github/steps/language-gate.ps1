# =============================================================================
#  The language gate on the CI (entry W3.1-bis.1)
# =============================================================================
#
#  The Italian in the repository may go down, never up. The local pre-push
#  hook says so too, but a hook is skipped with --no-verify and exists only
#  where someone ran install-hooks.ps1: THIS is the gate that counts.
#
#  EVERY COMMIT OF THE RANGE IS JUDGED ON ITS OWN, against its own parent(s):
#
#    a. its tree is within its own committed baseline.json (so the next
#       commit is judged against a baseline that tells the truth);
#    b. if it changes the gate itself (the detector, the lexicon, the
#       exceptions, this script, the workflow, CODEOWNERS), it carries the
#       trailer   Language-Gate-Config-Change: <reason>
#    c. its baseline.json is not above the parent's one;
#    d. its tree is not above the parent's baseline, measured with the
#       DETECTOR, LEXICON AND EXCEPTIONS OF THE PARENT: a commit cannot loosen
#       the rules it is judged by;
#    c and d pass anyway if THAT commit carries the trailer
#       Language-Baseline-Raise: <reason>
#    (and the reason goes into the CHANGELOG entry). A signature covers only
#    the commit that carries it, never the rest of the range.
#
#  Trailers are read by git (%(trailers)): the last paragraph of the message,
#  "Key: value" lines. A line with the same words in the middle of the body is
#  not a signature.
#
#  A renamed file (git diff -M between the commit and its first parent) is
#  compared with the counts of its old path: a rename is not new Italian.
#  A merge commit is judged against the union of its parents' baselines
#  (per file the larger count, per set the union): the Italian a merged
#  branch brings was judged commit by commit on that branch, and those
#  commits are in the range too.
#
#  THE SCRIPT THAT JUDGES IS THE ONE OF THE BASE. If this file differs between
#  the base and the head, the base's copy is run instead (-Delegated): a change
#  to the gate takes effect only once it is merged. On its own this protects
#  nothing against a pull request that edits this file, because in tests.yml
#  the copy that decides whether to delegate comes from the pull request. What
#  protects it is .github/workflows/language-gate.yml: on pull_request_target
#  the workflow and the checkout are the target branch's, so the base's copy
#  runs and judges the pull request's commits from the object store
#  (-FetchPullRequest). CODEOWNERS covers .github/ and Checks/language/, and
#  (b) makes every such change visible in the history.
#
#  The first commits of W3.1-bis.1 carry a detector from before per-commit
#  judging (it has no --config-rev): they are treated like commits before the
#  gate, and the first commit after them judges only itself (a).
#
#  The base is, in order:
#    - for a pull request (GITHUB_EVENT_NAME=pull_request) whose checkout is
#      the merge commit: its first parent, i.e. the target branch as it is now
#      (pull_request.base.sha is not updated when main moves on);
#    - $env:LANGUAGE_GATE_BASE (the workflow passes github.event.before for
#      a push and the "base" input of a manual run);
#    - the first parent of the head when that is empty, all zeros (a new
#      branch) or not in the clone.
#
#  At a release tag (a v* tag on HEAD) the category `transition` must be 0.
#
#      .\.github\steps\language-gate.ps1
#      .\.github\steps\language-gate.ps1 -Base origin/main
#
#  Exit 0 if the gate holds, 1 otherwise, 2 if it cannot run.
# =============================================================================

param(
    [string] $Base = $env:LANGUAGE_GATE_BASE,
    [string] $Root = '',
    [switch] $Delegated,
    [string] $Head = 'HEAD',
    [string] $FetchPullRequest = '',
    [switch] $PullRequest = ($env:GITHUB_EVENT_NAME -eq 'pull_request')
)

$ErrorActionPreference = 'Stop'
# git prints paths and trailers in UTF-8
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
if (-not $Root) { $Root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)) }
$root = (Resolve-Path $Root).Path
# Rename window 3 (W3.1-bis.6) moves the gate's folder from Controlli/lingua  # window-3: keep
# to Checks/language, this script from .github/passi to .github/steps and  # window-3: keep
# prove.yml to tests.yml. Each commit is read where ITS files are (the newer  # window-3: keep
# names first), so the commits on both sides of the move are judged; a commit
# whose first parent has the gate and that has none under either name is a
# failure, not "before the gate".
$langDirs = @('Checks/language', 'Controlli/lingua')  # window-3: keep
$selfRels = @('.github/steps/language-gate.ps1', '.github/passi/language-gate.ps1')  # window-3: keep
$configPaths = @('Checks/language/detect-italian.js', 'Checks/language/lexicon.json', 'Checks/language/exceptions.json',
                 'Controlli/lingua/detect-italian.js', 'Controlli/lingua/lexicon.json', 'Controlli/lingua/exceptions.json',  # window-3: keep
                 '.github/steps/language-gate.ps1', '.github/passi/language-gate.ps1',  # window-3: keep
                 '.github/workflows/tests.yml', '.github/workflows/prove.yml', '.github/workflows/language-gate.yml',  # window-3: keep
                 '.github/CODEOWNERS')
$detectorRel = 'Checks/language/detect-italian.js'
$baselineRel = 'Checks/language/baseline.json'

function Invoke-Git {
    # Windows PowerShell turns a native command's stderr into a terminating
    # error under 'Stop'; `git cat-file -e` on a missing object is an answer,
    # not an error.
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $out = & git -C $root @args 2>$null } finally { $ErrorActionPreference = $old }
    return @{ Code = $LASTEXITCODE; Out = $out }
}

function Test-GitObject([string] $spec) { return (Invoke-Git cat-file -e $spec).Code -eq 0 }

# The folder of the gate's files in a commit (Checks/language or, before
# rename window 3, Controlli/lingua), or $null when it has no detector.  # window-3: keep
function Get-LangDir([string] $commit) {
    foreach ($d in $langDirs) { if (Test-GitObject "${commit}:$d/detect-italian.js") { return $d } }
    return $null
}
function Get-DetectorRel([string] $commit) { $d = Get-LangDir $commit; if ($d) { return "$d/detect-italian.js" } return $null }
function Get-BaselineRel([string] $commit) { $d = Get-LangDir $commit; if ($d) { return "$d/baseline.json" } return $null }
function Get-SelfRel([string] $commit) {
    foreach ($s in $selfRels) { if (Test-GitObject "${commit}:$s") { return $s } }
    return $null
}

# A commit whose detector can judge commits from the object store: it has the
# detector, and the detector knows --config-rev.
function Test-PerCommitGate([string] $commit) {
    $rel = Get-DetectorRel $commit
    if (-not $rel) { return $false }
    return (Invoke-Git grep -q -F -e "'--config-rev'" $commit '--' $rel).Code -eq 0
}

# A blob written byte for byte: a PowerShell redirection would re-encode it.
function Save-GitBlob([string] $spec, [string] $file) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.Arguments = "-C `"$root`" cat-file blob `"$spec`""
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $fs = [System.IO.File]::Create($file)
    try { $p.StandardOutput.BaseStream.CopyTo($fs) } finally { $fs.Close() }
    $null = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { throw "git cat-file blob $spec failed" }
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("language-gate-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null
$detectors = @{}
# The detector of a commit, extracted once per distinct blob.
function Get-Detector([string] $commit) {
    $blob = ((Invoke-Git rev-parse ("${commit}:" + (Get-DetectorRel $commit))).Out | Select-Object -First 1)
    if (-not $detectors.ContainsKey($blob)) {
        $file = Join-Path $work "detect-$blob.js"
        Save-GitBlob $blob $file
        $detectors[$blob] = $file
    }
    return $detectors[$blob]
}

function Invoke-Detector([string] $detector, [string[]] $arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $out = & node $detector --root $root @arguments 2>&1 } finally { $ErrorActionPreference = $old }
    return @{ Code = $LASTEXITCODE; Out = @($out | ForEach-Object { "$_" }) }
}

function Write-Indented($lines) { foreach ($l in @($lines)) { if ("$l".Trim()) { Write-Output ("      " + $l) } } }

function Get-Trailer([string] $commit, [string] $key) {
    $r = Invoke-Git log -1 "--format=%(trailers:key=$key,valueonly)" $commit
    $values = @($r.Out | Where-Object { "$_".Trim() })
    if ($values.Count -eq 0) {
        # the words are in the message but git does not read them as a
        # trailer: say so, it is the usual slip (a paragraph after them, or
        # two trailers in two paragraphs)
        # (Write-Host, not Write-Output: the output of this function is the
        # list of signatures, and a note in it would read as one)
        $body = @((Invoke-Git log -1 --format=%B $commit).Out) -join "`n"
        if ($body -match ('(?m)^\s*' + [regex]::Escape($key) + '\s*:')) {
            Write-Host "    note: '${key}:' is in the message of $($commit.Substring(0, 10)) but not as a trailer; trailers go together in the LAST paragraph."
        }
    }
    return $values
}

try {
    # the workflow on pull_request_target checks out the BASE and names the
    # pull request: its merge commit is fetched and judged from the object
    # store, with the rules of the base
    if ($FetchPullRequest) {
        $r = Invoke-Git fetch --quiet --no-tags origin "+refs/pull/$FetchPullRequest/merge:refs/language-gate/pr-merge"
        if ($r.Code -ne 0) { Write-Output "LANGUAGE GATE: cannot fetch the merge commit of pull request $FetchPullRequest (a conflict?)"; exit 2 }
        $Head = 'refs/language-gate/pr-merge'
        $PullRequest = $true
    }
    if ((Invoke-Git rev-parse --verify -q "$Head^{commit}").Code -ne 0) { Write-Output "LANGUAGE GATE: no commit $Head in $root"; exit 2 }
    $head = ((Invoke-Git rev-parse "$Head^{commit}").Out | Select-Object -First 1)

    # -- the base -------------------------------------------------------------
    $headParents = @(((Invoke-Git rev-list --parents -n 1 $head).Out | Select-Object -First 1) -split ' ' | Select-Object -Skip 1)
    if ($PullRequest -and $headParents.Count -eq 2) {
        Write-Output "language gate: pull request merge commit; the base is its first parent (the target branch now)"
        $Base = $headParents[0]
    } elseif (-not $Base -or $Base -match '^0+$' -or -not (Test-GitObject "$Base^{commit}")) {
        if ($Base) { Write-Output "base '$Base' is not usable here; falling back to HEAD~1" }
        if ($headParents.Count -gt 0) { $Base = $headParents[0] } else { $Base = '' }
    }
    $baseSha = ''
    if ($Base) { $baseSha = ((Invoke-Git rev-parse $Base).Out | Select-Object -First 1) }
    Write-Output "language gate: base $Base ($baseSha), head $head"

    # -- the script that judges is the one of the base ------------------------
    $selfRel = if ($baseSha) { Get-SelfRel $baseSha } else { $null }
    if (-not $Delegated -and $baseSha -and $selfRel -and (Test-PerCommitGate $baseSha)) {
        $mine = ((Invoke-Git hash-object $MyInvocation.MyCommand.Definition).Out | Select-Object -First 1)
        $theirs = ((Invoke-Git rev-parse "${baseSha}:$selfRel").Out | Select-Object -First 1)
        if ($mine -ne $theirs) {
            Write-Output "language gate: $selfRel changed in this range; the copy of the base judges it."
            if (-not ((Invoke-Git grep -q -F -e 'Get-LangDir' $baseSha '--' $selfRel).Code -eq 0)) {
                Write-Output "language gate: WARNING - the copy of the base looks for the gate only under Controlli/lingua:"  # window-3: keep
                Write-Output "    a commit of this range that has it under Checks/language is skipped there, as if it came before the gate."
                Write-Output "    Merge the commit that teaches the gate both folder names first, or judge with this copy (-Delegated)."
            }
            $copy = Join-Path $work 'language-gate-of-the-base.ps1'
            Save-GitBlob $theirs $copy
            $shell = (Get-Process -Id $PID).Path
            & $shell -NoProfile -ExecutionPolicy Bypass -File $copy -Base $baseSha -Root $root -Head $head -Delegated
            exit $LASTEXITCODE
        }
    }

    # -- the commits ----------------------------------------------------------
    if ($baseSha) { $commits = @((Invoke-Git rev-list --reverse --topo-order "$baseSha..$head").Out | Where-Object { $_ }) }
    else { $commits = @($head) }
    Write-Output ("language gate: " + $commits.Count + " commit(s) to judge")

    $failed = $false
    foreach ($c in $commits) {
        $short = $c.Substring(0, 10)
        $subject = ((Invoke-Git log -1 --format=%s $c).Out | Select-Object -First 1)
        Write-Output ""
        Write-Output "commit $short  $subject"
        $detectorRel = Get-DetectorRel $c
        $baselineRel = Get-BaselineRel $c
        if (-not $detectorRel) {
            $p0 = ((Invoke-Git rev-list --parents -n 1 $c).Out | Select-Object -First 1) -split ' ' | Select-Object -Skip 1 | Select-Object -First 1
            if ($p0 -and (Get-DetectorRel $p0)) {
                Write-Output "  LANGUAGE GATE: $short has no detector under $($langDirs -join ' or '), and its parent had one: a commit cannot step out of the gate"
                $failed = $true; continue
            }
            Write-Output "    before the gate: nothing to judge"; continue
        }
        if (-not (Test-PerCommitGate $c)) {
            # the first commits of W3.1-bis.1, before the review: their
            # detector cannot read a commit from the object store, and their
            # baseline counts lines, not words. They are judged like the
            # commits before the gate; the first commit after them judges
            # itself only (a), as a commit that introduces the gate.
            Write-Output "    a gate from before per-commit judging (no --config-rev): nothing to judge"; continue
        }
        if (-not (Test-GitObject "${c}:$baselineRel")) {
            Write-Output "  LANGUAGE GATE: $short has the detector but no $baselineRel"; $failed = $true; continue
        }
        # @(): one signature comes back as a bare string, and [0] of a
        # string is its first letter
        $raise = @(Get-Trailer $c 'Language-Baseline-Raise')
        $configSigned = @(Get-Trailer $c 'Language-Gate-Config-Change')
        $own = Get-Detector $c

        # a. the tree against its own baseline
        $r = Invoke-Detector $own @('--rev', $c, '--config-rev', $c, '--baseline', "git:$c", '--quiet')
        if ($r.Code -eq 1) {
            Write-Output "  LANGUAGE GATE: the tree of $short is ABOVE its own ${baselineRel}:"
            Write-Indented $r.Out
            $failed = $true
        } elseif ($r.Code -ne 0) {
            Write-Output "  LANGUAGE GATE: the detector of $short cannot run (exit $($r.Code)):"
            Write-Indented $r.Out
            exit 2
        } else { Write-Output "    a. within its own baseline" }

        $parents = @(((Invoke-Git rev-list --parents -n 1 $c).Out | Select-Object -First 1) -split ' ' | Select-Object -Skip 1)
        $gated = @($parents | Where-Object { (Test-PerCommitGate $_) -and (Test-GitObject ("${_}:" + (Get-BaselineRel $_))) })
        if ($parents.Count -eq 0 -or -not ($gated -contains $parents[0])) {
            Write-Output "    the first parent has no gate: this commit introduces it, only (a) applies"
            continue
        }
        $p1 = $parents[0]

        # b. a change to the gate itself is signed
        $changed = @((Invoke-Git diff --name-only $p1 $c '--' @configPaths).Out | Where-Object { $_ })
        foreach ($p in ($parents | Select-Object -Skip 1)) {
            $other = @((Invoke-Git diff --name-only $p $c '--' @configPaths).Out | Where-Object { $_ })
            $changed = @($changed | Where-Object { $other -contains $_ })
        }
        if ($changed.Count -gt 0) {
            if ($configSigned.Count -gt 0) {
                Write-Output ("    b. the gate itself changed (" + ($changed -join ', ') + "), signed: " + $configSigned[0])
            } else {
                Write-Output ("  LANGUAGE GATE: $short changes the gate itself (" + ($changed -join ', ') + ")")
                Write-Output "    without the trailer 'Language-Gate-Config-Change: <reason>'."
                $failed = $true
            }
        }

        # the renames against the first parent, for c and d
        $renamesFile = Join-Path $work "renames-$short.json"
        $map = @{}
        $ns = (Invoke-Git diff -M --name-status $p1 $c).Out
        foreach ($line in @($ns)) {
            $f = "$line" -split "`t"
            if ($f.Count -eq 3 -and $f[0] -match '^R') { $map[$f[2]] = $f[1] }
        }
        $json = if ($map.Count -gt 0) { $map | ConvertTo-Json -Compress } else { '{}' }
        [System.IO.File]::WriteAllText($renamesFile, $json, (New-Object System.Text.UTF8Encoding($false)))
        if ($map.Count -gt 0) { Write-Output ("    renamed: " + $map.Count + " file(s), compared with their old paths") }

        $parentDetector = Get-Detector $p1
        $parentBaseline = 'git:' + ($gated -join '+')

        # c. the committed baseline against the parent's
        $r = Invoke-Detector $parentDetector @('--compare-baselines', $parentBaseline, "git:$c", '--renames', $renamesFile, '--quiet')
        if ($r.Code -eq 1) {
            if ($raise.Count -gt 0) { Write-Output ("    c. baseline raised, signed: " + $raise[0]) }
            else {
                Write-Output "  LANGUAGE GATE: $short RAISES $baselineRel without the trailer 'Language-Baseline-Raise: <reason>':"
                Write-Indented $r.Out
                $failed = $true
            }
        } elseif ($r.Code -ne 0) { Write-Output "  LANGUAGE GATE: cannot compare the baselines of $short (exit $($r.Code))"; Write-Indented $r.Out; exit 2 }
        else { Write-Output "    c. baseline not raised" }

        # d. the tree against the parent's baseline, with the parent's rules
        $r = Invoke-Detector $parentDetector @('--rev', $c, '--config-rev', $p1, '--baseline', $parentBaseline, '--renames', $renamesFile, '--quiet')
        if ($r.Code -eq 1) {
            if ($raise.Count -gt 0) { Write-Output ("    d. above the parent's baseline, signed: " + $raise[0]) }
            else {
                Write-Output "  LANGUAGE GATE: the tree of $short is ABOVE the baseline of its parent (rules of the parent):"
                Write-Indented $r.Out
                $failed = $true
            }
        } elseif ($r.Code -ne 0) { Write-Output "  LANGUAGE GATE: cannot judge $short with the parent's rules (exit $($r.Code))"; Write-Indented $r.Out; exit 2 }
        else { Write-Output "    d. not above the parent's baseline" }
    }

    # -- a release tag: no transition left ------------------------------------
    $tags = @((Invoke-Git tag --points-at $head).Out | Where-Object { $_ -match '^v\d' })
    if ($tags.Count -gt 0 -and (Get-DetectorRel $head)) {
        $r = Invoke-Detector (Get-Detector $head) @('--rev', $head, '--config-rev', $head, '--zero', 'transition', '--baseline', "git:$head", '--quiet')
        if ($r.Code -eq 1) {
            Write-Output ("LANGUAGE GATE: release tag " + ($tags -join ', ') + " with Italian still in transition:")
            Write-Indented $r.Out
            $failed = $true
        } elseif ($r.Code -ne 0) { Write-Output "LANGUAGE GATE: the tag check cannot run (exit $($r.Code))"; exit 2 }
        else { Write-Output ("language gate: release tag " + ($tags -join ', ') + ": transition is 0") }
    }

    Write-Output ""
    if ($failed) {
        Write-Output "LANGUAGE GATE: FAILED. New text is written in English. If a growth is intended (a rename"
        Write-Output "window, an approved exception), rewrite the baseline in THAT commit"
        Write-Output "(node Checks/language/detect-italian.js --write-baseline) and end its message with the"
        Write-Output "trailer 'Language-Baseline-Raise: <reason>'; a change to the gate itself needs"
        Write-Output "'Language-Gate-Config-Change: <reason>'."
        exit 1
    }
    Write-Output "language gate: held."
    exit 0
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
