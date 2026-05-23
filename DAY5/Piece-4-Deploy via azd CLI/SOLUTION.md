# Piece 4 — Deploy via azd CLI

## Live URL

```
https://ca-api-342m3golxdrt6.livelydune-368712a9.centralindia.azurecontainerapps.io
```

## Health Check — 200 OK

```bash
curl https://ca-api-342m3golxdrt6.livelydune-368712a9.centralindia.azurecontainerapps.io/health
```

Response:
```json
{"status":"healthy","timestamp":"2026-05-23T11:42:45.2771058+00:00"}
HTTP_STATUS:200
```

---

## azure.yaml

```yaml
# yaml-language-server: $schema=https://raw.githubusercontent.com/Azure/azure-dev/main/schemas/v1.0/azure.yaml.json

name: quotes-api
services:
  api:
    project: ./QuotesAPI-Amey
    language: dotnet
    host: containerapp
```

---

## azd up Terminal Output

```
Initialize bicep provider

Provisioning and deploying (azd up)
Packaging overlaps with provisioning for faster execution.

  api: Packaging
Initialize bicep provider
  api: Packaging (Building Docker image)
Comparing deployment state
Validating deployment
  api: Packaging (Tagging container image)
Creating/Updating resources
  (✓) Done: Resource group: rg-quotes-amey (4.603s)
  (✓) Done: Container Registry: acr342m3golxdrt6 (9.145s)
  (✓) Done: Log Analytics workspace: log-342m3golxdrt6 (22.856s)
  (✓) Done: Container Apps Environment: cae-342m3golxdrt6 (52.766s)
  (✓) Done: Container App: ca-api-342m3golxdrt6 (16.853s)
  api: Publishing
  api: Publishing (Tagging container image)
  api: Publishing (Logging into container registry)
  api: Publishing (Pushing container image)
  api: Deploying (Updating container app revision)
  api: Deploying (Waiting for container revision)
  api: Deploying (Fetching endpoints for service)
  api: Done [1m19s]
  - Endpoint: https://ca-api-342m3golxdrt6.livelydune-368712a9.centralindia.azurecontainerapps.io/

SUCCESS: Your application was provisioned and deployed to Azure in 3 minutes 44 seconds.
  Provisioning: 2 minutes 23 seconds
  Deploying:    1 minute 19 seconds
```

---

## Azure Resources Created

| Resource | Name | Type |
|---|---|---|
| Resource Group | `rg-quotes-amey` | Resource Group |
| Container Registry | `acr342m3golxdrt6` | Azure Container Registry (Basic) |
| Log Analytics | `log-342m3golxdrt6` | Log Analytics Workspace |
| Container Apps Env | `cae-342m3golxdrt6` | Container Apps Environment |
| Container App | `ca-api-342m3golxdrt6` | Container App |

**Region:** `centralindia`  
**Subscription:** Azure for Students (`6b3f49de-c9ab-436d-b896-27ebc13a1e3a`)

---

## Files Created for azd

### `azure.yaml`
Root config that tells azd what services exist and how to deploy them.

### `infra/main.bicep`
Subscription-scope Bicep that creates the resource group and calls `resources.bicep`.

### `infra/resources.bicep`
Resource-group-scope Bicep that provisions:
- Azure Container Registry (ACR) — stores the Docker image
- Log Analytics Workspace — captures container logs
- Container Apps Environment — the shared managed environment
- Container App — the running application with external HTTPS ingress on port 8080

### `QuotesAPI-Amey/Dockerfile`
Multi-stage build: SDK image compiles + publishes, ASP.NET runtime image runs the app.

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["QuotesApi.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet publish "QuotesApi.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "QuotesApi.dll"]
```

---

## What I Learned

1. **One command, full stack**: `azd up` handles image build → ACR push → Bicep provisioning → Container App deploy in a single, reproducible workflow. No manual portal clicking.

2. **azure.yaml is the map**: The `azure.yaml` file is the only thing that connects your source code to Azure resources. The `host: containerapp` tells azd exactly how to package and deploy the service.

3. **Bicep generates the infrastructure**: Two Bicep files replace pages of portal clicks — subscription-scope `main.bicep` creates the resource group, resource-group-scope `resources.bicep` creates ACR, Log Analytics, Container Apps Environment, and the Container App itself.

4. **azd-service-name tag is critical**: The Container App must have the `azd-service-name: api` tag to match the service name in `azure.yaml`. Without it, azd can't identify which container app to update after the image is pushed.

5. **Student subscription limits**: Azure for Students allows only **1 Container Apps Environment** per subscription. Old environments from previous exercises must be deleted before creating new ones.

6. **HTTP inside, HTTPS outside**: Container Apps ingress terminates TLS externally. The app listens on `HTTP:8080` internally. Setting `ASPNETCORE_HTTP_PORTS=8080` prevents the app from trying to redirect HTTP → HTTPS internally.

7. **`azd down` to stop billing**: One command tears down all provisioned resources and stops all charges.

---

## What Would Break This

| Failure | Why |
|---|---|
| Delete the Container App | App goes offline immediately — no replicas |
| ACR credentials rotation | Container App can't pull new image revisions |
| SQLite in ephemeral storage | Container restarts wipe all quotes data — need persistent storage (Azure Files mount) for production |
| Student subscription limit | Only 1 Container Apps Environment allowed — deploying a second without deleting the first fails provisioning |
| `Jwt:Key` missing or too short | App throws `InvalidOperationException` on startup and crashes |
| `ASPNETCORE_HTTP_PORTS` not set | Container Apps sends HTTP to port 8080 but app may redirect, causing redirect loops |

---

## GitHub Folder

[DAY5/Piece-4-Deploy via azd CLI](https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1/tree/day5/cloud-deployment-observability/DAY5/Piece-4-Deploy%20via%20azd%20CLI)

---

## Cleanup (After Submission)

```bash
azd down
# Deletes: rg-quotes-amey (ACR + Log Analytics + Container Apps Env + Container App)
```
