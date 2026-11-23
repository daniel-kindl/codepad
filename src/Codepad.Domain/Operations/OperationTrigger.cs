namespace Codepad.Domain.Operations;

/// <summary>What can start an operation.</summary>
public enum OperationTrigger
{
    /// <summary>Only an explicit user or CLI action starts the operation, for example Run.</summary>
    ExplicitOnly,

    /// <summary>An edit can start the operation after a short pause in typing. Only a side-effect-free operation can use it.</summary>
    LiveEligible,
}
