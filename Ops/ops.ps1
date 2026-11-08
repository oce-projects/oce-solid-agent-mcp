# =============================================================================
#  ops.ps1 - one entry point for the operations of this repository
# =============================================================================
#
#  Version 1.0.1
#  Changelog:
#    1.0.1 (08/11/2026) a command with exactly ONE argument works: "repo-map -Check"
#          reached the command as "- C h e c k" and failed with an invalid "name"
#          argument. An if expression unrolls a one-element array into a plain
#          string, and splatting a string passes one argument per character.
#    1.0.0 (08/11/2026) first version.
#
#  Every command is a script in Ops\commands and runs in a PowerShell process of
#  its own. That is on purpose: a command that sets OCE_NO_ATTACH or
#  OCE_WELDMENT_PROFILES must not leave it behind in the caller's shell (a
#  variable left behind once made a whole series of measurements answer "not
#  attached" in a few milliseconds).
#
#      powershell -NoProfile -ExecutionPolicy Bypass -File Ops\ops.ps1 help
#      powershell -NoProfile -ExecutionPolicy Bypass -File Ops\ops.ps1 <command> [arguments]
#
#  The exit code is the one of the command. Read Ops\README.md first.
# =============================================================================
# No param block on purpose: with one, PowerShell would try to bind "-DryRun" or
# "-Reason" to this script and refuse them. $args passes them through untouched.
$Command = if ($args.Count -gt 0) { "$($args[0])" } else { 'help' }
# @( ... ) OUTSIDE the if: inside it, one argument would come out as a string, and
# "@Rest" would then pass it one character at a time.
$Rest = @(if ($args.Count -gt 1) { $args[1..($args.Count - 1)] })
$commands = Join-Path $PSScriptRoot 'commands'
if ($Command -in 'help', '-h', '--help', '/?') {
    Write-Output 'commands (Ops\commands\<command>.ps1):'
    foreach ($f in Get-ChildItem -LiteralPath $commands -Filter '*.ps1' | Sort-Object Name) {
        $first = (Get-Content -LiteralPath $f.FullName -TotalCount 1) -replace '^#\s*', ''
        $what = ($first -split ' - ', 2)[-1]
        Write-Output ('  {0,-20} {1}' -f $f.BaseName, $what)
    }
    Write-Output 'details: the header of each script, and Ops\README.md'
    exit 0
}
$script = Join-Path $commands ($Command + '.ps1')
if (-not (Test-Path -LiteralPath $script)) { Write-Output "unknown command: $Command (Ops\ops.ps1 help)"; exit 2 }
& powershell -NoProfile -ExecutionPolicy Bypass -File $script @Rest
exit $LASTEXITCODE
