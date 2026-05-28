using Microsoft.EntityFrameworkCore;

namespace QueryProjectionDemo;

/// <summary>
/// Demonstrates what happens when EF Core cannot translate a LINQ expression
/// to SQL.  In EF Core 3.0+ this is a hard exception — no silent client-eval.
/// </summary>
public static class Demo3_ClientEval
{
    // This C# method has no SQL equivalent — EF Core cannot translate it.
    private static bool IsHighValue(decimal price) => price > 500m;

    public static async Task RunAsync(AppDbContext db)
    {
        Console.WriteLine(new string('─', 66));
        Console.WriteLine("  DEMO 3 — Accidental Client-Side Evaluation (caught + fixed)");
        Console.WriteLine(new string('─', 66));

        // ── PART A: BAD query ──────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("  [BAD] Using a custom C# method inside .Where()");
        Console.WriteLine("  ─────────────────────────────────────────────────");
        Console.WriteLine("    context.Products");
        Console.WriteLine("           .Where(p => IsHighValue(p.Price))  // not translatable");
        Console.WriteLine("           .Take(5)");
        Console.WriteLine("           .ToListAsync()");
        Console.WriteLine();
        Console.WriteLine("  Attempting bad query...");

        try
        {
            var _ = await db.Products
                .Where(p => IsHighValue(p.Price))
                .Take(5)
                .ToListAsync();

            Console.WriteLine("  (No exception — unexpected)");
        }
        catch (InvalidOperationException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("  CAUGHT InvalidOperationException:");
            // Trim to first 300 chars so the output stays readable
            string msg = ex.Message.Length > 300 ? ex.Message[..300] + "…" : ex.Message;
            Console.WriteLine($"  {msg}");
            Console.ResetColor();
        }

        // ── PART B: FIX ───────────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("  [FIX] Inline the predicate so EF can translate it to SQL");
        Console.WriteLine("  ─────────────────────────────────────────────────");
        Console.WriteLine("    context.Products");
        Console.WriteLine("           .Where(p => p.Price > 500m)  // translatable");
        Console.WriteLine("           .Take(5)");
        Console.WriteLine("           .ToListAsync()");
        Console.WriteLine();
        Console.WriteLine("  ── EF-generated SQL ─────────────────────────────────────");

        var fixed_ = await db.Products
            .Where(p => p.Price > 500m)
            .Take(5)
            .ToListAsync();

        Console.WriteLine("  ─────────────────────────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine($"  Result  : {fixed_.Count} rows returned successfully");
        Console.WriteLine($"  Sample  : [{fixed_[0].ProductId}] {fixed_[0].Name}  ${fixed_[0].Price}");
        Console.WriteLine();
        Console.WriteLine("  Rule    : never call a plain C# method in a .Where() on");
        Console.WriteLine("            IQueryable — use member-access / operators that");
        Console.WriteLine("            EF Core's SQL provider can translate.");
    }
}
