using Codepad.Application.Editing;
using Codepad.Domain.Results;
using Codepad.Language;

namespace Codepad.Tests.Editing;

public sealed class CSharpAnalyzerTests
{
    private readonly CSharpAnalyzer analyzer = new CSharpAnalyzer();

    [Fact]
    public async Task ValidScriptHasNoDiagnostics()
    {
        EditorAnalysis analysis = await analyzer.AnalyzeAsync("var xs = new List<int> { 1, 2 };\nxs.Sum()", TestContext.Current.CancellationToken);

        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public async Task SyntaxErrorReportsTheExactLineAndColumn()
    {
        EditorAnalysis analysis = await analyzer.AnalyzeAsync("int a = 1;\nint b = ;\n", TestContext.Current.CancellationToken);

        EditorDiagnostic diagnostic = Assert.Single(analysis.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("CS1525", diagnostic.Code);
        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(9, diagnostic.Column);
        Assert.Equal("int a = 1;\nint b = ".Length, diagnostic.Start);
    }

    [Fact]
    public async Task SemanticErrorIsFoundWithoutRunningTheSource()
    {
        // Running this source would write the file. The analysis must only compile it.
        string marker = Path.Combine(Path.GetTempPath(), "codepad-analyzer-" + Guid.NewGuid().ToString("N"));
        string source = $"File.WriteAllText(@\"{marker}\", \"x\");\nint n = \"text\";";

        EditorAnalysis analysis = await analyzer.AnalyzeAsync(source, TestContext.Current.CancellationToken);

        EditorDiagnostic diagnostic = Assert.Single(analysis.Diagnostics);
        Assert.Equal("CS0029", diagnostic.Code);
        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(9, diagnostic.Column);
        Assert.Equal("\"text\"".Length, diagnostic.Length);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task DefaultImportsAndTopLevelAwaitCompile()
    {
        EditorAnalysis analysis = await analyzer.AnalyzeAsync("await Task.Delay(1);\nConsole.WriteLine(Path.GetTempPath());", TestContext.Current.CancellationToken);

        Assert.Empty(analysis.Diagnostics);
    }

    [Fact]
    public async Task WarningHasWarningSeverityAndErrorsComeFirst()
    {
        EditorAnalysis analysis = await analyzer.AnalyzeAsync("int a = 1;\na = a;\nint x = \"a\";", TestContext.Current.CancellationToken);

        Assert.Equal([DiagnosticSeverity.Error, DiagnosticSeverity.Warning], analysis.Diagnostics.Select(item => item.Severity).Take(2));
    }

    [Fact]
    public async Task CancelledAnalysisThrows()
    {
        using CancellationTokenSource cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analyzer.AnalyzeAsync("int a = 1;", cancelled.Token));
    }

    [Fact]
    public async Task EmptySourceHasNoDiagnostics()
    {
        EditorAnalysis analysis = await analyzer.AnalyzeAsync(string.Empty, TestContext.Current.CancellationToken);

        Assert.Empty(analysis.Diagnostics);
    }
}
