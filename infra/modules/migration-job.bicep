// A manually triggered Container Apps job that runs a service's image with `migrate`: EF Core
// migrations (and the catalog seed), then exit. The azd postpublish hook of the service points it
// at the image just pushed and runs it before the new revision rolls out
// (infra/scripts/run-migration-job.*), the Container Apps counterpart of the Kubernetes Job.

param name string
param location string
param tags object = {}

@description('The container app whose image the job runs.')
param appName string
@description('True once azd has deployed that app, so re-provisioning keeps the job on its image.')
param appExists bool = false

param environmentId string
param identityId string
param registryServer string
param env array = []

@secure()
param secrets object = {}

resource app 'Microsoft.App/containerApps@2024-03-01' existing = if (appExists) {
  name: appName
}

// Before the first publish the hook replaces this; it is never started with the placeholder.
var image = appExists ? app!.properties.template.containers[0].image : 'mcr.microsoft.com/dotnet/samples:aspnetapp'

resource job 'Microsoft.App/jobs@2024-03-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environmentId
    configuration: {
      triggerType: 'Manual'
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      replicaTimeout: 600
      replicaRetryLimit: 2   // PostgreSQL may be briefly unavailable
      registries: [
        { server: registryServer, identity: identityId }
      ]
      secrets: secrets.?items ?? []
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: image
          args: ['migrate']
          env: env
          resources: { cpu: json('0.5'), memory: '1Gi' }
        }
      ]
    }
  }
}

output name string = job.name
