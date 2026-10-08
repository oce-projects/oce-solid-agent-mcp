# =============================================================================
#  Installer smoke test, step 2: the installer on a machine that is not ours
# =============================================================================
#
#  THIS FILE IS PURE ASCII, like the other .ps1 files of the project.
#
#  Called by .github/workflows/installer-smoke.yml, once per scenario. It runs
#  the downloaded Installer.exe the way INSTALL-FOR-AI.md says (Start-Process
#  -Wait -PassThru -NoNewWindow, --silent, --dir, --log) and asserts the exit
#  codes and the effects that page promises. Every assertion prints "ok" or
#  "XX"; the script exits 1 if any is "XX".
#
#  WHAT A MACHINE WITHOUT SOLIDWORKS CAN AND CANNOT SHOW (read from the
#  installer's source, Installer/Program.cs and Installer/Steps.cs)
#
#  The prerequisite "SOLIDWORKS installed" is BLOCKING: it asks only whether the
#  file api\redist\SolidWorks.Interop.sldworks.dll exists. Without it a silent
#  install prints "[NO] SOLIDWORKS installed", "I stop here: there are things to
#  fix." and EXITS 1 WITH NOTHING INSTALLED. So on a runner the honest results
#  are these, and the scenarios assert them exactly:
#
#    absent          no SOLIDWORKS, nothing faked. install: exit 1, and the only
#                    [NO] line is the SOLIDWORKS one; nothing written, no client
#                    config touched. verify: exit 1, five checks failing.
#                    uninstall of nothing: exit 0.
#
#    install-temp    the installer MECHANICS, with a stand-in. A folder that has
#    install-default an EMPTY file named SolidWorks.Interop.sldworks.dll is all the
#                    prerequisite looks at, so the install goes through. The file
#                    is a placeholder written by this script: it is NOT a
#                    Dassault file and it is not loadable. OCE_NO_ATTACH=1 is set,
#                    so the bridge never touches COM (self-test exit 5, "not
#                    tried", which the installer counts as installed). What this
#                    proves: the six files land, the client entries are written
#                    and keep what was there, --verify reads them back and the
#                    bridge answers an MCP handshake, a reinstall keeps the
#                    variables of the entry, the uninstall removes the files, the
#                    entries and the archives the bridge writes. What it does NOT
#                    prove: that the bridge attaches to SOLIDWORKS. Nothing on a
#                    machine without SOLIDWORKS can.
#
#  SAFETY. The scenarios that look for the clients by themselves (-Clients
#  discover) and the one that installs in the default folder write to the
#  profile of whoever runs them. They refuse to run unless GITHUB_ACTIONS is
#  'true' or OCE_SMOKE_DISPOSABLE is 'yes'. With -Clients config-file (the
#  default) one temporary file stands for the client and nothing else is touched.
#
#  USAGE ON A WORKSTATION, to try the script itself (SOLIDWORKS may be installed
#  there: -ForceAbsent points --redist at a folder that does not exist, the only
#  way to make the machine look clean without uninstalling anything)
#      .\.github\steps\installer-smoke.ps1 -Installer C:\x\Installer.exe -Scenario absent -ForceAbsent
#      .\.github\steps\installer-smoke.ps1 -Installer C:\x\Installer.exe -Scenario install-temp
#
#  EXITS 0 when every assertion held, 1 when one did not, 2 when it refused to run.
# =============================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Installer,
    [Parameter(Mandatory = $true)] [ValidateSet('absent', 'install-temp', 'install-default')] [string] $Scenario,
    [ValidateSet('config-file', 'discover')] [string] $Clients = 'config-file',
    [string] $Work = $(if ($env:RUNNER_TEMP) { Join-Path $env:RUNNER_TEMP 'oce-smoke' } else { Join-Path ([System.IO.Path]::GetTempPath()) 'oce-smoke' }),
    [switch] $ForceAbsent
)

$ErrorActionPreference = 'Stop'

$onRunner = ($env:GITHUB_ACTIONS -eq 'true') -or ($env:OCE_SMOKE_DISPOSABLE -eq 'yes')
if (($Clients -eq 'discover' -or $Scenario -eq 'install-default') -and -not $onRunner) {
    Write-Output "REFUSED: '-Clients discover' and 'install-default' write to the profile of the user who runs them."
    Write-Output "         Run them on a throw-away machine (GITHUB_ACTIONS=true, or OCE_SMOKE_DISPOSABLE=yes)."
    exit 2
}
if (-not (Test-Path $Installer)) { Write-Output "REFUSED: $Installer is not there"; exit 2 }
$Installer = (Resolve-Path $Installer).Path

