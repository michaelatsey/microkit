---

name: dotnet-build
description: Use this skill when building, restoring, validating, troubleshooting, or reviewing the build process for any MicroKit module or solution component.
----------------------------------------------------------------------------------------------------------------------------------------------------------------

# Purpose

Provide a standardized process for restoring dependencies, building solutions or projects, validating build configurations, and diagnosing build failures across the MicroKit monorepo.

This skill applies to all modules in the MicroKit ecosystem.

## When to Use

Use this skill when:

* Building any MicroKit module or solution.
* Building a specific project during development.
* Validating a pull request before submission.
* Troubleshooting build failures or CI issues.
* Verifying Release readiness.
* Ensuring compliance with repository build standards.
* Investigating dependency or SDK-related build issues.

## Instructions

### 1. Restore Dependencies

Before diagnosing build issues, ensure dependencies are restored.

```bash
dotnet restore <solution>
```

Verify:

* Restore completes successfully
* No version conflicts exist
* SDK resolution succeeds

---

### 2. Build the Solution

Standard validation builds:

```bash
dotnet build <solution> -c Debug
```

Release validation builds (strict mode):

```bash
dotnet build <solution> -c Release
```

Release builds are authoritative and enforce stricter rules (warnings treated as errors, validation constraints, etc.).

---

### 3. Build Individual Projects

For faster iteration on a single component:

```bash
dotnet build <project-path> -c Debug
```

Use this during local development to reduce feedback loop time.

---

### 4. Troubleshoot Build Failures

Enable detailed diagnostics when needed:

```bash
dotnet build <solution> -c Debug -v d
```

Analyze:

* Compilation errors
* Analyzer diagnostics
* Package resolution issues
* SDK compatibility problems
* Misconfigured project references

---

### 5. Respect Dependency Order (when building manually)

When building projects individually, respect dependency flow.

Typical order:

1. Core / Abstractions projects
2. Domain / main implementation projects
3. Extension / integration projects
4. Analyzers and source generators (build-time only)

Notes:

* The solution file already enforces correct ordering.
* Manual builds require explicit attention to dependencies.
* Analyzers and generators must never be treated as runtime dependencies.

---

## Common Build Failures

### Missing XML Documentation

Error:

```text
CS1591
```

Cause:

Public API member is missing required XML documentation.

Fix:

Add proper XML documentation:

```csharp
/// <summary>
/// Describes the operation.
/// </summary>
public void Execute() { }
```

---

### Central Package Management Violation

Cause:

```xml
<PackageReference Include="PackageName" Version="1.0.0" />
```

Fix:

Remove version and use Central Package Management:

```text
build/Directory.Packages.props
```

---

### SDK Resolution Failure

Error:

```text
NETSDK1004
```

Cause:

Incorrect or missing SDK version.

Fix:

Check:

```text
global.json
```

at repository root.

---

### Analyzer / Generator Build Failure

Cause:

Mismatch in Roslyn or analyzer package versions.

Fix:

Ensure consistency in:

```text
build/Directory.Packages.props
```

especially:

* Microsoft.CodeAnalysis.CSharp
* related analyzer dependencies

---

## Build Environment Requirements

All MicroKit modules MUST follow these constraints:

| Setting               | Requirement            |
| --------------------- | ---------------------- |
| SDK                   | Defined in global.json |
| Target Framework      | net10.0                |
| Nullable              | enable                 |
| ImplicitUsings        | enable                 |
| LangVersion           | latest                 |
| TreatWarningsAsErrors | true in Release        |

Validate these before investigating unexpected behavior.

---

## Best Practices

* Prefer full solution builds before PR submission
* Validate both Debug and Release configurations
* Fix warnings early (do not rely on Release to surface them)
* Restore dependencies after package changes
* Use project-level builds for fast iteration
* Use verbose logging only when diagnosing issues
* Keep builds deterministic and reproducible
* Treat build failures as architectural signals, not just compilation errors

---

## Validation Checklist

* [ ] Dependencies restored successfully
* [ ] Solution builds in Debug
* [ ] Solution builds in Release
* [ ] No unexpected warnings remain
* [ ] Dependency graph valid
* [ ] SDK version matches global.json
* [ ] No analyzer or generator failures
* [ ] Build is reproducible across environments
