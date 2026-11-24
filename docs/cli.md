# The `codepad` command line

`codepad` lets an AI agent or a script use CodePad from a shell. It runs C# snippets, validates and formats JSON, manages
the experiments of the library and reads the run history. It uses the same worker, the same limits, the same result
model and the same database as the desktop app. It works offline and needs no account.

## Trusted code

> **`codepad run` executes C# code with the permissions of your user account.**
> The worker process is a crash boundary, not a security sandbox. It stops a runaway run and its child processes,
> but it does not limit files, network or memory. Run only code that you trust. The caller of the CLI is trusted: the
> user or the agent that starts the command accepts the risk.
>
> Reading, listing, searching and updating experiments never runs code. Only `codepad run` does, and only when you ask.

## Commands

| Command | What it does |
| --- | --- |
| `codepad run` | Runs a C# snippet. It stores the snippet as an experiment and saves a history record. |
| `codepad json` | Validates and formats JSON. It needs no database. |
| `codepad list` | Lists the experiments, newest edit first. |
| `codepad search <text>` | Finds experiments by title and source, ignoring case. |
| `codepad get <id>` | Reads one experiment with its source. |
| `codepad create` | Creates an experiment. |
| `codepad update <id>` | Changes the title or the source. |
| `codepad history <id>` | Lists the saved runs, or shows one saved run. |

Global options: `--data-dir DIR`, `--config-dir DIR` (use other folders; the defaults are the folders of the desktop app,
and `CODEPAD_DATA_DIR` and `CODEPAD_CONFIG_DIR` set them too), `--pretty` (indent the JSON), `--help`, `--version`.

### run

```text
codepad run [--file PATH|-] [--title TITLE] [--timeout SECONDS]
codepad run --experiment ID [--file PATH|-] [--expected-revision N] [--timeout SECONDS]
```

Without `--experiment`, the snippet comes from `--file`, or from the standard input when you pipe it, and the command creates a
new experiment. With `--experiment`, it runs the stored source, or the source from `--file` after it stores that source.
`--expected-revision` makes the update safe: a different stored revision gives exit code 31 and writes nothing. The time
limit is `--timeout` (1 to 600 seconds), or the run timeout of the settings of CodePad (10 seconds by default). The result
and the output have the same limits as in the desktop app. A snippet can be up to 2,000,000 characters.

```bash
echo '1 + 1' | codepad run
```

### json

```text
codepad json [--file PATH|-]
```

The size limit is 1,000,000 bytes and the depth limit is 64. The diagnostics are the same as in the desktop tool. Exit code 0 means valid JSON. Exit code 20 means invalid JSON,
with the line and the column of the first error.

### list, search, get, create, update, history

```text
codepad list   [--kind csharp|json] [--limit N] [--offset N]
codepad search <text> [--kind csharp|json] [--limit N] [--offset N]
codepad get    <id>
codepad create --kind csharp|json --title TITLE [--file PATH|-]
codepad update <id> [--title TITLE] [--file PATH|-] [--expected-revision N]
codepad history <id> [--limit N]
codepad history <id> --run EXECUTION_ID
```

A page has at most 200 items. `hasMore` and `nextOffset` tell how to read the next page. An experiment that the CLI creates or
updates shows in the library of the desktop app, and the reverse. A run of the CLI shows in the history of the experiment in
the desktop app.

## Output

The standard output holds **one JSON document** and a line feed. Logs and short messages go to the standard error. `--help`
and `--version` print text on the standard output.

Every document has `schemaVersion` (now `1`), `command`, `ok` and `exitCode`. `ok` is true when the command did what you asked
(for `run`: the outcome is `Success`). The names are camel case. A value that is absent is `null`, not missing. The version grows when a name or a meaning
changes in a way that can break a reader. A new property is not a change of the version.

### Error document

```json
{ "schemaVersion": 1, "command": "get", "ok": false, "exitCode": 30, "error": { "code": "not_found", "message": "No experiment has this ID." } }
```

The error codes are `usage`, `input_not_found`, `input_unreadable`, `input_too_large`, `storage_unavailable`, `storage_failed`,
`not_found`, `revision_conflict`, `wrong_kind`, `worker_missing`, `canceled` and `internal_error`. A message never holds source, a
path or the content of a file.

### run document

