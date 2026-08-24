targetScope = 'resourceGroup'

@description('Short deployment environment name.')
@minLength(1)
@maxLength(16)
param environmentName string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('First API-key identifier. This value is not secret.')
@minLength(1)
@maxLength(64)
param apiKey1Id string

@description('SHA-256 hash of the first API key.')
@secure()
@minLength(64)
@maxLength(64)
param apiKey1Sha256 string

@description('Second API-key identifier. This value is not secret.')
@minLength(1)
@maxLength(64)
param apiKey2Id string

@description('SHA-256 hash of the second API key.')
@secure()
@minLength(64)
@maxLength(64)
param apiKey2Sha256 string

@description('Maximum number of Flex Consumption instances.')
@minValue(1)
@maxValue(1000)
param maximumInstanceCount int = 20

@description('Memory allocated to each Flex Consumption instance.')
@allowed([
  512
  2048
  4096
])
param instanceMemoryMB int = 2048

var resourceToken = toLower(uniqueString(subscription().id, resourceGroup().id, environmentName))
var storageEnvironmentName = take(toLower(replace(environmentName, '-', '')), 6)
var functionAppName = 'func-koi-${environmentName}-${resourceToken}'
var functionPlanName = 'plan-koi-${environmentName}-${resourceToken}'
var storageAccountName = 'stkoi${storageEnvironmentName}${resourceToken}'
var logAnalyticsName = 'log-koi-${environmentName}-${resourceToken}'
var applicationInsightsName = 'appi-koi-${environmentName}-${resourceToken}'
var deploymentContainerName = 'app-package-${take(resourceToken, 8)}'
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
var tags = {
  environment: environmentName
  managedBy: 'Bicep'
  service: 'KOI'
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  tags: tags
  properties: {
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    allowSharedKeyAccess: true
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Enabled'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: deploymentContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Bluefield'
    IngestionMode: 'LogAnalytics'
    RetentionInDays: 30
    Request_Source: 'rest'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource functionPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: functionPlanName
  location: location
  kind: 'functionapp'
  tags: tags
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
    zoneRedundant: false
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    functionAppConfig: {
      deployment: {
        storage: {
          authentication: {
            storageAccountConnectionStringName: 'DEPLOYMENT_STORAGE_CONNECTION_STRING'
            type: 'StorageAccountConnectionString'
          }
          type: 'blobContainer'
          value: 'https://${storage.name}.blob.${environment().suffixes.storage}/${deploymentContainer.name}'
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
      scaleAndConcurrency: {
        alwaysReady: []
        instanceMemoryMB: instanceMemoryMB
        maximumInstanceCount: maximumInstanceCount
      }
    }
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    serverFarmId: functionPlan.id
    siteConfig: {
      alwaysOn: false
      ftpsState: 'Disabled'
      http20Enabled: true
      minTlsVersion: '1.2'
    }
  }
}

resource appSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: functionApp
  name: 'appsettings'
  properties: {
    APPLICATIONINSIGHTS_CONNECTION_STRING: applicationInsights.properties.ConnectionString
    AzureWebJobsStorage: storageConnectionString
    DEPLOYMENT_STORAGE_CONNECTION_STRING: storageConnectionString
    FUNCTIONS_EXTENSION_VERSION: '~4'
    FUNCTIONS_WORKER_RUNTIME: 'dotnet-isolated'
    ApiKeys__Credentials__0__Enabled: 'true'
    ApiKeys__Credentials__0__Id: apiKey1Id
    ApiKeys__Credentials__0__Sha256: apiKey1Sha256
    ApiKeys__Credentials__1__Enabled: 'true'
    ApiKeys__Credentials__1__Id: apiKey2Id
    ApiKeys__Credentials__1__Sha256: apiKey2Sha256
  }
}

output applicationInsightsName string = applicationInsights.name
output functionAppName string = functionApp.name
output functionAppPrincipalId string = functionApp.identity.principalId
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output storageAccountName string = storage.name
