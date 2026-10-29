# Keycloak for Azure Container Apps (infra/): production mode on PostgreSQL, with the storefront
# realm baked in. Docker Compose, Aspire and Kubernetes use the stock image and mount the realm.
# The realm's placeholders (STOREFRONT_URL, STOREFRONT_BFF_CLIENT_SECRET) are filled from the
# environment on import.
FROM quay.io/keycloak/keycloak:26.8 AS build
ENV KC_DB=postgres \
    KC_HEALTH_ENABLED=true
RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:26.8
COPY --from=build /opt/keycloak/ /opt/keycloak/
COPY storefront-realm.json /opt/keycloak/data/import/
ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
CMD ["start", "--optimized", "--import-realm"]
