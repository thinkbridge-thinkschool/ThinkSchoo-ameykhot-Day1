using OrderApi.Models;

namespace OrderApi.Repositories
{
    public interface IOrderRepository
    {
        Task<Customer?> GetCustomerAsync(int customerId, CancellationToken cancellationToken);
        Task<List<Product>> GetProductsByIdsAsync(IEnumerable<int> productIds, CancellationToken cancellationToken);
        Task<Order?> GetOrderByIdAsync(int orderId, CancellationToken cancellationToken);
        Task AddOrderAsync(Order order, CancellationToken cancellationToken);
        Task SaveChangesAsync(CancellationToken cancellationToken);
    }
}
