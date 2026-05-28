# Piece 5 — Verify in App Insights with your first KQL

## Live App URL

```
https://ca-api-342m3golxdrt6.livelydune-368712a9.centralindia.azurecontainerapps.io
```

---

## Step 1 — Traffic Generated

Hit 15 requests across all endpoints to populate App Insights:

```powershell
$url = "https://ca-api-342m3golxdrt6.livelydune-368712a9.centralindia.azurecontainerapps.io"
$endpoints = @(
  "/health", "/api/quotes", "/health", "/api/quotes", "/api/quotes/1",
  "/health", "/api/quotes", "/api/quotes/2", "/health", "/api/quotes",
  "/api/quotes/1", "/health", "/api/quotes", "/health", "/api/quotes"
)
foreach ($ep in $endpoints) {
    Invoke-WebRequest -Uri "$url$ep" -UseBasicParsing -TimeoutSec 20
}
```

| Endpoint | Count | Avg latency |
|---|---|---|
| `GET /health` | 6 | ~133ms |
| `GET /api/quotes` | 6 | ~124ms |
| `GET /api/quotes/{id}` | 3 | ~88ms |

---

## Step 2 — App Insights Resource

Resource created by adding `Microsoft.Insights/components` to `infra/resources.bicep`:

```bicep
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'ai-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logWorkspace.id
    IngestionMode: 'LogAnalytics'
  }
}
```

Connection string injected into Container App as secret `appinsights-connstr`, mapped to env var `application-insights-connectionstring1` which `Program.cs` already reads via:

```csharp
var aiConnStr = builder.Configuration["application-insights-connectionstring1"];
if (!string.IsNullOrWhiteSpace(aiConnStr))
    otelBuilder.UseAzureMonitor(o => { o.ConnectionString = aiConnStr; });
```

Azure resources after `azd up`:

| Resource | Name | Type |
|---|---|---|
| Application Insights | `ai-342m3golxdrt6` | Application Insights |
| Log Analytics | `log-342m3golxdrt6` | Log Analytics Workspace (backing store) |
| Container App | `ca-api-342m3golxdrt6` | Container App (redeployed with AI conn string) |

---

## Step 3 — KQL Query

Paste in **App Insights → Logs** and click **Run**:

```kql
requests
| where timestamp > ago(30m)
| summarize count(), p50=percentile(duration, 50), p99=percentile(duration, 99) by name
| order by p99 desc
```

### KQL Results Table (actual output from App Insights)

| name | count_ | p50 | p99 |
|---|---|---|---|
| GET /api/quotes/ | 11 | 3.07ms | 271.90ms |
| GET /health | 12 | 0.37ms | 181.42ms |
| GET /api/quotes/{id} | 8 | 2.36ms | 60.21ms |
| GET / | 2 | 0.28ms | 1.09ms |
| GET /favicon.ico | 1 | 0.45ms | 0.45ms |

### Screenshots

**Results table view:**

![KQL Results Table](KQL%20Qury%20Output.png)

**Chart view:**

![KQL Results Chart](KQL%20Qury%20Output%202.png)

> App Insights resource: `ai-342m3golxdrt6` | Resource group: `rg-quotes-amey` | Query ran in 3s 922ms | 5 records

---

## Step 4 — Saved as Function (Confirmed)

The query was saved as a reusable function via the Log Analytics REST API (`PUT savedSearches/EndpointPerformance`) and verified with a GET request:

| Field | Value |
|---|---|
| **Function name** | `EndpointPerformance` |
| **Alias** | `EndpointPerformance` |
| **Category** | `QuotesAPI` |
| **Workspace** | `log-342m3golxdrt6` |
| **Resource ID** | `/subscriptions/6b3f49de-c9ab-436d-b896-27ebc13a1e3a/resourceGroups/rg-quotes-amey/providers/Microsoft.OperationalInsights/workspaces/log-342m3golxdrt6/savedSearches/EndpointPerformance` |

**Screenshot — portal confirmation toast:**

![EndpointPerformance saved confirmation](KQL%20function.png)

> Portal shows: *"Successfully saved function 'EndpointPerformance'"* (green toast, top-right). The function tab `EndpointPer…` is visible in the query editor. Query ran in 4s 290ms.

