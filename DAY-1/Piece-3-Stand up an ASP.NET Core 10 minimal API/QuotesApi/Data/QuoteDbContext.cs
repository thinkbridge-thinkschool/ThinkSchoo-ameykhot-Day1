using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuoteDbContext : DbContext
{
    public QuoteDbContext(DbContextOptions<QuoteDbContext> options) : base(options) { }

    public DbSet<Quote> Quotes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Author).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Text).IsRequired().HasMaxLength(2000);
        });
    }
}
