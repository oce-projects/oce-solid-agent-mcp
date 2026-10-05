# =============================================================================
#  prepare-release.ps1 - everything that is needed for a Release, except
#                        publishing it.
# =============================================================================
#
#  THIS FILE IS PURE ASCII, like the other .ps1 files of the project.
#
#  WHY A HAND RITUAL AND NOT A WORKFLOW
#
#  No GitHub runner can compile the bridge: `Oce.Solid.Agent.csproj`
#  compiles against the SOLIDWORKS interop, and on windows-latest the interop is
#  not there (measured: CS0246 IModelDoc2). The CI only compiles the contract suite,
#  and says so. A self-hosted runner with a SOLIDWORKS licence is an expense and
#  a maintenance burden; on 24/9/2026 the owner chose the hand ritual.
#
#  The risk of the hand ritual is one only, and it is the usual one: that a step
#  gets skipped the day one is in a hurry. So the steps that can be checked
#  are checked by this script, and it stops at the first one that does not add up.
#
#  WHAT IT DOES NOT DO: it does not create the tag, does not push anything, does not publish. It prints the three
#  commands. Publishing is an action towards the outside, and it is done by a person.
#
#  USAGE
#      .\Checks\prepare-release.ps1
#
#  EXITS 0 if the release is ready, 1 at the first check that does not pass.
# =============================================================================

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Stop($why) {
    Write-Output ""
    Write-Output "FERMO: $why"
    exit 1
}
function Ok($what) { Write-Output "  ok  $what" }

function RunGit($arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & git.exe -C $root @arguments 2>$null } finally { $ErrorActionPreference = $old }
}

Write-Output "=== la versione"
$version = (Get-Content (Join-Path $root "VERSION.txt") -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { Stop "VERSION.txt non e' una versione X.Y.Z: '$version'" }
Ok "VERSION.txt dice $version"
$tag = "v$version"

# AN ENTRY IN THE CHANGELOG, and not just any one: that of THIS version. The
# notes of the Release are taken from there, and a Release without notes is a file
# of which nobody knows what changes.
$cl = Get-Content (Join-Path $root "CHANGELOG.md") -Encoding UTF8
$start = -1
for ($i = 0; $i -lt $cl.Count; $i++) {
    if ($cl[$i] -match ('^## ' + [regex]::Escape($version) + '\b')) { $start = $i; break }
}
if ($start -lt 0) { Stop "CHANGELOG.md non ha una voce '## $version'" }
$fine = $cl.Count
for ($i = $start + 1; $i -lt $cl.Count; $i++) { if ($cl[$i] -match '^## \d') { $fine = $i; break } }
Ok ("CHANGELOG.md ha la voce, " + ($fine - $start) + " righe")

Write-Output ""
Write-Output "=== il repository"
# NOTHING UNCOMMITTED: the binary must be that of a commit, or
# nobody will ever be able to say what code it comes from.
$dirty = @(RunGit @('status', '--porcelain') | Where-Object { $_ })
if ($dirty.Count -gt 0) {
    $dirty | Select-Object -First 5 | ForEach-Object { Write-Output "     $_" }
    Stop "l'albero di lavoro ha modifiche non committate"
}
Ok "albero di lavoro pulito"

# AND PUSHED: a Release that points to a commit that the remote does not have is a tag
# that nobody else can resolve.
$null = RunGit @('fetch', '--quiet', 'origin')
$locale = (RunGit @('rev-parse', 'HEAD')) | Select-Object -First 1
$remote = (RunGit @('rev-parse', 'origin/main')) | Select-Object -First 1
if ($locale -ne $remote) { Stop "HEAD ($locale) non e' origin/main ($remote): prima si spinge" }
Ok "HEAD e' origin/main ($($locale.Substring(0,7)))"

$exists = @(RunGit @('tag', '-l', $tag) | Where-Object { $_ })
if ($exists.Count -gt 0) { Stop "il tag $tag esiste gia': una versione si rilascia una volta" }
Ok "il tag $tag non esiste ancora"

Write-Output ""
Write-Output "=== la compilazione"
& (Join-Path $root "build.ps1") | Out-Null
if ($LASTEXITCODE -ne 0) { Stop "build.ps1 e' uscito $LASTEXITCODE" }
$exe = Join-Path $root "dist\Installer.exe"
$sha = Join-Path $root "dist\Installer.exe.sha256"
if (-not (Test-Path $exe)) { Stop "manca dist\Installer.exe" }
if (-not (Test-Path $sha)) { Stop "manca dist\Installer.exe.sha256" }
Ok ("dist\Installer.exe, " + [int]((Get-Item $exe).Length / 1KB) + " kB")

# THE SHA256 IS RECOMPUTED, THE FILE NEXT TO IT IS NOT TRUSTED. It is the value that whoever
# downloads will compare: if the .sha256 file has been left over from a previous
# build, the verification that QUICKSTART teaches would say "tampered" to everybody.
$real = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
$declared = ((Get-Content $sha -Raw).Trim() -split '\s+')[0].ToLower()
if ($real -ne $declared) { Stop "lo sha256 accanto all'eseguibile non e' il suo ($declared contro $real)" }
Ok "sha256 ricalcolato e uguale: $($real.Substring(0,16))..."

# THE CONTRACT SUITE, GREEN. Inside there are the two checks that a Release
# must have: the version that the bridge announces is that of VERSION.txt, and
# the last bench run is later than the last change to the library and to the
# bench. A Release on an old bench is a binary that nobody has measured.
$tests = Join-Path $root "Tests\bin\Oce.Solid.Agent.ContractTests.exe"
if (-not (Test-Path $tests)) { Stop "manca Tests\bin\Oce.Solid.Agent.ContractTests.exe: si compila Tests\Oce.Solid.Agent.ContractTests.csproj" }
$old = $ErrorActionPreference; $ErrorActionPreference = "Continue"
$outcome = & $tests 2>&1 | Out-String
$code = $LASTEXITCODE
$ErrorActionPreference = $old
$line = ($outcome -split "`n" | Where-Object { $_ -match 'controlli, \d+ falliti' } | Select-Object -Last 1)
if ($code -ne 0) {
    ($outcome -split "`n" | Where-Object { $_ -match '^\s+NO ' }) | Select-Object -First 5 | ForEach-Object { Write-Output "     $_" }
    Stop ("la suite di contratto non e' verde: " + $line)
}
Ok ("suite di contratto: " + $line.Trim())

Write-Output ""
Write-Output "=== le note della Release"
$note = Join-Path $root ("dist\note-di-rilascio-" + $version + ".md")
$cl[$start..($fine - 1)] | Out-File $note -Encoding utf8
Ok ("dist\note-di-rilascio-" + $version + ".md, dalla voce del CHANGELOG")

Write-Output ""
Write-Output "PRONTA. Non e' stato pubblicato niente. I tre passi, da fare a mano:"
Write-Output ""
Write-Output "  git tag -a $tag -m ""$tag"""
Write-Output "  git push origin $tag"
Write-Output "  gh release create $tag dist\Installer.exe dist\Installer.exe.sha256 --title ""$tag"" --notes-file ""$note"""
Write-Output ""
Write-Output "Il repository e' privato: la Release lo e' con lui."
exit 0
