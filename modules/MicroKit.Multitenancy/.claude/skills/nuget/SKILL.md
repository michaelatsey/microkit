# Skill: NuGet — MicroKit.Multitenancy

## Check package versions in CPM
```bash
grep -A2 'Label="Multitenancy"' /home/cryxtol/workspace/libraries/microkit/Directory.Packages.props
```

## Add a new package dependency

1. Check it isn't already declared in `Directory.Packages.props`
2. Add under the correct label group:
   ```xml
   <PackageVersion Include="SomePackage" Version="1.2.3" />
   ```
3. Reference from `.csproj` without `Version=`:
   ```xml
   <PackageReference Include="SomePackage" />
   ```
4. Run `dotnet restore` to verify resolution
5. Commit: `chore(monorepo): add SomePackage to Directory.Packages.props`

## Verify CPM compliance (no inline versions)
```bash
grep -rn 'Version=' modules/MicroKit.Multitenancy/src/ --include="*.csproj"
# Expected: no output (all versions in Directory.Packages.props)
```
