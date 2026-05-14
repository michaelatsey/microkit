---
name: MicroKit HIGH issue resolution status
description: Which HIGH severity issues have been fixed and test counts per module
type: project
---

All HIGH severity issues for phase 3–4 modules resolved (2026-05-12).

**Idempotency (I-4, I-5, I-6)**
- I-4: Removed dead `is` branch in IdempotencyBehavior (TRequest constraint already ensures IIdempotentRequest)
- I-5: Added `UpdateState(IdempotencyState)` to IIdempotencyContext interface; removed concrete IdempotencyContext cast
- I-6: Extracted IRequestHasher into Abstractions; RequestHasher implements it; DI registers IRequestHasher→RequestHasher
- Tests: 24 passing (IdempotencyBehaviorTests, InMemoryIdempotencyStoreTests, RequestHasherTests, IdempotencyContextTests)

**Data (D-2, D-3)**
- D-2: Added IRepository<T> (full CRUD) and IReadRepository<T> (read-only) to MicroKit.Data.Abstractions
- D-3: Removed unused Microsoft.Extensions.Logging PackageReference from MicroKit.Data.Abstractions.csproj
- Tests: 10 passing (RepositoryContractTests with in-memory fake, UnitOfWorkTests)

**MultiTenancy (MT-2, MT-3, MT-5)**
- MT-2: Changed ITenantResolutionStrategy.ResolveAsync and IHttpTenantResolutionStrategy.GetTenantIdentifierAsync to ValueTask<string?>
- MT-3: Added RemoveAsync to ITenantCache; implemented in DefaultTenantCache and RedisTenantCache
- MT-5: Moved HeaderResolutionStrategy, JwtClaimResolutionStrategy, IHttpTenantResolutionStrategy, ClaimsTenantRegionResolver to MicroKit.MultiTenancy.Extensions; removed FrameworkReference from MicroKit.MultiTenancy.csproj
- Tests: 17 passing (DefaultTenantCacheTests, RedisTenantCacheTests, TenantResolutionStrategyTests)

**Caching (C-1, C-2)**
- C-1: Created MicroKit.Caching.Abstractions project; moved ICacheService and CacheOptions there; updated all references
- C-2: Created DistributedCacheOptions; DistributedCacheService now injects IOptions<DistributedCacheOptions> instead of hardcoded JsonSerializerOptions
- Unit tests: 8 passing (DistributedCacheServiceTests)
- Integration tests: MicroKit.Caching.Integration.Tests with Testcontainers.Redis — requires Docker Desktop

**MicroKit.OpenApi — fully implemented (2026-05-14)**
- Removed Autofac (empty module, violates zero-vendor-lockdown rule) and Swashbuckle (unused)
- Removed `NoWarn CS1591`, added all missing XML docs
- Fixed `DeprecationDocumentFilter` bug: description banner was set N times inside path loop
- Completed `ApplyOAuth2Flow` for all four OAuth flows (was only handling ClientCredentials)
- Removed Console.WriteLine debug calls from OpenApiDocumentTransformer
- Deleted 4 dead-code files (.old.cs files, empty OpenApiDescriptor.cs, empty Autofac module)
- Fixed validator: now allows all-deprecated config (no supported versions)
- Added `InternalsVisibleTo("MicroKit.OpenApi.Tests")` for testability
- Added 72 unit tests covering: defaults, options, security options, validator, FilterRegistry, ScalarOptionsRegistry, DeprecationDocumentFilter, RequiredSchemaFilter, ServiceCollectionExtensions
- Added test project to MicroKit.slnx; fixed wrong docs path (was pointing to MicroKit.ApiVersioning)
- Rewrote docs/Readme.md in English (was French, for wrong package)
- Tests: 72 passing; solution total: 307 passing across all 5 test projects; 0 build errors

**Why:** Stabilization pass for phase 3–4 modules before NuGet publishing
**How to apply:** The next batch of work is likely MicroKit.EntityFrameworkCore and remaining MEDIUM issues. Check STRUCTURE.md before adding new projects.
