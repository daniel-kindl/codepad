using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
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
    private bool replacingDocument;
    private CompletionWindow? completionWindow;
    private CancellationTokenSource? completionRequest;

    /// <summary>Initializes a new instance of the <see cref="CSharpEditorView"/> class.</summary>
    public CSharpEditorView()
    {
        InitializeComponent();
        Editor.TextChanged += OnEditorTextChanged;
        Editor.TextArea.TextEntering += OnTextEntering;
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.AddHandler(KeyDownEvent, OnEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Editor.AddHandler(PointerWheelChangedEvent, OnEditorWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DiagnosticList.AddHandler(KeyDownEvent, OnListKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DiagnosticList.DoubleTapped += OnListDoubleTapped;
        ActualThemeVariantChanged += (_, _) => RefreshColors();
    }

    /// <summary>Gets the editor control. Tests use it to type and to read the caret.</summary>
    internal TextEditor TextEditor => Editor;

    /// <summary>Gets the open completion window, or null. Tests use it to read the suggestions.</summary>
    internal CompletionWindow? OpenCompletion => completionWindow;

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
            model.Preferences.Changed -= ApplyPreferences;
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
        model.Preferences.Changed += ApplyPreferences;
        ApplyPreferences();
        OnDocumentLoaded(model.Text);
    }

    // The settings change the font size and the indentation of all open editors at once.
    private void ApplyPreferences()
    {
        if (model is null)
        {
            return;
        }

        Editor.FontSize = model.Preferences.FontSize;
        Editor.Options.IndentationSize = model.Preferences.IndentationWidth;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.TextArea.TextView.Redraw();
    }

    private void OnDocumentLoaded(string text)
    {
        CancelCompletion();
        replacingDocument = true;
        try
        {
            Editor.Document = new TextDocument(text);
        }
        finally
        {
            replacingDocument = false;
        }

        colorizer?.Invalidate();
        Editor.TextArea.TextView.Redraw();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        colorizer?.Invalidate();
        if (!replacingDocument)
        {
            model?.OnTextEdited(Editor.Text);
        }
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

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        // A character that cannot be part of a name ends the word. The window closes and inserts nothing, so the
        // typed punctuation stays exactly as typed.
        if (completionWindow is not null && !string.IsNullOrEmpty(e.Text) && !IsNameCharacter(e.Text[0]))
        {
            completionWindow.Close();
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (completionWindow is null && !string.IsNullOrEmpty(e.Text) && (IsNameCharacter(e.Text[0]) || e.Text[0] == '.'))
        {
            _ = ShowCompletionAsync();
        }
    }

    private static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    private void CancelCompletion()
    {
        completionRequest?.Cancel();
        completionRequest?.Dispose();
        completionRequest = null;
        completionWindow?.Close();
    }

    private async Task ShowCompletionAsync()
    {
        if (model is null || !model.CanComplete)
        {
            return;
        }

        completionRequest?.Cancel();
        completionRequest?.Dispose();
        CancellationTokenSource source = new CancellationTokenSource();
        completionRequest = source;
        int caret = Editor.CaretOffset;
        CSharpEditorViewModel requestedModel = model;
        long version = requestedModel.DocumentVersion;

        CompletionResult? result = await requestedModel.GetCompletionAsync(caret, source.Token);

        // Show only suggestions for the text and the caret that asked for them, in the document that is still open.
        if (result is null || result.Items.Count == 0 || source.IsCancellationRequested || !ReferenceEquals(model, requestedModel)
            || requestedModel.DocumentVersion != version || Editor.CaretOffset != caret || completionWindow is not null)
        {
            return;
        }

        CompletionWindow window = new CompletionWindow(Editor.TextArea)
        {
            StartOffset = result.ReplaceStart,
            CloseWhenCaretAtBeginning = true,
        };
        foreach (CompletionItem item in result.Items)
        {
            window.CompletionList.CompletionData.Add(new CompletionData(item));
        }

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(completionWindow, window))
            {
                completionWindow = null;
            }
        };
        completionWindow = window;
        window.Show();
        if (result.ReplaceLength > 0)
        {
            window.CompletionList.SelectItem(Editor.Document.GetText(result.ReplaceStart, result.ReplaceLength));
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (completionWindow is not null && e.Key == Key.Escape)
        {
            // The completion window handles Escape and closes. The editor must not also leave for the list.
            return;
        }

        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            CancelCompletion();
            _ = ShowCompletionAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            // Tab inserts a tab character in the editor, so Escape is the way out of the editor with the keyboard.
            DiagnosticList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.F8 && model is not null && model.Diagnostics.Count > 0)
        {
            int caret = Editor.CaretOffset;
            // The list shows errors first, so F8 uses source order.
            List<DiagnosticItem> ordered = [.. model.Diagnostics.OrderBy(item => item.Source.Start)];
            DiagnosticItem next = ordered.FirstOrDefault(item => item.Source.Start > caret) ?? ordered[0];
            GoTo(next);
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.OemPlus or Key.Add)
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
