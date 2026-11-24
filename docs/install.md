# Install CodePad

CodePad v0.1 is a per-user desktop app for **Linux x64** and **Windows x64**. It needs no administrator or root rights, no .NET and no network. A
release has one full-install archive for each system. The archives hold the app, the `codepad` command line, the worker and the update helper, all with their
own .NET runtime.

> **Trusted code.** CodePad runs the C# that you write with the permissions of your user account. The worker process is a crash boundary, not a
> security sandbox: it stops a runaway run, but it does not limit files, network or memory. Run only code that you trust.

## Requirements

| | Linux x64 | Windows x64 |
| --- | --- | --- |
| System | A desktop session with X11 (or Wayland with XWayland). Tested on Ubuntu (`ubuntu-latest` in CI) and Fedora. | Windows 10 (1809 or later) or Windows 11. Tested on `windows-latest` in CI. |
| Libraries | The libraries that Avalonia loads: `libX11`, `libfontconfig` and the C library. Most desktop systems have them. | None to add. |
| Disk | About 300 MB for the app, and your data. | About 300 MB for the app, and your data. |
| Network | Not needed. Only the optional update check uses it. | Not needed. Only the optional update check uses it. |

## Install on Linux

```bash
sha256sum --check SHA256SUMS --ignore-missing      # optional: check the download
bash install.sh codepad-<version>-linux-x64.tar.gz
```

The script puts the app in `~/.local/share/CodePad/app`, links `~/.local/bin/codepad` and adds a launcher entry. Make sure `~/.local/bin` is in your
`PATH`. Start CodePad from the launcher or run `~/.local/share/CodePad/app/Codepad.Desktop`. Running the script again with a newer archive updates the files.

## Install on Windows

```powershell
./install.ps1 -Archive codepad-<version>-win-x64.zip
```

The script puts the app in `%LOCALAPPDATA%\Programs\CodePad\app`, adds a Start menu shortcut and adds the folder to the `PATH` of your user, so that
`codepad` works in a new terminal. Windows can warn that the program comes from an unknown publisher: the v0.1 files have no Authenticode signature (see the limits).

Both scripts check the SHA-256 digest when you give `--sha256` (`-Sha256`) or when a `SHA256SUMS` file stands next to the archive.

## First start and use

1. **First start.** CodePad opens with one new C# experiment. A notice explains the daily update check. You can keep it or turn it off.
2. **C#.** Type a script. Diagnostics show as you type. Press **Ctrl+Enter** to run it. The result shows as text, a tree or a table.
3. **JSON.** Create a JSON experiment with **Ctrl+K** and *New JSON experiment*. It formats and checks as you type and shows the line and the column of an error.
4. **Tabs and the library.** Each experiment is a tab. **Ctrl+P** searches all experiments. CodePad saves as you type and restores your tabs after a restart.
5. **The command palette** (**Ctrl+K**) and the **settings** (**Ctrl+,**) reach every action. The settings list all shortcuts.

The command line is described in [cli.md](cli.md).

## Where your data is

| | Linux | Windows |
| --- | --- | --- |
| Experiments, history, session | `~/.local/share/codepad/codepad.db` | `%LOCALAPPDATA%\CodePad\codepad.db` |
| Settings | `~/.config/codepad/settings.json` | `%LOCALAPPDATA%\CodePad\settings.json` |
| Working folder of a run | `~/.local/share/codepad/runs` | `%LOCALAPPDATA%\CodePad\runs` |
| Update files and the safety copy | `~/.local/share/codepad/updates` | `%LOCALAPPDATA%\CodePad\updates` |

`XDG_DATA_HOME` and `XDG_CONFIG_HOME` change the Linux folders. The app folder holds no data, so an update or an uninstall does not touch your experiments.

- **Backup.** Close CodePad, then copy `codepad.db` (and `settings.json`). To restore, close CodePad and put the files back.
- **Reset.** Close CodePad and delete the data folder and the settings file. CodePad then starts as new.
- **History.** CodePad keeps the newest 100 runs of each experiment. You can change this in the settings, and you can clear the history there. Clearing never deletes an experiment.

## Offline use

Everything works with the network off: editing, running C#, JSON, search, history, tabs and the command line. Only the update check needs the network.
When it fails, you see nothing, and CodePad works as before.

## Updates

CodePad can look for a newer **stable** release once a day, and the settings have **Check now**. A notice offers *Download and install* or *Later*. Nothing downloads or
installs without your click, and CodePad never restarts by itself. A new version replaces the old one only after CodePad closed, and the old
version comes back if the new one does not start. The trust rules and the limits are in [updates.md](updates.md).

CodePad can update itself only when its folder is writable by your user. If you installed it another way, update it the same way.

## Uninstall

```bash
bash uninstall.sh                # Linux: removes the app, keeps your data
bash uninstall.sh --purge-data   # Linux: also deletes your data. This cannot be undone.
```

```powershell
./uninstall.ps1                  # Windows: removes the app, the shortcut and the PATH entry, keeps your data
./uninstall.ps1 -PurgeData       # Windows: also deletes your data. This cannot be undone.
```

## Known limits of v0.1

- The worker is not a sandbox, and there is no memory or CPU limit. A time limit (10 seconds by default) stops a run and its child processes.
- A child process that leaves the process tree on purpose is not stopped.
- Only one run goes at a time. Output is cut at 100,000 characters and a result at the limits of the result panel.
- The Windows files have no Authenticode signature, and no Linux distribution package exists. Linux and Windows x64 only.
- Not verified on a real screen reader, with the operating system text scaling, or with an input method editor.
- The updater installs only into a folder that the user can write, and it installs nothing in a build without the public key of the publisher.

More detail is in the "Reliability and known limitations" part of [ARCHITECTURE.md](../ARCHITECTURE.md).
