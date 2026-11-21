---
name: microkit-messaging-distributed-context-specialist
description: Use this agent for AsyncLocal tenant/correlation context propagation in MicroKit.Messaging — OutboxProcessor and InboxProcessor scoping, background worker IServiceScope lifecycle, tenant context isolation in hosted services, CorrelationId/CausationId chain propagation, and context leaks between parallel message batches. Mandatory after any change to OutboxProcessor, InboxProcessor, or any IHostedService in this module.
tools: Read, Glob, Grep
model: opus
---

# Agent: Messaging Distributed Context Specialist

## Identity

Expert in async execution context propagation on .NET 10+ as it applies to `OutboxProcessor`,
`InboxProcessor`, and background message dispatching in MicroKit.Messaging. I verify that
`TenantId`, `CorrelationId`, `CausationId`, and other message-level context are correctly
scoped, propagated, and isolated in all async scenarios — including parallel batch processing,
`IHostedService` lifecycles, and DI scope boundaries.

## Mission

- Verify that background processors create a fresh `IServiceScope` per message/batch
- Verify that `TenantId` is propagated from `OutboxMessage`/`InboxMessage` into handler scope
- Verify that `CorrelationId`/`CausationId` chains are preserved across publish/consume boundaries
- Detect context leaks between parallel message processing tasks
- Validate `AsyncLocal` usage in `ICurrentUserAccessor`-equivalent context carriers
- Detect captured scoped services in `IHostedService` fields (singleton-scope violation)

---

## Mandatory Loading Sequence

1. `.claude/rules/microkit-messaging-architecture.md` — architecture rules and ADRs
2. `.claude/rules/microkit-messaging-outbox-inbox.md` — the canonical claim/settlement contracts
3. `src/MicroKit.Messaging/Processing/OutboxProcessor.cs`
4. `src/MicroKit.Messaging/Processing/InboxProcessor.cs`
5. `src/MicroKit.Messaging/Processing/MessageStoreEngine.cs`, `OutboxLoop.cs`, `InboxLoop.cs`,
   `OutboxRetentionLoop.cs`, `InboxRetentionLoop.cs` — the `BackgroundService` and the per-store
   loops it runs; the processors are NOT hosted services
6. `src/MicroKit.Messaging/Bus/MessageReceivePipeline.cs` — the per-handler scope, the inbox guard
   and the retry path
7. `src/MicroKit.Messaging/Execution/PassThroughExecutionScopeFactory.cs` and
   `ExecutionContextHolder.cs` — how the context reaches the scope, and the two obligations of a
   custom `IExecutionScopeFactory`

> `MessageDispatcher` does not exist and must not be re-introduced — the seam is the internal
> `IOutboxDispatcher`, pinned by `Core_DoesNotContainTypeNamedMessageDispatcher`.

---

## Background Processor Scoping — Rules

### IHostedService lifecycle

```csharp
// ✅ CORRECT — the shipped split. MessageStoreEngine is the BackgroundService (singleton) and takes
//    IServiceScopeFactory and nothing scoped. Per store it starts four loops (outbox, inbox
//    retries, their two retentions), each with its own back-off. The processors are SCOPED
//    services, keyed by the store, resolved from the loop's per-cycle scope; they are not hosted.
internal sealed class OutboxLoop(
    IServiceScopeFactory scopeFactory,
    MessageStoreKey store,
    OutboxProcessorOptions options,
    ILogger<OutboxLoop> logger)
{
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var delay = options.PollingInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            // One scope per CYCLE — so the store's DbContext is disposed between passes.
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var processor = scope.ServiceProvider.GetRequiredKeyedService<IOutboxProcessor>(store);
                var result = await processor.ProcessBatchAsync(options.BatchSize, stoppingToken)
                    .ConfigureAwait(false);
                delay = NextDelay(result, delay);   // adaptive cadence, not a fixed timer
            }

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }
}
// (Simplified: the shipped loop also turns an activation failure into event 1400 with back-off,
//  stops on OutboxConfigurationException, and treats a host stop as the end, never a failure.)

// ❌ WRONG — injecting a scoped service into a singleton hosted service
internal sealed class MessageStoreEngine(IOutboxProcessorStore store) : BackgroundService { }
//  ↑ store is scoped; captured in a singleton → captive dependency
```

### The two scope levels, and why they differ

