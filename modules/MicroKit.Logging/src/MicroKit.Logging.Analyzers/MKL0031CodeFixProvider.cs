using System.Threading;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace MicroKit.Logging.Analyzers;

/// <summary>Code fix for <see cref="MKL0031Analyzer"/>: replaces the sensitive placeholder with a redacted version.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MKL0031CodeFixProvider))]
[Shared]
public sealed class MKL0031CodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MKL0031Analyzer.DiagnosticId);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);

        var diagnostic = context.Diagnostics[0];
        var node = root?.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

        if (node is not LiteralExpressionSyntax literal)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Replace sensitive placeholder with [redacted]",
                createChangedDocument: ct => ApplyFixAsync(context.Document, literal, ct),
                equivalenceKey: nameof(MKL0031CodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        LiteralExpressionSyntax literal,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        var originalText = literal.Token.ValueText;
        var redactedText = ReplaceSensitivePlaceholders(originalText);

        var newLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(redactedText));

        var newRoot = root.ReplaceNode(literal, newLiteral);
        return document.WithSyntaxRoot(newRoot);
    }

    private static string ReplaceSensitivePlaceholders(string template)
    {
        var result = new System.Text.StringBuilder();
        var i = 0;

        while (i < template.Length)
        {
            if (template[i] != '{')
            {
                result.Append(template[i++]);
                continue;
            }

            if (i + 1 < template.Length && template[i + 1] == '{')
            {
                result.Append("{{");
                i += 2;
                continue;
            }

            var start = i + 1;
            var end = template.IndexOf('}', start);
            if (end < 0)
            {
                result.Append(template, i, template.Length - i);
                break;
            }

            var placeholder = template.Substring(start, end - start);
            var colonIndex = placeholder.IndexOf(':');
            var name = colonIndex >= 0 ? placeholder.Substring(0, colonIndex) : placeholder;
            var format = colonIndex >= 0 ? placeholder.Substring(colonIndex) : string.Empty;

            if (MKL0031Analyzer.IsSensitiveTerm(name))
            {
                // Replace the sensitive placeholder name with [Redacted]
                result.Append('{').Append("[Redacted]").Append(format).Append('}');
            }
            else
            {
                result.Append('{').Append(placeholder).Append('}');
            }

            i = end + 1;
        }

        return result.ToString();
    }
}
