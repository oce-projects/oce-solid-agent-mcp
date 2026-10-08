# =============================================================================
#  Installer smoke test, step 1: get the release candidate and check its hash
# =============================================================================
#
#  THIS FILE IS PURE ASCII, like the other .ps1 files of the project.
#
#  Called by .github/workflows/installer-smoke.yml. It downloads Installer.exe
#  and Installer.exe.sha256 from the release whose tag the person gave (the
#  release is a DRAFT: that is what Checks/release-candidate.ps1 creates), then
#  checks the hash the way INSTALL-FOR-AI.md step 1 teaches. The hash check is
#  the same three lines on purpose: the smoke test is also a test of that page.
#
#  WHY THE RELEASE IS LISTED BY THE API FIRST
#
#  A draft release has no tag yet when it is created from the command line
#  (the tag appears when somebody publishes it), and the endpoint that asks for
#  a release BY TAG does not return drafts. The listing endpoint returns them
#  to a token that has push access, and that is what the workflow gives the
#  step (permissions: contents: write). So: list, pick the one whose tag_name
#  is the one asked for, say whether it is a draft, and then download. `gh
#  release download <tag>` is tried first because it is the documented way and
#  knows about drafts; if it fails the assets are fetched by id, which is what
#  it does underneath. Which of the two worked is printed.
#
#  STATUS OF THAT CLAIM: not measured yet. There is no draft release in this
#  repository to measure on; the first run of the workflow is the measurement,
#  and the lines this script prints are written so that its failure says which
#  of the steps did not work.
#
#  A CHECK THAT CANNOT FAIL IS NOT A CHECK. After the hash matches, the script
#  flips one byte in a COPY of the file and requires the comparison to fail.
#
#  USAGE (by hand, with gh authenticated)
#      .\.github\steps\smoke-fetch-release.ps1 -Tag v0.1.0-rc1 -Out C:\temp\dl -Repo owner/name
#
#  EXITS 0 when both files are there and the hash matches, 1 otherwise.
# =============================================================================

[CmdletBinding()]
param(
    [string] $Tag = $env:OCE_SMOKE_TAG,
    [string] $Out = $(if ($env:RUNNER_TEMP) { Join-Path $env:RUNNER_TEMP 'oce-smoke\download' } else { '' }),
    [string] $Repo = $env:GITHUB_REPOSITORY
)

$ErrorActionPreference = 'Stop'

function Stop($why) {
    Write-Output ""
    Write-Output "STOPPED: $why"
    exit 1
}
function Ok($what) { Write-Output "  ok  $what" }

# The tag comes from a person who typed it into a form: it is checked before it
# goes anywhere near a command line.
if (-not $Tag) { Stop "no tag: pass -Tag or set OCE_SMOKE_TAG" }
if ($Tag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$') { Stop "the tag '$Tag' is not of the form vX.Y.Z or vX.Y.Z-rc1" }
if (-not $Out) { Stop "no output folder: pass -Out" }
if (-not $Repo) { Stop "no repository: pass -Repo owner/name or run it where GITHUB_REPOSITORY is set" }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Stop "the gh command is not on this machine" }
if (-not $env:GH_TOKEN -and -not $env:GITHUB_TOKEN) {
    # by hand gh may be logged in; on the runner the workflow sets GH_TOKEN
    Write-Output "  (no GH_TOKEN in the environment: relying on the login of gh)"
}

New-Item -ItemType Directory -Force $Out | Out-Null

function RunGh {
    # a native command that fails is an answer here, not an exception
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $text = & gh @args 2>&1 | Out-String; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $old }
    return @{ Code = $code; Text = $text }
}

Write-Output "=== the release $Tag in $Repo"

$jq = '.[] | {id: .id, tag_name: .tag_name, draft: .draft, prerelease: .prerelease, ' +
      'target: .target_commitish, assets: [.assets[] | {id: .id, name: .name, size: .size, state: .state}]}'
$listed = RunGh api "repos/$Repo/releases?per_page=100" --paginate --jq $jq
if ($listed.Code -ne 0) { Stop ("the list of releases could not be read: " + $listed.Text.Trim()) }
$releases = @($listed.Text -split "`n" | Where-Object { $_.Trim().StartsWith('{') } | ForEach-Object { $_ | ConvertFrom-Json })
Ok ("the token sees " + $releases.Count + " release(s), drafts included only if it has push access")

$mine = @($releases | Where-Object { $_.tag_name -eq $Tag })
if ($mine.Count -eq 0) {
    $seen = ($releases | Select-Object -First 10 | ForEach-Object { $_.tag_name + $(if ($_.draft) { ' (draft)' } else { '' }) }) -join ', '
    Stop ("no release has the tag '$Tag'. Visible: " + $(if ($seen) { $seen } else { 'none' }) +
          ". If the draft exists, the token cannot see drafts: the workflow must give it push access (the contents permission, write).")
}
if ($mine.Count -gt 1) { Stop ("$($mine.Count) releases have the tag '$Tag': delete the extra ones, a smoke test of 'one of them' proves nothing") }
$release = $mine[0]
Ok ("release id " + $release.id + ", draft: " + $release.draft + ", prerelease: " + $release.prerelease + ", target: " + $release.target)
if (-not $release.draft) { Write-Output "  note: this release is already PUBLISHED; the smoke test is still valid, it just is not a pre-publication one" }

