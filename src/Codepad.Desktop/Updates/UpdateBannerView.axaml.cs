using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Codepad.Desktop.Updates;

/// <summary>
/// The notice about updates. A button does one step: it never does the next one. Download and install does not restart. Restart and
/// install saves the work first.
/// </summary>
public sealed partial class UpdateBannerView : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="UpdateBannerView"/> class.</summary>
    public UpdateBannerView()
    {
        InitializeComponent();
    }

    private UpdateViewModel? Model => DataContext as UpdateViewModel;

    private async void OnKeepCheckingClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not null)
        {
            await Model.AnswerPromptAsync(true);
        }
    }

    private async void OnTurnOffClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not null)
        {
            await Model.AnswerPromptAsync(false);
        }
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not null)
        {
            await Model.DownloadAsync();
        }
    }

    private async void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not null)
        {
            await Model.RetryAsync();
        }
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (Model is not null)
        {
            await Model.InstallAsync();
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Model?.CancelDownload();

    private void OnLaterClick(object? sender, RoutedEventArgs e) => Model?.Later();

    private void OnDismissClick(object? sender, RoutedEventArgs e) => Model?.Dismiss();
}
