# Ops\ops.config.example.psd1 - the configuration of the operations toolkit, with NO machine data.
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) first version.
#
# Copy it to Ops\ops.config.local.psd1 (git ignores that name) and fill in the values of the machine,
# or point $env:OCE_OPS_CONFIG at a file of yours. Every key is optional; a command that needs an
# empty one stops with exit 2 and names it. An unknown key is an error.
# %VARIABLES% are expanded. The repository is public: real paths, user names and addresses go in the
# local file only (Checks\check-publishability.ps1 refuses them in tracked files).
@{
    # The bin folder the MCP clients start the bridge from (deploy, smoke-bridge, preflight).
    DeployTarget         = ''      # e.g. 'D:\Tools\OceSolidAgent\bin'

    # Where the toolkit writes logs, states and records. Empty = Ops\out (ignored by git).
    OutDir               = ''

    # Folder of the weld profile library (.SLDLFP). The bench exits 2 without it.
    WeldmentProfiles     = ''      # e.g. 'D:\SolidWorksData\Weldments\Profiles'

    # bench-run -LocalCopies: network folder of templates and libraries, local copy, sub-folders.
    LocalCopiesShare     = ''
    LocalCopiesRoot      = ''      # empty = %LOCALAPPDATA%\oce-bench-copies
    LocalCopiesFolders   = @()     # e.g. @('Templates', 'Sheet formats', 'Weldments\Profiles')

    # open-documents -CloseAllDiscarding refuses if a saved document is outside this folder.
    DocumentsAllowedRoot = ''      # e.g. 'D:\Work\TestPieces\'
    # Extra folders whose documents close-leftovers may close (never saved ones always may).
    LeftoverFolders      = @()

    SolidWorksExe        = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe'
    SolidWorksInterop    = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll'
    SolidWorksYearFolder = 'SOLIDWORKS 2021'

    # An add-in loaded in SOLIDWORKS: CLSID (restart-solidworks -AddIn on|off) and its log file,
    # copied after every bench round because it is rewritten at every start of SOLIDWORKS.
    AddInClsid           = ''
    AddInLog             = ''

    # analyze-crashes: cdb.exe of the Debugging Tools for Windows, and the symbol path.
    Cdb                  = ''
    SymbolPath           = 'srv*%LOCALAPPDATA%\oce-symbols*https://msdl.microsoft.com/download/symbols'

    # guard-401: a dialog is closed only when its title contains ModalTitle AND it contains ModalText.
    ModalTitle           = 'OpenPDM'
    ModalText            = 'HTTP 401'

    # Bench watchdog: the run journal unchanged this many minutes = stalled.
    StallMinutes         = 25

    # A scheduled task that empties %TEMP% (preflight warns about it while a bench may run).
    TempCleanupTask      = ''
}