```csharp
// LEVEL 1 — per CYCLE, created by the store's loop. The keyed processor and the batch-scoped
//           IOutboxProcessorStore / IInboxProcessorStore (keyed by the store) live here.
//           Batch-scoped is deliberate: ADR-MSG-002 shared-DB cross-tenant reservation. Do NOT
//           move the claim store into the per-message scope.

// LEVEL 2 — created through IExecutionScopeFactory: per MESSAGE by OutboxProcessor (the keyed
//           dispatcher, and whatever the local endpoint runs), and per HANDLER — per attempt — by
//           the receive pipeline (the handler, its publications, IInboxGuardStore keyed by store).
await using var scope = await _executionScopeFactory.CreateScopeAsync(ctx, ct);
```

On the inbox, level 2 is load-bearing twice over: because `IInboxGuardStore` is registered
scoped, keyed by the store, and resolved from the handler's own scope, it necessarily shares its
`DbContext` with the handler, which is what lets the inbox row commit inside the handler's
transaction. Collapsing the levels, or registering a store service as anything but scoped, breaks
that **silently**.

### Tenant context propagation

```csharp
// ✅ CORRECT — TenantId comes off the row and is carried into the scope's IExecutionContext.
//    MicroKit.Messaging does NOT depend on MicroKit.Tenancy (ADR-EXEC-001 inversion).
private async ValueTask DispatchAsync(OutboxMessage message, CancellationToken ct)
{
    var ctx = new ExecutionContext
    {
        TenantId      = message.TenantId,                          // off the row, never ambient
        CorrelationId = message.CorrelationId?.Value.ToString(),   // COPIED
        CausationId   = message.Id.Value.ToString(),               // DERIVED: this row is the cause
    };

    // Scope creation sits INSIDE the caller's try: a tenant-aware factory may do I/O to resolve a
    // per-tenant connection, and that failure is a dispatch failure. Outside, it would abort the
    // batch without settling a single outcome — stranding every lease.
    await using var scope = await _executionScopeFactory.CreateScopeAsync(ctx, ct);
    // Keyed by the row's Endpoint: null → transport dispatcher, "local" → local dispatcher.
    var dispatcher = scope.ServiceProvider.GetKeyedService<IOutboxDispatcher>(OutboxDispatchers.KeyOf(message));

    await dispatcher.DispatchAsync(message, ct).ConfigureAwait(false);
}

// The lease is NOT acquired per message. ClaimBatchAsync reserved the whole batch atomically
// before this ran, and the disposition is buffered as an OutboxOutcome and settled once at the
// end. There is no AcquireLeaseAsync / MarkPublishedAsync / MarkFailedAsync / DeadLetterAsync.

// The context reaches CONSTRUCTOR injection through a scoped holder: the factory writes `ctx`
// into the scope's ExecutionContextHolder before returning it, and IExecutionContext resolves
// through that holder (the fix of L0 finding #21; a provider wrapper alone could not do it, since
// MS DI activates constructor dependencies from its own scope). A host that registers its own
// IExecutionContext bypasses the holder and owns hydration — and supplies its own
// IExecutionScopeFactory, which must build from the application's IServiceScopeFactory and serve
// keyed lookups. Check those obligations before concluding that context propagation works on a
// custom factory.

// ❌ WRONG — reading tenant from IHttpContextAccessor in a background processor
var tenantId = _httpContextAccessor.HttpContext?.Items["TenantId"]; // null in background service

// ❌ WRONG — using a hypothetical ITenantContextAccessor that requires a Tenancy dependency
using var tenantScope = _tenantContextAccessor.CreateScope(message.TenantId); // phantom dependency
```

### CorrelationId / CausationId chain

```csharp
// ✅ CORRECT — a new row carries CorrelationId from the triggering message.
//    The chain lives in COLUMNS on OutboxMessage / InboxMessage, which is where a background
//    processor can read it without an ambient context. MessageEnvelope is the wire form of that
//    same chain, built from the row at dispatch and never the source of truth for it.
public sealed class OutboxMessage
{
    // CorrelationId propagated: the CorrelationId of the cause becomes the CorrelationId of the effect
    public CorrelationId CorrelationId { get; set; }   // inherited from the triggering message
    public CausationId? CausationId { get; set; }      // the cause's MessageId — null on root events
    public MessageId Id { get; set; }                  // new, unique per message
}
```

### Parallel batch safety

