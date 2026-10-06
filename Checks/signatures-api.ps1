# =============================================================================
#  signatures-api.ps1 - what the INTEROP says, not the documentation page
#                                                       (entries W1.1 and W1.2)
# =============================================================================
#
#  THIS FILE IS PURE ASCII, like bench.ps1 and for the same reason.
#
#  WHY IT EXISTS
#
#  Entries W1.1 and W1.2 rest on sentences read on a web page: "Obsolete",
#  "does not declare any return value", "lists as the only accessor
#  GetDefinition". They are verifiable statements, and until they are verified
#  they remain **quotations**.
#
#  The house rule is that one: we verify the thing, not what describes it.
#  A docstring ages, an assembly does not -- and here the assembly is the one
#  installed on THIS workstation, that is exactly the one against which the
#  code runs.
#
#  SOLIDWORKS DOES NOT NEED TO BE OPEN: it needs to be installed.
#
#  USAGE
#      .\Checks\signatures-api.ps1
# =============================================================================

# `-TestMe` ADDS A CLAIM THAT CANNOT HOLD, and it serves to see
# this probe turn red. A guard that has never been seen to say
# no cannot be told from a broken one -- and the real condition (a SOLIDWORKS
# update that changes a signature) cannot be provoked on demand.
param([switch]$TestMe)

$ErrorActionPreference = "Continue"

$redist = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist"
if ($env:SW_INSTALL_PATH) { $redist = Join-Path $env:SW_INSTALL_PATH "api\redist" }

$sldworks = Join-Path $redist "SolidWorks.Interop.sldworks.dll"
$swconst  = Join-Path $redist "SolidWorks.Interop.swconst.dll"
if (-not (Test-Path $sldworks)) { Write-Output "cannot find $sldworks"; exit 2 }

$asm = [Reflection.Assembly]::LoadFrom($sldworks)
$cst = [Reflection.Assembly]::LoadFrom($swconst)

# ── AND SIMULATION, WHICH IS AN INTEROP APART (entry S3) ─────────
#
# `cosworks` is not inside `sldworks`: it is an assembly of its own, and on a
# machine without Simulation installed it is not there at all. Here we do not
# die: the section is skipped saying so. A check that demands a
# module that the shop has not bought would be red forever and
# would stop being read.
$cosworks = Join-Path $redist "SolidWorks.Interop.cosworks.dll"
$sim = $null
if (Test-Path $cosworks) { $sim = [Reflection.Assembly]::LoadFrom($cosworks) }

function TypeName($itemName) { return $asm.GetTypes() | Where-Object { $_.Name -eq $itemName } | Select-Object -First 1 }

function TypeSim($itemName) {
    if (-not $sim) { return $null }
    return $sim.GetTypes() | Where-Object { $_.Name -eq $itemName } | Select-Object -First 1
}

function MethodSim($typeName, $methodName) {
    $t = TypeSim $typeName
    if (-not $t) { return $null }
    return $t.GetMethods() | Where-Object { $_.Name -eq $methodName } | Select-Object -First 1
}

function EnumValue($enumName, $entry) {
    $t = TypeSim $enumName
    if (-not $t) { return "(the enum is not there)" }
    $n = [Enum]::GetNames($t); $v = [Enum]::GetValues($t)
    for ($i = 0; $i -lt $n.Count; $i++) { if ($n[$i] -ceq $entry) { return [string][int]$v[$i] } }
    return "(the entry is not there)"
}

function Method($typeName, $methodName) {
    $t = TypeName $typeName
    if (-not $t) { return $null }
    return $t.GetMethods() | Where-Object { $_.Name -eq $methodName } | Select-Object -First 1
}

function Line($e, $v) { Write-Output ("  {0,-38} {1}" -f $e, $v) }

function CostValue($enumName, $entry) {
    $t = $cst.GetTypes() | Where-Object { $_.Name -eq $enumName } | Select-Object -First 1
    if (-not $t) { return "(the enum is not there)" }
    $n = [Enum]::GetNames($t); $v = [Enum]::GetValues($t)
    for ($i = 0; $i -lt $n.Count; $i++) { if ($n[$i] -ceq $entry) { return [string][int]$v[$i] } }
    return "(the entry is not there)"
}

function Obsolete($m) {
    if (-not $m) { return "(the method is not there)" }
    $a = $m.GetCustomAttributes([System.ObsoleteAttribute], $false)
    if ($a.Count -eq 0) { return "NO -- the interop does not mark it" }
    return "SI': " + $a[0].Message
}

