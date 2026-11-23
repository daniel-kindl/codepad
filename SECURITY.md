# Security policy

## Report a vulnerability

Do not open a public issue for a vulnerability.

Report it privately on GitHub: open the **Security** tab of this repository and select
**Report a vulnerability**. Include the steps to reproduce the problem and the version or commit
that you tested.

## Supported versions

Only the latest release gets security fixes.

## Run only code that you trust

CodePad runs a C# snippet in a separate worker process. The process keeps the app responsive and
contains a crash or a hang. It is **not a security sandbox**. The snippet has the permissions of the
user who runs CodePad: it can read and write files, use the network and start programs.

Run only code that you trust. CodePad never runs C# code on its own. Typing, autosave, opening an
experiment, restoring a session, importing a file and startup do not start a run. Only an explicit Run
starts one. See [ARCHITECTURE.md](ARCHITECTURE.md#worker-trust-boundary).
