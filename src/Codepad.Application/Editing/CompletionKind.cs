namespace Codepad.Application.Editing;

/// <summary>What a completion item stands for. The editor shows it as a word, so it never relies on color.</summary>
public enum CompletionKind
{
    /// <summary>A C# keyword.</summary>
    Keyword,

    /// <summary>A local variable or a parameter.</summary>
    Variable,

    /// <summary>A field, a property or an event.</summary>
    Member,

    /// <summary>A method or a local function.</summary>
    Method,

    /// <summary>A class, struct, interface, enum or delegate.</summary>
    TypeName,

    /// <summary>A namespace.</summary>
    Namespace,
}
