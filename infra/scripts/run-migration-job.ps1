# azd postpublish hook (azure.yaml) on Windows; see run-migration-job.sh for the reasoning.
# Usage: run-migration-job.ps1 <service>     e.g. catalog-api → runs the catalog-migrate job
# Needs the Azure CLI signed in (`az login`); azd provides the AZURE_* values.
param([Parameter(Mandatory)][string] $Service)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

foreach ($name in 'AZURE_RESOURCE_GROUP', 'AZURE_CONTAINER_REGISTRY_ENDPOINT', 'AZURE_ENV_NAME') {
    if (-not [Environment]::GetEnvironmentVariable($name)) { throw "$name is not set; run through azd." }
}
$group = $env:AZURE_RESOURCE_GROUP
$job = ($Service -replace '-api$', '') + '-migrate'
$registry = $env:AZURE_CONTAINER_REGISTRY_ENDPOINT.Split('.')[0]
az config set extension.use_dynamic_install=yes_without_prompt --only-show-errors | Out-Null

# azd pushes to <project>/<service>-<environment>; take the tag pushed most recently.
$repository = az acr repository list --name $registry --output tsv |
    Where-Object { $_ -match "(^|/)$([regex]::Escape("$Service-$env:AZURE_ENV_NAME"))$" } |
    Select-Object -First 1
if (-not $repository) { throw "No repository for $Service in $registry" }
$tag = az acr repository show-tags --name $registry --repository $repository --orderby time_desc --top 1 --output tsv
$image = "$env:AZURE_CONTAINER_REGISTRY_ENDPOINT/${repository}:$tag"

Write-Host "Migrating with $image ($job)"
az containerapp job update --name $job --resource-group $group --image $image --only-show-errors | Out-Null
$execution = az containerapp job start --name $job --resource-group $group --query name --output tsv

for ($i = 0; $i -lt 90; $i++) {
    $status = az containerapp job execution show --name $job --resource-group $group `
        --job-execution-name $execution --query properties.status --output tsv
    switch ($status) {
        'Succeeded' { Write-Host "${job}: $execution succeeded"; exit 0 }
        { $_ -in 'Failed', 'Stopped', 'Degraded' } {
            throw "${job}: $execution $status. Logs: az containerapp job logs show --name $job --resource-group $group --execution $execution --container migrate"
        }
    }
    Start-Sleep -Seconds 10
}

throw "${job}: $execution still running after 15 minutes"
