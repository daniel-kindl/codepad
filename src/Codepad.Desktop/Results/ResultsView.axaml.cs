using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Codepad.Desktop.Results;

/// <summary>The result panel: the status of the last run, and tabs for the value, the problems, and the two output streams.</summary>
public sealed partial class ResultsView : UserControl
{
    private ResultsViewModel? model;

    /// <summary>Initializes a new instance of the <see cref="ResultsView"/> class.</summary>
    public ResultsView()
    {
        InitializeComponent();
    }

    /// <summary>Gets the table control. Tests use it to read the columns and the rows.</summary>
    internal DataGrid Grid => ValueGrid;

    /// <summary>Gets the tree control. Tests use it to read the rows.</summary>
    internal TreeView TreeControl => ValueTree;

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (model is not null)
        {
            model.PropertyChanged -= OnModelChanged;
        }

        model = DataContext as ResultsViewModel;
        if (model is not null)
        {
            model.PropertyChanged += OnModelChanged;
            RebuildTable();
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsViewModel.Table))
        {
            RebuildTable();
        }
    }

    private void RebuildTable()
    {
        ValueGrid.Columns.Clear();
        ResultTable? table = model?.Table;
        if (table is null)
        {
            ValueGrid.ItemsSource = null;
            return;
        }

        for (int index = 0; index < table.Columns.Count; index++)
        {
            ValueGrid.Columns.Add(new DataGridTextColumn
            {
                Header = table.Columns[index],
                Binding = new Binding { Path = $"[{index}]", Mode = BindingMode.OneWay },
                IsReadOnly = true,
            });
        }

        ValueGrid.ItemsSource = table.Rows;
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        string? text = model?.GetCopyText();
        if (text is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
