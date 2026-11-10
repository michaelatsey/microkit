# Workflow: Adding a Provider

Adding a database provider (MySQL, Oracle, …) next to PostgreSql and SqlServer.

## 1. Plan

Run `implementer` first. The issue is the spec.

## 2. Create the project

`src/MicroKit.Persistence.EntityFrameworkCore.<Provider>/`, namespace = project name, added to
`MicroKit.Persistence.slnx`:

```xml
<ItemGroup>
  <ProjectReference Include="..\MicroKit.Persistence.EntityFrameworkCore\MicroKit.Persistence.EntityFrameworkCore.csproj" />
</ItemGroup>
<ItemGroup>
  <PackageReference Include="<Provider.EFCore.Package>" />
</ItemGroup>
```

The version goes in the root `Directory.Packages.props`. Copy `Description`, `PackageTags` and the
`IL2091` suppression from an existing provider `.csproj`.

## 3. The registration extension

Mirror `UsePostgreSQL<TContext>` / `UseSqlServer<TContext>`: same parameter order, same
`ProviderOptions` handling.

```csharp
public static EfCoreBuilder UseMyProvider<TContext>(
    this EfCoreBuilder builder,
    string connectionString,
    Action<MyProviderDbContextOptionsBuilder>? providerOptions = null,
    ProviderOptions? options = null)
    where TContext : DbContext
{
    ArgumentNullException.ThrowIfNull(builder);
    ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

    var retry = options?.Retry;
    void Configure(DbContextOptionsBuilder opts) =>
        opts.UseMyProvider(connectionString, provider =>
        {
            if (retry is not null)
                provider.EnableRetryOnFailure(retry.MaxRetryCount, retry.MaxRetryDelay, /* extra codes */ null);
            providerOptions?.Invoke(provider);
        });

    if (options?.PoolSize is { } poolSize)
    {
        EnsurePoolable<TContext>(nameof(options)); // private copy, as in the PostgreSQL provider
        builder.Services.AddDbContextPool<TContext>(Configure, poolSize);
    }
    else
    {
        builder.Services.AddDbContext<TContext>(Configure);
    }

    return builder;
}
```

`ProviderOptions` (in EntityFrameworkCore) is provider-neutral: `Retry` (`RetryOnFailureOptions`)
turns on the provider's retrying execution strategy, `PoolSize` switches to a context pool after
`EnsurePoolable` has rejected a context whose constructor takes more than its options. Copy that
private method from the PostgreSQL provider: provider packages do not share internals, because they
version separately from the EntityFrameworkCore package. A
provider without a retrying strategy documents that `Retry` is not supported rather than ignoring it.
Provider-specific options go through the provider builder action, not new `ProviderOptions` members.

## 4. Check the transaction semantics

`EfUnitOfWork` relies on the provider's execution strategy (ADR-006). Verify that the provider's
non-retrying strategy wraps transient failures the way `EfUnitOfWork` expects
(`InvalidOperationException` with an inner `DbUpdateException`/`DbException`), or extend the
translation with a test.

## 5. Tests

- `tests/MicroKit.Persistence.IntegrationTests/Providers/`: registration (plain vs pool, retry on
  or off, a non-poolable context rejected), as `ProviderOptionsTests` does.
- Behaviour against a real server with Testcontainers, skipped without Docker
  (`DockerRequiredFact`), in the existing IntegrationTests project.
- Add the assembly to `IlCallScanTests.LibraryAssemblies` and a namespace test in
  `NamespaceConventionTests`; add a `LayerDependencyTests` case confining the provider package.

## 6. Reviews

`dependency-guardian` (new `.csproj`), `api-reviewer` (new public extension), `ef-core-specialist`.
Update `README.md`, `.claude/CLAUDE.md`, `.claude/rules/dependencies.md`,
`.claude/rules/packaging.md` (package count) and `.claude-context/context/dependency-graph.md`.
