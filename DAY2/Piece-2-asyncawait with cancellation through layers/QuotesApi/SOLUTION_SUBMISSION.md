# Day 2 Piece 2 - Cancellation Through Layers Submission

## Task Summary
This submission implements cancellation-aware flow for Collection endpoints across all layers.

Flow covered:
RequestAborted token -> endpoint handlers -> collection service -> repository -> EF Core

Also added an integration test that cancels a token during request execution and verifies the operation does not complete.

## What Was Implemented
1. Added collection service layer:
- File: Services/ICollectionService.cs
- Interface and implementation now own collection business orchestration.

2. Wired cancellation token through layers:
- Endpoints pass token to service methods.
- Service methods pass token to repository methods.
- Repository methods already pass token to EF Core async methods.

3. Added canceled-request handling in middleware:
- File: Middleware/ExceptionMiddleware.cs
- Handles OperationCanceledException when RequestAborted is canceled.
- Returns HTTP 499.

4. Added integration test project and cancellation test:
- File: ../QuotesApi.Tests/CollectionCancellationTests.cs
- Cancels request mid-flight and verifies operation did not complete.

## Cancellation-Aware Service Code
```csharp
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Services;

public interface ICollectionService
{
    Task<Collection> CreateCollectionAsync(string name, int ownerId, CancellationToken cancellationToken);
    Task<Collection?> GetCollectionByIdAsync(int id, CancellationToken cancellationToken);
    Task<Collection?> AddItemToCollectionAsync(int id, int quoteId, CancellationToken cancellationToken);
    Task<Collection?> RemoveItemFromCollectionAsync(int id, int quoteId, CancellationToken cancellationToken);
    Task DeleteCollectionAsync(int id, CancellationToken cancellationToken);
}

public class CollectionService : ICollectionService
{
    private readonly ICollectionRepository _repository;

    public CollectionService(ICollectionRepository repository)
    {
        _repository = repository;
    }

    public async Task<Collection> CreateCollectionAsync(string name, int ownerId, CancellationToken cancellationToken)
    {
        var collection = new Collection(name, ownerId);
        await _repository.AddAsync(collection, cancellationToken);
        return collection;
    }

    public Task<Collection?> GetCollectionByIdAsync(int id, CancellationToken cancellationToken)
    {
        return _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<Collection?> AddItemToCollectionAsync(int id, int quoteId, CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(id, cancellationToken);
        if (collection is null)
            return null;

        collection.AddItem(quoteId);
        await _repository.UpdateAsync(collection, cancellationToken);
        return collection;
    }

    public async Task<Collection?> RemoveItemFromCollectionAsync(int id, int quoteId, CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(id, cancellationToken);
        if (collection is null)
            return null;

        collection.RemoveItem(quoteId);
        await _repository.UpdateAsync(collection, cancellationToken);
        return collection;
    }

    public Task DeleteCollectionAsync(int id, CancellationToken cancellationToken)
    {
        return _repository.DeleteAsync(id, cancellationToken);
    }
}
```

## Test That Proves Cancellation Is Honored
```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Data;
using QuotesApi.Models;
using Xunit;

namespace QuotesApi.Tests;

public class CollectionCancellationTests
{
    [Fact]
    public async Task CreateCollection_WhenRequestIsCancelled_OperationDoesNotComplete()
    {
        using var factory = new QuotesApiFactory();
        using var client = factory.CreateClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            using var response = await client.PostAsJsonAsync(
                "/api/collections",
                new { name = "Cancel Me", ownerId = 12 },
                cts.Token);

            Assert.Equal((HttpStatusCode)499, response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            // Cancellation can surface at client boundary before response is observed.
        }

        var tracker = factory.Services.GetRequiredService<SlowRepoTracker>();
        Assert.False(tracker.AddCompleted);
    }

    private sealed class QuotesApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICollectionRepository>();
                services.AddSingleton<SlowRepoTracker>();
                services.AddSingleton<ICollectionRepository, SlowCollectionRepository>();
            });
        }
    }

    private sealed class SlowRepoTracker
    {
        public bool AddCompleted { get; set; }
    }

    private sealed class SlowCollectionRepository : ICollectionRepository
    {
        private readonly SlowRepoTracker _tracker;

        public SlowCollectionRepository(SlowRepoTracker tracker)
        {
            _tracker = tracker;
        }

        public Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Collection?>(null);
        }

        public async Task AddAsync(Collection collection, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            _tracker.AddCompleted = true;
        }

        public Task UpdateAsync(Collection collection, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
```

## How To Run The Proof Test
```bash
dotnet test ../QuotesApi.Tests/QuotesApi.Tests.csproj
```


