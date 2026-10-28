# Shared multi-stage build for every .NET service in the solution.
# Usage (from the repo root):
#   docker build -f src/Dockerfile --build-arg PROJECT=Services/Catalog/Catalog.API/Catalog.API.csproj -t catalog-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /repo

# Restore first so this layer stays cached until a project file changes.
COPY src/BuildingBlocks/BuildingBlocks.Common/BuildingBlocks.Common.csproj src/BuildingBlocks/BuildingBlocks.Common/
COPY src/Services/Catalog/Catalog.API/Catalog.API.csproj    src/Services/Catalog/Catalog.API/
COPY src/Services/Basket/Basket.API/Basket.API.csproj       src/Services/Basket/Basket.API/
COPY src/Services/Ordering/Ordering.API/Ordering.API.csproj src/Services/Ordering/Ordering.API/
COPY src/ApiGateways/ApiGateway/ApiGateway.csproj           src/ApiGateways/ApiGateway/
RUN dotnet restore "src/${PROJECT}"

COPY src/ src/
RUN dotnet publish "src/${PROJECT}" -c Release --no-restore -o /app \
 # The native apphost embeds its DLL name, so a fixed copy gives every image the same entrypoint.
 && cp "/app/$(basename "${PROJECT}" .csproj)" /app/service

# Chiseled = distroless Ubuntu: no shell or package manager, non-root user, listens on 8080.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["./service"]