$tag = "$Scenario-$Clients"
$Logs = Join-Path $Work "logs\$tag"
if (Test-Path $Logs) { Remove-Item $Logs -Recurse -Force }
New-Item -ItemType Directory -Force $Logs | Out-Null
$scratch = Join-Path $Work "scratch\$tag"
if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force }
New-Item -ItemType Directory -Force $scratch | Out-Null

$trouble = 0
$checked = 0
function Assert($condition, $what) {
    $script:checked++
    if ($condition) { Write-Output "  ok  $what" }
    else { $script:trouble++; Write-Output "  XX  $what" }
}
function Section($t) { Write-Output ""; Write-Output "=== $t" }

# -----------------------------------------------------------------------------
#  Running the installer
# -----------------------------------------------------------------------------

function Invoke-Installer([string[]] $Arguments, [string] $LogName) {
    $log = Join-Path $Logs $LogName
    $all = @($Arguments) + @('--log', $log)
    $quoted = @($all | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } })
    Write-Output ("  > Installer.exe " + ($quoted -join ' '))
    $p = Start-Process -FilePath $Installer -ArgumentList $quoted -Wait -PassThru -NoNewWindow
    $text = ''
    if (Test-Path $log) { $text = [System.IO.File]::ReadAllText($log, [System.Text.Encoding]::UTF8) }
    return [pscustomobject]@{ Exit = $p.ExitCode; Log = $text; LogFile = $log }
}

# The JSON of --verify --json is the one line of the log that starts with '{'.
function Get-VerifyJson($run) {
    $line = @($run.Log -split "`r?`n" | Where-Object { $_.StartsWith('{') }) | Select-Object -First 1
    if (-not $line) { return $null }
    try { return ($line | ConvertFrom-Json) } catch { return $null }
}

function Get-Statuses($json) {
    $m = [ordered]@{}
    foreach ($c in @($json.checks)) { $m[$c.name] = $c.status }
    return $m
}
function Describe-Statuses($m) { return (($m.Keys | ForEach-Object { "$_=$($m[$_])" }) -join ', ') }

function Count-Lines($text, $pattern) { return @($text -split "`r?`n" | Where-Object { $_ -match $pattern }).Count }

# -----------------------------------------------------------------------------
#  The clients
# -----------------------------------------------------------------------------

$seedFile = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Checks\config-with-other-servers.json'
if (-not (Test-Path $seedFile)) { Write-Output "REFUSED: $seedFile is not there"; exit 2 }
$seedText = [System.IO.File]::ReadAllText($seedFile, [System.Text.Encoding]::UTF8)

# A JSON value as one string, object keys in order: two files that say the same
# thing in a different layout have the same canonical string.
function Get-Canon($o) {
    if ($null -eq $o) { return 'null' }
    if ($o -is [System.Management.Automation.PSCustomObject]) {
        $parts = @($o.PSObject.Properties | Sort-Object Name | ForEach-Object { '"' + $_.Name + '":' + (Get-Canon $_.Value) })
        return '{' + ($parts -join ',') + '}'
    }
    if ($o -is [System.Collections.IEnumerable] -and $o -isnot [string]) {
        return '[' + ((@($o) | ForEach-Object { Get-Canon $_ }) -join ',') + ']'
    }
    return (ConvertTo-Json $o -Compress)
}
function Read-Config([string] $file) {
    return ([System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8) | ConvertFrom-Json)
}
# The file as it is, minus the entry this product writes.
function Get-CanonWithoutEntry([string] $file) {
    $j = Read-Config $file
    if ($j.mcpServers -and $j.mcpServers.PSObject.Properties['solidworks']) { $j.mcpServers.PSObject.Properties.Remove('solidworks') }
    return (Get-Canon $j)
}
$seedCanon = Get-Canon ($seedText | ConvertFrom-Json)

