# Rule: Analyzers — MicroKit.Persistence

Applies to any task touching `MicroKit.Persistence.Analyzers` or its tests.

## Diagnostic Registry

| ID | Severity | Category | Title | Analyzer class |
|----|----------|----------|-------|----------------|
| `MKP001` | Error | Usage | Read repository calls CommitAsync or SaveChangesAsync | `ReadRepositoryMutationAnalyzer` |
| `MKP002` | Error | Usage | Read repository declares or calls a write method | `ReadRepositoryMutationAnalyzer` |
| `MKP003` | Warning | Usage | SaveChangesAsync called directly — bypasses IUnitOfWork | `DirectSaveChangesAnalyzer` |
| `MKP004` | Warning | Design | DbContext injected outside infrastructure layer | `DbContextInjectionAnalyzer` |
| `MKP005` | Error | Design | Repository method exposes IQueryable\<T\> as return type | `RepositoryIQueryableLeakAnalyzer` |
| `MKP006` | Info | Performance | A query includes two or more collections without a split decision | `CollectionIncludeAnalyzer` |
| `MKP007` | Warning | Performance | A query runs on each iteration of a loop | `QueryInLoopAnalyzer` |
| `MKP008` | Warning | Performance | A query is materialized before it is filtered | `MaterializeBeforeFilterAnalyzer` |
| `MKP009` | Warning | Usage | IgnoreQueryFilters() disables every query filter | `IgnoreAllQueryFiltersAnalyzer` |

Categories are `MicroKit.Persistence.Usage`, `MicroKit.Persistence.Design` and `MicroKit.Persistence.Performance`
(`DiagnosticCategories`). No diagnostic ships a code fix (ADR-014).

## Detection Strategies

### MKP001 + MKP002 — `ReadRepositoryMutationAnalyzer`

- **Invocation axis** (`RegisterOperationAction`, `OperationKind.Invocation`): the containing type
  implements `MicroKit.Persistence.IReadRepository<T>` and calls `CommitAsync`, `SaveChangesAsync` or `SaveChanges`
  (MKP001) or `AddAsync`/`UpdateAsync`/`DeleteAsync` (MKP002).
- **Declaration axis** (`RegisterSymbolAction`, `SymbolKind.Method`): a read repository declares one
  of the write methods (MKP002, at the method name).

`UpdateAsync` is not on any MicroKit contract; the analyzer still flags it as a write verb.

### MKP003 — `DirectSaveChangesAnalyzer`

- `SaveChanges`/`SaveChangesAsync` invoked on a receiver deriving from `DbContext`.
- Excluded: the containing type implements `IUnitOfWork` or `ITransactionalUnitOfWork`
  (`EfUnitOfWork<TContext>`).
- A `DbContext` subclass overriding `SaveChangesAsync` (auditing, events) is flagged; suppress
  with `#pragma warning disable MKP003`.

### MKP004 — `DbContextInjectionAnalyzer`

- `RegisterSymbolAction(SymbolKind.NamedType)`: a constructor parameter derives from `DbContext`
  and the class's namespace contains none of `Infrastructure`, `Persistence`,
  `EntityFrameworkCore`, `Repository`, `Data`.
- Heuristic: suppress for infrastructure in an unconventional namespace.

### MKP005 — `RepositoryIQueryableLeakAnalyzer`

- `RegisterSymbolAction(SymbolKind.Method)`: a public or internal method of a type implementing
  `IRepository<TAggregate, TId>` or `IReadRepository<T>` returns `IQueryable<T>`, possibly wrapped
  in `Task<>`/`ValueTask<>`. Reported at the method name.
- `IAsyncEnumerable<T>` is out of scope.

### MKP006–MKP009 — the query shapes (ADR-014)

What they cannot see, they leave alone; MKP006 and MKP008 judge a whole chain in one expression. Symbols come from
`PersistenceSymbolHelper.ResolveQuerySymbols` once per compilation (`RegisterCompilationStartAction`), each rule group
gated on its own (EF Core, relational, the MicroKit contracts). `Helpers/QueryChain` walks fluent chains (the receiver
is the instance or the argument with `Parameter.Ordinal == 0`; implicit conversions are stripped; `await` and
`ConfigureAwait` unwrapped); `Helpers/QueryExecution` tells an executing call from composition. Diagnostics span the
method name to the end of its argument list.

