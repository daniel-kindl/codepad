# CodePad

CodePad is a desktop workbench and a CLI for Linux and Windows. People and AI agents use it to run C# snippets
and to check JSON, offline.

## Language

### Experiments

**Experiment**:
One unit of work that CodePad stores: a title, an operation kind, a source and an input. The user edits it.

**Operation kind**:
What CodePad does with the source of an experiment. v0.1 has two kinds: C# and JSON.

**Source**:
The text that the operation uses: C# code, or a JSON document.

**Input**:
Optional text for a C# experiment. The snippet reads it as standard input. A JSON experiment does not use it.

**Revision**:
The number of the current version of an experiment. Each edit that changes the title, the source or the input
adds 1.

### Executions

**Execution request**:
A snapshot of an experiment at one revision, with the policy for one execution. An edit to the experiment does
not change it.

**Execution**:
One evaluation of an execution request. Its status is pending, running or completed.

**Rerun**:
A new execution of the snapshot of an earlier execution request. It gets a new ID.

**Outcome**:
How a completed execution ended: success, compilation failed, runtime failed, canceled, timed out or worker
failed.

**Operation policy**:
The rules for one operation kind: what can start it, its time limit and its result limits.

### Results

**Result**:
The bounded tree of values that an execution or a live evaluation gives.

**Diagnostic**:
A message about the source, with a severity and, if known, a line and a column.

**Truncation**:
The record that a result holds less than the full value, with the reason, for example a cycle or a limit.
