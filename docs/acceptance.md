# v0.1 acceptance matrix

This page maps each acceptance item of v0.1 to its evidence. **Automated** means a test or a CI job that runs on every change (or in `release-prepare`). **Owner** means a check that
only a person on a real machine can do. The release is ready when the owner has done every *Owner* item and has seen the CI evidence.

## Matrix

| # | Scenario | Evidence | Status |
| --- | --- | --- | --- |
| 1 | First launch opens one window with a new C# experiment, with no network | `validate` job: launch smoke test with no network on Linux and Windows. `WorkspaceViewModelTests.AnEmptyStoreStartsWithOneNewCSharpTab`. `release-prepare`: the installed app starts with the network blocked | Automated |
| 2 | C# expression, diagnostics and completion | `CSharpEditorViewTests`, `CSharpAnalyzerTests`, `CSharpCompletionProviderTests`. A run in the real worker: `CommandPaletteViewTests.RunWithTheRealWorkerGivesTheValueInTheResultPanel`, `WorkerSupervisorTests`, the CLI `RunningOnePlusOne...` test | Automated |
| 3 | JSON live validation and formatting | `JsonEvaluatorTests`, `JsonToolViewModelTests`, `JsonToolViewTests`, CLI `json` tests | Automated |
| 4 | Text, tree and table results | `ResultsViewTests`, `ResultsViewModelTests`, `ResultNodeConverterTests` | Automated |
| 5 | Tabs: create, switch, reorder, close, independent text and kind | `WorkspaceViewModelTests`, `WorkspaceViewTests` | Automated |
| 6 | Search and the library | `SqliteLibraryRepositoryTests` (5,000 experiments), `LibraryViewModelTests`, `LibraryViewTests` | Automated |
| 7 | Run history and rerun | `SqliteRunHistoryRepositoryTests`, `RunHistoryServiceTests`, `RunHistoryViewModelTests`, `RunHistoryViewTests` | Automated |
| 8 | Restart restores tabs and text | `WorkspaceViewModelTests.RestartRestoresTheTabOrderTheActiveTabAndTheSavedText`, `SessionStoreTests`, `JsonExperimentPersistenceTests` | Automated |
| 9 | Offline: C#, JSON, search, history, tabs, CLI | Smoke test with the network blocked (CI). `release-prepare` runs `codepad run` and `codepad json` from the installed package with the network blocked (`unshare -rn` on Linux, a firewall rule on Windows) | Automated |
| 10 | Abnormal worker stop | `FaultInjectionTests` (crash, hang, hang with a child, bad frames, impossible results, flood, cancel), `WorkerSupervisorTests` | Automated |
| 11 | Autosave recovery and a failed write | `AutosaveSessionTests`, `CrashRecoveryTests`, `WorkspaceViewModelTests.RapidEditsDuringAFailedWrite...`, `SqliteExperimentRepositoryTests` (busy, read-only, full, corrupt) | Automated |
| 12 | Nothing runs by itself | `NoAutomaticExecutionTests`, `ExperimentCatalogTests`, CLI `ReadSearchListCreateAndUpdateNeverStartARun` | Automated |
| 13 | Large and cyclic output stays bounded | `BoundedOutputIntegrationTests`, `ResultNodeConverterTests` | Automated |
| 14 | Keyboard-only core journey | `CommandPaletteViewTests` (Ctrl+K, type, Enter), `SettingsViewTests.ShortcutsWorkFromTheEditorAndFromTheLibraryList`, `LibraryViewTests`, `WorkspaceViewTests` (Ctrl+Tab, Ctrl+W, reorder) | Automated |
| 15 | Accessible names and focus | Each view test asserts the names of its controls. `UpdateBannerViewTests`, `SettingsViewTests.EveryControlOfTheSettingsWindow...` | Automated |
| 16 | Normal, empty, error, focus, narrow-window states (light and dark) | Screenshots in every PR and in the `screenshots-*` artifact of `validate`: workspace, library, palette (matches, empty, disabled), settings, update notice (all states), tabs overflow (narrow), results | Automated |
| 17 | Build, format and analyzer gates, no warnings | `validate`: restore (locked, audited), build with warnings as errors, `dotnet format --verify-no-changes`, tests | Automated |
| 18 | Clean install, run, upgrade and uninstall on Linux and Windows | `InstallScriptTests` (bash on Linux, PowerShell on Windows), `release-prepare` (install, run, uninstall in a clean home), `ReleaseUpgradeTests` and `FileUpdateInstallerTests` (a real upgrade with the real helper process, a rollback, and the database copy) | Automated |
| 19 | Reproducible archive and checksums | `ReleaseToolTests` (same bytes for the same files), `release-prepare` (two builds per system, equal digests), `SHA256SUMS` | Automated |
| 20 | The manifest and the signature work with the updater | `ReleaseToolTests`, `ReleaseUpgradeTests`, `UpdateEndToEndTests` (both platforms, tamper cases) | Automated |
| 21 | Documentation: install, data, backup, limits, trusted code, updater trust | `docs/install.md`, `docs/updates.md`, `docs/cli.md`, `docs/release.md`, `ARCHITECTURE.md` | Done |
| 22 | The signing key and the first release | The owner makes the key, adds the public key and publishes (`docs/release.md`) | **Owner** |

## Owner checks (on real machines)

These need a person. Record the result in the verification issue.

1. **Windows and Linux GUI, by eye.** Start the packaged app on a clean Linux desktop and a clean Windows machine. Check the normal, empty, error and narrow-window views in light and dark. The CI screenshots come from a headless renderer on Linux; the Windows files are in the `screenshots-Windows` artifact.
2. **High scale and text scaling.** Set the display scale to 150% and 200% and the system text size to a larger value. Check that nothing is cut off and that the editor stays usable. This is **not verified** by automation.
3. **Screen reader.** Walk through the first start, a run, the library, the palette and the settings with Orca (Linux) and Narrator or NVDA (Windows). This is **not verified** by automation.
4. **Input method editor.** Type text with an IME (for example Japanese or Chinese) in the editor and the search box. **Not verified.**
5. **Signing key and first update.** Make the key (`docs/release.md`), add the public key, sign `manifest.json`, publish a draft release, then publish `0.1.1` and check on both systems that `0.1.0` finds it, installs it and keeps your data. Also try a failing update on purpose (a broken archive) and check the rollback message.
6. **A real copy of the data.** Install over a copy of your own database and check that the experiments and the history are intact after the upgrade.
7. **Windows warnings.** Note what SmartScreen shows for the unsigned files, and decide whether to buy a code-signing certificate for a later release.

## Known gaps

- There is no loading placeholder anywhere: search, formatting and history load fast enough on the tested sizes, so no skeleton was needed.
- A 50,000-node tree and a very large table were tested for bounds, not for scroll speed on a real device.
- The Windows helper, the PowerShell scripts and the firewall block run in CI only. Nobody has run them on a developer machine yet.
