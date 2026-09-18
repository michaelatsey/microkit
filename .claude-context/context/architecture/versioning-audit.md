# Versioning audit — the mechanism as it is

Date: 2026-09-05 · Repository `michaelatsey/microkit` at `HEAD = 9e5c682` (branch `dev`, working
tree clean) · Commissioned by `LOT.md` ("Versioning audit: report the mechanism as it is").

**This document reports. It proposes nothing.** No recommendation, no target design, no argument
for or against the unified-versioning direction, which `LOT.md` freezes.

Every statement carries an evidence marker:

- **OBSERVED** — read from a repository file (path:line), a command and its output, NuGet API
  metadata, or a GitHub Actions run.
- **INFERRED** — derived from stated observations.
- **UNDETERMINED** — the evidence needed does not exist or could not be obtained.

Where `LOT.md`'s own *Observed state* or a session trace disagrees with what was measured, the
measurement is reported and the disagreement is listed under **Surprises**.

---

## 0. Executive factual summary

**Mechanism, in one paragraph.** Nothing in the repository sets a version. There is exactly one
`Directory.Build.props` and it contains no version property; there is no `Directory.Build.targets`
anywhere, no `build/` directory, no `nuget.config`, no `.config/dotnet-tools.json`, and
**Nerdbank.GitVersioning is not installed and never has been** — so the nine per-module
`version.json` files are inert documentation and every ordinary build stamps the MSBuild default
`1.0.0` / `1.0.0.0`. The only live version computation in the repository is a bash string-strip
inside each of the nine `release-*.yml` workflows: a tag push `<slug>-v<semver>` has its prefix
removed into `PACKAGE_VERSION`, which is passed to `dotnet pack --no-build` as
`-p:PackageVersion=` (effective, nuspec only) and `-p:AssemblyVersion=` (**ineffective** — `pack`
does not recompile, proven below by hash). Pack targets the module **solution**, whose `/deps/`
folder holds sibling-module projects needed for a source restore, so pack emits those siblings too
and `dotnet nuget push nupkgs/*.nupkg --skip-duplicate` publishes whichever of them do not already
exist at that version.

| Fact | Value | Marker |
|---|---|---|
| Package ids published to nuget.org under this account | **83** | OBSERVED |
| Published id/version pairs | **232** (71 listed, 161 unlisted) | OBSERVED |
| Version dispersion, nine module `version.json` | `1.0` · `preview.1` ×2 · `preview.2` ×2 · `preview.3` ×2 · `preview.4` · `preview.5` | OBSERVED |
| Workflow files on disk | **18** — 9 `ci-*.yml`, 9 `release-*.yml` | OBSERVED |
| Workflow definitions attested by run history but deleted from the tree | **4** (`build.yml`, `nuget-publish.yml`, `ci-multitenancy.yml`, `release-multitenancy.yml`) | OBSERVED |
| Runs that packed and pushed | **36** (30 `release-*`, 6 `nuget-publish.yml`) | OBSERVED |
| Publications whose provenance chain closed | **232 of 232** | OBSERVED |
| Foreign publications (modules era) | **9**, every one still **listed** | OBSERVED |
| Duplicate pushes silently swallowed by `--skip-duplicate` | **12**, in the 17 runs whose logs survive | OBSERVED |
| Stable `1.0.0` packages published by accident | **19**, all unlisted | OBSERVED |
| Modules failing `dotnet build -c Release -p:CIReleaseBuild=true` | **2 of 9** — `MicroKit.MediatR`, `MicroKit.Messaging` | OBSERVED |
| Modules failing the plain `Release` build | **0 of 9** | OBSERVED |
| Packable projects in the tree | **42**; 41 are emitted by a module pack, 1 is in no solution | OBSERVED |
| Intra-module references inside a `CIReleaseBuild` block (CLAUDE.md forbids) | **6**, all in `MicroKit.Persistence` | OBSERVED |

---

## 1. Version origin and propagation

### 1.1 Every mechanism that can set a version

An exhaustive search over the tracked tree (`git archive HEAD`, 1218 files, equal to
`git ls-files | wc -l`) for `Directory.Build.props`, `Directory.Build.targets`, any `.props`,
any `.targets`, `nuget.config`, `dotnet-tools.json`, `version.json`, `global.json` returns
**eleven files and no others** — OBSERVED (`derived`/`raw/buildlogic-files.txt`):

```
Directory.Build.props
Directory.Packages.props
global.json
modules/{Auth,Domain,Execution.Abstractions,Logging,MediatR,Messaging,Persistence,Result,Tenancy}/version.json
```

| Mechanism | Where | Value / expression | Overridden by | Reaches `pack` as | Marker |
|---|---|---|---|---|---|
| MSBuild default | SDK | `Version=1.0.0` | any explicit property or `-p:` | `PackageVersion=1.0.0` when nothing else acts | OBSERVED |
| `Directory.Build.props` | root, 20 lines | **no version property at all** | — | nothing | OBSERVED |
| `global.json` | root | `sdk.version 10.0.107`, `rollForward latestMinor` | — | SDK selection only, not a package version | OBSERVED |
| `version.json` ×9 | per module | e.g. `"version": "1.0.0-preview.4"` | — | **nothing — never read** | OBSERVED |
| Nerdbank.GitVersioning | — | **absent** | — | — | OBSERVED |
| `.csproj` version properties | — | **none in any of the 91 projects** | — | — | OBSERVED |
| Custom `.props`/`.targets` transform | — | **none exist** | — | — | OBSERVED |
| Scripts | 16 `.sh` under `.claude/hooks/`, `.githooks/pre-push` | none touch a version | — | — | OBSERVED |
| **Tag → `PACKAGE_VERSION`** | `release-*.yml` | `TAG="${GITHUB_REF#refs/tags/}"; PACKAGE_VERSION="${TAG#<slug>-v}"` | — | `-p:PackageVersion=` | OBSERVED |
| **`AssemblyVersion` transform** | `release-*.yml` | `$(echo "$PACKAGE_VERSION" \| grep -oE '^[0-9]+\.[0-9]+\.[0-9]+').0` | — | `-p:AssemblyVersion=`, **inert** | OBSERVED |
| CPM pins | `Directory.Packages.props:18-37` | 16 `MicroKit.*` `PackageVersion` entries | — | the *dependency* versions in the nuspec, only when `CIReleaseBuild=true` | OBSERVED |

**Negative evidence for Nerdbank.GitVersioning, with its scope stated.** `grep -rn 'Nerdbank'`
over the tracked tree returns 23 files: 21 are Markdown (`.claude/CLAUDE.md:220`, agent and skill
files), and 2 are comments in `release-mediatr.yml:21` and `release-messaging.yml:21` that say the
version is derived from the tag *"independent of Nerdbank.GitVersioning tooling availability"*.
`git grep -l Nerdbank` over **all 287 reachable commits**, restricted to `*.csproj`, `*.props`,
`*.targets`, returns nothing; `git log --all -- '**/dotnet-tools.json'` returns **0 commits**; and
the only two commits that ever touched the string in a `.json` file (`0ab63fa`, `6835216`) changed
a `"versioningTool": "Nerdbank.GitVersioning"` key in `.claude/settings.json`. There has never been
a root `version.json`. **OBSERVED**, scope: all reachable refs of this clone.

### 1.2 The three determinations

**(i) What MSBuild evaluates.** `dotnet msbuild MicroKit.Result.csproj -getProperty:…` returns
`Version 1.0.0`, `VersionPrefix 1.0.0`, `VersionSuffix ""`, `PackageVersion 1.0.0`, and **empty**
for `AssemblyVersion`, `FileVersion`, `InformationalVersion` — those three are computed in a target,
not at evaluation. OBSERVED (`derived/eval-version-default.json`).

**(ii) What the compiler received.** After `dotnet build MicroKit.Result.slnx -c Release`, the
generated `obj/Release/net10.0/MicroKit.Result.AssemblyInfo.cs` contains
`AssemblyFileVersionAttribute("1.0.0.0")`, `AssemblyInformationalVersionAttribute("1.0.0")`,
`AssemblyVersionAttribute("1.0.0.0")`. OBSERVED (`raw/result-assemblyinfo-release.cs`).

**(iii) `-p:AssemblyVersion` on a `--no-build` pack is inert — proven, not argued.**
Packing the already-built output with the CI's own arguments:

```
dotnet pack MicroKit.Result.slnx --no-build -c Release \
  -p:PackageVersion=9.9.9-probe -p:AssemblyVersion=9.9.9.0 -o <scratch>
```

```
sha256(lib/net10.0/MicroKit.Result.dll inside the .nupkg) = 41768a9c4c7fb96a…32650
sha256(bin/Release/net10.0/MicroKit.Result.dll)          = 41768a9c4c7fb96a…32650
```

Byte-identical. The nuspec reads `<version>9.9.9-probe</version>` while the assembly identity is
untouched. **OBSERVED**, this repository, SDK 10.0.111.

Corroborated on a real publication: `MicroKit.Result 1.0.0-preview.3` downloaded from nuget.org
carries `AssemblyVersion 1.0.0.0` and `File/InformationalVersion 1.0.0`. OBSERVED.

