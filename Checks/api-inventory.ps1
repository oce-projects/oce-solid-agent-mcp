# =============================================================================
#  api-inventory.ps1 - the API inventory, written by PowerShell (entry U1.1)
# =============================================================================
#
#  THIS FILE IS PURE ASCII, like bench.ps1 and signatures-api.ps1.
#
#  WHY IT EXISTS
#
#  `api_signature` prints signatures with a C# formatter (Oce.Solid.Agent.Core
#  Api\ApiCatalog.cs). A formatter can be wrong in quiet ways: an overload
#  dropped, a `&` lost on an out parameter, a property shown as a method. The
#  contract `ApiSignatureMatchesInventory` compares the formatter with THIS
#  script, which reads the same assembly with a different engine (PowerShell
#  reflection, not the C# code under test). Two writers that agree on 14,928
#  lines are checking each other; one writer checking itself is not.
#
#  It checks the FORMATTER, not the truth of the API: the truth is in the
#  point checks of the contract (Check3 is a property, AddMate5 has
#  AlignFromEnum, ...).
#
#  WHAT IS LISTED
#
#    members : every method of every interface of SolidWorks.Interop.sldworks
#              whose name starts with a capital I and that declares at least
#              one method (463 interfaces, 14,928 members on 2021 SP5.1).
#              Properties appear as their get_/set_ accessors.
#              One line each:  Interface.Member(Type Name, Type& Name) -> Ret
#    enums   : every enum of SolidWorks.Interop.swconst, one line each:
#              EnumName: name=value, name=value
#
#  SOLIDWORKS DOES NOT NEED TO BE RUNNING: it must be installed.
#
#  USE
#      .\Checks\api-inventory.ps1 -Members out\members.txt -Enums out\enums.txt
#      .\Checks\api-inventory.ps1 -Redist 'D:\...\api\redist' -Members m.txt
#
#  Exit 0 on success, 2 if the interop is not found.
# =============================================================================

param(
    [string] $Members,
    [string] $Enums,
    [string] $Redist
)

$ErrorActionPreference = "Stop"

if (-not $Redist) {
    $Redist = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist"
    if ($env:SW_INSTALL_PATH) { $Redist = Join-Path $env:SW_INSTALL_PATH "api\redist" }
    elseif ($env:SW_REDIST) { $Redist = $env:SW_REDIST }
}

$sldworks = Join-Path $Redist "SolidWorks.Interop.sldworks.dll"
$swconst  = Join-Path $Redist "SolidWorks.Interop.swconst.dll"
if (-not (Test-Path $sldworks)) { Write-Output "interop not found: $sldworks"; exit 2 }

$utf8 = New-Object System.Text.UTF8Encoding($false)

if ($Members) {
    $asm = [Reflection.Assembly]::LoadFrom($sldworks)
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($t in $asm.GetTypes()) {
        if (-not $t.IsInterface) { continue }
        if ($t.Name -cnotmatch '^I') { continue }
        foreach ($m in $t.GetMethods()) {
            $ps = @()
            foreach ($p in $m.GetParameters()) { $ps += ($p.ParameterType.Name + " " + $p.Name) }
            $lines.Add($t.Name + "." + $m.Name + "(" + ($ps -join ", ") + ") -> " + $m.ReturnType.Name)
        }
    }
    $sorted = $lines.ToArray()
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    [IO.File]::WriteAllLines($Members, $sorted, $utf8)
    Write-Output ("members: " + $sorted.Count)
}

if ($Enums) {
    $cst = [Reflection.Assembly]::LoadFrom($swconst)
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($t in $cst.GetTypes()) {
        if (-not $t.IsEnum) { continue }
        # Enum.GetNames/GetValues order (by value), the order the catalog
        # shows too: values that share a number stay side by side.
        $parts = @()
        $names = [Enum]::GetNames($t)
        $values = [Enum]::GetValues($t)
        for ($i = 0; $i -lt $names.Count; $i++) {
            $parts += ($names[$i] + "=" + [Convert]::ToInt64($values.GetValue($i)))
        }
        $lines.Add($t.Name + ": " + ($parts -join ", "))
    }
    $sorted = $lines.ToArray()
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    [IO.File]::WriteAllLines($Enums, $sorted, $utf8)
    Write-Output ("enums: " + $sorted.Count)
}
exit 0
