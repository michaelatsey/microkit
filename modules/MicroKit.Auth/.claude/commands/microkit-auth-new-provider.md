---
description: Scaffold a new Federation provider adapter in MicroKit.Auth. Produces a plan for the provider package structure, claims mapper, DI registration, and tests.
---

Use the microkit-auth-implementer agent.

Load in order:
1. `.claude/CLAUDE.md`
2. `.claude/rules/microkit-auth-architecture.md`
3. `.claude/rules/microkit-auth-jwt.md`
4. `.claude/rules/microkit-auth-dependencies.md`
5. `.claude-context/templates/microkit-auth-provider-template/`

Produce an implementation plan for a new Federation provider: $ARGUMENTS

The plan must cover:
- Package name: `MicroKit.Auth.{ProviderName}`
- Claims mapper: `{ProviderName}ClaimsMapper : IClaimsMapper`
- JWT/OIDC validator: `{ProviderName}JwtValidator : IJwtValidator`
- Options: `{ProviderName}AuthOptions`
- DI extension: `Add{ProviderName}()` on `MicroKitAuthBuilder`
- Unit tests: claims mapping happy path + failure paths
- Integration test: full auth flow with test JWT

Wait for explicit approval before writing any code.
Do not commit anything.
