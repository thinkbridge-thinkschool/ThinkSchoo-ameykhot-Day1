using OrderApi.Models;

namespace OrderApi.Services
{
    public interface IOrderService
    {
        Task<OrderResponse> CreateOrderAsync(OrderRequest request, CancellationToken cancellationToken);
        Task<OrderResponse?> GetOrderAsync(int orderId, CancellationToken cancellationToken);
    }
}
