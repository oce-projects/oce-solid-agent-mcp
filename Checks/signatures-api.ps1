# =============================================================================
#  signatures-api.ps1 - cosa dice l'INTEROP, non la pagina di documentazione
#                                                       (voci W1.1 e W1.2)
# =============================================================================
#
#  QUESTO FILE E' IN ASCII PURO, come bench.ps1 e per la stessa ragione.
#
#  PERCHE' ESISTE
#
#  Le voci W1.1 e W1.2 poggiano su frasi lette in una pagina web: "Obsolete",
#  "non dichiara nessun valore di ritorno", "elenca come accessore solo
#  GetDefinition". Sono affermazioni verificabili, e finche' non si verificano
#  restano **citazioni**.
#
#  La regola di casa e' quella: si verifica la cosa, non cio' che la descrive.
#  Un docstring invecchia, un assembly no -- e qui l'assembly e' quello
#  installato su QUESTA postazione, cioe' esattamente quello contro cui il
#  codice gira.
#
#  NON SERVE CHE SOLIDWORKS SIA APERTO: serve che sia installato.
#
#  USO
#      .\Checks\signatures-api.ps1
# =============================================================================

# `-TestMe` AGGIUNGE UNA PRETESA CHE NON PUO' REGGERE, e serve a vedere
# questa sonda diventare rossa. Una guardia che non si e' mai vista dire di
# no non si distingue da una rotta -- e la condizione vera (un aggiornamento
# di SOLIDWORKS che cambia una firma) non si puo' provocare a comando.
param([switch]$TestMe)

$ErrorActionPreference = "Continue"

$redist = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist"
if ($env:SW_INSTALL_PATH) { $redist = Join-Path $env:SW_INSTALL_PATH "api\redist" }

$sldworks = Join-Path $redist "SolidWorks.Interop.sldworks.dll"
$swconst  = Join-Path $redist "SolidWorks.Interop.swconst.dll"
if (-not (Test-Path $sldworks)) { Write-Output "non trovo $sldworks"; exit 2 }

$asm = [Reflection.Assembly]::LoadFrom($sldworks)
$cst = [Reflection.Assembly]::LoadFrom($swconst)

# ── E SIMULATION, CHE E' UN INTEROP A PARTE (voce S3) ────────────
#
# `cosworks` non e' dentro `sldworks`: e' un assembly suo, e su una
# macchina senza Simulation installato non c'e' affatto. Qui non si
# muore: si salta la sezione dicendolo. Un controllo che pretende un
# modulo che l'officina non ha comprato sarebbe rosso per sempre e
# smetterebbe di essere letto.
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

# ── I VALORI ATTESI, E IL VERDETTO ──────────────────────────────────────────
#
# Fino al 23/9/2026 questa sonda STAMPAVA e usciva 0 comunque. Una
# contestazione avversariale l'ha chiamata col suo nome: «niente valori
# attesi, niente confronto, niente exit 1 -- non e' un controllo di
# regressione, e' un promemoria».
#
# Adesso cio' che ci si aspetta e' scritto QUI, e se una firma cambia la
# sonda esce 1 e dice quale. *Un attrezzo che non puo' dire di no non
# sorveglia niente.*
$trouble = @()
function Expect($what, $expected, $found) {
    if ("$found" -eq "$expected") { Line $what ("ok  " + $found); return }
    Line $what ("CAMBIATO: atteso <" + $expected + ">, trovato <" + $found + ">")
    $script:trouble += $what
}

Write-Output ""
Write-Output ("interop: " + $sldworks)
Write-Output ""

# -- W1.1: FeatureFillPattern e la strada nuova ---------------------------------
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

