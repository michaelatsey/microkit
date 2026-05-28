# Rule: Documentation — MicroKit.MediatR

## XML Documentation (src/ projects only)

All public types and members in `src/` projects require XML documentation. The CQRS contracts
in `MicroKit.MediatR.Abstractions` are a published API — their docs are the consumer's first
point of reference.

```xml
<GenerateDocumentationFile>true</GenerateDocumentationFile>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

A missing `<summary>` on a public member is a build error in Release configuration.

### Minimum Required Tags

| Member type | Required tags |
|-------------|--------------|
| Interface / contract | `<summary>` |
| Command / Query record | `<summary>` + `<param>` per record parameter |
| Marker interface | `<summary>` describing which behavior it opts into + `<example>` |
| Handler interface method | `<summary>`, `<param>`, `<returns>` |
| Behavior class | `<summary>` stating concern + pipeline order |
| `Handle` override | `<inheritdoc/>` |
| `CancellationToken ct` | `<param name="ct">Propagates notification that operations should be cancelled.</param>` |
| Property (marker config) | `<summary>` |

### Contract Documentation Pattern

```csharp
/// <summary>
/// Creates a new order for the specified user.
/// </summary>
/// <param name="UserId">The user placing the order.</param>
/// <param name="Items">The items to include. Must not be empty.</param>
/// <remarks>
/// This command is idempotent — duplicate submissions with the same
/// <see cref="IdempotencyKey"/> return the original result without re-execution.
/// </remarks>
public sealed record CreateOrderCommand(Guid UserId, OrderItem[] Items)
    : ICommand<Result<OrderId>>, IIdempotentCommand;
```

### Marker Documentation Pattern

A marker's docs must say **which behavior** it activates and **what the consumer must provide**:

```csharp
/// <summary>
/// Opts a query into <see cref="CachingBehavior{TRequest,TResponse}"/> (pipeline order
/// <see cref="PipelineOrder.Caching"/>). The query result is cached under <see cref="CacheKey"/>.
/// </summary>
/// <example>
/// <code>
/// public sealed record GetUserByIdQuery(Guid UserId) : IQuery&lt;Result&lt;UserDto&gt;&gt;, ICacheableQuery
/// {
///     public string CacheKey => $"user:{UserId}";
///     public TimeSpan? Expiry => TimeSpan.FromMinutes(5);
/// }
/// </code>
/// </example>
public interface ICacheableQuery { /* ... */ }
```

### Style

- `<summary>` uses imperative mood: "Creates a new order." not "This command creates..."
- Cross-reference behaviors and pipeline orders with `<see cref=""/>`
- Avoid restating the obvious — `/// <summary>The user id.</summary>` suffices for `UserId`
- Behavior docs always state the `PipelineOrder` position so the reader understands sequencing