Once saved, the function can be called directly in any future query without re-typing:

```kql
EndpointPerformance
```

The function appears in App Insights → Logs → **Queries hub** → category **QuotesAPI**.

---

## Observation — Which Endpoint Surprised Me Most

**`GET /health` surprised me the most.**

I expected it to be the fastest endpoint — it does nothing except return `{ "status": "healthy" }` with no database call. But its p99 was **181ms**, which is actually *higher* than `GET /api/quotes/{id}` (p99 = 60ms), a route that runs a real database query.

The reason: `GET /health` had a p50 of **0.37ms** but a p99 of **181ms** — a **490× gap**. That extreme spread means one request in every hundred is ~500 times slower than the median. This is caused by **cold-start on scale-from-zero**: the Container App scales down when idle, and the first incoming request — whichever route it hits — absorbs the full container startup cost (~180ms). Since `/health` was the first endpoint hit after each idle period (I used it as a warm-up), it absorbed every cold-start penalty.

The lesson: for low-traffic Container Apps that scale from zero, p99 reflects startup latency more than actual endpoint logic. Pinning `minReplicas: 1` eliminates cold starts but keeps the container running 24/7 (small cost). For a health check endpoint the p99 should be under 5ms — 181ms is a red flag in any SLA.

---

## What I Learned

1. **App Insights needs the connection string in the container.** The `Azure.Monitor.OpenTelemetry.AspNetCore` SDK was already in the project, but `azd` only provisions what's in Bicep — adding the `Microsoft.Insights/components` resource and passing the connection string as a secret env var was all that was needed to start receiving telemetry.

2. **KQL `requests` table is rich out of the box.** With zero custom code beyond the SDK, you get `name` (route template), `duration` (ms), `resultCode` (HTTP status), `timestamp`, `cloud_RoleInstance`, `session_Id` — enough to answer "which endpoint is slow and how often".

3. **`percentile()` vs `avg()` tells a different story.** The average hides the cold-start spike because 14 warm requests dilute 1 cold-start outlier. p99 surfaces the worst-case user experience.

4. **Workspace-based App Insights shares the Log Analytics table.** By setting `WorkspaceResourceId` in the Bicep, both the Container Apps system logs (`ContainerAppSystemLogs`) and the OpenTelemetry `requests` table land in the same workspace — one place to query everything.

5. **`UseAzureMonitor()` auto-correlates traces.** The SDK automatically correlates incoming HTTP spans from `AddAspNetCoreInstrumentation()` and outgoing EF Core spans from `AddEntityFrameworkCoreInstrumentation()` under one operation ID, so the App Insights Transaction Search shows end-to-end traces for free.

---

## What Would Break This

| Failure | Why |
|---|---|
| Missing `application-insights-connectionstring1` env var | `UseAzureMonitor` never called → no telemetry flows to App Insights, `requests` table stays empty |
| App Insights resource deleted | New telemetry has nowhere to go; `requests` table becomes stale immediately |
| Log Analytics workspace deleted | App Insights loses its backing store — all historical queries fail |
| Firewall blocks `dc.services.visualstudio.com` | OpenTelemetry exporter cannot reach the ingestion endpoint; telemetry is buffered then dropped |
| Cold start on scale-from-zero | p99 spikes to >500ms on first request — misrepresents actual steady-state performance |
| High-cardinality route names (e.g., `/api/quotes/123` not `/api/quotes/{id}`) | `summarize by name` produces one row per unique ID → thousands of rows, useless aggregation |

---

## infra/resources.bicep (final)

```bicep
// Application Insights (workspace-based)
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'ai-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logWorkspace.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}
```

And in the Container App secrets + env:
```bicep
secrets: [
  { name: 'appinsights-connstr'; value: appInsights.properties.ConnectionString }
]
// ...
env: [
  { name: 'application-insights-connectionstring1'; secretRef: 'appinsights-connstr' }
]
```

---

## GitHub Folder

[DAY5/Piece-5-Verify in App Insights with your first KQL](https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1/tree/day5/cloud-deployment-observability/DAY5/Piece-5-Verify%20in%20App%20Insights%20with%20your%20first%20KQL)
