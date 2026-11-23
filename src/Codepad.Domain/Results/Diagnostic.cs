using System.Text.Json.Serialization;

namespace Codepad.Domain.Results;

/// <summary>How serious a diagnostic is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticSeverity>))]
public enum DiagnosticSeverity
{
    /// <summary>Information only.</summary>
    Info,

    /// <summary>A possible problem. It does not stop the operation.</summary>
    Warning,

    /// <summary>A problem that stops the operation, for example a compile error or a JSON parse error.</summary>
    Error,
}

/// <summary>A place in the source. Both numbers start at 1.</summary>
/// <param name="Line">The line. At least 1.</param>
/// <param name="Column">The column. At least 1.</param>
public sealed record SourceLocation(int Line, int Column)
{
    /// <summary>Gets the line. At least 1.</summary>
    public int Line { get; } = Guard.AtLeastOne(Line);

    /// <summary>Gets the column. At least 1.</summary>
    public int Column { get; } = Guard.AtLeastOne(Column);
}

/// <summary>A message about the source from a compiler, a parser or a run.</summary>
/// <param name="Severity">How serious the diagnostic is.</param>
/// <param name="Code">A stable code, for example <c>CS0103</c>, or null if the source of the diagnostic has none.</param>
/// <param name="Message">The message. It cannot be empty.</param>
/// <param name="Location">The place in the source, or null if the diagnostic has no place.</param>
public sealed record Diagnostic(DiagnosticSeverity Severity, string? Code, string Message, SourceLocation? Location)
{
    /// <summary>Gets how serious the diagnostic is.</summary>
    public DiagnosticSeverity Severity { get; } = Guard.Defined(Severity);

    /// <summary>Gets the stable code, or null.</summary>
    public string? Code { get; } = Code;

    /// <summary>Gets the message.</summary>
    public string Message { get; } = string.IsNullOrWhiteSpace(Message)
        ? throw new ArgumentException("A diagnostic needs a message.", nameof(Message))
        : Message;

    /// <summary>Gets the place in the source, or null.</summary>
    public SourceLocation? Location { get; } = Location;
}
