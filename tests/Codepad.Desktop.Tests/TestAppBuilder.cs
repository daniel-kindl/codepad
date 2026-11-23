using Avalonia;
using Avalonia.Headless;
using Codepad.Desktop;
using Codepad.Desktop.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Codepad.Desktop.Tests;

/// <summary>
/// Starts the real <see cref="App"/> on the headless platform. Skia draws the frames, so the
/// tests can capture screenshots. The Inter font makes the text the same on Linux and Windows.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
