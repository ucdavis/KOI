targetScope = 'subscription'

@description('Short deployment environment name.')
@minLength(1)
@maxLength(16)
param environmentName string

@description('Azure region for the resource group.')
param location string

@description('Resource group managed by this deployment.')
param resourceGroupName string

@description('User-assigned managed identity used by GitHub Actions.')
param deploymentIdentityName string

@description('Federated credential name on the deployment identity.')
param federatedCredentialName string

@description('Exact GitHub Actions OIDC subject trusted by Azure.')
param githubOidcSubject string

var tags = {
  environment: environmentName
  managedBy: 'Bicep'
  service: 'KOI'
}

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-11-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module bootstrapResources 'bootstrap-resources.bicep' = {
  name: 'koi-${environmentName}-bootstrap-resources'
  scope: resourceGroup
  params: {
    deploymentIdentityName: deploymentIdentityName
    federatedCredentialName: federatedCredentialName
    githubOidcSubject: githubOidcSubject
    location: location
    tags: tags
  }
}

output deploymentIdentityClientId string = bootstrapResources.outputs.deploymentIdentityClientId
output deploymentIdentityPrincipalId string = bootstrapResources.outputs.deploymentIdentityPrincipalId
output resourceGroupId string = resourceGroup.id
output resourceGroupName string = resourceGroup.name
