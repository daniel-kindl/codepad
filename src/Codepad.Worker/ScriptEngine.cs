using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using Codepad.Contracts;
using Codepad.Domain.Executions;
using Codepad.Domain.Results;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using CodeAnalysisDiagnostic = Microsoft.CodeAnalysis.Diagnostic;
using DomainDiagnostic = Codepad.Domain.Results.Diagnostic;
using DomainSeverity = Codepad.Domain.Results.DiagnosticSeverity;
using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Codepad.Worker;

/// <summary>
/// Compiles one C# script, and runs it only when it compiles without an error. The script semantics are
/// in ARCHITECTURE.md, "C# script semantics": top-level statements, the value of the last expression, top-level
/// <c>await</c>, the default framework references, the default imports, and no NuGet packages.
/// </summary>
internal static class ScriptEngine
{
    private const int MAX_DIAGNOSTICS = 100;
    private const int MAX_MESSAGE_CHARS = 2_000;
    private const int MAX_STACK_FRAMES = 20;
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

    /// <summary>Compiles and runs one request.</summary>
    /// <param name="request">The request.</param>
    /// <param name="stdout">The writer that the script sees as <c>Console.Out</c>.</param>
    /// <param name="stderr">The writer that the script sees as <c>Console.Error</c>.</param>
    /// <returns>The response. It has one of the outcomes Success, CompilationFailed and RuntimeFailed.</returns>
    public static async Task<WorkerRunResponse> RunAsync(WorkerRunRequest request, BoundedTextWriter stdout, BoundedTextWriter stderr)
    {
        ScriptOptions options = ScriptOptions.Default
            .WithReferences(s_references.Value)
            .WithImports(s_imports)
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithFilePath(SCRIPT_NAME)
            .WithFileEncoding(System.Text.Encoding.UTF8)
            .WithEmitDebugInformation(true);

        Script<object> script = CSharpScript.Create(request.Source, options);
        ImmutableArray<CodeAnalysisDiagnostic> compiled = script.Compile();
        List<DomainDiagnostic> diagnostics = Convert(compiled);

        if (compiled.Any(diagnostic => diagnostic.Severity == RoslynSeverity.Error))
        {
            return Respond(request, ExecutionOutcome.CompilationFailed, diagnostics, null, stdout, stderr);
        }

        try
        {
            ScriptState<object> state = await script.RunAsync().ConfigureAwait(false);
            ResultNode? value = state.ReturnValue is null ? null : new ResultNodeConverter(request.Results).Convert(state.ReturnValue);
            return Respond(request, ExecutionOutcome.Success, diagnostics, value, stdout, stderr);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            diagnostics.Add(new DomainDiagnostic(DomainSeverity.Error, "RuntimeException", Describe(exception), null));
            return Respond(request, ExecutionOutcome.RuntimeFailed, diagnostics, null, stdout, stderr);
        }
    }

    /// <summary>Gets the facts about the host. They make a run reproducible.</summary>
    /// <returns>The facts.</returns>
    public static WorkerHostInfo Host()
    {
        return new WorkerHostInfo(
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            typeof(ScriptEngine).Assembly.GetName().Version?.ToString() ?? "unknown",
            "C# " + LanguageVersion.Latest.MapSpecifiedToEffectiveVersion().ToDisplayString(),
            Directory.GetCurrentDirectory());
    }

    private static WorkerRunResponse Respond(WorkerRunRequest request, ExecutionOutcome outcome, List<DomainDiagnostic> diagnostics, ResultNode? value, BoundedTextWriter stdout, BoundedTextWriter stderr)
    {
        return new WorkerRunResponse(
            WorkerProtocol.Version,
            request.ExecutionId,
            request.SourceRevision,
            outcome,
            diagnostics,
            value,
            stdout.Snapshot(),
            stderr.Snapshot(),
            Host());
    }

    private static List<DomainDiagnostic> Convert(ImmutableArray<CodeAnalysisDiagnostic> compiled)
    {
        List<DomainDiagnostic> result = [];
        foreach (CodeAnalysisDiagnostic diagnostic in compiled.Where(item => item.Severity != RoslynSeverity.Hidden).OrderByDescending(item => item.Severity))
        {
            if (result.Count == MAX_DIAGNOSTICS)
            {
                break;
            }

            DomainSeverity severity = diagnostic.Severity switch
            {
                RoslynSeverity.Error => DomainSeverity.Error,
                RoslynSeverity.Warning => DomainSeverity.Warning,
                _ => DomainSeverity.Info,
            };

            SourceLocation? location = null;
            if (diagnostic.Location.IsInSource)
            {
                Microsoft.CodeAnalysis.Text.LinePosition start = diagnostic.Location.GetLineSpan().StartLinePosition;
                location = new SourceLocation(start.Line + 1, start.Character + 1);
            }

            result.Add(new DomainDiagnostic(severity, diagnostic.Id, Cut(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)), location));
        }

        return result;
    }

    /// <summary>Describes an exception with its type, its message and the first frames of its stack trace.</summary>
    private static string Describe(Exception exception)
    {
        string[] frames = (exception.StackTrace ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string trace = string.Join('\n', frames.Take(MAX_STACK_FRAMES));
        if (frames.Length > MAX_STACK_FRAMES)
        {
            trace += $"\n... {frames.Length - MAX_STACK_FRAMES} more frames";
        }

        string head = (exception.GetType().FullName ?? exception.GetType().Name) + ": " + exception.Message;
        return Cut(trace.Length == 0 ? head : head + "\n" + trace);
    }

    private static string Cut(string text)
    {
        return text.Length <= MAX_MESSAGE_CHARS ? text : text[..MAX_MESSAGE_CHARS] + "...";
    }

    private static ImmutableArray<MetadataReference> LoadFrameworkReferences()
    {
        // The default reference set: every managed assembly of the shared .NET runtime that the worker runs on.
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
