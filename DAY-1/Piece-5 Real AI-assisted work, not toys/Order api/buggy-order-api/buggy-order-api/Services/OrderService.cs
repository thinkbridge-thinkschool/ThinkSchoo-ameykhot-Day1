using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderApi.Models;
using OrderApi.Repositories;

namespace OrderApi.Services
{
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _repository;
        private readonly ILogger<OrderService> _logger;
        private readonly List<IDiscountRule> _discountRules;

        public OrderService(IOrderRepository repository, ILogger<OrderService> logger)
        {
            _repository = repository;
            _logger = logger;
            _discountRules = new List<IDiscountRule>
            {
                new SubtotalDiscountRule(),
                new LoyaltyDiscountRule(),
                new PromoCodeDiscountRule()
            };
        }

        public async Task<OrderResponse> CreateOrderAsync(OrderRequest request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                return OrderResponse.Invalid("Request body is required.");
            }

            if (request.CustomerId <= 0)
            {
                return OrderResponse.Invalid("CustomerId is required.");
            }

            if (request.Items is null || request.Items.Count == 0)
            {
                return OrderResponse.Invalid("At least one order item is required.");
            }

            var customer = await _repository.GetCustomerAsync(request.CustomerId, cancellationToken);
            if (customer is null)
            {
                return OrderResponse.Invalid("Customer not found.");
            }

            var validItems = request.Items
                .Where(item => item.ProductId > 0 && item.Quantity > 0)
                .ToList();

            if (validItems.Count == 0)
            {
                return OrderResponse.Invalid("No valid items were provided.");
            }

            var productIds = validItems.Select(item => item.ProductId).Distinct();
            var products = await _repository.GetProductsByIdsAsync(productIds, cancellationToken);

            if (products.Count == 0)
            {
                return OrderResponse.Invalid("No matching products were found.");
            }

            var outOfStock = new List<string>();
            double subtotal = 0;

            foreach (var item in validItems)
            {
                var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (product is null)
                {
                    continue;
                }

                if (product.Stock < item.Quantity)
                {
                    outOfStock.Add(product.Name);
                    continue;
                }

                subtotal += product.Price * item.Quantity;
            }

            if (outOfStock.Count > 0)
            {
                return OrderResponse.Invalid("Some items are out of stock.", outOfStock);
            }

            var discount = CalculateDiscount(subtotal, request.PromoCode, customer.TotalOrdersPlaced);
            var order = BuildOrder(request, customer, validItems, products, subtotal, discount);

            try
            {
                await _repository.AddOrderAsync(order, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Failed to save order to the database.");
                throw;
            }

            return OrderResponse.FromOrder(order);
        }

        public async Task<OrderResponse?> GetOrderAsync(int orderId, CancellationToken cancellationToken)
        {
            var order = await _repository.GetOrderByIdAsync(orderId, cancellationToken);
            if (order is null)
            {
                return null;
            }

            return OrderResponse.FromOrder(order);
        }

        private double CalculateDiscount(double subtotal, string? promoCode, int totalOrdersPlaced)
        {
            double totalDiscount = 0;

            foreach (var rule in _discountRules)
            {
                totalDiscount += rule.Calculate(subtotal, promoCode, totalOrdersPlaced);
            }

            return totalDiscount;
        }

        private static Order BuildOrder(OrderRequest request, Customer customer, List<OrderItem> validItems, List<Product> products, double subtotal, double discount)
        {
            var shippingAddress = request.ShippingAddress?.Trim() ?? string.Empty;
            var orderLines = validItems
                .Select(item =>
                {
                    var product = products.First(p => p.Id == item.ProductId);
                    return new OrderLineItem
                    {
                        ProductId = item.ProductId,
                        ProductName = product.Name,
                        Quantity = item.Quantity,
                        UnitPrice = product.Price,
                        LineTotal = product.Price * item.Quantity
                    };
                })
                .ToList();

            foreach (var item in orderLines)
            {
                var product = products.First(p => p.Id == item.ProductId);
                product.Stock -= item.Quantity;
            }

            customer.TotalOrdersPlaced += 1;
            customer.LastOrderDate = DateTime.UtcNow;

            return new Order
            {
                CustomerId = request.CustomerId,
                CreatedAt = DateTime.UtcNow,
                Status = OrderStatus.Pending,
                SubTotal = subtotal,
                Discount = discount,
                Total = subtotal - discount,
                ShippingAddress = shippingAddress,
                Notes = request.Notes,
                Items = orderLines
            };
        }
    }
}
