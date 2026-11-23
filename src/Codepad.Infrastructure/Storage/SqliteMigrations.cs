namespace Codepad.Infrastructure.Storage;

/// <summary>
/// The schema of the CodePad database. The version is in <c>PRAGMA user_version</c>.
/// Never edit a released step. Add a new step: a migration must keep the data, and a destructive one needs a backup first (#17).
/// </summary>
internal static class SqliteMigrations
{
    /// <summary>Gets all steps in order. The last one is the current schema.</summary>
    public static IReadOnlyList<Migration> All { get; } =
    [
        new Migration(1, """
            CREATE TABLE experiments (
                id          TEXT    NOT NULL PRIMARY KEY,
                kind        TEXT    NOT NULL,
                title       TEXT    NOT NULL,
                source      TEXT    NOT NULL,
                input       TEXT    NOT NULL,
                created_at  TEXT    NOT NULL,
                modified_at TEXT    NOT NULL,
                revision    INTEGER NOT NULL CHECK (revision >= 1)
            ) STRICT;

            CREATE INDEX ix_experiments_modified_at ON experiments (modified_at DESC);

            CREATE TABLE settings (
                key   TEXT NOT NULL PRIMARY KEY,
                value TEXT NOT NULL
            ) STRICT;

            -- Reserved for run history (#8). #8 adds the columns with its own migration.
            CREATE TABLE execution_history (
                id            TEXT NOT NULL PRIMARY KEY,
                experiment_id TEXT NOT NULL REFERENCES experiments (id)
            ) STRICT;

            -- Reserved for the tab session (#10). #10 adds the columns with its own migration.
            CREATE TABLE tab_session (
                id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1)
            ) STRICT;
            """),

        // Run history (#8). The table from step 1 was reserved and no code wrote to it, so it is empty and can be replaced.
        new Migration(2, """
            DROP TABLE execution_history;

            CREATE TABLE execution_history (
                id                   TEXT    NOT NULL PRIMARY KEY,
                experiment_id        TEXT    NOT NULL REFERENCES experiments (id),
                experiment_revision  INTEGER NOT NULL,
                requested_at         TEXT    NOT NULL,
                completed_at         TEXT    NOT NULL,
                outcome              TEXT    NOT NULL,
                policy_json          TEXT    NOT NULL,
                source               TEXT    NOT NULL,
                input                TEXT    NOT NULL,
                source_revision      INTEGER NOT NULL,
                diagnostics_json     TEXT    NOT NULL,
                value_json           TEXT,
                stdout               TEXT    NOT NULL,
                stdout_original_len  INTEGER,
                stderr               TEXT    NOT NULL,
                stderr_original_len  INTEGER,
                host_json            TEXT
            ) STRICT;

            CREATE INDEX ix_execution_history_experiment ON execution_history (experiment_id, completed_at DESC, id DESC);
            """),

        // Tabs (#10). The session is two IDs and an order. It lives in the settings table under one key, so the empty reserved table has no use.
        new Migration(3, """
            DROP TABLE tab_session;
            """),
    ];
}
