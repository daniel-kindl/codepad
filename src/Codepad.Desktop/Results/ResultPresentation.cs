namespace Codepad.Desktop.Results;

/// <summary>How the result panel shows a value.</summary>
public enum ResultPresentation
{
    /// <summary>There is no value to show.</summary>
    None,

    /// <summary>One value as text: null, a number, a string.</summary>
    Text,

    /// <summary>An expandable tree: an object, a nested collection or a list of mixed values.</summary>
    Tree,

    /// <summary>A table: a finite list of objects with the same properties.</summary>
    Table,
}
