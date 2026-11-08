# analyze-crashes.ps1 - reads with cdb the crash dumps that SOLIDWORKS writes through its error report (CXP).
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) ported from the maintainer's crash script 1.0.1 (30/10/2026), in English, with
#         cdb, symbol path and output folder from the configuration and -DryRun (lists the dumps,
#         runs nothing).
#
# Where the dumps are: when SOLIDWORKS falls it writes minidumps in
#   %LOCALAPPDATA%\SolidWorks\CXPD\<date>_<version>\  (two <guid>.dmp of about 1.5 MB, sometimes also
#   SLDWORKS.exe.<pid>.dmp of 100-200 MB); at the NEXT start it zips them into
#   %LOCALAPPDATA%\SolidWorks\CXPA\<date>_<version>.zip with SolidWorksPerformance.log. The zip of a
#   session closed normally has only the log (about 5.5 KB).
# The journal of SOLIDWORKS only gives the stack of the thread that falls; the dump has all of them.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\analyze-crashes.ps1
#       [-Since "yyyy-MM-dd HH:mm"] [-Out <folder>] [-Limit 0] [-SmallOnly] [-Commands "<cdb commands>"]
#       [-Cdb <cdb.exe>] [-SymbolPath <path>] [-TimeoutS 300] [-DryRun]
#   -Since: only zips and CXPD folders written from then on (default: today 00:00).
#   -SmallOnly skips dumps over 20 MB. -Limit: how many dumps at most (0 = all).
#
# For every dump: <dump>.txt with the cdb output. At the end one summary line per dump: exception code,
# first frame of the exception stack, threads with OnPaint, whether removeOleBitmapNode appears
# (the crash of a sketch picture in a sheet format), threads dispatching an incoming COM call.
#
# Exit codes: 0 done (also with no dump), 2 cdb missing.
param(
    [string] $Since = (Get-Date -Format 'yyyy-MM-dd 00:00'),
    [string] $Out = '',
    [int] $Limit = 0,
    [switch] $SmallOnly,
    [string] $Commands = '.ecxr; .echo ===EXCEPTION_STACK===; kb 80; .echo ===ALL_THREADS===; ~*kb 60; q',
    [string] $Cdb = '',
    [string] $SymbolPath = '',
    [int] $TimeoutS = 300,
    [switch] $DryRun
)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$cfg = Get-OpsConfig
$Cdb = Resolve-OpsValue $Cdb $cfg 'Cdb'
$SymbolPath = Resolve-OpsValue $SymbolPath $cfg 'SymbolPath'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$from = [datetime]::ParseExact($Since, 'yyyy-MM-dd HH:mm', $null)
if (-not $Out) { $Out = Join-Path (New-OpsOutDir $cfg) ('crash_' + $from.ToString('yyyyMMdd')) }
$cxpa = Join-Path $env:LOCALAPPDATA 'SolidWorks\CXPA'
$cxpd = Join-Path $env:LOCALAPPDATA 'SolidWorks\CXPD'
if (-not $DryRun -and (-not $Cdb -or -not (Test-Path -LiteralPath $Cdb))) { Stop-Ops 2 "cdb not found: '$Cdb' (set Cdb: Debugging Tools for Windows)" }

