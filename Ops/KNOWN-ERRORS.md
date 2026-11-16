# Known errors

Version 1.1.0 (16/11/2026: [value-starting-with-a-dash]). Every entry here cost at least one run. Each says the symptom,
the cause, how to recognise it in a minute, and the remedy. Look here before guessing a
cause; add an entry when a new one is paid for. The names in brackets in
[README.md](README.md) are the headings below.

Dates are when the cause was measured. "Not tested" marks what is still a hypothesis.

## Bench

### bench-in-pipeline

- **Symptom:** the bench dies halfway, exit 255, no report in `Bench/report/`.
- **Cause:** `Select-Object -First N` stops the command upstream as soon as it has N objects;
  `Tee-Object` on a long run times out a semaphore. Measured: a full run died at 8469 s with
  644 checks passed and 0 failed.
- **Recognise:** no new file in `Bench/report/`. A real run always writes one.
- **Remedy:** never put the bench in a pipeline. `Ops/commands/bench-run.ps1` redirects to files.

### bench-detached-launcher-closed

- **Symptom:** the state file has neither "end round" nor "closed", but the bench finished.
- **Cause:** a launcher started with a minimized window was closed from the taskbar by the
  person connected over remote desktop (confirmed, 02/11/2026).
- **Remedy:** start hidden, never minimized: `bench-run -Detach` does.

### tool-call-kills-the-run

- **Symptom:** a bench "started" by an agent never writes its log, or dies at one hour.
- **Cause:** a PowerShell `Start-Job` dies with the tool call that started it; a background
  task of the agent's tool is killed at one hour; a full bench lasts 60 to 70 minutes.
- **Remedy:** `bench-run -Detach` (a separate hidden process), then `bench-status -Wait`.

### bench-watched

- **Symptom:** the agent's context fills up while the bench runs.
- **Cause:** reading the run journal every few minutes; it does not make the bench faster.
- **Remedy:** wait once with `bench-status -Wait` (55 minutes), re-arm if it exits 3. Meanwhile
  work on something that touches neither SOLIDWORKS nor the build (documents, contracts
  written but not built, notes). "Over" means the state file ends with "closed": a shell loop
  that slept once said "finished" with the bench still running (27/10/2026).

### bench-and-build

- **Symptom:** a run mixes old and new binaries; a fix "tried three times" was never in the binary.
- **Cause:** the bench executable loads the library next to it; building during a run replaces it.
  The bridge and the bench load DIFFERENT copies of the library: the bridge the one in `bin/`
  (put there by `build.ps1`), the bench the one its own build puts next to it.
- **Remedy:** never build while a bench runs; run the bench through `Bench/bench.ps1` (it builds
  first), never its executable alone; to try a change through the bridge, run `build.ps1`.

### weld-profiles-missing

- **Symptom:** the bench stops within a minute, exit 2, "OCE_WELDMENT_PROFILES is not set".
- **Cause:** the variable is not set for the user nor the system, by design.
- **Remedy:** `WeldmentProfiles` in the local configuration; `bench-run` sets it for the bench
  process only. Never `setx`.

### stale-shell-variable

- **Symptom:** every call of a measurement answers "The bridge is not attached to SolidWorks."
  in 1 to 20 ms.
- **Cause:** `OCE_NO_ATTACH=1` left in the shell by a previous contracts run (27/10/2026).
- **Remedy:** the commands of `Ops/` set variables in their own process only; `preflight` warns
  when the shell has it; `bench-run` removes it before the bench. An answer in a few ms is the sign.

### value-starting-with-a-dash

- **Symptom:** `ops.ps1 bench-run -Detach -Extra "-Group part-fixes-131,cases -Case x"` stops
  at once with `STOP (exit 2): no bench in part-fixes-131,cases`; the same arguments given to
  `Ops\commands\bench-run.ps1` directly work (16/11/2026).
- **Cause:** `ops.ps1` 1.0.1 started the command with `& powershell ... -File $script @Rest`.
  Windows PowerShell 5.1 hands an element that starts with `-` to a program as it is, without
  quotes even when it holds blanks. Measured: the child's command line was
  `-Extra -Group part-fixes-131,cases -Case x`, so `-Extra` got `-Group` and `-Root` (the first
  positional parameter) got `part-fixes-131,cases`; `-Case` and `x` went unbound. The relaunch
  of `bench-run -Detach` quoted by hand and broke on a value ending with a backslash
  (`-Root "C:\repo\"`) and on a value that is a parameter name (`-Extra:-DryRun`).
