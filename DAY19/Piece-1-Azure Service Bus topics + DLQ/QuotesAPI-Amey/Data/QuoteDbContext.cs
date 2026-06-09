using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuoteDbContext : DbContext
{
    public QuoteDbContext(DbContextOptions<QuoteDbContext> options) : base(options) { }

    public DbSet<Quote> Quotes { get; set; }
    public DbSet<Author> Authors { get; set; }
    public DbSet<Collection> Collections { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Author>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
        });

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Author).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Text).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.OwnerId).IsRequired(false);
            entity.Property(e => e.AuthorId).IsRequired(false);
            // Index added as fix — eliminates full table scan on every per-author query
            entity.HasIndex(e => e.AuthorId).HasDatabaseName("IX_Quotes_AuthorId");
            // FK relationship — enables .Include(a => a.Quotes) on the fast endpoint
            entity.HasOne<Author>()
                  .WithMany(a => a.Quotes)
                  .HasForeignKey(e => e.AuthorId)
                  .IsRequired(false);
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
