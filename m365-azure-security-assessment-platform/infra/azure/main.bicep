targetScope = 'resourceGroup'

@allowed([
  'central'
  'standalone'
])
param deploymentMode string

@allowed([
  'development-test'
  'pre-production'
  'production'
])
param environmentClass string

param location string
@minLength(3)
@maxLength(15)
param namePrefix string
@minLength(3)
@maxLength(24)
@description('Globally unique lowercase alphanumeric storage account name.')
param storageAccountName string
@minLength(3)
@maxLength(24)
@description('Globally unique Key Vault name containing only letters, digits, and hyphens.')
param keyVaultName string
@minLength(6)
@maxLength(50)
@description('Globally unique Service Bus namespace name.')
param serviceBusName string
param standaloneTenantId string

param vnetAddressPrefix string
param webAcaSubnetPrefix string
param apiAcaSubnetPrefix string
param postgresSubnetPrefix string
param privateEndpointSubnetPrefix string
param firewallPrivateIp string = ''
param egressPolicyApprovalReference string = ''
param approvedMicrosoftApiFqdns array = []

@allowed([
  'Standard'
  'Premium'
])
param serviceBusSku string
param serviceBusCapacity int
param serviceBusZoneRedundant bool

param postgresSkuName string
@allowed([
  'Burstable'
  'GeneralPurpose'
  'MemoryOptimized'
])
param postgresSkuTier string
param postgresStorageSizeGb int
param postgresBackupRetentionDays int
@minLength(36)
@maxLength(36)
param postgresMigrationPrincipalObjectId string
param postgresMigrationPrincipalName string
@allowed([
  'Enabled'
  'Disabled'
])
param postgresGeoRedundantBackup string
@allowed([
  'Disabled'
  'SameZone'
  'ZoneRedundant'
])
param postgresHaMode string

param storageSkuName string
param logRetentionDays int
param evidenceRetentionDays int
param auditRetentionDays int
param keyVaultSoftDeleteRetentionDays int
param customerWorkerBindings array = []

param webImage string
param apiImage string
param assessmentWorkerImage string
param remediationWorkerImage string
param webMaxReplicas int
param apiMaxReplicas int
param assessmentWorkerMaxReplicas int
param remediationWorkerMaxReplicas int
param webCpuCores int
param apiCpuCores int
param assessmentWorkerCpuCores int
param remediationWorkerCpuCores int
param webMemory string
param apiMemory string
param assessmentWorkerMemory string
param remediationWorkerMemory string
param containerAppsZoneRedundant bool

param approvedRegion string = ''
param regionApprovalReference string = ''
param networkProfileApprovalReference string = ''
param premiumCostApprovalReference string = ''
param budgetApprovalReference string = ''
param retentionApprovalReference string = ''
param productionSloApprovalReference string = ''
param productionApprovalReference string = ''
param productionApprovalActor string = ''
param productionApprovalTimestampUtc string = ''
param premiumWorkloadProfileId string = ''
param premiumCostEstimateSource string = ''
param premiumEstimatedMonthlyCost string = ''
param premiumEstimatedMonthlyCurrency string = ''
param approvedMonthlyBudget string = ''
param approvedBudgetCurrency string = ''
param approvedAvailabilityPercent string = ''
param approvedQueueAgeAlertSeconds int = 0
param approvedJobTimeoutSeconds int = 0
param approvedPerCustomerConcurrency int = 0
param approvedMaxTransientRetries int = 0
param approvedRecoveryRtoHours int = 0
param approvedRecoveryRpoMinutes int = 0

