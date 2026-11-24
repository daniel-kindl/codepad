using Codepad.Domain.Results;

namespace Codepad.Cli;

/// <summary>The version of the JSON output. It grows when a name or a meaning changes in a way that can break a reader.</summary>
internal static class Schema
{
    public const int VERSION = 1;
}

internal sealed record ErrorDto(string Code, string Message);

internal sealed record ErrorOutput(int SchemaVersion, string Command, bool Ok, int ExitCode, ErrorDto Error);

internal sealed record DiagnosticDto(string Severity, string? Code, string Message, int? Line, int? Column);

internal sealed record TextDto(string Text, bool Truncated, int? OriginalLength);

internal sealed record HostDto(string Runtime, string OperatingSystem, string Architecture, string WorkerVersion, string Language);

internal sealed record ExperimentRefDto(string Id, string Title, string Kind, long Revision);

internal sealed record ExperimentDto(string Id, string Kind, string Title, string Source, string Input, DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt, long Revision);

internal sealed record SummaryDto(string Id, string Kind, string Title, string Preview, DateTimeOffset ModifiedAt);

internal sealed record RunOutput(
    int SchemaVersion,
    string Command,
    bool Ok,
    int ExitCode,
    string Outcome,
    ExperimentRefDto Experiment,
    string ExecutionId,
    long DurationMs,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    ResultNode? Value,
    TextDto Stdout,
    TextDto Stderr,
    HostDto? Host,
    bool HistorySaved,
    string? HistoryError);

internal sealed record JsonOutput(int SchemaVersion, string Command, bool Ok, int ExitCode, bool Valid, string? Formatted, ResultNode? Tree, DiagnosticDto? Problem);

internal sealed record ExperimentOutput(int SchemaVersion, string Command, bool Ok, int ExitCode, ExperimentDto Experiment);

internal sealed record ListOutput(int SchemaVersion, string Command, bool Ok, int ExitCode, IReadOnlyList<SummaryDto> Items, bool HasMore, int NextOffset);

internal sealed record HistoryItemDto(string ExecutionId, long ExperimentRevision, DateTimeOffset RequestedAt, DateTimeOffset CompletedAt, long DurationMs, string Outcome);

internal sealed record HistoryOutput(int SchemaVersion, string Command, bool Ok, int ExitCode, string ExperimentId, IReadOnlyList<HistoryItemDto> Runs);

internal sealed record SavedRunDto(
    string ExecutionId,
    string ExperimentId,
    long ExperimentRevision,
    DateTimeOffset RequestedAt,
    DateTimeOffset CompletedAt,
    string Outcome,
    string Source,
    string Input,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    ResultNode? Value,
    TextDto Stdout,
    TextDto Stderr,
    HostDto? Host);

internal sealed record SavedRunOutput(int SchemaVersion, string Command, bool Ok, int ExitCode, SavedRunDto Run);
