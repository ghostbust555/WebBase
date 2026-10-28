#!/usr/bin/env bash
# Build the production image and upload it to ANY container registry, for any
# host that runs a container image: Docker Hub, GitHub (ghcr.io), DigitalOcean,
# Fly, Render, Railway, a Kubernetes cluster...
#
#   ./scripts/cloud/push-image.sh ghcr.io/yourname/webbase
#   ./scripts/cloud/push-image.sh docker.io/yourname/webbase
#
# Log in to the registry first (docker login ghcr.io). Pushes three tags: the
# build id, sha-<commit> (what azure.sh deploys) and "latest". Then point your
# host at the image and give it the settings listed in docs/CLOUD.md ("What
# every host needs"). GitHub Actions runs this for you after each push to main
# (.github/workflows/publish-image.yml).
. "$(dirname "$0")/common.sh"
need_docker

REPO="${1:-}"
[ -n "$REPO" ] || { sed -n '2,14p' "$0"; exit 1; }
REPO="${REPO%:*}"

BUILD="$(build_id)"
build_and_push "${REPO}:${BUILD}" "$BUILD"
SHA_TAG="sha-$(git rev-parse --short=12 HEAD)"
for tag in "$SHA_TAG" latest; do
  docker tag "${REPO}:${BUILD}" "${REPO}:${tag}"
  docker push "${REPO}:${tag}"
done
ok "Pushed ${REPO}:${BUILD}, ${REPO}:${SHA_TAG} and ${REPO}:latest"
