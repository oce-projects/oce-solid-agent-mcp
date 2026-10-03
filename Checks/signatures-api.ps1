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

# `-Provami` AGGIUNGE UNA PRETESA CHE NON PUO' REGGERE, e serve a vedere
# questa sonda diventare rossa. Una guardia che non si e' mai vista dire di
# no non si distingue da una rotta -- e la condizione vera (un aggiornamento
# di SOLIDWORKS che cambia una firma) non si puo' provocare a comando.
param([switch]$Provami)

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

function Tipo($nome) { return $asm.GetTypes() | Where-Object { $_.Name -eq $nome } | Select-Object -First 1 }

function TipoSim($nome) {
    if (-not $sim) { return $null }
    return $sim.GetTypes() | Where-Object { $_.Name -eq $nome } | Select-Object -First 1
}

function MetodoSim($tipoNome, $metodoNome) {
    $t = TipoSim $tipoNome
    if (-not $t) { return $null }
    return $t.GetMethods() | Where-Object { $_.Name -eq $metodoNome } | Select-Object -First 1
}

function ValoreEnum($enumNome, $voce) {
    $t = TipoSim $enumNome
    if (-not $t) { return "(l'enum non c'e')" }
    $n = [Enum]::GetNames($t); $v = [Enum]::GetValues($t)
    for ($i = 0; $i -lt $n.Count; $i++) { if ($n[$i] -ceq $voce) { return [string][int]$v[$i] } }
    return "(la voce non c'e')"
}

function Metodo($tipoNome, $metodoNome) {
    $t = Tipo $tipoNome
    if (-not $t) { return $null }
    return $t.GetMethods() | Where-Object { $_.Name -eq $metodoNome } | Select-Object -First 1
}

function Riga($e, $v) { Write-Output ("  {0,-38} {1}" -f $e, $v) }

function ValoreCosto($enumNome, $voce) {
    $t = $cst.GetTypes() | Where-Object { $_.Name -eq $enumNome } | Select-Object -First 1
    if (-not $t) { return "(l'enum non c'e')" }
    $n = [Enum]::GetNames($t); $v = [Enum]::GetValues($t)
    for ($i = 0; $i -lt $n.Count; $i++) { if ($n[$i] -ceq $voce) { return [string][int]$v[$i] } }
    return "(la voce non c'e')"
}

