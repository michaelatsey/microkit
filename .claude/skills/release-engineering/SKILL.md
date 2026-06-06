---

name: release-engineering
description: Use this skill when preparing, coordinating, troubleshooting, or executing releases across MicroKit modules, including versioning, tagging, packaging, and publishing to NuGet.
--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

# Purpose

Define and orchestrate the full release lifecycle of MicroKit modules, including versioning, CI/CD release workflows, dependency-aware release ordering, and NuGet publishing.

This skill applies to all modules in the MicroKit ecosystem.

---

# When to Use

Use this skill when:

* Preparing a module release
* Resolving versioning issues (Nerdbank.GitVersioning)
* Creating or updating release workflows
* Coordinating multi-module releases
* Debugging CI/CD release failures
* Publishing packages to NuGet
* Managing release tags and branches

---

# Versioning System (Nerdbank.GitVersioning)

Each module defines a `version.json` controlling:

* semantic version base
* branch-based version computation
* tag-based public releases
* pre-release channels
* CI build metadata injection

---

## Version Computation Model

Typical behavior:

* `main + tag` → stable release version
* `main without tag` → pre-release build
* `feature/*` → feature-based pre-release
* `release/*` → beta or staging versions

---

# Release Triggers

Releases are triggered by Git tags:

```text id="v7r3m2"
[module]-vX.Y.Z
```

Each tag corresponds to:

* build
* test
* pack
* publish
* GitHub release creation

---

# CI/CD Release Pipeline

Each release pipeline MUST:

1. Checkout full git history
2. Detect module from tag
3. Build in Release configuration
4. Run tests
5. Create NuGet packages
6. Push to NuGet feed
7. Publish GitHub release

---

# Multi-Module Release Coordination

When multiple modules are involved:

* Respect dependency graph order
* Ensure each package is published before dependent modules are released
* Avoid version drift across dependent modules

Rule:

> A dependent module MUST always reference a published version

---

# Dependency Version Strategy

During development:

* Stable: pinned versions
* Pre-release: `*-dev.*` versions
* Local testing: NuGet local feed or GitHub Packages

---

# Release Workflow (Manual)

Steps:

1. Ensure main is up-to-date
2. Run full Release build
3. Validate tests
4. Update changelog
5. Commit release preparation
6. Push changes
7. Create and push tag
8. Monitor CI pipeline
9. Verify NuGet publication

---

# GitHub Actions Requirements

Each release workflow MUST:

* Support tag-triggered execution
* Build with full git history
* Pack in Release mode
* Publish to NuGet
* Generate GitHub release notes

---

# Failure Modes

## Version mismatch

Cause:

* incorrect tag format
* inconsistent version.json

Fix:

* align tag with module versioning scheme

---

## Dependency not yet published

Cause:

* multi-module release race condition

Fix:

* enforce sequential release order

---

## Build mismatch

Cause:

* missing Release configuration validation

Fix:

* ensure CI uses `-c Release`

---

# Best Practices

* Treat releases as architectural events
* Never release without full dependency graph validation
* Always ensure reproducible builds
* Use tags as immutable release contracts
* Avoid manual NuGet publishing outside CI
* Maintain strict separation between dev and release versions

---

## Validation Checklist

* [ ] Versioning scheme valid (Nerdbank)
* [ ] Tag format correct
* [ ] Build passes in Release
* [ ] Tests pass
* [ ] Packages generated correctly
* [ ] NuGet publish successful
* [ ] GitHub release created
* [ ] Dependency graph respected
