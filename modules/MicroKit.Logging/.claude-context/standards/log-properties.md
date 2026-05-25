# Standard: Log Properties

**This is the canonical registry of log property names for MicroKit.Logging.**

All properties used in `ILogger` scopes, enrichers, `Activity.SetTag()`, and `DiagnosticSource` payloads must use these exact names. No exceptions.

Adding a new property requires:
1. Adding it to this file (with rationale)
2. Adding the constant to `LogPropertyNames` in `MicroKit.Logging.Abstractions`
3. Updating `.claude/CLAUDE.md` property table
4. Review by `api-reviewer` agent (Abstractions change)

---

## Core Correlation Properties

| Constant | String Value | Type | Description | Source |
|----------|-------------|------|-------------|--------|
| `LogPropertyNames.CorrelationId` | `"CorrelationId"` | `string` | Cross-service correlation identifier. Propagated via HTTP header `X-Correlation-ID` and Activity baggage. | Inbound request / generated at entry point |
| `LogPropertyNames.TraceId` | `"TraceId"` | `string` | W3C TraceContext trace ID (32 hex chars). Extracted from `Activity.Current.TraceId`. | `Activity.Current` |
| `LogPropertyNames.SpanId` | `"SpanId"` | `string` | W3C TraceContext span ID (16 hex chars). Extracted from `Activity.Current.SpanId`. | `Activity.Current` |
| `LogPropertyNames.RequestId` | `"RequestId"` | `string` | Unique identifier for an individual HTTP request or message. Distinct from `CorrelationId` — does not propagate to downstream services. | HTTP `X-Request-ID` header / generated |
| `LogPropertyNames.OperationId` | `"OperationId"` | `string` | Identifier for a logical business operation scope (e.g., a CQRS command execution). Scoped to a single service boundary. | MicroKit operation scope |

## Identity Properties

| Constant | String Value | Type | Description | Source |
|----------|-------------|------|-------------|--------|
| `LogPropertyNames.TenantId` | `"TenantId"` | `string` | Multi-tenant identifier. Set by `MicroKit.MultiTenancy`. | MicroKit.MultiTenancy |
| `LogPropertyNames.UserId` | `"UserId"` | `string` | Authenticated user identifier. Set by `MicroKit.Auth`. | MicroKit.Auth |

## Messaging / CQRS Properties

| Constant | String Value | Type | Description | Source |
|----------|-------------|------|-------------|--------|
| `LogPropertyNames.CommandName` | `"CommandName"` | `string` | Name of the CQRS command being executed. Set by `MicroKit.MediatR`. | MicroKit.MediatR pipeline |
| `LogPropertyNames.MessageId` | `"MessageId"` | `string` | Unique identifier for a message in `MicroKit.Messaging` (outbox, broker). | MicroKit.Messaging |

---

## Naming Rules

1. **PascalCase** — always. Never `tenant_id`, `tenantId`, `TENANT_ID`.
2. **No prefix** — never `mk_`, `microkit_`, `log_`.
3. **Noun or NounNoun** — `TenantId`, `CorrelationId`, `CommandName`. Never verbs.
4. **`Id` suffix** for identifiers, **`Name` suffix** for display names.

## Forbidden Property Names

These names are **explicitly banned** — use the canonical equivalent:

| Banned | Use instead |
|--------|-------------|
| `tenant_id` | `TenantId` |
| `tenantId` | `TenantId` |
| `correlation` | `CorrelationId` |
| `correlationId` | `CorrelationId` |
| `trace_id` | `TraceId` |
| `userId` | `UserId` |
| `user_id` | `UserId` |
| `requestId` | `RequestId` |
| `request_id` | `RequestId` |

The `MKL002x` analyzer family enforces this at compile time.

## Sensitive Properties — Never Log

These property names must **never** appear in log scope keys or structured log templates. The `MKL003x` analyzer blocks them:

- `Password`, `password`, `pwd`
- `Token`, `token`, `AccessToken`, `RefreshToken`
- `Secret`, `secret`, `ApiKey`, `api_key`
- `CreditCard`, `CardNumber`, `Cvv`
- `Ssn`, `SocialSecurityNumber`
- `PrivateKey`, `private_key`