### 1.3 The actual relation between the seven version notions

| Notion | Where it comes from | Value today | Marker |
|---|---|---|---|
| Repository version | **does not exist** — no root `version.json`, no repo-wide property | — | OBSERVED |
| Project version (`Version`/`PackageVersion`) | MSBuild default, unless a release workflow overrides | `1.0.0` locally; the tag's semver in a release run | OBSERVED |
| `AssemblyVersion` | SDK default derived from `Version` **at compile time** | always `1.0.0.0` — the release override arrives after compilation | OBSERVED |
| `FileVersion` | idem | always `1.0.0.0` | OBSERVED |
| `InformationalVersion` | idem | always `1.0.0` | OBSERVED |
| NuGet package version | `-p:PackageVersion` at pack, i.e. the tag string minus its prefix | e.g. `1.0.0-preview.4` | OBSERVED |
| Git tag | typed by hand | `{module-kebab}-v{semver}` for 17 of 21 tags | OBSERVED |

So: **the git tag is the only source of a package version, and it reaches the package manifest
only. It never reaches the binary.** Every assembly ever published by this repository claims to be
version `1.0.0.0`.

### 1.4 Diagram — both paths, and how one module's tag reaches `push`

```
                    ┌──────────────────────────────── NORMAL BUILD (ci-*.yml, and any local build)
                    │
  version.json ✗ ───┤  never read: Nerdbank.GitVersioning is not installed
  Directory.Build.props ─── contains no version property
                    │
                    ▼
            MSBuild default  Version = 1.0.0
                    │
                    ├──► AssemblyVersion 1.0.0.0 · FileVersion 1.0.0.0 · InformationalVersion 1.0.0
                    │        (stamped into the DLL at COMPILE time)
                    │
        cross-module refs = ProjectReference (source)      ← CIReleaseBuild != 'true'
        intra-module refs = ProjectReference (source)
                    │
                    ▼
              bin/Release/net10.0/*.dll        no pack, no push


  ══════════════════════════════════════════════════════════════════════════════════════

  git push origin messaging-v1.0.0-preview.4                       RELEASE PATH
                    │
                    ▼   on: push: tags: ['messaging-v*']        release-messaging.yml:6
        ┌───────────────────────────────────────────────┐
        │ TAG="${GITHUB_REF#refs/tags/}"                │  → messaging-v1.0.0-preview.4
        │ PACKAGE_VERSION="${TAG#messaging-v}"          │  → 1.0.0-preview.4
        └───────────────────────────────────────────────┘        release-messaging.yml:23-26
                    │
                    ▼
   restore  MicroKit.Messaging.slnx  -p:CIReleaseBuild=true        :39
   build    …       --no-restore -c Release -p:CIReleaseBuild=true :42   ← NO version override
                    │                                                     ⇒ DLL = 1.0.0.0
        cross-module refs = PackageReference, version from CPM  ← CIReleaseBuild == 'true'
        intra-module refs = ProjectReference (source, version 1.0.0)
                    │
                    ▼
   pack     MicroKit.Messaging.slnx --no-build                     :49-54
              -p:PackageVersion=1.0.0-preview.4        ← lands in the nuspec ONLY
              -p:AssemblyVersion=1.0.0.0               ← inert: --no-build does not recompile
              -o nupkgs
                    │
                    │   the .slnx carries a /deps/ folder of SIBLING-MODULE projects,
                    │   required for a source restore in dev mode — pack emits them too
                    ▼
   nupkgs/  MicroKit.Messaging{,.Abstractions,.EntityFrameworkCore,.MediatR}.1.0.0-preview.4.nupkg
            MicroKit.Result.1.0.0-preview.4.nupkg                 ┐
            MicroKit.Domain.1.0.0-preview.4.nupkg                 │ FOREIGN — belong to
            MicroKit.MediatR{,.Abstractions}.1.0.0-preview.4.nupkg│ other modules, versioned
            MicroKit.Execution.Abstractions.1.0.0-preview.4.nupkg │ by THIS module's tag
            MicroKit.Persistence.EntityFrameworkCore.1.0.0-preview.4.nupkg ┘
                    │
                    ▼
   dotnet nuget push nupkgs/*.nupkg --skip-duplicate               :58-61
                    │
                    ├── MicroKit.Domain.1.0.0-preview.4  →  "already exists at feed"  → SKIPPED
                    └── the other nine  →  "Your package was pushed."   ← 5 of them foreign
```

Every element of the release column is OBSERVED from `release-messaging.yml` and from the log of
run `28295477658`, quoted in §5.4.

---

## 2. Declared versus resolved

### 2.1 The five stages, three projects

Method: evaluate each `.csproj` with `dotnet msbuild -getItem:` in both modes; read stage (c) from
`obj/project.assets.json` after a `CIReleaseBuild=true` restore into an isolated packages folder;
read stage (d) from that folder's `.nupkg.metadata`; read stage (e) from the nuspec of a package
built locally and of the same package downloaded from nuget.org.

**`MicroKit.Messaging` — following `MicroKit.Domain`.**

| Stage | Result | Marker |
|---|---|---|
| (a) declared | **not declared.** `MicroKit.Messaging.csproj`'s only cross-module reference is `MicroKit.Execution.Abstractions` (`:29`/`:32`) | OBSERVED |
| (b) CPM supply | `MicroKit.Domain = 1.0.0-preview.5` (`Directory.Packages.props:24`) | OBSERVED |
| (c) resolved | `project.assets.json` → `MicroKit.Domain/1.0.0-preview.5  type=package`, and it appears under **`centralTransitiveDependencyGroups`** with range `[1.0.0-preview.5, )` | OBSERVED |
| (d) consumed | `<cache>/microkit.domain/1.0.0-preview.5/.nupkg.metadata` → `source: https://api.nuget.org/v3/index.json` | OBSERVED |
| (e) in the `.nupkg` | published `MicroKit.Messaging 1.0.0-preview.4` nuspec declares `<dependency id="MicroKit.Domain" version="1.0.0-preview.5" />` | OBSERVED |

**No stage disagrees on the version.** The finding is of a different kind: `MicroKit.Messaging`
ships a dependency on `MicroKit.Domain` that its own `.csproj` never declares. The three-way test
answers *how*:

1. **Declared direct?** No — absent from the project's `CIReleaseBuild=true` `PackageReference` set.
2. **Transitively pinned?** **Yes** — `Directory.Packages.props:4` sets
   `CentralPackageTransitivePinningEnabled=true`, and `centralTransitiveDependencyGroups` names
   `MicroKit.Domain` and `MicroKit.Result` explicitly. OBSERVED.
3. **Inherited from an intra-module reference?** Yes, that is the origin: `MicroKit.Messaging.csproj:24`
   references `MicroKit.Messaging.Abstractions`, whose own `.csproj:16-23` declares
   **`MicroKit.Domain` in both `CIReleaseBuild` ItemGroups**.

So the ADR-MSG-001 contradiction is not an artefact of transitive pinning; there is a genuine
declared `Messaging → Domain` edge in `MicroKit.Messaging.Abstractions`, and transitive pinning
then promotes it onto `MicroKit.Messaging`'s own manifest. OBSERVED + INFERRED (the causal order
rests on the three observations above).

**`MicroKit.Messaging.MediatR` — following `MicroKit.MediatR`.**

| Stage | Result | Marker |
|---|---|---|
| (a) declared | `ProjectReference ../../../MicroKit.MediatR/src/MicroKit.MediatR/…` when `CIReleaseBuild != 'true'`; `PackageReference MicroKit.MediatR` when `== 'true'` (`:35-42`) | OBSERVED |
| (b) CPM supply | `MicroKit.MediatR = 1.0.0-preview.2` (`Directory.Packages.props:34`) | OBSERVED |
| (c) resolved | `MicroKit.MediatR/1.0.0-preview.2 type=package`; alongside it `MicroKit.Messaging/1.0.0 type=**project**` and `MicroKit.Messaging.Abstractions/1.0.0 type=project` | OBSERVED |
| (d) consumed | downloaded from nuget.org; **that version is `listed=False`** | OBSERVED |
| (e) in the `.nupkg` | published `MicroKit.Messaging.MediatR 1.0.0-preview.4` declares `MicroKit.MediatR 1.0.0-preview.2` | OBSERVED |

**The disagreement is here, and it is at stage (c)/(d), not on a version string.** The published
`MicroKit.MediatR 1.0.0-preview.2` does not contain `IDomainEventsSink`, which the source tree
introduced afterwards, so the compile fails (§8). Within one restore graph, intra-module references
resolve as `type=project` at version `1.0.0` while cross-module ones resolve as `type=package` at a
pinned published version — a mixed graph in which half the code is the working tree and half is a
NuGet snapshot.

**`MicroKit.Execution.Abstractions` — the leaf control.** One project, zero cross-module edges,
zero `MicroKit.*` entries in `dependencies`, `centralPackageVersions`, `libraries` or
`centralTransitiveDependencyGroups`; `Release` and `CIReleaseBuild=true` both succeed. Nothing
disagrees at any stage. OBSERVED.

