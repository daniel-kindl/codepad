using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Codepad.Application.Json;
using Codepad.Desktop.Json;

namespace Codepad.Desktop.Tests.Json;

/// <summary>Headless UI tests of the live JSON tool with the real evaluator.</summary>
public sealed class JsonToolViewTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

    [AvaloniaFact]
    public async Task TypingValidJsonShowsTheFormattedTextAndTheTreeWithoutRun()
    {
        (Window window, JsonToolView view, JsonToolViewModel model) = Show("light");

        view.Input.Focus();
        window.KeyTextInput("{\"a\":1,\"b\":[2,3]}");
        await WaitAsync(() => model.FormattedText.Length > 0);

        Assert.Equal("{\n  \"a\": 1,\n  \"b\": [\n    2,\n    3\n  ]\n}", model.FormattedText.ReplaceLineEndings("\n"));
        Assert.Equal("{\n  \"a\": 1,\n  \"b\": [\n    2,\n    3\n  ]\n}", Named<TextBox>(window, "Formatted JSON").Text?.ReplaceLineEndings("\n"));
        Assert.True(model.Results.IsTree);
        Assert.Equal("Valid JSON", Named<TextBlock>(window, "JSON status").Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task InvalidJsonShowsTheLocationAndKeepsTheTypedInput()
    {
        (Window window, JsonToolView view, JsonToolViewModel model) = Show("light");

        view.Input.Focus();
        window.KeyTextInput("{\n  \"a\": ,\n}");
        await WaitAsync(() => model.HasProblem);

        Assert.Equal("{\n  \"a\": ,\n}", view.Input.Text?.ReplaceLineEndings("\n"));
        Assert.Contains("line 2, column 8", Named<TextBlock>(window, "JSON status").Text, StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void TheInputAndTheFormattedPreviewHaveAccessibleNames()
    {
        (Window window, _, _) = Show("light");

        Assert.False(Named<TextBox>(window, "JSON input").IsReadOnly);
        Assert.True(Named<TextBox>(window, "Formatted JSON").IsReadOnly);
        Assert.True(Named<TextBox>(window, "JSON input").Focusable);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light", "valid")]
    [InlineData("dark", "valid")]
    [InlineData("light", "invalid")]
    [InlineData("dark", "invalid")]
    public async Task CapturesTheTool(string theme, string state)
    {
        (Window window, JsonToolView view, JsonToolViewModel model) = Show(theme);

        view.Input.Focus();
        window.KeyTextInput("{\"name\":\"CodePad\",\"tags\":[\"a\",\"b\"],\"size\":{\"w\":3,\"h\":4}}");
        await WaitAsync(() => model.FormattedText.Length > 0);
        if (state == "invalid")
        {
            model.OnTextEdited("{\"name\":\"CodePad\",\n \"tags\": [\"a\",,]}");
            view.Input.Text = "{\"name\":\"CodePad\",\n \"tags\": [\"a\",,]}";
            await WaitAsync(() => model.HasProblem);
        }

        WriteableBitmap? frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        string folder = Path.Combine(AppContext.BaseDirectory, "screenshots", OperatingSystem.IsWindows() ? "windows" : "linux");
        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, $"json-{state}-{theme}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        window.Close();
    }

    private static (Window Window, JsonToolView View, JsonToolViewModel Model) Show(string theme)
    {
        JsonToolViewModel model = new JsonToolViewModel(new JsonEvaluator(), TimeProvider.System, TimeSpan.FromMilliseconds(30));
        JsonToolView view = new JsonToolView { DataContext = model };
        Window window = new Window
        {
            Content = view,
            Width = 1000,
            Height = 640,
            RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();
        return (window, view, model);
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

            Assert.True(DateTime.UtcNow < deadline, "The condition did not become true.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static T Named<T>(Window window, string name)
        where T : Control => window.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);
}
