# Updates

CodePad can find, download, check and install later stable releases from the official GitHub Releases of
`daniel-kindl/codepad`. This page is the contract between the app and the release process ([#15]), and the
rules that keep an update safe.

## What the user sees

- **First start:** a notice explains the daily update check, with two buttons: Keep checking and Turn off. The default is on.
- **Daily check:** at most once in 24 hours, a few seconds after the start, in the background. It is silent when there is nothing new. A failure
  (offline, rate limit, GitHub down) is silent too and never blocks the start, editing, running code or data access.
- **A newer stable release:** a notice shows the version you have, the new version, the size of the download and the release notes.
  The buttons are **Download and install** and **Later**. Nothing changes until the click.
- **Download:** a progress bar and a Cancel button. After the download the app checks the size and the SHA-256 digest, and it stages the file outside the installation.
- **Restart and install:** a second click. CodePad saves all open experiments and the tab session, closes, installs and starts again. If a tab cannot save, the app stays open.
- **Failure:** a plain sentence, a Try again button and the promise that CodePad is unchanged. If the new version does not start, the old version comes back, and the next start says so.
- **Settings > Updates:** the installed version, the daily check on or off, and **Check now**.
- **No in-app update is possible** when the installation folder is not writable by the user (for example a package manager owns it). The notice then says to update the way you installed CodePad, and gives the release page.

Nothing downloads or installs by itself. There is no forced update, no automatic restart and no silent replacement.

## The release contract (for #15)

A release on GitHub is a **stable** release (not a draft, not a pre-release) with the tag `vMAJOR.MINOR.PATCH`. It has these files:

| File | What it is |
| --- | --- |
| `codepad-<version>-linux-x64.tar.gz` | The files of the app for Linux x64. The archive root is the app folder. The program `Codepad.Desktop` is at the root and has the execute bit. |
| `codepad-<version>-win-x64.zip` | The files of the app for Windows x64. `Codepad.Desktop.exe` is at the root. |
| `manifest.json` | The description of the release. Its signed bytes are the file as published. |
| `manifest.json.sig` | The signature of the exact bytes of `manifest.json`. |

The archive also holds `Codepad.Updater.dll` and its runtime files (or a self-contained `Codepad.Updater`), next to the app. The archive has no links,
no devices and no path outside its root. It has at most 20,000 files and 3 GiB of data.

### manifest.json

```json
{
  "schemaVersion": 1,
  "protocol": 1,
  "version": "0.2.0",
  "releaseUrl": "https://github.com/daniel-kindl/codepad/releases/tag/v0.2.0",
  "releaseNotes": "Text for the user. At most 20,000 characters.",
  "publishedAt": "2026-12-01T10:00:00Z",
  "assets": [
    { "platform": "linux-x64", "name": "codepad-0.2.0-linux-x64.tar.gz", "size": 12900000, "sha256": "<64 hex digits>", "url": "https://github.com/daniel-kindl/codepad/releases/download/v0.2.0/codepad-0.2.0-linux-x64.tar.gz" },
    { "platform": "win-x64",   "name": "codepad-0.2.0-win-x64.zip",       "size": 13400000, "sha256": "<64 hex digits>", "url": "https://github.com/daniel-kindl/codepad/releases/download/v0.2.0/codepad-0.2.0-win-x64.zip" }
  ]
}
```

The app rejects the whole manifest when: the schema or the protocol is not 1; the version is not valid SemVer; an address is not
`https://github.com/daniel-kindl/codepad/releases/...` (no user name, no port, no query, no `..`); an asset address is not the file `name` in the release of
`version`; a name is not a plain file name; a size is not from 1 byte to 1 GiB; a digest is not 64 hex digits; two assets have one platform; there are more than 16 assets; or the file is larger than 256 KiB.
The tag of the release must equal the version in the manifest. An unknown platform is ignored.

### Release signing

The signature is **ECDSA on the curve P-256 with SHA-256**, in the fixed 64 byte form (r followed by s, 32 bytes each), over the exact bytes of `manifest.json`. The public
key is in `src/Codepad.Desktop/Updates/UpdatePublicKey.pem` (SubjectPublicKeyInfo PEM). It is compiled into the app.

- A checksum from the same release page would not prove who published it. A signature does, because the key comes with the app.
- **The repository has no key yet.** A build without a key cannot verify anything, so it installs no update. The owner makes the key pair, keeps
  the private key outside the repository and outside CI that agents can start, puts the public key in the file, and signs each manifest by hand or with a tool that
  the owner starts. Nothing in this repository publishes a release.
- To change the key, ship a release with the new public key that is signed with the old key.

## How the app updates itself

1. **Check.** `GitHubUpdateSource` reads `GET /repos/daniel-kindl/codepad/releases/latest` (no token), ignores drafts and pre-releases, and downloads `manifest.json` and
   `manifest.json.sig`. Each request is HTTPS to an official host (`api.github.com`, `github.com`, `objects.githubusercontent.com`,
   `release-assets.githubusercontent.com`), with at most 3 redirects, and each redirect target is checked again. The client sends no cookie and no credentials.
2. **Verify.** The signature is checked first. Then the manifest is parsed and checked. Then the version is compared with the installed version by the SemVer rules (never by date). An equal or older
   release, a pre-release, and a release without an archive for this platform are not offered.
3. **Download.** The archive goes to `<data>/updates/staging/<version>/`. The download stops when the body is longer than the signed size. At the end the size and the SHA-256 digest must match.
   A failed or canceled download leaves no file.
4. **Hand-off** (after the click on Restart and install):
   - the app saves all experiments and the session, and stays open when any save fails;
   - it checks the archive again, unpacks it next to the installation (`app.new`), and checks that the program is in it;
   - it writes a consistent copy of the database (`VACUUM INTO`) to `<data>/updates/pre-update-<version>.db`, and stops when the copy fails;
   - it copies the helper to a folder outside the installation, starts it with a plan file, and closes.
5. **Swap** (the helper `Codepad.Updater`, after the app exited): `app` becomes `app.old`, `app.new` becomes `app`, and the new version starts with the variable `CODEPAD_UPDATE_HEALTH_FILE`. The new version creates that file when its window is open.
   - **Healthy:** the helper deletes `app.old` and writes the result `updated`.
   - **Not healthy** (it exits, does not start, or does not create the file in 60 seconds): the helper stops it, puts `app.old` back as `app`, puts the database copy back, starts the old version, and writes the result `rolled_back`. The next start tells the user.
   - A crash between the two renames is repaired the next time the helper runs.
   - The running program is never overwritten, and the old files stay until the new version has proven itself.
6. **Data.** Experiments, settings, tabs and history are in the data and config folders, outside the installation, so a swap cannot touch them. A new version can raise the schema of the database. The old version cannot read a newer schema.
   So a rollback puts the copy from before the update back. Work done in the seconds of a failed first start is lost. This is the price of a safe rollback.

## Install location

The installation folder must be writable by the user, together with its parent folder (the app makes `app.new` and `app.old` there). The packages of [#15] must use a per-user folder:
Linux `~/.local/share/CodePad/app`, Windows `%LOCALAPPDATA%\Programs\CodePad\app`. A folder that needs root or administrator rights is not supported: the app says so and gives the release page.

## Limits

- Linux x64 and Windows x64 only. No delta updates, no pre-release channel, no downgrade, no self-hosted server.
- Only one update runs at a time.
- GitHub can limit anonymous API calls. The app checks once a day and reports a limit as a retry message.
- The helper needs the .NET runtime of the app, or must be self-contained in the package.
- A machine without write access, a read-only file system, or a package-manager install gets a manual-update message.

[#15]: https://github.com/daniel-kindl/codepad/issues/15
