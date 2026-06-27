# Command: /audit-tenant-isolation

Detect EF Core tenant isolation violations, query filter bypasses, and cross-tenant leaks.

## Usage
```
/audit-tenant-isolation
```

## What gets checked

### 1. ITenantEntity without query filter
```bash
grep -rn 'ITenantEntity' modules/MicroKit.Tenancy/src/ --include="*.cs"
# Then verify each found entity has a HasQueryFilter registration
```

### 2. IgnoreQueryFilters without justification
```bash
grep -rn 'IgnoreQueryFilters' modules/MicroKit.Tenancy/ --include="*.cs" | grep -v '\[MTK-BYPASS\]'
```

### 3. Nullable TenantId (MKT001)
```bash
grep -rn 'TenantId?' modules/MicroKit.Tenancy/src/ --include="*.cs"
```

### 4. ITenantContextAccessor in Singleton (MKT003)
```bash
grep -rn 'AddSingleton.*TenantContextAccessor\|AddSingleton.*ITenantContextAccessor' \
  modules/MicroKit.Tenancy/ --include="*.cs"
```

### 5. SaveChanges interceptor registration
```bash
grep -rn 'TenantStampInterceptor\|AddInterceptors' \
  modules/MicroKit.Tenancy/src/MicroKit.Tenancy.EntityFrameworkCore/ --include="*.cs"
```

### 6. AsyncLocal in static field
```bash
grep -rn 'static.*AsyncLocal\|AsyncLocal.*static' modules/MicroKit.Tenancy/ --include="*.cs"
```

## Output

For each issue found, report:
- File and line number
- Rule violated (MKT001/MKT002/MKT003 or rule name)
- Suggested fix
