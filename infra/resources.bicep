// The storefront on Azure Container Apps.
//
//   Internet ──HTTPS──▶ storefront (nginx) ──▶ api-gateway ──▶ catalog-api / basket-api / ordering-api
//            ──HTTPS──▶ keycloak
//
// Only the storefront and Keycloak are public. The services reach each other by app name inside
// the environment (VNet-integrated, which TCP ingress for Redis and RabbitMQ requires).
// PostgreSQL is a managed Flexible Server; Redis and RabbitMQ run as container apps.

param location string
param tags object
@description('uniqueString() of the subscription, group and environment: 13 lowercase characters.')
@minLength(13)
@maxLength(13)
param resourceToken string

@secure()
param postgresPassword string
@secure()
param rabbitmqPassword string
@secure()
param keycloakAdminPassword string
@secure()
param bffClientSecret string

param catalogApiExists bool
param basketApiExists bool
param orderingApiExists bool
param apiGatewayExists bool
param storefrontExists bool
param keycloakExists bool

var postgresAdmin = 'storefront_admin'
var vnetAddressSpace = '10.0.0.0/16'

// ---------------------------------------------------------------------------------------------
// Shared infrastructure
// ---------------------------------------------------------------------------------------------

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-${resourceToken}'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: [vnetAddressSpace] }
    subnets: [
      {
        name: 'container-apps'
        properties: {
          addressPrefix: '10.0.0.0/23'
          delegations: [
            { name: 'container-apps', properties: { serviceName: 'Microsoft.App/environments' } }
          ]
        }
      }
    ]
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${resourceToken}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: vnet.properties.subnets[0].id
    }
    workloadProfiles: [
      { name: 'Consumption', workloadProfileType: 'Consumption' }
    ]
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'cr${resourceToken}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

// Every app pulls from the registry with this identity; no registry passwords anywhere.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${resourceToken}'
  location: location
  tags: tags
}

var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, acrPullRoleId)
  scope: registry
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: 'psql-${resourceToken}'
  location: location
  tags: tags
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '16'
    administratorLogin: postgresAdmin
    administratorLoginPassword: postgresPassword
    authConfig: { passwordAuth: 'Enabled', activeDirectoryAuth: 'Disabled' }
    storage: { storageSizeGB: 32 }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
  }

  // Public endpoint limited to Azure-originated traffic, TLS required by default.
  resource allowAzure 'firewallRules' = {
    name: 'AllowAzureServices'
    properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
  }
}

resource databases 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = [for db in ['catalogdb', 'orderingdb', 'keycloak']: {
  parent: postgres
  name: db
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}]

// ---------------------------------------------------------------------------------------------
// Apps
// ---------------------------------------------------------------------------------------------

var domain = environment.properties.defaultDomain
var storefrontUrl = 'https://storefront.${domain}'
var keycloakUrl = 'https://keycloak.${domain}'
var authority = '${keycloakUrl}/realms/storefront'

// Values with secrets in them live in each app's secret store, referenced by name from env.
var npgsqlPassword = replace(postgresPassword, '"', '""')
func npgsql(host string, database string, user string, password string) string =>
  'Host=${host};Database=${database};Username=${user};Password="${password}";SSL Mode=Require'
var rabbitmqConnection = 'amqp://storefront:${uriComponent(rabbitmqPassword)}@rabbitmq:5672'

// Startup and liveness ask whether the process responds; readiness also checks its dependencies.
// (Container Apps allows at most 10 failures per probe, hence the longer startup period.)
var dotnetProbes = [
  { type: 'Startup', httpGet: { path: '/alive', port: 8080 }, periodSeconds: 10, failureThreshold: 10 }
  { type: 'Liveness', httpGet: { path: '/alive', port: 8080 }, periodSeconds: 10 }
  { type: 'Readiness', httpGet: { path: '/health', port: 8080 }, periodSeconds: 5 }
]

var commonServiceEnv = [
  { name: 'Identity__Authority', value: authority }
]

module redis 'modules/container-app.bicep' = {
  name: 'redis'
  dependsOn: [acrPull]
  params: {
    location: location
    tags: tags
    environmentId: environment.id
    identityId: identity.id
    registryServer: registry.properties.loginServer
    name: 'redis'
    image: 'docker.io/library/redis:8-alpine'
    targetPort: 6379
    transport: 'tcp'
    exposedPort: 6379
    cpu: '0.25'
    memory: '0.5Gi'
  }
}

