namespace MicroKit.Persistence.Samples;

public sealed record InvoiceId : EntityId<InvoiceId>
{
    public InvoiceId(Guid value) : base(value) { }
}

public sealed class InvoiceLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Label { get; private set; } = string.Empty;
}

public sealed class Invoice : AggregateRoot<InvoiceId>
{
    private readonly List<InvoiceLine> _lines = [];

    private Invoice(InvoiceId id) : base(id) { }

    public string Number { get; private set; } = string.Empty;

    public IReadOnlyList<InvoiceLine> Lines => _lines;
}

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddDomainConventions(typeof(InvoiceId).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // README: "Soft delete"
        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.Ignore(i => i.DomainEvents);
            invoice.HasSoftDelete(); // the invoice and its lines are marked, never deleted
            invoice.HasMany(i => i.Lines).WithOne().HasForeignKey("InvoiceId").IsRequired(); // Cascade inside the aggregate

            // A soft-deleted invoice still holds its number: filter the unique index on the live rows.
            invoice.HasIndex(i => i.Number).IsUnique().HasFilter("\"AggregateDeletedAt\" IS NULL");
        });
        modelBuilder.Entity<InvoiceLine>().Property(l => l.Id).ValueGeneratedNever();
    }
}

public sealed class InvoiceReads(BillingDbContext context, ISpecificationEvaluator evaluator, PaginationLimits limits)
    : EfReadRepository<Invoice, BillingDbContext>(context, evaluator, limits)
{
    // README: "Soft delete" — the read side, with the soft-deleted invoices too
    public ValueTask<IReadOnlyList<Invoice>> ListAllAsync(CancellationToken ct = default) =>
        ListAsync(new QueryOptions<Invoice>().WithDeleted().WithOrderBy(i => i.Number), ct);
}