### 2.2 Every cross-module dependency: symmetry and placement

Derived by **evaluation**, not by reading XML: each project was evaluated twice and the two item
sets differenced (`derived/itemgroup-symmetry.tsv`, `derived/edges-declared.tsv`).

- **27 cross-module reference edges** across the tree — 15 in `src/`, 12 in `tests/`. OBSERVED.
- **17 projects** carry a `CIReleaseBuild` conditional swap. OBSERVED.
- **One-sided declarations: none.** In all 17, the set of package ids implied by the
  `ProjectReference`s that disappear under `CIReleaseBuild=true` equals exactly the set of
  `MicroKit.*` `PackageReference`s that appear. OBSERVED.
- **Conditions on individual items: none.** OBSERVED.
- **Hardcoded `Version=` on a MicroKit `PackageReference`: none.** OBSERVED.
- **Intra-module references inside a `CIReleaseBuild` block — 6, which `.claude/rules/cross-module-references.md`
  rule 6 and `.claude/CLAUDE.md` forbid.** All six are in `MicroKit.Persistence`. OBSERVED:

| Project | Same-module target it swaps for a package |
|---|---|
| `src/MicroKit.Persistence/MicroKit.Persistence.csproj` | `MicroKit.Persistence.Abstractions` |
| `src/MicroKit.Persistence.EntityFrameworkCore/…` | `MicroKit.Persistence` |
| `src/MicroKit.Persistence.EntityFrameworkCore.PostgreSql/…` | `MicroKit.Persistence.EntityFrameworkCore` |
| `src/MicroKit.Persistence.EntityFrameworkCore.SqlServer/…` | `MicroKit.Persistence.EntityFrameworkCore` |
| `src/MicroKit.Persistence.Specifications/…` | `MicroKit.Persistence` |
| `src/MicroKit.Persistence.Testing/…` | `MicroKit.Persistence` |

`Directory.Packages.props:26-28` pins `MicroKit.Persistence`, `.Abstractions` and
`.EntityFrameworkCore` — the three entries that exist to service exactly these six swaps. OBSERVED.

**This defect has already shipped.** The published `persistence-v1.0.0-preview.3` set declares its
own siblings at `preview.2` — a module depending on itself at the previous version, 13 dependency
rows in all. OBSERVED (`derived/nuspec-microkit-deps.tsv`):

```
MicroKit.Persistence                       1.0.0-preview.3  → MicroKit.Persistence.Abstractions 1.0.0-preview.2
MicroKit.Persistence.EntityFrameworkCore   1.0.0-preview.3  → MicroKit.Persistence            1.0.0-preview.2
MicroKit.Persistence.EntityFrameworkCore   1.0.0-preview.3  → MicroKit.Persistence.Abstractions 1.0.0-preview.2
…PostgreSql / …SqlServer / …Specifications / …Testing  1.0.0-preview.3  → the same preview.2 trio
```

Every dependency range declared by every published package resolves to a version that exists on
nuget.org: **0 dangling ranges** across 669 dependency rows (checked case-insensitively, as NuGet
ids are). OBSERVED.

---

## 3. Release mechanism

### 3.1 One workflow end to end — `release-messaging.yml` (61 lines)

Chosen because its `.slnx` carries the largest `/deps/` folder, so the whole mechanism is visible
in one file. Exact command sequence, verbatim, with line numbers — OBSERVED:

```yaml
 3  on:
 4    push:
 5      tags:
 6        - 'messaging-v*'
12        - uses: actions/checkout@v4
14            fetch-depth: 0
16        - uses: actions/setup-dotnet@v4
18            global-json-file: global.json
22        - name: Extract version from tag
23          run: |
24            TAG="${GITHUB_REF#refs/tags/}"
25            PACKAGE_VERSION="${TAG#messaging-v}"
26            echo "PACKAGE_VERSION=$PACKAGE_VERSION" >> "$GITHUB_ENV"
38        - name: Restore
39          run: dotnet restore modules/MicroKit.Messaging/MicroKit.Messaging.slnx -p:CIReleaseBuild=true
41        - name: Build
42          run: dotnet build modules/MicroKit.Messaging/MicroKit.Messaging.slnx --no-restore -c Release -p:CIReleaseBuild=true
44        - name: Test
45          run: dotnet test modules/MicroKit.Messaging/MicroKit.Messaging.slnx --no-build -c Release
47        - name: Pack
49            dotnet pack modules/MicroKit.Messaging/MicroKit.Messaging.slnx \
50              --no-build -c Release \
51              -p:CIReleaseBuild=true \
52              -p:PackageVersion=${{ env.PACKAGE_VERSION }} \
53              -p:AssemblyVersion=$(echo "${{ env.PACKAGE_VERSION }}" | grep -oE '^[0-9]+\.[0-9]+\.[0-9]+').0 \
54              -o nupkgs
56        - name: Push to NuGet.org
58            dotnet nuget push nupkgs/*.nupkg \
59              --api-key ${{ secrets.NUGET_API_KEY }} \
60              --source https://api.nuget.org/v3/index.json \
61              --skip-duplicate
```

Note lines 41-45: **`dotnet test` runs without `-p:CIReleaseBuild=true`, against a tree restored
and built with it, under `--no-build`.** The test step therefore evaluates a different item graph
than the one that was restored. OBSERVED, in all seven release workflows that have a test step.

### 3.2 All nine release workflows

| Workflow | Trigger (tag glob) | `pack` target | `CIReleaseBuild=true` on restore / build / pack | test step | `--no-build` on pack | push glob | `--skip-duplicate` |
|---|---|---|---|---|---|---|---|
| `release-auth.yml` | `auth-v*` | `.slnx` | ✓ / ✓ / ✓ | yes, **without** the flag | ✓ | `nupkgs/*.nupkg` | ✓ |
| `release-domain.yml` | `domain-v*` | `.slnx` | ✓ / ✓ / ✓ | yes, without | ✓ | `nupkgs/*.nupkg` | ✓ |
| `release-execution-abstractions.yml` | `execution-abstractions-v*` | `.slnx` | ✓ / ✓ / ✓ | **absent** | ✓ | `nupkgs/*.nupkg` | ✓ |
| `release-logging.yml` | `logging-v*` | `.slnx` | ✓ / ✓ / ✓ | yes, without | ✓ | `nupkgs/*.nupkg` | ✓ |
| `release-mediatr.yml` | `mediatr-v*` | `.slnx` | ✓ / ✓ / ✓ | yes, without | ✓ | `nupkgs/*.nupkg` | ✓ |
| `release-messaging.yml` | `messaging-v*` | `.slnx` | ✓ / ✓ / ✓ | yes, without | ✓ | `nupkgs/*.nupkg` | ✓ |
| `release-persistence.yml` | `persistence-v*` | `.slnx` | ✓ / ✓ / ✓ | yes, without | ✓ | `nupkgs/*.nupkg` | ✓ |
| `release-result.yml` | `result-v*` | `.slnx` | ✓ / ✓ / ✓ | yes, without | ✓ | `nupkgs/*.nupkg` | ✓ |
| `release-tenancy.yml` | `tenancy-v*` | `.slnx` | ✓ / ✓ / ✓ | yes, without | ✓ | `nupkgs/*.nupkg` | ✓ |

**No release workflow is missing `CIReleaseBuild=true` on restore, build or pack.** OBSERVED —
uniform since `c5618d1 fix(ci): add CIReleaseBuild=true to all release workflows (#51)`.

Every pack targets a **solution**, never a project. Every push uses the same glob. OBSERVED.

**`nupkgs/*.nupkg` does not match `.snupkg`, yet symbol packages are published anyway** — `dotnet
nuget push` uploads the matching `.snupkg` beside each `.nupkg` of its own accord. OBSERVED in
run `28896221653`: 11 `Pushing … .nupkg` lines and 11 `Pushing … .snupkg …/symbolpackage` lines.

### 3.3 Negative inventory

Absent from the tracked tree, each checked by direct path test — OBSERVED:
`.github/CODEOWNERS`, `.github/pull_request_template.md`, `.github/ISSUE_TEMPLATE/`,
`.github/actions/`, `.github/dependabot.yml`, `build/`, `nuget.config`, `eng/`.
Across all 18 workflow files: **no** `workflow_dispatch`, **no** `concurrency`, **no**
`permissions`, **no** GitHub-Release-creating step.

### 3.4 The two earlier release mechanisms, recovered from history

**(a) The pre-modules publisher — `.github/workflows/nuget-publish.yml`, deleted from the tree,
6 runs in history.** Its last content (`git show 02ea37a:.github/workflows/nuget-publish.yml`) —
OBSERVED:

