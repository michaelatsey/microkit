# Hook: dependency-check — MicroKit.Messaging

Runs after any `.csproj` change under `modules/MicroKit.Messaging/`. Invoke the
`microkit-messaging-dependency-guardian` agent or run the checks below manually.

## Checks

### 1. MediatR.Contracts forbidden everywhere

```bash
grep -rn "MediatR\.Contracts" modules/MicroKit.Messaging/ \
  --include="*.csproj" --include="*.cs" --include="*.props"
```

**BLOCK** if any match. Zero tolerance — no exception for any package.

### 2. MicroKit.Persistence.EntityFrameworkCore confined

```bash
grep -rn "MicroKit\.Persistence\.EntityFrameworkCore" \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging/ \
  modules/MicroKit.Messaging/src/MicroKit.Messaging.Testing/ \
  --include="*.csproj"
```

**BLOCK** if any match. `MicroKit.Persistence.EntityFrameworkCore` must appear only in
`MicroKit.Messaging.EntityFrameworkCore.csproj`.

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

Verify that any cross-module `ProjectReference` is inside an `ItemGroup` with
`Condition="'$(CIReleaseBuild)' != 'true'"` and has a symmetric `PackageReference`
`ItemGroup` with `Condition="'$(CIReleaseBuild)' == 'true'"`.

**BLOCK** if pattern is missing for any cross-module dependency.

### 5. No circular dependencies

```bash
dotnet build modules/MicroKit.Messaging/MicroKit.Messaging.slnx --no-restore 2>&1 \
  | grep -i "circular"
```

**BLOCK** if circular dependency detected.

### 6. No MicroKit.Auth or MicroKit.Multitenancy dependency

```bash
grep -rn "MicroKit\.Auth\|MicroKit\.Multitenancy" \
  modules/MicroKit.Messaging/ --include="*.csproj"
```

**BLOCK** if any match. Messaging does not depend on Auth or Multitenancy.

### 7. v2 provider scaffolds have IsPackable=false

```bash
for pkg in RabbitMQ AzureServiceBus Kafka OpenTelemetry Serialization; do
  csproj="modules/MicroKit.Messaging/src/MicroKit.Messaging.${pkg}/MicroKit.Messaging.${pkg}.csproj"
  if [ -f "$csproj" ]; then
    grep -L "IsPackable.*false" "$csproj" && echo "BLOCK: $csproj missing IsPackable=false"
  fi
done
```

**BLOCK** if any v2 scaffold package is missing `<IsPackable>false</IsPackable>`.

## Scope

This hook applies **only to `.csproj` files under `modules/MicroKit.Messaging/`**.
