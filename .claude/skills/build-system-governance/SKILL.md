---

name: build-system-governance
description: Use this skill when configuring, modifying, or reasoning about the MicroKit monorepo build system, including MSBuild configuration, Central Package Management, SDK versioning, analyzers, and cross-module build rules.
-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

# Purpose

Define and enforce the global build architecture of the MicroKit monorepo, including MSBuild configuration, dependency management strategy, SDK versioning, and cross-cutting build constraints.

This skill applies to the entire repository, not individual modules.

---

## When to Use

Use this skill when:

* Modifying `Directory.Build.props`
* Modifying `Directory.Packages.props`
* Updating `global.json`
* Adding or updating NuGet dependencies globally
* Fixing cross-module build issues
* Designing repository-wide build rules
* Configuring analyzers or code style enforcement
* Adjusting packaging or SDK constraints

---

# Build System Architecture

## 1. Directory.Build.props (global MSBuild rules)

Applies to all projects in the monorepo.

Responsibilities:

* Target framework enforcement
* Code style enforcement
* Nullable reference types
* Build quality rules
* Shared NuGet metadata
* Analyzer configuration

Key principle:

> This file defines **how every project in MicroKit behaves**

---

## 2. Directory.Packages.props (Central Package Management)

Defines all dependency versions centrally.

Rules:

* No `Version=` in `.csproj`
* All versions declared centrally
* Version consistency across modules is mandatory

Versioning strategy:

* Stable framework packages → pinned minor (`10.0.8`)
* Libraries → version ranges (`12.*`, `8.*`)
* Test tools → pinned major range

---

## 3. global.json (SDK control)

Locks the .NET SDK version for reproducible builds.

Rules:

* All contributors must use same SDK baseline
* `rollForward` allowed only within minor range

---

## 4. Analyzer Strategy

Global analyzers:

* Roslyn analyzers
* Code style enforcement
* Trimming and AOT validation

Rules:

* Analyzers must not break runtime builds
* Must be deterministic across CI and local

---

# Package Management Rules

## Core Principles

1. All versions centralized
2. No inline versions allowed
3. Dependency changes are architectural decisions
4. Prefer framework-native APIs
5. Avoid unnecessary transitive dependencies

---

## Package Categories

### Framework Packages

* Microsoft.Extensions.*
* System.*

Strict version alignment required.

---

### Library Packages

* MediatR
* FluentValidation
* Polly

May use version ranges (`*`) within major version.

---

### Testing Packages

* xUnit
* FluentAssertions
* NSubstitute
* BenchmarkDotNet

Only used in test projects.

---

### Analyzers

* Roslyn analyzers
* Code quality tools

Must use `PrivateAssets="all"`.

---

# Build Commands

## Monorepo build

```bash id="k8w3bz"
dotnet build MicroKit.slnx
```

---

## Module build

```bash id="d7x2rp"
dotnet build modules/<Module>/<Module>.slnx
```

---

## Test execution

```bash id="m2q9sw"
dotnet test --collect:"XPlat Code Coverage"
```

---

## Packaging

```bash id="p0v8qa"
dotnet pack -c Release
```

---

## Dependency inspection

```bash id="v5x1lt"
dotnet list package --outdated
dotnet list package --vulnerable --include-transitive
```

---

# Dependency Rules

## Adding a package

1. Add version in `Directory.Packages.props`
2. Add reference without version in `.csproj`
3. Restore solution
4. Validate build + tests

---

## Anti-patterns

* ❌ Version in `.csproj`
* ❌ Per-project version drift
* ❌ Uncontrolled transitive dependencies
* ❌ Mixing test dependencies in production projects

---

# Common Failures

## Version drift

Cause:

* inconsistent package versions across modules

Fix:

* enforce central package management

---

## SDK mismatch

Cause:

* wrong `global.json` SDK version

Fix:

* align local SDK with repo

---

## Analyzer instability

Cause:

* mismatched Roslyn packages

Fix:

* align analyzer versions globally

---

# Best Practices

* Treat build system as architecture, not configuration
* Prefer reproducible builds over flexibility
* Keep dependency graph minimal
* Enforce deterministic SDK usage
* Validate all changes at monorepo level first

---

## Validation Checklist

* [ ] MSBuild rules consistent across projects
* [ ] Central Package Management respected
* [ ] No inline versions in csproj
* [ ] SDK version aligned via global.json
* [ ] Build reproducible locally and CI
* [ ] Analyzer rules stable
* [ ] No dependency drift across modules
