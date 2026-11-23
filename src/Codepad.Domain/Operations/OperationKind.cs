namespace Codepad.Domain.Operations;

/// <summary>The kinds of operation in v0.1. There is no provider registry: a new kind is a code change.</summary>
public enum OperationKind
{
    /// <summary>Compile and run a C# snippet in a worker process.</summary>
    CSharp,

    /// <summary>Validate and format a JSON document.</summary>
    Json,
}
