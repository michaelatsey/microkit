using MicroKit.Messaging.IntegrationTests.PostgreSql;
using Microsoft.Extensions.Logging;

namespace MicroKit.Messaging.MediatR.IntegrationTests.PostgreSql;

/// <summary>
/// V4, domain events (#188 D3, D5, D8): with two stores, a notification is enlisted in the store its
/// type's assembly is bound to — so a unit of work that saves another store's context leaves it
/// unsaved, and that is reported (event 4000), never silent.
/// </summary>
[Collection(PostgreSqlSuite.Name)]
public sealed class TwoStoreDomainEventTests(PostgreSqlFixture fixture)
{
    [DockerRequiredFact]
    public async Task ANotificationBoundToStoreB_RaisedInAUnitOfWorkThatSavesStoreA_IsReportedUnsaved()
    {
        await using var host = await PgHost.StartAsync(fixture.ConnectionString, twoStores: true);
        var order = Guid.NewGuid();

        await host.SendAsync(new PgSettleOrderCommand(order));

        // A's unit of work committed its aggregate; the notification went to B's context, unsaved.
        await using (var a = host.NewA())
        {
            (await a.Aggregates.AsNoTracking().CountAsync(x => x.Id == order)).ShouldBe(1);
            (await a.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(0, "the notification is bound to B, not A");
        }

        await using (var b = host.NewB())
        {
            (await b.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(0, "nothing saved B's context");
        }

        var lost = host.Logs.Since(0).Where(e => e.EventId == 4000).ShouldHaveSingleItem();
        lost.Level.ShouldBe(LogLevel.Error);
        lost.RenderedMessage.ShouldContain(nameof(PgOrderSettledNotification));
        lost.RenderedMessage.ShouldContain(PgHost.StoreB.Value);
    }
}
