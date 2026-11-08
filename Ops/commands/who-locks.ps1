# who-locks.ps1 - which processes of THIS machine hold some files open (Restart Manager).
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) ported from the maintainer's lock script 1.0.0 (31/10/2026).
#
# Born with a bench stopped on a weld profile on the network share: "in use by another process". The
# Restart Manager sees only local processes: when it finds none, the file is held by ANOTHER machine.
# Ask it before deducing anything: a SLDWORKS stuck in shutdown, with 0 threads and 0 MB in the task
# manager, still held three DLLs (measured); such a process is released only by a reboot.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Ops\commands\who-locks.ps1 -Path "<file>[,<file>...]"
#
# Exit codes: 0 no local process holds them, 1 at least one does, 2 Restart Manager error.
param([Parameter(Mandatory)] [string] $Path)
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib\Common.ps1')
$files = @($Path -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class OpsLocks
{
    [StructLayout(LayoutKind.Sequential)]
    struct RM_UNIQUE_PROCESS { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
        public int ApplicationType; public uint AppStatus; public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmStartSession(out uint h, int f, string key);
    [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint h);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmRegisterResources(uint h, uint n, string[] files, uint np, RM_UNIQUE_PROCESS[] p, uint ns, string[] s);
    [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint h, out uint needed, ref uint n, [In, Out] RM_PROCESS_INFO[] info, ref uint reasons);

    public static string[] Who(string[] files)
    {
        uint h; var res = new List<string>();
        int rc = RmStartSession(out h, 0, Guid.NewGuid().ToString());
        if (rc != 0) return new[] { "ERROR RmStartSession " + rc };
        try
        {
            rc = RmRegisterResources(h, (uint)files.Length, files, 0, null, 0, null);
            if (rc != 0) return new[] { "ERROR RmRegisterResources " + rc };
            uint needed = 0, n = 0, reasons = 0;
            rc = RmGetList(h, out needed, ref n, null, ref reasons);
            if (rc == 234)
            {
                var info = new RM_PROCESS_INFO[needed]; n = needed;
                rc = RmGetList(h, out needed, ref n, info, ref reasons);
                if (rc != 0) return new[] { "ERROR RmGetList " + rc };
                for (int i = 0; i < n; i++) res.Add(info[i].Process.dwProcessId + " " + info[i].strAppName);
            }
            else if (rc != 0) return new[] { "ERROR RmGetList " + rc };
        }
        finally { RmEndSession(h); }
        return res.ToArray();
    }
}
'@
$who = @([OpsLocks]::Who($files))
if ($who.Count -gt 0 -and $who[0].StartsWith('ERROR')) { Stop-Ops 2 $who[0] }
if ($who.Count -eq 0) { Write-Output 'no process of this machine holds the files open (if they are in use, another machine holds them)'; exit 0 }
$who | ForEach-Object { Write-Output "holds open: $_" }
exit 1