# Name (as the installer prints it) and file of each client in play.
$clientList = @()
$savedClaudeConfigDir = $env:CLAUDE_CONFIG_DIR
$configArgs = @()
if ($Clients -eq 'config-file') {
    $clientList = @([pscustomobject]@{ Name = 'chosen file'; File = (Join-Path $scratch 'client-config.json') })
    $configArgs = @('--config', $clientList[0].File)
} else {
    $appdata = [Environment]::GetFolderPath('ApplicationData')
    $claudeHome = Join-Path $scratch 'claude-home'
    New-Item -ItemType Directory -Force $claudeHome | Out-Null
    # Claude Code looks for .claude.json in CLAUDE_CONFIG_DIR when it is set
    $env:CLAUDE_CONFIG_DIR = $claudeHome
    $clientList = @(
        [pscustomobject]@{ Name = 'Claude Desktop'; File = (Join-Path $appdata 'Claude\claude_desktop_config.json') },
        [pscustomobject]@{ Name = 'Claude Code'; File = (Join-Path $claudeHome '.claude.json') }
    )
}

function Reset-Clients {
    foreach ($c in $clientList) {
        $dir = Split-Path -Parent $c.File
        New-Item -ItemType Directory -Force $dir | Out-Null
        Get-ChildItem $dir -Filter ((Split-Path -Leaf $c.File) + '.before-oce-solid-agent-*.bak') -ErrorAction SilentlyContinue | Remove-Item -Force
        [System.IO.File]::WriteAllText($c.File, $seedText, (New-Object System.Text.UTF8Encoding($false)))
    }
}
function Get-Backups($c) {
    $dir = Split-Path -Parent $c.File
    return @(Get-ChildItem $dir -Filter ((Split-Path -Leaf $c.File) + '.before-oce-solid-agent-*.bak') -ErrorAction SilentlyContinue)
}
function Get-Hash($file) { return (Get-FileHash $file -Algorithm SHA256).Hash }
function Save-ClientCopies([string] $stage) {
    $i = 0
    foreach ($c in $clientList) {
        $i++
        if (Test-Path $c.File) { Copy-Item $c.File (Join-Path $Logs ("client$i-$stage.json")) -Force }
    }
}

# -----------------------------------------------------------------------------
#  The stand-in for SOLIDWORKS, and the environment of the children
# -----------------------------------------------------------------------------

$redist = Join-Path $scratch 'standin\api\redist'
$redistArgs = @()
if ($Scenario -ne 'absent') {
    New-Item -ItemType Directory -Force $redist | Out-Null
    # EMPTY, and named like the Dassault interop only because that is the one
    # file the prerequisite looks for. Not loadable, not Dassault's.
    New-Item -ItemType File -Force (Join-Path $redist 'SolidWorks.Interop.sldworks.dll') | Out-Null
    $redistArgs = @('--redist', $redist)
} elseif ($ForceAbsent) {
    $redistArgs = @('--redist', (Join-Path $scratch 'no-such-folder'))
}

$savedNoAttach = $env:OCE_NO_ATTACH
$savedSwInstall = $env:SW_INSTALL_PATH
$savedSwRedist = $env:SW_REDIST

function Finish {
    $env:CLAUDE_CONFIG_DIR = $savedClaudeConfigDir
    $env:OCE_NO_ATTACH = $savedNoAttach
    $env:SW_INSTALL_PATH = $savedSwInstall
    $env:SW_REDIST = $savedSwRedist
    Write-Output ""
    $verdict = if ($trouble -eq 0) { "all $checked assertions held" } else { "$trouble of $checked assertions FAILED" }
    Write-Output "[$tag] $verdict"
    if ($env:GITHUB_STEP_SUMMARY) {
        "- ``$tag``: $verdict" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }
    if ($trouble -eq 0) { exit 0 } else { exit 1 }
}

# =============================================================================
#  Scenario: absent
# =============================================================================