```yaml
on: push: tags: ['v*']
- run: dotnet build MicroKit.slnx -c Release --no-incremental
- run: dotnet test  MicroKit.slnx -c Release --no-build
- id: version
  run: echo "VERSION=${GITHUB_REF_NAME#v}" >> "$GITHUB_OUTPUT"
- name: Pack stable modules
  run: |
    find . -name "*.csproj" -path "*/src/*" ! -path "*/tests/*" ! -path "*Tests*" \
      ! -path "*/Samples/*" ! -path "*/MicroKit.Payments/*" ! -path "*/MicroKit.Resilience/*" \
      ! -path "*/MicroKit.Security/*" ! -path "*/MicroKit.OpenApi/*" \
      | xargs -I{} dotnet pack {} --no-build -c Release -p:Version=${{ steps.version.outputs.VERSION }} --output ./artifacts/
- run: dotnet nuget push "./artifacts/*.nupkg" --skip-duplicate
```

Repository-wide `find`, one version for everything, `--no-build` again, `--skip-duplicate` again.
At `v1.0.0-preview.3` it matched **40 projects**. This is the mechanism behind the 118 pre-modules
publications in §5.

**(b) The pre-fix module release workflows — no version override at all.** Before commit
`299b6dd4 fix(ci): extract package version from tag in all release workflows` (2026-06-01), the
`Pack` step read, verbatim (`git show 87665a92:.github/workflows/release-logging.yml`) — OBSERVED:

```yaml
      - name: Pack
        run: |
          dotnet pack modules/MicroKit.Logging/MicroKit.Logging.slnx \
            --no-build -c Release \
            -o nupkgs
```

No `-p:PackageVersion`. Pack therefore used the MSBuild default and produced **stable `1.0.0`**
packages, which the next step pushed to nuget.org. That is the origin of the 19 accidental
stable releases listed in §5.5. OBSERVED.

---

## 4. Tag inventory

21 tags locally, 20 on `origin`. `del` is local-only (absent from `git ls-remote --tags origin`).
OBSERVED.

**Two kinds of tag, and they do not carry the same evidence.** Five tags are **annotated** and
carry a real tagger date. The other sixteen are **lightweight**: git stores no creation time for
them at all, and `%(creatordate)` silently returns the *commit* date. For those, the tagging
instant is **UNDETERMINED**; the nearest available evidence is the triggered run's `created_at`.

| Tag | Object | Commit | Tagger date (annotated only) | Commit date | Matches a release trigger? | Run(s) |
|---|---|---|---|---|---|---|
| `auth-v1.0.0-preview.1` | **tag** | `1181ee9e` | 2026-06-10T21:00:06+02:00 | 2026-06-10T20:59:08 | `auth-v*` | 27299195029 ✓ |
| `auth-v1.0.0-preview.2` | commit | `a1c175fa` | — | 2026-06-28T05:44:43 | `auth-v*` | 28310411394 ✓ |
| `auth-v1.0.0-preview.3` | commit | `b95d9047` | — | 2026-07-07T22:23:42 | `auth-v*` | 28896221653 ✓ |
| `del` | commit | `e62d8e3a` | — | 2026-06-01T22:06:43 | **no glob matches** | none — OBSERVED negative |
| `domain-v1.0.0-preview.4` | **tag** | `299b6dd4` | 2026-06-01T23:38:50+02:00 | 2026-06-01T23:00:57 | `domain-v*` | 26783636625 ✓ |
| `domain-v1.0.0-preview.5` | commit | `0eace39d` | — | 2026-06-22T17:13:55 | `domain-v*` | 27963418838 ✓ |
| `execution-abstractions-v1.0.0-preview.1` | commit | `dc91521c` | — | 2026-06-25T18:03:26 | `execution-abstractions-v*` | 28183587366 ✓ |
| `logging-v1.0.0-preview.1` | **tag** | `299b6dd4` | 2026-06-01T23:38:50+02:00 | 2026-06-01T23:00:57 | `logging-v*` | 26442594547 ✗, 26442885303 ✓, 26783636690 ✓ |
| `mediatr-v1.0.0-preview.1` | **tag** | `66dfd9cd` | 2026-05-29T19:17:09+02:00 | 2026-05-29T19:16:11 | `mediatr-v*` | 26651558242 ✓ |
| `mediatr-v1.0.0-preview.2` | commit | `13d65485` | — | 2026-06-25T17:23:04 | `mediatr-v*` | 27967830468 ✗, 27977976583 ✗, 28116948538 ✗, 28181021237 ✓ |
| `messaging-v1.0.0-preview.4` | commit | `6dfce33a` | — | 2026-06-27T18:46:03 | `messaging-v*` | 28295477658 ✓ |
| `multitenancy-v1.0.0-preview.1` | commit | `b253f9be` | — | 2026-06-06T21:09:40 | **no current glob matches** | 27071324614 ✓, via the since-deleted `release-multitenancy.yml` |
| `persistence-v1.0.0-preview.1` | **tag** | `299b6dd4` | 2026-06-01T23:01:25+02:00 | 2026-06-01T23:00:57 | `persistence-v*` | 26780223251 ✗, 26780524237 ✓, 26781321841 ✓, 26781829264 ✓ |
| `persistence-v1.0.0-preview.2` | commit | `46a84e1d` | — | 2026-06-22T17:38:30 | `persistence-v*` | 27964904947 ✓ |
| `persistence-v1.0.0-preview.3` | commit | `d1987671` | — | 2026-06-27T18:05:00 | `persistence-v*` | 28293152641 ✗, 28293365667 ✗, 28294453482 ✓ |
| `result-v1.0.0-preview.1` | **tag** | `299b6dd4` | 2026-06-01T23:38:50+02:00 | 2026-06-01T23:00:57 | `result-v*` | 26568905272 ✓, 26783637611 ✓ |
| `result-v1.0.0-preview.2` | commit | `5ec2bf3b` | — | 2026-06-24T18:30:40 | `result-v*` | 28113844476 ✓ |
| `tenancy-v1.0.0-preview.1` | commit | `cba0f844` | — | 2026-06-28T04:22:26 | `tenancy-v*` | 28305480801 ✗, 28308872684 ✓ |
| `v1.0.0-preview.1` | commit | `1584994e` | — | 2026-05-12T22:54:01 | **no `release-*` glob** | 25761625031 ✓, via `nuget-publish.yml` (`v*`) |
| `v1.0.0-preview.2` | commit | `4b1ea66e` | — | 2026-05-12T23:18:25 | **no `release-*` glob** | 25762898505 ✓, via `nuget-publish.yml` |
| `v1.0.0-preview.3` | commit | `335c0a0a` | — | 2026-05-14T13:49:29 | **no `release-*` glob** | 25858419562 ✓, via `nuget-publish.yml` |

All OBSERVED: tag objects from `git for-each-ref`, run association by `head_sha` and `head_branch`
from `gh api repos/michaelatsey/microkit/actions/runs`.

**Four annotated tags point at one commit.** `299b6dd4` carries `domain-v1.0.0-preview.4`,
`logging-v1.0.0-preview.1`, `persistence-v1.0.0-preview.1` and `result-v1.0.0-preview.1`
simultaneously, and four release workflows ran on it within three seconds (21:38:52, :53, :54, plus
persistence at 21:01:31). Commit → tag is therefore one-to-many here; §5.2 states how the chain was
still closed without appealing to version numbers.

**Tags that were moved or deleted.** Run history attests SHAs that no tag points at today, and one
tag that no longer exists at all — git preserves nothing about remote tag movement, so run records
are the only evidence. OBSERVED:

| Tag name | SHAs attested by runs | Tag today points at |
|---|---|---|
| `logging-v1.0.0-preview.1` | `36ec93b3`, `87665a92`, `299b6dd4` | `299b6dd4` |
| `persistence-v1.0.0-preview.1` | `e8cbd7ea`, `5520667e` (×2), `299b6dd4` | `299b6dd4` |
| `mediatr-v1.0.0-preview.2` | `4f3d2200`, `c5618d13`, `5ec2bf3b`, `13d65485` | `13d65485` |
| `persistence-v1.0.0-preview.3` | `cb1028a3`, `ea88cc40`, `d1987671` | `d1987671` |
| `result-v1.0.0-preview.1` | `43f8b61e`, `299b6dd4` | `299b6dd4` |
| `tenancy-v1.0.0-preview.1` | `0dd03589`, `cba0f844` | `cba0f844` |
| `domain-v1.0.0-preview.1` | `46df8a55` | **tag no longer exists** |

Whether any *other* SHA was ever tagged and left no run: **UNDETERMINED** — no artefact records it.

---

## 5. NuGet publication inventory, with demonstrated provenance

### 5.1 How the inventory was built, and the limit of the claim

The NuGet search index only returns packages with at least one **listed** version, so search alone
undercounts. A candidate-id set was assembled from six independent sources — search results, the
`PackageId` evaluated from every `.csproj`, every `Include=` in `Directory.Packages.props`, every
`microkit.*` directory in the machine's NuGet cache, every project name in the pre-modules tree at
`v1.0.0-preview.3`, and mechanical `<Module>.<Suffix>` variants — giving **223 candidates**, each
probed against the flat-container index, which lists unlisted versions too.

- **83 ids returned HTTP 200**, 140 returned 404. OBSERVED.
- Search returned **55**. The other **28 are invisible to search** — every version of each is
  unlisted. OBSERVED.
- Total **232** id/version pairs: **71 listed, 161 unlisted**. OBSERVED
  (registration index, `published` and `listed`; `published = 1900-01-01` marks an unlisted entry).

