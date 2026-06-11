# load-test.ps1  — HybridCache before/after p99 measurement
# Usage: .\load-test.ps1
# Requires: API running on http://localhost:5000

param(
    [string]$BaseUrl     = "http://localhost:5000",
    [int]$TotalRequests  = 200,
    [int]$Concurrency    = 30
)

function Invoke-LoadTest {
    param([string]$Name, [string]$Url, [string]$BaseUrl, [int]$N, [int]$C)

    Write-Host ""
    Write-Host ("=" * 55) -ForegroundColor DarkGray
    Write-Host "  $Name" -ForegroundColor Cyan
    Write-Host "  URL: $Url" -ForegroundColor DarkGray
    Write-Host ("=" * 55) -ForegroundColor DarkGray

    # Zero out the stats counter
    try { Invoke-RestMethod -Method Delete "$BaseUrl/api/cache/stats/reset" | Out-Null } catch {}

    $pool = [RunspaceFactory]::CreateRunspacePool(1, $C)
    $pool.Open()

    $running = [System.Collections.Generic.List[hashtable]]::new()

    for ($i = 1; $i -le $N; $i++) {
        $ps = [PowerShell]::Create()
        $ps.RunspacePool = $pool

        [void]$ps.AddScript({
            param($u)
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            try {
                $r = Invoke-WebRequest -Uri $u -UseBasicParsing -TimeoutSec 15
                $sw.Stop()
                return @{ ms = $sw.ElapsedMilliseconds; ok = ($r.StatusCode -eq 200) }
            } catch {
                $sw.Stop()
                return @{ ms = $sw.ElapsedMilliseconds; ok = $false }
            }
        }).AddArgument($Url)

        $running.Add(@{ ps = $ps; h = $ps.BeginInvoke() })
    }

    $times  = [System.Collections.Generic.List[long]]::new()
    $errs   = 0
    $wallSw = [System.Diagnostics.Stopwatch]::StartNew()

    foreach ($r in $running) {
        $res = $r.ps.EndInvoke($r.h)[0]
        $r.ps.Dispose()
        if ($res -and $res.ok) { $times.Add($res.ms) } else { $errs++ }
    }

    $wallSw.Stop()
    $pool.Close()

    $sorted = @($times | Sort-Object)
    $n2     = $sorted.Count

    if ($n2 -eq 0) { Write-Host "  All requests failed!" -ForegroundColor Red; return }

    $p50 = $sorted[[int]([Math]::Floor($n2 * 0.50))]
    $p95 = $sorted[[int]([Math]::Floor($n2 * 0.95))]
    $p99 = $sorted[[Math]::Min([int]([Math]::Floor($n2 * 0.99)), $n2 - 1)]
    $avg = [Math]::Round(($sorted | Measure-Object -Average).Average, 1)
    $rps = [Math]::Round($n2 / $wallSw.Elapsed.TotalSeconds, 0)

    Write-Host "  Requests:   $n2 ok  /  $errs errors" -ForegroundColor White
    Write-Host "  Throughput: $rps req/s" -ForegroundColor White
    Write-Host "  avg:        $avg ms"
    Write-Host "  p50:        $p50 ms"
    Write-Host "  p95:        $p95 ms"
    Write-Host "  p99:        $p99 ms" -ForegroundColor Yellow

    try {
        $stats = Invoke-RestMethod "$BaseUrl/api/cache/stats" -ErrorAction SilentlyContinue
        Write-Host "  DB hits:    $($stats.dbHits)" -ForegroundColor Red
        Write-Host "  Cache hits: $($stats.cacheHits)" -ForegroundColor Green
        Write-Host "  Hit rate:   $($stats.hitRatePct)%" -ForegroundColor Green
    } catch {}
}

# ── Warm up (so JIT / connection pool is ready) ───────────────────────────────
Write-Host "`nWarming up..." -ForegroundColor DarkGray
1..5 | ForEach-Object {
    try { Invoke-RestMethod "$BaseUrl/api/quotes/1" | Out-Null } catch {}
    try { Invoke-RestMethod "$BaseUrl/api/quotes/1/direct" | Out-Null } catch {}
}

Write-Host "`n  HybridCache Load Test — $TotalRequests requests @ $Concurrency concurrency"

# ── BEFORE: /api/quotes/{id}/direct — bypasses cache, every request hits SQLite ─
Invoke-LoadTest `
    -Name   "BEFORE  — no cache  (GET /api/quotes/1/direct → SQLite every time)" `
    -Url    "$BaseUrl/api/quotes/1/direct" `
    -BaseUrl $BaseUrl `
    -N      $TotalRequests `
    -C      $Concurrency

# ── AFTER: /api/quotes/{id} — served from L1 in-memory after first hit ──────────
Invoke-LoadTest `
    -Name   "AFTER   — HybridCache  (GET /api/quotes/1 → L1 memory after first hit)" `
    -Url    "$BaseUrl/api/quotes/1" `
    -BaseUrl $BaseUrl `
    -N      $TotalRequests `
    -C      $Concurrency

Write-Host ""
Write-Host ("=" * 55) -ForegroundColor DarkGray
Write-Host "  Done. Screenshot this window for your submission." -ForegroundColor Green
Write-Host ("=" * 55) -ForegroundColor DarkGray
