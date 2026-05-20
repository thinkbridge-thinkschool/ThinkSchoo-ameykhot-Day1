using System.Linq;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderApi.Data;
using OrderApi.Models;
using Xunit;

namespace OrderApi.Tests
{
    public class OrderControllerIntegrationTests : IClassFixture<OrderApiFactory>
    {
        private readonly HttpClient _client;

        public OrderControllerIntegrationTests(OrderApiFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task PostOrder_ReturnsCreatedOrderResponse()
        {
            var request = new OrderRequest
            {
                CustomerId = 1,
                ShippingAddress = "12 Integration Rd",
                Items = new List<OrderItem>
                {
                    new() { ProductId = 1, Quantity = 1 }
                }
            };

            var response = await _client.PostAsJsonAsync("/api/orders", request);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<OrderResponse>();
            Assert.NotNull(body);
            Assert.True(body!.Success);
            Assert.Equal(1, body.CustomerId);
            Assert.Equal(1, body.Items.Count);
        }
    }

    public class OrderApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureDeleted();
                db.Database.EnsureCreated();
                db.Customers.Add(new Customer { Id = 1, FirstName = "Integration", LastName = "Tester", Email = "integration@example.com", TotalOrdersPlaced = 0 });
                db.Products.Add(new Product { Id = 1, Name = "IntegrationProduct", Price = 25, Stock = 5 });
                db.SaveChanges();
            });
        }
    }
}
