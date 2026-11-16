# =============================================================================
#  Common.ps1 - shared helpers of the operations toolkit (Ops\)
# =============================================================================
#
#  Version 1.1.0
#  Changelog:
#    1.1.0 (16/11/2026) ConvertTo-OpsArgument, Join-OpsArguments and
#                       Invoke-OpsPowerShell: a command line built with the
#                       quoting rules of Windows, for ops.ps1 and for the
#                       relaunch of bench-run -Detach (a value that starts
#                       with "-" was split into pieces).
#    1.0.0 (08/11/2026) first version: configuration loader, exit helper,
#                       timestamped output, MSBuild lookup, path test with a
#                       timeout, typed SOLIDWORKS interop, window enumeration.
#
#  Dot-sourced by every script in Ops\commands. It does nothing by itself.
#
#  PURE ASCII ON PURPOSE: PowerShell 5.1 reads a script without a BOM as ANSI,
#  and a non-ASCII character in a message comes out garbled.
#
#  CONFIGURATION. Nothing that belongs to one machine is written in this
#  repository (the repository is public: see Checks\check-publishability.ps1).
#  Machine values live in a local file that git ignores:
#
#      Ops\ops.config.local.psd1          (copy Ops\ops.config.example.psd1)
#      or the file named by $env:OCE_OPS_CONFIG
#
#  Precedence: a parameter of the command > the local file > the default below.
#  Environment variables like %LOCALAPPDATA% are expanded in every string.
# =============================================================================

$script:OpsLibRoot = $PSScriptRoot
$script:OpsRoot = Split-Path -Parent $script:OpsLibRoot
$script:RepoRoot = Split-Path -Parent $script:OpsRoot

# Every key the local file may set. An unknown key is an error, not a silence:
# a misspelled key would otherwise leave the default in place without a word.
$script:OpsDefaults = [ordered]@{
    DeployTarget         = ''      # the bin folder that MCP clients start the bridge from
    OutDir               = ''      # reports and logs of the toolkit; empty = Ops\out
    WeldmentProfiles     = ''      # folder of the weld profile library (.SLDLFP): OCE_WELDMENT_PROFILES
    LocalCopiesShare     = ''      # network folder of templates and libraries the bench reads
    LocalCopiesRoot      = ''      # where the bench copies them; empty = %LOCALAPPDATA%\oce-bench-copies
    LocalCopiesFolders   = @()     # sub-folders of LocalCopiesShare to copy
    DocumentsAllowedRoot = ''      # documents saved under this folder may be closed discarding changes
    LeftoverFolders      = @()     # extra folders whose documents close-leftovers may close
    SolidWorksExe        = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe'
    SolidWorksInterop    = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll'
    SolidWorksYearFolder = 'SOLIDWORKS 2021'   # folder under %APPDATA%\SolidWorks that holds swxJRNL.swj
    AddInClsid           = ''      # CLSID of the add-in that restart-solidworks -AddIn switches on or off
    AddInLog             = ''      # log of that add-in, copied after every bench round
    Cdb                  = ''      # cdb.exe of the Debugging Tools for Windows (analyze-crashes)
    SymbolPath           = 'srv*%LOCALAPPDATA%\oce-symbols*https://msdl.microsoft.com/download/symbols'
    ModalTitle           = 'OpenPDM'    # guard-401: title of the dialogs it may close
    ModalText            = 'HTTP 401'   # guard-401: text that must also be in the dialog
    StallMinutes         = 25      # bench watchdog: journal unchanged this long = stalled
    TempCleanupTask      = ''      # name of a scheduled task that empties %TEMP% (preflight reports it)
}

