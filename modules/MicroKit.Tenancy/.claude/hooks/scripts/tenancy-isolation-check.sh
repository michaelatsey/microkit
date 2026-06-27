#!/bin/bash
# isolation-check.sh — MicroKit.Tenancy
# Triggered PostToolUse on Edit|Write
# Detects tenant isolation violations (EF Core and AsyncLocal)

FILE="${1:-}"
[[ -z "$FILE" ]] && exit 0
[[ "$FILE" != *.cs ]] && exit 0
[[ "$FILE" != *MicroKit.Tenancy* ]] && exit 0

# Nullable TenantId on ITenantEntity implementation
if grep -qE 'ITenantEntity' "$FILE" 2>/dev/null; then
  if grep -qE 'TenantId\?' "$FILE" 2>/dev/null; then
    echo "❌ [isolation-check] TenantId must not be nullable on ITenantEntity (MKT001): $FILE"
  fi
fi

# AsyncLocal in a static field
if grep -qE 'static.*AsyncLocal|AsyncLocal.*static' "$FILE" 2>/dev/null; then
  echo "❌ [isolation-check] AsyncLocal<T> must not be a static field — use instance field in accessor: $FILE"
fi

exit 0