module rabbitmq 'modules/container-app.bicep' = {
  name: 'rabbitmq'
  dependsOn: [acrPull]
  params: {
    location: location
    tags: tags
    environmentId: environment.id
    identityId: identity.id
    registryServer: registry.properties.loginServer
    name: 'rabbitmq'
    image: 'docker.io/library/rabbitmq:4-alpine'
    targetPort: 5672
    transport: 'tcp'
    exposedPort: 5672
    secrets: { items: [{ name: 'rabbitmq-password', value: rabbitmqPassword }] }
    env: [
      { name: 'RABBITMQ_DEFAULT_USER', value: 'storefront' }
      { name: 'RABBITMQ_DEFAULT_PASS', secretRef: 'rabbitmq-password' }
    ]
  }
}

module keycloak 'modules/container-app.bicep' = {
  name: 'keycloak'
  dependsOn: [acrPull, databases]
  params: {
    location: location
    tags: tags
    environmentId: environment.id
    identityId: identity.id
    registryServer: registry.properties.loginServer
    name: 'keycloak'
    azdServiceName: 'keycloak'
    exists: keycloakExists
    targetPort: 8080
    external: true
    cpu: '1'
    memory: '2Gi'
    secrets: {
      items: [
        { name: 'db-password', value: postgresPassword }
        { name: 'admin-password', value: keycloakAdminPassword }
        { name: 'bff-client-secret', value: bffClientSecret }
      ]
    }
    env: [
      { name: 'KC_DB_URL', value: 'jdbc:postgresql://${postgres.properties.fullyQualifiedDomainName}:5432/keycloak?sslmode=require' }
      { name: 'KC_DB_USERNAME', value: postgresAdmin }
      { name: 'KC_DB_PASSWORD', secretRef: 'db-password' }
      { name: 'KC_HOSTNAME', value: keycloakUrl }
      { name: 'KC_PROXY_HEADERS', value: 'xforwarded' }   // TLS ends at the Container Apps ingress
      { name: 'KC_HTTP_ENABLED', value: 'true' }
      { name: 'KC_BOOTSTRAP_ADMIN_USERNAME', value: 'admin' }
      { name: 'KC_BOOTSTRAP_ADMIN_PASSWORD', secretRef: 'admin-password' }
      // Realm import placeholders (deploy/keycloak/storefront-realm.json).
      { name: 'STOREFRONT_URL', value: storefrontUrl }
      { name: 'STOREFRONT_BFF_CLIENT_SECRET', secretRef: 'bff-client-secret' }
    ]
    probes: [
      { type: 'Startup', httpGet: { path: '/health/started', port: 9000 }, initialDelaySeconds: 20, periodSeconds: 15, failureThreshold: 10 }
      { type: 'Liveness', httpGet: { path: '/health/live', port: 9000 }, periodSeconds: 20 }
      { type: 'Readiness', httpGet: { path: '/health/ready', port: 9000 }, periodSeconds: 10 }
    ]
  }
}

module catalogApi 'modules/container-app.bicep' = {
  name: 'catalog-api'
  dependsOn: [acrPull, databases]
  params: {
    location: location
    tags: tags
    environmentId: environment.id
    identityId: identity.id
    registryServer: registry.properties.loginServer
    name: 'catalog-api'
    azdServiceName: 'catalog-api'
    exists: catalogApiExists
    targetPort: 8080
    maxReplicas: 1   // migrations run at startup
    secrets: {
      items: [
        { name: 'catalogdb', value: npgsql(postgres.properties.fullyQualifiedDomainName, 'catalogdb', postgresAdmin, npgsqlPassword) }
        { name: 'rabbitmq', value: rabbitmqConnection }
      ]
    }
    env: concat(commonServiceEnv, [
      { name: 'ConnectionStrings__catalogdb', secretRef: 'catalogdb' }
      { name: 'ConnectionStrings__rabbitmq', secretRef: 'rabbitmq' }
    ])
    probes: dotnetProbes
  }
}