var subnetRouteTableName = '${namePrefix}-aca-routes'
var blobPrivateDnsZoneName = 'privatelink.blob.${environment().suffixes.storage}'
var keyVaultPrivateDnsZoneName = 'privatelink.vaultcore.${environment().suffixes.keyvaultDns}'
var tenantEvidenceLifecycleRules = [for binding in customerWorkerBindings: {
  enabled: true
  name: 'tenant-evidence-expiry-${replace(toLower(binding.tenantId), '-', '')}'
  type: 'Lifecycle'
  definition: {
    actions: {
      baseBlob: {
        delete: {
          daysAfterModificationGreaterThan: evidenceRetentionDays
        }
      }
      version: {
        delete: {
          daysAfterCreationGreaterThan: evidenceRetentionDays
        }
      }
    }
    filters: {
      blobTypes: [
        'blockBlob'
      ]
      prefixMatch: [
        'customer-${replace(toLower(binding.tenantId), '-', '')}-evidence/'
      ]
    }
  }
}]
var keyVaultCryptoUserRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bea7-57ae8d297424')

resource acaRouteTable 'Microsoft.Network/routeTables@2024-05-01' = {
  name: subnetRouteTableName
  location: location
  properties: {
    disableBgpRoutePropagation: false
    routes: empty(firewallPrivateIp) ? [] : [
      {
        name: 'approved-egress-firewall'
        properties: {
          addressPrefix: '0.0.0.0/0'
          nextHopType: 'VirtualAppliance'
          nextHopIpAddress: firewallPrivateIp
        }
      }
    ]
  }
}

resource network 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${namePrefix}-vnet'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [vnetAddressPrefix]
    }
    subnets: [
      {
        name: 'snet-web-aca'
        properties: {
          addressPrefix: webAcaSubnetPrefix
          routeTable: {
            id: acaRouteTable.id
          }
          delegations: [
            {
              name: 'aca-web-environment'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'snet-api-aca'
        properties: {
          addressPrefix: apiAcaSubnetPrefix
          routeTable: {
            id: acaRouteTable.id
          }
          delegations: [
            {
              name: 'aca-api-environment'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'snet-postgres'
        properties: {
          addressPrefix: postgresSubnetPrefix
          delegations: [
            {
              name: 'postgres-flexible-server'
              properties: {
                serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
              }
            }
          ]
        }
      }
      {
        name: 'snet-private-endpoints'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

resource webAcaSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: network
  name: 'snet-web-aca'
}

resource apiAcaSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: network
  name: 'snet-api-aca'
}

resource postgresSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: network
  name: 'snet-postgres'
}

resource privateEndpointSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' existing = {
  parent: network
  name: 'snet-private-endpoints'
}

resource postgresPrivateDns 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'private.postgres.database.azure.com'
  location: 'global'
}

resource blobPrivateDns 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: blobPrivateDnsZoneName
  location: 'global'
}

resource keyVaultPrivateDns 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: keyVaultPrivateDnsZoneName
  location: 'global'
}

resource serviceBusPrivateDns 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.servicebus.windows.net'
  location: 'global'
}

resource postgresDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: postgresPrivateDns
  name: '${namePrefix}-pg-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: network.id
    }
  }
}

resource blobDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: blobPrivateDns
  name: '${namePrefix}-blob-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: network.id
    }
  }
}

resource keyVaultDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: keyVaultPrivateDns
  name: '${namePrefix}-kv-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: network.id
    }
  }
}

resource serviceBusDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: serviceBusPrivateDns
  name: '${namePrefix}-sb-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: network.id
    }
  }
}

resource logs 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${namePrefix}-logs'
  location: location
  properties: {
    retentionInDays: logRetentionDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Disabled'
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${namePrefix}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    DisableIpMasking: true
  }
}

resource evidenceStorage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: storageSkuName
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    encryption: {
      keySource: 'Microsoft.Storage'
      services: {
        blob: {
          enabled: true
          keyType: 'Account'
        }
      }
    }
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Disabled'
    supportsHttpsTrafficOnly: true
    networkAcls: {
      bypass: 'None'
      defaultAction: 'Deny'
      ipRules: []
      virtualNetworkRules: []
    }
  }
}

resource evidenceBlobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: evidenceStorage
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: evidenceRetentionDays
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: evidenceRetentionDays
    }
    isVersioningEnabled: true
  }
}

