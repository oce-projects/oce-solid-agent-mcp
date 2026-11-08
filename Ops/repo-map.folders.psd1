# Ops\repo-map.folders.psd1 - the descriptions that Ops\commands\repo-map.ps1 puts into REPO-MAP.md.
#
# Version 1.0.0
# Changelog:
#   1.0.0 (08/11/2026) first version.
#
# The script produces the mechanical part of the map (which folders and files exist, how many files);
# the words come from here. `repo-map.ps1 -Check` FAILS when a top-level folder or a root file has no
# entry here, or an entry names something that no longer exists: describe the new folder here, then
# run `repo-map.ps1 -Write`.
@{
    # Top-level folders: what they are, who reads them, who writes them.
    Folders = @{
        '.github'                 = @{ What = 'CI: tests.yml (language gate, build, contract suite, what could not run), language-gate.yml, installer-smoke.yml, and their steps in .github/steps'; ReadBy = 'GitHub Actions (windows-latest); Checks/check-workflow.ps1 in the pre-push hook'; WrittenBy = 'by hand' }
        'Bench'                   = @{ What = 'the bench: the C# runner that drives a real SOLIDWORKS session, the cases (Bench/cases/<family>/*.json), the tasks, the report'; ReadBy = 'Bench/bench.ps1, Ops/commands/bench-run.ps1, the contract suite and the CI (Bench/report/last.json)'; WrittenBy = 'cases by hand with each operation; Bench/report/last.json by bench.ps1 (committed after a full green run)' }
        'Bridge'                  = @{ What = 'the SOLIDWORKS side of the bridge: attaching to the session (Session.cs, OCE_NO_ATTACH), documents, plans kept in memory, shape'; ReadBy = 'Program.cs, Tools/McpTools.cs'; WrittenBy = 'code changes' }
        'Checks'                  = @{ What = 'probes that drive SOLIDWORKS (one folder each), the release scripts, the publishability and language gates (Checks/language), the git hooks (Checks/hooks)'; ReadBy = 'the pre-push hook, the CI, the maintainer; Checks/CloseLeftovers is used by the bench and by Ops'; WrittenBy = 'by hand' }
        'Installer'               = @{ What = 'Installer.exe: window, silent mode, --verify, uninstall; it carries the bridge inside'; ReadBy = 'build.ps1 (embeds bin into Installer/Payload), the people who install'; WrittenBy = 'code changes' }
        'Mcp'                     = @{ What = 'pieces of the MCP protocol: protocol helpers, elicitation, resources, progress notifications'; ReadBy = 'Program.cs'; WrittenBy = 'code changes' }
        'Oce.Solid.Agent.Catalog' = @{ What = 'the program that exports Tools/catalog.json from the catalog in the library (Tools/update.md)'; ReadBy = 'build.ps1'; WrittenBy = 'code changes' }
        'Oce.Solid.Agent.Core'    = @{ What = 'the library of operations: SwOperations.cs and the *Operations.cs files, PlanExecutor.cs, DocumentState.cs, and the catalog the tools are exported from (Catalog/Operations.cs)'; ReadBy = 'the bridge, the bench, the catalog exporter, the contract suite'; WrittenBy = 'code changes' }
        'Ops'                     = @{ What = 'the operations toolkit for agents and people: preflight, detached bench, contracts, deploy with gates, SOLIDWORKS restart, diagnostics; the manual (Ops/README.md) and the known errors (Ops/KNOWN-ERRORS.md)'; ReadBy = 'agents (DevAgentAi, a Claude session), the maintainer'; WrittenBy = 'by hand; Ops/out and the local configuration are not tracked' }
        'Shared'                  = @{ What = 'code shared by bridge, bench and tests: Leash.cs (a call that does not come back), McpClient.cs, SolidWorksSentinel.cs, PreparationFailed.cs'; ReadBy = 'the bridge, the bench, the contract suite'; WrittenBy = 'code changes' }
        'Skills'                  = @{ What = 'the skill that teaches a model to drive the bridge (Skills/solidworks-bridge/SKILL.md and its references)'; ReadBy = 'Claude clients that load the skill'; WrittenBy = 'by hand, with a version in SKILL.md' }
        'Tests'                   = @{ What = 'the contract suite: needs no SOLIDWORKS (the checks that need it say "not here"), it is what the CI runs'; ReadBy = 'Ops/commands/contracts.ps1, the CI'; WrittenBy = 'code changes, one contract per rule' }
        'Tools'                   = @{ What = 'the MCP tool definitions (McpTools.cs), the catalog reader (Catalog.cs), the exported catalog.json, api_knowledge.json, QuestionRegistry.cs'; ReadBy = 'Program.cs; catalog.json is served next to the executable'; WrittenBy = 'catalog.json by Oce.Solid.Agent.Catalog.exe only, never by hand' }
    }

    # Files at the root of the repository.
    RootFiles = @{
        '.gitattributes'         = @{ What = 'line endings and binary types for git'; ReadBy = 'git'; WrittenBy = 'by hand' }
        '.gitignore'             = @{ What = 'what git never tracks: build outputs, bench outputs, internal notes, the local Ops configuration'; ReadBy = 'git, Checks/check-publishability.ps1'; WrittenBy = 'by hand, every line with its reason' }
        'AGENTS.md'              = @{ What = 'the first page for an AI agent: install, change the repository, work autonomously'; ReadBy = 'AI agents; an orchestrator may inject it into its prompts'; WrittenBy = 'by hand' }
        'App.config'             = @{ What = 'configuration of the bridge executable (.NET Framework 4.8)'; ReadBy = 'the bridge at start'; WrittenBy = 'by hand' }
        'CHANGELOG.md'           = @{ What = 'one entry per version; every code change bumps VERSION.txt and adds an entry'; ReadBy = 'people, the release, the contract suite'; WrittenBy = 'with every change' }
        'CONTRIBUTING.md'        = @{ What = 'what the project accepts: issues yes, pull requests not yet; declared is not measured'; ReadBy = 'contributors, agents'; WrittenBy = 'by hand' }
        'INSTALL-FOR-AI.md'      = @{ What = 'the installation procedure an AI agent follows step by step, with exit codes'; ReadBy = 'AI agents asked to install'; WrittenBy = 'by hand' }
        'LICENSE'                = @{ What = 'Apache License 2.0'; ReadBy = 'everyone'; WrittenBy = 'never changed' }
        'NOTICE'                 = @{ What = 'copyright and trademark notices'; ReadBy = 'everyone, the installer'; WrittenBy = 'by hand' }
        'Oce.Solid.Agent.csproj' = @{ What = 'the project of the bridge executable'; ReadBy = 'build.ps1, MSBuild, the CI'; WrittenBy = 'code changes' }
        'Program.cs'             = @{ What = 'the bridge: the stdio MCP server loop (initialize, tools/list, tools/call)'; ReadBy = 'MSBuild'; WrittenBy = 'code changes' }
        'QUICKSTART.md'          = @{ What = 'installation by hand, for a person'; ReadBy = 'people'; WrittenBy = 'by hand' }
        'README.md'              = @{ What = 'what the bridge is, the tools, the operations count, the environment variables, how it is measured, how to build'; ReadBy = 'people; contracts read its numbers'; WrittenBy = 'by hand; counts changed where declared' }
        'RELEASE.md'             = @{ What = 'the release rite, done by a person (tag and GitHub release)'; ReadBy = 'the maintainer'; WrittenBy = 'by hand' }
        'REPO-MAP.md'            = @{ What = 'this map: folders, entry points, where to find what, main flows'; ReadBy = 'agents and people'; WrittenBy = 'generated part by Ops/commands/repo-map.ps1, the rest by hand' }
        'SECURITY.md'            = @{ What = 'how to report a vulnerability privately'; ReadBy = 'people'; WrittenBy = 'by hand' }
        'VERSION.txt'            = @{ What = 'the version of the bridge, one line'; ReadBy = 'build.ps1, the bridge (serverInfo), the installer, the contracts, Ops deploy'; WrittenBy = 'with every code change' }
        'build.ps1'              = @{ What = 'builds the bridge into bin/, the catalog, and dist/Installer.exe'; ReadBy = 'people, Ops/commands/contracts.ps1, Ops/commands/deploy.ps1'; WrittenBy = 'by hand' }
    }

    # Folders whose sub-folders are listed in the map (with their file counts).
    Expand = @('.github', 'Bench', 'Bench/cases', 'Checks', 'Oce.Solid.Agent.Core', 'Ops', 'Skills', 'Tests')

    # Untracked folders that are not part of the repository: never mapped.
    Skip = @('.tickets')

    # Words with a slash that are not paths: MCP method names and the like.
    NotPaths = @('tools/call', 'tools/list', 'notifications/initialized')

    # Paths that exist only after a build or a run (ignored by git). A path cited in the map may
    # point under them without existing in a fresh clone.
    GeneratedRoots = @('bin', 'obj', 'dist', 'Tests/bin', 'Checks/*/bin', 'Bench/bin', 'Bench/report', 'Bench/work', 'Bench/in-progress.txt', 'Bench/last-run.txt', 'Bench/history.txt', 'Ops/out', 'Ops/ops.config.local.psd1', 'Installer/Payload', 'Oce.Solid.Agent.Core/bin')
}
