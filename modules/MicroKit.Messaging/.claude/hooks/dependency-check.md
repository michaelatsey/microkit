# Hook: dependency-check — MicroKit.Messaging

Runs after any `.csproj` change under `modules/MicroKit.Messaging/`. Invoke the
`microkit-messaging-dependency-guardian` agent or run the checks below manually.

## Checks

### 1. MediatR / MediatR.Contracts confined to the MediatR glue

```bash
grep -rn "Include=\"MediatR\|MicroKit\.MediatR" \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.EntityFrameworkCore/ \
  --include="*.csproj"
```

**BLOCK** if any match. Only `MicroKit.Messaging.MediatR` (through `MicroKit.MediatR`) and its test
projects may reach `MediatR` / `MediatR.Contracts` (ADR-MSG-009). The assemblies are pinned by
`AllAssemblies_HaveNoMediatRContractsDependency`, `Core_HasNoMediatRDependency` and
`EfCore_HasNoMediatRDependency`.

### 2. MicroKit.Persistence.EntityFrameworkCore confined

```bash
grep -rn "MicroKit\.Persistence\.EntityFrameworkCore" \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.MediatR/ \
  --include="*.csproj"
```

**BLOCK** if any match. Among `src/` projects, `MicroKit.Persistence.EntityFrameworkCore` must appear
only in `MicroKit.Messaging.EntityFrameworkCore.csproj` (the test project
`MicroKit.Messaging.MediatR.IntegrationTests` references it, test-only).

### 3. No Version= on PackageReference

```bash
grep -rn "Version=" modules/MicroKit.Messaging/ --include="*.csproj" \
  | grep "PackageReference" | grep -v "PackageVersion"
```

**BLOCK** if any match. All versions managed by `Directory.Packages.props` (CPM).

### 4. Cross-module references use CIReleaseBuild pattern

```bash
grep -rn "CIReleaseBuild" modules/MicroKit.Messaging/src/ --include="*.csproj"
```

Verify that any cross-module `ProjectReference` of a `src/` project is inside an `ItemGroup` with
`Condition="'$(CIReleaseBuild)' != 'true'"` and has a symmetric `PackageReference`
`ItemGroup` with `Condition="'$(CIReleaseBuild)' == 'true'"`. References inside the module, and
every reference of a test project, are unconditional `ProjectReference`.

**BLOCK** if pattern is missing for any cross-module dependency of a `src/` project.

### 5. No circular dependencies

```bash
dotnet build modules/MicroKit.Messaging/MicroKit.Messaging.slnx --no-restore 2>&1 \
  | grep -i "circular"
```

**BLOCK** if circular dependency detected.

### 6. No MicroKit.Auth or MicroKit.Tenancy dependency

```bash
grep -rn "MicroKit\.Auth\|MicroKit\.Tenancy" \
  modules/MicroKit.Messaging/ --include="*.csproj"
```

**BLOCK** if any match. Messaging does not depend on Auth or Tenancy (ADR-EXEC-001: the host
composes a tenant-aware `IExecutionScopeFactory`).

### 7. v2 provider scaffolds have IsPackable=false

```bash
for pkg in RabbitMQ AzureServiceBus Kafka OpenTelemetry Serialization; do
  csproj="modules/MicroKit.Messaging/src/MicroKit.Messaging.${pkg}/MicroKit.Messaging.${pkg}.csproj"
  if [ -f "$csproj" ]; then
    grep -L "IsPackable.*false" "$csproj" && echo "BLOCK: $csproj missing IsPackable=false"
  fi
done
```

**BLOCK** if any v2 scaffold package is missing `<IsPackable>false</IsPackable>`. None of these
projects exists today, so the loop checks nothing until one is added.

## Scope

This hook applies **only to `.csproj` files under `modules/MicroKit.Messaging/`**.
