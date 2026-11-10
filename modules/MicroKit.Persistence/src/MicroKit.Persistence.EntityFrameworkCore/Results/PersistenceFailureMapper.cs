using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using MicroKit.Result;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MicroKit.Persistence.EntityFrameworkCore;

/// <summary>
/// Turns a persistence failure into the <see cref="PersistenceErrors"/> error the
/// <see cref="IResultUnitOfWork"/> path returns, from the exception's type, the provider's error code
/// (<see cref="IDatabaseFailureClassifier"/>) and the model — never from a message.
/// </summary>
/// <remarks>
/// <para>It runs after the transaction is rolled back, when the tracker is already cleared: the failing
/// entries' entity types are still known, and phase 1 makes each entity type's aggregate root unique, so
/// the root is found from the type alone.</para>
/// <list type="bullet">
///   <item><description><c>CONCURRENCY</c>: a <see cref="DbUpdateConcurrencyException"/> (root from its
///     entries), or an <see cref="AggregateVersionConflictException"/> (root from its name).</description></item>
///   <item><description><c>DUPLICATE</c> / <c>REFERENCE</c>: a <see cref="DbUpdateException"/> whose
///     <see cref="DbException"/> the classifier recognises (root from its entries), or a bare
///     <see cref="DbException"/> — a constraint checked at COMMIT, raw SQL — with no root.</description></item>
///   <item><description>Never: a chain holding a <see cref="RetryLimitExceededException"/>, and anything
///     else.</description></item>
/// </list>
/// </remarks>
internal static class PersistenceFailureMapper
{
    internal static bool TryMap(Exception exception, DbContext context, [NotNullWhen(true)] out CategorizedError? error)
    {
        error = null;
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case RetryLimitExceededException:
                    return false;
                case DbUpdateConcurrencyException concurrency:
                    error = PersistenceErrors.Concurrency(ErrorNameOf(context, concurrency.Entries));
                    return true;
                case AggregateVersionConflictException conflict:
                    error = PersistenceErrors.Concurrency(conflict.ErrorName);
                    return true;
                case DbUpdateException { InnerException: DbException database } update:
                    return TryMapDatabase(database, context, update.Entries, out error);
                case DbException database:
                    return TryMapDatabase(database, context, [], out error);
            }
        }

        return false;
    }

    /// <summary>Gets the error name of the aggregate an entity type belongs to, when it is a root itself.</summary>
    internal static string? ErrorNameOfRoot(IEntityType root) =>
        AggregateErrorNameConvention.ErrorNameOf(TopMostRoot(root));

    private static bool TryMapDatabase(
        DbException database,
        DbContext context,
        IReadOnlyList<EntityEntry> entries,
        [NotNullWhen(true)] out CategorizedError? error)
    {
        error = null;
        var classifier = context.GetService<IDbContextOptions>()
            .FindExtension<MicroKitPersistenceOptionsExtension>()?.FailureClassifier;
        switch (classifier?.Classify(database) ?? DatabaseFailure.None)
        {
            case DatabaseFailure.UniqueViolation:
                error = PersistenceErrors.Duplicate(ErrorNameOf(context, entries));
                return true;
            case DatabaseFailure.ForeignKeyViolation:
                error = PersistenceErrors.Reference(ErrorNameOf(context, entries));
                return true;
            case DatabaseFailure.ForeignKeyOrCheckViolation when entries.Count > 0 && !DeclaresCheckConstraint(context, entries):
                error = PersistenceErrors.Reference(ErrorNameOf(context, entries));
                return true;
            default:
                return false;
        }
    }

    // The root's name when every failing entry belongs to one aggregate; null for none, several, or an
    // entry outside every boundary: a root is never guessed.
    private static string? ErrorNameOf(DbContext context, IReadOnlyList<EntityEntry> entries)
    {
        if (entries.Count == 0)
            return null;

        var map = AggregateBoundaryMap.For(context.Model);
        string? name = null;
        foreach (var entry in entries)
        {
            var type = entry.Metadata;
            IEntityType? root = AggregateBoundaryMap.IsRoot(type) ? TopMostRoot(type)
                : map.IsInside(type) ? map.RootOf(type)
                : null;
            if (root is null || AggregateErrorNameConvention.ErrorNameOf(root) is not { } current)
                return null;

            if (name is null)
                name = current;
            else if (!string.Equals(name, current, StringComparison.Ordinal))
                return null;
        }

        return name;
    }

    private static IEntityType TopMostRoot(IEntityType type)
    {
        var root = type;
        while (root.BaseType is { } baseType && AggregateBoundaryMap.IsRoot(baseType))
            root = baseType;
        return root;
    }

    // SQL Server reports a foreign-key and a check violation with one number (547): decided from the
    // model only. A check constraint declared by any entity type sharing a failing entry's table makes it
    // undecidable, and so does a model without its design-time view (a compiled model).
    private static bool DeclaresCheckConstraint(DbContext context, IReadOnlyList<EntityEntry> entries)
    {
        IModel designModel;
        try
        {
            designModel = context.GetService<IDesignTimeModel>().Model;
        }
        catch (InvalidOperationException)
        {
            return true;
        }

        foreach (var entry in entries)
        {
            if (designModel.FindEntityType(entry.Metadata.Name) is not { } type)
                return true;

            foreach (var mapping in type.GetTableMappings())
            {
                foreach (var shared in mapping.Table.EntityTypeMappings)
                {
                    if (shared.TypeBase is IEntityType sharer && sharer.GetCheckConstraints().Any())
                        return true;
                }
            }
        }

        return false;
    }
}
