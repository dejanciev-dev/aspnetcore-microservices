// Entry point for `azd provision`: one resource group holding the whole storefront.
targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('azd environment name; used for the resource group and resource names.')
param environmentName string

@minLength(1)
@description('Azure region for every resource.')
param location string

@secure()
@description('PostgreSQL administrator password (Catalog, Ordering and Keycloak databases).')
param postgresPassword string

@secure()
@description('Password of the RabbitMQ user the services connect with.')
param rabbitmqPassword string

@secure()
@description('Keycloak bootstrap admin password (admin console).')
param keycloakAdminPassword string

@secure()
@description('Client secret of the storefront-bff client, shared by Keycloak and the gateway.')
param bffClientSecret string

// Set by azd once a service has been deployed, so re-provisioning keeps its image.
param catalogApiExists bool = false
param basketApiExists bool = false
param orderingApiExists bool = false
param apiGatewayExists bool = false
param storefrontExists bool = false
param keycloakExists bool = false

var tags = { 'azd-env-name': environmentName }

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  name: 'resources'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: toLower(uniqueString(subscription().id, rg.id, environmentName))
    postgresPassword: postgresPassword
    rabbitmqPassword: rabbitmqPassword
    keycloakAdminPassword: keycloakAdminPassword
    bffClientSecret: bffClientSecret
    catalogApiExists: catalogApiExists
    basketApiExists: basketApiExists
    orderingApiExists: orderingApiExists
    apiGatewayExists: apiGatewayExists
    storefrontExists: storefrontExists
    keycloakExists: keycloakExists
  }
}

// azd pushes images here and prints the URLs after `azd up`.
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.registryLoginServer
output AZURE_RESOURCE_GROUP string = rg.name
output STOREFRONT_URL string = resources.outputs.storefrontUrl
output KEYCLOAK_URL string = resources.outputs.keycloakUrl
