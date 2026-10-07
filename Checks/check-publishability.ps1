# What must not leave this machine.
#
# This repository is meant to become public. A path with the name of whoever works in it,
# or the address of a company server, is not a style defect: it is somebody
# else's stuff leaving without anyone having decided it.
#
#   .\Checks\check-publishability.ps1
#
# Exits 0 if clean, 1 if not.
#
# ------------------------------------------------------------------------------
# THE PERIMETER WAS WRONG THREE TIMES, AND THE THIRD COST ALMOST A PUSH.
# ------------------------------------------------------------------------------
#
#   1. `check-catalog.ps1` looked inside `catalog.json` and nothing else, because
#      when it was written the catalogue was the only thing that went out. Then the
#      bench went out: NINETEEN files with the path of the session scratchpad, user
#      name included. The check was there and was looking elsewhere.
#
#   2. So this one was born, which looked at the whole working tree - but only the
#      files with certain extensions. `App.config`, `app.manifest`, `LICENSE` and
#      `NOTICE` did not have a good one and were read by nobody.
#
#   3. And it only looked at the TREE. On 14/9/2026, one minute before the first push,
#      it said «Clean»: the tree had really been repaired. But `git push` sends
#      the HISTORY, and in the history the path was still there in 155 places over 41
#      commits - because blobs live in every intermediate tree, not only in the two
#      commits where `git log -S` reports a change.
#
# THE TWO LESSONS, written here so that the fourth time does not cost again:
#
#   THE PERIMETER IS NOT A LIST, IT IS WHAT GOES OUT. No longer a list of
#   extensions to keep updated by hand, but `git ls-files`: what git
#   tracks is exactly what git pushes. The extensions are now an
#   EXCLUSION list (the binaries): if tomorrow a new type of text file
#   is born, it enters the check by itself. An inclusion list fails
#   silently, an exclusion one fails noisily.
#
#   WE LOOK AT THE HISTORY TOO, and we look at the TREE of every commit, not its
#   diff. A `git log -p` would have shown two commits; the trees show
#   forty-one.
#
# ------------------------------------------------------------------------------
# TWO PRECAUTIONS THAT CANNOT BE DEDUCED, and each cost a false positive:
#
#   1. `Select-String` does NOT distinguish upper and lower case by itself. Searching
#      "O:\" it found `example:\n` inside a JSON description - that is "o" +
#      ":" + the escape of a newline. Drive letters are searched with
#      -CaseSensitive and with the rule that no letter comes before.
#   2. The files that NAME the forbidden things because their job is to search for them
#      are listed one by one. A broad rule here would be a hole: the exception
#      of the TODO only holds for the patterns that entry A1 discusses, and in fact the
#      user name was in the TODO and it was a leak like the others.
#
# NOTE: in pure ASCII, like all the .ps1 files of the project.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$trouble = 0

# EXCLUDED extensions: the binaries, which Select-String cannot read and which
# carry no text. Everything else enters, even what has no extension.
$binary = @(
    '.dll', '.exe', '.pdb', '.res', '.cache', '.suo', '.zip', '.7z', '.gz',
    '.png', '.jpg', '.jpeg', '.gif', '.ico', '.bmp', '.pdf',
    '.sldprt', '.slddrw', '.sldasm', '.sldlfp', '.sldmat', '.step', '.stp', '.igs'
)