# ── THE EXPECTED VALUES, AND THE VERDICT ────────────────────────────────────
#
# Until 23/9/2026 this probe PRINTED and exited 0 anyway. An adversarial
# challenge called it by its name: «no expected values,
# no comparison, no exit 1 -- it is not a regression check, it is a
# reminder».
#
# Now what is expected is written HERE, and if a signature changes the
# probe exits 1 and says which. *A tool that cannot say no guards
# nothing.*
$trouble = @()
function Expect($what, $expected, $found) {
    if ("$found" -eq "$expected") { Line $what ("ok  " + $found); return }
    Line $what ("CHANGED: expected <" + $expected + ">, found <" + $found + ">")
    $script:trouble += $what
}

Write-Output ""
Write-Output ("interop: " + $sldworks)
Write-Output ""

# -- W1.1: FeatureFillPattern and the new road ---------------------------------
Write-Output "=== W1.1  Is FeatureFillPattern really Obsolete? ==="
$ffp = Method "IFeatureManager" "FeatureFillPattern"
Line "IFeatureManager.FeatureFillPattern" $(if ($ffp) { "it is there" } else { "NOT there" })
Line "  marked [Obsolete] in the interop?" (Obsolete $ffp)
if ($ffp) { Line "  ritorna" $ffp.ReturnType.Name }

$cd = Method "IFeatureManager" "CreateDefinition"
Line "IFeatureManager.CreateDefinition" $(if ($cd) { "it is there, returns " + $cd.ReturnType.Name } else { "NOT there" })
$cf = Method "IFeatureManager" "CreateFeature"
Line "IFeatureManager.CreateFeature" $(if ($cf) { "it is there, returns " + $cf.ReturnType.Name } else { "NOT there" })

foreach ($n in @("IFillPatternFeatureData", "ITablePatternFeatureData")) {
    $t = TypeName $n
    Line $n $(if ($t) { "it is there" } else { "NOT there" })
}
$pa = (TypeName "ITablePatternFeatureData")
if ($pa) {
    $p = $pa.GetProperties() | Where-Object { $_.Name -eq "PointArray" } | Select-Object -First 1
    Line "  ITablePatternFeatureData.PointArray" $(if ($p) { "it is there, " + $p.PropertyType.Name } else { "NOT there" })
}

# the marks that the entry cites, read from the enum instead of from the page
# I HAD GUESSED THE NAME, and it is exactly the thing that this probe
# exists to prevent: `swFeatureManagerCreateDefinitionType_e` does not exist.
# The marks are in `swFeatureNameID_e`, and are found by searching for them instead of
# remembering them.
$e = $cst.GetTypes() | Where-Object { $_.Name -eq "swFeatureNameID_e" } | Select-Object -First 1
if ($e) {
    foreach ($n in [Enum]::GetNames($e)) {
        if ($n -match "FillPattern|TablePattern") { Line ("  " + $n) ([int][Enum]::Parse($e, $n)) }
    }
} else { Line "  swFeatureNameID_e" "not found" }
Write-Output ""

# -- W1.2: InsertRib and how it is read back ------------------------------------
Write-Output "=== W1.2  Does InsertRib really return void? ==="
$rib = Method "IFeatureManager" "InsertRib"
Line "IFeatureManager.InsertRib" $(if ($rib) { "it is there" } else { "NOT there" })
if ($rib) {
    Line "  ritorna" $rib.ReturnType.Name
    Line "  argomenti" $rib.GetParameters().Count
}
$g6 = Method "ISelectionMgr" "GetSelectedObject6"
Line "ISelectionMgr.GetSelectedObject6" $(if ($g6) { "it is there, returns " + $g6.ReturnType.Name } else { "NOT there" })

$rfd = TypeName "IRibFeatureData2"
Line "IRibFeatureData2" $(if ($rfd) { "it is there" } else { "NOT there" })
$gd = Method "IFeature" "GetDefinition"
Line "IFeature.GetDefinition" $(if ($gd) { "it is there, returns " + $gd.ReturnType.Name } else { "NOT there" })

# and does the "modern" way exist for the rib?
#
# `-cmatch '^swFmRib'` AND NOT `-match "Rib"`: the PowerShell comparison ignores
# case, and the first draft answered **swFmAttribute** -- because
# "Att-RIB-ute" contains "rib". A line that must say yes or no had said
# yes because of one syllable.
if ($e) {
    $rib2 = [Enum]::GetNames($e) | Where-Object { $_ -cmatch '^swFmRib' }
    Line "Does CreateDefinition have a mark for the rib?" $(if ($rib2) { $rib2 -join ", " } else { "NO -- no swFmRib*" })
}
Write-Output ""

