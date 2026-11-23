# Changelog

All notable changes to this project are in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). The project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). The current version is in
`Directory.Build.props`.

## [Unreleased]

### Added

- .NET 11 console project with warnings as errors, analyzers, naming rules and Allman braces.
- CI workflow `validate`: locked restore, NuGet audit, build, format check, tests and the dkstack
  format checks.
- Dependabot updates for NuGet, the .NET SDK and GitHub Actions.
- dkstack agent rules (`AGENTS.md`, `CLAUDE.md`), `SECURITY.md` and `CODEOWNERS`.
