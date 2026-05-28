using Microsoft.EntityFrameworkCore;

namespace QueryProjectionDemo;

/// <summary>
/// Shows the SQL EF generates when you load a full entity — all 7 columns travel
/// over the wire even though the caller only needs 3 of them.
/// </summary>
public static class Demo1_FullEntityQuery
{
    public static async Task RunAsync(AppDbContext db)
    {
        Console.WriteLine(new string('─', 66));
        Console.WriteLine("  DEMO 1 — Full Entity Query  (fetches ALL 7 columns)");
        Console.WriteLine(new string('─', 66));
        Console.WriteLine();
        Console.WriteLine("  C# code:");
        Console.WriteLine("    context.Products");
        Console.WriteLine("           .Where(p => p.IsActive)");
        Console.WriteLine("           .Take(5)");
        Console.WriteLine("           .ToListAsync()");
        Console.WriteLine();
        Console.WriteLine("  ── EF-generated SQL ─────────────────────────────────────");

        // No projection → EF selects every column on the Products table.
        var results = await db.Products
            .Where(p => p.IsActive)
            .Take(5)
            .ToListAsync();

        Console.WriteLine("  ─────────────────────────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine($"  Result  : {results.Count} Product objects returned");
        Console.WriteLine($"  Columns : ProductId, Name, Category, Price, Stock, IsActive, Description");
        Console.WriteLine($"  Sample  : [{results[0].ProductId}] {results[0].Name}  " +
                          $"${results[0].Price}  active={results[0].IsActive}");
        Console.WriteLine($"            desc=\"{results[0].Description}\"");
        Console.WriteLine();
        Console.WriteLine("  Problem : Description and Stock travelled over the wire");
        Console.WriteLine("            but the caller never uses them.");
    }
}