# ==============================================================================
# THE EXCEPTIONS, ONE PER PATTERN AND NEVER ONE PER FILE
# ==============================================================================
#
# HERE STOOD `$canNameThem`, three file names applied inside `Lines()` and
# therefore valid for EVERY pattern: whoever was in that list was not checked
# at all. It looked narrow - only three names - and instead it was the widest
# of the exceptions, because it did not say WHAT it exempted from.
#
# **The bill came inside this very file.** Repairing the exception of the
# TODO I wrote the two real addresses in the comment that tells it, and they
# stayed there without anything flagging them: the check did not look at the check.
# It was not found by a probe, it was found by a census done by hand
# three hours later.
#
# Now every pattern carries ITS OWN list, and every name in a list must
# have a reason that can be read next to it:
#
#   $nameOnly        whoever must write «oscar» because it searches for it (the two
#                      checks) or because it attributes it (LICENSE, NOTICE).
#   $sessionOnly    whoever must write the scratchpad path because it
#                      replaces it.
#   $talkAboutDisks  whoever must write `O:` and `W:` because it lists them.
#   the address        NOBODY. The pattern does not name itself, so nobody
#                      needs a pass - and whoever needed one
#                      would be writing a real address.
#
# The names stay NAMES and not paths, and the reason is measured: in the history
# `check-catalog.ps1` lives in `Tools\`, because it was moved to
# `Checks\` only afterwards. An exception tied to the path would have flagged
# the check itself as a leak at every commit before the move. Files
# move, their job does not.
#
# A move keeps the name; a rename does not. Window 3 of W3.1-bis renamed these
# files into English, and the history that is pushed still carries them under
# the names they had before (the commits before the move, and the merges that
# bring them in): the same files, the same job, so each list below names a
# file by both its names. The pairing is not written down anywhere: git itself
# knows it (`git log --follow` lists every path a file has had), so the old
# names are read from the history and no list of them has to be kept.
function WithNamesOfBefore($names) {
    $all = @($names)
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    foreach ($name in @($names)) {
        $paths = @(& git.exe -C $root ls-files "*$name" 2>$null | Where-Object { (Split-Path $_ -Leaf) -eq $name })
        if ($paths.Count -eq 0) { $paths = @("Checks/$name") }
        foreach ($path in $paths) {
            foreach ($before in @(& git.exe -C $root log --all --follow --name-only --format= -- $path 2>$null)) {
                if (-not $before) { continue }
                $leaf = Split-Path $before -Leaf
                if ($leaf -and ($all -notcontains $leaf)) { $all += $leaf }
            }
        }
    }
    $ErrorActionPreference = $old
    $all
}
$twoChecks = WithNamesOfBefore @('check-catalog.ps1', 'check-publishability.ps1')

# THE EXCEPTION WAS ON THE FILE, AND IT HAD TO BE ON THE SHAPE. THREE REAL ADDRESSES
# CAME OUT FROM HERE.
#
# Here stood `$theTodoSaysSo = @('TODO.md')`, passed to the pattern `192\.168\.`
# because entry A1 lists what must not go out and to list it has to
# write it. The comment said «it holds ONLY for the patterns that entry
# discusses»; the code exempted the WHOLE FILE, and `Look` compares the name of the
# file, not the line. Between one draft and the next the TODO grew by
# sixteen thousand lines, and three REAL addresses ended up inside:
#
#   TODO.md:12107   the server address of another project
#   TODO.md:16569   the UNC path of the share behind S:
#   TODO.md:16750   the same one, another time
#
# AND THEY ARE NOT WRITTEN HERE. The first draft of this comment reported them in
# full, both of them, «for clarity» - inside the file that exists to forbid them,
# and which exempted itself. It is the short circuit that `cleanup-history.js` had
# already paid for and written at the top of itself: *a tool that removes a
# string must name it, and so the cure carries the disease.* An incident is
# told by its shape, not by its value.
#
# They had already been pushed to GitHub. They were NOT public: the repository is still
# private (verified with `gh repo view`: visibility PRIVATE), so no outsider
# could ever have read them. But they were inside the history that on the day
# of publication would have gone out all at once, and this check exists
# exactly for that day: it ran, said «ok no internal address», and
# was right according to the rule it had been given.
#
# THE REPAIR IS NOT A NARROWER EXCEPTION, it is a rule that looks at the
# thing instead of at who wrote it: a real address has a DIGIT after the third
# dot, a list of rules writes the wildcard. `192.168.*` passes everywhere, an
# address with digits is forbidden everywhere, TODO included. NO FILE HAS ANY
# MORE A PASS ON THIS PATTERN - not even this one, and the reason is down
# here, in `$nameOnly`.
#
# Note that the pattern does not name itself: `192\.168\.\d` as TEXT does not
# match `192\.168\.\d` as a RULE, because after the «192» the rule
# demands a dot and the text has a backslash. That is why this
# file can write the rule and stay under the rule.
#
# *An exception that names a file widens by itself every time the file
# grows. One that describes the shape does not.*
$TrueAddress = '192\.168\.\d'