function Obsoleto($m) {
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
$guai = @()
function Pretendi($cosa, $atteso, $trovato) {
    if ("$trovato" -eq "$atteso") { Riga $cosa ("ok  " + $trovato); return }
    Riga $cosa ("CAMBIATO: atteso <" + $atteso + ">, trovato <" + $trovato + ">")
    $script:guai += $cosa
}

Write-Output ""
Write-Output ("interop: " + $sldworks)
Write-Output ""

# -- W1.1: FeatureFillPattern e la strada nuova ---------------------------------
Write-Output "=== W1.1  FeatureFillPattern e' davvero Obsolete? ==="
$ffp = Metodo "IFeatureManager" "FeatureFillPattern"
Riga "IFeatureManager.FeatureFillPattern" $(if ($ffp) { "c'e'" } else { "NON c'e'" })
Riga "  marcato [Obsolete] nell'interop?" (Obsoleto $ffp)
if ($ffp) { Riga "  ritorna" $ffp.ReturnType.Name }

$cd = Metodo "IFeatureManager" "CreateDefinition"
Riga "IFeatureManager.CreateDefinition" $(if ($cd) { "c'e', ritorna " + $cd.ReturnType.Name } else { "NON c'e'" })
$cf = Metodo "IFeatureManager" "CreateFeature"
Riga "IFeatureManager.CreateFeature" $(if ($cf) { "c'e', ritorna " + $cf.ReturnType.Name } else { "NON c'e'" })

foreach ($n in @("IFillPatternFeatureData", "ITablePatternFeatureData")) {
    $t = Tipo $n
    Riga $n $(if ($t) { "c'e'" } else { "NON c'e'" })
}
$pa = (Tipo "ITablePatternFeatureData")
if ($pa) {
    $p = $pa.GetProperties() | Where-Object { $_.Name -eq "PointArray" } | Select-Object -First 1
    Riga "  ITablePatternFeatureData.PointArray" $(if ($p) { "c'e', " + $p.PropertyType.Name } else { "NON c'e'" })
}

# le marche che la voce cita, lette dall'enum invece che dalla pagina
# IL NOME L'AVEVO INDOVINATO, ed e' esattamente la cosa che questa sonda
# esiste per non fare: `swFeatureManagerCreateDefinitionType_e` non esiste.
# Le marche stanno in `swFeatureNameID_e`, e si trovano cercandole invece di
# ricordarsele.
$e = $cst.GetTypes() | Where-Object { $_.Name -eq "swFeatureNameID_e" } | Select-Object -First 1
if ($e) {
    foreach ($n in [Enum]::GetNames($e)) {
        if ($n -match "FillPattern|TablePattern") { Riga ("  " + $n) ([int][Enum]::Parse($e, $n)) }
    }
} else { Riga "  swFeatureNameID_e" "non trovato" }
Write-Output ""

# -- W1.2: InsertRib e come si rilegge ------------------------------------------
Write-Output "=== W1.2  InsertRib torna davvero void? ==="
$rib = Metodo "IFeatureManager" "InsertRib"
Riga "IFeatureManager.InsertRib" $(if ($rib) { "c'e'" } else { "NON c'e'" })
if ($rib) {
    Riga "  ritorna" $rib.ReturnType.Name
    Riga "  argomenti" $rib.GetParameters().Count
}
$g6 = Metodo "ISelectionMgr" "GetSelectedObject6"
Riga "ISelectionMgr.GetSelectedObject6" $(if ($g6) { "c'e', ritorna " + $g6.ReturnType.Name } else { "NON c'e'" })

$rfd = Tipo "IRibFeatureData2"
Riga "IRibFeatureData2" $(if ($rfd) { "c'e'" } else { "NON c'e'" })
$gd = Metodo "IFeature" "GetDefinition"
Riga "IFeature.GetDefinition" $(if ($gd) { "c'e', ritorna " + $gd.ReturnType.Name } else { "NON c'e'" })

# e la via "moderna" esiste per la nervatura?
#
# `-cmatch '^swFmRib'` E NON `-match "Rib"`: il confronto di PowerShell ignora
# le maiuscole, e la prima stesura rispondeva **swFmAttribute** -- perche'
# "Att-RIB-ute" contiene "rib". Una riga che deve dire si' o no aveva detto
# si' per una sillaba.
if ($e) {
    $rib2 = [Enum]::GetNames($e) | Where-Object { $_ -cmatch '^swFmRib' }
    Riga "CreateDefinition ha una marca per la nervatura?" $(if ($rib2) { $rib2 -join ", " } else { "NO -- nessun swFmRib*" })
}
Write-Output ""

# ── LE PRETESE ──────────────────────────────────────────────────────────────
#
# Solo i fatti su cui una voce del TODO poggia una decisione. Non si pretende
# tutto: un elenco che pretende tutto diventa rosso a ogni aggiornamento di
# SOLIDWORKS per ragioni che non interessano nessuno, e allora si spegne.
Write-Output "=== QUELLO CHE LE VOCI DANNO PER BUONO ==="
Pretendi "W1.1 FeatureFillPattern esiste" "True" ([bool](Metodo "IFeatureManager" "FeatureFillPattern"))
Pretendi "W1.1 ...e NON e' marcato Obsolete" "NO -- l'interop non lo marca" (Obsoleto (Metodo "IFeatureManager" "FeatureFillPattern"))
Pretendi "W1.1 CreateDefinition esiste" "True" ([bool](Metodo "IFeatureManager" "CreateDefinition"))
Pretendi "W1.2 InsertRib ritorna Void" "Void" ((Metodo "IFeatureManager" "InsertRib").ReturnType.Name)
Pretendi "W1.2 InsertRib ha 10 argomenti" "10" ((Metodo "IFeatureManager" "InsertRib").GetParameters().Count)
Pretendi "W1.2 GetSelectedObject6 esiste" "True" ([bool](Metodo "ISelectionMgr" "GetSelectedObject6"))
Pretendi "GA11 MirrorComponents3 ha 14 argomenti" "14" ((Metodo "IAssemblyDoc" "MirrorComponents3").GetParameters().Count)
Pretendi "Z-B5 ActivateDoc3 ha 4 argomenti" "4" ((Metodo "ISldWorks" "ActivateDoc3").GetParameters().Count)
Pretendi "GP18 GetFlatPatternFolder esiste" "True" ([bool](Metodo "IFeatureManager" "GetFlatPatternFolder"))

# -- I QUATTRO «DA ACCERTARE» ACCERTATI IL 24/9/2026 ---------------------------
#
# Quattro voci del TODO dicevano «non verificato» / «da accertare quale».
# Nessuna delle quattro voleva SOLIDWORKS acceso: volevano che qualcuno
# leggesse l'interop. Adesso letto, e messo qui perche' un fatto accertato
# una volta e mai piu' guardato torna «da accertare» da solo.
Write-Output ""
Write-Output "=== i quattro accertati il 24/9/2026 ==="

# GT5: il 33 era giusto, e la nota di 80r era vera.
Pretendi "GT5 il 33 e' HoleTableTemplates" "33" (ValoreCosto "swUserPreferenceStringValue_e" "swFileLocationsHoleTableTemplates")
Pretendi "GT5 GetUserPreferenceStringValue torna String" "String" ((Metodo "ISldWorks" "GetUserPreferenceStringValue").ReturnType.Name)
Pretendi "GT5 ...e vuole 1 argomento" "1" ((Metodo "ISldWorks" "GetUserPreferenceStringValue").GetParameters().Count)

# GT2: l'inserimento della distinta sta su IView, non sull'annotazione.
Pretendi "GT2 InsertWeldmentTable sta su IView" "True" ([bool](Metodo "IView" "InsertWeldmentTable"))

# GA6: perche' GetTotalTransform e non GetXform -- una ragione, non un gusto.
Pretendi "GA6 GetTotalTransform torna MathTransform" "MathTransform" ((Metodo "IComponent2" "GetTotalTransform").ReturnType.Name)
Pretendi "GA6 GetXform torna un Object nudo" "Object" ((Metodo "IComponent2" "GetXform").ReturnType.Name)
Pretendi "GA6 GetMates torna un Object nudo" "Object" ((Metodo "IComponent2" "GetMates").ReturnType.Name)

# X2: la riga della filettatura diceva tre cose e due erano sbagliate.
Pretendi "X2 InsertHelix NON sta su IFeatureManager" "False" ([bool](Metodo "IFeatureManager" "InsertHelix"))
Pretendi "X2 ...sta su IModelDoc2" "True" ([bool](Metodo "IModelDoc2" "InsertHelix"))
Pretendi "X2 ...torna Void" "Void" ((Metodo "IModelDoc2" "InsertHelix").ReturnType.Name)
Pretendi "X2 ...con 10 argomenti" "10" ((Metodo "IModelDoc2" "InsertHelix").GetParameters().Count)
Pretendi "X2 InsertCutSwept5 torna Feature, non void" "Feature" ((Metodo "IFeatureManager" "InsertCutSwept5").ReturnType.Name)
Pretendi "X2 ...e ha 22 argomenti, non 21" "22" ((Metodo "IFeatureManager" "InsertCutSwept5").GetParameters().Count)
Pretendi "X2 EndVariablePitchHelix torna Feature" "Feature" ((Metodo "IFeatureManager" "EndVariablePitchHelix").ReturnType.Name)

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

    Pretendi "S3 cosworks ha 589 tipi" "589" ($sim.GetTypes().Count)
    Pretendi "S3 ...di cui 160 interfacce" "160" (($sim.GetTypes() | Where-Object { $_.IsInterface }).Count)

    # 1-3. arrivare allo studio
    Pretendi "S3 CreateNewStudy3 ha 4 argomenti" "4" ((MetodoSim "ICWStudyManager" "CreateNewStudy3").GetParameters().Count)
    Pretendi "S3 studio statico = 0" "0" (ValoreEnum "swsAnalysisStudyType_e" "swsAnalysisStudyTypeStatic")

    # 4. il materiale, che NON sta sullo studio
    Pretendi "S3 SetLibraryMaterial sta su ICWSolidBody" "True" ([bool](MetodoSim "ICWSolidBody" "SetLibraryMaterial"))
    Pretendi "S3 ...e vuole 2 argomenti" "2" ((MetodoSim "ICWSolidBody" "SetLibraryMaterial").GetParameters().Count)
    # E NON c'e' modo di RILEGGERLO: nessun GetMaterial. E' la stessa
    # forma trovata per la trasparenza (voce Z-D4): un setter senza
    # lettore. Chi vuole sapere che materiale c'e' deve chiederlo altrove.
    Pretendi "S3 ...e non esiste nessun GetMaterial" "False" ([bool](MetodoSim "ICWSolidBody" "GetMaterial"))

    # 5. vincolo e carico
    Pretendi "S3 AddRestraint ha 4 argomenti" "4" ((MetodoSim "ICWLoadsAndRestraintsManager" "AddRestraint").GetParameters().Count)
    Pretendi "S3 incastro = 0" "0" (ValoreEnum "swsRestraintType_e" "swsRestraintTypeFixed")
    Pretendi "S3 AddForce ha 4 argomenti" "4" ((MetodoSim "ICWLoadsAndRestraintsManager" "AddForce").GetParameters().Count)
    Pretendi "S3 AddForce3 ne ha 21" "21" ((MetodoSim "ICWLoadsAndRestraintsManager" "AddForce3").GetParameters().Count)

    # 6. mesh ed esecuzione -- ED E' QUI IL RITROVAMENTO
    Pretendi "S3 CreateMesh ha 3 argomenti" "3" ((MetodoSim "ICWStudy" "CreateMesh").GetParameters().Count)
    Pretendi "S3 MeshAndRun torna Void" "Void" ((MetodoSim "ICWStudy" "MeshAndRun").ReturnType.Name)
    Pretendi "S3 RunAnalysis torna Int32" "Int32" ((MetodoSim "ICWStudy" "RunAnalysis").ReturnType.Name)

    # 7. la tensione
    Pretendi "S3 GetMinMaxStress ha 6 argomenti" "6" ((MetodoSim "ICWResults" "GetMinMaxStress").GetParameters().Count)
    Pretendi "S3 von Mises = 9" "9" (ValoreEnum "swsStressComponent_e" "swsStressComponentVON")
    Pretendi "S3 N/mm2 = 3" "3" (ValoreEnum "swsStrengthUnit_e" "swsStrengthUnitNewtonPerSquareMillimeter")
}
if ($Provami) {
    Pretendi "PROVA: InsertRib ha 99 argomenti" "99" ((Metodo "IFeatureManager" "InsertRib").GetParameters().Count)
}
Write-Output ""

if ($guai.Count -gt 0) {
    Write-Output ("FERMO: " + $guai.Count + " firme non sono piu' quelle che le voci danno per buone.")
    foreach ($g in $guai) { Write-Output ("  !  " + $g) }
    Write-Output "Guardare la voce del TODO che ci poggia sopra PRIMA di aggiornare il numero."
    exit 1
}
Write-Output "Tutte le firme su cui il TODO poggia una decisione sono quelle di sempre."
Write-Output ""

Write-Output "Un assembly non invecchia come una pagina: quello che dice qui e'"
Write-Output "quello contro cui il codice gira su QUESTA postazione."
