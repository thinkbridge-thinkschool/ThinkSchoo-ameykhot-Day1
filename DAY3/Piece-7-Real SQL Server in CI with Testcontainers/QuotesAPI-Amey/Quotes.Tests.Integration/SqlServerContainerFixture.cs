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
    // Pinned CU tag avoids instability bugs in rolling "latest"; startup timeout
    // is extended to 5 min so CI runners that pull the image cold don't time out.
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
        .WithStartupTimeout(TimeSpan.FromMinutes(5))
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