> **Completeness is INFERRED, never OBSERVED.** A 404 proves that *that id* has no versions now; it
> can never prove that nothing was published under a name the candidate generator failed to guess.
> The generation procedure above is the boundary of the claim.

`LOT.md`'s Observed state says "Nine packages published to NuGet." The measured figure is 83 ids
and 232 versions.

### 5.2 The provenance chain and how each link was established

| Link | Question | Instrument | Result |
|---|---|---|---|
| **L1** package → commit | which source commit produced this `.nupkg`? | `<repository … commit="…">` in the published nuspec | **present in all 232** — OBSERVED |
| **L2** commit → tag(s) | which tags point there? | `git tag --points-at` | 22 distinct commits, all known to the clone |
| **L3** tag → module | which module owns that prefix? | the `on: push: tags` glob table of §3.2 | — |
| **L4** commit → run | which run built it? | `head_sha` match over all 747 runs | every commit resolved |

**Classification actually applied:**

- **205 publications** — exactly one successful publishing run exists at the nuspec commit.
  OBSERVED.
- **19 publications** — several successful runs at the same commit, but only one of them packed a
  solution containing a project whose `PackageId` equals the package id. That test is **structural**
  (which projects the packed `.slnx` contained at that commit), not version-based. This is what
  closed the four-tags-on-`299b6dd4` case: at that commit the four module solutions were
  **disjoint** — `MicroKit.Domain.slnx` held 5 projects, `Logging` 13, `Persistence` 11,
  `Result` 7, with no sibling-module entries — so each package maps to exactly one run.
  OBSERVED.
- **8 publications** — the `MicroKit.Persistence*` stable `1.0.0` set at commit `5520667e`, where
  **two runs of the same workflow** (26780524237 and 26781321841, both `release-persistence.yml`,
  both `persistence-v1.0.0-preview.1`, both successful) could each have pushed it. Workflow, module
  and tag are unambiguous; **only the run id is UNDETERMINED**, and the attribution does not depend
  on it. OBSERVED for module and tag.

**No publication required an inference from a matching version number, and none is UNDETERMINED.**
232 of 232 chains closed.

The May-2026 unprefixed tags are the case that looked hopeless and was not: `v1.0.0-preview.1/2/3`
match **no current** release trigger, but run history retains six runs of the deleted
`.github/workflows/nuget-publish.yml`, whose `on: push: tags: ['v*']` does match them. The three
successful ones (25761625031, 25762898505, 25858419562) account for all 118 pre-modules
publications. Attributing those to a manual `dotnet nuget push` would have been wrong.

### 5.3 The nine foreign publications

**Definition used, stated before counting:** a publication is *foreign* when the release workflow
its chain resolves to belongs to a module other than the one whose `src/` held the project whose
`PackageId` equals the package id, at that same commit. Pre-modules `nuget-publish.yml`
publications are repo-wide by construction and are excluded (counted separately as 118).

| Package | Version | Published under tag | Owning module | Listed today |
|---|---|---|---|---|
| `MicroKit.Result` | `1.0.0-preview.3` | `auth-v1.0.0-preview.3` | MicroKit.Result | **yes** |
| `MicroKit.Result` | `1.0.0-preview.4` | `messaging-v1.0.0-preview.4` | MicroKit.Result | **yes** |
| `MicroKit.Tenancy.Abstractions` | `1.0.0-preview.2` | `auth-v1.0.0-preview.2` | MicroKit.Tenancy | **yes** |
| `MicroKit.Tenancy.Abstractions` | `1.0.0-preview.3` | `auth-v1.0.0-preview.3` | MicroKit.Tenancy | **yes** |
| `MicroKit.Logging.Abstractions` | `1.0.0-preview.2` | `mediatr-v1.0.0-preview.2` | MicroKit.Logging | **yes** |
| `MicroKit.MediatR` | `1.0.0-preview.4` | `messaging-v1.0.0-preview.4` | MicroKit.MediatR | **yes** |
| `MicroKit.MediatR.Abstractions` | `1.0.0-preview.4` | `messaging-v1.0.0-preview.4` | MicroKit.MediatR | **yes** |
| `MicroKit.Execution.Abstractions` | `1.0.0-preview.4` | `messaging-v1.0.0-preview.4` | MicroKit.Execution.Abstractions | **yes** |
| `MicroKit.Persistence.EntityFrameworkCore` | `1.0.0-preview.4` | `messaging-v1.0.0-preview.4` | MicroKit.Persistence | **yes** |

**Nine, and all nine are still listed.** OBSERVED.

Consequences visible on nuget.org today, each OBSERVED:

- `MicroKit.Logging` was only ever tagged `logging-v1.0.0-preview.1`; `MicroKit.Logging.Abstractions
  1.0.0-preview.2` exists **only** because the MediatR release packed and pushed it.
- `MicroKit.Tenancy` was only ever tagged `tenancy-v1.0.0-preview.1`, yet
  `MicroKit.Tenancy.Abstractions` shows `preview.1`, `preview.2` and `preview.3` — the latter two
  from Auth releases.
- The newest **listed** `MicroKit.MediatR` is `1.0.0-preview.4`, produced under the Messaging tag,
  while the module's own `1.0.0-preview.2` is unlisted.

**Does a current CPM pin resolve to a foreign publication? Yes — one.**
`Directory.Packages.props:25` pins `MicroKit.Logging.Abstractions = 1.0.0-preview.2`, which is
foreign and listed. It is not hypothetical: the isolated `CIReleaseBuild=true` restore of
`MicroKit.MediatR` downloaded exactly that package from `https://api.nuget.org/v3/index.json`.
OBSERVED (`derived/stage-d-consumed.txt`).

Two further pins resolve to **unlisted** versions — `MicroKit.MediatR 1.0.0-preview.2` and
`MicroKit.MediatR.Abstractions 1.0.0-preview.2`, plus `MicroKit.Auth.Permissions 1.0.0-preview.2`.
Restore by exact version still succeeds. OBSERVED.

### 5.4 What a release run actually pushed — the manifest evidence

Logs survive for **17 of the 36** publishing runs; the other 19 return **HTTP 410 Gone**, all dated
2026-06-01 or earlier, consistent with a 90-day retention window against today's 2026-09-05. The
cause of the gap is OBSERVED; the manifests themselves are **UNDETERMINED** for those 19 runs.

`release-messaging.yml` run `28295477658`, verbatim from its log — OBSERVED:

```
Successfully created package '…/nupkgs/MicroKit.Messaging.1.0.0-preview.4.nupkg'.
Successfully created package '…/nupkgs/MicroKit.Messaging.Abstractions.1.0.0-preview.4.nupkg'.
Successfully created package '…/nupkgs/MicroKit.Messaging.EntityFrameworkCore.1.0.0-preview.4.nupkg'.
Successfully created package '…/nupkgs/MicroKit.Messaging.MediatR.1.0.0-preview.4.nupkg'.
Successfully created package '…/nupkgs/MicroKit.Domain.1.0.0-preview.4.nupkg'.
Successfully created package '…/nupkgs/MicroKit.Execution.Abstractions.1.0.0-preview.4.nupkg'.
Successfully created package '…/nupkgs/MicroKit.MediatR.1.0.0-preview.4.nupkg'.
Successfully created package '…/nupkgs/MicroKit.MediatR.Abstractions.1.0.0-preview.4.nupkg'.
Successfully created package '…/nupkgs/MicroKit.Persistence.EntityFrameworkCore.1.0.0-preview.4.nupkg'.
Successfully created package '…/nupkgs/MicroKit.Result.1.0.0-preview.4.nupkg'.
…
Package '…/nupkgs/MicroKit.Domain.1.0.0-preview.4.nupkg' already exists at feed 'https://www.nuget.org/api/v2/package'.
Pushing MicroKit.Execution.Abstractions.1.0.0-preview.4.nupkg to 'https://www.nuget.org/api/v2/package'...
Pushing MicroKit.Execution.Abstractions.1.0.0-preview.4.snupkg to 'https://www.nuget.org/api/v2/symbolpackage'...
… (eight more) …
Your package was pushed.
```

Ten packages built from a Messaging tag, four of them Messaging's. One was swallowed by
`--skip-duplicate`; five foreign ones were new and shipped.

Across the 17 retained logs — OBSERVED (`derived/push-summary.tsv`):

