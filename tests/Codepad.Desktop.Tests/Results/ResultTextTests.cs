using Codepad.Desktop.Results;

namespace Codepad.Desktop.Tests.Results;

public sealed class ResultTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("System.Int32", "Int32")]
    [InlineData("System.Int32[]", "Int32[]")]
    [InlineData("Demo.Outer+Inner", "Inner")]
    [InlineData("System.Collections.Generic.List`1[[Demo.Person, script, Version=1.0.0.0]]", "List<…>")]
    [InlineData("System.Collections.Generic.List`1[[System.Int32, System.Private.CoreLib]][]", "List<…>[]")]
    [InlineData("Person", "Person")]
    public void ShortTypeKeepsTheNameAndDropsTheNamespace(string? full, string expected)
    {
        Assert.Equal(expected, ResultText.ShortType(full));
    }
}
