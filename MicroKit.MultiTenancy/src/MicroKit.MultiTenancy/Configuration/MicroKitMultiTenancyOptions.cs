namespace MicroKit.MultiTenancy.Configuration;

public class MicroKitMultiTenancyOptions
{
    public const string SectionName = "MicroKit:MultiTenancy:Core";

    public string HeaderName { get; set; } = "X-Tenant-Id";
    public string ClaimNames { get; set; } = "tenant_id";

    public HashSet<string> ExemptedPaths { get; set; } = [];
    public bool EnableValidationWorker { get; set; } = true;
}
