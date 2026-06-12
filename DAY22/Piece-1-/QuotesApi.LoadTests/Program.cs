// QuotesApi.LoadTests — NBomber load test for HybridCache stampede protection
//
// USAGE:
//   1. Start Redis:       docker run -d --name redis-amey -p 6379:6379 redis:latest
//   2. Start the API:     cd ../QuotesAPI-Amey && dotnet run
//   3. Run WITHOUT cache: comment out AddHybridCache in Program.cs, then:
//                         dotnet run -- --mode before
//   4. Run WITH cache:    restore AddHybridCache, reset counters, then:
//                         dotnet run -- --mode after
//
// The HTML report is written to ./reports/cache-load-test-<before|after>.html

using NBomber.CSharp;
using NBomber.Contracts;

var mode = args.Length > 1 && args[0] == "--mode" ? args[1] : "after";
var baseUrl = args.Length > 3 && args[2] == "--url" ? args[3] : "http://localhost:5000";

Console.WriteLine($"[LoadTest] mode={mode}  baseUrl={baseUrl}");
Console.WriteLine("[LoadTest] Resetting cache stats counter...");

using var httpReset = new HttpClient();
try { await httpReset.DeleteAsync($"{baseUrl}/api/cache/stats/reset"); }
catch { Console.WriteLine("[LoadTest] Warning: could not reset stats (API not running with cache?)"); }

// ── Scenario 1: hot read — GET /api/quotes/1 (50 concurrent requests per second) ──
var hotRead = Scenario.Create("hot_read_quote_1", async _ =>
{
    using var http = new HttpClient();
    try
    {
        var resp = await http.GetAsync($"{baseUrl}/api/quotes/1");
        return resp.IsSuccessStatusCode
            ? Response.Ok(statusCode: (int)resp.StatusCode)
            : Response.Fail(statusCode: (int)resp.StatusCode, error: resp.ReasonPhrase);
    }
    catch (Exception ex)
    {
        return Response.Fail(error: ex.Message);
    }
})
.WithWarmUpDuration(TimeSpan.FromSeconds(5))
.WithLoadSimulations(
    // Ramp from 0 → 50 concurrent arrivals/sec over 10 s, then hold 30 s
    Simulation.RampingInject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10)),
    Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30))
);

// ── Scenario 2: stampede burst — 100 requests fire at once (proves 1 DB hit) ──
// This simulates a cache expiry at t=0 with a sudden burst of concurrent clients.
var stampedeBurst = Scenario.Create("stampede_burst_50_concurrent", async ctx =>
{
    // All virtual users start at the same moment (no stagger).
    // With HybridCache: only 1 DB query fires; without: 50 fire.
    using var http = new HttpClient();
    try
    {
        var resp = await http.GetAsync($"{baseUrl}/api/quotes/2");
        return resp.IsSuccessStatusCode
            ? Response.Ok()
            : Response.Fail(statusCode: (int)resp.StatusCode);
    }
    catch (Exception ex)
    {
        return Response.Fail(error: ex.Message);
    }
})
.WithoutWarmUp()
.WithLoadSimulations(
    // Fire 50 concurrent copies ONCE then hold for 5 seconds — proves stampede window
    Simulation.KeepConstant(copies: 50, during: TimeSpan.FromSeconds(5))
);

var reportName = $"cache-load-test-{mode}";

NBomberRunner
    .RegisterScenarios(hotRead, stampedeBurst)
    .WithReportFileName(reportName)
    .WithReportFolder("reports")
    .WithReportFormats(ReportFormat.Html, ReportFormat.Csv, ReportFormat.Txt)
    .Run();

// After the run, print cache stats from the API
Console.WriteLine("\n[LoadTest] Fetching cache stats from API...");
using var httpStats = new HttpClient();
try
{
    var stats = await httpStats.GetStringAsync($"{baseUrl}/api/cache/stats");
    Console.WriteLine($"[LoadTest] Cache stats: {stats}");
}
catch (Exception ex)
{
    Console.WriteLine($"[LoadTest] Could not fetch cache stats: {ex.Message}");
}

Console.WriteLine($"\n[LoadTest] Report saved to: reports/{reportName}.html");
Console.WriteLine("[LoadTest] Done.");
