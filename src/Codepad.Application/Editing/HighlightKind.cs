namespace Codepad.Application.Editing;

/// <summary>The class of a piece of C# source for syntax coloring.</summary>
public enum HighlightKind
{
    /// <summary>A keyword, for example <c>if</c> or <c>var</c>.</summary>
    Keyword,

    /// <summary>A line comment, a block comment or an XML documentation comment.</summary>
    Comment,

    /// <summary>A string, an interpolated string or a character literal.</summary>
    StringLiteral,

    /// <summary>A numeric literal.</summary>
    Number,

    /// <summary>The name of a type, as the syntax shows it.</summary>
    Type,

    /// <summary>An operator or a punctuation mark.</summary>
    Punctuation,
}