| Run | Tag | Packed | Pushed | Skipped as duplicate |
|---|---|---|---|---|
| 27299195029 | `auth-v1.0.0-preview.1` | 11 | 11 | 2 — `MicroKit.Multitenancy.Abstractions`, `MicroKit.Result` @ preview.1 |
| 27963418838 | `domain-v1.0.0-preview.5` | 1 | 1 | 0 |
| 27964904947 | `persistence-v1.0.0-preview.2` | 8 | 8 | 0 |
| 28113844476 | `result-v1.0.0-preview.2` | 2 | 2 | 0 |
| 28181021237 | `mediatr-v1.0.0-preview.2` | 8 | 8 | 3 — `MicroKit.Domain`, `MicroKit.Persistence.Abstractions`, `MicroKit.Result` @ preview.2 |
| 28183587366 | `execution-abstractions-v1.0.0-preview.1` | 1 | 1 | 0 |
| 28294453482 | `persistence-v1.0.0-preview.3` | 8 | 8 | 0 |
| 28295477658 | `messaging-v1.0.0-preview.4` | 10 | 10 | 1 — `MicroKit.Domain` @ preview.4 |
| 28308872684 | `tenancy-v1.0.0-preview.1` | 10 | 10 | 5 — `MicroKit.Domain`, `MicroKit.Persistence`, `.Abstractions`, `.EntityFrameworkCore`, `MicroKit.Result` @ preview.1 |
| 28310411394 | `auth-v1.0.0-preview.2` | 11 | 11 | 1 — `MicroKit.Result` @ preview.2 |
| 28896221653 | `auth-v1.0.0-preview.3` | 11 | 11 | 0 |
| 6 further runs (4 mediatr, 2 persistence, 1 tenancy) | — | 0 | 0 | 0 — failed before Pack |

**12 duplicate pushes silently swallowed in 17 runs.** Each is a foreign publication attempt that
happened to collide with an existing version; where there was no collision — `auth-v…preview.3`
with 0 skips — the foreign package shipped instead.

A concrete case where the collision hid a whole module: `multitenancy-v1.0.0-preview.1` packed 10
projects (5 its own, 5 sibling), yet only 3 new ids appeared on nuget.org — `MicroKit.Multitenancy`
and `.Abstractions` at `preview.1` already existed from the May era under the case-variant id
`MicroKit.MultiTenancy` (NuGet ids are case-insensitive), and `MicroKit.Result`, `MicroKit.Domain`,
`MicroKit.Persistence*` at `preview.1` already existed from the 2026-06-01 releases. Its log is one
of the 19 expired, so the skip lines themselves are UNDETERMINED; the collisions are OBSERVED from
the publication dates of the colliding versions.

### 5.5 The 19 accidental stable `1.0.0` publications

Published between 2026-05-26 and 2026-06-01, i.e. before commit `299b6dd4` added
`-p:PackageVersion`. Every one is **unlisted** today. All native, none foreign. OBSERVED:

`MicroKit.Domain`, `MicroKit.Result`, `MicroKit.Result.AspNetCore`, **`MicroKit.Result.Samples`**,
`MicroKit.Logging` + `.Abstractions` + `.Analyzers` + `.AspNetCore` + `.Diagnostics` + `.Generators`
+ `.OpenTelemetry`, `MicroKit.Persistence` + `.Abstractions` + `.Analyzers` + `.EntityFrameworkCore`
+ `.EntityFrameworkCore.PostgreSql` + `.EntityFrameworkCore.SqlServer` + `.Specifications` +
`.Testing` — 19 packages carrying the MSBuild default version, published as **stable releases** of
a project whose modules are all at `1.0.0-preview.*`.

`MicroKit.Result.Samples` is a `samples/` project. It reached nuget.org at `1.0.0-preview.1` and at
`1.0.0` because it was a member of `MicroKit.Result.slnx` at those commits and carried no
`IsPackable=false` then. It carries one today (`samples/MicroKit.Result.Samples/…:10-11`), so it is
no longer emitted. OBSERVED.

### 5.6 Dependency metadata of published packages

669 dependency rows extracted from all 232 nuspecs; 190 of them are `MicroKit.*` dependencies of
modules-era packages. Compared against the current CPM pins and against what exists on nuget.org:

- **0 dangling ranges** — every declared range resolves to a version that exists. OBSERVED.
- **92 rows match the current CPM pin exactly**, 78 differ, 20 have no pin. Most differences are
  simply historical: a `preview.1` package citing `preview.1` where the pin has since moved.
- The differences that are **not** historical drift are the 13 self-skew rows inside
  `persistence-v1.0.0-preview.3` documented in §2.2 — a package at `preview.3` declaring its own
  module siblings at `preview.2`.
- Every dependency range is an **exact-version lower bound** (`1.0.0-preview.4`, rendered by NuGet
  as `[1.0.0-preview.4, )`); no range in any published MicroKit package is a floating or bracketed
  range. OBSERVED.

Two full examples, OBSERVED from the downloaded nuspecs:

```
MicroKit.Messaging 1.0.0-preview.4
  MicroKit.Messaging.Abstractions  1.0.0-preview.4   = CPM pin
  MicroKit.Execution.Abstractions  1.0.0-preview.1   = CPM pin
  MicroKit.Domain                  1.0.0-preview.5   = CPM pin   ← not declared by the .csproj (§2.1)
  MicroKit.Result                  1.0.0-preview.2   = CPM pin

MicroKit.Messaging.MediatR 1.0.0-preview.4
  MicroKit.Messaging               1.0.0-preview.4   MicroKit.MediatR              1.0.0-preview.2
  MicroKit.Messaging.Abstractions  — (via Messaging)  MicroKit.MediatR.Abstractions 1.0.0-preview.2
  MicroKit.Domain                  1.0.0-preview.5   MicroKit.Execution.Abstractions 1.0.0-preview.1
  MicroKit.Result                  1.0.0-preview.2
```

Note the second: `MicroKit.Messaging.MediatR 1.0.0-preview.4` correctly cites `MicroKit.MediatR
1.0.0-preview.2` — the CPM pin — while the *same run* published `MicroKit.MediatR 1.0.0-preview.4`.
The manifest and the sibling package the run shipped disagree about which MediatR is current.
OBSERVED.

---

## 6. Packable projects

Determined by **evaluation**, not by reading XML: every one of the 91 `.csproj` was evaluated with
`dotnet msbuild -getProperty:IsPackable -getProperty:PackageId …`, in both modes.

- **91 projects; 42 evaluate to `IsPackable=true`.** OBSERVED.
- **`PackageId` is set explicitly in 0 of 91 projects** — every package id is implicit from the
  project file name. OBSERVED.
- Breakdown of the 42: **40 under `src/`**, 1 under `testing/` (`MicroKit.Auth.Testing`), 1 under
  `benchmarks/` (`MicroKit.Domain.Benchmarks`).
- The 49 non-packable: 31 test projects, 12 `MicroKit.Auth.*` Phase-2/3 scaffolds carrying
  `<IsPackable>false</IsPackable>`, 1 sample, and 5 further `src/` projects.
- **`IsPublishable` is applied inconsistently**: of 36 test projects, 14 set it to `false` and 22
  leave it at the default `true`. `IsPackable=false` already prevents packing, so this has no effect
  on packaging. OBSERVED.

**`MicroKit.Domain.Benchmarks` is packable and publishable by default.** It sets only
`<OutputType>Exe</OutputType>` and `<TargetFramework>net10.0</TargetFramework>` — no
`IsPackable=false`, unlike `MicroKit.Logging.PerformanceTests` which pairs `OutputType=Exe` with the
opt-out. It is nonetheless the **one packable project no release could ship**, because it appears in
**no `.slnx` at all**: the root `MicroKit.slnx` lists 90 of the 91 projects and `MicroKit.Domain.slnx`
does not list it either. It is never restored, never built, never CI-verified — and never published
(absent from nuget.org). OBSERVED.

**What each module's release would emit today.** Every module solution was restored, built and
packed to a scratch directory at a probe version:

| Module | `.nupkg` emitted | Its own | **Foreign — other modules' packages, versioned by this module's tag** |
|---|---|---|---|
| MicroKit.Auth | 11 | 9 | **2** — `MicroKit.Result`, `MicroKit.Tenancy.Abstractions` |
| MicroKit.Domain | 1 | 1 | — |
| MicroKit.Execution.Abstractions | 1 | 1 | — |
| MicroKit.Logging | 7 | 7 | — |
| MicroKit.MediatR | 10 | 4 | **6** — `MicroKit.Domain`, `MicroKit.Logging.Abstractions`, `MicroKit.Persistence`, `.Abstractions`, `.EntityFrameworkCore`, `MicroKit.Result` |
| MicroKit.Messaging | 11 | 4 | **7** — `MicroKit.Domain`, `MicroKit.Execution.Abstractions`, `MicroKit.MediatR`, `.Abstractions`, `.Behaviors`, `MicroKit.Persistence.EntityFrameworkCore`, `MicroKit.Result` |
| MicroKit.Persistence | 8 | 8 | — |
| MicroKit.Result | 2 | 2 | — |
| MicroKit.Tenancy | 10 | 5 | **5** — `MicroKit.Domain`, `MicroKit.Persistence`, `.Abstractions`, `.EntityFrameworkCore`, `MicroKit.Result` |

Four of nine modules would emit foreign packages, **20 foreign emissions** in a full release cycle.
41 distinct package ids are emitted in total. OBSERVED.

**Cross-check against nuget.org.** 42 packable ids; 41 published (all but `MicroKit.Domain.Benchmarks`);
**42 published ids have no packable counterpart in the current tree** — the pre-modules families
(`MicroKit.Abstractions`, `.Core`, `.Cqrs*`, `.Data*`, `.Events*`, `.Idempotency*`, `.MultiTenancy*`,
`.EntityFrameworkCore`, `.Domain.Abstractions`, `.Domain.Contracts`, `.Messaging.Core`,
`.Messaging.Persistence.*`, `.Messaging.Transport.*`, `.Caching*`) plus `MicroKit.Result.Samples`.
OBSERVED.

