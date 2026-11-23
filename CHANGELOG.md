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
