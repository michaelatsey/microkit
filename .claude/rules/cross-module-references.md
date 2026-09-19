---
paths:
  - "**/*.csproj"
  - "**/*.slnx"
  - "Directory.*.props"
---

# Cross-module references and module boundaries

## Abstractions projects

A `MicroKit.<Module>.Abstractions` project holds contracts only:

- Interfaces, contracts and value types. No implementation: no concrete class other than an
  immutable record or struct.
- No third-party package that carries an implementation. `Microsoft.Extensions.*.Abstractions`
  packages are allowed.

## Adapter packages

When module A needs a capability of module B and the graph in CLAUDE.md does not allow A → B,
A does not reference B. An optional package `MicroKit.A.B` references both and carries the
bridge; a consumer who wants the integration installs it. Example: `MicroKit.Messaging.MediatR`
puts `MicroKit.MediatR` domain events on the Messaging outbox, so neither `MicroKit.Messaging`
nor `MicroKit.MediatR` references the other.

## The two-ItemGroup pattern

A `src/` project that references another module declares the reference twice, in two
`ItemGroup` elements conditioned on `CIReleaseBuild`:

```xml
<!-- ⚠ Any new cross-module dependency must be added to BOTH ItemGroups -->
<ItemGroup Condition="'$(CIReleaseBuild)' != 'true'">
  <ProjectReference Include="../../../MicroKit.Result/src/MicroKit.Result/MicroKit.Result.csproj" />
  <ProjectReference Include="../../../MicroKit.Domain/src/MicroKit.Domain/MicroKit.Domain.csproj" />
</ItemGroup>
<ItemGroup Condition="'$(CIReleaseBuild)' == 'true'">
  <PackageReference Include="MicroKit.Result" />
  <PackageReference Include="MicroKit.Domain" />
</ItemGroup>
```

By default a build resolves every module from source. `CIReleaseBuild=true` swaps each
cross-module `ProjectReference` for the published package of the same id. Per ADR-GLOBAL-002,
this mode is a compatibility validation, not a packaging mechanism: it checks that the working
tree compiles against the published catalogue. A failure in this mode means the source calls an
API newer than the published sibling carries; that is information about publishability, not a
broken release.

The pattern applies to cross-module references only.

Rules:

- The condition goes on the `ItemGroup`, never on an individual `ProjectReference` or
  `PackageReference`. Conditions scattered over items hide an asymmetry.
- Strict symmetry: every `ProjectReference` in the first group has a `PackageReference` with the
  matching package id in the second, and the reverse. A reference missing from one group breaks
  the build in that mode.
- `ProjectReference` paths are relative, never absolute.
- A test project (`IsPackable=false`) references other modules with unconditional
  `ProjectReference` and no `CIReleaseBuild` group: it is never packed.
- A module's `.slnx` lists, under its `/deps/` folder, every project of another module that its
  projects reach through `ProjectReference`; otherwise `dotnet restore` of the module solution
  fails.
