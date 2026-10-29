#!/usr/bin/env bash
# azd postpublish hook (azure.yaml): run a service's migration job with the image azd has just
# pushed, before the service's new revision rolls out. The revision's readiness waits for the
# schema, so migrating first is what lets it become ready; a failed migration stops `azd deploy`
# while the previous revision keeps serving.
#
# Usage: run-migration-job.sh <service>      e.g. catalog-api → runs the catalog-migrate job
# Needs the Azure CLI signed in (`az login`); azd provides the AZURE_* values.
set -euo pipefail

service=${1:?usage: run-migration-job.sh <service>}
job="${service%-api}-migrate"
: "${AZURE_RESOURCE_GROUP:?run through azd}" "${AZURE_CONTAINER_REGISTRY_ENDPOINT:?run through azd}" "${AZURE_ENV_NAME:?run through azd}"
registry=${AZURE_CONTAINER_REGISTRY_ENDPOINT%%.*}
az config set extension.use_dynamic_install=yes_without_prompt --only-show-errors > /dev/null

# azd pushes to <project>/<service>-<environment>; take the tag pushed most recently. (The
# SERVICE_*_IMAGE_NAME value may still name the previous deployment's image at this point.)
repository=$(az acr repository list --name "$registry" --output tsv | grep -E "(^|/)${service}-${AZURE_ENV_NAME}\$" | head -n 1)
[ -n "$repository" ] || { echo "No repository for $service in $registry" >&2; exit 1; }
tag=$(az acr repository show-tags --name "$registry" --repository "$repository" --orderby time_desc --top 1 --output tsv)
image="$AZURE_CONTAINER_REGISTRY_ENDPOINT/$repository:$tag"

echo "Migrating with $image ($job)"
az containerapp job update --name "$job" --resource-group "$AZURE_RESOURCE_GROUP" --image "$image" --only-show-errors > /dev/null
execution=$(az containerapp job start --name "$job" --resource-group "$AZURE_RESOURCE_GROUP" --query name --output tsv)

for _ in $(seq 1 90); do
  status=$(az containerapp job execution show --name "$job" --resource-group "$AZURE_RESOURCE_GROUP" \
    --job-execution-name "$execution" --query properties.status --output tsv)
  case "$status" in
    Succeeded) echo "$job: $execution succeeded"; exit 0 ;;
    Failed | Stopped | Degraded)
      echo "$job: $execution $status. Logs: az containerapp job logs show --name $job --resource-group $AZURE_RESOURCE_GROUP --execution $execution --container migrate" >&2
      exit 1 ;;
  esac
  sleep 10
done

echo "$job: $execution still running after 15 minutes" >&2
exit 1
