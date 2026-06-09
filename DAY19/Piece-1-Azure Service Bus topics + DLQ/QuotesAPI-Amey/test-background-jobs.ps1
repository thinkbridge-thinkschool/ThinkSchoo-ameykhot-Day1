# Run this AFTER starting the API in a separate terminal: dotnet run
# Update port below if dotnet run shows a different port

$base = "http://localhost:5000"

Write-Host ""
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "  DAY18 - Background Jobs - Screenshot Test Script   " -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan

# SCREENSHOT 2: POST /api/jobs/enqueue returns 202 immediately
Write-Host ""
Write-Host "--- SCREENSHOT 2: Enqueue a job (expect 202 Accepted) ---" -ForegroundColor Yellow
$body = '{"quoteId": 1, "jobType": "notify-followers"}'
try {
    $resp = Invoke-WebRequest -Uri "$base/api/jobs/enqueue" -Method POST -ContentType "application/json" -Body $body
    Write-Host "Status : $($resp.StatusCode) $($resp.StatusDescription)" -ForegroundColor Green
    Write-Host "Body   : $($resp.Content)" -ForegroundColor Green
    Write-Host ">>> Switch to API console -- screenshot the Processing and Completed log lines" -ForegroundColor Magenta
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Is the API running? Start it with: dotnet run" -ForegroundColor Red
}

Start-Sleep -Seconds 2

# SCREENSHOT 3: Enqueue 3 jobs to show drain loop
Write-Host ""
Write-Host "--- SCREENSHOT 3: Enqueue 3 jobs in quick succession ---" -ForegroundColor Yellow
for ($i = 1; $i -le 3; $i++) {
    $b = "{""quoteId"": $i, ""jobType"": ""send-email""}"
    try {
        $r = Invoke-WebRequest -Uri "$base/api/jobs/enqueue" -Method POST -ContentType "application/json" -Body $b
        Write-Host "  Job $i enqueued -> $($r.StatusCode)" -ForegroundColor Green
    } catch {
        Write-Host "  Job $i failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}
Write-Host ">>> API console should show 3 jobs processed one after the other" -ForegroundColor Magenta

Start-Sleep -Seconds 3

# SCREENSHOT 4: Login + CreateQuote auto-enqueues background job
Write-Host ""
Write-Host "--- SCREENSHOT 4: Login then CreateQuote (background job auto-enqueued) ---" -ForegroundColor Yellow
try {
    $loginBody = '{"email":"user@test.com","password":"password123"}'
    $loginResp = Invoke-RestMethod -Uri "$base/api/auth/login" -Method POST -ContentType "application/json" -Body $loginBody
    $token = $loginResp.access_token
    Write-Host "Login OK -- got access token" -ForegroundColor Green

    $quoteBody = '{"author":"Marcus Aurelius","text":"The obstacle is the way."}'
    $headers = @{ Authorization = "Bearer $token" }
    $quoteResp = Invoke-WebRequest -Uri "$base/api/quotes" -Method POST -ContentType "application/json" -Headers $headers -Body $quoteBody
    Write-Host "CreateQuote -> $($quoteResp.StatusCode) $($quoteResp.StatusDescription)" -ForegroundColor Green
    Write-Host "Body: $($quoteResp.Content)" -ForegroundColor Green
    Write-Host ">>> API console should show: Enqueued notify-followers job for QuoteId=..." -ForegroundColor Magenta
    Write-Host "    then BackgroundService Processing and Completed lines" -ForegroundColor Magenta
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "  All test calls done." -ForegroundColor Cyan
Write-Host ""
Write-Host "  SCREENSHOT 5 -- Graceful Shutdown:" -ForegroundColor Yellow
Write-Host "  Go to the API terminal and press Ctrl+C" -ForegroundColor Yellow
Write-Host "  Screenshot the final log lines:" -ForegroundColor Yellow
Write-Host "    [BackgroundService] Shutdown requested -- stopping cleanly" -ForegroundColor White
Write-Host "    [BackgroundService] Stopped at ..." -ForegroundColor White
Write-Host "======================================================" -ForegroundColor Cyan
