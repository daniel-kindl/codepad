using Codepad.Application.Commands;
using Codepad.Desktop.Workspace;
using Codepad.Domain.Operations;

namespace Codepad.Desktop.Commands;

/// <summary>
/// The built-in commands of the app. The list is made again each time the palette opens, so it follows the open tabs.
/// A command only calls code of the app. It runs no text that the user typed.
/// </summary>
public static class BuiltInCommands
{
    /// <summary>Makes the list of commands.</summary>
    /// <param name="workspace">The open tabs.</param>
    /// <param name="openLibrary">Shows the library and moves the focus to its search box. Null when the app has no library.</param>
    /// <param name="copyResult">Copies the result of the active tab to the clipboard.</param>
    /// <param name="openSettings">Opens the settings window. Null when the app has no settings.</param>
    /// <returns>The commands in the order that the palette lists them.</returns>
    public static IReadOnlyList<AppCommand> Create(WorkspaceViewModel workspace, Action? openLibrary, Func<Task> copyResult, Action? openSettings = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(copyResult);
        List<AppCommand> commands =
        [
            new AppCommand("experiment.new.csharp", "New C# experiment", () => CommandAvailability.Available, () => workspace.NewExperimentAsync(OperationKind.CSharp), "Ctrl+N", "create add"),
            new AppCommand("experiment.new.json", "New JSON experiment", () => CommandAvailability.Available, () => workspace.NewExperimentAsync(OperationKind.Json), null, "create add"),
            new AppCommand("run.csharp", "Run C#", () => RunAvailability(workspace), () => RunActiveAsync(workspace), "Ctrl+Enter", "execute start"),
            new AppCommand("run.cancel", "Cancel run", () => workspace.Runs?.CancelAvailability() ?? new CommandAvailability(false, "No run is in progress."), () => { workspace.Runs?.Cancel(); return Task.CompletedTask; }, null, "stop"),
            new AppCommand("library.open", "Open library", () => openLibrary is null ? new CommandAvailability(false, "The library is not available because CodePad can't use its database.") : CommandAvailability.Available, () => { openLibrary?.Invoke(); return Task.CompletedTask; }, "Ctrl+P", "search find experiment open switch"),
            new AppCommand("result.copy", "Copy result", () => workspace.ActiveResults is { CanCopy: true } ? CommandAvailability.Available : new CommandAvailability(false, "The active experiment has no result to copy."), copyResult),
            new AppCommand("tab.next", "Next tab", () => TabsAvailability(workspace), () => { workspace.Next(); return Task.CompletedTask; }, "Ctrl+Tab"),
            new AppCommand("tab.previous", "Previous tab", () => TabsAvailability(workspace), () => { workspace.Previous(); return Task.CompletedTask; }, "Ctrl+Shift+Tab"),
            new AppCommand("tab.close", "Close tab", () => workspace.ActiveTab is null ? new CommandAvailability(false, "No tab is open.") : CommandAvailability.Available, () => CloseActiveAsync(workspace), "Ctrl+W", "remove"),
            new AppCommand("settings.open", "Settings", () => openSettings is null ? new CommandAvailability(false, "Settings are not available yet.") : CommandAvailability.Available, () => { openSettings?.Invoke(); return Task.CompletedTask; }, "Ctrl+,", "preferences options theme font timeout history"),
        ];

        foreach (ExperimentTabViewModel tab in workspace.Tabs.Where(tab => !ReferenceEquals(tab, workspace.ActiveTab)))
        {
            ExperimentTabViewModel target = tab;
            commands.Add(new AppCommand($"tab.switch.{target.Id}", $"Switch to {target.Title}", () => CommandAvailability.Available, () => { workspace.Activate(target); return Task.CompletedTask; }, null, $"tab experiment {target.KindText}"));
        }

        return commands;
    }

    private static CommandAvailability RunAvailability(WorkspaceViewModel workspace) =>
        workspace.Runs is RunController runs ? runs.Availability(workspace.ActiveTab) : new CommandAvailability(false, "Running code is not available in this build.");

    private static Task RunActiveAsync(WorkspaceViewModel workspace) =>
        workspace.Runs is RunController runs && workspace.ActiveTab is ExperimentTabViewModel tab ? runs.RunAsync(tab) : Task.CompletedTask;

    private static CommandAvailability TabsAvailability(WorkspaceViewModel workspace) =>
        workspace.Tabs.Count > 1 ? CommandAvailability.Available : new CommandAvailability(false, "Only one tab is open.");

    private static async Task CloseActiveAsync(WorkspaceViewModel workspace)
    {
        if (workspace.ActiveTab is ExperimentTabViewModel tab)
        {
            await workspace.CloseAsync(tab).ConfigureAwait(true);
        }
    }
}