| Property | Meaning |
| --- | --- |
| `outcome` | `Success`, `CompilationFailed`, `RuntimeFailed`, `TimedOut`, `Canceled` or `WorkerFailed`. |
| `experiment` | `id`, `title`, `kind`, `revision` of the experiment that ran. |
| `executionId` | The ID of the run. Use it with `codepad history <id> --run`. |
| `durationMs` | The run time in milliseconds. |
| `diagnostics` | A list of `severity`, `code`, `message`, `line`, `column`. A compile error has the place. |
| `value` | The bounded result as a `ResultNode` tree, or `null`. |
| `stdout`, `stderr` | `text`, `truncated`, `originalLength`. The output of the snippet, cut at 100,000 characters. |
| `host` | The runtime, the operating system, the architecture, the worker version and the language version. |
| `historySaved`, `historyError` | Whether the history record was stored. A failed store does not change the outcome. |

A `ResultNode` has a `kind`: `null`, `scalar` (`typeName`, `value`), `text` (`value`, `originalLength`), `object` (`typeName`, `properties`,
`truncation`), `sequence` (`typeName`, `items`, `truncation`), `truncated` (`typeName`, `reason`) or `unsupported`.

```json
{"schemaVersion":1,"command":"run","ok":true,"exitCode":0,"outcome":"Success","experiment":{"id":"01a20b09-65f7-7b6d-9066-ff05a4798279","title":"CLI run 2026-11-24 00:51:35","kind":"CSharp","revision":1},"executionId":"01a20b09-6600-71e0-bcc2-721cf4efcbcf","durationMs":1112,"diagnostics":[],"value":{"kind":"scalar","typeName":"System.Int32","value":"2"},"stdout":{"text":"","truncated":false,"originalLength":null},"stderr":{"text":"","truncated":false,"originalLength":null},"host":{"runtime":".NET 11.0.0","operatingSystem":"Linux","architecture":"X64","workerVersion":"0.1.0.0","language":"C# 15.0"},"historySaved":true,"historyError":null}
```

### json document

`valid`, `formatted` (the indented text), `tree` (a `ResultNode`) and `problem` (`severity`, `code`, `message`, `line`, `column`). The problem codes
are `JSON_SYNTAX`, `JSON_EMPTY`, `JSON_TOO_LARGE`, `JSON_TOO_DEEP` and `JSON_INVALID_TEXT`.

### Other documents

- `list` and `search`: `items` (`id`, `kind`, `title`, `preview`, `modifiedAt`), `hasMore`, `nextOffset`.
- `get`, `create`, `update`: `experiment` (`id`, `kind`, `title`, `source`, `input`, `createdAt`, `modifiedAt`, `revision`).
- `history <id>`: `experimentId` and `runs` (`executionId`, `experimentRevision`, `requestedAt`, `completedAt`, `durationMs`, `outcome`).
- `history <id> --run`: `run` with the saved source, input, outcome, diagnostics, value, output and host.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success. A run with outcome `Success`, valid JSON, or a command that worked. |
| 1 | Internal error. |
| 2 | Usage error: an unknown command or option, a bad value, or no input. |
| 10 | Run outcome `CompilationFailed`. The snippet did not run. |
| 11 | Run outcome `RuntimeFailed`. |
| 12 | Run outcome `TimedOut`. The worker and its child processes were stopped. |
| 13 | Run outcome `Canceled` (Ctrl+C). |
| 14 | Run outcome `WorkerFailed`, or the worker program is missing. |
| 20 | Invalid JSON, or JSON over a limit. |
| 30 | The experiment does not exist. |
| 31 | Revision conflict. Nothing was written. |
| 32 | The experiment is not a C# experiment, so it cannot run. |
| 40 | The data folder or the database cannot be used. |
| 50 | The input file is missing, unreadable or too large. |

## The database and the desktop app

The CLI and a running desktop app use the same database file. SQLite takes turns for writes (a writer waits up to 5 seconds, then the
command fails with exit code 40). Each experiment has a revision number. An update writes only when it is based on the latest
revision, so two writers cannot overwrite each other without a notice. The CLI gives exit code 31. The desktop app shows "changed somewhere else" on the tab and keeps
the text of the editor. The settings file has no lock: the last write wins.

## Limits

The CLI does not start a server, listen on a port, check for updates or send data anywhere. It has no sandbox for untrusted code. The time limit works the same on
Linux and Windows. There is no memory limit.
