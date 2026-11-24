# Preparing a release (for the owner)

Agents and CI **prepare** a release: they build, test, package and check. **Only the owner publishes.** Nothing in this repository tags, signs with the owner's key,
uploads to a GitHub release or publishes. The signing key stays with the owner.

## One time: the signing key

```bash
dotnet run --project tools/Codepad.ReleaseTool -c Release -- keygen --out ~/codepad-keys
```

1. Keep `release-private.pem` secret and outside the repository (a password manager or an offline disk). The file is readable by the owner only.
2. Put the contents of `release-public.pem` into `src/Codepad.Desktop/Updates/UpdatePublicKey.pem` and commit it in a reviewed change. Without a public key, a build installs no update.
3. Never put the private key in CI, in a secret that an agent can use, or in the chat.

## Each release

1. **Version.** Set `<Version>` in `Directory.Build.props` and write `CHANGELOG.md`. The version is stable SemVer (for example `0.1.0`). The tag is `v0.1.0`.
2. **Prepare.** Start the workflow **release-prepare** by hand (Actions tab) with the version. For each system, it restores from the lock files, builds, checks the format, runs the tests, builds
   the package **twice and compares the two archives**, installs the package in a clean home folder with the network blocked, runs C# and JSON through the installed `codepad`, starts the
   installed app, and uninstalls. It uploads `package-linux-x64` and `package-win-x64` (the archive and `SHA256SUMS`) and `manifest-unsigned`.
3. **Review the evidence.** Read the logs, the screenshots of the normal CI run (`screenshots-Linux`, `screenshots-Windows`) and `docs/acceptance.md`. Do the manual checks that the acceptance list names.
4. **Manifest.** Download the two packages, write the release notes in a text file, and make the manifest (or use `manifest-unsigned` after you check it):

   ```bash
   codepad-release manifest --version 0.1.0 --notes notes.txt --linux codepad-0.1.0-linux-x64.tar.gz --windows codepad-0.1.0-win-x64.zip --out manifest.json
   ```

5. **Sign** on your own machine, with your key:

   ```bash
   codepad-release sign --key ~/codepad-keys/release-private.pem --manifest manifest.json     # writes manifest.json.sig
   codepad-release verify --public-key src/Codepad.Desktop/Updates/UpdatePublicKey.pem --manifest manifest.json \
     --archive codepad-0.1.0-linux-x64.tar.gz --archive codepad-0.1.0-win-x64.zip
   ```

   `verify` uses the same code as the app. Sign the exact bytes of `manifest.json` and do not edit the file after that.
6. **Publish** a GitHub release for the tag with these files: the two archives, `manifest.json`, `manifest.json.sig`, `SHA256SUMS`, `install.sh`, `install.ps1`, `uninstall.sh`, `uninstall.ps1`. Make it a
   draft first. A draft is not seen by the updater. Publish it (not as a pre-release) when you are satisfied.
7. **Upgrade test.** Install the previous version on Linux and on Windows, publish the new release, and check that the old version offers the update, installs it and keeps your data. See `docs/acceptance.md`.

## What the contract is

The file names, the manifest format, the signature format and the install layout are in [updates.md](updates.md). The updater accepts only a stable release whose
signed manifest names a newer version and an archive for the platform of the user.

## Limits

- **Windows code signing.** The v0.1 files have no Authenticode signature. Windows SmartScreen can warn. The update itself is protected by the manifest signature, not by Authenticode.
- **Reproducibility.** The archive bytes are the same for the same commit, the same SDK and the same version on the same operating system. CI checks it by building twice on each system. A build on another SDK build can differ.
- **Distro packages.** None. The archive and the install script are the v0.1 format.