# 1. Dumps out of the zips, and those still in CXPD.
$dumps = New-Object System.Collections.Generic.List[string]
$zips = @(Get-ChildItem $cxpa -Filter '*.zip' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $from } | Sort-Object LastWriteTime)
$dirs = @(Get-ChildItem $cxpd -Directory -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $from })
if ($DryRun) {
    foreach ($z in $zips) { Write-Output ("zip {0} ({1:N0} bytes, {2})" -f $z.Name, $z.Length, $z.LastWriteTime) }
    foreach ($d in $dirs) { Get-ChildItem $d.FullName -Filter '*.dmp' | ForEach-Object { Write-Output ("dump {0}\{1} ({2:N0} bytes)" -f $d.Name, $_.Name, $_.Length) } }
    Write-Ops "would extract into $Out and run cdb on each dump"
    exit 0
}
New-Item -ItemType Directory -Force $Out | Out-Null
foreach ($zf in $zips) {
    $dest = Join-Path $Out $zf.BaseName
    $z = [IO.Compression.ZipFile]::OpenRead($zf.FullName)
    try {
        foreach ($e in $z.Entries) {
            if (-not $e.Name.EndsWith('.dmp', [StringComparison]::OrdinalIgnoreCase)) { continue }
            if ($SmallOnly -and $e.Length -gt 20MB) { continue }
            New-Item -ItemType Directory -Force $dest | Out-Null
            $file = Join-Path $dest $e.Name
            if (-not (Test-Path $file)) { [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $file) }
            $dumps.Add($file)
        }
    } finally { $z.Dispose() }
}
foreach ($d in $dirs) {
    $dest = Join-Path $Out ('CXPD_' + $d.Name)
    Get-ChildItem $d.FullName -Filter '*.dmp' | ForEach-Object {
        if ($SmallOnly -and $_.Length -gt 20MB) { return }
        New-Item -ItemType Directory -Force $dest | Out-Null
        $file = Join-Path $dest $_.Name
        if (-not (Test-Path $file)) { Copy-Item $_.FullName $file }
        $dumps.Add($file)
    }
}
Write-Output ('dumps found: ' + $dumps.Count + ' (in ' + $Out + ')')
if ($Limit -gt 0 -and $dumps.Count -gt $Limit) { $dumps = $dumps.GetRange(0, $Limit) }

# 2. cdb on every dump (those already read are skipped).
foreach ($d in $dumps) {
    $txt = $d + '.txt'
    if ((Test-Path $txt) -and (Get-Item $txt).Length -gt 1000) { continue }
    $t = Get-Date
    $p = Start-Process -FilePath $Cdb -ArgumentList @('-z', ('"' + $d + '"'), '-y', ('"' + $SymbolPath + '"'), '-c', ('"' + $Commands + '"')) -RedirectStandardOutput $txt -RedirectStandardError ($txt + '.err') -NoNewWindow -PassThru
    if (-not $p.WaitForExit($TimeoutS * 1000)) { $p.Kill(); Add-Content $txt "`r`n=== cdb stopped after $TimeoutS s ===" }
    '{0}: {1} s' -f (Split-Path $d -Leaf), [int]((Get-Date) - $t).TotalSeconds
}

# 3. Summary.
''
'dump | exception | first frame of the exception stack | OnPaint (threads) | removeOleBitmapNode | incoming COM call (threads)'
foreach ($d in $dumps) {
    $txt = $d + '.txt'
    if (-not (Test-Path $txt)) { continue }
    $lines = Get-Content $txt
    $exc = ($lines | Select-String -Pattern '^\(\w+\.\w+\): (.+)$' | Select-Object -First 1)
    $exc = if ($exc) { $exc.Matches[0].Groups[1].Value } else { '?' }
    $i = [Array]::IndexOf($lines, '===EXCEPTION_STACK===')
    $first = '?'
    if ($i -ge 0) { $first = ($lines[($i + 1)..([Math]::Min($lines.Count - 1, $i + 6))] | Where-Object { $_ -match '!' } | Select-Object -First 1); if ($first) { $first = ($first -split '\s+')[-1] } }
    $thread = ''; $paint = @(); $rpc = @(); $ole = $false
    foreach ($l in $lines) {
        if ($l -match '^\s*[#\.]?\s*(\d+)\s+Id:\s+\w+\.\w+') { $thread = $Matches[1] }
        if ($l -match 'OnPaint' -and $paint -notcontains $thread) { $paint += $thread }
        if ($l -match 'removeOleBitmapNode') { $ole = $true }
        if ($l -match 'rpcrt4!Invoke|combase!.*(Dispatch|ServerCall|StubInvoke)|rpcrt4!Ndr\w*StubCall' -and $rpc -notcontains $thread) { $rpc += $thread }
    }
    '{0} | {1} | {2} | {3} | {4} | {5}' -f ((Split-Path (Split-Path $d -Parent) -Leaf) + '\' + (Split-Path $d -Leaf)), $exc, $first, ($paint -join ','), $ole, ($rpc -join ',')
}
exit 0