# ── THE CLAIMS ──────────────────────────────────────────────────────────────
#
# Only the facts on which an entry of the TODO rests a decision. Not everything is
# claimed: a list that claims everything turns red at every SOLIDWORKS update
# for reasons that interest nobody, and then it is turned off.
Write-Output "=== WHAT THE ENTRIES TAKE AS GRANTED ==="
Expect "W1.1 FeatureFillPattern exists" "True" ([bool](Method "IFeatureManager" "FeatureFillPattern"))
Expect "W1.1 ...and is NOT marked Obsolete" "NO -- the interop does not mark it" (Obsolete (Method "IFeatureManager" "FeatureFillPattern"))
Expect "W1.1 CreateDefinition exists" "True" ([bool](Method "IFeatureManager" "CreateDefinition"))
Expect "W1.2 InsertRib returns Void" "Void" ((Method "IFeatureManager" "InsertRib").ReturnType.Name)
Expect "W1.2 InsertRib has 10 arguments" "10" ((Method "IFeatureManager" "InsertRib").GetParameters().Count)
Expect "W1.2 GetSelectedObject6 exists" "True" ([bool](Method "ISelectionMgr" "GetSelectedObject6"))
Expect "GA11 MirrorComponents3 has 14 arguments" "14" ((Method "IAssemblyDoc" "MirrorComponents3").GetParameters().Count)
Expect "Z-B5 ActivateDoc3 has 4 arguments" "4" ((Method "ISldWorks" "ActivateDoc3").GetParameters().Count)
Expect "GP18 GetFlatPatternFolder exists" "True" ([bool](Method "IFeatureManager" "GetFlatPatternFolder"))

# -- THE FOUR «TO BE ESTABLISHED» ESTABLISHED ON 24/9/2026 ---------------------
#
# Four entries of the TODO said «not verified» / «which one to be established».
# None of the four needed SOLIDWORKS running: they needed somebody to
# read the interop. Now read, and put here because a fact established
# once and never looked at again turns back into «to be established» by itself.
Write-Output ""
Write-Output "=== the four established on 24/9/2026 ==="

# GT5: the 33 was right, and the note of 80r was true.
Expect "GT5 the 33 is HoleTableTemplates" "33" (CostValue "swUserPreferenceStringValue_e" "swFileLocationsHoleTableTemplates")
Expect "GT5 GetUserPreferenceStringValue returns String" "String" ((Method "ISldWorks" "GetUserPreferenceStringValue").ReturnType.Name)
Expect "GT5 ...and takes 1 argument" "1" ((Method "ISldWorks" "GetUserPreferenceStringValue").GetParameters().Count)

# GT2: the insertion of the bill of materials sits on IView, not on the annotation.
Expect "GT2 InsertWeldmentTable sta su IView" "True" ([bool](Method "IView" "InsertWeldmentTable"))

# GA6: why GetTotalTransform and not GetXform -- a reason, not a taste.
Expect "GA6 GetTotalTransform returns MathTransform" "MathTransform" ((Method "IComponent2" "GetTotalTransform").ReturnType.Name)
Expect "GA6 GetXform returns a bare Object" "Object" ((Method "IComponent2" "GetXform").ReturnType.Name)
Expect "GA6 GetMates returns a bare Object" "Object" ((Method "IComponent2" "GetMates").ReturnType.Name)

# X2: the thread line said three things and two were wrong.
Expect "X2 InsertHelix is NOT on IFeatureManager" "False" ([bool](Method "IFeatureManager" "InsertHelix"))
Expect "X2 ...sta su IModelDoc2" "True" ([bool](Method "IModelDoc2" "InsertHelix"))
Expect "X2 ...torna Void" "Void" ((Method "IModelDoc2" "InsertHelix").ReturnType.Name)
Expect "X2 ...with 10 arguments" "10" ((Method "IModelDoc2" "InsertHelix").GetParameters().Count)
Expect "X2 InsertCutSwept5 returns Feature, not void" "Feature" ((Method "IFeatureManager" "InsertCutSwept5").ReturnType.Name)
Expect "X2 ...and has 22 arguments, not 21" "22" ((Method "IFeatureManager" "InsertCutSwept5").GetParameters().Count)
Expect "X2 EndVariablePitchHelix returns Feature" "Feature" ((Method "IFeatureManager" "EndVariablePitchHelix").ReturnType.Name)

