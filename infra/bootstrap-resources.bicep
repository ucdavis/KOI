targetScope = 'resourceGroup'

@description('User-assigned managed identity used by GitHub Actions.')
param deploymentIdentityName string

@description('Federated credential name on the deployment identity.')
param federatedCredentialName string

@description('Exact GitHub Actions OIDC subject trusted by Azure.')
param githubOidcSubject string

@description('Azure region for the deployment identity.')
param location string

param tags object

var contributorRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  'b24988ac-6180-42a0-ab88-20f7382dd24c'
)

resource deploymentIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: deploymentIdentityName
  location: location
  tags: tags
}

resource federatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: deploymentIdentity
  name: federatedCredentialName
  properties: {
    audiences: [
      'api://AzureADTokenExchange'
    ]
    issuer: 'https://token.actions.githubusercontent.com'
    subject: githubOidcSubject
  }
}

resource contributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deploymentIdentity.id, contributorRoleDefinitionId)
  properties: {
    principalId: deploymentIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: contributorRoleDefinitionId
  }
}

output deploymentIdentityClientId string = deploymentIdentity.properties.clientId
output deploymentIdentityPrincipalId string = deploymentIdentity.properties.principalId
