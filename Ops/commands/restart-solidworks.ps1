# restart-solidworks.ps1 - closes SOLIDWORKS (ExitApp, then Stop-Process if it stays) and starts it again.
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) ported from the maintainer's restart script 1.3.0 (30/10/2026), with two
#         additions: it refuses while documents are open (-Force to skip the check), and -Kill for
#         a process that no longer responds. Behaviour kept from 1.3.0:
#         - ExitApp and StartupProcessCompleted from the TYPED interop (late binding answers
#           TYPE_E_ELEMENTNOTFOUND from PowerShell);
#         - -UnloadAddIn <dll> unloads an add-in for this session only (no registry change);
#         - -AddIn on|off writes HKCU\Software\SolidWorks\AddInsStartup\<CLSID> AFTER the close and
#           BEFORE the start: SOLIDWORKS writes that value itself on exit, so an add-in unloaded
#           with -UnloadAddIn would stay off at every later start. Whoever switches it off must
#           switch it on again with a last restart -AddIn on.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\restart-solidworks.ps1 [-DryRun]
#       [-Force] [-Kill] [-AddIn on|off] [-UnloadAddIn <dll>] [-AddInClsid <clsid>] [-StartTimeoutS 240]
#
# Exit codes:
#   0  SOLIDWORKS is running again and StartupProcessCompleted is true (or -DryRun)
#   1  started, but not ready within -StartTimeoutS
#   2  usage or configuration error (nothing touched)
#   3  refused: documents are open, or the process does not respond and -Kill was not given
#      (nothing touched). Open documents may be someone's work: close them first with
#      open-documents or close-leftovers, or pass -Force only when you know they are test pieces.
param(
    [switch] $DryRun,
    [switch] $Force,
    [switch] $Kill,
    [string] $AddIn = '',
    [string] $UnloadAddIn = '',
    [string] $AddInClsid = '',
    [int] $StartTimeoutS = 240
)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$cfg = Get-OpsConfig
$AddInClsid = Resolve-OpsValue $AddInClsid $cfg 'AddInClsid'
if ($AddIn -ne '' -and $AddIn -ne 'on' -and $AddIn -ne 'off') { Stop-Ops 2 '-AddIn wants on or off' }
if (($AddIn -ne '' -or $UnloadAddIn -ne '') -and -not $AddInClsid) { Stop-Ops 2 '-AddIn and -UnloadAddIn need the CLSID of the add-in (-AddInClsid or AddInClsid in the local configuration)' }
if (-not (Test-Path -LiteralPath $cfg.SolidWorksExe)) { Stop-Ops 2 "SOLIDWORKS not found: $($cfg.SolidWorksExe) (set SolidWorksExe)" }

$p = @(Get-Process SLDWORKS -ErrorAction SilentlyContinue)
foreach ($one in $p) {
    Write-Ops ("SLDWORKS pid {0} started {1}, responding {2}, handles {3}, memory {4} MB" -f $one.Id, $one.StartTime, $one.Responding, $one.HandleCount, [int]($one.WorkingSet64 / 1MB))
}
if ($DryRun) {
    if ($p.Count -gt 0) {
        if ($Kill) { Write-Ops 'would Stop-Process SLDWORKS without ExitApp' }
        else { Write-Ops 'would read the open documents, refuse if any is open (unless -Force), then ExitApp and wait 60 s' }
    }
    if ($AddIn) { Write-Ops "would write AddInsStartup\$AddInClsid = $([int]($AddIn -eq 'on'))" }
    Write-Ops "would start $($cfg.SolidWorksExe) and wait up to $StartTimeoutS s for StartupProcessCompleted"
    if ($UnloadAddIn) { Write-Ops "would unload $UnloadAddIn for this session" }
    exit 0
}

Add-OpsSolidWorks $cfg
if ($p.Count -gt 0) {
    if (-not $Kill) {
        if (@($p | Where-Object { -not $_.Responding }).Count -gt 0) {
            Stop-Ops 3 'SLDWORKS does not respond: attaching to it could hang this script. Look for a modal first (Ops\ops.ps1 modals); use -Kill only when there is no work to lose.'
        }
        if (-not $Force) {
            $docs = @([OpsSw]::Documents())
            if ($docs.Count -gt 0) {
                Write-Output 'open documents (save flag, visibility, title, path):'
                $docs | ForEach-Object { '  ' + $_ }
                Stop-Ops 3 "$($docs.Count) documents are open. Close the test pieces first (Ops\ops.ps1 close-leftovers -Close), or pass -Force."
            }
        }
        Write-Ops ([OpsSw]::Exit())
        $t0 = Get-Date
        while ((Get-Process SLDWORKS -ErrorAction SilentlyContinue) -and ((Get-Date) - $t0).TotalSeconds -lt 60) { Start-Sleep -Milliseconds 500 }
    }
    if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) {
        Stop-Process -Name SLDWORKS -Force
        Write-Ops 'Stop-Process SLDWORKS'
        Start-Sleep -Seconds 3
    }
    else { Write-Ops 'closed' }
    if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) {
        Write-Ops 'SLDWORKS is still there after Stop-Process: a process stuck in shutdown keeps its files mapped and is released only by a reboot of Windows (Ops\ops.ps1 who-locks tells who holds a file).'
    }
}
if ($AddIn -ne '') {
    $key = 'HKCU:\Software\SolidWorks\AddInsStartup\' + $AddInClsid
    if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
    Set-ItemProperty -Path $key -Name '(default)' -Value ([int]($AddIn -eq 'on')) -Type DWord
    Write-Ops ('add-in start value written: ' + (Get-Item $key).GetValue(''))
}
Start-Process $cfg.SolidWorksExe
$t0 = Get-Date
$ok = $false
while (((Get-Date) - $t0).TotalSeconds -lt $StartTimeoutS) { Start-Sleep -Seconds 3; if ([OpsSw]::Ready()) { $ok = $true; break } }
Write-Output ("reopened: {0} in {1} s" -f $ok, [int]((Get-Date) - $t0).TotalSeconds)
if ($ok -and $AddIn -ne '') {
    $want = if ($AddIn -eq 'on') { 'yes' } else { 'no' }
    $t1 = Get-Date
    while ([OpsSw]::Loaded($AddInClsid) -ne $want -and ((Get-Date) - $t1).TotalSeconds -lt 40) { Start-Sleep -Seconds 2 }
    Write-Ops ('add-in loaded: ' + [OpsSw]::Loaded($AddInClsid) + ' after ' + [int]((Get-Date) - $t1).TotalSeconds + ' s (wanted: ' + $want + ')')
}
if ($ok -and $UnloadAddIn -ne '') {
    Write-Ops ('add-in loaded before: ' + [OpsSw]::Loaded($AddInClsid))
    Write-Ops ([OpsSw]::Unload($UnloadAddIn))
    Write-Ops ('add-in loaded after: ' + [OpsSw]::Loaded($AddInClsid))
}
if ($ok) { exit 0 } else { exit 1 }
