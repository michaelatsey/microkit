# microkit-auth-testing

## Libraries

| Library | Role | Status |
|---------|------|--------|
| `xUnit` | Test runner | ✅ Required |
| `Shouldly` | Assertions | ✅ Required |
| `NSubstitute` | Mocking | ✅ Required |
| `NetArchTest` | Architecture tests | ✅ Required |
| `FluentAssertions` | — | ❌ Banned (Xceed commercial licence) |

---

## MicroKit.Auth.Testing Usage

Always use `FakeCurrentUserBuilder` in tests — never instantiate `CurrentUser` directly.

```csharp
// ✅ Correct
var user = FakeCurrentUserBuilder
    .Create()
    .WithUserId(Guid.NewGuid())
    .WithTenantId(TenantId.New())
    .WithEmail("user@example.com")
    .WithRole(SystemRoles.Auditor)
    .WithPermission(AuditPermissions.Create)
    .Build();

// ✅ FakePermissionChecker — configurable responses
var checker = new FakePermissionChecker()
    .Allow(AuditPermissions.Create)
    .Deny(AuditPermissions.Delete);
```

---

## Test Categories

### Unit Tests (`MicroKit.Auth.UnitTests`)
- `IPermissionChecker` evaluation logic
- `IJwtValidator` token parsing (valid, expired, malformed, missing claims)
- `IClaimsMapper` claim extraction (happy path + edge cases)
- `Permission` value object equality, wildcards
- `Role` mapping and inheritance
- `ICurrentUserAccessor` AsyncLocal isolation

### Integration Tests (`MicroKit.Auth.IntegrationTests`)
- Full ASP.NET Core pipeline with `WebApplicationFactory`
- JWT Bearer authentication flow with test JWT
- `[RequirePermission]` attribute enforcement end-to-end
- Supabase claims mapping integration

### Architecture Tests (`MicroKit.Auth.ArchitectureTests`)
- Abstractions has zero ASP.NET Core / EF Core reference
- Core has zero ASP.NET Core reference
- Federation providers do not reference each other
- Testing package does not reference Core
- No circular dependencies

---

## Mandatory Test Cases Per Component

### JWT Validation
```
ValidateAsync_WhenTokenValid_ReturnsPrincipal
ValidateAsync_WhenTokenExpired_ReturnsFailure
ValidateAsync_WhenTokenMalformed_ReturnsFailure
ValidateAsync_WhenSignatureInvalid_ReturnsFailure
ValidateAsync_WhenAudienceMismatch_ReturnsFailure
ValidateAsync_WhenIssuerMismatch_ReturnsFailure
ValidateAsync_WhenSubClaimMissing_ReturnsFailure
```

### Permission Checker
```
HasPermissionAsync_WhenUserHasDirectPermission_ReturnsTrue
HasPermissionAsync_WhenRoleGrantsPermission_ReturnsTrue
HasPermissionAsync_WhenWildcardMatches_ReturnsTrue
HasPermissionAsync_WhenPermissionNotGranted_ReturnsFalse
HasPermissionAsync_WhenUserNotAuthenticated_ReturnsFailure
```

### Claims Mapper
```
MapFromClaims_WhenAllClaimsPresent_ReturnsCurrentUser
MapFromClaims_WhenSubMissing_ReturnsFailure
MapFromClaims_WhenEmailMissing_ReturnsCurrentUserWithNullEmail
MapFromClaims_WhenTenantIdPresent_MapsCorrectly
```

---

## Naming Convention

```
{Method}_{Scenario}_{ExpectedResult}

✅ HasPermissionAsync_WhenUserHasPermission_ReturnsTrue
✅ ValidateAsync_WhenTokenExpired_ReturnsFailure
❌ TestPermissionCheck
❌ ShouldReturnTrueWhenPermissionExists
```

---

## Architecture Test Pattern

```csharp
[Fact]
public void Abstractions_ShouldHave_ZeroAspNetCoreDependency()
{
    Types.InAssembly(typeof(ICurrentUser).Assembly)
        .ShouldNot()
        .HaveDependencyOn("Microsoft.AspNetCore")
        .GetResult()
        .IsSuccessful
        .ShouldBeTrue();
}
```

---

## Rules

1. **No untested public code** — every public method in `src/` has at least one test
2. **Bugs get regression tests** — every bug fix includes a test reproducing the failure
3. **No `Thread.Sleep` in tests** — use `Task.Delay` with `CancellationToken` if timing matters
4. **No shared state between tests** — `FakeCurrentUser` is always created fresh per test
5. **`ConfigureAwait(false)` in test helpers** — not in test methods themselves
6. **`GenerateDocumentationFile=false`** in all test `.csproj` files
7. **`NoWarn CS1591;CA1707`** in all test `.csproj` files
