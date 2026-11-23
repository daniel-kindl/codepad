using System.Collections.Immutable;
using System.Globalization;
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

    /// <inheritdoc />
    public Task<EditorAnalysis> AnalyzeAsync(string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Analyze(source, cancellationToken));
    }

    private static EditorAnalysis Analyze(string source, CancellationToken cancellationToken)
    {
        SourceText text = SourceText.From(source);
        SyntaxTree tree = ScriptCompilationFactory.Parse(text, cancellationToken);
        CSharpCompilation compilation = ScriptCompilationFactory.Create(tree);

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
}
