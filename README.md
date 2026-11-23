# codepad

<!-- TODO(owner): say what codepad is and who it is for. -->

## Requirements

- .NET SDK 11.0.100 or a later 11.0.1xx patch. `global.json` pins it.

## Build and run

```bash
dotnet build
dotnet run --project src/Codepad
```

## Check a change

CI runs the same commands on each pull request:

```bash
dotnet restore
dotnet build --configuration Release --no-restore
dotnet format --verify-no-changes --no-restore
dotnet test --configuration Release --no-build
```

The build treats each warning as an error.

## Contribute

- This project follows [dkstack](https://github.com/daniel-kindl/dkstack). See
  [AGENTS.md](AGENTS.md).
- Write each commit and pull request title as a
  [conventional commit](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/version-control/index.md)
  of 50 characters or fewer.
- Use a short-lived branch for each change. Open a pull request to `main`. Pull requests are
  squash-merged.
- Versions follow
  [semantic versioning](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/versioning/index.md).
  Add each change to `CHANGELOG.md` under `[Unreleased]`.
- Report a vulnerability as [SECURITY.md](SECURITY.md) says.
