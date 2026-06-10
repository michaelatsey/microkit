# MicroKit — Module Development Flow

## PRE-IMPLEMENTATION Phase (per package)

1. Create branch: `git checkout -b feature/<module>/<package>`
2. Activate Plan Mode manually in Claude Code (native Plan Mode ≠ `/plan` command)
3. Run `/plan` using the implementer prompt (inline specs — no dedicated per-package plan file)
4. Architect review via option 4 "Tell Claude what to change"
5. `/compact`
6. Implementation (auto mode)

## POST-IMPLEMENTATION Phase (per package)

7. Post-code agents (same session, fixed order):
   - `distributed-context-specialist` — if AsyncLocal / context propagation involved
   - `dependency-guardian` — if any `.csproj` modified
   - `api-reviewer` — if any public API changed
   - All prompts must include `Do not commit anything`
8. `dotnet build MicroKit.slnx --configuration Release` → 0 errors, 0 warnings
9. Verify `MicroKit.slnx` is up to date (all new projects registered)
10. Commit + push + PR → dev (squash merge)

## RELEASE Phase (after all packages in the module are complete)

11. `git checkout dev && git pull`
12. Final architect review — on dev
13. If findings: fix on feature branch → merge to dev → back to 12
14. microkit-<module>-release-manager — pre-release checklist on dev
15. `git checkout -b release/<module>-<version>`
16. PR release/<module>-<version> → main
17. Merge PR → main
18. `git checkout main && git pull`
19. `git tag <module>-v<semver> -m "<Module> <semver>"`
20. `git push origin <module>-v<semver>` → triggers release-<module>.yml
21. Verify CI release is green on GitHub
22. Back-merge: `git checkout dev && git merge main --ff-only && git push origin dev`
23. `git branch -d release/<module>-<version>`
