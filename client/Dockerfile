# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------------------------
FROM node:22-alpine AS build
WORKDIR /app

COPY package.json yarn.lock ./
RUN yarn install --frozen-lockfile

COPY . .

# No build args. Unlike the reference project this client has no src/environments: the generated
# API client's rootUrl is '' and every request is same-origin, because the backend reverse-proxies
# this app and serves /api itself. There is no backend URL to bake in, which also means no image
# is tied to an environment.
#
# `yarn build` needs no running backend either: client/src/app/api is committed, so ng-openapi-gen
# does not run here (it is wired to prestart, not prebuild), and the routes Angular prerenders are
# the ones marked RenderMode.Client-free in app.routes.server.ts — login and register, neither of
# which makes an HTTP call. If that ever changes, note that a prerender request which cannot be
# answered does not fail the build, it hangs until the job times out.
RUN yarn build

# ---------------------------------------------------------------------------------------------
# Final
# ---------------------------------------------------------------------------------------------
FROM node:22-alpine AS final
WORKDIR /app

ENV NODE_ENV=production
ENV PORT=4000
EXPOSE 4000

# Angular 22's SSR engine answers 400 to any request whose Host it does not recognise, and the
# allowlist is empty in angular.json. Two names go in it, and neither is the public domain:
#
#   webly-client  what YARP puts in the Host header, because it rewrites Host to the destination.
#                  It must match the compose service name and the address in
#                  appsettings.Production.json. Renaming the service in one place 400s every page.
#   127.0.0.1      the HEALTHCHECK below, which would otherwise report the container unhealthy
#                  forever while it served pages perfectly.
#
# Set here rather than in angular.json so the image is not built for one deployment, and the real
# public hostname is deliberately absent: this container publishes no port, so the only Host value
# that can ever reach it is the one YARP writes.
ENV NG_ALLOWED_HOSTS=webly-client,127.0.0.1

# dist/client only, with no node_modules beside it. The Angular application builder bundles the
# server output: server.mjs and its chunks import nothing but node:path, node:process and node:url,
# express and @angular/ssr included. Verified by grepping the bundle's bare imports — recheck it
# before adding a dependency that resists bundling, because a missing one surfaces as a crash loop
# at container start, not as a build error.
#
# The layout is kept as built. src/server.ts resolves its static root as '../browser' relative to
# the server folder, so flattening these two directories would serve zero assets.
COPY --from=build /app/dist/client ./

USER node

# Alpine ships busybox wget; there is no curl in this image.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1:4000/ || exit 1

CMD ["node", "server/server.mjs"]
