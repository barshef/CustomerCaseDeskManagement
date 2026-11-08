// CaseDesk — Azure side of the CaseClosed integration (tasks T054, research R10/R15).
//
// Deploys into an existing resource group:
//   * Log Analytics workspace + workspace-based Application Insights
//   * Storage account (Functions host storage, Flex deployment container, stub de-dup table "CaseClosedReceived")
//   * Service Bus namespace (Basic) + queue "caseclosed" + queue-scoped SAS rules
//       - "dataverse-send"  (Send)   -> entered on the Dataverse service endpoint registration
//       - "function-listen" (Listen) -> stored in Key Vault, read by the Function app
//   * Key Vault (RBAC) holding ServiceBusConnection (written here) and ExternalStubKey (written by you, see README)
//   * Function app on the Flex Consumption plan (Linux, .NET 8 isolated), system-assigned identity
//
// Why Flex Consumption (FC1) and not Y1: Flex is the current serverless plan for Linux and supports
// .NET 8 isolated, Service Bus triggers, Key Vault references and identity-based host storage; the
// Linux Y1 Consumption plan is legacy (no new features, retirement announced). Flex also scales to zero.
//
// Secrets: none are parameters or outputs. The listen connection string goes straight from Service Bus
// into Key Vault; the app reads it through a Key Vault reference (Constitution V).

targetScope = 'resourceGroup'

@description('Short prefix for resource names (lowercase letters, digits, hyphens).')
@minLength(3)
@maxLength(12)
param namePrefix string = 'casedesk'

@description('Azure region. Must support the Flex Consumption plan.')
param location string = resourceGroup().location

@description('Optional: object id of a user/group that may write Key Vault secrets (Key Vault Secrets Officer). Leave empty to skip.')
param secretsOfficerPrincipalId string = ''

@description('Principal type for secretsOfficerPrincipalId.')
@allowed([ 'User', 'Group', 'ServicePrincipal' ])
param secretsOfficerPrincipalType string = 'User'

@description('Flex Consumption: maximum scale-out instances.')
@minValue(1)
@maxValue(1000)
param maximumInstanceCount int = 40

@description('Flex Consumption: memory per instance (MB).')
@allowed([ 512, 2048, 4096 ])
param instanceMemoryMB int = 2048

param tags object = {
  app: 'CaseDesk'
  component: 'CaseClosed integration'
}

// ---------------------------------------------------------------------------------------------
// Names
// ---------------------------------------------------------------------------------------------
var suffix = uniqueString(resourceGroup().id, namePrefix)
var compactPrefix = take(toLower(replace(namePrefix, '-', '')), 11)
var storageName = '${compactPrefix}st${take(suffix, 11)}' // <= 24, alphanumeric
var keyVaultName = '${take(namePrefix, 10)}-kv-${take(suffix, 8)}' // <= 22
var serviceBusName = '${namePrefix}-sb-${suffix}'
var functionAppName = '${namePrefix}-fn-${suffix}'
var planName = '${namePrefix}-plan-${suffix}'
var logAnalyticsName = '${namePrefix}-log-${suffix}'
var appInsightsName = '${namePrefix}-ai-${suffix}'
var deploymentContainerName = 'app-package-${take(suffix, 8)}'
var queueName = 'caseclosed'

// Built-in role definition ids
var roles = {
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageQueueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  storageTableDataContributor: '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  keyVaultSecretsOfficer: 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
}

// ---------------------------------------------------------------------------------------------
// Monitoring
// ---------------------------------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

// ---------------------------------------------------------------------------------------------
// Storage (identity-based access only)
// ---------------------------------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false // the Function app uses its managed identity
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: deploymentContainerName
  properties: { publicAccess: 'None' }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource dedupTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: 'CaseClosedReceived'
}

// ---------------------------------------------------------------------------------------------
// Service Bus (Basic: queues only, no duplicate detection — the stub de-dups by messageId)
// ---------------------------------------------------------------------------------------------
resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: queueName
  properties: {
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P14D' // Basic tier maximum
    lockDuration: 'PT1M'
  }
}

