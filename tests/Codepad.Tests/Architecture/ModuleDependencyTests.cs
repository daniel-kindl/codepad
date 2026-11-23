using System.Reflection;

namespace Codepad.Tests.Architecture;

/// <summary>
/// Checks the module dependency direction in ARCHITECTURE.md from the compiled assembly references.
/// A reference to a forbidden assembly fails the test, also when no project reference adds it directly.
/// </summary>
public sealed class ModuleDependencyTests
{
    public static TheoryData<string, string> ForbiddenReferences => new()
    {
        { "Codepad.Domain", "Codepad.Application" },
        { "Codepad.Domain", "Codepad.Infrastructure" },
        { "Codepad.Domain", "Codepad.Desktop" },
        { "Codepad.Domain", "Avalonia" },
        { "Codepad.Domain", "Microsoft.Data.Sqlite" },
        { "Codepad.Domain", "Microsoft.CodeAnalysis" },
        { "Codepad.Application", "Codepad.Infrastructure" },
        { "Codepad.Application", "Codepad.Desktop" },
        { "Codepad.Application", "Avalonia" },
        { "Codepad.Application", "Microsoft.Data.Sqlite" },
        { "Codepad.Infrastructure", "Codepad.Desktop" },
        { "Codepad.Infrastructure", "Avalonia" },
    };

    [Theory]
    [MemberData(nameof(ForbiddenReferences))]
    public void ModuleDoesNotReferenceAForbiddenAssembly(string module, string forbiddenPrefix)
    {
        IEnumerable<string> references = Assembly.Load(module).GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty);

        Assert.DoesNotContain(references, name => name == forbiddenPrefix || name.StartsWith(forbiddenPrefix + ".", StringComparison.Ordinal));
    }

    [Fact]
    public void InfrastructureReferencesApplication()
    {
        // Proves that the reflection sees a used project reference, so the forbidden checks are not empty passes.
        IEnumerable<string?> references = Assembly.Load("Codepad.Infrastructure").GetReferencedAssemblies().Select(reference => reference.Name);

        Assert.Contains("Codepad.Application", references);
    }
}
