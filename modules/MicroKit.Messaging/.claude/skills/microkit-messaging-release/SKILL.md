# Skill: microkit-messaging-release

How to prepare, validate, and execute a MicroKit.Messaging release.

## Tag Convention

```
messaging-v1.0.0-preview.1    → first preview release
messaging-v1.0.0              → stable release
messaging-v1.1.0              → minor feature addition
messaging-v1.0.1              → patch / bug fix
messaging-v2.0.0              → breaking change (v2 providers included)
```

The tag name must follow exactly `messaging-v{semver}` — the CI release workflow extracts
the version from the tag via regex `messaging-v(.+)`.

## Release Workflow

```
1. Feature work complete on dev branch
2. Run pre-release checklist (microkit-messaging-release-manager agent)
3. Create release branch from dev:
     git checkout dev && git pull
     git checkout -b release/messaging/1.0.0-preview.1

4. Update CHANGELOG.md
5. Verify version.json is correct
6. Commit: chore(messaging): prepare release 1.0.0-preview.1
7. PR: release/messaging/1.0.0-preview.1 → main
8. Merge (fast-forward only — no merge commit)
9. Tag on main:
     git tag messaging-v1.0.0-preview.1
     git push origin messaging-v1.0.0-preview.1
10. Back-merge main → dev:
     git checkout dev && git merge main && git push origin dev
```

**Never execute git push or tag commands** — produce them for the human.

## CI Release Workflow Trigger

Pushing `messaging-v*` tag triggers `.github/workflows/release-messaging.yml`:
```yaml
on:
  push:
    tags:
      - 'messaging-v*'
```

The workflow runs `dotnet pack -p:CIReleaseBuild=true -p:PackageVersion=${{ env.VERSION }}` where
`VERSION` is extracted from the tag name.

## Pre-Release Verification

```bash
# 1. All tests pass
dotnet test modules/MicroKit.Messaging/MicroKit.Messaging.slnx --no-build -c Release

# 2. Zero MediatR.Contracts references
grep -r "MediatR.Contracts" modules/MicroKit.Messaging/ --include="*.csproj" --include="*.cs"
# must return no output

# 3. Zero FluentAssertions references
grep -r "FluentAssertions" modules/MicroKit.Messaging/ --include="*.csproj" --include="*.cs"
# must return no output

# 4. Release build is clean
dotnet build modules/MicroKit.Messaging/MicroKit.Messaging.slnx -c Release
# must show: Build succeeded. 0 Error(s). 0 Warning(s).

# 5. v2 scaffolds do not produce packages
dotnet pack modules/MicroKit.Messaging/src/MicroKit.Messaging.RabbitMQ/ -c Release -o /tmp
ls /tmp/MicroKit.Messaging.RabbitMQ.*.nupkg 2>/dev/null && echo "FAIL: should not produce nupkg"

# 6. Verify version.json
cat modules/MicroKit.Messaging/version.json
```

## Published Packages (Phase 1)

```
MicroKit.Messaging.Abstractions
MicroKit.Messaging
MicroKit.Messaging.EntityFrameworkCore
MicroKit.Messaging.Testing
```

## NOT Published (Phase 2 scaffolds, IsPackable=false)

```
MicroKit.Messaging.RabbitMQ
MicroKit.Messaging.AzureServiceBus
MicroKit.Messaging.Kafka
MicroKit.Messaging.OpenTelemetry
MicroKit.Messaging.Serialization
```

## version.json

```json
{
  "version": "1.0",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/messaging-v\\d+\\.\\d+\\.\\d+"
  ]
}
```
