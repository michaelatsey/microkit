using System.Threading;
using Microsoft.CodeAnalysis.Formatting;

namespace MicroKit.Logging.Analyzers;

/// <summary>Code fix for <see cref="MKL0041Analyzer"/>: wraps the log call in an <c>IsEnabled</c> guard.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MKL0041CodeFixProvider))]
[Shared]
public sealed class MKL0041CodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MKL0041Analyzer.DiagnosticId);

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
        // Diagnostic message format: "... LogLevel.{0} ..." — extract the level name
        var logLevelName = ExtractLogLevelFromMessage(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));

        var invocationSyntax = root?.FindNode(diagnostic.Location.SourceSpan) as InvocationExpressionSyntax;
        if (invocationSyntax is null)
            return;

        // We need a statement context — find the enclosing ExpressionStatement
        var statementSyntax = invocationSyntax.Parent as ExpressionStatementSyntax;
        if (statementSyntax is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Wrap in if (logger.IsEnabled(LogLevel.{logLevelName}))",
                createChangedDocument: ct => ApplyFixAsync(
                    context.Document, invocationSyntax, statementSyntax, logLevelName, ct),
                equivalenceKey: nameof(MKL0041CodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        ExpressionStatementSyntax statement,
        string logLevelName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        // Extract the logger receiver expression (e.g. "_logger" from "_logger.LogDebug(...)")
        ExpressionSyntax loggerExpr;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            loggerExpr = memberAccess.Expression;
        else
            return document; // cannot determine receiver; bail out

        // Build: logger.IsEnabled(LogLevel.{Level})
        var isEnabledCall = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                loggerExpr.WithoutTrivia(),
                SyntaxFactory.IdentifierName("IsEnabled")),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("LogLevel"),
                        SyntaxFactory.IdentifierName(logLevelName))))));

        // Build: if (logger.IsEnabled(LogLevel.X)) { <original statement> }
        var ifStatement = SyntaxFactory.IfStatement(
            isEnabledCall,
            SyntaxFactory.Block(statement.WithoutTrivia()))
            .WithLeadingTrivia(statement.GetLeadingTrivia())
            .WithTrailingTrivia(statement.GetTrailingTrivia())
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot = root.ReplaceNode(statement, ifStatement);
        return document.WithSyntaxRoot(newRoot);
    }

    private static string ExtractLogLevelFromMessage(string message)
    {
        // The diagnostic message format ends with "LogLevel.{LevelName}" or just the level name
        foreach (var level in new[] { "Debug", "Trace", "Information", "Warning", "Error", "Critical" })
        {
            if (message.Contains(level))
                return level;
        }

        return "Debug"; // fallback
    }
}
