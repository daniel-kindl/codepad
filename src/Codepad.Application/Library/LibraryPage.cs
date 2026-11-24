namespace Codepad.Application.Library;

/// <summary>One page of search results, newest edit first.</summary>
/// <param name="Items">The results. At most <see cref="LibraryQuery.Limit"/>.</param>
/// <param name="HasMore">True when more results follow this page.</param>
/// <param name="NextOffset">The offset of the next page. It counts the rows that the page consumed, also the damaged rows that it skipped, so it can be more than the number of items.</param>
public sealed record LibraryPage(IReadOnlyList<ExperimentSummary> Items, bool HasMore, int NextOffset);
