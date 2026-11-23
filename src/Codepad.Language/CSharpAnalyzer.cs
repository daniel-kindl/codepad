using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Codepad.Application.Editing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using DomainSeverity = Codepad.Domain.Results.DiagnosticSeverity;
using RoslynDiagnostic = Microsoft.CodeAnalysis.Diagnostic;
using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Codepad.Language;

/// <summary>
/// Finds the syntax and semantic diagnostics of one C# script with Roslyn. It uses the same language version,
/// default imports and framework references as the worker, so the editor and a run agree. It compiles the
/// source in memory. It never emits an assembly and never runs the source.
/// </summary>
public sealed class CSharpAnalyzer : ICSharpAnalyzer
{
    private const int MAX_DIAGNOSTICS = 100;
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

    /// <inheritdoc />
    public Task<EditorAnalysis> AnalyzeAsync(string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Analyze(source, cancellationToken));
    }

    private static EditorAnalysis Analyze(string source, CancellationToken cancellationToken)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Script);
        SourceText text = SourceText.From(source);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text, parseOptions, SCRIPT_NAME, cancellationToken);
        CSharpCompilationOptions options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithUsings(s_imports)
            .WithAllowUnsafe(false);
        CSharpCompilation compilation = CSharpCompilation.CreateScriptCompilation("codepad-analysis", tree, s_references.Value, options);

        ImmutableArray<RoslynDiagnostic> found = compilation.GetDiagnostics(cancellationToken);
        List<EditorDiagnostic> result = [];
        foreach (RoslynDiagnostic diagnostic in found.Where(item => item.Severity != RoslynSeverity.Hidden && item.Location.IsInSource)
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.Location.SourceSpan.Start))
        {
            if (result.Count == MAX_DIAGNOSTICS)
            {
                break;
            }

            TextSpan span = diagnostic.Location.SourceSpan;
            LinePosition position = text.Lines.GetLinePosition(span.Start);
            DomainSeverity severity = diagnostic.Severity switch
            {
                RoslynSeverity.Error => DomainSeverity.Error,
                RoslynSeverity.Warning => DomainSeverity.Warning,
                _ => DomainSeverity.Info,
            };
            result.Add(new EditorDiagnostic(severity, diagnostic.Id, diagnostic.GetMessage(CultureInfo.InvariantCulture), span.Start, span.Length, position.Line + 1, position.Character + 1));
        }

        return new EditorAnalysis(result);
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
