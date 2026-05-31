# Standard: CQRS Taxonomy

**The decision tree for choosing between Command, Query, StreamQuery, and Event.** Use this when
modeling any new operation. The rules live in `.claude/rules/cqrs-patterns.md`; this is the
decision aid.

---

## The Root Question

> Does the operation **change** state or **observe** it?

```
                     ┌─────────────────────────────┐
                     │ Does it change state?        │
                     └──────────────┬──────────────┘
                       YES          │          NO
              ┌────────────────────┘ └────────────────────┐
              ▼                                            ▼
   ┌──────────────────────┐                    ┌──────────────────────┐
   │ Is it a reaction to a │                    │ Is the result set      │
   │ fact that happened?   │                    │ large/unbounded?       │
   └──────────┬───────────┘                    └──────────┬────────────┘
       YES    │    NO                              YES     │     NO
      ┌───────┘    └───────┐                      ┌────────┘     └────────┐
      ▼                    ▼                       ▼                       ▼
  IEvent +            ICommand /              IStreamQuery<T>          IQuery<T> /
  Notification +      ICommand<TResult>                               IQuery<Result<T>>
  Handler
```

> If the answer to "change or observe?" is **both**, you have two operations. Split them.

---

## Contract Selection Matrix

| Intent | Contract | Return type | Result<T>? |
|--------|----------|-------------|-----------|
| Delete, with nothing useful to return | `ICommand` | `ValueTask` | — |
| Create, return new id | `ICommand<Result<TId>>` | `ValueTask<Result<TId>>` | when creation can be rejected |
| Update, can fail in a modeled way | `ICommand<Result<Unit>>` | `ValueTask<Result<Unit>>` | yes |
| Read one, may be absent | `IQuery<Result<TDto>>` | `ValueTask<Result<TDto>>` | yes (`NotFoundError`) |
| Read static/config, never fails | `IQuery<TDto>` | `ValueTask<TDto>` | no |
| Read large/unbounded set | `IStreamQuery<TDto>` | `IAsyncEnumerable<TDto>` | no (stream items) |
| React to a committed fact | `IEvent` + `IDomainEventNotification` | (handlers react) | — |

---

## When `Result<T>` Earns Its Place

Use `Result<T>` when failure is an **expected, modeled outcome**: validation rejection, not-found,
conflict, business-rule violation. Skip it when the operation either succeeds or fails
exceptionally (infrastructure error worth throwing). Don't wrap a config read that cannot fail.

---

## Smells That Signal a Wrong Choice

| Smell | Correct shape |
|-------|---------------|
| A "command" returns the full updated entity | Command (no entity) + separate Query |
| A query handler writes (audit, last-seen) | Move the write into a domain event handler |
| One handler implements both `ICommandHandler` and `IQueryHandler` | Two handlers |
| A command returns a list of business data | That's a query |
| `Result<T>` on an operation that never fails | Drop `Result<T>` |
| Event published before persistence | Publish after the write commits |

---

## Where Events Fit

A domain event is a **fact in the past tense** (`OrderShippedEvent`). It is raised by the command
handler after the state change is persisted, wrapped in a `DomainEventNotification`, and handled by
one or more dedicated handlers (one responsibility each). Events are how bounded contexts react to
each other without direct coupling — never via a shared `IMediator` call inside a handler.
