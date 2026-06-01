namespace MicroKit.Persistence.Analyzers.Helpers;

/// <summary>
/// Shared helpers for resolving and comparing MicroKit.Persistence contract types across analyzers.
/// All methods null-guard: if the target type is absent from the compilation (package not referenced),
/// they return false immediately rather than throw.
/// </summary>
internal static class PersistenceSymbolHelper
{
    // Both IReadRepository<T> variants
    private const string AbsReadRepositoryName = "MicroKit.Persistence.Abstractions.IReadRepository`1";
    private const string CoreReadRepositoryName = "MicroKit.Persistence.IReadRepository`1";
    private const string RepositoryName         = "MicroKit.Persistence.Abstractions.IRepository`1";
    private const string UnitOfWorkName         = "MicroKit.Persistence.Abstractions.IUnitOfWork";
    private const string TransactionalUoWName   = "MicroKit.Persistence.EntityFrameworkCore.ITransactionalUnitOfWork";
    private const string DbContextName          = "Microsoft.EntityFrameworkCore.DbContext";
    private const string TaskName               = "System.Threading.Tasks.Task`1";
    private const string ValueTaskName          = "System.Threading.Tasks.ValueTask`1";
    private const string IQueryableName         = "System.Linq.IQueryable`1";

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="type"/> implements either
    /// <c>MicroKit.Persistence.Abstractions.IReadRepository&lt;T&gt;</c> (the Abstractions marker)
    /// or <c>MicroKit.Persistence.IReadRepository&lt;T&gt;</c> (the Core extension).
    /// </summary>
    internal static bool IsReadRepositoryImplementation(INamedTypeSymbol type, Compilation compilation)
    {
        var absMarker  = compilation.GetTypeByMetadataName(AbsReadRepositoryName);
        var coreIface  = compilation.GetTypeByMetadataName(CoreReadRepositoryName);

        if (absMarker is null && coreIface is null)
            return false;

        return ImplementsGenericInterface(type, absMarker) ||
               ImplementsGenericInterface(type, coreIface);
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="type"/> implements
    /// <c>IRepository&lt;T&gt;</c> or either variant of <c>IReadRepository&lt;T&gt;</c>.
    /// </summary>
    internal static bool IsRepositoryImplementation(INamedTypeSymbol type, Compilation compilation)
    {
        var repo = compilation.GetTypeByMetadataName(RepositoryName);
        if (repo is not null && ImplementsGenericInterface(type, repo))
            return true;

        return IsReadRepositoryImplementation(type, compilation);
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="type"/> implements <c>IUnitOfWork</c>
    /// or <c>ITransactionalUnitOfWork</c>.
    /// </summary>
    internal static bool IsUnitOfWorkImplementation(INamedTypeSymbol type, Compilation compilation)
    {
        var uow            = compilation.GetTypeByMetadataName(UnitOfWorkName);
        var transactional  = compilation.GetTypeByMetadataName(TransactionalUoWName);

        return (uow           is not null && ImplementsInterface(type, uow)) ||
               (transactional is not null && ImplementsInterface(type, transactional));
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="type"/> is or derives from
    /// <c>Microsoft.EntityFrameworkCore.DbContext</c>.
    /// </summary>
    internal static bool IsOrDerivesFromDbContext(ITypeSymbol type, Compilation compilation)
    {
        var dbContext = compilation.GetTypeByMetadataName(DbContextName);
        if (dbContext is null)
            return false;

        var current = type;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, dbContext))
                return true;
            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Strips one level of <c>Task&lt;T&gt;</c> or <c>ValueTask&lt;T&gt;</c> wrapping.
    /// Returns <paramref name="type"/> unchanged if it is not a generic Task or ValueTask.
    /// </summary>
    internal static ITypeSymbol UnwrapTaskType(ITypeSymbol type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol named || !named.IsGenericType || named.TypeArguments.Length != 1)
            return type;

        var taskType      = compilation.GetTypeByMetadataName(TaskName);
        var valueTaskType = compilation.GetTypeByMetadataName(ValueTaskName);
        var constructed   = named.ConstructedFrom;

        if ((taskType      is not null && SymbolEqualityComparer.Default.Equals(constructed, taskType)) ||
            (valueTaskType is not null && SymbolEqualityComparer.Default.Equals(constructed, valueTaskType)))
        {
            return named.TypeArguments[0];
        }

        return type;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="type"/> is <c>IQueryable&lt;T&gt;</c>
    /// from <c>System.Linq</c>.
    /// </summary>
    internal static bool IsIQueryable(ITypeSymbol type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol named || !named.IsGenericType)
            return false;

        var iqueryable = compilation.GetTypeByMetadataName(IQueryableName);
        if (iqueryable is null)
            return false;

        return SymbolEqualityComparer.Default.Equals(named.ConstructedFrom, iqueryable);
    }

    // Compares ConstructedFrom for generic interfaces (e.g. IReadRepository<User> vs IReadRepository<T>)
    private static bool ImplementsGenericInterface(INamedTypeSymbol type, INamedTypeSymbol? interfaceSymbol)
    {
        if (interfaceSymbol is null)
            return false;

        foreach (var iface in type.AllInterfaces)
        {
            var toCompare = iface.IsGenericType ? iface.ConstructedFrom : (ITypeSymbol)iface;
            if (SymbolEqualityComparer.Default.Equals(toCompare, interfaceSymbol))
                return true;
        }

        return false;
    }

    // Direct equality for non-generic interfaces (e.g. IUnitOfWork)
    private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceSymbol)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, interfaceSymbol))
                return true;
        }

        return false;
    }
}
