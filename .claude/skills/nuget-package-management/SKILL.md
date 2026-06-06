---

name: nuget-package-management
description: Use this skill when adding, updating, validating, inspecting, testing, packaging, or reviewing NuGet dependencies and packages within MicroKit modules.
--------------------------------------------------------------------------------------------------------------------------------------------------------------------

---

# Purpose

Provide a standardized process for managing NuGet dependencies, enforcing Central Package Management (CPM), validating package health, inspecting generated packages, and testing package consumption across all MicroKit modules.

This skill applies to every module in the MicroKit monorepo.

## When to Use

Use this skill when:

* Adding a new dependency to any module.
* Updating package versions centrally or locally.
* Reviewing dependency-related pull requests.
* Investigating restore or version conflicts.
* Performing security or vulnerability audits.
* Building or publishing NuGet packages.
* Testing local package consumption.
* Reviewing package structure, metadata, or integrity.

## Central Package Management (CPM)

MicroKit enforces Central Package Management.

All package versions must be defined in:

```text
build/Directory.Packages.props
```

### Requirements

* Package versions MUST only be declared in `Directory.Packages.props`.
* `PackageReference` MUST NOT contain `Version=`.
* Version changes MUST be reviewed centrally.
* All modules MUST use consistent dependency versions.

### Correct Example

```xml
<PackageReference Include="Microsoft.Extensions.Logging" />
```

### Incorrect Example

```xml
<PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.0" />
```

## Inspect Existing Dependencies

View centralized package versions:

```bash
cat build/Directory.Packages.props
```

Check outdated packages:

```bash
dotnet list <solution> package --outdated
```

Check vulnerable packages:

```bash
dotnet list <solution> package --vulnerable
```

### Review Requirements

Before updating a dependency:

* Verify compatibility with all modules.
* Review release notes and breaking changes.
* Check security advisories.
* Evaluate transitive dependency impact.
* Ensure alignment with MicroKit architecture principles.

## Add a New Package

### Required Process

#### 1. Register Version Centrally

Add version to:

```text
build/Directory.Packages.props
```

#### 2. Add Package Reference

In the target project:

```xml
<PackageReference Include="PackageName" />
```

(no version allowed)

#### 3. Restore Dependencies

```bash
dotnet restore <solution>
```

#### 4. Validate Dependency Policies

Each MicroKit module owns its own dependency validation rules and dependency guardian.

Run the dependency guardian for the target module before submitting changes.

Example:

```text
dependency-guardian
```

The exact command or implementation may vary by module, but dependency validation MUST pass before proceeding.

#### 5. Build & Verify

Ensure:

* Restore succeeds
* Build succeeds
* Tests pass
* Analyzers pass

## Inspect Generated Packages

### Inspect Package Metadata

```bash
dotnet nuget inspect artifacts/<module>/PackageName.*.nupkg
```

Check:

* Package identity
* Dependencies
* Target frameworks
* Metadata correctness

### Inspect Package Contents

```bash
unzip -l artifacts/<module>/PackageName.*.nupkg
```

Verify:

* Assemblies are included
* XML documentation is present
* Source Link is included
* License file exists (if applicable)
* No unintended files included

## Local Package Testing

Validate packages before publishing.

### Add Local Feed

```bash
dotnet nuget add source ./artifacts/<module>/ \
  --name MicroKitLocal
```

### Consume Package Locally

```bash
dotnet add package PackageName \
  --source MicroKitLocal \
  --prerelease
```

### Validation Goals

* Package restores correctly
* Build succeeds
* APIs work as expected
* No missing dependencies
* Symbols are available for debugging

## Symbol Packages

Every NuGet package MUST produce symbols.

Required configuration:

```xml
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```

### Requirements

* `.snupkg` must be generated
* Source Link must be functional
* Debugging into package code must work
* Symbols must be published alongside packages

## Dependency Review Guidelines

Evaluate dependencies using:

### Necessity

* Is the dependency strictly required?
* Can framework features replace it?

### Maintenance

* Is the package actively maintained?
* Is the release cadence healthy?

### Security

* Known vulnerabilities?
* Trusted source?

### Complexity

* Does it introduce heavy transitive dependencies?
* Does it increase runtime or build complexity?

### Architectural Fit

* Does it align with MicroKit principles?
* Does it introduce unwanted coupling?

## Common Failures

### CPM Violation

Cause:

```xml
<PackageReference Version="..." />
```

Fix:

Move version to:

```text
build/Directory.Packages.props
```

---

### Vulnerable Package

Fix:

* Upgrade package
* Or document mitigation if unavoidable

---

### Restore Failure

Possible causes:

* Missing feed
* Version conflict
* Broken dependency graph
* SDK mismatch

---

### Dependency Guardian Failure

Possible causes:

* Dependency policy violation
* Unauthorized package introduction
* Version rule violation
* Security or compliance rule failure

Fix:

* Review the module's dependency guardian output
* Apply the required remediation
* Re-run validation until it passes

---

### Missing Symbol Package

Possible causes:

* Symbols disabled
* Packaging misconfiguration
* Build pipeline issue

## Best Practices

* Prefer framework-native APIs over new dependencies
* Minimize transitive dependencies
* Centralize all version management
* Validate security before introducing packages
* Run the module-specific dependency guardian before submitting changes
* Always test packages locally before publishing
* Ensure symbol packages are generated
* Treat dependency changes as architectural changes
* Keep package metadata clean and complete

## Validation Checklist

* [ ] Version defined in `Directory.Packages.props`
* [ ] No `Version=` in project files
* [ ] Restore succeeds
* [ ] Build succeeds
* [ ] Tests pass
* [ ] Module dependency guardian passes
* [ ] No known vulnerabilities
* [ ] Package inspected
* [ ] Local consumption validated
* [ ] Symbol package generated
* [ ] Metadata verified