# le marche che la voce cita, lette dall'enum invece che dalla pagina
# IL NOME L'AVEVO INDOVINATO, ed e' esattamente la cosa che questa sonda
# esiste per non fare: `swFeatureManagerCreateDefinitionType_e` non esiste.
# Le marche stanno in `swFeatureNameID_e`, e si trovano cercandole invece di
# ricordarsele.
$e = $cst.GetTypes() | Where-Object { $_.Name -eq "swFeatureNameID_e" } | Select-Object -First 1
if ($e) {
    foreach ($n in [Enum]::GetNames($e)) {
        if ($n -match "FillPattern|TablePattern") { Line ("  " + $n) ([int][Enum]::Parse($e, $n)) }
    }
} else { Line "  swFeatureNameID_e" "non trovato" }
Write-Output ""

# -- W1.2: InsertRib e come si rilegge ------------------------------------------
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

# e la via "moderna" esiste per la nervatura?
#
# `-cmatch '^swFmRib'` E NON `-match "Rib"`: il confronto di PowerShell ignora
# le maiuscole, e la prima stesura rispondeva **swFmAttribute** -- perche'
# "Att-RIB-ute" contiene "rib". Una riga che deve dire si' o no aveva detto
# si' per una sillaba.
if ($e) {
    $rib2 = [Enum]::GetNames($e) | Where-Object { $_ -cmatch '^swFmRib' }
    Line "CreateDefinition ha una marca per la nervatura?" $(if ($rib2) { $rib2 -join ", " } else { "NO -- nessun swFmRib*" })
}
Write-Output ""

# ── LE PRETESE ──────────────────────────────────────────────────────────────
#
# Solo i fatti su cui una voce del TODO poggia una decisione. Non si pretende
# tutto: un elenco che pretende tutto diventa rosso a ogni aggiornamento di
# SOLIDWORKS per ragioni che non interessano nessuno, e allora si spegne.
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

# -- I QUATTRO «DA ACCERTARE» ACCERTATI IL 24/9/2026 ---------------------------
#
# Quattro voci del TODO dicevano «non verificato» / «da accertare quale».
# Nessuna delle quattro voleva SOLIDWORKS acceso: volevano che qualcuno
# leggesse l'interop. Adesso letto, e messo qui perche' un fatto accertato
# una volta e mai piu' guardato torna «da accertare» da solo.
Write-Output ""
Write-Output "=== i quattro accertati il 24/9/2026 ==="

# GT5: il 33 era giusto, e la nota di 80r era vera.
Expect "GT5 il 33 e' HoleTableTemplates" "33" (CostValue "swUserPreferenceStringValue_e" "swFileLocationsHoleTableTemplates")
Expect "GT5 GetUserPreferenceStringValue torna String" "String" ((Method "ISldWorks" "GetUserPreferenceStringValue").ReturnType.Name)
Expect "GT5 ...e vuole 1 argomento" "1" ((Method "ISldWorks" "GetUserPreferenceStringValue").GetParameters().Count)

# GT2: l'inserimento della distinta sta su IView, non sull'annotazione.
Expect "GT2 InsertWeldmentTable sta su IView" "True" ([bool](Method "IView" "InsertWeldmentTable"))

# GA6: perche' GetTotalTransform e non GetXform -- una ragione, non un gusto.
Expect "GA6 GetTotalTransform torna MathTransform" "MathTransform" ((Method "IComponent2" "GetTotalTransform").ReturnType.Name)
Expect "GA6 GetXform torna un Object nudo" "Object" ((Method "IComponent2" "GetXform").ReturnType.Name)
Expect "GA6 GetMates torna un Object nudo" "Object" ((Method "IComponent2" "GetMates").ReturnType.Name)