function Get-OpsConfig {
    $cfg = [ordered]@{}
    foreach ($k in $script:OpsDefaults.Keys) { $cfg[$k] = $script:OpsDefaults[$k] }
    $file = if ($env:OCE_OPS_CONFIG) { $env:OCE_OPS_CONFIG } else { Join-Path $script:OpsRoot 'ops.config.local.psd1' }
    $cfg['ConfigFile'] = ''
    if (Test-Path -LiteralPath $file) {
        $data = Import-PowerShellDataFile -LiteralPath $file
        foreach ($k in $data.Keys) {
            if (-not $script:OpsDefaults.Contains($k)) {
                Stop-Ops 2 "unknown key '$k' in $file. Known keys: $(@($script:OpsDefaults.Keys) -join ', ')"
            }
            $cfg[$k] = $data[$k]
        }
        $cfg['ConfigFile'] = $file
    }
    foreach ($k in @($cfg.Keys)) {
        $v = $cfg[$k]
        if ($v -is [string]) { $cfg[$k] = [Environment]::ExpandEnvironmentVariables($v) }
        elseif ($v -is [array]) { $cfg[$k] = @($v | ForEach-Object { if ($_ -is [string]) { [Environment]::ExpandEnvironmentVariables($_) } else { $_ } }) }
    }
    if (-not $cfg.OutDir) { $cfg.OutDir = Join-Path $script:OpsRoot 'out' }
    if (-not $cfg.LocalCopiesRoot) { $cfg.LocalCopiesRoot = Join-Path $env:LOCALAPPDATA 'oce-bench-copies' }
    $cfg
}

# A parameter wins; then the configuration; an empty required value stops with
# the name of the key to set, so that the next person knows what to write.
function Resolve-OpsValue($given, $cfg, [string]$key, [switch]$Required) {
    if ($given -ne $null -and "$given" -ne '') { return $given }
    $v = $cfg[$key]
    if ($Required -and ($v -eq $null -or "$v" -eq '')) {
        Stop-Ops 2 "no value for ${key}: pass it as a parameter or set $key in Ops\ops.config.local.psd1 (see Ops\ops.config.example.psd1)"
    }
    $v
}

# Both write straight to the standard output of the process, NOT to the PowerShell
# pipeline: a message written with Write-Output inside a function that returns a
# value becomes part of the value (and a STOP message inside `$x = f` is lost when
# `exit` follows). The process output is what a caller reads anyway.
function Stop-Ops([int]$Code, [string]$Message) {
    [Console]::Out.WriteLine(("STOP (exit {0}): {1}" -f $Code, $Message))
    exit $Code
}