$wanted = @('Installer.exe', 'Installer.exe.sha256')
foreach ($name in $wanted) {
    $found = @($release.assets | Where-Object { $_.name -eq $name })
    if ($found.Count -ne 1) { Stop "the release must carry exactly one asset named $name (found $($found.Count))" }
    if ($found[0].state -ne 'uploaded') { Stop "the asset $name is in state '$($found[0].state)', not 'uploaded'" }
    if ($found[0].size -le 0) { Stop "the asset $name is empty" }
}
Ok ("both assets are there: " + (($release.assets | ForEach-Object { $_.name + ' ' + $_.size + ' bytes' }) -join ', '))

Write-Output ""
Write-Output "=== the download"
foreach ($name in $wanted) { Remove-Item (Join-Path $Out $name) -ErrorAction SilentlyContinue }

$way = 'gh release download'
$d = RunGh release download $Tag --repo $Repo --pattern 'Installer.exe' --pattern 'Installer.exe.sha256' --dir $Out --clobber
if ($d.Code -ne 0 -or -not (Test-Path (Join-Path $Out 'Installer.exe')) -or -not (Test-Path (Join-Path $Out 'Installer.exe.sha256'))) {
    Write-Output ("  gh release download did not work (exit " + $d.Code + "): " + $d.Text.Trim())
    Write-Output "  falling back to the assets by id"
    $way = 'the assets by id'
    foreach ($a in $release.assets) {
        if ($wanted -notcontains $a.name) { continue }
        # cmd writes the bytes as they come; a PowerShell redirection would re-encode them
        $target = Join-Path $Out $a.name
        $url = "repos/$Repo/releases/assets/" + $a.id
        $oldPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            & cmd.exe /c ('gh api -H "Accept: application/octet-stream" ' + $url + ' > "' + $target + '"')
            $byId = $LASTEXITCODE
        } finally { $ErrorActionPreference = $oldPreference }
        if ($byId -ne 0) { Stop "the asset $($a.name) could not be downloaded by id either" }
    }
}
Ok "downloaded with $way"

$exe = Join-Path $Out 'Installer.exe'
$sha = Join-Path $Out 'Installer.exe.sha256'
foreach ($f in @($exe, $sha)) {
    if (-not (Test-Path $f)) { Stop "$f is not there after the download" }
    if ((Get-Item $f).Length -le 0) { Stop "$f is empty" }
}
$sizeOfExe = (Get-Item $exe).Length
$declaredSize = ($release.assets | Where-Object { $_.name -eq 'Installer.exe' }).size
if ($sizeOfExe -ne $declaredSize) { Stop "Installer.exe is $sizeOfExe bytes, the release says $declaredSize" }
$head = [System.IO.File]::ReadAllBytes($exe)[0..1]
if ($head[0] -ne 0x4D -or $head[1] -ne 0x5A) { Stop "Installer.exe does not start with MZ: it is not a Windows program" }
Ok "Installer.exe, $sizeOfExe bytes, a Windows program"

Write-Output ""
Write-Output "=== the hash, as INSTALL-FOR-AI.md step 1 teaches"
$line = (Get-Content $sha -Raw).Trim()
if ($line -notmatch '^[0-9a-fA-F]{64}  Installer\.exe$') { Stop "Installer.exe.sha256 is not '<64 hex characters>, two spaces, Installer.exe': '$line'" }
Ok "the .sha256 file has the documented shape"

Push-Location $Out
try {
    $expected = (Get-Content .\Installer.exe.sha256 -Raw).Trim().Split(' ')[0]
    $actual   = (Get-FileHash .\Installer.exe -Algorithm SHA256).Hash
    $matches_ = ($actual -ieq $expected)
} finally { Pop-Location }
if (-not $matches_) { Stop "the hash does not match: declared $expected, computed $actual" }
Ok "sha256 matches: $($actual.Substring(0,16))..."

# The same comparison on a copy with one byte changed must say False.
$copy = Join-Path $Out 'Installer.tampered.exe'
$bytes = [System.IO.File]::ReadAllBytes($exe)
$bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 0xFF
[System.IO.File]::WriteAllBytes($copy, $bytes)
$tampered = (Get-FileHash $copy -Algorithm SHA256).Hash
Remove-Item $copy
if ($tampered -ieq $expected) { Stop "the hash comparison cannot fail: a changed copy has the same hash" }
Ok "the same comparison on a copy with one byte changed says False"

# what was tested, for the artifact and the summary
$meta = [ordered]@{
    repository = $Repo; tag = $Tag; release_id = $release.id; draft = [bool]$release.draft
    prerelease = [bool]$release.prerelease; target = $release.target
    downloaded_with = $way; installer_bytes = $sizeOfExe; sha256 = $actual.ToLower()
}
$meta | ConvertTo-Json | Set-Content -Path (Join-Path $Out 'release.json') -Encoding UTF8

if ($env:GITHUB_STEP_SUMMARY) {
    @(
        "### Release candidate $Tag",
        "",
        "- repository: $Repo, release id $($release.id), draft: **$($release.draft)**",
        "- Installer.exe: $sizeOfExe bytes, sha256 ``$($actual.ToLower())``",
        "- downloaded with: $way"
    ) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

Write-Output ""
Write-Output "READY: $exe"
exit 0
