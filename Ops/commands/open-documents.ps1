# open-documents.ps1 - the documents open in SOLIDWORKS, with path, visibility and unsaved changes.
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) ported from the maintainer's open-documents script 1.1.0 (27/10/2026).
#         -CloseAllDiscarding closes EVERYTHING without saving, and only if every saved document
#         lies under -Allowed (the folder of the test pieces): one document outside it and nothing
#         is closed. A document never saved (empty path) counts as a test piece.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\open-documents.ps1
#   ... open-documents.ps1 -CloseAllDiscarding [-Allowed <folder>] [-DryRun]
#
# Output: one line per document, tab separated: UNSAVED|saved, visible|hidden, title, path.
#
# Exit codes:
#   0  listed (and closed, with -CloseAllDiscarding)
#   2  configuration error, or SOLIDWORKS cannot be attached to
#   3  refused: a document lies outside -Allowed (nothing closed)
param([switch] $CloseAllDiscarding, [string] $Allowed = '', [switch] $DryRun)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$cfg = Get-OpsConfig
if (-not (Get-Process SLDWORKS -ErrorAction SilentlyContinue)) { Stop-Ops 2 'SOLIDWORKS is not running' }
Add-OpsSolidWorks $cfg
if (-not [OpsSw]::Attachable()) { Stop-Ops 2 'SOLIDWORKS cannot be attached to' }
$rows = @([OpsSw]::Documents())
Write-Output "open documents: $($rows.Count)"
$rows
if (-not $CloseAllDiscarding) { exit 0 }

$Allowed = Resolve-OpsValue $Allowed $cfg 'DocumentsAllowedRoot' -Required
$outside = @($rows | Where-Object { $path = ($_ -split "`t")[3]; $path -and -not $path.StartsWith($Allowed, [StringComparison]::OrdinalIgnoreCase) })
if ($outside.Count -gt 0) {
    Write-Output "outside ${Allowed}:"
    $outside
    Stop-Ops 3 "$($outside.Count) documents are outside $Allowed : nothing closed"
}
if ($DryRun) { Write-Ops "would call CloseAllDocuments(true) on $($rows.Count) documents"; exit 0 }
Write-Ops ("CloseAllDocuments(true): {0}" -f [OpsSw]::CloseAll())
Write-Ops ("still open: {0}" -f @([OpsSw]::Documents()).Count)
exit 0
