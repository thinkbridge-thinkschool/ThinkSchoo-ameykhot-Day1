# Screenshots — Day 22 Piece 1: Polly Resilience

Add screenshots to this folder as you run the demo. Filename must match the table below.

---

## How to take each screenshot (step-by-step)

### Pre-requisites
- API running: `dotnet run` inside `QuotesAPI-Amey/`
- A PowerShell terminal open side-by-side with VS Code
- Windows Snipping Tool: `Win + Shift + S` to capture a region

---

### Screenshot 1 — `01-polly-packages.png`
**What:** NuGet packages listed in terminal.

```powershell
cd QuotesAPI-Amey
dotnet list package
```

Capture the terminal showing:
- `Microsoft.Extensions.Http.Polly   10.0.9`
- `Polly.Extensions.Http              3.0.0`

`Win + Shift + S` → save as `01-polly-packages.png` in this folder.

---

### Screenshot 2 — `02-pipeline-code.png`
**What:** The full `PollyPolicies.cs` file open in VS Code.

1. Open `Resilience/PollyPolicies.cs` in VS Code
2. Make sure all four policies (retry, circuitBreaker, timeout, bulkhead) and the `Policy.WrapAsync` line are visible
3. `Win + Shift + S` → save as `02-pipeline-code.png`

---

### Screenshot 3 — `08-timeout-log.png` ← Do this FIRST
**What:** The `[Polly TIMEOUT]` log appearing in the terminal.

1. Enable failures:
   ```powershell
   Invoke-RestMethod -Method Post "http://localhost:5000/api/test/force-failure/true"
   ```
2. Send ONE request and watch the terminal:
   ```powershell
   Invoke-RestMethod "http://localhost:5000/api/quotes/external/1"
   ```
3. After ~5 seconds you'll see `[Polly TIMEOUT] Request timed out after 5s` in the API terminal
4. `Win + Shift + S` → save as `08-timeout-log.png`

---

### Screenshot 4 — `03-retry-backoff-logs.png`
**What:** Retry log lines showing 2s / 4s / 8s delays.

Continue watching the same terminal from Screenshot 3.
After the first timeout you'll see:
```
[Polly RETRY] Attempt 1 failed: ... Waiting 2s before retry.
[Polly TIMEOUT] Request timed out after 5s
[Polly RETRY] Attempt 2 failed: ... Waiting 4s before retry.
[Polly TIMEOUT] Request timed out after 5s
[Polly RETRY] Attempt 3 failed: ... Waiting 8s before retry.
```
`Win + Shift + S` → capture those lines → save as `03-retry-backoff-logs.png`

---

### Screenshot 5 — `04-circuit-opens.png`
**What:** The `[Polly CIRCUIT OPEN]` line.

Fire 8 parallel requests to exhaust the circuit breaker threshold:
```powershell
1..8 | ForEach-Object {
    Start-Job { Invoke-RestMethod "http://localhost:5000/api/quotes/external/1" }
}
```
Wait ~35 s. In the API terminal you'll see:
```
[Polly CIRCUIT OPEN] Too many failures. Circuit open for 30s. Error: ...
```
`Win + Shift + S` → save as `04-circuit-opens.png`

---

### Screenshot 6 — `05-open-rejecting.png`
**What:** Requests failing instantly while circuit is OPEN (no `[Client] Calling...` log).

Immediately after the circuit opens, send a few requests:
```powershell
Invoke-RestMethod "http://localhost:5000/api/quotes/external/1"
Invoke-RestMethod "http://localhost:5000/api/quotes/external/1"
```
You'll get immediate 503 responses in the PowerShell window.
In the API terminal you will NOT see any `[Client] Calling external service` lines — the circuit breaker blocks the call before HttpClient is used.
`Win + Shift + S` → capture the terminal showing instant 503s → save as `05-open-rejecting.png`

---

### Screenshot 7 — `06-half-open.png`
**What:** `[Polly CIRCUIT HALF-OPEN]` log line.

1. Disable the failure simulation:
   ```powershell
   Invoke-RestMethod -Method Post "http://localhost:5000/api/test/force-failure/false"
   ```
2. Wait 30 seconds for the circuit to enter HALF-OPEN state:
   ```powershell
   Start-Sleep 30
   ```
3. Send one request:
   ```powershell
   Invoke-RestMethod "http://localhost:5000/api/quotes/external/1"
   ```
4. In the API terminal you'll see: `[Polly CIRCUIT HALF-OPEN] Testing if service recovered...`
5. `Win + Shift + S` → save as `06-half-open.png`

---

### Screenshot 8 — `07-circuit-closed.png`
**What:** `[Polly CIRCUIT CLOSED]` recovery log line.

Immediately after the successful half-open probe (previous step) you'll see:
```
[Polly CIRCUIT CLOSED] Circuit reset — service recovered!
```
`Win + Shift + S` → save as `07-circuit-closed.png`

---

### Screenshot 9 — `09-bulkhead-log.png`
**What:** `[Polly BULKHEAD] Too many concurrent requests` log line.

The bulkhead allows max 10 concurrent + 20 queued. To trigger rejection, fire 35 jobs:
```powershell
# Make sure failure mode is ON so requests are slow (otherwise they complete before the bulkhead fills up)
Invoke-RestMethod -Method Post "http://localhost:5000/api/test/force-failure/true"

1..35 | ForEach-Object {
    Start-Job { Invoke-RestMethod "http://localhost:5000/api/quotes/external/1" }
}
```
In the API terminal look for: `[Polly BULKHEAD] Too many concurrent requests — rejected!`
`Win + Shift + S` → save as `09-bulkhead-log.png`

> **Note:** You may need to reset the circuit breaker first if it opened during earlier steps.
> Stop the API (`Ctrl+C`), restart it (`dotnet run`), then run the bulkhead test.

---

### Screenshot 10 — `10-github-repo.png`
**What:** The GitHub repo page.

1. Push your branch and open the repo in a browser
2. Navigate to the `DAY22/Piece-1-` folder
3. `Win + Shift + S` → save as `10-github-repo.png`

---

## Quick Reference — Demo Sequence

```
Start API                                      dotnet run
Enable failures      POST /api/test/force-failure/true
Send 1 request       GET  /api/quotes/external/1       → screenshots 3, 4, 8
Send 8 parallel      1..8 | ForEach-Object { Start-Job { ... } }  → screenshot 5
After circuit opens  GET  /api/quotes/external/1 (×2)  → screenshot 6
Disable failures     POST /api/test/force-failure/false
Wait 30 s
Send 1 request       GET  /api/quotes/external/1       → screenshots 7, 9
```
