using System.Collections.Immutable;
using Codepad.Application.Editing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Codepad.Language;

/// <summary>
/// Suggests keywords, names in scope and members with the Roslyn semantic model of the script compilation. This is the
/// same compilation that the analyzer uses. It compiles in memory, never runs the source, and never looks up packages.
/// </summary>
public sealed class CSharpCompletionProvider : ICSharpCompletionProvider
{
    /// <summary>The largest number of suggestions in one result.</summary>
    internal const int MAX_ITEMS = 200;

    private static readonly string[] s_contextualKeywords = ["var", "async", "await", "record", "yield", "nameof"];

    private static readonly Lazy<ImmutableArray<string>> s_keywords = new Lazy<ImmutableArray<string>>(LoadKeywords);

    /// <inheritdoc />
    public Task<CompletionResult> GetCompletionsAsync(string source, int caret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (caret < 0 || caret > source.Length)
        {
            return Task.FromResult(CompletionResult.None);
        }

        return Task.FromResult(Complete(source, caret, cancellationToken));
    }

    private static CompletionResult Complete(string source, int caret, CancellationToken cancellationToken)
    {
        SourceText text = SourceText.From(source);
        SyntaxTree tree = ScriptCompilationFactory.Parse(text, cancellationToken);
        SyntaxNode root = tree.GetRoot(cancellationToken);
        if (IsInCommentOrString(root, source, caret))
        {
            return CompletionResult.None;
        }

        int wordStart = caret;
        while (wordStart > 0 && IsIdentifierPart(source[wordStart - 1]))
        {
            wordStart--;
        }

        string prefix = source[wordStart..caret];
        if (prefix.Length > 0 && char.IsDigit(prefix[0]))
        {
            return CompletionResult.None;
        }

        int before = wordStart - 1;
        while (before >= 0 && source[before] is ' ' or '\t' or '\r' or '\n')
        {
            before--;
        }

        CSharpCompilation compilation = ScriptCompilationFactory.Create(tree);
        SemanticModel model = compilation.GetSemanticModel(tree);
        List<Candidate> candidates;
        if (before >= 0 && source[before] == '.' && root.FindToken(before).IsKind(SyntaxKind.DotToken))
        {
            ExpressionSyntax? target = FindMemberTarget(root, before);
            if (target is null)
            {
                return CompletionResult.None;
            }

            candidates = MemberCandidates(model, target, wordStart, prefix, cancellationToken);
        }
        else
        {
            if (prefix.Length > 0 && IsDeclarationName(root, caret - 1))
            {
                return CompletionResult.None;
            }

            candidates = ScopeCandidates(model, wordStart, prefix, cancellationToken);
            foreach (string keyword in s_keywords.Value.Where(word => word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(new Candidate(keyword, CompletionKind.Keyword, string.Empty));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        List<CompletionItem> items = candidates
            .OrderBy(candidate => candidate.Text.StartsWith(prefix, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(candidate => candidate.Kind)
            .ThenBy(candidate => candidate.Text, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Text, StringComparer.Ordinal)
            .Take(MAX_ITEMS)
            .Select(candidate => new CompletionItem(candidate.Text, candidate.Kind, candidate.Detail))
            .ToList();
        return new CompletionResult(wordStart, caret - wordStart, items);
    }

    private static bool IsIdentifierPart(char character) => char.IsLetterOrDigit(character) || character == '_';

    private static bool IsInCommentOrString(SyntaxNode root, string source, int caret)
    {
        if (caret == 0)
        {
            return false;
        }

        SyntaxTrivia trivia = root.FindTrivia(caret - 1, findInsideTrivia: false);
        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && trivia.Span.Start < caret && caret <= trivia.Span.End)
        {
            return true;
        }

        if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) && trivia.Span.Start < caret && (caret < trivia.Span.End || !source[..caret].EndsWith("*/", StringComparison.Ordinal)))
        {
            return true;
        }

        SyntaxToken token = root.FindToken(caret - 1);
        bool literal = token.Kind() is SyntaxKind.StringLiteralToken or SyntaxKind.CharacterLiteralToken or SyntaxKind.InterpolatedStringTextToken
            or SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken;
        if (!literal || token.Span.Start >= caret || caret > token.Span.End)
        {
            return false;
        }

        // A closed literal ends with its quote. A caret after that quote is outside the literal.
        bool closed = token.Text.Length >= 2 && token.Text[^1] is '"' or '\'';
        return caret < token.Span.End || !closed;
    }

    private static bool IsDeclarationName(SyntaxNode root, int position)
    {
        SyntaxToken token = root.FindToken(Math.Max(position, 0));
        return token.IsKind(SyntaxKind.IdentifierToken) && token.Parent is not IdentifierNameSyntax and not null;
    }

    private static ExpressionSyntax? FindMemberTarget(SyntaxNode root, int dotPosition)
    {
        SyntaxToken dot = root.FindToken(dotPosition);
        if (!dot.IsKind(SyntaxKind.DotToken))
        {
            return null;
        }

        return dot.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Expression,
            MemberBindingExpressionSyntax binding => binding.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault()?.Expression,
            QualifiedNameSyntax qualified => qualified.Left,
            _ => null,
        };
    }

    private static List<Candidate> MemberCandidates(SemanticModel model, ExpressionSyntax target, int position, string prefix, CancellationToken cancellationToken)
    {
        ISymbol? symbol = model.GetSymbolInfo(target, cancellationToken).Symbol;
        INamespaceOrTypeSymbol? container;
        bool staticContext;
        if (symbol is INamespaceOrTypeSymbol namespaceOrType and not IErrorTypeSymbol)
        {
            container = namespaceOrType;
            staticContext = true;
        }
        else
        {
            ITypeSymbol? type = model.GetTypeInfo(target, cancellationToken).Type;
            container = type is null or IErrorTypeSymbol ? null : type;
            staticContext = false;
        }

        if (container is null)
        {
            return [];
        }

        ImmutableArray<ISymbol> found = model.LookupSymbols(position, container, name: null, includeReducedExtensionMethods: true);
        IEnumerable<ISymbol> usable = found.Where(item => container is INamespaceSymbol
            || (staticContext ? item.IsStatic || item is INamedTypeSymbol : !item.IsStatic && item is not INamedTypeSymbol));
        return Group(usable, prefix);
    }

    private static List<Candidate> ScopeCandidates(SemanticModel model, int position, string prefix, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Group(model.LookupSymbols(position), prefix);
    }

    private static List<Candidate> Group(IEnumerable<ISymbol> symbols, string prefix)
    {
        Dictionary<string, List<ISymbol>> byName = new Dictionary<string, List<ISymbol>>(StringComparer.Ordinal);
        foreach (ISymbol symbol in symbols)
        {
            if (!IsSuggestable(symbol, prefix))
            {
                continue;
            }

            if (!byName.TryGetValue(symbol.Name, out List<ISymbol>? group))
            {
                group = [];
                byName[symbol.Name] = group;
            }

            group.Add(symbol);
        }

        List<Candidate> candidates = [];
        foreach ((string name, List<ISymbol> group) in byName)
        {
            // One item for each name. The most specific kind wins, and methods count as overloads.
            ISymbol best = group.OrderBy(symbol => KindOf(symbol)).First();
            CompletionKind kind = KindOf(best);
            candidates.Add(new Candidate(name, kind, DetailOf(best, kind, group)));
        }

        return candidates;
    }

    private static bool IsSuggestable(ISymbol symbol, string prefix)
    {
        if (!symbol.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !SyntaxFacts.IsValidIdentifier(symbol.Name))
        {
            return false;
        }

        if (symbol is IMethodSymbol method)
        {
            return method.MethodKind is MethodKind.Ordinary or MethodKind.ReducedExtension or MethodKind.LocalFunction;
        }

        return symbol is ILocalSymbol or IParameterSymbol or IFieldSymbol or IPropertySymbol or IEventSymbol or INamedTypeSymbol or INamespaceSymbol or IRangeVariableSymbol or ITypeParameterSymbol;
    }

    private static CompletionKind KindOf(ISymbol symbol) => symbol switch
    {
        ILocalSymbol or IParameterSymbol or IRangeVariableSymbol => CompletionKind.Variable,
        IFieldSymbol field when field.ContainingType is { IsScriptClass: true } => CompletionKind.Variable,
        IFieldSymbol or IPropertySymbol or IEventSymbol => CompletionKind.Member,
        IMethodSymbol => CompletionKind.Method,
        INamespaceSymbol => CompletionKind.Namespace,
        _ => CompletionKind.TypeName,
    };

    private static string DetailOf(ISymbol symbol, CompletionKind kind, List<ISymbol> group)
    {
        switch (symbol)
        {
            case ILocalSymbol local:
                return Show(local.Type);
            case IParameterSymbol parameter:
                return Show(parameter.Type);
            case IRangeVariableSymbol:
                return string.Empty;
            case IFieldSymbol field:
                return Show(field.Type);
            case IPropertySymbol property:
                return Show(property.Type);
            case IEventSymbol evt:
                return Show(evt.Type);
            case IMethodSymbol method:
                int overloads = group.OfType<IMethodSymbol>().Count();
                string text = Show(method.ReturnType);
                return overloads > 1 ? $"{text} (+{overloads - 1} overloads)" : text;
            case INamedTypeSymbol type:
                return type.TypeKind.ToString().ToLowerInvariant();
            default:
                return kind == CompletionKind.Namespace ? string.Empty : symbol.Kind.ToString().ToLowerInvariant();
        }
    }

    private static string Show(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static ImmutableArray<string> LoadKeywords()
    {
        IEnumerable<string> reserved = SyntaxFacts.GetReservedKeywordKinds().Select(kind => SyntaxFacts.GetText(kind));
        return [.. reserved.Concat(s_contextualKeywords)
            .Where(word => word.Length > 1 && word.All(char.IsAsciiLetter))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private sealed record Candidate(string Text, CompletionKind Kind, string Detail);
}
