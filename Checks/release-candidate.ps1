# =============================================================================
#  release-candidate.ps1 - builds the installer and puts it in a DRAFT release,
#                          so that GitHub can test it on a clean machine
#                          BEFORE anything is published.
# =============================================================================
#
#  THIS FILE IS PURE ASCII, like the other .ps1 files of the project.
#
#  WHERE IT SITS IN THE RELEASE RITE (RELEASE.md)
#
#  prepare-release.ps1 prepares the FINAL release: version, changelog, tag that
#  does not exist, HEAD equal to origin/main, contract suite, release notes,
#  and the three commands a person runs. This script is the step BEFORE it:
#  the installer is built on the one machine that can build it (the one with
#  SOLIDWORKS), uploaded as a DRAFT PRERELEASE, and the workflow
#  .github/workflows/installer-smoke.yml downloads it from there on a Windows
#  machine that has never seen this project and runs it the way
#  INSTALL-FOR-AI.md says. A draft is visible only to people with push access;
#  no tag is created and nothing is public.
#
#  WHAT IT DOES
#
#    1. checks the tag: it must be v<VERSION.txt>-<suffix> (for example
#       v0.130.0-rc1); the final tag vX.Y.Z is prepare-release.ps1's, never
#       this script's;
#    2. checks that the working tree is clean and that HEAD is on the remote
#       (a binary has to come from a commit others can resolve);
#    3. builds with build.ps1 and RECOMPUTES the SHA-256 of dist\Installer.exe
#       (the file next to it is not trusted: it may be left over);
#    4. looks at the releases that exist: none -> creates a draft; a draft with
#       this tag -> replaces its two assets; a PUBLISHED release with this tag
#       -> stops, it never touches one;
#    5. reads the release back and checks it IS a draft and carries both files
#       with the size they have here;
#    6. prints the command that runs the smoke test.
#
#  WHAT IT NEVER DOES: publish. Every gh command in it that creates or edits a
#  release carries --draft, and Checks/check-workflow.ps1 (run by the pre-push
#  hook) fails if a line of this file could turn a draft into a public release
#  or create a release that is not a draft. It does not create a tag and it
#  does not push.
#
#  -DryRun runs only the READ-ONLY checks (git, version, a listing of the
#  releases) and PRINTS every command that would change something, including
#  the build. A check that fails is reported as "WOULD STOP" and the rest is
#  still printed. Exit 0 if nothing would stop, 1 otherwise.
#
#  USAGE
#      .\Checks\release-candidate.ps1 -DryRun
#      .\Checks\release-candidate.ps1                       # tag v<VERSION>-rc1
#      .\Checks\release-candidate.ps1 -Tag v0.130.0-rc2
#      .\Checks\release-candidate.ps1 -SkipBuild            # use the dist\ that is there
#
#  EXITS 0 when the draft is ready (or, with -DryRun, when nothing would stop),
#  1 at the first check that does not pass.
# =============================================================================

[CmdletBinding()]
param(
    [string] $Tag,
    [string] $Repo,
    [switch] $DryRun,
    [switch] $SkipBuild,
    # the SOLIDWORKS root, passed to build.ps1 as it is; see its own header
    [string] $SwInstallPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$problems = 0
function Stop($why) {
    Write-Output ""
    if ($DryRun) {
        $script:problems++
        Write-Output "WOULD STOP: $why"
    } else {
        Write-Output "STOPPED: $why"
        exit 1
    }
}
function Ok($what) { Write-Output "  ok  $what" }
function Would($what) { Write-Output "  would run: $what" }

function RunGit($arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & git.exe -C $root @arguments 2>$null } finally { $ErrorActionPreference = $old }
}
function RunGh {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $text = & gh @args 2>&1 | Out-String; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $old }
    return @{ Code = $code; Text = $text }
}

if ($DryRun) { Write-Output "DRY RUN: nothing below is executed except the read-only checks." }

