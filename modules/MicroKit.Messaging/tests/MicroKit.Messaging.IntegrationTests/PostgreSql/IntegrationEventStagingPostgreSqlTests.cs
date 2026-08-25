namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

using MicroKit.Messaging.EntityFrameworkCore;

/// <summary>
/// The integration event mapping against a real PostgreSQL server.
/// </summary>
/// <remarks>
/// SQLite proves the staging behaviour; it does not prove the mapping is valid on the provider
/// anyone actually deploys. Creating the schema exercises every column type, length and index name
/// at once — a mapping SQLite tolerates and Npgsql rejects would pass the whole fast suite and fail
/// on first deployment.
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class IntegrationEventStagingPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 24, 9, 14, 22, TimeSpan.Zero);

    [DockerRequiredFact]
    public async Task TheSchemaCreatesAndARowRoundTrips()
    {
        await using var database = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);

        var id = MessageId.New();
        var correlationId = CorrelationId.New();
        var causationId = CausationId.New();
        var occurredOn = Now.AddHours(-3);

        await using (var context = InboxTestDatabase.NewContext(database.ConnectionString))
        {
            var writer = new EfIntegrationEventWriter<TestMessagingDbContext>(context);

            await using var transaction = await context.Database.BeginTransactionAsync();

            writer.HasOpenTransaction.ShouldBeTrue();

            await writer.AddAsync(new IntegrationEventMessage
            {
                Id = id,
                ContractName = "microkit.test.staging-event.v1",
                Source = "/microkit/staging-tests",
                Data = """{"constatId":"a1b2c3d4-0000-0000-0000-000000000001"}""",
                TenantId = "org_7f3a",
                CorrelationId = correlationId,
                CausationId = causationId,
                TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
                CreatedAtUtc = Now,
                OccurredOnUtc = occurredOn,
                Status = IntegrationEventStatus.Pending,
                DeadLettered = false,
                RetryCount = 0,
            });

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var probe = InboxTestDatabase.NewContext(database.ConnectionString);
        var row = await probe.Set<IntegrationEventMessage>().AsNoTracking().SingleAsync();

        row.Id.ShouldBe(id);
        row.ContractName.ShouldBe("microkit.test.staging-event.v1");
        row.Source.ShouldBe("/microkit/staging-tests");
        row.TenantId.ShouldBe("org_7f3a");
        row.CorrelationId.ShouldBe(correlationId);
        row.CausationId.ShouldBe(causationId);
        row.TraceParent.ShouldNotBeNull();
        row.Status.ShouldBe(IntegrationEventStatus.Pending);
        row.DeadLettered.ShouldBeFalse();
        row.RetryCount.ShouldBe(0);
        row.ClaimToken.ShouldBeNull();
        row.ProcessedAtUtc.ShouldBeNull();

        // timestamptz round-trips as UTC; compare the instant, not the offset representation.
        row.CreatedAtUtc.ToUniversalTime().ShouldBe(Now.ToUniversalTime());
        row.OccurredOnUtc!.Value.ToUniversalTime().ShouldBe(occurredOn.ToUniversalTime());
    }

    /// <summary>
    /// The rollback guarantee on the provider that actually enforces transactions.
    /// </summary>
    [DockerRequiredFact]
    public async Task AnUncommittedTransactionLeavesNoRow()
    {
        await using var database = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);

        await using (var context = InboxTestDatabase.NewContext(database.ConnectionString))
        {
            var writer = new EfIntegrationEventWriter<TestMessagingDbContext>(context);

            await using var transaction = await context.Database.BeginTransactionAsync();

            await writer.AddAsync(new IntegrationEventMessage
            {
                Id = MessageId.New(),
                ContractName = "microkit.test.staging-event.v1",
                Source = "/microkit/staging-tests",
                Data = "{}",
                CreatedAtUtc = Now,
                Status = IntegrationEventStatus.Pending,
            });

            await context.SaveChangesAsync();

            // Disposed without CommitAsync: the transaction rolls back.
            await transaction.RollbackAsync();
        }

        await using var probe = InboxTestDatabase.NewContext(database.ConnectionString);
        (await probe.Set<IntegrationEventMessage>().AsNoTracking().ToListAsync()).ShouldBeEmpty();
    }
}
