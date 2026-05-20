using Microsoft.EntityFrameworkCore;
using OrderApi.Data;
using OrderApi.Models;

namespace OrderApi.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly AppDbContext _dbContext;

        public OrderRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public Task<Customer?> GetCustomerAsync(int customerId, CancellationToken cancellationToken)
            => _dbContext.Customers
                .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

        public Task<List<Product>> GetProductsByIdsAsync(IEnumerable<int> productIds, CancellationToken cancellationToken)
            => _dbContext.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

        public Task<Order?> GetOrderByIdAsync(int orderId, CancellationToken cancellationToken)
            => _dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        public Task AddOrderAsync(Order order, CancellationToken cancellationToken)
        {
            _dbContext.Orders.Add(order);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
            => _dbContext.SaveChangesAsync(cancellationToken);
    }
}