function Write-Ops([string]$Text) {
    [Console]::Out.WriteLine(("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Text))
}

function New-OpsOutDir($cfg) {
    if (-not (Test-Path -LiteralPath $cfg.OutDir)) { New-Item -ItemType Directory -Path $cfg.OutDir -Force | Out-Null }
    $cfg.OutDir
}

function Get-OpsStamp { Get-Date -Format 'yyyyMMdd_HHmmss' }

# Runs git in the repository and returns its output; never throws on stderr.
function Invoke-OpsGit([string[]]$Arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & git.exe -C $script:RepoRoot @Arguments 2>$null } finally { $ErrorActionPreference = $old }
}

function Get-OpsHead { (Invoke-OpsGit @('rev-parse', '--short=7', 'HEAD')) }

function Get-OpsVersion {
    $f = Join-Path $script:RepoRoot 'VERSION.txt'
    if (Test-Path -LiteralPath $f) { (Get-Content -LiteralPath $f -TotalCount 1).Trim() } else { '' }
}

# ONE argument written for a Windows command line, so that the program started
# reads it back as exactly one argument with exactly this text. The rules are
# the ones of CommandLineToArgvW, which powershell.exe and every .NET program
# follow: quotes around a value that is empty or holds a blank or a quote, a
# quote inside written \", and the backslashes just before a quote or before
# the closing quote doubled.
#
# Why a command line is never left to "& powershell ... @list" or "$list": Windows
# PowerShell 5.1 hands a program an element that starts with "-" as it is, with
# no quotes even when it holds blanks, and never escapes a quote inside an
# element. Measured 16/11/2026 (ops.ps1 1.0.1): the elements "-Extra" and
# "-Group a,b -Case c" reached bench-run.ps1 as the command line
# "-Extra -Group a,b -Case c": -Extra got "-Group", the first positional
# parameter (-Root) got "a,b", and the run stopped with "no bench in a,b".
function ConvertTo-OpsArgument([string]$Value) {
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    $v = $Value -replace '(\\*)"', '$1$1\"'
    $v = $v -replace '(\\+)$', '$1$1'
    '"' + $v + '"'
}

# A whole command line from a list of arguments, each one through ConvertTo-OpsArgument.
function Join-OpsArguments([string[]]$Arguments) {
    (@($Arguments) | ForEach-Object { ConvertTo-OpsArgument $_ }) -join ' '
}

# Runs Windows PowerShell (the one running this script) with the arguments given,
# in the same console or pipe, and returns its exit code. It waits for THAT
# process only. Start-Process -Wait waits for the processes it starts as well:
# measured 16/11/2026, 9.2 s against 0.6 s for a child that leaves an 8 s
# process behind, and "bench-run -Detach" leaves one behind for an hour.
function Invoke-OpsPowerShell([string[]]$Arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = Join-Path $PSHOME 'powershell.exe'
    $psi.Arguments = Join-OpsArguments $Arguments
    $psi.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::Start($psi)
    $p.WaitForExit()
    $p.ExitCode
}

# The same places Bench\bench.ps1 looks in. The one in C:\Windows\Microsoft.NET
# is NOT one of them: it cannot read these csproj (see build.ps1).
function Find-OpsMSBuild {
    $places = @(
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
    )
    foreach ($p in $places) { if (Test-Path -LiteralPath $p) { return $p } }
    $null
}

# Test-Path on a network share can hang for minutes when the share hiccups,
# and the probe hangs with it. Measured: a template on the share did not answer
# within 20 s while ping said 0 ms. The test runs in a job with a timeout.
# Returns 'yes', 'no' or 'timeout'.
function Test-OpsPathWithTimeout([string]$Path, [int]$TimeoutS = 20) {
    $job = Start-Job -ScriptBlock { param($p) Test-Path -LiteralPath $p } -ArgumentList $Path
    try {
        if (Wait-Job $job -Timeout $TimeoutS) { if (Receive-Job $job) { 'yes' } else { 'no' } }
        else { 'timeout' }
    }
    finally { Remove-Job $job -Force -ErrorAction SilentlyContinue }
}

# Typed interop, never late binding: from PowerShell a late-bound ISldWorks call
# answers TYPE_E_ELEMENTNOTFOUND (measured). Attaching reads the running
# SOLIDWORKS session through the COM running object table: it changes nothing,
# but it is the session someone may be using.
function Add-OpsSolidWorks($cfg) {
    if ('OpsSw' -as [type]) { return }
    $interop = $cfg.SolidWorksInterop
    if (-not (Test-Path -LiteralPath $interop)) { Stop-Ops 2 "SOLIDWORKS interop not found: $interop (set SolidWorksInterop)" }
    Add-Type -Path $interop
    Add-Type -ReferencedAssemblies $interop -TypeDefinition @'
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
public static class OpsSw
{
    static ISldWorks Sw() { return (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
    public static bool Attachable() { try { return Sw() != null; } catch { return false; } }
    public static string Exit() { try { Sw().ExitApp(); return "ExitApp sent"; } catch (System.Exception e) { return "ExitApp failed: " + e.Message; } }
    public static bool Ready() { try { return Sw().StartupProcessCompleted; } catch { return false; } }
    public static string Unload(string dll) { try { return "UnloadAddIn " + Sw().UnloadAddIn(dll); } catch (System.Exception e) { return "UnloadAddIn failed: " + e.Message; } }
    public static string Loaded(string clsid) { try { return Sw().GetAddInObject(clsid) != null ? "yes" : "no"; } catch (System.Exception e) { return "unknown: " + e.Message; } }
    // One row per open document: save flag, visibility, title, path (tab separated).
    public static List<string> Documents()
    {
        var rows = new List<string>();
        var d = Sw().GetFirstDocument() as IModelDoc2;
        for (int guard = 0; d != null && guard < 500; guard++)
        {
            string t = "", p = "", flag = "?", vis = "?";
            try { t = d.GetTitle(); } catch { }
            try { p = d.GetPathName(); } catch { }
            try { flag = d.GetSaveFlag() ? "UNSAVED" : "saved"; } catch { }
            try { vis = d.Visible ? "visible" : "hidden"; } catch { }
            rows.Add(flag + "\t" + vis + "\t" + t + "\t" + p);
            d = d.GetNext() as IModelDoc2;
        }
        return rows;
    }
    public static bool CloseAll() { return Sw().CloseAllDocuments(true); }
}
'@
}

# Top-level windows and the texts of their children, read with WM_GETTEXT under
# a timeout (GetWindowText does not read the controls of another process, and a
# plain SendMessage to a hung window would hang this script too).
function Add-OpsWindows {
    if ('OpsWin' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class OpsWindow
{
    public IntPtr Handle; public int ProcessId; public string Class; public string Title;
    public List<string> Texts = new List<string>();
    public List<IntPtr> Buttons = new List<IntPtr>();
    public List<string> ButtonTexts = new List<string>();
}
public static class OpsWin
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, StringBuilder l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    const uint WM_GETTEXT = 0x000D, WM_CLOSE = 0x0010, BM_CLICK = 0x00F5, SMTO_ABORTIFHUNG = 0x0002;

    static string Text(IntPtr h)
    {
        var sb = new StringBuilder(1024); IntPtr r;
        var ok = SendMessageTimeout(h, WM_GETTEXT, (IntPtr)sb.Capacity, sb, SMTO_ABORTIFHUNG, 1000, out r);
        return ok == IntPtr.Zero ? "" : sb.ToString();
    }
    static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }

    // Visible top-level windows of the given processes (all processes when the list is empty).
    public static List<OpsWindow> Top(int[] pids)
    {
        var set = new HashSet<int>(pids ?? new int[0]);
        var list = new List<OpsWindow>();
        EnumWindows((h, l) =>
        {
            if (!IsWindowVisible(h)) return true;
            int pid; GetWindowThreadProcessId(h, out pid);
            if (set.Count > 0 && !set.Contains(pid)) return true;
            var w = new OpsWindow { Handle = h, ProcessId = pid, Class = ClassOf(h), Title = Text(h) };
            EnumChildWindows(h, (c, l2) =>
            {
                var t = Text(c);
                if (ClassOf(c) == "Button") { w.Buttons.Add(c); w.ButtonTexts.Add(t); }
                else if (t.Length > 0) w.Texts.Add(t);
                return true;
            }, IntPtr.Zero);
            list.Add(w);
            return true;
        }, IntPtr.Zero);
        return list;
    }
    public static bool Click(IntPtr button) { return PostMessage(button, BM_CLICK, IntPtr.Zero, IntPtr.Zero); }
    public static bool Close(IntPtr window) { return PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
}
'@
}

# The visible dialogs (class #32770) of the SOLIDWORKS processes: a modal of
# SOLIDWORKS or of an add-in blocks a COM call as surely as a hung process, and
# it leaves no trace in any log. The class is the only sign.
function Get-OpsDialogs([switch]$AllProcesses) {
    Add-OpsWindows
    $pids = @()
    if (-not $AllProcesses) {
        $pids = @(Get-Process SLDWORKS -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
        if ($pids.Count -eq 0) { return @() }
    }
    @([OpsWin]::Top([int[]]$pids) | Where-Object { $_.Class -eq '#32770' })
}
