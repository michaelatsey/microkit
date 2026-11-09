using DotNet.Testcontainers.Configurations;
using MicroKit.Persistence.IntegrationTests.Docker;
using MicroKit.Persistence.IntegrationTests.PostgreSql;
using MicroKit.Persistence.IntegrationTests.SqlServer;
using MicroKit.Persistence.Testing;
using MicroKit.Persistence.Testing.PostgreSql;
using MicroKit.Persistence.Testing.SqlServer;
using Microsoft.Data.SqlClient;
using Npgsql;
using Shouldly;
using Xunit;

namespace MicroKit.Persistence.IntegrationTests.Testing;

/// <summary>
/// #197 phase 7 (ADR-015): the published fixtures against real servers. The lifecycle rules themselves are
/// unit-tested over a fake container (<c>ContainerLifecycleTests</c>); these pin what only Docker shows.
/// </summary>
public sealed class DockerProbeAgreementTests
{
    [Fact]
    public void DockerProbe_AgreesWithTestcontainersDiscovery_OnThisMachine()
    {
        // Testcontainers' discovery is non-null only when a daemon it found answered its ping. A disagreement
        // here means the probe would skip tests Testcontainers could run, or the reverse (ADR-015).
        DockerProbe.IsAvailable.ShouldBe(TestcontainersSettings.OS.DockerEndpointAuthConfig is not null);
    }
}

public sealed class RelationalFixtureWithoutDockerTests
{
    [Fact]
    public async Task PostgreSqlFixture_BeforeAStart_RefusesItsConnectionString_AndDisposesAsANoOp()
    {
        var fixture = new PostgreSqlFixture();

        Should.Throw<InvalidOperationException>(() => fixture.ConnectionString).Message.ShouldContain("DockerProbe.IsAvailable");
        Should.Throw<InvalidOperationException>(() => fixture.GetConnectionString("orders"));
        Should.Throw<ArgumentException>(() => fixture.GetConnectionString(" "));
        await fixture.DisposeAsync();
        await fixture.DisposeAsync();
        Should.Throw<ObjectDisposedException>(() => fixture.ConnectionString);
        await Should.ThrowAsync<ObjectDisposedException>(() => fixture.StartAsync().AsTask());
    }

    [Fact]
    public async Task SqlServerFixture_BeforeAStart_RefusesItsConnectionString_AndDisposesAsANoOp()
    {
        var fixture = new SqlServerFixture();

        Should.Throw<InvalidOperationException>(() => fixture.ConnectionString).Message.ShouldContain("DockerProbe.IsAvailable");
        Should.Throw<InvalidOperationException>(() => fixture.GetConnectionString("orders"));
        Should.Throw<ArgumentException>(() => fixture.GetConnectionString(""));
        await fixture.DisposeAsync();
        await fixture.DisposeAsync();
        Should.Throw<ObjectDisposedException>(() => fixture.GetConnectionString("orders"));
        await Should.ThrowAsync<ObjectDisposedException>(() => fixture.StartAsync().AsTask());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TheFixtures_RefuseABlankImage(string? image)
    {
        Should.Throw<ArgumentException>(() => new PostgreSqlFixture(image!));
        Should.Throw<ArgumentException>(() => new SqlServerFixture(image!));
    }

    [Fact]
    public void TheDefaultImages_AreTheOnesTheModuleTestsOn()
    {
        PostgreSqlFixture.DefaultImage.ShouldBe("postgres:17-alpine");
        SqlServerFixture.DefaultImage.ShouldBe("mcr.microsoft.com/mssql/server:2022-latest");
    }
}

[Collection(PostgreSqlSuite.Name)]
public sealed class PostgreSqlFixtureTests(PostgreSqlSuiteFixture suite)
{
    [DockerRequiredFact]
    public async Task ConnectionString_OpensTheServersDefaultDatabase()
    {
        await using var connection = new NpgsqlConnection(suite.Server.ConnectionString);
        await connection.OpenAsync();

        (await new NpgsqlCommand("SELECT current_database()", connection).ExecuteScalarAsync()).ShouldBe("postgres");
    }

    [DockerRequiredFact]
    public async Task GetConnectionString_ChangesOnlyTheDatabase_WhichThenOpens()
    {
        var database = "fixture_" + Guid.NewGuid().ToString("N");
        await using (var server = new NpgsqlConnection(suite.Server.ConnectionString))
        {
            await server.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE {database}", server).ExecuteNonQueryAsync();
        }

        var edited = new NpgsqlConnectionStringBuilder(suite.Server.GetConnectionString(database));
        var original = new NpgsqlConnectionStringBuilder(suite.Server.ConnectionString);
        edited.Database.ShouldBe(database);
        (edited.Host, edited.Port, edited.Username, edited.Password).ShouldBe((original.Host, original.Port, original.Username, original.Password));

        await using var connection = new NpgsqlConnection(edited.ConnectionString);
        await connection.OpenAsync();
        (await new NpgsqlCommand("SELECT current_database()", connection).ExecuteScalarAsync()).ShouldBe(database);
    }

