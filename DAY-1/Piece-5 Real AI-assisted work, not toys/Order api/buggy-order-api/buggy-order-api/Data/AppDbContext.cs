using Microsoft.EntityFrameworkCore;
using OrderApi.Models;

namespace OrderApi.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderLineItem> OrderLineItems => Set<OrderLineItem>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>()
                .HasMany(o => o.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .IsRequired();

            modelBuilder.Entity<Order>()
                .Property(o => o.SubTotal)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Order>()
                .Property(o => o.Discount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Order>()
                .Property(o => o.Total)
                .HasPrecision(18, 2);

            modelBuilder.Entity<OrderLineItem>()
                .Property(i => i.LineTotal)
                .HasPrecision(18, 2);
        }
    }
}
