using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderApi.Data;
using OrderApi.Models;
using OrderApi.Repositories;
using OrderApi.Services;
using Xunit;

namespace OrderApi.Tests
{
    public class OrderServiceTests
    {
        private readonly AppDbContext _dbContext;
        private readonly OrderService _service;

        public OrderServiceTests()
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase($"OrderServiceTests_{Guid.NewGuid()}") );
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddScoped<IOrderService, OrderService>();
            services.AddLogging();

            var provider = services.BuildServiceProvider();
            _dbContext = provider.GetRequiredService<AppDbContext>();
            _service = new OrderService(provider.GetRequiredService<IOrderRepository>(), provider.GetRequiredService<ILogger<OrderService>>());

            SeedData();
        }

        [Fact]
        public async Task CreateOrderAsync_AppliesPromoAndLoyaltyDiscount()
        {
            var request = new OrderRequest
            {
                CustomerId = 1,
                ShippingAddress = "123 Main St",
                PromoCode = "SAVE10",
                Items = new List<OrderItem>
                {
                    new() { ProductId = 1, Quantity = 3 },
                    new() { ProductId = 2, Quantity = 2 }
                }
            };

            var response = await _service.CreateOrderAsync(request, CancellationToken.None);

            Assert.True(response.Success);
            Assert.Equal(1, response.CustomerId);
            Assert.True(response.Discount > 0);
            Assert.Equal(5, response.Items.Sum(i => i.Quantity));
        }

        [Fact]
        public async Task CreateOrderAsync_AllowsMissingShippingAddress()
        {
            var request = new OrderRequest
            {
                CustomerId = 1,
                PromoCode = "SAVE10",
                Items = new List<OrderItem>
                {
                    new() { ProductId = 1, Quantity = 1 }
                }
            };

            var response = await _service.CreateOrderAsync(request, CancellationToken.None);

            Assert.True(response.Success);
            Assert.NotNull(response.ShippingAddress);
            Assert.Equal(string.Empty, response.ShippingAddress);
        }

        [Fact]
        public async Task CreateOrderAsync_HandlesSingleValidItemWithoutOffByOne()
        {
            var request = new OrderRequest
            {
                CustomerId = 1,
                ShippingAddress = "10 Test Ave",
                Items = new List<OrderItem>
                {
                    new() { ProductId = 1, Quantity = 1 }
                }
            };

            var response = await _service.CreateOrderAsync(request, CancellationToken.None);

            Assert.True(response.Success);
            Assert.Equal(1, response.Items.Count);
            Assert.True(response.Total > 0);
        }

        // Test: CreateOrderAsync returns invalid when item quantity is negative
        [Fact]
        public async Task CreateOrderAsync_ReturnsInvalidWhenItemQuantityIsNegative()
        {
            var request = new OrderRequest
            {
                CustomerId = 1,
                ShippingAddress = "10 Test Ave",
                Items = new List<OrderItem>
                {
                    new() { ProductId = 1, Quantity = -1 }
                }
            };

            var response = await _service.CreateOrderAsync(request, CancellationToken.None);

            Assert.False(response.Success);
            Assert.Contains("valid items", response.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Test: CreateOrderAsync returns invalid when CustomerId is zero
        [Fact]
        public async Task CreateOrderAsync_ReturnsInvalidWhenCustomerIdIsZero()
        {
            var request = new OrderRequest
            {
                CustomerId = 0,
                ShippingAddress = "10 Test Ave",
                Items = new List<OrderItem>
                {
                    new() { ProductId = 1, Quantity = 1 }
                }
            };

            var response = await _service.CreateOrderAsync(request, CancellationToken.None);

            Assert.False(response.Success);
            Assert.Contains("CustomerId", response.Message);
        }

        // Test: CalculateDiscount applies no discount when subtotal is below 200
        [Fact]
        public async Task CreateOrderAsync_AppliesNoDiscountWhenSubtotalBelowThreshold()
        {
            var request = new OrderRequest
            {
                CustomerId = 2,
                ShippingAddress = "10 Test Ave",
                Items = new List<OrderItem>
                {
                    new() { ProductId = 1, Quantity = 1 }
                }
            };

            var response = await _service.CreateOrderAsync(request, CancellationToken.None);

            Assert.True(response.Success);
            Assert.Equal(0, response.Discount);
        }

        private void SeedData()
        {
            _dbContext.Customers.Add(new Customer { Id = 1, FirstName = "Alice", LastName = "Smith", Email = "alice@example.com", TotalOrdersPlaced = 11 });
            _dbContext.Customers.Add(new Customer { Id = 2, FirstName = "Bob", LastName = "Jones", Email = "bob@example.com", TotalOrdersPlaced = 5 });
            _dbContext.Products.AddRange(
                new Product { Id = 1, Name = "Widget", Price = 20, Stock = 10 },
                new Product { Id = 2, Name = "Gadget", Price = 15, Stock = 10 });
            _dbContext.SaveChanges();
        }
    }
}
