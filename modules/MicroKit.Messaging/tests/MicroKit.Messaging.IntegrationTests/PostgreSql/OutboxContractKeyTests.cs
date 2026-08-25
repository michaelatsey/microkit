namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The replay natural key against a real PostgreSQL server.
/// </summary>
/// <remarks>
/// <para>
/// SQLite proves the constraint is declared; it does not prove it behaves the same way on the
/// provider anyone deploys. The behaviour under test is <b>null distinctness in a unique index</b>,
/// and that is exactly where providers disagree: PostgreSQL and SQLite treat nulls as distinct,
/// SQL Server treats them as equal. The module supports the first two for this constraint and
/// declares SQL Server unsupported — so the PostgreSQL half has to be exercised, not assumed.
/// </para>
/// <para>
/// The first test is also the guard against a plausible future "improvement": PostgreSQL 15
/// introduced <c>NULLS NOT DISTINCT</c>, and turning it on here would reject the second
/// notification row the system ever wrote.
/// </para>
/// </remarks>
[Collection(PostgreSqlSuite.Name)]
public sealed class OutboxContractKeyTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 25, 10, 30, 0, TimeSpan.Zero);

    private const string Contract = "saasbtp.safety.constat-recorded.v1";

    private static OutboxMessage Row(
        MessageKind messageKind = MessageKind.Notification,
        string? contractName = null,
        MessageId? originMessageId = null)
        => new()
        {
            Id = MessageId.New(),
            TenantId = "tenant-a",
            MessageKind = messageKind,
            ContractName = contractName,
            OriginMessageId = originMessageId,
            EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
            Payload = "{}",
            Status = OutboxMessageStatus.Pending,
            OccurredOnUtc = Now,
            CreatedAtUtc = Now,
            CorrelationId = CorrelationId.New(),
        };

    [DockerRequiredFact]
    public async Task Many_notification_rows_with_no_contract_key_coexist()
    {
        await using var database = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);

        await using (var context = InboxTestDatabase.NewContext(database.ConnectionString))
        {
            for (var i = 0; i < 5; i++)
            {
                context.OutboxMessages.Add(Row());
            }

            await context.SaveChangesAsync();
        }

        await using var probe = InboxTestDatabase.NewContext(database.ConnectionString);
        (await probe.OutboxMessages.CountAsync()).ShouldBe(
            5, "nulls are distinct in a PostgreSQL unique index — every notification carries two");
    }

    [DockerRequiredFact]
    public async Task A_second_row_with_the_same_origin_and_contract_is_rejected()
    {
        await using var database = await InboxTestDatabase.CreateAsync(fixture.ConnectionString);

        var origin = MessageId.New();

        await using var context = InboxTestDatabase.NewContext(database.ConnectionString);

        context.OutboxMessages.Add(Row(MessageKind.Contract, Contract, origin));
        await context.SaveChangesAsync();

        context.OutboxMessages.Add(Row(MessageKind.Contract, Contract, origin));

        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
