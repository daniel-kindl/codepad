using Codepad.Domain.Experiments;
using Codepad.Domain.Operations;

namespace Codepad.Application.Library;

/// <summary>One row of the library: the facts of an experiment without its full source.</summary>
/// <param name="Id">The experiment.</param>
/// <param name="Kind">The operation kind.</param>
/// <param name="Title">The title.</param>
/// <param name="Preview">The start of the source. It has at most <see cref="LibraryQuery.PreviewLength"/> Unicode characters.</param>
/// <param name="ModifiedAt">When the experiment was last edited.</param>
public sealed record ExperimentSummary(ExperimentId Id, OperationKind Kind, string Title, string Preview, DateTimeOffset ModifiedAt);