# THE TWO FILES WHOSE JOB IS TO NAME THE AUTHOR.
#
# The user name rule went from `-CaseSensitive` to insensitive,
# because it searched for the name all in lower case and the one with the capital passed
# right under its nose:
# `Bench\NoPartTests.cs:185` carried it in a comment, and the check
# said «ok». No Italian word contains «oscar», so removing the
# sensitivity does not cost a single false positive... except these two, where the
# name is NOT a leak: it is what the file must say.
#
#   LICENSE:189   «Copyright 2026 <author>» - the line that the Apache-2.0
#                 requires, and without which the licence is not granted to
#                 anybody.
#   NOTICE:2,4    the attribution, which the Apache-2.0 requires to be redistributed.
#
# WHY THIS EXCEPTION IS NOT THE ONE THAT LET THREE ADDRESSES OUT.
# That one exempted a journal of seventeen thousand lines that grows every day: it
# widened by itself, and nobody reread what had ended up inside. These are
# two licence texts that do not grow, in which the name sits in three fixed lines
# put there on purpose. The exception holds ONLY for the name pattern: paths,
# addresses and drive letters on these two files stay forbidden as elsewhere.
$nameOnly = @('LICENSE', 'NOTICE') + $twoChecks

# THE TWO SUPPORT ADDRESSES OF THE AUTHOR, AND NOTHING ELSE (W3.1-bis.11).
#
# The README opens with two buttons the owner asked for in his own words: «Buy me a
# coffee» and «Donate via PayPal». Both addresses are the author's own pages, so both
# contain the name, and a rule that forbids the name everywhere would refuse them.
#
# THE EXCEPTION IS ON THE SHAPE, NOT ON THE FILE. It does not exempt README.md (a
# file that is exempt widens by itself: three real addresses left that way, see
# above), it exempts exactly two strings, wherever they stand:
#
#   https://buymeacoffee.com/ + the one account name, then a character that
#   cannot continue a name;
#   https://www.paypal.biz/   + the one account code, in its exact case, then a
#   character that cannot continue it.
#
# The name stays forbidden everywhere else, including on the same line as
# these addresses, and in any URL of those two hosts that differs from the two
# (another account, another case, http instead of https, a longer name). The
# exemption is a negative lookahead that lets the match through only when the
# text after the prefix is NOT the exact account, so a change of one letter
# is red. `(?-i:...)` keeps the account case-exact while the rule itself is
# case-insensitive. The same string serves the working tree (.NET regex) and the
# history (`git grep -P`): the lookbehinds are of fixed length for that reason.
$AuthorName = 'oscar'
$AuthorNameButTheTwoSupportUrls =
    '(?<!https://buymeacoffee\.com/)(?<!https://www\.paypal\.biz/)' + $AuthorName +
    '|(?<=https://buymeacoffee\.com/)(?!(?-i:oscarcontatoengineering)(?![A-Za-z0-9]))' + $AuthorName +
    '|(?<=https://www\.paypal\.biz/)(?!(?-i:OSCARCONTATO94)(?![A-Za-z0-9]))' + $AuthorName

# Whoever replaces the scratchpad path must write it to find it:
# `cleanup-history.js` (gone from the tree in W3.1-bis.10, but still in the history this check reads) had the schema, this check has the pattern.
$sessionOnly = WithNamesOfBefore @('cleanup-history.js', 'check-publishability.ps1')   # the first one only exists in the history now

# THE STRUCTURE OF ANOTHER REPOSITORY, WHICH IS PRIVATE.
#
# This bridge was born from a larger project and talks to it: naming it and
# telling where the catalogue comes from is fine, and the bare name of a file
# (`the_drawing_module.py`) is context, not a leak.
#
# But on 24/9/2026 a census by hand found written in full
# `api/<file>.py:283`, `services/<file>.py:104` and `:252` - that is FOLDERS,
# FILES and LINE NUMBERS of a private repository, which allow reconstructing how
# it is made inside without ever having seen it. They were in two places
# (`Tools\update.md` and the CHANGELOG entry that copied it), and no
# check looked at them: we searched for paths of THIS machine and addresses of THIS
# network, and it had not occurred to us that the inside of a neighbouring
# project could be published.
#
# THE SHAPE, and not the name of the project: in this repository THERE IS NOT A SINGLE
# PYTHON FILE (verified with `git ls-files '*.py'`: empty). So a `.py`
# with a FOLDER in front, or with a LINE NUMBER behind, is necessarily talking about
# somebody else's house. The bare name keeps passing.
$InsideAnotherRepo = '([A-Za-z0-9_]+/[A-Za-z0-9_/]*\.py|\.py:\d)'

