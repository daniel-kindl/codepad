namespace Codepad.Application.Library;

/// <summary>One page of search results, newest edit first.</summary>
/// <param name="Items">The results. At most <see cref="LibraryQuery.Limit"/>.</param>
/// <param name="HasMore">True when more results follow this page.</param>
public sealed record LibraryPage(IReadOnlyList<ExperimentSummary> Items, bool HasMore);
