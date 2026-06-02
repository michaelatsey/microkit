# Rule: Testing — MicroKit.Multitenancy

## Test Project Responsibilities

| Project | Tests |
|---------|-------|
| `UnitTests` | Resolution pipeline, AsyncLocal context, ITenantStore implementations — no real DB, no HTTP |
| `IntegrationTests` | EF Core isolation with real DB (SQLite in-memory or Testcontainers), full middleware pipeline |
| `ArchitectureTests` | Dependency rules via NetArchTest, no ITenantContextAccessor in Singleton |
| `PerformanceTests` | Resolution pipeline overhead, AsyncLocal capture/restore cost via BenchmarkDotNet |

## Library Choices

- **xUnit** — test framework
- **Shouldly** — all assertions — see root `.claude/rules/testing-libraries.md`
- **FluentAssertions is banned** — any `.Should()` call blocks the build
- **NSubstitute** — all mocks and stubs
- **NetArchTest.Rules** — architecture tests only

## Isolation Principle

Unit tests must not require HTTP context, EF Core, or DI container:

```csharp
// ✅ Pure unit test — pipeline resolution without HTTP
var strategy1 = Substitute.For<ITenantResolutionStrategy>();
strategy1.Order.Returns(1);
strategy1.TryResolveAsync(Arg.Any<CancellationToken>())
    .Returns(Result<TenantId>.Failure(MultitenancyErrors.TenantNotFound));

var strategy2 = Substitute.For<ITenantResolutionStrategy>();
strategy2.Order.Returns(2);
strategy2.TryResolveAsync(Arg.Any<CancellationToken>())
    .Returns(Result<TenantId>.Success(new TenantId(Guid.NewGuid())));

var pipeline = new TenantResolutionPipeline([strategy1, strategy2], store, logger);
var result = await pipeline.ResolveAsync();

result.IsSuccess.ShouldBeTrue();
```

## Mandatory Test Cases

### Resolution Pipeline
- [ ] `ResolveAsync_WhenFirstStrategySucceeds_ShortCircuits`
- [ ] `ResolveAsync_WhenAllStrategiesFail_ReturnsFailure`
- [ ] `ResolveAsync_WhenStrategyThrows_ReturnsFailure` (pipeline never propagates exception)
- [ ] `ResolveAsync_ExecutesStrategiesInOrderAscending`

### AsyncLocal Context
- [ ] `SetTenant_GetTenant_ReturnsSameTenant`
- [ ] `CreateScope_DisposedScope_RestoresPreviousTenant`
- [ ] `CreateScope_NestedScopes_RestoreCorrectly`
- [ ] `AsyncLocal_ParallelTasks_DoNotLeakContext` (critical isolation test)

### EF Core Isolation (IntegrationTests)
- [ ] `Query_WithTenantContext_ReturnsOnlyCurrentTenantEntities`
- [ ] `SaveChanges_AutoStampsTenantId_OnAddedEntity`
- [ ] `SaveChanges_WhenNoTenantContext_ThrowsInvalidOperation`
- [ ] `Query_WithIgnoreQueryFilters_ReturnsCrossTenantEntities`
- [ ] `ParallelRequests_DifferentTenants_DoNotSeeEachOthersData`

### Analyzers (if testing analyzer project)
- [ ] MKT001 triggered on ITenantEntity without TenantId
- [ ] MKT001 not triggered when TenantId present and non-nullable
- [ ] MKT002 triggered on IgnoreQueryFilters without comment
- [ ] MKT002 not triggered with [MTK-BYPASS] comment
- [ ] MKT003 triggered on Singleton with ITenantContextAccessor constructor param
- [ ] MKT003 not triggered for Scoped/Transient

## Test Project .csproj

```xml
<GenerateDocumentationFile>false</GenerateDocumentationFile>
<NoWarn>CS1591;CA1707</NoWarn>
```

## Detecting Violations

```bash
# FluentAssertions must never appear
grep -rn 'FluentAssertions\|\.Should()\.' modules/MicroKit.Multitenancy/tests/ --include="*.cs"
```
