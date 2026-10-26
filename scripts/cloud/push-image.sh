#!/usr/bin/env bash
# Build the production image and upload it to ANY container registry, for any
# host that runs a container image: Docker Hub, GitHub (ghcr.io), DigitalOcean,
# Fly, Render, Railway, a Kubernetes cluster...
#
#   ./scripts/cloud/push-image.sh ghcr.io/yourname/webbase
#   ./scripts/cloud/push-image.sh docker.io/yourname/webbase
#
# Log in to the registry first (docker login ghcr.io). Pushes two tags: the
# build id, and "latest". Then point your host at the image and give it the
# settings listed in docs/CLOUD.md ("What every host needs").
. "$(dirname "$0")/common.sh"
need_docker

REPO="${1:-}"
[ -n "$REPO" ] || { sed -n '2,12p' "$0"; exit 1; }
REPO="${REPO%:*}"

BUILD="$(build_id)"
build_and_push "${REPO}:${BUILD}" "$BUILD"
docker tag "${REPO}:${BUILD}" "${REPO}:latest"
docker push "${REPO}:latest"
ok "Pushed ${REPO}:${BUILD} and ${REPO}:latest"
