targetScope = 'resourceGroup'

@minLength(3)
@maxLength(15)
param namePrefix string
param location string
@minLength(36)
@maxLength(36)
param tenantId string
@allowed([
  'assessment'
  'remediation'
])
param purpose string
@minLength(71)
@maxLength(71)
param adapterManifestDigest string
param image string
param apiEnvironmentId string
param serviceBusName string
param storageAccountName string
param keyVaultName string
param apiPrincipalId string
param apiIdentityName string
param workerCpuCores int
param workerMemory string
param workerMaxReplicas int

var tenantKey = replace(toLower(tenantId), '-', '')
var workerIdentityName = '${namePrefix}-${purpose}-${tenantKey}'
var queueName = '${purpose}-${tenantKey}-jobs'
var evidenceContainerName = 'customer-${tenantKey}-evidence'
var credentialSecretName = 'customer-${toLower(tenantId)}-${purpose}'
var blobContributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
var keyVaultSecretsUserRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var serviceBusSenderRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
var serviceBusReceiverRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')

resource workerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: workerIdentityName
  location: location
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: serviceBusName
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: serviceBus
  name: queueName
  properties: {
    lockDuration: 'PT5M'
    maxDeliveryCount: 5
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: purpose == 'assessment' ? 'P1D' : 'PT1H'
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: storage
  name: 'default'
}

resource evidenceContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' existing = {
  parent: blobService
  name: evidenceContainerName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource credentialSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' existing = {
  parent: keyVault
  name: credentialSecretName
}

resource workerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: workerIdentityName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workerIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: apiEnvironmentId
    configuration: {}
    template: {
      containers: [
        {
          name: purpose
          image: image
          resources: {
            cpu: workerCpuCores
            memory: workerMemory
          }
          env: [
            {
              name: 'M365_PLATFORM_PURPOSE'
              value: purpose
            }
            {
              name: 'M365_CUSTOMER_TENANT_ID'
              value: tenantId
            }
            {
              name: 'M365_APPROVED_ADAPTER_MANIFEST_SHA256'
              value: adapterManifestDigest
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: workerMaxReplicas
      }
    }
  }
}

resource apiQueueSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(queue.id, apiIdentityName, serviceBusSenderRoleId)
  scope: queue
  properties: {
    roleDefinitionId: serviceBusSenderRoleId
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource workerQueueReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(queue.id, workerIdentity.name, serviceBusReceiverRoleId)
  scope: queue
  properties: {
    roleDefinitionId: serviceBusReceiverRoleId
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource workerEvidenceWriter 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(evidenceContainer.id, workerIdentity.name, blobContributorRoleId)
  scope: evidenceContainer
  properties: {
    roleDefinitionId: blobContributorRoleId
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource workerCredentialReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(credentialSecret.id, workerIdentity.name, keyVaultSecretsUserRoleId)
  scope: credentialSecret
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleId
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

output purpose string = purpose
output tenantId string = tenantId
output queueName string = queue.name
output workerIdentityPrincipalId string = workerIdentity.properties.principalId
