# Command: /audit-tenant-isolation

Detect EF Core tenant isolation violations, query filter bypasses, and cross-tenant leaks.

## Usage
```
/audit-tenant-isolation
```

## What gets checked

### 1. ITenantEntity without query filter
```bash
grep -rn 'ITenantEntity' modules/MicroKit.Multitenancy/src/ --include="*.cs"
# Then verify each found entity has a HasQueryFilter registration
```

### 2. IgnoreQueryFilters without justification
```bash
grep -rn 'IgnoreQueryFilters' modules/MicroKit.Multitenancy/ --include="*.cs" | grep -v '\[MTK-BYPASS\]'
```

### 3. Nullable TenantId (MKT001)
```bash
grep -rn 'TenantId?' modules/MicroKit.Multitenancy/src/ --include="*.cs"
```

### 4. ITenantContextAccessor in Singleton (MKT003)
```bash
grep -rn 'AddSingleton.*TenantContextAccessor\|AddSingleton.*ITenantContextAccessor' \
  modules/MicroKit.Multitenancy/ --include="*.cs"
```

### 5. SaveChanges interceptor registration
```bash
grep -rn 'TenantStampInterceptor\|AddInterceptors' \
  modules/MicroKit.Multitenancy/src/MicroKit.Multitenancy.EntityFrameworkCore/ --include="*.cs"
```

### 6. AsyncLocal in static field
```bash
grep -rn 'static.*AsyncLocal\|AsyncLocal.*static' modules/MicroKit.Multitenancy/ --include="*.cs"
```

## Output

For each issue found, report:
- File and line number
- Rule violated (MKT001/MKT002/MKT003 or rule name)
- Suggested fix