# -----------------------------------------------------------------------------
Write-Output "=== the version and the tag"
# -----------------------------------------------------------------------------
$version = (Get-Content (Join-Path $root 'VERSION.txt') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { Stop "VERSION.txt is not an X.Y.Z version: '$version'" }
if (-not $Tag) { $Tag = "v$version-rc1" }
if ($Tag -notmatch ('^v' + [regex]::Escape($version) + '-[0-9A-Za-z.]+$')) {
    Stop "the tag '$Tag' is not v$version-<suffix> (for example v$version-rc1). The final tag is prepare-release.ps1's."
} else {
    Ok "VERSION.txt says $version, the tag is $Tag"
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Stop "the gh command is not on this machine" }

if (-not $Repo) {
    $r = RunGh repo view --json nameWithOwner --jq .nameWithOwner
    if ($r.Code -eq 0 -and $r.Text.Trim() -match '^[\w.-]+/[\w.-]+$') { $Repo = $r.Text.Trim() }
    else {
        $url = (RunGit @('remote', 'get-url', 'origin')) | Select-Object -First 1
        if ($url -match 'github\.com[:/]([\w.-]+/[\w.-]+?)(\.git)?$') { $Repo = $Matches[1] }
    }
}
if (-not $Repo) { Stop "cannot tell which GitHub repository this is: pass -Repo owner/name" }
else { Ok "repository: $Repo" }

# -----------------------------------------------------------------------------
Write-Output ""
Write-Output "=== the repository"
# -----------------------------------------------------------------------------
$dirty = @(RunGit @('status', '--porcelain') | Where-Object { $_ })
if ($dirty.Count -gt 0) {
    $dirty | Select-Object -First 5 | ForEach-Object { Write-Output "     $_" }
    Stop "the working tree has uncommitted changes: a binary must come from a commit"
} else { Ok "working tree clean" }

$null = RunGit @('fetch', '--quiet', 'origin')
$head = (RunGit @('rev-parse', 'HEAD')) | Select-Object -First 1
$branch = (RunGit @('rev-parse', '--abbrev-ref', 'HEAD')) | Select-Object -First 1
$onRemote = @(RunGit @('branch', '-r', '--contains', $head) | Where-Object { $_ })
if ($onRemote.Count -eq 0) { Stop "HEAD ($head) is on no remote branch: push first, a release candidate has to point at a commit others can resolve" }
else { Ok ("HEAD " + $head.Substring(0, 7) + " on " + $branch + ", also on " + $onRemote[0].Trim()) }

# -----------------------------------------------------------------------------
Write-Output ""
Write-Output "=== the build"
# -----------------------------------------------------------------------------
$exe = Join-Path $root 'dist\Installer.exe'
$shaFile = Join-Path $root 'dist\Installer.exe.sha256'
if ($SkipBuild) {
    Write-Output "  -SkipBuild: the dist\ that is there is used as it is"
} elseif ($DryRun) {
    $cmd = '.\build.ps1' + $(if ($SwInstallPath) { " -SwInstallPath '$SwInstallPath'" } else { '' })
    Would $cmd
} else {
    $buildArgs = @()
    if ($SwInstallPath) { $buildArgs += @('-SwInstallPath', $SwInstallPath) }
    & (Join-Path $root 'build.ps1') @buildArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { Stop "build.ps1 exited $LASTEXITCODE" }
}

$haveFiles = (Test-Path $exe) -and (Test-Path $shaFile)
$hash = $null
if ($haveFiles) {
    # THE SHA256 IS RECOMPUTED, THE FILE NEXT TO IT IS NOT TRUSTED: a stale one
    # would make the smoke test (and every person downloading) say "tampered".
    $hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
    $declared = ((Get-Content $shaFile -Raw).Trim() -split '\s+')[0].ToLower()
    if ($hash -ne $declared) { Stop "the sha256 next to the executable is not its own ($declared against $hash)" }
    else { Ok ("dist\Installer.exe, " + [int]((Get-Item $exe).Length / 1KB) + " kB, sha256 " + $hash.Substring(0, 16) + "... (recomputed, equal to the file)") }
} elseif ($DryRun -and -not $SkipBuild) {
    Write-Output "  (dist\ is not there yet: the build above would produce it)"
} else {
    Stop "dist\Installer.exe or dist\Installer.exe.sha256 is missing"
}

# -----------------------------------------------------------------------------
Write-Output ""
Write-Output "=== the releases that exist"
# -----------------------------------------------------------------------------
$jq = '.[] | {id: .id, tag_name: .tag_name, draft: .draft, prerelease: .prerelease, ' +
      'url: .html_url, assets: [.assets[] | {name: .name, size: .size, state: .state}]}'
function Get-Releases {
    $l = RunGh api "repos/$Repo/releases?per_page=100" --paginate --jq $jq
    if ($l.Code -ne 0) { return $null }
    # the comma keeps an EMPTY list an empty list: a bare empty array comes out of a function as nothing
    return ,@($l.Text -split "`n" | Where-Object { $_.Trim().StartsWith('{') } | ForEach-Object { $_ | ConvertFrom-Json })
}
$releases = Get-Releases
$existing = $null
if ($null -eq $releases) {
    Write-Output "  the list of releases could not be read (gh not logged in, or no network)"
    if ($DryRun) { Write-Output "  (dry run: assuming there is none)" } else { Stop "the list of releases could not be read: without it a published release could be touched by mistake" }
} else {
    $same = @($releases | Where-Object { $_.tag_name -eq $Tag })
    if ($same.Count -gt 1) { Stop "$($same.Count) releases have the tag '$Tag': delete the extra ones by hand" }
    elseif ($same.Count -eq 1) {
        $existing = $same[0]
        if (-not $existing.draft) { Stop "the release '$Tag' is PUBLISHED ($($existing.url)): this script never touches a published release. Use another tag." }
        else { Ok "a draft with the tag $Tag exists (id $($existing.id)): its two assets will be replaced" }
    } else { Ok "no release has the tag ${Tag}: a draft will be created" }
}

# -----------------------------------------------------------------------------
Write-Output ""
Write-Output "=== the draft"
# -----------------------------------------------------------------------------
$notes = Join-Path $root ("dist\release-candidate-notes-" + $Tag + ".md")
$notesText = @(
    "Release candidate $Tag. **Not for use.**",
    "",
    "Built from commit $head (branch $branch) for the clean-machine smoke test of",
    ".github/workflows/installer-smoke.yml. If that run is green and the maintainer agrees,",
    "the final release is made with Checks/prepare-release.ps1; this draft is then deleted.",
    "",
    "Installer.exe is not code-signed. Windows SmartScreen will warn."
) -join "`n"
$title = "$Tag (release candidate, draft)"

# Every command below carries --draft, and none can turn a draft into a public release.
$assets = @($exe, $shaFile)
if ($DryRun) {
    Write-Output "  the notes (dist\release-candidate-notes-$Tag.md) would say:"
    $notesText -split "`n" | ForEach-Object { Write-Output "      $_" }
    if ($existing) {
        Would "gh release upload $Tag dist\Installer.exe dist\Installer.exe.sha256 --repo $Repo --clobber"
        Would "gh release edit $Tag --repo $Repo --draft --prerelease --target $head --title ""$title"" --notes-file <the notes>"
    } else {
        Would "gh release create $Tag dist\Installer.exe dist\Installer.exe.sha256 --repo $Repo --draft --prerelease --target $head --title ""$title"" --notes-file <the notes>"
    }
} elseif ($problems -eq 0) {
    [System.IO.File]::WriteAllText($notes, $notesText + "`n", (New-Object System.Text.UTF8Encoding($false)))
    if ($existing) {
        $u = RunGh release upload $Tag $exe $shaFile --repo $Repo --clobber
        if ($u.Code -ne 0) { Stop ("the upload of the assets failed: " + $u.Text.Trim()) }
        $e = RunGh release edit $Tag --repo $Repo --draft --prerelease --target $head --title $title --notes-file $notes
        if ($e.Code -ne 0) { Stop ("the edit of the draft failed: " + $e.Text.Trim()) }
        Ok "the draft $Tag was updated"
    } else {
        $c = RunGh release create $Tag $exe $shaFile --repo $Repo --draft --prerelease --target $head --title $title --notes-file $notes
        if ($c.Code -ne 0) { Stop ("the creation of the draft failed: " + $c.Text.Trim()) }
        Ok "the draft $Tag was created"
    }

    # READ IT BACK: the draft flag and the two files, as GitHub has them.
    $after = Get-Releases
    $mine = @($after | Where-Object { $_.tag_name -eq $Tag })
    if ($mine.Count -ne 1) { Stop "after the upload $($mine.Count) releases have the tag $Tag, expected one" }
    elseif (-not $mine[0].draft) { Stop "THE RELEASE $Tag IS NOT A DRAFT after the upload: look at $($mine[0].url) now" }
    else {
        Ok "read back: it is a draft and a prerelease: $($mine[0].prerelease)"
        foreach ($f in $assets) {
            $name = Split-Path -Leaf $f
            $a = @($mine[0].assets | Where-Object { $_.name -eq $name })
            if ($a.Count -ne 1) { Stop "the release does not carry $name" }
            elseif ($a[0].size -ne (Get-Item $f).Length) { Stop "$name is $($a[0].size) bytes on GitHub and $((Get-Item $f).Length) here" }
            elseif ($a[0].state -ne 'uploaded') { Stop "$name is in state '$($a[0].state)'" }
            else { Ok "$name is there, $($a[0].size) bytes" }
        }
    }
}

# -----------------------------------------------------------------------------
Write-Output ""
if ($DryRun) {
    if ($problems -gt 0) { Write-Output "DRY RUN: $problems thing(s) would stop it. Nothing was changed." }
    else { Write-Output "DRY RUN: nothing would stop it. Nothing was changed." }
} else {
    Write-Output "READY. A draft prerelease holds the installer; nothing is public and no tag exists yet."
}
Write-Output ""
Write-Output "To test it on a clean machine (GitHub, windows-latest and windows-2022):"
Write-Output ""
Write-Output "  gh workflow run installer-smoke.yml --repo $Repo --ref $branch -f tag=$Tag"
Write-Output "  gh run watch --repo $Repo"
Write-Output ""
Write-Output "The workflow file must exist on the branch given to --ref. Publishing stays a decision of the owner."
if ($problems -gt 0) { exit 1 }
exit 0
