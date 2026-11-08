namespace MicroKit.Persistence.Analyzers.Tests.Stubs;

/// <summary>
/// Inline stub declarations used by analyzer tests.
/// The analyzer matches types by fully-qualified metadata name — stubs must use
/// the exact namespaces and method signatures that the analyzers resolve via
/// <c>Compilation.GetTypeByMetadataName</c>.
///
/// All <c>using</c> directives are file-scoped (at the top of the stub content)
/// so test code appended after these stubs can use simple type names without
/// adding any additional <c>using</c> clauses (which would cause CS1529 after
/// the namespace blocks below).
/// </summary>
internal static class PersistenceStubs
{
    /// <summary>
    /// All persistence contract stubs and a minimal EF Core DbContext stub.
    /// Prepend to any test source string. Do NOT add <c>using</c> directives
    /// to the appended test code — use simple names (already in scope) or
    /// fully-qualified names for types defined in multiple namespaces.
    /// </summary>
    public const string All = """
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using MicroKit.Persistence;
        using MicroKit.Persistence.Abstractions;
        using MicroKit.Persistence.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore;

        namespace MicroKit.Persistence.Abstractions
        {
            public interface IAggregateRoot { }

            public interface IEntityId { }

            public interface IRepository<TAggregate, TId>
                where TAggregate : IAggregateRoot
                where TId : IEntityId
            {
                ValueTask<TAggregate> FindAsync(TId id, CancellationToken ct = default);
                ValueTask AddAsync(TAggregate aggregate, CancellationToken ct = default);
                ValueTask DeleteAsync(TAggregate aggregate, CancellationToken ct = default);
            }

            public interface IUnitOfWork
            {
                ValueTask CommitAsync(CancellationToken ct = default);
            }
        }

        namespace MicroKit.Persistence
        {
            // The single read contract (#196 D8). Members are omitted: the analyzers match the
            // interface by metadata name only, and test classes declare what they exercise.
            public interface IReadRepository<TAggregate>
                where TAggregate : IAggregateRoot { }
        }

        namespace MicroKit.Persistence.EntityFrameworkCore
        {
            public interface ITransactionalUnitOfWork : IUnitOfWork { }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public abstract class DbContext
            {
                public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
                    => Task.FromResult(0);
                public virtual int SaveChanges() => 0;
            }
        }

        """;
}
