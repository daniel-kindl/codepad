using System.Text.Json;
using Codepad.Updater;

namespace Codepad.Tests.Updates;

public sealed class UpdateSwapTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("codepad-swap-").FullName;
    private readonly FakeHost host = new FakeHost();
    private readonly List<string> log = [];

    public UpdateSwapTests()
    {
        Directory.CreateDirectory(InstallDir);
        File.WriteAllText(Path.Combine(InstallDir, "app.txt"), "old");
        File.WriteAllText(Path.Combine(InstallDir, "Codepad.Desktop"), "old exe");
        Directory.CreateDirectory(NewDir);
        File.WriteAllText(Path.Combine(NewDir, "app.txt"), "new");
        File.WriteAllText(Path.Combine(NewDir, "Codepad.Desktop"), "new exe");
        host.Home = root;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string InstallDir => Path.Combine(root, "app");

    private string NewDir => Path.Combine(root, "app.new");

    private string OldDir => Path.Combine(root, "app.old");

    private string HealthFile => Path.Combine(root, "updates", "health.ok");

    private string ResultFile => Path.Combine(root, "updates", "result.json");

    private string Database => Path.Combine(root, "data", "codepad.db");

    private string Backup => Path.Combine(root, "data", "backup.db");

    private UpdatePlan Plan(int pid = 4242, int health = 5, int wait = 2) => new UpdatePlan(
        1,
        pid,
        InstallDir,
        NewDir,
        OldDir,
        Path.Combine(InstallDir, "Codepad.Desktop"),
        ["--post-update"],
        HealthFile,
        ResultFile,
        wait,
        health,
        Database,
        Backup,
        "0.1.0",
        "0.2.0");

    private Task<int> RunAsync(UpdatePlan plan) => new UpdateSwap(host, log.Add).RunAsync(plan, TestContext.Current.CancellationToken);

    private string Installed(string file = "app.txt") => File.ReadAllText(Path.Combine(InstallDir, file));

    private UpdateResult Result() => JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(ResultFile), JsonSerializerOptions.Web)!;

    [Fact]
    public async Task ANewVersionThatStartsReplacesTheInstallationAndTheOldOneIsDeletedOnlyAfterwards()
    {
        host.NewAppWritesHealthFile = true;
        host.OldFolderStillThereWhenNewAppStarts = () => Directory.Exists(OldDir);

        int exit = await RunAsync(Plan());

        Assert.Equal(UpdateSwap.OK, exit);
        Assert.Equal("new", Installed());
        Assert.False(Directory.Exists(OldDir));
        Assert.False(Directory.Exists(NewDir));
        Assert.Equal("updated", Result().Status);
        Assert.True(host.SawOldFolderWhenNewAppStarted, "The old installation was deleted before the new version proved itself.");
        Assert.Contains("--post-update", host.Started.Single().Arguments);
        Assert.Equal(HealthFile, host.Started.Single().Environment["CODEPAD_UPDATE_HEALTH_FILE"]);
    }

    [Fact]
    public async Task ANewVersionThatNeverBecomesHealthyIsRolledBackAndTheOldVersionRestarts()
    {
        host.NewAppWritesHealthFile = false;

        int exit = await RunAsync(Plan(health: 2));

        Assert.Equal(UpdateSwap.ROLLED_BACK, exit);
        Assert.Equal("old", Installed());
        Assert.Equal("old exe", Installed("Codepad.Desktop"));
        Assert.False(Directory.Exists(OldDir));
        Assert.False(Directory.Exists(InstallDir + ".failed"));
        UpdateResult result = Result();
        Assert.Equal("rolled_back", result.Status);
        Assert.Equal("health_timeout", result.Reason);
        Assert.Equal(2, host.Started.Count);
        Assert.True(host.Started[0].Killed);
        Assert.Empty(host.Started[1].Environment);
    }

    [Fact]
    public async Task ANewVersionThatExitsAtOnceIsRolledBackWithoutWaitingForTheTimeout()
    {
        host.NewAppExitsImmediately = true;

        int exit = await RunAsync(Plan(health: 120));

        Assert.Equal(UpdateSwap.ROLLED_BACK, exit);
        Assert.Equal("exited_before_ready", Result().Reason);
        Assert.Equal("old", Installed());
        Assert.True(host.Elapsed < TimeSpan.FromSeconds(10), $"The helper waited {host.Elapsed}.");
    }

    [Fact]
    public async Task ARollbackPutsTheDatabaseBackIBecauseTheOldVersionCannotReadANewerSchema()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Database)!);
        File.WriteAllText(Database, "schema 3 written by the new version");
        File.WriteAllText(Database + "-wal", "wal");
        File.WriteAllText(Database + "-shm", "shm");
        File.WriteAllText(Backup, "schema 2 before the update");
        host.NewAppWritesHealthFile = false;

        await RunAsync(Plan(health: 1));

        Assert.Equal("schema 2 before the update", File.ReadAllText(Database));
        Assert.False(File.Exists(Database + "-wal"));
        Assert.False(File.Exists(Database + "-shm"));
    }

    [Fact]
    public async Task ASuccessfulUpdateLeavesTheDatabaseAlone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Database)!);
        File.WriteAllText(Database, "schema 3");
        File.WriteAllText(Backup, "schema 2");
        host.NewAppWritesHealthFile = true;

        await RunAsync(Plan());

        Assert.Equal("schema 3", File.ReadAllText(Database));
    }

    [Fact]
    public async Task IfCodePadDoesNotExitNothingChanges()
    {
        host.AppStaysRunning = true;

        int exit = await RunAsync(Plan(wait: 1));

        Assert.Equal(UpdateSwap.APP_STILL_RUNNING, exit);
        Assert.Equal("old", Installed());
        Assert.True(Directory.Exists(NewDir));
        Assert.Equal("not_applied", Result().Status);
        Assert.Empty(host.Started);
    }

    [Fact]
    public async Task ANewFolderWithoutTheProgramIsNotSwappedIn()
    {
        File.Delete(Path.Combine(NewDir, "Codepad.Desktop"));

        int exit = await RunAsync(Plan());

        Assert.Equal(UpdateSwap.NOT_APPLIED, exit);
        Assert.Equal("old", Installed());
        Assert.Equal("new_version_incomplete", Result().Reason);
    }

    [Fact]
    public async Task AnInterruptedSwapIsRepairedAtTheNextRun()
    {
        // A crash after the first rename leaves no installation and an old folder.
        Directory.Move(InstallDir, OldDir);

        int exit = await RunAsync(Plan());

        Assert.Equal(UpdateSwap.ROLLED_BACK, exit);
        Assert.Equal("old", Installed());
        Assert.Equal("recovered_after_interruption", Result().Reason);
    }

    [Fact]
    public async Task AStaleOldFolderFromAnEarlierUpdateIsReplaced()
    {
        Directory.CreateDirectory(OldDir);
        File.WriteAllText(Path.Combine(OldDir, "stale.txt"), "stale");
        host.NewAppWritesHealthFile = true;

        int exit = await RunAsync(Plan());

        Assert.Equal(UpdateSwap.OK, exit);
        Assert.False(Directory.Exists(OldDir));
    }

    [Fact]
    public async Task AStartFailureOfTheNewVersionIsRolledBack()
    {
        host.StartFailureForNewApp = true;

        int exit = await RunAsync(Plan());

        Assert.Equal(UpdateSwap.ROLLED_BACK, exit);
        Assert.Equal("old", Installed());
        Assert.Equal("start_failed", Result().Reason);
    }

    [Fact]
    public async Task AHealthFileFromAnEarlierRunIsNotMistakenForProofOfLife()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HealthFile)!);
        File.WriteAllText(HealthFile, "stale");
        host.NewAppWritesHealthFile = false;

        int exit = await RunAsync(Plan(health: 1));

        Assert.Equal(UpdateSwap.ROLLED_BACK, exit);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("not_neighbors")]
    [InlineData("restart_outside")]
    [InlineData("schema")]
    [InlineData("same_folder")]
    public async Task AnInvalidPlanChangesNothing(string kind)
    {
        UpdatePlan good = Plan();
        UpdatePlan plan = kind switch
        {
            "relative" => good with { InstallDir = "app" },
            "not_neighbors" => good with { NewDir = Path.Combine(root, "elsewhere", "app.new") },
            "restart_outside" => good with { RestartPath = Path.Combine(root, "evil.exe") },
            "schema" => good with { SchemaVersion = 2 },
            _ => good with { OldDir = good.NewDir },
        };

        int exit = await RunAsync(plan);

        Assert.Equal(UpdateSwap.INVALID_PLAN, exit);
        Assert.Equal("old", Installed());
        Assert.Empty(host.Started);
    }

    [Fact]
    public async Task TheResultNeverHoldsPathsOrUserData()
    {
        host.NewAppWritesHealthFile = false;

        await RunAsync(Plan(health: 1));

        string text = File.ReadAllText(ResultFile);
        Assert.DoesNotContain(root, text, StringComparison.Ordinal);
    }

    private sealed record StartedApp(string Path, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment) : IStartedProcess
    {
        public bool Killed { get; private set; }

        public bool Exited { get; set; }

        public bool HasExited => Exited;

        public void KillTree()
        {
            Killed = true;
            Exited = true;
        }
    }

    private sealed class FakeHost : IUpdateHost
    {
        private DateTimeOffset now = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        private readonly DateTimeOffset begin;

        public FakeHost()
        {
            begin = now;
        }

        public string Home { get; set; } = string.Empty;

        public bool NewAppWritesHealthFile { get; set; }

        public bool NewAppExitsImmediately { get; set; }

        public bool AppStaysRunning { get; set; }

        public bool StartFailureForNewApp { get; set; }

        public Func<bool>? OldFolderStillThereWhenNewAppStarts { get; set; }

        public bool SawOldFolderWhenNewAppStarted { get; private set; }

        public List<StartedApp> Started { get; } = [];

        public DateTimeOffset Now => now;

        public TimeSpan Elapsed => now - begin;

        public bool IsRunning(int pid) => AppStaysRunning;

        public IStartedProcess Start(string path, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
        {
            bool isNew = environment.ContainsKey("CODEPAD_UPDATE_HEALTH_FILE");
            if (isNew && StartFailureForNewApp)
            {
                throw new InvalidOperationException("cannot start");
            }

            StartedApp app = new StartedApp(path, arguments, new Dictionary<string, string>(environment));
            Started.Add(app);
            if (isNew)
            {
                SawOldFolderWhenNewAppStarted = OldFolderStillThereWhenNewAppStarts?.Invoke() ?? false;
                if (NewAppWritesHealthFile)
                {
                    string file = environment["CODEPAD_UPDATE_HEALTH_FILE"];
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllText(file, "ok");
                }

                app.Exited = NewAppExitsImmediately;
            }

            return app;
        }

        public Task DelayAsync(TimeSpan time, CancellationToken cancellationToken)
        {
            now += time;
            return Task.CompletedTask;
        }
    }
}