**Caching, Http, Observability.** They contain **no `.csproj`, no `.slnx`, no `version.json`, and
no source file**. `git ls-files` shows exactly one tracked file in each — a **0-byte `README.md`**
dated 2026-05-22. The `src/`, `tests/`, `benchmarks/`, `samples/` and `.claude/` directory trees
exist only on this working copy (git cannot track empty directories) and contain zero files.
Nothing in them is packable because nothing in them exists. OBSERVED.

A fourth directory, `modules/MicroKit.Multitenancy/`, has **no tracked file at all**
(`git ls-files` returns nothing); on disk it holds only `tests/MicroKit.Multitenancy.ArchitectureTests/{bin,obj}`
— build residue from the module renamed to `MicroKit.Tenancy` in `0dd0358`. OBSERVED.

---

## 7. The dependency graph as the files declare it

Extracted from the `.csproj` files by evaluation and collapsed to module level; compared against
the graph documented at `.claude/CLAUDE.md:156-182`.

| Edge | Declared in `src/` | Declared anywhere | In CLAUDE.md |
|---|---|---|---|
| Auth → Result | yes | yes | yes |
| **Auth → Tenancy** | **yes** | yes | **no** |
| Auth → Domain | no | no | **yes** |
| Caching → Result | no | no | **yes** |
| Http → Observability | no | no | **yes** |
| Http → Result | no | no | **yes** |
| MediatR → Domain | yes | yes | yes |
| MediatR → Logging | yes | yes | yes |
| MediatR → Persistence | yes | yes | yes |
| MediatR → Result | yes | yes | yes |
| **Messaging → Domain** | **yes** | yes | **no — CLAUDE.md:171 states the opposite** |
| Messaging → Execution.Abstractions | yes | yes | yes |
| **Messaging → MediatR** | **yes** | yes | **no** |
| Messaging → Persistence | yes | yes | yes |
| Messaging → Result | yes | yes | yes |
| Observability → Logging | no | no | **yes** |
| Observability → Result | no | no | **yes** |
| Persistence → Domain | yes | yes | yes |
| Persistence → Result | yes | yes | yes |
| Tenancy → Auth | no | no | **yes** |
| Tenancy → Execution.Abstractions | no | no | **yes** |
| Tenancy → Persistence | yes | yes | yes |
| Tenancy → Result | yes | yes | yes |

**Declared but not documented — 3:**

- **`Messaging → Domain`.** `.claude/CLAUDE.md:171` states *"ADR-MSG-001: does NOT depend on Domain
  (IIntegrationEvent standalone)"*. The edge is declared in
  `modules/MicroKit.Messaging/src/MicroKit.Messaging.Abstractions/MicroKit.Messaging.Abstractions.csproj:16-23`,
  in **both** `CIReleaseBuild` ItemGroups, and again in
  `…/MicroKit.Messaging.MediatR/…csproj:35-42`. It reaches the published manifest of
  `MicroKit.Messaging` itself through transitive pinning (§2.1). OBSERVED.
- **`Messaging → MediatR`** (`MicroKit.Messaging.MediatR` → `MicroKit.MediatR`). CLAUDE.md:174-175
  authorises `MicroKit.Messaging.MediatR` to reference *MediatR / MediatR.Contracts*, the
  third-party packages; the dependency on the **MicroKit.MediatR module** is not in the graph.
- **`Auth → Tenancy`** (`MicroKit.Auth.Multitenancy` → `MicroKit.Tenancy.Abstractions`). The graph
  documents the reverse direction, `Tenancy → Auth`, which is not declared anywhere.

**Documented but not declared — 8:** `Auth → Domain`, `Tenancy → Auth`,
`Tenancy → Execution.Abstractions`, and the five edges of `Caching`, `Http` and `Observability`,
which have no projects to declare anything.

No cross-module edge in a test project introduces a module-level edge that `src/` does not already
have (15 module edges either way). No circular dependency exists in the declared graph. OBSERVED.

---

## 8. Release build matrix

Run against a `git archive HEAD` export, restore and build as separate steps so a restore failure
can never be mistaken for a compile failure, with an isolated NuGet packages folder.

| Module | `Release` | `Release -p:CIReleaseBuild=true` | First error, verbatim | Resolved package / CPM entry that supplied it |
|---|---|---|---|---|
| MicroKit.Auth | restore 0, build **0** | restore 0, build **0** | — | — |
| MicroKit.Caching | **N/A — no `.slnx`** | N/A | module contains only a 0-byte README | — |
| MicroKit.Domain | 0 / **0** | 0 / **0** | — | — |
| MicroKit.Execution.Abstractions | 0 / **0** | 0 / **0** | — | — |
| MicroKit.Http | **N/A — no `.slnx`** | N/A | as Caching | — |
| MicroKit.Logging | 0 / **0** | 0 / **0** | — | — |
| **MicroKit.MediatR** | 0 / **0** | restore 0, build **1** | `TransactionBehavior.cs(129,42): error CS1061: 'IUnitOfWork' does not contain a definition for 'DiscardChanges'…` (also at 149,42) | `MicroKit.Persistence.Abstractions/1.0.0-preview.3`, package, from `Directory.Packages.props:26` |
| **MicroKit.Messaging** | 0 / **0** | restore 0, build **1** | `OutboxDomainEventSink.cs(44,7): error CS0246: The type or namespace name 'IDomainEventsSink' could not be found…` **plus** the two MediatR `CS1061` above | `MicroKit.MediatR/1.0.0-preview.2` from `:34`; `MicroKit.Persistence.Abstractions/1.0.0-preview.3` from `:26` |
| MicroKit.Observability | **N/A — no `.slnx`** | N/A | as Caching | — |
| MicroKit.Persistence | 0 / **0** | 0 / **0** | — | — |
| MicroKit.Result | 0 / **0** | 0 / **0** | — | — |
| MicroKit.Tenancy | 0 / **0** | 0 / **0** | — | — |

`modules/MicroKit.Multitenancy/` is absent from the export entirely — it has no tracked file — so it
has no row.

**The question the matrix was run to answer.** Trace 025 §6 states that
`MicroKit.Messaging.MediatR` cannot find `IDomainEventsSink` under `CIReleaseBuild=true`. That is
confirmed. It is **not specific to Messaging**: `MicroKit.MediatR` fails independently, in
`MicroKit.MediatR.Behaviors/Pipeline/TransactionBehavior.cs`, because the pinned
`MicroKit.Persistence.Abstractions 1.0.0-preview.3` has no `IUnitOfWork.DiscardChanges` while the
source tree calls it. Both failures are the same shape — a project compiled against a *published*
sibling that predates an API the working tree added — and Messaging inherits MediatR's failure as
well as having its own, because `MicroKit.MediatR.Behaviors` sits in `MicroKit.Messaging.slnx`'s
`/deps/` folder and is built from source there. OBSERVED, 4 × `CS1061` + 2 × `CS0246`.

Error family distribution: **`CS****` only**. No `NU****` — every restore succeeded, in both modes,
for all nine solutions. No analyzer diagnostic was promoted to an error despite Release-only
`TreatWarningsAsErrors` (`Directory.Build.props:7`). OBSERVED.

**Warm-cache control.** Every `CIReleaseBuild=true` restore was repeated against the machine's
default `~/.nuget/packages`, which already held 40 MicroKit package/version pairs including several
unlisted ones. The resolved MicroKit versions were **identical** in both runs; no module depends on
a warm cache to restore. OBSERVED.

**Fidelity limit.** The local SDK is 10.0.111; `global.json` pins 10.0.107 with
`rollForward: latestMinor`, and CI's `setup-dotnet@v4` resolves against whatever the runner offers.
The matrix does not reproduce CI byte-for-byte.

---

## Surprises

Each contradicts `LOT.md`, `.claude/CLAUDE.md` or a session trace, and each was re-established from
artefacts before being written here.

1. **Nerdbank.GitVersioning is not installed, and never has been.** `.claude/CLAUDE.md:220` says
   each module is *"versioned independently via `version.json` (Nerdbank.GitVersioning)"*. There is
   no `PackageReference`, no `PackageVersion`, no `dotnet-tools.json` — in the current tree or in any
   of 287 reachable commits. The nine `version.json` files are read by nothing. A dozen agent and
   skill files describe a computation model that does not execute.
2. **Nine `release-*.yml`, not eight.** `LOT.md:48` and `.claude/CLAUDE.md:193,225` both say eight.
   `ls .github/workflows/` returns 9 CI + 9 release.
