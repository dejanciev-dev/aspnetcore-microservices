// One Azure Container App. Used for every service and for the in-environment Redis and RabbitMQ.

@description('App name; also its DNS name inside the environment (http://<name> for HTTP ingress).')
param name string
param location string
param tags object = {}

@description('azd service name (azure.yaml) when azd deploys this app; empty for third-party images.')
param azdServiceName string = ''

param environmentId string
param identityId string
param registryServer string

@description('Fixed image (third-party apps). Leave empty for azd services.')
param image string = ''

@description('True once azd has deployed this service, so re-provisioning keeps its current image.')
param exists bool = false

param targetPort int
@description('Public HTTPS ingress (true) or reachable only inside the environment (false).')
param external bool = false
@allowed(['http', 'tcp'])
param transport string = 'http'
@description('TCP only: the port other apps connect to (e.g. 6379).')
param exposedPort int = 0

param command array = []
param args array = []
param env array = []

@description('Secrets as { name, value }; reference them in env with secretRef.')
@secure()
param secrets object = {}

param probes array = []
param minReplicas int = 1
param maxReplicas int = 1
param cpu string = '0.5'
param memory string = '1Gi'

// Re-provisioning must not swap a deployed image back to the placeholder.
resource existingApp 'Microsoft.App/containerApps@2024-03-01' existing = if (exists) {
  name: name
}

// Before the first `azd deploy` an azd service runs a placeholder that listens on the same port, so
// the first revision is healthy; its probes are added once the real image is in place (the next
// provision, e.g. a second `azd up`).
var placeholderImage = targetPort == 80
  ? 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
  : 'mcr.microsoft.com/dotnet/samples:aspnetapp'   // ASP.NET Core sample, listens on 8080
var isPlaceholder = empty(image) && !exists
var resolvedImage = !empty(image) ? image : (exists ? existingApp!.properties.template.containers[0].image : placeholderImage)

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: union(tags, empty(azdServiceName) ? {} : { 'azd-service-name': azdServiceName })
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: external
        targetPort: targetPort
        transport: transport
        exposedPort: transport == 'tcp' ? exposedPort : null
        allowInsecure: false
      }
      registries: [
        { server: registryServer, identity: identityId }
      ]
      secrets: secrets.?items ?? []
    }
    template: {
      containers: [
        {
          name: name
          image: resolvedImage
          command: empty(command) ? null : command
          args: empty(args) ? null : args
          env: env
          probes: isPlaceholder ? [] : probes
          resources: { cpu: json(cpu), memory: memory }
        }
      ]
      scale: { minReplicas: minReplicas, maxReplicas: maxReplicas }
    }
  }
}

output name string = app.name
output fqdn string = app.properties.configuration.?ingress.?fqdn ?? ''
