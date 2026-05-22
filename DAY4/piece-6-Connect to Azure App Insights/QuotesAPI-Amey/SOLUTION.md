# Day 4 – Piece 6: Connect to Azure App Insights

## Paste 1 — Program.cs Setup Code

The two changes that wire App Insights into the existing OpenTelemetry pipeline:

### 1. Azure Key Vault configuration (added before any services)

```csharp
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

// Azure Key Vault — loads secrets into IConfiguration before any service reads them
var keyVaultUrl = builder.Configuration["KeyVault:Url"]
    ?? "https://quotes-api-keyvault1.vault.azure.net/";
builder.Configuration.AddAzureKeyVault(
    new Uri(keyVaultUrl),
    new DefaultAzureCredential());
```

`DefaultAzureCredential` tries, in order: environment variables → Workload Identity → Managed Identity → Visual Studio → Azure CLI → Azure PowerShell. In dev the Azure CLI credential is used; in production on Azure it uses Managed Identity with no code change.

### 2. OpenTelemetry + Azure Monitor (replaces bare AddOpenTelemetry)

```csharp
builder.Services.AddOpenTelemetry()
    .UseAzureMonitor(o =>
    {
        // Secret name in Key Vault: application-insights-connectionstring1
        // Key Vault provider maps hyphens as-is in the config key
        o.ConnectionString = builder.Configuration["application-insights-connectionstring1"];
    })
    .ConfigureResource(r => r.AddService("QuotesApi"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(QuotesApi.Services.AuthTokenService.ActivitySourceName)
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
        }));
```

`UseAzureMonitor` routes all OpenTelemetry signals (traces, metrics, logs) to App Insights. The `WithTracing` call adds EF Core instrumentation and the custom `AuthTokenService` source, which `UseAzureMonitor` doesn't include by default.

### 3. NuGet packages required

| Package | Version | Purpose |
|---------|---------|---------|
| `Azure.Identity` | 1.13.2 | `DefaultAzureCredential` — picks the right auth method per environment |
| `Azure.Extensions.AspNetCore.Configuration.Secrets` | 1.3.2 | Key Vault config provider (`AddAzureKeyVault`) |
| `Azure.Monitor.OpenTelemetry.AspNetCore` | 1.3.0 | `UseAzureMonitor()` — exports traces, metrics & logs to App Insights |

Install via:
```
dotnet add package Azure.Identity --version 1.13.2
dotnet add package Azure.Extensions.AspNetCore.Configuration.Secrets --version 1.3.2
dotnet add package Azure.Monitor.OpenTelemetry.AspNetCore --version 1.3.0
```

### 4. Custom ActivitySource span in `RefreshAsync`

`AuthTokenService.RefreshAsync` carries a custom span that covers the full token-rotation operation. Key tags visible in App Insights:

```csharp
// Activity already started at method entry:
using var activity = _activitySource.StartActivity("refresh-token");

// --- failure path: reuse detected ---
activity?.SetTag("security.reuse_detected", true);
activity?.SetTag("user.id", existing.UserId.ToString());
activity?.SetStatus(ActivityStatusCode.Error, "Token reuse detected");

// --- success path: token rotated ---
activity?.SetTag("user.id", existing.UserId.ToString());
activity?.SetTag("token.family", existing.Family);
activity?.SetStatus(ActivityStatusCode.Ok);
```

In App Insights these appear under **Transaction search → Custom dimensions**. Filter by `customDimensions["security.reuse_detected"] == true` to build a security alert.

---

## Paste 2 — KQL Query: Slowest 10 Requests in the Last Hour

```kusto
requests
| where timestamp > ago(1h)
| order by duration desc
| take 10
| project
    timestamp,
    name,
    url,
    resultCode,
    duration,
    cloud_RoleName,
    operation_Id
```

To correlate with the custom `refresh-token` span:

```kusto
dependencies
| where timestamp > ago(1h)
| where name == "refresh-token"
| order by duration desc
| take 10
| project timestamp, name, duration, success, customDimensions
```

To find security reuse events:

```kusto
dependencies
| where timestamp > ago(24h)
| where customDimensions["security.reuse_detected"] == "True"
| project timestamp, operation_Id, customDimensions["user.id"]
```

---

## Paste 3 — Screenshot of App Insights in Azure Portal

> **Note:** Add a screenshot of the Azure Portal App Insights **Live Metrics** or **Transaction Search** blade here once the app has sent its first telemetry.
>
> Suggested view: **App Insights → Transaction search → filter by Operation name "POST /api/auth/login"** — it shows the parent request span, the EF Core child span, and the custom `issue-token-pair` span in a single waterfall.

---

## Paste 4 — What I Learned

1. **Key Vault + DefaultAzureCredential is zero-friction across environments.** The same code works in dev (Azure CLI auth) and in production on Azure (Managed Identity). The `AddAzureKeyVault` call must happen before any service that reads the connection string — ordering in `Program.cs` matters.

2. **`UseAzureMonitor` is the modern App Insights path.** The legacy `ApplicationInsights.AspNetCore` package with `AddApplicationInsightsTelemetry()` is separate from OpenTelemetry. `Azure.Monitor.OpenTelemetry.AspNetCore` is the new approach: it plugs into the same OTel pipeline you already have for Jaeger/OTLP, so you get App Insights and Jaeger from one instrumentation.

3. **Secret names with hyphens map directly as config keys.** The Key Vault provider maps `application-insights-connectionstring1` (hyphen-separated) straight to `builder.Configuration["application-insights-connectionstring1"]`. The `--` double-dash convention is for hierarchical keys like `Jwt--Key` → `Jwt:Key`.

4. **Custom span tags become App Insights custom dimensions.** Any tag set via `activity.SetTag(key, value)` shows up under `customDimensions` in KQL, which lets you build targeted queries and alerts (e.g. alert on `security.reuse_detected`).

5. **OTLP and Azure Monitor co-exist.** The same span is exported to both Jaeger (via `AddOtlpExporter`) and App Insights (via `UseAzureMonitor`) simultaneously. This lets you keep local Jaeger for development and ship to App Insights in all environments without changing instrumentation.

---

## Paste 5 — What Would Break This

| Failure | Symptom | Fix |
|---------|---------|-----|
| No RBAC for Managed Identity / Azure CLI user on Key Vault | `AuthorizationFailedException` at startup — app won't start | Assign `Key Vault Secrets User` role to the identity |
| `DefaultAzureCredential` can't find any credential (CI without `AZURE_*` env vars) | Same startup crash | Set `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID` in CI environment, or use a Workload Identity Federation for GitHub Actions |
| Key Vault secret name typo (`application-insights-connectionstring1`) | `o.ConnectionString` is `null` → `UseAzureMonitor` swallows it silently; telemetry never arrives | Log the config value before `UseAzureMonitor` to verify; add a startup check like `ArgumentNullException.ThrowIfNull` |
| App Insights connection string references wrong region endpoint | SDK initialises but telemetry is dropped by a mismatched ingestion endpoint | Always copy the full connection string from the portal **Overview** blade — it includes the correct `IngestionEndpoint` |
| Integration tests hit Key Vault during CI | Tests slow (Key Vault round-trip) or fail (no network / no credentials) | Use `WebApplicationFactory` environment override to replace the Key Vault config source with an `InMemoryCollection` for tests |
| `AddAzureKeyVault` called after `AddInfrastructure` | Connection string is `null` when EF Core or other services read it | Always add Key Vault to `builder.Configuration` before any `builder.Services` call that depends on those secrets |
