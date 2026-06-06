---

name: module-bootstrap
description: Use this skill when creating, validating, migrating, or completing a MicroKit module, including its structure, CI/CD pipeline, documentation, versioning, and registration in the monorepo.
--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

# Purpose

Provide a complete and deterministic process for bootstrapping, validating, and maintaining MicroKit modules across their entire lifecycle.

This includes architecture, project structure, CI/CD, documentation, and monorepo registration.

---

## When to Use

Use this skill when:

* Creating a new MicroKit module from scratch
* Validating an incomplete module
* Migrating or refactoring an existing module into standard structure
* Ensuring a module is compliant with monorepo standards
* Adding CI/CD for a new module
* Registering a module in the global system

---

# Module Lifecycle

## Phase 1 — Design (Before any file creation)

1. Define module purpose (1 paragraph)
2. Identify domain boundaries
3. Identify dependencies (other MicroKit modules)
4. Identify external dependencies (NuGet)
5. Identify optional providers/adapters
6. Validate architecture via `.claude/` bootstrap process
7. Confirm design before implementation

---

## Phase 2 — Structure Creation

After design approval:

* Create module directory structure

* Generate solution file

* Create standard projects:

  * `Abstractions`
  * `Core`
  * `Providers` (optional)
  * `Tests`

* Ensure Central Package Management compliance

* Register module in monorepo solution

---

## Phase 3 — Versioning Setup

Each module MUST include versioning configuration:

* Semantic versioning enabled
* Release tagging strategy defined
* Path filters configured for CI/CD

---

## Phase 4 — CI/CD Setup

Each module MUST include:

* Build workflow
* Unit tests execution
* Integration tests execution
* Architecture tests execution
* Coverage reporting

Rules:

* CI must be scoped to module path only
* Must support manual trigger
* Must validate Release configuration

---

## Phase 5 — Documentation

Each module MUST include:

* README.md (usage + quickstart)
* Architecture documentation
* Initial changelog
* Integration notes (if applicable)

---

## Phase 6 — Global Registration

Each module MUST be registered in the monorepo system:

* Module registry (.claude configuration)
* Documentation index
* Dependency graph
* Root README update

---

# Module Structure Standards

## Abstractions Project

* Defines contracts only
* No implementation logic
* Must be dependency-light

## Core Project

* Implements business logic
* Depends on Abstractions only

## Providers (optional)

* External integrations
* Must not contain business logic

## Tests

* Unit tests
* Integration tests
* Architecture tests

---

# CI/CD Requirements

Each module CI MUST include:

* Build validation (Release)
* Unit tests
* Integration tests
* Architecture tests
* Coverage upload

All workflows must be scoped to module path.

---

# Versioning Model

Each module uses:

* Semantic versioning
* Branch-based release workflow
* Tag-based release tracking

---

# Templates

## Abstractions Project

```xml id="b3q8yx"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>MicroKit.[Module].Abstractions</AssemblyName>
    <RootNamespace>MicroKit.[Module]</RootNamespace>
    <Description>Abstractions for MicroKit.[Module]</Description>
  </PropertyGroup>
</Project>
```

---

## Core Project

```xml id="k9x1qp"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>MicroKit.[Module]</AssemblyName>
    <RootNamespace>MicroKit.[Module]</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../Abstractions/MicroKit.[Module].Abstractions.csproj" />
  </ItemGroup>
</Project>
```

---

## CI Pipeline

```yaml id="c7m2pp"
name: CI — MicroKit.[Module]

on:
  push:
    paths:
      - 'modules/MicroKit.[Module]/**'
  pull_request:

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json

      - name: Build
        run: dotnet build <solution> -c Release

      - name: Test
        run: dotnet test <solution> -c Release --no-build
```

---

# Best Practices

* Enforce strict module boundaries
* Keep Abstractions pure
* Avoid cyclic dependencies between modules
* Always validate architecture before implementation
* Ensure CI is deterministic
* Treat module creation as a first-class architectural event

---

## Validation Checklist

* [ ] Module design approved
* [ ] Structure created
* [ ] Solution registered in monorepo
* [ ] CI/CD configured
* [ ] Tests included
* [ ] Documentation present
* [ ] Versioning configured
* [ ] Module registered in global system
