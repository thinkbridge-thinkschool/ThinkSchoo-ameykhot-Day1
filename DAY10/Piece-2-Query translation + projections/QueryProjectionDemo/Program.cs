using QueryProjectionDemo;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("═══════════════════════════════════════════════════════════════════");
Console.WriteLine("  Day 10 · Piece 2 — Query Translation + Projections");
Console.WriteLine("  EF Core LogTo · SELECT * vs projected · client-eval catch");
Console.WriteLine("═══════════════════════════════════════════════════════════════════");

// ① Setup & seed ─────────────────────────────────────────────────────────────
Console.WriteLine("\n① Setup & Seed (SQL logging OFF — noise reduction)");
await using (var db = new AppDbContext())
{
    await db.Database.EnsureCreatedAsync();
    await Seeder.SeedAsync(db);
}
Console.WriteLine("  Schema ready.");

// SQL log sink: only print lines that carry the actual query or timing info.
static void SqlLog(string msg) => Console.WriteLine(msg);

// ② DEMO 1 — full entity (SELECT *) ──────────────────────────────────────────
Console.WriteLine("\n② Demo 1 — Full Entity Query");
await using (var db = new AppDbContext(SqlLog))
{
    await Demo1_FullEntityQuery.RunAsync(db);
}

// ③ DEMO 2 — projected query (SELECT 3 cols) ─────────────────────────────────
Console.WriteLine("\n③ Demo 2 — Projected Query");
await using (var db = new AppDbContext(SqlLog))
{
    await Demo2_ProjectedQuery.RunAsync(db);
}

// ④ DEMO 3 — client-side evaluation caught + fixed ───────────────────────────
Console.WriteLine("\n④ Demo 3 — Client-Side Evaluation");
await using (var db = new AppDbContext(SqlLog))
{
    await Demo3_ClientEval.RunAsync(db);
}

Console.WriteLine("\n═══════════════════════════════════════════════════════════════════");
Console.WriteLine("  Done.  See SOLUTION.md for the full submission write-up.");
Console.WriteLine("═══════════════════════════════════════════════════════════════════");
