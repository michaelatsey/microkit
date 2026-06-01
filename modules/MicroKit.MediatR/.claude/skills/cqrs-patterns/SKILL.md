---
name: cqrs-patterns
description: How to apply CQRS correctly in MicroKit.MediatR — deciding between Command, Query, StreamQuery, and Event; whether to wrap results in Result<T>; where to publish domain events; and how to keep handlers isolated. Use whenever modeling a new operation, splitting a read/write concern, or reviewing a handler design. This is a behavior guide, not a contract reference (see .claude/rules/cqrs-patterns.md for the rules).
---

# Skill: Applying CQRS

How to *reason about* CQRS modeling in MicroKit.MediatR. For the hard rules, see
`.claude/rules/cqrs-patterns.md`. For the canonical type signatures, see
`.claude-context/standards/handler-contracts.md`. For the decision taxonomy, see
`.claude-context/standards/cqrs-taxonomy.md`.

## The One Question That Settles It

> **Does this operation change state, or observe it?**

- Changes state → **Command**
- Observes state → **Query**
- If the answer is "both" → you have two operations. Split them. The urge to combine them is the
  smell CQRS exists to catch.

## Choosing the Contract

| You are modeling… | Use | Returns |
|-------------------|-----|---------|
| A state change with no useful return | `ICommand` | `ValueTask` |
| A state change that yields the new resource id | `ICommand<Result<TId>>` | `ValueTask<Result<TId>>` |
| A read that can legitimately fail (not found) | `IQuery<Result<TDto>>` | `ValueTask<Result<TDto>>` |
| A read that never fails (static config) | `IQuery<TDto>` | `ValueTask<TDto>` |
| A read over a large/unbounded set | `IStreamQuery<TDto>` | `IAsyncEnumerable<TDto>` |
| A fact that already happened | `IEvent` + notification | (handlers react) |

## When to Reach for Result<T>

`Result<T>` earns its place when failure is an **expected, modeled outcome** (validation, not-found,
conflict). It is noise when the operation either succeeds or throws a genuinely exceptional error.
Don't wrap a config read that can't fail; do wrap a "create order" that can be rejected.

## Domain Events: Where and When

Publish a domain event from the **command handler**, **after** persistence — the fact is only real
once it is committed. Never from a behavior, never from a query handler, never before the write.

```csharp
var order = Order.Create(cmd.UserId, cmd.Items);
var id = await _repo.SaveAsync(order, ct).ConfigureAwait(false);
await _events.PublishAsync(new OrderCreatedEvent(id, cmd.UserId), ct).ConfigureAwait(false);
return Result.Success(id);
```

## Keep Handlers Isolated

A handler knows its direct dependencies and nothing about the pipeline. No `IMediator`, no
`HttpContext`, no `DbContext` in a query handler, no calling another handler. If you cannot write
`new MyHandler(mockA, mockB)` and test it, the design is wrong — see
`.claude/rules/no-handler-coupling.md`.

## Common Mistakes

- A "command" that returns the full updated entity → that's a command **and** a query; split it.
- A query handler that writes (audit log, last-accessed timestamp) → move the write to an event.
- One handler implementing both `ICommandHandler` and `IQueryHandler` → never.
- Forcing `Result<T>` on operations that can't fail → unnecessary ceremony.

## Scaffolding

Use `/new-handler` to generate a contract + handler + test skeleton, and `/new-domain-event` for the
event/notification/handler triptych. Bring ambiguous designs to the `architect` agent.
