# Template: Analyzer

Code template for a new Roslyn diagnostic analyzer + code fix.

Used by `/new-analyzer` command. Replace all `{Placeholder}` values.

---

## File: `{DiagnosticId}Analyzer.cs`

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MicroKit.Logging.Analyzers;

/// <summary>
/// {DiagnosticId}: {Title}.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class {DiagnosticId}Analyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID for this analyzer.</summary>
    public const string DiagnosticId = "{DiagnosticId}";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "{Title}",
        messageFormat: "{MessageFormat with {0} placeholders}",
        category: "{Category}", // MicroKit.Logging.Usage | MicroKit.Logging.Performance | MicroKit.Logging.Security
        defaultSeverity: DiagnosticSeverity.Warning, // Error only for runtime failures
        isEnabledByDefault: true,
        description: "{Longer description for IDE tooltip.}",
        helpLinkUri: $"https://github.com/michaelatsey/microkit/docs/analyzers/{DiagnosticId}");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Register on the most specific operation kind
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;

        // Check if this is the target method
        if (!IsTargetMethod(invocation.TargetMethod))
        {
            return;
        }

        // Perform the specific analysis
        if (HasViolation(invocation, out var violatingArg))
        {
            var diagnostic = Diagnostic.Create(
                Rule,
                violatingArg.Syntax.GetLocation(),
                violatingArg.Syntax.ToString());

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsTargetMethod(IMethodSymbol method)
    {
        // Check method name, containing type, etc.
        // Use SymbolEqualityComparer.Default for symbol comparison
        return false; // TODO: implement
    }

    private static bool HasViolation(IInvocationOperation invocation, out IArgumentOperation? violatingArg)
    {
        violatingArg = null;
        // TODO: implement violation detection
        return false;
    }
}
```

---

## File: `{DiagnosticId}CodeFixProvider.cs`

```csharp
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MicroKit.Logging.Analyzers;

/// <summary>
/// Code fix for <see cref="{DiagnosticId}Analyzer"/>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof({DiagnosticId}CodeFixProvider))]
[Shared]
public sealed class {DiagnosticId}CodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create({DiagnosticId}Analyzer.DiagnosticId);

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
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var node = root?.FindNode(diagnosticSpan);

        if (node is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "{Action-oriented fix title}",
                createChangedDocument: ct => ApplyFixAsync(context.Document, node, ct),
                equivalenceKey: nameof({DiagnosticId}CodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        // TODO: build the replacement node
        // var newNode = ...;
        // var newRoot = root.ReplaceNode(node, newNode);
        // return document.WithSyntaxRoot(newRoot);

        return document;
    }
}
```
