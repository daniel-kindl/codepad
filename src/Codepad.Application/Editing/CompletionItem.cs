namespace Codepad.Application.Editing;

/// <summary>One suggestion.</summary>
/// <param name="Text">The text that replaces the typed word. It is only a name, with no brackets and no semicolon.</param>
/// <param name="Kind">What the item stands for.</param>
/// <param name="Detail">Short context that tells similar items apart, for example the type of a variable or the return type of a method. Can be empty.</param>
public sealed record CompletionItem(string Text, CompletionKind Kind, string Detail);
