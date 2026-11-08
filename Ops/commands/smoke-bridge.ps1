# smoke-bridge.ps1 - start test of a bridge executable: initialize + tools/list, without SOLIDWORKS.
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) first version, from the start test the maintainer ran by hand after every
#         deploy. Two traps it avoids, both measured:
#         - the requests go through a FILE written as UTF-8 WITHOUT a BOM and `cmd /c "exe < file"`.
#           From PowerShell 5.1 a redirected StandardInput writes the BOM of the console encoding
#           before anything else, and the bridge answers -32700 "Invalid JSON";
#         - OCE_NO_ATTACH=1 in this process only: the bridge answers the protocol and lists its tools
#           without attaching to the SOLIDWORKS session someone may be using.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\smoke-bridge.ps1
#       [-Exe <path to oce-solid-agent-mcp.exe>] [-ExpectVersion <x.y.z>] [-TimeoutS 60] [-DryRun]
#   Without -Exe it tests the deployed one (DeployTarget of the configuration).
#
# Output: serverInfo name and version, number of tools; "SMOKE OK" or "SMOKE FAILED: <why>".
#
# Exit codes:
#   0  the bridge answered initialize and tools/list (and the version is the expected one)
#   1  no answer, a wrong answer, or another version
#   2  the executable does not exist (or a configuration value is missing)
param([string] $Exe = '', [string] $ExpectVersion = '', [int] $TimeoutS = 60, [switch] $DryRun)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$cfg = Get-OpsConfig
if (-not $Exe) {
    $target = Resolve-OpsValue '' $cfg 'DeployTarget' -Required
    $Exe = Join-Path $target 'oce-solid-agent-mcp.exe'
}
if (-not (Test-Path -LiteralPath $Exe)) { Stop-Ops 2 "no executable: $Exe" }
$dir = Join-Path (New-OpsOutDir $cfg) ('smoke_' + (Get-OpsStamp))
New-Item -ItemType Directory -Path $dir -Force | Out-Null
$req = Join-Path $dir 'requests.jsonl'
$resp = Join-Path $dir 'responses.jsonl'
$err = Join-Path $dir 'stderr.txt'
$lines = @(
    '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"oce-ops-smoke","version":"1.0.0"}}}',
    '{"jsonrpc":"2.0","method":"notifications/initialized"}',
    '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
)
[IO.File]::WriteAllText($req, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))
if ($DryRun) { Write-Ops "would run (OCE_NO_ATTACH=1): cmd /s /c `"`"$Exe`" < `"$req`" > `"$resp`"`""; exit 0 }

$env:OCE_NO_ATTACH = '1'
$cmdLine = '/s /c ""' + $Exe + '" < "' + $req + '" > "' + $resp + '" 2> "' + $err + '""'
$p = Start-Process cmd -ArgumentList $cmdLine -NoNewWindow -PassThru
if (-not $p.WaitForExit($TimeoutS * 1000)) {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Write-Output "the bridge did not exit within $TimeoutS s after the end of its input"
}
$answers = @{}
if (Test-Path -LiteralPath $resp) {
    foreach ($l in [IO.File]::ReadAllLines($resp)) {
        if (-not $l.Trim()) { continue }
        try { $o = $l | ConvertFrom-Json } catch { continue }
        if ($o.PSObject.Properties['id']) { $answers["$($o.id)"] = $o }
    }
}
$why = ''
$init = $answers['1']; $list = $answers['2']
if (-not $init) { $why = 'no answer to initialize' }
elseif ($init.PSObject.Properties['error']) { $why = 'initialize answered an error: ' + ($init.error | ConvertTo-Json -Compress) }
$name = ''; $version = ''; $tools = -1
if (-not $why) {
    $name = $init.result.serverInfo.name; $version = $init.result.serverInfo.version
    Write-Output "serverInfo: $name $version"
    if (-not $list) { $why = 'no answer to tools/list' }
    elseif ($list.PSObject.Properties['error']) { $why = 'tools/list answered an error: ' + ($list.error | ConvertTo-Json -Compress) }
    else { $tools = @($list.result.tools).Count; Write-Output "tools: $tools" }
}
if (-not $why -and $tools -le 0) { $why = 'tools/list is empty' }
if (-not $why -and $ExpectVersion -and -not "$version".StartsWith($ExpectVersion)) { $why = "version $version, expected $ExpectVersion" }
if ((Test-Path -LiteralPath $err) -and (Get-Item -LiteralPath $err).Length -gt 0) {
    Write-Output 'stderr of the bridge (last 5 lines):'
    Get-Content -LiteralPath $err -Tail 5 | ForEach-Object { '  ' + $_ }
}
Write-Output "files: $dir"
if ($why) { Write-Output "SMOKE FAILED: $why"; exit 1 }
Write-Output 'SMOKE OK'
exit 0
