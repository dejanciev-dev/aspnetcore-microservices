# Build the Angular app, then serve the static bundle with nginx.
# Debian (glibc) rather than Alpine: the lockfile is generated on glibc platforms and some
# native build dependencies (e.g. @napi-rs/lzma) have no musl binary recorded in it.
FROM node:22-slim AS build
WORKDIR /app
# .npmrc (legacy-peer-deps) must be present, or npm ci sees the lockfile as out of sync.
COPY package.json package-lock.json .npmrc ./
RUN npm ci
COPY . .
RUN npx ng build --configuration production

FROM nginx:1.29-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/storefront/browser /usr/share/nginx/html
EXPOSE 80
