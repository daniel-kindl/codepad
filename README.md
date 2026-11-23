# codepad

CodePad is a desktop workbench for Linux and Windows. Developers and AI agents use it to test
scripts and tools quickly, offline. Run C# snippets on demand and check JSON as you type.

## Documentation

- [PRODUCT.md](PRODUCT.md): v0.1 scope, out-of-scope features and release acceptance scenarios.
- [Roadmap](PRODUCT.md#roadmap): milestones M0 to M4 and the issue dependencies.
- [ARCHITECTURE.md](ARCHITECTURE.md): modules, the worker trust boundary and storage.

## Requirements

- .NET SDK 11.0.100 or a later 11.0.1xx patch. `global.json` pins it.
- Linux x64 or Windows x64. No network connection is needed after the restore.
- Linux: a desktop session (X11 or Wayland with XWayland). To run the launch smoke test with no
  display, install Xvfb (`xvfb-run`).

## Projects

| Project | Holds |
| --- | --- |
| `src/Codepad.Domain` | The domain model. Empty until #2. |
| `src/Codepad.Application` | Use cases and the interfaces for their IO needs. |
| `src/Codepad.Infrastructure` | IO adapters, including the OS-specific folder locator. |
| `src/Codepad.Desktop` | The Avalonia app and the composition root. |
| `tests/Codepad.Tests` | Unit tests and the module dependency tests. |
| `tests/Codepad.Desktop.Tests` | View model tests and headless Avalonia UI tests. |

[ARCHITECTURE.md](ARCHITECTURE.md) states the dependency rules. `ModuleDependencyTests` checks them.

## Build and run

```bash
dotnet restore
dotnet build
dotnet run --project src/Codepad.Desktop
```

## Check a change

CI runs the same commands on Linux and Windows for each pull request:

```bash
dotnet restore
dotnet build --configuration Release --no-restore
dotnet format --verify-no-changes --no-restore
dotnet test --configuration Release --no-build
```

The build treats each warning as an error. The code style forbids `var`: write explicit types.

### Launch smoke test

The smoke test starts the real app, opens the window and exits with code 0. CI runs it with no
network access: in a network namespace on Linux, and with a firewall rule on Windows. To run it:

```bash
# Linux with no display and no network
unshare -rn xvfb-run -a dotnet src/Codepad.Desktop/bin/Release/net11.0/Codepad.Desktop.dll --smoke-test
# Windows, or Linux with a display
dotnet src/Codepad.Desktop/bin/Release/net11.0/Codepad.Desktop.dll --smoke-test
```

`unshare -rn` needs unprivileged user namespaces. Ubuntu 23.10 and later restrict them with
AppArmor, so the command fails there by default. Without `unshare -rn`, the command runs with the
network available. CI allows user namespaces on its disposable runner with
`sudo sysctl -w kernel.apparmor_restrict_unprivileged_userns=0`. Do not change this setting on
your own computer only to run the smoke test.

### Screenshots

`dotnet test` saves screenshots of the shell in the light and dark themes, at the minimum and at a
wide window size, to `tests/Codepad.Desktop.Tests/bin/<Configuration>/net11.0/screenshots/<os>/`.
CI uploads them as the `screenshots-Linux` and `screenshots-Windows` artifacts. The tests do not
compare pixels.

## Settings and logs

CodePad reads an optional `settings.json` at startup:

- Linux: `$XDG_CONFIG_HOME/codepad/settings.json` (default `~/.config/codepad/`).
- Windows: `%LOCALAPPDATA%\CodePad\settings.json`.

```json
{ "logLevel": "Debug" }
```

`logLevel` is a `Microsoft.Extensions.Logging` level name. The default is `Information`. If the
file is not valid, CodePad starts with the default settings and shows a message in the status bar.

The data folder is `$XDG_DATA_HOME/codepad` (default `~/.local/share/codepad/`) on Linux and
`%LOCALAPPDATA%\CodePad` on Windows. If CodePad cannot write to it, the app starts and shows a
message in the status bar.

The app writes JSON log events to standard output. The events do not hold source, input, results
or file paths. Each JSON line has the numeric `EventId`. The output does not hold the event name.
The name is the stable label in the code (`[LoggerMessage]` in `src/Codepad.Desktop/Program.cs`)
and in this table.

| `EventId` | Name | Level | When |
| --- | --- | --- | --- |
| 1 | `startup.degraded` | Warning | Startup used a fallback. Fields: `NoticeKind`, `Reason`. |
| 2 | `shell.opened` | Information | The main window opened. Fields: `Version`, `StartupMs`. |
| 3 | `shell.crashed` | Critical | An unhandled exception stopped the app. Field: `ExceptionType`. |

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
