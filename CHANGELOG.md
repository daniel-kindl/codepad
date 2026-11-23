# Changelog

All notable changes to this project are in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). The current version is in
`Directory.Build.props`.

## [Unreleased]

### Fixed

- A C# source that is larger than the worker protocol limit now gives a `WorkerFailed` report with the code
  `WorkerRequestTooLarge`. Before, the runner threw `FrameException`.

### Added

- Live JSON tool (#9): `JsonEvaluator` checks and formats JSON with `System.Text.Json` after a short pause in
  typing, with no Run command. It limits the size to 1,000,000 bytes and the depth to 64. A valid document
  shows as indented text and as a tree in the result panel. An invalid document shows the line and column of
  the error, and the input stays as typed. A late result of an old edit is dropped. A JSON experiment saves
  and restores like a C# experiment. Live evaluation makes no history record. The tool view is not in the
  main window yet.
- Run history (#8): `RunHistoryService` runs a C# snapshot, or runs a saved snapshot again, and stores one
  immutable record for each run: the source, the input, the policy, the time, the outcome, the problems, the
  bounded value and the output. Schema step 2 replaces the empty reserved `execution_history` table. History
  keeps the newest 100 runs of each experiment and removes the older ones in the same transaction as the new
  record. It never removes an experiment or its current source. A history panel lists the saved runs, newest
  first, shows the saved source and result of one run as read-only, and has a Rerun button. The window shows the
  panel only when it has a history model. No Run command or experiment list uses it yet.
- Result panel (#7): a status header, and tabs for the result, problems, standard output and standard error.
  A number, a string or null shows as selectable text. An object or nested list shows as an expandable tree
  that opens its rows when asked. A list of objects with the same properties shows as a table. A cut value says
  why. A failed, canceled or timed-out run keeps the last successful value on screen. Copy puts the bounded
  value on the clipboard as JSON. The panel shows "No run yet" until a later change connects a run.
- The worker now shows records, anonymous types, tuples and plain classes by their fields and stored
  auto-property values, without calling a getter. A dictionary shows its keys and values. An exception while
  it reads a sequence, or a slow sequence, stops the read and keeps what it has (`TruncationReason.Error`
  and `TimeLimit`).
- C# code completion (#6): keywords, names in scope and members, from the same Roslyn script compilation as
  the diagnostics. Suggestions open as you type a name or a dot, and with Ctrl+Space. Enter or Tab accepts,
  Escape closes, and a punctuation key closes the list and keeps what you typed. A request for an old text
  version or another document is dropped. Completion never runs your code.
- C# editor (#5): the workspace shows an AvaloniaEdit editor with syntax colors for light and dark
  themes, compiler diagnostics and a diagnostics list. The new module `Codepad.Language` gives
  Roslyn syntax coloring and syntax and semantic diagnostics. The analysis runs in the background
  on an immutable text version, waits 250 ms for a pause, cancels on a new edit, and drops stale
  results. A tab or document switch never shows the diagnostics of another document. Enter on a
  diagnostic selects its range. F8 goes to the next one. Escape leaves the editor. Ctrl with
  plus, minus or the mouse wheel zooms. The editor does not run code, and its text is not saved yet.
- C# compilation and isolated execution (#4): the new modules `Codepad.Contracts` and `Codepad.Worker`,
  the `ICSharpRunner` port and `WorkerSupervisor`. Each explicit run compiles the snapshot with Roslyn
  scripting and runs it in a new worker process. The report has the value, stdout, stderr, compiler
  diagnostics with line and column, the exception of a failed run, and the host runtime facts. The
  supervisor enforces the time limit, output caps and cancel, and stops the whole process tree. A worker
  crash, an early exit and a malformed message each give a different `WorkerFailed` reason. Messages are
  length-prefixed JSON on the standard streams, separate from the output of the script. The worker is a
  crash boundary, not a security sandbox. Nothing runs C# code except an explicit call to the runner.
- Experiment persistence (#3): one SQLite database (`codepad.db`) in the per-user data folder, with
  write-ahead logging, full synchronization and a versioned schema (`PRAGMA user_version`). Each
  migration step is one transaction. The app refuses a schema that is newer than itself and does not
  change it. The schema has tables for experiments and settings, and reserved tables for run
  history (#8) and the tab session (#10).
- `IExperimentRepository` and `SqliteExperimentRepository`: create, read, update and list. A write
  succeeds only when its revision is higher than the stored revision, so an older write never
  replaces newer source.
- `AutosaveSession`: waits 500 ms after the last edit, then writes the newest revision once. Writes
  never overlap. `FlushAsync` saves at once, for a tab switch and for shutdown. The save state is
  clean, dirty, saving, saved or error. The state is never saved before the commit. A failure keeps
  the edited text and gives a reason that the UI can turn into an action with a Retry. These are
  building blocks: the editor (#5) connects them to the window.
- A startup notice when the experiment database cannot be used.
- Crash-recovery tests: the last committed version comes back after a stop in the middle of a
  transaction.
- Domain model (#2): `Experiment` with revisions and `Restore`, immutable `ExecutionRequest` snapshots and
  reruns, the `Execution` lifecycle with six outcomes, operation and result policies, diagnostics,
  and the bounded `ResultNode` tree with truncation reasons. Results and diagnostics serialize
  with System.Text.Json.
- `CONTEXT.md`: the domain glossary.
- Architecture tests: Domain references only the .NET base class library and uses no IO, ADO.NET or OS
  types.
- Avalonia desktop shell for Linux and Windows: one window with a File menu, a workspace region
  and a status region, in light and dark themes.
- Projects `Codepad.Domain`, `Codepad.Application`, `Codepad.Infrastructure` and
  `Codepad.Desktop`, with tests that check the module dependency direction.
- Optional `settings.json` with a `logLevel` setting. A settings file that is not valid or a data
  folder that is not usable shows a message in the status region. The app still starts.
- JSON console log events `startup.degraded`, `shell.opened` and `shell.crashed`.
- Headless UI tests with screenshots and a keyboard walk-through, and a `--smoke-test` launch of
  the real app with no network access.
- CI runs the build, format, test and launch checks on Linux and Windows.
- Code style: explicit types only. The build rejects `var`.
- .NET 11 solution with warnings as errors, analyzers, naming rules and Allman braces.
- CI workflow `validate`: locked restore, NuGet audit, build, format check, tests and the dkstack
  format checks.
- Dependabot updates for NuGet, the .NET SDK and GitHub Actions.
- dkstack agent rules (`AGENTS.md`, `CLAUDE.md`), `SECURITY.md` and `CODEOWNERS`.
- `PRODUCT.md`: v0.1 scope, out-of-scope features, roadmap and acceptance scenarios
  AC-01 to AC-19, each mapped to its issues.
- `ARCHITECTURE.md`: module boundaries, the worker trust boundary, storage and retention, and
  open validation questions.
