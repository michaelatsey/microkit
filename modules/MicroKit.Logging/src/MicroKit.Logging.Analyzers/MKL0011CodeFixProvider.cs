using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis.Formatting;

namespace MicroKit.Logging.Analyzers;

/// <summary>Code fix for <see cref="MKL0011Analyzer"/>: converts interpolated strings to structured log templates.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MKL0011CodeFixProvider))]
[Shared]
public sealed class MKL0011CodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MKL0011Analyzer.DiagnosticId);

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

        if (node is not InterpolatedStringExpressionSyntax interpolated)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Use structured log template",
                createChangedDocument: ct => ApplyFixAsync(context.Document, interpolated, ct),
                equivalenceKey: nameof(MKL0011CodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        InterpolatedStringExpressionSyntax interpolated,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        // Collect: template parts and extracted argument expressions
        var templateBuilder = new StringBuilder();
        var extractedArgs = new List<ExpressionSyntax>();

        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    templateBuilder.Append(text.TextToken.ValueText);
                    break;

                case InterpolationSyntax hole:
                    var placeholder = BuildPlaceholderName(hole.Expression);
                    templateBuilder.Append('{').Append(placeholder).Append('}');
                    extractedArgs.Add(hole.Expression.WithoutTrivia());
                    break;
            }
        }

        // Build: new string literal for the template
        var templateLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(templateBuilder.ToString()));

        // Find the enclosing invocation argument list to append extracted args
        if (interpolated.Parent is not ArgumentSyntax messageArgSyntax ||
            messageArgSyntax.Parent is not ArgumentListSyntax argList ||
            argList.Parent is not InvocationExpressionSyntax invocationSyntax)
        {
            // Fallback: just replace the interpolated string with the template (no extra args)
            var newRoot = root.ReplaceNode(interpolated, templateLiteral);
            return document.WithSyntaxRoot(newRoot);
        }

        // Replace the interpolated string with the template literal
        var newInvocation = invocationSyntax.ReplaceNode(
            (SyntaxNode)interpolated, templateLiteral);

        // Append extracted expressions as additional arguments (after the message arg)
        var newArgList = newInvocation.ArgumentList;
        var messageArgIndex = argList.Arguments.IndexOf(messageArgSyntax);

        foreach (var expr in extractedArgs)
        {
            var newArg = SyntaxFactory.Argument(expr);
            newArgList = newArgList.AddArguments(newArg);
        }

        newInvocation = newInvocation.WithArgumentList(newArgList)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var finalRoot = root.ReplaceNode(invocationSyntax, newInvocation);
        return document.WithSyntaxRoot(finalRoot);
    }

    private static string BuildPlaceholderName(ExpressionSyntax expr)
    {
        // Use the last identifier in the expression as the placeholder name (PascalCase)
        var name = expr switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            _ => expr.ToString(),
        };

        if (name.Length == 0) return "Value";

        // PascalCase: capitalise first letter
        return char.IsUpper(name[0]) ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}
