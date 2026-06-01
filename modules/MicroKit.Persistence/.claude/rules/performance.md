# Rule: Performance — MicroKit.Persistence

Every query in a consuming application flows through these repositories. Overhead multiplies
by database request rate — it must be invisible.

## Mandatory Rules

### AsNoTracking on All Read Paths
```csharp
// ✅ Every IReadRepository implementation starts with AsNoTracking
var query = _context.Set<User>().AsNoTracking();

// ❌ Missing AsNoTracking in a read repository — change-tracker overhead + memory leak risk
var query = _context.Set<User>(); // Analyzer PRDANA003 flags this
```

### ValueTask over Task
Repository methods return `ValueTask<T>` — a synchronous cache-hit or in-memory path allocates
no state-machine box.

```csharp
// ✅
public async ValueTask<User?> FindAsync(UserId id, CancellationToken ct = default)

// ❌
public async Task<User?> FindAsync(UserId id, CancellationToken ct = default)
```

### Single Commit per Command
One `CommitAsync()` at the end of a command handler. Multiple commits within a single command
handler indicate a design flaw — the work should be atomic.

```csharp
// ✅
user.UpdateEmail(cmd.NewEmail);
user.AddRole(role);
await _uow.CommitAsync(ct);  // single SaveChanges call under the hood

// ❌ Multiple commits
await _uow.CommitAsync(ct); // mid-handler
// ... more work ...
await _uow.CommitAsync(ct); // second commit — not atomic
```

### AnyAsync over CountAsync for Existence Checks
```csharp
// ✅
var exists = await _context.Users.AnyAsync(u => u.Email == email, ct);

// ❌ Materializes count for a boolean result
var exists = await _context.Users.CountAsync(u => u.Email == email, ct) > 0;
```

### Server-Side Projection
```csharp
// ✅ Project on the database — never load full entities for DTO reads
var dtos = await _context.Users
    .AsNoTracking()
    .Where(spec.Criteria)
    .Select(UserDto.Projection) // Expression<Func<User, UserDto>>
    .ToListAsync(ct);

// ❌ Load entities then map in memory
var users = await _context.Users.Where(...).ToListAsync(ct);
var dtos = users.Select(u => new UserDto(u)); // wasteful — loads columns not needed
```

### Pagination at DB Level
```csharp
// ✅ Skip/Take before ToListAsync
query = query.Skip((page - 1) * pageSize).Take(pageSize);

// ❌ In-memory pagination — loads entire table
var all = await _context.Users.ToListAsync(ct);
var page = all.Skip(offset).Take(pageSize);
```

### Compiled Queries for Hot Paths
```csharp
// ✅ Compile once, reuse across requests
private static readonly Func<AppDbContext, Guid, CancellationToken, Task<User?>> _findById =
    EF.CompileAsyncQuery((AppDbContext ctx, Guid id, CancellationToken ct) =>
        ctx.Users.AsNoTracking().FirstOrDefault(u => u.Id == id));
```

### No Sync-over-Async
```csharp
// ❌ Thread-pool starvation
var user = _context.Users.FindAsync(id).Result;

// ✅
var user = await _context.Users.FindAsync([id], ct).ConfigureAwait(false);
```

### ConfigureAwait(false)
Every `await` in library code uses `.ConfigureAwait(false)`.

## Performance Budget
See `.claude-context/standards/performance-budget.md` for concrete targets.

A PR that regresses FindAsync or ListAsync overhead by > 10% requires `performance-reviewer` approval.

## Verification
```bash
dotnet run --project benchmarks/MicroKit.Persistence.Benchmarks/ -c Release --filter *
```
