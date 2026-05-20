using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuoteDbContext : DbContext
{
    public QuoteDbContext(DbContextOptions<QuoteDbContext> options) : base(options) { }

    public DbSet<Quote> Quotes { get; set; }
    public DbSet<Collection> Collections { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Author).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Text).IsRequired().HasMaxLength(2000);
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(80);
            entity.Property(e => e.OwnerId).IsRequired();

            // CollectionItem is a value object stored as an owned type
            entity.OwnsMany<CollectionItem>("_items", items =>
            {
                items.WithOwner().HasForeignKey("CollectionId");
                items.Property(i => i.QuoteId).IsRequired();
                items.Property(i => i.AddedAt).IsRequired();
                items.HasKey("CollectionId", "QuoteId");
            });
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired();
            entity.Property(e => e.Family).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ExpiresAt).IsRequired();

            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.Family);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
