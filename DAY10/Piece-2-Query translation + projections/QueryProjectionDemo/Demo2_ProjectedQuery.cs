using Microsoft.EntityFrameworkCore;
using QueryProjectionDemo.Dtos;

namespace QueryProjectionDemo;

/// <summary>
/// Rewrites Demo 1 with .Select() so EF only fetches the 3 columns the
/// caller actually needs.  Compare the SQL to Demo 1's output.
/// </summary>
public static class Demo2_ProjectedQuery
{
    public static async Task RunAsync(AppDbContext db)
    {
        Console.WriteLine(new string('─', 66));
        Console.WriteLine("  DEMO 2 — Projected Query  (fetches only 3 columns)");
        Console.WriteLine(new string('─', 66));
        Console.WriteLine();
        Console.WriteLine("  C# code:");
        Console.WriteLine("    context.Products");
        Console.WriteLine("           .Where(p => p.IsActive)");
        Console.WriteLine("           .Select(p => new ProductSummaryDto {");
        Console.WriteLine("               ProductId = p.ProductId,");
        Console.WriteLine("               Name      = p.Name,");
        Console.WriteLine("               Price     = p.Price");
        Console.WriteLine("           })");
        Console.WriteLine("           .Take(5)");
        Console.WriteLine("           .ToListAsync()");
        Console.WriteLine();
        Console.WriteLine("  ── EF-generated SQL ─────────────────────────────────────");

        // Projection → EF only emits ProductId, Name, Price in the SELECT list.
        var results = await db.Products
            .Where(p => p.IsActive)
            .Select(p => new ProductSummaryDto
            {
                ProductId = p.ProductId,
                Name      = p.Name,
                Price     = p.Price
            })
            .Take(5)
            .ToListAsync();

        Console.WriteLine("  ─────────────────────────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine($"  Result  : {results.Count} ProductSummaryDto objects returned");
        Console.WriteLine($"  Columns : ProductId, Name, Price  (3 of 7)");
        Console.WriteLine($"  Sample  : [{results[0].ProductId}] {results[0].Name}  ${results[0].Price}");
        Console.WriteLine();
        Console.WriteLine("  Win     : Category, Stock, IsActive, Description are never");
        Console.WriteLine("            read from disk or sent across the network.");
    }
}
