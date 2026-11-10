namespace MicroKit.Persistence.Analyzers.Helpers;

/// <summary>
/// The symbols the query-shape analyzers (MKP006–MKP009) need, resolved once per compilation by
/// <see cref="PersistenceSymbolHelper.ResolveQuerySymbols"/>. A symbol is <see langword="null"/> when its
/// package is not referenced; each analyzer gates its rules on the group it needs (EF Core, relational,
/// MicroKit repositories), never on all of them.
/// </summary>
internal sealed class QuerySymbols
{
    internal QuerySymbols(Compilation compilation)
    {
        IQueryableOfT                 = compilation.GetTypeByMetadataName("System.Linq.IQueryable`1");
        IEnumerableOfT                = compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
        Queryable                     = compilation.GetTypeByMetadataName("System.Linq.Queryable");
        Enumerable                    = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        MemoryExtensions              = compilation.GetTypeByMetadataName("System.MemoryExtensions");
        NotMapped                     = compilation.GetTypeByMetadataName("System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute");
        EfQueryableExtensions         = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");
        RelationalQueryableExtensions = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions");
        DbSet                         = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1");
        DbContext                     = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
        NavigationEntry               = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.ChangeTracking.NavigationEntry");
        QueryOptions                  = compilation.GetTypeByMetadataName("MicroKit.Persistence.QueryOptions`1");
        QueryOptionsExtensions        = compilation.GetTypeByMetadataName("MicroKit.Persistence.QueryOptionsExtensions");
        Repository                    = compilation.GetTypeByMetadataName("MicroKit.Persistence.Abstractions.IRepository`2");
        ReadRepository                = compilation.GetTypeByMetadataName("MicroKit.Persistence.IReadRepository`1");
    }

    public INamedTypeSymbol? IQueryableOfT { get; }
    public INamedTypeSymbol? IEnumerableOfT { get; }
    public INamedTypeSymbol? Queryable { get; }
    public INamedTypeSymbol? Enumerable { get; }
    public INamedTypeSymbol? MemoryExtensions { get; }
    public INamedTypeSymbol? NotMapped { get; }
    public INamedTypeSymbol? EfQueryableExtensions { get; }
    public INamedTypeSymbol? RelationalQueryableExtensions { get; }
    public INamedTypeSymbol? DbSet { get; }
    public INamedTypeSymbol? DbContext { get; }
    public INamedTypeSymbol? NavigationEntry { get; }
    public INamedTypeSymbol? QueryOptions { get; }
    public INamedTypeSymbol? QueryOptionsExtensions { get; }
    public INamedTypeSymbol? Repository { get; }
    public INamedTypeSymbol? ReadRepository { get; }

    /// <summary>EF Core's query surface is referenced.</summary>
    public bool HasEntityFrameworkCore => EfQueryableExtensions is not null && DbSet is not null && IQueryableOfT is not null;

    /// <summary>EF Core's relational split and single query hints are referenced.</summary>
    public bool HasRelational => HasEntityFrameworkCore && RelationalQueryableExtensions is not null;

    /// <summary>A MicroKit repository contract is referenced.</summary>
    public bool HasRepositories => Repository is not null || ReadRepository is not null;

    /// <summary>The type is <c>IQueryable&lt;T&gt;</c> or implements it (<c>DbSet</c>, <c>IOrderedQueryable</c>, <c>IIncludableQueryable</c>).</summary>
    public bool IsQueryable(ITypeSymbol? type) => IsOrImplements(type, IQueryableOfT);

    /// <summary>The type is <c>IEnumerable&lt;T&gt;</c> or implements it, and is not <see cref="string"/>.</summary>
    public bool IsCollection(ITypeSymbol? type) =>
        type is not null && type.SpecialType != SpecialType.System_String && IsOrImplements(type, IEnumerableOfT);

    /// <summary>The type is or derives from EF Core's <c>DbSet&lt;T&gt;</c>.</summary>
    public bool IsDbSet(ITypeSymbol? type)
    {
        if (DbSet is null)
            return false;
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, DbSet))
                return true;
        }

        return false;
    }

    /// <summary><paramref name="method"/> is declared by <paramref name="type"/> and named <paramref name="name"/>.</summary>
    public static bool Is(IMethodSymbol method, INamedTypeSymbol? type, string name) =>
        type is not null && method.Name == name && IsDeclaredBy(method, type);

    /// <summary><paramref name="method"/> is declared by <paramref name="type"/>.</summary>
    public static bool IsDeclaredBy(IMethodSymbol method, INamedTypeSymbol? type) =>
        type is not null && SymbolEqualityComparer.Default.Equals((method.ReducedFrom ?? method).ContainingType.OriginalDefinition, type);

    /// <summary><paramref name="type"/> derives from, or is, <paramref name="baseType"/>.</summary>
    public static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol? baseType)
    {
        if (baseType is null)
            return false;
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
                return true;
        }

        return false;
    }

    /// <summary><paramref name="type"/> is, or implements, a construction of <paramref name="generic"/>.</summary>
    public static bool IsOrImplements(ITypeSymbol? type, INamedTypeSymbol? generic)
    {
        if (type is null || generic is null)
            return false;
        if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, generic))
            return true;
        foreach (var candidate in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, generic))
                return true;
        }

        return false;
    }
}
