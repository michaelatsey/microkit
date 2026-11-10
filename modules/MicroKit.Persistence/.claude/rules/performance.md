# Rule: Performance — MicroKit.Persistence

Every query of a consuming application flows through these repositories; their overhead
multiplies by request rate.

## Read paths do not track

`EfReadRepository` runs every query `AsNoTracking()` — there is no opt-out — and `FindAsync`
never consults the tracker. A typed read repository keeps that: its own queries start from
`AsNoTracking()`.

## Write-side loads are whole aggregates

`EfRepository.FindAsync` loads the full aggregate in one tracked query, or 1 + C queries when it
has C ≥ 2 collections (split). It is not a primary-key lookup; it costs what the aggregate
weighs. Keep aggregates small, and mark navigations to reference data `IsOutsideAggregate()`.

## ValueTask

Repository and unit-of-work methods return `ValueTask`/`ValueTask<T>`: a tracked hit or an
in-memory double completes synchronously without allocating a task.

## One commit per command

One `IUnitOfWork.CommitAsync` per command, at the end (`TransactionBehavior` issues it when the
MicroKit.MediatR pipeline is used); never a commit mid-handler.

## Existence checks

`AnyAsync` over `CountAsync(...) > 0`. Both drop includes, ordering and pagination.

## Project on the server

A read that returns DTOs projects in the query (`.Select(Dto.Projection)`) inside a typed read
repository, rather than loading entities and mapping in memory.

## Paginate in the database

`QueryOptions.WithPagination` becomes `Skip/Take` in SQL. `PaginationLimits` bounds every list
(default 50, max 500); `ListPagedAsync` issues a count and a page query. Always order a paginated
query (`WithOrderBy…`) so page boundaries are deterministic.

## Split queries

Two or more collection includes in `QueryOptions` call for `AsSplitQuery()`; the write-side
aggregate load switches to split by itself.

## Compiled queries

The repositories' finds by id run compiled queries already, one per model (ADR-013). For another measured
hot path, a typed repository may hold an `EF.CompileAsyncQuery` delegate — cached per `IModel`, not in a
plain `static readonly` field: EF Core refuses to run a compiled query on another model (another provider, a
model variant).

## Keyset pages

A deep offset page reads and discards every row before it; `ListKeysetAsync` starts after the cursor's row
and costs the same at any depth. Prefer it for feeds and exports; an index on the sort keys plus the primary
key serves it.

## No sync-over-async; ConfigureAwait(false)

Never `.Result`/`.Wait()`. Every `await` in library code has `.ConfigureAwait(false)`.

One recorded exception: `DockerProbe.IsAvailable` (MicroKit.Persistence.Testing, ADR-015). It is a cached,
once-per-process check whose public API must be synchronous — a test framework's skip condition reads it in an
attribute constructor — and only the asynchronous socket and pipe calls bound every wait (a named pipe has no read
timeout). It runs the probe through `Task.Run(…).GetAwaiter().GetResult()`, so no captured context can deadlock it.
Nothing else blocks on asynchronous work.

## Budget

Targets: `.claude-context/standards/performance-budget.md`. There is no benchmark project yet, so
the budget is a review reference, not a gate.