resource customerEvidenceContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [for binding in customerWorkerBindings: {
  parent: evidenceBlobService
  name: 'customer-${replace(toLower(binding.tenantId), '-', '')}-evidence'
  properties: {
    publicAccess: 'None'
  }
}]

resource auditArchiveContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: evidenceBlobService
  name: 'audit-archive'
  properties: {
    publicAccess: 'None'
  }
}

resource evidenceLifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: evidenceStorage
  name: 'default'
  properties: {
    policy: {
      rules: concat([
        {
          enabled: true
          name: 'approved-audit-archive-expiry'
          type: 'Lifecycle'
          definition: {
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: auditRetentionDays
                }
              }
              version: {
                delete: {
                  daysAfterCreationGreaterThan: auditRetentionDays
                }
              }
            }
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                'audit-archive/'
              ]
            }
          }
        }
      ], tenantEvidenceLifecycleRules)
    }
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: keyVaultSoftDeleteRetentionDays
    enablePurgeProtection: true
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      bypass: 'None'
      defaultAction: 'Deny'
      ipRules: []
      virtualNetworkRules: []
    }
  }
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: serviceBusName
  location: location
  sku: {
    name: serviceBusSku
    tier: serviceBusSku
    capacity: serviceBusSku == 'Premium' ? serviceBusCapacity : 0
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    disableLocalAuth: true
    zoneRedundant: serviceBusZoneRedundant
  }
}

resource assessmentQueues 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = [for binding in customerWorkerBindings: if (!empty(binding.assessmentManifestDigest) && binding.assessmentManifestApproval.approved == true && !empty(binding.assessmentManifestApproval.reference)) {
  parent: serviceBus
  name: 'assessment-${replace(toLower(binding.tenantId), '-', '')}-jobs'
  properties: {
    lockDuration: 'PT5M'
    maxDeliveryCount: 5
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P1D'
  }
}]

resource remediationQueues 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = [for binding in customerWorkerBindings: if (!empty(binding.remediationManifestDigest) && binding.remediationManifestApproval.approved == true && !empty(binding.remediationManifestApproval.reference)) {
  parent: serviceBus
  name: 'remediation-${replace(toLower(binding.tenantId), '-', '')}-jobs'
  properties: {
    lockDuration: 'PT5M'
    maxDeliveryCount: 5
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'PT1H'
  }
}]

resource postgresServer 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: '${namePrefix}-pg'
  location: location
  sku: {
    name: postgresSkuName
    tier: postgresSkuTier
  }
  properties: {
    administratorLogin: 'platformbootstrap'
    version: '16'
    storage: {
      storageSizeGB: postgresStorageSizeGb
      autoGrow: 'Disabled'
      type: 'Premium_LRS'
    }
    backup: {
      backupRetentionDays: postgresBackupRetentionDays
      geoRedundantBackup: postgresGeoRedundantBackup
    }
    network: {
      publicNetworkAccess: 'Disabled'
      delegatedSubnetResourceId: postgresSubnet.id
      privateDnsZoneArmResourceId: postgresPrivateDns.id
    }
    authConfig: {
      activeDirectoryAuth: 'Enabled'
      passwordAuth: 'Disabled'
      tenantId: subscription().tenantId
    }
    highAvailability: {
      mode: postgresHaMode
    }
  }
  dependsOn: [
    postgresDnsLink
  ]
}

resource postgresMigrationAdmin 'Microsoft.DBforPostgreSQL/flexibleServers/administrators@2024-08-01' = {
  parent: postgresServer
  name: postgresMigrationPrincipalObjectId
  properties: {
    principalName: postgresMigrationPrincipalName
    principalType: 'ServicePrincipal'
    tenantId: subscription().tenantId
  }
}

resource postgresDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgresServer
  name: 'm365_platform'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-api-id'
  location: location
}

resource webIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-web-id'
  location: location
}

