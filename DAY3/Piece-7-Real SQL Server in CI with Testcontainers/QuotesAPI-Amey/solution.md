# Piece 7 – Real SQL Server in CI with Testcontainers

## Submission

### Testcontainers Fixture

**`SqlServerContainerFixture.cs`** — one container per xUnit collection:

```csharp
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<SqlServerContainerFixture> { }

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

**`IntegrationTestFactory.cs`** — unique SQL Server database per factory instance:

```csharp
public sealed class IntegrationTestFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public IntegrationTestFactory(string masterConnectionString)
    {
        // Stamp a unique catalog so every test gets an isolated, clean database.
        var csb = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = $"QuotesTest_{Guid.NewGuid():N}"
        };
        _connectionString = csb.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Key"] = TestJwtKey,
                // ...
            });
        });

        builder.ConfigureServices(services =>
        {
            // Swap SQLite → SQL Server (Testcontainers connection string)
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<QuoteDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            services.AddDbContext<QuoteDbContext>(options =>
                options.UseSqlServer(_connectionString));
        });
    }
}
```

**Test class** — receives the shared fixture via constructor injection:

```csharp
[Collection("Integration")]
public sealed class QuoteEndpointTests : IDisposable
{
    private readonly IntegrationTestFactory _factory;

    public QuoteEndpointTests(SqlServerContainerFixture containerFixture)
        => _factory = new IntegrationTestFactory(containerFixture.ConnectionString);

    public void Dispose() => _factory.Dispose();
    // ... 11 tests unchanged
}
```

### `ApplyMigrations()` — SQL Server compatibility

The existing EF Core migrations use SQLite column types (`type: "TEXT"`, `Sqlite:Autoincrement`). Running them against SQL Server fails on Guid columns. The fix: use `EnsureCreated()` for any non-SQLite provider:

```csharp
// ServiceCollectionExtensions.cs
if (dbContext.Database.IsSqlite())
    dbContext.Database.Migrate();   // production: SQLite with full migration history
else
    dbContext.Database.EnsureCreated();  // tests: SQL Server schema from model
```

### GitHub Actions Workflow

```yaml
name: Day 3 – Piece 7 Real SQL Server with Testcontainers

on:
  push:
    paths:
      - "DAY3/Piece-7-Real SQL Server in CI with Testcontainers/**"
      - ".github/workflows/day3-p7-testcontainers.yml"

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "10.0.x" }
      - run: dotnet test --configuration Release --logger "console;verbosity=detailed"
        working-directory: "DAY3/Piece-7-Real SQL Server in CI with Testcontainers/QuotesAPI-Amey/Quotes.Tests.Unit"

  integration-tests:
    runs-on: ubuntu-latest   # ubuntu-latest has Docker pre-installed
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "10.0.x" }
      - run: dotnet test --no-build --configuration Release --logger "console;verbosity=detailed"
        working-directory: "DAY3/Piece-7-Real SQL Server in CI with Testcontainers/QuotesAPI-Amey/Quotes.Tests.Integration"
        env:
          DOCKER_HOST: unix:///var/run/docker.sock
```

`ubuntu-latest` runners have Docker pre-installed. Testcontainers pulls `mcr.microsoft.com/mssql/server:2022-latest` automatically — no `docker-compose` required.

---

## What I Learned

**One container, many databases.** The `ICollectionFixture<SqlServerContainerFixture>` pattern starts SQL Server once for the entire test run. Each `IntegrationTestFactory` stamps a unique `Initial Catalog` (via `SqlConnectionStringBuilder`) so every test still gets a perfectly isolated, empty database — without the 30-second spin-up cost per test.

**Migration portability matters.** SQLite migrations embed provider-specific column types (`type: "TEXT"` for GUIDs, `Sqlite:Autoincrement`). Running them on SQL Server would silently create wrong column types for Guid PKs. The fix — `EnsureCreated()` for non-SQLite — lets the SQL Server provider infer correct types from the CLR model.

**CI Docker is free.** `ubuntu-latest` GitHub Actions runners include the Docker daemon. Testcontainers talks to it via `unix:///var/run/docker.sock` with zero additional setup. First-time image pulls take ~5 minutes; subsequent runs use the Docker layer cache.

---

## What Would Break This

| Scenario | What fails |
|---|---|
| Docker not running locally | Tests throw `Docker daemon not responding` at container start |
| SQL Server image pull timeout | Slow network on first run; mitigation: pre-pull or increase timeout |
| Parallel test runners without unique DB names | Data contamination across tests; the `Guid.NewGuid()` catalog stamp prevents this |
| Running SQLite migrations on SQL Server | `Guid` columns created as `TEXT` instead of `uniqueidentifier`; the `IsSqlite()` guard prevents this |
| Self-hosted CI runners without Docker | Integration tests fail; unit tests still pass on any runner |
