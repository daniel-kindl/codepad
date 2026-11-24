namespace Codepad.Application.Library;

/// <summary>Searches the stored experiments. A search never changes an experiment.</summary>
public interface ILibraryRepository
{
    /// <summary>Finds experiments.</summary>
    /// <param name="query">The search.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>One page, newest edit first. A damaged row is skipped.</returns>
    /// <exception cref="Codepad.Application.Experiments.ExperimentStorageException">The storage failed.</exception>
    Task<LibraryPage> SearchAsync(LibraryQuery query, CancellationToken cancellationToken = default);
}
