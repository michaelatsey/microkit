# Skill: Testing — MicroKit.Multitenancy

## Run all tests
```bash
dotnet test modules/MicroKit.Multitenancy/MicroKit.Multitenancy.slnx -c Release
```

## Run specific suite
```bash
# Unit tests only
dotnet test modules/MicroKit.Multitenancy/tests/MicroKit.Multitenancy.UnitTests -c Release

# Integration tests only
dotnet test modules/MicroKit.Multitenancy/tests/MicroKit.Multitenancy.IntegrationTests -c Release

# Architecture tests only
dotnet test modules/MicroKit.Multitenancy/tests/MicroKit.Multitenancy.ArchitectureTests -c Release
```

## Filter by name
```bash
dotnet test --filter "DisplayName~AsyncLocal" -c Release
dotnet test --filter "DisplayName~TenantIsolation" -c Release
```

## With coverage
```bash
dotnet test modules/MicroKit.Multitenancy/MicroKit.Multitenancy.slnx \
  --collect:"XPlat Code Coverage" \
  --results-directory coverage/
```

## Verify no FluentAssertions
```bash
grep -rn 'FluentAssertions\|\.Should()\.' modules/MicroKit.Multitenancy/tests/ --include="*.cs"
# Expected: no output
```
