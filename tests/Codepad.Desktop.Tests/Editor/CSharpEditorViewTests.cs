using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Rendering;
using Codepad.Application.Editing;
using Codepad.Desktop.Editor;
using Codepad.Desktop.Shell;
using Codepad.Language;

namespace Codepad.Desktop.Tests.Editor;

/// <summary>Headless UI tests of the C# editor with the real Roslyn language service.</summary>
public sealed class CSharpEditorViewTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

    [AvaloniaFact]
    public async Task TypingAnInvalidConstructShowsADiagnosticAtTheRightLineAndColumn()
    {
        (MainWindow window, CSharpEditorView view) = Show("light");

        view.TextEditor.TextArea.Focus();
        window.KeyTextInput("int a = 1;\nint b = ;");
        await WaitAsync(() => view.List.ItemCount == 1);

        DiagnosticItem item = Assert.IsType<DiagnosticItem>(view.List.Items.Cast<object>().Single());
        Assert.Equal(2, item.Source.Line);
        Assert.Equal(9, item.Source.Column);
        Assert.Equal("1 error", ((CSharpEditorViewModel)view.DataContext!).SummaryText);
        window.Close();
    }

    [AvaloniaFact]
    public async Task EnterOnADiagnosticMovesTheCaretToItsLineAndColumn()
    {
        (MainWindow window, CSharpEditorView view) = Show("light");
        CSharpEditorViewModel model = (CSharpEditorViewModel)view.DataContext!;
        model.Load("int a = 1;\nint b = ;\nint c = 3;");
        await WaitAsync(() => view.List.ItemCount == 1);

        view.List.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.List.Focus());
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Equal(2, view.TextEditor.TextArea.Caret.Line);
        Assert.Equal(9, view.TextEditor.TextArea.Caret.Column);
        Assert.Same(view.TextEditor.TextArea, window.FocusManager?.GetFocusedElement());
        window.Close();
    }

    [AvaloniaFact]
    public async Task F8MovesToTheNextDiagnosticAndWrapsAround()
    {
        (MainWindow window, CSharpEditorView view) = Show("light");
        CSharpEditorViewModel model = (CSharpEditorViewModel)view.DataContext!;
        model.Load("int a = ;\nint b = ;");
        await WaitAsync(() => view.List.ItemCount == 2);
        view.TextEditor.TextArea.Focus();
        view.TextEditor.CaretOffset = 0;

        window.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.F8, RawInputModifiers.None);
        Assert.Equal(1, view.TextEditor.TextArea.Caret.Line);

        window.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.F8, RawInputModifiers.None);
        Assert.Equal(2, view.TextEditor.TextArea.Caret.Line);

        window.KeyPressQwerty(PhysicalKey.F8, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.F8, RawInputModifiers.None);
        Assert.Equal(1, view.TextEditor.TextArea.Caret.Line);
        window.Close();
    }

    [AvaloniaFact]
    public async Task UndoAndRedoRestoreTheTextAndTheDiagnostics()
    {
        (MainWindow window, CSharpEditorView view) = Show("light");
        CSharpEditorViewModel model = (CSharpEditorViewModel)view.DataContext!;
        model.Load("int a = 1;");
        await WaitAsync(() => model.SummaryText == CSharpEditorViewModel.NO_PROBLEMS_TEXT);
        view.TextEditor.TextArea.Focus();
        view.TextEditor.CaretOffset = view.TextEditor.Document.TextLength;
        window.KeyTextInput("\nint b = ;");
        await WaitAsync(() => view.List.ItemCount == 1);

        view.TextEditor.Undo();
        Assert.Equal("int a = 1;", view.TextEditor.Text);
        Assert.Equal("int a = 1;", model.Text);
        await WaitAsync(() => view.List.ItemCount == 0 && model.SummaryText == CSharpEditorViewModel.NO_PROBLEMS_TEXT);

        view.TextEditor.Redo();
        await WaitAsync(() => view.List.ItemCount == 1);
        window.Close();
    }

    [AvaloniaFact]
    public async Task PastingMultipleLinesKeepsTheCaretAtTheEndOfThePaste()
    {
        (MainWindow window, CSharpEditorView view) = Show("light");
        view.TextEditor.TextArea.Focus();

        view.TextEditor.Document.Insert(0, "a\nb\nc");
        view.TextEditor.CaretOffset = view.TextEditor.Document.TextLength;
        await Task.CompletedTask;

        Assert.Equal(3, view.TextEditor.TextArea.Caret.Line);
        Assert.Equal(2, view.TextEditor.TextArea.Caret.Column);
        window.Close();
    }

    [AvaloniaFact]
    public async Task EditorReportsContentChangesToAutosaveListeners()
    {
        (MainWindow window, CSharpEditorView view) = Show("light");
        CSharpEditorViewModel model = (CSharpEditorViewModel)view.DataContext!;
        List<string> changes = [];
        model.ContentChanged += changes.Add;

        view.TextEditor.TextArea.Focus();
        window.KeyTextInput("x");
        await Task.CompletedTask;

        Assert.Equal(["x"], changes);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light", "EditorKeywordBrush", "int a;", 0)]
    [InlineData("light", "EditorCommentBrush", "int a; // note", 7)]
    [InlineData("light", "EditorStringBrush", "string s = \"text\";", 11)]
    [InlineData("light", "EditorNumberBrush", "int n = 42;", 8)]
    [InlineData("light", "EditorTypeBrush", "List<int> n;", 0)]
    [InlineData("light", "EditorPunctuationBrush", "int a;", 5)]
    [InlineData("dark", "EditorKeywordBrush", "int a;", 0)]
    [InlineData("dark", "EditorCommentBrush", "int a; // note", 7)]
    [InlineData("dark", "EditorStringBrush", "string s = \"text\";", 11)]
    [InlineData("dark", "EditorNumberBrush", "int n = 42;", 8)]
    [InlineData("dark", "EditorTypeBrush", "List<int> n;", 0)]
    [InlineData("dark", "EditorPunctuationBrush", "int a;", 5)]
    public void SyntaxColorReachesTheDrawnText(string theme, string brushKey, string source, int offset)
    {
        (MainWindow window, CSharpEditorView view) = Show(theme);
        CSharpEditorViewModel model = (CSharpEditorViewModel)view.DataContext!;
        model.Load(source);

        TextView textView = view.TextEditor.TextArea.TextView;
        textView.EnsureVisualLines();
        VisualLine line = textView.GetVisualLine(1)!;
        VisualLineElement element = line.Elements.Single(item => item.RelativeTextOffset + line.FirstDocumentLine.Offset <= offset && offset < item.RelativeTextOffset + line.FirstDocumentLine.Offset + item.DocumentLength);

        Color expected = ((SolidColorBrush)Lookup(view, brushKey)).Color;
        Assert.Equal(expected, ((ISolidColorBrush)element.TextRunProperties.ForegroundBrush!).Color);
        window.Close();
    }

    public static TheoryData<string, string, int, int> ScreenshotCases()
    {
        TheoryData<string, string, int, int> cases = new();
        foreach (string theme in new[] { "light", "dark" })
        {
            foreach (string state in new[] { "empty", "valid", "diagnostics", "long" })
            {
                cases.Add(theme, state, 1000, 640);
            }

            cases.Add(theme, "diagnostics", 640, 400);
        }

        return cases;
    }

    [AvaloniaTheory]
    [MemberData(nameof(ScreenshotCases))]
    public async Task CapturesTheEditor(string theme, string state, int width, int height)
    {
        (MainWindow window, CSharpEditorView view) = Show(theme, width, height);
        CSharpEditorViewModel model = (CSharpEditorViewModel)view.DataContext!;
        model.Load(Source(state));
        await WaitAsync(() => model.SummaryText != CSharpEditorViewModel.CHECKING_TEXT);
        if (state == "diagnostics")
        {
            view.List.SelectedIndex = 0;
            view.GoTo((DiagnosticItem)view.List.SelectedItem!);
        }

        WriteableBitmap? frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        string folder = Path.Combine(AppContext.BaseDirectory, "screenshots", OperatingSystem.IsWindows() ? "windows" : "linux");
        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, $"editor-{theme}-{state}-{width}x{height}.png"), PngBitmapEncoderOptions.Default);
        window.Close();
    }

    private static string Source(string state) => state switch
    {
        "empty" => string.Empty,
        "valid" => "// Sum the squares.\nList<int> numbers = new() { 1, 2, 3 };\nint total = numbers.Sum(n => n * n);\nConsole.WriteLine($\"Total: {total}\");\ntotal",
        "diagnostics" => "int a = 1;\na = a;\nint b = ;\nstring s = 5;\nUnknown.Call();",
        _ => string.Join("\n", Enumerable.Range(1, 120).Select(i => $"int value{i} = {i} * 2; // line {i}")) + "\nint broken = ;",
    };

    private static (MainWindow Window, CSharpEditorView View) Show(string theme, int width = 1000, int height = 640)
    {
        CSharpEditorViewModel editor = new CSharpEditorViewModel(new CSharpHighlighter(), new CSharpAnalyzer(), TimeProvider.System, TimeSpan.FromMilliseconds(20));
        MainWindow window = new MainWindow
        {
            DataContext = new MainWindowViewModel([], editor),
            Width = width,
            Height = height,
            RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();
        CSharpEditorView view = window.GetVisualDescendants().OfType<CSharpEditorView>().Single();
        return (window, view);
    }

    private static IBrush Lookup(Control control, string key)
    {
        Assert.True(control.TryFindResource(key, control.ActualThemeVariant, out object? value));
        return Assert.IsAssignableFrom<IBrush>(value);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + _timeout;
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            Assert.True(DateTime.UtcNow < deadline, "The UI did not reach the expected state.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
