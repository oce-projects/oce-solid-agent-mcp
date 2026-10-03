# =============================================================================
#  ci-proof.ps1 - see the language gate fire ON THE CI (entry W3.1-bis.1)
# =============================================================================
#
#  A gate never seen firing is not a gate. The contract LanguageGateTests
#  proves the detector and the gate script on a throw-away repository; this
#  proves the CI step, on GitHub, with throw-away branches cut from the
#  current HEAD, each started with `gh workflow run prove.yml --ref <branch>
#  -f base=<HEAD>` so the step judges exactly the commits of the case:
#
#    italian-line      one Italian comment line, baseline untouched
#                      -> the "Language gate" step must FAIL
#    raise-unsigned    the same line, baseline.json rewritten in the same
#                      commit, no trailer                      -> FAIL
#    raise-signed      the same, with the trailer
#                      "Language-Baseline-Raise: <reason>"     -> PASS
#    signed-later      two commits: the Italian line (unsigned), then a raise
#                      signed in the SECOND commit             -> FAIL
#                      (a signature covers only its own commit)
#    lexicon-loosened  the words of the Italian line taken out of lexicon.json,
#                      the line added, the baseline rewritten, and only
#                      "Language-Gate-Config-Change:"          -> FAIL
#                      (the commit is judged with its parent's lexicon)
#    rename            a file renamed by hand, baseline rewritten, no
#                      trailer                                 -> PASS
#
#  The Italian line comes from `injection_samples` in lexicon.json. The
#  branches are pushed with --no-verify (the local hook would stop them, and
#  the point is to see the CI stop them), and deleted at the end unless
#  -KeepBranches. Nothing is pushed to the branch you are on.
#
#      .\Controlli\lingua\ci-proof.ps1
#      .\Controlli\lingua\ci-proof.ps1 -Remote origin -KeepBranches
#
#  Needs: git, node, gh (logged in), push rights on the remote. HEAD need not
#  be on the remote: it travels as the parent of every throw-away branch (so
#  its objects reach the remote even after the branches are deleted).
#  Exit 0 when every step ends as expected, 1 otherwise.
# =============================================================================

param([string] $Remote = 'origin', [switch] $KeepBranches)

$ErrorActionPreference = 'Continue'
function Must($what) { if ($LASTEXITCODE -ne 0) { Write-Output "ci-proof: $what failed (exit $LASTEXITCODE)"; exit 1 } }

$root = (& git rev-parse --show-toplevel); Must 'git rev-parse'
$head = (& git -C $root rev-parse HEAD); Must 'git rev-parse HEAD'
$url = (& git -C $root remote get-url $Remote); Must "git remote get-url $Remote"
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path ([IO.Path]::GetTempPath()) "language-gate-ci-proof-$stamp"