- **Recognise:** `bench-run -Detach -DryRun` (1.1.0 and later) prints the hidden command line
  and runs it in the foreground: the "would run" line must show the whole `-Extra`.
- **Remedy:** fixed in `ops.ps1` 1.0.2 and `bench-run.ps1` 1.1.0: every command line is built
  with `Join-OpsArguments` (`Ops/lib/Common.ps1`), which quotes with the rules of Windows. Never
  build a command line for a program by splatting or by quoting by hand.
- **Limit that stays:** a value that is exactly the name of a parameter of the command
  (`-Extra "-DryRun"` for `bench-run`) is read as that parameter by `powershell.exe -File`
  itself. Directly, write `-Extra:-DryRun`; through `ops.ps1` it cannot be passed, because the
  first `powershell.exe` splits `-Extra:-DryRun` into two arguments before `ops.ps1` runs. No
  argument of `Bench/bench.ps1` has the name of a parameter of `bench-run` (16/11/2026).

### partial-report

- **Symptom:** "the bench is green", and the CI says "it is not a partial run".
- **Cause:** the committed `Bench/report/last.json` was a partial run (6 cases, 66 checks, 0
  red) that hid three red cases of the full run (31/10/2026).
- **Remedy:** green = `summary` AND `cases`, `family`, `batch` empty, plus the last line of
  `Bench/history.txt` without "PARTIAL RUN". Never commit a partial report for a release.
  `deploy` refuses a partial one (gate G2).

### leftover-documents

- **Symptom:** the bench exits 3; or a confirmation contract times out with SOLIDWORKS open.
- **Cause:** test documents left open by probes or runs. With a part active the bridge really
  runs the plan and goes past the timeout of the contract.
- **Remedy:** `close-leftovers -Close` before the contracts with SOLIDWORKS open and after a
  series of probes.

### close-leftovers-with-the-bench

- **Symptom:** "closing two leftovers" ran almost a full bench (01/10/2026).
- **Cause:** the bench executable's option to close leftovers starts the whole case group first.
- **Remedy:** `close-leftovers` (it calls `Checks/CloseLeftovers`, which only closes).

### temp-cleanup-deletes-copies

- **Symptom:** in the middle of a run every new assembly is null; 26 bench errors (03/11/2026).
- **Cause:** a scheduled task of the machine deletes from `%TEMP%` what has not changed for 3
  days; `File.Copy` keeps the date of the original, so a fresh copy of a template looked
  abandoned. Cured in the bench in 0.170.14 (the local copy carries the time of now).
- **Recognise:** files vanishing from `%TEMP%` at the time of the task; its log.
- **Remedy:** a copy kept in `%TEMP%` must get the time of now (`File.SetLastWriteTime`) or live
  outside `%TEMP%`. Set `TempCleanupTask` so that `preflight` shows the next run. The task belongs
  to the owner: never change it.

### weldment-null-intermittent

- **Symptom:** `InsertStructuralWeldment5` returns null on a valid path, only in long runs
  (2 full runs out of 7 by 03/11/2026, 0 in 34 short ones).
- **Cause:** **not found yet.** Excluded by measurement: the drag guard, the groups before it,
  a 3D sketch left open. The only regularity: in the two runs with the null, the sketch was not
  removed ("stayed in the part").
- **Remedy:** none yet. At the next null, read in the bench log the lines the library writes
  since 0.198.12 (duration, open sketch, document in front, GDI and USER objects) before any
  new hypothesis.

## SOLIDWORKS

### solidworks-short-of-resources

- **Symptom:** new reds of "rebuild": a dimension changed and read back right, the volume the
  same; holes that did not move; cases that must fail that succeed (01/10/2026, 9 reds).
- **Cause:** a session open for days (about 4,900 documents created). Short of window
  resources SOLIDWORKS skips rebuilds and graphics silently; at closing it warned that "window
  resources are very low". After a restart: 314 checks, 0 red.
- **Remedy:** restart SOLIDWORKS before a full bench when it has been up for a day
  (`preflight` warns). Reds that the diff does not explain: rerun the family after a restart
  before looking at the code.

### share-hiccup

- **Symptom:** the bench "hangs" on a new part: `NewDocument` answers null, then never comes
  back; exit 4 (three documents not born in a row) or a stall.
- **Cause:** the default template lives on a network share; every new part of the bench is a
  read over the network (about 800 per run). When the share hiccups the read hangs. Ping says
  nothing: once ping was 0 ms while the template did not answer within 20 s.
