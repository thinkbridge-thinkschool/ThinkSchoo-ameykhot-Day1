using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace Quotes.Tests.Integration;

// One SQL Server container is shared across all tests in the "Integration" collection.
// Each IntegrationTestFactory creates its own uniquely-named database on this container,
// so every test still gets a clean, isolated schema.
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<SqlServerContainerFixture> { }

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    // Pinned CU tag avoids instability bugs in the rolling "latest" image.
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Testcontainers' default wait strategy for MsSql only checks that
        // port 1433 is open — but SQL Server opens the port ~10 s before it
        // is ready to accept queries. Poll until a real connection succeeds
        // (typically 30-60 s after container start) so no test fails with a
        // "login timeout" on the very first EnsureCreated() call.
        await WaitForSqlServerReadyAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private async Task WaitForSqlServerReadyAsync()
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var con = new SqlConnection(ConnectionString);
                await con.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(2_000);
            }
        }
        throw new InvalidOperationException(
            "SQL Server did not accept connections within 3 minutes of container start.");
    }
}
