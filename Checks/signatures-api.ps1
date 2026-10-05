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
if (-not (Test-Path $sldworks)) { Write-Output "non trovo $sldworks"; exit 2 }

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
    if (-not $t) { return "(l'enum non c'e')" }
    $n = [Enum]::GetNames($t); $v = [Enum]::GetValues($t)
    for ($i = 0; $i -lt $n.Count; $i++) { if ($n[$i] -ceq $entry) { return [string][int]$v[$i] } }
    return "(la voce non c'e')"
}

function Method($typeName, $methodName) {
    $t = TypeName $typeName
    if (-not $t) { return $null }
    return $t.GetMethods() | Where-Object { $_.Name -eq $methodName } | Select-Object -First 1
}

function Line($e, $v) { Write-Output ("  {0,-38} {1}" -f $e, $v) }

function CostValue($enumName, $entry) {
    $t = $cst.GetTypes() | Where-Object { $_.Name -eq $enumName } | Select-Object -First 1
    if (-not $t) { return "(l'enum non c'e')" }
    $n = [Enum]::GetNames($t); $v = [Enum]::GetValues($t)
    for ($i = 0; $i -lt $n.Count; $i++) { if ($n[$i] -ceq $entry) { return [string][int]$v[$i] } }
    return "(la voce non c'e')"
}

function Obsolete($m) {
    if (-not $m) { return "(il metodo non c'e')" }
    $a = $m.GetCustomAttributes([System.ObsoleteAttribute], $false)
    if ($a.Count -eq 0) { return "NO -- l'interop non lo marca" }
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
    Line $what ("CAMBIATO: atteso <" + $expected + ">, trovato <" + $found + ">")
    $script:trouble += $what
}

Write-Output ""
Write-Output ("interop: " + $sldworks)
Write-Output ""

# -- W1.1: FeatureFillPattern and the new road ---------------------------------
Write-Output "=== W1.1  FeatureFillPattern e' davvero Obsolete? ==="
$ffp = Method "IFeatureManager" "FeatureFillPattern"
Line "IFeatureManager.FeatureFillPattern" $(if ($ffp) { "c'e'" } else { "NON c'e'" })
Line "  marcato [Obsolete] nell'interop?" (Obsolete $ffp)
if ($ffp) { Line "  ritorna" $ffp.ReturnType.Name }

$cd = Method "IFeatureManager" "CreateDefinition"
Line "IFeatureManager.CreateDefinition" $(if ($cd) { "c'e', ritorna " + $cd.ReturnType.Name } else { "NON c'e'" })
$cf = Method "IFeatureManager" "CreateFeature"
Line "IFeatureManager.CreateFeature" $(if ($cf) { "c'e', ritorna " + $cf.ReturnType.Name } else { "NON c'e'" })

foreach ($n in @("IFillPatternFeatureData", "ITablePatternFeatureData")) {
    $t = TypeName $n
    Line $n $(if ($t) { "c'e'" } else { "NON c'e'" })
}
$pa = (TypeName "ITablePatternFeatureData")
if ($pa) {
    $p = $pa.GetProperties() | Where-Object { $_.Name -eq "PointArray" } | Select-Object -First 1
    Line "  ITablePatternFeatureData.PointArray" $(if ($p) { "c'e', " + $p.PropertyType.Name } else { "NON c'e'" })
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
} else { Line "  swFeatureNameID_e" "non trovato" }
Write-Output ""

# -- W1.2: InsertRib and how it is read back ------------------------------------
Write-Output "=== W1.2  InsertRib torna davvero void? ==="
$rib = Method "IFeatureManager" "InsertRib"
Line "IFeatureManager.InsertRib" $(if ($rib) { "c'e'" } else { "NON c'e'" })
if ($rib) {
    Line "  ritorna" $rib.ReturnType.Name
    Line "  argomenti" $rib.GetParameters().Count
}
$g6 = Method "ISelectionMgr" "GetSelectedObject6"
Line "ISelectionMgr.GetSelectedObject6" $(if ($g6) { "c'e', ritorna " + $g6.ReturnType.Name } else { "NON c'e'" })

$rfd = TypeName "IRibFeatureData2"
Line "IRibFeatureData2" $(if ($rfd) { "c'e'" } else { "NON c'e'" })
$gd = Method "IFeature" "GetDefinition"
Line "IFeature.GetDefinition" $(if ($gd) { "c'e', ritorna " + $gd.ReturnType.Name } else { "NON c'e'" })

