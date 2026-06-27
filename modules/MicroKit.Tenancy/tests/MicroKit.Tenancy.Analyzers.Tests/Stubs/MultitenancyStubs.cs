namespace MicroKit.Tenancy.Analyzers.Tests.Stubs;

/// <summary>
/// Inline stub declarations used by analyzer tests.
/// The analyzer matches types by fully-qualified metadata name — stubs must use
/// the exact namespaces and type names that the analyzers resolve via
/// <c>Compilation.GetTypeByMetadataName</c>.
///
/// All <c>using</c> directives are file-scoped (at the top of the stub content)
/// so test code appended after these stubs can use simple type names without
/// adding additional <c>using</c> clauses (which would cause CS1529).
/// </summary>
internal static class MultitenancyStubs
{
    /// <summary>
    /// All multitenancy contract stubs plus minimal EF Core and DI stubs.
    /// Prepend to any test source string. Do NOT add <c>using</c> directives
    /// to the appended test code — use simple names (already in scope).
    /// </summary>
    public const string All = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using MicroKit.Tenancy;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;

        namespace MicroKit.Tenancy
        {
            public sealed record TenantId(Guid Value);

            public interface ITenantInfo
            {
                TenantId Id { get; }
                string Name { get; }
            }

            public interface ITenantContextAccessor
            {
                ITenantInfo? GetTenant();
                void SetTenant(ITenantInfo? tenant);
                IDisposable CreateScope(ITenantInfo tenant);
            }

            public interface ITenantEntity
            {
                TenantId TenantId { get; }
            }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public static class EntityFrameworkQueryableExtensions
            {
                // Architect issue #6: where T : class constraint must be present
                public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) where T : class
                    => source;
            }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }

            public static class ServiceCollectionServiceExtensions
            {
                public static IServiceCollection AddSingleton<TService, TImpl>(this IServiceCollection services)
                    where TService : class
                    where TImpl : class, TService
                    => services;

                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services)
                    where TService : class
                    => services;

                public static IServiceCollection AddSingleton(
                    this IServiceCollection services,
                    Type serviceType,
                    Type implementationType)
                    => services;

                public static IServiceCollection AddScoped<TService, TImpl>(this IServiceCollection services)
                    where TService : class
                    where TImpl : class, TService
                    => services;

                public static IServiceCollection AddTransient<TService, TImpl>(this IServiceCollection services)
                    where TService : class
                    where TImpl : class, TService
                    => services;
            }
        }

        // Required for sealed record positional parameters (init setters) in test compilations
        // that target reference assemblies without System.Runtime.CompilerServices.IsExternalInit.
        namespace System.Runtime.CompilerServices
        {
            internal static class IsExternalInit { }
        }

        """;
}
