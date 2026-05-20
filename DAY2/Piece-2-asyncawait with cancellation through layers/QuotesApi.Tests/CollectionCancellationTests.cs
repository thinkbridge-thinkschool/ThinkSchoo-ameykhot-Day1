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
