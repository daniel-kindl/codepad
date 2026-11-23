using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Codepad.Language;

/// <summary>
/// Creates the in-memory script compilation. The analyzer and the completion provider both use it, so they see the same
/// language version, default imports and framework references as the worker. It never emits an assembly.
/// </summary>
internal static class ScriptCompilationFactory
{
    private const string SCRIPT_NAME = "script.csx";

    private static readonly string[] s_imports =
    [
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Text",
        "System.Threading",
        "System.Threading.Tasks",
    ];

    private static readonly Lazy<ImmutableArray<MetadataReference>> s_references = new Lazy<ImmutableArray<MetadataReference>>(LoadFrameworkReferences);

    /// <summary>The parse options of a script.</summary>
    public static CSharpParseOptions ParseOptions { get; } = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Script);

    /// <summary>Parses the source as a script.</summary>
    /// <param name="text">The source text.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    /// <returns>The syntax tree.</returns>
    public static SyntaxTree Parse(Microsoft.CodeAnalysis.Text.SourceText text, CancellationToken cancellationToken) =>
        CSharpSyntaxTree.ParseText(text, ParseOptions, SCRIPT_NAME, cancellationToken);

    /// <summary>Creates the compilation of one syntax tree.</summary>
    /// <param name="tree">The script tree.</param>
    /// <returns>The compilation.</returns>
    public static CSharpCompilation Create(SyntaxTree tree)
    {
        CSharpCompilationOptions options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithUsings(s_imports)
            .WithAllowUnsafe(false);
        return CSharpCompilation.CreateScriptCompilation("codepad-analysis", tree, s_references.Value, options);
    }

    private static ImmutableArray<MetadataReference> LoadFrameworkReferences()
    {
        // The default reference set: every managed assembly of the shared .NET runtime that the app runs on.
        ImmutableArray<MetadataReference>.Builder references = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (string path in Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll").Order(StringComparer.Ordinal))
        {
            try
            {
                _ = AssemblyName.GetAssemblyName(path);
            }
            catch (BadImageFormatException)
            {
                // A native library. It is not a reference.
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(path));
        }

        return references.ToImmutable();
    }
}
