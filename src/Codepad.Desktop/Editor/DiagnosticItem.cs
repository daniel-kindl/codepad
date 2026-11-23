using Codepad.Application.Editing;
using Codepad.Domain.Results;

namespace Codepad.Desktop.Editor;

/// <summary>One row of the diagnostics list.</summary>
/// <param name="Source">The diagnostic from the analysis of the current text.</param>
public sealed record DiagnosticItem(EditorDiagnostic Source)
{
    /// <summary>Gets a value indicating whether the diagnostic is an error.</summary>
    public bool IsError => Source.Severity == DiagnosticSeverity.Error;

    /// <summary>Gets a value indicating whether the diagnostic is a warning.</summary>
    public bool IsWarning => Source.Severity == DiagnosticSeverity.Warning;

    /// <summary>Gets the severity as a word. The list never shows severity by color alone.</summary>
    public string SeverityText => Source.Severity switch
    {
        DiagnosticSeverity.Error => "Error",
        DiagnosticSeverity.Warning => "Warning",
        _ => "Info",
    };

    /// <summary>Gets the line, the column and the code, for example <c>Line 2, column 9 · CS1525</c>.</summary>
    public string PlaceText => $"Line {Source.Line}, column {Source.Column} · {Source.Code}";

    /// <summary>Gets the full text that a screen reader reads for the row.</summary>
    public string AccessibleName => $"{SeverityText}, {PlaceText}: {Source.Message}";
}
