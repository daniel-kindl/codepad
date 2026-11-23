using Codepad.Domain.Operations;

namespace Codepad.Domain.Experiments;

/// <summary>
/// One experiment: the source and input that the user edits, with a title and an operation kind.
/// </summary>
/// <remarks>
/// <para>
/// An experiment is immutable. An edit returns a new experiment with the next <see cref="Revision"/>.
/// An edit to the value that the experiment already has returns the same instance and keeps the revision.
/// Storage uses the revision to refuse an older write after a newer one.
/// </para>
/// <para>
/// The caller supplies each timestamp. The domain reads no clock for a timestamp.
/// <see cref="Id"/>, <see cref="Kind"/> and <see cref="CreatedAt"/> never change.
/// </para>
/// </remarks>
public sealed record Experiment
{
    private Experiment(ExperimentId id, OperationKind kind, string title, string source, string input, DateTimeOffset createdAt, DateTimeOffset modifiedAt, long revision)
    {
        Id = id;
        Kind = kind;
        Title = title;
        Source = source;
        Input = input;
        CreatedAt = createdAt;
        ModifiedAt = modifiedAt;
        Revision = revision;
    }

    /// <summary>Gets the identity of the experiment.</summary>
    public ExperimentId Id { get; }

    /// <summary>Gets the operation kind. It does not change after creation.</summary>
    public OperationKind Kind { get; }

    /// <summary>Gets the title. It can be empty.</summary>
    public string Title { get; }

    /// <summary>Gets the source: C# code, or a JSON document.</summary>
    public string Source { get; }

    /// <summary>Gets the input. For C#, the worker gives it to the snippet as standard input. JSON does not use it. Empty by default.</summary>
    public string Input { get; }

    /// <summary>Gets the time of creation.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the time of the last edit that made a new revision.</summary>
    public DateTimeOffset ModifiedAt { get; }

    /// <summary>Gets the revision. A new experiment has revision 1. Each edit that changes a value adds 1.</summary>
    public long Revision { get; }

    /// <summary>Makes a new experiment at revision 1 with empty input.</summary>
    /// <param name="id">The identity.</param>
    /// <param name="kind">The operation kind.</param>
    /// <param name="title">The title.</param>
    /// <param name="source">The source.</param>
    /// <param name="createdAt">The time of creation. It is also the first <see cref="ModifiedAt"/>.</param>
    /// <returns>The new experiment.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not defined.</exception>
    public static Experiment Create(ExperimentId id, OperationKind kind, string title, string source, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        Guard.Defined(kind);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(source);

        return new Experiment(id, kind, title, source, string.Empty, createdAt, createdAt, 1);
    }

    /// <summary>Rebuilds an experiment that storage holds, at its stored revision.</summary>
    /// <param name="id">The identity.</param>
    /// <param name="kind">The operation kind.</param>
    /// <param name="title">The title.</param>
    /// <param name="source">The source.</param>
    /// <param name="input">The input.</param>
    /// <param name="createdAt">The time of creation.</param>
    /// <param name="modifiedAt">The time of the last edit. It cannot be before <paramref name="createdAt"/>.</param>
    /// <param name="revision">The revision. At least 1.</param>
    /// <returns>The experiment.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is not defined, <paramref name="revision"/> is less than 1, or
    /// <paramref name="modifiedAt"/> is before <paramref name="createdAt"/>.
    /// </exception>
    public static Experiment Restore(ExperimentId id, OperationKind kind, string title, string source, string input, DateTimeOffset createdAt, DateTimeOffset modifiedAt, long revision)
    {
        ArgumentNullException.ThrowIfNull(id);
        Guard.Defined(kind);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(modifiedAt, createdAt);

        return new Experiment(id, kind, title, source, input, createdAt, modifiedAt, revision);
    }

    /// <summary>Changes the title.</summary>
    /// <param name="title">The new title.</param>
    /// <param name="modifiedAt">The time of the edit. If it is before <see cref="ModifiedAt"/>, the edit keeps <see cref="ModifiedAt"/>.</param>
    /// <returns>A new revision, or this instance if the title is the same.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is null.</exception>
    public Experiment WithTitle(string title, DateTimeOffset modifiedAt)
    {
        ArgumentNullException.ThrowIfNull(title);
        return title == Title ? this : new Experiment(Id, Kind, title, Source, Input, CreatedAt, Later(modifiedAt), Revision + 1);
    }

    /// <summary>Changes the source.</summary>
    /// <param name="source">The new source.</param>
    /// <param name="modifiedAt">The time of the edit. If it is before <see cref="ModifiedAt"/>, the edit keeps <see cref="ModifiedAt"/>.</param>
    /// <returns>A new revision, or this instance if the source is the same.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public Experiment WithSource(string source, DateTimeOffset modifiedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source == Source ? this : new Experiment(Id, Kind, Title, source, Input, CreatedAt, Later(modifiedAt), Revision + 1);
    }

    /// <summary>Changes the input.</summary>
    /// <param name="input">The new input.</param>
    /// <param name="modifiedAt">The time of the edit. If it is before <see cref="ModifiedAt"/>, the edit keeps <see cref="ModifiedAt"/>.</param>
    /// <returns>A new revision, or this instance if the input is the same.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    public Experiment WithInput(string input, DateTimeOffset modifiedAt)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input == Input ? this : new Experiment(Id, Kind, Title, Source, input, CreatedAt, Later(modifiedAt), Revision + 1);
    }

    // A clock change must not lose an edit, so an earlier time does not stop it. ModifiedAt never goes back.
    private DateTimeOffset Later(DateTimeOffset modifiedAt) => modifiedAt > ModifiedAt ? modifiedAt : ModifiedAt;
}
