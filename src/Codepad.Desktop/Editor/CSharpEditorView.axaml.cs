using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Codepad.Application.Editing;
using Codepad.Domain.Results;

namespace Codepad.Desktop.Editor;

/// <summary>
/// The C# editor with syntax coloring, diagnostic underlines and a diagnostics list. Enter or a double click on a
/// diagnostic moves the caret to it. F8 moves to the next diagnostic. Escape leaves the editor for the diagnostics list.
/// </summary>
public sealed partial class CSharpEditorView : UserControl
{
    private const double MIN_FONT_SIZE = 8;
    private const double MAX_FONT_SIZE = 48;

    private SyntaxColorizer? colorizer;
    private CSharpEditorViewModel? model;

    /// <summary>Initializes a new instance of the <see cref="CSharpEditorView"/> class.</summary>
    public CSharpEditorView()
    {
        InitializeComponent();
        Editor.TextChanged += OnEditorTextChanged;
        Editor.AddHandler(KeyDownEvent, OnEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Editor.AddHandler(PointerWheelChangedEvent, OnEditorWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DiagnosticList.AddHandler(KeyDownEvent, OnListKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DiagnosticList.DoubleTapped += OnListDoubleTapped;
        ActualThemeVariantChanged += (_, _) => RefreshColors();
    }

    /// <summary>Gets the editor control. Tests use it to type and to read the caret.</summary>
    internal TextEditor TextEditor => Editor;

    /// <summary>Gets the diagnostics list. Tests use it to select a row.</summary>
    internal ListBox List => DiagnosticList;

    /// <summary>Moves the caret to a diagnostic, selects its range and gives the editor the focus.</summary>
    /// <param name="item">The diagnostic to show.</param>
    internal void GoTo(DiagnosticItem item)
    {
        EditorDiagnostic diagnostic = item.Source;
        int start = Math.Min(diagnostic.Start, Editor.Document.TextLength);
        int length = Math.Min(Math.Max(diagnostic.Length, 0), Editor.Document.TextLength - start);
        Editor.Select(start, length);
        Editor.CaretOffset = start;
        Editor.ScrollTo(diagnostic.Line, diagnostic.Column);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.TextArea.Focus();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (model is not null)
        {
            model.DocumentLoaded -= OnDocumentLoaded;
            model.Diagnostics.CollectionChanged -= OnDiagnosticsChanged;
        }

        model = DataContext as CSharpEditorViewModel;
        if (colorizer is not null)
        {
            Editor.TextArea.TextView.LineTransformers.Remove(colorizer);
            colorizer = null;
        }

        if (model is null)
        {
            return;
        }

        colorizer = new SyntaxColorizer(model.Highlighter, BrushFor, () => [.. model.Diagnostics.Select(item => item.Source)], UnderlineBrushFor);
        Editor.TextArea.TextView.LineTransformers.Add(colorizer);
        model.DocumentLoaded += OnDocumentLoaded;
        model.Diagnostics.CollectionChanged += OnDiagnosticsChanged;
        OnDocumentLoaded(model.Text);
    }

    private void OnDocumentLoaded(string text)
    {
        Editor.Document = new TextDocument(text);
        colorizer?.Invalidate();
        Editor.TextArea.TextView.Redraw();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        colorizer?.Invalidate();
        model?.OnTextEdited(Editor.Text);
    }

    private void OnDiagnosticsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Editor.TextArea.TextView.Redraw();

    private void RefreshColors() => Editor.TextArea.TextView.Redraw();

    private IBrush? BrushFor(HighlightKind kind) => Resource(kind switch
    {
        HighlightKind.Keyword => "EditorKeywordBrush",
        HighlightKind.Comment => "EditorCommentBrush",
        HighlightKind.StringLiteral => "EditorStringBrush",
        HighlightKind.Number => "EditorNumberBrush",
        HighlightKind.Type => "EditorTypeBrush",
        _ => "EditorPunctuationBrush",
    });

    private IBrush? UnderlineBrushFor(DiagnosticSeverity severity) => Resource(severity switch
    {
        DiagnosticSeverity.Error => "EditorErrorBrush",
        DiagnosticSeverity.Warning => "EditorWarningBrush",
        _ => "EditorInfoBrush",
    });

    private IBrush? Resource(string key)
    {
        ThemeVariant variant = ActualThemeVariant ?? ThemeVariant.Default;
        return this.TryFindResource(key, variant, out object? value) ? value as IBrush : null;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            // Tab inserts a tab character in the editor, so Escape is the way out of the editor with the keyboard.
            DiagnosticList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.F8 && model is not null && model.Diagnostics.Count > 0)
        {
            int caret = Editor.CaretOffset;
            DiagnosticItem next = model.Diagnostics.FirstOrDefault(item => item.Source.Start > caret) ?? model.Diagnostics[0];
            GoTo(next);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.OemPlus or Key.Add)
        {
            Zoom(1);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.OemMinus or Key.Subtract)
        {
            Zoom(-1);
            e.Handled = true;
        }
    }

    private void OnEditorWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Delta.Y != 0)
        {
            Zoom(e.Delta.Y > 0 ? 1 : -1);
            e.Handled = true;
        }
    }

    private void Zoom(int steps) => Editor.FontSize = Math.Clamp(Editor.FontSize + steps, MIN_FONT_SIZE, MAX_FONT_SIZE);

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DiagnosticList.SelectedItem is DiagnosticItem item)
        {
            GoTo(item);
            e.Handled = true;
        }
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DiagnosticList.SelectedItem is DiagnosticItem item)
        {
            GoTo(item);
        }
    }
}
