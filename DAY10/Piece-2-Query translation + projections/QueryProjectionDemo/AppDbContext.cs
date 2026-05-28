using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using QueryProjectionDemo.Models;

namespace QueryProjectionDemo;

public class AppDbContext : DbContext
{
    public const string ConnectionString = "Data Source=QueryProjectionDemo.db";

    private readonly Action<string>? _logTo;

    public AppDbContext() { }

    // Pass a write action to capture SQL; filters to command-level log only.
    public AppDbContext(Action<string> logTo) { _logTo = logTo; }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            options.UseSqlite(ConnectionString);

            if (_logTo != null)
                options
                    .LogTo(
                        _logTo,
                        new[] { DbLoggerCategory.Database.Command.Name },
                        LogLevel.Information,
                        DbContextLoggerOptions.None)
                    .EnableSensitiveDataLogging();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.ProductId);
            e.Property(p => p.Name).HasMaxLength(100).IsRequired();
            e.Property(p => p.Category).HasMaxLength(50).IsRequired();
            e.Property(p => p.Price).HasColumnType("decimal(10,2)");
            e.Property(p => p.Description).HasMaxLength(200);
        });
    }
}