- **Recognise:** `Get-Process SLDWORKS` twice 20 s apart, not responding AND no CPU = hung, not
  busy; `modals` shows no dialog; `preflight` tests the path with a 20 s timeout.
- **Remedy:** `bench-run -LocalCopies` (templates copied locally). If hung: kill SLDWORKS; if the
  lock stays on the server (sessions of killed processes), close the open file on the server,
  remap the share, and as a last resort reboot the PC. `who-locks` finding no local process
  means another machine holds the file.

### call-does-not-come-back

- **Symptom:** bench exit 5, "THE CALL DOES NOT COME BACK: <operation> started ..." (`Shared/Leash.cs`).
- **Cause:** one of: a modal dialog [api-modal], [addin-401-dialogs]; the share [share-hiccup];
  a SOLIDWORKS call that never returns.
- **Remedy:** `bench-run` writes a minidump of every live SLDWORKS before anything else
  (`_hung_<pid>.dmp`); read its stacks with cdb (`analyze-crashes` reads the CXP dumps; a hung
  dump is opened the same way). Then follow section 12 of [README.md](README.md).

### api-modal

- **Symptom:** a COM call that neither returns nor fails.
- **Cause:** some API calls open a modal window and wait: `AddDimension` opens "Modify" to ask
  for the value; an export may ask a question.
- **Recognise:** a window of class `#32770` (`modals`); there is no other sign, in no log.
- **Remedy:** in code, switch off the user preference that opens the window (for dimensions
  `swInputDimValOnCreate`) and put it back as it was in every case. To unblock without a mouse:
  `WM_CLOSE` to that window.

### addin-401-dialogs

- **Symptom:** the bench stops on a save (exit 5 on `save_document`) after hundreds of green
  checks (26/09/2026, 623).
- **Cause:** the add-in loaded in SOLIDWORKS had an expired token and opened three modal
  message boxes at every save.
- **Remedy:** the owner signs in again, or SOLIDWORKS restarted with an add-in that does not
  open them. Meanwhile `guard-401 -Detach` closes ONLY dialogs whose title contains `ModalTitle`
  and whose text contains `ModalText`, and logs each one.

### addin-stays-off

- **Symptom:** after a restart without the add-in, the add-in no longer starts at all.
- **Cause:** SOLIDWORKS writes on exit whether the add-in was loaded
  (`HKCU\Software\SolidWorks\AddInsStartup\<CLSID>`): unloading it for one session switched it
  off for every later start (30/10/2026).
- **Remedy:** `restart-solidworks -AddIn off` for the test, then `restart-solidworks -AddIn on`:
  the value is written after the close and before the start.

### slduiu-access-violation

- **Symptom:** in the Application log, error 1000 of `SLDWORKS.exe` in `slduiu.dll` at offset
  `0x94d513`; sometimes the bench then exits 5.
- **Cause:** a program outside SOLIDWORKS released a reference to a co-edge taken from a drawing
  view AFTER the drawing was closed (03/11/2026: after the close 3 of 3, before 0 of 3). The
  bench closed drawings before releasing; cured in 0.170.13 (release before closing; 0 of 4
  runs after, 4 of 4 before).
- **Remedy:** a new entry means somebody closes a document while objects of its views are still
  alive: release before `CloseDoc`. `events` lists the entries.

### sketch-picture-crash

- **Symptom:** SOLIDWORKS falls while drawings are created and closed; stacks with
  `removeOleBitmapNode` (`analyze-crashes` flags it).
- **Cause:** a logo stored as a sketch picture in the sheet format of the drawing template
  (30/10/2026: with the picture 4 falls in 4 runs, without it 0 in 4, also without the bridge).
- **Remedy:** a decision of the owner (a logo drawn with lines). Test a new title block over many
  alternated runs before using it.

### explode-crash

- **Symptom:** RPC server unavailable, then SOLIDWORKS dead.
- **Cause:** writing `IExplodeStep.ExplodeDistance` from outside the process (26/09/2026).
- **Remedy:** never write properties of explode steps from outside. A probe that writes on
  little explored assembly objects runs only on a session with no work of the owner open, or
  after asking.

### stuck-process-holds-files

- **Symptom:** "SOLIDWORKS is open" for an installer, but it is invisible in the task manager
  (0 threads, 0 MB); `taskkill /F` says access denied.
- **Cause:** a process stuck in shutdown still maps its DLLs.
- **Remedy:** ask Windows who holds the file (`who-locks`), never deduce it from the thread
  count. Such a process is released only by a reboot.

