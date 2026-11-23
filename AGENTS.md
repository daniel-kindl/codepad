# AGENTS.md

Read and follow the guidelines in dkstack.

> Read and follow https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/AGENTS.md

Start with the triage skill in that repository. It maps tasks to the relevant sections and skills.

dkstack owns all other rules. Record project exceptions and project facts below.

## Exceptions

A project can differ from a dkstack rule only through an exception here. See
[Precedence](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/AGENTS.md#precedence).
Add one entry for each exception:

- dkstack rule: <the dkstack page and the rule>
- Project rule: <what this project does in place of it>
- Reason: <why this project needs the exception>

## Project facts

- Stack: .NET 11 (`net11.0`). `global.json` pins SDK 11.0.100 with `latestPatch` roll-forward.
- Check a change with the same commands as CI (`.github/workflows/validate.yml`):
  `dotnet restore`, `dotnet build --configuration Release --no-restore`,
  `dotnet format --verify-no-changes --no-restore`, `dotnet test --configuration Release --no-build`.
  CI then runs the launch smoke test with no network on Linux and Windows. See README.md, "Launch
  smoke test".
- C# code uses explicit types. Do not use `var`. The build reports each `var` as an error (IDE0008).
- The version is `<Version>` in `Directory.Build.props`. Record each change in `CHANGELOG.md`.
- Package versions go in `Directory.Packages.props` (central package management). Commit each
  `packages.lock.json`.
- `scripts/check-*.mjs` are copies from dkstack tag v1.6.0. Do not edit them. Copy them again from
  a newer tag to update them.