# and does the "modern" way exist for the rib?
#
# `-cmatch '^swFmRib'` AND NOT `-match "Rib"`: the PowerShell comparison ignores
# case, and the first draft answered **swFmAttribute** -- because
# "Att-RIB-ute" contains "rib". A line that must say yes or no had said
# yes because of one syllable.
if ($e) {
    $rib2 = [Enum]::GetNames($e) | Where-Object { $_ -cmatch '^swFmRib' }
    Line "CreateDefinition ha una marca per la nervatura?" $(if ($rib2) { $rib2 -join ", " } else { "NO -- nessun swFmRib*" })
}
Write-Output ""

# ── THE CLAIMS ──────────────────────────────────────────────────────────────
#
# Only the facts on which an entry of the TODO rests a decision. Not everything is
# claimed: a list that claims everything turns red at every SOLIDWORKS update
# for reasons that interest nobody, and then it is turned off.
Write-Output "=== QUELLO CHE LE VOCI DANNO PER BUONO ==="
Expect "W1.1 FeatureFillPattern esiste" "True" ([bool](Method "IFeatureManager" "FeatureFillPattern"))
Expect "W1.1 ...e NON e' marcato Obsolete" "NO -- l'interop non lo marca" (Obsolete (Method "IFeatureManager" "FeatureFillPattern"))
Expect "W1.1 CreateDefinition esiste" "True" ([bool](Method "IFeatureManager" "CreateDefinition"))
Expect "W1.2 InsertRib ritorna Void" "Void" ((Method "IFeatureManager" "InsertRib").ReturnType.Name)
Expect "W1.2 InsertRib ha 10 argomenti" "10" ((Method "IFeatureManager" "InsertRib").GetParameters().Count)
Expect "W1.2 GetSelectedObject6 esiste" "True" ([bool](Method "ISelectionMgr" "GetSelectedObject6"))
Expect "GA11 MirrorComponents3 ha 14 argomenti" "14" ((Method "IAssemblyDoc" "MirrorComponents3").GetParameters().Count)
Expect "Z-B5 ActivateDoc3 ha 4 argomenti" "4" ((Method "ISldWorks" "ActivateDoc3").GetParameters().Count)
Expect "GP18 GetFlatPatternFolder esiste" "True" ([bool](Method "IFeatureManager" "GetFlatPatternFolder"))

# -- THE FOUR «TO BE ESTABLISHED» ESTABLISHED ON 24/9/2026 ---------------------
#
# Four entries of the TODO said «not verified» / «which one to be established».
# None of the four needed SOLIDWORKS running: they needed somebody to
# read the interop. Now read, and put here because a fact established
# once and never looked at again turns back into «to be established» by itself.
Write-Output ""
Write-Output "=== i quattro accertati il 24/9/2026 ==="

# GT5: the 33 was right, and the note of 80r was true.
Expect "GT5 il 33 e' HoleTableTemplates" "33" (CostValue "swUserPreferenceStringValue_e" "swFileLocationsHoleTableTemplates")
Expect "GT5 GetUserPreferenceStringValue torna String" "String" ((Method "ISldWorks" "GetUserPreferenceStringValue").ReturnType.Name)
Expect "GT5 ...e vuole 1 argomento" "1" ((Method "ISldWorks" "GetUserPreferenceStringValue").GetParameters().Count)

# GT2: the insertion of the bill of materials sits on IView, not on the annotation.
Expect "GT2 InsertWeldmentTable sta su IView" "True" ([bool](Method "IView" "InsertWeldmentTable"))

# GA6: why GetTotalTransform and not GetXform -- a reason, not a taste.
Expect "GA6 GetTotalTransform torna MathTransform" "MathTransform" ((Method "IComponent2" "GetTotalTransform").ReturnType.Name)
Expect "GA6 GetXform torna un Object nudo" "Object" ((Method "IComponent2" "GetXform").ReturnType.Name)
Expect "GA6 GetMates torna un Object nudo" "Object" ((Method "IComponent2" "GetMates").ReturnType.Name)

