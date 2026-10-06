# =============================================================================
#  The language gate on the CI (entry W3.1-bis.1, closed in W3.1-bis.11)
# =============================================================================
#
#  language-gate-strict: the gate judges the tip against ZERO.
#
#  The repository is English. The rule is the one of the release conditions:
#  0 in every category of Checks/language/detect-italian.js, for every file,
#  outside the exceptions of Checks/language/exceptions.json THAT ARE SIGNED.
#  An entry counts only when the owner has signed it (signed_by and signed_on
#  are not null): until then the gate fails and prints, apart, the entries that
#  wait for the signature and the Italian that no entry covers. There is no
#  baseline and no ratchet any more: a tree is judged by itself, not by its
#  parent.
#
#  WHAT IS JUDGED: the tip (HEAD, or the merge commit of a pull request), read
#  from the object store, so that work not yet committed changes nothing.
#
#  TWO RULES BESIDES THE ZERO:
#
#    a. A CHANGE TO THE GATE ITSELF IS SIGNED. Every commit of the range
#       base..head that changes the detector, the lexicon, the exceptions, this
#       script, the workflow or CODEOWNERS carries the trailer
#           Language-Gate-Config-Change: <reason>
#       so that a loosened rule is visible in the history. Trailers are read by
#       git (%(trailers)): the last paragraph of the message, "Key: value"
#       lines. A line with the same words in the middle of the body is not a
#       signature.
#    b. THE CODE THAT JUDGES IS THE ONE OF THE BASE. If this file differs
#       between the base and the head, and the base has a gate of this kind
#       (it carries the marker at the top), the base's copy is run instead
#       (-Delegated), and the detector of the base judges the tip: a change to
#       the gate takes effect only once it is merged. On its own this protects
#       nothing against a pull request that edits this file, because in
#       tests.yml the copy that decides whether to delegate comes from the pull
#       request. What protects it is .github/workflows/language-gate.yml: on
#       pull_request_target the workflow and the checkout are the target
#       branch's, so the base's copy runs and judges the pull request's merge
#       commit from the object store (-FetchPullRequest). CODEOWNERS covers
#       .github/ and Checks/language/, and (a) makes every change visible.
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
$selfRel = '.github/steps/language-gate.ps1'
$detectorRel = 'Checks/language/detect-italian.js'
$configPaths = @('Checks/language/detect-italian.js', 'Checks/language/lexicon.json', 'Checks/language/exceptions.json',
                 '.github/steps/language-gate.ps1', '.github/workflows/tests.yml', '.github/workflows/language-gate.yml',
                 '.github/CODEOWNERS')

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

