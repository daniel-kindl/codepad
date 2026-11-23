namespace Codepad.Infrastructure.Storage;

/// <summary>One step of the schema. It moves the database from <c>Version - 1</c> to <c>Version</c>.</summary>
/// <param name="Version">The schema version after the step. Versions start at 1 and rise by 1.</param>
/// <param name="Sql">The statements of the step. They run in one transaction, so a failure leaves no partial change.</param>
internal sealed record Migration(int Version, string Sql);