Write-Output "ci-proof: HEAD $head, remote $url"
& git clone --quiet --no-hardlinks $root $work; Must 'git clone'
Push-Location $work
$cases = @()
try {
    & git remote set-url origin $url; Must 'git remote set-url'
    $detector = Join-Path $work 'Controlli\lingua\detect-italian.js'
    $target = Join-Path $work 'Controlli\lingua\measure.js'
    # the helpers are Node: PowerShell 5 would re-encode and re-indent the JSON
    $helper = Join-Path $work 'ci-proof-helper.js'
    @'
const fs = require('fs');
const [, , what, file] = process.argv;
const lex = JSON.parse(fs.readFileSync('Controlli/lingua/lexicon.json', 'utf8'));
const line = '// ' + lex.injection_samples.prose + '\n';
if (what === 'line') fs.appendFileSync(file, line);
if (what === 'loosen') {
  const words = lex.injection_samples.prose.split(/\s+/);
  for (const k of ['function_words', 'content_words', 'weak_words']) lex[k] = (lex[k] || []).filter(w => !words.includes(w));
  fs.writeFileSync('Controlli/lingua/lexicon.json', JSON.stringify(lex, null, 1) + '\n');
}
'@ | Set-Content -Path $helper -Encoding ASCII

    $cases = @(
        @{ Name = 'italian-line';     Expect = 'failure' },
        @{ Name = 'raise-unsigned';   Expect = 'failure' },
        @{ Name = 'raise-signed';     Expect = 'success' },
        @{ Name = 'signed-later';     Expect = 'failure' },
        @{ Name = 'lexicon-loosened'; Expect = 'failure' },
        @{ Name = 'rename';           Expect = 'success' }
    )
    foreach ($c in $cases) {
        $c.Branch = "gate-proof/$stamp-$($c.Name)"
        & git checkout --quiet -B $c.Branch $head; Must "git checkout $($c.Branch)"
        $subject = "Language gate proof on the CI: $($c.Name) (throw-away branch)"
        switch ($c.Name) {
            'italian-line' {
                & node $helper line $target; Must 'line'
                & git commit --quiet -am $subject; Must 'git commit'
            }
            'raise-unsigned' {
                & node $helper line $target; Must 'line'
                & node $detector --write-baseline | Out-Null; Must 'write-baseline'
                & git commit --quiet -am $subject; Must 'git commit'
            }
            'raise-signed' {
                & node $helper line $target; Must 'line'
                & node $detector --write-baseline | Out-Null; Must 'write-baseline'
                & git commit --quiet -am $subject -m 'Language-Baseline-Raise: proof that a signed raise passes (throw-away branch)'; Must 'git commit'
            }
            'signed-later' {
                & node $helper line $target; Must 'line'
                & git commit --quiet -am "$subject (1 of 2, unsigned)"; Must 'git commit'
                & node $detector --write-baseline | Out-Null; Must 'write-baseline'
                & git commit --quiet -am "$subject (2 of 2)" -m 'Language-Baseline-Raise: signed on the wrong commit'; Must 'git commit'
            }
            'lexicon-loosened' {
                & node $helper loosen; Must 'loosen'
                & node $helper line $target; Must 'line'
                & node $detector --write-baseline | Out-Null; Must 'write-baseline'
                & git commit --quiet -am $subject -m 'Language-Gate-Config-Change: proof that a looser lexicon does not judge itself'; Must 'git commit'
            }
            'rename' {
                & git mv RELEASE.md RELEASE-NOTES.md; Must 'git mv'
                & node $detector --write-baseline | Out-Null; Must 'write-baseline'
                & git commit --quiet -am $subject; Must 'git commit'
            }
        }
        & git push --quiet --no-verify origin "HEAD:refs/heads/$($c.Branch)"; Must "git push $($c.Branch)"
        & gh workflow run prove.yml --ref $c.Branch -f "base=$head"; Must "gh workflow run $($c.Branch)"
        Write-Output "ci-proof: $($c.Branch) pushed and started"
    }

    foreach ($c in $cases) {
        $id = $null
        for ($i = 0; $i -lt 30 -and -not $id; $i++) {
            Start-Sleep -Seconds 5
            $id = (& gh run list --workflow prove.yml --branch $c.Branch --limit 1 --json databaseId --jq '.[0].databaseId')
        }
        if (-not $id) { Write-Output "ci-proof: no run found for $($c.Branch)"; $c.Got = 'no run'; continue }
        & gh run watch $id --interval 15 | Out-Null
        $jobs = (& gh run view $id --json jobs) | ConvertFrom-Json
        $step = $jobs.jobs | ForEach-Object { $_.steps } | Where-Object { $_.name -eq 'Language gate' } | Select-Object -First 1
        $c.Got = if ($step) { $step.conclusion } else { 'no step' }
        $c.Run = $id
        if ($c.Got -eq 'failure') {
            & gh run view $id --log-failed | Select-String 'LANGUAGE GATE' | Select-Object -First 8 | ForEach-Object { Write-Output ("    " + $_.Line) }
        }
    }
} finally {
    Pop-Location
}

$ok = $true
Write-Output ""
foreach ($c in $cases) {
    $mark = if ($c.Got -eq $c.Expect) { 'ok' } else { $ok = $false; 'XX' }
    Write-Output ("  {0}  {1,-17} expected {2,-8} got {3,-8} run {4}" -f $mark, $c.Name, $c.Expect, $c.Got, $c.Run)
}
if (-not $KeepBranches) {
    foreach ($c in $cases) { if ($c.Branch) { & git -C $root push --quiet --no-verify $Remote --delete $c.Branch 2>$null } }
    Write-Output "ci-proof: throw-away branches deleted"
}
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
if ($ok) { Write-Output "ci-proof: the gate fired where it must and held where it must."; exit 0 }
Write-Output "ci-proof: NOT AS EXPECTED"; exit 1