### reboot-first

- **Symptom:** SOLIDWORKS, the bridges and the bench all "closed for no reason".
- **Cause:** the PC restarted (once a blue screen nobody had looked at, 02/11/2026).
- **Remedy:** `events` first: "last boot" and the System events 41, 1001, 6008.

### disconnected-session-timings

- **Symptom:** the same drawing dimension takes about 30 s instead of about 11 s.
- **Cause:** **not tested.** It coincided with the remote desktop session disconnected (27 of 28
  rounds slow after, 35 of 36 fast before, 02/11/2026); the graphics driver is another hypothesis.
- **Remedy:** before an A/B of timings read the session state (`preflight` prints it) and never
  compare rounds across a change of state.

### active-document

- **Symptom:** a plan writes on, or refuses because of, a document it did not open.
- **Cause:** several sessions (people, add-ins, other agents) share one SOLIDWORKS; the active
  document can change from outside during a plan.
- **Remedy:** keep a reference to your own document, never rely on the active one. The bridge
  stops when the document in front changes ("I touched nothing").

## Bridge and tools

### stdin-bom

- **Symptom:** the bridge answers `-32700 Invalid JSON` to a request sent from PowerShell.
- **Cause:** from PowerShell 5.1 a redirected StandardInput writes the BOM of the console
  encoding before anything else; writing bytes to the base stream is not enough (27/10/2026).
- **Remedy:** write the requests to a file in UTF-8 without BOM and run `cmd /c "exe < file"`:
  `smoke-bridge` does exactly that.

### bin-log-overwritten

- **Symptom:** after a deploy the log of questions without answer of the production bridge is
  replaced by the 878 lines of the test runs.
- **Cause:** `questions-without-answer.jsonl` in the bin folder is a log the bridge WRITES while
  it works; the build folder has its own.
- **Remedy:** `deploy` never copies `*.jsonl` (`-Exclude`).

### running-bridges-keep-the-old-version

- **Symptom:** "the bridge was updated" and a session still behaves as before.
- **Cause:** an MCP client starts the bridge when its session starts; a deploy does not restart it.
- **Remedy:** say it in every deploy report; the session must be restarted. `preflight` and
  `deploy` count the bridges running from the target.

### network-drive-paths

- **Symptom:** a tool green in a clone on a local disk fails on the checkout on a mapped network drive.
- **Cause:** `git rev-parse --show-toplevel` answers with the UNC path, never equal to the drive
  letter; `Assembly.LoadFrom` refuses an assembly on a network drive.
- **Remedy:** recognise the root with `git rev-parse --show-prefix` empty, never by comparing
  paths; load assemblies from a local copy. Never write server names or addresses in the
  repository (the publishability check refuses them).

### packaged-appdata

- **Symptom:** a file written by one process "does not exist" for another.
- **Cause:** a process started from a packaged app (MSIX: the Claude desktop app, the Python of
  the Store) writes `%LOCALAPPDATA%` into a private folder under `Packages\<package>\LocalCache`.
- **Remedy:** files shared between processes of different hosts live outside AppData, or are
  named by an explicit environment variable.

## Repository

### check-before-the-bench

- **Symptom:** after a full green bench the push is stopped by the hook (zero-or-unknown, or a
  word of Italian in an intermediate commit); fixing it costs another full bench.
- **Cause:** the fix changes a file after the run, and the report names the commit before it.
- **Remedy:** run the hook's checks and the language gate per commit BEFORE the final bench.

### ci-not-looked-at

- **Symptom:** the CI was red for five pushes in a row and nobody knew (25/09/2026).
- **Remedy:** `gh run list --limit 3` after every push; on a red run `gh run view <id> --log-failed`.
  A check that cannot run in the CI says "not here", it does not turn red.

### hook-self-test-and-git-dir

- **Symptom:** after a self-test of the hook the repository says `core.bare=true` and the index is empty.
- **Cause:** a `git init` inside a test inherited `GIT_DIR` from the hook environment and wrote
  into the real repository (30/10/2026).
- **Remedy:** any test that creates a repository clears `GIT_DIR`, `GIT_WORK_TREE` and
  `GIT_INDEX_FILE` first.

### language-gate-new-files

- **Symptom:** a new file with Italian passes the language gate.
- **Cause:** the detector reads `git ls-files`; a file not yet added is not seen.
- **Remedy:** judge with a temporary index and `git add -N` (the command of section 1 of
  [README.md](README.md), and `contracts`).