# WHO TALKS ABOUT DRIVE LETTERS BECAUSE THEY ARE EXPLAINING THIS CHECK.
#
# It holds ONLY for the `O:` and `W:` patterns, and does not touch the user name, the session
# paths and the private addresses, which on these same files stay
# forbidden as on all the others.
#
#   CHANGELOG.md       the entry that tells the false positive of 14/9 must
#                      quote the pattern that caused it.
#   CatalogTests.cs   is the check that searches for local paths inside the
#                      catalogue descriptions: its job is to name them.
#
# Why the hole is acceptable here and not elsewhere: none of the three is a
# GENERATED file. A real network path gets there only if somebody writes it by
# hand, and whoever writes it by hand in a document that explains why it must not be written
# is doing it on purpose. The generated files - the catalogue, the bench cases -
# stay covered.
$talkAboutDisks = (WithNamesOfBefore @('CHANGELOG.md', 'CatalogTests.cs')) + $twoChecks

# This function CANNOT be called `Git`: PowerShell does not distinguish upper
# and lower case in command names, so `& git` in here would resolve to the
# function itself instead of the executable - infinite recursion, and the error
# that is read is «call depth overflow», which does not name the
# cause. The different name is the repair, as for `ReadWithAnchor`.
function RunGit($arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & git.exe -C $root @arguments 2>$null } finally { $ErrorActionPreference = $old }
}

# ==============================================================================
# 1. The working tree: what is about to be committed
# ==============================================================================

