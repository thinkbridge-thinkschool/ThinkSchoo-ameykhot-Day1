# Day 5 – Piece 3: Azure Container Apps Fundamentals

**Author:** Amey Khot  
**Branch:** `day5/cloud-deployment-observability`  
**Repo:** https://github.com/thinkbridge-thinkschool/ThinkSchoo-ameykhot-Day1

---

## Commands Executed

### 1. Login to Azure

```bash
az login
```

### 2. Check Subscription

```bash
az account show
```

### 3. Create Resource Group

```bash
az group create -n thinkschool-rg -l centralindia
```

**Output:**
```json
{
  "id": "/subscriptions/6b3f49de-c9ab-436d-b896-27ebc13a1e3a/resourceGroups/thinkschool-rg",
  "location": "centralindia",
  "managedBy": null,
  "name": "thinkschool-rg",
  "properties": {
    "provisioningState": "Succeeded"
  },
  "tags": null,
  "type": "Microsoft.Resources/resourceGroups"
}
```

### 4. Create Container Apps Environment

```bash
az containerapp env create -n thinkschool-env -g thinkschool-rg -l centralindia
```

This provisions the shared networking, logging, and runtime boundary that all Container Apps inside it share. Took ~27 minutes to complete (Azure auto-generated Log Analytics workspace `workspace-thinkschoolrglx4x`).

**Provisioning output:**
- `provisioningState: Succeeded`
- `defaultDomain: greenbay-3b4cda8d.centralindia.azurecontainerapps.io`
- `staticIp: 4.224.99.71`
- Dapr version: `1.16.4-msft.6`
- KEDA version: `2.18.1`

### 5. Get Environment JSON (for submission)

```bash
az containerapp env show -n thinkschool-env -g thinkschool-rg > env-output.json
```

See [env-output.json](env-output.json) for the full JSON output.

---

## env-output.json

See [env-output.json](env-output.json) — the complete JSON output from `az containerapp env show`.

---

## What I Learned

**Container Apps Environment** is the foundational shared boundary for Azure Container Apps. It is not a container itself — it is the infrastructure layer that all apps inside it share:

- **Networking**: All apps in the same environment share a virtual network and can communicate with each other using internal service names.
- **Logging**: A single Log Analytics workspace captures logs from every app in the environment.
- **Runtime**: The environment manages the Kubernetes-based runtime cluster — you never touch Kubernetes directly; Azure abstracts it away.

One environment can host many apps. The `Consumption` SKU means you pay only for the compute time your apps actually use — zero cost when idle (though the environment itself has a small standing cost).

---

## What Would Break This

| Failure | Cause |
|---------|-------|
| Environment creation fails | Insufficient student subscription credits or quota limits for the Central India region |
| Wrong region | Typing `centralindia` incorrectly (e.g., `central-india`) — Azure region names have no hyphens for this region |
| Resource group not found | Running `env create` before `group create` completes |
| Billing surprise | Forgetting to delete the resource group after submission — the environment costs money even when idle |

---

## Cleanup (Run After Submission)

```bash
az group delete -n thinkschool-rg --yes --no-wait
```

This deletes the resource group and everything inside it (environment + all apps), stopping all billing.

---

## Submission Checklist

- [x] Resource Group created: `thinkschool-rg` (Central India)
- [x] Container Apps Environment created: `thinkschool-env`
- [x] JSON output saved: `env-output.json`
- [x] Resources deleted to save student credits