- **MKP006** — at the outermost call of a chain: two or more collection `Include`/`ThenInclude` lambdas (by navigation
  path; `Include(string)` ignored), no `AsSplitQuery`/`AsSingleQuery` and no projection or scalar executor (EF Core
  ignores the includes then) in the chain, the query whole — rooted at a
  `DbSet` and executed (or enumerated by `foreach`), or the body of a `WithIncludes`/`Includes` lambda whose options
  carry no `AsSplitQuery()`/`AsSplitQueryEnabled = true` and do not go to `CountAsync`, `AnyAsync` or a projection.
  Silent without `RelationalQueryableExtensions`.
- **MKP007** — an executing call (LINQ executor over a query, EF Core async executor or `Load`, `Find`, explicit
  loading, a repository's awaitable method other than `Add*`/`Delete*`/`Remove*`/`ListKeysetAsync`/`ListPagedAsync`),
  or a `foreach` over a query, whose nearest `foreach`/`for` runs it per iteration. A `foreach` collection or a `for`
  initializer is exempt for that loop only; a lambda or local function ends the search; `while`/`do` and a `for`
  without a condition do not count;
  LINQ to objects through `AsQueryable()` is ignored.
- **MKP008** — `ToList`/`ToArray` (or an awaited `ToListAsync`/`ToArrayAsync`) directly under an `Enumerable` filter
  with an expression-bodied, single-parameter predicate (`Last*` only after an `OrderBy`), the query rooted at a
  `DbSet` through filter-commuting operators only, and a predicate that plainly translates: single-hop public,
  settable, non-`[NotMapped]` scalar properties, literals, built-in or scalar-type operators, `string.Contains`/`StartsWith`/
  `EndsWith`, `Contains` over a captured collection; anything else that reads the element makes it silent.
- **MKP009** — an invocation or method-group reference of the parameterless
  `EntityFrameworkQueryableExtensions.IgnoreQueryFilters<T>`; not inside `nameof`.

## Null-Guard Convention

`PersistenceSymbolHelper` is the single entry point for symbol resolution, by metadata name
(`MicroKit.Persistence.IReadRepository`1`, `MicroKit.Persistence.Abstractions.IRepository`2`,
`…Abstractions.IUnitOfWork`, `…EntityFrameworkCore.ITransactionalUnitOfWork`,
`Microsoft.EntityFrameworkCore.DbContext`; `ResolveQuerySymbols` for MKP006–MKP009). When
`GetTypeByMetadataName` returns `null` the package is not referenced and the helper returns `false`
(a `QuerySymbols` member is `null`, its group's rules do not register). Never assume a symbol is non-null.

## Adding a Diagnostic

1. Next ID (`MKP010`, …); category `Usage` (behaviour), `Design` (structure) or `Performance` (query cost).
2. Severity: `Error` for a definite violation, `Warning` for a heuristic, `Info` for a suggestion the code may rightly
   decline.
3. Add the `DiagnosticDescriptor`, update `SupportedDiagnostics`, update this table.
4. Tests: the violation, valid code and an edge case.
5. A code fix ships only where it provably preserves semantics; record why, or why not, in an ADR.

## Packaging

`netstandard2.0`, `IncludeBuildOutput=false`; the DLL is packed to `analyzers/dotnet/cs/` — no
`lib/` folder. Consumers reference it with `PrivateAssets=all`. Inside the repository a project
references it with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`
(`samples/MicroKit.Persistence.Samples`, whose Release build fails on any MKP diagnostic).

## Testing

- `CSharpAnalyzerTest<TAnalyzer, CompatXUnitVerifier>` with inline markers `{|MKPxxx:code|}`.
- `PersistenceStubs.All` provides the MicroKit and `DbContext` stubs for MKP001–MKP005; their namespaces and
  names must match the metadata names the helper resolves.
- MKP006–MKP009 run through `QueryTest<TAnalyzer>.RunAsync(code, StubSet)`: standalone stub documents
  (`QueryStubs`: MicroKit, EF Core, relational, a shop model) compiled beside the code against
  `ReferenceAssemblies.Net.Net90`. Leaving a stub set out tests the null guard.
- Auxiliary assertions may use Shouldly or a bare `Xunit.Assert` (root
  `.claude/rules/testing-libraries.md`); never FluentAssertions.
