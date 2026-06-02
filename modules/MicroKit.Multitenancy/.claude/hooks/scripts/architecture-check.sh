#!/bin/bash
# architecture-check.sh — MicroKit.Multitenancy
# Triggered PostToolUse on Edit|Write
# Detects cross-layer violations in the modified file

FILE="${1:-}"
[[ -z "$FILE" ]] && exit 0
[[ "$FILE" != *.cs && "$FILE" != *.csproj ]] && exit 0
[[ "$FILE" != *MicroKit.Multitenancy* ]] && exit 0

VIOLATIONS=0

# Abstractions must not reference EF Core or ASP.NET Core
if [[ "$FILE" == *Abstractions* ]]; then
  if grep -qE 'EntityFrameworkCore|DbContext|IQueryable|HttpContext|IHttpContextAccessor' "$FILE" 2>/dev/null; then
    echo "❌ [architecture-check] Abstractions must not reference EF Core or ASP.NET Core: $FILE"
    VIOLATIONS=$((VIOLATIONS + 1))
  fi
fi

# Core must not reference EF Core or HTTP
if [[ "$FILE" == *src/MicroKit.Multitenancy/* && "$FILE" != *AspNetCore* && "$FILE" != *EntityFrameworkCore* && "$FILE" != *Abstractions* ]]; then
  if grep -qE 'EntityFrameworkCore|DbContext|HttpContext|IHttpContextAccessor' "$FILE" 2>/dev/null; then
    echo "❌ [architecture-check] Core must not reference EF Core or ASP.NET Core HTTP types: $FILE"
    VIOLATIONS=$((VIOLATIONS + 1))
  fi
fi

# ITenantContextAccessor must not be registered as Singleton
if [[ "$FILE" == *.cs ]]; then
  if grep -qE 'AddSingleton.*ITenantContextAccessor|AddSingleton.*TenantContextAccessor' "$FILE" 2>/dev/null; then
    echo "❌ [architecture-check] ITenantContextAccessor must not be registered as Singleton (MKT003): $FILE"
    VIOLATIONS=$((VIOLATIONS + 1))
  fi
fi

# IgnoreQueryFilters without bypass comment
if [[ "$FILE" == *.cs ]]; then
  # Simple check: IgnoreQueryFilters() on a line without a [MTK-BYPASS] comment
  if grep -n 'IgnoreQueryFilters()' "$FILE" 2>/dev/null | grep -qv '\[MTK-BYPASS\]'; then
    echo "⚠️  [architecture-check] IgnoreQueryFilters() without [MTK-BYPASS] comment (MKT002): $FILE"
  fi
fi

exit 0
