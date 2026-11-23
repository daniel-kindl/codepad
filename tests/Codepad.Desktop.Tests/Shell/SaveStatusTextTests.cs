using Codepad.Application.Experiments;
using Codepad.Desktop.Shell;

namespace Codepad.Desktop.Tests.Shell;

public sealed class SaveStatusTextTests
{
    [Theory]
    [InlineData(SaveState.Clean, "")]
    [InlineData(SaveState.Dirty, "Unsaved changes")]
    [InlineData(SaveState.Saving, "Saving…")]
    [InlineData(SaveState.Saved, "Saved")]
    public void StatesWithoutAnErrorHaveShortText(SaveState state, string expected)
    {
        Assert.Equal(expected, SaveStatusText.For(state, null));
    }

    [Theory]
    [InlineData("disk_full", "The disk is full")]
    [InlineData("read_only", "read-only")]
    [InlineData("busy", "in use")]
    public void ErrorTextNamesTheCauseAndTheRetryAndSaysTheTextIsKept(string reason, string expectedPart)
    {
        string text = SaveStatusText.For(SaveState.Error, reason);

        Assert.Contains(expectedPart, text, StringComparison.Ordinal);
        Assert.Contains("Retry", text, StringComparison.Ordinal);
        Assert.Contains("still in the editor", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownReasonStillGivesAUsableErrorAndHidesTheCode()
    {
        string text = SaveStatusText.For(SaveState.Error, "marker-reason-9c1d");

        Assert.Contains("Retry", text, StringComparison.Ordinal);
        Assert.DoesNotContain("marker-reason-9c1d", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheErrorStateOffersARetry()
    {
        Assert.False(SaveStatusText.OffersRetry(SaveState.Saved));
        Assert.False(SaveStatusText.OffersRetry(SaveState.Dirty));
        Assert.True(SaveStatusText.OffersRetry(SaveState.Error));
    }
}
