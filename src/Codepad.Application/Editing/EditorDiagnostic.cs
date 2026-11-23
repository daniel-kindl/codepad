using Codepad.Domain.Results;

namespace Codepad.Application.Editing;

/// <summary>A compiler message with the exact range in the source that the editor marks.</summary>
/// <param name="Severity">How serious the diagnostic is.</param>
/// <param name="Code">The compiler code, for example <c>CS0103</c>.</param>
/// <param name="Message">The message.</param>
/// <param name="Start">The zero-based offset of the first marked character.</param>
/// <param name="Length">The number of marked characters. It can be 0 at the end of the source.</param>
/// <param name="Line">The line of <paramref name="Start"/>. The first line is 1.</param>
/// <param name="Column">The column of <paramref name="Start"/>. The first column is 1.</param>
public sealed record EditorDiagnostic(DiagnosticSeverity Severity, string Code, string Message, int Start, int Length, int Line, int Column);
