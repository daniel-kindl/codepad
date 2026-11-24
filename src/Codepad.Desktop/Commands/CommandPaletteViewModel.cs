using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Codepad.Application.Commands;

namespace Codepad.Desktop.Commands;

/// <summary>
/// The state of the command palette: a search text, the ranked commands and the selection. The palette lists a command
/// that cannot run, with its reason, and never runs it. It reads the availability again when the user chooses a command,
/// so a command that became unavailable does not run. A chosen command runs once: the palette closes first.
/// </summary>
public sealed class CommandPaletteViewModel : INotifyPropertyChanged
{
    private readonly Func<IReadOnlyList<AppCommand>> source;
    private readonly Action<string> reportFailure;
    private string query = string.Empty;
    private int selectedIndex = -1;
    private string statusText = string.Empty;
    private bool isOpen;
    private bool invoking;

    /// <summary>Initializes a new instance of the <see cref="CommandPaletteViewModel"/> class.</summary>
    /// <param name="source">Gives the commands. The palette calls it each time it opens, so the list can follow the open tabs.</param>
    /// <param name="reportFailure">Receives the exception type name when a handler throws. It never holds a message. Null ignores it.</param>
    public CommandPaletteViewModel(Func<IReadOnlyList<AppCommand>> source, Action<string>? reportFailure = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.source = source;
        this.reportFailure = reportFailure ?? (_ => { });
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised once when the palette closes. The window gives the focus back to the control that had it.</summary>
    public event Action? Closed;

    /// <summary>Raised when the palette opens. The window remembers the focus and moves it to the search box.</summary>
    public event Action? Opened;

    /// <summary>Gets the ranked rows.</summary>
    public ObservableCollection<PaletteItem> Items { get; } = [];

    /// <summary>Gets a value indicating whether the palette is open.</summary>
    public bool IsOpen
    {
        get => isOpen;
        private set => Set(ref isOpen, value);
    }

    /// <summary>Gets or sets the search text.</summary>
    public string Query
    {
        get => query;
        set
        {
            string next = value ?? string.Empty;
            if (Set(ref query, next))
            {
                Refresh();
            }
        }
    }

    /// <summary>Gets or sets the index of the selected row, or -1 when no row is selected.</summary>
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (Set(ref selectedIndex, Math.Clamp(value, -1, Items.Count - 1)))
            {
                Raise(nameof(SelectedItem));
                UpdateStatus();
            }
        }
    }

    /// <summary>Gets the selected row, or null.</summary>
    public PaletteItem? SelectedItem => selectedIndex >= 0 && selectedIndex < Items.Count ? Items[selectedIndex] : null;

    /// <summary>Gets a sentence for a live region: the count, the empty state, or why the selected command is unavailable.</summary>
    public string StatusText
    {
        get => statusText;
        private set => Set(ref statusText, value);
    }

    /// <summary>Gets a value indicating whether no command matches the text.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>Opens the palette with an empty text. An open palette stays open and keeps its text.</summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        invoking = false;
        query = string.Empty;
        Raise(nameof(Query));
        IsOpen = true;
        Refresh();
        Opened?.Invoke();
    }

    /// <summary>Closes the palette.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Closed?.Invoke();
    }

    /// <summary>Moves the selection. It stops at the first and the last row.</summary>
    /// <param name="offset">The number of rows. A negative number moves up.</param>
    public void MoveSelection(int offset)
    {
        if (Items.Count > 0)
        {
            SelectedIndex = Math.Clamp(selectedIndex + offset, 0, Items.Count - 1);
        }
    }

    /// <summary>
    /// Runs the selected command. A command that cannot run does not run: the status says why and the palette stays open.
    /// </summary>
    /// <returns>True when the palette ran a command.</returns>
    public async Task<bool> InvokeAsync()
    {
        if (!IsOpen || invoking || SelectedItem is not PaletteItem item)
        {
            return false;
        }

        // The state can change between the list and the choice, for example when a run ends. The choice decides.
        CommandAvailability now = item.Command.Availability();
        if (!now.Enabled)
        {
            StatusText = $"{item.Label} is unavailable. {now.Reason}";
            return false;
        }

        invoking = true;
        Close();
        try
        {
            await item.Command.Execute().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A handler is code of the app. A bug must not stop the window. Its message can hold user text, so only the type goes out.
            reportFailure(exception.GetType().Name);
        }

        return true;
    }

    private void Refresh()
    {
        IReadOnlyList<CommandMatch> matches = CommandSearch.Rank(source(), query);
        Items.Clear();
        foreach (CommandMatch match in matches)
        {
            Items.Add(new PaletteItem(match.Command, match.Availability.Enabled, match.Availability.Reason));
        }

        Raise(nameof(IsEmpty));
        selectedIndex = Items.Count > 0 ? 0 : -1;
        Raise(nameof(SelectedIndex));
        Raise(nameof(SelectedItem));
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (Items.Count == 0)
        {
            StatusText = $"No command matches “{query.Trim()}”.";
        }
        else if (SelectedItem is { IsEnabled: false } disabled && query.Trim().Length > 0)
        {
            StatusText = disabled.Reason ?? string.Empty;
        }
        else
        {
            StatusText = Items.Count == 1 ? "1 command" : $"{Items.Count} commands";
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