module orderingApi 'modules/container-app.bicep' = {
  name: 'ordering-api'
  dependsOn: [acrPull, databases]
  params: {
    location: location
    tags: tags
    environmentId: environment.id
    identityId: identity.id
    registryServer: registry.properties.loginServer
    name: 'ordering-api'
    azdServiceName: 'ordering-api'
    exists: orderingApiExists
    targetPort: 8080
    maxReplicas: 1   // migrations run at startup
    secrets: {
      items: [
        { name: 'orderingdb', value: npgsql(postgres.properties.fullyQualifiedDomainName, 'orderingdb', postgresAdmin, npgsqlPassword) }
        { name: 'rabbitmq', value: rabbitmqConnection }
      ]
    }
    env: concat(commonServiceEnv, [
      { name: 'ConnectionStrings__orderingdb', secretRef: 'orderingdb' }
      { name: 'ConnectionStrings__rabbitmq', secretRef: 'rabbitmq' }
    ])
    probes: dotnetProbes
  }
}

module basketApi 'modules/container-app.bicep' = {
  name: 'basket-api'
  dependsOn: [acrPull]
  params: {
    location: location
    tags: tags
    environmentId: environment.id
    identityId: identity.id
    registryServer: registry.properties.loginServer
    name: 'basket-api'
    azdServiceName: 'basket-api'
    exists: basketApiExists
    targetPort: 8080
    maxReplicas: 3   // stateless: baskets live in Redis
    secrets: { items: [{ name: 'rabbitmq', value: rabbitmqConnection }] }
    env: concat(commonServiceEnv, [
      { name: 'ConnectionStrings__redis', value: 'redis:6379' }
      { name: 'ConnectionStrings__rabbitmq', secretRef: 'rabbitmq' }
      { name: 'Services__Catalog', value: 'http://catalog-api' }
      { name: 'Services__Ordering', value: 'http://ordering-api' }
    ])
    probes: dotnetProbes
  }
}

module apiGateway 'modules/container-app.bicep' = {
  name: 'api-gateway'
  dependsOn: [acrPull]
  params: {
    location: location
    tags: tags
    environmentId: environment.id
    identityId: identity.id
    registryServer: registry.properties.loginServer
    name: 'api-gateway'
    azdServiceName: 'api-gateway'
    exists: apiGatewayExists
    targetPort: 8080
    maxReplicas: 1   // session keys, output cache and rate-limit counters are per instance
    secrets: { items: [{ name: 'bff-client-secret', value: bffClientSecret }] }
    env: concat(commonServiceEnv, [
      { name: 'Identity__ClientSecret', secretRef: 'bff-client-secret' }
      // Redirect URIs and Secure cookies from the public URL, whatever the proxies forward.
      { name: 'Bff__PublicOrigin', value: storefrontUrl }
      // Unwind X-Forwarded-For through the environment's own proxies to the real client.
      { name: 'ForwardedHeaders__KnownNetworks__0', value: vnetAddressSpace }
      { name: 'ReverseProxy__Clusters__catalog__Destinations__d1__Address', value: 'http://catalog-api' }
      { name: 'ReverseProxy__Clusters__basket__Destinations__d1__Address', value: 'http://basket-api' }
      { name: 'ReverseProxy__Clusters__ordering__Destinations__d1__Address', value: 'http://ordering-api' }
    ])
    probes: dotnetProbes
  }
}

module storefront 'modules/container-app.bicep' = {
  name: 'storefront'
  dependsOn: [acrPull]
  params: {
    location: location
    tags: tags
    environmentId: environment.id
    identityId: identity.id
    registryServer: registry.properties.loginServer
    name: 'storefront'
    azdServiceName: 'storefront'
    exists: storefrontExists
    targetPort: 80
    external: true
    maxReplicas: 3
    cpu: '0.25'
    memory: '0.5Gi'
    env: [
      { name: 'GATEWAY_URL', value: 'http://api-gateway' }   // internal ingress listens on 80
    ]
    probes: [
      { type: 'Liveness', httpGet: { path: '/', port: 80 }, periodSeconds: 10 }
      { type: 'Readiness', httpGet: { path: '/', port: 80 }, periodSeconds: 5 }
    ]
  }
}

output registryLoginServer string = registry.properties.loginServer
output storefrontUrl string = 'https://${storefront.outputs.fqdn}'
output keycloakUrl string = 'https://${keycloak.outputs.fqdn}'