resource apiEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-api-env'
  location: location
  properties: {
    vnetConfiguration: {
      internal: true
      infrastructureSubnetId: apiAcaSubnet.id
    }
    appLogsConfiguration: {
      destination: 'azure-monitor'
    }
    zoneRedundant: containerAppsZoneRedundant
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
        minimumCount: 0
        maximumCount: 3
      }
    ]
  }
}

resource webEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-web-env'
  location: location
  properties: {
    vnetConfiguration: {
      internal: false
      infrastructureSubnetId: webAcaSubnet.id
    }
    appLogsConfiguration: {
      destination: 'azure-monitor'
    }
    zoneRedundant: containerAppsZoneRedundant
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
        minimumCount: 0
        maximumCount: 3
      }
    ]
  }
}

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: apiEnvironment.id
    configuration: {
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: apiCpuCores
            memory: apiMemory
          }
          env: [
            {
              name: 'ASPNETCORE_HTTP_PORTS'
              value: '8080'
            }
            {
              name: 'M365_PLATFORM_MODE'
              value: deploymentMode
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: apiMaxReplicas
      }
    }
  }
}

resource webApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-web'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${webIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: webEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 3000
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          resources: {
            cpu: webCpuCores
            memory: webMemory
          }
          env: [
            {
              name: 'PLATFORM_API_ORIGIN'
              value: 'https://${namePrefix}-api.${apiEnvironment.properties.defaultDomain}'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: webMaxReplicas
      }
    }
  }
}

resource apiKeyVaultCryptoGrant 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, apiIdentity.name, keyVaultCryptoUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: keyVaultCryptoUserRoleId
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

module assessmentWorkerStacks './modules/customer-worker.bicep' = [for binding in customerWorkerBindings: if (!empty(binding.assessmentManifestDigest) && binding.assessmentManifestApproval.approved == true && !empty(binding.assessmentManifestApproval.reference)) {
  name: '${namePrefix}-assessment-${replace(toLower(binding.tenantId), '-', '')}'
  params: {
    namePrefix: namePrefix
    location: location
    tenantId: binding.tenantId
    purpose: 'assessment'
    adapterManifestDigest: binding.assessmentManifestDigest
    image: assessmentWorkerImage
    apiEnvironmentId: apiEnvironment.id
    serviceBusName: serviceBus.name
    storageAccountName: evidenceStorage.name
    keyVaultName: keyVault.name
    apiPrincipalId: apiIdentity.properties.principalId
    apiIdentityName: apiIdentity.name
    workerCpuCores: assessmentWorkerCpuCores
    workerMemory: assessmentWorkerMemory
    workerMaxReplicas: assessmentWorkerMaxReplicas
  }
}]

module remediationWorkerStacks './modules/customer-worker.bicep' = [for binding in customerWorkerBindings: if (!empty(binding.remediationManifestDigest) && binding.remediationManifestApproval.approved == true && !empty(binding.remediationManifestApproval.reference)) {
  name: '${namePrefix}-remediation-${replace(toLower(binding.tenantId), '-', '')}'
  params: {
    namePrefix: namePrefix
    location: location
    tenantId: binding.tenantId
    purpose: 'remediation'
    adapterManifestDigest: binding.remediationManifestDigest
    image: remediationWorkerImage
    apiEnvironmentId: apiEnvironment.id
    serviceBusName: serviceBus.name
    storageAccountName: evidenceStorage.name
    keyVaultName: keyVault.name
    apiPrincipalId: apiIdentity.properties.principalId
    apiIdentityName: apiIdentity.name
    workerCpuCores: remediationWorkerCpuCores
    workerMemory: remediationWorkerMemory
    workerMaxReplicas: remediationWorkerMaxReplicas
  }
}]

resource blobPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: '${namePrefix}-blob-pe'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnet.id
    }
    privateLinkServiceConnections: [
      {
        name: 'blob'
        properties: {
          privateLinkServiceId: evidenceStorage.id
          groupIds: [
            'blob'
          ]
        }
      }
    ]
  }
}