// Queue-scoped rules: Dataverse can only send to this queue; the Function can only listen on it.
resource dataverseSendRule 'Microsoft.ServiceBus/namespaces/queues/authorizationRules@2022-10-01-preview' = {
  parent: queue
  name: 'dataverse-send'
  properties: { rights: [ 'Send' ] }
}

resource functionListenRule 'Microsoft.ServiceBus/namespaces/queues/authorizationRules@2022-10-01-preview' = {
  parent: queue
  name: 'function-listen'
  properties: { rights: [ 'Listen' ] }
}

// ---------------------------------------------------------------------------------------------
// Key Vault (RBAC)
// ---------------------------------------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

// Written by the deployment straight from Service Bus — never passes through a parameter or output.
resource serviceBusConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ServiceBusConnection'
  properties: {
    value: functionListenRule.listKeys().primaryConnectionString
    contentType: 'Service Bus connection string (Listen, queue caseclosed)'
  }
}

// ExternalStubKey is NOT created here: its value is a function key that only exists after the app
// is running, and re-deploying must not overwrite it. See README "Secrets".

// ---------------------------------------------------------------------------------------------
// Function app (Flex Consumption, Linux, .NET 8 isolated)
// ---------------------------------------------------------------------------------------------
resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    tier: 'FlexConsumption'
    name: 'FC1'
  }
  properties: {
    reserved: true // Linux
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: { type: 'SystemAssignedIdentity' }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: maximumInstanceCount
        instanceMemoryMB: instanceMemoryMB
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '8.0'
      }
    }
  }
  dependsOn: [ deploymentContainer ]
}

// --- Role assignments for the Function app's system-assigned identity -------------------------
resource fnStorageBlob 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, functionApp.id, roles.storageBlobDataOwner)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataOwner)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource fnStorageQueue 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, functionApp.id, roles.storageQueueDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageQueueDataContributor)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource fnStorageTable 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, functionApp.id, roles.storageTableDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageTableDataContributor)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource fnKeyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, functionApp.id, roles.keyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource officerKeyVault 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(secretsOfficerPrincipalId)) {
  scope: keyVault
  name: guid(keyVault.id, secretsOfficerPrincipalId, roles.keyVaultSecretsOfficer)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsOfficer)
    principalId: secretsOfficerPrincipalId
    principalType: secretsOfficerPrincipalType
  }
}

// --- App settings (separate resource so the identity + RBAC exist before references resolve) ---
// Note: Flex Consumption sets FUNCTIONS_WORKER_RUNTIME / FUNCTIONS_EXTENSION_VERSION from
// functionAppConfig.runtime; they must not be app settings.
resource appSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: functionApp
  name: 'appsettings'
  properties: {
    AzureWebJobsStorage__accountName: storage.name
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
    ServiceBusConnection: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=ServiceBusConnection)'
    ExternalStubKey: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=ExternalStubKey)'
    ExternalStubUrl: 'https://${functionApp.properties.defaultHostName}/api/external/caseclosed'
  }
  dependsOn: [
    fnStorageBlob
    fnStorageQueue
    fnStorageTable
    fnKeyVaultSecretsUser
    serviceBusConnectionSecret
  ]
}

// ---------------------------------------------------------------------------------------------
// Outputs (no secrets)
// ---------------------------------------------------------------------------------------------
output functionAppName string = functionApp.name
output functionHostname string = functionApp.properties.defaultHostName
output webhookUrl string = 'https://${functionApp.properties.defaultHostName}/api/caseclosed'
output serviceBusNamespace string = serviceBus.name
output serviceBusEndpoint string = 'sb://${serviceBus.name}.servicebus.windows.net/'
output serviceBusQueue string = queue.name
output keyVaultName string = keyVault.name
output storageAccountName string = storage.name
output appInsightsName string = appInsights.name
