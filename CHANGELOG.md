# Changelog

All notable changes to this project are in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). The current version is in
`Directory.Build.props`.

## [Unreleased]

### Added

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
