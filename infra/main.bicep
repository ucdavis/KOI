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

@description('Aggie Enterprise GraphQL API URL.')
@minLength(8)
param financialApiUrl string

@description('Aggie Enterprise OAuth consumer key.')
@secure()
@minLength(1)
param financialConsumerKey string

@description('Aggie Enterprise OAuth consumer secret.')
@secure()
@minLength(1)
param financialConsumerSecret string

@description('Aggie Enterprise OAuth token endpoint.')
@minLength(8)
param financialTokenEndpoint string

@description('Aggie Enterprise application scope name.')
@minLength(1)
param financialScopeApp string

@description('Aggie Enterprise environment scope name.')
@minLength(1)
param financialScopeEnv string

@description('HTTPS endpoint for the central OTLP collector.')
@minLength(8)
param otelExporterOtlpEndpoint string

@description('Authentication headers sent to the central OTLP collector.')
@secure()
@minLength(1)
param otelExporterOtlpHeaders string

@description('Wire protocol accepted by the central OTLP collector.')
@allowed([
  'grpc'
  'http/protobuf'
])
param otelExporterOtlpProtocol string = 'grpc'

@description('Application version attached to OpenTelemetry resources.')
@minLength(1)
param serviceVersion string

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

// Flex Consumption takes the worker runtime from functionAppConfig.runtime and
// rejects FUNCTIONS_WORKER_RUNTIME when it is duplicated in app settings.
resource appSettings 'Microsoft.Web/sites/config@2024-04-01' = {
  parent: functionApp
  name: 'appsettings'
  properties: {
    AzureWebJobsStorage: storageConnectionString
    DEPLOYMENT_STORAGE_CONNECTION_STRING: storageConnectionString
    FUNCTIONS_EXTENSION_VERSION: '~4'
    OTEL_EXPORTER_OTLP_ENDPOINT: otelExporterOtlpEndpoint
    OTEL_EXPORTER_OTLP_HEADERS: otelExporterOtlpHeaders
    OTEL_EXPORTER_OTLP_PROTOCOL: otelExporterOtlpProtocol
    OTEL_RESOURCE_ATTRIBUTES: 'service.name=koi,service.version=${serviceVersion},deployment.environment=${environmentName},service.namespace=ucdavis'
    OTEL_SERVICE_NAME: 'koi'
    ApiKeys__Credentials__0__Enabled: 'true'
    ApiKeys__Credentials__0__Id: apiKey1Id
    ApiKeys__Credentials__0__Sha256: apiKey1Sha256
    ApiKeys__Credentials__1__Enabled: 'true'
    ApiKeys__Credentials__1__Id: apiKey2Id
    ApiKeys__Credentials__1__Sha256: apiKey2Sha256
    Financial__ApiUrl: financialApiUrl
    Financial__ConsumerKey: financialConsumerKey
    Financial__ConsumerSecret: financialConsumerSecret
    Financial__ScopeApp: financialScopeApp
    Financial__ScopeEnv: financialScopeEnv
    Financial__TokenEndpoint: financialTokenEndpoint
  }
}

output functionAppName string = functionApp.name
output functionAppPrincipalId string = functionApp.identity.principalId
output functionAppUrl string = 'https://${functionApp.properties.defaultHostName}'
output storageAccountName string = storage.name
