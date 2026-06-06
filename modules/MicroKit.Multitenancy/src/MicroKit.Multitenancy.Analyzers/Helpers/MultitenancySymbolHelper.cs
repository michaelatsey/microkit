namespace MicroKit.Multitenancy.Analyzers.Helpers;

/// <summary>
/// Shared helpers for resolving and comparing MicroKit.Multitenancy contract types across analyzers.
/// All methods null-guard: if the target type is absent from the compilation (package not referenced),
/// they return false immediately rather than throw.
/// </summary>
internal static class MultitenancySymbolHelper
{
    private const string ITenantEntityName          = "MicroKit.Multitenancy.ITenantEntity";
    private const string TenantIdName               = "MicroKit.Multitenancy.TenantId";
    private const string ITenantContextAccessorName = "MicroKit.Multitenancy.ITenantContextAccessor";
    private const string EfQueryableExtensionsName  = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string IServiceCollectionName     = "Microsoft.Extensions.DependencyInjection.IServiceCollection";

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="type"/> implements
    /// <c>MicroKit.Multitenancy.ITenantEntity</c>.
    /// </summary>
    internal static bool ImplementsITenantEntity(INamedTypeSymbol type, Compilation compilation)
    {
        var tenantEntity = compilation.GetTypeByMetadataName(ITenantEntityName);
        if (tenantEntity is null)
            return false;

        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, tenantEntity))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="type"/> (or any base type) declares
    /// a non-nullable <c>TenantId</c> property of type <c>MicroKit.Multitenancy.TenantId</c>.
    /// Tolerates <c>NullableAnnotation.None</c> (nullable-oblivious codebases) to avoid
    /// false positives. Returns <see langword="true"/> when <c>TenantId</c> symbol cannot be
    /// resolved (package absent) — never penalise the caller for a missing dep.
    /// </summary>
    internal static bool HasNonNullableTenantIdProperty(INamedTypeSymbol type, Compilation compilation)
    {
        var tenantIdType = compilation.GetTypeByMetadataName(TenantIdName);
        if (tenantIdType is null)
            return true; // can't verify → avoid false positives

        var current = (ITypeSymbol?)type;
        while (current is not null)
        {
            foreach (var member in current.GetMembers("TenantId"))
            {
                if (member is IPropertySymbol property &&
                    SymbolEqualityComparer.Default.Equals(property.Type, tenantIdType) &&
                    property.NullableAnnotation != NullableAnnotation.Annotated)
                {
                    return true;
                }
            }

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if any explicit constructor of <paramref name="type"/>
    /// declares a parameter of type <c>MicroKit.Multitenancy.ITenantContextAccessor</c>.
    /// </summary>
    internal static bool HasITenantContextAccessorInConstructor(
        INamedTypeSymbol type, Compilation compilation)
    {
        var accessor = compilation.GetTypeByMetadataName(ITenantContextAccessorName);
        if (accessor is null)
            return false;

        foreach (var ctor in type.Constructors)
        {
            if (ctor.IsImplicitlyDeclared)
                continue;

            foreach (var param in ctor.Parameters)
            {
                if (SymbolEqualityComparer.Default.Equals(param.Type, accessor))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="method"/> is
    /// <c>EntityFrameworkQueryableExtensions.IgnoreQueryFilters</c>.
    /// </summary>
    internal static bool IsIgnoreQueryFiltersMethod(IMethodSymbol method, Compilation compilation)
    {
        var efExtensions = compilation.GetTypeByMetadataName(EfQueryableExtensionsName);
        if (efExtensions is null)
            return false;

        return method.Name == "IgnoreQueryFilters" &&
               SymbolEqualityComparer.Default.Equals(method.ContainingType, efExtensions);
    }

    /// <summary>
    /// Returns <see langword="true"/> if a <c>// [MTK-BYPASS]</c> comment appears on the same
    /// line as <paramref name="operation"/> or on the first non-blank line immediately above it.
    /// Blank lines between the comment and the call are skipped; the search stops at the first
    /// non-blank preceding line.
    /// </summary>
    internal static bool HasBypassComment(IOperation operation)
    {
        var syntaxNode = operation.Syntax;
        var sourceText = syntaxNode.SyntaxTree.GetText();
        var lines      = sourceText.Lines;
        var callLine   = lines.GetLineFromPosition(syntaxNode.SpanStart).LineNumber;

        if (ContainsBypassComment(lines[callLine].ToString()))
            return true;

        // Walk upward: skip blank lines, stop at first non-blank
        for (var i = callLine - 1; i >= 0; i--)
        {
            var lineText = lines[i].ToString();
            if (string.IsNullOrWhiteSpace(lineText))
                continue;
            return ContainsBypassComment(lineText);
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="receiverType"/> is
    /// <c>Microsoft.Extensions.DependencyInjection.IServiceCollection</c> or implements it.
    /// Used to avoid false positives from user-defined <c>AddSingleton</c> methods.
    /// </summary>
    internal static bool IsIServiceCollectionReceiver(ITypeSymbol? receiverType, Compilation compilation)
    {
        if (receiverType is null)
            return false;

        var serviceCollection = compilation.GetTypeByMetadataName(IServiceCollectionName);
        if (serviceCollection is null)
            return false;

        if (SymbolEqualityComparer.Default.Equals(receiverType, serviceCollection))
            return true;

        if (receiverType is INamedTypeSymbol named)
        {
            foreach (var iface in named.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface, serviceCollection))
                    return true;
            }
        }

        return false;
    }

    private static bool ContainsBypassComment(string line) =>
        line.Contains("// [MTK-BYPASS]");
}