# X2: la riga della filettatura diceva tre cose e due erano sbagliate.
Expect "X2 InsertHelix NON sta su IFeatureManager" "False" ([bool](Method "IFeatureManager" "InsertHelix"))
Expect "X2 ...sta su IModelDoc2" "True" ([bool](Method "IModelDoc2" "InsertHelix"))
Expect "X2 ...torna Void" "Void" ((Method "IModelDoc2" "InsertHelix").ReturnType.Name)
Expect "X2 ...con 10 argomenti" "10" ((Method "IModelDoc2" "InsertHelix").GetParameters().Count)
Expect "X2 InsertCutSwept5 torna Feature, non void" "Feature" ((Method "IFeatureManager" "InsertCutSwept5").ReturnType.Name)
Expect "X2 ...e ha 22 argomenti, non 21" "22" ((Method "IFeatureManager" "InsertCutSwept5").GetParameters().Count)
Expect "X2 EndVariablePitchHelix torna Feature" "Feature" ((Method "IFeatureManager" "EndVariablePitchHelix").ReturnType.Name)

# -- S3: la catena della mensola in Simulation ---------------------------------
#
# Accertata per riflessione il 24/9/2026, SOLIDWORKS INSTALLATO E NON
# ACCESO. La voce S3 dice «accertare prima, costruire poi»: questo e'
# l'accertare, e sta qui perche' una firma accertata una volta e mai
# piu' guardata e' una firma che invecchia.
#
# I due numeri della voce erano giusti: 589 tipi, 160 interfacce.
Write-Output ""
if (-not $sim) {
    Write-Output "=== S3  Simulation: NON INSTALLATO su questa macchina, sezione saltata ==="
    Write-Output ("  cercato in: " + $cosworks)
} else {
    Write-Output "=== S3  la catena della mensola: sette passi, accertati ==="

    Expect "S3 cosworks ha 589 tipi" "589" ($sim.GetTypes().Count)
    Expect "S3 ...di cui 160 interfacce" "160" (($sim.GetTypes() | Where-Object { $_.IsInterface }).Count)

    # 1-3. arrivare allo studio
    Expect "S3 CreateNewStudy3 ha 4 argomenti" "4" ((MethodSim "ICWStudyManager" "CreateNewStudy3").GetParameters().Count)
    Expect "S3 studio statico = 0" "0" (EnumValue "swsAnalysisStudyType_e" "swsAnalysisStudyTypeStatic")

    # 4. il materiale, che NON sta sullo studio
    Expect "S3 SetLibraryMaterial sta su ICWSolidBody" "True" ([bool](MethodSim "ICWSolidBody" "SetLibraryMaterial"))
    Expect "S3 ...e vuole 2 argomenti" "2" ((MethodSim "ICWSolidBody" "SetLibraryMaterial").GetParameters().Count)
    # E NON c'e' modo di RILEGGERLO: nessun GetMaterial. E' la stessa
    # forma trovata per la trasparenza (voce Z-D4): un setter senza
    # lettore. Chi vuole sapere che materiale c'e' deve chiederlo altrove.
    Expect "S3 ...e non esiste nessun GetMaterial" "False" ([bool](MethodSim "ICWSolidBody" "GetMaterial"))

    # 5. vincolo e carico
    Expect "S3 AddRestraint ha 4 argomenti" "4" ((MethodSim "ICWLoadsAndRestraintsManager" "AddRestraint").GetParameters().Count)
    Expect "S3 incastro = 0" "0" (EnumValue "swsRestraintType_e" "swsRestraintTypeFixed")
    Expect "S3 AddForce ha 4 argomenti" "4" ((MethodSim "ICWLoadsAndRestraintsManager" "AddForce").GetParameters().Count)
    Expect "S3 AddForce3 ne ha 21" "21" ((MethodSim "ICWLoadsAndRestraintsManager" "AddForce3").GetParameters().Count)

    # 6. mesh ed esecuzione -- ED E' QUI IL RITROVAMENTO
    Expect "S3 CreateMesh ha 3 argomenti" "3" ((MethodSim "ICWStudy" "CreateMesh").GetParameters().Count)
    Expect "S3 MeshAndRun torna Void" "Void" ((MethodSim "ICWStudy" "MeshAndRun").ReturnType.Name)
    Expect "S3 RunAnalysis torna Int32" "Int32" ((MethodSim "ICWStudy" "RunAnalysis").ReturnType.Name)

    # 7. la tensione
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
