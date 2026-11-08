# events.ps1 - crashes, hangs and unexpected reboots from the Windows event logs, since a given time.
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) first version, from the queries the maintainer ran by hand. Two lessons are
#         written into it:
#         - LOOK AT THE REBOOT FIRST. A program "closed for no reason" was once explained by a blue
#           screen and a restart of the PC that nobody had looked at; the search went to the wrong
#           place. LastBootUpTime and the System events 41, 1001, 6008 come first in the output.
#         - The fields come from the event PROPERTIES, not from the message: the message is localized
#           (on an Italian Windows its labels are Italian), the properties are not.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\events.ps1 [-Since "yyyy-MM-dd HH:mm"]
#       [-Processes "SLDWORKS.exe,oce-solid-agent-mcp.exe"]
#
# Known entry: Application Error 1000 of SLDWORKS.exe in slduiu.dll at offset 0x94d513 means that a
# program outside SOLIDWORKS released a reference to a drawing-view co-edge AFTER the drawing was
# closed (cured in the bench in 0.170.13: release before closing). A new one means somebody closes a
# document while objects of its views are still alive.
#
# Exit codes: 0 nothing found, 1 at least one event listed.
param([string] $Since = (Get-Date -Format 'yyyy-MM-dd 00:00'), [string] $Processes = 'SLDWORKS.exe,oce-solid-agent-mcp.exe')
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$from = [datetime]::ParseExact($Since, 'yyyy-MM-dd HH:mm', $null)
$names = @($Processes -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$found = 0
$boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
Write-Output ("last boot: {0:s}{1}" -f $boot, $(if ($boot -ge $from) { '   <-- the PC restarted inside the period' } else { '' }))

$sys = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; Id = 41, 1001, 6008; StartTime = $from } -ErrorAction SilentlyContinue)
foreach ($e in $sys) {
    $found++
    $what = switch ($e.Id) { 41 { 'Kernel-Power 41: rebooted without a clean shutdown' } 1001 { 'BugCheck 1001: blue screen' } 6008 { 'EventLog 6008: unexpected shutdown' } }
    $extra = if ($e.Id -eq 1001 -and $e.Properties.Count -gt 0) { ' ' + $e.Properties[0].Value } else { '' }
    Write-Output ("{0:s} System {1} {2}{3}" -f $e.TimeCreated, $e.Id, $what, $extra)
}
$app = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Id = 1000, 1002, 1026; StartTime = $from } -ErrorAction SilentlyContinue)
foreach ($e in $app) {
    $p = @($e.Properties | ForEach-Object { "$($_.Value)" })
    $proc = if ($p.Count -gt 0) { $p[0] } else { '' }
    if ($e.Id -eq 1026) { $proc = (@($names | Where-Object { $e.Message -match [regex]::Escape($_) }) | Select-Object -First 1) }
    if (-not $proc -or -not (@($names | Where-Object { $proc -like "*$_*" -or $_ -like "*$proc*" }).Count)) { continue }
    $found++
    switch ($e.Id) {
        1000 { Write-Output ("{0:s} Application Error 1000 {1}: module {2}, exception {3}, offset {4}" -f $e.TimeCreated, $p[0], $p[3], $p[6], $p[7]) }
        1002 { Write-Output ("{0:s} Application Hang 1002 {1}" -f $e.TimeCreated, $p[0]) }
        1026 { Write-Output ("{0:s} .NET Runtime 1026 {1}: {2}" -f $e.TimeCreated, $proc, (($e.Message -split "`n" | Select-Object -First 3) -join ' ' -replace '\s+', ' ')) }
    }
}
Write-Output "events: $found since $($from.ToString('s'))"
if ($found -gt 0) { exit 1 }
exit 0