# A commit that has a gate of this kind: the detector, and this script with its marker.
function Test-StrictGate([string] $commit) {
    if (-not (Test-GitObject "${commit}:$detectorRel")) { return $false }
    if (-not (Test-GitObject "${commit}:$selfRel")) { return $false }
    return (Invoke-Git grep -q -F -e 'language-gate-strict' $commit '--' $selfRel).Code -eq 0
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

function Get-Trailer([string] $commit, [string] $key) {
    $r = Invoke-Git log -1 "--format=%(trailers:key=$key,valueonly)" $commit
    $values = @($r.Out | Where-Object { "$_".Trim() })
    if ($values.Count -eq 0) {
        # the words are in the message but git does not read them as a
        # trailer: say so, it is the usual slip (a paragraph after them, or
        # two trailers in two paragraphs)
        $body = @((Invoke-Git log -1 --format=%B $commit).Out) -join "`n"
        if ($body -match ('(?m)^\s*' + [regex]::Escape($key) + '\s*:')) {
            Write-Host "    note: '${key}:' is in the message of $($commit.Substring(0, 10)) but not as a trailer; trailers go together in the LAST paragraph."
        }
    }
    return $values
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("language-gate-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null

try {
    # the workflow on pull_request_target checks out the BASE and names the
    # pull request: its merge commit is fetched and judged from the object
    # store, with the detector of the base
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
    if (-not $Delegated -and $baseSha -and (Test-StrictGate $baseSha)) {
        $mine = ((Invoke-Git hash-object $MyInvocation.MyCommand.Definition).Out | Select-Object -First 1)
        $theirs = ((Invoke-Git rev-parse "${baseSha}:$selfRel").Out | Select-Object -First 1)
        if ($mine -ne $theirs) {
            Write-Output "language gate: $selfRel changed in this range; the copy of the base judges it."
            $copy = Join-Path $work 'language-gate-of-the-base.ps1'
            Save-GitBlob $theirs $copy
            $shell = (Get-Process -Id $PID).Path
            & $shell -NoProfile -ExecutionPolicy Bypass -File $copy -Base $baseSha -Root $root -Head $head -Delegated
            exit $LASTEXITCODE
        }
    }

    $failed = $false

    # -- a. a change to the gate itself is signed -----------------------------
    if ($baseSha) {
        $commits = @((Invoke-Git rev-list --reverse --topo-order "$baseSha..$head").Out | Where-Object { $_ })
        Write-Output ("language gate: " + $commits.Count + " commit(s) in the range")
        foreach ($c in $commits) {
            $parents = @(((Invoke-Git rev-list --parents -n 1 $c).Out | Select-Object -First 1) -split ' ' | Select-Object -Skip 1)
            if ($parents.Count -eq 0) { continue }
            $changed = @((Invoke-Git diff --name-only $parents[0] $c '--' @configPaths).Out | Where-Object { $_ })
            # a merge changes the gate only where it differs from every parent
            foreach ($p in ($parents | Select-Object -Skip 1)) {
                $other = @((Invoke-Git diff --name-only $p $c '--' @configPaths).Out | Where-Object { $_ })
                $changed = @($changed | Where-Object { $other -contains $_ })
            }
            if ($changed.Count -eq 0) { continue }
            $short = $c.Substring(0, 10)
            $signed = @(Get-Trailer $c 'Language-Gate-Config-Change')
            if ($signed.Count -gt 0) {
                Write-Output ("    $short changes the gate itself (" + ($changed -join ', ') + "), signed: " + $signed[0])
            } else {
                Write-Output ("  LANGUAGE GATE: $short changes the gate itself (" + ($changed -join ', ') + ")")
                Write-Output "    without the trailer 'Language-Gate-Config-Change: <reason>'."
                $failed = $true
            }
        }
    }

    # -- the zero: the tip, against the signed exceptions ---------------------
    # The detector of the base when it is a strict one (a change to the detector
    # takes effect once merged), else the one of the head; the lexicon and the
    # exceptions are the head's: a new exception is an entry the owner signs.
    $judge = $head
    if ($baseSha -and (Test-StrictGate $baseSha)) { $judge = $baseSha }
    if (-not (Test-GitObject "${judge}:$detectorRel")) { Write-Output "LANGUAGE GATE: $judge has no detector"; exit 2 }
    $detector = Join-Path $work 'detect-italian.js'
    Save-GitBlob ((Invoke-Git rev-parse "${judge}:$detectorRel").Out | Select-Object -First 1) $detector

    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $out = & node $detector --root $root --rev $head --config-rev $head 2>&1 } finally { $ErrorActionPreference = $old }
    $code = $LASTEXITCODE
    foreach ($l in @($out)) { Write-Output "$l" }
    if ($code -eq 2) { Write-Output "LANGUAGE GATE: the detector cannot run (exit 2)"; exit 2 }
    if ($code -ne 0) { $failed = $true }

    Write-Output ""
    if ($failed) {
        Write-Output "LANGUAGE GATE: FAILED. The repository is English: 0 outside the signed exceptions."
        Write-Output "An exception is an entry of Checks/language/exceptions.json (one of the six classes), with its reason,"
        Write-Output "and counts only when the owner signs it (signed_by, signed_on); a change to the gate itself needs"
        Write-Output "'Language-Gate-Config-Change: <reason>' in the message of its commit."
        exit 1
    }
    Write-Output "language gate: held."
    exit 0
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
