using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Codepad.Application.Startup;
using Codepad.Desktop.Shell;

namespace Codepad.Desktop.Tests.Shell;

/// <summary>
/// Captures the shell in each theme, size and state as visual verification evidence. The images are
/// not golden files: the tests do not compare pixels, because the visual output is not a fixed
/// requirement yet. CI uploads the images from bin/.../screenshots as an artifact for review.
/// </summary>
public sealed class ShellScreenshotTests
{
    public static TheoryData<string, int, int, bool> Cases()
    {
        TheoryData<string, int, int, bool> cases = new();
        foreach (string theme in new[] { "light", "dark" })
        {
            // 640 x 400 is the minimum window size. 1280 x 800 is one wide size.
            foreach ((int width, int height) in new[] { (640, 400), (1280, 800) })
            {
                cases.Add(theme, width, height, false);
                cases.Add(theme, width, height, true);
            }
        }

        return cases;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void CapturesTheShell(string theme, int width, int height, bool warning)
    {
        IReadOnlyList<StartupNotice> notices = warning
            ? [new StartupNotice(StartupNoticeKind.SettingsUnusable, "invalid_json"), new StartupNotice(StartupNoticeKind.DataFolderUnavailable, "IOException")]
            : [];
        MainWindow window = new MainWindow
        {
            DataContext = new MainWindowViewModel(notices),
            Width = width,
            Height = height,
            RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();

        WriteableBitmap? frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.Equal(width, frame.PixelSize.Width);
        Assert.Equal(height, frame.PixelSize.Height);
        string folder = Path.Combine(AppContext.BaseDirectory, "screenshots", OperatingSystem.IsWindows() ? "windows" : "linux");
        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, $"shell-{theme}-{width}x{height}-{(warning ? "warning" : "ready")}.png"), PngBitmapEncoderOptions.Default);
        window.Close();
    }
}