resource blobPrivateDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: blobPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'blob'
        properties: {
          privateDnsZoneId: blobPrivateDns.id
        }
      }
    ]
  }
}

resource keyVaultPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: '${namePrefix}-kv-pe'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnet.id
    }
    privateLinkServiceConnections: [
      {
        name: 'vault'
        properties: {
          privateLinkServiceId: keyVault.id
          groupIds: [
            'vault'
          ]
        }
      }
    ]
  }
}

resource keyVaultPrivateDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: keyVaultPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'vault'
        properties: {
          privateDnsZoneId: keyVaultPrivateDns.id
        }
      }
    ]
  }
}

resource serviceBusPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: '${namePrefix}-sb-pe'
  location: location
  properties: {
    subnet: {
      id: privateEndpointSubnet.id
    }
    privateLinkServiceConnections: [
      {
        name: 'servicebus'
        properties: {
          privateLinkServiceId: serviceBus.id
          groupIds: [
            'namespace'
          ]
        }
      }
    ]
  }
}

resource serviceBusPrivateDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: serviceBusPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'servicebus'
        properties: {
          privateDnsZoneId: serviceBusPrivateDns.id
        }
      }
    ]
  }
}

output platformMode string = deploymentMode
output configuredStandaloneTenant string = deploymentMode == 'standalone' ? standaloneTenantId : ''
output webUrl string = 'https://${webApp.properties.configuration.ingress.fqdn}'
output apiInternalFqdn string = '${namePrefix}-api.${apiEnvironment.properties.defaultDomain}'
output postgresFqdn string = postgresServer.properties.fullyQualifiedDomainName
output storageAccount string = evidenceStorage.name
output keyVault string = keyVault.name
output serviceBusSku string = serviceBus.sku.name
output apiIdentityPrincipalId string = apiIdentity.properties.principalId
output appRoleName string = 'platform_app'
output postgresMigrationPrincipalObjectId string = postgresMigrationPrincipalObjectId
output deploymentHasProductionApproval bool = environmentClass == 'production' && !empty(productionApprovalReference)
output productionApprovalInputs object = {
  approvedRegion: approvedRegion
  regionApprovalReference: regionApprovalReference
  networkProfileApprovalReference: networkProfileApprovalReference
  egressPolicyApprovalReference: egressPolicyApprovalReference
  approvedMicrosoftApiFqdns: approvedMicrosoftApiFqdns
  premiumCostApprovalReference: premiumCostApprovalReference
  budgetApprovalReference: budgetApprovalReference
  retentionApprovalReference: retentionApprovalReference
  productionSloApprovalReference: productionSloApprovalReference
  productionApprovalReference: productionApprovalReference
  productionApprovalActor: productionApprovalActor
  productionApprovalTimestampUtc: productionApprovalTimestampUtc
  premiumWorkloadProfileId: premiumWorkloadProfileId
  premiumCostEstimateSource: premiumCostEstimateSource
  premiumEstimatedMonthlyCost: premiumEstimatedMonthlyCost
  premiumEstimatedMonthlyCurrency: premiumEstimatedMonthlyCurrency
  approvedMonthlyBudget: approvedMonthlyBudget
  approvedBudgetCurrency: approvedBudgetCurrency
  approvedAvailabilityPercent: approvedAvailabilityPercent
  approvedQueueAgeAlertSeconds: approvedQueueAgeAlertSeconds
  approvedJobTimeoutSeconds: approvedJobTimeoutSeconds
  approvedPerCustomerConcurrency: approvedPerCustomerConcurrency
  approvedMaxTransientRetries: approvedMaxTransientRetries
  approvedRecoveryRtoHours: approvedRecoveryRtoHours
  approvedRecoveryRpoMinutes: approvedRecoveryRpoMinutes
}

resource postgresSecureTransport 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgresServer
  name: 'require_secure_transport'
  properties: {
    value: 'on'
  }
}