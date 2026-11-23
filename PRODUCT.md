# CodePad product specification

This page states the v0.1 scope of CodePad. Each feature has an owner issue. The owner issue holds
the detailed rules for that feature. When this page and an owner issue disagree, the owner issue
wins. Fix this page in the same change.

For the module boundaries, the worker process and the storage, see
[ARCHITECTURE.md](ARCHITECTURE.md).

## Purpose

CodePad is a hybrid developer workbench for the desktop. It is a safe place to test scripts and
tools quickly. "Safe" means that an experiment cannot crash the app or lose your work, and that you
do not set anything up. It does not mean protection from harmful code. See
[Product requirements](#product-requirements).

CodePad has two kinds of operation:

- **Explicit C# runs.** You write a C# snippet. The snippet runs only when you start a run.
- **Live tools.** You type input, and CodePad evaluates it as you type. The JSON tool is the only
  live tool in v0.1.

Both kinds share one model of experiments, results and diagnostics. CodePad stores each experiment
automatically on the local disk. You do not manage files or projects.

The long-term goal is a workbench for more kinds of scripts and tools, for example SQL. v0.1 has
only C# and JSON. See [Later](#later).

## Target users

- **Developers** who want to test small scripts and tools on their own computer, with no network
  and no account. They use the desktop app.
- **AI agents** that need the same place to test code and data. They use the CLI. See [#19].

## Product requirements

These requirements apply to every feature in v0.1.

- **Linux and Windows are equal.** Linux x64 and Windows x64 are both release targets. Each
  feature works on both. CI builds and tests on both. See [#1] and [#15].
- **Offline first.** Startup, editing, runs, JSON evaluation, history and search work with no
  network. An update check that fails does not block CodePad. See [#15] and [#17].
- **No account and no cloud.** All data stays in a per-user folder on the local disk. See [#3].
- **Trusted code only.** CodePad runs C# code that its caller trusts. The caller is the user in
  the desktop app, or the user or agent that starts the CLI. The code runs with the user's
  permissions. The worker process keeps the app stable. It is not a security sandbox. See
  [ARCHITECTURE.md](ARCHITECTURE.md#worker-trust-boundary), [#4] and [#19].
- **No automatic C# runs.** C# code never runs when you type, open, restore, import or select an
  experiment, or at startup. A CLI read, search or update never runs code. See [#4], [#14] and
  [#19].
- **No user data in logs.** Logs do not contain source, input, results, tokens or personal file
  paths by default. See [#14] and [#17].

## Workflows

1. Open CodePad. The last open tabs come back. No code runs.
2. Create a C# experiment. Type a snippet. Diagnostics and completion update as you type.
3. Press Ctrl+Enter to run. Read the return value, stdout, stderr and diagnostics.
4. Edit the snippet and run again. The history keeps both runs as separate snapshots.
5. Create a JSON experiment. Paste JSON. The formatted text and a tree show without a run.
6. Search the library for an old experiment. Open it in a tab.
7. Close CodePad. Your edits are already saved. Open CodePad again to continue.

An agent uses the CLI for the same work. See [#19].

1. Run a C# snippet from a file or standard input. Read the JSON result.
2. Validate and format JSON. Read the diagnostics.
3. Create or update an experiment. A person can open it later in the desktop app.
4. Read the run history of an experiment.

## v0.1 scope

All the issues below have the label `priority:p0`. Each row names the feature and its owner issue.

| Area | Feature | Owner |
| --- | --- | --- |
| Base | Linux and Windows desktop shell, module projects, CI on both OSs | [#1] |
| Base | Domain model: experiment, execution request, outcomes, `ResultNode` | [#2] |
| Storage | SQLite storage, transactional autosave, crash recovery | [#3] |
| C# | Compile and run in a disposable worker process, after an explicit Run only | [#4] |
| C# | Editor with syntax colors and diagnostics, with no code run | [#5] |
| C# | Completion for keywords, identifiers and members | [#6] |
| Results | Typed results as text, tree and table, with limits and truncation | [#7] |
| C# | Immutable run history, retention and explicit rerun | [#8] |
| JSON | Live JSON validation and formatting | [#9] |
| Workbench | Tabs and session restore | [#10] |
| Workbench | Searchable experiment library | [#11] |
| Workbench | Command palette (Ctrl+K) | [#12] |
| Workbench | Settings and keyboard shortcuts | [#13] |
| Quality | Failure handling for the worker and the storage | [#14] |
| Release | Linux and Windows packages, release checks, documentation | [#15] |
| Docs | This specification and the architecture | [#16] |
| Release | In-app updater from GitHub Releases | [#17] |
| Agents | CLI for AI agents and scripts: run C#, JSON, experiments, history | [#19] |

The in-app updater ([#17]) is a v0.1 feature. It is not a future enhancement.

### Default values

These are the built-in defaults. They are not fixed requirements. The owner issue can change them.

| Setting | Default | Owner |
| --- | --- | --- |
| Autosave idle delay | About 500 ms, tuned with tests | [#3] |
| C# run time limit | 10 seconds | [#13] |
| History records for each experiment | 100 | [#8], [#13] |
| Update check | On, at most once each 24 hours | [#13], [#17] |

### Keyboard shortcuts

Linux and Windows both use Ctrl-based shortcuts. See [#13].

| Shortcut | Action |
| --- | --- |
| Ctrl+N | New experiment |
| Ctrl+Enter | Run the C# experiment |
| Ctrl+K | Open the command palette |
| Ctrl+P | Open the library search |
| Ctrl+Tab, Ctrl+Shift+Tab | Next tab, previous tab |
| Ctrl+W | Close the tab. The experiment stays in the library. |
| Esc | Close the overlay or popup |

### Updater

The updater follows these rules. [#17] holds the full contract.

- It uses only stable GitHub Releases of `daniel-kindl/codepad`. It ignores drafts and
  prereleases.
- It compares SemVer versions. It rejects an equal or older version.
- A check is optional and periodic. You can also start a check with **Check for updates**.
- It downloads and installs only after you agree. It never restarts CodePad without your OK.
- It verifies a signed manifest and a SHA-256 digest before it installs.
- It installs per user. It does not need administrator or root rights.
- It stages the new version and keeps the old version until the new version starts. If the
  update fails, it rolls back.
- It keeps experiments, settings, tabs and history.
- Only the owner starts a GitHub release. See [#15].

### Platform rules

- Shared domain, application and UI code is the same on both OSs.
- OS-specific behavior is behind small adapters: data folders, worker process control,
  packaging, update install and key handling. See [ARCHITECTURE.md](ARCHITECTURE.md#platform-adapters).
- On Linux, data goes in the XDG data and config folders. On Windows, data goes in
  `LocalApplicationData`. See [#3].

## Out of scope for v0.1

The owner issues list these exclusions. Do not add them to v0.1.

- **Execution:** debugger, NuGet packages, C# project references, worker pools, a sandbox for
  untrusted code, a container sandbox. See [#4] and [#14].
- **Editor:** refactorings and code actions, signature help, symbol search, AI suggestions,
  completion from external packages, more than one language service. See [#5] and [#6].
- **Results:** charts, images, rich HTML, a full object debugger, custom renderers. See [#7].
- **History:** sync across computers, side-by-side compare, export and import, automatic replay.
  See [#8].
- **JSON:** JSONPath, schemas, YAML, remote API calls, pipelines, plugins. See [#9].
- **Workbench:** projects and folders, tags, semantic search, file system indexing, split panes,
  detached windows, an OS-wide shortcut, an external launcher, a plugin registry, a shortcut
  editor. See [#10], [#11], [#12] and [#13].
- **Accounts:** cloud sync, cloud storage, accounts, telemetry, automatic backups. See [#3],
  [#13] and [#14].
- **Updates:** automatic install, restart without permission, differential patches, a prerelease
  channel, downgrades, a self-hosted update server, updates that need administrator or root
  rights. See [#17].
- **Release:** other OS targets, store listings, P1 and P2 features. See [#15].
- **Agent interface:** an MCP server, a network API, remote access, a daemon. See [#19].

### Later

The owner named these as the long-term goal on 2026-11-23. They have no owner issue. They are not
v0.1 scope.

- More operation kinds, for example SQL and other scripts and tools.

## Roadmap

The milestones are in the order of the work. Each issue names its milestone in its body.

| Milestone | Issues |
| --- | --- |
| M0: base | [#1], [#2], [#3], [#16] |
| M1: C# runs | [#4], [#5], [#6], [#8] |
| M2: results and JSON | [#7], [#9] |
| M3: workbench, updater and CLI | [#10], [#11], [#12], [#13], [#17], [#19] |
| M4: hardening and release | [#14], [#15] |

### Dependencies

Each row lists the issues that must close first. This comes from the "Blocked by" line of each
issue.

| Issue | Milestone | Blocked by |
| --- | --- | --- |
| [#1] Bootstrap | M0 | none |
| [#2] Domain contracts | M0 | [#1] |
| [#3] Storage and autosave | M0 | [#2] |
| [#16] Specification | M0 | none |
| [#4] Worker | M1 | [#2] |
| [#5] Editor | M1 | [#1], [#2] |
| [#6] Completion | M1 | [#5] |
| [#8] History | M1 | [#3], [#4] |
| [#7] Results | M2 | [#2], [#4] |
| [#9] JSON tool | M2 | [#2], [#3], [#7] |
| [#10] Tabs | M3 | [#3], [#5] |
| [#11] Library | M3 | [#3], [#10] |
| [#12] Command palette | M3 | [#10], [#11] |
| [#13] Settings | M3 | [#3], [#4], [#5], [#8], [#10] |
| [#17] Updater | M3 | [#3], [#13] |
| [#19] Agent CLI | M3 | [#3], [#4], [#7], [#8], [#9], [#11] |
| [#14] Hardening | M4 | [#3], [#4], [#7], [#8], [#9] |
| [#15] Release | M4 | [#6] to [#14], [#16], [#17], [#19] |

## Release acceptance scenarios

The owner reviewed and approved each scenario on 2026-11-23. Each scenario restates criteria
from its owner issues. It adds no new requirement. AC-19 comes from [#19], which the owner added
on the same date.

Run each scenario on Linux x64 and on Windows x64, from a clean install. [#15] runs the full set
before the v0.1 release.

| ID | Scenario | Pass when | Issues |
| --- | --- | --- | --- |
| AC-01 | First launch | One window opens with no network. A bad config or a missing data folder gives a message that you can recover from. | [#1], [#15] |
| AC-02 | Run C# | An expression and a multi-statement snippet run after Run. The return value and stdout show. `Console.WriteLine` output does not corrupt the IPC messages. A thrown exception gives `RuntimeFailed` with a bounded stack trace. | [#4] |
| AC-03 | C# compile error | Diagnostics show the line and column. The snippet does not run. | [#4], [#5] |
| AC-04 | C# editor | Syntax colors and diagnostics update as you type. No code runs. No old diagnostics show. A tab switch never shows the diagnostics of another tab. | [#5] |
| AC-05 | C# completion | Keyword, identifier and member completion work by keyboard. Completion never runs code. | [#6] |
| AC-06 | Worker failure | Timeout, cancel, crash and bad IPC each give their own outcome. The UI stays responsive. No child process stays. | [#4], [#14] |
| AC-07 | Typed results | A value, an object and a record list show as text, tree and table. Cycles and large values truncate safely. An infinite enumerable or a throwing getter cannot freeze the worker or the UI. | [#7] |
| AC-08 | Run history | Two runs with different source keep two snapshots. History survives a restart. Rerun needs an explicit action. Retention removes only old run records, never experiments or the current source. | [#8] |
| AC-09 | Live JSON | Valid JSON formats with no Run. Invalid JSON shows its location and keeps the input. An old result never replaces a newer one. Oversized or deeply nested JSON gives a bounded failure with a reason. | [#9] |
| AC-10 | Autosave | Edits save automatically and come back after a restart. A save failure shows and keeps the editor text. An older write never replaces a newer revision. | [#3], [#10] |
| AC-11 | Crash recovery | After a forced stop, the last committed state comes back. Restored code does not run. | [#3], [#14] |
| AC-12 | Tabs | C# and JSON tabs keep their own state. Closing a tab keeps the experiment. Open tabs come back after a restart. | [#10] |
| AC-13 | Library search | A search finds experiments by title, source and type. Opening an open experiment focuses its tab. A search over thousands of experiments does not block the UI. | [#11] |
| AC-14 | Keyboard and visual | Ctrl+K opens the palette. You can complete the core journey with no mouse. Focus comes back after Esc. Focus states, scaling, a narrow window and empty, loading and error states pass visual review. | [#12], [#13], [#15] |
| AC-15 | Settings | Settings survive a restart. Invalid values are rejected. A new time limit applies only to new runs. Clearing history keeps experiments. | [#13] |
| AC-16 | Offline | All of AC-01 to AC-15 pass with the network disconnected. A failed update check does not block CodePad. | [#15], [#17] |
| AC-17 | Update | Install one version from a clean state, then update to the next version. The update installs only after you agree. A bad signature or digest is rejected. A failed update rolls back. Data stays after the update and after a rollback. | [#15], [#17] |
| AC-18 | Release package | Linux and Windows artifacts have checksums and a signed manifest. All CI gates pass. The docs state the trusted-code warning and known limits. | [#15] |
| AC-19 | Agent CLI | An agent runs C# and JSON from a shell and parses the versioned JSON output. CLI changes show in the desktop library and history. A CLI write while the desktop app is open loses no data. | [#19] |

## Open questions

[ARCHITECTURE.md](ARCHITECTURE.md#open-validation-questions) lists the technical questions that
the owner issues leave open. Resolve each one in its owner issue during implementation.

[#1]: https://github.com/daniel-kindl/codepad/issues/1
[#2]: https://github.com/daniel-kindl/codepad/issues/2
[#3]: https://github.com/daniel-kindl/codepad/issues/3
[#4]: https://github.com/daniel-kindl/codepad/issues/4
[#5]: https://github.com/daniel-kindl/codepad/issues/5
[#6]: https://github.com/daniel-kindl/codepad/issues/6
[#7]: https://github.com/daniel-kindl/codepad/issues/7
[#8]: https://github.com/daniel-kindl/codepad/issues/8
[#9]: https://github.com/daniel-kindl/codepad/issues/9
[#10]: https://github.com/daniel-kindl/codepad/issues/10
[#11]: https://github.com/daniel-kindl/codepad/issues/11
[#12]: https://github.com/daniel-kindl/codepad/issues/12
[#13]: https://github.com/daniel-kindl/codepad/issues/13
[#14]: https://github.com/daniel-kindl/codepad/issues/14
[#15]: https://github.com/daniel-kindl/codepad/issues/15
[#16]: https://github.com/daniel-kindl/codepad/issues/16
[#17]: https://github.com/daniel-kindl/codepad/issues/17
[#19]: https://github.com/daniel-kindl/codepad/issues/19