function Scenario-Absent {
    Section "this machine has no SOLIDWORKS (nothing is faked)"
    if (-not $ForceAbsent) {
        $places = @()
        if ($env:SW_INSTALL_PATH) { $places += (Join-Path $env:SW_INSTALL_PATH 'api\redist') }
        if ($env:SW_REDIST) { $places += $env:SW_REDIST }
        foreach ($pf in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
            if ($pf) { $places += (Join-Path $pf 'SOLIDWORKS Corp\SOLIDWORKS\api\redist') }
        }
        $there = @($places | Where-Object { Test-Path (Join-Path $_ 'SolidWorks.Interop.sldworks.dll') })
        Assert ($there.Count -eq 0) "no SolidWorks.Interop.sldworks.dll in any place the installer looks"
        if ($there.Count -gt 0) {
            Write-Output "  this machine HAS SOLIDWORKS ($($there[0])): the scenario would not be a clean-machine proof. Use -ForceAbsent to try the script here."
            return
        }
    } else {
        Write-Output "  -ForceAbsent: --redist points at a folder that does not exist (a stand-in for a clean machine)"
    }
    Reset-Clients
    $before = @{}
    foreach ($c in $clientList) { $before[$c.File] = Get-Hash $c.File }
    $folder = Join-Path $scratch 'install'

    Section "Installer.exe starts at all (--help)"
    $helpOut = Join-Path $Logs 'help.txt'
    $p = Start-Process -FilePath $Installer -ArgumentList '--help' -Wait -PassThru -NoNewWindow -RedirectStandardOutput $helpOut
    $help = [System.IO.File]::ReadAllText($helpOut)
    Assert ($p.ExitCode -eq 0) "--help exits 0 (exit $($p.ExitCode))"
    Assert ($help -match '--silent' -and $help -match 'Exit codes of --silent') "--help prints the options and the exit codes"

    Section "silent install: the right exit on a machine without SOLIDWORKS is 1, nothing installed"
    $run = Invoke-Installer (@('--silent', '--dir', $folder) + $redistArgs + $configArgs) 'install.log'
    Assert ($run.Exit -eq 1) "exit code is 1 (exit $($run.Exit))"
    Assert ($run.Log -match '(?m)^\[NO\] SOLIDWORKS installed') "the prerequisite that stops it is 'SOLIDWORKS installed'"
    Assert ((Count-Lines $run.Log '^\[NO\]') -eq 1) "it is the ONLY [NO] line (found $(Count-Lines $run.Log '^\[NO\]'))"
    foreach ($title in @('.NET Framework 4.8', 'Bridge to install', 'Drawing operations')) {
        Assert ($run.Log -match ('(?m)^\[ok\] ' + [regex]::Escape($title))) "prerequisite '$title' is [ok]"
    }
    Assert ($run.Log -match 'I stop here: there are things to fix\.') "it says 'I stop here: there are things to fix.'"
    Assert ($run.Log -cnotmatch '(?m)^Installed in ') "it does not say 'Installed in'"
    Assert (-not (Test-Path (Join-Path $folder 'oce-solid-agent-mcp.exe'))) "no bridge was written to the folder"
    foreach ($c in $clientList) {
        Assert ((Get-Hash $c.File) -eq $before[$c.File]) "$($c.Name): the configuration file is byte for byte what it was"
        Assert ((Get-Backups $c).Count -eq 0) "$($c.Name): no backup copy was made (nothing was touched)"
    }

    Section "--verify --json on a folder where nothing is installed"
    $v = Invoke-Installer (@('--verify', '--json', '--dir', $folder) + $redistArgs + $configArgs) 'verify.log'
    $json = Get-VerifyJson $v
    Assert ($v.Exit -eq 1) "exit code is 1 (exit $($v.Exit))"
    Assert ($null -ne $json) "the log holds one line of JSON"
    if ($json) {
        $st = Get-Statuses $json
        Write-Output ("  checks: " + (Describe-Statuses $st))
        foreach ($n in @('files', 'solidworks', 'clients', 'self-test', 'handshake')) {
            Assert ($st[$n] -eq 'fail') "check '$n' fails (it is '$($st[$n])')"
        }
        Assert ($st.Count -eq 5) "there are exactly five checks (no 'weldment profiles' without a wired client): $($st.Count)"
        Assert ($json.ok -eq $false -and $json.exit -eq 1) "the JSON says ok: false, exit: 1"
        Assert ($json.handshake.ok -eq $false) "the handshake did not pass"
    }

    Section "--uninstall of nothing"
    $u = Invoke-Installer (@('--uninstall', '--dir', $folder) + $configArgs) 'uninstall.log'
    Assert ($u.Exit -eq 0) "exit code is 0 (exit $($u.Exit))"
    Assert ($u.Log -match 'Uninstalled\. \(exit 0\)') "it ends with 'Uninstalled. (exit 0)'"
    foreach ($c in $clientList) {
        Assert ((Get-Canon (Read-Config $c.File)) -eq $seedCanon) "$($c.Name): the configuration is still what it was"
    }
    Save-ClientCopies 'end'
}

