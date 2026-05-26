using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis.Formatting;

namespace MicroKit.Logging.Analyzers;

/// <summary>Code fix for <see cref="MKL0012Analyzer"/>: converts string concatenation to structured log templates.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MKL0012CodeFixProvider))]
[Shared]
public sealed class MKL0012CodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MKL0012Analyzer.DiagnosticId);

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

        if (node is not BinaryExpressionSyntax binaryExpr)
            return;

        // Walk up to the root of a chained concatenation
        while (binaryExpr.Parent is BinaryExpressionSyntax parentBinary &&
               parentBinary.IsKind(SyntaxKind.AddExpression))
        {
            binaryExpr = parentBinary;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Use structured log template",
                createChangedDocument: ct => ApplyFixAsync(context.Document, binaryExpr, ct),
                equivalenceKey: nameof(MKL0012CodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        BinaryExpressionSyntax rootConcat,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        var templateBuilder = new StringBuilder();
        var extractedArgs = new List<ExpressionSyntax>();

        CollectParts(rootConcat, templateBuilder, extractedArgs);

        var templateLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(templateBuilder.ToString()));

        if (rootConcat.Parent is not ArgumentSyntax ||
            rootConcat.Parent.Parent is not ArgumentListSyntax argList ||
            argList.Parent is not InvocationExpressionSyntax invocationSyntax)
        {
            var newRoot = root.ReplaceNode(rootConcat, templateLiteral);
            return document.WithSyntaxRoot(newRoot);
        }

        var newInvocation = invocationSyntax.ReplaceNode(
            (SyntaxNode)rootConcat, templateLiteral);

        var newArgList = newInvocation.ArgumentList;
        foreach (var expr in extractedArgs)
            newArgList = newArgList.AddArguments(SyntaxFactory.Argument(expr));

        newInvocation = newInvocation
            .WithArgumentList(newArgList)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var finalRoot = root.ReplaceNode(invocationSyntax, newInvocation);
        return document.WithSyntaxRoot(finalRoot);
    }

    private static void CollectParts(
        ExpressionSyntax expr,
        StringBuilder template,
        List<ExpressionSyntax> args)
    {
        if (expr is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            CollectParts(binary.Left, template, args);
            CollectParts(binary.Right, template, args);
            return;
        }

        if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            template.Append(literal.Token.ValueText);
        }
        else
        {
            var placeholder = BuildPlaceholderName(expr);
            template.Append('{').Append(placeholder).Append('}');
            args.Add(expr.WithoutTrivia());
        }
    }

    private static string BuildPlaceholderName(ExpressionSyntax expr)
    {
        var name = expr switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            _ => expr.ToString(),
        };

        if (name.Length == 0) return "Value";
        return char.IsUpper(name[0]) ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}
