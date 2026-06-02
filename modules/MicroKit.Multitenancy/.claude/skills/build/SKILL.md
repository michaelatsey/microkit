# Skill: Build — MicroKit.Multitenancy

## Build commands

```bash
# Full solution
dotnet build modules/MicroKit.Multitenancy/MicroKit.Multitenancy.slnx -c Release

# Single project
dotnet build modules/MicroKit.Multitenancy/src/MicroKit.Multitenancy.Abstractions -c Release

# Restore first
dotnet restore modules/MicroKit.Multitenancy/MicroKit.Multitenancy.slnx
```

## Common build errors

### CS1591 — Missing XML doc
```
Cause: Public member without <summary> in a src/ project
Fix: Add XML documentation; set GenerateDocumentationFile=false in test projects only
```

### NU1605 / CPM violation
```
Cause: Version= attribute on PackageReference
Fix: Move version to Directory.Packages.props; remove Version= from .csproj
```

### Analyzer type not found
```
Cause: netstandard2.0 target can't resolve net10.0 type
Fix: Ensure Analyzers project only references Microsoft.CodeAnalysis.CSharp, no MicroKit runtime packages
```

### IL2091 (EF Core trim warning)
```
Cause: DbContext.Set<T>() with generic T
Fix: <NoWarn>$(NoWarn);IL2091</NoWarn> in EntityFrameworkCore .csproj
```

## Build order (safe)
```
1. MicroKit.Multitenancy.Abstractions
2. MicroKit.Multitenancy
3. MicroKit.Multitenancy.AspNetCore
4. MicroKit.Multitenancy.EntityFrameworkCore
5. MicroKit.Multitenancy.Analyzers (independent)
```
