# CaseDesk Functions — infrastructure

`main.bicep` deploys the Azure side of the CaseClosed integration into one resource group:

| Resource | Notes |
| --- | --- |
| Function app (Flex Consumption `FC1`, Linux, .NET 8 isolated) | System-assigned identity; host storage, deployment package and the stub's table are reached through that identity (no storage keys) |
| Storage account | `allowSharedKeyAccess: false`; holds the Flex deployment container and table `CaseClosedReceived` (stub de-dup) |
| Log Analytics + Application Insights | Workspace-based; `APPLICATIONINSIGHTS_CONNECTION_STRING` set on the app |
| Service Bus namespace (Basic) + queue `caseclosed` | `maxDeliveryCount` 10, dead-letter on expiration, TTL 14 days. Queue-scoped SAS rules `dataverse-send` (Send) and `function-listen` (Listen) |
| Key Vault (RBAC) | Secret `ServiceBusConnection` is written by the deployment from the `function-listen` rule. You add `ExternalStubKey` yourself (below). The app's identity has **Key Vault Secrets User** |

**Why Flex Consumption?** It is the current serverless Linux plan: it runs .NET 8 isolated,
supports Service Bus triggers, Key Vault references and identity-based storage, and scales to
zero. The Linux Y1 Consumption plan is legacy (no new features, retirement announced), so a new
sample should not start there.

App settings set by the template:

| Setting | Value |
| --- | --- |
| `AzureWebJobsStorage__accountName` | storage account name (identity-based) |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | from Application Insights |
| `ServiceBusConnection` | `@Microsoft.KeyVault(VaultName=…;SecretName=ServiceBusConnection)` |
| `ExternalStubKey` | `@Microsoft.KeyVault(VaultName=…;SecretName=ExternalStubKey)` |
| `ExternalStubUrl` | `https://<app>/api/external/caseclosed` |

> The template was written without the Bicep compiler available on the authoring machine. Run
> `az bicep build -f functions/infra/main.bicep` (or the `what-if` below) before the first deploy.

## Deploy

```bash
RG=rg-casedesk
LOCATION=uksouth            # any region that offers Flex Consumption
az login
az group create -n $RG -l $LOCATION

# Optional: let yourself write Key Vault secrets (Key Vault Secrets Officer)
ME=$(az ad signed-in-user show --query id -o tsv)

az deployment group what-if -g $RG -f functions/infra/main.bicep \
  -p namePrefix=casedesk secretsOfficerPrincipalId=$ME

az deployment group create -g $RG -n casedesk-functions -f functions/infra/main.bicep \
  -p namePrefix=casedesk secretsOfficerPrincipalId=$ME

# Capture outputs
APP=$(az deployment group show -g $RG -n casedesk-functions --query properties.outputs.functionAppName.value -o tsv)
KV=$(az deployment group show -g $RG -n casedesk-functions --query properties.outputs.keyVaultName.value -o tsv)
SB=$(az deployment group show -g $RG -n casedesk-functions --query properties.outputs.serviceBusNamespace.value -o tsv)
HOST=$(az deployment group show -g $RG -n casedesk-functions --query properties.outputs.functionHostname.value -o tsv)
```

Publish the code (Azure Functions Core Tools v4):

```bash
cd functions/CaseDesk.Functions
func azure functionapp publish $APP --dotnet-isolated
```

## Secrets

Nothing secret is in the template parameters, outputs or this repo (Constitution V).

### `ExternalStubKey` (the handler → stub call)

The stub is an `AuthorizationLevel.Function` HTTP trigger in the same app, so any function or
host key of the app works. Create a dedicated one and store it in Key Vault:

```bash
STUBKEY=$(az functionapp function keys set -g $RG -n $APP --function-name ExternalStub \
  --key-name casedesk-handler --query value -o tsv)
az keyvault secret set --vault-name $KV --name ExternalStubKey --value "$STUBKEY" -o none
unset STUBKEY

# Key Vault references are resolved at start-up; restart so the new secret is picked up.
az functionapp restart -g $RG -n $APP
```

### `ServiceBusConnection`

Written by the deployment (listen-only, queue-scoped). To rotate: regenerate the
`function-listen` key and re-run the deployment (it rewrites the secret), then restart the app.

```bash
az servicebus queue authorization-rule keys renew -g $RG --namespace-name $SB \
  --queue-name caseclosed --name function-listen --key PrimaryKey -o none
```

### Values you enter in Dataverse (never in Key Vault or source)

```bash
# Service Bus endpoint: SAS key name dataverse-send, and its key
az servicebus queue authorization-rule keys list -g $RG --namespace-name $SB \
  --queue-name caseclosed --name dataverse-send --query primaryKey -o tsv

# Webhook endpoint: function key for CaseClosedFromWebhook
az functionapp function keys set -g $RG -n $APP --function-name CaseClosedFromWebhook \
  --key-name dataverse-webhook --query value -o tsv
```

Paste them straight into the Plugin Registration Tool — see
[docs/caseclosed-endpoints.md](../../docs/caseclosed-endpoints.md).

### Checking Key Vault references

Portal → Function app → *Settings → Environment variables*: each Key Vault setting shows a green
tick when resolved. From the CLI:

```bash
az rest --method get --url \
  "https://management.azure.com$(az functionapp show -g $RG -n $APP --query id -o tsv)/config/configreferences/appsettings?api-version=2023-12-01" \
  --query "value[].{name:name, status:properties.status}" -o table
```

## Local development

Copy `functions/CaseDesk.Functions/local.settings.sample.json` to `local.settings.json`
(git-ignored), start Azurite, and run `func start`. Function keys are not enforced locally.
Point `ServiceBusConnection` at a real namespace (Service Bus has no local emulator in this setup),
or exercise only the webhook path:

```bash
curl -X POST http://localhost:7071/api/caseclosed \
  -H "Content-Type: application/json" \
  --data @tests/CaseDesk.Functions.Tests/Fixtures/remote-execution-context-closed.json
curl "http://localhost:7071/api/external/caseclosed?caseNumber=CD-00042"
```