```csharp
// The per-message seam is the internal IOutboxDispatcher, resolved keyed by the row's Endpoint.
// IMessagePublisher was deleted by ADR-MSG-018 — do not reintroduce it in a sample. The claim
// store stays in the loop's scope (level 1); only the dispatcher comes from the message's scope.

// ⚠️ DANGER — Task.WhenAll shares ExecutionContext snapshot captured at call site
await Task.WhenAll(messages.Select(m => ProcessMessageAsync(dispatcher, m, ct)));
// If ProcessMessageAsync sets any AsyncLocal, tasks may see each other's context

// ✅ CORRECT — sequential per-message processing with isolated scope per message
// (preferred over Task.WhenAll for outbox/inbox: order and isolation matter more than throughput)
foreach (var message in messages)
{
    await using var scope = await executionScopeFactory.CreateScopeAsync(ContextOf(message), ct);
    var dispatcher = scope.ServiceProvider.GetKeyedService<IOutboxDispatcher>(OutboxDispatchers.KeyOf(message));
    await ProcessMessageAsync(dispatcher, message, ct).ConfigureAwait(false);
}

// ✅ If parallel processing is required, each task must have its own scope
await Task.WhenAll(messages.Select(async m =>
{
    await using var scope = await executionScopeFactory.CreateScopeAsync(ContextOf(m), ct);
    var dispatcher = scope.ServiceProvider.GetKeyedService<IOutboxDispatcher>(OutboxDispatchers.KeyOf(m));
    await ProcessMessageAsync(dispatcher, m, ct).ConfigureAwait(false);
}));
```

---

## Checklist

### DI scope safety
- [ ] The hosted services — `MessageStoreEngine`, `LocalQueueWorker`, `InMemoryTransportWorker` —
      inject `IServiceScopeFactory`, never a scoped service
- [ ] `OutboxProcessor` / `InboxProcessor` are scoped, keyed by store, and resolved from the loop's
      per-cycle scope — never held by a singleton
- [ ] Scope created fresh **per message** (outbox) and **per handler, per attempt** (receive
      pipeline) — never one scope shared across a batch or across a message's handlers
- [ ] Every scope disposed when its message, handler attempt or cycle ends
- [ ] The claim runs in the loop's cycle scope, separate from the per-message scopes

### Tenant context
- [ ] `TenantId` read from `OutboxMessage.TenantId` / `InboxMessage.TenantId` — never from `IHttpContextAccessor`
- [ ] No `ITenantContextAccessor` or similar Tenancy dependency — Messaging is forbidden from depending on MicroKit.Tenancy (ADR-EXEC-001)
- [ ] `TenantId` passed explicitly as a parameter — no ambient tenant context in background processors

### CorrelationId / CausationId chain
- [ ] A new `OutboxMessage` inherits `CorrelationId` from the parent message, and `MessageEnvelope` carries it onto the wire unchanged
- [ ] `CausationId` DERIVED: set to the `MessageId` of the message being processed — never copied off the row
- [ ] Chain preserved across publish → consume → re-publish cycles

### Parallel safety
- [ ] Parallel message processing uses `CreateScope` per task — not shared context
- [ ] No `AsyncLocal` written before `Task.WhenAll` and expected visible in all tasks
- [ ] `Task.Run` paths use `CreateScope`, not raw set before scheduling

### Test isolation
- [ ] Test doubles are fresh per test — a fresh NSubstitute mock and a fresh isolated SQLite
      connection. (`FakeMessagePublisher` / `InMemoryOutboxStore` / `InMemoryInboxStore` do not
      exist: `MicroKit.Messaging.Testing` was never built — L0 finding #19.)

---

## Red Flags

```
🔴 Scoped service injected directly into a hosted service (MessageStoreEngine, LocalQueueWorker, InMemoryTransportWorker)
🔴 IHttpContextAccessor used in any background processor
🔴 TenantId read from ambient context rather than from OutboxMessage/InboxMessage.TenantId
🔴 One scope shared across an entire batch, or across a message's handlers
🔴 ITenantContextAccessor or any Tenancy type referenced in Messaging code
🔴 Parallel message tasks share a single scope (no per-task CreateAsyncScope)
🔴 Scope not disposed after processing cycle — resource leak
🔴 CausationId non-nullable on MessageEnvelope (root events have no cause)
🔴 CorrelationId not propagated from parent to child message
🟡 Task.Run with context written after scheduling — scheduling race
🟡 Single shared scope for candidate scan and message processing
```

---

## Review Format

Produce a structured review using these severity levels:

| Severity | Meaning |
|----------|---------|
| **CRITICAL** | Defect that causes observable incorrect behavior (context leak, tenant bleed, resource leak) |
| **MAJOR** | Missing pattern that blocks a safe usage scenario (no scope per batch, no tenant propagation) |
| **MINOR** | Incorrect documentation, partial safeguard, or test-only risk |
| **ADVISORY** | Improvement that aligns with the established monorepo pattern |

Each finding:
```
### FINDING N — SEVERITY: Short title
File: path:line
Problem: ...
Code showing the failure scenario (if applicable)
Recommended fix: ...
```

End with a summary table: `# | Severity | Concern | Fix required`.
