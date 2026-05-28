using Microsoft.EntityFrameworkCore;
using QueryProjectionDemo.Models;

namespace QueryProjectionDemo;

public static class Seeder
{
    private static readonly string[] Categories =
        ["Electronics", "Clothing", "Books", "Food", "Tools", "Sports", "Toys", "Garden"];

    private static readonly string[] Descriptions =
    [
        "High quality product for everyday use",
        "Premium grade item with extended warranty",
        "Budget-friendly option with great value",
        "Professional-grade equipment for experts",
        "Eco-friendly sustainable product"
    ];

    public static async Task SeedAsync(AppDbContext db)
    {
        int existing = await db.Products.CountAsync();
        if (existing >= 1_000)
        {
            Console.WriteLine($"  {existing:N0} rows already present — skipping seed.");
            return;
        }

        Console.WriteLine("  Seeding 1 000 products...");

        var products = Enumerable.Range(1, 1_000).Select(i => new Product
        {
            Name        = $"Product-{i:D5}",
            Category    = Categories[i % Categories.Length],
            Price       = Math.Round(0.99m + (i % 1_000), 2),
            Stock       = i % 500,
            IsActive    = i % 3 != 0,   // ~667 active rows
            Description = Descriptions[i % Descriptions.Length]
        });

        db.Products.AddRange(products);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Console.WriteLine("  Seed complete — 1 000 rows inserted.");
    }
}
