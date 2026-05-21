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
    // Cold-pull latency is handled by the "Pre-pull SQL Server image" CI step,
    // so the default Testcontainers startup timeout is sufficient here.
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
