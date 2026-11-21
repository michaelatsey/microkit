namespace MicroKit.Messaging.IntegrationTests.PostgreSql;

/// <summary>
/// The CHANGELOG's PostgreSQL statements for the consumer-key change (#188), read from the CHANGELOG
/// itself, rewrite assembly-qualified keys into exactly the keys <see cref="MessageConsumerKey"/>
/// builds — generic handlers included — over a message already split across two versions, and can
/// run again after a rolling deployment.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class ConsumerKeyMigrationTests(PostgreSqlFixture fixture)
{
    private const string Marker = "-- InboxMessages.ConsumerType: the version-free consumer key";

    [DockerRequiredFact]
    public async Task TheChangelogStatements_RewriteEveryKey_IntoTheKeyTheCodeBuilds_EvenForAMessageSplitAcrossVersions()
    {
        await using var db = await StoreDatabase.CreateAsync<TestMessagingDbContext>(fixture.ConnectionString, o => new(o));
        var migration = ChangelogMigration();

        // One message redelivered across a version bump: a row per version for one handler.
        var split = MessageId.New();
        const string V1 = "Shop.Orders.OrderPlacedHandler, Shop.Orders, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
        const string V2 = "Shop.Orders.OrderPlacedHandler, Shop.Orders, Version=1.1.0.0, Culture=neutral, PublicKeyToken=null";
        var now = DateTimeOffset.UtcNow;
        await using (var context = NewContext(db))
        {
            context.InboxMessages.AddRange(
                Row(MessageId.New(), typeof(ConsumerKeyMigrationTests).AssemblyQualifiedName!, InboxMessageStatus.Processed, now),
                Row(MessageId.New(), typeof(List<ConsumerKeyMigrationTests>).AssemblyQualifiedName!, InboxMessageStatus.Processed, now),
                Row(split, V1, InboxMessageStatus.Processed, now.AddMinutes(-10)),
                Row(split, V2, InboxMessageStatus.Failed, now));
            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlRawAsync(migration);
            (await context.Database.ExecuteSqlRawAsync(migration)).ShouldBe(0, "a second run, after a rolling deployment, changes nothing");
        }

        await using var read = NewContext(db);
        var rows = await read.InboxMessages.AsNoTracking().ToListAsync();
        rows.Select(r => r.ConsumerType).ShouldContain(MessageConsumerKey.Of(typeof(ConsumerKeyMigrationTests)));
        rows.Select(r => r.ConsumerType).ShouldContain(MessageConsumerKey.Of(typeof(List<ConsumerKeyMigrationTests>)));
        var kept = rows.Where(r => r.MessageId == split).ShouldHaveSingleItem("the split pair collapses onto one key");
        kept.ConsumerType.ShouldBe("Shop.Orders.OrderPlacedHandler, Shop.Orders");
        kept.Status.ShouldBe(InboxMessageStatus.Processed, "the Processed row is kept: the handler ran");
        rows.Count.ShouldBe(3);
    }

    /// <summary>The consumer-key block of the CHANGELOG's migration, verbatim.</summary>
    private static string ChangelogMigration()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MicroKit.Messaging.slnx")))
        {
            dir = dir.Parent;
        }

        var changelog = File.ReadAllText(Path.Combine(dir!.FullName, "CHANGELOG.md")).ReplaceLineEndings("\n");
        var start = changelog.IndexOf(Marker, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, "the CHANGELOG gives the consumer-key migration");
        var end = changelog.IndexOf("\n```", start, StringComparison.Ordinal);
        return changelog[start..end];
    }

    private static InboxMessage Row(MessageId id, string consumer, InboxMessageStatus status, DateTimeOffset receivedAt) => new()
    {
        RowId = Guid.NewGuid(),
        MessageId = id,
        ConsumerType = consumer,
        EventType = "MicroKit.Test.TestEvent, MicroKit.Test",
        Payload = "{}",
        Status = status,
        ReceivedAtUtc = receivedAt,
    };

    private static TestMessagingDbContext NewContext(StoreDatabase db)
        => new(new DbContextOptionsBuilder<TestMessagingDbContext>().UseNpgsql(db.ConnectionString).Options);
}
