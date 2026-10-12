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
    Write-Output "STOPPED: $why"
    exit 1
}
function Ok($what) { Write-Output "  ok  $what" }

function RunGit($arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & git.exe -C $root @arguments 2>$null } finally { $ErrorActionPreference = $old }
}

Write-Output "=== the version"
$version = (Get-Content (Join-Path $root "VERSION.txt") -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { Stop "VERSION.txt is not an X.Y.Z version: '$version'" }
Ok "VERSION.txt says $version"
$tag = "v$version"

# AN ENTRY IN THE CHANGELOG, and not just any one: that of THIS version. The
# notes of the Release are taken from there, and a Release without notes is a file
# of which nobody knows what changes.
$cl = Get-Content (Join-Path $root "CHANGELOG.md") -Encoding UTF8
$start = -1
for ($i = 0; $i -lt $cl.Count; $i++) {
    if ($cl[$i] -match ('^## ' + [regex]::Escape($version) + '\b')) { $start = $i; break }
}
if ($start -lt 0) { Stop "CHANGELOG.md has no entry '## $version'" }
# THE ENTRY ENDS AT THE NEXT `## ` HEADING OF ANY KIND, not only at the next version number.
# This stopped at `^## \d`, and the CHANGELOG also holds `## Part of 0.130.1 - ...`
# entries (the work of each branch, kept for the maintainers): the notes of 0.130.1 took in
# seventeen of them, 223 thousand characters, and GitHub refused them (the limit of a
# Release body is 125000). The notes of a version are its own entry; the `###`
# headings inside it are part of it.
$entryEnd = $cl.Count
for ($i = $start + 1; $i -lt $cl.Count; $i++) { if ($cl[$i] -match '^## ') { $entryEnd = $i; break } }
Ok ("CHANGELOG.md has the entry, " + ($entryEnd - $start) + " lines")

# AND IF THE ENTRY IS TOO BIG, SAY SO NOW, not at the `gh release create` that fails with a
# message about the body. 125000 characters is the limit GitHub puts on a Release body.
$notesLimit = 125000
$notesLength = (($cl[$start..($entryEnd - 1)]) -join "`n").Length
if ($notesLength -gt $notesLimit) {
    Stop ("the CHANGELOG entry of $version is $notesLength characters and a GitHub Release body takes at most ${notesLimit}: shorten the entry, and move the detail to a '## Part of $version' entry, which the notes do not include")
}
Ok "the notes are $notesLength characters, under the $notesLimit that GitHub takes"

Write-Output ""
Write-Output "=== the repository"
# NOTHING UNCOMMITTED: the binary must be that of a commit, or
# nobody will ever be able to say what code it comes from.
$dirty = @(RunGit @('status', '--porcelain') | Where-Object { $_ })
if ($dirty.Count -gt 0) {
    $dirty | Select-Object -First 5 | ForEach-Object { Write-Output "     $_" }
    Stop "the working tree has uncommitted changes"
}
Ok "working tree clean"

# AND PUSHED: a Release that points to a commit that the remote does not have is a tag
# that nobody else can resolve.
$null = RunGit @('fetch', '--quiet', 'origin')
$locale = (RunGit @('rev-parse', 'HEAD')) | Select-Object -First 1
$remote = (RunGit @('rev-parse', 'origin/main')) | Select-Object -First 1
if ($locale -ne $remote) { Stop "HEAD ($locale) is not origin/main ($remote): push first" }
Ok "HEAD is origin/main ($($locale.Substring(0,7)))"

$exists = @(RunGit @('tag', '-l', $tag) | Where-Object { $_ })
if ($exists.Count -gt 0) { Stop "the tag $tag already exists: a version is released once" }
Ok "the tag $tag does not exist yet"

Write-Output ""
Write-Output "=== the build"
& (Join-Path $root "build.ps1") | Out-Null
if ($LASTEXITCODE -ne 0) { Stop "build.ps1 exited $LASTEXITCODE" }
$exe = Join-Path $root "dist\Installer.exe"
$sha = Join-Path $root "dist\Installer.exe.sha256"
if (-not (Test-Path $exe)) { Stop "dist\Installer.exe is missing" }
if (-not (Test-Path $sha)) { Stop "dist\Installer.exe.sha256 is missing" }
Ok ("dist\Installer.exe, " + [int]((Get-Item $exe).Length / 1KB) + " kB")

# THE SHA256 IS RECOMPUTED, THE FILE NEXT TO IT IS NOT TRUSTED. It is the value that whoever
# downloads will compare: if the .sha256 file has been left over from a previous
# build, the verification that QUICKSTART teaches would say "tampered" to everybody.
$real = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
$declared = ((Get-Content $sha -Raw).Trim() -split '\s+')[0].ToLower()
if ($real -ne $declared) { Stop "the sha256 next to the executable is not its own ($declared against $real)" }
Ok "sha256 recomputed and equal: $($real.Substring(0,16))..."

# THE CONTRACT SUITE, GREEN. Inside there are the two checks that a Release
# must have: the version that the bridge announces is that of VERSION.txt, and
# the last bench run is later than the last change to the library and to the
# bench. A Release on an old bench is a binary that nobody has measured.
$tests = Join-Path $root "Tests\bin\Oce.Solid.Agent.ContractTests.exe"
if (-not (Test-Path $tests)) { Stop "Tests\bin\Oce.Solid.Agent.ContractTests.exe is missing: build Tests\Oce.Solid.Agent.ContractTests.csproj" }
$old = $ErrorActionPreference; $ErrorActionPreference = "Continue"
$outcome = & $tests 2>&1 | Out-String
$code = $LASTEXITCODE
$ErrorActionPreference = $old
$line = ($outcome -split "`n" | Where-Object { $_ -match 'checks, \d+ failed' } | Select-Object -Last 1)
if (-not $line) { Stop 'the contract suite printed no summary line ("N checks, M failed"): its output changed, and this script reads it' }
if ($code -ne 0) {
    ($outcome -split "`n" | Where-Object { $_ -match '^\s+NO ' }) | Select-Object -First 5 | ForEach-Object { Write-Output "     $_" }
    Stop ("the contract suite is not green: " + $line)
}
Ok ("contract suite: " + $line.Trim())

Write-Output ""
Write-Output "=== the Release notes"
$note = Join-Path $root ("dist\release-notes-" + $version + ".md")
$cl[$start..($entryEnd - 1)] | Out-File $note -Encoding utf8
Ok ("dist\release-notes-" + $version + ".md, from the CHANGELOG entry")

Write-Output ""
Write-Output "READY. Nothing was published. The three steps, to be done by hand:"
Write-Output ""
Write-Output "  git tag -a $tag -m ""$tag"""
Write-Output "  git push origin $tag"
Write-Output "  gh release create $tag dist\Installer.exe dist\Installer.exe.sha256 --title ""$tag"" --notes-file ""$note"""
Write-Output ""
Write-Output "The repository is public: the Release is public the moment it is created."
exit 0
