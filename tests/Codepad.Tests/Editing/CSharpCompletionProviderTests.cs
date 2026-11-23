using Codepad.Application.Editing;
using Codepad.Language;

namespace Codepad.Tests.Editing;

public sealed class CSharpCompletionProviderTests
{
    private readonly CSharpCompletionProvider provider = new CSharpCompletionProvider();

    private async Task<CompletionResult> CompleteAtEndAsync(string source) =>
        await provider.GetCompletionsAsync(source, source.Length, TestContext.Current.CancellationToken);

    [Fact]
    public async Task MemberAccessOfAStaticTypeSuggestsMatchingMembersAndReplacesTheTypedWord()
    {
        const string SOURCE = "Console.Wri";

        CompletionResult result = await CompleteAtEndAsync(SOURCE);

        Assert.Equal(SOURCE.IndexOf("Wri", StringComparison.Ordinal), result.ReplaceStart);
        Assert.Equal(3, result.ReplaceLength);
        Assert.Contains(result.Items, item => item.Text == "Write" && item.Kind == CompletionKind.Method);
        Assert.Contains(result.Items, item => item.Text == "WriteLine" && item.Kind == CompletionKind.Method);
        Assert.DoesNotContain(result.Items, item => item.Text == "ReadLine");
        Assert.Single(result.Items, item => item.Text == "WriteLine");
        Assert.All(result.Items, item => Assert.DoesNotContain("(", item.Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MethodItemShowsTheReturnTypeAndTheOverloadCount()
    {
        CompletionResult result = await CompleteAtEndAsync("Console.WriteLi");

        CompletionItem item = Assert.Single(result.Items);
        Assert.StartsWith("void", item.Detail, StringComparison.Ordinal);
        Assert.Contains("overload", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DotWithNoWordSuggestsEveryMember()
    {
        CompletionResult result = await CompleteAtEndAsync("Console.");

        Assert.Equal("Console.".Length, result.ReplaceStart);
        Assert.Equal(0, result.ReplaceLength);
        Assert.Contains(result.Items, item => item.Text == "ReadLine");
        Assert.Contains(result.Items, item => item.Text == "WriteLine");
    }

    [Fact]
    public async Task InstanceMemberAccessSuggestsInstanceMembersAndExtensionMethods()
    {
        CompletionResult result = await CompleteAtEndAsync("string s = \"abc\";\ns.");

        Assert.Contains(result.Items, item => item.Text == "Length" && item.Kind == CompletionKind.Member);
        Assert.Contains(result.Items, item => item.Text == "ToUpper");
        Assert.Contains(result.Items, item => item.Text == "Select");
        Assert.DoesNotContain(result.Items, item => item.Text == "Empty");
    }

    [Fact]
    public async Task StaticMemberAccessLeavesOutInstanceMembers()
    {
        CompletionResult result = await CompleteAtEndAsync("string.");

        Assert.Contains(result.Items, item => item.Text == "Empty");
        Assert.DoesNotContain(result.Items, item => item.Text == "Length");
    }

    [Fact]
    public async Task MemberAccessInsideAnExpressionAfterOtherCodeWorks()
    {
        CompletionResult result = await CompleteAtEndAsync("var items = new List<int> { 1 };\nint n = items.Cou");

        Assert.Contains(result.Items, item => item.Text == "Count");
        Assert.Contains(result.Items, item => item.Text == "Count" && item.Kind == CompletionKind.Member);
    }

    [Fact]
    public async Task NamespaceMemberAccessSuggestsTypes()
    {
        CompletionResult result = await CompleteAtEndAsync("System.Collections.Generic.Dic");

        Assert.Contains(result.Items, item => item.Text == "Dictionary" && item.Kind == CompletionKind.TypeName);
    }

    [Fact]
    public async Task KeywordsMatchThePrefixCaseInsensitively()
    {
        CompletionResult result = await CompleteAtEndAsync("whi");

        Assert.Contains(result.Items, item => item.Text == "while" && item.Kind == CompletionKind.Keyword);
        Assert.DoesNotContain(result.Items, item => item.Text == "for" && item.Kind == CompletionKind.Keyword);
    }

    [Fact]
    public async Task MemberContextHasNoKeywords()
    {
        CompletionResult result = await CompleteAtEndAsync("Console.");

        Assert.DoesNotContain(result.Items, item => item.Kind == CompletionKind.Keyword);
    }

    [Fact]
    public async Task LocalsInScopeAreSuggestedWithTheirType()
    {
        CompletionResult result = await CompleteAtEndAsync("int counter = 1;\nstring cache = \"\";\ncou");

        CompletionItem item = Assert.Single(result.Items, candidate => candidate.Kind == CompletionKind.Variable || candidate.Kind == CompletionKind.Member);
        Assert.Equal("counter", item.Text);
        Assert.Equal("int", item.Detail);
    }

    [Fact]
    public async Task ALocalOfAnotherScopeIsNotSuggested()
    {
        const string SOURCE = "void Work()\n{\n    int hidden = 1;\n}\nint visible = 2;\nhid";

        CompletionResult result = await CompleteAtEndAsync(SOURCE);

        Assert.DoesNotContain(result.Items, item => item.Text == "hidden");
    }

    [Fact]
    public async Task ALocalInTheEnclosingMethodAndItsParametersAreSuggested()
    {
        const string SOURCE = "int Work(int input)\n{\n    int total = 1;\n    return tot";

        CompletionResult result = await provider.GetCompletionsAsync(SOURCE, SOURCE.Length, TestContext.Current.CancellationToken);

        Assert.Contains(result.Items, item => item.Text == "total" && item.Kind == CompletionKind.Variable);
        CompletionResult parameters = await provider.GetCompletionsAsync(SOURCE.Replace("tot", "inp", StringComparison.Ordinal), SOURCE.Length, TestContext.Current.CancellationToken);
        Assert.Contains(parameters.Items, item => item.Text == "input" && item.Kind == CompletionKind.Variable);
    }

    [Fact]
    public async Task ImportedTypesAreSuggestedAndNamesAreNotDuplicated()
    {
        CompletionResult result = await CompleteAtEndAsync("Con");

        Assert.Contains(result.Items, item => item.Text == "Console" && item.Kind == CompletionKind.TypeName);
        Assert.Equal(result.Items.Count, result.Items.Select(item => item.Text).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TheNameThatTheUserDeclaresIsNotSuggested()
    {
        CompletionResult result = await CompleteAtEndAsync("int tot");

        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData("// Console.Wri")]
    [InlineData("string s = \"Console.Wri")]
    [InlineData("/* whi")]
    public async Task CommentsAndStringsHaveNoSuggestions(string source)
    {
        CompletionResult result = await CompleteAtEndAsync(source);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ACaretInTheMiddleUsesTheTextBeforeIt()
    {
        const string SOURCE = "Console.Wri;\nint after = 1;";
        int caret = "Console.Wri".Length;

        CompletionResult result = await provider.GetCompletionsAsync(SOURCE, caret, TestContext.Current.CancellationToken);

        Assert.Equal(8, result.ReplaceStart);
        Assert.Equal(3, result.ReplaceLength);
        Assert.Contains(result.Items, item => item.Text == "WriteLine");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public async Task ACaretOutsideTheTextGivesNoSuggestions(int caret)
    {
        CompletionResult result = await provider.GetCompletionsAsync("Con", caret, TestContext.Current.CancellationToken);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SuggestingNeverRunsTheSource()
    {
        string marker = Path.Combine(Path.GetTempPath(), "codepad-completion-" + Guid.NewGuid().ToString("N"));
        string source = $"File.WriteAllText(@\"{marker}\", \"x\");\nConsole.Wri";

        CompletionResult result = await CompleteAtEndAsync(source);

        Assert.Contains(result.Items, item => item.Text == "WriteLine");
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task ACancelledRequestThrows()
    {
        using CancellationTokenSource cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetCompletionsAsync("Con", 3, cancelled.Token));
    }

    [Fact]
    public async Task TheListIsBounded()
    {
        CompletionResult result = await CompleteAtEndAsync(string.Empty);

        Assert.InRange(result.Items.Count, 1, CSharpCompletionProvider.MAX_ITEMS);
    }
}