# -- S3: the chain of the bracket in Simulation --------------------------------
#
# Established by reflection on 24/9/2026, SOLIDWORKS INSTALLED AND NOT
# RUNNING. Entry S3 says «establish first, build afterwards»: this is
# the establishing, and it sits here because a signature established once and never
# looked at again is a signature that ages.
#
# The two numbers of the entry were right: 589 types, 160 interfaces.
Write-Output ""
if (-not $sim) {
    Write-Output "=== S3  Simulation: NOT INSTALLED on this machine, section skipped ==="
    Write-Output ("  looked in: " + $cosworks)
} else {
    Write-Output "=== S3  the bracket chain: seven steps, established ==="

    Expect "S3 cosworks has 589 types" "589" ($sim.GetTypes().Count)
    Expect "S3 ...of which 160 interfaces" "160" (($sim.GetTypes() | Where-Object { $_.IsInterface }).Count)

    # 1-3. reaching the study
    Expect "S3 CreateNewStudy3 has 4 arguments" "4" ((MethodSim "ICWStudyManager" "CreateNewStudy3").GetParameters().Count)
    Expect "S3 static study = 0" "0" (EnumValue "swsAnalysisStudyType_e" "swsAnalysisStudyTypeStatic")

    # 4. the material, which is NOT on the study
    Expect "S3 SetLibraryMaterial sta su ICWSolidBody" "True" ([bool](MethodSim "ICWSolidBody" "SetLibraryMaterial"))
    Expect "S3 ...and takes 2 arguments" "2" ((MethodSim "ICWSolidBody" "SetLibraryMaterial").GetParameters().Count)
    # And there is NO way to READ IT BACK: no GetMaterial. It is the same
    # shape found for transparency (entry Z-D4): a setter without a
    # reader. Whoever wants to know which material is there must ask elsewhere.
    Expect "S3 ...and there is no GetMaterial" "False" ([bool](MethodSim "ICWSolidBody" "GetMaterial"))

    # 5. restraint and load
    Expect "S3 AddRestraint has 4 arguments" "4" ((MethodSim "ICWLoadsAndRestraintsManager" "AddRestraint").GetParameters().Count)
    Expect "S3 fixed support = 0" "0" (EnumValue "swsRestraintType_e" "swsRestraintTypeFixed")
    Expect "S3 AddForce has 4 arguments" "4" ((MethodSim "ICWLoadsAndRestraintsManager" "AddForce").GetParameters().Count)
    Expect "S3 AddForce3 has 21" "21" ((MethodSim "ICWLoadsAndRestraintsManager" "AddForce3").GetParameters().Count)

    # 6. mesh and run -- AND HERE IS THE FINDING
    Expect "S3 CreateMesh has 3 arguments" "3" ((MethodSim "ICWStudy" "CreateMesh").GetParameters().Count)
    Expect "S3 MeshAndRun returns Void" "Void" ((MethodSim "ICWStudy" "MeshAndRun").ReturnType.Name)
    Expect "S3 RunAnalysis returns Int32" "Int32" ((MethodSim "ICWStudy" "RunAnalysis").ReturnType.Name)

    # 7. the stress
    Expect "S3 GetMinMaxStress has 6 arguments" "6" ((MethodSim "ICWResults" "GetMinMaxStress").GetParameters().Count)
    Expect "S3 von Mises = 9" "9" (EnumValue "swsStressComponent_e" "swsStressComponentVON")
    Expect "S3 N/mm2 = 3" "3" (EnumValue "swsStrengthUnit_e" "swsStrengthUnitNewtonPerSquareMillimeter")
}
if ($TestMe) {
    Expect "TEST: InsertRib has 99 arguments" "99" ((Method "IFeatureManager" "InsertRib").GetParameters().Count)
}
Write-Output ""

if ($trouble.Count -gt 0) {
    Write-Output ("STOPPED: " + $trouble.Count + " signatures are no longer the ones the entries take as granted.")
    foreach ($g in $trouble) { Write-Output ("  !  " + $g) }
    Write-Output "Look at the TODO entry that rests on it BEFORE updating the number."
    exit 1
}
Write-Output "All the signatures a TODO decision rests on are the usual ones."
Write-Output ""

Write-Output "An assembly does not age like a page: what it says here is"
Write-Output "what the code runs against on THIS workstation."