# =============================================================================
#  Scenario: install-temp, install-default
# =============================================================================

function Scenario-Install([bool] $defaultFolder) {
    $folder = if ($defaultFolder) { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Oce.Solid.Agent' } else { Join-Path $scratch 'install' }
    $dirArgs = if ($defaultFolder) { @() } else { @('--dir', $folder) }
    $profiles = Join-Path $scratch 'profiles'
    New-Item -ItemType Directory -Force $profiles | Out-Null
    New-Item -ItemType File -Force (Join-Path $profiles 'standin.sldlfp') | Out-Null
    $profilesFull = [System.IO.Path]::GetFullPath($profiles).TrimEnd('\')

    Section "the starting point"
    if ($defaultFolder) {
        Assert (-not (Test-Path $folder)) "the default folder does not exist yet: $folder"
        if (Test-Path $folder) { return }
    } else {
        Assert (-not (Test-Path $folder)) "the install folder does not exist yet"
    }
    # The bridge must not touch COM: OCE_NO_ATTACH makes its self-test exit 5.
    $env:OCE_NO_ATTACH = '1'
    $env:SW_INSTALL_PATH = $null
    $env:SW_REDIST = $null
    Reset-Clients
    Write-Output "  client(s): $(($clientList | ForEach-Object { $_.Name }) -join ', ')"

    Section "silent install (SOLIDWORKS stand-in, OCE_NO_ATTACH=1)"
    $run = Invoke-Installer (@('--silent') + $dirArgs + $redistArgs + @('--weldment-profiles', $profiles) + $configArgs) 'install-1.log'
    Assert ($run.Exit -eq 0) "exit code is 0 (exit $($run.Exit))"
    Assert ($run.Log -cmatch '(?m)^Installed in ') "the last line is 'Installed in <folder>'"
    Assert ((Count-Lines $run.Log '^\[NO\]') -eq 0) "no [NO] line"
    Assert ($run.Log -match '(?m)^\s*installing \d') "it says 'installing <version>' (a fresh install)"
    Assert ($run.Log -match 'self-test: exit 5') "the bridge's self-test exited 5: attach not tried"
    $version = $null
    if ($run.Log -match '(?m)^\s*installing (\S+)') { $version = $Matches[1] }
    foreach ($name in @('oce-solid-agent-mcp.exe', 'oce-solid-agent-mcp.exe.config', 'catalog.json', 'Oce.Solid.Agent.Core.dll', 'LICENSE', 'NOTICE')) {
        $f = Join-Path $folder $name
        Assert ((Test-Path $f) -and (Get-Item $f).Length -gt 0) "installed: $name"
    }
    $exe = Join-Path $folder 'oce-solid-agent-mcp.exe'
    foreach ($c in $clientList) {
        Assert ($run.Log -match ('added the entry "solidworks" in ' + [regex]::Escape($c.Name))) "$($c.Name): the log says the entry was added"
        $j = Read-Config $c.File
        $e = $j.mcpServers.solidworks
        Assert ($null -ne $e) "$($c.Name): the file has the entry 'solidworks'"
        if ($e) {
            Assert ($e.command -ieq $exe) "$($c.Name): command is the installed bridge"
            Assert ($e.env.SW_REDIST -ieq $redist) "$($c.Name): env.SW_REDIST names the folder given with --redist"
            Assert ($e.env.OCE_WELDMENT_PROFILES -ieq $profilesFull) "$($c.Name): env.OCE_WELDMENT_PROFILES names the folder given"
        }
        Assert ((Get-CanonWithoutEntry $c.File) -eq $seedCanon) "$($c.Name): everything else in the file is what it was (other servers, other settings)"
        Assert ((Get-Backups $c).Count -eq 1) "$($c.Name): one backup copy was made"
    }
    Save-ClientCopies 'after-install-1'

    Section "--verify --json after the install"
    $v = Invoke-Installer (@('--verify', '--json') + $dirArgs + $redistArgs + $configArgs) 'verify-1.log'
    Check-Verify $v $folder $version

    Section "observation, no assertion: the bridge's own self-test without OCE_NO_ATTACH"
    if ($onRunner) { Observe-SelfTest $exe }
    else { Write-Output "  skipped off a throw-away machine: here it could attach to a real SOLIDWORKS" }

    Section "reinstall over it, with a variable the user added to the entry"
    foreach ($c in $clientList) {
        $j = Read-Config $c.File
        $j.mcpServers.solidworks.env | Add-Member -NotePropertyName 'OCE_WRITE_ROOT' -NotePropertyValue (Join-Path $scratch 'write-root') -Force
        [System.IO.File]::WriteAllText($c.File, ($j | ConvertTo-Json -Depth 20), (New-Object System.Text.UTF8Encoding($false)))
    }
    $run2 = Invoke-Installer (@('--silent') + $dirArgs + $redistArgs + $configArgs) 'install-2.log'
    Assert ($run2.Exit -eq 0) "exit code is 0 (exit $($run2.Exit))"
    Assert ($run2.Log -match 'reinstalling ') "it says 'reinstalling <version>' (it was already this one)"
    foreach ($c in $clientList) {
        Assert ($run2.Log -match ('updated the entry "solidworks" in ' + [regex]::Escape($c.Name))) "$($c.Name): the log says the entry was updated"
        $e = (Read-Config $c.File).mcpServers.solidworks
        Assert ($e.env.OCE_WRITE_ROOT -ieq (Join-Path $scratch 'write-root')) "$($c.Name): the variable the user added is kept"
        Assert ($e.env.SW_REDIST -ieq $redist) "$($c.Name): SW_REDIST is still there"
        Assert ($e.env.OCE_WELDMENT_PROFILES -ieq $profilesFull) "$($c.Name): the weldment profiles folder is kept from the existing entry"
        Assert ((Get-CanonWithoutEntry $c.File) -eq $seedCanon) "$($c.Name): everything else in the file is still what it was"
    }
    Assert ((Test-Path $exe)) "the bridge is still installed"
    Save-ClientCopies 'after-install-2'

    $v2 = Invoke-Installer (@('--verify', '--json') + $dirArgs + $redistArgs + $configArgs) 'verify-2.log'
    Check-Verify $v2 $folder $version

    if ($defaultFolder) {
        # The bridge writes these beside itself while it runs; the profile-sections
        # archive lives in the DEFAULT folder, which is why this scenario exists.
        Section "files the bridge writes beside itself"
        Set-Content -Path (Join-Path $folder 'profile-sections.txt') -Value 'standin archive of measured profile sections' -Encoding UTF8
        Set-Content -Path (Join-Path $folder 'questions-without-answer.jsonl') -Value '{"standin":true}' -Encoding UTF8
        Write-Output "  planted profile-sections.txt and questions-without-answer.jsonl in $folder"
    }

    Section "uninstall"
    $un = Invoke-Installer (@('--uninstall') + $dirArgs + $configArgs) 'uninstall.log'
    Assert ($un.Exit -eq 0) "exit code is 0 (exit $($un.Exit))"
    Assert ($un.Log -match 'Uninstalled\. \(exit 0\)') "it ends with 'Uninstalled. (exit 0)'"
    foreach ($name in @('oce-solid-agent-mcp.exe', 'oce-solid-agent-mcp.exe.config', 'catalog.json', 'Oce.Solid.Agent.Core.dll', 'LICENSE', 'NOTICE')) {
        Assert (-not (Test-Path (Join-Path $folder $name))) "removed: $name"
    }
    if ($defaultFolder) {
        Assert (-not (Test-Path (Join-Path $folder 'profile-sections.txt'))) "removed: the archive of measured profile sections (profile-sections.txt)"
        Assert (-not (Test-Path (Join-Path $folder 'questions-without-answer.jsonl'))) "removed: the log of unanswered questions"
    }
    Assert (-not (Test-Path $folder)) "the folder itself is gone"
    foreach ($c in $clientList) {
        $j = Read-Config $c.File
        Assert (-not ($j.mcpServers.PSObject.Properties['solidworks'])) "$($c.Name): the entry 'solidworks' is gone"
        Assert ((Get-Canon $j) -eq $seedCanon) "$($c.Name): the file is what it was before the first install"
        $n = (Get-Backups $c).Count
        Assert ($n -ge 1 -and $n -le 3) "$($c.Name): the backup copies stay, at most three ($n)"
    }
    Save-ClientCopies 'after-uninstall'

    Section "--verify --json after the uninstall: it must see that nothing is left"
    $v3 = Invoke-Installer (@('--verify', '--json') + $dirArgs + $redistArgs + $configArgs) 'verify-3.log'
    $json = Get-VerifyJson $v3
    Assert ($v3.Exit -eq 1) "exit code is 1 (exit $($v3.Exit))"
    if ($json) {
        $st = Get-Statuses $json
        Assert ($st['files'] -eq 'fail' -and $st['clients'] -eq 'fail') "files and clients fail (the checks can fail: $(Describe-Statuses $st))"
    } else { Assert $false "the log holds one line of JSON" }
}

# What --verify --json must say after an install made with the stand-in.
function Check-Verify($v, [string] $folder, $version) {
    $json = Get-VerifyJson $v
    Assert ($v.Exit -eq 2) "verify exits 2: nothing failed, warnings only (exit $($v.Exit))"
    Assert ($null -ne $json) "the log holds one line of JSON"
    if (-not $json) { return }
    $st = Get-Statuses $json
    Write-Output ("  checks: " + (Describe-Statuses $st))
    $expected = [ordered]@{ 'files' = 'ok'; 'solidworks' = 'ok'; 'clients' = 'ok'; 'weldment profiles' = 'ok'; 'self-test' = 'warn'; 'handshake' = 'ok' }
    foreach ($n in $expected.Keys) {
        Assert ($st[$n] -eq $expected[$n]) "check '$n' is '$($expected[$n])' (it is '$($st[$n])')"
    }
    Assert ($st.Count -eq $expected.Count) "there are exactly $($expected.Count) checks ($($st.Count))"
    Assert ($json.ok -eq $false -and $json.exit -eq 2) "the JSON says ok: false, exit: 2"
    Assert ($json.selfTest.code -eq 5) "the bridge's self-test is exit 5: attach not tried, OCE_NO_ATTACH is set"
    Assert ($json.handshake.ok -eq $true -and $json.handshake.tools -ge 1) "the MCP handshake passed: $($json.handshake.tools) tools"
    Assert ($version -and $json.bridgeVersion -eq $version -and $json.installer -eq $version) "the bridge, the library and this installer agree on the version ($version)"
    Assert (@($json.files | Where-Object { -not $_.present }).Count -eq 0) "every file the install puts down is present"
    $wired = @($json.clients | Where-Object { $_.pointsToInstalled })
    Assert ($wired.Count -eq $clientList.Count) "every client in play runs the installed bridge ($($wired.Count) of $($clientList.Count))"
}

# Not an assertion: what the bridge says on a machine without SOLIDWORKS when
# it is allowed to try. Written down so the next version of this script can
# assert it.
function Observe-SelfTest([string] $exe) {
    $saved = $env:OCE_NO_ATTACH
    $env:OCE_NO_ATTACH = $null
    $env:SW_REDIST = $redist
    try {
        $out = Join-Path $Logs 'self-test-with-attach.txt'
        $err = Join-Path $Logs 'self-test-with-attach.err.txt'
        $p = Start-Process -FilePath $exe -ArgumentList '--self-test' -PassThru -NoNewWindow -RedirectStandardOutput $out -RedirectStandardError $err
        if (-not $p.WaitForExit(90000)) { try { $p.Kill() } catch { } ; Write-Output "  [observed] the self-test did not finish within 90 s and was stopped" }
        else { Write-Output "  [observed] bridge --self-test without OCE_NO_ATTACH: exit $($p.ExitCode)" }
    } finally {
        $env:OCE_NO_ATTACH = $saved
        $env:SW_REDIST = $null
    }
}

try {
    Write-Output "scenario: $Scenario, clients: $Clients, work folder: $Work"
    Write-Output "installer: $Installer ($((Get-Item $Installer).Length) bytes)"
    switch ($Scenario) {
        'absent' { Scenario-Absent }
        'install-temp' { Scenario-Install $false }
        'install-default' { Scenario-Install $true }
    }
} catch {
    $script:trouble++
    Write-Output "  XX  the script itself stopped: $($_.Exception.Message)"
    Write-Output ("      " + $_.ScriptStackTrace)
}
Finish
