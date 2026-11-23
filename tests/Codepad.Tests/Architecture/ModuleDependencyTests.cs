using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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
        { "Codepad.Domain", "System.Data" },
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

    [Fact]
    public void DomainReferencesOnlyTheBaseClassLibrary()
    {
        // An allowlist: a new package or CodePad module in Domain fails here, also one that the list above does not name.
        IEnumerable<string> references = Assembly.Load("Codepad.Domain").GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty);

        Assert.All(references, name => Assert.True(name == "System" || name.StartsWith("System.", StringComparison.Ordinal), $"Codepad.Domain references {name}."));
    }

    public static TheoryData<string> ForbiddenDomainTypePrefixes => new()
    {
        "System.IO.",
        "System.Net.",
        "System.Data.",
        "Microsoft.Win32.",
        "System.Diagnostics.Process",
        "System.Environment",
        "System.TimeProvider",
        "System.Threading.Thread",
    };

    [Theory]
    [MemberData(nameof(ForbiddenDomainTypePrefixes))]
    public void DomainUsesNoIoPersistenceOrOperatingSystemType(string forbiddenPrefix)
    {
        // IO, ADO.NET and OS types live in System.Runtime or System.Data.Common, so the assembly references cannot show them. The type references can.
        IEnumerable<string> types = DomainTypeReferences();

        Assert.DoesNotContain(types, name => name.StartsWith(forbiddenPrefix, StringComparison.Ordinal));
    }

    public static TheoryData<string> ForbiddenDomainClockMembers => new()
    {
        "System.DateTime.get_Now",
        "System.DateTime.get_UtcNow",
        "System.DateTime.get_Today",
        "System.DateTimeOffset.get_Now",
        "System.DateTimeOffset.get_UtcNow",
    };

    [Theory]
    [MemberData(nameof(ForbiddenDomainClockMembers))]
    public void DomainReadsNoClockForTimestamps(string forbiddenMember)
    {
        // The caller supplies each timestamp. Guid.CreateVersion7 in the ID New() methods is the one allowed clock use.
        Assert.DoesNotContain(forbiddenMember, DomainMemberReferences());
    }

    [Fact]
    public void DomainMemberReferenceScanSeesTheGuidFactory()
    {
        // Proves that the member scan reads real references, so the clock checks are not empty passes.
        Assert.Contains("System.Guid.CreateVersion7", DomainMemberReferences());
    }

    [Fact]
    public void DomainTypeReferenceScanSeesTheSerializationAttributes()
    {
        // Proves that the type scan reads real references, so the forbidden type checks are not empty passes.
        Assert.Contains("System.Text.Json.Serialization.JsonPolymorphicAttribute", DomainTypeReferences());
    }

    private static List<string> DomainTypeReferences()
    {
        using FileStream stream = File.OpenRead(Assembly.Load("Codepad.Domain").Location);
        using PEReader reader = new PEReader(stream);
        MetadataReader metadata = reader.GetMetadataReader();

        return metadata.TypeReferences
            .Select(handle => metadata.GetTypeReference(handle))
            .Select(reference => metadata.GetString(reference.Namespace) + "." + metadata.GetString(reference.Name))
            .ToList();
    }

    private static List<string> DomainMemberReferences()
    {
        using FileStream stream = File.OpenRead(Assembly.Load("Codepad.Domain").Location);
        using PEReader reader = new PEReader(stream);
        MetadataReader metadata = reader.GetMetadataReader();
        List<string> members = [];

        foreach (MemberReferenceHandle handle in metadata.MemberReferences)
        {
            MemberReference member = metadata.GetMemberReference(handle);
            if (member.Parent.Kind == HandleKind.TypeReference)
            {
                TypeReference parent = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
                members.Add(metadata.GetString(parent.Namespace) + "." + metadata.GetString(parent.Name) + "." + metadata.GetString(member.Name));
            }
        }

        return members;
    }
}