    [DockerRequiredTheory]
    [InlineData("we;ird")]
    [InlineData("a=b")]
    [InlineData("it's")]
    public void GetConnectionString_QuotesANameNpgsqlReadsBack(string database) =>
        new NpgsqlConnectionStringBuilder(suite.Server.GetConnectionString(database)).Database.ShouldBe(database);
}

[Collection(SqlServerSuite.Name)]
public sealed class SqlServerFixtureTests(SqlServerSuiteFixture suite)
{
    [DockerRequiredFact]
    public async Task ConnectionString_OpensMaster()
    {
        await using var connection = new SqlConnection(suite.Server.ConnectionString);
        await connection.OpenAsync();

        (await new SqlCommand("SELECT DB_NAME()", connection).ExecuteScalarAsync()).ShouldBe("master");
    }

    [DockerRequiredFact]
    public async Task GetConnectionString_ChangesOnlyTheDatabase_WhichThenOpens()
    {
        var database = "fixture_" + Guid.NewGuid().ToString("N");
        await using (var server = new SqlConnection(suite.Server.ConnectionString))
        {
            await server.OpenAsync();
            await new SqlCommand($"CREATE DATABASE [{database}]", server).ExecuteNonQueryAsync();
        }

        var edited = new SqlConnectionStringBuilder(suite.Server.GetConnectionString(database));
        var original = new SqlConnectionStringBuilder(suite.Server.ConnectionString);
        edited.InitialCatalog.ShouldBe(database);
        (edited.DataSource, edited.UserID, edited.Password).ShouldBe((original.DataSource, original.UserID, original.Password));

        await using var connection = new SqlConnection(edited.ConnectionString);
        await connection.OpenAsync();
        (await new SqlCommand("SELECT DB_NAME()", connection).ExecuteScalarAsync()).ShouldBe(database);
    }

    [DockerRequiredTheory]
    [InlineData("we;ird")]
    [InlineData("a=b")]
    [InlineData("it's")]
    public void GetConnectionString_QuotesANameSqlClientReadsBack(string database) =>
        new SqlConnectionStringBuilder(suite.Server.GetConnectionString(database)).InitialCatalog.ShouldBe(database);
}

/// <summary>Own instances: a container removed by the dispose, and a start that fails.</summary>
public sealed class RelationalFixtureLifecycleTests
{
    // Pooling off: a pooled connection would reopen without touching the network, the server gone or not.
    [DockerRequiredFact]
    public async Task PostgreSqlFixture_DisposeAsync_RemovesTheServer()
    {
        var fixture = new PostgreSqlFixture();
        await fixture.StartAsync();
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Timeout = 3, Pooling = false }.ConnectionString;
        await using (var connection = new NpgsqlConnection(connectionString))
            await connection.OpenAsync();

        await fixture.DisposeAsync();

        await using var after = new NpgsqlConnection(connectionString);
        await Should.ThrowAsync<Exception>(() => after.OpenAsync());
        Should.Throw<ObjectDisposedException>(() => fixture.ConnectionString);
    }

    [DockerRequiredFact]
    public async Task SqlServerFixture_DisposeAsync_RemovesTheServer()
    {
        var fixture = new SqlServerFixture();
        await fixture.StartAsync();
        var connectionString = new SqlConnectionStringBuilder(fixture.ConnectionString) { ConnectTimeout = 3, Pooling = false }.ConnectionString;
        await using (var connection = new SqlConnection(connectionString))
            await connection.OpenAsync();

        await fixture.DisposeAsync();

        await using var after = new SqlConnection(connectionString);
        await Should.ThrowAsync<Exception>(() => after.OpenAsync());
        Should.Throw<ObjectDisposedException>(() => fixture.ConnectionString);
    }

    [DockerRequiredFact]
    public async Task AStartThatFails_IsRethrownToLaterCalls_AndStillDisposes()
    {
        var fixture = new PostgreSqlFixture("postgres:microkit-no-such-tag");

        var first = await Should.ThrowAsync<Exception>(() => fixture.StartAsync().AsTask());
        var second = await Should.ThrowAsync<Exception>(() => fixture.StartAsync().AsTask());

        second.ShouldBeSameAs(first);
        Should.Throw<InvalidOperationException>(() => fixture.ConnectionString);
        await Should.NotThrowAsync(async () => await fixture.DisposeAsync());
    }
}
