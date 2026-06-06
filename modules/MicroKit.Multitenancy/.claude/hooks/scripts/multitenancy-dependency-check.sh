#!/bin/bash
# dependency-check.sh — MicroKit.Multitenancy
# Triggered PostToolUse on Edit|Write for .csproj files
# Detects CPM violations and forbidden cross-module references

FILE="${1:-}"
[[ -z "$FILE" ]] && exit 0
[[ "$FILE" != *.csproj ]] && exit 0
[[ "$FILE" != *MicroKit.Multitenancy* ]] && exit 0

VIOLATIONS=0

# CPM: no inline Version= on PackageReference
if grep -qE 'PackageReference.*Version=' "$FILE" 2>/dev/null; then
  echo "❌ [dependency-check] Inline Version= on PackageReference — use Directory.Packages.props: $FILE"
  grep -n 'PackageReference.*Version=' "$FILE"
  VIOLATIONS=$((VIOLATIONS + 1))
fi

# FluentAssertions banned
if grep -q 'FluentAssertions' "$FILE" 2>/dev/null; then
  echo "❌ [dependency-check] FluentAssertions is banned (commercial license): $FILE"
  VIOLATIONS=$((VIOLATIONS + 1))
fi

# EF Core in Abstractions or Core
if [[ "$FILE" == *Abstractions* || ( "$FILE" == *src/MicroKit.Multitenancy.csproj* ) ]]; then
  if grep -q 'EntityFrameworkCore' "$FILE" 2>/dev/null; then
    echo "❌ [dependency-check] EF Core must not be referenced in Abstractions or Core: $FILE"
    VIOLATIONS=$((VIOLATIONS + 1))
  fi
fi

[[ $VIOLATIONS -gt 0 ]] && exit 1
exit 0
