using Codepad.Application.Editing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Codepad.Language;

/// <summary>
/// Classifies C# source with the Roslyn syntax tree only. It does not bind symbols, so it takes a few
/// milliseconds for a normal script and runs on each key press. A name is a type when the syntax says so.
/// </summary>
public sealed class CSharpHighlighter : ICSharpHighlighter
{
    private static readonly CSharpParseOptions s_options = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Script);

    /// <inheritdoc />
    public IReadOnlyList<HighlightSpan> Highlight(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length == 0)
        {
            return [];
        }

        SyntaxNode root = CSharpSyntaxTree.ParseText(source, s_options).GetRoot();
        List<HighlightSpan> spans = [];
        foreach (SyntaxToken token in root.DescendantTokens(descendIntoTrivia: true))
        {
            foreach (SyntaxTrivia trivia in token.LeadingTrivia)
            {
                AddTrivia(spans, trivia);
            }

            AddToken(spans, token);
            foreach (SyntaxTrivia trivia in token.TrailingTrivia)
            {
                AddTrivia(spans, trivia);
            }
        }

        spans.Sort((left, right) => left.Start.CompareTo(right.Start));
        return Merge(spans);
    }

    private static void AddTrivia(List<HighlightSpan> spans, SyntaxTrivia trivia)
    {
        if (trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
            && trivia.Span.Length > 0)
        {
            spans.Add(new HighlightSpan(HighlightKind.Comment, trivia.Span.Start, trivia.Span.Length));
        }
    }

    private static void AddToken(List<HighlightSpan> spans, SyntaxToken token)
    {
        if (token.Span.Length == 0)
        {
            return;
        }

        HighlightKind? kind = Classify(token);
        if (kind is not null)
        {
            spans.Add(new HighlightSpan(kind.Value, token.Span.Start, token.Span.Length));
        }
    }

    private static HighlightKind? Classify(SyntaxToken token)
    {
        SyntaxKind kind = token.Kind();
        if (SyntaxFacts.IsKeywordKind(kind) || SyntaxFacts.IsContextualKeyword(kind) || kind == SyntaxKind.IdentifierToken && SyntaxFacts.GetContextualKeywordKind(token.ValueText) == SyntaxKind.VarKeyword)
        {
            return HighlightKind.Keyword;
        }

        switch (kind)
        {
            case SyntaxKind.NumericLiteralToken:
                return HighlightKind.Number;
            case SyntaxKind.StringLiteralToken:
            case SyntaxKind.CharacterLiteralToken:
            case SyntaxKind.InterpolatedStringStartToken:
            case SyntaxKind.InterpolatedVerbatimStringStartToken:
            case SyntaxKind.InterpolatedStringTextToken:
            case SyntaxKind.InterpolatedStringEndToken:
            case SyntaxKind.InterpolatedRawStringEndToken:
            case SyntaxKind.SingleLineRawStringLiteralToken:
            case SyntaxKind.MultiLineRawStringLiteralToken:
            case SyntaxKind.Utf8StringLiteralToken:
            case SyntaxKind.Utf8SingleLineRawStringLiteralToken:
            case SyntaxKind.Utf8MultiLineRawStringLiteralToken:
                return HighlightKind.StringLiteral;
            case SyntaxKind.IdentifierToken:
                return IsTypeName(token) ? HighlightKind.Type : null;
            default:
                return IsPunctuation(kind) ? HighlightKind.Punctuation : null;
        }
    }

    private static bool IsTypeName(SyntaxToken token)
    {
        return token.Parent switch
        {
            TypeDeclarationSyntax declaration => declaration.Identifier == token,
            EnumDeclarationSyntax declaration => declaration.Identifier == token,
            DelegateDeclarationSyntax declaration => declaration.Identifier == token,
            SimpleNameSyntax name => IsInTypePosition(name),
            _ => false,
        };
    }

    private static bool IsInTypePosition(SimpleNameSyntax name)
    {
        SyntaxNode? parent = name.Parent;
        if (parent is QualifiedNameSyntax qualified && qualified.Right == name || parent is AliasQualifiedNameSyntax || name is GenericNameSyntax && parent is not InvocationExpressionSyntax)
        {
            return true;
        }

        return parent switch
        {
            TypeArgumentListSyntax => true,
            VariableDeclarationSyntax declaration => declaration.Type == name,
            ObjectCreationExpressionSyntax creation => creation.Type == name,
            ParameterSyntax parameter => parameter.Type == name,
            CastExpressionSyntax cast => cast.Type == name,
            ArrayTypeSyntax array => array.ElementType == name,
            NullableTypeSyntax nullable => nullable.ElementType == name,
            TypeOfExpressionSyntax typeOf => typeOf.Type == name,
            DefaultExpressionSyntax defaultExpression => defaultExpression.Type == name,
            BaseTypeSyntax baseType => baseType.Type == name,
            MethodDeclarationSyntax method => method.ReturnType == name,
            PropertyDeclarationSyntax property => property.Type == name,
            FieldDeclarationSyntax => false,
            MemberAccessExpressionSyntax access => access.Expression == name && char.IsUpper(name.Identifier.Text[0]),
            _ => false,
        };
    }

    private static bool IsPunctuation(SyntaxKind kind)
    {
        // Roslyn kinds are grouped: punctuation and operator tokens sit between OpenBraceToken and the last operator.
        return SyntaxFacts.IsPunctuation(kind) && kind is not SyntaxKind.None;
    }

    private static List<HighlightSpan> Merge(List<HighlightSpan> sorted)
    {
        // Drop a span that overlaps an earlier one, so the editor never colors one character twice.
        List<HighlightSpan> result = [];
        int end = 0;
        foreach (HighlightSpan span in sorted)
        {
            if (span.Start < end)
            {
                continue;
            }

            result.Add(span);
            end = span.Start + span.Length;
        }

        return result;
    }
}