3. **83 published package ids and 232 versions, not nine packages.** `LOT.md:49`.
4. **Trace 025 §6's foreign-publication figures do not hold.** It states *"Twelve foreign packages
   have already shipped — `MicroKit.Result` at preview.5 and .6 under `logging-v*` and `tenancy-v*`
   tags when its real release was preview.4; `MicroKit.Domain` at preview.7 likewise."*
   `MicroKit.Result` has **no** `preview.5` or `preview.6` on nuget.org — its versions are
   `preview.1..4` and an unlisted `1.0.0`; `MicroKit.Domain` has no `preview.7`. The measured count
   is **nine** foreign publications, none of them under a `logging-v*` or `tenancy-v*` tag: five
   came from `messaging-v1.0.0-preview.4`, three from `auth-v*`, one from `mediatr-v1.0.0-preview.2`.
   The *mechanism* trace 025 describes is real and is confirmed in §5.4; the figures are not.
5. **Four workflow definitions ran and were then deleted from the tree**, and one of them —
   `nuget-publish.yml`, `on: push: tags: ['v*']`, a repository-wide `find … | xargs dotnet pack` —
   accounts for 118 of the 232 publications. A per-workflow-file run query returns nothing for it;
   only a global run enumeration finds it.
6. **19 stable `1.0.0` packages were published by accident**, between 2026-05-26 and 2026-06-01,
   because the release workflows had no `-p:PackageVersion` before commit `299b6dd4`. All are
   unlisted today. One of them is `MicroKit.Result.Samples` — a `samples/` project.
7. **`-p:AssemblyVersion` in every release workflow has never had any effect.** `pack --no-build`
   cannot reach the compiler; proven by identical SHA256 of the DLL in `bin/` and in the `.nupkg`.
   Every assembly published by this repository claims `AssemblyVersion 1.0.0.0`.
8. **`dotnet test` in the release workflows runs without `CIReleaseBuild=true`** on a tree restored
   and built with it, under `--no-build` — a different item graph than the one that was restored.
   Seven of nine; `release-execution-abstractions.yml` has no test step at all, and neither does its
   CI counterpart, so that package has no test gate on either path.
9. **Six intra-module references sit inside `CIReleaseBuild` blocks**, all in `MicroKit.Persistence`,
   which `.claude/CLAUDE.md` and `.claude/rules/cross-module-references.md` rule 6 forbid. **The
   defect has already shipped**: `persistence-v1.0.0-preview.3` published seven packages whose
   manifests depend on their own module siblings at `preview.2`.
10. **`Messaging → Domain` is declared in the `.csproj`**, contradicting ADR-MSG-001 as cited at
    `.claude/CLAUDE.md:171`, and `Auth → Tenancy` is declared while the graph documents
    `Tenancy → Auth`.
11. **`MicroKit.Domain.Benchmarks` is packable, publishable, and in no solution.** The root
    `MicroKit.slnx` holds 90 of 91 projects.
12. **`.github/CODEOWNERS` and `.github/pull_request_template.md` do not exist**, though
    `.claude/CLAUDE.md:107-108` lists both in the physical structure.
13. **`modules/MicroKit.Multitenancy/` is untracked build residue.** `LOT.md:50-51` names three
    modules without `.claude/`; there are four directories under `modules/` with no solution, and
    the three "unbootstrapped" ones contain a single 0-byte `README.md` each and nothing else — their
    `.claude/` directories exist locally but hold zero files.
14. **A local-only tag `del` exists** on commit `e62d8e3a`, absent from `origin`, matching no
    trigger glob.
15. **`MicroKit.Logging/version.json` reads `"version": "1.0"`** where its eight peers use a full
    pre-release semver — and `modules/MicroKit.Tenancy/.claude/agents/tenancy-release-manager.md:20`
    attributes that `"1.0"` to Tenancy, whose file actually reads `1.0.0-preview.1`.
16. **Three current CPM pins resolve to unlisted versions** (`MicroKit.MediatR` and
    `.Abstractions 1.0.0-preview.2`, `MicroKit.Auth.Permissions 1.0.0-preview.2`), and one resolves
    to a foreign publication (`MicroKit.Logging.Abstractions 1.0.0-preview.2`).
17. **Symbol packages are published**, despite the push glob being `nupkgs/*.nupkg` — `dotnet nuget
    push` uploads the sibling `.snupkg` on its own.

---

## Methodology and evidence

**Read-only guarantee.** No file in the repository was written, no ref created, moved or deleted.
All builds, restores and packs ran against `$SCRATCH/tree`, a `git archive HEAD | tar -x` export
which at extraction contained **1218 files, equal to `git ls-files | wc -l`** (it has since
accumulated `bin/` and `obj/` from the builds run in it, so re-counting now gives a larger figure),
with **no `.git` directory** — so a stray git write-verb there fails rather than acts. This substitution is faithful because
`git status --porcelain --untracked-files=all` was empty at the start (tracked content == HEAD) and
because Nerdbank.GitVersioning is not installed, so nothing in the build reads git. A snapshot of
`HEAD`, `status --porcelain -uall`, and every tag and branch ref was taken before the audit and
re-taken after: **byte-identical**.

One deliberate exception to isolation: the warm-cache control in §8 restored against
`~/.nuget/packages`, which downloads into the user's global NuGet cache. That is a read-mostly
side effect outside the repository.

**Method artefact that must not be mistaken for a finding.** Packages built locally carry
`<repository type="git" url="…"/>` **without a `commit` attribute**, because the export has no
`.git`. Every one of the 232 packages downloaded from nuget.org **does** carry `commit`. The
absence is a property of the method, never of the mechanism.

**Instruments used.**

| Question | Instrument |
|---|---|
| Version properties, packability, reference sets | `dotnet msbuild -getProperty: -getItem:`, each project twice (default and `-p:CIReleaseBuild=true`) — evaluation, not XML reading |
| Resolved versions | `obj/project.assets.json`: `dependencies`, `centralPackageVersions`, `centralTransitiveDependencyGroups`, `libraries` |
| Package actually consumed | `<isolated-cache>/<id>/<ver>/.nupkg.metadata` `source` field |
| Published inventory | `https://api.nuget.org/v3-flatcontainer/<id>/index.json` (includes unlisted) + `registration5-gz-semver2` (`published`, `listed`) |
| Package provenance | `<repository commit>` in each downloaded nuspec, parsed with a namespace-aware XML parser |
| Runs | `gh api --paginate repos/michaelatsey/microkit/actions/runs` — **747 runs, enumerated globally**, never per workflow file, so deleted definitions survive |
| Push manifests | `gh api …/actions/runs/<id>/logs` |
| Tags | `git for-each-ref refs/tags` with `%(objecttype)`, `%(*objectname)`, `%(taggerdate)`; `git ls-remote --tags`; `gh api git/refs/tags` |
| Historical workflow content | `git show <sha>:<path>` |

Every `gh` call was a bare GET (`gh api` with `--jq`/`--paginate` only; no `-f`, `-F` or `--method`).
Every git call was a read verb. `dotnet pack -o` always took an absolute scratch path, never the
workflows' relative `nupkgs`.

**What remains UNDETERMINED, and why.**

1. **Push manifests for 19 of the 36 publishing runs.** Logs return HTTP 410 Gone; all 19 are dated
   2026-06-01 or earlier, against a 90-day retention window. Provenance still closed for every
   package they published, via nuspec commit + run metadata, which do not expire. Only *which files
   each of those runs uploaded, and which `--skip-duplicate` swallowed*, is lost.
2. **Tag creation instants for the 16 lightweight tags.** Git stores none;
   `%(creatordate)` returns the commit date, which is a different fact. The GitHub refs API returns
   no creation time either. The commit date is OBSERVED; the tagging instant is not.
3. **Which of two runs pushed the 8 stable `MicroKit.Persistence*` `1.0.0` packages.** Runs
   26780524237 and 26781321841 are the same workflow, same tag, same commit. Module and tag
   attribution do not depend on the answer.
4. **Whether any SHA was ever tagged and left no run.** Git preserves no remote-tag movement history
   in this clone; run records are the only witness, and they only witness what ran.
5. **Completeness of the published-id inventory.** INFERRED-complete, with the candidate-generation
   procedure of §5.1 as the stated boundary. A fully-unlisted package under a name not guessed would
   be invisible.

**Two things that look UNDETERMINED and are not.** Which run tag `v1.0.0-preview.2` triggered: the
answer is a run of the deleted `nuget-publish.yml`, found by global enumeration — an OBSERVED
positive, where a per-workflow query would have produced a false negative. And whether a given
foreign publication is still listed: the registration `listed` flag answers it directly, with no
dependence on provenance.

**Reproduction.** Every table in this document was rendered from machine-readable intermediates,
not from recollection of command output. They are in the session scratch directory under
`vaudit/derived/`: `packable.tsv`, `edges-declared.tsv`, `itemgroup-symmetry.tsv`,
`intra-module-conditional.tsv`, `graph-diff.tsv`, `publications.tsv`, `nuspec-repository.tsv`,
`nuspec-deps.tsv`, `nuspec-microkit-deps.tsv`, `provenance.tsv`, `foreign.tsv`,
`release-runs.tsv`, `log-availability.tsv`, `push-summary.tsv`, `build-matrix.tsv`,
`packoutput.tsv`, `stage-d-consumed.txt`, plus `raw/` (verbatim API and command output) and
`logs/` (every restore, build and pack log). That directory is outside the repository and is not
durable; the tables above are.
