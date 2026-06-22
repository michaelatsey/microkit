# Skill: microkit-messaging-build

How to build MicroKit.Messaging reliably in all configurations.

## Build Targets

### Full solution build (all packages + tests)

```bash
dotnet build modules/MicroKit.Messaging/MicroKit.Messaging.slnx
```

### Release build (enables TreatWarningsAsErrors)

```bash
dotnet build modules/MicroKit.Messaging/MicroKit.Messaging.slnx -c Release
```

### CI / Release build (uses published NuGet packages instead of ProjectReferences)

```bash
dotnet build modules/MicroKit.Messaging/MicroKit.Messaging.slnx -c Release \
  -p:CIReleaseBuild=true
```

### Single package build

```bash
dotnet build modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/
dotnet build modules/MicroKit.Messaging/src/MicroKit.Messaging/
dotnet build modules/MicroKit.Messaging/src/MicroKit.Messaging.EntityFrameworkCore/
dotnet build modules/MicroKit.Messaging/src/MicroKit.Messaging.Testing/
```

## Dependency-Safe Build Order

Build in this order when building incrementally (avoids restore failures):

1. `MicroKit.Messaging.Abstractions`
2. `MicroKit.Messaging` (Core)
3. `MicroKit.Messaging.EntityFrameworkCore`
4. `MicroKit.Messaging.Testing`
5. `tests/*` (any order)
6. v2 scaffolds (any order — all `IsPackable=false`)

## Common Build Errors

### CS1591 — Missing XML documentation

```
error CS1591: Missing XML comment for publicly visible type or member 'XxxYyy'
```

**Fix:** Add `<summary>` doc to the indicated public member. All public `src/` members require XML docs.

**Do not suppress** with `NoWarn` in `src/` projects — only in `tests/` projects.

### CPM violation — Version= in .csproj

```
error NU1008: Projects that use central package version management should not define the version on the PackageReference items
```

**Fix:** Remove `Version="x.y.z"` from the `<PackageReference>`. All versions live in root `Directory.Packages.props`.

### Missing CIReleaseBuild cross-module ref

```
error MSB4019: The imported project ".../MicroKit.Result/..." was not found.
```

When running with `CIReleaseBuild=true`, cross-module `ProjectReference` elements must not exist.
**Fix:** Verify the two-ItemGroup pattern in the `.csproj` — see `microkit-messaging-dependencies.md`.

### TreatWarningsAsErrors (Release only)

Warnings become errors in Release config. Common sources:
- CS1591: missing XML doc on public member → add doc
- CS8602: nullable dereference → fix nullability
- CA1707: identifier contains underscore → rename (test projects suppress via `NoWarn`)

## Verify 0 Errors / 0 Warnings Gate

```bash
result=$(dotnet build modules/MicroKit.Messaging/MicroKit.Messaging.slnx -c Release 2>&1)

# Check for compiler diagnostics (avoids false positives from informational MSBuild lines)
diagnostics=$(echo "$result" | grep -E "^.+ (error|warning) [A-Z]+[0-9]+" || true)

if [ -n "$diagnostics" ]; then
  echo "BUILD GATE FAILED:"
  echo "$diagnostics"
  exit 1
fi

echo "$result" | grep "Build succeeded"
```

The gate passes when `Build succeeded` with `0 Error(s)` and `0 Warning(s)`.
The `-E "^.+ (error|warning) [A-Z]+[0-9]+"` pattern matches MSBuild diagnostic lines
(e.g. `src/Foo.cs(10,5): error CS0001`) and avoids false positives from NuGet restore
messages that contain "warning" or "error" in informational text.

## Package Verification After Build

```bash
dotnet pack modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/ -c Release -o /tmp/messaging-nupkg
dotnet pack modules/MicroKit.Messaging/src/MicroKit.Messaging/ -c Release -o /tmp/messaging-nupkg
dotnet pack modules/MicroKit.Messaging/src/MicroKit.Messaging.EntityFrameworkCore/ -c Release -o /tmp/messaging-nupkg
dotnet pack modules/MicroKit.Messaging/src/MicroKit.Messaging.Testing/ -c Release -o /tmp/messaging-nupkg

ls /tmp/messaging-nupkg/*.nupkg
```

v2 scaffold packages (`RabbitMQ`, `AzureServiceBus`, etc.) must NOT produce `.nupkg` files
(`IsPackable=false`).