# X2: the thread line said three things and two were wrong.
Expect "X2 InsertHelix NON sta su IFeatureManager" "False" ([bool](Method "IFeatureManager" "InsertHelix"))
Expect "X2 ...sta su IModelDoc2" "True" ([bool](Method "IModelDoc2" "InsertHelix"))
Expect "X2 ...torna Void" "Void" ((Method "IModelDoc2" "InsertHelix").ReturnType.Name)
Expect "X2 ...con 10 argomenti" "10" ((Method "IModelDoc2" "InsertHelix").GetParameters().Count)
Expect "X2 InsertCutSwept5 torna Feature, non void" "Feature" ((Method "IFeatureManager" "InsertCutSwept5").ReturnType.Name)
Expect "X2 ...e ha 22 argomenti, non 21" "22" ((Method "IFeatureManager" "InsertCutSwept5").GetParameters().Count)
Expect "X2 EndVariablePitchHelix torna Feature" "Feature" ((Method "IFeatureManager" "EndVariablePitchHelix").ReturnType.Name)

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
    Write-Output "=== S3  Simulation: NON INSTALLATO su questa macchina, sezione saltata ==="
    Write-Output ("  cercato in: " + $cosworks)
} else {
    Write-Output "=== S3  la catena della mensola: sette passi, accertati ==="

    Expect "S3 cosworks ha 589 tipi" "589" ($sim.GetTypes().Count)
    Expect "S3 ...di cui 160 interfacce" "160" (($sim.GetTypes() | Where-Object { $_.IsInterface }).Count)

    # 1-3. reaching the study
    Expect "S3 CreateNewStudy3 ha 4 argomenti" "4" ((MethodSim "ICWStudyManager" "CreateNewStudy3").GetParameters().Count)
    Expect "S3 studio statico = 0" "0" (EnumValue "swsAnalysisStudyType_e" "swsAnalysisStudyTypeStatic")

    # 4. the material, which is NOT on the study
    Expect "S3 SetLibraryMaterial sta su ICWSolidBody" "True" ([bool](MethodSim "ICWSolidBody" "SetLibraryMaterial"))
    Expect "S3 ...e vuole 2 argomenti" "2" ((MethodSim "ICWSolidBody" "SetLibraryMaterial").GetParameters().Count)
    # And there is NO way to READ IT BACK: no GetMaterial. It is the same
    # shape found for transparency (entry Z-D4): a setter without a
    # reader. Whoever wants to know which material is there must ask elsewhere.
    Expect "S3 ...e non esiste nessun GetMaterial" "False" ([bool](MethodSim "ICWSolidBody" "GetMaterial"))

    # 5. restraint and load
    Expect "S3 AddRestraint ha 4 argomenti" "4" ((MethodSim "ICWLoadsAndRestraintsManager" "AddRestraint").GetParameters().Count)
    Expect "S3 incastro = 0" "0" (EnumValue "swsRestraintType_e" "swsRestraintTypeFixed")
    Expect "S3 AddForce ha 4 argomenti" "4" ((MethodSim "ICWLoadsAndRestraintsManager" "AddForce").GetParameters().Count)
    Expect "S3 AddForce3 ne ha 21" "21" ((MethodSim "ICWLoadsAndRestraintsManager" "AddForce3").GetParameters().Count)

    # 6. mesh and run -- AND HERE IS THE FINDING
    Expect "S3 CreateMesh ha 3 argomenti" "3" ((MethodSim "ICWStudy" "CreateMesh").GetParameters().Count)
    Expect "S3 MeshAndRun torna Void" "Void" ((MethodSim "ICWStudy" "MeshAndRun").ReturnType.Name)
    Expect "S3 RunAnalysis torna Int32" "Int32" ((MethodSim "ICWStudy" "RunAnalysis").ReturnType.Name)

    # 7. the stress
    Expect "S3 GetMinMaxStress ha 6 argomenti" "6" ((MethodSim "ICWResults" "GetMinMaxStress").GetParameters().Count)
    Expect "S3 von Mises = 9" "9" (EnumValue "swsStressComponent_e" "swsStressComponentVON")
    Expect "S3 N/mm2 = 3" "3" (EnumValue "swsStrengthUnit_e" "swsStrengthUnitNewtonPerSquareMillimeter")
}
if ($TestMe) {
    Expect "PROVA: InsertRib ha 99 argomenti" "99" ((Method "IFeatureManager" "InsertRib").GetParameters().Count)
}
Write-Output ""

if ($trouble.Count -gt 0) {
    Write-Output ("FERMO: " + $trouble.Count + " firme non sono piu' quelle che le voci danno per buone.")
    foreach ($g in $trouble) { Write-Output ("  !  " + $g) }
    Write-Output "Guardare la voce del TODO che ci poggia sopra PRIMA di aggiornare il numero."
    exit 1
}
Write-Output "Tutte le firme su cui il TODO poggia una decisione sono quelle di sempre."
Write-Output ""

Write-Output "Un assembly non invecchia come una pagina: quello che dice qui e'"
Write-Output "quello contro cui il codice gira su QUESTA postazione."