function FilesThatGoOut {
    # Tracked + not yet tracked but not ignored. Together they are everything
    # that a `git add -A` would bring in. Ignored files (bin, obj) are not: they do not
    # go out, and reading them doubles the time for nothing.
    $tracked = @(RunGit @('ls-files'))
    $newItems = @(RunGit @('ls-files', '--others', '--exclude-standard'))
    @($tracked + $newItems) |
        Where-Object { $_ } |
        Sort-Object -Unique |
        Where-Object { $binary -notcontains [IO.Path]::GetExtension($_).ToLower() } |
        ForEach-Object { Join-Path $root ($_ -replace '/', '\') } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
}

$script:list = $null
function Lines($pattern, $caseSensitive) {
    if ($null -eq $script:list) { $script:list = @(FilesThatGoOut) }
    if ($script:list.Count -eq 0) { return @() }
    # -LiteralPath, NOT the pipeline. An array of strings sent to
    # `Select-String` through the pipeline is taken as TEXT TO SEARCH FOR, not as a
    # list of files: the check searched for the user name inside the NAMES of the
    # paths and answered «ok none» without having opened a single file.
    # It was seen only because the pattern `S:\` matches every absolute path
    # of this drive and blew up the line below.
    if ($caseSensitive) { $foundOnes = Select-String -LiteralPath $script:list -Pattern $pattern -CaseSensitive }
    else { $foundOnes = Select-String -LiteralPath $script:list -Pattern $pattern }
    @($foundOnes)
}

function Look($itemName, $pattern, $why, $caseSensitive, $except = @()) {
    $foundItems = @(Lines $pattern $caseSensitive | Where-Object {
        -not ($except -contains (Split-Path -Leaf $_.Path))
    })
    if ($foundItems.Count -eq 0) { Write-Host "  ok  no $itemName"; return }
    Write-Host "  XX  $itemName ($why) in $($foundItems.Count) places:"
    $script:trouble++
    $seen = @{}
    foreach ($t in $foundItems) {
        $relative = $t.Path.Substring($root.Length + 1)
        if ($seen.ContainsKey($relative)) { continue }
        $seen[$relative] = $true
        Write-Host "        ${relative}:$($t.LineNumber)"
    }
    if ($seen.Count -lt $foundItems.Count) { Write-Host "        (and more lines in the same files)" }
}

Write-Host "=== In the working tree"
Look 'the user name' $AuthorNameButTheTwoSupportUrls 'the name of the person working' $false $nameOnly
Look 'a session path' 'AppData.Local.Temp' 'exists only on this machine' $false $sessionOnly
Look 'an internal address' $TrueAddress 'a private network' $false
Look 'the inside of another repository' $InsideAnotherRepo 'folders and lines of other people''s homes' $false $twoChecks
Look 'the O: drive' '(?<![A-Za-z])O:\\' 'a company network drive' $true $talkAboutDisks
Look 'the W: drive' '(?<![A-Za-z])W:\\' 'a company network drive' $true $talkAboutDisks

# ==============================================================================
# 2. The history about to be pushed
# ==============================================================================
#
# We look at the TREE of every commit to be pushed, not its diff: a blob
# introduced once lives in all the later trees until it is
# removed, and the diff shows it only twice.
#
# `git grep` searches inside a tree without extracting it, so forty-one commits
# cost seconds and not minutes.
#
# BINARY FILES ARE SKIPPED BY EXTENSION, NOT BY GIT'S GUESS (entry Z-C22). Here
# stood `git grep -I`, which skips every blob git takes for binary: a text file
# with one NUL byte inside is one. Checks/questions-without-answer.js had a real
# NUL (offset 3899) and was never looked at, in the tree or in the history. Now
# `--text` reads every blob, and the paths whose extension is in `$binary`
# (the same list the working-tree pass uses) are dropped afterwards — the rule
# of the language detector: `git ls-files` minus the binaries by extension.

Write-Host ""
Write-Host "=== In the history about to be pushed"

$upstream = (RunGit @('rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{upstream}'))
if (-not $upstream) { $upstream = 'origin/main' }
$exists = (RunGit @('rev-parse', '--verify', '--quiet', $upstream))

# ── THE LOCAL REFERENCE CAN BE A LIE, AND IT WAS ──────────────────────────
#
# `git filter-branch -- --all` rewrites the LOCAL remote references too.
# After the cleanup of 23/9/2026 `origin/main` here pointed to a rewritten
# commit: `origin/main..HEAD` gave 29 commits out of 104, and this check
# said «Clean» after having looked at less than a third of them.
#
# The real remote, meanwhile, still had the previous history. At the force-push
# all 104 will go out - that is, the check measured something different from
# what is about to happen, and it is the way a real defence
# turns into a rubber stamp.
#
# We ask the remote where it REALLY is. If it does not match, the local
# reference is worth nothing and we look at the whole history. If the remote does not
# answer, we look at the whole history all the same: between erring on the side of excess and
# pushing a user name, the choice is not hard.
$allHistory = $false
$why = ""

if (-not $exists) {
    $allHistory = $true
    $why = "no upstream branch ($upstream)"
} else {
    $remote = ($upstream -split '/')[0]
    $branch   = ($upstream -split '/', 2)[1]
    $la = (RunGit @('ls-remote', '--heads', $remote, $branch))
    if (-not $la) {
        $allHistory = $true
        $why = "the remote does not answer, and a local reference is not enough"
    } else {
        $remoteSha = ($la -split "\s+")[0]
        $shaLocale = (RunGit @('rev-parse', $upstream))
        if ($remoteSha -ne $shaLocale) {
            $allHistory = $true
            $why = "$upstream here says $($shaLocale.Substring(0,7)) but the remote " +
                      "says $($remoteSha.Substring(0,7)): the history was rewritten"
        }
    }
}

if ($allHistory) {
    Write-Host "  .. $why"
    Write-Host "  .. the WHOLE history is examined"
    $toPush = @(RunGit @('rev-list', 'HEAD'))
} else {
    $toPush = @(RunGit @('rev-list', "$upstream..HEAD"))
}

if ($toPush.Count -eq 0) {
    Write-Host "  ok  nothing to push"
} else {
    Write-Host "  .. $($toPush.Count) commits to look at"

    function LookAtHistory($itemName, $pattern, $why, $caseSensitive, $except = @()) {
        $foundItems = @(RunGit (@('grep', '--text', '-n', '--no-color') +
            $(if ($caseSensitive) { @() } else { @('-i') }) +
            @('-P', '-e', $pattern) + $toPush))

        # Shape of a line: <sha>:<path>:<line>:<text>
        $gaps = @($foundItems | Where-Object {
            if ($_ -notmatch '^([0-9a-f]{7,40}):([^:]+):(\d+):') { return $false }
            $filePath = $Matches[2] -replace '/', '\'
            # Only `$except`, that is the exception OF THE PATTERN. Here there also stood
            # `CanNameThem`, the list valid for all the patterns together: it is
            # gone, and with it the pass that the two checks had
            # on the internal address.
            if ($except -contains (Split-Path -Leaf $filePath)) { return $false }
            if ($binary -contains [IO.Path]::GetExtension($filePath).ToLower()) { return $false }
            $true
        })

        if ($gaps.Count -eq 0) { Write-Host "  ok  no $itemName"; return }
        $script:trouble++
        $file = @($gaps | ForEach-Object {
            $null = $_ -match '^([0-9a-f]{7,40}):([^:]+):'
            $Matches[2]
        } | Sort-Object -Unique)
        Write-Host "  XX  $itemName ($why) in $($gaps.Count) places, $($file.Count) files:"
        foreach ($f in $file) { Write-Host "        $f" }
        Write-Host "        History cannot be repaired with a change: it has to be"
        Write-Host "        rewritten with git filter-branch (the tree filter used in 2026,"
        Write-Host "        Checks/cleanup-history.js, left the tree in W3.1-bis.10: see it with"
        Write-Host "        git show b829cff:Checks/cleanup-history.js), and it has to be done"
        Write-Host "        BEFORE the push - afterwards it becomes a force-push."
    }

    LookAtHistory 'the user name' $AuthorNameButTheTwoSupportUrls 'the name of the person working' $false $nameOnly
    LookAtHistory 'a session path' 'AppData.Local.Temp' 'exists only on this machine' $false $sessionOnly
    LookAtHistory 'an internal address' $TrueAddress 'a private network' $false
    LookAtHistory 'the inside of another repository' $InsideAnotherRepo 'folders and lines of other people''s homes' $false $twoChecks
    LookAtHistory 'the O: drive' '(?<![A-Za-z])O:\\' 'a company network drive' $true $talkAboutDisks
    LookAtHistory 'the W: drive' '(?<![A-Za-z])W:\\' 'a company network drive' $true $talkAboutDisks
}

# ==============================================================================
# 3. The S: drive, which is stated instead of hidden
# ==============================================================================
#
# On 24/9/2026 the HARD-WIRED FALLBACKS disappeared: the profile library is stated
# with `OCE_WELDMENT_PROFILES` and the document templates with `OCE_DOCUMENT_TEMPLATES`, and
# without those variables the bench stops at once saying what is missing instead of
# looking for a drive that on that machine does not exist. A fallback that works on
# one workstation only helps nobody, and meanwhile it publishes how that
# drive is laid out.
#
# THIS LIST STAYS, HOWEVER, and has not become useless: `S:` keeps
# appearing in EXAMPLES (README, catalogue descriptions) and in the checks that
# use fake paths. Those are fine. What must not come back is a
# CONSTANT on which a behaviour depends - and that is exactly what
# this list shows, file by file, to whoever reads before publishing.

Write-Host ""
Write-Host "=== Paths of this installation (not forbidden, but worth knowing)"
$onDiskS = Lines '(?<![A-Za-z])S:\\' $true
if ($onDiskS.Count -eq 0) {
    Write-Host "  none"
} else {
    $file = $onDiskS | ForEach-Object { $_.Path.Substring($root.Length + 1) } | Sort-Object -Unique
    Write-Host "  $($onDiskS.Count) references to S: in $($file.Count) files:"
    foreach ($f in $file) { Write-Host "     $f" }
    Write-Host "  They are fine only if whoever clones can override them with a variable or an"
    Write-Host "  argument. If they are constants, it is not a repository that can be cloned."
}

Write-Host ""
if ($trouble -eq 0) { Write-Host 'Clean.'; exit 0 }
Write-Host "$trouble categories to clean up."
exit 1
